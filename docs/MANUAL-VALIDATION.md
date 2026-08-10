# Manual validation checklist

- [ ] Install the MSIX on a supported Windows 11 machine and reload Command Palette.
- [ ] Add Codex and Claude independently in Dock edit mode; verify each band can be pinned/unpinned alone.
- [ ] With Codex CLI absent, confirm Codex —/diagnostic details are shown rather than 0%.
- [ ] With Codex unauthenticated, confirm the Dock shows an authentication hint.
- [ ] With a valid Codex fixture or account, verify session and weekly used percentages and reset countdowns.
- [ ] Stop/restart the Codex CLI and confirm last-known values are retained while the adapter recovers.
- [ ] Run `claude -p "/usage" --output-format json --no-session-persistence` manually and verify authenticated usage output.
- [ ] Refresh Claude from Command Palette; verify session and weekly windows appear.
- [ ] Wait beyond the 15-minute threshold or edit a fixture timestamp; verify the stale label appears without deleting values.
- [ ] Feed malformed Claude CLI JSON/output; verify the provider reports an error or stale state without crashing.
- [ ] Inspect the package-local diagnostic log to confirm entries are bounded and sanitized.
