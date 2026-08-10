using AIUsageDock.Providers;

namespace AIUsageDock.Bridge;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("AI Usage Dock Claude bridge");
                Console.Error.WriteLine("  --command-line <existing-command>  Cache Claude JSON and run the existing status line");
                Console.Error.WriteLine("  --install --bridge <path> [--replace]  Wrap ~/.claude/settings.json");
                Console.Error.WriteLine("  --restore  Restore the backed-up Claude settings");
                return args.Length == 0 ? 2 : 0;
            }

            if (args.Contains("--restore", StringComparer.OrdinalIgnoreCase))
            {
                var restored = await new ClaudeStatusLineInstaller().RestoreAsync(CancellationToken.None);
                Console.Error.WriteLine(restored ? "Claude status-line settings restored." : "No AI Usage Dock backup was found.");
                return restored ? 0 : 1;
            }

            if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
            {
                var bridgeIndex = Array.FindIndex(args, item => string.Equals(item, "--bridge", StringComparison.OrdinalIgnoreCase));
                if (bridgeIndex < 0 || bridgeIndex + 1 >= args.Length)
                {
                    Console.Error.WriteLine("--install requires --bridge <path>.");
                    return 2;
                }

                var result = await new ClaudeStatusLineInstaller().InstallAsync(
                    args[bridgeIndex + 1],
                    args.Contains("--replace", StringComparer.OrdinalIgnoreCase),
                    CancellationToken.None);
                Console.Error.WriteLine(result.Message);
                return result.Installed ? 0 : 1;
            }

            var commandIndex = Array.FindIndex(args, item => string.Equals(item, "--command-line", StringComparison.OrdinalIgnoreCase));
            if (commandIndex < 0 || commandIndex + 1 >= args.Length)
            {
                Console.Error.WriteLine("Missing --command-line <existing-command>.");
                return 2;
            }

            var bridge = new ClaudeStatusLineBridge(new ClaudeCacheStore());
            return await bridge.RunAsync(args[commandIndex + 1], Console.In, Console.Out, Console.Error, CancellationToken.None);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"AI Usage Dock bridge failed: {exception.Message}");
            return 1;
        }
    }
}
