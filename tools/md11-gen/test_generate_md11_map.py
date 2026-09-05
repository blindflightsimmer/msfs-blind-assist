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


if __name__ == "__main__":
    unittest.main()
