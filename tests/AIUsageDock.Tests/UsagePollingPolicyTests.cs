using AIUsageDock.Core;

namespace AIUsageDock.Tests;

public sealed class UsagePollingPolicyTests
{
    [Fact]
    public void DefaultsToFiveSecondsIdleAndOneSecondDuringActiveUsage()
    {
        var policy = UsagePollingPolicy.Default;

        Assert.Equal(TimeSpan.FromSeconds(5), policy.GetInterval(usageActive: false));
        Assert.Equal(TimeSpan.FromSeconds(1), policy.GetInterval(usageActive: true));
    }

    [Fact]
    public void UsesConfiguredIntervalsForBothStates()
    {
        var policy = new UsagePollingPolicy(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(15));

        Assert.Equal(TimeSpan.FromMinutes(5), policy.GetInterval(usageActive: false));
        Assert.Equal(TimeSpan.FromSeconds(15), policy.GetInterval(usageActive: true));
    }

    [Fact]
    public void RejectsNonPositiveIntervals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UsagePollingPolicy(TimeSpan.Zero, TimeSpan.FromSeconds(30)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UsagePollingPolicy(TimeSpan.FromMinutes(1), TimeSpan.Zero));
    }
}
