using System;
using System.Collections.Generic;
using System.Threading;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.Tests.Fakes;

/// <summary>
/// Bellekte yaşayan sahte bir vault. PDM istemcisi olmadan tüm uygulama ve geri alma
/// senaryolarını çalıştırmayı sağlar.
/// </summary>
/// <remarks>
/// Gerçek PDM adaptörünün yaptığı her şeyi taklit eder: değer okuma/yazma, check-out durumu,
/// yetki, ve programlanabilir hatalar. Testler "başkası dosyayı çekti", "yazma patladı",
/// "değer önizlemeden sonra değişti" gibi durumları buradan kurar.
/// </remarks>
internal sealed class FakeVault : IPdmVaultContext, IPdmVariableReader, IPdmVariableWriter, IPdmCheckoutService
{
    private readonly Dictionary<CellCoordinate, VariableValue> _values = new();
    private readonly Dictionary<PdmFileIdentity, FakeFile> _files = new();

    public FakeVault(string vaultName = "MakinaVault")
    {
        Vault = new VaultIdentity(vaultName, "C:\\" + vaultName, vaultName + "Db");
    }

    public VaultIdentity Vault { get; }

    public string CurrentUserName { get; set; } = "ayse";

    // ---- programlanabilir hatalar ----

    /// <summary>Bu dosyalarda yazma başarısız olur.</summary>
    public HashSet<int> FailWriteForFileIds { get; } = new();

    /// <summary>Bu dosyalarda check-out başarısız olur.</summary>
    public HashSet<int> FailCheckoutForFileIds { get; } = new();

    /// <summary>Bu dosyalarda check-in başarısız olur.</summary>
    public HashSet<int> FailCheckInForFileIds { get; } = new();

    // ---- gözlemlenebilir eylemler ----

    public List<int> CheckedOutFileIds { get; } = new();

    public List<int> CheckedInFileIds { get; } = new();

    public List<int> UndoneCheckoutFileIds { get; } = new();

    public int WriteCallCount { get; private set; }

    // ---- kurulum ----

    public FakeFile AddFile(int fileId, int folderId, string name, params ConfigurationKey[] configurations)
    {
        var identity = new PdmFileIdentity(fileId, folderId, name, "\\Test");
        var file = new FakeFile(identity, configurations.Length > 0
            ? new List<ConfigurationKey>(configurations)
            : new List<ConfigurationKey> { ConfigurationKey.FileLevel });

        _files[identity] = file;
        return file;
    }

    public void SetValue(PdmFileIdentity file, ConfigurationKey configuration, PdmVariableDefinition variable, VariableValue value) =>
        _values[new CellCoordinate(file, configuration, variable.VariableId)] = value;

    public VariableValue GetValue(PdmFileIdentity file, ConfigurationKey configuration, PdmVariableDefinition variable) =>
        _values.TryGetValue(new CellCoordinate(file, configuration, variable.VariableId), out var value)
            ? value
            : VariableValue.Empty;

    /// <summary>Başka bir kullanıcının değişikliğini taklit eder; günlüğe girmez.</summary>
    public void SimulateExternalChange(PdmFileIdentity file, ConfigurationKey configuration, PdmVariableDefinition variable, VariableValue value) =>
        SetValue(file, configuration, variable, value);

    public void RemoveFile(PdmFileIdentity file) => _files.Remove(file);

    // ---- IPdmVaultContext ----

    public OperationOutcome<IReadOnlyList<PdmVariableDefinition>> GetVariables() =>
        OperationOutcome<IReadOnlyList<PdmVariableDefinition>>.Success(Array.Empty<PdmVariableDefinition>());

    public OperationOutcome<string> GetFolderPath(int folderId) =>
        OperationOutcome<string>.Success("\\Test");

    // ---- IPdmVariableReader ----

    public OperationOutcome<IReadOnlyList<PdmFileSnapshot>> ReadSnapshots(
        IReadOnlyList<PdmFileIdentity> files,
        IReadOnlyList<PdmVariableDefinition> variables,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var snapshots = new List<PdmFileSnapshot>();

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var single = ReadSnapshot(files[i], variables);
            if (single.IsSuccess)
            {
                snapshots.Add(single.Value);
            }

            progress?.Report(i + 1);
        }

