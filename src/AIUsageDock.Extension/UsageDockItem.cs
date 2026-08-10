using AIUsageDock.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Extension;

public sealed partial class UsageDockItem : ListItem, IDisposable
{
    private readonly ProviderId _provider;
    private readonly UsageWindow _window;
    private readonly UsageCoordinator _coordinator;
    private bool _disposed;

    public UsageDockItem(ProviderId provider, UsageWindow window, UsageCoordinator coordinator)
        : base(new UsageDetailsPage(provider, coordinator))
    {
        _provider = provider;
        _window = window;
        _coordinator = coordinator;
        Icon = IconHelpers.FromRelativePath(provider == ProviderId.Codex ? "Assets/openai.svg" : "Assets/claude.svg");
        Update(coordinator.GetSnapshot(provider));
        _coordinator.SnapshotChanged += OnSnapshotChanged;
    }

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
            _disposed = true;
        }
    }
}
