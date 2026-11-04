namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// MSFS 2024's covers (<c>COVER ON:1..8</c>) — the table, their SimVarUpdated names and their
/// request ids. Kept apart from <see cref="SimConnectManager"/> so it is pure (no SimConnect
/// library) and testable. The chocks are index 0 and stay on their own path
/// (SimConnectManager.WheelChocks.cs).
/// </summary>
public static class SimCovers
{
    /// <summary>VarName prefix for a cover's state (1 = on): <c>AIRCRAFT_COVER_&lt;index&gt;</c>.</summary>
    public const string VarPrefix = "AIRCRAFT_COVER_";

    /// <summary>The first of the covers' request (and definition) ids, one per cover: 361-368.</summary>
    public const int FirstRequestId = 361;

    /// <summary>
    /// The sim's covers after the chocks, with the phrase spoken when one comes off. Indices 1
    /// (engine) and 2 (pitot) are corroborated by the MSFS DevSupport thread "How to remove Covers
    /// and Chocks programmatically?"; the rest follow the order of a saved flight's [Covers] section
    /// (Chock, Engine, Pitot, Static_Port, Rotor, Landing_Gear, Propeller, Windshield, Generic.1 —
    /// read from a live iniBuilds A330 save, 2026-10-28). A wrong guess past index 2 can only fail to
    /// remove a cover, never fit one: the app only ever sends "off".
    /// </summary>
    public static readonly IReadOnlyList<(int Index, string Name)> All = new[]
    {
        (1, "Engine covers"),
        (2, "Pitot covers"),
        (3, "Static port covers"),
        (4, "Rotor covers"),
        (5, "Landing gear pins"),
        (6, "Propeller covers"),
        (7, "Windshield covers"),
        (8, "Covers"),
    };

    /// <summary>The SimVarUpdated name for cover <paramref name="index"/>.</summary>
    public static string VarName(int index) => VarPrefix + index;

    /// <summary>The cover index a SimVarUpdated name carries, or null for any other name.</summary>
    public static int? IndexForVarName(string varName) =>
        varName.StartsWith(VarPrefix, StringComparison.Ordinal)
        && int.TryParse(varName.AsSpan(VarPrefix.Length), out int i) && All.Any(c => c.Index == i) ? i : null;

    /// <summary>The cover index a covers request id answers for, or null for any other request.</summary>
    public static int? IndexForRequest(uint requestId)
    {
        long offset = (long)requestId - FirstRequestId;
        return offset >= 0 && offset < All.Count ? (int)offset + 1 : null;
    }

    /// <summary>The spoken name of cover <paramref name="index"/>.</summary>
    public static string NameOf(int index) => All.First(c => c.Index == index).Name;
}
