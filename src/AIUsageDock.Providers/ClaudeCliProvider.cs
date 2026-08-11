using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIUsageDock.Core;

namespace AIUsageDock.Providers;

public sealed class ClaudeCliClient
{
    private readonly string? _configuredExecutablePath;
    private readonly TimeSpan _requestTimeout;

    public string? LastExecutablePath { get; private set; }

    public ClaudeCliClient(string? configuredExecutablePath = null, TimeSpan? requestTimeout = null)
    {
        _configuredExecutablePath = configuredExecutablePath ?? Environment.GetEnvironmentVariable("AI_USAGE_CLAUDE_PATH");
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<string> ReadUsageAsync(CancellationToken cancellationToken)
    {
        var executable = ClaudeExecutableResolver.Resolve(_configuredExecutablePath);
        if (executable is null)
        {
            throw new FileNotFoundException("Claude CLI was not found on PATH.");
        }
        LastExecutablePath = executable;

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("/usage");
        startInfo.ArgumentList.Add("--output-format");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("--no-session-persistence");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Claude CLI could not be started.");
        }

        process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new ClaudeCliProcessException(process.ExitCode, error);
            }

            return output;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException("Claude CLI usage request timed out.");
        }
        finally
        {
            TryKill(process);
        }
    }

    private static void TryKill(Process process)
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

public sealed class ClaudeCliProcessException : InvalidOperationException
{
    public ClaudeCliProcessException(int exitCode, string? standardError)
        : base("Claude CLI returned a non-zero exit code.")
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }

    public int ExitCode { get; }

    public string? StandardError { get; }
}

internal static class ClaudeDiagnosticLog
{
    private static readonly object Gate = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIUsage",
        "claude-cli.log");

    public static void Write(string message, string? stdout = null, string? stderr = null)
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(directory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 128 * 1024)
                {
                    File.WriteAllText(LogPath, string.Empty);
                }

                var lines = new List<string>
                {
                    $"{DateTimeOffset.UtcNow:O} {message}",
                };
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    lines.Add($"stdout: {Sanitize(stdout)}");
                }
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    lines.Add($"stderr: {Sanitize(stderr)}");
                }

                File.AppendAllLines(LogPath, lines);
            }
        }
        catch
        {
            // Diagnostics must never affect usage refresh.
        }
    }

    private static string Sanitize(string value)
    {
        var singleLine = Regex.Replace(value, @"\s+", " ").Trim();
        singleLine = Regex.Replace(singleLine, @"(?i)(authorization|token|api[_-]?key|cookie|session[_-]?id|uuid)\s*[:=]\s*\S+", "$1=<redacted>");
        return singleLine.Length > 2000 ? singleLine[..2000] : singleLine;
    }
}

public static class ClaudeExecutableResolver
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
            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "claude.exe" : "claude");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userLocalClaude = Path.Combine(userProfile, ".local", "bin", OperatingSystem.IsWindows() ? "claude.exe" : "claude");
        if (File.Exists(userLocalClaude))
        {
            return userLocalClaude;
        }

        return null;
    }
}

public sealed class ClaudeProvider : IUsageProvider
{
    private readonly ClaudeCliClient _client;
    private readonly IClock _clock;
    private ProviderSnapshot? _lastSnapshot;

    public ClaudeProvider(ClaudeCliClient? client = null, IClock? clock = null)
    {
        _client = client ?? new ClaudeCliClient();
        _clock = clock ?? new SystemClock();
    }

    public ProviderId Id => ProviderId.Claude;

    public event EventHandler<ProviderSnapshot>? SnapshotChanged;

