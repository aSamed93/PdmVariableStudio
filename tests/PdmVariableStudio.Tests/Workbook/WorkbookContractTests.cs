using System;
using System.Linq;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;
using PdmVariableStudio.Core.Workbook;
using Xunit;

namespace PdmVariableStudio.Tests.Workbook;

/// <summary>
/// Çalışma kitabı sözleşmesinin testleri.
/// </summary>
/// <remarks>
/// Testlerin çoğu "kullanıcı dosyayı bozdu" senaryolarıdır. Buradaki temel beklenti şudur:
/// şüpheli bir satır UYGULANMAZ. Bir satırı atlamak, yanlış dosyaya yazmaktan her zaman iyidir.
/// </remarks>
public class WorkbookContractTests
{
    // ------------------------------------------------------------------ tam dönüş

    [Fact]
    public void GecerliKitap_YazilipOkununcaAyniIcerigiVerir()
    {
        using var fixture = new WorkbookFixture().Write();
        var read = fixture.Read();

        Assert.False(read.IsRejected);
        Assert.Equal(WorkbookSchema.CurrentVersion, read.SchemaVersion);
        Assert.Equal(WorkbookFixture.SessionId, read.ExportSessionId);
        Assert.Equal("MakinaVault", read.Vault.Name);
        Assert.Equal(4, read.Rows.Count);
        Assert.Equal(6, read.Variables.Count);
    }

    [Fact]
    public void TipliHucreler_DogruDegerlerleGeriOkunur()
    {
        using var fixture = new WorkbookFixture().Write();
        var read = fixture.Read();

        var row = read.Rows.Single(r => r.ExportRowId == 1);

        Assert.Equal("Ana mil", Value(read, row, "Description").ToStorageString());
        Assert.Equal("12.4", Value(read, row, "Weight").ToStorageString());
        Assert.Equal("2026-03-14", Value(read, row, "ReleaseDate").ToStorageString());
        Assert.Equal("true", Value(read, row, "Approved").ToStorageString());
    }

    [Fact]
    public void OrijinalDegerler_InvariantOlarakKorunur()
    {
        using var fixture = new WorkbookFixture().Write();
        var read = fixture.Read();

        var row = read.Rows.Single(r => r.ExportRowId == 1);
        var weightIndex = IndexOf(read, "Weight");

        Assert.Equal("12.4", row.OriginalValues[weightIndex].ToStorageString());
    }

    [Fact]
    public void Konfigurasyon_DosyaDuzeyiVeAdlandirilmisAyriKorunur()
    {
        using var fixture = new WorkbookFixture().Write();
        var read = fixture.Read();

        Assert.True(read.Rows.Single(r => r.ExportRowId == 1).Configuration.IsFileLevel);
        Assert.Equal("Uzun", read.Rows.Single(r => r.ExportRowId == 2).Configuration.Name);
    }

    [Fact]
    public void DosyaKimligi_RowsSayfasindanGelir()
    {
        using var fixture = new WorkbookFixture().Write();
        var read = fixture.Read();

        var row = read.Rows.Single(r => r.ExportRowId == 3);

        Assert.Equal(8815, row.File.FileId);
        Assert.Equal(143, row.File.FolderId);
    }

    // ------------------------------------------------------- ölümcül bozulmalar

    [Fact]
    public void MetadataSayfasiSilinmisse_KitapReddedilir()
    {
        using var fixture = new WorkbookFixture().Write();
        fixture.DeleteSheet(WorkbookSchema.MetadataSheet);

        var read = fixture.Read();

        Assert.True(read.IsRejected);
        Assert.Contains(read.Issues, i => i.Code == IssueCode.WorkbookCorrupted);
    }

    [Fact]
    public void RowsSayfasiSilinmisse_KitapReddedilir()
    {
        // _Rows olmadan orijinal snapshot yok; three-way karşılaştırma yapılamaz ve
        // iki değerle çalışmak başkasının değişikliğini yok etme riski demektir.
        using var fixture = new WorkbookFixture().Write();
        fixture.DeleteSheet(WorkbookSchema.RowsSheet);

        var read = fixture.Read();

        Assert.True(read.IsRejected);
        Assert.Contains(read.Issues, i => i.Code == IssueCode.WorkbookCorrupted);
    }

    [Fact]
    public void GelecekSemaSurumu_KitapReddedilirVeHicbirIslemYapilmaz()
    {
        using var fixture = new WorkbookFixture().Write();
        fixture.SetText(WorkbookSchema.MetadataSheet, 1, 2, "99");

        var read = fixture.Read();

        Assert.True(read.IsRejected);
        Assert.Contains(read.Issues, i => i.Code == IssueCode.UnsupportedWorkbookVersion);
        Assert.Empty(read.Rows);
    }

