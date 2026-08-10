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
}
