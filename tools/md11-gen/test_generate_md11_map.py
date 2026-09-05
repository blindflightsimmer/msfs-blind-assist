"""Label and structure repairs in the MD-11 control-map generator.

Pure-function tests on tiny fixtures: no aircraft package, no wasm. Every case is a
defect seen in the shipped map (see docs/md11.md, "Labels").
"""
import unittest

import generate_md11_map as g


def ctl(node_id, kind="button", label=None, events=None, state_var=None, value_map=None,
        guard_id=None, area=None, source="FlightDeck/Overhead.xml"):
    return {
        "node_id": node_id, "kind": kind, "template": "", "area": area or g.area_of(node_id),
        "label": label, "label_source": "tooltip" if label else "derived",
        "state_var": state_var or node_id, "value_map": value_map or {},
        "num_states": None, "events": events or {}, "guard_id": guard_id, "source": source,
    }


class SpeakableTests(unittest.TestCase):
    def test_html_entities_are_decoded_and_the_arrow_reads_as_to(self):
        self.assertEqual("Air System 1 to 2 Isolation Valve",
                         g.speakable("Air System 1&lt;-&gt;2 Isolation Valve"))

    def test_degrees_still_spelled_out(self):
        self.assertEqual("5 degrees", g.speakable("5°"))


class ParenthesisTests(unittest.TestCase):
    def test_balanced_trailing_parenthetical_is_kept(self):
        label, _, _ = g.parse_tooltip("APU Generator (APU Panel)")
        self.assertEqual("APU Generator (APU Panel)", label)

    def test_wrapping_parens_are_stripped_as_a_pair(self):
        self.assertEqual("Strobes", g.strip_outer_parens("(Strobes)"))
        self.assertEqual("High Intensity Lights (Strobes)",
                         g.strip_outer_parens("High Intensity Lights (Strobes)"))


