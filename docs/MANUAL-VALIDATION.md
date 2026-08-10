# Manual validation checklist

- [ ] Install the MSIX on a supported Windows 11 machine and reload Command Palette.
- [ ] Add Codex and Claude independently in Dock edit mode; verify each band can be pinned/unpinned alone.
- [ ] With Codex CLI absent, confirm Codex —/diagnostic details are shown rather than 0%.
- [ ] With Codex unauthenticated, confirm the Dock shows an authentication hint.
- [ ] With a valid Codex fixture or account, verify session and weekly used percentages and reset countdowns.
- [ ] Stop/restart the Codex CLI and confirm last-known values are retained while the adapter recovers.
- [ ] Configure a visible Claude status line, install the bridge with --replace, and verify its output remains unchanged.
- [ ] Run Claude until rate_limits is present; verify the cache updates and the two windows appear.
- [ ] Wait beyond the 15-minute threshold or edit a fixture timestamp; verify the stale label appears without deleting values.
- [ ] Feed malformed Claude JSON; verify the original status line still runs and no cache is written.
- [ ] Restore Claude settings and compare the restored file with the pre-install backup.
- [ ] Inspect logs and cache contents to confirm no credentials, cookies, commands, environment values, or raw provider JSON are persisted.