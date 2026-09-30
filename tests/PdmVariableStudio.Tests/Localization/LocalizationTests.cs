using System;
using System.IO;
using System.Linq;
using PdmVariableStudio.Core.Localization;
using PdmVariableStudio.Core.Results;
using PdmVariableStudio.Core.Settings;
using PdmVariableStudio.Core.Workbook;
using PdmVariableStudio.Tests.Workbook;
using Xunit;

namespace PdmVariableStudio.Tests.Localization;

/// <summary>
/// Dil seçimi kuralı, çeviri eksiksizliği ve dil değiştiğinde verinin bozulmaması.
/// </summary>
/// <remarks>
/// Testler süreç dilini değiştirmez; <see cref="Loc.Scope"/> ile yalnızca kendi akışlarında
/// dil seçer. xUnit sınıfları paralel koşturduğu için süreç dilini değiştirmek, Türkçe metin
/// bekleyen başka bir testi rastgele düşürürdü.
/// </remarks>
public class LocalizationTests
{
    // ------------------------------------------------------------ dil seçimi

    [Theory]
    [InlineData("en", "tr", "tr-TR", UiLanguage.English)]   // kullanıcı seçimi her şeyi geçer
    [InlineData("tr", "en", "en-US", UiLanguage.Turkish)]
    [InlineData("", "en", "tr-TR", UiLanguage.English)]     // makine varsayılanı Windows'u geçer
    [InlineData("", "tr", "de-DE", UiLanguage.Turkish)]
    [InlineData("", "", "tr-TR", UiLanguage.Turkish)]       // son çare Windows dili
    [InlineData("", "", "de-DE", UiLanguage.English)]       // Türkçe olmayan her dil İngilizce
    [InlineData("", "", "", UiLanguage.English)]
    [InlineData("fr", "de", "tr-TR", UiLanguage.Turkish)]   // tanınmayan değer atlanır
    [InlineData(" EN-us ", "", "tr-TR", UiLanguage.English)] // büyük/küçük harf, boşluk, kültür adı
    public void DilCozumu_OncelikSirasiniIzler(string user, string machine, string windows, UiLanguage expected)
    {
        Assert.Equal(expected, UiLanguages.Resolve(user, machine, windows));
    }

    [Fact]
    public void DilKodu_GidipGelir()
    {
        foreach (var language in new[] { UiLanguage.Turkish, UiLanguage.English })
        {
            Assert.Equal(language, UiLanguages.Parse(UiLanguages.ToCode(language)));
        }
    }

    [Fact]
    public void Kapsam_BittigindeOncekiDileDoner()
    {
        using (Loc.Scope(UiLanguage.Turkish))
        {
            using (Loc.Scope(UiLanguage.English))
            {
                Assert.Equal("en", Loc.T("tr", "en"));
            }

            Assert.Equal("tr", Loc.T("tr", "en"));
        }
    }

    [Fact]
    public void Sayi_IngilizcedeTekilCogulAyrilir()
    {
        using (Loc.Scope(UiLanguage.English))
        {
            Assert.Equal("1 file", Loc.N(1, "dosya", "file", "files"));
            Assert.Equal("0 files", Loc.N(0, "dosya", "file", "files"));
            Assert.Equal("3 files", Loc.N(3, "dosya", "file", "files"));
        }

        using (Loc.Scope(UiLanguage.Turkish))
        {
            Assert.Equal("3 dosya", Loc.N(3, "dosya", "file", "files"));
        }
    }

    // ------------------------------------------------------------ çeviri eksiksizliği

    /// <summary>
    /// Her <see cref="IssueCode"/> için iki dilde de metin olmalı ve İngilizce, Türkçenin
    /// kopyası olmamalı. Yeni bir kod eklenip İngilizcesi unutulursa burada düşer.
    /// </summary>
    [Fact]
    public void HerHataKodununIngilizceMetniVar()
    {
        foreach (var code in Enum.GetValues(typeof(IssueCode)).Cast<IssueCode>().Where(c => c != IssueCode.None))
        {
            string trSummary, trDetail, enSummary, enDetail;

            using (Loc.Scope(UiLanguage.Turkish))
            {
                trSummary = IssueText.Summary(code);
                trDetail = IssueText.Detail(code);
            }

            using (Loc.Scope(UiLanguage.English))
            {
                enSummary = IssueText.Summary(code);
                enDetail = IssueText.Detail(code);
            }

            Assert.False(string.IsNullOrWhiteSpace(enSummary), $"{code}: İngilizce özet boş.");
            Assert.False(string.IsNullOrWhiteSpace(enDetail), $"{code}: İngilizce ayrıntı boş.");
            Assert.NotEqual(trDetail, enDetail);

            // "Çakışma" / "Conflict" gibi kısa özetler farklı olmalı; ortak bir özet yok.
            Assert.NotEqual(trSummary, enSummary);
            Assert.DoesNotMatch("[çğıöşüÇĞİÖŞÜ]", enSummary + enDetail);
        }
    }

