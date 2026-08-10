using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using AIUsageDock.Core;

namespace AIUsageDock.Providers;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly string? _configuredExecutablePath;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonDocument>> _pending = new(StringComparer.Ordinal);
    private Process? _process;
    private StreamWriter? _writer;
    private Task? _readerTask;
    private long _nextRequestId;
    private bool _disposed;

    public CodexAppServerClient(string? configuredExecutablePath = null, TimeSpan? requestTimeout = null)
    {
        _configuredExecutablePath = configuredExecutablePath ?? Environment.GetEnvironmentVariable("AI_USAGE_CODEX_PATH");
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
    }

    public event EventHandler<JsonElement>? RateLimitsUpdated;

    public async Task<JsonDocument> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);
        return await SendRequestAsync("account/rateLimits/read", new { }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();
        _lifecycle.Dispose();
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_process is { HasExited: false })
            {
                return;
            }

            await StopAsync();
            var executable = CodexExecutableResolver.Resolve(_configuredExecutablePath);
            if (executable is null)
            {
                throw new FileNotFoundException("Codex CLI was not found on PATH.");
            }

            var info = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("app-server");
            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("Codex app-server could not be started.");
            }

            _process = process;
            _writer = process.StandardInput;
            _readerTask = ReadLoopAsync(process.StandardOutput, process);
            _ = DrainErrorsAsync(process.StandardError);
            using var initialize = await SendRequestAsync("initialize", new
            {
                clientInfo = new { name = "ai-usage-dock", version = "0.1.0" },
                capabilities = new { },
            }, cancellationToken);
            await SendNotificationAsync("initialized", new { }, cancellationToken);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task<JsonDocument> SendRequestAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var writer = _writer ?? throw new InvalidOperationException("Codex app-server is not running.");
        var id = Interlocked.Increment(ref _nextRequestId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });
        try
        {
            await writer.WriteLineAsync(request.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);
            return await completion.Task.WaitAsync(timeout.Token);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
    }

    private async Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var writer = _writer ?? throw new InvalidOperationException("Codex app-server is not running.");
        var notification = JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters });
        await writer.WriteLineAsync(notification.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private async Task ReadLoopAsync(StreamReader reader, Process process)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.Length > UsageJson.MaxPayloadBytes)
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement))
                {
                    var id = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : idElement.GetRawText();
                    if (id is not null && _pending.TryRemove(id, out var completion))
                    {
                        if (root.TryGetProperty("error", out var error))
                        {
                            completion.TrySetException(new InvalidOperationException(ReadProtocolError(error)));
                        }
                        else
                        {
                            completion.TrySetResult(JsonDocument.Parse(root.GetRawText()));
                        }
                    }
                }
                else if (root.TryGetProperty("method", out var method) && method.GetString() == "account/rateLimits/updated" && root.TryGetProperty("params", out var parameters))
                {
                    RateLimitsUpdated?.Invoke(this, parameters.Clone());
                }
            }
        }
        catch (Exception exception)
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(exception);
            }
            _pending.Clear();
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static async Task DrainErrorsAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is not null)
        {
        }
    }

    private async Task StopAsync()
    {
        var process = _process;
        _process = null;
        _writer = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string ReadProtocolError(JsonElement error) =>
        error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() ?? "Codex protocol error" : "Codex protocol error";
}

public sealed class CodexProvider : IUsageProvider
{
    private readonly CodexAppServerClient _client;
    private readonly IClock _clock;
    private ProviderSnapshot? _lastSnapshot;

    public CodexProvider(CodexAppServerClient? client = null, IClock? clock = null)
    {
        _client = client ?? new CodexAppServerClient();
        _clock = clock ?? new SystemClock();
        _client.RateLimitsUpdated += OnRateLimitsUpdated;
    }

    public ProviderId Id => ProviderId.Codex;

    public event EventHandler<ProviderSnapshot>? SnapshotChanged;

    public async Task<ProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.ReadRateLimitsAsync(cancellationToken);
            var snapshot = UsageJson.ParseCodexRateLimits(response.RootElement, _clock.UtcNow);
            _lastSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
            return snapshot;
        }
        catch (FileNotFoundException)
        {
            return Publish(new ProviderSnapshot(Id, ProviderHealth.Unavailable, Array.Empty<UsageWindowSnapshot>(), _lastSnapshot?.LastUpdated, "codex app-server", "Codex CLI not found on PATH."));
        }
        catch (UnauthorizedAccessException)
        {
            return Publish(new ProviderSnapshot(Id, ProviderHealth.Unauthenticated, Array.Empty<UsageWindowSnapshot>(), _lastSnapshot?.LastUpdated, "codex app-server", "Codex CLI is not authenticated. Run the Codex CLI sign-in flow."));
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            if (_lastSnapshot is not null)
            {
                return Publish(_lastSnapshot with { Health = ProviderHealth.Stale, Message = "Codex refresh failed; showing the last known values." });
            }

            return Publish(new ProviderSnapshot(Id, ProviderHealth.Error, Array.Empty<UsageWindowSnapshot>(), null, "codex app-server", "Codex app-server did not return usable rate-limit data."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _client.RateLimitsUpdated -= OnRateLimitsUpdated;
        await _client.DisposeAsync();
    }

    private void OnRateLimitsUpdated(object? sender, JsonElement parameters)
    {
        try
        {
            var snapshot = UsageJson.ParseCodexRateLimits(parameters, _clock.UtcNow);
            _lastSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
        catch (JsonException)
        {
        }
    }

    private ProviderSnapshot Publish(ProviderSnapshot snapshot)
    {
        SnapshotChanged?.Invoke(this, snapshot);
        return snapshot;
    }
}

public sealed class ClaudeProvider : IUsageProvider
{
    private readonly ClaudeCacheStore _cacheStore;
    private readonly IClock _clock;
    private readonly FreshnessPolicy _freshness;

    public ClaudeProvider(ClaudeCacheStore? cacheStore = null, IClock? clock = null, FreshnessPolicy? freshness = null)
    {
        _cacheStore = cacheStore ?? new ClaudeCacheStore();
        _clock = clock ?? new SystemClock();
        _freshness = freshness ?? FreshnessPolicy.Default;
    }

    public ProviderId Id => ProviderId.Claude;

    public event EventHandler<ProviderSnapshot>? SnapshotChanged;

    public async Task<ProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _cacheStore.ReadAsync(_clock.UtcNow, _freshness.ClaudeStaleAfter, cancellationToken);
        SnapshotChanged?.Invoke(this, snapshot);
        return snapshot;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public static class CodexExecutableResolver
{
    public static string? Resolve(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return File.Exists(configuredPath) ? configuredPath : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in OperatingSystem.IsWindows() ? new[] { "codex.exe", "codex.cmd", "codex" } : new[] { "codex" })
            {
                var fullPath = Path.Combine(directory, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }
}