    [Fact]
    public void MetadataDegistirilmisse_ChecksumYakalar()
    {
        using var fixture = new WorkbookFixture().Write();

        // Vault adını değiştirmek en tehlikeli müdahale: dosya kimlikleri başka bir vault'ta
        // bambaşka dosyalara denk gelir.
        fixture.SetText(WorkbookSchema.MetadataSheet, 7, 2, "BaskaVault");

        var read = fixture.Read();

        Assert.True(read.IsRejected);
        Assert.Contains(read.Issues, i => i.Code == IssueCode.MetadataChecksumMismatch);
    }

    [Fact]
    public void DegiskenTanimiDegistirilmisse_ChecksumYakalar()
    {
        using var fixture = new WorkbookFixture().Write();

        // Değişken tablosunun ilk tanım satırındaki VariableId'yi bozalım.
        var definitionRow = FindVariableTableFirstRow();
        fixture.SetText(WorkbookSchema.MetadataSheet, definitionRow, 1, "999");

        var read = fixture.Read();

        Assert.True(read.IsRejected);
        Assert.Contains(read.Issues, i => i.Code == IssueCode.MetadataChecksumMismatch);
    }

    // ------------------------------------------------------ satır düzeyi bozulmalar

    [Fact]
    public void RowsSayfasindakiOrijinalDegerDegistirilmisse_SatirDamgasiTutmaz()
    {
        using var fixture = new WorkbookFixture().Write();

        // İlk veri satırının ilk orijinal değerini değiştir.
        fixture.SetText(WorkbookSchema.RowsSheet, 2, WorkbookSchema.RowsFirstValueColumn, "Sahte");

        var read = fixture.Read();

        Assert.False(read.IsRejected);

        var row = read.Rows.Single(r => r.ExportRowId == 1);
        Assert.False(row.IsTrustworthy);
        Assert.Contains(row.Issues, i => i.Code == IssueCode.RowTampered);
    }

    [Fact]
    public void RowsSayfasindakiFileIdDegistirilmisse_SatirDamgasiTutmaz()
    {
        // En tehlikeli senaryo: değerler doğru görünür ama yanlış dosyaya yazılırdı.
        using var fixture = new WorkbookFixture().Write();
        fixture.SetText(WorkbookSchema.RowsSheet, 2, WorkbookSchema.RowsColFileId, "9999");

        var read = fixture.Read();
        var row = read.Rows.Single(r => r.ExportRowId == 1);

        Assert.False(row.IsTrustworthy);
        Assert.Contains(row.Issues, i => i.Code == IssueCode.RowTampered);
    }

    [Fact]
    public void KullaniciSatirSilmisse_KalanlarNormalOkunur()
    {
        using var fixture = new WorkbookFixture().Write();
        fixture.DeleteRow(WorkbookSchema.VariablesSheet, 3);

        var read = fixture.Read();

        Assert.False(read.IsRejected);
        Assert.Equal(3, read.Rows.Count);
        Assert.DoesNotContain(read.Rows, r => r.ExportRowId == 2);

        // Silinen satır bir hata değil: kullanıcı o dosyayla ilgilenmiyor demektir.
        Assert.All(read.Rows, r => Assert.True(r.IsTrustworthy));
    }

    [Fact]
    public void KullaniciYeniSatirEklemisse_TaninmayanSatirAtlanir()
    {
        using var fixture = new WorkbookFixture().Write();
        fixture.SetText(WorkbookSchema.VariablesSheet, 99, WorkbookSchema.ColExportRowId, "500");
        fixture.SetText(WorkbookSchema.VariablesSheet, 99, WorkbookSchema.FirstVariableColumn, "Yeni dosya");

        var read = fixture.Read();

        Assert.False(read.IsRejected);
        Assert.Equal(4, read.Rows.Count);
        Assert.DoesNotContain(read.Rows, r => r.ExportRowId == 500);
        Assert.Contains(read.Issues, i => i.Code == IssueCode.UnknownRow);
    }

    [Fact]
    public void AyniSatirNumarasiIkiKezVarsa_IkisiDeGuvenilmezOlur()
    {
        using var fixture = new WorkbookFixture().Write();
        fixture.DuplicateRow(WorkbookSchema.VariablesSheet, 2);

        var read = fixture.Read();

        var duplicated = read.Rows.Where(r => r.ExportRowId == 1).ToList();
        Assert.All(duplicated, r => Assert.False(r.IsTrustworthy));
        Assert.Contains(read.Issues, i => i.Code == IssueCode.DuplicateRow);
    }

