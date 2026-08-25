using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using Microsoft.Win32;
using EPDM.Interop.epdm;
using PdmVariableStudio.App.Pdm;
using PdmVariableStudio.App.Views;
using PdmVariableStudio.App.Threading;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Diagnostics;
using PdmVariableStudio.Core.Diff;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Journal;
using PdmVariableStudio.Core.Results;
using PdmVariableStudio.Core.Services;
using PdmVariableStudio.Core.Workbook;

namespace PdmVariableStudio.App.ViewModels;

/// <summary>Dışa aktarımda seçilebilen bir değişken.</summary>
internal sealed class VariableChoice : ObservableObject
{
    private bool _isSelected = true;

    public VariableChoice(PdmVariableDefinition definition)
    {
        Definition = definition;
    }

    public PdmVariableDefinition Definition { get; }

    public string Label => Definition.DataType == PdmVariableType.Text
        ? Definition.DisplayName
        : $"{Definition.DisplayName}  ({TypeText})";

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    private string TypeText => Definition.DataType switch
    {
        PdmVariableType.Int => "tam sayı",
        PdmVariableType.Float => "ondalık",
        PdmVariableType.Bool => "evet/hayır",
        PdmVariableType.Date => "tarih",
        _ => "metin",
    };
}

/// <summary>İşlem geçmişindeki bir satır.</summary>
internal sealed class OperationRowViewModel
{
    public OperationRowViewModel(ApplyOperation operation)
    {
        Operation = operation;
    }

    public ApplyOperation Operation { get; }

    public string Timestamp => Operation.UtcTimestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string User => Operation.PdmUser.Length > 0 ? Operation.PdmUser : Operation.WindowsUser;

    public string TypeText => Operation.TypeText;

    public string Summary =>
        $"{Operation.FileCount} dosya · {Operation.AppliedCount} değer";

    public string Workbook => System.IO.Path.GetFileName(Operation.SourceWorkbookPath);

    public string OutcomeText => Operation.IsUndone
        ? Operation.OutcomeText + " (geri alındı)"
        : Operation.OutcomeText;

    public bool CanUndo => Operation.IsUndoCandidate;
}

/// <summary>
/// Ana pencerenin görünüm modeli. Üç sekmenin (dışa aktar / içe aktar / geçmiş) durumunu tutar.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tek pencere, sekmeli — sihirbaz değil.</b> Kullanıcı "dışa aktar → Excel'de saatlerce
/// çalış → içe aktar" döngüsünde pencereyi kapatıp tekrar açacak. Sihirbaz her açılışta baştan
/// başlamayı dayatır; sekmeli düzen durumu korur ve işlem geçmişi her an erişilebilir kalır.
/// </para>
/// <para>
/// <b>Hiçbir COM nesnesi burada tutulmaz.</b> Tüm PDM erişimi
/// <see cref="PdmWorkQueue.RunAsync{T}"/> üzerinden, adanmış STA thread'inde yapılır ve
/// buraya yalnızca düz nesneler döner.
/// </para>
/// </remarks>
internal sealed class StudioViewModel : ObservableObject, IDisposable
{
    private readonly PdmWorkQueue _queue;
    private readonly IStudioLog _log;
    private readonly int _folderId;
    private readonly string _databaseName;

    private IPdmVaultContext? _vault;
    private ExportService? _exportService;
    private ImportService? _importService;
    private ApplyService? _applyService;
    private UndoService? _undoService;
    private IOperationJournal? _journal;
    private IPdmFolderScanner? _scanner;
    private IPdmFileBrowser? _browser;

    /// <summary>Ana pencerenin tanıtıcısı; PDM'in kendi iletişim kutularına ebeveyn olur.</summary>
    private IntPtr _windowHandle;

    private CancellationTokenSource? _cancellation;

    private string _folderPath = string.Empty;
    private string _vaultName = string.Empty;
    private bool _includeSubfolders;
    private bool _isBusy;
    private bool _isCommitting;
    private string _statusMessage = string.Empty;
    private string _busyMessage = string.Empty;

    private ChangeSet? _changeSet;
    private string _workbookPath = string.Empty;
    private bool _checkoutConsent;
    private bool _checkInAfterApply = true;
    private string _checkInComment = "PDM Variable Studio ile toplu kart güncellemesi";

    private bool _showChangesOnly = true;
    private bool _showConflictsOnly;
    private bool _showErrorsOnly;
    private string _searchText = string.Empty;

    private int _scopeFolderId;
    private string _scopeDescription = string.Empty;
    private bool _scopeIncludeSubfolders;

    private UndoPreview? _undoPreview;
    private OperationRowViewModel? _selectedOperation;

