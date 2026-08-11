using AIUsageDock.Core;

namespace AIUsageDock.Tests;

public sealed class UsageJsonTests
{
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
    public void ParsesClaudeCliUsageWithMojibakeBulletSeparator()
    {
        var observed = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var snapshot = UsageJson.ParseClaudeCliUsage(
            "{\"is_error\":false,\"result\":\"Current session: 10% used \\u00c2\\u00b7 resets Aug 10, 9:50pm (UTC)\\nCurrent week (all models): 59% used \\u00c2\\u00b7 resets Aug 12, 8am (UTC)\"}",
            observed);

        Assert.Equal(10, snapshot.GetWindow(UsageWindow.Session)!.UsedPercent);
        Assert.Equal(41, snapshot.GetWindow(UsageWindow.Weekly)!.RemainingPercent);
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
    public void ExcessivePayloadIsRejected()
    {
        var payload = "{\"result\":\"" + new string('x', UsageJson.MaxPayloadBytes) + "\"}";

        Assert.Throws<System.Text.Json.JsonException>(() => UsageJson.ParseClaudeCliUsage(payload, DateTimeOffset.UtcNow));
    }
}
