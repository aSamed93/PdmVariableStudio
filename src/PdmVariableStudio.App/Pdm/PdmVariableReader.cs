using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using EPDM.Interop.epdm;
using PdmVariableStudio.App.Threading;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.App.Pdm;

/// <summary>
/// Kart değerlerini okur. Tüm çağrılar adanmış STA worker thread'inden yapılmalıdır.
/// </summary>
/// <remarks>
/// <para>
/// <b>Okuma stratejisi.</b> Dosya başına TEK bir <see cref="IEdmEnumeratorVariable5"/> açılır
/// ve tüm değişken/konfigürasyon çiftleri onun üzerinden okunur. Hücre başına ayrı bir
/// numaralandırıcı açmak, 1.000 dosya × 20 değişken ölçeğinde arayüzü kullanılamaz hâle
/// getirirdi.
/// </para>
/// <para>
/// <b>GetVarFromDb tercih ediliyor.</b> <c>GetVar</c> değeri dosyanın yerel önbelleğinden
/// okumaya çalışabiliyor ve bu, yerel kopyası olmayan dosyalarda ağdan çekme tetikleyerek
/// taramayı dakikalara çıkarabiliyor. <c>GetVarFromDb</c> doğrudan veritabanından okur.
/// <b>PHASE 0'DA DOĞRULANACAK:</b> ikisinin dönen değerleri ve hızları gerçek vault'ta
/// karşılaştırılacak; fark beklendiği gibi değilse <see cref="PreferDatabaseRead"/> kapatılır.
/// </para>
/// </remarks>
internal sealed class PdmVariableReader : IPdmVariableReader
{
    private readonly IEdmVault5 _vault;
    private readonly IStudioLog _log;

    public PdmVariableReader(IEdmVault5 vault, IStudioLog log)
    {
        _vault = vault;
        _log = log;
    }

    /// <summary>
    /// <c>GetVarFromDb</c> kullanılsın mı. Phase 0 doğrulaması bunun aksini gösterirse tek
    /// satırla kapatılabilir.
    /// </summary>
    public bool PreferDatabaseRead { get; set; } = true;

    public OperationOutcome<IReadOnlyList<PdmFileSnapshot>> ReadSnapshots(
        IReadOnlyList<PdmFileIdentity> files,
        IReadOnlyList<PdmVariableDefinition> variables,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var snapshots = new List<PdmFileSnapshot>(files.Count);
        var failures = 0;

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = ReadSnapshot(files[i], variables);

            if (outcome.IsSuccess)
            {
                snapshots.Add(outcome.Value);
            }
            else
            {
                // Tek bir dosyanın okunamaması taramayı durdurmaz: silinmiş ya da yetkisiz
                // bir dosya yüzünden 999 dosyanın dışa aktarımını kaybetmek kabul edilemez.
                failures++;
                _log.Warn($"{files[i]} okunamadı: {outcome.Code}. {outcome.TechnicalDetail}");
            }

            if ((i + 1) % 10 == 0 || i == files.Count - 1)
            {
                progress?.Report(i + 1);
            }
        }

        if (failures > 0)
        {
            _log.Warn($"{failures} / {files.Count} dosya okunamadı.");
        }