class FinalizeTests(unittest.TestCase):
    def test_guard_is_named_after_the_control_it_covers(self):
        out = g.finalize_controls([
            ctl("MD11_OVHD_ELEC_BATT_BT", label="Battery", guard_id="MD11_OVHD_ELEC_BATT_GRD",
                events={"LEFT_BUTTON_DOWN": 1, "LEFT_BUTTON_UP": 2}),
            ctl("MD11_OVHD_ELEC_BATT_GRD", kind="guard", label="Battery", events={"LEFT_BUTTON_DOWN": 3}),
        ])
        labels = {c["node_id"]: c["label"] for c in out}
        self.assertEqual("Battery", labels["MD11_OVHD_ELEC_BATT_BT"])
        self.assertEqual("Battery guard", labels["MD11_OVHD_ELEC_BATT_GRD"])

    def test_breaker_carries_its_grid_position_and_drops_the_word_breaker(self):
        out = g.finalize_controls([ctl("MD11_BKR_BWU_C24", label="Tank 1 Transfer Pump Power Breaker",
                                       value_map={"1": "Pulled", "0": "Pushed"})])
        self.assertEqual("C24 Tank 1 Transfer Pump Power", out[0]["label"])

    def test_second_clickspot_of_one_button_is_dropped(self):
        ev = {"LEFT_BUTTON_DOWN": 90297, "LEFT_BUTTON_UP": 90298}
        out = g.finalize_controls([
            ctl("MD11_OVHD_PNEU_ECON_BT", label="ECON Mode", events=ev),
            ctl("MD11_OVHD_PNEU_ECON_BT001", label="ECON Mode", events=ev),
            ctl("MD11_OVHD_LTS_CREW_REST_BT", label="Crew Rest Call", events={"LEFT_BUTTON_DOWN": 1, "LEFT_BUTTON_UP": 2}),
            ctl("MD11_OVHD_LTS_CREW_REST_BT_F", label="Crew Rest Call", events={"LEFT_BUTTON_DOWN": 1, "LEFT_BUTTON_UP": 2}),
        ])
        self.assertEqual(["MD11_OVHD_PNEU_ECON_BT", "MD11_OVHD_LTS_CREW_REST_BT"], [c["node_id"] for c in out])

    def test_distinct_events_are_not_duplicates(self):
        out = g.finalize_controls([
            ctl("MD11_LYOKE_TRIM_SW", kind="switch", label="Captain Elevator Trim Switch", events={"LEFT_BUTTON_DOWN": 1}),
            ctl("MD11_LYOKE_TRIM_SW001", kind="switch", label="First Officer Elevator Trim Switch", events={"LEFT_BUTTON_DOWN": 9}),
        ])
        self.assertEqual(2, len(out))

    def test_second_lamp_node_on_the_same_var_is_dropped(self):
        out = g.finalize_controls([
            ctl("MD11_PED_CPT_AUDIO_PNL_VHF1_MIC_LT", kind="annun"),
            ctl("MD11_CPT_AUDIO_PNL_VHF1_MIC_LT", kind="annun", state_var="MD11_PED_CPT_AUDIO_PNL_VHF1_MIC_LT"),
            ctl("MD11_OVHD_PNEU_ECON_OFF_LT", kind="annun"),
            ctl("MD11_OVHD_PNEU_ECON_OFF_LT001", kind="annun", state_var="MD11_OVHD_PNEU_ECON_OFF_LT"),
        ])
        self.assertEqual(["MD11_PED_CPT_AUDIO_PNL_VHF1_MIC_LT", "MD11_OVHD_PNEU_ECON_OFF_LT"], [c["node_id"] for c in out])

    def test_option_flags_become_kind_option(self):
        out = g.finalize_controls([ctl("MD11_OPT_EFB", kind="annun")])
        self.assertEqual("option", out[0]["kind"])

    def test_curated_labels_replace_derived_garbage(self):
        out = g.finalize_controls([
            ctl("MD11_OVHD_L_RAIN_REPLNT_BT"), ctl("MD11_PED_XPNDR_7_BT"), ctl("MD11_LMCDU_LSK_1L_BT"),
            ctl("MD11_CTR_FLTNO2_SW", kind="switch"), ctl("MD11_OVHD_100_PAX_LOAD_SW", kind="switch", label="Pax Load Selector"),
        ])
        labels = {c["node_id"]: c["label"] for c in out}
        self.assertEqual("Left Rain Repellent", labels["MD11_OVHD_L_RAIN_REPLNT_BT"])
        self.assertEqual("Transponder 7", labels["MD11_PED_XPNDR_7_BT"])
        self.assertEqual("LSK 1L", labels["MD11_LMCDU_LSK_1L_BT"])
        self.assertEqual("Flight Number Digit 2", labels["MD11_CTR_FLTNO2_SW"])
        self.assertEqual("Passenger Load Hundreds", labels["MD11_OVHD_100_PAX_LOAD_SW"])

    def test_raw_3d_names_get_a_real_area(self):
        out = g.finalize_controls([ctl("Cylinder11904", label="Door 1L Slides"), ctl("knob_kohlsman", kind="knob", label="Standby Altimeter Setting")])
        areas = {c["node_id"]: c["area"] for c in out}
        self.assertEqual("Doors and Exterior", areas["Cylinder11904"])
        self.assertEqual("Main Instrument Panel", areas["knob_kohlsman"])

    def test_the_mirrored_window_shade_is_the_first_officers(self):
        # "Mirror_" names the modelling mirror the node was made with, not the left side of the
        # cockpit: FOAux_Light.xml declares it with ANIM_NAME MD11_RSIDE_WINDOW_SHADE (event
        # 95518, beside MD11_RSIDE_WINDOW's 95517). Calling it "Left Window Shade (mirror)" on
        # the Captain panel had a pilot operating the RIGHT shade, with no shade on the F/O side
        # at all.
        out = g.finalize_controls([ctl("l_window_shade_pull"), ctl("Mirror_l_window_shade_pull")])
        by = {c["node_id"]: c for c in out}
        self.assertEqual("Left Window Shade", by["l_window_shade_pull"]["label"])
        self.assertEqual("Captain Side Panel", by["l_window_shade_pull"]["area"])
        self.assertEqual("Right Window Shade", by["Mirror_l_window_shade_pull"]["label"])
        self.assertEqual("F/O Side Panel", by["Mirror_l_window_shade_pull"]["area"])

    def test_glareshield_warning_areas_are_named_correctly(self):
        self.assertEqual("Glareshield (Captain)", g.area_of("MD11_GSL_MST_WRN_BT"))
        self.assertEqual("Glareshield (First Officer)", g.area_of("MD11_GSR_MST_WRN_BT"))


class KindCountsTests(unittest.TestCase):
    def test_reclassified_option_is_counted_once_not_under_annun_too(self):
        # MD11_OPT_* nodes arrive from collect() tagged "annun" (that's their template
        # kind) and finalize_controls repoints them to "option". kind_counts() must be
        # called on that FINALIZED list, so the row is tallied under "option" only --
        # never counted a second time under "annun", which is what happened when the
        # generator instead patched an "option" tally onto collect()'s pre-finalize
        # per-kind stats (the two counts landed on the same 7 rows).
        out = g.finalize_controls([
            ctl("MD11_OPT_EFB", kind="annun"),
            ctl("MD11_OVHD_PNEU_ECON_OFF_LT", kind="annun"),
        ])
        counts = g.kind_counts(out)
        self.assertEqual(1, counts["option"])
        self.assertEqual(1, counts["annun"])
        self.assertEqual(len(out), sum(counts.values()))


