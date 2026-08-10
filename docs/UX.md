# UX expectations

## Dock

Use two independently pinnable bands where the SDK permits it:

```text
Wk 66%/3d6h
Ses 88%/2h14m
```

The top line shows the weekly remaining percentage and reset countdown. The bottom line shows the session remaining percentage and reset countdown. Labels are intentionally compact because Command Palette does not expose per-band width. Keep missing provider windows as `—` rather than fabricating values.

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
