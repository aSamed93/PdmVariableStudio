using System;
using System.Globalization;
using System.IO;
using System.Text;
using PdmVariableStudio.Core.Abstractions;

namespace PdmVariableStudio.Core.Diagnostics;

/// <summary>
/// Dosyaya yazan, boyutu sınırlı günlük.
/// </summary>
/// <remarks>
/// <para>
/// Eklenti PDM sürecinin içinde çalışır; konsol yoktur. Tüm hata ayrıntıları ve yığın izleri
/// buraya yazılır ve hata iletişim kutuları kullanıcıya bu dosyanın yolunu gösterir.
/// </para>
/// <para>
/// <b>Ne yazılmaz:</b> kart değerlerinin tamamı, dosya içerikleri, parolalar. Günlüğe
/// kimlikler (dosya/değişken/işlem numaraları), hata kodları ve süreler girer. Değerler
/// yalnızca çakışma teşhisinde ve kısaltılmış olarak yazılır — bir vault'un tüm metadata'sını
/// düz metin bir dosyaya kopyalamak kabul edilebilir değil.
/// </para>
/// </remarks>
public sealed class StudioLog : IStudioLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int MaxArchivedFiles = 5;

    private static readonly object Gate = new();

    public StudioLog(string? filePath = null)
    {
        FilePath = filePath ?? DefaultPath();
    }

    public string FilePath { get; }

    public static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PdmVariableStudio",
            "studio.log");

    public void Info(string message) => Write("BILGI", message);

    public void Warn(string message) => Write("UYARI", message);

    public void Error(string message, Exception? exception = null) =>
        Write("HATA", exception is null ? message : message + Environment.NewLine + Describe(exception));

    /// <summary>
    /// Bir istisnayı, kullanıcıya gösterilebilecek kadar kısa ama teşhise yetecek kadar
    /// ayrıntılı biçimde tanımlar.
    /// </summary>
    public static string Describe(Exception exception)
    {
        if (exception is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var current = exception;
        var depth = 0;

        while (current is not null && depth < 5)
        {
            builder.Append(current.GetType().Name).Append(": ").Append(current.Message);

            if (current is System.Runtime.InteropServices.COMException com)
            {
                builder.Append(" (HRESULT 0x")
                    .Append(com.ErrorCode.ToString("X8", CultureInfo.InvariantCulture))
                    .Append(')');
            }

            current = current.InnerException;
            depth++;

            if (current is not null)
            {
                builder.Append(Environment.NewLine).Append("  -> ");
            }
        }

        if (exception.StackTrace is not null)
        {
            builder.Append(Environment.NewLine).Append(exception.StackTrace);
        }

        return builder.ToString();
    }

    private void Write(string level, string message)
    {
        // Kilit bilinçli: eklenti içinde birden fazla thread (UI, STA worker, thread pool)
        // aynı anda yazabiliyor ve karışmış satırlar teşhisi imkânsız hâle getiriyor.
        lock (Gate)
        {
            try
            {
                EnsureDirectory();
                RollIfTooLarge();

                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss.fff} [{1,-5}] [{2,3}] {3}",
                    DateTime.Now,
                    level,
                    System.Threading.Thread.CurrentThread.ManagedThreadId,
                    message);

                File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Günlük yazılamıyorsa asıl işi durdurmayız. Teşhis kaybı, işlevsellik
                // kaybından iyidir; günlüğün kendisi hiçbir zaman kritik yolda değil.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory!);
        }
    }

    private void RollIfTooLarge()
    {
        if (!File.Exists(FilePath))
        {
            return;
        }

        var info = new FileInfo(FilePath);
        if (info.Length < MaxBytes)
        {
            return;
        }

        var oldest = FilePath + "." + MaxArchivedFiles.ToString(CultureInfo.InvariantCulture);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = MaxArchivedFiles - 1; i >= 1; i--)
        {
            var from = FilePath + "." + i.ToString(CultureInfo.InvariantCulture);
            var to = FilePath + "." + (i + 1).ToString(CultureInfo.InvariantCulture);

            if (File.Exists(from))
            {
                if (File.Exists(to))
                {
                    File.Delete(to);
                }

                File.Move(from, to);
            }
        }

        File.Move(FilePath, FilePath + ".1");
    }
}
