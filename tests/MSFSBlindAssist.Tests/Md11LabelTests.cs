using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Spoken-label defects that shipped in the first control map and must not return after a
/// regeneration: raw HTML entities, a parenthesis cut in half, "button" suffixes, guards that
/// share their control's name, breakers without their grid position, trailing " Breaker" suffix
/// not stripped.
/// </summary>
public class Md11LabelTests
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();
    private static IEnumerable<Md11Control> Spoken => Map.Controls.Where(c => c.Kind != Md11Kinds.Option);

    [Fact]
    public void NoLabel_CarriesAnHtmlEntity()
    {
        Assert.Empty(Spoken.Where(c => c.DisplayLabel.Contains('&') && c.DisplayLabel.Contains(';')).Select(c => c.NodeId));
    }

    [Fact]
    public void EveryLabel_HasBalancedParentheses()
    {
        Assert.Empty(Spoken.Where(c => c.DisplayLabel.Count(ch => ch == '(') != c.DisplayLabel.Count(ch => ch == ')')).Select(c => c.NodeId));
    }

    [Fact]
    public void NoDerivedLabel_EndsWithTheWordButton()
    {
        Assert.Empty(Spoken.Where(c => c.DisplayLabel.EndsWith(" button", StringComparison.OrdinalIgnoreCase)).Select(c => c.NodeId));
    }

    [Fact]
    public void EveryGuard_IsNamedAfterItsControl_AndNeverCollidesWithIt()
    {
        foreach (var g in Map.Controls.Where(c => c.Kind == Md11Kinds.Guard))
        {
            Assert.EndsWith(" guard", g.DisplayLabel);
            var covered = Map.Controls.FirstOrDefault(c => c.GuardId == g.NodeId);
            if (covered != null) Assert.Equal($"{covered.DisplayLabel} guard", g.DisplayLabel);
        }
    }

    [Fact]
    public void EveryBreaker_StartsWithItsGridPosition()
    {
        foreach (var b in Map.Controls.Where(c => c.NodeId.StartsWith("MD11_BKR_")))
        {
            var grid = b.NodeId.Split('_')[^1];
            Assert.StartsWith(grid + " ", b.DisplayLabel);
            Assert.False(b.DisplayLabel.EndsWith(" Breaker", StringComparison.Ordinal),
                $"{b.NodeId}: the trailing word Breaker must be dropped ({b.DisplayLabel})");
        }
    }

    [Fact]
    public void TheFourTruncatedTooltips_AreWhole()
    {
        var def = new TFDiMD11Definition().GetVariables();
        Assert.Equal("APU Generator (APU Panel)", def["MD11_AOVHD_APU_GEN_BT"].DisplayName);
        Assert.Equal("APU Start (APU Panel)", def["MD11_AOVHD_APU_START_BT"].DisplayName);
        Assert.Equal("High Intensity Lights (Strobes)", def["MD11_OVHD_LTS_HI_INT_BT"].DisplayName);
        Assert.Equal("Elevator Trim (Long Trim)", def["MD11_THR_LONG_TRIM_SW"].DisplayName);
        Assert.Equal("Air System 1 to 2 Isolation Valve", def["MD11_OVHD_PNEU_1_2_ISOL_BT"].DisplayName);
    }
}
