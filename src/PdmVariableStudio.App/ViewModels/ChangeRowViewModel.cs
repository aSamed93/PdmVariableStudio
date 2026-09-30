using System.Globalization;
using PdmVariableStudio.Core.Diff;
using PdmVariableStudio.Core.Localization;
using PdmVariableStudio.Core.Results;
using PdmVariableStudio.Core.Services;

namespace PdmVariableStudio.App.ViewModels;

/// <summary>
/// Önizleme tablosundaki tek bir satır.
/// </summary>
/// <remarks>
/// Durum kullanıcıya ÜÇ kanaldan birden gösterilir: simge, metin ve renk. Renk tek başına
/// yeterli değil — renk körlüğü ve yüksek kontrast temalarında bilgi kaybolur. İpucu metni
/// (tooltip) ayrıca nedeni ve önerilen eylemi anlatır.
/// </remarks>
internal sealed class ChangeRowViewModel : ObservableObject
{
    private readonly CellChange _cell;

    public ChangeRowViewModel(CellChange cell)
    {
        _cell = cell;
    }

    public CellChange Cell => _cell;

    public bool IsSelected
    {
        get => _cell.IsSelected;
        set
        {
            if (_cell.IsSelected == value)
            {
                return;
            }

            // Uygulanamayan bir hücre asla seçilemez. Arayüz onay kutusunu da devre dışı
            // bırakıyor ama tek savunma hattı olarak bırakmıyoruz.
            _cell.IsSelected = value && _cell.CanApply;
            Raise();
        }
    }

    public bool CanSelect => _cell.CanApply;

    public string FileName => _cell.Coordinate.File.FileName;

    public string RelativePath => _cell.Coordinate.File.RelativePath;

    public string Configuration => _cell.Coordinate.Configuration.ToDisplayString();

    public string VariableName => _cell.Variable.DisplayName;

    public string OriginalValue => _cell.Original.ToDisplayString(CultureInfo.CurrentCulture);

    public string CurrentValue => _cell.Current.ToDisplayString(CultureInfo.CurrentCulture);

    public string RequestedValue => _cell.Requested.ToDisplayString(CultureInfo.CurrentCulture);

    public int ExportRowId => _cell.ExportRowId;

    public ChangeStatus Status => _cell.Status;

    /// <summary>Durum simgesi. Renkle birlikte değil, renge EK olarak taşınır.</summary>
    public string StatusGlyph => _cell.Status switch
    {
        ChangeStatus.Unchanged => "·",
        ChangeStatus.SafeChange => _cell.CanApply ? "✓" : "🔒",
        ChangeStatus.AlreadyApplied => "=",
        ChangeStatus.Conflict => "⚠",
        ChangeStatus.ValidationError => "✗",
        ChangeStatus.NotWritable => "🔒",
        ChangeStatus.Applied => "✔",
        ChangeStatus.Failed => "!",
        _ => "–",
    };

    public string StatusText => _cell.Status switch
    {
        ChangeStatus.Unchanged => Loc.T("Değişmemiş", "Unchanged"),
        ChangeStatus.SafeChange => _cell.CanApply ? Loc.T("Güvenli", "Safe") : IssueText.Summary(_cell.Reason),
        ChangeStatus.AlreadyApplied => Loc.T("Zaten uygulanmış", "Already applied"),
        ChangeStatus.Conflict => Loc.T("Çakışma", "Conflict"),
        ChangeStatus.ValidationError => Loc.T("Hata", "Error"),
        ChangeStatus.NotWritable => Loc.T("Yazılamaz", "Not writable"),
        ChangeStatus.Applied => Loc.T("Uygulandı", "Applied"),
        ChangeStatus.Failed => Loc.T("Başarısız", "Failed"),
        _ => Loc.T("Atlandı", "Skipped"),
    };

    /// <summary>Tema kaynak anahtarı. Renk XAML'de tanımlı; burada yalnızca sınıf adı üretilir.</summary>
    public string StatusKind => _cell.Status switch
    {
        ChangeStatus.SafeChange => _cell.CanApply ? "Safe" : "Blocked",
        ChangeStatus.AlreadyApplied => "Neutral",
        ChangeStatus.Conflict => "Conflict",
        ChangeStatus.ValidationError => "Error",
        ChangeStatus.NotWritable => "Blocked",
        ChangeStatus.Applied => "Applied",
        ChangeStatus.Failed => "Error",
        ChangeStatus.Skipped => "Blocked",
        _ => "Default",
    };

