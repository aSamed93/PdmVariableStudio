using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using PdmVariableStudio.Core.Diff;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;
using PdmVariableStudio.Core.Services;
using PdmVariableStudio.Core.Workbook;
using PdmVariableStudio.Tests.Fakes;
using Xunit;

namespace PdmVariableStudio.Tests.Assemblies;

/// <summary>
/// Montajdan dışa aktarım → Excel → içe aktarım, uçtan uca.
/// </summary>
/// <remarks>
/// Asıl risk bilgi sütunları: değişken sütunlarını sağa kaydırıyorlar. Okuyucu sütunları
/// <c>_Metadata</c>'daki <c>ColumnIndex</c>'ten buluyor; bu testler kaymanın yanlış değişkene
/// yazmaya dönüşmediğini ve kitabın şemaya uygun kaldığını doğrular.
/// </remarks>
public class AssemblyExportTests : IDisposable
{
    private static readonly PdmVariableDefinition Description =
        new(60, "Description", PdmVariableType.Text, displayName: "Açıklama");

    private static readonly PdmVariableDefinition Material =
        new(68, "Material", PdmVariableType.Text, displayName: "Malzeme");

    private static readonly IReadOnlyList<PdmVariableDefinition> Variables = new[] { Description, Material };

    private static readonly ConfigurationKey Default = ConfigurationKey.Named("Default");
    private static readonly ConfigurationKey Kisa = ConfigurationKey.Named("Kısa");
    private static readonly ConfigurationKey Uzun = ConfigurationKey.Named("Uzun");

    private readonly string _directory;
    private readonly string _workbookPath;
    private readonly FakeVault _vault = new();
    private readonly FakeLog _log = new();

    private readonly PdmFileIdentity _root;
    private readonly PdmFileIdentity _pin;
    private readonly PdmFileIdentity _loose;

    public AssemblyExportTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pvs-asm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _workbookPath = Path.Combine(_directory, "montaj.xlsx");

        _root = _vault.AddFile(1, 10, "KOK.sldasm", ConfigurationKey.FileLevel, Default).Identity;
        _pin = _vault.AddFile(2, 11, "PIM.sldprt", ConfigurationKey.FileLevel, Kisa, Uzun).Identity;
        _loose = _vault.AddFile(3, 12, "SARTNAME.docx").Identity;

