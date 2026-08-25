using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.Core.Workbook;

/// <summary>
/// Kullanıcı tarafından düzenlenmiş bir çalışma kitabını okur.
/// </summary>
/// <remarks>
/// <para>
/// Okuyucunun temel varsayımı: <b>kullanıcı dosyaya her şeyi yapmış olabilir.</b> Satırları
/// sıralamış, silmiş, eklemiş; sütunları taşımış, silmiş; korumayı kaldırıp teknik sayfaları
/// açmış olabilir. Bu yüzden:
/// </para>
/// <list type="bullet">
/// <item>Satır İNDİSİNE güvenilmez — eşleme <c>#</c> numarası üzerinden yapılır.</item>
/// <item>Sütun İNDİSİNE güvenilmez — eşleme <c>_Metadata</c>'daki <c>VariableId</c> üzerinden,
/// sütun taşınmışsa başlık metniyle yeniden bulunarak yapılır.</item>
/// <item>Başlık METNİNE tek başına güvenilmez — yalnızca taşınmış sütunu bulmak için ipucu.</item>
/// <item>Şüpheli her satır uygulanmaz; bir satırı atlamak, yanlış dosyaya yazmaktan iyidir.</item>
/// </list>
/// </remarks>
public sealed class WorkbookReader
{
    private static readonly DateTime ExcelEpoch = new(1899, 12, 30);

