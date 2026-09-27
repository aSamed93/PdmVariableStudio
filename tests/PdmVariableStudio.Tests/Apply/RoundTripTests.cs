using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PdmVariableStudio.Core.Diff;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Journal;
using PdmVariableStudio.Core.Results;
using PdmVariableStudio.Core.Services;
using PdmVariableStudio.Core.Workbook;
using PdmVariableStudio.Tests.Fakes;
using Xunit;

namespace PdmVariableStudio.Tests.Apply;

/// <summary>
/// Uçtan uca: dışa aktar → Excel'i düzenle → içe aktar → karşılaştır → uygula → geri al.
/// </summary>
/// <remarks>
/// PDM istemcisi olmadan çalışır: vault yerine <see cref="FakeVault"/>, Excel yerine gerçek
/// bir <c>.xlsx</c> dosyası kullanılır. Boru hattının parçaları tek tek test edilmiş olsa da
/// asıl güven bu testten geliyor — katmanlar arası bir uyumsuzluğu ancak tam akış yakalar.
/// </remarks>
public class RoundTripTests : IDisposable
{
    private static readonly PdmVariableDefinition Material =
        new(23, "Material", PdmVariableType.Text, displayName: "Malzeme");

    private static readonly PdmVariableDefinition Weight =
        new(41, "Weight", PdmVariableType.Float, displayName: "Ağırlık");

    private static readonly IReadOnlyList<PdmVariableDefinition> Variables = new[] { Material, Weight };

    private readonly string _directory;
    private readonly string _workbookPath;
    private readonly string _journalRoot;

    private readonly FakeVault _vault = new();
    private readonly FakeLog _log = new();
    private readonly JsonlOperationJournal _journal;
    private readonly ExportService _export;
    private readonly ImportService _import;
    private readonly ApplyService _apply;
    private readonly UndoService _undo;

    private readonly PdmFileIdentity _mil;
    private readonly PdmFileIdentity _govde;

    public RoundTripTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pvs-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _workbookPath = Path.Combine(_directory, "test.xlsx");
        _journalRoot = Path.Combine(_directory, "journal");

        _mil = _vault.AddFile(1, 10, "MIL-001.sldprt", ConfigurationKey.FileLevel, ConfigurationKey.Named("Uzun")).Identity;
        _govde = _vault.AddFile(2, 10, "GOVDE.sldasm").Identity;

        _vault.SetValue(_mil, ConfigurationKey.FileLevel, Material, VariableValue.FromText("Ç1040"));
        _vault.SetValue(_mil, ConfigurationKey.FileLevel, Weight, VariableValue.FromFloat(12.4m));
        _vault.SetValue(_mil, ConfigurationKey.Named("Uzun"), Material, VariableValue.FromText("Ç1040"));
        _vault.SetValue(_mil, ConfigurationKey.Named("Uzun"), Weight, VariableValue.FromFloat(15.1m));
        _vault.SetValue(_govde, ConfigurationKey.FileLevel, Material, VariableValue.FromText("Kaynak"));

        _journal = new JsonlOperationJournal(_journalRoot);

        _export = new ExportService(_vault, _vault, _log);
        _import = new ImportService(_vault, _vault, _log);
        _apply = new ApplyService(_vault, _vault, _vault, _vault, _journal, _log);
        _undo = new UndoService(_vault, _vault, _journal, _apply, _log);
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
    public void TamDongu_DisaAktarDuzenleIceAktarUygulaGeriAl()
    {
        // --- 1. dışa aktar ---
        ExportToWorkbook();

        // Konfigürasyonlu dosya iki satır, konfigürasyonsuz bir satır üretir.
        var afterExport = new WorkbookReader().Read(_workbookPath);
        Assert.Equal(3, afterExport.Rows.Count);

        // --- 2. kullanıcı Excel'de düzenler ---
        SetCell(row: 2, column: WorkbookSchema.FirstVariableColumn, "Ç4140");

        // --- 3. içe aktar ve karşılaştır ---
        var changeSet = _import.BuildChangeSet(_workbookPath).Value;

        Assert.False(changeSet.IsRejected);
        Assert.Equal(1, changeSet.SafeChangeCount);
        Assert.Equal(0, changeSet.ConflictCount);
        Assert.Equal(0, changeSet.ErrorCount);

        // --- 4. uygula ---
        var applyResult = _apply.Apply(changeSet, new ApplyOptions { CheckoutConsent = true });

        Assert.Equal(1, applyResult.Value.AppliedCount);
        Assert.Equal("Ç4140", _vault.GetValue(_mil, ConfigurationKey.FileLevel, Material).ToStorageString());

        // Diğer konfigürasyon ve diğer dosya etkilenmedi.
        Assert.Equal("Ç1040", _vault.GetValue(_mil, ConfigurationKey.Named("Uzun"), Material).ToStorageString());
        Assert.Equal("Kaynak", _vault.GetValue(_govde, ConfigurationKey.FileLevel, Material).ToStorageString());

        // --- 5. geri al ---
        var operationId = applyResult.Value.Operation.OperationId;
        var preview = _undo.BuildPreview(operationId).Value;

        Assert.Equal(1, preview.SafeCount);

        _undo.Undo(preview, new ApplyOptions { CheckoutConsent = true });

        Assert.Equal("Ç1040", _vault.GetValue(_mil, ConfigurationKey.FileLevel, Material).ToStorageString());
    }

