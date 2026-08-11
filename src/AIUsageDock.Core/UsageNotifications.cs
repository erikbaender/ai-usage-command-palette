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

public sealed record UsageNotificationMessage(
    UsageNotification Notification,
    string Title,
    string Body);

public interface IUsageNotificationSink
{
    void Show(UsageNotificationMessage message);
}

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

public sealed class UsageNotificationTracker
{
    private readonly UsageNotificationPreferences _preferences;
    private readonly IUsageNotificationSink _sink;
    private readonly Dictionary<ProviderId, ProviderSnapshot> _previousSnapshots = new();
    private readonly object _gate = new();

    public UsageNotificationTracker(
        UsageNotificationPreferences preferences,
        IUsageNotificationSink sink)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(sink);
        _preferences = preferences;
        _sink = sink;
    }

    public void Observe(ProviderSnapshot current, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Health != ProviderHealth.Available)
        {
            return;
        }

        ProviderSnapshot? previous;
        lock (_gate)
        {
            _previousSnapshots.TryGetValue(current.Provider, out previous);
            _previousSnapshots[current.Provider] = current;
        }

        if (previous is null)
        {
            return;
        }

        var notifications = UsageNotificationDetector.Detect(
            previous,
            current,
            now,
            _preferences.RemainingUsageThreshold);

        foreach (var notification in notifications)
        {
            if (IsEnabled(notification.Kind))
            {
                _sink.Show(UsageNotificationText.CreateMessage(notification));
            }
        }
    }

    private bool IsEnabled(UsageNotificationKind kind) => kind switch
    {
        UsageNotificationKind.LimitReset => _preferences.ResetNotificationsEnabled,
        UsageNotificationKind.RemainingThreshold => _preferences.ThresholdNotificationsEnabled,
        _ => false,
    };
}

public static class UsageNotificationText
{
    public static UsageNotificationMessage CreateMessage(UsageNotification notification) =>
        new(notification, GetTitle(notification), GetBody(notification));

    public static string GetTitle(UsageNotification notification) =>
        notification.Kind == UsageNotificationKind.LimitReset
            ? $"{notification.Provider} {FormatWindow(notification.Window)} limit reset"
            : $"{notification.Provider} {FormatWindow(notification.Window)} usage remaining";

    public static string GetBody(UsageNotification notification) =>
        notification.Kind == UsageNotificationKind.LimitReset
            ? "Your usage limit is available again."
            : $"{notification.RemainingPercent!.Value:0}% of the limit remains.";

    private static string FormatWindow(UsageWindow window) => window switch
    {
        UsageWindow.Session => "session",
        UsageWindow.Weekly => "weekly",
        _ => window.ToString().ToLowerInvariant(),
    };
}