        return OperationOutcome<IReadOnlyList<PdmFileSnapshot>>.Success(snapshots);
    }

    public OperationOutcome<PdmFileSnapshot> ReadSnapshot(
        PdmFileIdentity file,
        IReadOnlyList<PdmVariableDefinition> variables)
    {
        if (!_files.TryGetValue(file, out var fake))
        {
            return OperationOutcome<PdmFileSnapshot>.Failure(IssueCode.FileNotFound, file.ToString());
        }

        var values = new Dictionary<CellCoordinate, VariableValue>();

        foreach (var configuration in fake.Configurations)
        {
            foreach (var variable in variables)
            {
                var coordinate = new CellCoordinate(file, configuration, variable.VariableId);
                values[coordinate] = _values.TryGetValue(coordinate, out var value) ? value : VariableValue.Empty;
            }
        }

        return OperationOutcome<PdmFileSnapshot>.Success(new PdmFileSnapshot(
            file,
            fake.Version,
            fake.Checkout,
            "Onaylandı",
            fake.Configurations,
            values,
            fake.HasWritePermission));
    }

    // ---- IPdmVariableWriter ----

    public OperationOutcome<IReadOnlyList<VariableWriteResult>> WriteValues(
        PdmFileIdentity file,
        IReadOnlyList<VariableWrite> writes)
    {
        WriteCallCount++;

        if (FailWriteForFileIds.Contains(file.FileId))
        {
            return OperationOutcome<IReadOnlyList<VariableWriteResult>>.Failure(
                IssueCode.FileExclusivelyOpen, "Sahte yazma hatası.");
        }

        if (!_files.TryGetValue(file, out var fake))
        {
            return OperationOutcome<IReadOnlyList<VariableWriteResult>>.Failure(
                IssueCode.FileNotFound, file.ToString());
        }

        // Gerçek adaptörde olduğu gibi: yazmadan önce dosyanın çekili olması beklenir.
        if (fake.Checkout.Status != CheckoutStatus.CheckedOutByMe)
        {
            return OperationOutcome<IReadOnlyList<VariableWriteResult>>.Failure(
                IssueCode.CheckoutRequired, file.ToString());
        }

        var results = new List<VariableWriteResult>(writes.Count);

        foreach (var write in writes)
        {
            SetValue(file, write.Configuration, write.Variable, write.Value);
            results.Add(new VariableWriteResult(write.Configuration, write.Variable.VariableId, succeeded: true));
        }

        return OperationOutcome<IReadOnlyList<VariableWriteResult>>.Success(results);
    }

    // ---- IPdmCheckoutService ----

    public OperationOutcome<CheckoutState> GetCheckoutState(PdmFileIdentity file) =>
        _files.TryGetValue(file, out var fake)
            ? OperationOutcome<CheckoutState>.Success(fake.Checkout)
            : OperationOutcome<CheckoutState>.Failure(IssueCode.FileNotFound);

    public CheckoutResult EnsureCheckedOut(PdmFileIdentity file)
    {
        if (!_files.TryGetValue(file, out var fake))
        {
            return CheckoutResult.Failed(IssueCode.FileNotFound);
        }

        if (fake.Checkout.Status == CheckoutStatus.CheckedOutByMe)
        {
            return CheckoutResult.AlreadyMine();
        }

        if (fake.Checkout.Status == CheckoutStatus.CheckedOutByOther)
        {
            return CheckoutResult.Failed(IssueCode.LockedByOtherUser, fake.Checkout.User);
        }

        if (FailCheckoutForFileIds.Contains(file.FileId))
        {
            return CheckoutResult.Failed(IssueCode.CheckoutFailed, "Sahte check-out hatası.");
        }

        fake.Checkout = CheckoutState.ByMe(Environment.MachineName);
        CheckedOutFileIds.Add(file.FileId);
        return CheckoutResult.CheckedOut();
    }

    public OperationOutcome CheckIn(PdmFileIdentity file, string comment)
    {
        if (FailCheckInForFileIds.Contains(file.FileId))
        {
            return OperationOutcome.Failure(IssueCode.CheckInFailed, "Sahte check-in hatası.");
        }

        if (_files.TryGetValue(file, out var fake))
        {
            fake.Checkout = CheckoutState.NotCheckedOut;
            fake.Version++;
        }

        CheckedInFileIds.Add(file.FileId);
        return OperationOutcome.Success();
    }

    public OperationOutcome UndoCheckout(PdmFileIdentity file)
    {
        if (_files.TryGetValue(file, out var fake))
        {
            fake.Checkout = CheckoutState.NotCheckedOut;
        }

        UndoneCheckoutFileIds.Add(file.FileId);
        return OperationOutcome.Success();
    }
}

internal sealed class FakeFile
{
    public FakeFile(PdmFileIdentity identity, List<ConfigurationKey> configurations)
    {
        Identity = identity;
        Configurations = configurations;
        Checkout = CheckoutState.NotCheckedOut;
        HasWritePermission = true;
        Version = 1;
    }

    public PdmFileIdentity Identity { get; }

    public List<ConfigurationKey> Configurations { get; }

    public CheckoutState Checkout { get; set; }

    public bool HasWritePermission { get; set; }

    public int Version { get; set; }

    public FakeFile LockedBy(string user)
    {
        Checkout = CheckoutState.ByOther(user, "PC-" + user);
        return this;
    }

    public FakeFile LockedByMe()
    {
        Checkout = CheckoutState.ByMe(Environment.MachineName);
        return this;
    }

    public FakeFile WithoutWritePermission()
    {
        HasWritePermission = false;
        return this;
    }
}

/// <summary>Testlerde günlüğü sessizce yutan kayıt tutucu.</summary>
internal sealed class FakeLog : IStudioLog
{
    public List<string> Messages { get; } = new();

    public string FilePath => "(test)";

    public void Info(string message) => Messages.Add("INFO " + message);

    public void Warn(string message) => Messages.Add("WARN " + message);

    public void Error(string message, Exception? exception = null) => Messages.Add("ERROR " + message);
}
