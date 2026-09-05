using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The composition rule for an MD-11 control's spoken state (spec §3.3), in order: lit legends
/// win; else the proven latch position; else "unpowered" when the DC gate is off; else the dark
/// meaning. Pinned because every branch was reached by a live probe on 2026-09-05 — the
/// cold-and-dark cockpit reads every lamp as 0, so "dark" must never be spoken as "normal"
/// without the gate.
/// </summary>
public class Md11ControlStateTests
{
    private static Md11StateSpec ExtPower() => new()
    {
        Lamps = new()
        {
            new() { Var = "MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", Legend = "AVAIL", Lit = "Available" },
            new() { Var = "MD11_OVHD_ELEC_EXT_PWR_ON_LT", Legend = "ON", Lit = "On" },
        },
        Dark = "Not available",
    };

    private static Md11StateSpec Battery() => new()
    {
        Lamps = new() { new() { Var = "MD11_OVHD_ELEC_BATT_OFF_LT", Legend = "OFF", Lit = "Off" } },
        Latch = new() { Var = "MD11_OVHD_ELEC_BATT_BT", On = "On", Off = "Off" },
        Dark = "On",
    };

    private static Func<string, double?> Values(params (string var, double? value)[] pairs)
        => v => pairs.FirstOrDefault(p => p.var == v).value;

    [Fact]
    public void LitLegends_AreJoinedInLampOrder()
    {
        var text = Md11ControlState.Compose(ExtPower(),
            Values(("MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", 1), ("MD11_OVHD_ELEC_EXT_PWR_ON_LT", 1)), powered: true);
        Assert.Equal("Available, On", text);
    }

    [Fact]
    public void RepeatedLitWords_AreSpokenOnce()
    {
        var spec = new Md11StateSpec { Lamps = new() { new() { Var = "A", Legend = "ON", Lit = "On" }, new() { Var = "B", Legend = "PA", Lit = "On" } } };
        Assert.Equal("On", Md11ControlState.Compose(spec, Values(("A", 1), ("B", 1)), true));
    }

    [Fact]
    public void LitLegend_OutranksTheLatch()
    {
        var text = Md11ControlState.Compose(Battery(),
            Values(("MD11_OVHD_ELEC_BATT_OFF_LT", 1), ("MD11_OVHD_ELEC_BATT_BT", 1)), powered: true);
        Assert.Equal("Off", text);
    }

    [Fact]
    public void Latch_AnswersWhenEveryLegendIsDark_EvenUnpowered()
    {
        // Cold and dark: BATT_OFF_LT reads 0 because nothing powers it; the latch says Off.
        var text = Md11ControlState.Compose(Battery(),
            Values(("MD11_OVHD_ELEC_BATT_OFF_LT", 0), ("MD11_OVHD_ELEC_BATT_BT", 0)), powered: false);
        Assert.Equal("Off", text);
    }

    [Fact]
    public void Unpowered_WhenDarkAndNoLatchAndNoDcPower()
    {
        var text = Md11ControlState.Compose(ExtPower(),
            Values(("MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", 0), ("MD11_OVHD_ELEC_EXT_PWR_ON_LT", 0)), powered: false);
        Assert.Equal(Md11ControlState.Unpowered, text);
    }

    [Fact]
    public void DarkMeaning_WhenPoweredAndNothingLit()
    {
        var text = Md11ControlState.Compose(ExtPower(),
            Values(("MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", 0), ("MD11_OVHD_ELEC_EXT_PWR_ON_LT", 0)), powered: true);
        Assert.Equal("Not available", text);
    }

    [Fact]
    public void UncachedLamp_CountsAsDark()
    {
        var text = Md11ControlState.Compose(ExtPower(), Values(), powered: true);
        Assert.Equal("Not available", text);
    }

    [Fact]
    public void NoSpec_OrNothingToSay_IsNoOpinion()
    {
        Assert.Null(Md11ControlState.Compose(null, Values(), true));
        var empty = new Md11StateSpec();   // no lamps, no latch, no dark
        Assert.Null(Md11ControlState.Compose(empty, Values(), true));
    }

    [Fact]
    public void Guard_ReadsOpenOrClosedFromItsOwnVar()
    {
        var guard = new Md11StateSpec { Latch = new() { Var = "MD11_OVHD_ELEC_BATT_GRD", On = "Open", Off = "Closed" } };
        Assert.Equal("Open", Md11ControlState.Compose(guard, Values(("MD11_OVHD_ELEC_BATT_GRD", 1)), false));
        Assert.Equal("Closed", Md11ControlState.Compose(guard, Values(("MD11_OVHD_ELEC_BATT_GRD", 0)), false));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0.0, false)]
    [InlineData(19.9, false)]
    [InlineData(20.0, true)]
    [InlineData(24.0, true)]   // measured: battery or external power on
    public void IsPowered_UsesTheTwentyVoltGate(double? volts, bool expected)
    {
        Assert.Equal(expected, Md11ControlState.IsPowered(volts));
    }

    [Fact]
    public void EmbeddedMap_CarriesStateForTheBatteryAndNamesItsLamp()
    {
        var map = Md11ControlMap.Load();
        var batt = map.Controls.Single(c => c.NodeId == "MD11_OVHD_ELEC_BATT_BT");
        Assert.NotNull(batt.State);
        Assert.Equal("MD11_OVHD_ELEC_BATT_BT", batt.State!.Latch?.Var);
        Assert.Contains(batt.State.Lamps, l => l.Var == "MD11_OVHD_ELEC_BATT_OFF_LT" && l.Lit == "Off");
        var lamp = map.Controls.Single(c => c.NodeId == "MD11_OVHD_ELEC_BATT_OFF_LT");
        Assert.Equal("Battery OFF light", lamp.Label);
    }

    [Fact]
    public void EmbeddedMap_HasNoDerivedLampNamesOnTheSystemsPanels()
    {
        var map = Md11ControlMap.Load();
        var derived = map.Controls
            .Where(c => c.Kind == Md11Kinds.Annunciator && c.LabelSource == "derived"
                        && (c.Area.StartsWith("Overhead") || c.Area.StartsWith("Aft Overhead") || c.Area.StartsWith("Glareshield")))
            .Select(c => c.NodeId).ToList();
        Assert.Empty(derived);
    }

    [Fact]
    public void EmbeddedMap_HasOneLampPerVar()
    {
        var map = Md11ControlMap.Load();
        var dupes = map.Controls.Where(c => c.Kind == Md11Kinds.Annunciator)
            .GroupBy(c => c.StateVar).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }
}
