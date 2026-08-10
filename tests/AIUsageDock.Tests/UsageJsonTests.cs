using AIUsageDock.Core;

namespace AIUsageDock.Tests;

public sealed class UsageJsonTests
{
    [Fact]
    public void ParsesClaudeRateLimitsAndUnixResetTimes()
    {
        var observed = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var snapshot = UsageJson.ParseClaudeStatusLine(
            """{"rate_limits":{"five_hour":{"used_percentage":23.5,"resets_at":1786363200},"seven_day":{"used_percentage":41.2,"resets_at":1786780800}}}""",
            observed);

        Assert.Equal(ProviderHealth.Available, snapshot.Health);
        Assert.Equal(23.5, snapshot.GetWindow(UsageWindow.Session)!.UsedPercent);
        Assert.Equal(58.8, snapshot.GetWindow(UsageWindow.Weekly)!.RemainingPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786363200), snapshot.GetWindow(UsageWindow.Session)!.ResetsAt);
    }

    [Fact]
    public void RejectsOutOfRangePercentagesWithoutFabricatingZero()
    {
        var snapshot = UsageJson.ParseClaudeStatusLine(
            """{"rate_limits":{"five_hour":{"used_percentage":101},"seven_day":{"used_percentage":-1}}}""",
            DateTimeOffset.UtcNow);

        Assert.Equal(ProviderHealth.Available, snapshot.Health);
        Assert.Null(snapshot.GetWindow(UsageWindow.Session)!.UsedPercent);
        Assert.Null(snapshot.GetWindow(UsageWindow.Weekly)!.UsedPercent);
    }

    [Fact]
    public void ParsesClaudeCliUsageResultAndResetTime()
    {
        var observed = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var snapshot = UsageJson.ParseClaudeCliUsage(
            """{"is_error":false,"result":"Current session: 0% used · resets Aug 10, 9:50pm (UTC)\nCurrent week (all models): 59% used · resets Aug 12, 8am (UTC)"}""",
            observed);

        Assert.Equal(ProviderHealth.Available, snapshot.Health);
        Assert.Equal(0, snapshot.GetWindow(UsageWindow.Session)!.UsedPercent);
        Assert.Equal(41, snapshot.GetWindow(UsageWindow.Weekly)!.RemainingPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-08-10T21:50:00Z"), snapshot.GetWindow(UsageWindow.Session)!.ResetsAt);
        Assert.Equal("Claude CLI · claude -p /usage", snapshot.Source);
    }

    [Fact]
    public void RejectsClaudeCliUsageWithoutRecognizedWindows()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => UsageJson.ParseClaudeCliUsage(
            """{"is_error":false,"result":"No usage information"}""",
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ParsesCodexPrimaryAndSecondaryBuckets()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """{"id":"1","result":{"rateLimits":{"primary":{"usedPercent":42,"windowDurationMins":300,"resetsAt":1786363200},"secondary":{"usedPercent":68,"windowDurationMins":10080,"resetsAt":1786780800},"planType":"pro"}}}""");

        var snapshot = UsageJson.ParseCodexRateLimits(document.RootElement, DateTimeOffset.UtcNow);

        Assert.Equal(ProviderHealth.Available, snapshot.Health);
        Assert.Equal(42, snapshot.GetWindow(UsageWindow.Session)!.UsedPercent);
        Assert.Equal(68, snapshot.GetWindow(UsageWindow.Weekly)!.UsedPercent);
    }

    [Fact]
    public void MissingClaudeLimitsIsExplicitWaitingState()
    {
        var snapshot = UsageJson.ParseClaudeStatusLine("""{"model":{"display_name":"Opus"}}""", DateTimeOffset.UtcNow);

        Assert.Equal(ProviderHealth.Unknown, snapshot.Health);
        Assert.Contains("Waiting", snapshot.Message);
        Assert.Empty(snapshot.Windows);
    }

    [Fact]
    public void ExcessivePayloadIsRejected()
    {
        var payload = "{\"rate_limits\":{}}" + new string('x', UsageJson.MaxPayloadBytes);

        Assert.Throws<System.Text.Json.JsonException>(() => UsageJson.ParseClaudeStatusLine(payload, DateTimeOffset.UtcNow));
    }
}
