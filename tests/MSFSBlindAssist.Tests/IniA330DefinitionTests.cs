using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Forms.IniA300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>Structural checks of the iniBuilds A330 (MSFS 2024) definition and its generated map.</summary>
public class IniA330DefinitionTests
{
    private static readonly IniA330Definition Def = new();
    private static readonly A330ControlMap Map = A330ControlMap.Embedded;

    [Fact]
    public void The_generated_map_is_embedded()
        => Assert.True(Map.Controls.Count > 200, $"only {Map.Controls.Count} controls in the embedded map");

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
    {
        // MainForm renders any key containing _SET as a TextBox + Set button
        var setKeys = Def.GetPanelControls().SelectMany(p => p.Value).Where(k => k.Contains("_SET")).ToList();
        Assert.Empty(setKeys);
    }

    [Fact]
    public void Monitored_variable_count_stays_inside_the_data_definition_budget()
    {
        int continuous = Def.GetVariables().Values.Count(v => v.UpdateFrequency == UpdateFrequency.Continuous && v.Type != SimVarType.InputEvent);
        Assert.InRange(continuous, 100, 850);
    }

    [Fact]
    public void Registers_the_bridge_probe_so_calculator_writes_can_be_verified()
        => Assert.True(Def.GetVariables().ContainsKey("MSFSBA_BRIDGE_PROBE"));

    [Fact]
    public void Switches_read_from_their_own_input_event_are_InputEvent_variables()
    {
        var vars = Def.GetVariables();
        Assert.Equal(SimVarType.InputEvent, vars["A330_PROBE_HEAT"].Type);
        Assert.Equal("AIRLINER_PROBE_HEAT", vars["A330_PROBE_HEAT"].Name);
        Assert.Equal(SimVarType.LVar, vars["A330_PACK1"].Type);
        Assert.Equal("INI_PACK1_BUTTON", vars["A330_PACK1"].Name);
    }

    [Fact]
    public void Every_map_control_has_an_input_event_and_every_select_has_two_positions()
    {
        var bad = Map.Controls.Where(c => c.Kind != "status" && string.IsNullOrEmpty(c.Event)).Select(c => c.Key)
            .Concat(Map.Controls.Where(c => c.Kind == "select" && (c.Positions?.Count ?? 0) < 2).Select(c => c.Key + " (positions)"))
            .ToList();
        Assert.True(bad.Count == 0, string.Join(", ", bad));
    }

    [Fact]
    public void Momentary_keys_use_their_push_action_and_switches_set_a_value()
    {
        Assert.Equal("push", Map.Controls.Single(c => c.Key == "A330_ECAM_HYD").Mode);
        Assert.Equal("set", Map.Controls.Single(c => c.Key == "A330_STROBE").Mode);
    }

    [Theory]
    // a push-button: read 1 (on) is input 0 — measured on PACK 1
    [InlineData("A330_PACK1", 1, 0)]
    [InlineData("A330_PACK1", 0, 1)]
    // three-position switch numbered from the top: strobe ON is 0
    [InlineData("A330_STROBE", 0, 0)]
    [InlineData("A330_STROBE", 2, 2)]
    public void Picks_translate_to_the_measured_input_value(string key, double read, double input)
        => Assert.Equal(input, A330ControlMap.InputFor(Map.Controls.Single(c => c.Key == key), read));

    [Fact]
    public void Read_kinds_parse()
    {
        Assert.Equal(A330ControlMap.ReadKind.InputEvent, A330ControlMap.ParseReadKind("B:AIRLINER_X").kind);
        Assert.Equal(A330ControlMap.ReadKind.LVar, A330ControlMap.ParseReadKind("INI_X").kind);
        var sv = A330ControlMap.ParseReadKind("A:GENERAL ENG FUEL VALVE:1, bool");
        Assert.Equal((A330ControlMap.ReadKind.SimVar, "GENERAL ENG FUEL VALVE:1", "bool"), sv);
    }

    [Fact]
    public void Autobrake_levels_map_to_their_buttons()
    {
        Assert.Equal("AIRLINER_AUTOBRK_LO", IniA330Definition.AutobrakeEvent(1));
        Assert.Equal("AIRLINER_AUTOBRK_HI", IniA330Definition.AutobrakeEvent(3));
        Assert.Null(IniA330Definition.AutobrakeEvent(0));
    }

    [Theory]
    [InlineData(1.7, "1.7 up")]
    [InlineData(-0.4, "0.4 down")]
    [InlineData(0.01, "0.0")]
    public void Trim_reads_up_and_down(double deg, string text) => Assert.Equal(text, IniA330Definition.FormatTrim(deg));
}

