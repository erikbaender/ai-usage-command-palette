using AIUsageDock.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Extension;

public sealed partial class UsageDetailsPage : ContentPage
{
    private readonly ProviderId _provider;
    private readonly UsageCoordinator _coordinator;

    public UsageDetailsPage(ProviderId provider, UsageCoordinator coordinator)
    {
        _provider = provider;
        _coordinator = coordinator;
        Title = $"{provider} usage";
        Name = $"ai-usage-dock.{provider.ToString().ToLowerInvariant()}";
        Commands =
        [
            new CommandContextItem(new RefreshUsageCommand(provider, coordinator)),
        ];
        _coordinator.SnapshotChanged += OnSnapshotChanged;
    }

    public override IContent[] GetContent()
    {
        var snapshot = _coordinator.GetSnapshot(_provider);
        return
        [
            new MarkdownContent
            {
                Body = UsageFormatting.FormatDetails(snapshot, DateTimeOffset.UtcNow),
            },
        ];
    }

    private void OnSnapshotChanged(object? sender, ProviderSnapshot snapshot)
    {
        if (snapshot.Provider == _provider)
        {
            RaiseItemsChanged();
        }
    }
}

public sealed partial class UsageOverviewPage : ContentPage
{
    private readonly UsageCoordinator _coordinator;

    public UsageOverviewPage(UsageCoordinator coordinator)
    {
        _coordinator = coordinator;
        Title = "AI Usage Dock";
        Name = "ai-usage-dock.overview";
        _coordinator.SnapshotChanged += OnSnapshotChanged;
    }

    public override IContent[] GetContent()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new MarkdownContent
            {
                Body = string.Join(Environment.NewLine + Environment.NewLine,
                    UsageFormatting.FormatDetails(_coordinator.GetSnapshot(ProviderId.Codex), now),
                    UsageFormatting.FormatDetails(_coordinator.GetSnapshot(ProviderId.Claude), now)),
            },
        ];
    }

    private void OnSnapshotChanged(object? sender, ProviderSnapshot snapshot) => RaiseItemsChanged();
}

public sealed partial class RefreshUsageCommand : InvokableCommand
{
    private readonly ProviderId _provider;
    private readonly UsageCoordinator _coordinator;

    public RefreshUsageCommand(ProviderId provider, UsageCoordinator coordinator)
    {
        _provider = provider;
        _coordinator = coordinator;
        Name = "Refresh usage";
        Icon = new IconInfo("\uE72C");
    }

    public override CommandResult Invoke()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _coordinator.RefreshProviderAsync(_provider, CancellationToken.None);
            }
            catch (Exception)
            {
            }
        });
        return CommandResult.KeepOpen();
    }
}

public sealed partial class ConnectWebUsageCommand : InvokableCommand
{
    private readonly IWebUsageSession _webSession;

    public ConnectWebUsageCommand(IWebUsageSession webSession)
    {
        _webSession = webSession;
        Name = $"Connect {webSession.Provider} web usage";
        Icon = new IconInfo("\uE77B");
    }

    public override CommandResult Invoke()
    {
        _webSession.ShowConnectionWindow();
        return CommandResult.KeepOpen();
    }
}
