using AIUsageDock.Core;

namespace AIUsageDock.Extension;

public sealed class ProviderActivityMonitor : IProviderActivityDetector, IDisposable
{
    public static TimeSpan DefaultBlinkPeriod { get; } = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<ProviderId, ProviderActivityState> _states = new();
    private readonly object _gate = new();
    private readonly TimeSpan _halfPeriod;
    private readonly Task _monitorTask;
    private readonly ProviderUsageActivityTracker _usageActivityTracker = new();
    private bool _disposed;

    public ProviderActivityMonitor(TimeSpan? blinkPeriod = null)
    {
        var period = blinkPeriod ?? DefaultBlinkPeriod;
        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(blinkPeriod), "The blink period must be positive.");
        }

        _halfPeriod = TimeSpan.FromTicks(Math.Max(1, period.Ticks / 2));
        foreach (var provider in Enum.GetValues<ProviderId>())
        {
            _states[provider] = new ProviderActivityState(provider, IsActive: false, IsDimmed: false);
        }

        _monitorTask = MonitorAsync(_shutdown.Token);
    }

    public event EventHandler<ProviderActivityState>? ActivityChanged;

    public ProviderActivityState GetState(ProviderId provider)
    {
        lock (_gate)
        {
            return _states[provider];
        }
    }

    public bool IsActive(ProviderId provider)
    {
        lock (_gate)
        {
            return _usageActivityTracker.IsActive(provider, DateTimeOffset.UtcNow);
        }
    }

    public void UpdateBlinkExpiration(TimeSpan expiration)
    {
        lock (_gate)
        {
            _usageActivityTracker.UpdateActiveHoldDuration(expiration);
        }
    }

    public void ObserveSnapshot(object? sender, ProviderSnapshot snapshot)
    {
        lock (_gate)
        {
            _usageActivityTracker.Observe(snapshot, DateTimeOffset.UtcNow);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        try
        {
            _monitorTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        _shutdown.Dispose();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        var dimmed = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            var observedAt = DateTimeOffset.UtcNow;
            foreach (var provider in Enum.GetValues<ProviderId>())
            {
                bool active;
                lock (_gate)
                {
                    active = _usageActivityTracker.IsActive(provider, observedAt);
                }

                Publish(new ProviderActivityState(provider, active, active && dimmed));
            }

            await Task.Delay(_halfPeriod, cancellationToken);
            dimmed = !dimmed;
        }
    }

    private void Publish(ProviderActivityState next)
    {
        ProviderActivityState previous;
        lock (_gate)
        {
            previous = _states[next.Provider];
            if (previous == next)
            {
                return;
            }
            _states[next.Provider] = next;
        }

        if (previous.IsActive != next.IsActive)
        {
            ActivityDiagnosticLog.Write(next.Provider, next.IsActive);
        }
        ActivityChanged?.Invoke(this, next);
    }
}

internal static class ActivityDiagnosticLog
{
    private static readonly object Gate = new();

    public static void Write(ProviderId provider, bool isActive)
    {
        try
        {
            lock (Gate)
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIUsage",
                    "activity.log");
                var directory = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(directory);
                if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024)
                {
                    File.WriteAllText(path, string.Empty);
                }

                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.UtcNow:O} provider={provider} active={isActive}{Environment.NewLine}");
            }
        }
        catch
        {
            // Activity diagnostics must not affect the monitor loop.
        }
    }
}

public sealed record ProviderActivityState(ProviderId Provider, bool IsActive, bool IsDimmed);
