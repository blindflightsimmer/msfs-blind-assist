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
    public void GlareshieldPanels_NameTheTwoWarningPanels_NotGroundService()
    {
        // A deviation from the design spec's panel list, and a deliberate one: GSL/GSR are the
        // glareshield master warning and caution, not a ground-service panel, so the spec's
        // "Ground Service" was dropped rather than filled with something else.
        Assert.Equal(new[] { "Flight Control Panel", "EFIS Captain", "EFIS First Officer",
                             "Warnings Captain", "Warnings First Officer" },
                     P.Structure["Glareshield"].ToArray());
        Assert.Equal("MD11_GSL_MST_WRN_BT", P.Controls["Warnings Captain"][0]);
        Assert.Equal("MD11_GSR_MST_WRN_BT", P.Controls["Warnings First Officer"][0]);
    }

    [Fact]
    public void CircuitBreakers_AreFourPanels_UpperFirst()
    {
        // The spec named two ("Lower Panel, Upper Panel"); the aircraft has four banks, and the
        // upper one leads because that is the order they sit in the cockpit.
        Assert.Equal(new[] { "Circuit Breakers Upper", "Circuit Breakers Lower",
                             "Circuit Breakers Left Aft", "Circuit Breakers Overhead" },
                     P.Structure["Circuit Breakers"].ToArray());
    }

    [Fact]
    public void CircuitBreakers_AreInGridOrder()
    {
        var upper = P.Controls["Circuit Breakers Upper"];
        Assert.Equal("MD11_BKR_BWU_A24", upper[0]);
        Assert.Equal("MD11_BKR_BWU_A25", upper[1]);
        Assert.Equal("MD11_BKR_BWU_B21", upper[2]);
    }

    /// <summary>
    /// The safety net the shipped map never exercises: a control the table does not name is
    /// APPENDED to a "{Area} (other)" panel in the section its area maps to, never dropped, and a
    /// key the table names that the map does not have is reported rather than silently skipped.
    /// <see cref="EveryOperableControl_IsPlacedExactlyOnce_AndNoFallbackPanelIsNeeded"/> pins that
    /// neither happens today, which is exactly why the path itself needs its own test.
    /// </summary>
    [Fact]
    public void Place_AppendsAnUnlistedControl_AndReportsAKeyTheMapLacks()
    {
        var map = new Md11ControlMap
        {
            Controls =
            {
                // Named by the table, so it lands in its panel as usual.
                new Md11Control { NodeId = "MD11_OVHD_ELEC_BATT_BT", Kind = Md11Kinds.Button, Area = "Overhead" },
                // Not named anywhere: the fallback must catch it.
                new Md11Control { NodeId = "MD11_MADE_UP_SW", Kind = Md11Kinds.Switch, Area = "Pedestal" },
                // Options are placed by nobody, fallback included.
                new Md11Control { NodeId = "MD11_OPT_MADE_UP", Kind = Md11Kinds.Option, Area = "Aircraft Options" },
            },
        };

        var p = Md11PanelLayout.Place(map);

        Assert.Equal(new[] { "MD11_MADE_UP_SW" }, p.Unplaced.ToArray());
        Assert.Equal(new[] { "MD11_MADE_UP_SW" }, p.Controls["Pedestal (other)"].ToArray());
        Assert.Contains("Pedestal (other)", p.Structure["Pedestal"]);
        Assert.Equal(new[] { "MD11_OVHD_ELEC_BATT_BT" }, p.Controls["Electrical"].ToArray());
        Assert.DoesNotContain("MD11_OPT_MADE_UP", p.Controls.Values.SelectMany(v => v));
        Assert.DoesNotContain("MD11_OPT_MADE_UP", p.Unplaced);
        // Every other key the table names is missing from this two-control map, and all of them
        // are reported — that list is what turns a typo in the table into a failing test.
        Assert.Contains("MD11_OVHD_ELEC_BATT_GRD", p.MissingKeys);
    }

    [Fact]
    public void SectionForArea_FallsBackToOther_ForAnAreaTheTableDoesNotKnow()
    {
        Assert.Equal("Instrument Panel", Md11PanelLayout.SectionForArea("F/O Side Panel"));
        Assert.Equal("Glareshield", Md11PanelLayout.SectionForArea("Captain EFIS Control Panel"));
        Assert.Equal("Other", Md11PanelLayout.SectionForArea("Somewhere TFDi Added Later"));
    }

    [Fact]
    public void EachWindowShade_SitsOnItsOwnPilotsPanel()
    {
        // "Mirror_l_window_shade_pull" is a modelling-mirror name, not a left-hand one: TFDi
        // declare it in FOAux_Light.xml as MD11_RSIDE_WINDOW_SHADE. On the Captain panel it had a
        // pilot pulling the RIGHT shade, and the F/O panel had no shade at all.
        Assert.Contains("l_window_shade_pull", P.Controls["Captain Side"]);
        Assert.DoesNotContain("Mirror_l_window_shade_pull", P.Controls["Captain Side"]);
        var fo = P.Controls["First Officer Side"];
        Assert.Equal("Mirror_l_window_shade_pull", fo[fo.IndexOf("MD11_RSIDE_WINDOW") + 1]);
    }

    [Fact]
    public void McduKeys_StartWithTheLineSelectKeys()
    {
        Assert.Equal("MD11_LMCDU_LSK_1L_BT", P.Controls["MCDU Left"][0]);
        Assert.Equal("MD11_CMCDU_LSK_1L_BT", P.Controls["MCDU Center"][0]);
        Assert.Contains("MD11_RMCDU_Z_BT", P.Controls["MCDU Right"]);
    }
}
