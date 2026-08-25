using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PdmVariableStudio.App.Threading;

/// <summary>
/// Alınan COM nesnelerini deterministik biçimde serbest bırakır.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden çöp toplayıcıya bırakmak yetmiyor:</b> PDM'in değişken numaralandırıcısı
/// (<c>IEdmEnumeratorVariable</c>) yaşadığı sürece yerel dosyayı açık tutuyor. Hemen ardından
/// gelen check-in <c>"An attempt was made to access a file that is exclusively opened by
/// another application"</c> ile düşüyor. Referansı toplayıcıya bırakmak çare değil: check-in
/// aynı metot içinde, toplayıcı hiç çalışmadan yapılıyor.
/// </para>
/// <para>
/// Serbest bırakma TERS SIRADA yapılır: türetilmiş nesneler (numaralandırıcı) kaynaklarından
/// (dosya) önce bırakılır.
/// </para>
/// <para>
/// <b>Apartment kuralı:</b> bir nesne hangi thread'de alındıysa orada bırakılmalıdır. Bu tip
/// her zaman adanmış STA worker thread'i içinde kullanılır (bkz. <see cref="PdmWorkQueue"/>).
/// </para>
/// </remarks>
internal sealed class ComScope : IDisposable
{
    private readonly List<object> _tracked = new();
    private bool _disposed;

    /// <summary>Nesneyi kapsam sonunda serbest bırakılmak üzere kaydeder ve geri döndürür.</summary>
    public T Track<T>(T comObject)
        where T : class
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            _tracked.Add(comObject);
        }

        return comObject!;
    }

    /// <summary>
    /// Nesneyi kapsamdan çıkarmadan HEMEN serbest bırakır.
    /// </summary>
    /// <remarks>
    /// Numaralandırıcıyı <c>Flush()</c> ile check-in arasında bırakmak için kullanılır;
    /// kapsam sonunu beklemek o senaryoda geç kalır.
    /// </remarks>
    public void ReleaseNow(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        _tracked.Remove(comObject);
        Release(comObject);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        for (var i = _tracked.Count - 1; i >= 0; i--)
        {
            Release(_tracked[i]);
        }

        _tracked.Clear();
    }

    private static void Release(object comObject)
    {
        try
        {
            if (Marshal.IsComObject(comObject))
            {
                // FinalReleaseComObject değil: aynı COM nesnesine başka bir RCW referansı
                // kalmış olabilir ve onu da geçersizleştirmek, hâlâ kullanılan bir nesneyi
                // öldürürdü. ReleaseComObject sayacı bir azaltır; doğru olan bu.
                Marshal.ReleaseComObject(comObject);
            }
        }
        catch (ArgumentException)
        {
            // Nesne zaten serbest bırakılmış. Beklenen bir yarış değil ama olursa
            // teşhis değeri yok; asıl işi durdurmaz.
        }
        catch (InvalidComObjectException)
        {
        }
    }
}