public class A330FmaTests
{
    private static A330FmaText C(int at = 0, int pitch = 0, int pArm = 0, int roll = 0, int rArm = 0, int appr = 0,
        bool mach = false, bool trk = false, bool lockThr = false, int cap = 0, bool climbing = true)
        => A330Fma.Compose(new A330FmaInputs(at, pitch, pArm, 0, roll, rArm, appr, mach, trk, lockThr, cap, climbing));

    [Fact]
    public void Heading_pull_reads_HDG_and_TRACK_in_TRK_FPA()
    {
        Assert.Equal("HDG", C(roll: 2).Lateral);
        Assert.Equal("TRACK", C(roll: 2, trk: true).Lateral);
        Assert.Equal("TRACK", C(roll: 11).Lateral);
    }

    [Fact]
    public void Open_mode_says_climb_or_descent_from_the_FCU_altitude()
    {
        Assert.Equal("OP CLB", C(pitch: 2, climbing: true).Vertical);
        Assert.Equal("OP DES", C(pitch: 2, climbing: false).Vertical);
    }

    [Fact]
    public void VS_reads_FPA_in_TRK_FPA()
    {
        Assert.Equal("V S", C(pitch: 8).Vertical);
        Assert.Equal("FPA", C(pitch: 8, trk: true).Vertical);
    }

    [Fact]
    public void ALT_armed_shows_beside_an_open_climb()
        => Assert.Equal("ALT", C(pitch: 2, pArm: 6).Armed);

    [Fact]
    public void An_unknown_mode_is_spoken_as_its_number_never_guessed()
    {
        Assert.Equal("vertical mode 17", C(pitch: 17).Vertical);
        Assert.Equal("lateral mode 9", C(roll: 9).Lateral);
    }

    [Fact]
    public void Land_replaces_both_columns_and_shows_the_capability()
    {
        var f = C(appr: 1, roll: 5, pitch: 7, cap: 3);
        Assert.Equal("LAND", f.Vertical);
        Assert.Equal("", f.Lateral);
        Assert.Equal("CAT 3", f.Capability);
    }

    [Fact]
    public void Nothing_engaged_says_so() => Assert.Equal("No flight modes", C().Summary());
}

public class CduColourTests
{
    [Theory]
    [InlineData(1, 1, 1, 220, "white")]
    [InlineData(1, 1, 1, 77, "grey")]
    [InlineData(1.55, 1.15, 0.30, 200, "amber")]
    [InlineData(0.55, 1.45, 1.0, 230, "cyan")]
    [InlineData(0.6, 1.7, 0.7, 200, "green")]
    [InlineData(1.45, 0.45, 1.1, 210, "magenta")]
    public void Classifies_the_MCDU_colours(double r, double g, double b, double peak, string colour)
        => Assert.Equal(colour, CduColour.Classify(r, g, b, peak));

    [Theory]
    // the app normalises glyphs with the layout's letter height and the trainer with BASE_CAP;
    // 18.5 against 18.3 scaled every glyph 1 % off its templates and read "<TO DATA" as "<TO OATA"
    [InlineData("a330-cdu-ocr", nameof(CduLayout.A330))]
    [InlineData("a321-cdu-ocr", nameof(CduLayout.A321))]
    public void Layout_letter_height_matches_the_trainer(string tool, string layout)
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "tools", tool, "ocr2.py"));
        var m = System.Text.RegularExpressions.Regex.Match(src, @"^BASE_CAP\s*=\s*([0-9.]+)", System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(m.Success, "BASE_CAP not found in " + tool);
        var l = layout == nameof(CduLayout.A330) ? CduLayout.A330 : CduLayout.A321;
        Assert.Equal(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), l.BaseCap, 3);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Grey_words_are_marked_unavailable_once_per_run()
    {
        var text = "<FM1 NAV B/UP>";
        var colours = text.Select((ch, i) => ch == ' ' ? "" : i < 4 ? CduColour.Green : CduColour.Grey).ToArray();
        Assert.Equal("<FM1 NAV B/UP> (unavailable)", CduLineText.Annotate(text, colours));
    }

    [Fact]
    public void A_line_without_grey_is_unchanged()
    {
        var text = "<MONITOR";
        Assert.Equal(text, CduLineText.Annotate(text, text.Select(_ => CduColour.White).ToArray()));
    }

    [Fact]
    public void The_A330_profile_types_punctuation_with_its_own_key_names()
    {
        Assert.Equal("DECIMAL", CduFormProfile.A330KeyForChar('.'));
        Assert.Equal("PLUS", CduFormProfile.A330KeyForChar('-'));
        Assert.Equal("SPACE", CduFormProfile.A330KeyForChar(' '));
        Assert.Equal("Q", CduFormProfile.A330KeyForChar('Q'));
    }
}
