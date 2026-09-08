using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11's take-off speeds are announced as the FMS sets them, the PMDG way ("V1 145 knots"):
/// baseline-first, silent on an unchanged redelivery and on a cleared speed, spoken again when the
/// speeds come back. Reported 2026-09-08: "the v-speeds aren't announced automatically when they
/// are being set, as is the case with the PMDGs".
/// </summary>
public class Md11VSpeedsTests
{
    private static readonly TFDiMD11Definition Def = new();

    [Fact]
    public void ASpeed_IsSpokenWhenSet_NotWhenFirstSeen()
    {
        var a = new Md11VSpeedAnnouncer();
        Assert.Null(a.OnUpdate("MD11_V1", 145));                 // connecting with speeds entered is not a change
        Assert.Null(a.OnUpdate("MD11_V1", 145));                 // the panel's per-second force-read
        Assert.Equal("V1 147 knots", a.OnUpdate("MD11_V1", 147));
        Assert.Null(a.OnUpdate("MD11_V1", 147.3));               // jitter inside half a knot
    }

    [Fact]
    public void AllFiveSpeeds_SpeakWithTheirOwnLabel()
    {
        var a = new Md11VSpeedAnnouncer();
        foreach (var key in Md11VSpeeds.Keys) a.OnUpdate(key, 0);   // the FMS has computed nothing yet
        Assert.Equal("V1 145 knots", a.OnUpdate("MD11_V1", 145));
        Assert.Equal("VR 150 knots", a.OnUpdate("MD11_VR", 150));
        Assert.Equal("V2 158 knots", a.OnUpdate("MD11_V2", 158));
        Assert.Equal("Slat retraction speed 190 knots", a.OnUpdate("MD11_VSR", 190));
        Assert.Equal("Flap retraction speed 210 knots", a.OnUpdate("MD11_VFR", 210));
    }

    [Fact]
    public void AClearedSpeed_IsSilent_AndItsReturnIsNot()
    {
        var a = new Md11VSpeedAnnouncer();
        a.OnUpdate("MD11_V2", 158);
        Assert.Null(a.OnUpdate("MD11_V2", 0));                   // the FMS wiped the perf entry
        Assert.Null(a.OnUpdate("MD11_V2", -999));                // TFDi's dashed sentinel, likewise
        Assert.Equal("V2 160 knots", a.OnUpdate("MD11_V2", 160));
    }

    [Fact]
    public void Reset_MakesEverySpeedABaselineAgain()
    {
        var a = new Md11VSpeedAnnouncer();
        a.OnUpdate("MD11_VR", 150);
        a.Reset();
        Assert.Null(a.OnUpdate("MD11_VR", 152));
        Assert.Equal("VR 153 knots", a.OnUpdate("MD11_VR", 153));
    }

    [Fact]
    public void AnUnrelatedExport_IsNotASpeed()
    {
        Assert.Null(new Md11VSpeedAnnouncer().OnUpdate("MD11_ENG1_N1", 95));
        Assert.False(Md11VSpeeds.IsKey("MD11_ENG1_N1"));
    }

    /// <summary>Every speed that speaks has a Ctrl+M row: ExcludeFromMonitorManager means "muted by plumbing" and must never sit on a var that speaks.</summary>
    [Fact]
    public void EverySpeedThatSpeaks_HasAMuteRow()
    {
        var vars = Def.GetVariables();
        foreach (var key in Md11VSpeeds.Keys)
        {
            var d = vars[key];
            Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
            Assert.True(d.IsAnnounced);
            Assert.False(d.ExcludeFromMonitorManager);
            Assert.True(Md11VSpeeds.IsKey(key));
        }
    }
}
