# UX expectations

## Dock

Use two independently pinnable bands where the SDK permits it:

```text
Codex   5h 58% left · 7d 32% left
Claude  5h 79% left · 7d 26% left
```

The exact typography and icon API depend on the installed Command Palette SDK. Keep text short enough for the Dock and avoid pretending that percentages are remaining when they are used percentages. If space is constrained, prefer `C 42/68` and `Cl 21/74` only after tooltip/detail support is available.

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
