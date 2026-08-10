using AIUsageDock.Core;

namespace AIUsageDock.Tests;

public sealed class UsagePollingPolicyTests
{
    [Fact]
    public void DefaultsToOneMinuteIdleAndThirtySecondsDuringRunningSession()
    {
        var policy = UsagePollingPolicy.Default;

        Assert.Equal(TimeSpan.FromSeconds(60), policy.GetInterval(runningSession: false));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetInterval(runningSession: true));
    }

    [Fact]
    public void UsesConfiguredIntervalsForBothStates()
    {
        var policy = new UsagePollingPolicy(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(15));

        Assert.Equal(TimeSpan.FromMinutes(5), policy.GetInterval(runningSession: false));
        Assert.Equal(TimeSpan.FromSeconds(15), policy.GetInterval(runningSession: true));
    }

    [Fact]
    public void RejectsNonPositiveIntervals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UsagePollingPolicy(TimeSpan.Zero, TimeSpan.FromSeconds(30)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UsagePollingPolicy(TimeSpan.FromMinutes(1), TimeSpan.Zero));
    }
}
