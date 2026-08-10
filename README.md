# AI Usage Dock

PowerToys Command Palette extension concept for live Codex and Claude subscription usage.

This repository is intentionally bootstrap-first. The initial pull request is an implementation brief for an agent, not a production implementation.

## MVP

- Show separate Codex and Claude Dock bands in PowerToys Command Palette.
- Show used percentages for the rolling five-hour/session window and seven-day/weekly window.
- Show reset countdowns and last-updated/source details in the expanded view.
- Reuse installed CLI authentication. Do not collect browser cookies, API keys, or duplicate provider credentials.
- Codex reads `account/rateLimits/read` from a long-lived `codex app-server` child process.
- Claude receives status-line JSON through a preserving wrapper, caches `rate_limits`, and forwards the user's existing status-line command unchanged.

See [docs/BOOTSTRAP.md](docs/BOOTSTRAP.md) for the agent handoff and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design.

## Status

Bootstrap only. Provider behavior and Command Palette API details must be verified against the installed versions before implementation.
