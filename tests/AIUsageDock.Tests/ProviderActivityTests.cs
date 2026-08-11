using AIUsageDock.Core;

namespace AIUsageDock.Tests;

public sealed class ProviderActivityTests
{
    [Fact]
    public void DetectsExternalProviderProcess()
    {
        ProcessDescriptor[] processes =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(101, 100, "codex.exe"),
            new(200, 1, "CODEX.EXE"),
        ];

        Assert.True(ProviderActivityDetection.IsActive(ProviderId.Codex, processes, 100));
        Assert.False(ProviderActivityDetection.IsActive(ProviderId.Claude, processes, 100));
    }

    [Fact]
    public void IgnoresMonitorProcessAndAllDescendants()
    {
        ProcessDescriptor[] processes =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(101, 100, "launcher.exe"),
            new(102, 101, "claude.exe"),
            new(103, 100, "codex.exe"),
        ];

        Assert.False(ProviderActivityDetection.IsActive(ProviderId.Codex, processes, 100));
        Assert.False(ProviderActivityDetection.IsActive(ProviderId.Claude, processes, 100));
    }

    [Fact]
    public void MatchesExecutableNameWithoutExtension()
    {
        ProcessDescriptor[] processes =
        [
            new(100, 1, "AIUsageDock.Extension.exe"),
            new(200, 1, "claude"),
        ];

        Assert.True(ProviderActivityDetection.IsActive(ProviderId.Claude, processes, 100));
    }
}
