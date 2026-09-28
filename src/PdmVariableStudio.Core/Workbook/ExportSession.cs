using System;
using System.Collections.Generic;
using PdmVariableStudio.Core.Domain;

namespace PdmVariableStudio.Core.Workbook;

/// <summary>Çalışma kitabındaki tek bir satır: bir dosyanın bir konfigürasyonu.</summary>
public sealed class ExportRow
{
    public ExportRow(
        int exportRowId,
        PdmFileIdentity file,
        ConfigurationKey configuration,
        int fileVersion,
        IReadOnlyList<VariableValue> values,
        AssemblyPlacement? placement = null)
    {
        ExportRowId = exportRowId;
        File = file;
        Configuration = configuration;
        FileVersion = fileVersion;
        Values = values;
        Placement = placement;
    }

    /// <summary>
    /// Satırın kararlı numarası. Kullanıcıya görünen tek teknik sütun.
    /// </summary>
    /// <remarks>
    /// Gizli sütun yerine görünür bir numara kullanılıyor: gizli sütunlar kopyala/yapıştır ve
    /// sıralama sırasında sessizce bozuluyor, görünür bir numara bozulduğunda kullanıcı fark
    /// ediyor. Satır İNDİSİ değil, satır NUMARASI — kullanıcı satırları sıralasa da eşleme
    /// bozulmaz.
    /// </remarks>
    public int ExportRowId { get; }

    public PdmFileIdentity File { get; }

    public ConfigurationKey Configuration { get; }

    /// <summary>Dışa aktarım anındaki dosya sürümü. Teşhis için; çakışma kararı değer bazlıdır.</summary>
    public int FileVersion { get; }

    /// <summary>Değerler, <see cref="ExportSession.Variables"/> ile AYNI sırada.</summary>
    public IReadOnlyList<VariableValue> Values { get; }

    /// <summary>
    /// Satır montajdan geldiyse montajdaki yeri; değilse <c>null</c>. Yalnızca gösterim —
    /// <c>_Rows</c>'a ve damgaya girmez, içe aktarımda okunmaz.
    /// </summary>
    public AssemblyPlacement? Placement { get; }
}

/// <summary>
/// Bir dışa aktarımın tam içeriği. <see cref="WorkbookWriter"/> bunu <c>.xlsx</c> yazar,
/// <see cref="WorkbookReader"/> geri okuduğunda <see cref="ImportedWorkbook"/> üretir.
/// </summary>
public sealed class ExportSession
{
    public ExportSession(
        Guid exportSessionId,
        DateTime exportedAtUtc,
        VaultIdentity vault,
        int sourceFolderId,
        string sourceFolderPath,
        bool includeSubfolders,
        string fileFilter,
        string windowsUser,
        string pdmUser,
        IReadOnlyList<PdmVariableDefinition> variables,
        IReadOnlyList<ExportRow> rows)
    {
        ExportSessionId = exportSessionId;
        ExportedAtUtc = exportedAtUtc;
        Vault = vault;
        SourceFolderId = sourceFolderId;
        SourceFolderPath = sourceFolderPath ?? string.Empty;
        IncludeSubfolders = includeSubfolders;
        FileFilter = fileFilter ?? string.Empty;
        WindowsUser = windowsUser ?? string.Empty;
        PdmUser = pdmUser ?? string.Empty;
        Variables = variables;
        Rows = rows;
    }

    public Guid ExportSessionId { get; }

    public DateTime ExportedAtUtc { get; }

    public VaultIdentity Vault { get; }

    public int SourceFolderId { get; }

    public string SourceFolderPath { get; }

    public bool IncludeSubfolders { get; }

    public string FileFilter { get; }

    public string WindowsUser { get; }

    public string PdmUser { get; }

    /// <summary>Sütun sırasını belirler. <see cref="ExportRow.Values"/> bu sıraya uyar.</summary>
    public IReadOnlyList<PdmVariableDefinition> Variables { get; }

    public IReadOnlyList<ExportRow> Rows { get; }

    /// <summary>
    /// En az bir satır montajdan geldiyse <c>true</c>: çalışma kitabına montaj bilgi sütunları
    /// eklenir (bkz. <see cref="WorkbookSchema.AssemblyInfoHeaders"/>).
    /// </summary>
    public bool HasAssemblyInfo
    {
        get
        {
            foreach (var row in Rows)
            {
                if (row.Placement is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>İlk değişken sütunu: montaj bilgi sütunları varsa onların ardından.</summary>
    public int FirstVariableColumn =>
        WorkbookSchema.FirstVariableColumn + (HasAssemblyInfo ? WorkbookSchema.AssemblyInfoHeaders.Count : 0);
}
