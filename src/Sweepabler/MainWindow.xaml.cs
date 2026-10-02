using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Media;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ProperAppUpdater.Models;
using ProperAppUpdater.Services;
using ProperAppUpdater.ViewModels;

namespace ProperAppUpdater;

public partial class MainWindow : Window
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly CatalogService _catalogService = new();
    private readonly InstalledAppScanner _installedAppScanner = new();
    private readonly HistoryService _historyService = new();
    private readonly UnsupportedAppReportService _unsupportedAppReportService = new();
    private readonly UnsupportedIgnoreService _unsupportedIgnoreService = new();
    private readonly UpdateFailureBlockService _failureBlockService = new();
    private readonly PackageManagerUpdateScanner _packageManagerUpdateScanner = new();
    private readonly UpdaterMaintenanceService _updaterMaintenanceService = new();
    private readonly LocalCleanupService _localCleanupService = new();
    private readonly UpdateExecutor _updateExecutor;
    private readonly UpdateProviderFactory _providerFactory;

    private bool _isBusy;
    private bool _isClosing;
    private readonly CancellationTokenSource _appCts = new();
    private CancellationTokenSource? _workCts;
    private TaskCompletionSource? _workFinished;
    private bool _closeReady;

    public MainWindow()
    {
        InitializeComponent();

        _providerFactory = new UpdateProviderFactory(
            new GitHubReleaseProvider(_httpClient),
            new ElectronYamlReleaseProvider(_httpClient),
            new OfficialWebPageProvider(_httpClient),
            new AppcastReleaseProvider(_httpClient));
        _updateExecutor = new UpdateExecutor(_httpClient);

        DataContext = this;
        ApplyLocalization();
    }

    public ObservableCollection<UpdateCandidateViewModel> Updates { get; } = new();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        SystemThemeService.Apply(System.Windows.Application.Current.Resources);
        ApplyLocalization();
        PlayLaunchFinished();
        _ = CleanupLocalDataAsync();
    }

    public void ShowRecoverableError(string message)
    {
        SetStatus(Text.Get("RecoverableError"));
        FooterText.Text = message;
        _isBusy = false;
        UpdateButtons();
    }

    private async void FindButton_Click(object sender, RoutedEventArgs e)
    {
        await FindUpdatesAsync();
    }

    private async void SweepButton_Click(object sender, RoutedEventArgs e)
    {
        await UpdateSelectedAsync();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CandidateCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateButtons();
    }

    private async Task FindUpdatesAsync()
    {
        var cancellationToken = StartWork(Text.Get("OldSearching"));
        var selfCheck = Task.CompletedTask;
        try
        {
            Updates.Clear();
            selfCheck = CheckSelfUpdateAvailabilityAsync(cancellationToken);
            await SweepUpdaterToolsAsync(cancellationToken);

            FooterText.Text = Text.Get("InstalledAppsChecking");

            var catalog = await _catalogService.LoadAsync(cancellationToken);
            var registryApps = (await Task.Run(_installedAppScanner.Scan, cancellationToken))
                .Where(app => !AppSelfIdentity.IsSelf(app.DisplayName))
                .ToList();
            var installedApps = registryApps;
            var ignoredNames = (await _unsupportedIgnoreService.LoadAsync(cancellationToken))
                .Select(AppMatcher.Normalize)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var blockedUpdateKeys = await _failureBlockService.LoadBlockedKeysAsync(cancellationToken);
            var checkedCount = 0;
            var failedCount = 0;
            var blockedCount = 0;

            using var checkSlots = new SemaphoreSlim(4);
            var checks = new List<Task>();
            var packageManagerTask = _packageManagerUpdateScanner.ScanWithInventoryAsync(cancellationToken);
            try
            {
                foreach (var entry in catalog)
                {
                    var installedMatch = AppMatcher.FindInstalledMatch(entry, installedApps);
                    if (installedMatch is null || AppSelfIdentity.IsSelf(entry, installedMatch.DisplayName))
                    {
                        continue;
                    }

                    if (UpdateSkipPolicy.ShouldSkip(entry, installedMatch, out _))
                    {
                        continue;
                    }

                    if (IsIgnored(entry.Name, installedMatch.DisplayName, ignoredNames))
                    {
                        continue;
                    }

                    checkedCount++;
                    FooterText.Text = string.Format(Text.Get("CheckingApp"), entry.Name);
                    cancellationToken.ThrowIfCancellationRequested();
                    var candidate = new UpdateCandidateViewModel(entry, installedMatch)
                    {
                        Status = "...",
                        LatestVersion = "..."
                    };

                    checks.Add(CheckAndAddAsync(candidate));
                }
            }
            finally
            {
                // Drain every started probe before disposing slots or completing cancellation.
                await Task.WhenAll(checks.Append(packageManagerTask));
            }

            async Task CheckAndAddAsync(UpdateCandidateViewModel candidate)
            {
                await checkSlots.WaitAsync(cancellationToken);
                try
                {
                    if (!await CheckCandidateAsync(candidate, cancellationToken)) { failedCount++; return; }
                    if (!candidate.UpdateAvailable) return;
                    if (_failureBlockService.IsBlocked(candidate, blockedUpdateKeys)) { blockedCount++; return; }
                    candidate.PropertyChanged += Candidate_PropertyChanged;
                    Updates.Add(candidate);
                }
                finally { checkSlots.Release(); }
            }

            FooterText.Text = Text.Get("PackageManagersChecking");
            var packageManagerScan = await packageManagerTask;
            var packageManagerCount = AddPackageManagerCandidates(packageManagerScan.Updates, ignoredNames, blockedUpdateKeys, registryApps);
            RefreshFamilies();
            var unsupportedCount = await _unsupportedAppReportService.WriteAsync(
                registryApps,
                catalog,
                packageManagerScan.ManagedDisplayNames
                    .Concat(packageManagerScan.Updates.Select(update => update.DisplayName)),
                cancellationToken);

            var oldCount = Updates.Count;
            var message = oldCount == 0
                ? Text.Get("CleanNoOld")
                : string.Format(Text.Get("OldAppsFound"), oldCount);

            SetStatus(message);
            FooterText.Text = failedCount == 0
                ? string.Format(Text.Get("ScanFooterOk"), checkedCount, packageManagerCount, unsupportedCount)
                : string.Format(Text.Get("ScanFooterErrors"), checkedCount, packageManagerCount, failedCount, unsupportedCount);
            if (blockedCount > 0)
            {
                FooterText.Text = $"{FooterText.Text} {string.Format(Text.Get("ScanFooterBlocked"), blockedCount)}";
            }
        }
        catch (OperationCanceledException) when (_isClosing)
        {
        }
        catch (Exception exception)
        {
            LogAndShow(Text.Get("CheckCrashed"), exception);
        }
        finally
        {
            await selfCheck;
            EndWork();
        }
    }

    private async Task SweepUpdaterToolsAsync(CancellationToken cancellationToken)
    {
        if (App.Settings.LastMaintenanceUtc is { } last && DateTimeOffset.UtcNow - last >= TimeSpan.Zero &&
            DateTimeOffset.UtcNow - last < TimeSpan.FromMinutes(30)) return;
        SetStatus(Text.Get("UpdaterSweepStatus"));
        FooterText.Text = Text.Get("UpdaterSweepFooter");

        var progress = new Progress<string>(message =>
        {
            FooterText.Text = message;
        });

        var result = await _updaterMaintenanceService.SweepAsync(progress, cancellationToken);

        SetStatus(Text.Get("UpdaterFreshStatus"));
        FooterText.Text = result.Summary;
        if (result.Checks.All(check => check.WasSkipped || check.IsSuccess))
        {
            App.Settings.LastMaintenanceUtc = DateTimeOffset.UtcNow;
            await new AppSettingsService().SaveAsync(App.Settings, cancellationToken);
        }
    }

    private async Task<bool> CheckCandidateAsync(UpdateCandidateViewModel candidate, CancellationToken cancellationToken)
    {
        try
        {
            var release = await _providerFactory
                .Resolve(candidate.Catalog)
                .GetLatestAsync(candidate.Catalog, cancellationToken);
            ApplyRelease(candidate, release);
            if (candidate.UpdateAvailable && candidate.DownloadSizeBytes is null)
                candidate.DownloadSizeBytes = await _updateExecutor.GetDownloadSizeAsync(candidate.Catalog, candidate.DownloadUrl, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            candidate.LatestVersion = "?";
            candidate.UpdateAvailable = false;
            candidate.IsSelected = false;
            candidate.Status = Text.Get("Error");
            FooterText.Text = $"{candidate.Name}: {exception.Message}";
            CrashLogger.Log(exception);
            return false;
        }
    }

    private async Task UpdateSelectedAsync()
    {
        var selected = Updates
            .Where(candidate => candidate.IsSelected && candidate.UpdateAvailable && !string.IsNullOrWhiteSpace(candidate.DownloadUrl))
            .ToList();

        if (selected.Count == 0)
        {
            SetStatus(Text.Get("NoSelection"));
            return;
        }

        var cancellationToken = StartWork(Text.Get("Sweeping"));
        SweepAssistantWindow? assistant = null;
        try
        {
            assistant = new SweepAssistantWindow
            {
                Owner = this
            };
            assistant.Show();

            var removedShortcuts = 0;
            var updatedCount = 0;
            var failedCount = 0;
            var skippedCount = 0;
            var trustWarningCount = 0;

            var remaining = UpdateDownloadQueue.OrderCandidates(selected).ToList();
            await using var downloads = new UpdateDownloadQueue(remaining, _updateExecutor,
                (candidate, message) => { if (!_isClosing) candidate.Status = message; }, cancellationToken);
            while (remaining.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = await downloads.NextAsync(remaining);
                remaining.Remove(candidate);
                var preparedTask = downloads.GetPrepared(candidate);
                var prepared = preparedTask is null ? null : await preparedTask;
                // Failed downloads are recorded without closing an app that cannot be updated.
                if (prepared is { Error.Length: > 0 })
                {
                    failedCount++;
                    await _historyService.AppendAsync(prepared, cancellationToken);
                    var failure = await _failureBlockService.RecordFailureAsync(candidate, prepared, cancellationToken);
                    candidate.Status = failure.IsBlocked ? Text.Get("BlockedStatus") :
                        string.Format(Text.Get("FailureAttemptStatus"), failure.FailureCount, UpdateFailureBlockService.FailureThreshold);
                    if (failure.IsBlocked) { candidate.IsSelected = false; candidate.UpdateAvailable = false; }
                    FooterText.Text = prepared.Error;
                    continue;
                }
                SetAssistantWorking();
                SetAssistantTarget(candidate.Name);

                var blockingProcesses = RunningAppGuard.FindBlockingProcesses(candidate);
                if (blockingProcesses.Count > 0)
                {
                    var blockingName = blockingProcesses[0].ProcessName;

                    // Never offer to close Süpürücü itself; keep the plain skip behaviour.
                    if (candidate.IsSelfUpdate)
                    {
                        foreach (var process in blockingProcesses)
                        {
                            process.Dispose();
                        }

                        candidate.Status = string.Format(Text.Get("AppOpenStatus"), blockingName);
                        candidate.IsSelected = false;
                        FooterText.Text = string.Format(Text.Get("AppOpenFooter"), candidate.Name);
                        SetAssistantMessage(candidate.Status);
                        skippedCount++;
                        continue;
                    }

                    var wantsClose = MessageBox.Show(
                        this,
                        string.Format(Text.Get("AppOpenAskClose"), candidate.Name),
                        Text.AppName,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes;

                    if (!wantsClose)
                    {
                        foreach (var process in blockingProcesses)
                        {
                            process.Dispose();
                        }

                        candidate.Status = string.Format(Text.Get("AppOpenStatus"), blockingName);
                        candidate.IsSelected = false;
                        FooterText.Text = string.Format(Text.Get("AppOpenFooter"), candidate.Name);
                        SetAssistantMessage(candidate.Status);
                        skippedCount++;
                        continue;
                    }

                    candidate.Status = string.Format(Text.Get("ClosingApp"), candidate.Name);
                    SetStatus(candidate.Status);
                    SetAssistantMessage(candidate.Status);

                    // TryForceCloseAsync disposes the processes either way.
                    var closed = await RunningAppGuard.TryForceCloseAsync(blockingProcesses, cancellationToken);
                    if (!closed)
                    {
                        candidate.Status = string.Format(Text.Get("AppOpenStatus"), blockingName);
                        candidate.IsSelected = false;
                        FooterText.Text = string.Format(Text.Get("AppCloseFailed"), candidate.Name);
                        SetAssistantMessage(FooterText.Text);
                        skippedCount++;
                        continue;
                    }
                    // Closed successfully — fall through to the normal update path.
                }

                var progress = new Progress<string>(message =>
                {
                    candidate.Status = message;
                    SetStatus($"{candidate.Name}: {message}");
                    SetAssistantMessage($"{candidate.Name}: {message}");
                });

                var desktopSnapshot = DesktopShortcutCleanupService.Snapshot();

                var record = await _updateExecutor.InstallAsync(
                    candidate.Catalog,
                    candidate.Name,
                    candidate.InstalledVersion,
                    candidate.LatestVersion,
                    candidate.DownloadUrl,
                    candidate.Sha512,
                    candidate.Sha256,
                    candidate.SourceName,
                    candidate.TrustSummary,
                    progress,
                    cancellationToken,
                    prepared);

                var exitSucceeded = UpdateExecutor.IsSuccessfulInstallerExit(record.ExitCode);
                var packageManagerBacked = IsPackageManagerProvider(candidate.Catalog.Provider);
                var installedVersionVerified = false;

                if (packageManagerBacked || !exitSucceeded)
                {
                    candidate.Status = Text.Get("VerifyingInstalled");
                    SetAssistantMessage($"{candidate.Name}: {candidate.Status}");
                    installedVersionVerified = await IsNowCurrentAsync(candidate, cancellationToken);
                }

                // Package managers use inconsistent success codes and can return zero
                // while the same update remains pending. For them, the post-update query
                // is authoritative. Direct installers retain normal exit-code semantics,
                // with registry verification able to rescue a non-standard success code.
                var updateVerified = packageManagerBacked
                    ? installedVersionVerified
                    : exitSucceeded || installedVersionVerified;

                if (updateVerified && !exitSucceeded)
                {
                    record.Outcome = "Updated; installed version verified after a non-success exit code";
                    record.Error = string.Empty;
                }
                else if (!updateVerified && packageManagerBacked && exitSucceeded)
                {
                    record.Outcome = "Not updated; package manager still reports the update as pending";
                    record.Error = Text.Get("UpdateStillPending");
                }

                await _historyService.AppendAsync(record, cancellationToken);

                if (updateVerified)
                {
                    updatedCount++;
                    await _failureBlockService.ClearFailureAsync(candidate, cancellationToken);
                    Updates.Remove(candidate);
                    RefreshFamilies();

                    try
                    {
                        removedShortcuts += DesktopShortcutCleanupService.RemoveNewShortcutsFor(candidate, desktopSnapshot);
                    }
                    catch (Exception shortcutException)
                    {
                        CrashLogger.Log(shortcutException);
                    }

                    if (string.IsNullOrWhiteSpace(record.TrustWarning))
                    {
                        FooterText.Text = string.Format(Text.Get("Updated"), candidate.Name);
                    }
                    else
                    {
                        trustWarningCount++;
                        FooterText.Text = string.Format(Text.Get("UpdatedUnverified"), candidate.Name, record.TrustWarning);
                        SetAssistantMessage(FooterText.Text);
                    }
                }
                else
                {
                    failedCount++;
                    var failure = await _failureBlockService.RecordFailureAsync(candidate, record, cancellationToken);
                    FooterText.Text = record.Error.Length > 0 ? record.Error : record.Outcome;
                    if (failure.IsBlocked)
                    {
                        candidate.Status = Text.Get("BlockedStatus");
                        candidate.IsSelected = false;
                        candidate.UpdateAvailable = false;
                        FooterText.Text = string.Format(Text.Get("BlockedAfterFailuresFooter"), candidate.Name);
                        SetAssistantFailure();
                        SetAssistantTarget(Text.Get("AssistantBlockedCry"));
                        SetAssistantMessage(FooterText.Text);
                        await Task.Delay(5000, cancellationToken);
                    }
                    else
                    {
                        candidate.Status = string.Format(Text.Get("FailureAttemptStatus"), failure.FailureCount, UpdateFailureBlockService.FailureThreshold);
                    }
                }
            }

            var hasIssues = failedCount > 0 || skippedCount > 0 || trustWarningCount > 0;
            SetStatus(Text.Get(hasIssues ? "DoneWithIssues" : "Done"));
            FooterText.Text = string.Format(
                Text.Get("SweepSummary"),
                updatedCount,
                failedCount,
                skippedCount,
                trustWarningCount);
            if (removedShortcuts > 0)
            {
                FooterText.Text += " " + string.Format(Text.Get("SweepSummaryShortcuts"), removedShortcuts);
            }

            if (failedCount > 0)
            {
                SetAssistantFailure();
            }

            SetAssistantTarget(Text.Get(hasIssues ? "AssistantDoneWithIssues" : "AssistantDone"));
            SetAssistantMessage(FooterText.Text);
            _ = CleanupLocalDataAsync();
        }
        catch (OperationCanceledException) when (_isClosing)
        {
        }
        catch (Exception exception)
        {
            LogAndShow(Text.Get("Error"), exception);
        }
        finally
        {
            if (assistant is { IsClosedByUser: false })
            {
                assistant.Close();
            }

            EndWork();
        }

        void SetAssistantTarget(string value)
        {
            if (assistant is null || assistant.IsClosedByUser)
            {
                return;
            }

            assistant.SetTarget(value);
        }

        void SetAssistantWorking()
        {
            if (assistant is null || assistant.IsClosedByUser)
            {
                return;
            }

            assistant.SetWorkingState();
        }

        void SetAssistantFailure()
        {
            if (assistant is null || assistant.IsClosedByUser)
            {
                return;
            }

            assistant.SetFailureState();
        }

        void SetAssistantMessage(string value)
        {
            if (assistant is null || assistant.IsClosedByUser)
            {
                return;
            }

            assistant.SetMessage(value);
        }
    }

    private static void ApplyRelease(UpdateCandidateViewModel candidate, ReleaseInfo release)
    {
        candidate.LatestVersion = release.Version;
        candidate.DownloadUrl = release.DownloadUrl;
        candidate.Sha512 = release.Sha512;
        candidate.Sha256 = release.Sha256;
        candidate.DownloadSizeBytes = release.DownloadSizeBytes;
        candidate.SourceName = string.IsNullOrWhiteSpace(release.SourceName)
            ? candidate.Catalog.Provider
            : release.SourceName;
        candidate.TrustSummary = release.TrustSummary;
        candidate.UpdateAvailable = VersionComparer.Compare(candidate.InstalledVersion, release.Version) < 0;
        candidate.IsSelected = candidate.UpdateAvailable;
        candidate.IsSelfUpdate = AppSelfIdentity.IsSelf(candidate.Catalog, candidate.Name);
        candidate.Status = candidate.UpdateAvailable
            ? candidate.IsSelfUpdate ? Text.Get("SelfNeedsSweep") : Text.Get("OldStatus")
            : Text.Get("CurrentStatus");
    }

    private int AddPackageManagerCandidates(
        IReadOnlyList<PackageManagerUpdate> updates,
        IReadOnlySet<string> ignoredNames,
        IReadOnlySet<string> blockedUpdateKeys,
        IReadOnlyList<InstalledApp> installedApps)
    {
        var existing = Updates
            .Select(candidate => AppMatcher.Normalize(candidate.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var update in updates)
        {
            var normalizedName = AppMatcher.Normalize(update.DisplayName);
            if (string.IsNullOrWhiteSpace(normalizedName) ||
                existing.Contains(normalizedName) ||
                ignoredNames.Contains(normalizedName))
            {
                continue;
            }

            var installedMatch = FindInstalledAppForPackage(update, installedApps);
            if (UpdateSkipPolicy.ShouldSkip(update, installedMatch, out _))
            {
                continue;
            }

            var candidate = CreatePackageManagerCandidate(update, installedMatch);
            if (candidate.IsSelfUpdate || _failureBlockService.IsBlocked(candidate, blockedUpdateKeys))
            {
                continue;
            }

            candidate.PropertyChanged += Candidate_PropertyChanged;
            Updates.Add(candidate);
            existing.Add(normalizedName);
            added++;
        }

        return added;
    }

    private static UpdateCandidateViewModel CreatePackageManagerCandidate(PackageManagerUpdate update, InstalledApp? installedMatch)
    {
        var catalog = new CatalogEntry
        {
            Id = $"{update.Provider}-{SanitizeId(update.PackageId)}",
            Name = update.DisplayName,
            Provider = update.Provider,
            PackageId = update.PackageId,
            InstallerType = update.Provider,
            SilentArgs = string.Empty,
            RequiresElevation = update.RequiresElevation,
            RequireHttps = false,
            Notes = "Package-manager-backed update candidate."
        };

        var installedApp = installedMatch ?? new InstalledApp
        {
            DisplayName = update.DisplayName,
            DisplayVersion = update.InstalledVersion,
            Publisher = update.SourceName
        };

        var candidate = new UpdateCandidateViewModel(catalog, installedApp)
        {
            LatestVersion = update.AvailableVersion,
            DownloadUrl = $"package-manager://{update.Provider}/{Uri.EscapeDataString(update.PackageId)}",
            SourceName = update.SourceName,
            TrustSummary = update.TrustSummary,
            UpdateAvailable = true,
            IsSelected = true,
            IsSelfUpdate = AppSelfIdentity.IsSelf(catalog, update.DisplayName)
        };
        candidate.Status = candidate.IsSelfUpdate ? Text.Get("SelfNeedsSweep") : update.Provider;
        return candidate;
    }

    private static InstalledApp? FindInstalledAppForPackage(
        PackageManagerUpdate update,
        IReadOnlyList<InstalledApp> installedApps)
    {
        var normalizedDisplay = AppMatcher.Normalize(update.DisplayName);
        var normalizedPackage = AppMatcher.Normalize(update.PackageId);
        if (string.IsNullOrWhiteSpace(normalizedDisplay) && string.IsNullOrWhiteSpace(normalizedPackage))
        {
            return null;
        }

        return installedApps
            .Where(app =>
            {
                var normalizedInstalled = AppMatcher.Normalize(app.DisplayName);
                return !string.IsNullOrWhiteSpace(normalizedInstalled) &&
                       (normalizedInstalled == normalizedDisplay ||
                        normalizedInstalled == normalizedPackage ||
                        normalizedInstalled.Contains(normalizedDisplay, StringComparison.OrdinalIgnoreCase) ||
                        normalizedDisplay.Contains(normalizedInstalled, StringComparison.OrdinalIgnoreCase));
            })
            .OrderByDescending(app => ScoreInstalledPackageMatch(update, app))
            .FirstOrDefault();
    }

    private static int ScoreInstalledPackageMatch(PackageManagerUpdate update, InstalledApp app)
    {
        var normalizedInstalled = AppMatcher.Normalize(app.DisplayName);
        var normalizedDisplay = AppMatcher.Normalize(update.DisplayName);
        var normalizedPackage = AppMatcher.Normalize(update.PackageId);

        if (normalizedInstalled == normalizedDisplay)
        {
            return 100;
        }

        if (normalizedInstalled == normalizedPackage)
        {
            return 90;
        }

        if (!string.IsNullOrWhiteSpace(app.InstallLocation))
        {
            return 70;
        }

        return 40;
    }

    private static string SanitizeId(string value)
    {
        var sanitized = new string(value
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        return sanitized.Trim('-').ToLowerInvariant();
    }

    private async Task<bool> IsNowCurrentAsync(UpdateCandidateViewModel candidate, CancellationToken cancellationToken)
    {
        var provider = candidate.Catalog.Provider?.Trim().ToLowerInvariant();
        if (provider is "winget" or "choco" or "chocolatey" or "scoop")
        {
            // For a package-manager update, confirm success by re-querying the manager
            // rather than trusting the exit code (which is unreliable across winget/choco/scoop).
            var packageId = candidate.Catalog.PackageId ?? candidate.Catalog.Id;
            return !await _packageManagerUpdateScanner.HasPendingUpdateAsync(provider, packageId, cancellationToken);
        }

        var installedApps = (await Task.Run(_installedAppScanner.Scan, cancellationToken))
            .Concat(new[] { AppSelfIdentity.CreateInstalledApp() })
            .ToList();
        var installedMatch = AppMatcher.FindInstalledMatch(candidate.Catalog, installedApps);
        return installedMatch is not null &&
               VersionComparer.Compare(installedMatch.DisplayVersion, candidate.LatestVersion) >= 0;
    }

    private static bool IsPackageManagerProvider(string? provider)
    {
        return provider?.Trim().ToLowerInvariant() is "winget" or "choco" or "chocolatey" or "scoop";
    }

    private void Candidate_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UpdateCandidateViewModel.IsSelected) or nameof(UpdateCandidateViewModel.UpdateAvailable))
        {
            UpdateButtons();
        }
    }

    private CancellationToken StartWork(string status)
    {
        _workCts?.Cancel();
        _workCts?.Dispose();
        _workCts = CancellationTokenSource.CreateLinkedTokenSource(_appCts.Token);
        _workFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _isBusy = true;
        SetStatus(status);
        UpdateButtons();
        return _workCts.Token;
    }

    private void EndWork()
    {
        _workCts?.Dispose();
        _workCts = null;
        _isBusy = false;
        UpdateButtons();
        _workFinished?.TrySetResult();
    }

    private void UpdateButtons()
    {
        FindButton.IsEnabled = !_isBusy && !_isClosing;
        PirateButton.IsEnabled = !_isBusy && !_isClosing;
        SelfSweepButton.IsEnabled = !_isBusy && !_isClosing;
        RefreshSelfUpdateButton();
        DeleteSelfButton.IsEnabled = !_isBusy && !_isClosing;
        LanguageMenuItem.IsEnabled = !_isBusy && !_isClosing;
        WorkspaceSwitcher.IsEnabled = !_isBusy && !_isClosing;
        SweepButton.IsEnabled = !_isBusy && !_isClosing && Updates.Any(candidate => candidate.IsSelected && candidate.UpdateAvailable);
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private void PlayLaunchFinished()
    {
        Opacity = 0;
        RootScale.ScaleX = 0.97;
        RootScale.ScaleY = 0.97;
        SetStatus(Text.Get("LaunchFinished"));

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = ease
        });
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(360))
        {
            EasingFunction = ease
        });
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(360))
        {
            EasingFunction = ease
        });

        SystemSounds.Asterisk.Play();
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(700);
            if (!_isBusy && !_isClosing)
            {
                SetStatus(Text.Get("ReadyStatus"));
            }
        });
    }

    private async Task CleanupLocalDataAsync()
    {
        try
        {
            var result = await _localCleanupService.CleanAsync(_appCts.Token);
            if (result.DeletedFiles > 0 && !_isBusy && !_isClosing)
            {
                FooterText.Text = string.Format(Text.Get("LocalCleanupDone"), result.DeletedFiles);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            CrashLogger.Log(exception);
        }
    }

    private void RefreshFamilies()
    {
        AppFamilyClassifier.Apply(Updates, Text.Language);
    }

    private static bool IsIgnored(
        string catalogName,
        string installedName,
        IReadOnlySet<string> ignoredNames)
    {
        return ignoredNames.Contains(AppMatcher.Normalize(catalogName)) ||
               ignoredNames.Contains(AppMatcher.Normalize(installedName));
    }

    private static LocalizationService Text => LocalizationService.Current;

    private void ApplyLocalization()
    {
        Title = Text.AppName;
        AppNameText.Text = Text.AppName;
        FindButton.Content = Text.Get("FindOldButton");
        SweepButton.Content = Text.Get("SweepButton");
        PirateButton.Content = Text.Get("PirateButton");
        SelfSweepButton.Content = Text.Get("SelfSweepButton");
        DeleteSelfButton.Content = Text.Get("DeleteSelfButton");
        ResetBlocksMenuItem.Header = Text.Get("ResetBlocksMenu");
        LanguageMenuItem.Header = Text.Get("LanguageMenu");
        StatusText.Text = Text.Get("ReadyStatus");
        foreach (var candidate in Updates) candidate.RefreshLocalization();
        FooterText.Text = Text.Get("ReadyFooter");
        RefreshDeckLocalization();
    }

    private async void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var dialog = new SetupWindow(languageOnly: true) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        App.Settings.Language = dialog.SelectedLanguage;
        await new AppSettingsService().SaveAsync(App.Settings, _appCts.Token);
        LocalizationService.SetCurrent(App.Settings.Language);
        ApplyLocalization();
        RefreshFamilies();
        foreach (var candidate in Updates)
            if (candidate.UpdateAvailable) candidate.Status = Text.Get("OldStatus");
    }

    private void PirateButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new PirateLoverWindow
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private async void SelfSweepButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _isClosing) return;
        var cancellationToken = StartWork(Text.Get("SelfChecking"));
        var restart = false;
        try
        {
            var update = await new SelfMaintenanceService(_httpClient).PrepareUpdateAsync(App.Settings.UpdateRepository,
                new Progress<string>(message => SetStatus(message)), cancellationToken);
            if (update is null)
            {
                SetSelfUpdateAvailable(false);
                SetStatus(Text.Get("SelfCurrent"));
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                SelfMaintenanceService.ScheduleUpdate(update);
                SetStatus(Text.Get("SelfRestarting"));
                restart = true;
            }
        }
        catch (OperationCanceledException) when (_isClosing) { }
        catch (Exception exception) { LogAndShow(Text.Get("Error"), exception); }
        finally { EndWork(); }
        if (restart) Close();
    }

    private void DeleteSelfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _isClosing) return;
        try
        {
            var target = SelfMaintenanceService.CurrentExecutable();
            if (MessageBox.Show(this, string.Format(Text.Get("DeleteSelfConfirm"), target), Text.AppName,
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            SelfMaintenanceService.ScheduleRemoval();
            Close();
        }
        catch (Exception exception) { LogAndShow(Text.Get("Error"), exception); }
    }

    private async void ResetBlocksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        try
        {
            var cleared = await _failureBlockService.ClearAllBlocksAsync(CancellationToken.None);
            SetStatus(string.Format(Text.Get("BlocksReset"), cleared));
            if (cleared > 0)
            {
                await FindUpdatesAsync();
            }
        }
        catch (Exception exception)
        {
            LogAndShow(Text.Get("Error"), exception);
        }
    }

    private async void PirateCandidateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCandidate(sender, out var candidate))
        {
            return;
        }

        await _unsupportedIgnoreService.AddAsync(new[] { candidate.Name }, CancellationToken.None);
        Updates.Remove(candidate);
        RefreshFamilies();
        UpdateButtons();
        SetStatus(string.Format(Text.Get("IgnoredCandidateStatus"), candidate.Name));
    }

    private void DeleteCandidateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCandidate(sender, out _))
        {
            return;
        }

        try
        {
            AppPathResolver.OpenSystemUninstaller();
            SetStatus(Text.Get("SystemUninstallerOpened"));
        }
        catch (Exception exception)
        {
            LogAndShow(Text.Get("Error"), exception);
        }
    }

    private void FilePathCandidateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCandidate(sender, out var candidate))
        {
            return;
        }

        try
        {
            AppPathResolver.OpenInExplorer(AppPathResolver.ResolveOpenTarget(candidate));
        }
        catch (Exception exception)
        {
            LogAndShow(Text.Get("Error"), exception);
        }
    }

    private static bool TryGetCandidate(object sender, out UpdateCandidateViewModel candidate)
    {
        candidate = default!;
        if (sender is not FrameworkElement { DataContext: UpdateCandidateViewModel item })
        {
            return false;
        }

        candidate = item;
        return true;
    }

    private void LogAndShow(string status, Exception exception)
    {
        CrashLogger.Log(exception);
        SetStatus(status);
        FooterText.Text = exception.Message;
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeReady) return;
        if (_isClosing) { e.Cancel = true; return; }
        _isClosing = true;
        var workFinished = _workFinished?.Task;
        if (workFinished is { IsCompleted: false }) e.Cancel = true;
        _appCts.Cancel();
        _workCts?.Cancel();
        _httpClient.CancelPendingRequests();

        foreach (var ownedWindow in OwnedWindows.Cast<Window>().ToList())
        {
            ownedWindow.Close();
        }
        UpdateButtons();
        if (e.Cancel && workFinished is not null)
        {
            await workFinished;
            _closeReady = true;
            Close();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _workCts?.Dispose();
        _workCts = null;
        _appCts.Dispose();
        _httpClient.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}
