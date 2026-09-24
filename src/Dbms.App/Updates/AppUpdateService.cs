using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace Dbms.App.Updates;

public sealed record UpdateCheckResult(
    bool IsInstalled,
    bool IsAvailable,
    string Message,
    string? AvailableVersion = null);

public sealed class AppUpdateService
{
    private readonly UpdateManager? updateManager;
    private readonly string? initializationMessage;
    private UpdateInfo? pendingUpdate;

    public AppUpdateService()
    {
        if (PackageIdentity.IsPackaged)
        {
            return;
        }

        try
        {
            updateManager = new UpdateManager(
                new GithubSource(UpdateConfiguration.RepositoryUrl, null, UpdateConfiguration.IncludePrereleases));
        }
        catch (Exception exception)
        {
            initializationMessage = exception.Message;
        }
    }

    public bool IsStoreInstalled => PackageIdentity.IsPackaged;

    public bool IsInstalled => !IsStoreInstalled && updateManager?.IsInstalled == true;

    public string CurrentVersion => updateManager?.CurrentVersion?.ToString() ?? GetAssemblyVersion();

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsStoreInstalled)
        {
            return new(true, false, "Updates are managed by Microsoft Store.");
        }

        if (updateManager is null)
        {
            return new(false, false, $"Update service unavailable: {Sanitize(initializationMessage)}");
        }

        if (!updateManager.IsInstalled)
        {
            return new(false, false, $"Version {CurrentVersion} · install the app to enable updates.");
        }

        try
        {
            pendingUpdate = await updateManager.CheckForUpdatesAsync();
            if (pendingUpdate is null)
            {
                return new(true, false, $"Up to date · version {CurrentVersion}.");
            }

            var version = pendingUpdate.TargetFullRelease.Version.ToString();
            return new(true, true, $"Update available · version {version}.", version);
        }
        catch (NotInstalledException)
        {
            return new(false, false, $"Version {CurrentVersion} · install the app to enable updates.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(true, false, $"Update check unavailable · {Sanitize(exception.Message)}");
        }
    }

    public async Task DownloadAndRestartAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (updateManager is null || pendingUpdate is null)
        {
            throw new InvalidOperationException("No update is ready to install.");
        }

        Action<int>? progressCallback = progress is null ? null : progress.Report;
        await updateManager.DownloadUpdatesAsync(pendingUpdate, progressCallback, cancellationToken);
        updateManager.ApplyUpdatesAndRestart(pendingUpdate.TargetFullRelease);
    }

    private static string GetAssemblyVersion() =>
        typeof(AppUpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    private static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "unknown error";
        return message.Length > 180 ? message[..180] + "…" : message;
    }
}
