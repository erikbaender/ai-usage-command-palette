using System.Net;
using System.Text.Json;
using AIUsageDock.Core;
using AIUsageDock.Providers;

namespace AIUsageDock.Tests;

public sealed class BackendRoutingTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-08-12T10:00:00Z");

    [Fact]
    public async Task ClaudeWebFirstFallsBackToCliOnlyForAuthenticationFailure()
    {
        var cli = new FakeClaudeCliClient();
        using var web = new FakeClaudeWebClient(new ClaudeWebUsageException(HttpStatusCode.Unauthorized));
        await using var provider = new ClaudeProvider(cli, new FixedClock(), web, new UsageBackendPreferences());

        var snapshot = await provider.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(1, cli.ReadCount);
        Assert.StartsWith("Claude CLI", snapshot.Source);
        Assert.Contains("web session needs reconnecting", snapshot.Message);
    }

    [Fact]
    public async Task ClaudeWebFirstDoesNotMixInCliDataAfterTransientWebFailure()
    {
        var cli = new FakeClaudeCliClient();
        using var web = new FakeClaudeWebClient(new InvalidOperationException("temporary failure"));
        await using var provider = new ClaudeProvider(cli, new FixedClock(), web, new UsageBackendPreferences());

        var snapshot = await provider.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(0, cli.ReadCount);
        Assert.Equal(ProviderHealth.Error, snapshot.Health);
        Assert.Equal("Claude web usage", snapshot.Source);
    }

    [Fact]
    public async Task CodexCliPreferenceBypassesWebBackend()
    {
        var preferences = new UsageBackendPreferences();
        preferences.Set(ProviderId.Codex, UsageBackendPreference.Cli);
        await using var cli = new FakeCodexAppServerClient();
        using var web = new FakeCodexWebClient();
        await using var provider = new CodexProvider(cli, new FixedClock(), web, preferences);

        var snapshot = await provider.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(0, web.ReadCount);
        Assert.Equal(1, cli.ReadCount);
        Assert.StartsWith("codex app-server", snapshot.Source);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => ObservedAt;
    }

    private sealed class FakeClaudeCliClient : IClaudeCliClient
    {
        public int ReadCount { get; private set; }

        public string? LastExecutablePath => "claude";

        public Task<string> ReadUsageAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult("""{"result":"Current session: 12% used\nCurrent week (all models): 34% used"}""");
        }
    }

    private sealed class FakeClaudeWebClient(Exception exception) : IClaudeWebUsageClient
    {
        public bool IsConfigured => true;

        public Task<string> ReadUsageAsync(CancellationToken cancellationToken) => Task.FromException<string>(exception);

        public void Dispose()
        {
        }
    }

    private sealed class FakeCodexWebClient : ICodexWebUsageClient
    {
        public int ReadCount { get; private set; }

        public bool IsConfigured => true;

        public Task<string> ReadUsageAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult("""{"rate_limit":{"primary_window":{"used_percent":1},"secondary_window":{"used_percent":2}}}""");
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeCodexAppServerClient : ICodexAppServerClient
    {
        public int ReadCount { get; private set; }

        public event EventHandler<JsonElement>? RateLimitsUpdated
        {
            add { }
            remove { }
        }

        public Task<JsonDocument> ReadRateLimitsAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(JsonDocument.Parse(
                """{"result":{"rateLimits":{"primary":{"usedPercent":11,"windowDurationMins":300},"secondary":{"usedPercent":22,"windowDurationMins":10080}}}}"""));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
