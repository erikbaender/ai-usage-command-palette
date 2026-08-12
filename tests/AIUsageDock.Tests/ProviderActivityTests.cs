using AIUsageDock.Core;

namespace AIUsageDock.Tests;

public sealed class ProviderActivityTests
{
    [Fact]
    public void UsageIncreaseActivityExpiresAfterShortHold()
    {
        var tracker = new ProviderUsageActivityTracker(TimeSpan.FromSeconds(5));
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        var baseline = Snapshot(ProviderId.Claude, 10, observed);
        var increased = Snapshot(ProviderId.Claude, 11, observed.AddSeconds(1));

        Assert.False(tracker.Observe(baseline, observed));
        Assert.True(tracker.Observe(increased, observed.AddSeconds(1)));
        Assert.True(tracker.IsActive(ProviderId.Claude, observed.AddSeconds(5)));
        Assert.False(tracker.IsActive(ProviderId.Claude, observed.AddSeconds(6)));
    }

    [Fact]
    public void ExistingUsageDoesNotMeanSessionIsActive()
    {
        var tracker = new ProviderUsageActivityTracker(TimeSpan.FromSeconds(5));
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        var snapshot = Snapshot(ProviderId.Codex, 32, observed);

        Assert.False(tracker.Observe(snapshot, observed));
        Assert.False(tracker.Observe(snapshot with { LastUpdated = observed.AddSeconds(5) }, observed.AddSeconds(5)));
    }

    [Fact]
    public void ResetOrUsageDecreaseDoesNotCountAsActivity()
    {
        var tracker = new ProviderUsageActivityTracker(TimeSpan.FromSeconds(5));
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");

        Assert.False(tracker.Observe(Snapshot(ProviderId.Claude, 99, observed), observed));
        Assert.False(tracker.Observe(Snapshot(ProviderId.Claude, 0, observed.AddSeconds(1)), observed.AddSeconds(1)));
    }

    [Fact]
    public void TracksProvidersIndependently()
    {
        var tracker = new ProviderUsageActivityTracker(TimeSpan.FromSeconds(5));
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");

        tracker.Observe(Snapshot(ProviderId.Codex, 20, observed), observed);
        tracker.Observe(Snapshot(ProviderId.Claude, 20, observed), observed);
        tracker.Observe(Snapshot(ProviderId.Codex, 21, observed.AddSeconds(1)), observed.AddSeconds(1));

        Assert.True(tracker.IsActive(ProviderId.Codex, observed.AddSeconds(1)));
        Assert.False(tracker.IsActive(ProviderId.Claude, observed.AddSeconds(1)));
    }

    [Fact]
    public void UnavailableSnapshotDoesNotReplaceLastGoodBaseline()
    {
        var tracker = new ProviderUsageActivityTracker(TimeSpan.FromSeconds(5));
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        var unavailable = new ProviderSnapshot(
            ProviderId.Codex,
            ProviderHealth.Unavailable,
            [],
            observed.AddSeconds(1),
            "test");

        Assert.False(tracker.Observe(Snapshot(ProviderId.Codex, 20, observed), observed));
        Assert.False(tracker.Observe(unavailable, observed.AddSeconds(1)));
        Assert.True(tracker.Observe(Snapshot(ProviderId.Codex, 21, observed.AddSeconds(2)), observed.AddSeconds(2)));
    }

    private static ProviderSnapshot Snapshot(ProviderId provider, double used, DateTimeOffset observedAt) =>
        new(
            provider,
            ProviderHealth.Available,
            [new UsageWindowSnapshot(UsageWindow.Weekly, used, observedAt.AddDays(5), observedAt)],
            observedAt,
            "test");
}
