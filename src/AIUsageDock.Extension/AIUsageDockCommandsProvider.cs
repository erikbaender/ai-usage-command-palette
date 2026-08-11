using AIUsageDock.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Extension;

public sealed partial class AIUsageDockCommandsProvider : CommandProvider, IDisposable
{
    private readonly UsageCoordinator _coordinator;
    private readonly UsageDockItem _codexSessionBand;
    private readonly UsageDockItem _codexWeeklyBand;
    private readonly UsageDockItem _claudeSessionBand;
    private readonly UsageDockItem _claudeWeeklyBand;
    private readonly UsageSettingsPage _settingsPage;
    private bool _disposed;

    public AIUsageDockCommandsProvider(UsageCoordinator coordinator)
    {
        _coordinator = coordinator;
        DisplayName = "AI Usage Dock";
        Icon = new IconInfo("\uE945");
        _codexSessionBand = new UsageDockItem(ProviderId.Codex, UsageWindow.Session, coordinator);
        _codexWeeklyBand = new UsageDockItem(ProviderId.Codex, UsageWindow.Weekly, coordinator);
        _claudeSessionBand = new UsageDockItem(ProviderId.Claude, UsageWindow.Session, coordinator);
        _claudeWeeklyBand = new UsageDockItem(ProviderId.Claude, UsageWindow.Weekly, coordinator);
        _settingsPage = new UsageSettingsPage(coordinator);
        Settings = _settingsPage.ExtensionSettings;
    }

    public override ICommandItem[] TopLevelCommands() =>
    [
        new CommandItem(new UsageDetailsPage(ProviderId.Codex, _coordinator)) { Title = "Codex usage" },
        new CommandItem(new UsageDetailsPage(ProviderId.Claude, _coordinator)) { Title = "Claude usage" },
    ];

    public override ICommandItem[]? GetDockBands() =>
    [
        new WrappedDockItem([_codexSessionBand], "com.erikbaender.aiusage.codex.session", "Codex Session"),
        new WrappedDockItem([_codexWeeklyBand], "com.erikbaender.aiusage.codex.weekly", "Codex Weekly"),
        new WrappedDockItem([_claudeSessionBand], "com.erikbaender.aiusage.claude.session", "Claude Session"),
        new WrappedDockItem([_claudeWeeklyBand], "com.erikbaender.aiusage.claude.weekly", "Claude Weekly"),
    ];

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _codexSessionBand.Dispose();
        _codexWeeklyBand.Dispose();
        _claudeSessionBand.Dispose();
        _claudeWeeklyBand.Dispose();
    }
}
