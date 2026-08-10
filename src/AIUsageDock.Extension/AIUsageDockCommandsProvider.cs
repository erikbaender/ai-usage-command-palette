using AIUsageDock.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Extension;

public sealed partial class AIUsageDockCommandsProvider : CommandProvider, IDisposable
{
    private readonly UsageCoordinator _coordinator;
    private readonly UsageDockItem _codexBand;
    private readonly UsageDockItem _claudeBand;
    private bool _disposed;

    public AIUsageDockCommandsProvider(UsageCoordinator coordinator)
    {
        _coordinator = coordinator;
        DisplayName = "AI Usage Dock";
        Icon = new IconInfo("\uE945");
        _codexBand = new UsageDockItem(ProviderId.Codex, coordinator);
        _claudeBand = new UsageDockItem(ProviderId.Claude, coordinator);
    }

    public override ICommandItem[] TopLevelCommands() =>
    [
        new CommandItem(new UsageDetailsPage(ProviderId.Codex, _coordinator)) { Title = "Codex usage" },
        new CommandItem(new UsageDetailsPage(ProviderId.Claude, _coordinator)) { Title = "Claude usage" },
    ];

    public override ICommandItem[]? GetDockBands() =>
    [
        new WrappedDockItem([_codexBand], "com.erikbaender.aiusage.codex", "Codex"),
        new WrappedDockItem([_claudeBand], "com.erikbaender.aiusage.claude", "Claude"),
    ];

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _codexBand.Dispose();
        _claudeBand.Dispose();
    }
}
