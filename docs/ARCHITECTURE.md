# Architecture

```text
Command Palette extension
  ├─ DockController
  ├─ UsageCoordinator
  │    ├─ CodexProvider ── codex app-server (stdio JSONL)
  │    │                    └─ account/rateLimits/read + update notification
    │    └─ ClaudeProvider ── claude -p "/usage" (JSON)
    │                         └─ authenticated CLI output
    └─ DetailView / formatting
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

## Claude CLI adapter

- Resolve `claude.exe` from `AI_USAGE_CLAUDE_PATH`, PATH, and the per-user `.local\bin` fallback.
- Start `claude -p "/usage" --output-format json --no-session-persistence` with redirected UTF-8 stdout/stderr.
- Parse the JSON result for session and weekly percentages plus timezone-aware reset dates.
- Bound each request and retain the last successful snapshot as stale when a refresh fails.
- Do not modify Claude settings, read provider credentials, scrape browser data, or install a helper executable.
- Write bounded, sanitized diagnostics to the packaged app's user-local log when the CLI fails or returns unrecognized output.

## Refresh and freshness

Both providers are actively refreshed. Detail views show `Last updated` and the provider source. A configurable stale threshold should default to 15 minutes; stale data remains visible with a stale indicator rather than disappearing.
