using System.Collections.ObjectModel;
using Dbms.App.Branding;
using Dbms.Core.Abstractions;
using Dbms.Core.Models;
using Dbms.Core.Services;
using Dbms.Infrastructure.Storage;
using Dbms.App.Updates;

namespace Dbms.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IDatabaseProviderRegistry providerRegistry;
    private readonly IProfileStore profileStore;
    private readonly HistoryStore historyStore;
    private readonly SchemaComparisonService schemaComparisonService = new();
    private readonly DataComparisonService dataComparisonService = new();
    private readonly TransferService transferService = new();
    private readonly AppUpdateService updateService;
    private string currentSection = "connections";
    private ConnectionProfile sourceDraft;
    private ConnectionProfile destinationDraft;
    private ConnectionProfile? selectedSavedProfile;
    private TableComparisonRow? selectedTableComparison;
    private string? selectedCommonTableName;
    private bool isBusy;
    private string statusMessage = GetInitialStatusMessage();
    private string structureSummary = "No structure comparison has been run.";
    private string dataSummary = "Select a matching table and compare its rows.";
    private string dataMessage = string.Empty;
    private string transferSummary = "Build a transfer plan after comparing structure.";
    private bool transferPlanReady;
    private bool confirmTransfer;
    private string progressSummary = "No transfer has run in this session.";
    private string transferEndpoints = "Configure source and destination connections first.";
    private TransferPlan? transferPlan;
    private string updateStatus = "Updates activate after installation.";
    private bool updateAvailable;
    private string? availableUpdateVersion;

    public MainWindowViewModel(
        IDatabaseProviderRegistry providerRegistry,
        IProfileStore profileStore,
        HistoryStore historyStore,
        AppUpdateService? updateService = null)
    {
        this.providerRegistry = providerRegistry;
        this.profileStore = profileStore;
        this.historyStore = historyStore;
        this.updateService = updateService ?? new AppUpdateService();
        updateStatus = this.updateService.IsStoreInstalled
            ? "Updates are managed by Microsoft Store."
            : updateStatus;
        sourceDraft = CreateSourceProfile();
        destinationDraft = CreateDestinationProfile();

        EngineOptions = Enum.GetValues<DatabaseEngine>();
        ShowConnectionsCommand = new RelayCommand(() => CurrentSection = "connections");
        ShowStructureCommand = new RelayCommand(() => CurrentSection = "structure");
        ShowDataCommand = new RelayCommand(() => CurrentSection = "data");
        ShowTransferCommand = new RelayCommand(() => CurrentSection = "transfer");
        ShowHistoryCommand = new RelayCommand(() => CurrentSection = "history");
        TestSourceCommand = new AsyncCommand(() => TestConnectionAsync(SourceDraft), () => CanInteract, HandleError);
        TestDestinationCommand = new AsyncCommand(() => TestConnectionAsync(DestinationDraft), () => CanInteract, HandleError);
        SaveSourceProfileCommand = new AsyncCommand(() => SaveProfileAsync(SourceDraft), () => CanInteract, HandleError);
        SaveDestinationProfileCommand = new AsyncCommand(() => SaveProfileAsync(DestinationDraft), () => CanInteract, HandleError);
        LoadAsSourceCommand = new RelayCommand(() => LoadSelectedProfile(true), () => SelectedSavedProfile is not null && CanInteract);
        LoadAsDestinationCommand = new RelayCommand(() => LoadSelectedProfile(false), () => SelectedSavedProfile is not null && CanInteract);
        DeleteSelectedProfileCommand = new AsyncCommand(DeleteSelectedProfileAsync, () => SelectedSavedProfile is not null && CanInteract, HandleError);
        ResetDraftsCommand = new RelayCommand(ResetDrafts, () => CanInteract);
        CompareStructureCommand = new AsyncCommand(CompareStructureAsync, () => CanInteract, HandleError);
        CompareDataCommand = new AsyncCommand(CompareDataAsync, () => CanInteract && SelectedTableComparison?.IsTransferCandidate == true, HandleError);
        SelectAllTablesCommand = new RelayCommand(SelectAllTables, () => CanInteract && TableComparisons.Count > 0);
        BuildTransferPlanCommand = new AsyncCommand(BuildTransferPlanAsync, () => CanInteract && TableComparisons.Any(row => row.IsSelected && row.IsTransferCandidate), HandleError);
        ExecuteTransferCommand = new AsyncCommand(ExecuteTransferAsync, () => CanExecuteTransfer, HandleError);
        CancelTransferCommand = new RelayCommand(() => transferCancellation?.Cancel(), () => IsBusy && transferCancellation is not null);
        CheckForUpdatesCommand = new AsyncCommand(CheckForUpdatesAsync, () => CanInteract, HandleError);
        ApplyUpdateCommand = new AsyncCommand(ApplyUpdateAsync, () => CanInteract && UpdateAvailable, HandleError);
    }

    public IReadOnlyList<DatabaseEngine> EngineOptions { get; }
    public string BrandSuiteName => BrandIdentity.SuiteName;
    public string BrandProductName => BrandIdentity.ProductName;
    public string BrandProductDescriptor => BrandIdentity.ProductDescriptor;
    public string BrandWindowTitle => BrandIdentity.WindowTitle;
    public string BrandTagline => BrandIdentity.Tagline;
    public string BrandShortTagline => BrandIdentity.ShortTagline;
    public string BrandWorkspaceLabel => BrandIdentity.WorkspaceLabel;
    public string BrandSafetyLabel => BrandIdentity.SafetyLabel;
    public string CurrentVersion => updateService.CurrentVersion;
    public bool ShowAppUpdateControls => !updateService.IsStoreInstalled;
    public string UpdateStatus
    {
        get => updateStatus;
        private set => SetProperty(ref updateStatus, value);
    }

    public bool UpdateAvailable
    {
        get => updateAvailable;
        private set
        {
            if (SetProperty(ref updateAvailable, value)) ApplyUpdateCommand.RaiseCanExecuteChanged();
        }
    }

    public string? AvailableUpdateVersion
    {
        get => availableUpdateVersion;
        private set => SetProperty(ref availableUpdateVersion, value);
    }

    public ObservableCollection<ConnectionProfile> SavedProfiles { get; } = new();
    public ObservableCollection<TableComparisonRow> TableComparisons { get; } = new();
    public ObservableCollection<RowDifferenceRow> RowDifferences { get; } = new();
    public ObservableCollection<TransferTableRow> TransferPlanRows { get; } = new();
    public ObservableCollection<JobHistoryEntry> History { get; } = new();
    public ObservableCollection<string> DiagnosticLog { get; } = new();

    public RelayCommand ShowConnectionsCommand { get; }
    public RelayCommand ShowStructureCommand { get; }
    public RelayCommand ShowDataCommand { get; }
    public RelayCommand ShowTransferCommand { get; }
    public RelayCommand ShowHistoryCommand { get; }
    public AsyncCommand TestSourceCommand { get; }
    public AsyncCommand TestDestinationCommand { get; }
    public AsyncCommand SaveSourceProfileCommand { get; }
    public AsyncCommand SaveDestinationProfileCommand { get; }
    public RelayCommand LoadAsSourceCommand { get; }
    public RelayCommand LoadAsDestinationCommand { get; }
    public AsyncCommand DeleteSelectedProfileCommand { get; }
    public RelayCommand ResetDraftsCommand { get; }
    public AsyncCommand CompareStructureCommand { get; }
    public AsyncCommand CompareDataCommand { get; }
    public RelayCommand SelectAllTablesCommand { get; }
    public AsyncCommand BuildTransferPlanCommand { get; }
    public AsyncCommand ExecuteTransferCommand { get; }
    public RelayCommand CancelTransferCommand { get; }
    public AsyncCommand CheckForUpdatesCommand { get; }
    public AsyncCommand ApplyUpdateCommand { get; }

    public ConnectionProfile SourceDraft
    {
        get => sourceDraft;
        private set => SetProperty(ref sourceDraft, value);
    }

    public ConnectionProfile DestinationDraft
    {
        get => destinationDraft;
        private set => SetProperty(ref destinationDraft, value);
    }

    public ConnectionProfile? SelectedSavedProfile
    {
        get => selectedSavedProfile;
        set
        {
            if (SetProperty(ref selectedSavedProfile, value))
            {
                LoadAsSourceCommand.RaiseCanExecuteChanged();
                LoadAsDestinationCommand.RaiseCanExecuteChanged();
                DeleteSelectedProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public TableComparisonRow? SelectedTableComparison
    {
        get => selectedTableComparison;
        set
        {
            if (SetProperty(ref selectedTableComparison, value))
            {
                SelectedCommonTableName = value?.IsTransferCandidate == true ? value.Name : null;
                OnPropertyChanged(nameof(SelectedTableDetails));
                CompareDataCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SelectedCommonTableName
    {
        get => selectedCommonTableName;
        set
        {
            if (SetProperty(ref selectedCommonTableName, value))
            {
                SelectedTableComparison = TableComparisons.FirstOrDefault(row => string.Equals(row.Name, value, StringComparison.OrdinalIgnoreCase));
                CompareDataCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CurrentSection
    {
        get => currentSection;
        private set
        {
            if (SetProperty(ref currentSection, value))
            {
                OnPropertyChanged(nameof(IsConnectionsSection));
                OnPropertyChanged(nameof(IsStructureSection));
                OnPropertyChanged(nameof(IsDataSection));
                OnPropertyChanged(nameof(IsTransferSection));
                OnPropertyChanged(nameof(IsHistorySection));
            }
        }
    }

    public bool IsConnectionsSection => CurrentSection == "connections";
    public bool IsStructureSection => CurrentSection == "structure";
    public bool IsDataSection => CurrentSection == "data";
    public bool IsTransferSection => CurrentSection == "transfer";
    public bool IsHistorySection => CurrentSection == "history";

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanInteract));
                OnPropertyChanged(nameof(CanExecuteTransfer));
                RaiseCommandStates();
            }
        }
    }

    public bool CanInteract => !IsBusy;
    public bool CanExecuteTransfer => !IsBusy && TransferPlanReady && ConfirmTransfer && transferPlan?.CanExecute == true;

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string StructureSummary
    {
        get => structureSummary;
        private set => SetProperty(ref structureSummary, value);
    }

    public string DataSummary
    {
        get => dataSummary;
        private set => SetProperty(ref dataSummary, value);
    }

    public string DataMessage
    {
        get => dataMessage;
        private set => SetProperty(ref dataMessage, value);
    }

    public string TransferSummary
    {
        get => transferSummary;
        private set => SetProperty(ref transferSummary, value);
    }

    public string ProgressSummary
    {
        get => progressSummary;
        private set => SetProperty(ref progressSummary, value);
    }

    public string TransferEndpoints
    {
        get => transferEndpoints;
        private set => SetProperty(ref transferEndpoints, value);
    }

    public bool TransferPlanReady
    {
        get => transferPlanReady;
        private set
        {
            if (SetProperty(ref transferPlanReady, value))
            {
                OnPropertyChanged(nameof(CanExecuteTransfer));
                ExecuteTransferCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ConfirmTransfer
    {
        get => confirmTransfer;
        set
        {
            if (SetProperty(ref confirmTransfer, value))
            {
                OnPropertyChanged(nameof(CanExecuteTransfer));
                ExecuteTransferCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DiagnosticText => DiagnosticLog.Count == 0
        ? "No diagnostic entries."
        : string.Join(Environment.NewLine, DiagnosticLog.TakeLast(12));

    public string SelectedTableDetails
    {
        get
        {
            if (SelectedTableComparison is null) return "Select a table to inspect its source and destination columns.";
            var comparison = SelectedTableComparison.Comparison;
            return comparison.Differences.Count == 0
                ? $"{comparison.Name}: source and destination metadata match."
                : string.Join(Environment.NewLine, comparison.Differences);
        }
    }

    private CancellationTokenSource? transferCancellation;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var profile in await profileStore.LoadAsync(cancellationToken)) SavedProfiles.Add(profile);
            foreach (var entry in await historyStore.LoadAsync(cancellationToken)) History.Add(entry);
            if (SavedProfiles.Count > 0)
            {
                SourceDraft = SavedProfiles[0].Clone();
                DestinationDraft = SavedProfiles.Count > 1 ? SavedProfiles[1].Clone() : SavedProfiles[0].Clone();
            }

            StatusMessage = SavedProfiles.Count == 0
                ? GetInitialStatusMessage()
                : $"Loaded {SavedProfiles.Count} saved connection profile(s).";

            if (updateService.IsInstalled) _ = CheckForUpdatesAsync(silent: true, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            HandleError(exception);
        }
    }

    private async Task TestConnectionAsync(ConnectionProfile profile)
    {
        IsBusy = true;
        try
        {
            var provider = providerRegistry.Get(profile.Engine);
            await using var connection = await provider.OpenConnectionAsync(profile);
            StatusMessage = $"Connected to {provider.DisplayName} at {SafeLocation(profile)}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveProfileAsync(ConnectionProfile draft)
    {
        IsBusy = true;
        try
        {
            if (draft.Id == Guid.Empty) draft.Id = Guid.NewGuid();
            await profileStore.SaveAsync(draft);
            var existing = SavedProfiles.FirstOrDefault(profile => profile.Id == draft.Id);
            if (existing is not null) SavedProfiles.Remove(existing);
            SavedProfiles.Insert(0, draft.Clone());
            StatusMessage = $"Saved profile '{draft.DisplayName}'. Passwords are stored only when the secure-save option is enabled.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteSelectedProfileAsync()
    {
        if (SelectedSavedProfile is null) return;
        IsBusy = true;
        try
        {
            var profile = SelectedSavedProfile;
            await profileStore.DeleteAsync(profile.Id);
            SavedProfiles.Remove(profile);
            SelectedSavedProfile = null;
            StatusMessage = $"Deleted profile '{profile.DisplayName}'.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadSelectedProfile(bool source)
    {
        if (SelectedSavedProfile is null) return;
        if (source) SourceDraft = SelectedSavedProfile.Clone();
        else DestinationDraft = SelectedSavedProfile.Clone();
        StatusMessage = $"Loaded '{SelectedSavedProfile.DisplayName}' as {(source ? "source" : "destination")}.";
        OnPropertyChanged(nameof(SourceDraft));
        OnPropertyChanged(nameof(DestinationDraft));
    }

    private void ResetDrafts()
    {
        SourceDraft = CreateSourceProfile();
        DestinationDraft = CreateDestinationProfile();
        StatusMessage = GetInitialStatusMessage("Connection drafts reset. ");
    }

    private Task CheckForUpdatesAsync() => CheckForUpdatesAsync(silent: false);

    private async Task CheckForUpdatesAsync(bool silent, CancellationToken cancellationToken = default)
    {
        if (!silent) IsBusy = true;
        try
        {
            if (!silent) UpdateStatus = "Checking for updates…";
            var result = await updateService.CheckForUpdatesAsync(cancellationToken);
            UpdateAvailable = result.IsAvailable;
            AvailableUpdateVersion = result.AvailableVersion;
            UpdateStatus = result.Message;
            if (!silent && result.IsAvailable)
            {
                StatusMessage = $"DataBridge {result.AvailableVersion} is ready. Review the update before installing it.";
            }
        }
        finally
        {
            if (!silent) IsBusy = false;
        }
    }

    private async Task ApplyUpdateAsync()
    {
        if (!UpdateAvailable) return;
        IsBusy = true;
        try
        {
            UpdateStatus = $"Downloading {AvailableUpdateVersion}…";
            var progress = new Progress<int>(value => UpdateStatus = $"Downloading {AvailableUpdateVersion} · {value}%");
            await updateService.DownloadAndRestartAsync(progress);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CompareStructureAsync()
    {
        IsBusy = true;
        try
        {
            var sourceProvider = providerRegistry.Get(SourceDraft.Engine);
            var destinationProvider = providerRegistry.Get(DestinationDraft.Engine);
            await using var sourceConnection = await sourceProvider.OpenConnectionAsync(SourceDraft);
            await using var destinationConnection = await destinationProvider.OpenConnectionAsync(DestinationDraft);
            var sourceSchemaTask = sourceProvider.ReadSchemaAsync(sourceConnection);
            var destinationSchemaTask = destinationProvider.ReadSchemaAsync(destinationConnection);
            await Task.WhenAll(sourceSchemaTask, destinationSchemaTask);
            var result = schemaComparisonService.Compare(sourceSchemaTask.Result, destinationSchemaTask.Result);

            TableComparisons.Clear();
            foreach (var comparison in result.Tables) TableComparisons.Add(new TableComparisonRow(comparison));
            SelectedTableComparison = TableComparisons.FirstOrDefault(row => row.IsTransferCandidate);
            StructureSummary = $"{result.Tables.Count:N0} table(s) compared · {result.DifferenceCount:N0} table(s) with structural differences.";
            TransferPlanReady = false;
            ConfirmTransfer = false;
            StatusMessage = $"Structure comparison completed: {result.Tables.Count:N0} table(s) inspected.";
            CurrentSection = "structure";
            RaiseCommandStates();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CompareDataAsync()
    {
        if (SelectedTableComparison?.Comparison.SourceTable is null || SelectedTableComparison.Comparison.DestinationTable is null) return;
        IsBusy = true;
        try
        {
            var sourceProvider = providerRegistry.Get(SourceDraft.Engine);
            var destinationProvider = providerRegistry.Get(DestinationDraft.Engine);
            var result = await dataComparisonService.CompareAsync(
                sourceProvider, SourceDraft, SelectedTableComparison.Comparison.SourceTable,
                destinationProvider, DestinationDraft, SelectedTableComparison.Comparison.DestinationTable,
                maxRowsToInspect: 10_000, maxDifferences: 250);

            RowDifferences.Clear();
            foreach (var difference in result.Differences) RowDifferences.Add(new RowDifferenceRow(difference));
            DataSummary = result.IsReliable
                ? $"Source {result.SourceRowCount:N0} row(s) · Destination {result.DestinationRowCount:N0} row(s) · Source-only {result.SourceOnlyCount:N0} · Destination-only {result.DestinationOnlyCount:N0} · Different {result.DifferentCount:N0}"
                : "Row comparison is not reliable for this table.";
            DataMessage = result.Message;
            StatusMessage = $"Data comparison completed for {SelectedTableComparison.Name}.";
            CurrentSection = "data";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SelectAllTables()
    {
        foreach (var table in TableComparisons) table.IsSelected = table.IsTransferCandidate;
        StatusMessage = "Selected all tables that exist on both sides. Tables with structural conflicts remain visible in the plan.";
        BuildTransferPlanCommand.RaiseCanExecuteChanged();
    }

    private async Task BuildTransferPlanAsync()
    {
        var selections = TableComparisons
            .Where(row => row.IsSelected && row.Comparison.SourceTable is not null && row.Comparison.DestinationTable is not null)
            .Select(row => new TransferTableSelection
            {
                SourceTable = row.Comparison.SourceTable!,
                DestinationTable = row.Comparison.DestinationTable!
            })
            .ToArray();
        if (selections.Length == 0) return;

        IsBusy = true;
        try
        {
            var sourceProvider = providerRegistry.Get(SourceDraft.Engine);
            var destinationProvider = providerRegistry.Get(DestinationDraft.Engine);
            TransferEndpoints = $"{SafeLocation(SourceDraft)}  →  {SafeLocation(DestinationDraft)}";
            transferPlan = await transferService.BuildPlanAsync(sourceProvider, SourceDraft, destinationProvider, DestinationDraft, selections);
            TransferPlanRows.Clear();
            foreach (var tablePlan in transferPlan.Tables) TransferPlanRows.Add(new TransferTableRow(tablePlan));
            TransferSummary = $"{transferPlan.Tables.Count:N0} table(s) planned · {transferPlan.RowsToInsert:N0} row(s) to insert · {transferPlan.RowsToSkip:N0} existing row(s) to skip · {transferPlan.Conflicts:N0} conflict(s).";
            TransferPlanReady = true;
            ConfirmTransfer = false;
            CurrentSection = "transfer";
            StatusMessage = transferPlan.CanExecute
                ? "Transfer preview is ready. Review the mappings and explicitly confirm before inserting rows."
                : "Transfer preview contains a blocking conflict. Resolve the issue before inserting rows.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteTransferAsync()
    {
        if (transferPlan is null || !transferPlan.CanExecute || !ConfirmTransfer) return;
        IsBusy = true;
        transferCancellation = new CancellationTokenSource();
        CancelTransferCommand.RaiseCanExecuteChanged();
        try
        {
            var sourceProvider = providerRegistry.Get(SourceDraft.Engine);
            var destinationProvider = providerRegistry.Get(DestinationDraft.Engine);
            var progress = new Progress<TransferProgress>(value =>
            {
                ProgressSummary = $"{value.TableName}: processed {value.ProcessedRows:N0}/{value.TotalRows:N0} · inserted {value.InsertedRows:N0} · skipped {value.SkippedRows:N0} · failed {value.FailedRows:N0} · {value.Message}";
                StatusMessage = ProgressSummary;
            });
            var result = await transferService.ExecuteAsync(sourceProvider, SourceDraft, destinationProvider, DestinationDraft, transferPlan, progress, transferCancellation.Token);
            ProgressSummary = $"{result.Message} Processed {result.ProcessedRows:N0}; inserted {result.InsertedRows:N0}; skipped {result.SkippedRows:N0}; failed {result.FailedRows:N0}.";
            StatusMessage = ProgressSummary;
            await historyStore.AddAsync(new JobHistoryEntry(DateTimeOffset.Now, SafeLocation(SourceDraft), SafeLocation(DestinationDraft),
                string.Join(", ", transferPlan.Tables.Select(table => table.SourceTable.Name)), result.InsertedRows,
                result.SkippedRows, result.FailedRows, result.Completed, result.Message));
            History.Insert(0, new JobHistoryEntry(DateTimeOffset.Now, SafeLocation(SourceDraft), SafeLocation(DestinationDraft),
                string.Join(", ", transferPlan.Tables.Select(table => table.SourceTable.Name)), result.InsertedRows,
                result.SkippedRows, result.FailedRows, result.Completed, result.Message));
            ConfirmTransfer = false;
        }
        finally
        {
            transferCancellation.Dispose();
            transferCancellation = null;
            CancelTransferCommand.RaiseCanExecuteChanged();
            IsBusy = false;
        }
    }

    private void HandleError(Exception exception)
    {
        var message = Sanitize(exception.Message);
        StatusMessage = $"Operation failed: {message}";
        DiagnosticLog.Insert(0, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [{exception.GetType().Name}] {message}");
        while (DiagnosticLog.Count > 50) DiagnosticLog.RemoveAt(DiagnosticLog.Count - 1);
        OnPropertyChanged(nameof(DiagnosticText));
    }

    private void RaiseCommandStates()
    {
        TestSourceCommand.RaiseCanExecuteChanged();
        TestDestinationCommand.RaiseCanExecuteChanged();
        SaveSourceProfileCommand.RaiseCanExecuteChanged();
        SaveDestinationProfileCommand.RaiseCanExecuteChanged();
        LoadAsSourceCommand.RaiseCanExecuteChanged();
        LoadAsDestinationCommand.RaiseCanExecuteChanged();
        DeleteSelectedProfileCommand.RaiseCanExecuteChanged();
        ResetDraftsCommand.RaiseCanExecuteChanged();
        CompareStructureCommand.RaiseCanExecuteChanged();
        CompareDataCommand.RaiseCanExecuteChanged();
        SelectAllTablesCommand.RaiseCanExecuteChanged();
        BuildTransferPlanCommand.RaiseCanExecuteChanged();
        ExecuteTransferCommand.RaiseCanExecuteChanged();
        CheckForUpdatesCommand.RaiseCanExecuteChanged();
        ApplyUpdateCommand.RaiseCanExecuteChanged();
    }

    private const bool IsDevelopmentBuild =
#if DEBUG
        true;
#else
        false;
#endif

    private static bool UseLaragonDemoDefaults =>
        IsDevelopmentBuild || string.Equals(
            Environment.GetEnvironmentVariable("ORBIT_DATABRIDGE_DEMO"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    private static ConnectionProfile CreateSourceProfile() => UseLaragonDemoDefaults
        ? CreateLaragonProfile("BMS source", "bms")
        : CreateEmptyProfile("Source system");

    private static ConnectionProfile CreateDestinationProfile() => UseLaragonDemoDefaults
        ? CreateLaragonProfile("Booking destination", "booking")
        : CreateEmptyProfile("Destination system");

    private static string GetInitialStatusMessage(string prefix = "") => UseLaragonDemoDefaults
        ? $"{prefix}Laragon demo connections are prefilled: bms → booking."
        : $"{prefix}Ready. Add a source and destination connection to begin.";

    private static ConnectionProfile CreateEmptyProfile(string name) => new()
    {
        Name = name,
        Engine = DatabaseEngine.MySql,
        Server = "localhost",
        Port = 3306
    };

    private static ConnectionProfile CreateLaragonProfile(string name, string database) => new()
    {
        Name = name,
        Engine = DatabaseEngine.MySql,
        Server = "127.0.0.1",
        Port = 3306,
        Database = database,
        Username = "root"
    };

    private static string SafeLocation(ConnectionProfile profile) => profile.Engine == DatabaseEngine.Sqlite
        ? profile.Database
        : $"{profile.Server}:{(profile.Port > 0 ? profile.Port : DefaultPort(profile.Engine))}/{profile.Database}";

    private static int DefaultPort(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.PostgreSql => 5432,
        DatabaseEngine.MySql => 3306,
        _ => 0
    };

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown error.";
        var sanitized = System.Text.RegularExpressions.Regex.Replace(message, "(?i)(password|pwd)\\s*=\\s*[^;\\s]+", "$1=***");
        return sanitized.Length > 500 ? sanitized[..500] + "…" : sanitized;
    }
}
