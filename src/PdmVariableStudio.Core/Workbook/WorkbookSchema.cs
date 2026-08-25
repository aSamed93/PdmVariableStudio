using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PdmVariableStudio.Core.Domain;

namespace PdmVariableStudio.Core.Workbook;

/// <summary>
/// Çalışma kitabı sözleşmesi: sayfa adları, sütun düzeni, sürüm ve bütünlük damgaları.
/// </summary>
/// <remarks>
/// Çalışma kitabı bizim kontrol ettiğimiz, sürümlenmiş bir formattır. İçe aktarma yalnızca
/// sütun başlıklarına güvenmez: eşleme <c>_Metadata</c> sayfasındaki
/// <c>VariableId - ColumnIndex</c> tablosundan, satır kimliği <c>_Rows</c> sayfasından gelir.
/// </remarks>
public static class WorkbookSchema
{
    /// <summary>
    /// Bu eklentinin ürettiği ve okuyabildiği şema sürümü.
    /// </summary>
    /// <remarks>
    /// Daha DÜŞÜK bir sürüm görülürse göç denenir. Daha YÜKSEK bir sürüm görülürse dosya
    /// reddedilir ve hiçbir işlem yapılmaz — tanımadığımız bir düzeni yorumlamaya çalışmak,
    /// yanlış dosyaya yazmanın en kısa yoludur.
    /// </remarks>
    public const int CurrentVersion = 1;

    /// <summary>Göç ile okunabilen en eski sürüm.</summary>
    public const int MinimumSupportedVersion = 1;

    // ------------------------------------------------------------------ sayfa adları

    public const string VariablesSheet = "Variables";
    public const string MetadataSheet = "_Metadata";
    public const string RowsSheet = "_Rows";

    // ------------------------------------- Variables sayfası (1 tabanlı sütun indisleri)

    public const int ColExportRowId = 1;
    public const int ColFileName = 2;
    public const int ColRelativePath = 3;
    public const int ColConfiguration = 4;

    /// <summary>İlk değişken sütununun indisi. Öncesindeki sütunlar teknik ve kilitlidir.</summary>
    public const int FirstVariableColumn = 5;

    public const string HeaderExportRowId = "#";
    public const string HeaderFileName = "Dosya Adı";
    public const string HeaderRelativePath = "Klasör";
    public const string HeaderConfiguration = "Konfigürasyon";

    // --------------------------------------------------------------- _Rows sayfası

    public const int RowsColExportRowId = 1;
    public const int RowsColFileId = 2;
    public const int RowsColFolderId = 3;
    public const int RowsColConfiguration = 4;
    public const int RowsColFileVersion = 5;
    public const int RowsColIsSolidWorks = 6;
    public const int RowsColFingerprint = 7;

    /// <summary>Orijinal değerlerin başladığı sütun; sıra <c>_Metadata</c>'daki tanım sırasıdır.</summary>
    public const int RowsFirstValueColumn = 8;

    // ------------------------------------------------------------ _Metadata anahtarları

    public const string KeySchemaVersion = "SchemaVersion";
    public const string KeyProductVersion = "ProductVersion";
    public const string KeyExportSessionId = "ExportSessionId";
    public const string KeyExportedAtUtc = "ExportedAtUtc";
    public const string KeyExportedByWindowsUser = "ExportedByWindowsUser";
    public const string KeyExportedByPdmUser = "ExportedByPdmUser";
    public const string KeyVaultName = "VaultName";
    public const string KeyVaultRootPath = "VaultRootPath";
    public const string KeyVaultDatabase = "VaultDatabase";
    public const string KeySourceFolderId = "SourceFolderId";
    public const string KeySourceFolderPath = "SourceFolderPath";
    public const string KeyIncludeSubfolders = "IncludeSubfolders";
    public const string KeyFileFilter = "FileFilter";
    public const string KeyRowCount = "RowCount";
    public const string KeyMetadataChecksum = "MetadataChecksum";

    /// <summary>Anahtar/değer bölümünün bittiğini, değişken tablosunun başladığını gösterir.</summary>
    public const string VariableTableMarker = "[Variables]";

    // ------------------------------------------------------------------ bütünlük

    /// <summary>
    /// <c>_Metadata</c> sayfasının bütünlük damgası: anahtar/değer çiftleri (damganın kendisi
    /// hariç) ve değişken tanımları üzerinden hesaplanır.
    /// </summary>
    /// <remarks>
    /// Bu bir kriptografik imza DEĞİLDİR ve kötü niyetli bir değişikliği engellemez — anahtar
    /// dosyada olurdu, saklanacak yeri yok. Amacı KAZARA bozulmayı yakalamak: kullanıcının
    /// gizli sayfayı gösterip bir hücreyi silmesi ya da dosyayı başka bir araçtan geçirmesi.
    /// </remarks>
    public static string ComputeMetadataChecksum(
        IEnumerable<KeyValuePair<string, string>> metadata,
        IEnumerable<PdmVariableDefinition> variables,
        IEnumerable<int> columnIndexes)
    {
        var builder = new StringBuilder();

        foreach (var pair in metadata)
        {
            if (string.Equals(pair.Key, KeyMetadataChecksum, StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
        }

        using var columnEnumerator = columnIndexes.GetEnumerator();
        foreach (var variable in variables)
        {
            var column = columnEnumerator.MoveNext() ? columnEnumerator.Current : 0;

            builder.Append(variable.VariableId.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(variable.Name)
                .Append('|').Append((int)variable.DataType)
                .Append('|').Append(column.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(variable.IsMandatory ? '1' : '0')
                .Append('|').Append(variable.IsReadOnly ? '1' : '0')
                .Append('\n');
        }

        return Hash(builder.ToString());
    }

    /// <summary>
    /// Tek bir satırın bütünlük damgası: kimlik alanları ve orijinal değerler üzerinden.
    /// </summary>
    /// <remarks>
    /// Damga tutmuyorsa satırın kimliği ya da orijinal snapshot'ı değişmiş demektir. O satır
    /// uygulanmaz: yanlış dosyaya yazma riski, bir satırı atlamanın maliyetinden büyüktür.
    /// Oturum kimliği de damgaya giriyor ki bir çalışma kitabındaki satır başka bir kitaba
    /// kopyalandığında damga tutmasın.
    /// </remarks>
    public static string ComputeRowFingerprint(
        Guid exportSessionId,
        int exportRowId,
        int fileId,
        int folderId,
        ConfigurationKey configuration,
        IEnumerable<VariableValue> originalValues)
    {
        var builder = new StringBuilder();

        builder.Append(exportSessionId.ToString("N")).Append('|')
            .Append(exportRowId.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(fileId.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(folderId.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(configuration.ToStorageString()).Append('|');

        foreach (var value in originalValues)
        {
            // Uzunluk öneki: "ab" + "c" ile "a" + "bc" ayrımını korur.
            var stored = value?.ToStorageString() ?? string.Empty;
            builder.Append(stored.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(stored).Append('|');
        }

        return Hash(builder.ToString());
    }

    private static string Hash(string content)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));

        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    /// <summary>Sabit zamanlı olması gerekmiyor; yalnızca kaza tespiti için.</summary>
    public static bool ChecksumMatches(string? expected, string? actual) =>
        string.Equals(expected ?? string.Empty, actual ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