    public async Task<ProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        string? json = null;
        try
        {
            json = await _client.ReadUsageAsync(cancellationToken);
            var snapshot = ClaudeUsageSnapshotReconciler.Reconcile(
                _lastSnapshot,
                UsageJson.ParseClaudeCliUsage(json, _clock.UtcNow));
            _lastSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
            return snapshot;
        }
        catch (FileNotFoundException)
        {
            ClaudeDiagnosticLog.Write($"executable-not-found configured={Environment.GetEnvironmentVariable("AI_USAGE_CLAUDE_PATH") ?? "<none>"}");
            return Publish(new ProviderSnapshot(Id, ProviderHealth.Unavailable, Array.Empty<UsageWindowSnapshot>(), _lastSnapshot?.LastUpdated, "Claude CLI · claude -p /usage", "Claude CLI not found on PATH."));
        }
        catch (UnauthorizedAccessException)
        {
            ClaudeDiagnosticLog.Write($"access-denied executable={_client.LastExecutablePath ?? "<unknown>"}");
            return Publish(new ProviderSnapshot(Id, ProviderHealth.Unauthenticated, Array.Empty<UsageWindowSnapshot>(), _lastSnapshot?.LastUpdated, "Claude CLI · claude -p /usage", "Claude CLI could not be executed for the current user."));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ClaudeDiagnosticLog.Write($"timeout executable={_client.LastExecutablePath ?? "<unknown>"}");
            return PublishFailure("Claude CLI usage request timed out.");
        }
        catch (JsonException exception)
        {
            ClaudeDiagnosticLog.Write($"parse-failure executable={_client.LastExecutablePath ?? "<unknown>"} error={exception.Message}", json);
            return PublishFailure($"{exception.Message} Diagnostics: {ClaudeDiagnosticLog.LogPath}");
        }
        catch (ClaudeCliProcessException exception)
        {
            ClaudeDiagnosticLog.Write($"process-failure executable={_client.LastExecutablePath ?? "<unknown>"} exitCode={exception.ExitCode}", stderr: exception.StandardError);
            return PublishFailure($"Claude CLI returned exit code {exception.ExitCode}. Diagnostics: {ClaudeDiagnosticLog.LogPath}");
        }
        catch (InvalidOperationException)
        {
            ClaudeDiagnosticLog.Write($"process-failure executable={_client.LastExecutablePath ?? "<unknown>"}");
            return PublishFailure("Claude CLI returned a non-success result.");
        }
        catch (Win32Exception)
        {
            ClaudeDiagnosticLog.Write($"win32-start-failure executable={_client.LastExecutablePath ?? "<unknown>"}");
            return PublishFailure("Claude CLI could not be started by Command Palette.");
        }
        catch (Exception) when (_lastSnapshot is not null)
        {
            ClaudeDiagnosticLog.Write($"refresh-failure executable={_client.LastExecutablePath ?? "<unknown>"}");
            return Publish(_lastSnapshot with { Health = ProviderHealth.Stale, Message = "Claude CLI refresh failed; showing the last known values." });
        }
        catch (Exception)
        {
            ClaudeDiagnosticLog.Write($"unexpected-failure executable={_client.LastExecutablePath ?? "<unknown>"}");
            return Publish(new ProviderSnapshot(Id, ProviderHealth.Error, Array.Empty<UsageWindowSnapshot>(), null, "Claude CLI · claude -p /usage", "Claude CLI did not return usable usage data."));
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ProviderSnapshot PublishFailure(string message)
    {
        if (_lastSnapshot is not null)
        {
            return Publish(_lastSnapshot with { Health = ProviderHealth.Stale, Message = message });
        }

        return Publish(new ProviderSnapshot(Id, ProviderHealth.Error, Array.Empty<UsageWindowSnapshot>(), null, "Claude CLI · claude -p /usage", message));
    }

    private ProviderSnapshot Publish(ProviderSnapshot snapshot)
    {
        SnapshotChanged?.Invoke(this, snapshot);
        return snapshot;
    }
}

public static class ClaudeUsageSnapshotReconciler
{
    public static ProviderSnapshot Reconcile(ProviderSnapshot? previous, ProviderSnapshot current)
    {
        if (previous?.Provider != ProviderId.Claude || current.Provider != ProviderId.Claude)
        {
            return current;
        }

        var windows = current.Windows.Select(window => ReconcileWindow(previous, window)).ToArray();
        return current with { Windows = windows };
    }

    private static UsageWindowSnapshot ReconcileWindow(
        ProviderSnapshot previousSnapshot,
        UsageWindowSnapshot current)
    {
        var previous = previousSnapshot.GetWindow(current.Window);
        if (previous?.UsedPercent is not double previousUsed ||
            current.UsedPercent is not double currentUsed ||
            currentUsed >= previousUsed)
        {
            return current;
        }

        // Claude can briefly return an empty/new-session value while usage is
        // still being aggregated. A decrease is valid only after the reset
        // boundary of the prior window has actually passed.
        if (previous.ResetsAt is DateTimeOffset previousReset &&
            previousReset <= current.ObservedAt)
        {
            return current;
        }

        return previous;
    }
}
