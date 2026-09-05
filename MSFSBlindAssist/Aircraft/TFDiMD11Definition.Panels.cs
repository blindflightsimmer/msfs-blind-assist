using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Panel layout + the documented export/read-out surface.
/// </summary>
public partial class TFDiMD11Definition
{
    // =================================================================================
    // Export variables — TFDi's documented integration surface
    // =================================================================================

    /// <summary>
    /// Read-outs that matter enough to announce on change, with their spoken wording and
    /// decoding. Everything else in <c>export_vars</c> is registered as a silent OnRequest cache
    /// (the hotkeys read it) rather than narrated.
    ///
    /// These carry disproportionate weight on this aircraft: the DUs are WASM-rendered and
    /// unreadable, so for a blind pilot these L:vars ARE the instruments. V-speeds in particular
    /// have no other source — there is no speed tape to read.
    /// </summary>
    private static Dictionary<string, SimVarDefinition> BuildExportVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---- FCP (Flight Control Panel) selected values -----------------------------
        // -999 / -9999 are TFDi's "readout is dashed" sentinels, per the Variables doc.
        v["MD11_AFS_SPD"] = Export("MD11_AFS_SPD", "Selected speed");
        v["MD11_AFS_HDG"] = Export("MD11_AFS_HDG", "Selected heading");
        v["MD11_AFS_ALT"] = Export("MD11_AFS_ALT", "Selected altitude");
        v["MD11_AFS_VS"] = Export("MD11_AFS_VS", "Selected vertical speed");

        // ---- Autoflight state --------------------------------------------------------
        v["MD11_AP_STATE"] = Announced("MD11_AP_STATE", "Autopilot", new()
        {
            [0] = "off", [1] = "AP 1", [2] = "AP 2", [3] = "AP 1 and 2",
        });
        v["MD11_ATS_STATE"] = Export("MD11_ATS_STATE", "Autothrottle state");
        v["MD11_ATS_CLAMP"] = Export("MD11_ATS_CLAMP", "Autothrottle clamp");

        // Unit/mode toggles — these decide how the FCP windows above are SPOKEN, so they are
        // cached but never narrated in their own right (a bare "1" means nothing aloud).
        v["MD11_AP_IAS_MACH"] = Export("MD11_AP_IAS_MACH", "Speed unit");
        v["MD11_AP_HDG_TRK"] = Export("MD11_AP_HDG_TRK", "Heading or track");
        v["MD11_AP_VS_FPA"] = Export("MD11_AP_VS_FPA", "Vertical mode");
        v["MD11_AP_FT_M"] = Export("MD11_AP_FT_M", "Altitude unit");

        // ---- V-speeds ----------------------------------------------------------------
        // No speed tape to read them off; these are the only source.
        v["MD11_V1"] = Export("MD11_V1", "V1");
        v["MD11_VR"] = Export("MD11_VR", "Rotate speed");
        v["MD11_V2"] = Export("MD11_V2", "V2");
        v["MD11_VSR"] = Export("MD11_VSR", "Slat retraction speed");
        v["MD11_VFR"] = Export("MD11_VFR", "Flap retraction speed");

        // ---- Minimums / altimeters ---------------------------------------------------
        v["MD11_CAP_MINIMUMS"] = Export("MD11_CAP_MINIMUMS", "Captain minimums");
        v["MD11_FO_MINIMUMS"] = Export("MD11_FO_MINIMUMS", "First officer minimums");
        v["MD11_CAP_ALTIMETER"] = Export("MD11_CAP_ALTIMETER", "Captain altimeter");
        v["MD11_FO_ALTIMETER"] = Export("MD11_FO_ALTIMETER", "First officer altimeter");
        v["MD11_STBY_ALTIMETER"] = Export("MD11_STBY_ALTIMETER", "Standby altimeter");

        // ---- APU ---------------------------------------------------------------------
        v["MD11_APU_STATE"] = Announced("MD11_APU_STATE", "APU", new()
        {
            [0] = "off", [1] = "starting", [2] = "running", [3] = "stopping",
        });
        v["MD11_APU_N1"] = Export("MD11_APU_N1", "APU N1");
        v["MD11_APU_N2"] = Export("MD11_APU_N2", "APU N2");

