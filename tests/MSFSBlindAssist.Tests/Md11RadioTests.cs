using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11's COM frequency announcements: what is said, when it is said, and how the six stock
/// variables are registered. TFDi drives the simulator's own COM radios (measured live), so this
/// is the same shape as the FBW and HS787 read-outs.
/// </summary>
public class Md11RadioTests
{
    private static readonly TFDiMD11Definition Def = new();
    private static Dictionary<string, SimVarDefinition> Vars => Def.GetVariables();

    [Theory]
    [InlineData("COM_ACTIVE_FREQUENCY:1", 135500, "COM 1 active 135.500")]
    [InlineData("COM_STANDBY_FREQUENCY:2", 127750, "COM 2 standby 127.750")]
    [InlineData("COM_ACTIVE_FREQUENCY:3", 126800, "COM 3 active 126.800")]
    public void Describe_NamesTheRadioTheSideAndTheMegahertz(string key, double khz, string expected)
    {
        Assert.Equal(expected, Md11Radios.Describe(key, khz));
    }

    [Fact]
    public void FirstSample_SeedsSilently_AndOnlyARealChangeSpeaks()
    {
        var com = new Md11ComAnnouncer();
        Assert.Null(com.OnUpdate("COM_ACTIVE_FREQUENCY:1", 135500));   // connecting: seed, say nothing
        Assert.Null(com.OnUpdate("COM_ACTIVE_FREQUENCY:1", 135500));   // re-delivered, unchanged
        Assert.Equal("COM 1 active 132.525", com.OnUpdate("COM_ACTIVE_FREQUENCY:1", 132525));   // an XFER
        Assert.Null(com.OnUpdate("COM_STANDBY_FREQUENCY:1", 135500));  // the standby's own first sample
        Assert.Equal("COM 1 standby 135.525", com.OnUpdate("COM_STANDBY_FREQUENCY:1", 135525)); // one tuner click
    }

    [Fact]
    public void OutsideTheAirband_IsSilent_AndReseedsNothing()
    {
        var com = new Md11ComAnnouncer();
        com.OnUpdate("COM_ACTIVE_FREQUENCY:2", 127750);
        Assert.Null(com.OnUpdate("COM_ACTIVE_FREQUENCY:2", 0));          // radio unpowered: no "COM 2 active 0.000"
        Assert.Equal("COM 2 active 127.750", com.OnUpdate("COM_ACTIVE_FREQUENCY:2", 127750));   // power back: spoken
        Assert.Equal("--", Md11Radios.Display(0));
        Assert.Equal("127.750", Md11Radios.Display(127750));
    }

    [Fact]
    public void Reset_MakesTheNextSampleABaselineAgain()
    {
        var com = new Md11ComAnnouncer();
        com.OnUpdate("COM_ACTIVE_FREQUENCY:1", 135500);
        com.Reset();
        Assert.Null(com.OnUpdate("COM_ACTIVE_FREQUENCY:1", 121500));   // a reconnect must not narrate the stack
    }

    [Fact]
    public void TheSixComVariables_AreStockSimVars_BatchCovered_AndMutable()
    {
        foreach (var key in Md11Radios.Keys)
        {
            var d = Vars[key];
            Assert.Equal(Md11Radios.SimVarName(key), d.Name);     // "COM ACTIVE FREQUENCY:1" — a stock name, never an L:var
            Assert.Equal(SimVarType.SimVar, d.Type);
            Assert.Equal("kHz", d.Units);
            Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
            Assert.True(d.IsAnnounced);
            Assert.False(d.ExcludeFromMonitorManager);                 // a Ctrl+M row that really mutes something
            Assert.Equal(Md11Radios.DisplayName(key), d.DisplayName);
        }
        Assert.Equal("COM 1 Active Frequency", Md11Radios.DisplayName("COM_ACTIVE_FREQUENCY:1"));
        Assert.Equal("COM 3 Standby Frequency", Md11Radios.DisplayName("COM_STANDBY_FREQUENCY:3"));
    }

    [Fact]
    public void TheRadiosReadoutPanel_ListsAllSix_InRadioOrder()
    {
        Assert.Contains("Radios", Def.GetPanelStructure()["Read-outs"]);
        Assert.Equal(Md11Radios.Keys, Def.GetPanelControls()["Radios"]);
    }

    [Fact]
    public void ADisplayedFrequency_IsMegahertzWithThreeDecimals()
    {
        Assert.True(Def.TryGetDisplayOverride("COM_ACTIVE_FREQUENCY:1", 135500, out var text));
        Assert.Equal("135.500", text);
    }
}
