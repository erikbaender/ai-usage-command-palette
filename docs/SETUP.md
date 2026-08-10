# Build, install, and troubleshoot

## Requirements

- Windows 11 x64.
- PowerToys with Command Palette and Dock support enabled.
- .NET SDK 9.
- Visual Studio's Windows App SDK/MSIX prerequisites for deployment, or the cached Windows SDK metadata package used by this repository's build.
- An authenticated Codex CLI for Codex values.
- Claude Code with a configured statusLine command for Claude values.

The extension SDK is pinned to Microsoft.CommandPalette.Extensions 0.9.260303001, which is the SDK line used by the current Dock API in this repository.

## Build

    dotnet restore src\AIUsageDock.Extension\AIUsageDock.Extension.csproj
    dotnet build src\AIUsageDock.Extension\AIUsageDock.Extension.csproj -c Debug -p:Platform=x64

The resulting test MSIX is under:

    src/AIUsageDock.Extension/AppPackages/AIUsageDock.Extension_0.1.0.0_x64_Debug_Test/

Install that package using the normal Windows AppX deployment flow. After deployment, reload Command Palette and add the Codex and Claude bands from Dock edit mode.

## Codex

The extension starts:

    codex app-server

Communication is redirected stdio JSON-RPC. The adapter initializes the process, calls account/rateLimits/read, accepts account/rateLimits/updated notifications, tolerates rateLimitsByLimitId, and retains last-known data while a refresh is failing.

The extension never reads Codex token files or asks the user for an API key.

## Claude

Claude Code passes the complete status-line JSON through stdin. The bridge:

1. Reads and size-limits the payload.
2. Extracts only rate_limits.five_hour and rate_limits.seven_day.
3. Atomically writes %LOCALAPPDATA%\AIUsage\claude.json.
4. Runs the user's original command with the exact original JSON on stdin.
5. Forwards stdout/stderr and returns the original exit code.

The installer backs up the original settings file to settings.json.ai-usage-dock.backup. It changes only statusLine.type and statusLine.command. If an existing status line is present, --replace is required. --restore restores the backup byte-for-byte.

For arbitrary shell pipelines, keep the original command explicit through a script or powershell -NoProfile -File ...; direct executable-and-argument commands are launched without a shell.

## Troubleshooting

- No Codex band values: verify codex app-server starts and the CLI is authenticated. The Dock shows a missing/authentication/error state instead of zero.
- Claude shows waiting: invoke Claude Code after installing the bridge; rate_limits is only present for supported Claude.ai subscriber sessions after a response.
- Claude shows stale: the cache is older than the 15-minute default threshold. Run Claude Code again; the old values remain visible to avoid a misleading empty state.
- MSIX symbol warning: missing mspdbcmf.exe only prevents generation of a symbols package; the MSIX still builds.
- Command Palette does not show the extension: install the MSIX, reload Command Palette, and verify the Dock is enabled.