using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using EPDM.Interop.epdm;
using PdmVariableStudio.Core.Abstractions;

namespace PdmVariableStudio.App.Threading;

/// <summary>
/// PDM COM erişiminin tamamının üzerinden geçtiği, adanmış tek bir STA thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Temel kural: COM nesnesi thread geçmez.</b> <c>OnCmd</c> ile gelen
/// <c>EdmCmd.mpoVault</c> Explorer'ın kendi STA apartment'ına aittir. Onu başka bir thread'de
/// kullanmak COM'un proxy/stub marshalling'ini devreye sokar; PDM'in nesneleri bunu güvenilir
/// biçimde desteklemiyor ve hata belirtisi genellikle Explorer'ın çökmesi oluyor.
/// </para>
/// <para>
/// Bu yüzden Explorer thread'inden yalnızca DEĞERLER alınır (vault adı, klasör numarası,
/// pencere tanıtıcısı) ve bu thread kendi <see cref="IEdmVault5"/> örneğini kurup
/// <c>LoginAuto</c> ile bağlanır. Bedeli ikinci bir vault oturumu; karşılığında marshalling
/// sınıfı sorunların tamamı ortadan kalkıyor.
/// </para>
/// <para>
/// <b>PHASE 0'DA DOĞRULANACAK:</b> Explorer sürecinin içindeki bir arka plan STA thread'inde
/// <c>LoginAuto</c>'nun çalıştığı gerçek vault'ta teyit edilmeli. Çalışmazsa alternatif, tüm
/// PDM işini Explorer STA'sında <c>Dispatcher</c> ile parçalayarak yapmaktır (daha kötü
/// deneyim, ama güvenli). Bu sınıf değişimi tek noktada tutuyor.
/// </para>
/// <para>
/// İşler SIRAYLA çalışır. Eşzamanlı PDM erişimi bilinçli olarak yok: PDM API'si iş parçacığı
/// güvenli değil ve paralellikten kazanılacak süre, riskin yanında önemsiz.
/// </para>
/// </remarks>
internal sealed class PdmWorkQueue : IDisposable
{
    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly Thread _thread;
    private readonly IStudioLog _log;
    private readonly string _vaultName;
    private readonly TaskCompletionSource<bool> _ready = new();

    private IEdmVault5? _vault;
    private bool _disposed;

    public PdmWorkQueue(string vaultName, IStudioLog log)
    {
        _vaultName = vaultName;
        _log = log;

        _thread = new Thread(Run)
        {
            Name = "PdmVariableStudio.Pdm",

            // IsBackground = true bilinçli: eklenti kapanırken Explorer'ı bekletmemeli.
            // Kuyrukta iş kalırsa Dispose zaten bitmesini bekliyor; uygulama sırasında
            // pencere kapatma da ayrıca engelleniyor.
            IsBackground = true,
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Bu thread'in kendi vault nesnesi. YALNIZCA bu thread'den kullanılabilir.</summary>
    public IEdmVault5 Vault =>
        _vault ?? throw new InvalidOperationException(
            "Vault oturumu hazır değil. WaitUntilReadyAsync beklenmeden iş verilmiş.");

    /// <summary>Oturum açma tamamlanana kadar bekler.</summary>
    public Task<bool> WaitUntilReadyAsync() => _ready.Task;

    /// <summary>Bir işi kuyruğa alır ve sonucunu bekler.</summary>
    public Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PdmWorkQueue));
        }

        var completion = new TaskCompletionSource<T>();

        var item = new WorkItem(
            token =>
            {
                try
                {
                    completion.TrySetResult(work(token));
                }
                catch (OperationCanceledException)
                {
                    completion.TrySetCanceled();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            cancellationToken,
            () => completion.TrySetCanceled());

        _queue.Add(item, CancellationToken.None);
        return completion.Task;
    }

    public Task RunAsync(Action<CancellationToken> work, CancellationToken cancellationToken = default) =>
        RunAsync<bool>(token =>
        {
            work(token);
            return true;
        }, cancellationToken);

    private void Run()
    {
        try
        {
            // Kendi vault nesnemiz. Explorer'ın nesnesi BURAYA GELMEZ.
            var vault = new EdmVault5Class();
            vault.LoginAuto(_vaultName, 0);

            if (!vault.IsLoggedIn)
            {
                _log.Error($"'{_vaultName}' vault'una oturum açılamadı.");
                _ready.TrySetResult(false);
                return;
            }

            _vault = vault;
            _log.Info($"PDM çalışma kuyruğu hazır (vault '{_vaultName}', thread {Environment.CurrentManagedThreadId}).");
            _ready.TrySetResult(true);
        }
        catch (Exception exception)
        {
            _log.Error($"PDM çalışma kuyruğu başlatılamadı (vault '{_vaultName}').", exception);
            _ready.TrySetResult(false);
            return;
        }

        foreach (var item in _queue.GetConsumingEnumerable())
        {
            if (item.CancellationToken.IsCancellationRequested)
            {
                item.OnCancelled();
                continue;
            }

            try
            {
                item.Work(item.CancellationToken);
            }
            catch (Exception exception)
            {
                // İş öğesinin kendisi istisnayı TaskCompletionSource'a aktarıyor; buraya
                // düşmesi beklenmiyor. Yine de yutulmuyor: buradan sızan bir istisna
                // worker thread'ini öldürür ve eklenti sessizce yanıt vermez hâle gelirdi.
                _log.Error("PDM iş öğesi beklenmeyen bir hatayla düştü.", exception);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();

        // Kuyruğun boşalmasını kısa bir süre bekle: devam eden bir PDM çağrısını yarıda
        // kesmek, dosyayı belirsiz durumda bırakabilir.
        if (!_thread.Join(TimeSpan.FromSeconds(30)))
        {
            _log.Warn("PDM çalışma kuyruğu 30 saniyede kapanmadı.");
        }

        if (_vault is not null)
        {
            // Vault nesnesi kendi thread'inde alındı; oradan bırakılması gerekirdi. Thread
            // sonlandığı için burada yalnızca referans düşürülüyor.
            _vault = null;
        }

        _queue.Dispose();
    }

    private sealed class WorkItem
    {
        public WorkItem(Action<CancellationToken> work, CancellationToken cancellationToken, Action onCancelled)
        {
            Work = work;
            CancellationToken = cancellationToken;
            OnCancelled = onCancelled;
        }

        public Action<CancellationToken> Work { get; }

        public CancellationToken CancellationToken { get; }

        public Action OnCancelled { get; }
    }
}
