using System;
using System.IO;
using PdmVariableStudio.Core.Settings;
using Xunit;

namespace PdmVariableStudio.Tests.Settings;

/// <summary>
/// Ayar dosyası toleranslı olmalı: yok, bozuk ya da eksik alanlı dosya varsayılanlara düşer;
/// yazılan her değer okunduğunda aynen geri gelir.
/// </summary>
public class StudioSettingsTests : IDisposable
{
    private readonly string _root;
    private readonly string _path;

    public StudioSettingsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pvs-s-" + Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_root, "alt", "settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void DosyaYoksa_VarsayilanlarGelir()
    {
        var settings = StudioSettings.Load(_path);

        Assert.False(settings.IncludeSubfolders);
        Assert.True(settings.CheckInAfterApply);
        Assert.NotEmpty(settings.CheckInComment);
        Assert.Equal(string.Empty, settings.LastExportDirectory);
        Assert.Equal(string.Empty, settings.JournalRoot);
    }

    [Fact]
    public void TamDonus_TumAlanlarKorunur()
    {
        var written = new StudioSettings
        {
            IncludeSubfolders = true,
            CheckInAfterApply = false,
            CheckInComment = "Türkçe \"tırnaklı\" yorum \\ ters bölü",
            LastExportDirectory = @"C:\Users\ali\Masaüstü",
            JournalRoot = @"\\sunucu\pdm\gunluk",
        };

        written.Save(_path);
        var read = StudioSettings.Load(_path);

        Assert.True(read.IncludeSubfolders);
        Assert.False(read.CheckInAfterApply);
        Assert.Equal(written.CheckInComment, read.CheckInComment);
        Assert.Equal(written.LastExportDirectory, read.LastExportDirectory);
        Assert.Equal(written.JournalRoot, read.JournalRoot);
    }

    [Fact]
    public void BozukDosya_FirlatmazVarsayilanaDuser()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{\"includeSubfolders\": tru");

        var settings = StudioSettings.Load(_path);

        Assert.False(settings.IncludeSubfolders);
        Assert.True(settings.CheckInAfterApply);
    }

    [Fact]
    public void EksikAlan_EskiDosya_VarsayilanTamamlar()
    {
        // Eski sürümün yazdığı, journalRoot alanı olmayan bir dosya.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{\"includeSubfolders\":true,\"checkInComment\":\"x\"}");

        var settings = StudioSettings.Load(_path);

        Assert.True(settings.IncludeSubfolders);
        Assert.Equal("x", settings.CheckInComment);
        Assert.True(settings.CheckInAfterApply);
        Assert.Equal(string.Empty, settings.JournalRoot);
    }

    [Fact]
    public void UstUsteKayit_GeciciDosyaBirakmaz()
    {
        var settings = new StudioSettings();
        settings.Save(_path);
        settings.IncludeSubfolders = true;
        settings.Save(_path);

        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
        Assert.True(StudioSettings.Load(_path).IncludeSubfolders);
    }
}
