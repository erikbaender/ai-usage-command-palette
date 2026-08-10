using System.Text;
using AIUsageDock.Core;
using AIUsageDock.Providers;

namespace AIUsageDock.Tests;

public sealed class ClaudeBridgeTests
{
    [Fact]
    public async Task CachesUsageAndForwardsOriginalPayloadAndExitCode()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-usage-dock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var cache = new ClaudeCacheStore(Path.Combine(root, "claude.json"));
            var executor = new RecordingExecutor(17);
            var bridge = new ClaudeStatusLineBridge(cache, executor, new FixedClock(DateTimeOffset.Parse("2026-08-10T12:00:00Z")));
            var payload = """{"rate_limits":{"five_hour":{"used_percentage":12},"seven_day":{"used_percentage":34}}}""";
            using var input = new StringReader(payload);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await bridge.RunAsync("fake-status-line.exe --arg", input, output, error, CancellationToken.None);
            var cached = await cache.ReadAsync(DateTimeOffset.Parse("2026-08-10T12:01:00Z"), TimeSpan.FromMinutes(15), CancellationToken.None);

            Assert.Equal(17, exitCode);
            Assert.Equal(payload, executor.Input);
            Assert.Equal("visible status", output.ToString());
            Assert.Equal(12, cached.GetWindow(UsageWindow.Session)!.UsedPercent);
            Assert.Equal(34, cached.GetWindow(UsageWindow.Weekly)!.UsedPercent);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedInputStillRunsOriginalCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-usage-dock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executor = new RecordingExecutor(3);
            var bridge = new ClaudeStatusLineBridge(new ClaudeCacheStore(Path.Combine(root, "claude.json")), executor);
            var payload = "{malformed";

            var exitCode = await bridge.RunAsync("fake-status-line.exe", new StringReader(payload), new StringWriter(), new StringWriter(), CancellationToken.None);

            Assert.Equal(3, exitCode);
            Assert.Equal(payload, executor.Input);
            Assert.False(File.Exists(Path.Combine(root, "claude.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingExecutor : IStatusLineCommandExecutor
    {
        private readonly int _exitCode;

        public RecordingExecutor(int exitCode) => _exitCode = exitCode;

        public string? Input { get; private set; }

        public async Task<int> ExecuteAsync(string command, string input, TextWriter output, TextWriter error, CancellationToken cancellationToken)
        {
            Input = input;
            await output.WriteAsync("visible status");
            return _exitCode;
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
