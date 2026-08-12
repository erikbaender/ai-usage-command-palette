namespace AIUsageDock.Core;

public sealed class ProviderUsageActivityTracker
{
    public static TimeSpan DefaultActiveHoldDuration { get; } = TimeSpan.FromMinutes(1);

    private readonly Dictionary<ProviderId, ProviderSnapshot> _previousSnapshots = new();
    private readonly Dictionary<ProviderId, DateTimeOffset> _lastActivity = new();
    private TimeSpan _activeHoldDuration;

    public ProviderUsageActivityTracker(TimeSpan? activeHoldDuration = null)
    {
        activeHoldDuration ??= DefaultActiveHoldDuration;
        if (activeHoldDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(activeHoldDuration), "The activity hold duration must be positive.");
        }

        _activeHoldDuration = activeHoldDuration.Value;
    }

    public TimeSpan ActiveHoldDuration => _activeHoldDuration;

    public void UpdateActiveHoldDuration(TimeSpan activeHoldDuration)
    {
        if (activeHoldDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(activeHoldDuration), "The activity hold duration must be positive.");
        }

        _activeHoldDuration = activeHoldDuration;
    }

    public bool Observe(ProviderSnapshot snapshot, DateTimeOffset observedAt)
    {
        if (snapshot.Health == ProviderHealth.Available)
        {
            if (_previousSnapshots.TryGetValue(snapshot.Provider, out var previous) &&
                HasUsageIncreased(previous, snapshot))
            {
                _lastActivity[snapshot.Provider] = observedAt;
            }

            _previousSnapshots[snapshot.Provider] = snapshot;
        }

        return IsActive(snapshot.Provider, observedAt);
    }

    public bool IsActive(ProviderId provider, DateTimeOffset observedAt) =>
        _lastActivity.TryGetValue(provider, out var lastActivity) &&
        observedAt - lastActivity < _activeHoldDuration;

    private static bool HasUsageIncreased(ProviderSnapshot previous, ProviderSnapshot current) =>
        current.Windows.Any(window =>
            window.UsedPercent is double currentUsed &&
            previous.GetWindow(window.Window)?.UsedPercent is double previousUsed &&
            currentUsed > previousUsed);
}