        _vault.SetValue(_root, ConfigurationKey.FileLevel, Description, VariableValue.FromText("Kök montaj"));
        _vault.SetValue(_pin, ConfigurationKey.FileLevel, Description, VariableValue.FromText("Pim"));
        _vault.SetValue(_pin, Kisa, Material, VariableValue.FromText("Ç1040"));
        _vault.SetValue(_pin, Uzun, Material, VariableValue.FromText("Ç1050"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void YalnizcaMontajinKullandigiKonfigurasyonlarSatirOlur()
    {
        var session = BuildSession();

        var rows = session.Rows.Select(r => (r.File.FileName, r.Configuration.ToDisplayString())).ToArray();

        // PIM'in "Uzun" konfigürasyonu montajda kullanılmıyor: satırı yok.
        // Montajdan gelmeyen dosya (SARTNAME) ise bugünkü gibi tüm konfigürasyonlarıyla.
        Assert.Equal(new[]
        {
            ("KOK.sldasm", "@"),
            ("KOK.sldasm", "Default"),
            ("PIM.sldprt", "@"),
            ("PIM.sldprt", "Kısa"),
            ("SARTNAME.docx", "@"),
        }, rows);

        var pinRow = session.Rows.Single(r => r.File.Equals(_pin) && r.Configuration.Equals(Kisa));
        Assert.Equal(1, pinRow.Placement!.Level);
        Assert.Equal(4, pinRow.Placement.TotalQuantity);
        Assert.Equal("KOK.sldasm", pinRow.Placement.ParentName);

        Assert.Null(session.Rows.Single(r => r.File.Equals(_loose)).Placement);
    }

    [Fact]
    public void BilgiSutunlari_KimliktenSonraDegiskenlerdenOnceYazilir()
    {
        var session = BuildSession();
        new WorkbookWriter().Write(session, _workbookPath);

        var first = WorkbookSchema.FirstVariableColumn;
        Assert.Equal(first + WorkbookSchema.AssemblyInfoHeaders.Count, session.FirstVariableColumn);

        Assert.Equal("Montaj: Üst montaj", Cell(1, first));
        Assert.Equal("Montaj: Seviye", Cell(1, first + 1));
        Assert.Equal("Montaj: Adet", Cell(1, first + 2));
        Assert.Equal("Açıklama", Cell(1, session.FirstVariableColumn));

        // Satır 5 = PIM / Kısa: üst montaj, seviye, adet.
        Assert.Equal("KOK.sldasm", Cell(5, first));
        Assert.Equal("1", Cell(5, first + 1));
        Assert.Equal("4", Cell(5, first + 2));
    }

    [Fact]
    public void BilgiSutunluKitap_SemayaUygun()
    {
        new WorkbookWriter().Write(BuildSession(), _workbookPath);

        using var document = SpreadsheetDocument.Open(_workbookPath, isEditable: false);
        var errors = new OpenXmlValidator().Validate(document).ToList();

        Assert.True(
            errors.Count == 0,
            "Sema hatalari:\n" + string.Join("\n", errors.Select(e => $"  {e.Path?.XPath}: {e.Description}")));
    }

    [Fact]
    public void KaymisSutundakiDuzenleme_DogruDegiskeneVeDogruSatiraGider()
    {
        var session = BuildSession();
        new WorkbookWriter().Write(session, _workbookPath);

        // PIM / Kısa satırında Malzeme'yi değiştir (değişkenler bilgi sütunları kadar sağda).
        var materialColumn = session.FirstVariableColumn + 1;
        SetCell(row: 5, column: materialColumn, "Ç4140");

        var workbook = new WorkbookReader().Read(_workbookPath);
        Assert.False(workbook.IsRejected);
        Assert.DoesNotContain(workbook.Issues, i => i.Code == IssueCode.ColumnRelocated);

        var changeSet = new ImportService(_vault, _vault, _log).BuildChangeSet(_workbookPath).Value;

        var safe = changeSet.Cells.Where(c => c.Status == ChangeStatus.SafeChange).ToList();
        var cell = Assert.Single(safe);
        Assert.Equal(_pin, cell.Coordinate.File);
        Assert.Equal(Kisa, cell.Coordinate.Configuration);
        Assert.Equal(Material.VariableId, cell.Variable.VariableId);
        Assert.Equal("Ç4140", cell.Requested.ToStorageString());
    }

    [Fact]
    public void DokunulmamisMontajKitabi_HicDegisiklikOnermez()
    {
        new WorkbookWriter().Write(BuildSession(), _workbookPath);

        var changeSet = new ImportService(_vault, _vault, _log).BuildChangeSet(_workbookPath).Value;

        Assert.Equal(0, changeSet.SafeChangeCount);
        Assert.Equal(0, changeSet.ErrorCount);
        Assert.Equal(changeSet.Cells.Count, changeSet.UnchangedCount);
    }

    [Fact]
    public void MontajsizKitap_BilgiSutunuIcermezVeDuzeniDegismez()
    {
        var session = new ExportService(_vault, _vault, _log)
            .BuildSession(new ExportRequest(new[] { _pin }, Variables)).Value;

        Assert.False(session.HasAssemblyInfo);
        Assert.Equal(WorkbookSchema.FirstVariableColumn, session.FirstVariableColumn);
    }

    [Fact]
    public void MontajinKullandigiKonfigurasyonDosyadaYoksa_SatirUretilmezVeGunlugeYazilir()
    {
        var silinmis = ConfigurationKey.Named("Silinmiş");
        var expanded = AssemblyExpansion.Expand(_root, Default, new[]
        {
            new AssemblyOccurrence(_pin, silinmis, 1, 1, "KOK.sldasm"),
        }, includeRoot: false);

        var request = new ExportRequest(expanded.Select(e => e.File).ToList(), Variables)
        {
            Scopes = expanded.ToDictionary(e => e.File, e => e.Scope),
        };

        var session = new ExportService(_vault, _vault, _log).BuildSession(request).Value;

        Assert.DoesNotContain(session.Rows, r => r.Configuration.Equals(silinmis));
        Assert.Contains(_log.Messages, m => m.StartsWith("WARN") && m.Contains("Silinmiş"));
    }

    // ------------------------------------------------------------------ yardımcılar

    private ExportSession BuildSession()
    {
        var expanded = AssemblyExpansion.Expand(_root, Default, new[]
        {
            new AssemblyOccurrence(_pin, Kisa, 1, 4, "KOK.sldasm"),
        }, includeRoot: true);

        var files = expanded.Select(e => e.File).ToList();
        files.Add(_loose);

        var request = new ExportRequest(files, Variables)
        {
            ScopeDescription = "Montaj: KOK.sldasm [Default]",
            Scopes = expanded.ToDictionary(e => e.File, e => e.Scope),
        };

        return new ExportService(_vault, _vault, _log).BuildSession(request).Value;
    }

    private string Cell(int row, int column)
    {
        using var document = SpreadsheetDocument.Open(_workbookPath, isEditable: false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.Descendants<Sheet>().First(s => s.Name?.Value == WorkbookSchema.VariablesSheet);
        var part = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);

        var reference = WorkbookWriter.ColumnName(column) + row;
        var cell = part.Worksheet.Descendants<Cell>().FirstOrDefault(c => c.CellReference?.Value == reference);

        return cell?.InlineString?.Text?.Text ?? cell?.CellValue?.Text ?? string.Empty;
    }

    private void SetCell(int row, int column, string text)
    {
        using var document = SpreadsheetDocument.Open(_workbookPath, isEditable: true);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.Descendants<Sheet>().First(s => s.Name?.Value == WorkbookSchema.VariablesSheet);
        var part = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        var sheetData = part.Worksheet.GetFirstChild<SheetData>()!;

        var targetRow = sheetData.Elements<Row>().First(r => r.RowIndex?.Value == (uint)row);
        var reference = WorkbookWriter.ColumnName(column) + row;

        var cell = targetRow.Elements<Cell>().FirstOrDefault(c => c.CellReference?.Value == reference);
        if (cell is null)
        {
            cell = new Cell { CellReference = reference };
            targetRow.AppendChild(cell);
        }

        cell.DataType = CellValues.InlineString;
        cell.CellValue = null;
        cell.InlineString = new InlineString(new Text(text));

        part.Worksheet.Save();
    }
}
