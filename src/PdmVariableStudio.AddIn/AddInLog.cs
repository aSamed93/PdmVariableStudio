using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace PdmVariableStudio.AddIn;

/// <summary>
/// Eklentinin kendi küçük günlüğü.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden Core'daki <c>StudioLog</c> değil:</b> eklenti paketine giren her DLL, vault'a
/// yüklenmesi ve unutulmaması gereken bir dosya daha demek. <c>PdmVariableStudio.Core</c>
/// sık değişen bir bileşen (karşılaştırma kuralları, çalışma kitabı düzeni); onu vault'a
/// koymak, her sürümde tüm istemcilerde Explorer kapatmayı gerektirirdi. Eklentinin tek
/// ihtiyacı birkaç satır günlük olduğu için burada kendi kopyası duruyor ve paket iki DLL'de
/// kalıyor.
/// </para>
/// <para>
/// Uygulamanın kendi günlüğüyle <b>aynı dosyaya</b> yazar; bir sorunun izini sürerken
/// "eklenti komutu tetikledi mi, uygulama açıldı mı" sorusunun cevabı tek dosyada, sırayla
/// görünsün diye.
/// </para>
/// </remarks>
internal static class AddInLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PdmVariableStudio",
        "studio.log");

    public static void Info(string message) => Write("BILGI", message);

    public static void Warn(string message) => Write("UYARI", message);

    public static void Error(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            Write("HATA", message);
            return;
        }

        var detail = exception.GetType().Name + ": " + exception.Message;

        if (exception is System.Runtime.InteropServices.COMException com)
        {
            detail += $" (HRESULT 0x{com.ErrorCode:X8})";
        }

        Write("HATA", message + Environment.NewLine + detail + Environment.NewLine + exception.StackTrace);
    }

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss.fff} [{1,-5}] [eklenti] {2}",
                    DateTime.Now,
                    level,
                    message);

                File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Günlük yazılamıyorsa asıl işi durdurmayız.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
