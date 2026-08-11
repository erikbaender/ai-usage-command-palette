using AIUsageDock.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Extension;

public sealed partial class AIUsageDockCommandsProvider : CommandProvider, IDisposable
{
    private readonly UsageCoordinator _coordinator;
    private readonly Settings _settings;
    private readonly UsageNotificationPreferences _notificationPreferences;
    private readonly UsageDockItem _codexSessionBand;
    private readonly UsageDockItem _codexWeeklyBand;
    private readonly UsageDockItem _claudeSessionBand;
    private readonly UsageDockItem _claudeWeeklyBand;
    private readonly UsageSettingsPage _settingsPage;
    private bool _disposed;

    public AIUsageDockCommandsProvider(
        UsageCoordinator coordinator,
        UsageNotificationPreferences notificationPreferences,
        ProviderActivityMonitor activityMonitor)
    {
        _coordinator = coordinator;
        _notificationPreferences = notificationPreferences;
        _settingsPage = new UsageSettingsPage(coordinator);
        _settings = _settingsPage.ExtensionSettings;
        Settings = _settings;
        DisplayName = "AI Usage Dock";
        Icon = new IconInfo("\uE945");
        _settings.Add(new ToggleSetting(
            "resetNotifications",
            "Limit reset notifications",
            "Show a Windows notification when a session or weekly limit resets.",
            true));
        _settings.Add(new ToggleSetting(
            "thresholdNotifications",
            "Remaining usage notifications",
            "Show a Windows notification when remaining usage passes below the configured threshold.",
            true));
        _settings.Add(new TextSetting(
            "remainingUsageThreshold",
            "Remaining usage threshold (%)",
            "Notify when remaining usage passes below this percentage. Enter a value from 0 to 100.",
            "50"));
        _settings.SettingsChanged += OnSettingsChanged;
        ApplySettings();
        _codexSessionBand = new UsageDockItem(ProviderId.Codex, UsageWindow.Session, coordinator, activityMonitor);
        _codexWeeklyBand = new UsageDockItem(ProviderId.Codex, UsageWindow.Weekly, coordinator, activityMonitor);
        _claudeSessionBand = new UsageDockItem(ProviderId.Claude, UsageWindow.Session, coordinator, activityMonitor);
        _claudeWeeklyBand = new UsageDockItem(ProviderId.Claude, UsageWindow.Weekly, coordinator, activityMonitor);

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
        _settings.SettingsChanged -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object sender, Settings settings) => ApplySettings();

    private void ApplySettings()
    {
        _notificationPreferences.ResetNotificationsEnabled = _settings.GetSetting<bool>("resetNotifications");
        _notificationPreferences.ThresholdNotificationsEnabled = _settings.GetSetting<bool>("thresholdNotifications");
        _notificationPreferences.RemainingUsageThreshold = UsageNotificationPreferences.ParseRemainingUsageThreshold(_settings.GetSetting<string>("remainingUsageThreshold"));
    }
}
