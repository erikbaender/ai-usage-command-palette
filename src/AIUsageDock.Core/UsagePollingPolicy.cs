namespace AIUsageDock.Core;

public sealed record UsagePollingPolicy
{
    public UsagePollingPolicy(TimeSpan idleInterval, TimeSpan activeUsageInterval)
    {
        if (idleInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleInterval), "The idle polling interval must be positive.");
        }

        if (activeUsageInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(activeUsageInterval), "The active-usage polling interval must be positive.");
        }

        IdleInterval = idleInterval;
        ActiveUsageInterval = activeUsageInterval;
    }

    public TimeSpan IdleInterval { get; }

    public TimeSpan ActiveUsageInterval { get; }

    public static UsagePollingPolicy Default { get; } = new(
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(1));

    public TimeSpan GetInterval(bool usageActive) =>
        usageActive ? ActiveUsageInterval : IdleInterval;
}
