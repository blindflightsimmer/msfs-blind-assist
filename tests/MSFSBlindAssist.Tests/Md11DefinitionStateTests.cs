using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// How the MD-11 definition registers its controls for composed state (spec §3.5): which
/// buttons are read, which stay write-only, what a lamp is called, and that the whole thing
/// stays inside SimConnect's data-definition budget.
/// </summary>
public class Md11DefinitionStateTests
{
    private static readonly TFDiMD11Definition Def = new();
    private static Dictionary<string, SimVarDefinition> Vars => Def.GetVariables();

    [Fact]
    public void Battery_IsReadOnRequest_AndDependsOnItsLampLatchAndPower()
    {
        var d = Vars["MD11_OVHD_ELEC_BATT_BT"];
        Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
        Assert.True(d.RenderAsButton);
        Assert.NotNull(d.StateVariables);
        Assert.Contains("MD11_OVHD_ELEC_BATT_OFF_LT", d.StateVariables!);
        Assert.Contains("MD11_OVHD_ELEC_BATT_BT", d.StateVariables!);
        Assert.Contains(TFDiMD11Definition.DcPowerKey, d.StateVariables!);
    }

    [Fact]
    public void ExternalPower_StaysWriteOnly_ButDependsOnItsLamps()
    {
        var d = Vars["MD11_OVHD_ELEC_EXT_PWR_BT"];
        Assert.Equal(UpdateFrequency.Never, d.UpdateFrequency);
        Assert.Contains("MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", d.StateVariables!);
        Assert.Contains("MD11_OVHD_ELEC_EXT_PWR_ON_LT", d.StateVariables!);
    }

    [Fact]
    public void MomentaryButton_HasNoDependencies()
    {
        Assert.Null(Vars["MD11_OVHD_ANNUNLT_TEST_BT"].StateVariables);
        Assert.Equal(UpdateFrequency.Never, Vars["MD11_OVHD_ANNUNLT_TEST_BT"].UpdateFrequency);
    }

    [Fact]
    public void Lamp_SpeaksItsSystemName_WithLitAndDarkWords()
    {
        var d = Vars["MD11_OVHD_ELEC_AC1_OFF_LT"];
        Assert.Equal("AC Bus 1", d.DisplayName);
        Assert.Equal("Off", d.ValueDescriptions[1]);
        Assert.Equal("Powered", d.ValueDescriptions[0]);
        Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
        Assert.True(d.IsAnnounced);
        Assert.True(d.RenderAsReadOnlyStatus);
        Assert.Contains(TFDiMD11Definition.DcPowerKey, d.StateVariables!);
    }

    [Fact]
    public void PairedLamp_IsNamedFromItsOwnerAndLegend()
    {
        Assert.Equal("External Power AVAIL light", Vars["MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT"].DisplayName);
    }

    [Fact]
    public void Guard_IsReadOnRequest_RenderedAsButton_AndKnowsItself()
    {
        var d = Vars["MD11_OVHD_ELEC_BATT_GRD"];
        Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
        Assert.True(d.RenderAsButton);
        Assert.Equal("Battery guard", d.DisplayName);
        Assert.Contains("MD11_OVHD_ELEC_BATT_GRD", d.StateVariables!);
    }

    [Fact]
    public void OptionFlags_AreNotRegistered()
    {
        Assert.DoesNotContain("MD11_OPT_EFB", Vars.Keys);
        Assert.DoesNotContain("MD11_OPT_ISFD", Vars.Keys);
    }

    [Fact]
    public void DcPowerGate_IsAContinuousStockSimVar_KeptOffCtrlM()
    {
        var d = Vars[TFDiMD11Definition.DcPowerKey];
        Assert.Equal("ELECTRICAL MAIN BUS VOLTAGE", d.Name);
        Assert.Equal(SimVarType.SimVar, d.Type);
        Assert.Equal("Volts", d.Units);
        Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
        Assert.True(d.IsAnnounced);
        Assert.True(d.ExcludeFromMonitorManager);
    }

    [Fact]
    public void BatchCoveredNames_AreUnique()
    {
        // Two batch entries with one Name shift every later slot (VarNameCollision invariant).
        var dupes = Vars.Values
            .Where(v => v.UpdateFrequency == UpdateFrequency.Continuous && v.IsAnnounced && !v.ExcludeFromBatch)
            .GroupBy(v => v.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }

    [Fact]
    public void IndividualDefinitions_StayFarInsideTheBudget()
    {
        int individual = Vars.Values.Count(v =>
            v.UpdateFrequency == UpdateFrequency.OnRequest ||
            (v.UpdateFrequency == UpdateFrequency.Continuous && (!v.IsAnnounced || v.ExcludeFromBatch)));
        Assert.InRange(individual, 1, 500);   // cap is 900; ~206 before this work, ~170 latches added
    }

    [Fact]
    public void BeforeAttach_TheHookHasNoOpinion()
    {
        Assert.False(new TFDiMD11Definition().TryDescribeControlState("MD11_OVHD_ELEC_BATT_BT", out _));
    }
}
