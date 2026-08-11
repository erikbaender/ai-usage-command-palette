using AIUsageDock.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Extension;

public sealed partial class UsageDockItem : ListItem, IDisposable
{
    private readonly ProviderId _provider;
    private readonly UsageWindow _window;
    private readonly UsageCoordinator _coordinator;
    private readonly ProviderActivityMonitor _activityMonitor;
    private readonly IconInfo _fullOpacityIcon;
    private readonly IconInfo _halfOpacityIcon;
    private bool _disposed;

    public UsageDockItem(
        ProviderId provider,
        UsageWindow window,
        UsageCoordinator coordinator,
        ProviderActivityMonitor activityMonitor)
        : base(new UsageDetailsPage(provider, coordinator))
    {
        _provider = provider;
        _window = window;
        _coordinator = coordinator;
        _activityMonitor = activityMonitor;
        var iconName = provider == ProviderId.Codex ? "openai" : "claude";
        _fullOpacityIcon = IconHelpers.FromRelativePath($"Assets/{iconName}.svg");
        _halfOpacityIcon = IconHelpers.FromRelativePath($"Assets/{iconName}-dim.svg");
        ApplyActivityState(activityMonitor.GetState(provider));
        Update(coordinator.GetSnapshot(provider));
        _coordinator.SnapshotChanged += OnSnapshotChanged;
        _activityMonitor.ActivityChanged += OnActivityChanged;
    }

    private void OnActivityChanged(object? sender, ProviderActivityState state)
    {
        if (state.Provider == _provider)
        {
            ApplyActivityState(state);
        }
    }

    private void ApplyActivityState(ProviderActivityState state) =>
        Icon = state.IsActive && state.IsDimmed ? _halfOpacityIcon : _fullOpacityIcon;

    private void OnSnapshotChanged(object? sender, ProviderSnapshot snapshot)
    {
        if (snapshot.Provider == _provider)
        {
            Update(snapshot);
        }
    }

    private void Update(ProviderSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        Title = UsageFormatting.FormatDockWindow(snapshot, _window, now);
        Subtitle = $"{_provider} {_window switch
        {
            UsageWindow.Session => "Session",
            UsageWindow.Weekly => "Weekly",
            _ => _window.ToString(),
        }}";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _coordinator.SnapshotChanged -= OnSnapshotChanged;
            _activityMonitor.ActivityChanged -= OnActivityChanged;
            _disposed = true;
        }
    }
}