    public StudioViewModel(string vaultName, int folderId, IStudioLog log, string databaseName = "")
    {
        _vaultName = vaultName;
        _folderId = folderId;
        _log = log;
        _databaseName = databaseName;
        _queue = new PdmWorkQueue(vaultName, log);

        Variables = new ObservableCollection<VariableChoice>();
        Files = new ObservableCollection<FileRowViewModel>();
        Changes = new ObservableCollection<ChangeRowViewModel>();
        UndoRows = new ObservableCollection<UndoRowViewModel>();
        Operations = new ObservableCollection<OperationRowViewModel>();

        ChangesView = CollectionViewSource.GetDefaultView(Changes);
        ChangesView.Filter = FilterChangeRow;

        AddFolderCommand = new RelayCommand(async () => await AddFolderAsync(), () => !IsBusy);
        AddFilesCommand = new RelayCommand(async () => await AddFilesAsync(), () => !IsBusy);
        SearchAndAddCommand = new RelayCommand(async () => await SearchAndAddAsync(), () => !IsBusy);
        RemoveSelectedFilesCommand = new RelayCommand(RemoveSelectedFiles, () => !IsBusy && Files.Count > 0);
        ClearFilesCommand = new RelayCommand(ClearFiles, () => !IsBusy && Files.Count > 0);

        ExportCommand = new RelayCommand(async () => await ExportAsync(), () => !IsBusy);
        ImportCommand = new RelayCommand(async () => await ImportAsync(), () => !IsBusy);
        ApplyCommand = new RelayCommand(async () => await ApplyAsync(), CanApply);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy && !IsCommitting);
        SelectAllSafeCommand = new RelayCommand(SelectAllSafe, () => !IsBusy);
        DeselectAllCommand = new RelayCommand(DeselectAll, () => !IsBusy);
        SelectAllVariablesCommand = new RelayCommand(() => SetAllVariables(true), () => !IsBusy);
        DeselectAllVariablesCommand = new RelayCommand(() => SetAllVariables(false), () => !IsBusy);
        RefreshOperationsCommand = new RelayCommand(async () => await LoadOperationsAsync(), () => !IsBusy);
        PreviewUndoCommand = new RelayCommand(async () => await PreviewUndoAsync(), CanPreviewUndo);
        ApplyUndoCommand = new RelayCommand(async () => await ApplyUndoAsync(), CanApplyUndo);
    }

    // ------------------------------------------------------------------ durum

    public ObservableCollection<VariableChoice> Variables { get; }

    /// <summary>İşleme alınacak dosyalar. Üç kaynaktan da buraya eklenir.</summary>
    public ObservableCollection<FileRowViewModel> Files { get; }

    public ObservableCollection<ChangeRowViewModel> Changes { get; }

    public ICollectionView ChangesView { get; }

    public ObservableCollection<UndoRowViewModel> UndoRows { get; }

    public ObservableCollection<OperationRowViewModel> Operations { get; }

    public string VaultName
    {
        get => _vaultName;
        private set => Set(ref _vaultName, value);
    }

    public string FolderPath
    {
        get => _folderPath;
        private set => Set(ref _folderPath, value);
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => Set(ref _includeSubfolders, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    /// <summary>
    /// PDM'ye yazma başladı mı. True iken pencere kapatılamaz ve iptal farklı davranır.
    /// </summary>
    public bool IsCommitting
    {
        get => _isCommitting;
        private set
        {
            if (Set(ref _isCommitting, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => Set(ref _busyMessage, value);
    }

    public string WorkbookPath
    {
        get => _workbookPath;
        private set => Set(ref _workbookPath, value);
    }

    public bool CheckoutConsent
    {
        get => _checkoutConsent;
        set
        {
            if (Set(ref _checkoutConsent, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool CheckInAfterApply
    {
        get => _checkInAfterApply;
        set => Set(ref _checkInAfterApply, value);
    }

    public string CheckInComment
    {
        get => _checkInComment;
        set => Set(ref _checkInComment, value);
    }

    // ---- özet sayaçlar ----

    public int SafeChangeCount => _changeSet?.SafeChangeCount ?? 0;

    public int ConflictCount => _changeSet?.ConflictCount ?? 0;

    public int ErrorCount => _changeSet?.ErrorCount ?? 0;

    public int UnchangedCount => _changeSet?.UnchangedCount ?? 0;

    public int SelectedCount => _changeSet?.SelectedCount ?? 0;

    public int PendingCheckoutCount => _changeSet?.PendingCheckoutCount ?? 0;

    public int BlockedFileCount => _changeSet?.BlockedFileCount ?? 0;

    public bool RequiresCheckoutConsent => PendingCheckoutCount > 0;

    public string CheckoutWarning => PendingCheckoutCount == 0
        ? string.Empty
        : $"Bu işlem {PendingCheckoutCount} dosyayı CHECK-OUT edecek" +
          (CheckInAfterApply ? " ve işlem sonunda geri iade edecek." : " ve çekili bırakacak.");

    public bool HasChangeSet => _changeSet is not null;

    public string RejectionMessage
    {
        get
        {
            if (_changeSet is null || !_changeSet.IsRejected)
            {
                return string.Empty;
            }

            foreach (var issue in _changeSet.Issues)
            {
                if (issue.Severity == IssueSeverity.Fatal)
                {
                    return issue.Summary + Environment.NewLine + issue.Detail;
                }
            }

            return "Çalışma kitabı reddedildi.";
        }
    }

    public bool IsRejected => _changeSet?.IsRejected ?? false;

    // ---- süzgeçler ----

    public bool ShowChangesOnly
    {
        get => _showChangesOnly;
        set
        {
            if (Set(ref _showChangesOnly, value))
            {
                ChangesView.Refresh();
            }
        }
    }

    public bool ShowConflictsOnly
    {
        get => _showConflictsOnly;
        set
        {
            if (Set(ref _showConflictsOnly, value))
            {
                ChangesView.Refresh();
            }
        }
    }

    public bool ShowErrorsOnly
    {
        get => _showErrorsOnly;
        set
        {
            if (Set(ref _showErrorsOnly, value))
            {
                ChangesView.Refresh();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                ChangesView.Refresh();
            }
        }
    }

    // ---- geri alma ----

    public OperationRowViewModel? SelectedOperation
    {
        get => _selectedOperation;
        set
        {
            if (Set(ref _selectedOperation, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public UndoPreview? UndoPreviewData => _undoPreview;

    public int UndoSafeCount => _undoPreview?.SafeCount ?? 0;

    public int UndoConflictCount => _undoPreview?.ConflictCount ?? 0;

    public int UndoUnavailableCount => _undoPreview?.UnavailableCount ?? 0;

    public bool HasUndoPreview => _undoPreview is not null;

    // ---- komutlar ----

    public RelayCommand AddFolderCommand { get; }

    public RelayCommand AddFilesCommand { get; }

    public RelayCommand SearchAndAddCommand { get; }

    public RelayCommand RemoveSelectedFilesCommand { get; }

    public RelayCommand ClearFilesCommand { get; }

    public RelayCommand ExportCommand { get; }

    public RelayCommand ImportCommand { get; }

    public RelayCommand ApplyCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand SelectAllSafeCommand { get; }

    public RelayCommand DeselectAllCommand { get; }

    public RelayCommand SelectAllVariablesCommand { get; }

    public RelayCommand DeselectAllVariablesCommand { get; }

    public RelayCommand RefreshOperationsCommand { get; }

    public RelayCommand PreviewUndoCommand { get; }

    public RelayCommand ApplyUndoCommand { get; }

    // ------------------------------------------------------------ başlatma

    public async Task InitializeAsync()
    {
        IsBusy = true;
        BusyMessage = "Vault'a bağlanılıyor";

        try
        {
            if (!await _queue.WaitUntilReadyAsync())
            {
                StatusMessage = $"'{_vaultName}' vault'una bağlanılamadı. Ayrıntı: {_log.FilePath}";
                return;
            }

            var setup = await _queue.RunAsync(_ =>
            {
                var vaultContext = new PdmVaultContext(_queue.Vault, _log, _databaseName);
                var reader = new PdmVariableReader(_queue.Vault, _log);
                var writer = new PdmVariableWriter(_queue.Vault, _log);
                var checkout = new PdmCheckoutService(_queue.Vault, reader, _log);
                var scanner = new PdmFolderScanner(_queue.Vault, _log);
                var browser = new PdmFileBrowser(_queue.Vault, _log);
                var journal = new JsonlOperationJournal();

                var apply = new ApplyService(vaultContext, reader, writer, checkout, journal, _log);

                return new Services(
                    vaultContext,
                    new ExportService(vaultContext, reader, _log),
                    scanner,
                    browser,
                    new ImportService(vaultContext, reader, _log),
                    apply,
                    new UndoService(vaultContext, reader, journal, apply, _log),
                    journal,
                    vaultContext.GetVariables().ValueOr(Array.Empty<PdmVariableDefinition>()),
                    vaultContext.GetFolderPath(_folderId).ValueOr(string.Empty));
            });

            _vault = setup.Vault;
            _exportService = setup.Export;
            _scanner = setup.Scanner;
            _browser = setup.Browser;
            _importService = setup.Import;
            _applyService = setup.Apply;
            _undoService = setup.Undo;
            _journal = setup.Journal;

            VaultName = setup.Vault.Vault.Name;
            FolderPath = setup.FolderPath;

            Variables.Clear();
            foreach (var definition in setup.Variables)
            {
                Variables.Add(new VariableChoice(definition));
            }

            await LoadOperationsAsync();

            // Eklentiden bir klasörle açıldıysa o klasörün dosyaları hazır gelsin: kullanıcı
            // klasöre sağ tıklayarak geldiyse niyeti belli, tekrar "Klasör Ekle" demesi
            // gereksiz bir adım olurdu.
            if (_folderId > 0)
            {
                await LoadLaunchFolderAsync();
            }
            else
            {
                // Tek başına açıldı: liste boş. Açılışta klasör seçme penceresi göstermek
                // yerine kullanıcıyı kaynak düğmelerine yönlendiriyoruz — orada üç seçenek
                // var ve hangisini isteyeceğini uygulama açılmadan bilemez.
                StatusMessage = $"{Variables.Count} değişken bulundu. " +
                                "Başlamak için Klasör Ekle, Dosya Ekle ya da Ara ve Ekle kullanın.";
            }
        }
        catch (Exception exception)
        {
            _log.Error("Başlatma başarısız.", exception);
            StatusMessage = "Başlatma başarısız. Ayrıntı: " + _log.FilePath;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    /// <summary>
    /// Pencere tanıtıcısını kaydeder; PDM'in kendi klasör/dosya seçme pencereleri buna
    /// ebeveyn olur ki arkada kaybolmasınlar.
    /// </summary>
    public void SetWindowHandle(IntPtr handle) => _windowHandle = handle;

    /// <summary>Eklentiden gelen klasörün dosyalarını listeye alır.</summary>
    private async Task LoadLaunchFolderAsync()
    {
        if (_scanner is null)
        {
            return;
        }

        IsBusy = true;
        BusyMessage = "Klasör taranıyor";

        try
        {
            var folderId = _folderId;
            var includeSubfolders = IncludeSubfolders;
            var progress = new Progress<int>(count => BusyMessage = $"Klasör taranıyor: {count} dosya");

            var scan = await _queue.RunAsync(
                t => _scanner.ScanFolder(folderId, includeSubfolders, Array.Empty<string>(), progress, t));

            if (scan.IsFailure)
            {
                StatusMessage = scan.Summary + " " + scan.Detail;
                return;
            }

            var added = AddFiles(scan.Value, FileSourceKind.Folder);
            RememberFolderScope(folderId, FolderPath, includeSubfolders);

            _log.Info($"Başlangıç klasörü tarandı: {FolderPath} (#{folderId}), " +
                      $"{scan.Value.Count} dosya bulundu, {added} eklendi, " +
                      $"alt klasörler {(includeSubfolders ? "dahil" : "hariç")}.");

            StatusMessage = $"{FolderPath}: {added} dosya, {Variables.Count} değişken.";
        }
        catch (Exception exception)
        {
            _log.Error("Başlangıç klasörü taranamadı.", exception);
            StatusMessage = "Klasör taranamadı. Ayrıntı: " + _log.FilePath;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    /// <summary>PDM'in klasör seçme penceresini açar; iptal edilirse 0 döner.</summary>
    private int BrowseForFolderId(IntPtr parentWindow)
    {
        using var scope = new ComScope();

        try
        {
            if (_queue.Vault is not IEdmVault17 vault17)
            {
                _log.Warn("IEdmVault17 desteklenmiyor; klasör seçme penceresi açılamadı.");
                return 0;
            }

            var folder = scope.Track(vault17.BrowseForFolder(
                parentWindow.ToInt32(),
                "İşleme alınacak dosyaların bulunduğu klasörü seçin"));

            return folder?.ID ?? 0;
        }
        catch (System.Runtime.InteropServices.COMException exception)
        {
            _log.Error("Klasör seçme penceresi açılamadı.", exception);
            return 0;
        }
    }

    // ------------------------------------------------------------ dosya kaynakları

    /// <summary>
    /// Dosyaları listeye ekler; zaten listede olanlar atlanır.
    /// </summary>
    /// <remarks>
    /// Yineleme kontrolü <see cref="PdmFileIdentity"/> eşitliği (FileId + FolderId) üzerinden.
    /// Aynı dosyayı hem klasörden hem aramadan eklemek yaygın; iki kez işlenmesi çalışma
    /// kitabında yinelenen satır ve içe aktarımda <c>DuplicateRow</c> demek olurdu.
    /// </remarks>
    private int AddFiles(IReadOnlyList<PdmFileIdentity> files, FileSourceKind source)
    {
        var existing = new HashSet<PdmFileIdentity>();
        foreach (var row in Files)
        {
            existing.Add(row.File);
        }

        var added = 0;
        foreach (var file in files)
        {
            if (existing.Add(file))
            {
                Files.Add(new FileRowViewModel(file, source));
                added++;
            }
        }

        RaiseFileSummary();
        return added;
    }

    private async Task AddFolderAsync()
    {
        if (_scanner is null || _browser is null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            var token = _cancellation.Token;
            var includeSubfolders = IncludeSubfolders;
            var handle = _windowHandle;

            var folderId = await _queue.RunAsync(_ => BrowseForFolderId(handle), token);
            if (folderId <= 0)
            {
                return;
            }

            BusyMessage = "Klasör taranıyor";

            var progress = new Progress<int>(count => BusyMessage = $"Klasör taranıyor: {count} dosya");

            var scan = await _queue.RunAsync(
                t => _scanner.ScanFolder(folderId, includeSubfolders, Array.Empty<string>(), progress, t),
                token);

            if (scan.IsFailure)
            {
                StatusMessage = scan.Summary + " " + scan.Detail;
                return;
            }

            var path = await _queue.RunAsync(_ => _vault!.GetFolderPath(folderId).ValueOr(string.Empty), token);

            var added = AddFiles(scan.Value, FileSourceKind.Folder);
            RememberFolderScope(folderId, path, includeSubfolders);

            _log.Info($"Klasör eklendi: {path} (#{folderId}), {scan.Value.Count} dosya bulundu, " +
                      $"{added} eklendi, alt klasörler {(includeSubfolders ? "dahil" : "hariç")}.");

            StatusMessage = added == scan.Value.Count
                ? $"{path}: {added} dosya eklendi."
                : $"{path}: {added} dosya eklendi ({scan.Value.Count - added} zaten listedeydi).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Tarama durduruldu.";
        }
        catch (Exception exception)
        {
            _log.Error("Klasör eklenemedi.", exception);
            StatusMessage = "Klasör eklenemedi. Ayrıntı: " + _log.FilePath;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private async Task AddFilesAsync()
    {
        if (_browser is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var handle = _windowHandle;
            var outcome = await _queue.RunAsync(_ => _browser.BrowseForFiles(handle));

            if (outcome.IsFailure)
            {
                StatusMessage = outcome.Summary + " " + outcome.Detail;
                return;
            }

            if (outcome.Value.Count == 0)
            {
                return;
            }

            var added = AddFiles(outcome.Value, FileSourceKind.File);
            StatusMessage = $"{added} dosya eklendi.";
        }
        catch (Exception exception)
        {
            _log.Error("Dosya eklenemedi.", exception);
            StatusMessage = "Dosya eklenemedi. Ayrıntı: " + _log.FilePath;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    private async Task SearchAndAddAsync()
    {
        if (_browser is null)
        {
            return;
        }

        var criteria = SearchDialog.Show(_windowHandle, VariableNames());
        if (criteria is null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        BusyMessage = "Aranıyor";

        try
        {
            var token = _cancellation.Token;
            var progress = new Progress<int>(count => BusyMessage = $"Aranıyor: {count} sonuç");

            var outcome = await _queue.RunAsync(t => _browser.Search(criteria, progress, t), token);

            if (outcome.IsFailure)
            {
                StatusMessage = outcome.Summary + " " + outcome.Detail;
                return;
            }

            if (outcome.Value.Count == 0)
            {
                StatusMessage = "Arama sonuç vermedi.";
                return;
            }

            var added = AddFiles(outcome.Value, FileSourceKind.Search);

            StatusMessage = added == outcome.Value.Count
                ? $"Arama: {added} dosya eklendi."
                : $"Arama: {added} dosya eklendi ({outcome.Value.Count - added} zaten listedeydi).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Arama durduruldu.";
        }
        catch (Exception exception)
        {
            _log.Error("Arama başarısız.", exception);
            StatusMessage = "Arama başarısız. Ayrıntı: " + _log.FilePath;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void RemoveSelectedFiles()
    {
        var remaining = Files.Where(f => !f.IsSelected).ToList();
        var removed = Files.Count - remaining.Count;

        if (removed == 0)
        {
            StatusMessage = "Silinecek dosya işaretlenmemiş.";
            return;
        }

        Files.Clear();
        foreach (var row in remaining)
        {
            Files.Add(row);
        }

        RaiseFileSummary();
        StatusMessage = $"{removed} dosya listeden çıkarıldı.";
    }

    private void ClearFiles()
    {
        Files.Clear();
        _scopeFolderId = 0;
        _scopeDescription = string.Empty;
        RaiseFileSummary();
        StatusMessage = "Dosya listesi temizlendi.";
    }

    private IReadOnlyList<string> VariableNames()
    {
        var names = new List<string>(Variables.Count);
        foreach (var variable in Variables)
        {
            names.Add(variable.Definition.Name);
        }

        return names;
    }

    /// <summary>
    /// Klasör kaynağını hatırlar; çalışma kitabı metadata'sına yazılır.
    /// </summary>
    /// <remarks>
    /// Yalnızca TEK bir klasörden gelindiğinde anlamlı. Kullanıcı ikinci bir kaynak eklerse
    /// kapsam "çeşitli kaynaklar" olur — metadata'da yanlış bir tek klasör göstermek,
    /// dosyayı sonradan inceleyeni yanıltırdı.
    /// </remarks>
    private void RememberFolderScope(int folderId, string path, bool includeSubfolders)
    {
        var description = includeSubfolders ? path + " (alt klasörler dahil)" : path;

        if (_scopeDescription.Length == 0)
        {
            _scopeFolderId = folderId;
            _scopeDescription = description;
            _scopeIncludeSubfolders = includeSubfolders;
            return;
        }

        if (_scopeDescription != description)
        {
            _scopeFolderId = 0;
            _scopeDescription = "(çeşitli kaynaklar)";
        }
    }

    private void RaiseFileSummary()
    {
        RaiseAll(nameof(FileCount), nameof(HasFiles), nameof(FileCountText));
        RaiseCommandStates();
    }

    public int FileCount => Files.Count;

    public bool HasFiles => Files.Count > 0;

    public string FileCountText => Files.Count == 0
        ? "Henüz dosya eklenmedi"
        : $"{Files.Count} dosya";

    // ---------------------------------------------------------- dışa aktarım

    private async Task ExportAsync()
    {
        if (_exportService is null)
        {
            return;
        }

        if (Files.Count == 0)
        {
            StatusMessage = "Önce işleme alınacak dosyaları ekleyin.";
            return;
        }

        var selected = Variables.Where(v => v.IsSelected).Select(v => v.Definition).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "En az bir değişken seçmelisiniz.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Excel çalışma kitabı (*.xlsx)|*.xlsx",
            FileName = BuildDefaultFileName(),
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            var files = Files.Select(f => f.File).ToList();

            var request = new ExportRequest(files, selected)
            {
                ScopeDescription = _scopeDescription.Length > 0 ? _scopeDescription : "(seçili dosyalar)",
                PrimaryFolderId = _scopeFolderId,
                IncludeSubfolders = _scopeIncludeSubfolders,
            };

            var progress = new Progress<ExportProgress>(p => BusyMessage = p.Describe());
            var token = _cancellation.Token;

            var outcome = await _queue.RunAsync(
                t => _exportService.BuildSession(request, progress, t), token);

            if (outcome.IsFailure)
            {
                StatusMessage = outcome.Summary + " " + outcome.Detail;
                return;
            }

            BusyMessage = "Çalışma kitabı yazılıyor";

            // Excel yazımı saf .NET; COM yok, thread havuzunda çalışabilir.
            await Task.Run(() => new WorkbookWriter().Write(outcome.Value, dialog.FileName, token), token);

            StatusMessage = $"{outcome.Value.Rows.Count} satır dışa aktarıldı: {dialog.FileName}";
            _log.Info($"Dışa aktarım tamamlandı: {dialog.FileName}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Dışa aktarım durduruldu.";
        }
        catch (Exception exception)
        {
            _log.Error("Dışa aktarım başarısız.", exception);
            StatusMessage = "Dışa aktarım başarısız. Ayrıntı: " + _log.FilePath;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private string BuildDefaultFileName()
    {
        var folder = FolderPath.TrimStart('\\').Replace('\\', '-');
        if (folder.Length == 0)
        {
            folder = "vault";
        }

        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
        {
            folder = folder.Replace(c, '_');
        }

        return $"{folder}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
    }

    // ----------------------------------------------------------- içe aktarım

    private async Task ImportAsync()
    {
        if (_importService is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Excel çalışma kitabı (*.xlsx)|*.xlsx",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        WorkbookPath = dialog.FileName;

        try
        {
            var progress = new Progress<string>(message => BusyMessage = message);
            var token = _cancellation.Token;

            var outcome = await _queue.RunAsync(
                t => _importService.BuildChangeSet(dialog.FileName, progress, t), token);

            if (outcome.IsFailure)
            {
                StatusMessage = outcome.Summary + " " + outcome.Detail;
                return;
            }

            SetChangeSet(outcome.Value);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "İçe aktarım durduruldu.";
        }
        catch (Exception exception)
        {
            _log.Error("İçe aktarım başarısız.", exception);
            StatusMessage = "İçe aktarım başarısız. Ayrıntı: " + _log.FilePath;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void SetChangeSet(ChangeSet changeSet)
    {
        _changeSet = changeSet;
        CheckoutConsent = false;

        Changes.Clear();
        foreach (var cell in changeSet.Cells)
        {
            Changes.Add(new ChangeRowViewModel(cell));
        }

        ChangesView.Refresh();
        RaiseSummary();

        StatusMessage = changeSet.IsRejected
            ? RejectionMessage
            : $"{changeSet.SafeChangeCount} güvenli değişiklik, {changeSet.ConflictCount} çakışma, " +
              $"{changeSet.ErrorCount} hata, {changeSet.UnchangedCount} değişmemiş.";
    }

    // --------------------------------------------------------------- uygulama

    private bool CanApply() =>
        !IsBusy
        && _changeSet is not null
        && !_changeSet.IsRejected
        && _changeSet.SelectedCount > 0
        && (!RequiresCheckoutConsent || CheckoutConsent);

    private async Task ApplyAsync()
    {
        if (_applyService is null || _changeSet is null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        IsCommitting = true;

        try
        {
            var options = new ApplyOptions
            {
                CheckoutConsent = CheckoutConsent,
                CheckInAfterApply = CheckInAfterApply,
                CheckInComment = CheckInComment,
            };

            var progress = new Progress<ApplyProgress>(p => BusyMessage = p.Describe());
            var token = _cancellation.Token;

            var outcome = await _queue.RunAsync(
                t => _applyService.Apply(_changeSet, options, progress, t), token);

            RefreshRows();
            RaiseSummary();

            StatusMessage = outcome.IsFailure
                ? outcome.Summary + " " + outcome.Detail
                : $"{outcome.Value.AppliedCount} değer uygulandı, " +
                  $"{outcome.Value.FailedCount} başarısız, {outcome.Value.SkippedCount} atlandı.";

            if (outcome.IsSuccess && outcome.Value.FailedCount > 0)
            {
                // Başarısızlar kullanıcının gözünden kaçmasın.
                ShowErrorsOnly = true;
            }

            await LoadOperationsAsync();
        }
        catch (Exception exception)
        {
            _log.Error("Uygulama başarısız.", exception);
            StatusMessage = "Uygulama başarısız. Ayrıntı: " + _log.FilePath;
        }
        finally
        {
            IsCommitting = false;
            IsBusy = false;
            BusyMessage = string.Empty;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    // ------------------------------------------------------------ geri alma

    private async Task LoadOperationsAsync()
    {
        if (_journal is null || _vault is null)
        {
            return;
        }

        try
        {
            var vault = _vault.Vault;
            var operations = await Task.Run(() => _journal.ListOperations(vault, 100));

            Operations.Clear();
            foreach (var operation in operations)
            {
                Operations.Add(new OperationRowViewModel(operation));
            }
        }
        catch (Exception exception)
        {
            _log.Error("İşlem geçmişi okunamadı.", exception);
        }
    }

    private bool CanPreviewUndo() => !IsBusy && SelectedOperation?.CanUndo == true;

    private async Task PreviewUndoAsync()
    {
        if (_undoService is null || SelectedOperation is null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            var operationId = SelectedOperation.Operation.OperationId;
            var progress = new Progress<string>(message => BusyMessage = message);
            var token = _cancellation.Token;

            var outcome = await _queue.RunAsync(
                t => _undoService.BuildPreview(operationId, progress, t), token);

            if (outcome.IsFailure)
            {
                StatusMessage = outcome.Summary + " " + outcome.Detail;
                return;
            }

            _undoPreview = outcome.Value;

            UndoRows.Clear();
            foreach (var candidate in outcome.Value.Candidates)
            {
                UndoRows.Add(new UndoRowViewModel(candidate));
            }

            RaiseAll(nameof(HasUndoPreview), nameof(UndoSafeCount), nameof(UndoConflictCount),
                nameof(UndoUnavailableCount), nameof(UndoPreviewData));

            RaiseCommandStates();

            StatusMessage = $"Geri alma önizlemesi: {outcome.Value.SafeCount} güvenli, " +
                            $"{outcome.Value.ConflictCount} çakışma, " +
                            $"{outcome.Value.UnavailableCount} kullanılamaz.";
        }
        catch (Exception exception)
        {
            _log.Error("Geri alma önizlemesi başarısız.", exception);
            StatusMessage = "Geri alma önizlemesi başarısız. Ayrıntı: " + _log.FilePath;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private bool CanApplyUndo() => !IsBusy && _undoPreview is not null && _undoPreview.SafeCount > 0;

    private async Task ApplyUndoAsync()
    {
        if (_undoService is null || _undoPreview is null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        IsCommitting = true;

        try
        {
            var options = new ApplyOptions
            {
                CheckoutConsent = true,
                CheckInAfterApply = CheckInAfterApply,
            };

            var preview = _undoPreview;
            var progress = new Progress<ApplyProgress>(p => BusyMessage = p.Describe());
            var token = _cancellation.Token;

            var outcome = await _queue.RunAsync(
                t => _undoService.Undo(preview, options, progress, t), token);

            StatusMessage = outcome.IsFailure
                ? outcome.Summary + " " + outcome.Detail
                : $"{outcome.Value.AppliedCount} değer geri alındı, " +
                  $"{outcome.Value.FailedCount} başarısız.";

            _undoPreview = null;
            UndoRows.Clear();
            RaiseAll(nameof(HasUndoPreview), nameof(UndoSafeCount), nameof(UndoConflictCount),
                nameof(UndoUnavailableCount), nameof(UndoPreviewData));

            await LoadOperationsAsync();
        }
        catch (Exception exception)
        {
            _log.Error("Geri alma başarısız.", exception);
            StatusMessage = "Geri alma başarısız. Ayrıntı: " + _log.FilePath;
        }
        finally
        {
            IsCommitting = false;
            IsBusy = false;
            BusyMessage = string.Empty;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    // ------------------------------------------------------------ yardımcılar

    private void Cancel()
    {
        // Commit başladıysa iptal düğmesi zaten devre dışı; buraya düşmez. İptal edilen bir
        // commit yalnızca dosya sınırında durur ve kalanlar "atlandı" olarak kaydedilir.
        _cancellation?.Cancel();
        BusyMessage = "Durduruluyor";
    }

    private bool FilterChangeRow(object item)
    {
        if (item is not ChangeRowViewModel row)
        {
            return false;
        }

        if (ShowConflictsOnly && row.Status != ChangeStatus.Conflict)
        {
            return false;
        }

        if (ShowErrorsOnly && row.Status is not (ChangeStatus.ValidationError or ChangeStatus.Failed))
        {
            return false;
        }

        if (ShowChangesOnly && !ShowConflictsOnly && !ShowErrorsOnly && row.Status == ChangeStatus.Unchanged)
        {
            return false;
        }

        if (_searchText.Length == 0)
        {
            return true;
        }

        return Contains(row.FileName) || Contains(row.VariableName)
            || Contains(row.RequestedValue) || Contains(row.CurrentValue)
            || Contains(row.Configuration);

        bool Contains(string value) =>
            value.IndexOf(_searchText, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private void SelectAllSafe()
    {
        foreach (var row in Changes)
        {
            if (row.CanSelect && row.Status == ChangeStatus.SafeChange)
            {
                row.IsSelected = true;
            }
        }

        RaiseSummary();
    }

    private void DeselectAll()
    {
        foreach (var row in Changes)
        {
            row.IsSelected = false;
        }

        RaiseSummary();
    }

    private void SetAllVariables(bool selected)
    {
        foreach (var variable in Variables)
        {
            variable.IsSelected = selected;
        }
    }

    private void RefreshRows()
    {
        foreach (var row in Changes)
        {
            row.Refresh();
        }

        ChangesView.Refresh();
    }

    /// <summary>Seçim değiştiğinde arayüzün sayaçları ve Uygula düğmesi tazelenir.</summary>
    public void NotifySelectionChanged() => RaiseSummary();

    private void RaiseSummary()
    {
        RaiseAll(
            nameof(SafeChangeCount), nameof(ConflictCount), nameof(ErrorCount),
            nameof(UnchangedCount), nameof(SelectedCount), nameof(PendingCheckoutCount),
            nameof(BlockedFileCount), nameof(RequiresCheckoutConsent), nameof(CheckoutWarning),
            nameof(HasChangeSet), nameof(IsRejected), nameof(RejectionMessage));

        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        AddFolderCommand.RaiseCanExecuteChanged();
        AddFilesCommand.RaiseCanExecuteChanged();
        SearchAndAddCommand.RaiseCanExecuteChanged();
        RemoveSelectedFilesCommand.RaiseCanExecuteChanged();
        ClearFilesCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
        ImportCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        SelectAllSafeCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
        SelectAllVariablesCommand.RaiseCanExecuteChanged();
        DeselectAllVariablesCommand.RaiseCanExecuteChanged();
        RefreshOperationsCommand.RaiseCanExecuteChanged();
        PreviewUndoCommand.RaiseCanExecuteChanged();
        ApplyUndoCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _queue.Dispose();
    }

    /// <summary>Worker thread'inde kurulan servis demeti.</summary>
    private sealed class Services
    {
        public Services(
            IPdmVaultContext vault,
            ExportService export,
            IPdmFolderScanner scanner,
            IPdmFileBrowser browser,
            ImportService import,
            ApplyService apply,
            UndoService undo,
            IOperationJournal journal,
            IReadOnlyList<PdmVariableDefinition> variables,
            string folderPath)
        {
            Vault = vault;
            Export = export;
            Scanner = scanner;
            Browser = browser;
            Import = import;
            Apply = apply;
            Undo = undo;
            Journal = journal;
            Variables = variables;
            FolderPath = folderPath;
        }

        public IPdmVaultContext Vault { get; }

        public ExportService Export { get; }

        public IPdmFolderScanner Scanner { get; }

        public IPdmFileBrowser Browser { get; }

        public ImportService Import { get; }

        public ApplyService Apply { get; }

        public UndoService Undo { get; }

        public IOperationJournal Journal { get; }

        public IReadOnlyList<PdmVariableDefinition> Variables { get; }

        public string FolderPath { get; }
    }
}
