using System.Globalization;
using System.Text.Json;
using AIUsageDock.Core;

namespace AIUsageDock.Tests;

public sealed class UsageNotificationScenarioTests
{
    [Fact]
    public void CodexWeeklyUsageCrossingThresholdShowsOneNotification()
    {
        var firstObservedAt = DateTimeOffset.Parse("2026-08-11T10:00:00Z");
        var secondObservedAt = firstObservedAt.AddMinutes(5);
        var resetAt = DateTimeOffset.Parse("2026-08-17T08:00:00Z");
        var sink = new RecordingNotificationSink();
        var tracker = new UsageNotificationTracker(new UsageNotificationPreferences(), sink);

        tracker.Observe(ParseCodex(CodexWeeklyPayload(49, resetAt), firstObservedAt), firstObservedAt);

        Assert.Empty(sink.Shown);

        tracker.Observe(ParseCodex(CodexWeeklyPayload(51, resetAt), secondObservedAt), secondObservedAt);

        var shown = Assert.Single(sink.Shown);
        Assert.Equal(ProviderId.Codex, shown.Notification.Provider);
        Assert.Equal(UsageWindow.Weekly, shown.Notification.Window);
        Assert.Equal(UsageNotificationKind.RemainingThreshold, shown.Notification.Kind);
        Assert.Equal(49, shown.Notification.RemainingPercent);
        Assert.Equal("Codex weekly usage remaining", shown.Title);
        Assert.Equal("49% of the limit remains.", shown.Body);

        tracker.Observe(ParseCodex(CodexWeeklyPayload(60, resetAt), secondObservedAt.AddMinutes(5)), secondObservedAt.AddMinutes(5));

        Assert.Single(sink.Shown);
    }

    [Fact]
    public void ClaudeSessionResetWithoutANextResetTimeShowsOneNotification()
    {
        var firstObservedAt = DateTimeOffset.Parse("2026-08-10T21:45:00Z");
        var secondObservedAt = DateTimeOffset.Parse("2026-08-10T21:51:00Z");
        var beforeReset = ClaudeUsagePayload(
            "Current session: 93% used · resets Aug 10, 9:50pm (UTC)",
            "Current week (all models): 48% used · resets Aug 12, 8am (UTC)");
        var afterReset = ClaudeUsagePayload(
            "Current session: 0% used",
            "Current week (all models): 49% used · resets Aug 12, 8am (UTC)");
        var sink = new RecordingNotificationSink();
        var tracker = new UsageNotificationTracker(new UsageNotificationPreferences(), sink);

        tracker.Observe(UsageJson.ParseClaudeCliUsage(beforeReset, firstObservedAt), firstObservedAt);
        tracker.Observe(UsageJson.ParseClaudeCliUsage(afterReset, secondObservedAt), secondObservedAt);

        var shown = Assert.Single(sink.Shown);
        Assert.Equal(ProviderId.Claude, shown.Notification.Provider);
        Assert.Equal(UsageWindow.Session, shown.Notification.Window);
        Assert.Equal(UsageNotificationKind.LimitReset, shown.Notification.Kind);
        Assert.Equal(DateTimeOffset.Parse("2026-08-10T21:50:00Z"), shown.Notification.ResetAt);
        Assert.Equal("Claude session limit reset", shown.Title);
        Assert.Equal("Your usage limit is available again.", shown.Body);
    }

    [Fact]
    public void PassedResetTimestampWithoutFreshUsageDoesNotShowResetNotification()
    {
        var firstObservedAt = DateTimeOffset.Parse("2026-08-10T21:49:59Z");
        var secondObservedAt = firstObservedAt.AddSeconds(2);
        var resetAt = DateTimeOffset.Parse("2026-08-10T21:50:00Z");
        var previous = Snapshot(100, resetAt, firstObservedAt);
        var unchanged = Snapshot(100, resetAt, secondObservedAt);
        var sink = new RecordingNotificationSink();
        var tracker = new UsageNotificationTracker(new UsageNotificationPreferences(), sink);

        tracker.Observe(previous, firstObservedAt);
        tracker.Observe(unchanged, secondObservedAt);

        Assert.Empty(sink.Shown);
    }

    [Fact]
    public void DisabledThresholdPreferenceDoesNotShowNotificationForARealCrossing()
    {
        var firstObservedAt = DateTimeOffset.Parse("2026-08-11T10:00:00Z");
        var secondObservedAt = firstObservedAt.AddMinutes(5);
        var resetAt = DateTimeOffset.Parse("2026-08-17T08:00:00Z");
        var preferences = new UsageNotificationPreferences { ThresholdNotificationsEnabled = false };
        var sink = new RecordingNotificationSink();
        var tracker = new UsageNotificationTracker(preferences, sink);

        tracker.Observe(ParseCodex(CodexWeeklyPayload(49, resetAt), firstObservedAt), firstObservedAt);
        tracker.Observe(ParseCodex(CodexWeeklyPayload(51, resetAt), secondObservedAt), secondObservedAt);

        Assert.Empty(sink.Shown);
    }

