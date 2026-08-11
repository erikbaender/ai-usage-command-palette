using AIUsageDock.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AIUsageDock.Extension;

public sealed class UsageNotificationService : IDisposable
{
    private readonly UsageCoordinator _coordinator;
    private readonly UsageNotificationPreferences _preferences;
    private readonly AppNotificationManager? _manager;
    private readonly Dictionary<ProviderId, ProviderSnapshot> _previousSnapshots = new();
    private readonly object _gate = new();
    private bool _registered;
    private bool _disposed;

    public UsageNotificationService(UsageCoordinator coordinator, UsageNotificationPreferences preferences)
    {
        _coordinator = coordinator;
        _preferences = preferences;
        _coordinator.SnapshotChanged += OnSnapshotChanged;

        try
        {
            _manager = AppNotificationManager.Default;
            _manager.Register();
            _registered = true;
        }
        catch
        {
            // Notifications are optional. Registration failures must not stop the extension.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.SnapshotChanged -= OnSnapshotChanged;
        if (_registered)
        {
            try { _manager?.Unregister(); } catch { }
        }
    }

    private void OnSnapshotChanged(object? sender, ProviderSnapshot current)
    {
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

        if (previous is null || _manager is null || !_registered)
        {
            return;
        }

        var notifications = UsageNotificationDetector.Detect(
            previous,
            current,
            DateTimeOffset.UtcNow,
            _preferences.RemainingUsageThreshold);

        foreach (var notification in notifications)
        {
            if ((notification.Kind == UsageNotificationKind.LimitReset && !_preferences.ResetNotificationsEnabled) ||
                (notification.Kind == UsageNotificationKind.RemainingThreshold && !_preferences.ThresholdNotificationsEnabled))
            {
                continue;
            }

            Show(notification);
        }
    }

    private void Show(UsageNotification notification)
    {
        try
        {
            var toast = new AppNotificationBuilder()
                .AddText(GetTitle(notification))
                .AddText(GetBody(notification))
                .BuildNotification();
            _manager!.Show(toast);
        }
        catch
        {
            // Notification failures are non-fatal and should not interrupt refreshes.
        }
    }

    private static string GetTitle(UsageNotification notification) =>
        notification.Kind == UsageNotificationKind.LimitReset
            ? $"{notification.Provider} {FormatWindow(notification.Window)} limit reset"
            : $"{notification.Provider} {FormatWindow(notification.Window)} usage remaining";

    private static string GetBody(UsageNotification notification) =>
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
