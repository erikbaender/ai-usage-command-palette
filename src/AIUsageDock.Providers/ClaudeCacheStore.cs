using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using AIUsageDock.Core;

namespace AIUsageDock.Providers;

public sealed class ClaudeCacheStore
{
    public ClaudeCacheStore(string? cachePath = null)
    {
        CachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIUsage",
            "claude.json");
    }

    public string CachePath { get; }

    public async Task WriteAsync(ProviderSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Provider != ProviderId.Claude || snapshot.Windows.Count == 0)
        {
            throw new ArgumentException("Only a Claude snapshot with usage windows can be cached.", nameof(snapshot));
        }

        var directory = Path.GetDirectoryName(CachePath) ?? throw new InvalidOperationException("Cache path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{CachePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = UsageJson.CreateClaudeCache(snapshot);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                await writer.WriteAsync(json.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            ApplyUserOnlyAcl(temporaryPath);
            File.Move(temporaryPath, CachePath, true);
            ApplyUserOnlyAcl(CachePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<ProviderSnapshot> ReadAsync(DateTimeOffset now, TimeSpan staleAfter, CancellationToken cancellationToken)
    {
        if (!File.Exists(CachePath))
        {
            return ProviderSnapshot.Waiting(ProviderId.Claude, "Claude Code status line cache", "Waiting for first Claude Code response");
        }

        try
        {
            await using var stream = new FileStream(CachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > UsageJson.MaxPayloadBytes)
            {
                return new ProviderSnapshot(ProviderId.Claude, ProviderHealth.Error, Array.Empty<UsageWindowSnapshot>(), null, "Claude Code status line cache", "The cache file exceeds the safety limit.");
            }

            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var json = await reader.ReadToEndAsync(cancellationToken);
            return UsageJson.ParseClaudeCache(json, now, staleAfter);
        }
        catch (JsonException)
        {
            return new ProviderSnapshot(ProviderId.Claude, ProviderHealth.Error, Array.Empty<UsageWindowSnapshot>(), null, "Claude Code status line cache", "The Claude usage cache is malformed or corrupt.");
        }
        catch (IOException)
        {
            return new ProviderSnapshot(ProviderId.Claude, ProviderHealth.Error, Array.Empty<UsageWindowSnapshot>(), null, "Claude Code status line cache", "The Claude usage cache could not be read.");
        }
    }

    private static void ApplyUserOnlyAcl(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var identity = WindowsIdentity.GetCurrent().User;
            if (identity is null)
            {
                return;
            }

            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        catch (UnauthorizedAccessException)
        {
            // LocalAppData is already user-scoped; do not make the status line fail if ACL APIs are unavailable.
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}
