namespace AIUsageDock.Core;

public sealed record UsagePollingPolicy
{
    public UsagePollingPolicy(TimeSpan idleInterval, TimeSpan runningSessionInterval)
    {
        if (idleInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleInterval), "The idle polling interval must be positive.");
        }

        if (runningSessionInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(runningSessionInterval), "The running-session polling interval must be positive.");
        }

        IdleInterval = idleInterval;
        RunningSessionInterval = runningSessionInterval;
    }

    public TimeSpan IdleInterval { get; }

    public TimeSpan RunningSessionInterval { get; }

    public static UsagePollingPolicy Default { get; } = new(
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(15));

    public TimeSpan GetInterval(bool runningSession) =>
        runningSession ? RunningSessionInterval : IdleInterval;
}
