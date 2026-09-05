using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins what the MCDU window TELLS a pilot when a unit has nothing on it.
///
/// Live evidence this exists to cover (2026-09-05, MD-11F GE, build 4d09b482): across three
/// sessions the LEFT unit's export carried zero glyphs twice while Center and Right carried real
/// pages —
///
///   16:19  Left 204 glyphs 'A/C STATUS'   Center 102 'MENU'   Right 204 'A/C STATUS'
///   17:13  Left   0 glyphs ''             Center 102 'MENU'   Right 204 'A/C STATUS'
///   17:43  Left   0 glyphs ''             Center 102 'MENU'   Right 102 'MENU'
///
/// The window opens on Left. An all-zero screen rendered as "Title:", "1:" … "6:", "Scratchpad:"
/// with the status line reading "MCDU: connected" and NOTHING announced — eight blank rows and a
/// claim that all is well. To a blind pilot that is indistinguishable from a broken window, which
/// is exactly how it was reported: "my MCDU is not showing up in the app".
///
/// So a blank unit must SAY it is blank, and must name the units that do have something, because
/// the alternative — arrowing through eight empty rows — carries no information at all.
///
/// NOTE what this does NOT claim: it does not say WHY Left was empty. A genuinely unpowered CDU
/// and a delivery this app never received look identical here, and the app could not tell them
/// apart because it logged only the FIRST delivery per unit. Md11McduDataManager now logs every
/// blank/content transition; until a session with that build exists, attribution is open.
/// </summary>
public class Md11McduPresenceTests
{
    private static Md11McduScreen Screen(Md11McduUnit unit, params string[] rows)
    {
        var lines = new string[Md11McduLayout.Rows];
        for (var i = 0; i < lines.Length; i++) lines[i] = i < rows.Length ? rows[i] : string.Empty;
        return new Md11McduScreen { Unit = unit, Lines = lines };
    }

    [Fact]
    public void A_screen_that_never_arrived_is_NoData_not_Blank()
    {
        // The two are different facts and must stay different: "nothing has been delivered" is a
        // transport statement, "the unit is dark" is an aircraft statement.
        Assert.Equal(Md11McduPresenceState.NoData, Md11McduPresence.Classify(null));
    }

    [Fact]
    public void An_all_zero_screen_is_Blank()
    {
        Assert.Equal(Md11McduPresenceState.Blank, Md11McduPresence.Classify(Screen(Md11McduUnit.Left)));
    }

    [Fact]
    public void A_screen_of_only_spaces_is_Blank()
    {
        // Decode turns an empty cell (value 0) into a SPACE, so "blank" reaches us as whitespace,
        // never as an empty string. Testing IsNullOrEmpty here would classify the live 0-glyph
        // Left screen as content and defeat the whole check.
        var spaces = new string(' ', Md11McduLayout.Cols);
        Assert.Equal(Md11McduPresenceState.Blank,
            Md11McduPresence.Classify(Screen(Md11McduUnit.Left, spaces, spaces, spaces)));
    }

    [Fact]
    public void One_glyph_anywhere_is_Content()
    {
        Assert.Equal(Md11McduPresenceState.Content,
            Md11McduPresence.Classify(Screen(Md11McduUnit.Left, "", "", "          MENU")));
    }

    [Fact]
    public void A_blank_unit_names_itself_and_the_units_that_have_content()
    {
        // The live 17:13 shape: Left blank, the other two carrying pages one keystroke away.
        var rows = Md11McduPresence.Describe(
            Md11McduUnit.Left,
            Md11McduPresenceState.Blank,
            new[] { Md11McduUnit.Center, Md11McduUnit.Right });

        var text = string.Join(" ", rows);
        Assert.Contains("Left", text);
        Assert.Contains("blank", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Center", text);
        Assert.Contains("Right", text);
        // The chord is the whole point of naming them - saying "Center has content" without
        // saying how to get there leaves the pilot exactly where they were.
        Assert.Contains("Ctrl+Shift+C", text);
        Assert.Contains("Ctrl+Shift+R", text);
    }

    [Fact]
    public void A_blank_unit_with_no_other_content_does_not_invent_somewhere_to_go()
    {
        var text = string.Join(" ", Md11McduPresence.Describe(
            Md11McduUnit.Left, Md11McduPresenceState.Blank, Array.Empty<Md11McduUnit>()));

        Assert.Contains("blank", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ctrl+Shift+", text);
    }

    [Fact]
    public void A_blank_unit_never_offers_a_jump_back_to_itself()
    {
        var text = string.Join(" ", Md11McduPresence.Describe(
            Md11McduUnit.Left, Md11McduPresenceState.Blank,
            new[] { Md11McduUnit.Left, Md11McduUnit.Right }));

        Assert.DoesNotContain("Ctrl+Shift+L", text);
        Assert.Contains("Ctrl+Shift+R", text);
    }

    [Fact]
    public void NoData_says_nothing_has_arrived_rather_than_claiming_the_unit_is_dark()
    {
        var text = string.Join(" ", Md11McduPresence.Describe(
            Md11McduUnit.Center, Md11McduPresenceState.NoData, Array.Empty<Md11McduUnit>()));

        Assert.Contains("Center", text);
        Assert.DoesNotContain("blank", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Content_produces_no_advisory_rows_at_all()
    {
        // The normal path must stay untouched - a working CDU gains no extra line to arrow past.
        Assert.Empty(Md11McduPresence.Describe(
            Md11McduUnit.Right, Md11McduPresenceState.Content, new[] { Md11McduUnit.Right }));
    }
}