        // ---- Main engines ------------------------------------------------------------
        // UNDOCUMENTED, and not in the control map's export list — TFDi's Variables page lists the
        // APU's N1 but not the engines'. They are real all the same: found as registered L:vars in
        // md11host.wasm's DWARF, then CONFIRMED on a live aircraft (2026-07-17) reading 25.396,
        // 25.396 and 25.396 with per-engine variation — real data, not a constant or a miss (a
        // nonexistent L:var reads a flat 0, which is what the same probe returned for an invented
        // name and for MD11_ENG1_N2/EGT/FF — so those three do NOT exist; do not add them back).
        //
        // Worth having precisely because the EAD is WASM-rendered and unreadable: this is the only
        // way a blind pilot gets engine N1 on this aircraft. Silent, like the other numeric
        // read-outs — N1 narrated on every change through a whole take-off would be unusable.
        v["MD11_ENG1_N1"] = Export("MD11_ENG1_N1", "Engine 1 N1");
        v["MD11_ENG2_N1"] = Export("MD11_ENG2_N1", "Engine 2 N1");
        v["MD11_ENG3_N1"] = Export("MD11_ENG3_N1", "Engine 3 N1");
        // NOT silent, unlike every other Export() row: the three N1s drive HandleN1Callout's
        // one-shot "N1 70 percent" take-off cue, which MainForm's Ctrl+M wrap mutes exactly like
        // the flap read-out — so they keep their Ctrl+M rows. ExcludeFromMonitorManager means
        // "muted by plumbing" and must never sit on a var that speaks (the MD-11 monitor form
        // honours the flag; a flagged N1 would have made the cue unmutable).
        foreach (var n1 in new[] { "MD11_ENG1_N1", "MD11_ENG2_N1", "MD11_ENG3_N1" })
            v[n1].ExcludeFromMonitorManager = false;

        // ---- Fuel --------------------------------------------------------------------
        v["MD11_OVHD_TANK_1_VAL"] = Export("MD11_OVHD_TANK_1_VAL", "Tank 1");
        v["MD11_OVHD_TANK_2_VAL"] = Export("MD11_OVHD_TANK_2_VAL", "Tank 2");
        v["MD11_OVHD_TANK_3_VAL"] = Export("MD11_OVHD_TANK_3_VAL", "Tank 3");
        v["MD11_OVHD_TANK_AUX_VAL"] = Export("MD11_OVHD_TANK_AUX_VAL", "Auxiliary tank");
        v["MD11_OVHD_TANK_TAIL_VAL"] = Export("MD11_OVHD_TANK_TAIL_VAL", "Tail tank");

        // ---- Flap system -------------------------------------------------------------
        // FLAPS_MOVING is announced: on an aircraft whose flap gauge cannot be read, "flaps
        // moving" → "flaps set" is the only confirmation a selection actually took effect.
        v[Md11FlapSystem.FlapsMovingVar] = Announced(Md11FlapSystem.FlapsMovingVar, "Flaps", new()
        {
            [0] = "set", [1] = "moving",
        });