    public ImportedWorkbook Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return ImportedWorkbook.Rejected(path ?? string.Empty,
                ValidationIssue.Fatal(IssueCode.WorkbookCorrupted, path, "Dosya bulunamadı."));
        }

        try
        {
            return ReadCore(path);
        }
        catch (OpenXmlPackageException exception)
        {
            return ImportedWorkbook.Rejected(path,
                ValidationIssue.Fatal(IssueCode.WorkbookCorrupted, path, exception.Message));
        }
        catch (InvalidDataException exception)
        {
            // Zip yapısı bozuk: dosya .xlsx değil ya da yarım kopyalanmış.
            return ImportedWorkbook.Rejected(path,
                ValidationIssue.Fatal(IssueCode.WorkbookCorrupted, path, exception.Message));
        }
        catch (FormatException exception)
        {
            return ImportedWorkbook.Rejected(path,
                ValidationIssue.Fatal(IssueCode.WorkbookCorrupted, path, exception.Message));
        }
    }

    private ImportedWorkbook ReadCore(string path)
    {
        var issues = new IssueCollector();

        // Salt okunur açılır: kullanıcının dosyası hiçbir koşulda değiştirilmez.
        using var document = SpreadsheetDocument.Open(path, isEditable: false);

        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return ImportedWorkbook.Rejected(path,
                ValidationIssue.Fatal(IssueCode.WorkbookCorrupted, path, "WorkbookPart yok."));
        }

        var sharedStrings = LoadSharedStrings(workbookPart);

        var metadataGrid = ReadSheetGrid(workbookPart, WorkbookSchema.MetadataSheet, sharedStrings);
        if (metadataGrid is null)
        {
            return ImportedWorkbook.Rejected(path, ValidationIssue.Fatal(
                IssueCode.WorkbookCorrupted, path,
                $"'{WorkbookSchema.MetadataSheet}' sayfası yok."));
        }

        var metadata = ParseMetadata(metadataGrid, out var variables, out var declaredColumns);

        // --- şema sürümü: her şeyden önce ---
        var schemaVersion = ReadInt(metadata, WorkbookSchema.KeySchemaVersion, 0);

        if (schemaVersion > WorkbookSchema.CurrentVersion)
        {
            return ImportedWorkbook.Rejected(path, ValidationIssue.Fatal(
                IssueCode.UnsupportedWorkbookVersion, path,
                $"Dosya şeması {schemaVersion}, desteklenen en yüksek {WorkbookSchema.CurrentVersion}."));
        }

        if (schemaVersion < WorkbookSchema.MinimumSupportedVersion)
        {
            return ImportedWorkbook.Rejected(path, ValidationIssue.Fatal(
                IssueCode.UnsupportedWorkbookVersion, path,
                $"Dosya şeması {schemaVersion}, desteklenen en düşük {WorkbookSchema.MinimumSupportedVersion}."));
        }

        // --- bütünlük damgası ---
        var storedChecksum = ReadText(metadata, WorkbookSchema.KeyMetadataChecksum);
        var computedChecksum = WorkbookSchema.ComputeMetadataChecksum(metadata, variables, declaredColumns);

        if (!WorkbookSchema.ChecksumMatches(storedChecksum, computedChecksum))
        {
            return ImportedWorkbook.Rejected(path, ValidationIssue.Fatal(
                IssueCode.MetadataChecksumMismatch, path,
                $"Beklenen {storedChecksum}, hesaplanan {computedChecksum}."));
        }

        if (variables.Count == 0)
        {
            return ImportedWorkbook.Rejected(path, ValidationIssue.Fatal(
                IssueCode.WorkbookCorrupted, path, "Değişken tanımı bulunamadı."));
        }

        // --- teknik satır sayfası ---
        var rowsGrid = ReadSheetGrid(workbookPart, WorkbookSchema.RowsSheet, sharedStrings);
        if (rowsGrid is null)
        {
            return ImportedWorkbook.Rejected(path, ValidationIssue.Fatal(
                IssueCode.WorkbookCorrupted, path,
                $"'{WorkbookSchema.RowsSheet}' sayfası yok; three-way karşılaştırma yapılamaz."));
        }

        var variablesGrid = ReadSheetGrid(workbookPart, WorkbookSchema.VariablesSheet, sharedStrings);
        if (variablesGrid is null)
        {
            return ImportedWorkbook.Rejected(path, ValidationIssue.Fatal(
                IssueCode.WorkbookCorrupted, path,
                $"'{WorkbookSchema.VariablesSheet}' sayfası yok."));
        }

        var exportSessionId = ReadGuid(metadata, WorkbookSchema.KeyExportSessionId);
        var vault = new VaultIdentity(
            ReadText(metadata, WorkbookSchema.KeyVaultName),
            ReadText(metadata, WorkbookSchema.KeyVaultRootPath),
            ReadText(metadata, WorkbookSchema.KeyVaultDatabase));

        var actualColumns = ResolveVariableColumns(variablesGrid, variables, declaredColumns, issues);
        var snapshots = ReadRowSnapshots(rowsGrid, variables, exportSessionId, issues);
        var rows = ReadDataRows(variablesGrid, variables, actualColumns, snapshots, issues);

        return new ImportedWorkbook(
            path,
            schemaVersion,
            exportSessionId,
            ReadDate(metadata, WorkbookSchema.KeyExportedAtUtc),
            vault,
            ReadText(metadata, WorkbookSchema.KeySourceFolderPath),
            variables,
            actualColumns,
            rows,
            issues.Issues);
    }

    // ---------------------------------------------------------------- _Metadata

    private static List<KeyValuePair<string, string>> ParseMetadata(
        CellGrid grid,
        out List<PdmVariableDefinition> variables,
        out List<int> columns)
    {
        var metadata = new List<KeyValuePair<string, string>>();
        variables = new List<PdmVariableDefinition>();
        columns = new List<int>();

        var rowIndex = 1;
        var maxRow = grid.MaxRow;

        // 1. bölüm: anahtar/değer çiftleri, işaretçiye kadar.
        while (rowIndex <= maxRow)
        {
            var key = grid.Text(rowIndex, 1);
            if (string.Equals(key, WorkbookSchema.VariableTableMarker, StringComparison.Ordinal))
            {
                rowIndex++;
                break;
            }

            if (!string.IsNullOrWhiteSpace(key))
            {
                metadata.Add(new KeyValuePair<string, string>(key, grid.Text(rowIndex, 2)));
            }

            rowIndex++;
        }

        // 2. bölüm: başlık satırı atlanır, ardından tanımlar.
        rowIndex++;

        while (rowIndex <= maxRow)
        {
            var idText = grid.Text(rowIndex, 1);
            if (string.IsNullOrWhiteSpace(idText))
            {
                break;
            }

            if (int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var variableId))
            {
                variables.Add(new PdmVariableDefinition(
                    variableId,
                    grid.Text(rowIndex, 2),
                    ParseVariableType(grid.Text(rowIndex, 4)),
                    isMandatory: grid.Text(rowIndex, 6) == "1",
                    isUnique: grid.Text(rowIndex, 7) == "1",
                    isReadOnly: grid.Text(rowIndex, 8) == "1",
                    displayName: grid.Text(rowIndex, 3)));

                columns.Add(ParseIntOrDefault(grid.Text(rowIndex, 5), 0));
            }

            rowIndex++;
        }

        return metadata;
    }

    private static PdmVariableType ParseVariableType(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && Enum.IsDefined(typeof(PdmVariableType), value)
                ? (PdmVariableType)value
                : PdmVariableType.Text;

    // ------------------------------------------------------------ sütun eşlemesi

    /// <summary>
    /// Her değişkenin gerçekte hangi sütunda olduğunu bulur.
    /// </summary>
    /// <remarks>
    /// Önce <c>_Metadata</c>'da yazan sütunun başlığı beklenen başlıkla eşleşiyor mu bakılır.
    /// Eşleşmiyorsa sütun taşınmış demektir; başlık metni tüm sayfada aranır ve bulunursa
    /// satırlar <see cref="IssueCode.ColumnRelocated"/> uyarısı alır. Hiç bulunamazsa sütun
    /// silinmiş kabul edilir ve o değişkenin hücreleri atlanır — mevcut PDM değerine
    /// dokunulmaz.
    /// </remarks>
    private static List<int> ResolveVariableColumns(
        CellGrid grid,
        IReadOnlyList<PdmVariableDefinition> variables,
        IReadOnlyList<int> declaredColumns,
        IssueCollector issues)
    {
        var headerByColumn = new Dictionary<int, string>();
        for (var column = 1; column <= grid.MaxColumn; column++)
        {
            headerByColumn[column] = grid.Text(1, column);
        }

        var resolved = new List<int>(variables.Count);

        for (var i = 0; i < variables.Count; i++)
        {
            var variable = variables[i];
            var expectedHeader = ExpectedHeader(variable);
            var declared = i < declaredColumns.Count ? declaredColumns[i] : 0;

            if (declared > 0
                && headerByColumn.TryGetValue(declared, out var headerAtDeclared)
                && string.Equals(headerAtDeclared, expectedHeader, StringComparison.Ordinal))
            {
                resolved.Add(declared);
                continue;
            }

            var found = 0;
            foreach (var pair in headerByColumn)
            {
                if (string.Equals(pair.Value, expectedHeader, StringComparison.Ordinal))
                {
                    found = pair.Key;
                    break;
                }
            }

            if (found > 0)
            {
                issues.Warn(IssueCode.ColumnRelocated, variable.DisplayName,
                    $"Beklenen sütun {declared}, bulunan {found}.");
                resolved.Add(found);
                continue;
            }

            issues.Warn(IssueCode.VariableColumnMissing, variable.DisplayName,
                $"Beklenen sütun {declared}, başlık '{expectedHeader}'.");
            resolved.Add(0);
        }

        return resolved;
    }

    private static string ExpectedHeader(PdmVariableDefinition variable) =>
        variable.IsReadOnly ? variable.DisplayName + " (salt okunur)" : variable.DisplayName;

    // ----------------------------------------------------------------- _Rows

    /// <summary>
    /// <c>_Rows</c> sayfasından okunan satır kimliği ve orijinal değerler.
    /// </summary>
    /// <remarks>
    /// Dosya ADI ve YOLU burada YOK — kimlik için gerekmiyorlar. Gösterim için kullanılan
    /// ad/yol <c>Variables</c> sayfasından okunuyor ve yalnızca kullanıcı satırı tanısın diye
    /// taşınıyor; eşleştirme her zaman <see cref="FileId"/> / <see cref="FolderId"/> üzerinden.
    /// </remarks>
    private sealed class RowSnapshot
    {
        public int FileId { get; set; }

        public int FolderId { get; set; }

        /// <summary>
        /// Dışa aktarım anında saklanan bilgi. Konfigürasyon dizesinin PDM'ye nasıl
        /// gönderileceğini belirlediği için dosya adı uzantısından TÜRETİLMEZ — o sütun
        /// kullanıcı tarafından değiştirilmiş olabilir.
        /// </summary>
        public bool IsSolidWorks { get; set; }

        public ConfigurationKey Configuration { get; set; }

        public int FileVersion { get; set; }

        public List<VariableValue> OriginalValues { get; set; } = new();

        public bool FingerprintValid { get; set; }
    }

    private static Dictionary<int, RowSnapshot> ReadRowSnapshots(
        CellGrid grid,
        IReadOnlyList<PdmVariableDefinition> variables,
        Guid exportSessionId,
        IssueCollector issues)
    {
        var snapshots = new Dictionary<int, RowSnapshot>();
        var duplicates = new HashSet<int>();

        for (var row = 2; row <= grid.MaxRow; row++)
        {
            var idText = grid.Text(row, WorkbookSchema.RowsColExportRowId);
            if (string.IsNullOrWhiteSpace(idText))
            {
                continue;
            }

            if (!int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exportRowId))
            {
                continue;
            }

            var fileId = ParseIntOrDefault(grid.Text(row, WorkbookSchema.RowsColFileId), 0);
            var folderId = ParseIntOrDefault(grid.Text(row, WorkbookSchema.RowsColFolderId), 0);
            var configuration = ConfigurationKey.FromStorageString(grid.Text(row, WorkbookSchema.RowsColConfiguration));
            var fileVersion = ParseIntOrDefault(grid.Text(row, WorkbookSchema.RowsColFileVersion), 0);
            var isSolidWorks = grid.Text(row, WorkbookSchema.RowsColIsSolidWorks) == "1";
            var storedFingerprint = grid.Text(row, WorkbookSchema.RowsColFingerprint);

            var originals = new List<VariableValue>(variables.Count);
            for (var i = 0; i < variables.Count; i++)
            {
                var stored = grid.Text(row, WorkbookSchema.RowsFirstValueColumn + i);
                originals.Add(VariableValue.FromStorage(stored, variables[i].DataType));
            }

            var computed = WorkbookSchema.ComputeRowFingerprint(
                exportSessionId, exportRowId, fileId, folderId, configuration, originals);

            var snapshot = new RowSnapshot
            {
                FileId = fileId,
                FolderId = folderId,
                IsSolidWorks = isSolidWorks,
                Configuration = configuration,
                FileVersion = fileVersion,
                OriginalValues = originals,
                FingerprintValid = WorkbookSchema.ChecksumMatches(storedFingerprint, computed),
            };

            if (snapshots.ContainsKey(exportRowId))
            {
                duplicates.Add(exportRowId);
                continue;
            }

            snapshots[exportRowId] = snapshot;
        }

        foreach (var duplicate in duplicates)
        {
            // Hangisinin doğru olduğu belirlenemez; ikisi de kullanılmaz.
            snapshots.Remove(duplicate);
            issues.Error(IssueCode.DuplicateRow, $"satır #{duplicate}",
                "_Rows sayfasında aynı numara birden fazla kez geçiyor.");
        }

        return snapshots;
    }

    // ------------------------------------------------------------- Variables

    private static List<ImportedRow> ReadDataRows(
        CellGrid grid,
        IReadOnlyList<PdmVariableDefinition> variables,
        IReadOnlyList<int> columns,
        Dictionary<int, RowSnapshot> snapshots,
        IssueCollector issues)
    {
        var rows = new List<ImportedRow>();
        var seen = new HashSet<int>();
        var duplicated = new HashSet<int>();
        var unknownCount = 0;

        for (var row = 2; row <= grid.MaxRow; row++)
        {
            var idText = grid.Text(row, WorkbookSchema.ColExportRowId);

            if (string.IsNullOrWhiteSpace(idText))
            {
                // Tamamen boş bir satır kullanıcının eklediği bir ayraç olabilir; gürültü
                // üretmemek için sessizce atlanır. İçinde veri varsa aşağıda sayılır.
                if (!RowHasAnyValue(grid, row, columns))
                {
                    continue;
                }

                unknownCount++;
                continue;
            }

            if (!int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exportRowId))
            {
                unknownCount++;
                continue;
            }

            if (!seen.Add(exportRowId))
            {
                duplicated.Add(exportRowId);
                continue;
            }

            if (!snapshots.TryGetValue(exportRowId, out var snapshot))
            {
                unknownCount++;
                continue;
            }

            var raw = new List<object?>(variables.Count);
            for (var i = 0; i < variables.Count; i++)
            {
                var column = i < columns.Count ? columns[i] : 0;
                raw.Add(column > 0 ? grid.Raw(row, column) : null);
            }

            // Dosya adı ve klasör yolu Variables sayfasından, YALNIZCA GÖSTERİM için okunuyor.
            // Kullanıcı bu sütunları (koruma kaldırılırsa) değiştirmiş olabilir; eşleştirme
            // her zaman _Rows'taki FileId/FolderId üzerinden yapılıyor. Buraya okunmasının
            // sebebi önizlemede "hangi dosya" sorusunun yanıtlanabilmesi.
            var identity = new PdmFileIdentity(
                snapshot.FileId,
                snapshot.FolderId,
                grid.Text(row, WorkbookSchema.ColFileName),
                grid.Text(row, WorkbookSchema.ColRelativePath),
                snapshot.IsSolidWorks);

            var imported = new ImportedRow(
                exportRowId,
                identity,
                snapshot.Configuration,
                snapshot.FileVersion,
                snapshot.OriginalValues,
                raw);

            if (!snapshot.FingerprintValid)
            {
                imported.Issues.Add(ValidationIssue.Error(IssueCode.RowTampered, $"satır #{exportRowId}",
                    "RowFingerprint tutmuyor."));
            }

            if (identity.FileId <= 0)
            {
                imported.Issues.Add(ValidationIssue.Error(IssueCode.RowTampered, $"satır #{exportRowId}",
                    "FileId geçersiz."));
            }

            rows.Add(imported);
        }

        // Yinelenen satırların HİÇBİRİ uygulanmaz; ilk görülen de dahil.
        if (duplicated.Count > 0)
        {
            foreach (var row in rows)
            {
                if (duplicated.Contains(row.ExportRowId))
                {
                    row.Issues.Add(ValidationIssue.Error(IssueCode.DuplicateRow, $"satır #{row.ExportRowId}",
                        "Variables sayfasında aynı numara birden fazla kez geçiyor."));
                }
            }

            issues.Error(IssueCode.DuplicateRow, $"{duplicated.Count} satır");
        }

        if (unknownCount > 0)
        {
            issues.Warn(IssueCode.UnknownRow, $"{unknownCount} satır",
                "Numarası _Rows sayfasında bulunamayan satırlar atlandı.");
        }

        return rows;
    }

    private static bool RowHasAnyValue(CellGrid grid, int row, IReadOnlyList<int> columns)
    {
        foreach (var column in columns)
        {
            if (column > 0 && !string.IsNullOrWhiteSpace(grid.Text(row, column)))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------- yardımcılar

    private static string ReadText(IEnumerable<KeyValuePair<string, string>> metadata, string key)
    {
        foreach (var pair in metadata)
        {
            if (string.Equals(pair.Key, key, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return string.Empty;
    }

    private static int ReadInt(IEnumerable<KeyValuePair<string, string>> metadata, string key, int fallback) =>
        ParseIntOrDefault(ReadText(metadata, key), fallback);

    private static Guid ReadGuid(IEnumerable<KeyValuePair<string, string>> metadata, string key) =>
        Guid.TryParse(ReadText(metadata, key), out var value) ? value : Guid.Empty;

    private static DateTime ReadDate(IEnumerable<KeyValuePair<string, string>> metadata, string key) =>
        DateTime.TryParse(
            ReadText(metadata, key),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var value)
            ? value
            : default;

    private static int ParseIntOrDefault(string? text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string[] LoadSharedStrings(WorkbookPart workbookPart)
    {
        var table = workbookPart.SharedStringTablePart?.SharedStringTable;
        if (table is null)
        {
            return Array.Empty<string>();
        }

        var items = table.Elements<SharedStringItem>().ToList();
        var result = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            result[i] = items[i].InnerText;
        }

        return result;
    }

    private CellGrid? ReadSheetGrid(WorkbookPart workbookPart, string sheetName, string[] sharedStrings)
    {
        var sheet = workbookPart.Workbook.Descendants<Sheet>()
            .FirstOrDefault(s => string.Equals(s.Name?.Value, sheetName, StringComparison.Ordinal));

        if (sheet?.Id?.Value is null)
        {
            return null;
        }

        if (workbookPart.GetPartById(sheet.Id!.Value!) is not WorksheetPart part)
        {
            return null;
        }

        var grid = new CellGrid();

        foreach (var row in part.Worksheet.Descendants<Row>())
        {
            var rowIndex = (int)(row.RowIndex?.Value ?? 0);
            if (rowIndex <= 0)
            {
                continue;
            }

            foreach (var cell in row.Elements<Cell>())
            {
                var column = ColumnIndex(cell.CellReference?.Value);
                if (column <= 0)
                {
                    continue;
                }

                grid.Set(rowIndex, column, ExtractValue(cell, sharedStrings));
            }
        }

        return grid;
    }

    /// <summary>
    /// Bir hücrenin ham değerini çıkarır.
    /// </summary>
    /// <remarks>
    /// Tarih tespiti için sayı biçimi ÇÖZÜMLENMEZ. Buna gerek yok: her değişkenin beyan
    /// edilen tipi <c>_Metadata</c>'dan biliniyor, dolayısıyla sayısal bir hücrenin tarih mi
    /// yoksa sayı mı olduğuna değişken tanımına bakarak karar veriliyor
    /// (<see cref="VariableValue.From"/>). Bu, genel amaçlı bir sayı biçimi ayrıştırıcısına
    /// olan ihtiyacı ve ona bağlı bir kütüphaneyi tamamen ortadan kaldırıyor.
    /// </remarks>
    private static object? ExtractValue(Cell cell, string[] sharedStrings)
    {
        var type = cell.DataType?.Value;

        if (type == CellValues.InlineString)
        {
            return cell.InlineString?.Text?.Text ?? cell.InnerText;
        }

        if (type == CellValues.SharedString)
        {
            var raw = cell.CellValue?.InnerText;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0
                && index < sharedStrings.Length)
            {
                return sharedStrings[index];
            }

            return string.Empty;
        }

        if (type == CellValues.Boolean)
        {
            return cell.CellValue?.InnerText == "1";
        }

        if (type == CellValues.Date)
        {
            // Nadir ama geçerli: bazı araçlar ISO 8601 tarihi bu tiple yazar.
            return DateTime.TryParse(
                cell.CellValue?.InnerText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var date)
                ? date
                : cell.CellValue?.InnerText;
        }

        if (type == CellValues.Error)
        {
            // #REF! / #DIV/0! gibi bir formül hatası. Metin olarak taşınır ve tip dönüşümünde
            // ayrıştırılamaz olarak işaretlenir; sessizce boşa çevrilmesi veri kaybı olurdu.
            return cell.CellValue?.InnerText ?? "#ERROR";
        }

        var text = cell.CellValue?.InnerText;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return text;
    }

    /// <summary>"BC12" gibi bir hücre adresinden 1 tabanlı sütun indisini çıkarır.</summary>
    internal static int ColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference))
        {
            return 0;
        }

        var column = 0;
        foreach (var c in cellReference!)
        {
            if (c >= 'A' && c <= 'Z')
            {
                column = (column * 26) + (c - 'A' + 1);
            }
            else if (c >= 'a' && c <= 'z')
            {
                column = (column * 26) + (c - 'a' + 1);
            }
            else
            {
                break;
            }
        }

        return column;
    }

    /// <summary>Seyrek bir hücre tablosu. Excel boş hücreleri hiç yazmaz.</summary>
    private sealed class CellGrid
    {
        private readonly Dictionary<long, object?> _cells = new();

        public int MaxRow { get; private set; }

        public int MaxColumn { get; private set; }

        public void Set(int row, int column, object? value)
        {
            _cells[Key(row, column)] = value;

            if (row > MaxRow)
            {
                MaxRow = row;
            }

            if (column > MaxColumn)
            {
                MaxColumn = column;
            }
        }

        public object? Raw(int row, int column) =>
            _cells.TryGetValue(Key(row, column), out var value) ? value : null;

        public string Text(int row, int column)
        {
            var value = Raw(row, column);
            return value switch
            {
                null => string.Empty,
                string s => s,
                bool b => b ? "1" : "0",
                double d => d.ToString("R", CultureInfo.InvariantCulture),
                DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            };
        }

        private static long Key(int row, int column) => ((long)row << 20) | (uint)column;
    }
}