    public string Reason => _cell.Reason == IssueCode.None
        ? string.Empty
        : IssueText.Summary(_cell.Reason);

    /// <summary>İpucu: neden bu durumda ve kullanıcı ne yapabilir.</summary>
    public string Tooltip
    {
        get
        {
            if (_cell.Reason != IssueCode.None)
            {
                return IssueText.Detail(_cell.Reason);
            }

            return _cell.Status switch
            {
                ChangeStatus.SafeChange => Loc.T(
                    "Bu değer Excel'de değiştirilmiş ve PDM tarafı dışa aktarımdan beri " +
                    "değişmemiş. Uygulanması güvenli.",
                    "This value was changed in Excel and has not changed in PDM since the " +
                    "export. It is safe to apply."),
                ChangeStatus.Unchanged => Loc.T("Bu hücreye dokunulmamış.", "This cell was not edited."),
                ChangeStatus.Applied => Loc.T("Değer PDM'ye başarıyla yazıldı.", "The value was written to PDM successfully."),
                _ => string.Empty,
            };
        }
    }

    /// <summary>Uygulama sonrası satırın tümünü tazeler.</summary>
    public void Refresh() => RaiseAll(
        nameof(IsSelected), nameof(CanSelect), nameof(Status), nameof(StatusGlyph),
        nameof(StatusText), nameof(StatusKind), nameof(Reason), nameof(Tooltip),
        nameof(CurrentValue));
}

/// <summary>Geri alma önizlemesindeki tek bir satır.</summary>
internal sealed class UndoRowViewModel : ObservableObject
{
    private readonly UndoCandidate _candidate;

    public UndoRowViewModel(UndoCandidate candidate)
    {
        _candidate = candidate;
    }

    public UndoCandidate Candidate => _candidate;

    public bool IsSelected
    {
        get => _candidate.IsSelected;
        set
        {
            if (_candidate.IsSelected == value)
            {
                return;
            }

            _candidate.IsSelected = value && _candidate.CanUndo;
            Raise();
        }
    }

    public bool CanSelect => _candidate.CanUndo;

    public string FileName => _candidate.File.FileName;

    public string RelativePath => _candidate.File.RelativePath;

    public string Configuration => _candidate.Entry.Configuration.ToDisplayString();

    public string VariableName => _candidate.Entry.Variable.DisplayName;

    public string CurrentValue => _candidate.CurrentValue.ToDisplayString(CultureInfo.CurrentCulture);

    public string ValueAfterUndo => _candidate.ValueAfterUndo.ToDisplayString(CultureInfo.CurrentCulture);

    /// <summary>Bizim uyguladığımız değer. Çakışmanın neden çakışma olduğunu açıklar.</summary>
    public string AppliedValue => _candidate.Entry.AppliedValue.ToDisplayString(CultureInfo.CurrentCulture);

    public string StatusText => _candidate.StatusText;

    public string StatusGlyph => _candidate.Status switch
    {
        UndoStatus.SafeUndo => "✓",
        UndoStatus.AlreadyReverted => "=",
        UndoStatus.Conflict => "⚠",
        UndoStatus.Unavailable => "🔒",
        UndoStatus.Reverted => "✔",
        UndoStatus.Failed => "!",
        _ => "–",
    };

    public string StatusKind => _candidate.Status switch
    {
        UndoStatus.SafeUndo => "Safe",
        UndoStatus.AlreadyReverted => "Neutral",
        UndoStatus.Conflict => "Conflict",
        UndoStatus.Unavailable => "Blocked",
        UndoStatus.Reverted => "Applied",
        UndoStatus.Failed => "Error",
        _ => "Default",
    };

    public string Tooltip => _candidate.Reason == IssueCode.None
        ? Loc.T("Yazdığımız değer PDM'de hâlâ duruyor; geri alınması güvenli.",
                "The value we wrote is still in PDM; it is safe to undo.")
        : IssueText.Detail(_candidate.Reason);
}
