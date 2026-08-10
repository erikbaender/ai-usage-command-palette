# Implementation-agent bootstrap

## Mission

Build a Windows PowerToys Command Palette extension named AI Usage Dock. It displays live remaining/used subscription limits for OpenAI Codex and Anthropic Claude in the Command Palette Dock.

The first release is specifically for subscription usage exposed through the installed, authenticated CLIs. It is not an OpenAI API billing dashboard and it must not scrape browser sessions.

## Non-negotiable decisions

1. Target PowerToys Command Palette Dock extensions, not a replacement launcher or an injected UI.
2. Codex integration uses a long-lived `codex app-server` process over stdio JSONL/JSON-RPC and calls `account/rateLimits/read`. Subscribe to `account/rateLimits/updated` when supported by the installed protocol.
3. Claude integration uses Claude Code's documented status-line JSON. A wrapper extracts `rate_limits`, writes a small local cache, then invokes the pre-existing status-line command with the original JSON and returns its output unchanged.
4. MVP stores no provider access token, browser cookie, or API key. The cache contains usage telemetry and timestamps only.
5. Missing, stale, unauthenticated, unsupported, or malformed provider data is represented explicitly and never shown as a confident zero.

## Suggested implementation order

1. Verify the current PowerToys Command Palette extension SDK and Dock API from the installed/referenced PowerToys version. Confirm `ICommandProvider3.GetDockBands()` and the supported live mutation pattern.
2. Scaffold the .NET extension and a testable provider/domain library. Keep process, file, and UI adapters behind interfaces.
3. Implement the normalized usage model, reset countdown calculation, freshness policy, and error states.
4. Implement Codex app-server lifecycle, initialize handshake, request correlation, rate-limit parsing, notification handling, timeout, restart, and shutdown.
5. Implement the Claude wrapper/bridge. Detect and preserve the existing status-line configuration; make installation opt-in and reversible.
6. Build Dock bands and expanded detail pages. Add icons, compact text, accessibility labels, and provider-specific freshness disclosure.
7. Add unit, contract, process, and manual integration tests. Document setup and known limitations.

## Open research before coding against an assumption

- Exact SDK package/version and target PowerToys release.
- Exact installed Codex app-server initialize/request schema and whether notifications are emitted for rate-limit changes.
- Exact Claude status-line configuration shape on Windows, including command quoting and whether a configured command can be wrapped without changing its semantics.
- Safe atomic cache location and ACL behavior under `%LOCALAPPDATA%`.
- Whether a per-user installed bridge can coexist with arbitrary existing status-line tools (PowerShell, cmd, Node, Python, `.exe`).

## Deliverables expected from the implementation agent

- Buildable extension and bridge projects.
- README setup guide with version matrix and troubleshooting.
- Provider adapters with fake fixtures for both protocols.
- No secret material in logs or test output.
- A manual validation checklist for Dock pinning, provider absence, stale data, resets, and existing Claude status-line preservation.
