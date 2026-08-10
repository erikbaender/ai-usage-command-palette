using AIUsageDock.Core;
using AIUsageDock.Providers;

namespace AIUsageDock.Extension;

public sealed class UsageCoordinator : IAsyncDisposable
{
    private readonly IReadOnlyDictionary<ProviderId, IUsageProvider> _providers;
    private readonly Dictionary<ProviderId, ProviderSnapshot> _snapshots = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TimeSpan _refreshInterval;
    private readonly object _gate = new();
    private Task? _refreshLoop;
    private bool _disposed;

    private UsageCoordinator(IEnumerable<IUsageProvider> providers, TimeSpan refreshInterval)
    {
        _providers = providers.ToDictionary(provider => provider.Id);
        _refreshInterval = refreshInterval;
        foreach (var provider in _providers.Values)
        {
            _snapshots[provider.Id] = ProviderSnapshot.Waiting(provider.Id, provider.Id == ProviderId.Claude ? "Claude CLI · claude -p /usage" : "codex app-server", "Waiting for provider data");
            provider.SnapshotChanged += OnProviderSnapshotChanged;
        }
    }

    public event EventHandler<ProviderSnapshot>? SnapshotChanged;

    public static UsageCoordinator CreateDefault() => new(
        [
            new CodexProvider(),
            new ClaudeProvider(),
        ],
        TimeSpan.FromSeconds(60));

    public ProviderSnapshot GetSnapshot(ProviderId provider)
    {
        lock (_gate)
        {
            return _snapshots[provider];
        }
    }

    public void Start()
    {
        _refreshLoop ??= RefreshLoopAsync(_shutdown.Token);
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
        if (_refreshLoop is not null)
        {
            try { await _refreshLoop; } catch (OperationCanceledException) { }
        }

        foreach (var provider in _providers.Values)
        {
            provider.SnapshotChanged -= OnProviderSnapshotChanged;
            await provider.DisposeAsync();
        }

        _shutdown.Dispose();
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(cancellationToken);
                await Task.Delay(_refreshInterval, cancellationToken);
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

    private void OnProviderSnapshotChanged(object? sender, ProviderSnapshot snapshot)
    {
        lock (_gate)
        {
            _snapshots[snapshot.Provider] = snapshot;
        }
        SnapshotChanged?.Invoke(this, snapshot);
    }
}