    [Fact]
    public void DisabledResetPreferenceDoesNotShowNotificationForARealReset()
    {
        var firstObservedAt = DateTimeOffset.Parse("2026-08-10T21:45:00Z");
        var secondObservedAt = DateTimeOffset.Parse("2026-08-10T21:51:00Z");
        var preferences = new UsageNotificationPreferences { ResetNotificationsEnabled = false };
        var sink = new RecordingNotificationSink();
        var tracker = new UsageNotificationTracker(preferences, sink);

        tracker.Observe(
            UsageJson.ParseClaudeCliUsage(
                ClaudeUsagePayload(
                    "Current session: 93% used · resets Aug 10, 9:50pm (UTC)",
                    "Current week (all models): 48% used · resets Aug 12, 8am (UTC)"),
                firstObservedAt),
            firstObservedAt);
        tracker.Observe(
            UsageJson.ParseClaudeCliUsage(
                ClaudeUsagePayload(
                    "Current session: 0% used",
                    "Current week (all models): 49% used · resets Aug 12, 8am (UTC)"),
                secondObservedAt),
            secondObservedAt);

        Assert.Empty(sink.Shown);
    }

    private static ProviderSnapshot ParseCodex(string json, DateTimeOffset observedAt)
    {
        using var document = JsonDocument.Parse(json);
        return UsageJson.ParseCodexRateLimits(document.RootElement, observedAt);
    }

    private static ProviderSnapshot Snapshot(double used, DateTimeOffset resetAt, DateTimeOffset observedAt) =>
        new(
            ProviderId.Claude,
            ProviderHealth.Available,
            [new UsageWindowSnapshot(UsageWindow.Session, used, resetAt, observedAt)],
            observedAt,
            "test");

    // Shape captured from codex-cli 0.147.0 account/rateLimits/read. Current accounts can
    // report a weekly-only primary bucket and a null secondary bucket.
    private static string CodexWeeklyPayload(double usedPercent, DateTimeOffset resetAt)
    {
        var used = usedPercent.ToString(CultureInfo.InvariantCulture);
        var reset = resetAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return $$"""
            {
              "id": "scenario-rate-limits",
              "result": {
                "rateLimits": {
                  "limitId": "codex",
                  "limitName": null,
                  "primary": { "usedPercent": {{used}}, "windowDurationMins": 10080, "resetsAt": {{reset}} },
                  "secondary": null,
                  "credits": { "hasCredits": true, "unlimited": false, "balance": "0" },
                  "individualLimit": null,
                  "spendControlReached": false,
                  "planType": "plus",
                  "rateLimitReachedType": null
                },
                "rateLimitsByLimitId": {
                  "codex": {
                    "limitId": "codex",
                    "limitName": null,
                    "primary": { "usedPercent": {{used}}, "windowDurationMins": 10080, "resetsAt": {{reset}} },
                    "secondary": null,
                    "credits": { "hasCredits": true, "unlimited": false, "balance": "0" },
                    "individualLimit": null,
                    "spendControlReached": false,
                    "planType": "plus",
                    "rateLimitReachedType": null
                  }
                },
                "rateLimitResetCredits": { "availableCount": 0, "credits": [] }
              }
            }
            """;
    }

    // Shape captured from Claude Code 2.1.227 with: claude -p "/usage"
    // --output-format json --no-session-persistence. Identifiers are inert test UUIDs.
    private static string ClaudeUsagePayload(string sessionLine, string weeklyLine)
    {
        var result = JsonSerializer.Serialize($"You are currently using your subscription to power your Claude Code usage\n\n{sessionLine}\n{weeklyLine}");
        return $$"""
            {
              "is_error": false,
              "duration_api_ms": 0,
              "num_turns": 0,
              "stop_reason": null,
              "session_id": "00000000-0000-0000-0000-000000000001",
              "total_cost_usd": 0,
              "usage": {
                "input_tokens": 0,
                "cache_creation_input_tokens": 0,
                "cache_read_input_tokens": 0,
                "output_tokens": 0,
                "server_tool_use": { "web_search_requests": 0, "web_fetch_requests": 0 },
                "service_tier": "standard",
                "cache_creation": { "ephemeral_1h_input_tokens": 0, "ephemeral_5m_input_tokens": 0 },
                "inference_geo": "",
                "iterations": [],
                "speed": "standard"
              },
              "modelUsage": {},
              "permission_denials": [],
              "fast_mode_state": "off",
              "fast_mode_disabled_reason": "sdk_opt_in_required",
              "subtype": "success",
              "result": {{result}},
              "type": "result",
              "duration_ms": 58,
              "uuid": "00000000-0000-0000-0000-000000000002"
            }
            """;
    }

    private sealed class RecordingNotificationSink : IUsageNotificationSink
    {
        public List<UsageNotificationMessage> Shown { get; } = new();

        public void Show(UsageNotificationMessage message) => Shown.Add(message);
    }
}
