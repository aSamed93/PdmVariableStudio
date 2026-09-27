using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PdmVariableStudio.Core.Domain;

namespace PdmVariableStudio.Core.Workbook;

/// <summary>
/// <see cref="ExportSession"/> içeriğini şema sözleşmesine uygun bir <c>.xlsx</c> dosyasına yazar.
/// </summary>
/// <remarks>
/// Yazma önce geçici bir dosyaya yapılır, sonra hedefin üzerine taşınır. İptal ya da hata
/// durumunda kullanıcının elinde yarım bir çalışma kitabı kalmaz — yarım bir kitap, içe
/// aktarımda "bozuk" olarak reddedilse bile kullanıcıya saatlerce iş kaybettirir.
/// </remarks>
public sealed class WorkbookWriter
{
    /// <summary>Excel'in tarih seri numarası sıfır noktası (1900 sistemi, bilinen artık yıl hatasıyla).</summary>
    private static readonly DateTime ExcelEpoch = new(1899, 12, 30);

    public void Write(ExportSession session, string path, CancellationToken cancellationToken = default)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Hedef yol boş olamaz.", nameof(path));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        var temporaryPath = path + ".tmp";

        try
        {
            WriteCore(session, temporaryPath, cancellationToken);

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporaryPath, path);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private void WriteCore(ExportSession session, string path, CancellationToken cancellationToken)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);

        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();

        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = WorkbookStyles.Create();
        stylesPart.Stylesheet.Save();

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());

        AddSheet(workbookPart, sheets, WorkbookSchema.VariablesSheet, 1, SheetStateValues.Visible,
            part => BuildVariablesSheet(part, session, cancellationToken));

        // Teknik sayfalar veryHidden: Excel arayüzünden "Sayfayı Göster" ile geri getirilemezler,
        // yalnızca VBA ya da bir dosya düzenleyici ile açılabilirler. Kullanıcının kazara
        // bozmasını engellemek için yeterli; kasıtlı değişiklik zaten damgayla yakalanıyor.
        AddSheet(workbookPart, sheets, WorkbookSchema.MetadataSheet, 2, SheetStateValues.VeryHidden,
            part => BuildMetadataSheet(part, session));

        AddSheet(workbookPart, sheets, WorkbookSchema.RowsSheet, 3, SheetStateValues.VeryHidden,
            part => BuildRowsSheet(part, session, cancellationToken));

        workbookPart.Workbook.Save();
    }

    private static void AddSheet(
        WorkbookPart workbookPart,
        Sheets sheets,
        string name,
        uint sheetId,
        SheetStateValues state,
        Action<WorksheetPart> build)
    {
        var part = workbookPart.AddNewPart<WorksheetPart>();
        build(part);
        part.Worksheet.Save();

        sheets.AppendChild(new Sheet
        {
            Id = workbookPart.GetIdOfPart(part),
            SheetId = sheetId,
            Name = name,
            State = state,
        });
    }

    // ----------------------------------------------------------------- Variables

    private void BuildVariablesSheet(WorksheetPart part, ExportSession session, CancellationToken cancellationToken)
    {
        var data = new SheetData();

        data.AppendChild(BuildHeaderRow(session));

        uint rowIndex = 2;
        foreach (var exportRow in session.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            data.AppendChild(BuildVariablesRow(session, exportRow, rowIndex));
            rowIndex++;
        }

        var lastColumn = WorkbookSchema.FirstVariableColumn + session.Variables.Count - 1;
        var lastRow = Math.Max(1, session.Rows.Count + 1);

        var worksheet = new Worksheet();

        // SpreadsheetML'de <worksheet> alt elemanlarının SIRASI şemada sabittir:
        //   sheetViews -> cols -> sheetData -> sheetProtection -> autoFilter
        // Sıra bozulursa Excel dosyayı "onarılamaz içerik" diyerek açmayı reddeder ve
        // kullanıcı için bu, eklentinin hiç çalışmaması demektir. OpenXmlValidityTests
        // bu sırayı nöbetçi olarak koruyor.
        worksheet.AppendChild(new SheetViews(new SheetView
        {
            WorkbookViewId = 0,

            // Başlık satırı donuk kalır; 3.000 satırlık bir kitapta sütunun hangi değişken
            // olduğunu kaybetmek en sık şikayet edilen şey.
            Pane = new Pane
            {
                VerticalSplit = 1D,
                TopLeftCell = "A2",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen,
            },
        }));

        worksheet.AppendChild(BuildColumnWidths(session));
        worksheet.AppendChild(data);

        // Sayfa koruması PAROLASIZ: amaç güvenlik değil, kaza önlemek. Kullanıcı gerçekten
        // ihtiyaç duyarsa korumayı tek tıkla kaldırabilir; içe aktarma buna hazırlıklıdır ve
        // kimlik doğrulamasını korumaya değil, damgalara dayandırır.
        worksheet.AppendChild(new SheetProtection
        {
            Sheet = true,
            Objects = true,
            Scenarios = true,
            FormatCells = false,
            FormatColumns = false,
            FormatRows = false,
            Sort = false,
            AutoFilter = false,
            SelectLockedCells = false,
            SelectUnlockedCells = false,
        });

        worksheet.AppendChild(new AutoFilter
        {
            Reference = $"A1:{ColumnName(lastColumn)}{lastRow.ToString(CultureInfo.InvariantCulture)}",
        });

        part.Worksheet = worksheet;
    }

    private static Columns BuildColumnWidths(ExportSession session)
    {
        var columns = new Columns();

        columns.AppendChild(new Column { Min = 1, Max = 1, Width = 6D, CustomWidth = true });
        columns.AppendChild(new Column { Min = 2, Max = 2, Width = 32D, CustomWidth = true });
        columns.AppendChild(new Column { Min = 3, Max = 3, Width = 28D, CustomWidth = true });
        columns.AppendChild(new Column { Min = 4, Max = 4, Width = 18D, CustomWidth = true });

        if (session.Variables.Count > 0)
        {
            columns.AppendChild(new Column
            {
                Min = (uint)WorkbookSchema.FirstVariableColumn,
                Max = (uint)(WorkbookSchema.FirstVariableColumn + session.Variables.Count - 1),
                Width = 20D,
                CustomWidth = true,
            });
        }

        return columns;
    }

    private static Row BuildHeaderRow(ExportSession session)
    {
        var row = new Row { RowIndex = 1U };

        row.AppendChild(TextCell(1, 1, WorkbookSchema.HeaderExportRowId, WorkbookStyles.Header));
        row.AppendChild(TextCell(2, 1, WorkbookSchema.HeaderFileName, WorkbookStyles.Header));
        row.AppendChild(TextCell(3, 1, WorkbookSchema.HeaderRelativePath, WorkbookStyles.Header));
        row.AppendChild(TextCell(4, 1, WorkbookSchema.HeaderConfiguration, WorkbookStyles.Header));

        for (var i = 0; i < session.Variables.Count; i++)
        {
            var variable = session.Variables[i];
            var caption = variable.IsReadOnly
                ? variable.DisplayName + " (salt okunur)"
                : variable.DisplayName;

            row.AppendChild(TextCell(WorkbookSchema.FirstVariableColumn + i, 1, caption, WorkbookStyles.Header));
        }

        return row;
    }

    private Row BuildVariablesRow(ExportSession session, ExportRow exportRow, uint rowIndex)
    {
        var row = new Row { RowIndex = rowIndex };

        row.AppendChild(NumberCell(1, rowIndex, exportRow.ExportRowId, WorkbookStyles.Identity));
        row.AppendChild(TextCell(2, rowIndex, exportRow.File.FileName, WorkbookStyles.Identity));
        row.AppendChild(TextCell(3, rowIndex, exportRow.File.RelativePath, WorkbookStyles.Identity));
        // Dosya düzeyi hücresi BOŞ bırakılmıyor, "@" yazılıyor: boş hücre kullanıcıya
        // "burası doldurulmamış" izlenimi veriyordu, oysa "@" gerçek ve ayrı bir veri kartı
        // alanıdır (SOLIDWORKS'teki Custom sekmesi). Bu sütun kimlik sütunu olduğu için
        // eşleştirmede kullanılmıyor; gösterimi değiştirmek güvenli.
        row.AppendChild(TextCell(
            4,
            rowIndex,
            exportRow.Configuration.ToDisplayString(),
            WorkbookStyles.Identity));

        for (var i = 0; i < session.Variables.Count; i++)
        {
            var variable = session.Variables[i];
            var value = i < exportRow.Values.Count ? exportRow.Values[i] : VariableValue.Empty;
            var style = WorkbookStyles.ForVariable(variable.DataType, variable.IsReadOnly);

            row.AppendChild(ValueCell(WorkbookSchema.FirstVariableColumn + i, rowIndex, value, style));
        }

        return row;
    }

    // ----------------------------------------------------------------- _Metadata

    private static void BuildMetadataSheet(WorksheetPart part, ExportSession session)
    {
        var metadata = BuildMetadataPairs(session);
        var columnIndexes = new List<int>(session.Variables.Count);
        for (var i = 0; i < session.Variables.Count; i++)
        {
            columnIndexes.Add(WorkbookSchema.FirstVariableColumn + i);
        }

        var checksum = WorkbookSchema.ComputeMetadataChecksum(metadata, session.Variables, columnIndexes);
        metadata.Add(new KeyValuePair<string, string>(WorkbookSchema.KeyMetadataChecksum, checksum));

        var data = new SheetData();
        uint rowIndex = 1;

        foreach (var pair in metadata)
        {
            var row = new Row { RowIndex = rowIndex };
            row.AppendChild(TextCell(1, rowIndex, pair.Key, WorkbookStyles.Default));
            row.AppendChild(TextCell(2, rowIndex, pair.Value, WorkbookStyles.Default));
            data.AppendChild(row);
            rowIndex++;
        }

        var markerRow = new Row { RowIndex = rowIndex };
        markerRow.AppendChild(TextCell(1, rowIndex, WorkbookSchema.VariableTableMarker, WorkbookStyles.Default));
        data.AppendChild(markerRow);
        rowIndex++;

        var headerRow = new Row { RowIndex = rowIndex };
        var headers = new[]
        {
            "VariableId", "VariableName", "DisplayName", "DataType",
            "ColumnIndex", "IsMandatory", "IsUnique", "IsReadOnly",
        };
        for (var i = 0; i < headers.Length; i++)
        {
            headerRow.AppendChild(TextCell(i + 1, rowIndex, headers[i], WorkbookStyles.Default));
        }

        data.AppendChild(headerRow);
        rowIndex++;

        for (var i = 0; i < session.Variables.Count; i++)
        {
            var variable = session.Variables[i];
            var row = new Row { RowIndex = rowIndex };

            row.AppendChild(NumberCell(1, rowIndex, variable.VariableId, WorkbookStyles.Default));
            row.AppendChild(TextCell(2, rowIndex, variable.Name, WorkbookStyles.Default));
            row.AppendChild(TextCell(3, rowIndex, variable.DisplayName, WorkbookStyles.Default));
            row.AppendChild(NumberCell(4, rowIndex, (int)variable.DataType, WorkbookStyles.Default));
            row.AppendChild(NumberCell(5, rowIndex, columnIndexes[i], WorkbookStyles.Default));
            row.AppendChild(NumberCell(6, rowIndex, variable.IsMandatory ? 1 : 0, WorkbookStyles.Default));
            row.AppendChild(NumberCell(7, rowIndex, variable.IsUnique ? 1 : 0, WorkbookStyles.Default));
            row.AppendChild(NumberCell(8, rowIndex, variable.IsReadOnly ? 1 : 0, WorkbookStyles.Default));

            data.AppendChild(row);
            rowIndex++;
        }

        part.Worksheet = new Worksheet(data);
    }

    private static List<KeyValuePair<string, string>> BuildMetadataPairs(ExportSession session)
    {
        var invariant = CultureInfo.InvariantCulture;

        return new List<KeyValuePair<string, string>>
        {
            new(WorkbookSchema.KeySchemaVersion, WorkbookSchema.CurrentVersion.ToString(invariant)),
            new(WorkbookSchema.KeyProductVersion, ProductInfo.Version),
            new(WorkbookSchema.KeyExportSessionId, session.ExportSessionId.ToString("D")),
            new(WorkbookSchema.KeyExportedAtUtc, session.ExportedAtUtc.ToString("o", invariant)),
            new(WorkbookSchema.KeyExportedByWindowsUser, session.WindowsUser),
            new(WorkbookSchema.KeyExportedByPdmUser, session.PdmUser),
            new(WorkbookSchema.KeyVaultName, session.Vault.Name),
            new(WorkbookSchema.KeyVaultRootPath, session.Vault.RootFolderPath),
            new(WorkbookSchema.KeyVaultDatabase, session.Vault.DatabaseName),
            new(WorkbookSchema.KeySourceFolderId, session.SourceFolderId.ToString(invariant)),
            new(WorkbookSchema.KeySourceFolderPath, session.SourceFolderPath),
            new(WorkbookSchema.KeyIncludeSubfolders, session.IncludeSubfolders ? "true" : "false"),
            new(WorkbookSchema.KeyFileFilter, session.FileFilter),
            new(WorkbookSchema.KeyRowCount, session.Rows.Count.ToString(invariant)),
        };
    }

    // --------------------------------------------------------------------- _Rows

    private static void BuildRowsSheet(WorksheetPart part, ExportSession session, CancellationToken cancellationToken)
    {
        var data = new SheetData();
        uint rowIndex = 1;

        var header = new Row { RowIndex = rowIndex };
        var headers = new[]
        {
            "ExportRowId", "FileId", "FolderId", "Configuration",
            "FileVersion", "IsSolidWorks", "RowFingerprint",
        };
        for (var i = 0; i < headers.Length; i++)
        {
            header.AppendChild(TextCell(i + 1, rowIndex, headers[i], WorkbookStyles.Default));
        }

        for (var i = 0; i < session.Variables.Count; i++)
        {
            header.AppendChild(TextCell(
                WorkbookSchema.RowsFirstValueColumn + i,
                rowIndex,
                "v" + session.Variables[i].VariableId.ToString(CultureInfo.InvariantCulture),
                WorkbookStyles.Default));
        }

        data.AppendChild(header);
        rowIndex++;

        foreach (var exportRow in session.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fingerprint = WorkbookSchema.ComputeRowFingerprint(
                session.ExportSessionId,
                exportRow.ExportRowId,
                exportRow.File.FileId,
                exportRow.File.FolderId,
                exportRow.Configuration,
                exportRow.Values);

            var row = new Row { RowIndex = rowIndex };

            row.AppendChild(NumberCell(WorkbookSchema.RowsColExportRowId, rowIndex, exportRow.ExportRowId, WorkbookStyles.Default));
            row.AppendChild(NumberCell(WorkbookSchema.RowsColFileId, rowIndex, exportRow.File.FileId, WorkbookStyles.Default));
            row.AppendChild(NumberCell(WorkbookSchema.RowsColFolderId, rowIndex, exportRow.File.FolderId, WorkbookStyles.Default));
            row.AppendChild(TextCell(WorkbookSchema.RowsColConfiguration, rowIndex, exportRow.Configuration.ToStorageString(), WorkbookStyles.Default));
            row.AppendChild(NumberCell(WorkbookSchema.RowsColFileVersion, rowIndex, exportRow.FileVersion, WorkbookStyles.Default));
            row.AppendChild(NumberCell(WorkbookSchema.RowsColIsSolidWorks, rowIndex, exportRow.File.IsSolidWorksFile ? 1 : 0, WorkbookStyles.Default));
            row.AppendChild(TextCell(WorkbookSchema.RowsColFingerprint, rowIndex, fingerprint, WorkbookStyles.Default));

            // Orijinal değerler HER ZAMAN invariant metin olarak yazılır. Bunlar
            // karşılaştırmanın referans noktası; Excel'in tipli hücre yorumuna ya da
            // kullanıcının bölge ayarına bağlı olmamaları şart.
            for (var i = 0; i < session.Variables.Count; i++)
            {
                var value = i < exportRow.Values.Count ? exportRow.Values[i] : VariableValue.Empty;
                row.AppendChild(TextCell(
                    WorkbookSchema.RowsFirstValueColumn + i,
                    rowIndex,
                    value.ToStorageString(),
                    WorkbookStyles.Default));
            }

            data.AppendChild(row);
            rowIndex++;
        }

        part.Worksheet = new Worksheet(data);
    }

    // ------------------------------------------------------------------ hücreler

    private static Cell ValueCell(int column, uint row, VariableValue value, uint style)
    {
        var reference = CellReference(column, row);

        return value.Kind switch
        {
            VariableValueKind.Empty => new Cell { CellReference = reference, StyleIndex = style },

            VariableValueKind.Int or VariableValueKind.Float => new Cell
            {
                CellReference = reference,
                StyleIndex = style,
                DataType = CellValues.Number,
                CellValue = new CellValue(value.ToStorageString()),
            },

            VariableValueKind.Bool => new Cell
            {
                CellReference = reference,
                StyleIndex = style,
                DataType = CellValues.Boolean,
                CellValue = new CellValue(string.Equals(value.ToStorageString(), "true", StringComparison.Ordinal) ? "1" : "0"),
            },

            VariableValueKind.Date => new Cell
            {
                CellReference = reference,
                StyleIndex = style,
                DataType = CellValues.Number,
                CellValue = new CellValue(ToExcelSerial(value).ToString(CultureInfo.InvariantCulture)),
            },

            // Metin ve ayrıştırılamayan değerler inline string olarak yazılır. Paylaşılan
            // dize tablosu kullanılmıyor: kazanç küçük, okuma tarafında bir dolaylılık daha
            // demek ve bozuk bir tablo tüm sayfayı okunamaz hâle getirebiliyor.
            _ => TextCell(column, row, value.ToDisplayString(CultureInfo.InvariantCulture), style),
        };
    }

    private static Cell TextCell(int column, uint row, string? text, uint style) =>
        new()
        {
            CellReference = CellReference(column, row),
            StyleIndex = style,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(text ?? string.Empty)),
        };

    private static Cell NumberCell(int column, uint row, long number, uint style) =>
        new()
        {
            CellReference = CellReference(column, row),
            StyleIndex = style,
            DataType = CellValues.Number,
            CellValue = new CellValue(number.ToString(CultureInfo.InvariantCulture)),
        };

    private static double ToExcelSerial(VariableValue value)
    {
        var date = DateTime.ParseExact(value.ToStorageString(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (date - ExcelEpoch).TotalDays;
    }

    private static string CellReference(int column, uint row) =>
        ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);

    /// <summary>1 tabanlı sütun indisini Excel sütun adına çevirir (1 -> A, 27 -> AA).</summary>
    internal static string ColumnName(int column)
    {
        if (column < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Sütun indisi 1'den küçük olamaz.");
        }

        var name = string.Empty;
        var remaining = column;

        while (remaining > 0)
        {
            var modulo = (remaining - 1) % 26;
            name = (char)('A' + modulo) + name;
            remaining = (remaining - modulo - 1) / 26;
        }

        return name;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Geçici dosya silinemedi. Asıl hatanın üzerini örtmemek için yutuluyor;
            // dosya kullanıcının seçtiği klasörde ".tmp" uzantısıyla kalır.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Ürün sürüm bilgisi. Çalışma kitabı ve journal başlıklarına yazılır.</summary>
public static class ProductInfo
{
    /// <summary>Ürün sürümü (SemVer). Eklenti sürümünden ve şema sürümlerinden AYRIDIR.</summary>
    public const string Version = "1.1.1";

    public const string Name = "PDM Variable Studio";
}
