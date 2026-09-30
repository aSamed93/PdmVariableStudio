using System;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Localization;

namespace PdmVariableStudio.App.ViewModels;

/// <summary>
/// "İşleme alınacak dosyalar" listesindeki bir satır.
/// </summary>
/// <remarks>
/// Kullanıcı listeyi üç kaynaktan kurabiliyor (klasör, tek tek dosya, arama) ve hepsi aynı
/// listede toplanıyor. <see cref="SourceText"/> bir dosyanın oraya nasıl geldiğini gösterir —
/// otuz dosyalık bir listede "bu nereden geldi" sorusu yoksa liste güvenilmez olur.
/// </remarks>
internal sealed class FileRowViewModel : ObservableObject
{
    private bool _isSelected;
    private FileExportScope? _scope;
    private string _sourceDetail;

    public FileRowViewModel(PdmFileIdentity file, FileSourceKind source, FileExportScope? scope = null, string? sourceDetail = null)
    {
        File = file;
        Source = source;
        _scope = scope;
        _sourceDetail = sourceDetail ?? string.Empty;
    }

    public PdmFileIdentity File { get; }

    public FileSourceKind Source { get; }

    /// <summary>
    /// Dışa aktarım kapsamı. Montajdan gelen dosyada yalnızca montajın kullandığı
    /// konfigürasyonlar; <c>null</c> ise tüm konfigürasyonlar.
    /// </summary>
    /// <remarks>
    /// Aynı dosya sonradan klasörden ya da aramadan da eklenirse kapsam kaldırılır: kullanıcı
    /// artık dosyanın tamamını istiyor demektir. İki montajdan eklenirse kapsamlar birleşir.
    /// </remarks>
    public FileExportScope? Scope
    {
        get => _scope;
        set => Set(ref _scope, value);
    }

    /// <summary>Kaynak sütununun ipucu: dosyanın hangi montajdan geldiği.</summary>
    public string SourceDetail
    {
        get => _sourceDetail;
        set
        {
            if (Set(ref _sourceDetail, value ?? string.Empty))
            {
                Raise(nameof(SourceToolTip));
            }
        }
    }

    /// <summary>Boş ipucu WPF'te boş bir kutu gösteriyor; o yüzden null.</summary>
    public string? SourceToolTip => _sourceDetail.Length > 0 ? _sourceDetail : null;

    /// <summary>Listede işaretli mi. "Seçilenleri sil" bunu kullanır.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public string FileName => File.FileName;

    public string RelativePath => File.RelativePath;

    public string TypeText
    {
        get
        {
            var name = File.FileName;

            if (name.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
            {
                return Loc.T("Parça", "Part");
            }

            if (name.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
            {
                return Loc.T("Montaj", "Assembly");
            }

            if (name.EndsWith(".slddrw", StringComparison.OrdinalIgnoreCase))
            {
                return Loc.T("Teknik resim", "Drawing");
            }

            var dot = name.LastIndexOf('.');
            return dot > 0 && dot < name.Length - 1
                ? name.Substring(dot + 1).ToUpperInvariant()
                : Loc.T("Dosya", "File");
        }
    }

    public string SourceText => Source switch
    {
        FileSourceKind.Folder => Loc.T("Klasör", "Folder"),
        FileSourceKind.File => Loc.T("Dosya seçimi", "File selection"),
        FileSourceKind.Assembly => Loc.T("Montaj", "Assembly"),
        _ => Loc.T("Arama", "Search"),
    };
}
