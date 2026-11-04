using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The chocks/covers removal policy and the covers' request-id range. The covers are read every
/// second whether or not they changed, so the remover must pace its retries and only give up once
/// several removals have really failed.
/// </summary>
public class CoverAutoRemoverTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 23, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_cover_seen_on_is_removed_and_its_removal_announced()
    {
        var r = new WheelChocksAutoRemover();
        Assert.Equal(WheelChocksAutoRemover.Action.Remove, r.OnSample(true, T0));
        Assert.Equal(WheelChocksAutoRemover.Action.AnnounceRemoved, r.OnSample(false, T0.AddSeconds(1)));
        Assert.Equal(WheelChocksAutoRemover.Action.None, r.OnSample(false, T0.AddSeconds(2)));
    }

    [Fact]
    public void Samples_every_second_do_not_resend_before_the_retry_interval()
    {
        var r = new WheelChocksAutoRemover();
        Assert.Equal(WheelChocksAutoRemover.Action.Remove, r.OnSample(true, T0));
        for (int s = 1; s < WheelChocksAutoRemover.RetryInterval.TotalSeconds; s++)
            Assert.Equal(WheelChocksAutoRemover.Action.None, r.OnSample(true, T0.AddSeconds(s)));
        Assert.Equal(WheelChocksAutoRemover.Action.Remove, r.OnSample(true, T0 + WheelChocksAutoRemover.RetryInterval));
    }

    [Fact]
    public void A_removal_that_never_takes_is_retried_then_reported()
    {
        var r = new WheelChocksAutoRemover();
        int removes = 0;
        WheelChocksAutoRemover.Action last = WheelChocksAutoRemover.Action.None;
        for (int s = 0; s <= 30 && last != WheelChocksAutoRemover.Action.AnnounceGivingUp; s++)
        {
            last = r.OnSample(true, T0.AddSeconds(s));
            if (last == WheelChocksAutoRemover.Action.Remove) removes++;
        }
        Assert.Equal(WheelChocksAutoRemover.MaxRemovalsInWindow, removes);
        Assert.Equal(WheelChocksAutoRemover.Action.AnnounceGivingUp, last);
        Assert.Equal(WheelChocksAutoRemover.Action.None, r.OnSample(true, T0.AddSeconds(31)));
    }

    [Fact]
    public void Each_cover_request_id_maps_to_its_own_index_and_nothing_else_does()
    {
        Assert.Equal(361, SimCovers.FirstRequestId); // right after the chocks (360); 362-368 kept free
        Assert.Equal(1, SimCovers.All[0].Index);
        for (int i = 0; i < SimCovers.All.Count; i++)
            Assert.Equal(i + 1, SimCovers.IndexForRequest(361u + (uint)i));
        Assert.Null(SimCovers.IndexForRequest(360u)); // the chocks
        Assert.Null(SimCovers.IndexForRequest(361u + (uint)SimCovers.All.Count));
    }

    [Fact]
    public void Cover_names_round_trip_and_other_names_are_ignored()
    {
        Assert.Equal("AIRCRAFT_COVER_1", SimCovers.VarName(1));
        Assert.Equal(2, SimCovers.IndexForVarName("AIRCRAFT_COVER_2"));
        Assert.Null(SimCovers.IndexForVarName("AIRCRAFT_COVER_9"));
        Assert.Null(SimCovers.IndexForVarName("WHEEL_CHOCKS"));
        Assert.Equal("Engine covers", SimCovers.NameOf(1));
        Assert.Equal("Pitot covers", SimCovers.NameOf(2));
    }
}
