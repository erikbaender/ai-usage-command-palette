using System.Text.Json;
using AIUsageDock.Providers;

namespace AIUsageDock.Tests;

public sealed class ClaudeInstallerTests
{
    [Fact]
    public async Task RequiresExplicitReplacementAndRestoresBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-usage-dock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");
        var original = """{"theme":"dark","statusLine":{"type":"command","command":"node ~/.claude/statusline.js","padding":2}}""";
        await File.WriteAllTextAsync(settingsPath, original);
        try
        {
            var installer = new ClaudeStatusLineInstaller(settingsPath);
            var refused = await installer.InstallAsync("C:\\AI Usage Dock\\AIUsageDock.Bridge.exe", false, CancellationToken.None);
            Assert.False(refused.Installed);
            Assert.Equal(original, await File.ReadAllTextAsync(settingsPath));

            var installed = await installer.InstallAsync("C:\\AI Usage Dock\\AIUsageDock.Bridge.exe", true, CancellationToken.None);
            Assert.True(installed.Installed);
            Assert.True(File.Exists(installer.BackupPath));
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
            Assert.Contains("--command-line", document.RootElement.GetProperty("statusLine").GetProperty("command").GetString());

            Assert.True(await installer.RestoreAsync(CancellationToken.None));
            Assert.Equal(original, await File.ReadAllTextAsync(settingsPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallsBuiltInStatusLineWhenClaudeHasNoExistingCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-usage-dock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{}");
        try
        {
            var installer = new ClaudeStatusLineInstaller(settingsPath);
            var result = await installer.InstallAsync("C:\\AI Usage Dock\\AIUsageDock.Bridge.exe", true, CancellationToken.None);

            Assert.True(result.Installed);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
            Assert.Equal("command", document.RootElement.GetProperty("statusLine").GetProperty("type").GetString());
            Assert.Equal("\"C:\\AI Usage Dock\\AIUsageDock.Bridge.exe\"", document.RootElement.GetProperty("statusLine").GetProperty("command").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
