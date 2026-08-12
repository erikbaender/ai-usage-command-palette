# Manual validation checklist

- [ ] Install the MSIX on a supported Windows 11 machine and reload Command Palette.
- [ ] Add Codex and Claude independently in Dock edit mode; verify each band can be pinned/unpinned alone.
- [ ] With both CLIs absent, connect each web account and verify all four bands continue to update.
- [ ] Complete each web login; verify its window closes automatically and a Windows success notification appears.
- [ ] Sign a web profile out; verify Web-first uses the CLI fallback only for the resulting authentication failure.
- [ ] Simulate a transient authenticated web failure; verify last-known web values become stale and delayed CLI values are not mixed in.
- [ ] Select CLI independently for Codex and Claude and verify each provider switches without affecting the other.
- [ ] With a valid Codex account, compare web and CLI session/weekly percentages and reset countdowns.
- [ ] Stop/restart the Codex CLI and confirm last-known values are retained while the adapter recovers.
- [ ] Run `claude -p "/usage" --output-format json --no-session-persistence` manually and verify authenticated usage output.
- [ ] Refresh Claude from Command Palette; verify session and weekly windows appear.
- [ ] Wait beyond the 15-minute threshold or edit a fixture timestamp; verify the stale label appears without deleting values.
- [ ] Feed malformed Claude CLI JSON/output; verify the provider reports an error or stale state without crashing.
- [ ] Inspect the package-local diagnostic log to confirm entries are bounded and sanitized.
