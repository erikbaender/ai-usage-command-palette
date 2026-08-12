using AIUsageDock.Core;
using AIUsageDock.Providers;

namespace AIUsageDock.Extension;

public sealed class UsageCoordinator : IAsyncDisposable
{
    private readonly IReadOnlyDictionary<ProviderId, IUsageProvider> _providers;
    private readonly Dictionary<ProviderId, ProviderSnapshot> _snapshots = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly IProviderActivityDetector _activityDetector;
    private readonly SemaphoreSlim _scheduleChanged = new(0, 1);
    private readonly object _gate = new();
    private UsagePollingPolicy _pollingPolicy;
    private Task? _refreshLoops;
    private bool _disposed;

    private UsageCoordinator(
        IEnumerable<IUsageProvider> providers,
        UsagePollingPolicy pollingPolicy,
        IProviderActivityDetector activityDetector)
    {
        _providers = providers.ToDictionary(provider => provider.Id);
        _pollingPolicy = pollingPolicy;
        _activityDetector = activityDetector;
        foreach (var provider in _providers.Values)
        {
            _snapshots[provider.Id] = ProviderSnapshot.Waiting(
                provider.Id,
                provider.Id == ProviderId.Claude ? "Claude web usage" : "Codex web usage",
                "Waiting for provider data");
            provider.SnapshotChanged += OnProviderSnapshotChanged;
        }
    }

    public event EventHandler<ProviderSnapshot>? SnapshotChanged;

    public static UsageCoordinator CreateDefault(
        UsagePollingPolicy? pollingPolicy = null,
        IProviderActivityDetector? activityDetector = null,
        IClaudeWebUsageClient? claudeWebUsageClient = null,
        ICodexWebUsageClient? codexWebUsageClient = null,
        UsageBackendPreferences? backendPreferences = null)
    {
        var preferences = backendPreferences ?? new UsageBackendPreferences();
        return new(
        [
            new CodexProvider(webClient: codexWebUsageClient, backendPreferences: preferences),
            new ClaudeProvider(webClient: claudeWebUsageClient, backendPreferences: preferences),
        ],
        pollingPolicy ?? UsagePollingPolicy.Default,
        activityDetector ?? new InactiveProviderActivityDetector());
    }

    public UsagePollingPolicy PollingPolicy
    {
        get
        {
            lock (_gate)
            {
                return _pollingPolicy;
            }
        }
    }

    public void UpdatePollingPolicy(UsagePollingPolicy pollingPolicy)
    {
        ArgumentNullException.ThrowIfNull(pollingPolicy);
        lock (_gate)
        {
            _pollingPolicy = pollingPolicy;
        }

        try
        {
            _scheduleChanged.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public ProviderSnapshot GetSnapshot(ProviderId provider)
    {
        lock (_gate)
        {
            return _snapshots[provider];
        }
    }

    public void Start()
    {
        _refreshLoops ??= Task.WhenAll(_providers.Values.Select(provider =>
            RefreshProviderLoopAsync(provider.Id, _shutdown.Token)));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(_providers.Values.Select(provider => RefreshProviderAsync(provider.Id, cancellationToken)));
    }

    public async Task RefreshProviderAsync(ProviderId providerId, CancellationToken cancellationToken)
    {
        if (_providers.TryGetValue(providerId, out var provider))
        {
            await provider.GetSnapshotAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        if (_refreshLoops is not null)
        {
            try { await _refreshLoops; } catch (OperationCanceledException) { }
        }

        foreach (var provider in _providers.Values)
        {
            provider.SnapshotChanged -= OnProviderSnapshotChanged;
            await provider.DisposeAsync();
        }

        _scheduleChanged.Dispose();
        _shutdown.Dispose();
    }

    private async Task RefreshProviderLoopAsync(
        ProviderId providerId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshProviderAsync(providerId, cancellationToken);
                var usageActive = _activityDetector.IsActive(providerId);
                var interval = PollingPolicy.GetInterval(usageActive);
                await WaitForNextRefreshAsync(interval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task WaitForNextRefreshAsync(
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(interval, waitCancellation.Token);
        var settingsChanged = _scheduleChanged.WaitAsync(waitCancellation.Token);
        var completed = await Task.WhenAny(delay, settingsChanged);
        waitCancellation.Cancel();
        await completed;
    }

    private void OnProviderSnapshotChanged(object? sender, ProviderSnapshot snapshot)
    {
        lock (_gate)
        {
            _snapshots[snapshot.Provider] = snapshot;
        }
        SnapshotChanged?.Invoke(this, snapshot);
    }
}

public interface IProviderActivityDetector
{
    bool IsActive(ProviderId provider);
}

public sealed class InactiveProviderActivityDetector : IProviderActivityDetector
{
    public bool IsActive(ProviderId provider) => false;
}