    [Fact]
    public void KullaniciDokunmadiysa_HicbirDegisiklikOnerilmez()
    {
        ExportToWorkbook();

        var changeSet = _import.BuildChangeSet(_workbookPath).Value;

        Assert.Equal(0, changeSet.SafeChangeCount);
        Assert.Equal(0, changeSet.ConflictCount);
        Assert.Equal(0, changeSet.ErrorCount);
        Assert.Equal(changeSet.Cells.Count, changeSet.UnchangedCount);
    }

    [Fact]
    public void DisaAktarimdanSonraPdmDegismisse_Cakisma()
    {
        ExportToWorkbook();

        // Kullanıcı Excel'de düzenledi...
        SetCell(row: 2, column: WorkbookSchema.FirstVariableColumn, "Ç4140");

        // ...ama bu arada başkası PDM'de başka bir değer yazdı.
        _vault.SimulateExternalChange(_mil, ConfigurationKey.FileLevel, Material, VariableValue.FromText("Ç8620"));

        var changeSet = _import.BuildChangeSet(_workbookPath).Value;

        Assert.Equal(0, changeSet.SafeChangeCount);
        Assert.Equal(1, changeSet.ConflictCount);
        Assert.Equal(0, changeSet.SelectedCount);

        // Uygulama zorlanırsa bile başkasının değeri korunur.
        var result = _apply.Apply(changeSet, new ApplyOptions { CheckoutConsent = true });

        Assert.True(result.IsFailure);
        Assert.Equal("Ç8620", _vault.GetValue(_mil, ConfigurationKey.FileLevel, Material).ToStorageString());
    }

    [Fact]
    public void BaskasininCektigiDosya_OnizlemedeKilitliGorunurVeUygulanmaz()
    {
        ExportToWorkbook();
        SetCell(row: 2, column: WorkbookSchema.FirstVariableColumn, "Ç4140");

        // Dışa aktarımdan sonra Ayşe dosyayı çekti.
        _vault.AddFile(1, 10, "MIL-001.sldprt", ConfigurationKey.FileLevel, ConfigurationKey.Named("Uzun"))
            .LockedBy("ayse");
        _vault.SetValue(_mil, ConfigurationKey.FileLevel, Material, VariableValue.FromText("Ç1040"));
        _vault.SetValue(_mil, ConfigurationKey.FileLevel, Weight, VariableValue.FromFloat(12.4m));

        var changeSet = _import.BuildChangeSet(_workbookPath).Value;

        var cell = changeSet.Cells.Single(c =>
            c.Coordinate.File.FileId == 1
            && c.Coordinate.Configuration.IsFileLevel
            && c.Variable.VariableId == Material.VariableId);

        // Durum hâlâ "güvenli değişiklik": kullanıcı değişiklik olduğunu görmeli.
        // Ama uygulanamaz ve nedeni açıkça yazıyor.
        Assert.Equal(ChangeStatus.SafeChange, cell.Status);
        Assert.False(cell.CanApply);
        Assert.Equal(IssueCode.LockedByOtherUser, cell.Reason);
        Assert.Equal(1, changeSet.BlockedFileCount);
    }

