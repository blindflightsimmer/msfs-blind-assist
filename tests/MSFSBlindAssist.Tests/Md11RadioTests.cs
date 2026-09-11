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
        Assert.Null(com.OnUpdate("COM_ACTIVE_FREQUENCY:1", 121500));   // wiped on the disconnect: the reconnect must not narrate the stack
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
        Assert.Equal(Md11Radios.PanelKeys, Def.GetPanelControls()["Radios"].Take(Md11Radios.PanelKeys.Length));   // the COM rows open the panel
        Assert.Equal(new[] { "COM_STANDBY_FREQUENCY_SET:1", "COM1_RADIO_SWAP" }, Md11Radios.PanelKeys.Take(2));
        Assert.Equal(Md11Radios.Keys, Def.GetPanelDisplayVariables()["Radios"]);   // "COM 1 Active Frequency: 135.500"
    }

    /// <summary>
    /// The six COM rows OPEN the Radios panel, and the three crew positions' radio control panels
    /// the layout table placed there follow them in the table's order — 24 controls (VHF 1-3,
    /// HF 1-2, the MHz and kHz tuners and the transfer, per position) that the COM rows once
    /// replaced, leaving them on no panel at all.
    /// </summary>
    [Fact]
    public void TheRadiosPanel_KeepsTheThreeCrewRadioPanels_AfterTheComRows()
    {
        static string[] Crew(string p) => new[]
        {
            $"MD11_PED_{p}_RADIO_PNL_VHF1_BT", $"MD11_PED_{p}_RADIO_PNL_VHF2_BT", $"MD11_PED_{p}_RADIO_PNL_VHF3_BT",
            $"MD11_PED_{p}_RADIO_PNL_HF1_BT", $"MD11_PED_{p}_RADIO_PNL_HF2_BT",
            $"MD11_PED_{p}_OUTER_RADIO_FREQ_SEL_KB", $"MD11_PED_{p}_INNER_RADIO_FREQ_SEL_KB", $"MD11_PED_{p}_RADIO_PNL_XFER_BT",
        };
        var hardware = Crew("CPT").Concat(Crew("FO")).Concat(Crew("OBS")).ToArray();

        Assert.Equal(24, hardware.Length);
        Assert.Equal(hardware, Md11PanelLayout.Place(Md11ControlMap.Load()).Controls["Radios"]);   // what the table places
        Assert.Equal(Md11Radios.PanelKeys.Concat(hardware), Def.GetPanelControls()["Radios"]);    // what the pilot gets
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

    /// <summary>
    /// The tuning read-back speaks only a MISMATCH on a DELIVERED value — "COM 1 standby did not
    /// change, still 124.850." — and nothing at all when nothing was delivered: a verdict rests on
    /// the radio's report, never on a sleep that guessed when the 1 Hz batch would land. Outside
    /// the airband (an unpowered radio reads 0) the failure carries no "still 0.000".
    /// </summary>
    [Theory]
    [InlineData(124900, 124900.0, null)]
    [InlineData(124900, 124900.4, null)]                                          // half a kHz of float noise
    [InlineData(124900, 124850.0, "COM 1 standby did not change, still 124.850.")]
    [InlineData(124900, 0.0, "COM 1 standby did not change.")]                    // an unpowered radio: no "still 0.000"
    [InlineData(124900, null, null)]
    public void TheTuneReadBack_SpeaksOnlyAMismatch_OnADeliveredValue(double target, double? delivered, string? expected)
    {
        Assert.Equal(expected, Md11Radios.TuneReadBack(target, delivered, "COM 1 standby did not change"));
    }

    /// <summary>A flight load re-delivers only what changed, so an empty key is seeded from the cache and a seeded one is left alone.</summary>
    [Fact]
    public void SeedIfEmpty_SeedsOnlyAKeyWithNoBaseline()
    {
        var com = new Md11ComAnnouncer();
        Assert.True(com.SeedIfEmpty("COM_ACTIVE_FREQUENCY:1", 135500));
        Assert.False(com.SeedIfEmpty("COM_ACTIVE_FREQUENCY:1", 121500));   // already seeded: untouched
        Assert.Null(com.OnUpdate("COM_ACTIVE_FREQUENCY:1", 135500));         // unchanged
        Assert.NotNull(com.OnUpdate("COM_ACTIVE_FREQUENCY:1", 121500));      // the first real change speaks
    }
}
