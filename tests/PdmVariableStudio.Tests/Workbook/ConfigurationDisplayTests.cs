using PdmVariableStudio.Core.Domain;
using Xunit;

namespace PdmVariableStudio.Tests.Workbook;

/// <summary>
/// Konfigurasyon gosteriminin ve PDM'ye gonderilen dizenin ayri kavramlar oldugunu korur.
/// </summary>
/// <remarks>
/// "@" konfigurasyonu SOLIDWORKS'teki Custom sekmesine karsilik gelir; adlandirilmis
/// konfigurasyonlar Configuration Specific sekmelerine. PDM kartinda ikisi AYRI tutulur.
/// Ilk surumde dosya duzeyi arayuzde tire ile gosteriliyordu ve kullaniciya "burada deger
/// yok" izlenimi veriyordu.
/// </remarks>
public class ConfigurationDisplayTests
{
    [Fact]
    public void DosyaDuzeyi_ArayuzdeVeKitaptaAtIsaretiyleGosterilir()
    {
        Assert.Equal("@", ConfigurationKey.FileLevel.ToDisplayString());
        Assert.Equal("@", ConfigurationKey.FileLevel.ToString());
    }

    [Fact]
    public void AdlandirilmisKonfigurasyon_KendiAdiylaGosterilir()
    {
        var key = ConfigurationKey.Named("Uzun");

        Assert.Equal("Uzun", key.ToDisplayString());
        Assert.False(key.IsFileLevel);
    }

    [Fact]
    public void Gosterim_PdmyeGonderilenDizeyiDEGISTIRMEZ()
    {
        // Gosterim her iki dosya turunde de "@"; PDM'ye gonderilen dize ise farkli.
        var fileLevel = ConfigurationKey.FileLevel;

        Assert.Equal("@", fileLevel.ToDisplayString());
        Assert.Equal("@", fileLevel.ToPdmString(isSolidWorksFile: true));
        Assert.Equal(string.Empty, fileLevel.ToPdmString(isSolidWorksFile: false));
    }

    [Fact]
    public void AtIsaretiyleAdlandirilmisKonfigurasyon_DosyaDuzeyiSayilir()
    {
        // Kullanici Excel'de "@" yazdiysa ya da eski bir kitap "@" tasiyorsa ayni sey.
        Assert.True(ConfigurationKey.Named("@").IsFileLevel);
        Assert.True(ConfigurationKey.FromStorageString("@").IsFileLevel);
        Assert.True(ConfigurationKey.Named("  ").IsFileLevel);
    }
}
