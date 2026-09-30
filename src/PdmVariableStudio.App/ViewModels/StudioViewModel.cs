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
using PdmVariableStudio.Core.Localization;
using PdmVariableStudio.Core.Results;
using PdmVariableStudio.Core.Services;
using PdmVariableStudio.Core.Settings;
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
        PdmVariableType.Int => Loc.T("tam sayı", "integer"),
        PdmVariableType.Float => Loc.T("ondalık", "decimal"),
        PdmVariableType.Bool => Loc.T("evet/hayır", "yes/no"),
        PdmVariableType.Date => Loc.T("tarih", "date"),
        _ => Loc.T("metin", "text"),
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
        Loc.N(Operation.FileCount, "dosya", "file", "files") + " · " +
        Loc.N(Operation.AppliedCount, "değer", "value", "values");

    public string Workbook => System.IO.Path.GetFileName(Operation.SourceWorkbookPath);

    public string OutcomeText => Operation.IsUndone
        ? Operation.OutcomeText + Loc.T(" (geri alındı)", " (undone)")
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

    /// <summary>
    /// Kullanıcı tercihleri. Her değişiklikte diske yazılır — pencere çökerse de kaybolmasın.
    /// Yükleme sırasında yazmayı önlemek için <see cref="_settingsLoaded"/> bayrağı var.
    /// </summary>
    private readonly StudioSettings _settings;
    private readonly JournalRoot _journalRoot;
    private bool _settingsLoaded;

    private IPdmVaultContext? _vault;
    private ExportService? _exportService;
    private ImportService? _importService;
    private ApplyService? _applyService;
    private UndoService? _undoService;
    private IOperationJournal? _journal;
    private IPdmFolderScanner? _scanner;
    private IPdmFileBrowser? _browser;
    private IPdmAssemblyReader? _assemblyReader;

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
    private string _lastExportPath = string.Empty;
    private bool _checkoutConsent;
    private bool _checkInAfterApply = true;
    private string _checkInComment = StudioSettings.DefaultCheckInComment();

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

        _settings = StudioSettings.Load();
        _includeSubfolders = _settings.IncludeSubfolders;
        _checkInAfterApply = _settings.CheckInAfterApply;
        _checkInComment = _settings.CheckInComment;
        _settingsLoaded = true;

        // Dosya yoksa varsayılanlarla oluştur: kullanıcı journalRoot gibi bir alanı elle
        // düzenleyecekse dosyayı ve alan adlarını arayıp bulmak zorunda kalmasın.
        if (!System.IO.File.Exists(StudioSettings.DefaultPath()))
        {
            _settings.Save();
        }

        _journalRoot = JournalRootResolver.Resolve(_settings, log);
        log.Info($"İşlem geçmişi kökü: {_journalRoot.Path} ({_journalRoot.SourceText}).");

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
        AddFromAssemblyCommand = new RelayCommand(async () => await AddFromAssemblyAsync(), () => !IsBusy);
        RemoveSelectedFilesCommand = new RelayCommand(RemoveSelectedFiles, () => !IsBusy && Files.Count > 0);
        ClearFilesCommand = new RelayCommand(ClearFiles, () => !IsBusy && Files.Count > 0);

        ExportCommand = new RelayCommand(async () => await ExportAsync(), () => !IsBusy);
        OpenLastExportCommand = new RelayCommand(
            () => OpenLastExport(revealInFolder: false), () => LastExportPath.Length > 0);
        RevealLastExportCommand = new RelayCommand(
            () => OpenLastExport(revealInFolder: true), () => LastExportPath.Length > 0);
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
        ShowAboutCommand = new RelayCommand(
            () => AboutDialog.Show(_windowHandle, _log, _journalRoot.Path, _journalRoot.SourceText));
    }

    /// <summary>Tercih değişince diske yaz. Yükleme sırasında ve değişmeyen değerde yazmaz.</summary>
    private void PersistSettings()
    {
        if (!_settingsLoaded)
        {
            return;
        }

        _settings.IncludeSubfolders = _includeSubfolders;
        _settings.CheckInAfterApply = _checkInAfterApply;
        _settings.CheckInComment = _checkInComment;
        _settings.Save();
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
        set
        {
            if (Set(ref _includeSubfolders, value))
            {
                PersistSettings();
            }
        }
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

    /// <summary>
    /// Bu oturumda son yazılan çalışma kitabının tam yolu; boşsa henüz dışa aktarım yok.
    /// Kullanıcı dosyayı Excel'de açıp düzenlemeye hemen başlayabilsin diye tutulur.
    /// </summary>
    public string LastExportPath
    {
        get => _lastExportPath;
        private set
        {
            if (Set(ref _lastExportPath, value))
            {
                Raise(nameof(LastExportFileName));
                OpenLastExportCommand.RaiseCanExecuteChanged();
                RevealLastExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LastExportFileName =>
        _lastExportPath.Length > 0 ? System.IO.Path.GetFileName(_lastExportPath) : string.Empty;

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
        set
        {
            if (Set(ref _checkInAfterApply, value))
            {
                PersistSettings();
            }
        }
    }

    public string CheckInComment
    {
        get => _checkInComment;
        set
        {
            if (Set(ref _checkInComment, value))
            {
                PersistSettings();
            }
        }
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
        : CheckInAfterApply
            ? Loc.T(
                $"Bu işlem {PendingCheckoutCount} dosyayı CHECK-OUT edecek ve işlem sonunda geri iade edecek.",
                $"This operation will CHECK OUT {Loc.N(PendingCheckoutCount, "dosya", "file", "files")} " +
                $"and check {(PendingCheckoutCount == 1 ? "it" : "them")} back in when it finishes.")
            : Loc.T(
                $"Bu işlem {PendingCheckoutCount} dosyayı CHECK-OUT edecek ve çekili bırakacak.",
                $"This operation will CHECK OUT {Loc.N(PendingCheckoutCount, "dosya", "file", "files")} " +
                $"and leave {(PendingCheckoutCount == 1 ? "it" : "them")} checked out.");

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

            return Loc.T("Çalışma kitabı reddedildi.", "The workbook was rejected.");
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

    public RelayCommand AddFromAssemblyCommand { get; }

    public RelayCommand RemoveSelectedFilesCommand { get; }

    public RelayCommand ClearFilesCommand { get; }

    public RelayCommand ExportCommand { get; }

    /// <summary>Son dışa aktarılan çalışma kitabını varsayılan uygulamada (Excel) açar.</summary>
    public RelayCommand OpenLastExportCommand { get; }

    /// <summary>Son dışa aktarılan çalışma kitabını Gezgin'de seçili gösterir.</summary>
    public RelayCommand RevealLastExportCommand { get; }

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

    public RelayCommand ShowAboutCommand { get; }

    // ------------------------------------------------------------ başlatma

    public async Task InitializeAsync()
    {
        IsBusy = true;
        BusyMessage = Loc.T("Vault'a bağlanılıyor", "Connecting to vault");

        try
        {
            if (!await _queue.WaitUntilReadyAsync())
            {
                StatusMessage = Loc.T(
                    $"'{_vaultName}' vault'una bağlanılamadı. Ayrıntı: {_log.FilePath}",
                    $"Could not connect to vault '{_vaultName}'. Details: {_log.FilePath}");
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
                var assemblyReader = new PdmAssemblyReader(_queue.Vault, _log);
                var journal = new JsonlOperationJournal(_journalRoot.Path);

                var apply = new ApplyService(vaultContext, reader, writer, checkout, journal, _log);

                return new Services(
                    vaultContext,
                    new ExportService(vaultContext, reader, _log),
                    scanner,
                    browser,
                    assemblyReader,
                    new ImportService(vaultContext, reader, _log),
                    apply,
                    new UndoService(vaultContext, reader, journal, apply, _log),
                    journal,
                    vaultContext.GetVariables().ValueOr(Array.Empty<PdmVariableDefinition>()),
                    // Klasör yalnızca eklentiden gelir. Tek başına açılışta 0'dır ve
                    // GetObject(Folder, 0) interop tarafında ArgumentException fırlatır —
                    // sormamak, sorup düşmekten iyidir.
                    _folderId > 0
                        ? vaultContext.GetFolderPath(_folderId).ValueOr(string.Empty)
                        : string.Empty);
            });

            _vault = setup.Vault;
            _exportService = setup.Export;
            _scanner = setup.Scanner;
            _browser = setup.Browser;
            _assemblyReader = setup.AssemblyReader;
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
                StatusMessage = Loc.T(
                    $"{Variables.Count} değişken bulundu. " +
                    "Başlamak için Klasör Ekle, Dosya Ekle, Ara ve Ekle ya da Montajdan Ekle kullanın.",
                    $"{Loc.N(Variables.Count, "değişken", "variable", "variables")} found. " +
                    "To get started, use Add Folder, Add Files, Search and Add or Add from Assembly.");
            }
        }
        catch (Exception exception)
        {
            _log.Error("Başlatma başarısız.", exception);
            StatusMessage = Loc.T("Başlatma başarısız. Ayrıntı: ", "Startup failed. Details: ") + _log.FilePath;
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
        BusyMessage = Loc.T("Klasör taranıyor", "Scanning folder");

        try
        {
            var folderId = _folderId;
            var includeSubfolders = IncludeSubfolders;
            var progress = new Progress<int>(count => BusyMessage = Loc.T(
                $"Klasör taranıyor: {count} dosya",
                $"Scanning folder: {Loc.N(count, "dosya", "file", "files")}"));

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

            StatusMessage = $"{FolderPath}: {Loc.N(added, "dosya", "file", "files")}, " +
                            $"{Loc.N(Variables.Count, "değişken", "variable", "variables")}.";
        }
        catch (Exception exception)
        {
            _log.Error("Başlangıç klasörü taranamadı.", exception);
            StatusMessage = Loc.T("Klasör taranamadı. Ayrıntı: ", "Could not scan the folder. Details: ") + _log.FilePath;
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
                Loc.T("İşleme alınacak dosyaların bulunduğu klasörü seçin",
                    "Select the folder containing the files to process")));

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
        var existing = new Dictionary<PdmFileIdentity, FileRowViewModel>();
        foreach (var row in Files)
        {
            existing[row.File] = row;
        }

        var added = 0;
        foreach (var file in files)
        {
            if (existing.TryGetValue(file, out var row))
            {
                // Montajdan gelmiş bir dosya şimdi klasörden/aramadan da istendi: kullanıcı
                // artık dosyanın TÜM konfigürasyonlarını istiyor demektir.
                row.Scope = null;
                continue;
            }

            var newRow = new FileRowViewModel(file, source);
            existing[file] = newRow;
            Files.Add(newRow);
            added++;
        }

        RaiseFileSummary();
        return added;
    }

    /// <summary>
    /// Montaj açılımındaki dosyaları kapsamlarıyla listeye ekler.
    /// </summary>
    /// <remarks>
    /// Listede zaten olan bir dosyada: kapsamı yoksa (klasörden gelmiş, tüm konfigürasyonlar)
    /// dokunulmaz; kapsamı varsa (başka bir montajdan gelmiş) kapsamlar birleşir.
    /// </remarks>
    private int AddAssemblyFiles(IReadOnlyList<ExpandedFile> files, string sourceDetail)
    {
        var existing = new Dictionary<PdmFileIdentity, FileRowViewModel>();
        foreach (var row in Files)
        {
            existing[row.File] = row;
        }

        var added = 0;
        foreach (var file in files)
        {
            if (existing.TryGetValue(file.File, out var row))
            {
                if (row.Scope is not null)
                {
                    row.Scope = row.Scope.Merge(file.Scope);
                }

                continue;
            }

            var newRow = new FileRowViewModel(file.File, FileSourceKind.Assembly, file.Scope, sourceDetail);
            existing[file.File] = newRow;
            Files.Add(newRow);
            added++;
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

            BusyMessage = Loc.T("Klasör taranıyor", "Scanning folder");

            var progress = new Progress<int>(count => BusyMessage = Loc.T(
                $"Klasör taranıyor: {count} dosya",
                $"Scanning folder: {Loc.N(count, "dosya", "file", "files")}"));

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
                ? Loc.T(
                    $"{path}: {added} dosya eklendi.",
                    $"{path}: {Loc.N(added, "dosya", "file", "files")} added.")
                : Loc.T(
                    $"{path}: {added} dosya eklendi ({scan.Value.Count - added} zaten listedeydi).",
                    $"{path}: {Loc.N(added, "dosya", "file", "files")} added ({scan.Value.Count - added} already in the list).");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.T("Tarama durduruldu.", "Scan stopped.");
        }
        catch (Exception exception)
        {
            _log.Error("Klasör eklenemedi.", exception);
            StatusMessage = Loc.T("Klasör eklenemedi. Ayrıntı: ", "Could not add the folder. Details: ") + _log.FilePath;
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
            StatusMessage = Loc.T(
                $"{added} dosya eklendi.",
                $"{Loc.N(added, "dosya", "file", "files")} added.");
        }
        catch (Exception exception)
        {
            _log.Error("Dosya eklenemedi.", exception);
            StatusMessage = Loc.T("Dosya eklenemedi. Ayrıntı: ", "Could not add files. Details: ") + _log.FilePath;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    /// <summary>
    /// Montaj seçtirir, konfigürasyonunu sorar ve bileşenlerini listeye ekler.
    /// </summary>
    /// <remarks>
    /// Seçim PDM'in kendi dosya seçme penceresiyle yapılır (birden fazla montaj seçilebilir);
    /// montaj olmayan seçimler atlanır ve söylenir. Her montaj için ayrı ayrı konfigürasyon
    /// sorulur, çünkü iki montajın konfigürasyonları birbirinden bağımsız.
    /// </remarks>
    private async Task AddFromAssemblyAsync()
    {
        if (_browser is null || _assemblyReader is null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            var token = _cancellation.Token;
            var handle = _windowHandle;
            var picked = await _queue.RunAsync(_ => _browser.BrowseForFiles(handle), token);

            if (picked.IsFailure)
            {
                StatusMessage = picked.Summary + " " + picked.Detail;
                return;
            }

            var assemblies = picked.Value
                .Where(f => f.FileName.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (assemblies.Count == 0)
            {
                if (picked.Value.Count > 0)
                {
                    StatusMessage = Loc.T(
                        "Seçilen dosyalar arasında montaj (.sldasm) yok. " +
                        "Parça ya da belge eklemek için Dosya Ekle'yi kullanın.",
                        "None of the selected files is an assembly (.sldasm). " +
                        "To add parts or documents, use Add Files.");
                }

                return;
            }

            var totalAdded = 0;
            var summaries = new List<string>();

            foreach (var assembly in assemblies)
            {
                token.ThrowIfCancellationRequested();

                var configurations = await _queue.RunAsync(_ => _assemblyReader.GetConfigurations(assembly), token);
                if (configurations.IsFailure)
                {
                    StatusMessage = $"{assembly.FileName}: {configurations.Summary} {configurations.Detail}";
                    continue;
                }

                if (configurations.Value.Count == 0)
                {
                    StatusMessage = Loc.T(
                        $"{assembly.FileName}: konfigürasyon okunamadı; montaj atlandı.",
                        $"{assembly.FileName}: could not read configurations; assembly skipped.");
                    continue;
                }

                // İletişim kutusu arayüz thread'inde; meşgul göstergesi kapalıyken gösterilir.
                IsBusy = false;
                var choice = AssemblyDialog.Show(_windowHandle, assembly.FileName, configurations.Value);
                IsBusy = true;

                if (choice is null)
                {
                    continue;
                }

                BusyMessage = Loc.T($"Montaj okunuyor: {assembly.FileName}", $"Reading assembly: {assembly.FileName}");

                var structure = await _queue.RunAsync(
                    t => _assemblyReader.ReadStructure(
                        assembly, choice.Configuration, choice.IncludeSubassemblyContents, t),
                    token);

                if (structure.IsFailure)
                {
                    StatusMessage = $"{assembly.FileName}: {structure.Summary} {structure.Detail}";
                    continue;
                }

                var expanded = AssemblyExpansion.Expand(
                    assembly, choice.Configuration, structure.Value, choice.IncludeRoot);

                var detail = $"{assembly.FileName} [{choice.Configuration}]";
                var added = AddAssemblyFiles(expanded, detail);
                totalAdded += added;

                RememberFolderScope(assembly.FolderId, Loc.T("Montaj: ", "Assembly: ") + detail, includeSubfolders: false);

                _log.Info($"Montajdan eklendi: {detail}, {expanded.Count} dosya, {added} yeni, " +
                          $"alt montaj içerikleri {(choice.IncludeSubassemblyContents ? "dahil" : "hariç")}, " +
                          $"kök montaj {(choice.IncludeRoot ? "dahil" : "hariç")}.");

                summaries.Add(added == expanded.Count
                    ? $"{assembly.FileName}: {Loc.N(added, "dosya", "file", "files")}"
                    : Loc.T(
                        $"{assembly.FileName}: {added} dosya ({expanded.Count - added} zaten listedeydi)",
                        $"{assembly.FileName}: {Loc.N(added, "dosya", "file", "files")} ({expanded.Count - added} already in the list)"));
            }

            if (summaries.Count > 0)
            {
                StatusMessage = Loc.T("Montajdan eklendi — ", "Added from assembly — ") + string.Join("; ", summaries) + ".";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.T("Montaj okuma durduruldu.", "Assembly reading stopped.");
        }
        catch (Exception exception)
        {
            _log.Error("Montajdan eklenemedi.", exception);
            StatusMessage = Loc.T("Montajdan eklenemedi. Ayrıntı: ", "Could not add from assembly. Details: ") + _log.FilePath;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            _cancellation?.Dispose();
            _cancellation = null;
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
        BusyMessage = Loc.T("Aranıyor", "Searching");

        try
        {
            var token = _cancellation.Token;
            var progress = new Progress<int>(count => BusyMessage = Loc.T(
                $"Aranıyor: {count} sonuç",
                $"Searching: {Loc.N(count, "sonuç", "result", "results")}"));

            var outcome = await _queue.RunAsync(t => _browser.Search(criteria, progress, t), token);

            if (outcome.IsFailure)
            {
                StatusMessage = outcome.Summary + " " + outcome.Detail;
                return;
            }

            if (outcome.Value.Count == 0)
            {
                StatusMessage = Loc.T("Arama sonuç vermedi.", "The search returned no results.");
                return;
            }

            var added = AddFiles(outcome.Value, FileSourceKind.Search);

            StatusMessage = added == outcome.Value.Count
                ? Loc.T(
                    $"Arama: {added} dosya eklendi.",
                    $"Search: {Loc.N(added, "dosya", "file", "files")} added.")
                : Loc.T(
                    $"Arama: {added} dosya eklendi ({outcome.Value.Count - added} zaten listedeydi).",
                    $"Search: {Loc.N(added, "dosya", "file", "files")} added ({outcome.Value.Count - added} already in the list).");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.T("Arama durduruldu.", "Search stopped.");
        }
        catch (Exception exception)
        {
            _log.Error("Arama başarısız.", exception);
            StatusMessage = Loc.T("Arama başarısız. Ayrıntı: ", "Search failed. Details: ") + _log.FilePath;
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
            StatusMessage = Loc.T("Silinecek dosya işaretlenmemiş.", "No files are marked for removal.");
            return;
        }

        Files.Clear();
        foreach (var row in remaining)
        {
            Files.Add(row);
        }

        RaiseFileSummary();
        StatusMessage = Loc.T(
            $"{removed} dosya listeden çıkarıldı.",
            $"{Loc.N(removed, "dosya", "file", "files")} removed from the list.");
    }

    private void ClearFiles()
    {
        Files.Clear();
        _scopeFolderId = 0;
        _scopeDescription = string.Empty;
        RaiseFileSummary();
        StatusMessage = Loc.T("Dosya listesi temizlendi.", "File list cleared.");
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
        var description = includeSubfolders
            ? path + Loc.T(" (alt klasörler dahil)", " (subfolders included)")
            : path;

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
            _scopeDescription = Loc.T("(çeşitli kaynaklar)", "(multiple sources)");
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
        ? Loc.T("Henüz dosya eklenmedi", "No files added yet")
        : Loc.N(Files.Count, "dosya", "file", "files");

    // ---------------------------------------------------------- dışa aktarım

    /// <summary>
    /// Son dışa aktarılan dosyayı açar. Dosya kullanıcı tarafından taşınmış/silinmiş olabilir;
    /// o durumda hata penceresi değil durum satırında kısa bir açıklama gösterilir.
    /// </summary>
    private void OpenLastExport(bool revealInFolder)
    {
        var path = LastExportPath;
        if (path.Length == 0)
        {
            return;
        }

        if (!System.IO.File.Exists(path))
        {
            StatusMessage = Loc.T($"Dosya artık burada değil: {path}", $"The file is no longer here: {path}");
            LastExportPath = string.Empty;
            return;
        }

        try
        {
            // UseShellExecute: .xlsx için kayıtlı uygulama (Excel) açılır; Gezgin için
            // /select ile dosya seçili gelir. Kabuk kaydı yoksa Win32Exception döner.
            var startInfo = revealInFolder
                ? new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
                : new System.Diagnostics.ProcessStartInfo(path);

            startInfo.UseShellExecute = true;
            System.Diagnostics.Process.Start(startInfo)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            _log.Error("Dışa aktarılan dosya açılamadı: " + path, exception);
            StatusMessage = Loc.T(
                "Dosya açılamadı; .xlsx için kayıtlı bir uygulama bulunamadı. Ayrıntı: ",
                "Could not open the file; no application is registered for .xlsx. Details: ") + _log.FilePath;
        }
    }

    private async Task ExportAsync()
    {
        if (_exportService is null)
        {
            return;
        }

        if (Files.Count == 0)
        {
            StatusMessage = Loc.T("Önce işleme alınacak dosyaları ekleyin.", "Add the files to process first.");
            return;
        }

        var selected = Variables.Where(v => v.IsSelected).Select(v => v.Definition).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = Loc.T("En az bir değişken seçmelisiniz.", "Select at least one variable.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = Loc.T("Excel çalışma kitabı (*.xlsx)|*.xlsx", "Excel workbook (*.xlsx)|*.xlsx"),
            FileName = BuildDefaultFileName(),
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = RememberedExportDirectory(),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        RememberExportDirectory(dialog.FileName);

        _cancellation = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            var files = Files.Select(f => f.File).ToList();
            var scopes = new Dictionary<PdmFileIdentity, FileExportScope>();
            foreach (var row in Files)
            {
                if (row.Scope is not null)
                {
                    scopes[row.File] = row.Scope;
                }
            }

            var request = new ExportRequest(files, selected)
            {
                ScopeDescription = _scopeDescription.Length > 0
                    ? _scopeDescription
                    : Loc.T("(seçili dosyalar)", "(selected files)"),
                PrimaryFolderId = _scopeFolderId,
                IncludeSubfolders = _scopeIncludeSubfolders,
                Scopes = scopes,
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

            BusyMessage = Loc.T("Çalışma kitabı yazılıyor", "Writing workbook");

            // Excel yazımı saf .NET; COM yok, thread havuzunda çalışabilir.
            await Task.Run(() => new WorkbookWriter().Write(outcome.Value, dialog.FileName, token), token);

            LastExportPath = dialog.FileName;
            StatusMessage = Loc.T(
                $"{outcome.Value.Rows.Count} satır dışa aktarıldı: {dialog.FileName}",
                $"{Loc.N(outcome.Value.Rows.Count, "satır", "row", "rows")} exported: {dialog.FileName}");
            _log.Info($"Dışa aktarım tamamlandı: {dialog.FileName}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.T("Dışa aktarım durduruldu.", "Export stopped.");
        }
        catch (Exception exception)
        {
            _log.Error("Dışa aktarım başarısız.", exception);
            StatusMessage = Loc.T("Dışa aktarım başarısız. Ayrıntı: ", "Export failed. Details: ") + _log.FilePath;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    /// <summary>Son dışa aktarım klasörü hâlâ varsa onu, yoksa boş (Windows varsayılanı).</summary>
    private string RememberedExportDirectory()
    {
        var directory = _settings.LastExportDirectory;
        return directory.Length > 0 && System.IO.Directory.Exists(directory) ? directory : string.Empty;
    }

    private void RememberExportDirectory(string filePath)
    {
        var directory = System.IO.Path.GetDirectoryName(filePath) ?? string.Empty;
        if (directory.Length == 0 || directory == _settings.LastExportDirectory)
        {
            return;
        }

        _settings.LastExportDirectory = directory;
        _settings.Save();
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
            Filter = Loc.T("Excel çalışma kitabı (*.xlsx)|*.xlsx", "Excel workbook (*.xlsx)|*.xlsx"),
            CheckFileExists = true,
            // Dışa aktarılan dosya büyük olasılıkla aynı klasörden geri gelecek.
            InitialDirectory = RememberedExportDirectory(),
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
            StatusMessage = Loc.T("İçe aktarım durduruldu.", "Import stopped.");
        }
        catch (Exception exception)
        {
            _log.Error("İçe aktarım başarısız.", exception);
            StatusMessage = Loc.T("İçe aktarım başarısız. Ayrıntı: ", "Import failed. Details: ") + _log.FilePath;
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
            : Loc.T(
                $"{changeSet.SafeChangeCount} güvenli değişiklik, {changeSet.ConflictCount} çakışma, " +
                $"{changeSet.ErrorCount} hata, {changeSet.UnchangedCount} değişmemiş.",
                $"{Loc.N(changeSet.SafeChangeCount, "güvenli değişiklik", "safe change", "safe changes")}, " +
                $"{Loc.N(changeSet.ConflictCount, "çakışma", "conflict", "conflicts")}, " +
                $"{Loc.N(changeSet.ErrorCount, "hata", "error", "errors")}, " +
                $"{changeSet.UnchangedCount} unchanged.");
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
                : Loc.T(
                    $"{outcome.Value.AppliedCount} değer uygulandı, " +
                    $"{outcome.Value.FailedCount} başarısız, {outcome.Value.SkippedCount} atlandı.",
                    $"{Loc.N(outcome.Value.AppliedCount, "değer", "value", "values")} applied, " +
                    $"{outcome.Value.FailedCount} failed, {outcome.Value.SkippedCount} skipped.");

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
            StatusMessage = Loc.T("Uygulama başarısız. Ayrıntı: ", "Apply failed. Details: ") + _log.FilePath;
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

            StatusMessage = Loc.T(
                $"Geri alma önizlemesi: {outcome.Value.SafeCount} güvenli, " +
                $"{outcome.Value.ConflictCount} çakışma, " +
                $"{outcome.Value.UnavailableCount} kullanılamaz.",
                $"Undo preview: {outcome.Value.SafeCount} safe, " +
                $"{Loc.N(outcome.Value.ConflictCount, "çakışma", "conflict", "conflicts")}, " +
                $"{outcome.Value.UnavailableCount} unavailable.");
        }
        catch (Exception exception)
        {
            _log.Error("Geri alma önizlemesi başarısız.", exception);
            StatusMessage = Loc.T("Geri alma önizlemesi başarısız. Ayrıntı: ", "Undo preview failed. Details: ") + _log.FilePath;
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
                : Loc.T(
                    $"{outcome.Value.AppliedCount} değer geri alındı, " +
                    $"{outcome.Value.FailedCount} başarısız.",
                    $"{Loc.N(outcome.Value.AppliedCount, "değer", "value", "values")} undone, " +
                    $"{outcome.Value.FailedCount} failed.");

            _undoPreview = null;
            UndoRows.Clear();
            RaiseAll(nameof(HasUndoPreview), nameof(UndoSafeCount), nameof(UndoConflictCount),
                nameof(UndoUnavailableCount), nameof(UndoPreviewData));

            await LoadOperationsAsync();
        }
        catch (Exception exception)
        {
            _log.Error("Geri alma başarısız.", exception);
            StatusMessage = Loc.T("Geri alma başarısız. Ayrıntı: ", "Undo failed. Details: ") + _log.FilePath;
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
        BusyMessage = Loc.T("Durduruluyor", "Stopping");
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
        AddFromAssemblyCommand.RaiseCanExecuteChanged();
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
            IPdmAssemblyReader assemblyReader,
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
            AssemblyReader = assemblyReader;
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

        public IPdmAssemblyReader AssemblyReader { get; }

        public ImportService Import { get; }

        public ApplyService Apply { get; }

        public UndoService Undo { get; }

        public IOperationJournal Journal { get; }

        public IReadOnlyList<PdmVariableDefinition> Variables { get; }

        public string FolderPath { get; }
    }
}
