using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIUsageDock.Core;

namespace AIUsageDock.Providers;

public sealed class ClaudeStatusLineInstaller
{
    public ClaudeStatusLineInstaller(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            "settings.json");
    }

    public string SettingsPath { get; }

    public string BackupPath => SettingsPath + ".ai-usage-dock.backup";

    public async Task<ClaudeInstallResult> InstallAsync(string bridgeExecutablePath, bool replaceExisting, CancellationToken cancellationToken)
    {
        var originalBytes = File.Exists(SettingsPath) ? await File.ReadAllBytesAsync(SettingsPath, cancellationToken) : Encoding.UTF8.GetBytes("{}");
        if (originalBytes.Length > UsageJson.MaxPayloadBytes)
        {
            throw new InvalidDataException("Claude settings file exceeds the safety limit.");
        }

        var root = JsonNode.Parse(originalBytes)?.AsObject() ?? throw new JsonException("Claude settings must be a JSON object.");
        var existingCommand = root["statusLine"]?["command"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(existingCommand) && !replaceExisting)
        {
            return new ClaudeInstallResult(false, false, existingCommand, "An existing status-line command was found. Re-run with explicit replacement enabled to preserve and wrap it.");
        }

        var backupCreated = false;
        if (File.Exists(SettingsPath) && !File.Exists(BackupPath))
        {
            await File.WriteAllBytesAsync(BackupPath, originalBytes, cancellationToken);
            backupCreated = true;
        }

        var statusLine = root["statusLine"]?.AsObject() ?? new JsonObject();
        statusLine["type"] = "command";
        statusLine["command"] = BuildBridgeCommand(bridgeExecutablePath, existingCommand ?? string.Empty);
        root["statusLine"] = statusLine;
        await AtomicWriteAsync(SettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        var message = string.IsNullOrWhiteSpace(existingCommand)
            ? "Claude usage status line installed. Claude Code will now show a built-in usage line."
            : "Claude status-line wrapper installed; the original command is preserved in the wrapper arguments and backup file.";
        return new ClaudeInstallResult(true, backupCreated, existingCommand, message);
    }

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(BackupPath))
        {
            return false;
        }

        var backup = await File.ReadAllBytesAsync(BackupPath, cancellationToken);
        await AtomicWriteBytesAsync(SettingsPath, backup, cancellationToken);
        return true;
    }

    public static string BuildBridgeCommand(string bridgeExecutablePath, string? originalCommand) =>
        string.IsNullOrWhiteSpace(originalCommand)
            ? QuoteArgument(bridgeExecutablePath)
            : $"{QuoteArgument(bridgeExecutablePath)} --command-line {QuoteArgument(originalCommand)}";

    private static string QuoteArgument(string value)
    {
        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            builder.Append(character);
            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }
    private static Task AtomicWriteAsync(string path, string contents, CancellationToken cancellationToken) =>
        AtomicWriteBytesAsync(path, Encoding.UTF8.GetBytes(contents), cancellationToken);

    private static async Task AtomicWriteBytesAsync(string path, byte[] contents, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Settings path has no directory.");
        Directory.CreateDirectory(directory);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, contents, cancellationToken);
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}

public sealed record ClaudeInstallResult(bool Installed, bool BackupCreated, string? OriginalCommand, string Message);