        return OperationOutcome<IReadOnlyList<PdmFileSnapshot>>.Success(snapshots);
    }

    public OperationOutcome<PdmFileSnapshot> ReadSnapshot(
        PdmFileIdentity file,
        IReadOnlyList<PdmVariableDefinition> variables)
    {
        using var scope = new ComScope();

        try
        {
            if (_vault.GetObject(EdmObjectType.EdmObject_File, file.FileId) is not IEdmFile5 pdmFile)
            {
                return OperationOutcome<PdmFileSnapshot>.Failure(IssueCode.FileNotFound, file.ToString());
            }

            scope.Track(pdmFile);
            pdmFile.Refresh();

            var checkout = ReadCheckoutState(pdmFile);
            var hasPermission = ReadWritePermission(file, scope);
            var configurations = ReadConfigurations(pdmFile, scope);
            var stateName = ReadStateName(pdmFile, scope);

            var enumerator = scope.Track(pdmFile.GetEnumeratorVariable(string.Empty));
            var values = new Dictionary<CellCoordinate, VariableValue>(configurations.Count * variables.Count);

            foreach (var configuration in configurations)
            {
                var configurationText = configuration.ToPdmString(file.IsSolidWorksFile);

                foreach (var variable in variables)
                {
                    var value = ReadValue(enumerator, variable, configurationText);
                    values[new CellCoordinate(file, configuration, variable.VariableId)] = value;
                }
            }

            // Numaralandırıcı BURADA bırakılıyor. Kapsam sonunu beklemek, hemen ardından
            // gelen bir check-in çağrısı için geç kalırdı (bkz. ComScope açıklaması).
            scope.ReleaseNow(enumerator);

            return OperationOutcome<PdmFileSnapshot>.Success(new PdmFileSnapshot(
                file,
                pdmFile.CurrentVersion,
                checkout,
                stateName,
                configurations,
                values,
                hasPermission));
        }
        catch (COMException exception)
        {
            return OperationOutcome<PdmFileSnapshot>.Failure(
                PdmErrorTranslator.Translate(exception),
                PdmErrorTranslator.Describe(exception, _vault));
        }
    }

    private VariableValue ReadValue(
        IEdmEnumeratorVariable5 enumerator,
        PdmVariableDefinition variable,
        string configuration)
    {
        try
        {
            object? raw = null;
            bool found;

            if (PreferDatabaseRead && enumerator is IEdmEnumeratorVariable10 fromDb)
            {
                found = fromDb.GetVarFromDb(variable.Name, configuration, out raw);
            }
            else
            {
                found = enumerator.GetVar(variable.Name, configuration, out raw);
            }

            // GetVar false döndüğünde değer YOK demektir; bu bir hata değil. Boş bir kart
            // alanı ile okunamayan bir alanı karıştırmamak önemli: ilki normal, ikincisi
            // aşağıdaki catch bloğuna düşer.
            return found ? VariableValue.From(raw, variable.DataType) : VariableValue.Empty;
        }
        catch (COMException exception)
        {
            // Değişken bu kartta tanımlı olmayabilir. Yaygın ve normal bir durum; dosyanın
            // geri kalanının okunmasını engellemez.
            _log.Warn($"'{variable.Name}' okunamadı (konfig '{configuration}'): " +
                      PdmErrorTranslator.Describe(exception, _vault));

            return VariableValue.Empty;
        }
    }

    private CheckoutState ReadCheckoutState(IEdmFile5 file)
    {
        try
        {
            if (!file.IsLocked)
            {
                return CheckoutState.NotCheckedOut;
            }

            var lockedByUserId = file.LockedByUserID;
            var currentUserId = CurrentUserId();

            if (currentUserId != 0 && lockedByUserId == currentUserId)
            {
                // Aynı kullanıcı ama BAŞKA bir makine olabilir. O durumda yazamayız;
                // "benim çektiğim" saymak, yazma sırasında anlaşılmaz bir hataya yol açardı.
                var computer = file.LockedOnComputer ?? string.Empty;

                return string.Equals(computer, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                    ? CheckoutState.ByMe(computer)
                    : CheckoutState.ByOther(file.LockedByUser?.Name ?? string.Empty, computer);
            }

            return CheckoutState.ByOther(file.LockedByUser?.Name ?? string.Empty, file.LockedOnComputer);
        }
        catch (COMException exception)
        {
            _log.Warn("Check-out durumu okunamadı: " + PdmErrorTranslator.Describe(exception, _vault));
            return CheckoutState.Unknown;
        }
    }

    private int CurrentUserId()
    {
        try
        {
            var userManager = (IEdmUserMgr5)_vault;
            return userManager.GetLoggedInUser()?.ID ?? 0;
        }
        catch (COMException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Kullanıcının bu dosyanın kartını değiştirme yetkisi var mı.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>İmza:</b> <c>IEdmFolder5.HasRightsEx(int lRights, int lFileID)</c> — ikinci
    /// parametre <b>DOSYA</b> kimliğidir, klasör değil (parametre adları interop
    /// metadata'sından okundu).
    /// </para>
    /// <para>
    /// <b>Ama her hak dosya kapsamında anlamlı değil.</b> Gerçek vault'ta ölçüldü —
    /// üç farklı klasör, "Under Editing" dahil farklı iş akışı durumları:
    /// </para>
    /// <list type="table">
    /// <item><term><c>ChangeCard</c>, <c>lFileID = 0</c></term><description>True</description></item>
    /// <item><term><c>ChangeCard</c>, gerçek dosya kimliği</term><description><b>her zaman False</b></description></item>
    /// <item><term><c>Lock</c>, gerçek dosya kimliği</term><description>True</description></item>
    /// </list>
    /// <para>
    /// Yani <c>EdmRight_ChangeCard</c> <b>klasör kapsamlı</b> bir haktır ve dosya kimliğiyle
    /// sorulduğunda — durum ne olursa olsun — false döner. İlk iki sürüm bunu bilmediği için
    /// yönetici bir kullanıcıda bile önizlemedeki her satır "Yetki yok" görünüyordu.
    /// </para>
    /// <para>
    /// Bu yüzden iki hak <b>farklı kapsamlarda</b> sorulur: kart değiştirme klasörde,
    /// check-out ise dosyada. İkisi de gerekli — biri olmadan uygulama, kullanıcının
    /// anlamayacağı bir hatayla yarıda kalırdı.
    /// </para>
    /// <para>
    /// Yeniden ölçmek için: <c>docs/SPIKE-PHASE0.md</c> 6. madde.
    /// </para>
    /// </remarks>
    private bool ReadWritePermission(PdmFileIdentity file, ComScope scope)
    {
        // 0 = klasörün kendisi. Dosya kimliği DEĞİL.
        const int FolderScope = 0;

        try
        {
            if (_vault.GetObject(EdmObjectType.EdmObject_Folder, file.FolderId) is not IEdmFolder5 folder)
            {
                return false;
            }

            scope.Track(folder);

            var canChangeCard = folder.HasRightsEx((int)EdmRightFlags.EdmRight_ChangeCard, FolderScope);
            var canLock = folder.HasRightsEx((int)EdmRightFlags.EdmRight_Lock, file.FileId);

            if (!canChangeCard || !canLock)
            {
                // Hangi hakkın eksik olduğu günlüğe yazılır; "Yetki yok" tek başına
                // kullanıcıya da yöneticiye de yetmiyor.
                _log.Warn($"{file}: yazma yetkisi yok " +
                          $"(ChangeCard[klasör {file.FolderId}]={canChangeCard}, Lock[dosya]={canLock}).");
            }

            return canChangeCard && canLock;
        }
        catch (COMException exception)
        {
            // Okunamayan bir yetkiyi "yazılamaz" saymak kullanıcıyı gereksiz engellerdi ve
            // asıl reddi zaten PDM veriyor. Yanlış NEGATİF, yanlış pozitiften daha zararlı.
            _log.Warn($"{file} yetkisi okunamadı: " + PdmErrorTranslator.Describe(exception, _vault));
            return true;
        }
    }

    private List<ConfigurationKey> ReadConfigurations(IEdmFile5 file, ComScope scope)
    {
        var configurations = new List<ConfigurationKey>();

        try
        {
            // 0 = en son sürümün konfigürasyonları.
            object version = 0;
            var list = scope.Track(file.GetConfigurations(ref version));

            if (list is not null)
            {
                var position = scope.Track(list.GetHeadPosition());

                while (position is not null && !position.IsNull)
                {
                    var name = list.GetNext(position);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        var key = ConfigurationKey.Named(name);
                        if (!configurations.Contains(key))
                        {
                            configurations.Add(key);
                        }
                    }
                }
            }
        }
        catch (COMException exception)
        {
            _log.Warn($"{file.Name} konfigürasyonları okunamadı: " +
                      PdmErrorTranslator.Describe(exception, _vault));
        }

        // Konfigürasyonsuz dosyalar (Word, PDF, ...) tek bir dosya düzeyi satırı alır.
        if (configurations.Count == 0)
        {
            configurations.Add(ConfigurationKey.FileLevel);
        }

        return configurations;
    }

    private string ReadStateName(IEdmFile5 file, ComScope scope)
    {
        try
        {
            var state = scope.Track(file.CurrentState);
            return state?.Name ?? string.Empty;
        }
        catch (COMException)
        {
            // İş akışına dahil olmayan dosyalarda durum yok. Beklenen bir hâl.
            return string.Empty;
        }
    }
}