class StateTests(unittest.TestCase):
    def lamp(self, nid, area=None):
        return ctl(nid, kind="annun", area=area)

    def state_of(self, controls, nid):
        out = g.apply_state(g.finalize_controls(controls))
        return {c["node_id"]: c for c in out}[nid]

    def test_stem_rule_attaches_single_token_legends_only(self):
        gen = ctl("MD11_OVHD_ELEC_GEN1_BT", label="Generator 1", events={"LEFT_BUTTON_DOWN": 1, "LEFT_BUTTON_UP": 2})
        drive = ctl("MD11_OVHD_ELEC_GEN1_DRIVE_BT", label="Generator 1 IDG Disconnect", events={"LEFT_BUTTON_DOWN": 3, "LEFT_BUTTON_UP": 4})
        lamps = [self.lamp(n) for n in ("MD11_OVHD_ELEC_GEN1_OFF_LT", "MD11_OVHD_ELEC_GEN1_ARM_LT",
                                        "MD11_OVHD_ELEC_GEN1_DRIVE_FAULT_LT", "MD11_OVHD_ELEC_GEN1_DRIVE_DISCONNECT_LT")]
        s = self.state_of([gen, drive] + lamps, "MD11_OVHD_ELEC_GEN1_BT")["state"]
        self.assertEqual([("MD11_OVHD_ELEC_GEN1_OFF_LT", "OFF", "Off"), ("MD11_OVHD_ELEC_GEN1_ARM_LT", "ARM", "Armed")],
                         [(l["var"], l["legend"], l["lit"]) for l in s["lamps"]])
        d = self.state_of([gen, drive] + lamps, "MD11_OVHD_ELEC_GEN1_DRIVE_BT")["state"]
        self.assertEqual({"FAULT", "DISCONNECT"}, {l["legend"] for l in d["lamps"]})

    def test_multi_token_legend_and_dark_rule(self):
        econ = ctl("MD11_OVHD_PNEU_ECON_BT", label="ECON Mode", value_map={"1": "Off", "0": "On"},
                   state_var="MD11_OVHD_PNEU_SYSTEM_SEL_BT", events={"LEFT_BUTTON_DOWN": 1})
        s = self.state_of([econ, self.lamp("MD11_OVHD_PNEU_ECON_OFF_LT"), self.lamp("MD11_OVHD_PNEU_ECON_CAB_ALT_LT")],
                          "MD11_OVHD_PNEU_ECON_BT")["state"]
        self.assertEqual({"OFF": "Off", "CAB_ALT": "Cabin altitude"}, {l["legend"]: l["lit"] for l in s["lamps"]})
        self.assertEqual("On", s["dark"])          # an OFF legend dark means the system is on
        self.assertNotIn("latch", s)               # its tooltip reads a FOREIGN var: not a latch

    def test_bare_lamp_defaults_to_on_and_honours_the_override(self):
        nav = ctl("MD11_OVHD_LTS_NAV_BT", label="Navigation Lights", events={"LEFT_BUTTON_DOWN": 1})
        stby = ctl("MD11_OVHD_LTS_STBY_COMP_BT", label="Standby Compass Light", events={"LEFT_BUTTON_DOWN": 2})
        out = g.apply_state(g.finalize_controls([nav, stby, self.lamp("MD11_OVHD_LTS_NAV_LT"), self.lamp("MD11_OVHD_LTS_STBY_COMP_LT")]))
        by = {c["node_id"]: c for c in out}
        self.assertEqual([("MD11_OVHD_LTS_NAV_LT", "OFF", "Off")], [(l["var"], l["legend"], l["lit"]) for l in by["MD11_OVHD_LTS_NAV_BT"]["state"]["lamps"]])
        self.assertEqual("On", by["MD11_OVHD_LTS_NAV_BT"]["state"]["dark"])
        self.assertEqual([("MD11_OVHD_LTS_STBY_COMP_LT", "ON", "On")], [(l["var"], l["legend"], l["lit"]) for l in by["MD11_OVHD_LTS_STBY_COMP_BT"]["state"]["lamps"]])
        self.assertEqual("Off", by["MD11_OVHD_LTS_STBY_COMP_BT"]["state"]["dark"])
        self.assertEqual("Navigation Lights OFF light", by["MD11_OVHD_LTS_NAV_LT"]["label"])
        self.assertEqual("paired", by["MD11_OVHD_LTS_NAV_LT"]["label_source"])

    def test_curated_pairing_and_dark_override(self):
        tie = ctl("MD11_OVHD_ELEC_AC_TIE1_BT", label="AC Bus Tie 1", events={"LEFT_BUTTON_DOWN": 1})
        ext = ctl("MD11_OVHD_ELEC_EXT_PWR_BT", label="External Power", events={"LEFT_BUTTON_DOWN": 2})
        lamps = [self.lamp(n) for n in ("MD11_OVHD_ELEC_AC1_TIE_ARM_LT", "MD11_OVHD_ELEC_AC1_TIE_OFF_LT",
                                        "MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", "MD11_OVHD_ELEC_EXT_PWR_ON_LT")]
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([tie, ext] + lamps))}
        self.assertEqual({"ARM", "OFF"}, {l["legend"] for l in out["MD11_OVHD_ELEC_AC_TIE1_BT"]["state"]["lamps"]})
        self.assertEqual("Closed", out["MD11_OVHD_ELEC_AC_TIE1_BT"]["state"]["dark"])
        self.assertEqual("Not available", out["MD11_OVHD_ELEC_EXT_PWR_BT"]["state"]["dark"])
        self.assertEqual("External Power AVAIL light", out["MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT"]["label"])

    def test_latch_from_own_tooltip_keeps_tfdi_polarity(self):
        aice = ctl("MD11_OVHD_AICE_ENG1_BT", label="Engine 1 Anti Ice", value_map={"1": "On", "0": "Off"}, events={"LEFT_BUTTON_DOWN": 1})
        defog = ctl("MD11_OVHD_WNDSHLD_AICE_DEFOG_BT", label="Windshield Defog", value_map={"1": "Off", "0": "On"}, events={"LEFT_BUTTON_DOWN": 2})
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([aice, defog]))}
        self.assertEqual({"var": "MD11_OVHD_AICE_ENG1_BT", "on": "On", "off": "Off"}, out["MD11_OVHD_AICE_ENG1_BT"]["state"]["latch"])
        self.assertEqual({"var": "MD11_OVHD_WNDSHLD_AICE_DEFOG_BT", "on": "Off", "off": "On"}, out["MD11_OVHD_WNDSHLD_AICE_DEFOG_BT"]["state"]["latch"])

    def test_battery_and_guards_latch(self):
        batt = ctl("MD11_OVHD_ELEC_BATT_BT", label="Battery", guard_id="MD11_OVHD_ELEC_BATT_GRD", events={"LEFT_BUTTON_DOWN": 1})
        grd = ctl("MD11_OVHD_ELEC_BATT_GRD", kind="guard", label="Battery", events={"LEFT_BUTTON_DOWN": 2})
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([batt, grd, self.lamp("MD11_OVHD_ELEC_BATT_OFF_LT")]))}
        self.assertEqual({"var": "MD11_OVHD_ELEC_BATT_BT", "on": "On", "off": "Off"}, out["MD11_OVHD_ELEC_BATT_BT"]["state"]["latch"])
        self.assertEqual({"var": "MD11_OVHD_ELEC_BATT_GRD", "on": "Open", "off": "Closed"}, out["MD11_OVHD_ELEC_BATT_GRD"]["state"]["latch"])

    def test_fault_only_button_is_normal_when_dark(self):
        self.assertEqual("Normal", g.dark_text(["FAULT", "DISAG"], "X"))
        self.assertEqual("On", g.dark_text(["OFF", "LOW"], "X"))
        self.assertEqual("Off", g.dark_text(["ON", "AVAIL"], "X"))
        self.assertIsNone(g.dark_text([], "X"))

    def test_standalone_lamp_gets_a_system_name_and_states(self):
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([self.lamp("MD11_OVHD_ELEC_AC1_OFF_LT"), self.lamp("MD11_OVHD_HYD_SYS_2_PRESS_LT")]))}
        ac = out["MD11_OVHD_ELEC_AC1_OFF_LT"]
        self.assertEqual("AC Bus 1", ac["label"])
        self.assertEqual([{"var": "MD11_OVHD_ELEC_AC1_OFF_LT", "legend": "OFF", "lit": "Off"}], ac["state"]["lamps"])
        self.assertEqual("Powered", ac["state"]["dark"])
        self.assertEqual("Hydraulic System 2 Pressure", out["MD11_OVHD_HYD_SYS_2_PRESS_LT"]["label"])

    def test_lamp_of_a_knob_becomes_a_named_row_not_a_fold(self):
        knob = ctl("MD11_OVHD_ELEC_EMER_PWR_KB", kind="knob", label="Emergency Power", value_map={"0": "Off", "1": "Armed", "2": "On"}, events={"LEFT_BUTTON_DOWN": 1})
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([knob, self.lamp("MD11_OVHD_ELEC_EMER_PWR_ON_LT")]))}
        self.assertNotIn("state", out["MD11_OVHD_ELEC_EMER_PWR_KB"])
        row = out["MD11_OVHD_ELEC_EMER_PWR_ON_LT"]
        self.assertEqual("Emergency Power ON light", row["label"])
        self.assertEqual("On", row["state"]["lamps"][0]["lit"])
        self.assertEqual("Off", row["state"]["dark"])

    def test_side_panel_source_lamps_are_named(self):
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([self.lamp("MD11_LSIDE_INP_APPRCAP2_LT"), self.lamp("MD11_RSIDE_INP_EIS_FOAUX_LT")]))}
        self.assertEqual("Captain ILS Source CAP 2 light", out["MD11_LSIDE_INP_APPRCAP2_LT"]["label"])
        self.assertEqual("First Officer EIS Source FO AUX light", out["MD11_RSIDE_INP_EIS_FOAUX_LT"]["label"])

    def test_fuel_dump_stop_lamp_pairs_to_the_stop_button_not_the_dump_button(self):
        # MD11_OVHD_FUEL_DUMP_STOP_LT's stem is a superset of MD11_OVHD_FUEL_DUMP_BT's, so it
        # matches BOTH buttons' stem rule (DUMP_BT via "<stem>_STOP_LT", STOP a LEGEND_MEANINGS
        # key; DUMP_STOP_BT via the bare "<stem>_LT"). Per TFDi's Systems Guide the lamp is the
        # STOP button's own indicator. Listing the DUMP button first reproduces the ordering
        # that used to let it win the lamp before the curated STATE_LAMPS entry was added.
        dump = ctl("MD11_OVHD_FUEL_DUMP_BT", label="Fuel Dump", value_map={"1": "Open", "0": "Closed"})
        stop = ctl("MD11_OVHD_FUEL_DUMP_STOP_BT", label="Fuel Dump Emergency Stop", value_map={"1": "Stop", "0": "Normal"})
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls(
            [dump, stop, self.lamp("MD11_OVHD_FUEL_DUMP_LT"), self.lamp("MD11_OVHD_FUEL_DUMP_STOP_LT")]))}
        self.assertEqual([("MD11_OVHD_FUEL_DUMP_STOP_LT", "STOP", "Stop")],
                         [(l["var"], l["legend"], l["lit"]) for l in out["MD11_OVHD_FUEL_DUMP_STOP_BT"]["state"]["lamps"]])
        self.assertEqual([("MD11_OVHD_FUEL_DUMP_LT", "OPEN", "Open")],
                         [(l["var"], l["legend"], l["lit"]) for l in out["MD11_OVHD_FUEL_DUMP_BT"]["state"]["lamps"]])
        self.assertEqual("Fuel Dump Emergency Stop STOP light", out["MD11_OVHD_FUEL_DUMP_STOP_LT"]["label"])

    def test_stem_rule_tie_break_prefers_the_longer_more_specific_stem(self):
        # With no STATE_LAMPS curation at all, a lamp matching two buttons' stems must go to
        # whichever stem is longer (more specific) -- MD11_OVHD_X_TEST_BT's bare "<stem>_LT"
        # match, not MD11_OVHD_X_BT's shorter "<stem>_TEST_LT" match (TEST is a LEGEND_MEANINGS
        # key). Listing the shorter-stem button first would have won the old, order-dependent
        # code.
        x = ctl("MD11_OVHD_X_BT", label="X")
        x_test = ctl("MD11_OVHD_X_TEST_BT", label="X Test")
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls(
            [x, x_test, self.lamp("MD11_OVHD_X_TEST_LT")]))}
        self.assertEqual([("MD11_OVHD_X_TEST_LT", "ON", "On")],
                         [(l["var"], l["legend"], l["lit"]) for l in out["MD11_OVHD_X_TEST_BT"]["state"]["lamps"]])
        self.assertNotIn("state", out["MD11_OVHD_X_BT"])


if __name__ == "__main__":
    unittest.main()
