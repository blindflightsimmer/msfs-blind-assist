using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11's take-off roll callouts ("V1", "Rotate", "V2") ride the shared
/// TakeoffVSpeedCallouts machine (its contract is pinned in TakeoffVSpeedCalloutsTests); these
/// pin the MD-11's plumbing — the per-frame airspeed feed, the V-speed exports that arm it, and
/// the Ctrl+M rows that mute each call.
/// </summary>
public class Md11TakeoffCalloutsTests
{
    private static readonly TFDiMD11Definition Def = new();
    private static Dictionary<string, SimVarDefinition> Vars => Def.GetVariables();

    /// <summary>
    /// The airspeed feed is a per-var SIM_FRAME subscription (the G_FORCE pattern): the 1 Hz batch
    /// would call "Rotate" up to a second late. It is consumed, never spoken, and has no Ctrl+M
    /// row of its own — the callouts are muted through the V-speed rows.
    /// </summary>
    [Fact]
    public void TheAirspeedFeed_IsPerFrame_Silent_AndHiddenFromCtrlM()
    {
        var ias = Vars[Md11TakeoffCallouts.IasKey];
        Assert.Equal("AIRSPEED INDICATED", ias.Name);
        Assert.Equal("knots", ias.Units);
        Assert.Equal(SimVarType.SimVar, ias.Type);
        Assert.Equal(UpdateFrequency.Continuous, ias.UpdateFrequency);
        Assert.True(ias.IsAnnounced);              // monitored at all
        Assert.True(ias.ExcludeFromBatch);
        Assert.True(ias.HighFrequency);
        Assert.True(ias.ExcludeFromMonitorManager);
    }

    /// <summary>The rows that mute the calls are the V-speed read-outs themselves, so they must be in Ctrl+M.</summary>
    [Theory]
    [InlineData("V1", "MD11_V1", "V1")]
    [InlineData("Rotate", "MD11_VR", "Rotate speed")]
    [InlineData("V2", "MD11_V2", "V2")]
    public void EachCallout_IsMutedByItsVSpeedRow(string callout, string key, string rowName)
    {
        Assert.Equal(key, Md11TakeoffCallouts.MuteKeyFor(callout));
        var row = Vars[key];
        Assert.Equal(rowName, row.DisplayName);
        Assert.True(row.IsAnnounced);
        Assert.False(row.ExcludeFromMonitorManager);
    }

    /// <summary>
    /// The FMS exports arm the machine and a roll then speaks the three calls in order; an export
    /// the FMS has not filled (TFDi reads 0 there) leaves it disarmed, so a roll without V-speeds
    /// is silent rather than wrong.
    /// </summary>
    [Fact]
    public void TheExports_ArmTheMachine_AndARollSpeaksInOrder()
    {
        var m = new TakeoffVSpeedCallouts();
        Md11TakeoffCallouts.Feed(m, "MD11_V1", 0);
        Md11TakeoffCallouts.Feed(m, "MD11_VR", 0);
        Md11TakeoffCallouts.Feed(m, "MD11_ENG1_N1", 95);   // not a V-speed: ignored
        Assert.Empty(m.ProcessSample(0, onGround: true));
        Assert.Empty(m.ProcessSample(160, onGround: true));   // no speeds yet: silent

        Md11TakeoffCallouts.Feed(m, "MD11_V1", 145);
        Md11TakeoffCallouts.Feed(m, "MD11_VR", 150);
        Md11TakeoffCallouts.Feed(m, "MD11_V2", 158);
        Assert.Empty(m.ProcessSample(10, onGround: true));    // arms below 40 kt on the ground
        var spoken = new List<string>();
        foreach (var ias in new[] { 60.0, 100, 140, 146, 151, 157 })
            spoken.AddRange(m.ProcessSample(ias, onGround: true));
        spoken.AddRange(m.ProcessSample(160, onGround: false));
        Assert.Equal(new[] { "V1", "Rotate", "V2" }, spoken);
    }
}