        return v;
    }

    /// <summary>A silent cached read-out: continuously updated, never narrated on its own.</summary>
    private static SimVarDefinition Export(string name, string display) => new()
    {
        Name = name,
        DisplayName = display,
        Type = SimVarType.LVar,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        // Consumed by ProcessSimVarUpdate / the hotkey read-outs rather than spoken per change —
        // a raw stream of "Selected heading: 271" on every knob detent would be unusable. Hidden
        // from Ctrl+M because a checkbox that silences an already-silent var does nothing.
        ExcludeFromMonitorManager = true,
        RenderAsReadOnlyStatus = true,
    };

    /// <summary>A read-out that DOES narrate on change, with decoded wording.</summary>
    private static SimVarDefinition Announced(string name, string display, Dictionary<double, string> values) => new()
    {
        Name = name,
        DisplayName = display,
        Type = SimVarType.LVar,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        ValueDescriptions = values,
        RenderAsReadOnlyStatus = true,
    };

    // =================================================================================
    // Panels
    // =================================================================================

    private Dictionary<string, List<string>>? _panelStructure;
    private Dictionary<string, List<string>>? _panelControls;

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        BuildPanelsOnce();
        return _panelStructure!;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        BuildPanelsOnce();
        return _panelControls!;
    }

    /// <summary>
    /// Derives sections and panels from <see cref="Md11PanelLayout"/> — the curated table is the
    /// ONLY source of panel order (spec §3.8): sections in the order a preparation flows, panels
    /// named as TFDi's Systems Guide names them, controls in physical panel order, a guard cover
    /// immediately before the control it covers, status rows (standalone lamps) last.
    ///
    /// A control the table does not name is still appended (never dropped) rather than silently
    /// missing — see <see cref="Md11PanelLayout.Place"/>'s safety net — and logged loudly so a
    /// regenerated map that adds a control is noticed instead of hidden.
    ///
    /// Annunciators not named by the table are excluded: they announce on change instead (see
    /// BuildControlVariable). A panel is for operating controls, not for scanning lamp rows.
    /// </summary>
    private void BuildPanelsOnce()
    {
        if (_panelStructure != null && _panelControls != null) return;

        var placement = Md11PanelLayout.Place(_map);
        if (placement.MissingKeys.Count > 0)
            Log.Warn("MD11", $"Layout names {placement.MissingKeys.Count} keys the map lacks: {string.Join(", ", placement.MissingKeys.Take(10))}");
        if (placement.Unplaced.Count > 0)
            Log.Warn("MD11", $"{placement.Unplaced.Count} controls are not in the layout table and were appended: {string.Join(", ", placement.Unplaced.Take(10))}");

        AddReadoutPanels(placement.Structure, placement.Controls);

        _panelStructure = placement.Structure;
        _panelControls = placement.Controls;
    }

    /// <summary>
    /// Read-out panels that exist only on this aircraft, and only because its glass cannot be
    /// read. On any other airframe a pilot gets V-speeds off the PFD speed tape and minimums off
    /// the PFD; here the DUs are rendered inside the WASM with no DOM behind them, so these
    /// exported L:vars are the ONLY source. Surfacing them as read-only panels means they are at
    /// least reachable by keyboard even where no hotkey exists (there is no V1/VR/V2 HotkeyAction
    /// in the shared enum — adding one is a follow-up).
    /// </summary>
    private static void AddReadoutPanels(
        Dictionary<string, List<string>> structure,
        Dictionary<string, List<string>> controls)
    {
        controls["V-Speeds"] = new List<string> { "MD11_V1", "MD11_VR", "MD11_V2", "MD11_VSR", "MD11_VFR" };
        controls["Minimums and Altimeters"] = new List<string>
        {
            "MD11_CAP_MINIMUMS", "MD11_FO_MINIMUMS",
            "MD11_CAP_ALTIMETER", "MD11_FO_ALTIMETER", "MD11_STBY_ALTIMETER",
        };
        controls["Autoflight Status"] = new List<string>
        {
            "MD11_AP_STATE", "MD11_ATS_STATE",
            "MD11_AFS_SPD", "MD11_AFS_HDG", "MD11_AFS_ALT", "MD11_AFS_VS",
        };
        controls["APU Status"] = new List<string> { "MD11_APU_STATE", "MD11_APU_N1", "MD11_APU_N2" };
        controls["Fuel Quantity"] = new List<string>
        {
            "MD11_OVHD_TANK_1_VAL", "MD11_OVHD_TANK_2_VAL", "MD11_OVHD_TANK_3_VAL",
            "MD11_OVHD_TANK_AUX_VAL", "MD11_OVHD_TANK_TAIL_VAL",
        };

        structure["Read-outs"] = new List<string>
        {
            "V-Speeds", "Minimums and Altimeters", "Autoflight Status", "APU Status", "Fuel Quantity",
        };
    }
}
