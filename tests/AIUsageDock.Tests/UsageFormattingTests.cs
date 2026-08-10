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
    public void DockLabelsShowRemainingUsageAndResetCountdowns()
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

        Assert.Equal("Ses 88%/2h14m", UsageFormatting.FormatDockSession(snapshot, now));
        Assert.Equal("Wk 66%/3d6h", UsageFormatting.FormatDockWeekly(snapshot, now));
    }
}
