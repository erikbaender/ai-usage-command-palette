# UX expectations

## Dock

Use four independently pinnable bands because Command Palette does not expose a per-band width setting:

```text
66% - 3d 6h
Codex Weekly
88% - 2h 14m
Codex Session
66% - 3d 6h
Claude Weekly
88% - 2h 14m
Claude Session
```

Each band shows one provider/window pair. The top line is the remaining percentage followed by the nonzero reset units (`d`, `h`, `m`); the bottom line is the provider and window name. Keep missing provider windows as `—` rather than fabricating values.

## Expanded details

Show provider name, plan type when supplied, each window's used and remaining percentages, reset countdown/local time, source, and last-updated time. For Claude, explicitly label cached/stale data.

## States

- Available: normal percentage and countdown.
- No CLI: `Codex CLI not found` or `Claude Code not detected`.
- Not authenticated: actionable setup hint, no credentials requested by the extension.
- Waiting for data: `Waiting for first Claude Code response`.
- Stale: keep the last known values with a visible stale marker.
- Error: concise message plus retry/diagnostics action; never dump process output into the Dock.

## Interaction

Clicking a band opens a detail page. A refresh action may trigger a Codex read, but should not launch a Claude TUI or `/usage` scrape. Provide a settings/help action linking to the local setup documentation.
