using System.Globalization;

namespace AIUsageDock.Core;

public enum UsageNotificationKind
{
    LimitReset,
    RemainingThreshold,
}

public sealed record UsageNotification(
    ProviderId Provider,
    UsageWindow Window,
    UsageNotificationKind Kind,
    double? RemainingPercent = null,
    DateTimeOffset? ResetAt = null);

public sealed class UsageNotificationPreferences
{
    public const double DefaultRemainingUsageThreshold = 50;

    public bool ResetNotificationsEnabled { get; set; } = true;

    public bool ThresholdNotificationsEnabled { get; set; } = true;

    public double RemainingUsageThreshold { get; set; } = DefaultRemainingUsageThreshold;

    public static double ParseRemainingUsageThreshold(string? value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            !double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            return DefaultRemainingUsageThreshold;
        }

        return UsagePercent.IsValid(parsed) ? parsed : DefaultRemainingUsageThreshold;
    }
}

public static class UsageNotificationDetector
{
    public static IReadOnlyList<UsageNotification> Detect(
        ProviderSnapshot previous,
        ProviderSnapshot current,
        DateTimeOffset now,
        double remainingUsageThreshold = UsageNotificationPreferences.DefaultRemainingUsageThreshold)
    {
        if (previous.Provider != current.Provider ||
            current.Health != ProviderHealth.Available ||
            !UsagePercent.IsValid(remainingUsageThreshold))
        {
            return Array.Empty<UsageNotification>();
        }

        var notifications = new List<UsageNotification>();
        foreach (var window in new[] { UsageWindow.Session, UsageWindow.Weekly })
        {
            var oldWindow = previous.GetWindow(window);
            var newWindow = current.GetWindow(window);
            if (oldWindow is null || newWindow is null)
            {
                continue;
            }

            if (HasReachedReset(oldWindow, newWindow, now))
            {
                notifications.Add(new UsageNotification(
                    current.Provider,
                    window,
                    UsageNotificationKind.LimitReset,
                    ResetAt: oldWindow.ResetsAt));
            }

            if (oldWindow.RemainingPercent is double oldRemaining &&
                newWindow.RemainingPercent is double newRemaining &&
                oldRemaining > remainingUsageThreshold &&
                newRemaining <= remainingUsageThreshold)
            {
                notifications.Add(new UsageNotification(
                    current.Provider,
                    window,
                    UsageNotificationKind.RemainingThreshold,
                    newRemaining));
            }
        }

        return notifications;
    }

    private static bool HasReachedReset(
        UsageWindowSnapshot previous,
        UsageWindowSnapshot current,
        DateTimeOffset now)
    {
        if (previous.ResetsAt is not DateTimeOffset previousReset ||
            previousReset <= previous.ObservedAt ||
            now < previousReset)
        {
            return false;
        }

        return current.ObservedAt >= previousReset ||
            current.ResetsAt is DateTimeOffset currentReset && currentReset > previousReset;
    }
}
