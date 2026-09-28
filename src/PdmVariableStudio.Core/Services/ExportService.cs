using System;
using System.Collections.Generic;
using System.Threading;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;
using PdmVariableStudio.Core.Workbook;

namespace PdmVariableStudio.Core.Services;

/// <summary>Bir dışa aktarım isteğinin parametreleri.</summary>
/// <remarks>
/// Dosya listesi <b>hazır olarak</b> verilir; bu servis klasör taramaz. Kullanıcı listeyi
/// klasör ekleyerek, tek tek dosya seçerek ya da arama sonucundan ekleyerek kurar — üçü de
/// aynı listede toplanır. Taramayı buraya gömmek, "tek klasör" varsayımını servise
/// çakardı.
/// </remarks>
public sealed class ExportRequest
{
    public ExportRequest(IReadOnlyList<PdmFileIdentity> files, IReadOnlyList<PdmVariableDefinition> variables)
    {
        Files = files;
        Variables = variables;
    }

    /// <summary>İşleme alınacak dosyalar. Sıra korunur.</summary>
    public IReadOnlyList<PdmFileIdentity> Files { get; }

    /// <summary>Dışa aktarılacak değişkenler. Sütun sırasını da bu liste belirler.</summary>
    public IReadOnlyList<PdmVariableDefinition> Variables { get; }

    /// <summary>
    /// Kaynağın insan okur özeti; çalışma kitabı metadata'sına yazılır.
    /// Örn. <c>"\Parts\Mil (alt klasörler dahil)"</c> ya da <c>"3 kaynak"</c>.
    /// </summary>
    public string ScopeDescription { get; set; } = string.Empty;

    /// <summary>
    /// Tek bir klasörden geliniyorsa o klasör; karışık kaynakta 0. Yalnızca metadata için.
    /// </summary>
    public int PrimaryFolderId { get; set; }

    /// <summary>Metadata'ya yazılan süzgeç açıklaması.</summary>
    public string FileFilterDescription { get; set; } = "*.*";

    /// <summary>Metadata'ya yazılır; kaynak klasör taramasında alt klasörlere inildi mi.</summary>
    public bool IncludeSubfolders { get; set; }

    /// <summary>
    /// Dosya başına dışa aktarım kapsamı. Kapsamı olmayan dosya tüm konfigürasyonlarıyla
    /// aktarılır; montajdan gelen dosyada yalnızca montajın kullandığı konfigürasyonlar ve
    /// dosya düzeyi satır olur (bkz. <see cref="FileExportScope"/>).
    /// </summary>
    public IReadOnlyDictionary<PdmFileIdentity, FileExportScope> Scopes { get; set; } =
        new Dictionary<PdmFileIdentity, FileExportScope>();
}

/// <summary>Dışa aktarım akışı: klasör tara, değerleri oku, çalışma kitabı üret.</summary>
public sealed class ExportService
{
    private readonly IPdmVaultContext _vault;
    private readonly IPdmVariableReader _reader;
    private readonly IStudioLog _log;

    public ExportService(IPdmVaultContext vault, IPdmVariableReader reader, IStudioLog log)
    {
        _vault = vault;
        _reader = reader;
        _log = log;
    }

    /// <summary>
    /// Verilen dosyaların değerlerini okuyup bir <see cref="ExportSession"/> üretir. Dosyaya
    /// yazmaz — yazma çağıranın işi, böylece iptal ve hata yolları tek yerde toplanır.
    /// </summary>
    public OperationOutcome<ExportSession> BuildSession(
        ExportRequest request,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Variables.Count == 0)
        {
            return OperationOutcome<ExportSession>.Failure(
                IssueCode.Unexpected, "Dışa aktarılacak değişken seçilmemiş.");
        }

        if (request.Files.Count == 0)
        {
            return OperationOutcome<ExportSession>.Failure(
                IssueCode.Unexpected, "İşleme alınacak dosya seçilmemiş.");
        }

        var files = request.Files;
        _log.Info($"Dışa aktarım: {files.Count} dosya, {request.Variables.Count} değişken " +
                  $"(kaynak: {request.ScopeDescription}).");

        cancellationToken.ThrowIfCancellationRequested();

        var read = _reader.ReadSnapshots(
            files,
            request.Variables,
            new Progress<int>(count => progress?.Report(ExportProgress.Reading(count, files.Count))),
            cancellationToken);

        if (read.IsFailure)
        {
            return OperationOutcome<ExportSession>.Failure(read.Code, read.TechnicalDetail);
        }

