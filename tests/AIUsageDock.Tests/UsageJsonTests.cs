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
    public void ParsesClaudeWebUsageResponse()
    {
        var observed = DateTimeOffset.Parse("2026-08-11T21:00:00Z");
        var snapshot = UsageJson.ParseClaudeWebUsage(
            """{"five_hour":{"resets_at":"2026-08-12T01:40:00.462260+00:00","utilization":45},"seven_day":{"resets_at":"2026-08-12T06:00:00.462287+00:00","utilization":75}}""",
            observed);

        Assert.Equal(45, snapshot.GetWindow(UsageWindow.Session)!.UsedPercent);
        Assert.Equal(75, snapshot.GetWindow(UsageWindow.Weekly)!.UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-08-12T01:40:00.462260Z"), snapshot.GetWindow(UsageWindow.Session)!.ResetsAt);
        Assert.Equal("Claude web usage", snapshot.Source);
    }

    [Fact]
    public void ParsesClaudeCliSessionWhenResetIsNotReportedAfterReset()
    {
        var observed = DateTimeOffset.Parse("2026-08-11T01:00:00Z");
        var snapshot = UsageJson.ParseClaudeCliUsage(
            """{"is_error":false,"result":"Current session: 0% used\nCurrent week (all models): 67% used · resets Aug 12, 8am (Europe/Berlin)"}""",
            observed);

        var session = snapshot.GetWindow(UsageWindow.Session)!;
        Assert.Equal(0, session.UsedPercent);
        Assert.Null(session.ResetsAt);
        Assert.Equal(33, snapshot.GetWindow(UsageWindow.Weekly)!.RemainingPercent);
    }

    [Fact]
    public void RollsPastClaudeSessionResetForwardByFiveHourCadence()
    {
        var observed = DateTimeOffset.Parse("2026-08-11T02:59:00Z");
        var snapshot = UsageJson.ParseClaudeCliUsage(
            """{"is_error":false,"result":"Current session: 100% used · resets Aug 10, 3:50am (UTC)"}""",
            observed);

        Assert.Equal(DateTimeOffset.Parse("2026-08-11T04:50:00Z"), snapshot.GetWindow(UsageWindow.Session)!.ResetsAt);
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
    public void ParsesCodexWebUsageResponse()
    {
        var observed = DateTimeOffset.Parse("2026-08-12T10:00:00Z");
        var snapshot = UsageJson.ParseCodexWebUsage(
            """{"plan_type":"pro","rate_limit":{"primary_window":{"used_percent":37,"reset_at":1786532400,"limit_window_seconds":18000},"secondary_window":{"used_percent":64,"reset_at":1786964400,"limit_window_seconds":604800}}}""",
            observed);

        Assert.Equal(37, snapshot.GetWindow(UsageWindow.Session)!.UsedPercent);
        Assert.Equal(64, snapshot.GetWindow(UsageWindow.Weekly)!.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786532400), snapshot.GetWindow(UsageWindow.Session)!.ResetsAt);
        Assert.Equal("Codex web usage", snapshot.Source);
        Assert.Equal("pro", snapshot.PlanType);
    }

    [Fact]
    public void ExcessivePayloadIsRejected()
    {
        var payload = "{\"result\":\"" + new string('x', UsageJson.MaxPayloadBytes) + "\"}";

        Assert.Throws<System.Text.Json.JsonException>(() => UsageJson.ParseClaudeCliUsage(payload, DateTimeOffset.UtcNow));
    }
}
