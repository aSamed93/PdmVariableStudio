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

namespace PdmVariableStudio.Tests.Apply;

/// <summary>
/// Uygulama akışının testleri: yeniden doğrulama, check-out politikası, kısmi başarısızlık
/// izolasyonu ve günlük sıralaması.
/// </summary>
public class ApplyServiceTests : IDisposable
{
    private static readonly PdmVariableDefinition Material =
        new(23, "Material", PdmVariableType.Text, displayName: "Malzeme");

    private static readonly PdmVariableDefinition Weight =
        new(41, "Weight", PdmVariableType.Float, displayName: "Ağırlık");

    private readonly string _journalRoot;
    private readonly FakeVault _vault = new();
    private readonly FakeLog _log = new();
    private readonly JsonlOperationJournal _journal;
    private readonly ApplyService _apply;

    public ApplyServiceTests()
    {
        _journalRoot = Path.Combine(Path.GetTempPath(), "pvs-journal-" + Guid.NewGuid().ToString("N"));
        _journal = new JsonlOperationJournal(_journalRoot);
        _apply = new ApplyService(_vault, _vault, _vault, _vault, _journal, _log);
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

    // ------------------------------------------------------------- mutlu yol

    [Fact]
    public void GuvenliDegisiklik_Uygulanir()
    {
        var file = _vault.AddFile(1, 10, "MIL-001.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("Ç1040"));

        var changeSet = BuildChangeSet((file, Material, Text("Ç1040"), Text("Ç4140")));
        var result = _apply.Apply(changeSet, Consented());

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationOutcomeKind.Success, result.Value.Outcome);
        Assert.Equal(1, result.Value.AppliedCount);
        Assert.Equal("Ç4140", _vault.GetValue(file, ConfigurationKey.FileLevel, Material).ToStorageString());
        Assert.Equal(ChangeStatus.Applied, changeSet.Cells[0].Status);
    }

    [Fact]
    public void CekiliOlmayanDosya_CheckOutEdilirVeCheckInEdilir()
    {
        var file = _vault.AddFile(1, 10, "MIL-001.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("A"));

        var changeSet = BuildChangeSet((file, Material, Text("A"), Text("B")));
        _apply.Apply(changeSet, Consented());

        Assert.Contains(1, _vault.CheckedOutFileIds);
        Assert.Contains(1, _vault.CheckedInFileIds);
    }

    [Fact]
    public void KullanicininKendiCheckOutu_IsSonundaCheckInEDILMEZ()
    {
        // Kullanıcı dosyayı başka bir iş için çekmiş olabilir; onu iade etmek onun işini bozar.
        var file = _vault.AddFile(1, 10, "MIL-001.sldprt").LockedByMe().Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("A"));

        var changeSet = BuildChangeSet((file, Material, Text("A"), Text("B")));
        var result = _apply.Apply(changeSet, Consented());

        Assert.Equal(1, result.Value.AppliedCount);
        Assert.Empty(_vault.CheckedOutFileIds);
        Assert.Empty(_vault.CheckedInFileIds);
    }

    [Fact]
    public void CheckInKapaliysa_DosyaCekiliBirakilir()
    {
        var file = _vault.AddFile(1, 10, "MIL-001.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("A"));

        var options = Consented();
        options.CheckInAfterApply = false;

        _apply.Apply(BuildChangeSet((file, Material, Text("A"), Text("B"))), options);

        Assert.Contains(1, _vault.CheckedOutFileIds);
        Assert.Empty(_vault.CheckedInFileIds);
    }

    // --------------------------------------------------------- check-out onayı

    [Fact]
    public void CheckOutOnayiYoksa_HicbirDosyaCekilmez()
    {
        var file = _vault.AddFile(1, 10, "MIL-001.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("A"));

        var options = new ApplyOptions { CheckoutConsent = false };
        var result = _apply.Apply(BuildChangeSet((file, Material, Text("A"), Text("B"))), options);

        Assert.True(result.IsFailure);
        Assert.Empty(_vault.CheckedOutFileIds);
        Assert.Equal("A", _vault.GetValue(file, ConfigurationKey.FileLevel, Material).ToStorageString());
    }

    // ----------------------------------------------- iyimser eşzamanlılık

    [Fact]
    public void OnizlemedenSonraDegerDegistiyse_YazilmazVeCakismayaDoner()
    {
        var file = _vault.AddFile(1, 10, "MIL-001.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("A"));

        var changeSet = BuildChangeSet((file, Material, Text("A"), Text("B")));

        // Önizleme oluştu, kullanıcı düşünürken başkası değeri değiştirdi.
        _vault.SimulateExternalChange(file, ConfigurationKey.FileLevel, Material, Text("C"));

        var result = _apply.Apply(changeSet, Consented());

        Assert.True(result.IsFailure);
        Assert.Equal(IssueCode.ValueChangedSincePreview, result.Code);

        // Başkasının değeri korundu.
        Assert.Equal("C", _vault.GetValue(file, ConfigurationKey.FileLevel, Material).ToStorageString());
        Assert.Equal(ChangeStatus.Conflict, changeSet.Cells[0].Status);
    }

    [Fact]
    public void BirHucreDegismisDigeriDegismemisse_YalnizcaDegismeyenUygulanir()
    {
        var file = _vault.AddFile(1, 10, "MIL-001.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("A"));
        _vault.SetValue(file, ConfigurationKey.FileLevel, Weight, Float(10m));

        var changeSet = BuildChangeSet(
            (file, Material, Text("A"), Text("B")),
            (file, Weight, Float(10m), Float(20m)));

        _vault.SimulateExternalChange(file, ConfigurationKey.FileLevel, Material, Text("X"));

        var result = _apply.Apply(changeSet, Consented());

        Assert.Equal(1, result.Value.AppliedCount);
        Assert.Equal("X", _vault.GetValue(file, ConfigurationKey.FileLevel, Material).ToStorageString());
        Assert.Equal("20", _vault.GetValue(file, ConfigurationKey.FileLevel, Weight).ToStorageString());
    }

    // ------------------------------------------------ kısmi başarısızlık izolasyonu

    [Fact]
    public void BirDosyaninYazmasiPatlarsa_DigerleriDevamEder()
    {
        var bozuk = _vault.AddFile(1, 10, "BOZUK.sldprt").Identity;
        var saglam = _vault.AddFile(2, 10, "SAGLAM.sldprt").Identity;

        _vault.SetValue(bozuk, ConfigurationKey.FileLevel, Material, Text("A"));
        _vault.SetValue(saglam, ConfigurationKey.FileLevel, Material, Text("A"));
        _vault.FailWriteForFileIds.Add(1);

        var changeSet = BuildChangeSet(
            (bozuk, Material, Text("A"), Text("B")),
            (saglam, Material, Text("A"), Text("B")));

        var result = _apply.Apply(changeSet, Consented());

        Assert.Equal(OperationOutcomeKind.Partial, result.Value.Outcome);
        Assert.Equal(1, result.Value.AppliedCount);
        Assert.Equal(1, result.Value.FailedCount);
        Assert.Equal("B", _vault.GetValue(saglam, ConfigurationKey.FileLevel, Material).ToStorageString());
    }

    [Fact]
    public void YazmaBasarisizsa_KendiCheckOutumuzGeriAlinir()
    {
        // Dosyada iz bırakmamak: kullanıcı başarısız bir işlemden sonra dosyayı çekili bulmamalı.
        var file = _vault.AddFile(1, 10, "BOZUK.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("A"));
        _vault.FailWriteForFileIds.Add(1);

        _apply.Apply(BuildChangeSet((file, Material, Text("A"), Text("B"))), Consented());

        Assert.Contains(1, _vault.UndoneCheckoutFileIds);
    }

    [Fact]
    public void YazmaBasarisizsaVeDosyaZatenBizdeCekiliyse_CheckOutGeriALINMAZ()
    {
        var file = _vault.AddFile(1, 10, "BOZUK.sldprt").LockedByMe().Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("A"));
        _vault.FailWriteForFileIds.Add(1);

        _apply.Apply(BuildChangeSet((file, Material, Text("A"), Text("B"))), Consented());

        Assert.Empty(_vault.UndoneCheckoutFileIds);
    }

    [Fact]
    public void CheckOutBasarisizsa_DosyaAtlanirDigerleriUygulanir()
    {
        var kilitli = _vault.AddFile(1, 10, "KILITLI.sldprt").Identity;
        var saglam = _vault.AddFile(2, 10, "SAGLAM.sldprt").Identity;

        _vault.SetValue(kilitli, ConfigurationKey.FileLevel, Material, Text("A"));
        _vault.SetValue(saglam, ConfigurationKey.FileLevel, Material, Text("A"));
        _vault.FailCheckoutForFileIds.Add(1);

        var result = _apply.Apply(
            BuildChangeSet((kilitli, Material, Text("A"), Text("B")), (saglam, Material, Text("A"), Text("B"))),
            Consented());

        Assert.Equal(1, result.Value.AppliedCount);
        Assert.Equal(1, result.Value.FailedCount);
        Assert.Equal("A", _vault.GetValue(kilitli, ConfigurationKey.FileLevel, Material).ToStorageString());
    }

    [Fact]
    public void CheckInBasarisizsa_DegerlerYineDeUygulanmisSayilir()
    {
        // Check-in bir operasyon sorunu, veri sorunu değil. Değer PDM'ye yazıldı.
        var file = _vault.AddFile(1, 10, "MIL-001.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("A"));
        _vault.FailCheckInForFileIds.Add(1);

        var result = _apply.Apply(BuildChangeSet((file, Material, Text("A"), Text("B"))), Consented());

        Assert.Equal(1, result.Value.AppliedCount);
        Assert.Equal("B", _vault.GetValue(file, ConfigurationKey.FileLevel, Material).ToStorageString());
    }

    // ------------------------------------------------------------- günlük

    [Fact]
    public void UygulamaSonrasi_GunlukteTamKayitVardir()
    {
        var file = _vault.AddFile(1, 10, "MIL-001.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("A"));

        var result = _apply.Apply(BuildChangeSet((file, Material, Text("A"), Text("B"))), Consented());

        var operations = _journal.ListOperations(_vault.Vault);
        Assert.Single(operations);
        Assert.Equal(result.Value.Operation.OperationId, operations[0].OperationId);

        var loaded = _journal.LoadOperation(_vault.Vault, operations[0].OperationId);
        Assert.True(loaded.IsSuccess);

        var entry = Assert.Single(loaded.Value.Entries);
        Assert.Equal(EntryResult.Applied, entry.Result);
        Assert.Equal("A", entry.PreviousValue.ToStorageString());
        Assert.Equal("B", entry.AppliedValue.ToStorageString());
    }

    [Fact]
    public void GunlukAcilamazsa_HicbirDegerYazilmaz()
    {
        // Günlüğe yazılamayan bir değişiklik geri alınamaz; işlem hiç başlatılmamalı.
        var file = _vault.AddFile(1, 10, "MIL-001.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("A"));

        var failing = new ApplyService(_vault, _vault, _vault, _vault, new FailingJournal(), _log);
        var result = failing.Apply(BuildChangeSet((file, Material, Text("A"), Text("B"))), Consented());

        Assert.True(result.IsFailure);
        Assert.Equal(IssueCode.JournalWriteFailed, result.Code);
        Assert.Equal("A", _vault.GetValue(file, ConfigurationKey.FileLevel, Material).ToStorageString());
        Assert.Equal(0, _vault.WriteCallCount);
    }

    [Fact]
    public void CheckOutBasarisizOlanDosya_GunlugeNiyetYazmaz()
    {
        // Yazma hiç denenmediği için "belirsiz" bir kayıt bırakmak yanlış olurdu:
        // Undo önizlemesi onu gereksiz yere PDM'ye sorardı.
        var file = _vault.AddFile(1, 10, "KILITLI.sldprt").Identity;
        _vault.SetValue(file, ConfigurationKey.FileLevel, Material, Text("A"));
        _vault.FailCheckoutForFileIds.Add(1);

        var result = _apply.Apply(BuildChangeSet((file, Material, Text("A"), Text("B"))), Consented());

        Assert.Empty(result.Value.Operation.Entries);
    }

    // ------------------------------------------------------------ yardımcılar

    private static VariableValue Text(string value) => VariableValue.FromText(value);

    private static VariableValue Float(decimal value) => VariableValue.FromFloat(value);

    private static ApplyOptions Consented() => new() { CheckoutConsent = true };

    private ChangeSet BuildChangeSet(
        params (PdmFileIdentity File, PdmVariableDefinition Variable, VariableValue Original, VariableValue Requested)[] specs)
    {
        var cells = new List<CellChange>();
        var plans = new Dictionary<PdmFileIdentity, FileApplyPlan>();

        foreach (var spec in specs)
        {
            var current = _vault.GetValue(spec.File, ConfigurationKey.FileLevel, spec.Variable);

            var cell = new CellChange(
                new CellCoordinate(spec.File, ConfigurationKey.FileLevel, spec.Variable.VariableId),
                spec.Variable,
                exportRowId: 1,
                spec.Original,
                current,
                spec.Requested);

            var checkout = _vault.GetCheckoutState(spec.File).ValueOr(CheckoutState.NotCheckedOut);
            ThreeWayDiffEngine.Evaluate(cell, new WriteContext(checkout, hasWritePermission: true));
            cells.Add(cell);

            if (!plans.TryGetValue(spec.File, out var plan))
            {
                var action = checkout.Status == CheckoutStatus.CheckedOutByMe
                    ? CheckoutAction.UseExisting
                    : CheckoutAction.CheckOut;

                plan = new FileApplyPlan(spec.File, checkout, action);
                plans[spec.File] = plan;
            }

            plan.Cells.Add(cell);
        }

        return new ChangeSet(
            _vault.Vault, Guid.NewGuid(), "test.xlsx", cells, plans.Values.ToList(),
            Array.Empty<ValidationIssue>());
    }

    /// <summary>Her yazma girişiminde başarısız olan günlük.</summary>
    private sealed class FailingJournal : PdmVariableStudio.Core.Abstractions.IOperationJournal
    {
        public OperationOutcome Begin(ApplyOperation operation) =>
            OperationOutcome.Failure(IssueCode.JournalWriteFailed, "Disk dolu (sahte).");

        public OperationOutcome WriteIntent(Guid operationId, ApplyOperationEntry entry) =>
            OperationOutcome.Failure(IssueCode.JournalWriteFailed);

        public OperationOutcome WriteResult(Guid operationId, ApplyOperationEntry entry) =>
            OperationOutcome.Failure(IssueCode.JournalWriteFailed);

        public OperationOutcome Complete(ApplyOperation operation) =>
            OperationOutcome.Failure(IssueCode.JournalWriteFailed);

        public OperationOutcome LinkUndo(Guid originalOperationId, Guid undoOperationId) =>
            OperationOutcome.Failure(IssueCode.JournalWriteFailed);

        public IReadOnlyList<ApplyOperation> ListOperations(VaultIdentity vault, int maxCount = 100) =>
            Array.Empty<ApplyOperation>();

        public OperationOutcome<ApplyOperation> LoadOperation(VaultIdentity vault, Guid operationId) =>
            OperationOutcome<ApplyOperation>.Failure(IssueCode.JournalWriteFailed);
    }
}
