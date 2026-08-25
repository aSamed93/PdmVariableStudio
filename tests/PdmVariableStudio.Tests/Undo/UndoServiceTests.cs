using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdmVariableStudio.Core.Diff;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Journal;
using PdmVariableStudio.Core.Results;
using PdmVariableStudio.Core.Services;
using PdmVariableStudio.Tests.Fakes;
using Xunit;

namespace PdmVariableStudio.Tests.Undo;

/// <summary>
/// Geri alma güvenliği. Bu dosyadaki testler ürünün en sert vaadini korur:
/// <b>geri alma, başka birinin değişikliğini asla yok etmez.</b>
/// </summary>
public class UndoServiceTests : IDisposable
{
    private static readonly PdmVariableDefinition Material =
        new(23, "Material", PdmVariableType.Text, displayName: "Malzeme");

    private readonly string _journalRoot;
    private readonly FakeVault _vault = new();
    private readonly FakeLog _log = new();
    private readonly JsonlOperationJournal _journal;
    private readonly ApplyService _apply;
    private readonly UndoService _undo;

    public UndoServiceTests()
    {
        _journalRoot = Path.Combine(Path.GetTempPath(), "pvs-undo-" + Guid.NewGuid().ToString("N"));
        _journal = new JsonlOperationJournal(_journalRoot);
        _apply = new ApplyService(_vault, _vault, _vault, _vault, _journal, _log);
        _undo = new UndoService(_vault, _vault, _journal, _apply, _log);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_journalRoot))
            {
                Directory.Delete(_journalRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    // ------------------------------------------------------------- güvenli geri alma

    [Fact]
    public void YazdigimizDegerHalaYerindeyse_GeriAlmaGuvenlidir()
    {
        var (file, operationId) = ApplyChange("A", "B");

        var preview = _undo.BuildPreview(operationId);

        Assert.True(preview.IsSuccess);
        Assert.Equal(1, preview.Value.SafeCount);

        var candidate = Assert.Single(preview.Value.Candidates);
        Assert.Equal(UndoStatus.SafeUndo, candidate.Status);
        Assert.Equal("B", candidate.CurrentValue.ToStorageString());
        Assert.Equal("A", candidate.ValueAfterUndo.ToStorageString());
    }

    [Fact]
    public void GuvenliGeriAlma_DegeriEskiHalineDondurur()
    {
        var (file, operationId) = ApplyChange("A", "B");

        var preview = _undo.BuildPreview(operationId).Value;
        var result = _undo.Undo(preview, new ApplyOptions { CheckoutConsent = true });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AppliedCount);
        Assert.Equal("A", _vault.GetValue(file, ConfigurationKey.FileLevel, Material).ToStorageString());
    }

    // ------------------------------------------------------------- ÇAKIŞMA

    [Fact]
    public void BizdenSonraBaskasiDegistirmisse_CAKISMA_VeYazmaYapilmaz()
    {
        // Ürünün en kritik davranışı. Biz A -> B yaptık, sonra Ayşe B -> C yaptı.
        // C -> A yazmak Ayşe'nin işini yok ederdi.
        var (file, operationId) = ApplyChange("A", "B");

        _vault.SimulateExternalChange(file, ConfigurationKey.FileLevel, Material, VariableValue.FromText("C"));

        var preview = _undo.BuildPreview(operationId).Value;

        Assert.Equal(0, preview.SafeCount);
        Assert.Equal(1, preview.ConflictCount);

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal(UndoStatus.Conflict, candidate.Status);
        Assert.False(candidate.CanUndo);
        Assert.False(candidate.IsSelected);

        // Geri almayı zorlasak bile yazılmaz.
        var result = _undo.Undo(preview, new ApplyOptions { CheckoutConsent = true });

        Assert.True(result.IsFailure);
        Assert.Equal("C", _vault.GetValue(file, ConfigurationKey.FileLevel, Material).ToStorageString());
    }

    [Fact]
    public void CakismaliVeGuvenliKayitlarBirlikteyse_YalnizcaGuvenliOlanGeriAlinir()
    {
        var weight = new PdmVariableDefinition(41, "Weight", PdmVariableType.Text, displayName: "Ağırlık");

        var file = _vault.AddFile(1, 10, "MIL-001.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, VariableValue.FromText("A"));
        _vault.SetValue(file, ConfigurationKey.FileLevel, weight, VariableValue.FromText("10"));

        var changeSet = BuildChangeSet(
            (file, Material, VariableValue.FromText("A"), VariableValue.FromText("B")),
            (file, weight, VariableValue.FromText("10"), VariableValue.FromText("20")));

        var operationId = _apply.Apply(changeSet, new ApplyOptions { CheckoutConsent = true })
            .Value.Operation.OperationId;

        // Yalnızca Malzeme'yi başkası değiştirdi.
        _vault.SimulateExternalChange(file, ConfigurationKey.FileLevel, Material, VariableValue.FromText("X"));

        var preview = _undo.BuildPreview(operationId).Value;
        Assert.Equal(1, preview.SafeCount);
        Assert.Equal(1, preview.ConflictCount);

        _undo.Undo(preview, new ApplyOptions { CheckoutConsent = true });

        Assert.Equal("X", _vault.GetValue(file, ConfigurationKey.FileLevel, Material).ToStorageString());
        Assert.Equal("10", _vault.GetValue(file, ConfigurationKey.FileLevel, weight).ToStorageString());
    }

    // ------------------------------------------------------- zaten geri alınmış

    [Fact]
    public void DegerZatenEskiHalindeyse_ZatenGeriAlinmisSayilir()
    {
        var (file, operationId) = ApplyChange("A", "B");

        _vault.SimulateExternalChange(file, ConfigurationKey.FileLevel, Material, VariableValue.FromText("A"));

        var preview = _undo.BuildPreview(operationId).Value;

        Assert.Equal(1, preview.AlreadyRevertedCount);
        Assert.Equal(UndoStatus.AlreadyReverted, preview.Candidates[0].Status);
        Assert.False(preview.Candidates[0].CanUndo);
    }

    // ------------------------------------------------------------- kullanılamaz

    [Fact]
    public void DosyaSilinmisse_KullanilamazOlarakIsaretlenir()
    {
        var (file, operationId) = ApplyChange("A", "B");
        _vault.RemoveFile(file);

        var preview = _undo.BuildPreview(operationId).Value;

        Assert.Equal(1, preview.UnavailableCount);
        Assert.Equal(IssueCode.FileNotFound, preview.Candidates[0].Reason);
    }

    [Fact]
    public void YetkiKalkmissa_KullanilamazOlarakIsaretlenir()
    {
        var (file, operationId) = ApplyChange("A", "B");

        _vault.AddFile(file.FileId, file.FolderId, file.FileName).WithoutWritePermission();
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, VariableValue.FromText("B"));

        var preview = _undo.BuildPreview(operationId).Value;

        Assert.Equal(IssueCode.PermissionDenied, preview.Candidates[0].Reason);
    }

    [Fact]
    public void KonfigurasyonKaldirilmissa_KullanilamazOlarakIsaretlenir()
    {
        var file = _vault.AddFile(1, 10, "MIL-001.sldprt", ConfigurationKey.Named("Uzun")).Identity;
        _vault.SetValue(file, ConfigurationKey.Named("Uzun"), Material, VariableValue.FromText("A"));

        var changeSet = BuildChangeSet(
            (file, Material, VariableValue.FromText("A"), VariableValue.FromText("B")),
            configuration: ConfigurationKey.Named("Uzun"));

        var operationId = _apply.Apply(changeSet, new ApplyOptions { CheckoutConsent = true })
            .Value.Operation.OperationId;

        // Konfigürasyon dosyadan kaldırıldı.
        _vault.RemoveFile(file);
        _vault.AddFile(1, 10, "MIL-001.sldprt", ConfigurationKey.Named("Kisa"));

        var preview = _undo.BuildPreview(operationId).Value;

        Assert.Equal(IssueCode.ConfigurationNotFound, preview.Candidates[0].Reason);
    }

    // -------------------------------------------------- yarım kalmış işlemler

    [Fact]
    public void SonucuYazilmamisKayit_PdmDegeriUymuyorsa_HicYazilmamisSayilir()
    {
        // Süreç yazma sırasında ölmüş: niyet var, sonuç yok. PDM'deki değer bizim
        // yazacağımızla uyuşmuyorsa yazma hiç gerçekleşmemiş demektir.
        var entry = MakeEntry(previous: "A", applied: "B", result: EntryResult.Unknown);
        var snapshot = Snapshot(current: "A");

        var candidate = UndoService.Evaluate(entry, snapshot);

        Assert.Equal(UndoStatus.Unavailable, candidate.Status);
        Assert.Equal(IssueCode.NeverApplied, candidate.Reason);
    }

    [Fact]
    public void SonucuYazilmamisKayit_PdmDegeriUyuyorsa_UygulanmisSayilir()
    {
        // Değer bizim yazdığımızla aynı: yazma gerçekleşmiş, yalnızca sonuç satırı
        // diske inememişti. Geri alma güvenli.
        var entry = MakeEntry(previous: "A", applied: "B", result: EntryResult.Unknown);
        var snapshot = Snapshot(current: "B");

        var candidate = UndoService.Evaluate(entry, snapshot);

        Assert.Equal(UndoStatus.SafeUndo, candidate.Status);
    }

    [Fact]
    public void BasarisizKayit_GeriAlinmaz()
    {
        var entry = MakeEntry(previous: "A", applied: "B", result: EntryResult.Failed);
        var candidate = UndoService.Evaluate(entry, Snapshot(current: "A"));

        Assert.Equal(UndoStatus.Unavailable, candidate.Status);
        Assert.Equal(IssueCode.NotApplied, candidate.Reason);
    }

    // ------------------------------------------------------ geri almanın kaydı

    [Fact]
    public void GeriAlmaIslemi_GunlugeYeniIslemOlarakYazilir()
    {
        var (_, operationId) = ApplyChange("A", "B");

        var preview = _undo.BuildPreview(operationId).Value;
        var result = _undo.Undo(preview, new ApplyOptions { CheckoutConsent = true });

        var operations = _journal.ListOperations(_vault.Vault);

        Assert.Equal(2, operations.Count);
        Assert.Equal(OperationType.Undo, operations[0].Type);
        Assert.Equal(operationId, operations[0].UndoesOperationId);
        Assert.Equal(result.Value.Operation.OperationId, operations[0].OperationId);
    }

    [Fact]
    public void GeriAlinanIslem_GeriAlindiOlarakIsaretlenir()
    {
        var (_, operationId) = ApplyChange("A", "B");

        var preview = _undo.BuildPreview(operationId).Value;
        var undoResult = _undo.Undo(preview, new ApplyOptions { CheckoutConsent = true });

        var original = _journal.ListOperations(_vault.Vault).Single(o => o.OperationId == operationId);

        Assert.True(original.IsUndone);
        Assert.Equal(undoResult.Value.Operation.OperationId, original.UndoneByOperationId);
        Assert.False(original.IsUndoCandidate);
    }

    [Fact]
    public void ZatenGeriAlinmisIslem_TekrarGeriAlinamaz()
    {
        var (_, operationId) = ApplyChange("A", "B");

        var preview = _undo.BuildPreview(operationId).Value;
        _undo.Undo(preview, new ApplyOptions { CheckoutConsent = true });

        var second = _undo.BuildPreview(operationId);

        Assert.True(second.IsFailure);
        Assert.Equal(IssueCode.AlreadyReverted, second.Code);
    }

    // ------------------------------------------------------------ yardımcılar

    private (PdmFileIdentity File, Guid OperationId) ApplyChange(string from, string to)
    {
        var file = _vault.AddFile(1, 10, "MIL-001.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, VariableValue.FromText(from));

        var changeSet = BuildChangeSet(
            (file, Material, VariableValue.FromText(from), VariableValue.FromText(to)));

        var result = _apply.Apply(changeSet, new ApplyOptions { CheckoutConsent = true });
        Assert.Equal(1, result.Value.AppliedCount);

        return (file, result.Value.Operation.OperationId);
    }

    private ChangeSet BuildChangeSet(
        params (PdmFileIdentity File, PdmVariableDefinition Variable, VariableValue Original, VariableValue Requested)[] specs)
        => BuildChangeSet(ConfigurationKey.FileLevel, specs);

    private ChangeSet BuildChangeSet(
        (PdmFileIdentity File, PdmVariableDefinition Variable, VariableValue Original, VariableValue Requested) spec,
        ConfigurationKey configuration)
        => BuildChangeSet(configuration, new[] { spec });

    private ChangeSet BuildChangeSet(
        ConfigurationKey configuration,
        (PdmFileIdentity File, PdmVariableDefinition Variable, VariableValue Original, VariableValue Requested)[] specs)
    {
        var cells = new List<CellChange>();
        var plans = new Dictionary<PdmFileIdentity, FileApplyPlan>();

        foreach (var spec in specs)
        {
            var current = _vault.GetValue(spec.File, configuration, spec.Variable);

            var cell = new CellChange(
                new CellCoordinate(spec.File, configuration, spec.Variable.VariableId),
                spec.Variable,
                exportRowId: 1,
                spec.Original,
                current,
                spec.Requested);

            ThreeWayDiffEngine.Evaluate(cell, WriteContext.Writable);
            cells.Add(cell);

            if (!plans.TryGetValue(spec.File, out var plan))
            {
                plan = new FileApplyPlan(spec.File, CheckoutState.NotCheckedOut, CheckoutAction.CheckOut);
                plans[spec.File] = plan;
            }

            plan.Cells.Add(cell);
        }

        return new ChangeSet(
            _vault.Vault, Guid.NewGuid(), "test.xlsx", cells, plans.Values.ToList(),
            Array.Empty<ValidationIssue>());
    }

    private static ApplyOperationEntry MakeEntry(string previous, string applied, EntryResult result)
    {
        var entry = new ApplyOperationEntry(
            new PdmFileIdentity(1, 10, "MIL-001.sldprt"),
            ConfigurationKey.FileLevel,
            Material,
            VariableValue.FromText(previous),
            VariableValue.FromText(applied),
            fileVersion: 1);

        entry.Result = result;
        return entry;
    }

    private static PdmFileSnapshot Snapshot(string current)
    {
        var file = new PdmFileIdentity(1, 10, "MIL-001.sldprt");
        var values = new Dictionary<CellCoordinate, VariableValue>
        {
            [new CellCoordinate(file, ConfigurationKey.FileLevel, Material.VariableId)] =
                VariableValue.FromText(current),
        };

        return new PdmFileSnapshot(
            file,
            currentVersion: 1,
            CheckoutState.NotCheckedOut,
            "Onaylandı",
            new[] { ConfigurationKey.FileLevel },
            values,
            hasWritePermission: true);
    }
}
