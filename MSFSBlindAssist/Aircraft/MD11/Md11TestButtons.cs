namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11's hold-to-test buttons.
///
/// On the real aircraft — and in TFDi's model — a test button lights its lights while it is HELD
/// and they go out on release: holding the fire test on a live aircraft (2026-09-06) lit ENG 1,
/// 2 and 3 FIRE, APU FIRE and the master warning, all dark again the moment it was let go. The
/// bus's normal press is DOWN, 60 ms, UP — long enough for the aircraft to register a button,
/// and far too short for the 1 Hz lamp batch to ever see the lights, so a pilot pressing a test
/// button heard nothing at all. These buttons are held for <see cref="HoldMs"/> instead; the
/// lights that come on announce themselves through the normal lamp path.
///
/// Curated by node id, never by a name match: "Weather Radar TEST Mode" is a mode SELECTION
/// (one tap switches the radar into test mode), not a hold-to-test button.
/// </summary>
public static class Md11TestButtons
{
    /// <summary>How long a test button is held: three 1 Hz lamp deliveries, and not long enough to feel stuck.</summary>
    public const int HoldMs = 3000;

    public static readonly IReadOnlySet<string> HoldToTest = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "MD11_AOVHD_FIRETEST_BT",       // Engine and APU Fire Test — every detection loop at once
        "MD11_AOVHD_CRGSMK_TEST_BT",    // Cargo Fire and Smoke Test
        "MD11_OVHD_ANNUNLT_TEST_BT",    // Annunciator light test
        "MD11_OVHD_HYD_HYD_TEST_BT",    // Hydraulic System Test (guarded — the cover is lifted first)
        "MD11_OVHD_FUEL_QTY_TEST_BT",   // Fuel Quantity Test
        "MD11_OVHD_LTS_EMER_TEST_BT",   // Emergency Lights Test
        "MD11_OVHD_CRG_DOOR_TEST_BT",   // Cargo Door Test
        "MD11_OVHD_CVR_TEST_BT",        // Cockpit Voice Recorder Test (the meter deflects while held)
        "MD11_LSIDE_OXY_TEST_BT",       // Captain Oxygen Test
        "MD11_RSIDE_OXY_TEST_BT",       // First Officer Oxygen Test
        "MD11_MIP_ISFD_TEST_BT",        // Integrated Standby Flight Display Test
        "MD11_PED_XPNDR_TEST_BT",       // TCAS Test
    };

    public static bool IsHoldToTest(string nodeId) => HoldToTest.Contains(nodeId);
}
