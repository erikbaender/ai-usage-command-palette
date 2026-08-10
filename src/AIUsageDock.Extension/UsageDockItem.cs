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
        Icon = new IconInfo(provider == ProviderId.Codex ? "\uE8A7" : "\uE77B");
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
        Title = UsageFormatting.FormatDock(snapshot, DateTimeOffset.UtcNow);
        Subtitle = snapshot.Health switch
        {
            ProviderHealth.Unavailable => $"{_provider} CLI not found",
            ProviderHealth.Unauthenticated => "Sign in through the installed CLI",
            ProviderHealth.Stale => "Last known values · stale",
            ProviderHealth.Error => "Refresh failed",
            ProviderHealth.Unknown => snapshot.Message ?? "Waiting for first provider response",
            _ => snapshot.Source,
        };
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
