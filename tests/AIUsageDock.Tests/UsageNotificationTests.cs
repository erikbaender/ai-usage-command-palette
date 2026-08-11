using AIUsageDock.Core;

namespace AIUsageDock.Tests;

public sealed class UsageNotificationTests
{
    [Fact]
    public void PreferencesEnableBothNotificationsAndUseFiftyPercentThresholdByDefault()
    {
        var preferences = new UsageNotificationPreferences();

        Assert.True(preferences.ResetNotificationsEnabled);
        Assert.True(preferences.ThresholdNotificationsEnabled);
        Assert.Equal(50, preferences.RemainingUsageThreshold);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("12.5", 12.5)]
    [InlineData("100", 100)]
    [InlineData("-1", 50)]
    [InlineData("101", 50)]
    [InlineData("not-a-number", 50)]
    public void ParsesConfigurableRemainingUsageThreshold(string value, double expected)
    {
        Assert.Equal(expected, UsageNotificationPreferences.ParseRemainingUsageThreshold(value));
    }

    [Fact]
    public void DetectsSessionAndWeeklyResetsAfterThePreviousResetTime()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var resetAt = observedAt.AddMinutes(5);
        var previous = Snapshot(observedAt, 10, 20, resetAt, resetAt);
        var current = Snapshot(observedAt.AddMinutes(6), 2, 5, resetAt.AddHours(5), resetAt.AddHours(5));

        var notifications = UsageNotificationDetector.Detect(previous, current, current.LastUpdated!.Value);

        Assert.Collection(
            notifications,
            notification =>
            {
                Assert.Equal(UsageNotificationKind.LimitReset, notification.Kind);
                Assert.Equal(UsageWindow.Session, notification.Window);
            },
            notification =>
            {
                Assert.Equal(UsageNotificationKind.LimitReset, notification.Kind);
                Assert.Equal(UsageWindow.Weekly, notification.Window);
            });
    }

    [Fact]
    public void DetectsThresholdOnlyWhenRemainingUsageCrossesDownward()
    {
        var time = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var previous = Snapshot(time, 20, 30, time.AddHours(1), time.AddDays(1));
        var current = Snapshot(time.AddMinutes(1), 51, 30, time.AddHours(1), time.AddDays(1));

        var notifications = UsageNotificationDetector.Detect(previous, current, current.LastUpdated!.Value);

        var notification = Assert.Single(notifications);
        Assert.Equal(UsageNotificationKind.RemainingThreshold, notification.Kind);
        Assert.Equal(UsageWindow.Session, notification.Window);
        Assert.Equal(49, notification.RemainingPercent);
    }

    [Fact]
    public void DetectsResetWhenProviderOmitsTheNextResetTime()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var resetAt = observedAt.AddMinutes(5);
        var weeklyResetAt = observedAt.AddDays(1);
        var previous = Snapshot(observedAt, 10, 20, resetAt, weeklyResetAt);
        var current = new ProviderSnapshot(
            ProviderId.Codex,
            ProviderHealth.Available,
            [
                new UsageWindowSnapshot(UsageWindow.Session, 0, null, resetAt.AddMinutes(1)),
                new UsageWindowSnapshot(UsageWindow.Weekly, 20, weeklyResetAt, resetAt.AddMinutes(1)),
            ],
            resetAt.AddMinutes(1),
            "test");

        var notifications = UsageNotificationDetector.Detect(previous, current, current.LastUpdated!.Value);

        var notification = Assert.Single(notifications);
        Assert.Equal(UsageNotificationKind.LimitReset, notification.Kind);
        Assert.Equal(UsageWindow.Session, notification.Window);
    }

    [Fact]
    public void DoesNotNotifyAgainWhenAWindowRemainsBelowTheThreshold()
    {
        var time = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var previous = Snapshot(time, 51, 20, time.AddHours(1), time.AddDays(1));
        var current = Snapshot(time.AddMinutes(1), 60, 20, time.AddHours(1), time.AddDays(1));

        Assert.Empty(UsageNotificationDetector.Detect(previous, current, current.LastUpdated!.Value));
    }

    private static ProviderSnapshot Snapshot(
        DateTimeOffset observedAt,
        double sessionUsed,
        double weeklyUsed,
        DateTimeOffset sessionReset,
        DateTimeOffset weeklyReset) =>
        new(
            ProviderId.Codex,
            ProviderHealth.Available,
            [
                new UsageWindowSnapshot(UsageWindow.Session, sessionUsed, sessionReset, observedAt),
                new UsageWindowSnapshot(UsageWindow.Weekly, weeklyUsed, weeklyReset, observedAt),
            ],
            observedAt,
            "test");
}
