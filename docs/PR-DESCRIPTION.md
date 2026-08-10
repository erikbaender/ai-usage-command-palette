# Bootstrap AI Usage Dock for implementation

## Summary

This draft PR adds the implementation context for a PowerToys Command Palette Dock extension that shows live Codex and Claude subscription usage.

## Scope

- Codex: installed/authenticated `codex app-server`, `account/rateLimits/read`, and optional `account/rateLimits/updated` notifications.
- Claude: installed/authenticated Claude Code status-line JSON, preserving wrapper, and local `rate_limits` cache.
- Shared provider contract and normalized session/weekly usage model.
- Architecture, UX, milestones, risks, security boundary, testing strategy, and concrete acceptance criteria.

## Implementation instructions

Start with the SDK/version spike and keep all provider-specific process/file behavior behind interfaces. Do not add browser-cookie scraping, API-key setup, Claude TUI automation, or undocumented provider web endpoints to the MVP.

## Files

- `docs/BOOTSTRAP.md` — agent mission, decisions, research questions, and deliverables.
- `docs/ARCHITECTURE.md` — provider interfaces, data model, process boundaries, and refresh behavior.
- `docs/UX.md` — Dock/detail UX and state expectations.
- `docs/MILESTONES.md` — milestones, risks, acceptance criteria, and test strategy.
- `docs/SECURITY.md` — credential and privacy boundary.
