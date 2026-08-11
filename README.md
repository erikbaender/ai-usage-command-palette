# AI Usage Dock

AI Usage Dock is a Windows PowerToys Command Palette extension that shows live, used subscription limits for OpenAI Codex and Anthropic Claude.

It includes:

- Four independently pinnable Dock bands for Codex/Claude Session and Weekly usage.
- Compact 5-hour/session and 7-day/weekly used percentages.
- Expanded details with remaining percentages, reset countdowns, plan/source, freshness, and actionable provider state.
- A long-lived codex app-server adapter using account/rateLimits/read.
- Active Claude usage polling through claude -p "/usage" --output-format json.
- One-second delay between Claude usage reads while a Claude session is running; the CLI execution time is added to that delay.
- Provider icons blink between full and half opacity on a one-second cycle while an external Codex or Claude session is running.

## MVP

- Reuse installed CLI authentication. The extension does not read browser cookies, provider token files, API keys, or undocumented web endpoints.
- Keep unknown, stale, unauthenticated, missing, and malformed states explicit; never display fabricated zeroes.

See docs/SETUP.md for build/install instructions and docs/MANUAL-VALIDATION.md for the manual test matrix.

## Projects

- src/AIUsageDock.Core — normalized model, parsing, freshness, and formatting.
- src/AIUsageDock.Providers — Codex app-server lifecycle and active Claude CLI provider.
- src/AIUsageDock.Extension — packaged WinRT/COM Command Palette extension and Dock UI.
- tests/AIUsageDock.Tests — provider parsers, CLI-process behavior, and usage formatting tests.

## Build and test

The tested build uses:

- Windows 11 / x64
- .NET SDK 9.0.316
- Command Palette Extension SDK 0.9.260303001
- Windows App SDK 2.2.0
- Shmuelie.WinRTServer 2.1.1

    dotnet restore src\AIUsageDock.Extension\AIUsageDock.Extension.csproj
    dotnet build src\AIUsageDock.Extension\AIUsageDock.Extension.csproj -c Debug -p:Platform=x64
    dotnet test tests\AIUsageDock.Tests\AIUsageDock.Tests.csproj -c Debug

The MSIX is written under src/AIUsageDock.Extension/AppPackages/.

## Claude setup

The extension does not modify Claude settings or install a helper executable. On each refresh it runs:

    claude -p "/usage" --output-format json --no-session-persistence

Ensure claude.exe is available on the PATH visible to PowerToys. If it is installed elsewhere, set AI_USAGE_CLAUDE_PATH to the executable path before starting PowerToys.
