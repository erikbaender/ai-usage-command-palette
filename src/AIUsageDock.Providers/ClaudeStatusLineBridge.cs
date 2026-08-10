using System.Diagnostics;
using System.Text;
using AIUsageDock.Core;

namespace AIUsageDock.Providers;

public sealed class ClaudeStatusLineBridge
{
    public const int MaxInputBytes = UsageJson.MaxPayloadBytes;

    private readonly ClaudeCacheStore _cacheStore;
    private readonly IStatusLineCommandExecutor _executor;
    private readonly IClock _clock;

    public ClaudeStatusLineBridge(ClaudeCacheStore cacheStore, IStatusLineCommandExecutor? executor = null, IClock? clock = null)
    {
        _cacheStore = cacheStore;
        _executor = executor ?? new ProcessStatusLineCommandExecutor();
        _clock = clock ?? new SystemClock();
    }

    public async Task<int> RunAsync(string originalCommand, TextReader input, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalCommand);
        var payload = await ReadLimitedAsync(input, cancellationToken);

        // Caching is intentionally fail-open. A provider/cache error must never hide the user's status line.
        try
        {
            var snapshot = UsageJson.ParseClaudeStatusLine(payload, _clock.UtcNow);
            if (snapshot.Windows.Count > 0)
            {
                await _cacheStore.WriteAsync(snapshot, cancellationToken);
            }
        }
        catch (Exception)
        {
            // Never write provider payloads, commands, or exception details to diagnostics.
        }

        return await _executor.ExecuteAsync(originalCommand, payload, output, error, cancellationToken);
    }

    private static async Task<string> ReadLimitedAsync(TextReader input, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        var byteCount = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            byteCount += Encoding.UTF8.GetByteCount(buffer, 0, read);
            if (byteCount > MaxInputBytes)
            {
                throw new InvalidDataException("Status-line payload exceeds the safety limit.");
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }
}

public interface IStatusLineCommandExecutor
{
    Task<int> ExecuteAsync(string command, string input, TextWriter output, TextWriter error, CancellationToken cancellationToken);
}

public sealed class ProcessStatusLineCommandExecutor : IStatusLineCommandExecutor
{
    public async Task<int> ExecuteAsync(string command, string input, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(command);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Could not start the configured Claude status-line command.");
            }

            await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();

            var stdoutTask = ForwardAsync(process.StandardOutput, output, cancellationToken);
            var stderrTask = ForwardAsync(process.StandardError, error, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string command)
    {
        if (NeedsShell(command))
        {
            var shell = FindShell();
            var shellInfo = CreateRedirectedStartInfo(shell.FileName);
            foreach (var argument in shell.Arguments)
            {
                shellInfo.ArgumentList.Add(argument);
            }

            shellInfo.ArgumentList.Add(command);
            return shellInfo;
        }

        var arguments = WindowsCommandLine.Parse(command);
        if (arguments.Count == 0)
        {
            throw new InvalidOperationException("The configured Claude status-line command is empty.");
        }

        var info = CreateRedirectedStartInfo(arguments[0]);
        foreach (var argument in arguments.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private static ProcessStartInfo CreateRedirectedStartInfo(string fileName)
    {
        return new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
    }

    private static bool NeedsShell(string command) =>
        command.IndexOfAny(['|', '&', '<', '>', '\r', '\n']) >= 0 ||
        command.StartsWith('~') ||
        command.EndsWith(".sh", StringComparison.OrdinalIgnoreCase);

    private static (string FileName, IReadOnlyList<string> Arguments) FindShell()
    {
        var gitBash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        if (File.Exists(gitBash))
        {
            return (gitBash, ["--noprofile", "--norc", "-lc"]);
        }

        return ("cmd.exe", ["/d", "/s", "/c"]);
    }
    private static async Task ForwardAsync(StreamReader reader, TextWriter writer, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            await writer.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            await writer.FlushAsync(cancellationToken);
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

internal static class WindowsCommandLine
{
    public static IReadOnlyList<string> Parse(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return ParseWindows(command);
        }

        return command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyList<string> ParseWindows(string command)
    {
        var pointer = CommandLineToArgvW(command, out var count);
        if (pointer == IntPtr.Zero)
        {
            return Array.Empty<string>();
        }

        try
        {
            var result = new string[count];
            for (var index = 0; index < count; index++)
            {
                var item = System.Runtime.InteropServices.Marshal.ReadIntPtr(pointer, index * IntPtr.Size);
                result[index] = System.Runtime.InteropServices.Marshal.PtrToStringUni(item) ?? string.Empty;
            }

            return result;
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argc);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
