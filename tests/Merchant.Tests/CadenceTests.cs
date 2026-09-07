using Merchant.Discord;
using Xunit;

namespace Merchant.Tests;

/// <summary>
/// When a channel is owed a post. This is separate from fetching on purpose: everything is swept
/// on one interval, and each subscription posts on its own clock reading what the sweep filed.
/// </summary>
public class CadenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static Subscription At(Cadence cadence, DateTimeOffset? lastPosted) =>
        new(1, 1, 1, "under-10", cadence, null, lastPosted);

    [Fact]
    public void A_channel_that_has_never_posted_is_always_due()
    {
        foreach (Cadence cadence in Enum.GetValues<Cadence>())
        {
            Assert.True(Sweeper.IsDue(At(cadence, null), Now));
        }
    }

    [Fact]
    public void Live_is_due_on_every_sweep()
    {
        Assert.True(Sweeper.IsDue(At(Cadence.Live, Now.AddMinutes(-1)), Now));
    }

    [Theory]
    [InlineData(22, false)]
    [InlineData(24, true)]
    [InlineData(48, true)]
    public void Daily_waits_about_a_day(int hoursAgo, bool due)
    {
        Assert.Equal(due, Sweeper.IsDue(At(Cadence.Daily, Now.AddHours(-hoursAgo)), Now));
    }

    [Fact]
    public void Daily_tolerates_a_sweep_that_lands_slightly_late()
    {
        // Nominally 24h, but a 30-minute sweep interval means the tick after the anniversary is up
        // to 30 minutes late. A strict 24h window would walk the daily post around the clock.
        Assert.True(Sweeper.IsDue(At(Cadence.Daily, Now.AddHours(-23.5)), Now));
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(7, true)]
    [InlineData(14, true)]
    public void Weekly_waits_about_a_week(int daysAgo, bool due)
    {
        Assert.Equal(due, Sweeper.IsDue(At(Cadence.Weekly, Now.AddDays(-daysAgo)), Now));
    }

    [Fact]
    public void Weekly_is_due_before_a_full_seven_days_so_it_does_not_skip_a_chart()
    {
        // Steam republishes every Tuesday. A strict 7-day window drifts past the next publication
        // and eventually posts fortnightly.
        Assert.True(Sweeper.IsDue(At(Cadence.Weekly, Now.AddDays(-6.95)), Now));
    }
}