    /// <summary>
    /// Gerçek vault'ta yaşandı (2026-09-27): klasördeki dosyalar silinip aynı adla yeniden
    /// eklendi, yeni kimlik aldılar; ardından ESKİ çalışma kitabı içe aktarıldı. Ölü kimliğin
    /// okunamaması "güncel = orijinal" sayılıyor, her hücre güvenli değişiklik görünüyordu ve
    /// uygulama aynı yerel yoldaki yeni dosyanın kopyasına yazıyordu.
    /// </summary>
    [Fact]
    public void DosyaSilinipYenidenEklendiyse_EskiKitapYeniDosyayaYazmaz()
    {
        ExportToWorkbook();
        SetCell(row: 2, column: WorkbookSchema.FirstVariableColumn, "Ç4140");

        // Dosya silindi ve aynı adla, aynı klasöre yeniden eklendi: yeni kimlik #99.
        _vault.RemoveFile(_mil);
        var yeniMil = _vault.AddFile(99, 10, "MIL-001.sldprt", ConfigurationKey.FileLevel).Identity;
        _vault.SetValue(yeniMil, ConfigurationKey.FileLevel, Material, VariableValue.FromText("Ç1040"));

        var changeSet = _import.BuildChangeSet(_workbookPath).Value;

        var cell = changeSet.Cells.Single(c =>
            c.Coordinate.File.FileId == 1
            && c.Coordinate.Configuration.IsFileLevel
            && c.Variable.VariableId == Material.VariableId);

        Assert.False(cell.CanApply);
        Assert.Equal(IssueCode.FileNotFound, cell.Reason);
        Assert.Equal(1, changeSet.BlockedFileCount);
        Assert.Contains(changeSet.Issues, i => i.Code == IssueCode.FileNotFound);

        var result = _apply.Apply(changeSet, new ApplyOptions { CheckoutConsent = true });

        Assert.True(result.IsFailure);
        Assert.Empty(_vault.CheckedOutFileIds);
        Assert.Equal(0, _vault.WriteCallCount);
        Assert.Equal("Ç1040", _vault.GetValue(yeniMil, ConfigurationKey.FileLevel, Material).ToStorageString());
    }

    [Fact]
    public void SayisalAlanaGecersizDegerYazilirsa_UygulanmazDigerleriEtkilenmez()
    {
        ExportToWorkbook();

        SetCell(row: 2, column: WorkbookSchema.FirstVariableColumn, "Ç4140");
        SetCell(row: 2, column: WorkbookSchema.FirstVariableColumn + 1, "abc");

        var changeSet = _import.BuildChangeSet(_workbookPath).Value;

        Assert.Equal(1, changeSet.SafeChangeCount);
        Assert.Equal(1, changeSet.ErrorCount);

        _apply.Apply(changeSet, new ApplyOptions { CheckoutConsent = true });

        Assert.Equal("Ç4140", _vault.GetValue(_mil, ConfigurationKey.FileLevel, Material).ToStorageString());
        Assert.Equal("12.4", _vault.GetValue(_mil, ConfigurationKey.FileLevel, Weight).ToStorageString());
    }

    [Fact]
    public void BaskaVaultunKitabi_TamamenReddedilir()
    {
        ExportToWorkbook();
        SetCell(row: 2, column: WorkbookSchema.FirstVariableColumn, "Ç4140");

        var otherVault = new FakeVault("BaskaVault");
        otherVault.AddFile(1, 10, "MIL-001.sldprt");

        var otherImport = new ImportService(otherVault, otherVault, _log);
        var changeSet = otherImport.BuildChangeSet(_workbookPath).Value;

        Assert.True(changeSet.IsRejected);
        Assert.Contains(changeSet.Issues, i => i.Code == IssueCode.WrongVault);
        Assert.Empty(changeSet.Cells);
    }

    // ------------------------------------------------------------ yardımcılar

    private void ExportToWorkbook()
    {
        var request = new ExportRequest(new[] { _mil, _govde }, Variables)
        {
            ScopeDescription = "test",
        };

        var session = _export.BuildSession(request).Value;

        new WorkbookWriter().Write(session, _workbookPath);
    }

    /// <summary>Kullanıcının Excel'de bir hücreyi düzenlemesini taklit eder.</summary>
    private void SetCell(int row, int column, string text)
    {
        using var document = SpreadsheetDocument.Open(_workbookPath, isEditable: true);
        var workbookPart = document.WorkbookPart!;

        var sheet = workbookPart.Workbook.Descendants<Sheet>()
            .First(s => s.Name?.Value == WorkbookSchema.VariablesSheet);

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
        workbookPart.Workbook.Save();
    }

}
