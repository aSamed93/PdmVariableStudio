using System;

namespace PdmVariableStudio.Core.Domain;

/// <summary>
/// Bir vault'u tanımlar. Çalışma kitabının üretildiği vault ile uygulandığı vault'un aynı
/// olmasını garanti etmek için kullanılır.
/// </summary>
/// <remarks>
/// PDM API'si vault için bir GUID vermiyor — <c>IEdmVault5</c>'ten <c>IEdmVault22</c>'ye kadar
/// hiçbir arayüzde böyle bir üye yok (interop üzerinde reflection ile doğrulandı). Bu yüzden
/// kimlik ad + kök yol + veritabanı adı üçlüsünden oluşuyor.
/// </remarks>
public sealed class VaultIdentity : IEquatable<VaultIdentity>
{
    private const char PathSeparator = '\\';

    public VaultIdentity(string? name, string? rootFolderPath, string? databaseName = null)
    {
        Name = name ?? string.Empty;
        RootFolderPath = rootFolderPath ?? string.Empty;
        DatabaseName = databaseName ?? string.Empty;
    }

    /// <summary>Vault görünen adı (<c>IEdmVault20.Name</c>).</summary>
    public string Name { get; }

    /// <summary>Yerel vault view kök yolu (<c>IEdmVault5.RootFolderPath</c>).</summary>
    public string RootFolderPath { get; }

    /// <summary>SQL veritabanı adı. Bilinmiyorsa boş; o durumda karşılaştırmaya girmez.</summary>
    public string DatabaseName { get; }

    public bool Equals(VaultIdentity? other)
    {
        if (other is null)
        {
            return false;
        }

        // Veritabanı adı iki tarafta da doluysa karşılaştırılır. Tek tarafta boş olması
        // "bilinmiyor" demektir ve eşleşmeyi bozmamalıdır: eski bir çalışma kitabı bu alanı
        // hiç taşımamış olabilir ve onu yanlışlıkla başka vault sanmak, doğru bir dosyayı
        // gereksiz yere reddetmek olurdu.
        var databaseMatches =
            DatabaseName.Length == 0 ||
            other.DatabaseName.Length == 0 ||
            string.Equals(DatabaseName, other.DatabaseName, StringComparison.OrdinalIgnoreCase);

        return databaseMatches
            && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizePath(RootFolderPath), NormalizePath(other.RootFolderPath), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => path.TrimEnd(PathSeparator);

    public override bool Equals(object? obj) => Equals(obj as VaultIdentity);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

    public override string ToString() => Name;
}
