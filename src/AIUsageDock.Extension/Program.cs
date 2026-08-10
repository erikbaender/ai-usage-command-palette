using Microsoft.Windows.AppLifecycle;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace AIUsageDock.Extension;

public static class Program
{
    [MTAThread]
    public static async Task Main(string[] args)
    {
        if (args.Length == 0 || !args.Contains("-RegisterProcessAsComServer", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        await RunComServerAsync();
    }

    private static async Task RunComServerAsync()
    {
        await using var server = new ComServer();
        using var disposed = new ManualResetEvent(false);
        await using var coordinator = UsageCoordinator.CreateDefault();
        coordinator.Start();

        using var provider = new AIUsageDockCommandsProvider(coordinator);
        var extension = new AIUsageDockExtension(disposed, provider);
        server.RegisterClass<AIUsageDockExtension, Microsoft.CommandPalette.Extensions.IExtension>(() => extension);
        server.Start();
        disposed.WaitOne();
        server.Stop();
        server.UnsafeDispose();
    }
}