    // ------------------------------------------------------------ çalışma kitabı

    [Fact]
    public void IngilizceBasliklar_Yazilir()
    {
        using var fixture = new WorkbookFixture();

        using (Loc.Scope(UiLanguage.English))
        {
            fixture.Write();
        }

        var headers = fixture.Headers();

        Assert.Equal("File Name", headers[WorkbookSchema.ColFileName - 1]);
        Assert.Equal("Folder", headers[WorkbookSchema.ColRelativePath - 1]);
        Assert.Equal("Configuration", headers[WorkbookSchema.ColConfiguration - 1]);
        Assert.Contains("Revizyon (read-only)", headers);
    }

    /// <summary>
    /// İngilizce arayüzde dışa aktarılan kitap Türkçe arayüzde okunduğunda (ve tersi) hiçbir
    /// sütun "silinmiş" sayılmamalı. Salt okunur değişkenin başlık eki dile göre değiştiği için
    /// okuyucu yalnızca kendi dilinin ekini tanısaydı bu sütun kaybolurdu.
    /// </summary>
    [Theory]
    [InlineData(UiLanguage.English, UiLanguage.Turkish)]
    [InlineData(UiLanguage.Turkish, UiLanguage.English)]
    public void BaskaDildeDisaAktarilanKitap_SorunsuzOkunur(UiLanguage exportedIn, UiLanguage readIn)
    {
        using var fixture = new WorkbookFixture();

        using (Loc.Scope(exportedIn))
        {
            fixture.Write();
        }

        ImportedWorkbook read;
        using (Loc.Scope(readIn))
        {
            read = fixture.Read();
        }

        Assert.False(read.IsRejected);
        Assert.Empty(read.Issues);
        Assert.All(read.VariableColumns, column => Assert.True(column > 0));
        Assert.All(read.Rows, row => Assert.True(row.IsTrustworthy));
    }

    [Fact]
    public void BaskaDildeTasinmisSaltOkunurSutun_BaslikEkiyleBulunur()
    {
        using var fixture = new WorkbookFixture();

        using (Loc.Scope(UiLanguage.English))
        {
            fixture.Write();
        }

        // "Revizyon (read-only)" (sütun 10) kullanılmayan bir sütuna taşınır.
        fixture.MoveColumn(WorkbookSchema.VariablesSheet, 10, 20);

        ImportedWorkbook read;
        using (Loc.Scope(UiLanguage.Turkish))
        {
            read = fixture.Read();
        }

        Assert.Contains(read.Issues, i => i.Code == IssueCode.ColumnRelocated);
        Assert.DoesNotContain(read.Issues, i => i.Code == IssueCode.VariableColumnMissing);
        Assert.Equal(20, read.VariableColumns.Last());
    }

    // ------------------------------------------------------------ ayarlar

    /// <summary>
    /// Varsayılan check-in yorumu ayar dosyasına yazılıyor. Dil değiştiğinde kullanıcının hiç
    /// yazmadığı bu yorum eski dilde kalmamalı; kullanıcının kendi yorumuna ise dokunulmamalı.
    /// </summary>
    [Fact]
    public void VarsayilanCheckInYorumu_DilDegisinceCevrilir()
    {
        var path = Path.Combine(Path.GetTempPath(), "pvs-l-" + Guid.NewGuid().ToString("N"), "settings.json");

        try
        {
            using (Loc.Scope(UiLanguage.Turkish))
            {
                new StudioSettings().Save(path);
            }

            using (Loc.Scope(UiLanguage.English))
            {
                Assert.Equal(StudioSettings.DefaultCheckInComment(), StudioSettings.Load(path).CheckInComment);
                Assert.DoesNotMatch("[çğıöşüÇĞİÖŞÜ]", StudioSettings.Load(path).CheckInComment);
            }

            using (Loc.Scope(UiLanguage.Turkish))
            {
                new StudioSettings { CheckInComment = "Kendi yorumum" }.Save(path);
            }

            using (Loc.Scope(UiLanguage.English))
            {
                Assert.Equal("Kendi yorumum", StudioSettings.Load(path).CheckInComment);
            }
        }
        finally
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
