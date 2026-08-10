# Build, install, and troubleshoot

## Requirements

- Windows 11 x64.
- PowerToys with Command Palette and Dock support enabled.
- .NET SDK 9.
- Visual Studio's Windows App SDK/MSIX prerequisites for deployment, or the cached Windows SDK metadata package used by this repository's build.
- An authenticated Codex CLI for Codex values.
- Claude Code installed and authenticated, with claude.exe available on PATH.

The extension SDK is pinned to Microsoft.CommandPalette.Extensions 0.9.260303001, which is the SDK line used by the current Dock API in this repository.

## Build

    dotnet restore src\AIUsageDock.Extension\AIUsageDock.Extension.csproj
    dotnet build src\AIUsageDock.Extension\AIUsageDock.Extension.csproj -c Debug -p:Platform=x64

The resulting test MSIX is under:

    src/AIUsageDock.Extension/AppPackages/AIUsageDock.Extension_0.1.0.0_x64_Debug_Test/

Install that package using the normal Windows AppX deployment flow. After deployment, reload Command Palette and add the Codex and Claude bands from Dock edit mode.

## Recommended development loop

For iterative development, use Visual Studio rather than repeatedly installing the generated MSIX:

1. Open the repository/project in Visual Studio.
2. Select `Debug` and `x64`.
3. Use **Build > Deploy AIUsageDock.Extension**.
4. Open Command Palette and run **Reload** with the subtitle `Reload Command Palette Extension`.

The Visual Studio deployment step updates the packaged extension in place. Do not increment the manifest version, uninstall the package, or run `Add-AppDevPackage.ps1` for every code change. The certificate/developer-mode setup is a one-time machine setup; the Deploy/Reload cycle is the normal inner loop.

PowerToys should normally run unelevated. Administrator mode is only needed when PowerToys must interact with another application that is itself running elevated.

## Codex

The extension starts:

    codex app-server

Communication is redirected stdio JSON-RPC. The adapter initializes the process, calls account/rateLimits/read, accepts account/rateLimits/updated notifications, tolerates rateLimitsByLimitId, and retains last-known data while a refresh is failing.

The extension never reads Codex token files or asks the user for an API key.

## Claude

The extension actively runs claude -p "/usage" --output-format json --no-session-persistence on each refresh. It does not modify Claude settings or install a helper executable.

The CLI result is parsed into session and weekly windows, including the reset timezone. If a later CLI request fails, the provider retains the last successful values and marks them stale.

After installing the extension, reload Command Palette. No Claude settings changes or one-time setup command are required.

Ensure the PATH visible to PowerToys contains claude.exe. Set AI_USAGE_CLAUDE_PATH to the full executable path if necessary. Diagnostics are written to the package-local AIUsage\claude-cli.log file.

For arbitrary shell pipelines, keep the original command explicit through a script or powershell -NoProfile -File ...; direct executable-and-argument commands are launched without a shell.

## Troubleshooting

- No Codex band values: verify codex app-server starts and the CLI is authenticated. The Dock shows a missing/authentication/error state instead of zero.
- Claude shows unavailable: verify claude.exe is on PATH or set AI_USAGE_CLAUDE_PATH.
- Claude shows stale: the CLI refresh failed; run claude -p "/usage" manually to verify authentication and connectivity.
- MSIX symbol warning: missing mspdbcmf.exe only prevents generation of a symbols package; the MSIX still builds.
- Command Palette does not show the extension: install the MSIX, reload Command Palette, and verify the Dock is enabled.
