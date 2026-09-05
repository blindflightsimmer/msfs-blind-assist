using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11 panel tree is a curated table in preparation-flow order (spec §3.8). These pin the
/// properties a blind pilot navigates by: nothing is lost, nothing repeats, guards sit before the
/// control they cover, status rows come last, and every spoken row name in a panel is unique.
/// </summary>
public class Md11PanelLayoutTests
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();
    private static readonly Md11Placement P = Md11PanelLayout.Place(Map);

    [Fact]
    public void EveryOperableControl_IsPlacedExactlyOnce_AndNoFallbackPanelIsNeeded()
    {
        Assert.Empty(P.Unplaced);
        Assert.Empty(P.MissingKeys);   // a typo in the table would land here
        var all = P.Controls.Values.SelectMany(k => k).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var operable = Map.Controls.Where(c => c.Kind != Md11Kinds.Annunciator && c.Kind != Md11Kinds.Option).Select(c => c.NodeId);
        foreach (var key in operable) Assert.Contains(key, all);
        Assert.DoesNotContain(P.Structure.Values.SelectMany(n => n), n => n.EndsWith("(other)"));
    }

    [Fact]
    public void Sections_AreInPreparationOrder()
    {
        Assert.Equal(new[] { "Overhead", "Aft Overhead", "Glareshield", "Instrument Panel", "Pedestal",
                             "Ground and Exterior", "MCDU Keys", "Circuit Breakers" },
                     P.Structure.Keys.ToArray());
    }

    [Fact]
    public void OverheadPanels_FollowTheChecklist()
    {
        Assert.Equal(new[] { "Electrical", "IRS", "Fuel", "Hydraulic", "Air", "Cabin Pressurization", "Anti-Ice",
                             "Engines and Ignition", "Flight Controls", "Lights and Signs", "Cockpit Lights",
                             "Windshield Wipers", "Miscellaneous" },
                     P.Structure["Overhead"].ToArray());
        Assert.Equal("MD11_OVHD_ELEC_BATT_GRD", P.Controls["Electrical"][0]);
        Assert.Equal("MD11_OVHD_ELEC_BATT_BT", P.Controls["Electrical"][1]);
    }

    [Fact]
    public void GuardCovers_ImmediatelyPrecedeTheControlTheyCover()
    {
        foreach (var c in Map.Controls.Where(c => !string.IsNullOrEmpty(c.GuardId)))
        {
            var panel = P.Controls.Single(kv => kv.Value.Contains(c.NodeId));
            int i = panel.Value.IndexOf(c.NodeId);
            Assert.True(i > 0 && panel.Value[i - 1] == c.GuardId, $"{c.GuardId} should sit right before {c.NodeId} in {panel.Key}");
        }
    }

    [Fact]
    public void StatusRows_ComeLastInEveryPanel()
    {
        var byId = Map.Controls.ToDictionary(c => c.NodeId);
        foreach (var (panel, keys) in P.Controls)
        {
            bool seenLamp = false;
            foreach (var key in keys)
            {
                bool isLamp = byId[key].Kind == Md11Kinds.Annunciator;
                Assert.False(seenLamp && !isLamp, $"{panel}: {key} follows a status row");
                seenLamp |= isLamp;
            }
        }
    }

    [Fact]
    public void NoTwoRowsInOnePanel_ShareASpokenName()
    {
        var vars = new TFDiMD11Definition().GetVariables();
        foreach (var (panel, keys) in P.Controls)
        {
            var names = keys.Where(vars.ContainsKey).Select(k => vars[k].DisplayName).ToList();
            var dupes = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(dupes.Count == 0, $"{panel}: duplicate spoken names {string.Join(", ", dupes)}");
        }
    }

    [Fact]
    public void PanelNames_AreUniqueAcrossSections()
    {
        var names = P.Structure.Values.SelectMany(n => n).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Definition_ExposesTheLayoutPlusReadouts()
    {
        var def = new TFDiMD11Definition();
        var structure = def.GetPanelStructure();
        Assert.Equal("Overhead", structure.Keys.First());
        Assert.Equal("Read-outs", structure.Keys.Last());
        Assert.Contains("V-Speeds", structure["Read-outs"]);
        Assert.Equal(P.Controls["Electrical"], def.GetPanelControls()["Electrical"]);
    }

    [Fact]
    public void CircuitBreakers_AreInGridOrder()
    {
        var upper = P.Controls["Circuit Breakers Upper"];
        Assert.Equal("MD11_BKR_BWU_A24", upper[0]);
        Assert.Equal("MD11_BKR_BWU_A25", upper[1]);
        Assert.Equal("MD11_BKR_BWU_B21", upper[2]);
    }

    [Fact]
    public void McduKeys_StartWithTheLineSelectKeys()
    {
        Assert.Equal("MD11_LMCDU_LSK_1L_BT", P.Controls["MCDU Left"][0]);
        Assert.Equal("MD11_CMCDU_LSK_1L_BT", P.Controls["MCDU Center"][0]);
        Assert.Contains("MD11_RMCDU_Z_BT", P.Controls["MCDU Right"]);
    }
}
