using System;
using System.IO;
using System.Linq;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Journal;
using Xunit;

namespace PdmVariableStudio.Tests.Journal;

/// <summary>
/// İşlem günlüğünün çökmeye dayanıklılık testleri.
/// </summary>
/// <remarks>
/// Günlük append-only: yarım yazılmış son satır ayrıştırılamaz ve atılır, önceki satırlar
/// sağlam kalır. Bu dosyadaki testler bunun gerçekten böyle olduğunu doğrular.
/// </remarks>
public class JournalTests : IDisposable
{
    private static readonly VaultIdentity Vault = new("MakinaVault", "C:\\MakinaVault", "MakinaVaultDb");

    private static readonly PdmVariableDefinition Material =
        new(23, "Material", PdmVariableType.Text, displayName: "Malzeme");

    private readonly string _root;
    private readonly JsonlOperationJournal _journal;

    public JournalTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pvs-j-" + Guid.NewGuid().ToString("N"));
        _journal = new JsonlOperationJournal(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void IslemTamDonusYapar()
    {
        var operation = NewOperation();
        var entry = NewEntry("A", "B");

        Assert.True(_journal.Begin(operation).IsSuccess);
        Assert.True(_journal.WriteIntent(operation.OperationId, entry).IsSuccess);

        entry.Result = EntryResult.Applied;
        Assert.True(_journal.WriteResult(operation.OperationId, entry).IsSuccess);

        operation.Entries.Add(entry);
        operation.AppliedCount = 1;
        operation.FileCount = 1;
        operation.Outcome = OperationOutcomeKind.Success;
        Assert.True(_journal.Complete(operation).IsSuccess);

        var loaded = _journal.LoadOperation(Vault, operation.OperationId);

        Assert.True(loaded.IsSuccess);
        Assert.Equal(OperationOutcomeKind.Success, loaded.Value.Outcome);

        var loadedEntry = Assert.Single(loaded.Value.Entries);
        Assert.Equal(EntryResult.Applied, loadedEntry.Result);
        Assert.Equal("A", loadedEntry.PreviousValue.ToStorageString());
        Assert.Equal("B", loadedEntry.AppliedValue.ToStorageString());
        Assert.Equal(23, loadedEntry.Variable.VariableId);
    }

    [Fact]
    public void KapanisSatiriYoksa_IslemYarimKalmisGorunur()
    {
        // Süreç commit sırasında öldürülmüş.
        var operation = NewOperation();
        var entry = NewEntry("A", "B");

        _journal.Begin(operation);
        _journal.WriteIntent(operation.OperationId, entry);
        entry.Result = EntryResult.Applied;
        _journal.WriteResult(operation.OperationId, entry);
        // Complete YOK.

        var loaded = _journal.LoadOperation(Vault, operation.OperationId);

        Assert.True(loaded.IsSuccess);
        Assert.Equal(OperationOutcomeKind.Incomplete, loaded.Value.Outcome);
        Assert.Equal(1, loaded.Value.AppliedCount);
        Assert.True(loaded.Value.IsUndoCandidate);
    }

    [Fact]
    public void SonucSatiriYoksa_KayitBelirsizKalir()
    {
        // Niyet yazıldı, PDM'ye yazma sırasında çökme oldu. Kaydın gerçekten uygulanıp
        // uygulanmadığı yalnızca PDM'deki güncel değere bakılarak anlaşılabilir.
        var operation = NewOperation();
        var entry = NewEntry("A", "B");

        _journal.Begin(operation);
        _journal.WriteIntent(operation.OperationId, entry);

        var loaded = _journal.LoadOperation(Vault, operation.OperationId);

        Assert.Equal(EntryResult.Unknown, Assert.Single(loaded.Value.Entries).Result);
    }

    [Fact]
    public void YarimYazilmisSonSatir_OncekileriBozmaz()
    {
        var operation = NewOperation();
        var entry = NewEntry("A", "B");

        _journal.Begin(operation);
        _journal.WriteIntent(operation.OperationId, entry);
        entry.Result = EntryResult.Applied;
        _journal.WriteResult(operation.OperationId, entry);

        // Elektrik kesintisi: son satır yarıda kaldı.
        var path = OperationFile(operation.OperationId);
        File.AppendAllText(path, "{\"t\":\"end\",\"outcome\":\"Suc");

        var loaded = _journal.LoadOperation(Vault, operation.OperationId);

        Assert.True(loaded.IsSuccess);
        Assert.Equal(OperationOutcomeKind.Incomplete, loaded.Value.Outcome);

        // Bozuk satır atıldı, önceki kayıt sağlam.
        var loadedEntry = Assert.Single(loaded.Value.Entries);
        Assert.Equal(EntryResult.Applied, loadedEntry.Result);
    }

    [Fact]
    public void BozukSatirlarAtlanirDigerleriOkunur()
    {
        var operation = NewOperation();
        _journal.Begin(operation);

        var path = OperationFile(operation.OperationId);
        File.AppendAllText(path, "bu bir json degil\n");
        File.AppendAllText(path, "{bozuk\n");

        _journal.WriteIntent(operation.OperationId, NewEntry("A", "B"));

        var loaded = _journal.LoadOperation(Vault, operation.OperationId);

        Assert.True(loaded.IsSuccess);
        Assert.Single(loaded.Value.Entries);
    }

    [Fact]
    public void IslemListesiYenidenEskiyeSiralanir()
    {
        var first = CompleteSimpleOperation();
        var second = CompleteSimpleOperation();

        var operations = _journal.ListOperations(Vault);

        Assert.Equal(2, operations.Count);
        Assert.Equal(second, operations[0].OperationId);
        Assert.Equal(first, operations[1].OperationId);
    }

    [Fact]
    public void GeriAlmaBaglantisi_IndekseAppendEdilirVeOkunur()
    {
        var original = CompleteSimpleOperation();
        var undo = CompleteSimpleOperation();

        Assert.True(_journal.LinkUndo(original, undo).IsSuccess);

        var operations = _journal.ListOperations(Vault);
        var linked = operations.Single(o => o.OperationId == original);

        Assert.True(linked.IsUndone);
        Assert.Equal(undo, linked.UndoneByOperationId);
    }

    [Fact]
    public void OzelKarakterliDegerler_KacisliYazilirVeGeriOkunur()
    {
        var operation = NewOperation();
        var entry = new ApplyOperationEntry(
            new PdmFileIdentity(1, 10, "a\"b.sldprt"),
            ConfigurationKey.Named("Uzun \\ Kısa"),
            Material,
            VariableValue.FromText("satır1\nsatır2\t\"alıntı\""),
            VariableValue.FromText("ters\\bölü"),
            fileVersion: 1);

        _journal.Begin(operation);
        _journal.WriteIntent(operation.OperationId, entry);

        var loaded = _journal.LoadOperation(Vault, operation.OperationId).Value;
        var loadedEntry = Assert.Single(loaded.Entries);

        Assert.Equal("satır1\nsatır2\t\"alıntı\"", loadedEntry.PreviousValue.ToStorageString());
        Assert.Equal("ters\\bölü", loadedEntry.AppliedValue.ToStorageString());
        Assert.Equal("a\"b.sldprt", loadedEntry.File.FileName);
        Assert.Equal("Uzun \\ Kısa", loadedEntry.Configuration.Name);
    }

    [Fact]
    public void OlmayanIslem_HataDonerCokmez()
    {
        var loaded = _journal.LoadOperation(Vault, Guid.NewGuid());
        Assert.True(loaded.IsFailure);
    }

    [Fact]
    public void BeginCagrilmadanWriteIntent_AcikHataVerir()
    {
        // Sessizce yanlış dosyaya yazmaktansa patlaması iyi: bu bir programlama hatasıdır.
        Assert.Throws<InvalidOperationException>(
            () => _journal.WriteIntent(Guid.NewGuid(), NewEntry("A", "B")));
    }

    // ------------------------------------------------------------ yardımcılar

    private Guid CompleteSimpleOperation()
    {
        var operation = NewOperation();
        var entry = NewEntry("A", "B");

        _journal.Begin(operation);
        _journal.WriteIntent(operation.OperationId, entry);
        entry.Result = EntryResult.Applied;
        _journal.WriteResult(operation.OperationId, entry);

        operation.Entries.Add(entry);
        operation.AppliedCount = 1;
        operation.FileCount = 1;
        operation.Outcome = OperationOutcomeKind.Success;
        _journal.Complete(operation);

        return operation.OperationId;
    }

    private static ApplyOperation NewOperation() =>
        new(Guid.NewGuid(), OperationType.Apply, DateTime.UtcNow, Vault, "MAKINA\\ayse", "ayse")
        {
            SourceWorkbookPath = "C:\\test\\mil.xlsx",
            ExportSessionId = Guid.NewGuid(),
        };

    private static ApplyOperationEntry NewEntry(string previous, string applied) =>
        new(
            new PdmFileIdentity(1, 10, "MIL-001.sldprt", "\\Parts"),
            ConfigurationKey.FileLevel,
            Material,
            VariableValue.FromText(previous),
            VariableValue.FromText(applied),
            fileVersion: 3);

    private string OperationFile(Guid operationId) =>
        Path.Combine(_root, "MakinaVault", "ops", operationId.ToString("N") + ".jsonl");
}
