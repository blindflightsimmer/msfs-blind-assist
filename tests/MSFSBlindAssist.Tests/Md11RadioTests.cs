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
    public void TheRadiosPanel_OpensThePedestal_ReadBacksInTheStatusDisplay_TypedAndTransferAsRows()
    {
        var structure = Def.GetPanelStructure();
        Assert.Equal("Radios", structure["Pedestal"][0]);
        Assert.DoesNotContain("Radios", structure["Read-outs"]);
        Assert.Equal(Md11Radios.PanelKeys, Def.GetPanelControls()["Radios"]);
        Assert.Equal(new[] { "COM_STANDBY_FREQUENCY_SET:1", "COM1_RADIO_SWAP" }, Md11Radios.PanelKeys.Take(2));
        Assert.Equal(Md11Radios.Keys, Def.GetPanelDisplayVariables()["Radios"]);   // "COM 1 Active Frequency: 135.500"
    }

    [Theory]
    [InlineData(124.9, 124900000u)]
    [InlineData(118.0, 118000000u)]
    [InlineData(136.975, 136975000u)]
    [InlineData(121.500, 121500000u)]
    public void ATypedStandby_BecomesExactHertz(double mhz, uint expected)
    {
        Assert.True(Md11Radios.TryParseMhz(mhz, out var hz, out var error));
        Assert.Equal(expected, hz);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData(117.975)]
    [InlineData(137.0)]
    [InlineData(0)]
    [InlineData(1249)]      // "1249" — the pilot forgot the point; not a frequency
    public void AnOutOfBandStandby_IsRefused(double mhz)
    {
        Assert.False(Md11Radios.TryParseMhz(mhz, out var hz, out var error));
        Assert.Equal(0u, hz);
        Assert.Equal(Md11Radios.InvalidFrequencyMessage, error);
    }

    [Fact]
    public void TheStockEvents_AreNamedPerRadio_WithComOneUnnumbered()
    {
        Assert.Equal("COM_STBY_RADIO_SET_HZ", Md11Radios.StandbySetEvent(1));
        Assert.Equal("COM2_STBY_RADIO_SET_HZ", Md11Radios.StandbySetEvent(2));
        Assert.Equal("COM3_STBY_RADIO_SET_HZ", Md11Radios.StandbySetEvent(3));
        Assert.Equal("COM1_RADIO_SWAP", Md11Radios.SwapEvent(1));
        Assert.Equal(3, Md11Radios.RadioIndex("COM3_RADIO_SWAP"));
        Assert.Equal(2, Md11Radios.RadioIndex("COM_STANDBY_FREQUENCY_SET:2"));
        Assert.True(Md11Radios.IsStandbySetKey("COM_STANDBY_FREQUENCY_SET:1"));
        Assert.False(Md11Radios.IsComKey("COM_STANDBY_FREQUENCY_SET:1"));   // the field is not a read-out row
        Assert.True(Md11Radios.IsSwapKey("COM2_RADIO_SWAP"));
    }

    [Fact]
    public void TheSetFieldAndTransferButton_AreEventDefinitions()
    {
        for (int idx = 1; idx <= 3; idx++)
        {
            var set = Vars[Md11Radios.StandbySetKey(idx)];
            Assert.Equal(SimVarType.Event, set.Type);
            Assert.Equal(Md11Radios.StandbySetEvent(idx), set.Name);
            Assert.Equal($"COM {idx} Standby", set.DisplayName);
            Assert.False(set.PreventTextInput);

            var swap = Vars[Md11Radios.SwapKey(idx)];
            Assert.Equal(SimVarType.Event, swap.Type);
            Assert.Equal($"COM{idx}_RADIO_SWAP", swap.Name);
            Assert.True(swap.RenderAsButton);
            Assert.Equal($"COM {idx} Transfer", swap.DisplayName);
        }
    }

    [Fact]
    public void TheAnnouncer_RemembersTheLastStandby_ForTheTransferReadBack()
    {
        var com = new Md11ComAnnouncer();
        Assert.Null(com.Last("COM_STANDBY_FREQUENCY:1"));
        com.OnUpdate("COM_STANDBY_FREQUENCY:1", 124900);
        Assert.Equal(124900, com.Last("COM_STANDBY_FREQUENCY:1"));
    }

    [Fact]
    public void ADisplayedFrequency_IsMegahertzWithThreeDecimals()
    {
        Assert.True(Def.TryGetDisplayOverride("COM_ACTIVE_FREQUENCY:1", 135500, out var text));
        Assert.Equal("135.500", text);
    }
}
