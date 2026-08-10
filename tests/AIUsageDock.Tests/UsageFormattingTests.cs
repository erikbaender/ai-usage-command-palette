using AIUsageDock.Core;

namespace AIUsageDock.Tests;

public sealed class UsageFormattingTests
{
    [Fact]
    public void DockLabelShowsRemainingPercentagesAndWindowNames()
    {
        var snapshot = new ProviderSnapshot(
            ProviderId.Codex,
            ProviderHealth.Available,
            [
                new UsageWindowSnapshot(UsageWindow.Session, 12, null, DateTimeOffset.UtcNow),
                new UsageWindowSnapshot(UsageWindow.Weekly, 34, null, DateTimeOffset.UtcNow),
            ],
            DateTimeOffset.UtcNow,
            "codex app-server");

        Assert.Equal("Codex  5h 88% left · 7d 66% left", UsageFormatting.FormatDock(snapshot, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DockLabelOmitsMissingWindowsInsteadOfLeadingWithAnEmptyValue()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new ProviderSnapshot(
            ProviderId.Codex,
            ProviderHealth.Available,
            [new UsageWindowSnapshot(UsageWindow.Weekly, 22, now.AddDays(3), now)],
            now,
            "codex app-server");

        Assert.Equal("Codex  7d 78% left", UsageFormatting.FormatDock(snapshot, now));
    }

    [Fact]
    public void DockWindowLabelsShowRemainingUsageAndResetCountdowns()
    {
        var now = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var snapshot = new ProviderSnapshot(
            ProviderId.Codex,
            ProviderHealth.Available,
            [
                new UsageWindowSnapshot(UsageWindow.Session, 12, now.AddHours(2).AddMinutes(14), now),
                new UsageWindowSnapshot(UsageWindow.Weekly, 34, now.AddDays(3).AddHours(6), now),
            ],
            now,
            "codex app-server");

        Assert.Equal("88% - 2h 14m", UsageFormatting.FormatDockWindow(snapshot, UsageWindow.Session, now));
        Assert.Equal("66% - 3d 6h", UsageFormatting.FormatDockWindow(snapshot, UsageWindow.Weekly, now));
    }

    [Fact]
    public void DockWindowLabelsOmitZeroResetUnits()
    {
        var now = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var snapshot = new ProviderSnapshot(
            ProviderId.Claude,
            ProviderHealth.Available,
            [
                new UsageWindowSnapshot(UsageWindow.Session, 1, now.AddMinutes(14), now),
                new UsageWindowSnapshot(UsageWindow.Weekly, 2, now.AddDays(3), now),
            ],
            now,
            "claude status line");

        Assert.Equal("99% - 14m", UsageFormatting.FormatDockWindow(snapshot, UsageWindow.Session, now));
        Assert.Equal("98% - 3d", UsageFormatting.FormatDockWindow(snapshot, UsageWindow.Weekly, now));
    }

    [Fact]
    public void DockWindowLabelsOmitCountdownWhenResetIsDue()
    {
        var now = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var snapshot = new ProviderSnapshot(
            ProviderId.Codex,
            ProviderHealth.Available,
            [new UsageWindowSnapshot(UsageWindow.Session, 12, now, now)],
            now,
            "codex app-server");

        Assert.Equal("88%", UsageFormatting.FormatDockWindow(snapshot, UsageWindow.Session, now));
    }
}
