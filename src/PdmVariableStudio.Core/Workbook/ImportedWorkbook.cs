using System;
using System.Collections.Generic;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.Core.Workbook;

/// <summary>
/// İçe aktarılan çalışma kitabındaki tek bir satır: kimliği, orijinal snapshot'ı ve
/// kullanıcının bıraktığı yeni değerler.
/// </summary>
public sealed class ImportedRow
{
    public ImportedRow(
        int exportRowId,
        PdmFileIdentity file,
        ConfigurationKey configuration,
        int fileVersion,
        IReadOnlyList<VariableValue> originalValues,
        IReadOnlyList<object?> rawExcelValues)
    {
        ExportRowId = exportRowId;
        File = file;
        Configuration = configuration;
        FileVersion = fileVersion;
        OriginalValues = originalValues;
        RawExcelValues = rawExcelValues;
        Issues = new List<ValidationIssue>();
    }

    public int ExportRowId { get; }

    public PdmFileIdentity File { get; }

    public ConfigurationKey Configuration { get; }

    public int FileVersion { get; }

    /// <summary><c>_Rows</c> sayfasından; dışa aktarım anındaki PDM değerleri.</summary>
    public IReadOnlyList<VariableValue> OriginalValues { get; }

    /// <summary>
    /// <c>Variables</c> sayfasından okunan HAM hücre değerleri. Tip dönüşümü burada değil,
    /// içe aktarma servisinde yapılır — çünkü hangi tipe çevrileceğini değişken tanımı
    /// belirler ve o bilgi <c>_Metadata</c>'dan gelir.
    /// </summary>
    public IReadOnlyList<object?> RawExcelValues { get; }

    /// <summary>Bu satıra özel bulgular. Doluysa satır uygulanmaz.</summary>
    public List<ValidationIssue> Issues { get; }

    /// <summary>Bu satır güvenilir mi. False ise hiçbir hücresi PDM'ye yazılmaz.</summary>
    public bool IsTrustworthy
    {
        get
        {
            foreach (var issue in Issues)
            {
                if (issue.Severity != IssueSeverity.Warning)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

/// <summary>
/// Okunmuş bir çalışma kitabının tamamı.
/// </summary>
/// <remarks>
/// <see cref="IsRejected"/> true ise <see cref="Rows"/> içeriğine bakılmadan durulur:
/// güvenilmeyen bir kitaptan tek bir hücre bile uygulanmaz.
/// </remarks>
public sealed class ImportedWorkbook
{
    public ImportedWorkbook(
        string path,
        int schemaVersion,
        Guid exportSessionId,
        DateTime exportedAtUtc,
        VaultIdentity vault,
        string sourceFolderPath,
        IReadOnlyList<PdmVariableDefinition> variables,
        IReadOnlyList<int> variableColumns,
        IReadOnlyList<ImportedRow> rows,
        IReadOnlyList<ValidationIssue> issues)
    {
        Path = path ?? string.Empty;
        SchemaVersion = schemaVersion;
        ExportSessionId = exportSessionId;
        ExportedAtUtc = exportedAtUtc;
        Vault = vault;
        SourceFolderPath = sourceFolderPath ?? string.Empty;
        Variables = variables;
        VariableColumns = variableColumns;
        Rows = rows;
        Issues = issues;
    }

    public string Path { get; }

    public int SchemaVersion { get; }

    public Guid ExportSessionId { get; }

    public DateTime ExportedAtUtc { get; }

    public VaultIdentity Vault { get; }

    public string SourceFolderPath { get; }

    /// <summary>Sütun sırası; <see cref="ImportedRow.RawExcelValues"/> bu sıraya uyar.</summary>
    public IReadOnlyList<PdmVariableDefinition> Variables { get; }

    /// <summary>
    /// Her değişkenin okunduğu gerçek sütun indisi. Sütun taşınmışsa bu, <c>_Metadata</c>'daki
    /// değerden farklıdır ve satırlar uyarı taşır.
    /// </summary>
    public IReadOnlyList<int> VariableColumns { get; }

    public IReadOnlyList<ImportedRow> Rows { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>Bir ölümcül bulgu varsa kitabın tamamı reddedilmiştir.</summary>
    public bool IsRejected
    {
        get
        {
            foreach (var issue in Issues)
            {
                if (issue.Severity == IssueSeverity.Fatal)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Okuma tamamen başarısız olduğunda üretilen boş sonuç.</summary>
    public static ImportedWorkbook Rejected(string path, ValidationIssue issue) =>
        new(
            path,
            schemaVersion: 0,
            exportSessionId: Guid.Empty,
            exportedAtUtc: default,
            vault: new VaultIdentity(null, null),
            sourceFolderPath: string.Empty,
            variables: Array.Empty<PdmVariableDefinition>(),
            variableColumns: Array.Empty<int>(),
            rows: Array.Empty<ImportedRow>(),
            issues: new[] { issue });
}