    [Fact]
    public void KullaniciSatirlariSiralamissa_EslemeBozulmaz()
    {
        using var fixture = new WorkbookFixture().Write();
        fixture.ReverseDataRows(WorkbookSchema.VariablesSheet);

        var read = fixture.Read();

        Assert.Equal(4, read.Rows.Count);

        // Satır indisine değil, # numarasına göre eşlendiği için kimlik doğru kalır.
        var mil = read.Rows.Single(r => r.ExportRowId == 1);
        Assert.Equal(8814, mil.File.FileId);
        Assert.Equal("Ana mil", Value(read, mil, "Description").ToStorageString());
    }

    // ------------------------------------------------------ sütun düzeyi bozulmalar

    [Fact]
    public void KullaniciSutunuTasimissa_BaslikMetniyleBulunurVeUyariUretilir()
    {
        using var fixture = new WorkbookFixture().Write();

        // "Malzeme" sütununu (6) kullanılmayan bir sütuna (20) taşı.
        fixture.MoveColumn(WorkbookSchema.VariablesSheet, 6, 20);

        var read = fixture.Read();

        Assert.False(read.IsRejected);
        Assert.Contains(read.Issues, i => i.Code == IssueCode.ColumnRelocated);

        var row = read.Rows.Single(r => r.ExportRowId == 1);
        Assert.Equal("Ç1040", Value(read, row, "Material").ToStorageString());
    }

    [Fact]
    public void KullaniciDegiskenSutunuSilmisse_ODegiskenAtlanir()
    {
        using var fixture = new WorkbookFixture().Write();
        fixture.DeleteColumn(WorkbookSchema.VariablesSheet, 6);

        var read = fixture.Read();

        Assert.False(read.IsRejected);
        Assert.Contains(read.Issues, i => i.Code == IssueCode.VariableColumnMissing);

        // Sütun bulunamadı: değer null gelir ve mevcut PDM değerine dokunulmaz.
        var index = IndexOf(read, "Material");
        Assert.Equal(0, read.VariableColumns[index]);
        Assert.All(read.Rows, r => Assert.Null(r.RawExcelValues[index]));
    }

    // ------------------------------------------------------------- kullanıcı düzenlemesi

    [Fact]
    public void KullaniciDegerDegistirmisse_HamDegerOkunur()
    {
        using var fixture = new WorkbookFixture().Write();
        fixture.SetText(WorkbookSchema.VariablesSheet, 2, 6, "Ç4140");

        var read = fixture.Read();
        var row = read.Rows.Single(r => r.ExportRowId == 1);

        Assert.True(row.IsTrustworthy);
        Assert.Equal("Ç4140", Value(read, row, "Material").ToStorageString());

        // Orijinal snapshot değişmedi: three-way karşılaştırmanın referansı sağlam.
        Assert.Equal("Ç1040", row.OriginalValues[IndexOf(read, "Material")].ToStorageString());
    }

    [Fact]
    public void SayisalAlanaMetinYazilirsa_AyristirilamazOlarakIsaretlenir()
    {
        using var fixture = new WorkbookFixture().Write();
        fixture.SetText(WorkbookSchema.VariablesSheet, 2, 7, "abc");

        var read = fixture.Read();
        var row = read.Rows.Single(r => r.ExportRowId == 1);

        Assert.True(Value(read, row, "Weight").IsUnparseable);
    }

    [Fact]
    public void DosyaYoksa_KitapReddedilir()
    {
        var read = new WorkbookReader().Read("C:\\olmayan\\dosya.xlsx");

        Assert.True(read.IsRejected);
        Assert.Contains(read.Issues, i => i.Code == IssueCode.WorkbookCorrupted);
    }

    [Fact]
    public void XlsxOlmayanDosya_KitapReddedilir()
    {
        using var fixture = new WorkbookFixture();
        System.IO.File.WriteAllText(fixture.Path_, "bu bir excel dosyası değil");

        var read = fixture.Read();

        Assert.True(read.IsRejected);
    }

    // --------------------------------------------------------------- yardımcılar

    /// <summary>
    /// <c>_Metadata</c> sayfasında değişken tanımlarının başladığı satır: anahtar/değer
    /// çiftleri + işaretçi + başlık satırı.
    /// </summary>
    private static int FindVariableTableFirstRow()
    {
        // 14 anahtar/değer + checksum = 15 satır, sonra işaretçi (16), sonra başlık (17).
        return 18;
    }

    private static int IndexOf(ImportedWorkbook workbook, string variableName)
    {
        for (var i = 0; i < workbook.Variables.Count; i++)
        {
            if (workbook.Variables[i].Name == variableName)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"'{variableName}' değişkeni yok.");
    }

    private static VariableValue Value(ImportedWorkbook workbook, ImportedRow row, string variableName)
    {
        var index = IndexOf(workbook, variableName);
        return VariableValue.From(row.RawExcelValues[index], workbook.Variables[index].DataType);
    }
}
