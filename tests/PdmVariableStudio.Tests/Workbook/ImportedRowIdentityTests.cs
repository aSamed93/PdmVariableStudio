using System.Linq;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Workbook;
using Xunit;

namespace PdmVariableStudio.Tests.Workbook;

/// <summary>
/// Ice aktarilan satirin kimlik ve gosterim alanlari.
/// </summary>
/// <remarks>
/// Ilk surumde _Rows'tan okunan satirlar sahte bir dosya adiyla ("x.sldprt") kuruluyordu;
/// SOLIDWORKS bayragini tasimanin kestirme yoluydu ve onizleme tablosunda HER SATIR ayni
/// adi gosteriyordu. Kullanici hangi satirin hangi dosya oldugunu goremiyordu.
/// </remarks>
public class ImportedRowIdentityTests
{
    [Fact]
    public void GercekDosyaAdiVeKlasorYoluOkunur()
    {
        using var fixture = new WorkbookFixture().Write();
        var read = fixture.Read();

        var mil = read.Rows.Single(r => r.ExportRowId == 1);
        Assert.Equal("MIL-001.sldprt", mil.File.FileName);
        Assert.Equal(@"\Parts\Mil", mil.File.RelativePath);

        var govde = read.Rows.Single(r => r.ExportRowId == 3);
        Assert.Equal("GOVDE.sldasm", govde.File.FileName);
        Assert.Equal(@"\Assemblies", govde.File.RelativePath);
    }

    [Fact]
    public void SolidWorksBayragi_DosyaAdindanDegil_RowsSayfasindanGelir()
    {
        using var fixture = new WorkbookFixture().Write();

        // Kullanici korumayi kaldirip dosya adini bozmus olsun. Kimlik ve konfigurasyon
        // semantigi bundan ETKILENMEMELI.
        fixture.SetText(WorkbookSchema.VariablesSheet, 2, WorkbookSchema.ColFileName, "bozuk-ad.txt");

        var read = fixture.Read();
        var mil = read.Rows.Single(r => r.ExportRowId == 1);

        Assert.Equal("bozuk-ad.txt", mil.File.FileName);
        Assert.True(mil.File.IsSolidWorksFile);
        Assert.Equal(ConfigurationKey.FileLevelToken, mil.Configuration.ToPdmString(mil.File.IsSolidWorksFile));
    }

    [Fact]
    public void GenericDosya_SolidWorksSayilmaz()
    {
        using var fixture = new WorkbookFixture().Write();
        var read = fixture.Read();

        var docx = read.Rows.Single(r => r.ExportRowId == 4);

        Assert.Equal("sartname.docx", docx.File.FileName);
        Assert.False(docx.File.IsSolidWorksFile);

        // Generic dosyada dosya duzeyi bos dize ile gonderilir, "@" ile degil.
        Assert.Equal(string.Empty, docx.Configuration.ToPdmString(docx.File.IsSolidWorksFile));
    }

    [Fact]
    public void DosyaAdiDegistirilse_Bile_Eslestirme_KimlikUzerindenYurur()
    {
        using var fixture = new WorkbookFixture().Write();
        fixture.SetText(WorkbookSchema.VariablesSheet, 2, WorkbookSchema.ColFileName, "bambaska.sldprt");

        var read = fixture.Read();
        var mil = read.Rows.Single(r => r.ExportRowId == 1);

        // Kimlik _Rows'tan geldigi icin bozulmadi ve satir hala guvenilir.
        Assert.Equal(8814, mil.File.FileId);
        Assert.Equal(142, mil.File.FolderId);
        Assert.True(mil.IsTrustworthy);
    }

    [Fact]
    public void AcikSolidWorksBayragi_UzantiCikarimini_Ezer()
    {
        var explicitFalse = new PdmFileIdentity(1, 2, "parca.sldprt", isSolidWorksFile: false);
        var explicitTrue = new PdmFileIdentity(1, 2, "belge.docx", isSolidWorksFile: true);
        var derived = new PdmFileIdentity(1, 2, "parca.sldasm");

        Assert.False(explicitFalse.IsSolidWorksFile);
        Assert.True(explicitTrue.IsSolidWorksFile);
        Assert.True(derived.IsSolidWorksFile);
    }
}
