using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The captain's altimeter speaks once it SETTLES: a knob wind delivers a run of values a second
/// apart, and the pilot wants the final setting, not every hundredth on the way. Baseline-first
/// (connecting mid-flight must not read the altimeter), deduplicated on the sentence.
/// </summary>
public class Md11AltimeterAnnouncerTests
{
    [Fact]
    public void FirstSight_IsSilent()
    {
        var a = new Md11AltimeterAnnouncer();
        a.OnUpdate(29.92, 0);
        Assert.False(a.HasPending);
        Assert.Null(a.Due(5000));
    }

    [Fact]
    public void ARunOfValues_SpeaksOnce_AfterTheSettle()
    {
        var a = new Md11AltimeterAnnouncer();
        a.OnUpdate(29.92, 0);                       // baseline
        a.OnUpdate(29.93, 10000);
        a.OnUpdate(29.94, 10400);
        a.OnUpdate(29.95, 10800);
        Assert.Null(a.Due(11000));                  // 200 ms after the last value: not settled
        Assert.True(a.HasPending);
        Assert.Equal("Altimeter: 1014, 29.95", a.Due(10800 + Md11AltimeterAnnouncer.SettleMs));
        Assert.False(a.HasPending);
        Assert.Null(a.Due(20000));                  // nothing left to say
    }

    [Fact]
    public void TheSameSentenceAgain_IsSilent()
    {
        var a = new Md11AltimeterAnnouncer();
        a.OnUpdate(29.92, 0);
        a.OnUpdate(29.95, 1000);
        Assert.Equal("Altimeter: 1014, 29.95", a.Due(3000));
        a.OnUpdate(29.95, 5000);                    // re-delivered, unchanged
        Assert.Null(a.Due(7000));
    }

    [Fact]
    public void StandardPressure_IsSpokenAsStandard_InEitherUnit()
    {
        Assert.Equal("Altimeter standard", Md11AltimeterAnnouncer.Sentence(29.92));
        Assert.Equal("Altimeter standard", Md11AltimeterAnnouncer.Sentence(1013));
        Assert.Equal("Altimeter: 1020, 30.12", Md11AltimeterAnnouncer.Sentence(30.12));
        Assert.Equal("Altimeter: 995, 29.38", Md11AltimeterAnnouncer.Sentence(995));
    }

    [Fact]
    public void Reset_ReBaselines()
    {
        var a = new Md11AltimeterAnnouncer();
        a.OnUpdate(29.92, 0);
        a.OnUpdate(29.95, 1000);
        a.Reset();
        a.OnUpdate(30.00, 2000);                    // the first value after a reset is a baseline again
        Assert.Null(a.Due(9000));
        a.OnUpdate(30.05, 10000);
        Assert.Equal("Altimeter: 1018, 30.05", a.Due(12000));
    }
}
