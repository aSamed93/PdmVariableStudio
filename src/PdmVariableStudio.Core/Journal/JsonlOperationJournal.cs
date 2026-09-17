using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.Core.Journal;

/// <summary>
/// İşlem günlüğünün append-only JSONL uygulaması.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden SQLite değil.</b> PDM eklentisi Administration aracına düz bir DLL listesi olarak
/// yükleniyor; klasör yapısı korunmuyor. SQLite'ın <c>runtimes/win-x64/native/e_sqlite3.dll</c>
/// yerleşimi bu modelde çözülmez ve çözülmezse eklenti hiç yüklenmez. Bu riski ürünün temeline
/// koymak, kazanılan sorgulanabilirliğe değmiyor.
/// </para>
/// <para>
/// <b>Çökmeye dayanıklılık.</b> Her satır <c>FileOptions.WriteThrough</c> ile açılan bir akışa
/// yazılır ve <c>Flush(flushToDisk: true)</c> ile diske indirilir. Append-only olduğu için
/// yarım yazılmış son satır ayrıştırılamaz ve atılır; önceki satırlar sağlam kalır.
/// </para>
/// <para>
/// <b>Şema sürümü.</b> <see cref="JournalSchemaVersion"/> ürün ve çalışma kitabı
/// sürümlerinden ayrıdır. Eski kayıtlar okunmaya devam eder; günlük hiçbir zaman silinmez,
/// çünkü silinen bir kayıt geri alınamayan bir değişiklik demektir.
/// </para>
/// </remarks>
public sealed class JsonlOperationJournal : IOperationJournal
{
    public const int JournalSchemaVersion = 1;

    private const string TypeHeader = "header";
    private const string TypeIntent = "intent";
    private const string TypeResult = "result";
    private const string TypeEnd = "end";
    private const string TypeSummary = "op";
    private const string TypeUndoLink = "undolink";

    private const string IndexFileName = "index.jsonl";
    private const string OperationsFolder = "ops";

    private readonly string _rootDirectory;

    public JsonlOperationJournal(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? DefaultRootDirectory();
    }

