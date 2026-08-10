# Milestones, risks, and acceptance criteria

## Milestones

1. SDK spike: minimal extension with a static Dock band.
2. Domain layer: model, formatting, freshness, and fake-provider tests.
3. Codex: app-server lifecycle and rate-limit contract fixtures.
4. Claude: authenticated CLI polling, output parsing, bounded diagnostics.
5. UX: live Dock mutation, details, state handling, accessibility.
6. Hardening: packaging, diagnostics, documentation, manual matrix.

## Main risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Command Palette/Dock API changes | Pin SDK, document tested PowerToys version, isolate UI adapter. |
| Codex app-server protocol drift | Use installed schema, tolerant parsing, contract fixtures, visible unsupported state. |
| Codex process hangs | Async I/O, cancellation, bounded timeouts, restart backoff. |
| Claude has no direct quota API | Use the authenticated `claude -p /usage` command; do not scrape browser or TUI. |
| Claude CLI output changes | Tolerant parsing, UTF-8 capture, bounded timeout, explicit unavailable/stale state, and diagnostics. |
| Diagnostics expose sensitive data | Bound and sanitize logs; never persist credentials, cookies, or session identifiers. |

## Acceptance criteria

- [ ] A clean Windows machine with supported PowerToys and Codex CLI can install the extension and see a Codex Dock band without entering an API key.
- [ ] Codex values come from `codex app-server` and `account/rateLimits/read`; no browser cookies or undocumented web endpoint are used.
- [ ] Codex session and weekly windows display correct used percentages and reset countdowns from fixtures and a live account.
- [ ] Codex process restart, timeout, unauthenticated, missing-binary, and schema-error states are handled without crashing Command Palette.
- [ ] An authenticated Claude CLI returns session and weekly values through `claude -p /usage`.
- [ ] Claude session and weekly values update after a bounded CLI refresh, show source/last-updated, and become visibly stale after the configured threshold.
- [ ] Missing CLI, authentication failure, timeout, malformed JSON, unrecognized output, and encoding issues are handled without crashing Command Palette.
- [ ] Claude diagnostics are bounded and sanitized.
- [ ] No secret or browser-cookie handling exists in the MVP code or logs.
- [ ] Dock bands are independently clickable/pinnable where supported, have accessible labels, and never display fabricated zeros.

## Test strategy

- Unit: model validation, percentage formatting, countdowns, freshness, JSON parsing.
- Contract: recorded sanitized Codex RPC responses/notifications and Claude CLI JSON fixtures.
- Process: fake app-server and fake Claude CLI for framing, cancellation, restart, and output parsing.
- Manual: supported PowerToys versions, absent providers, CLI authentication failures, sleep/resume, timezone changes, and provider updates.
