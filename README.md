# AI Usage Dock

AI Usage Dock is a Windows PowerToys Command Palette extension that shows live, used subscription limits for OpenAI Codex and Anthropic Claude.

It includes:

- Four independently pinnable Dock bands for Codex/Claude Session and Weekly usage.
- Compact 5-hour/session and 7-day/weekly used percentages.
- Expanded details with remaining percentages, reset countdowns, plan/source, freshness, and actionable provider state.
- Fresh Codex and Claude web-usage polling through independent persistent embedded browser sessions.
- Per-provider backend settings: Web-first (default) or CLI.
- Web-first falls back to the corresponding CLI only when the browser session is not authenticated; transient web failures keep the last web snapshot instead of mixing in delayed CLI data.
- One-second delay between usage reads while a Claude session is running.
- Provider icons blink between full and half opacity on a one-second cycle while recent Codex or Claude process activity indicates usage is being consumed.

## MVP

- Browser authentication remains inside dedicated WebView2 profiles. The extension does not export cookie values, read provider token files, or ask for API keys.
- Keep unknown, stale, unauthenticated, missing, and malformed states explicit; never display fabricated zeroes.

See docs/SETUP.md for build/install instructions and docs/MANUAL-VALIDATION.md for the manual test matrix.

## Projects

- src/AIUsageDock.Core — normalized model, parsing, freshness, and formatting.
- src/AIUsageDock.Providers — web/CLI routing, Codex app-server lifecycle, provider parsers, and Claude CLI fallback.
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

## Web setup

Run **Connect Codex web usage** and **Connect Claude web usage** from Command Palette, then sign in inside each dedicated window. After a successful usage response, the window closes automatically and Windows shows a confirmation notification. The packaged app keeps each WebView profile in its user-local data so the providers can rotate their own session cookies. Cookie values are never exported into extension settings, environment variables, or diagnostic logs.

Web-first is the default for each provider. Choose **CLI** independently under the Codex or Claude usage-backend setting when CLI data is preferred. When a Web-first session is signed out or expired, the extension can fall back to:

    codex app-server
    claude -p "/usage" --output-format json --no-session-persistence

Neither CLI is required while its web session is authenticated. For CLI use, ensure the executable is on the PATH visible to PowerToys; AI_USAGE_CODEX_PATH and AI_USAGE_CLAUDE_PATH can point to explicit executable paths.
