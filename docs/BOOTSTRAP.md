# Implementation-agent bootstrap

## Mission

Build a Windows PowerToys Command Palette extension named AI Usage Dock. It displays live remaining/used subscription limits for OpenAI Codex and Anthropic Claude in the Command Palette Dock.

The first release is specifically for subscription usage exposed through the installed, authenticated CLIs. It is not an OpenAI API billing dashboard and it must not scrape browser sessions.

## Non-negotiable decisions

1. Target PowerToys Command Palette Dock extensions, not a replacement launcher or an injected UI.
2. Codex integration uses a long-lived `codex app-server` process over stdio JSONL/JSON-RPC and calls `account/rateLimits/read`. Subscribe to `account/rateLimits/updated` when supported by the installed protocol.
3. Claude integration invokes the installed, authenticated CLI with `claude -p "/usage" --output-format json --no-session-persistence` and parses the returned usage text.
4. MVP stores no provider access token, browser cookie, or API key. The cache contains usage telemetry and timestamps only.
5. Missing, stale, unauthenticated, unsupported, or malformed provider data is represented explicitly and never shown as a confident zero.

## Suggested implementation order

1. Verify the current PowerToys Command Palette extension SDK and Dock API from the installed/referenced PowerToys version. Confirm `ICommandProvider3.GetDockBands()` and the supported live mutation pattern.
2. Scaffold the .NET extension and a testable provider/domain library. Keep process, file, and UI adapters behind interfaces.
3. Implement the normalized usage model, reset countdown calculation, freshness policy, and error states.
4. Implement Codex app-server lifecycle, initialize handshake, request correlation, rate-limit parsing, notification handling, timeout, restart, and shutdown.
5. Implement the Claude CLI provider. Resolve the executable, capture UTF-8 output, parse session/weekly reset windows, and expose bounded diagnostics.
6. Build Dock bands and expanded detail pages. Add icons, compact text, accessibility labels, and provider-specific freshness disclosure.
7. Add unit, contract, process, and manual integration tests. Document setup and known limitations.

## Open research before coding against an assumption

- Exact SDK package/version and target PowerToys release.
- Exact installed Codex app-server initialize/request schema and whether notifications are emitted for rate-limit changes.
- Exact Claude CLI output variations on Windows, including time zones, encoding, and authentication failures.
- A package-local diagnostic log location that remains bounded and excludes credentials and session identifiers.

## Deliverables expected from the implementation agent

- Buildable extension project.
- README setup guide with version matrix and troubleshooting.
- Provider adapters with fake fixtures for both protocols.
- No secret material in logs or test output.
- A manual validation checklist for Dock pinning, provider absence, stale data, resets, and CLI failure handling.
