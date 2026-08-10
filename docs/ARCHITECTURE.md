# Architecture

```text
Command Palette extension
  ├─ DockController
  ├─ UsageCoordinator
  │    ├─ CodexProvider ── codex app-server (stdio JSONL)
  │    │                    └─ account/rateLimits/read + update notification
  │    └─ ClaudeProvider ── %LOCALAPPDATA%\AIUsage\claude.json
  │                         └─ written by Claude status-line bridge
  └─ DetailView / formatting

Claude Code status line
  └─ preserving wrapper
       ├─ parse stdin JSON
       ├─ atomically cache rate_limits
       ├─ invoke configured original command with identical JSON
       └─ forward stdout/stderr/exit behavior as safely as possible
```

## Provider contract

```csharp
public interface IUsageProvider : IAsyncDisposable
{
    ProviderId Id { get; }
    Task<ProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    event EventHandler<ProviderSnapshot>? SnapshotChanged;
}
```

The UI must not know how a provider authenticates or refreshes. A provider may be push-driven, poll-driven, or cache-driven.

## Normalized data model

```csharp
public enum UsageWindow { Session, Weekly, ModelWeekly }
public enum ProviderHealth { Unknown, Available, Unauthenticated, Unavailable, Stale, Error }

public sealed record UsageWindowSnapshot(
    UsageWindow Window,
    double? UsedPercent,
    DateTimeOffset? ResetsAt,
    DateTimeOffset ObservedAt,
    string? LimitId = null);

public sealed record ProviderSnapshot(
    ProviderId Provider,
    ProviderHealth Health,
    IReadOnlyList<UsageWindowSnapshot> Windows,
    DateTimeOffset? LastUpdated,
    string Source,
    string? Message = null,
    string? PlanType = null);
```

Use `double?` or decimal-like validation internally; clamp only values that are demonstrably percentages. Preserve unknown/null values. Calculate remaining as `100 - used` only when `used` is valid.

## Codex adapter

- Resolve `codex` using a configured path or PATH lookup; never assume a fixed install directory.
- Start `codex app-server` with redirected stdin/stdout/stderr and no shell interpretation.
- Perform the protocol initialize handshake and retain request IDs.
- Call `account/rateLimits/read`; parse the primary buckets and `rateLimitsByLimitId` without assuming a fixed number of windows.
- Map `usedPercent`, `windowDurationMins`, `resetsAt`, and `planType` to the normalized model.
- Listen for `account/rateLimits/updated` if the installed schema advertises it; otherwise refresh on a bounded timer (target 60 seconds).
- Restart on process exit or protocol failure with backoff. Surface stale data while retrying, never a fabricated empty state.
- Generate or capture the installed protocol schema during development so version drift is visible.

## Claude adapter and preserving bridge

Claude Code status-line input includes a `rate_limits` object with `five_hour` and `seven_day` entries, each commonly containing `used_percentage` and `resets_at`.

The wrapper must:

1. Read all stdin without modifying the JSON payload.
2. Validate and extract only the known usage fields.
3. Atomically write a cache file with restrictive per-user permissions and a schema/version marker.
4. Execute the user's prior command using an argument-safe process API, passing the exact original JSON on stdin.
5. Forward the prior command's stdout as the visible status line and preserve exit behavior where practical.
6. Fail open for the user's status line: if caching fails, the original command should still run; if the original command fails, do not hide that failure.

The installer must back up the original configuration, write a wrapper configuration, and provide an uninstall/restore path. Never overwrite an existing status-line command without an explicit user action.

## Refresh and freshness

Codex can be actively refreshed. Claude is updated when Claude Code invokes the status line, so its detail view must show `Last updated` and source `Claude Code status line`. A configurable stale threshold should default to 15 minutes; stale Claude data remains visible with a stale indicator rather than disappearing.
