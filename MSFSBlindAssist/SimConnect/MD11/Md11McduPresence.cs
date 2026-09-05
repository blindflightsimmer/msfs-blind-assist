namespace MSFSBlindAssist.SimConnect.MD11;

/// <summary>What a unit's exported screen actually amounts to.</summary>
public enum Md11McduPresenceState
{
    /// <summary>Nothing has ever been delivered for this unit — a statement about the FEED.</summary>
    NoData,

    /// <summary>A screen arrived and carries no glyphs — a statement about the AIRCRAFT.</summary>
    Blank,

    /// <summary>A screen with something on it.</summary>
    Content,
}

/// <summary>
/// Turns "this MCDU has nothing on it" into something a blind pilot can act on.
///
/// The window opens on the LEFT unit. When the left unit's export is all zeros, <c>Render</c>
/// would otherwise build "Title:", "1:" … "6:", "Scratchpad:" — eight blank rows — over a status
/// line reading "MCDU: connected", and announce nothing, because the announce is gated on a
/// non-empty title. Every one of those is individually reasonable and together they produce a
/// window that looks broken while claiming to be fine. That is how this was reported: *"my mcdu
/// is not showing up in the app"*, on a build whose MCDU feed was working correctly.
///
/// Measured on the live aircraft (MD-11F GE, 2026-09-05, one binary throughout — 4d09b482):
/// Left carried 204 glyphs at 16:19 and zero at both 17:13 and 17:43, while Center and Right
/// carried real pages in all three. So a blank unit beside working ones is a REAL state of this
/// aircraft, not a transport failure to be papered over.
///
/// Deliberately NOT done here: switching the pilot to a working unit. Which CDU they are looking
/// at is theirs to choose — the window says which units have something and which keystroke gets
/// there, and stops. A silent redirect would make "the left MCDU" mean whatever the app felt like.
/// </summary>
public static class Md11McduPresence
{
    /// <summary>
    /// The unit-switch chords, spelled once. <see cref="Md11McduForm"/>'s key handler binds
    /// Ctrl+Shift+L/C/R; these strings are the only place that chord is written for a pilot to
    /// read, so a rebind changes both together rather than leaving the advisory naming a dead key.
    /// </summary>
    public static string Chord(Md11McduUnit unit) => unit switch
    {
        Md11McduUnit.Left => "Ctrl+Shift+L",
        Md11McduUnit.Center => "Ctrl+Shift+C",
        _ => "Ctrl+Shift+R",
    };

    /// <summary>
    /// Blank vs never-delivered vs real content.
    ///
    /// The whitespace test is load-bearing: <c>Decode</c> maps an empty cell (value 0) to a SPACE
    /// on purpose, because the MCDU is a fixed-pitch grid where column position carries meaning.
    /// So a blank screen reaches us as rows of spaces and NEVER as empty strings — an
    /// <c>IsNullOrEmpty</c> check here would classify the live all-zero Left screen as content
    /// and this whole class would do nothing.
    /// </summary>
    public static Md11McduPresenceState Classify(Md11McduScreen? screen)
    {
        if (screen == null) return Md11McduPresenceState.NoData;

        foreach (var line in screen.Lines)
            if (!string.IsNullOrWhiteSpace(line))
                return Md11McduPresenceState.Content;

        return Md11McduPresenceState.Blank;
    }

    /// <summary>
    /// The rows to show INSTEAD of a screenful of blanks. Empty for <see cref="Md11McduPresenceState.Content"/> —
    /// a working CDU gains no extra line to arrow past.
    /// </summary>
    public static IReadOnlyList<string> Describe(
        Md11McduUnit unit,
        Md11McduPresenceState state,
        IReadOnlyList<Md11McduUnit> unitsWithContent)
    {
        if (state == Md11McduPresenceState.Content) return Array.Empty<string>();

        if (state == Md11McduPresenceState.NoData)
            return new[] { $"{unit} MCDU: no data received yet." };

        var rows = new List<string>(2) { $"{unit} MCDU is blank - nothing on this screen." };

        // Never offer a jump to the unit already being shown: the pilot is looking at it, and it
        // is the blank one.
        var elsewhere = unitsWithContent.Where(u => u != unit).Distinct().OrderBy(u => (int)u).ToList();
        if (elsewhere.Count > 0)
        {
            rows.Add("Showing something: " +
                string.Join(", ", elsewhere.Select(u => $"{u} ({Chord(u)})")) + ".");
        }

        return rows;
    }
}
