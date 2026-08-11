using AIUsageDock.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AIUsageDock.Extension;

public sealed class UsageNotificationService : IDisposable
{
    private readonly UsageCoordinator _coordinator;
    private readonly UsageNotificationTracker _tracker;
    private readonly WindowsUsageNotificationSink _sink;
    private bool _disposed;

    public UsageNotificationService(UsageCoordinator coordinator, UsageNotificationPreferences preferences)
    {
        _coordinator = coordinator;
        _sink = new WindowsUsageNotificationSink();
        _tracker = new UsageNotificationTracker(preferences, _sink);
        _coordinator.SnapshotChanged += OnSnapshotChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.SnapshotChanged -= OnSnapshotChanged;
        _sink.Dispose();
    }

    private void OnSnapshotChanged(object? sender, ProviderSnapshot current)
    {
        _tracker.Observe(current, DateTimeOffset.UtcNow);
    }
}

internal sealed class WindowsUsageNotificationSink : IUsageNotificationSink, IDisposable
{
    private readonly AppNotificationManager? _manager;
    private bool _registered;

    public Exception? LastError { get; private set; }

    public WindowsUsageNotificationSink()
    {
        try
        {
            _manager = AppNotificationManager.Default;
            _manager.Register();
            _registered = true;
        }
        catch (Exception exception)
        {
            LastError = exception;
            // Notifications are optional. Registration failures must not stop the extension.
        }
    }

    public void Show(UsageNotificationMessage message)
    {
        TryShow(message);
    }

    public bool TryShow(UsageNotificationMessage message)
    {
        if (!_registered || _manager is null)
        {
            return false;
        }

        try
        {
            var toast = new AppNotificationBuilder()
                .AddText(message.Title)
                .AddText(message.Body)
                .BuildNotification();
            _manager.Show(toast);
            return true;
        }
        catch (Exception exception)
        {
            LastError = exception;
            // Notification failures are non-fatal and should not interrupt refreshes.
            return false;
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            try { _manager?.Unregister(); } catch { }
            _registered = false;
        }
    }
}
