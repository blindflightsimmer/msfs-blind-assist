using System.Text.Json;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>Structural checks of the iniBuilds A321LR (MSFS 2024) definition, its generated map and its MCDU reader.</summary>
public class IniA321DefinitionTests
{
    private static readonly IniA321Definition Def = new();
    private static readonly A330ControlMap Map = A330ControlMap.A321;

    [Fact]
    public void The_generated_map_is_embedded()
        => Assert.True(Map.Controls.Count > 120, $"only {Map.Controls.Count} controls in the embedded A321 map");

    [Fact]
    public void Is_its_own_aircraft()
    {
        Assert.Equal("INI_A321", Def.AircraftCode);
        Assert.Equal("A321", Def.ShortName);
    }

    [Fact]
    public void Every_panel_control_and_display_row_is_a_registered_variable()
    {
        var vars = Def.GetVariables();
        var missing = Def.GetPanelControls().SelectMany(p => p.Value.Select(k => $"{p.Key}: {k}"))
            .Concat(Def.GetPanelDisplayVariables().SelectMany(p => p.Value.Select(k => $"{p.Key} (display): {k}")))
            .Where(s => !vars.ContainsKey(s[(s.LastIndexOf(": ") + 2)..])).ToList();
        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    [Fact]
    public void Every_panel_with_controls_is_in_the_section_tree()
    {
        var inTree = Def.GetPanelStructure().SelectMany(s => s.Value).ToHashSet();
        var orphans = Def.GetPanelControls().Keys.Concat(Def.GetPanelDisplayVariables().Keys).Where(p => !inTree.Contains(p)).Distinct().ToList();
        Assert.True(orphans.Count == 0, "panels not in any section: " + string.Join(", ", orphans));
    }

    [Fact]
    public void No_panel_has_two_rows_with_the_same_spoken_name()
    {
        var vars = Def.GetVariables();
        var dups = Def.GetPanelControls().Select(p => (panel: p.Key, keys: p.Value.Concat(
                Def.GetPanelDisplayVariables().TryGetValue(p.Key, out var d) ? d : new List<string>())))
            .SelectMany(p => p.keys.GroupBy(k => vars[k].DisplayName).Where(g => g.Count() > 1)
                .Select(g => $"{p.panel}: '{g.Key}' x{g.Count()}")).ToList();
        Assert.True(dups.Count == 0, string.Join("\n", dups));
    }

    [Fact]
    public void No_control_renders_as_a_text_entry()
        => Assert.DoesNotContain(Def.GetPanelControls().SelectMany(p => p.Value), k => k.Contains("_SET"));

    [Fact]
    public void Every_map_write_is_a_direct_L_var_write_or_a_toggling_press()
    {
        // the A321's input events do nothing from outside (probed): every control must write an L:var
        var bad = Map.Controls.Where(c => c.Kind != "status" &&
                !(c.Mode is "rpn" or "toggle" && c.Rpn is { Length: > 0 } r && r.Contains("(>L:")))
            .Select(c => $"{c.Key} ({c.Mode})").ToList();
        Assert.True(bad.Count == 0, string.Join(", ", bad));
    }

    [Fact]
    public void A_selector_write_carries_the_picked_value()
    {
        var bat = Map.Controls.Single(c => c.Key == "A321_BAT1");
        Assert.Equal("{value} (>L:INI_OVHD_ELEC_BAT_1_PB_IS_AUTO_SWITCH)", bat.Rpn);
        Assert.Contains(bat.Positions!, p => p.Value == 1 && p.Label == "Auto");
    }

    [Fact]
    public void The_FCU_has_EXPED_and_no_ALT_button()
    {
        var keys = Def.GetPanelControls()["Autopilot"];
        Assert.Contains(IniA330Definition.ExpedButtonKey, keys);
        Assert.DoesNotContain(IniA330Definition.AltButtonKey, keys);
        Assert.DoesNotContain("A330_ALT_STEP", keys);   // the 100/1000 selector cannot be worked from outside
    }

    [Fact]
    public void A330_event_names_translate()
    {
        Assert.Equal("LANDING_GEAR_PARKINGBRAKE", Def.Ev("AIRLINER_PARKINGBRAKE"));
        Assert.Null(Def.Ev("AIRLINER_MCU_ALTSELECT"));
        Assert.Equal(100, Def.ParkingBrakeInput(true));
        Assert.Equal(0, Def.ParkingBrakeInput(false));
    }

    [Fact]
    public void Characteristic_speeds_and_tanks_are_the_A321s()
    {
        var vars = Def.GetVariables();
        Assert.Equal("FMGEC_GD_SPD", vars["A330_GREEN_DOT"].Name);
        Assert.Equal("FCPC_S_SPEED", vars["A330_S_SPEED"].Name);
        Assert.Equal("INI_FUEL_WEIGHT_CENTER", vars["A330_FUEL_CTR_KG"].Name);
        Assert.False(Def.CentreTankFromSim);
    }

    [Fact]
    public void The_A330_definition_is_unchanged_by_the_hooks()
    {
        var a330 = new IniA330Definition();
        Assert.Equal("AIRLINER_PARKINGBRAKE", a330.Ev("AIRLINER_PARKINGBRAKE"));
        Assert.Contains(IniA330Definition.AltButtonKey, a330.GetPanelControls()["Autopilot"]);
        Assert.Contains("A330_ALT_STEP", a330.GetPanelControls()["Autopilot"]);
        Assert.True(a330.CentreTankFromSim);
    }
}

/// <summary>
/// The A321 MCDU pixel reader against captures that were NEVER used to train its glyph library (taken
/// after the training set, so the fuel, weight and accuracy figures differ from every trained page).
/// Every line must match exactly, characters and word breaks.
/// </summary>
public class A321CduScreenReaderTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "A321Cdu");

    public static IEnumerable<object[]> Pages()
    {
        var truth = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDir, "truth.json")));
        foreach (var p in truth.RootElement.EnumerateObject())
            yield return new object[] { p.Name };
    }

    private static string Collapse(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [Theory]
    [MemberData(nameof(Pages))]
    public void Reads_every_line_of_an_unseen_page_exactly(string page)
    {
        var lib = A300CduGlyphLibrary.A321;
        Assert.NotNull(lib);
        var truth = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDir, "truth.json"))).RootElement.GetProperty(page);
        var frame = A300CduFrame.FromFile(Path.Combine(FixtureDir, page + ".png"));
        var lines = new A300CduScreenReader(lib!, CduLayout.A321).Read(frame.Lum, frame.Width, frame.Height, frame.Rgb);
        var errors = new List<string>();
        for (int r = 0; r < A300CduScreenReader.Rows; r++)
        {
            string expected = truth.TryGetProperty(r.ToString(), out var e) ? e.GetString()! : "";
            string got = Collapse(lines[r].Text);
            if (got != Collapse(expected)) errors.Add($"row {r}: expected '{expected}' got '{got}'");
        }
        Assert.True(errors.Count == 0, $"{page}:\n" + string.Join("\n", errors));
    }
}
