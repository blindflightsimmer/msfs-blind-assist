using MSFSBlindAssist.Services;
using Xunit;
using A = MSFSBlindAssist.Services.WheelChocksAutoRemover.Action;

namespace MSFSBlindAssist.Tests;

public class WheelChocksAutoRemoverTests
{
    private static readonly DateTime T0 = new(2026, 10, 16, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Chocks_off_does_nothing()
    {
        var r = new WheelChocksAutoRemover();
        Assert.Equal(A.None, r.OnSample(false, T0));
    }

    [Fact]
    public void Chocks_on_are_removed_then_the_removal_is_announced()
    {
        var r = new WheelChocksAutoRemover();
        Assert.Equal(A.Remove, r.OnSample(true, T0));
        Assert.Equal(A.AnnounceRemoved, r.OnSample(false, T0.AddSeconds(1)));
        Assert.Equal(A.None, r.OnSample(false, T0.AddSeconds(2)));
    }

    [Fact]
    public void Chocks_put_back_repeatedly_are_left_on_after_three_tries()
    {
        var r = new WheelChocksAutoRemover();
        for (int i = 0; i < WheelChocksAutoRemover.MaxRemovalsInWindow; i++)
        {
            Assert.Equal(A.Remove, r.OnSample(true, T0.AddSeconds(i * 5)));
            Assert.Equal(A.AnnounceRemoved, r.OnSample(false, T0.AddSeconds(i * 5 + 1)));
        }
        Assert.Equal(A.AnnounceGivingUp, r.OnSample(true, T0.AddSeconds(20)));
        Assert.Equal(A.None, r.OnSample(true, T0.AddSeconds(25)));
        Assert.Equal(A.None, r.OnSample(false, T0.AddSeconds(26)));
    }

    [Fact]
    public void Removals_spread_beyond_the_window_never_give_up()
    {
        var r = new WheelChocksAutoRemover();
        for (int i = 0; i < 10; i++)
        {
            var t = T0.AddMinutes(i * 2);
            Assert.Equal(A.Remove, r.OnSample(true, t));
            Assert.Equal(A.AnnounceRemoved, r.OnSample(false, t.AddSeconds(1)));
        }
    }

    [Fact]
    public void Reset_rearms_after_giving_up()
    {
        var r = new WheelChocksAutoRemover();
        for (int i = 0; i <= WheelChocksAutoRemover.MaxRemovalsInWindow; i++) r.OnSample(true, T0.AddSeconds(i));
        r.Reset();
        Assert.Equal(A.Remove, r.OnSample(true, T0.AddSeconds(10)));
    }
}
