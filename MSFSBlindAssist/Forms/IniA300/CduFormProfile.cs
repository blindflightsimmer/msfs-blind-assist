using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Forms.IniA300;

/// <summary>
/// Everything that differs between the iniBuilds pixel-read CDUs (<see cref="IniA300CduForm"/>):
/// the press-flag prefix, the pop-out, the reader's grid and glyphs, the page keys and their
/// shortcuts. The A300 and A310 share one FMGS; the A330's MCDU is a different unit with different
/// keys, a bigger font and colour.
/// </summary>
internal sealed record CduFormProfile(
    string Name,
    string KeyPrefix,
    CduPopoutProfile Popout,
    Func<A300CduScreenReader> CreateReader,
    (string label, string key, string tip)[] PageKeys,
    IReadOnlyDictionary<Keys, string> AltKeys,
    string SlewUp,
    string SlewDown,
    string NextPage,
    string? PrevPage,
    bool ReadColour,
    Func<char, string?> KeyForChar)
{
    /// <summary>What the cockpit calls the unit — "CDU" on the A300/A310, "MCDU" on the A330 — used
    /// in the window title and every status message.</summary>
    public string Device { get; init; } = "CDU";

    /// <summary>
    /// The A330 MCDU. Key names are the aircraft's own <c>INI_MCDU1_&lt;KEY&gt;</c> press flags (write 1,
    /// the module consumes it), measured 2026-10-16 by pressing each and reading the page title: DIR,
    /// PROG, PERF, INIT, DATA, FPLAN, RADNAV, FUEL, SEC, ATC, MENU, AIRPORT, PREV, NEXT. They follow the
    /// iniBuilds A320's names (HubHop), not the A300's, which is where the slew keys (UARROW / DOWN),
    /// OVERFLY and the punctuation (DECIMAL, SPACE, PLUS, SLASH) come from. Shortcuts match the app's
    /// other A330 MCDU window (LVFR), so both A330s are flown the same way.
    /// </summary>
    public static CduFormProfile A330(A300CduGlyphLibrary library) => new(
        "A330", "INI_MCDU1_", CduPopoutProfile.A330,
        () => new A300CduScreenReader(library, CduLayout.A330),
        new[]
        {
            ("&DIR", "DIR", "Alt+D"), ("PROG", "PROG", "Alt+G"), ("&PERF", "PERF", "Alt+P"), ("&INIT", "INIT", "Alt+I"),
            ("D&ATA", "DATA", "Alt+A"), ("&F-PLN", "FPLAN", "Alt+F"), ("&RAD NAV", "RADNAV", "Alt+R"),
            ("FUEL PRED", "FUEL", "Alt+U"), ("SEC F-PLN", "SEC", "Alt+X"), ("ATC COMM", "ATC", "Alt+T"),
            ("MCDU &MENU", "MENU", "Alt+M"), ("AIRPORT", "AIRPORT", "Alt+O"), ("OVFY", "OVERFLY", "Alt+V"),
            ("Previous page", "PREV", "Alt+Left"), ("Next page", "NEXT", "Alt+Right"),
            ("Slew up", "DOWN", "Page Up"), ("Slew down", "UARROW", "Page Down"), ("CLR", "CLR", "Backspace"),
        },
        new Dictionary<Keys, string>
        {
            [Keys.D] = "DIR", [Keys.G] = "PROG", [Keys.P] = "PERF", [Keys.I] = "INIT", [Keys.A] = "DATA",
            [Keys.F] = "FPLAN", [Keys.R] = "RADNAV", [Keys.U] = "FUEL", [Keys.X] = "SEC",
            [Keys.T] = "ATC", [Keys.M] = "MENU", [Keys.O] = "AIRPORT", [Keys.V] = "OVERFLY",
        },
        // Like the A300, the slew keys are named the other way round from what they do (measured live
        // 2026-10-17 on the F-PLN page: UARROW moves on down the plan, DOWN back toward the top).
        SlewUp: "DOWN", SlewDown: "UARROW", NextPage: "NEXT", PrevPage: "PREV", ReadColour: true,
        KeyForChar: A330KeyForChar) { Device = "MCDU" };

    /// <summary>
    /// The iniBuilds A321LR MCDU (FS2024): the A330's keys and window behaviour (same INI_MCDU1_ key names,
    /// verified 2026-10-16), its own pop-out, grid and glyphs.
    /// </summary>
    public static CduFormProfile A321(A300CduGlyphLibrary library) => A330(library) with
    {
        Name = "A321",
        Popout = CduPopoutProfile.A321,
        CreateReader = () => new A300CduScreenReader(library, CduLayout.A321),
    };

    internal static string? A330KeyForChar(char c) => c switch
    {
        >= 'A' and <= 'Z' => c.ToString(),
        >= '0' and <= '9' => c.ToString(),
        '.' => "DECIMAL",
        '/' => "SLASH",
        '-' or '+' => "PLUS",   // the A330 MCDU has one +/- key
        ' ' => "SPACE",
        _ => null,
    };
}

/// <summary>
/// A CDU line as a screen reader user hears it, with what the A330 says in colour that matters for
/// operating it: text drawn GREY is not available yet (an option the FMS will not take, a field
/// that is not active) and is marked so — nothing else in the line can tell the pilot. Every other
/// colour is read as plain text. Pure.
/// </summary>
internal static class CduLineText
{
    public static string WithColour(A300CduLine line)
    {
        var text = line.Text;
        if (line.Chars.Count == 0) return IniA300CduForm.Tidy(text);
        // align every non-space character of the text with the glyph it came from ("×" reads as "><")
        var colours = new string[text.Length];
        int gi = 0, sub = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ' || gi >= line.Chars.Count) { colours[i] = ""; continue; }
            var c = line.Chars[gi];
            colours[i] = c.Colour;
            int width = c.Text == A300CduScreenReader.OverlappedMarkers ? 2 : Math.Max(1, c.Text.Length);
            if (++sub >= width) { gi++; sub = 0; }
        }
        return Annotate(text, colours);
    }

    /// <param name="colours">One colour per character of <paramref name="text"/> ("" for spaces).</param>
    internal static string Annotate(string text, string[] colours)
    {
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == ' ') { sb.Append(' '); i++; continue; }
            int s = i;
            while (i < text.Length && text[i] != ' ') i++;
            bool grey = Enumerable.Range(s, i - s).Count(k => colours[k] == CduColour.Grey) * 2 > i - s;
            sb.Append(text, s, i - s);
            if (grey)
            {
                // a run of grey words gets ONE marker, after its last word
                int j = i;
                while (j < text.Length && text[j] == ' ') j++;
                int e = j;
                while (e < text.Length && text[e] != ' ') e++;
                bool nextGrey = e > j && Enumerable.Range(j, e - j).Count(k => colours[k] == CduColour.Grey) * 2 > e - j;
                if (!nextGrey || j - i >= 3) sb.Append(" (unavailable)");
            }
        }
        return IniA300CduForm.Tidy(sb.ToString());
    }
}
