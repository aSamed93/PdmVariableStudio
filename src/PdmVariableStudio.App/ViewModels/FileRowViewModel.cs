using System;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Domain;

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

    public FileRowViewModel(PdmFileIdentity file, FileSourceKind source)
    {
        File = file;
        Source = source;
    }

    public PdmFileIdentity File { get; }

    public FileSourceKind Source { get; }

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
                return "Parça";
            }

            if (name.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
            {
                return "Montaj";
            }

            if (name.EndsWith(".slddrw", StringComparison.OrdinalIgnoreCase))
            {
                return "Teknik resim";
            }

            var dot = name.LastIndexOf('.');
            return dot > 0 && dot < name.Length - 1
                ? name.Substring(dot + 1).ToUpperInvariant()
                : "Dosya";
        }
    }

    public string SourceText => Source switch
    {
        FileSourceKind.Folder => "Klasör",
        FileSourceKind.File => "Dosya seçimi",
        _ => "Arama",
    };
}
