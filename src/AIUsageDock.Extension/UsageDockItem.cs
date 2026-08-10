using AIUsageDock.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Extension;

public sealed partial class UsageDockItem : ListItem, IDisposable
{
    private readonly ProviderId _provider;
    private readonly UsageCoordinator _coordinator;
    private bool _disposed;

    public UsageDockItem(ProviderId provider, UsageCoordinator coordinator)
        : base(new UsageDetailsPage(provider, coordinator))
    {
        _provider = provider;
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
        Title = UsageFormatting.FormatDockWeekly(snapshot, now);
        Subtitle = UsageFormatting.FormatDockSession(snapshot, now);
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
