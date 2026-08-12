using AIUsageDock.Core;
using Microsoft.Windows.AppLifecycle;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace AIUsageDock.Extension;

public static class Program
{
    [MTAThread]
    public static async Task Main(string[] args)
    {
        if (args.Contains("--notification-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            using var sink = new WindowsUsageNotificationSink();
            var notification = new UsageNotification(
                ProviderId.Codex,
                UsageWindow.Weekly,
                UsageNotificationKind.RemainingThreshold,
                RemainingPercent: 49);
            var shown = sink.TryShow(UsageNotificationText.CreateMessage(notification));
            WriteNotificationSmokeTestResult(shown, sink.LastError);
            Environment.ExitCode = shown ? 0 : 1;
            return;
        }

        if (args.Length == 0 || !args.Contains("-RegisterProcessAsComServer", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        await RunComServerAsync();
    }

    private static void WriteNotificationSmokeTestResult(bool shown, Exception? error)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIUsage");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "notification-smoke-test.log"),
                $"{DateTimeOffset.UtcNow:O} shown={shown}{Environment.NewLine}{error}");
        }
        catch
        {
            // Smoke-test diagnostics must not affect the process result.
        }
    }

    private static async Task RunComServerAsync()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        await using var server = new ComServer();
        using var disposed = new ManualResetEvent(false);
        using var activityMonitor = new ProviderActivityMonitor();
        using var claudeWebSession = new ClaudeWebViewUsageClient();
        using var codexWebSession = new CodexWebViewUsageClient();
        var backendPreferences = new AIUsageDock.Providers.UsageBackendPreferences();
        await using var coordinator = UsageCoordinator.CreateDefault(
            claudeSessionDetector: activityMonitor,
            claudeWebUsageClient: claudeWebSession,
            codexWebUsageClient: codexWebSession,
            backendPreferences: backendPreferences);
        coordinator.SnapshotChanged += activityMonitor.ObserveSnapshot;
        var notificationPreferences = new UsageNotificationPreferences();
        using var provider = new AIUsageDockCommandsProvider(
            coordinator,
            notificationPreferences,
            backendPreferences,
            activityMonitor,
            claudeWebSession,
            codexWebSession);
        using var notificationService = new UsageNotificationService(
            coordinator,
            notificationPreferences,
            claudeWebSession,
            codexWebSession);
        coordinator.Start();
        var extension = new AIUsageDockExtension(disposed, provider);
        server.RegisterClass<AIUsageDockExtension, Microsoft.CommandPalette.Extensions.IExtension>(() => extension);
        server.Start();
        disposed.WaitOne();
        server.Stop();
        server.UnsafeDispose();
        coordinator.SnapshotChanged -= activityMonitor.ObserveSnapshot;
    }
}
