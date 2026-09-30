using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Workbook;

namespace PdmVariableStudio.Tests.Workbook;

/// <summary>
/// Testler için örnek bir dışa aktarım üretir ve üretilen dosyayı, kullanıcının yapabileceği
/// her türlü bozmayı taklit ederek değiştirmeye izin verir.
/// </summary>
internal sealed class WorkbookFixture : IDisposable
{
    private readonly string _directory;

    public WorkbookFixture()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pvs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Path_ = Path.Combine(_directory, "test.xlsx");
    }

    public string Path_ { get; }

    public static Guid SessionId { get; } = new("11112222-3333-4444-5555-666677778888");

    public static IReadOnlyList<PdmVariableDefinition> Variables { get; } = new[]
    {
        new PdmVariableDefinition(17, "Description", PdmVariableType.Text, displayName: "Açıklama"),
        new PdmVariableDefinition(23, "Material", PdmVariableType.Text, displayName: "Malzeme"),
        new PdmVariableDefinition(41, "Weight", PdmVariableType.Float, displayName: "Ağırlık"),
        new PdmVariableDefinition(55, "ReleaseDate", PdmVariableType.Date, displayName: "Yayım Tarihi"),
        new PdmVariableDefinition(61, "Approved", PdmVariableType.Bool, displayName: "Onaylı"),
        new PdmVariableDefinition(70, "Revision", PdmVariableType.Text, displayName: "Revizyon", isReadOnly: true),
    };

    public static ExportSession BuildSession()
    {
        var mil = new PdmFileIdentity(8814, 142, "MIL-001.sldprt", "\\Parts\\Mil");
        var govde = new PdmFileIdentity(8815, 143, "GOVDE.sldasm", "\\Assemblies");
        var docx = new PdmFileIdentity(8816, 144, "sartname.docx", "\\Docs");

        var rows = new List<ExportRow>
        {
            new(1, mil, ConfigurationKey.FileLevel, 3, new[]
            {
                VariableValue.FromText("Ana mil"),
                VariableValue.FromText("Ç1040"),
                VariableValue.FromFloat(12.4m),
                VariableValue.FromDate(new DateTime(2026, 3, 14)),
                VariableValue.FromBool(true),
                VariableValue.FromText("A"),
            }),
            new(2, mil, ConfigurationKey.Named("Uzun"), 3, new[]
            {
                VariableValue.FromText("Ana mil (uzun)"),
                VariableValue.FromText("Ç1040"),
                VariableValue.FromFloat(15.1m),
                VariableValue.Empty,
                VariableValue.FromBool(false),
                VariableValue.FromText("A"),
            }),
            new(3, govde, ConfigurationKey.FileLevel, 7, new[]
            {
                VariableValue.FromText("Gövde"),
                VariableValue.Empty,
                VariableValue.Empty,
                VariableValue.Empty,
                VariableValue.Empty,
                VariableValue.FromText("B"),
            }),
            new(4, docx, ConfigurationKey.FileLevel, 1, new[]
            {
                VariableValue.FromText("Şartname"),
                VariableValue.Empty,
                VariableValue.Empty,
                VariableValue.Empty,
                VariableValue.Empty,
                VariableValue.Empty,
            }),
        };

        return new ExportSession(
            SessionId,
            new DateTime(2026, 8, 25, 9, 14, 33, DateTimeKind.Utc),
            new VaultIdentity("MakinaVault", "C:\\MakinaVault", "MakinaVaultDb"),
            sourceFolderId: 142,
            sourceFolderPath: "\\Parts\\Mil",
            includeSubfolders: false,
            fileFilter: "*.*",
            windowsUser: "MAKINA\\ayse",
            pdmUser: "ayse",
            Variables,
            rows);
    }

    public WorkbookFixture Write()
    {
        new WorkbookWriter().Write(BuildSession(), Path_);
        return this;
    }

    public ImportedWorkbook Read() => new WorkbookReader().Read(Path_);

    /// <summary>Değişkenler sayfasının başlık satırı, soldan sağa.</summary>
    public IReadOnlyList<string> Headers()
    {
        using var document = SpreadsheetDocument.Open(Path_, isEditable: false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.Descendants<Sheet>().First(s => s.Name?.Value == WorkbookSchema.VariablesSheet);
        var part = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        var header = part.Worksheet.Descendants<Row>().First(r => r.RowIndex?.Value == 1);

        return header.Elements<Cell>()
            .OrderBy(c => WorkbookReader.ColumnIndex(c.CellReference?.Value))
            .Select(c => c.InlineString?.Text?.Text ?? c.CellValue?.Text ?? string.Empty)
            .ToList();
    }

    // ------------------------------------------------------- bozma yardımcıları

    /// <summary>Bir hücrenin metin değerini değiştirir; yoksa oluşturur.</summary>
    public void SetText(string sheetName, int row, int column, string text)
    {
        Mutate(sheetName, sheetData =>
        {
            var cell = EnsureCell(sheetData, row, column);
            cell.DataType = CellValues.InlineString;
            cell.CellValue = null;
            cell.InlineString = new InlineString(new Text(text));
        });
    }

    /// <summary>Bir satırın tamamını siler (kullanıcı satır silmiş).</summary>
    public void DeleteRow(string sheetName, int row)
    {
        Mutate(sheetName, sheetData =>
        {
            var target = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == (uint)row);
            target?.Remove();
        });
    }

    /// <summary>Bir satırı kopyalayıp sona ekler (kullanıcı satır yapıştırmış).</summary>
    public void DuplicateRow(string sheetName, int sourceRow)
    {
        Mutate(sheetName, sheetData =>
        {
            var source = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == (uint)sourceRow);
            if (source is null)
            {
                return;
            }

            var maxRow = sheetData.Elements<Row>().Max(r => r.RowIndex?.Value ?? 0);
            var clone = (Row)source.CloneNode(true);
            clone.RowIndex = maxRow + 1;

            foreach (var cell in clone.Elements<Cell>())
            {
                var column = WorkbookReader.ColumnIndex(cell.CellReference?.Value);
                cell.CellReference = WorkbookWriter.ColumnName(column) + (maxRow + 1);
            }

            sheetData.AppendChild(clone);
        });
    }

    /// <summary>Satırların sırasını tersine çevirir (kullanıcı sıralamış).</summary>
    public void ReverseDataRows(string sheetName)
    {
        Mutate(sheetName, sheetData =>
        {
            var rows = sheetData.Elements<Row>().Where(r => r.RowIndex?.Value > 1).ToList();
            var indexes = rows.Select(r => r.RowIndex!.Value).ToList();

            foreach (var row in rows)
            {
                row.Remove();
            }

            rows.Reverse();

            for (var i = 0; i < rows.Count; i++)
            {
                var newIndex = indexes[i];
                rows[i].RowIndex = newIndex;

                foreach (var cell in rows[i].Elements<Cell>())
                {
                    var column = WorkbookReader.ColumnIndex(cell.CellReference?.Value);
                    cell.CellReference = WorkbookWriter.ColumnName(column) + newIndex;
                }

                sheetData.AppendChild(rows[i]);
            }
        });
    }

    /// <summary>Bir sütunun tamamını başka bir sütuna taşır (kullanıcı sütunu sürüklemiş).</summary>
    public void MoveColumn(string sheetName, int fromColumn, int toColumn)
    {
        Mutate(sheetName, sheetData =>
        {
            foreach (var row in sheetData.Elements<Row>().ToList())
            {
                var cell = row.Elements<Cell>()
                    .FirstOrDefault(c => WorkbookReader.ColumnIndex(c.CellReference?.Value) == fromColumn);

                if (cell is null)
                {
                    continue;
                }

                cell.CellReference = WorkbookWriter.ColumnName(toColumn) + row.RowIndex!.Value;
            }
        });
    }

    /// <summary>Bir sütunun tüm hücrelerini siler (kullanıcı sütunu silmiş).</summary>
    public void DeleteColumn(string sheetName, int column)
    {
        Mutate(sheetName, sheetData =>
        {
            foreach (var row in sheetData.Elements<Row>().ToList())
            {
                var cell = row.Elements<Cell>()
                    .FirstOrDefault(c => WorkbookReader.ColumnIndex(c.CellReference?.Value) == column);

                cell?.Remove();
            }
        });
    }

    /// <summary>Bir sayfanın tamamını kaldırır.</summary>
    public void DeleteSheet(string sheetName)
    {
        using var document = SpreadsheetDocument.Open(Path_, isEditable: true);
        var workbookPart = document.WorkbookPart!;

        var sheet = workbookPart.Workbook.Descendants<Sheet>()
            .FirstOrDefault(s => s.Name?.Value == sheetName);

        if (sheet?.Id?.Value is null)
        {
            return;
        }

        var part = workbookPart.GetPartById(sheet.Id!.Value!);
        workbookPart.DeletePart(part);
        sheet.Remove();
        workbookPart.Workbook.Save();
    }

    private void Mutate(string sheetName, Action<SheetData> mutation)
    {
        using var document = SpreadsheetDocument.Open(Path_, isEditable: true);
        var workbookPart = document.WorkbookPart!;

        var sheet = workbookPart.Workbook.Descendants<Sheet>()
            .FirstOrDefault(s => s.Name?.Value == sheetName);

        if (sheet?.Id?.Value is null)
        {
            throw new InvalidOperationException($"'{sheetName}' sayfası yok.");
        }

        var part = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        var sheetData = part.Worksheet.GetFirstChild<SheetData>()!;

        mutation(sheetData);

        part.Worksheet.Save();
        workbookPart.Workbook.Save();
    }

    private static Cell EnsureCell(SheetData sheetData, int rowIndex, int column)
    {
        var row = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == (uint)rowIndex);
        if (row is null)
        {
            row = new Row { RowIndex = (uint)rowIndex };
            sheetData.AppendChild(row);
        }

        var reference = WorkbookWriter.ColumnName(column) + rowIndex.ToString(CultureInfo.InvariantCulture);
        var cell = row.Elements<Cell>().FirstOrDefault(c => c.CellReference?.Value == reference);

        if (cell is null)
        {
            cell = new Cell { CellReference = reference };
            row.AppendChild(cell);
        }

        return cell;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Geçici klasör silinemedi; testin sonucunu etkilemez.
        }
    }
}
