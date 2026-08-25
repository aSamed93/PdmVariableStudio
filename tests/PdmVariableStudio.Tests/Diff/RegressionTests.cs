using System.Globalization;
using PdmVariableStudio.Core.Domain;
using Xunit;

namespace PdmVariableStudio.Tests.Diff;

/// <summary>
/// Gerçekte yaşanmış ayrıştırma hatalarının nöbetçileri. Buradaki bir gerileme sessiz veri
/// bozulması demektir; testler bu yüzden ayrı bir dosyada ve gerekçeleriyle duruyor.
/// </summary>
public class NumberParsingRegressionTests
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    [Fact]
    public void TurkceOndalik_BinlikAyraciSanilipOnKatBuyutulmez()
    {
        // Ilk surumde invariant kultur AllowThousands ile deneniyordu ve "12,4" -> 124
        // olarak ayristiriliyordu. Turkce yazan bir kullanicinin agirlik degeri PDM'ye
        // on kat buyuk yazilirdi.
        var value = VariableValue.FromRawText("12,4", PdmVariableType.Float, Tr);

        Assert.Equal(VariableValueKind.Float, value.Kind);
        Assert.Equal("12.4", value.ToStorageString());
    }

    [Fact]
    public void InvariantOndalik_TurkceKulturVarkenBileDogruOkunur()
    {
        // _Rows sayfasindaki orijinal deger her zaman invariant yazilir; kullanicinin
        // kulturu tr-TR olsa bile geri okunusu bozulmamali.
        var value = VariableValue.FromRawText("12.4", PdmVariableType.Float, Tr);

        Assert.Equal("12.4", value.ToStorageString());
    }

    [Fact]
    public void TamsayiAlaninaTurkceOndalik_YanlisBuyutulmez()
    {
        var value = VariableValue.FromRawText("12,0", PdmVariableType.Int, Tr);

        Assert.Equal(VariableValueKind.Int, value.Kind);
        Assert.Equal("12", value.ToStorageString());
    }

    [Fact]
    public void GruplanmisSayi_HicbirYorumTutmazsaKabulEdilir()
    {
        // Ayracsiz yorumlarin hicbiri tutmazsa gruplu yoruma dusulur.
        var value = VariableValue.FromRawText("1.234,56", PdmVariableType.Float, Tr);

        Assert.Equal(VariableValueKind.Float, value.Kind);
        Assert.Equal("1234.56", value.ToStorageString());
    }

    [Fact]
    public void DepolamaBicimi_TamDonusYapar()
    {
        foreach (var (raw, type) in new (string, PdmVariableType)[]
                 {
                     ("Ana mil", PdmVariableType.Text),
                     ("42", PdmVariableType.Int),
                     ("12.4", PdmVariableType.Float),
                     ("true", PdmVariableType.Bool),
                     ("2026-03-14", PdmVariableType.Date),
                 })
        {
            var original = VariableValue.FromStorage(raw, type);
            var roundTripped = VariableValue.FromStorage(original.ToStorageString(), type);

            Assert.Equal(original, roundTripped);
        }
    }
}