        var rows = BuildRows(read.Value, request.Variables, request.Scopes, cancellationToken);

        var session = new ExportSession(
            Guid.NewGuid(),
            DateTime.UtcNow,
            _vault.Vault,
            request.PrimaryFolderId,
            request.ScopeDescription,
            request.IncludeSubfolders,
            request.FileFilterDescription,
            Environment.UserDomainName + "\\" + Environment.UserName,
            _vault.CurrentUserName,
            request.Variables,
            rows);

        _log.Info($"Dışa aktarım oturumu {session.ExportSessionId:D}: {rows.Count} satır, " +
                  $"{request.Variables.Count} değişken.");

        return OperationOutcome<ExportSession>.Success(session);
    }

    /// <summary>
    /// Her dosyanın her konfigürasyonu için bir satır üretir; kapsamı olan dosyada yalnızca
    /// kapsamdaki konfigürasyonlar için.
    /// </summary>
    /// <remarks>
    /// Konfigürasyonlar AYRI satırlar olur; "tüm konfigürasyonlar" davranışı kullanıcı açıkça
    /// istemeden kullanılmaz. Bir dosyanın üç konfigürasyonuna aynı anda yazmak, kullanıcının
    /// yalnızca birini değiştirmek istediği durumda sessiz veri kaybıdır.
    /// </remarks>
    private List<ExportRow> BuildRows(
        IReadOnlyList<PdmFileSnapshot> snapshots,
        IReadOnlyList<PdmVariableDefinition> variables,
        IReadOnlyDictionary<PdmFileIdentity, FileExportScope> scopes,
        CancellationToken cancellationToken)
    {
        var rows = new List<ExportRow>();
        var nextRowId = 1;

        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var configurations = snapshot.Configurations.Count > 0
                ? snapshot.Configurations
                : new[] { ConfigurationKey.FileLevel };

            scopes.TryGetValue(snapshot.Identity, out var scope);
            if (scope is not null)
            {
                LogMissingConfigurations(snapshot, scope, configurations);
            }

            foreach (var configuration in configurations)
            {
                if (scope is not null && !scope.Includes(configuration))
                {
                    continue;
                }

                var values = new List<VariableValue>(variables.Count);

                foreach (var variable in variables)
                {
                    var coordinate = new CellCoordinate(snapshot.Identity, configuration, variable.VariableId);
                    values.Add(snapshot.ValueAt(coordinate));
                }

                rows.Add(new ExportRow(
                    nextRowId++,
                    snapshot.Identity,
                    configuration,
                    snapshot.CurrentVersion,
                    values,
                    scope?.PlacementFor(configuration)));
            }
        }

        return rows;
    }

    /// <summary>
    /// Montajın kullandığı ama dosyada artık bulunmayan konfigürasyonları günlüğe yazar.
    /// </summary>
    /// <remarks>
    /// Montaj ağacı referansları okur; konfigürasyon parçada sonradan silinmiş ya da yeniden
    /// adlandırılmış olabilir. O satır üretilmez — var olmayan bir konfigürasyona yazılamaz —
    /// ama sessizce de kaybolmaz.
    /// </remarks>
    private void LogMissingConfigurations(
        PdmFileSnapshot snapshot,
        FileExportScope scope,
        IReadOnlyList<ConfigurationKey> available)
    {
        foreach (var wanted in scope.Configurations)
        {
            var found = false;
            foreach (var configuration in available)
            {
                if (configuration.Equals(wanted))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                _log.Warn($"{snapshot.Identity}: montajın kullandığı '{wanted}' konfigürasyonu " +
                          "dosyada bulunamadı; satırı üretilmedi.");
            }
        }
    }
}

/// <summary>Dışa aktarım ilerlemesi. Arayüz bunu doğrudan gösterir.</summary>
public sealed class ExportProgress
{
    private ExportProgress(string stage, int current, int total)
    {
        Stage = stage;
        Current = current;
        Total = total;
    }

    public string Stage { get; }

    public int Current { get; }

    public int Total { get; }

    public static ExportProgress Reading(int current, int total) => new("Değerler okunuyor", current, total);

    public static ExportProgress Writing() => new("Çalışma kitabı yazılıyor", 0, 0);

    public string Describe() =>
        Total > 0
            ? $"{Stage}: {Current} / {Total}"
            : Current > 0
                ? $"{Stage}: {Current}"
                : Stage;
}