    public static string DefaultRootDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PdmVariableStudio",
            "journal");

    // ------------------------------------------------------------------ yazma

    public OperationOutcome Begin(ApplyOperation operation)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        var line = new FlatJson.Writer()
            .Text("t", TypeHeader)
            .Number("schema", JournalSchemaVersion)
            .Text("opId", operation.OperationId.ToString("D"))
            .Text("type", operation.Type.ToString())
            .Text("utc", operation.UtcTimestamp.ToString("o", CultureInfo.InvariantCulture))
            .Text("winUser", operation.WindowsUser)
            .Text("pdmUser", operation.PdmUser)
            .Text("vault", operation.Vault.Name)
            .Text("vaultRoot", operation.Vault.RootFolderPath)
            .Text("vaultDb", operation.Vault.DatabaseName)
            .Text("workbook", operation.SourceWorkbookPath)
            .Text("exportSessionId", operation.ExportSessionId.ToString("D"))
            .Text("undoes", operation.UndoesOperationId.ToString("D"))
            .Bool("checkIn", operation.CheckInRequested)
            .Build();

        return RememberAndAppend(OperationPath(operation.Vault, operation.OperationId), operation.OperationId, line);
    }

    public OperationOutcome WriteIntent(Guid operationId, ApplyOperationEntry entry)
    {
        var line = new FlatJson.Writer()
            .Text("t", TypeIntent)
            .Number("fileId", entry.File.FileId)
            .Number("folderId", entry.File.FolderId)
            .Text("fileName", entry.File.FileName)
            .Text("path", entry.File.RelativePath)
            .Text("cfg", entry.Configuration.ToStorageString())
            .Number("varId", entry.Variable.VariableId)
            .Text("varName", entry.Variable.Name)
            .Number("varType", (int)entry.Variable.DataType)
            .Text("old", entry.PreviousValue.ToStorageString())
            .Text("new", entry.AppliedValue.ToStorageString())
            .Number("fileVersion", entry.FileVersion)
            .Build();

        return AppendLine(CurrentOperationPath(operationId), line);
    }

    public OperationOutcome WriteResult(Guid operationId, ApplyOperationEntry entry)
    {
        var line = new FlatJson.Writer()
            .Text("t", TypeResult)
            .Number("fileId", entry.File.FileId)
            .Number("folderId", entry.File.FolderId)
            .Text("cfg", entry.Configuration.ToStorageString())
            .Number("varId", entry.Variable.VariableId)
            .Text("r", entry.Result.ToString())
            .Text("code", entry.Code.ToString())
            .Build();

        return AppendLine(CurrentOperationPath(operationId), line);
    }

    public OperationOutcome Complete(ApplyOperation operation)
    {
        var endLine = new FlatJson.Writer()
            .Text("t", TypeEnd)
            .Text("outcome", operation.Outcome.ToString())
            .Number("applied", operation.AppliedCount)
            .Number("failed", operation.FailedCount)
            .Number("skipped", operation.SkippedCount)
            .Number("files", operation.FileCount)
            .Text("utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
            .Build();

        var result = AppendLine(OperationPath(operation.Vault, operation.OperationId), endLine);
        if (result.IsFailure)
        {
            return result;
        }

        var summary = new FlatJson.Writer()
            .Text("t", TypeSummary)
            .Number("schema", JournalSchemaVersion)
            .Text("opId", operation.OperationId.ToString("D"))
            .Text("type", operation.Type.ToString())
            .Text("utc", operation.UtcTimestamp.ToString("o", CultureInfo.InvariantCulture))
            .Text("winUser", operation.WindowsUser)
            .Text("pdmUser", operation.PdmUser)
            .Text("vault", operation.Vault.Name)
            .Text("vaultRoot", operation.Vault.RootFolderPath)
            .Text("workbook", operation.SourceWorkbookPath)
            .Text("exportSessionId", operation.ExportSessionId.ToString("D"))
            .Text("undoes", operation.UndoesOperationId.ToString("D"))
            .Text("outcome", operation.Outcome.ToString())
            .Number("applied", operation.AppliedCount)
            .Number("failed", operation.FailedCount)
            .Number("skipped", operation.SkippedCount)
            .Number("files", operation.FileCount)
            .Bool("checkIn", operation.CheckInRequested)
            .Build();

        return AppendLine(IndexPath(operation.Vault), summary);
    }

    public OperationOutcome LinkUndo(Guid originalOperationId, Guid undoOperationId)
    {
        // Bağlantı indekse AYRI bir satır olarak eklenir; mevcut satır değiştirilmez.
        // Append-only kalmak, çökme dayanıklılığının tek sebebi.
        var line = new FlatJson.Writer()
            .Text("t", TypeUndoLink)
            .Text("opId", originalOperationId.ToString("D"))
            .Text("undoneBy", undoOperationId.ToString("D"))
            .Build();

        foreach (var indexPath in AllIndexPaths())
        {
            var result = AppendLine(indexPath, line);
            if (result.IsFailure)
            {
                return result;
            }
        }

        return OperationOutcome.Success();
    }

    // ------------------------------------------------------------------ okuma

    public IReadOnlyList<ApplyOperation> ListOperations(VaultIdentity vault, int maxCount = 100)
    {
        var path = IndexPath(vault);
        var operations = new Dictionary<Guid, ApplyOperation>();
        var order = new List<Guid>();

        foreach (var line in ReadLines(path))
        {
            var record = FlatJson.TryParse(line);
            if (record is null)
            {
                continue;
            }

            switch (record.Text("t"))
            {
                case TypeSummary:
                {
                    var operation = ReadSummary(record);
                    if (!operations.ContainsKey(operation.OperationId))
                    {
                        order.Add(operation.OperationId);
                    }

                    operations[operation.OperationId] = operation;
                    break;
                }

                case TypeUndoLink:
                {
                    var target = record.Guid("opId");
                    if (operations.TryGetValue(target, out var existing))
                    {
                        existing.UndoneByOperationId = record.Guid("undoneBy");
                    }

                    break;
                }
            }
        }

        // Yeniden eskiye. İndeks append-only olduğu için sondaki en yenidir.
        var result = new List<ApplyOperation>(Math.Min(maxCount, order.Count));
        for (var i = order.Count - 1; i >= 0 && result.Count < maxCount; i--)
        {
            result.Add(operations[order[i]]);
        }

        return result;
    }

    public OperationOutcome<ApplyOperation> LoadOperation(VaultIdentity vault, Guid operationId)
    {
        var path = OperationPath(vault, operationId);

        if (!File.Exists(path))
        {
            return OperationOutcome<ApplyOperation>.Failure(IssueCode.WorkbookCorrupted,
                $"İşlem dosyası yok: {path}");
        }

        ApplyOperation? operation = null;
        var pending = new Dictionary<string, ApplyOperationEntry>(StringComparer.Ordinal);
        var sawEnd = false;

        foreach (var line in ReadLines(path))
        {
            var record = FlatJson.TryParse(line);
            if (record is null)
            {
                // Yarım yazılmış satır. Çökme anında olabilir; atlanır.
                continue;
            }

            switch (record.Text("t"))
            {
                case TypeHeader:
                    operation = ReadHeader(record);
                    break;

                case TypeIntent when operation is not null:
                {
                    var entry = ReadIntent(record);
                    operation.Entries.Add(entry);
                    pending[EntryKey(entry)] = entry;
                    break;
                }

                case TypeResult when operation is not null:
                {
                    var key = ResultKey(record);
                    if (pending.TryGetValue(key, out var entry))
                    {
                        entry.Result = ParseEnum(record.Text("r"), EntryResult.Unknown);
                        entry.Code = ParseEnum(record.Text("code"), IssueCode.None);
                    }

                    break;
                }

                case TypeEnd when operation is not null:
                    operation.Outcome = ParseEnum(record.Text("outcome"), OperationOutcomeKind.Incomplete);
                    operation.AppliedCount = record.Int("applied");
                    operation.FailedCount = record.Int("failed");
                    operation.SkippedCount = record.Int("skipped");
                    operation.FileCount = record.Int("files");
                    sawEnd = true;
                    break;
            }
        }

        if (operation is null)
        {
            return OperationOutcome<ApplyOperation>.Failure(IssueCode.WorkbookCorrupted,
                "İşlem başlık satırı okunamadı.");
        }

        if (!sawEnd)
        {
            // Kapanış satırı yok: süreç yazma sırasında ölmüş. Kayıtların gerçekten
            // uygulanıp uygulanmadığı Undo önizlemesinde güncel PDM değerine bakılarak
            // belirlenir; burada olduğu gibi bırakılır.
            operation.Outcome = OperationOutcomeKind.Incomplete;
            operation.AppliedCount = CountApplied(operation);
        }

        // Geri alınma bağlantısı indekste tutuluyor.
        foreach (var summary in ListOperations(vault, int.MaxValue))
        {
            if (summary.OperationId == operationId)
            {
                operation.UndoneByOperationId = summary.UndoneByOperationId;
                break;
            }
        }

        return OperationOutcome<ApplyOperation>.Success(operation);
    }

    private static int CountApplied(ApplyOperation operation)
    {
        var count = 0;
        foreach (var entry in operation.Entries)
        {
            if (entry.Result == EntryResult.Applied)
            {
                count++;
            }
        }

        return count;
    }

    private static ApplyOperation ReadHeader(FlatJson.Record record)
    {
        var operation = new ApplyOperation(
            record.Guid("opId"),
            ParseEnum(record.Text("type"), OperationType.Apply),
            record.DateUtc("utc"),
            new VaultIdentity(record.Text("vault"), record.Text("vaultRoot"), record.Text("vaultDb")),
            record.Text("winUser"),
            record.Text("pdmUser"))
        {
            SourceWorkbookPath = record.Text("workbook"),
            ExportSessionId = record.Guid("exportSessionId"),
            UndoesOperationId = record.Guid("undoes"),
            CheckInRequested = record.Bool("checkIn"),
        };

        return operation;
    }

    private static ApplyOperation ReadSummary(FlatJson.Record record)
    {
        var operation = new ApplyOperation(
            record.Guid("opId"),
            ParseEnum(record.Text("type"), OperationType.Apply),
            record.DateUtc("utc"),
            new VaultIdentity(record.Text("vault"), record.Text("vaultRoot")),
            record.Text("winUser"),
            record.Text("pdmUser"))
        {
            SourceWorkbookPath = record.Text("workbook"),
            ExportSessionId = record.Guid("exportSessionId"),
            UndoesOperationId = record.Guid("undoes"),
            Outcome = ParseEnum(record.Text("outcome"), OperationOutcomeKind.Incomplete),
            AppliedCount = record.Int("applied"),
            FailedCount = record.Int("failed"),
            SkippedCount = record.Int("skipped"),
            FileCount = record.Int("files"),
            CheckInRequested = record.Bool("checkIn"),
        };

        return operation;
    }

    private static ApplyOperationEntry ReadIntent(FlatJson.Record record)
    {
        var dataType = (PdmVariableType)record.Int("varType", (int)PdmVariableType.Text);

        var file = new PdmFileIdentity(
            record.Int("fileId"),
            record.Int("folderId"),
            record.Text("fileName"),
            record.Text("path"));

        var variable = new PdmVariableDefinition(record.Int("varId"), record.Text("varName"), dataType);

        return new ApplyOperationEntry(
            file,
            ConfigurationKey.FromStorageString(record.Text("cfg")),
            variable,
            VariableValue.FromStorage(record.Text("old"), dataType),
            VariableValue.FromStorage(record.Text("new"), dataType),
            record.Int("fileVersion"));
    }

    private static string EntryKey(ApplyOperationEntry entry) =>
        string.Concat(
            entry.File.FileId.ToString(CultureInfo.InvariantCulture), "|",
            entry.File.FolderId.ToString(CultureInfo.InvariantCulture), "|",
            entry.Configuration.ToStorageString(), "|",
            entry.Variable.VariableId.ToString(CultureInfo.InvariantCulture));

    private static string ResultKey(FlatJson.Record record) =>
        string.Concat(
            record.Int("fileId").ToString(CultureInfo.InvariantCulture), "|",
            record.Int("folderId").ToString(CultureInfo.InvariantCulture), "|",
            record.Text("cfg"), "|",
            record.Int("varId").ToString(CultureInfo.InvariantCulture));

    private static T ParseEnum<T>(string text, T fallback)
        where T : struct
        => Enum.TryParse<T>(text, ignoreCase: false, out var value) ? value : fallback;

    // --------------------------------------------------------------- dosya sistemi

    /// <summary>
    /// Bir satırı diske indirerek ekler.
    /// </summary>
    /// <remarks>
    /// <c>WriteThrough</c> ve <c>Flush(true)</c> birlikte kullanılıyor: ilki işletim sistemi
    /// önbelleğini atlar, ikincisi sürücü önbelleğinin boşaltılmasını ister. Elektrik kesintisi
    /// dahil, yazılmış bir satırın kaybolmaması hedefleniyor. Maliyeti hücre başına birkaç
    /// milisaniye; PDM'ye yazma zaten bundan çok daha pahalı.
    /// </remarks>
    private static OperationOutcome AppendLine(string path, string line)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.WriteThrough);

            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);

            return OperationOutcome.Success();
        }
        catch (IOException exception)
        {
            return OperationOutcome.Failure(IssueCode.JournalWriteFailed, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return OperationOutcome.Failure(IssueCode.JournalWriteFailed, exception.Message);
        }
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        // FileShare.ReadWrite: aynı anda yazan bir işlem varken de okunabilsin.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    private string VaultDirectory(VaultIdentity vault) =>
        Path.Combine(_rootDirectory, SafeName(vault.Name));

    private string IndexPath(VaultIdentity vault) =>
        Path.Combine(VaultDirectory(vault), IndexFileName);

    private string OperationPath(VaultIdentity vault, Guid operationId) =>
        Path.Combine(VaultDirectory(vault), OperationsFolder, operationId.ToString("N") + ".jsonl");

    /// <summary>
    /// Açık bir işlemin dosya yolu. <see cref="Begin"/> sırasında kaydedilir ki niyet/sonuç
    /// satırları vault kimliğini tekrar taşımak zorunda kalmasın.
    /// </summary>
    private readonly Dictionary<Guid, string> _openOperations = new();

    private string CurrentOperationPath(Guid operationId) =>
        _openOperations.TryGetValue(operationId, out var path)
            ? path
            : throw new InvalidOperationException(
                $"'{operationId}' işlemi açılmamış. WriteIntent/WriteResult öncesinde Begin çağrılmalı.");

    private OperationOutcome RememberAndAppend(string path, Guid operationId, string line)
    {
        _openOperations[operationId] = path;
        return AppendLine(path, line);
    }

    private IEnumerable<string> AllIndexPaths()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            yield break;
        }

        foreach (var directory in Directory.GetDirectories(_rootDirectory))
        {
            var path = Path.Combine(directory, IndexFileName);
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "_bilinmeyen";
        }

        var builder = new StringBuilder(name.Length);
        var invalid = Path.GetInvalidFileNameChars();

        foreach (var c in name)
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return builder.ToString();
    }
}
