# Milestones, risks, and acceptance criteria

## Milestones

1. SDK spike: minimal extension with a static Dock band.
2. Domain layer: model, formatting, freshness, and fake-provider tests.
3. Codex: app-server lifecycle and rate-limit contract fixtures.
4. Claude: preserving bridge, atomic cache, installer/restore flow.
5. UX: live Dock mutation, details, state handling, accessibility.
6. Hardening: packaging, diagnostics, documentation, manual matrix.

## Main risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Command Palette/Dock API changes | Pin SDK, document tested PowerToys version, isolate UI adapter. |
| Codex app-server protocol drift | Use installed schema, tolerant parsing, contract fixtures, visible unsupported state. |
| Codex process hangs | Async I/O, cancellation, bounded timeouts, restart backoff. |
| Claude has no direct quota API | Use status-line telemetry only; do not scrape browser or TUI. |
| Existing Claude status line is broken | Backup, wrapper pass-through, atomic config changes, restore command, fail-open behavior. |
| Cache exposes sensitive data | Store usage-only fields, user-local ACLs, no tokens/commands in cache or logs. |
| Remote browser usage makes Claude stale | Show source and last-updated time; document limitation. |

## Acceptance criteria

- [ ] A clean Windows machine with supported PowerToys and Codex CLI can install the extension and see a Codex Dock band without entering an API key.
- [ ] Codex values come from `codex app-server` and `account/rateLimits/read`; no browser cookies or undocumented web endpoint are used.
- [ ] Codex session and weekly windows display correct used percentages and reset countdowns from fixtures and a live account.
- [ ] Codex process restart, timeout, unauthenticated, missing-binary, and schema-error states are handled without crashing Command Palette.
- [ ] Claude Code can be configured with the preserving bridge and its existing visible status line remains functionally unchanged.
- [ ] The bridge caches valid `rate_limits` atomically and the extension reads the cache without provider credentials.
- [ ] Claude session and weekly values update after a Claude Code response, show source/last-updated, and become visibly stale after the configured threshold.
- [ ] Malformed Claude input, cache corruption, original command failure, and concurrent writes are tested.
- [ ] No secret or browser-cookie handling exists in the MVP code or logs.
- [ ] Dock bands are independently clickable/pinnable where supported, have accessible labels, and never display fabricated zeros.

## Test strategy

- Unit: model validation, percentage formatting, countdowns, freshness, JSON parsing.
- Contract: recorded sanitized Codex RPC responses/notifications and Claude status-line fixtures.
- Process: fake app-server and fake status-line command for framing, cancellation, restart, and pass-through.
- File: atomic replacement, corruption recovery, ACL/permission checks, concurrent bridge invocations.
- Manual: supported PowerToys versions, absent providers, existing status-line tools, sleep/resume, timezone changes, and provider updates.
