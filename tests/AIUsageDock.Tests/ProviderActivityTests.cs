using AIUsageDock.Core;

namespace AIUsageDock.Tests;

public sealed class ProviderActivityTests
{
    [Fact]
    public void DetectsExternalProviderProcess()
    {
        ProcessDescriptor[] processes =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(101, 100, "codex.exe"),
            new(200, 1, "CODEX.EXE"),
        ];

        Assert.True(ProviderActivityDetection.IsActive(ProviderId.Codex, processes, 100));
        Assert.False(ProviderActivityDetection.IsActive(ProviderId.Claude, processes, 100));
    }

    [Fact]
    public void IgnoresMonitorProcessAndAllDescendants()
    {
        ProcessDescriptor[] processes =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(101, 100, "launcher.exe"),
            new(102, 101, "claude.exe"),
            new(103, 100, "codex.exe"),
        ];

        Assert.False(ProviderActivityDetection.IsActive(ProviderId.Codex, processes, 100));
        Assert.False(ProviderActivityDetection.IsActive(ProviderId.Claude, processes, 100));
    }

    [Fact]
    public void MatchesExecutableNameWithoutExtension()
    {
        ProcessDescriptor[] processes =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(200, 1, "claude"),
        ];

        Assert.True(ProviderActivityDetection.IsActive(ProviderId.Claude, processes, 100));
    }

    [Fact]
    public void ExistingIdleProcessDoesNotCountAsCpuActivity()
    {
        var tracker = new ProviderCpuActivityTracker(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        ProcessDescriptor[] processes =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(200, 1, "codex.exe", TimeSpan.FromSeconds(10)),
        ];

        Assert.False(tracker.Observe(ProviderId.Codex, processes, 100, observed));
        Assert.False(tracker.Observe(ProviderId.Codex, processes, 100, observed.AddSeconds(1)));
    }

    [Fact]
    public void CpuActivityExpiresAfterHoldDuration()
    {
        var tracker = new ProviderCpuActivityTracker(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        ProcessDescriptor[] idle =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(200, 1, "claude.exe", TimeSpan.FromSeconds(10)),
        ];
        ProcessDescriptor[] active =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(200, 1, "claude.exe", TimeSpan.FromSeconds(10.1)),
        ];

        Assert.False(tracker.Observe(ProviderId.Claude, idle, 100, observed));
        Assert.True(tracker.Observe(ProviderId.Claude, active, 100, observed.AddSeconds(1)));
        Assert.True(tracker.Observe(ProviderId.Claude, active, 100, observed.AddSeconds(3)));
        Assert.False(tracker.Observe(ProviderId.Claude, active, 100, observed.AddSeconds(4)));
    }

    [Fact]
    public void NewlyStartedProviderProcessCountsAsActivityAfterBaseline()
    {
        var tracker = new ProviderCpuActivityTracker(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        ProcessDescriptor[] baseline =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
        ];
        ProcessDescriptor[] started =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(200, 1, "codex.exe", TimeSpan.Zero),
        ];

        Assert.False(tracker.Observe(ProviderId.Codex, baseline, 100, observed));
        Assert.True(tracker.Observe(ProviderId.Codex, started, 100, observed.AddSeconds(1)));
    }

    [Fact]
    public void IgnoresSmallBackgroundCpuHeartbeat()
    {
        var tracker = new ProviderCpuActivityTracker(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        ProcessDescriptor[] baseline =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(200, 1, "codex.exe", TimeSpan.FromSeconds(10)),
        ];
        ProcessDescriptor[] heartbeat =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(200, 1, "codex.exe", TimeSpan.FromSeconds(10.02)),
        ];

        Assert.False(tracker.Observe(ProviderId.Codex, baseline, 100, observed));
        Assert.False(tracker.Observe(ProviderId.Codex, heartbeat, 100, observed.AddSeconds(1)));
    }

    [Fact]
    public void DetectsIoActivityFromProviderDescendant()
    {
        var tracker = new ProviderCpuActivityTracker(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        ProcessDescriptor[] baseline =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(200, 1, "codex.exe", TotalIoOperations: 10, TotalIoBytes: 1000),
            new(201, 200, "pwsh.exe", TotalIoOperations: 20, TotalIoBytes: 2000),
        ];
        ProcessDescriptor[] active =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(200, 1, "codex.exe", TotalIoOperations: 10, TotalIoBytes: 1000),
            new(201, 200, "pwsh.exe", TotalIoOperations: 21, TotalIoBytes: 7000),
        ];

        Assert.False(tracker.Observe(ProviderId.Codex, baseline, 100, observed));
        Assert.True(tracker.Observe(ProviderId.Codex, active, 100, observed.AddSeconds(1)));
    }

    [Fact]
    public void IgnoresBackgroundIoOperationsWithoutMeaningfulDataTransfer()
    {
        var tracker = new ProviderCpuActivityTracker(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        ProcessDescriptor[] baseline =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(200, 1, "codex.exe", TotalIoOperations: 10, TotalIoBytes: 1000),
        ];
        ProcessDescriptor[] heartbeat =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(200, 1, "codex.exe", TotalIoOperations: 50, TotalIoBytes: 1000),
        ];

        Assert.False(tracker.Observe(ProviderId.Codex, baseline, 100, observed));
        Assert.False(tracker.Observe(ProviderId.Codex, heartbeat, 100, observed.AddSeconds(1)));
    }

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

    private static ProviderSnapshot Snapshot(ProviderId provider, double used, DateTimeOffset observedAt) =>
        new(
            provider,
            ProviderHealth.Available,
            [new UsageWindowSnapshot(UsageWindow.Weekly, used, observedAt.AddDays(5), observedAt)],
            observedAt,
            "test");
}
