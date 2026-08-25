using System;

namespace PdmVariableStudio.Core.Domain;

/// <summary>
/// Bir PDM dosyasının kararlı kimliği.
/// </summary>
/// <remarks>
/// Eşitlik YALNIZCA <see cref="FileId"/> ve <see cref="FolderId"/> üzerinden hesaplanır.
/// <see cref="FileName"/> ve <see cref="RelativePath"/> insan okusun diye taşınır; dosya
/// yeniden adlandırılmış ya da taşınmış olabileceği için eşleştirmede KULLANILMAZ.
///
/// FolderId de kimliğe dahil: PDM'de bir dosya birden fazla klasörde paylaşılmış (shared)
/// olabilir ve değişken değerleri dosya düzeyinde olsa da yetki ile check-out klasör
/// bağlamına bağlıdır.
/// </remarks>
public sealed class PdmFileIdentity : IEquatable<PdmFileIdentity>
{
    private readonly bool? _isSolidWorksFile;

    /// <param name="isSolidWorksFile">
    /// Dosyanın SOLIDWORKS dosyası olup olmadığı. <c>null</c> ise
    /// <paramref name="fileName"/> uzantısından çıkarılır. Çalışma kitabından okurken açıkça
    /// verilir: oradaki bilgi dışa aktarım anında saklanmıştır ve kullanıcının değiştirebildiği
    /// dosya adı sütununa güvenmemek gerekir.
    /// </param>
    public PdmFileIdentity(
        int fileId,
        int folderId,
        string? fileName = null,
        string? relativePath = null,
        bool? isSolidWorksFile = null)
    {
        FileId = fileId;
        FolderId = folderId;
        FileName = fileName ?? string.Empty;
        RelativePath = relativePath ?? string.Empty;
        _isSolidWorksFile = isSolidWorksFile;
    }

    public int FileId { get; }

    public int FolderId { get; }

    /// <summary>Yalnızca gösterim için. Eşleştirmede kullanılmaz.</summary>
    public string FileName { get; }

    /// <summary>Vault köküne göre klasör yolu. Yalnızca gösterim için.</summary>
    public string RelativePath { get; }

    /// <summary>
    /// SOLIDWORKS dosyası mı. Konfigürasyon dizesinin PDM'ye nasıl gönderileceğini belirler
    /// (bkz. <see cref="ConfigurationKey.ToPdmString"/>).
    /// </summary>
    public bool IsSolidWorksFile
    {
        get
        {
            if (_isSolidWorksFile.HasValue)
            {
                return _isSolidWorksFile.Value;
            }

            var name = FileName;
            if (name.Length < 4)
            {
                return false;
            }

            return name.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".slddrw", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool Equals(PdmFileIdentity? other) =>
        other is not null && FileId == other.FileId && FolderId == other.FolderId;

    public override bool Equals(object? obj) => Equals(obj as PdmFileIdentity);

    public override int GetHashCode() => unchecked((FileId * 397) ^ FolderId);

    public override string ToString() => $"{FileName} (#{FileId}/{FolderId})";
}

/// <summary>
/// Sistemin birincil anahtarı: bir kart değerinin tam adresi.
/// </summary>
/// <remarks>
/// Kimlik "dosya + değişken" DEĞİL, "dosya + klasör + konfigürasyon + değişken".
/// Konfigürasyonu dışarıda bırakmak, konfigürasyonlu SOLIDWORKS dosyalarında farklı değerleri
/// aynı hücre sanmaya yol açardı.
/// </remarks>
public readonly struct CellCoordinate : IEquatable<CellCoordinate>
{
    public CellCoordinate(PdmFileIdentity file, ConfigurationKey configuration, int variableId)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
        Configuration = configuration;
        VariableId = variableId;
    }

    public PdmFileIdentity File { get; }

    public ConfigurationKey Configuration { get; }

    public int VariableId { get; }

    public bool Equals(CellCoordinate other) =>
        File.Equals(other.File)
        && Configuration.Equals(other.Configuration)
        && VariableId == other.VariableId;

    public override bool Equals(object? obj) => obj is CellCoordinate other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = File.GetHashCode();
            hash = (hash * 397) ^ Configuration.GetHashCode();
            hash = (hash * 397) ^ VariableId;
            return hash;
        }
    }

    public override string ToString() => $"{File.FileName}[{Configuration}].{VariableId}";
}
