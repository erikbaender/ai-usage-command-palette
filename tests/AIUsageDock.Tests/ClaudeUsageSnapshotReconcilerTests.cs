using AIUsageDock.Core;
using AIUsageDock.Providers;

namespace AIUsageDock.Tests;

public sealed class ClaudeUsageSnapshotReconcilerTests
{
    [Fact]
    public void RetainsUsageWhenCliTemporarilyReportsZeroBeforeReset()
    {
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        var reset = observed.AddHours(4);
        var previous = Snapshot(3, reset, observed);
        var current = Snapshot(0, null, observed.AddSeconds(3));

        var reconciled = ClaudeUsageSnapshotReconciler.Reconcile(previous, current);

        Assert.Equal(3, reconciled.GetWindow(UsageWindow.Session)!.UsedPercent);
        Assert.Equal(reset, reconciled.GetWindow(UsageWindow.Session)!.ResetsAt);
    }

    [Fact]
    public void AcceptsLowerUsageAfterPriorResetBoundary()
    {
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        var previous = Snapshot(97, observed.AddSeconds(1), observed);
        var current = Snapshot(0, null, observed.AddSeconds(2));

        var reconciled = ClaudeUsageSnapshotReconciler.Reconcile(previous, current);

        Assert.Equal(0, reconciled.GetWindow(UsageWindow.Session)!.UsedPercent);
        Assert.Null(reconciled.GetWindow(UsageWindow.Session)!.ResetsAt);
    }

    [Theory]
    [InlineData(71, 70)]
    [InlineData(71, 20)]
    public void AcceptsNonZeroProviderCorrectionsBeforeReset(double previousUsed, double correctedUsed)
    {
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        var reset = observed.AddHours(4);
        var previous = Snapshot(previousUsed, reset, observed);
        var current = Snapshot(correctedUsed, reset, observed.AddSeconds(3));

        var reconciled = ClaudeUsageSnapshotReconciler.Reconcile(previous, current);

        Assert.Equal(correctedUsed, reconciled.GetWindow(UsageWindow.Session)!.UsedPercent);
        Assert.Equal(current.GetWindow(UsageWindow.Session)!.ObservedAt, reconciled.GetWindow(UsageWindow.Session)!.ObservedAt);
    }

    [Fact]
    public void AcceptsZeroWhenThereIsNoKnownFutureResetBoundary()
    {
        var observed = DateTimeOffset.Parse("2026-08-11T20:00:00Z");
        var previous = Snapshot(40, null, observed);
        var current = Snapshot(0, null, observed.AddSeconds(3));

        var reconciled = ClaudeUsageSnapshotReconciler.Reconcile(previous, current);

        Assert.Equal(0, reconciled.GetWindow(UsageWindow.Session)!.UsedPercent);
    }

    private static ProviderSnapshot Snapshot(
        double used,
        DateTimeOffset? reset,
        DateTimeOffset observed) => new(
            ProviderId.Claude,
            ProviderHealth.Available,
            [new UsageWindowSnapshot(UsageWindow.Session, used, reset, observed)],
            observed,
            "test");
}
