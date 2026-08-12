namespace AIUsageDock.Core;

public enum ProviderId
{
    Codex,
    Claude,
}

public enum UsageWindow
{
    Session,
    Weekly,
    ModelWeekly,
}

public enum ProviderHealth
{
    Unknown,
    Available,
    Unauthenticated,
    Unavailable,
    Stale,
    Error,
}

public sealed record UsageWindowSnapshot(
    UsageWindow Window,
    double? UsedPercent,
    DateTimeOffset? ResetsAt,
    DateTimeOffset ObservedAt,
    string? LimitId = null)
{
    public double? RemainingPercent => UsedPercent is >= 0 and <= 100
        ? 100 - UsedPercent
        : null;
}

public sealed record ProviderSnapshot(
    ProviderId Provider,
    ProviderHealth Health,
    IReadOnlyList<UsageWindowSnapshot> Windows,
    DateTimeOffset? LastUpdated,
    string Source,
    string? Message = null,
    string? PlanType = null)
{
    public UsageWindowSnapshot? GetWindow(UsageWindow window) =>
        Windows.FirstOrDefault(candidate => candidate.Window == window);

    public bool IsStale(DateTimeOffset now, TimeSpan threshold) =>
        LastUpdated is not null && now - LastUpdated.Value > threshold;

    public static ProviderSnapshot Waiting(ProviderId provider, string source, string message) =>
        new(provider, ProviderHealth.Unknown, Array.Empty<UsageWindowSnapshot>(), null, source, message);
}

public interface IUsageProvider : IAsyncDisposable
{
    ProviderId Id { get; }

    event EventHandler<ProviderSnapshot>? SnapshotChanged;

    Task<ProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed record FreshnessPolicy(TimeSpan ClaudeStaleAfter)
{
    public static FreshnessPolicy Default { get; } = new(TimeSpan.FromMinutes(15));
}

public static class UsagePercent
{
    public static bool IsValid(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value is >= 0 and <= 100;

    public static double? FromNullable(double? value) => value is not null && IsValid(value.Value) ? value : null;
}
