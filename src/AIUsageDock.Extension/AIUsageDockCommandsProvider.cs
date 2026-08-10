using AIUsageDock.Core;
using System.Diagnostics;
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
        new CommandItem(new InstallClaudeBridgeCommand()) { Title = "Enable Claude usage" },
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

public sealed partial class InstallClaudeBridgeCommand : InvokableCommand
{
    public InstallClaudeBridgeCommand()
    {
        Name = "Enable Claude usage";
        Icon = new IconInfo("\uE72E");
    }

    public override CommandResult Invoke()
    {
        var bridgePath = Path.Combine(AppContext.BaseDirectory, "Bridge", "AIUsageDock.Bridge.exe");
        if (!File.Exists(bridgePath))
        {
            return CommandResult.KeepOpen();
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = bridgePath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--install");
        startInfo.ArgumentList.Add("--bridge");
        startInfo.ArgumentList.Add(bridgePath);
        startInfo.ArgumentList.Add("--replace");
        Process.Start(startInfo);
        return CommandResult.KeepOpen();
    }
}
