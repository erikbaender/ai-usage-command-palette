using AIUsageDock.Core;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Extension;

public sealed class UsageSettingsPage
{
    public const string IdleIntervalKey = "idlePollingInterval";
    // Keep the persisted key stable so existing user settings survive the semantic rename.
    public const string ActiveUsageIntervalKey = "runningSessionPollingInterval";
    public const string BlinkExpirationKey = "blinkExpirationSeconds";

    private readonly UsageCoordinator _coordinator;
    private readonly ProviderActivityMonitor _activityMonitor;

    public UsageSettingsPage(UsageCoordinator coordinator, ProviderActivityMonitor activityMonitor)
    {
        _coordinator = coordinator;
        _activityMonitor = activityMonitor;
        ExtensionSettings = new Settings();
        ExtensionSettings.Add(new ChoiceSetSetting(
            IdleIntervalKey,
            "Idle polling interval",
            "How often to check for provider usage changes.",
            CreateIntervalChoices(UsagePollingPolicy.Default.IdleInterval)));
        ExtensionSettings.Add(new ChoiceSetSetting(
            ActiveUsageIntervalKey,
            "Active usage polling interval",
            "Delay after each usage refresh while provider usage is changing.",
            CreateIntervalChoices(UsagePollingPolicy.Default.ActiveUsageInterval)));
        ExtensionSettings.Add(new ChoiceSetSetting(
            BlinkExpirationKey,
            "Blink expiration",
            "How long the active indicator keeps blinking after the most recent usage increase.",
            CreateBlinkExpirationChoices()));
        ExtensionSettings.SettingsChanged += OnSettingsChanged;
        ApplySettings();
    }

    public Settings ExtensionSettings { get; }

    private void OnSettingsChanged(object sender, Settings args) => ApplySettings();

    private void ApplySettings()
    {
        _coordinator.UpdatePollingPolicy(ReadPollingPolicy());
        _activityMonitor.UpdateBlinkExpiration(ParseInterval(
            ExtensionSettings.GetSetting<string>(BlinkExpirationKey),
            ProviderUsageActivityTracker.DefaultActiveHoldDuration));
    }

    private UsagePollingPolicy ReadPollingPolicy() => new(
        ParseInterval(ExtensionSettings.GetSetting<string>(IdleIntervalKey), UsagePollingPolicy.Default.IdleInterval),
        ParseInterval(ExtensionSettings.GetSetting<string>(ActiveUsageIntervalKey), UsagePollingPolicy.Default.ActiveUsageInterval));

    private static TimeSpan ParseInterval(string? value, TimeSpan fallback) =>
        int.TryParse(value, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : fallback;

    private static List<ChoiceSetSetting.Choice> CreateIntervalChoices(TimeSpan defaultInterval)
    {
        var choices = new[] { 1, 5, 15, 30, 60, 120, 300 }
            .Where(seconds => seconds != (int)defaultInterval.TotalSeconds)
            .Select(seconds => new ChoiceSetSetting.Choice(FormatInterval(seconds), seconds.ToString()))
            .ToList();
        choices.Insert(0, new ChoiceSetSetting.Choice(FormatInterval((int)defaultInterval.TotalSeconds), ((int)defaultInterval.TotalSeconds).ToString()));
        return choices;
    }

    private static List<ChoiceSetSetting.Choice> CreateBlinkExpirationChoices() =>
        new[] { 5, 15, 30, 60, 120, 300, 600 }
            .OrderBy(seconds => seconds == (int)ProviderUsageActivityTracker.DefaultActiveHoldDuration.TotalSeconds ? 0 : 1)
            .ThenBy(seconds => seconds)
            .Select(seconds => new ChoiceSetSetting.Choice(FormatInterval(seconds), seconds.ToString()))
            .ToList();

    private static string FormatInterval(int seconds) => seconds switch
    {
        1 => "1 second",
        < 60 => $"{seconds} seconds",
        60 => "1 minute",
        _ => $"{seconds / 60} minutes",
    };
}
