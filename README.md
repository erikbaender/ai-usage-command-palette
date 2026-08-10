# AI Usage Dock

AI Usage Dock is a Windows PowerToys Command Palette extension that shows live, used subscription limits for OpenAI Codex and Anthropic Claude.

It includes:

- Two independently pinnable Dock bands for Codex and Claude.
- Compact 5-hour/session and 7-day/weekly used percentages.
- Expanded details with remaining percentages, reset countdowns, plan/source, freshness, and actionable provider state.
- A long-lived codex app-server adapter using account/rateLimits/read.
- A preserving Claude Code status-line bridge that caches only rate_limits telemetry and forwards the existing status line.

## MVP

- Reuse installed CLI authentication. The extension does not read browser cookies, provider token files, API keys, or undocumented web endpoints.
- Keep unknown, stale, unauthenticated, missing, and malformed states explicit; never display fabricated zeroes.

See docs/SETUP.md for build/install instructions and docs/MANUAL-VALIDATION.md for the manual test matrix.

## Projects

- src/AIUsageDock.Core — normalized model, parsing, freshness, and formatting.
- src/AIUsageDock.Providers — Codex app-server lifecycle, Claude cache, preserving bridge, and installer/restore flow.
- src/AIUsageDock.Bridge — standalone executable used by Claude Code's statusLine.command.
- src/AIUsageDock.Extension — packaged WinRT/COM Command Palette extension and Dock UI.
- tests/AIUsageDock.Tests — parser, bridge pass-through, cache, and installer contract tests.

## Build and test

The tested build uses:

- Windows 11 / x64
- .NET SDK 9.0.316
- Command Palette Extension SDK 0.9.260303001
- Windows App SDK 2.2.0
- Shmuelie.WinRTServer 2.1.1

    dotnet restore src\AIUsageDock.Extension\AIUsageDock.Extension.csproj
    dotnet build src\AIUsageDock.Extension\AIUsageDock.Extension.csproj -c Debug -p:Platform=x64
    dotnet restore src\AIUsageDock.Bridge\AIUsageDock.Bridge.csproj
    dotnet test tests\AIUsageDock.Tests\AIUsageDock.Tests.csproj -c Debug

The MSIX is written under src/AIUsageDock.Extension/AppPackages/.

## Claude setup

Claude Code's user settings are normally %USERPROFILE%\.claude\settings.json. Install the bridge only after reviewing the existing status-line command:

    dotnet run --project src\AIUsageDock.Bridge -- --install --bridge "C:\path\to\AIUsageDock.Bridge.exe"

If a status line already exists, the installer refuses to modify it unless explicitly requested:

    dotnet run --project src\AIUsageDock.Bridge -- --install --bridge "C:\path\to\AIUsageDock.Bridge.exe" --replace

Restore the original settings with:

    dotnet run --project src\AIUsageDock.Bridge -- --restore

The bridge writes usage-only data to %LOCALAPPDATA%\AIUsage\claude.json using atomic replacement and user-only ACLs where Windows permits them. Claude data remains visible after it becomes stale and is labelled as cached/stale.