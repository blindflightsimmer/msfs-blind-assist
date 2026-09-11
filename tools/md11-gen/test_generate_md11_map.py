"""Label and structure repairs in the MD-11 control-map generator.

Pure-function tests on tiny fixtures: no aircraft package, no wasm. Every case is a
defect seen in the shipped map (see docs/md11.md, "Labels").
"""
import contextlib
import io
import json
import os
import sys
import tempfile
import unittest
from unittest import mock

import generate_md11_map as g
import md11_paths


def ctl(node_id, kind="button", label=None, events=None, state_var=None, value_map=None,
        guard_id=None, area=None, source="FlightDeck/Overhead.xml"):
    return {
        "node_id": node_id, "kind": kind, "template": "", "area": area or g.area_of(node_id),
        "label": label, "label_source": "tooltip" if label else "derived",
        "state_var": state_var or node_id, "value_map": value_map or {},
        "num_states": None, "events": events or {}, "guard_id": guard_id, "source": source,
    }


def use_template(template, **fields):
    """One ModelBehaviorDefs <UseTemplate> block, its fields in the order given."""
    inner = "".join(f"<{name}>{value}</{name}>" for name, value in fields.items())
    return f'<UseTemplate Name="{template}">{inner}</UseTemplate>'


def write_package(root, files):
    """A minimal MD-11 package under `root`, for tests that run collect() or main() end to end.

    `files` maps a path under ModelBehaviorDefs/TFDi_Design/MD11 ('FlightDeck/Overhead.xml') to the
    XML inside its <ModelBehaviors> root. The wasm names one control var, which is all
    _exit_if_incomplete asks for. Returns (package dir, wasm path)."""
    pkg = os.path.join(root, "pkg")
    base = os.path.join(pkg, md11_paths.PACKAGE_MARKER)
    for rel, xml in files.items():
        path = os.path.join(base, *rel.split("/"))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write("<ModelBehaviors>\n" + xml + "\n</ModelBehaviors>\n")
    wasm = os.path.join(root, "md11host.wasm")
    with open(wasm, "wb") as fh:
        fh.write(b"\0asm Aircraft::vars->MD11_OVHD_ELEC_X_OFF_LT\0")
    return pkg, wasm


def run_main(pkg, wasm, out):
    """generate_md11_map.main() on a fixture package: (the map it wrote, what it printed to stderr)."""
    err = io.StringIO()
    argv = ["generate_md11_map.py", "--pkg", pkg, "--wasm", wasm, "--out", out]
    with mock.patch.object(sys, "argv", argv), \
         contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(err):
        g.main()
    with open(out, encoding="utf-8") as fh:
        return json.load(fh), err.getvalue()


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


class StrayPercentTests(unittest.TestCase):
    """The aircraft ships one tooltip with a '%' directly before a branch literal — a typo, not
    syntax (the directives are '%(', '%{', '%!' and the literal '%%'). Left unparsed it gave the
    Auxiliary IRS selector no positions, and MSFSBA rendered the third IRS switch read-only."""

    def test_a_stray_percent_before_a_branch_literal_is_dropped(self):
        label, var, value_map = g.parse_tooltip(
            "Auxiliary IRS (%((L:MD11_OVHD_IRS_3_KB))%{if}%Nav%{else}Off%{end})")
        self.assertEqual("Auxiliary IRS", label)
        self.assertEqual("MD11_OVHD_IRS_3_KB", var)
        self.assertEqual({"1": "Nav", "0": "Off"}, value_map)

    def test_a_literal_percent_sign_is_still_dropped_as_live_data_not_as_a_typo(self):
        # '%%' is a real percent sign after a number ('%!d!%%'); the brightness knobs use it.
        # Their tooltips carry no positions before or after the fix.
        label, var, value_map = g.parse_tooltip(
            "DU1 Brightness (%((L:MD11_PED_DU1_BRT_KB) 10 *)%!d!%%)")
        self.assertEqual("DU1 Brightness", label)
        self.assertEqual("MD11_PED_DU1_BRT_KB", var)
        self.assertEqual({}, value_map)

    def test_a_triple_percent_before_a_directive_is_untouched(self):
        # '%%%{else}': a literal '%%' straight before a directive — the audio volume knobs.
        # Neither '%' before the '{' is a stray sign, and the knob still yields NO positions
        # (its state is a live number).
        label, _, value_map = g.parse_tooltip(
            "Captain VHF1 Volume (%((L:MD11_PED_CPT_AUDIO_PNL_VHF1_VOL_BT))%{if}"
            "%((L:MD11_PED_CPT_AUDIO_PNL_VHF1_VOL_KB) 10 *)%!d!%%%{else}Disabled%{end})")
        self.assertEqual("Captain VHF1 Volume", label)
        self.assertEqual({}, value_map)


class CompanionVarTests(unittest.TestCase):
    """A trailing if/else keyed on a SECOND L:var describes that var, not this control. The EFIS
    minimums caps read their own value and then the mode switch's var for the Baro/Radio word;
    lifting that word gave a 0-15000 ft value knob a {Radio, Baro} map and MSFSBA a two-entry
    combo whose wheel moved the minimums, never the mode."""

    def test_if_else_words_keyed_on_a_second_var_are_not_this_controls_positions(self):
        label, var, value_map = g.parse_tooltip(
            "Captain Minimums Setting (%((L:MD11_CAP_MINIMUMS))%!d! "
            "%((L:MD11_LECP_MINIMUMS_KB))%{if}Baro%{else}Radio%{end})")
        self.assertEqual("Captain Minimums Setting", label)
        self.assertEqual("MD11_CAP_MINIMUMS", var)
        self.assertEqual({}, value_map)

    def test_if_else_words_on_the_state_var_itself_are_still_positions(self):
        # The mode switch's own tooltip: one var, and the words are its positions.
        label, var, value_map = g.parse_tooltip(
            "Captain Minimums Mode (%((L:MD11_LECP_MINIMUMS_KB))%{if}Baro%{else}Radio%{end})")
        self.assertEqual("Captain Minimums Mode", label)
        self.assertEqual("MD11_LECP_MINIMUMS_KB", var)
        self.assertEqual({"1": "Baro", "0": "Radio"}, value_map)

    def test_a_nested_two_var_expression_also_yields_no_positions(self):
        # The third and fourth controls the rule touches, deliberately: the ECON and TRIM AIR
        # buttons' tooltips NEST the air-system selector around their own var, so the expression
        # names two L:vars and the Off/On words are dropped with the caps'. Nothing consumes
        # them — a button never reads `values`, and the `state` block that composes its spoken
        # position is generated separately and is unchanged.
        label, var, value_map = g.parse_tooltip(
            "ECON Mode (%((L:MD11_OVHD_PNEU_SYSTEM_SEL_BT))%{if}"
            "%((L:MD11_OVHD_PNEU_ECON_BT))%{if}Off%{else}On%{end}%{else}Auto%{end})")
        self.assertEqual("ECON Mode", label)
        self.assertEqual("MD11_OVHD_PNEU_SYSTEM_SEL_BT", var)
        self.assertEqual({}, value_map)


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

    def test_two_nodes_firing_the_same_events_are_one_control(self):
        # An event id IS the action on this aircraft, so identical events mean one physical
        # control reached from two 3D nodes — however differently the nodes are named. The
        # MD11_-prefixed id wins over a raw 3D name; between two MD11_ ids the shortest wins.
        out = g.finalize_controls([
            ctl("GA_BT_ALT", events={"LEFT_BUTTON_DOWN": 77851, "LEFT_BUTTON_UP": 77852}),
            ctl("MD11_THR_GA_BT", label="Go Around Mode", events={"LEFT_BUTTON_DOWN": 77851, "LEFT_BUTTON_UP": 77852}),
            ctl("MD11_EFB_TOGGLE", events={"LEFT_BUTTON_DOWN": 94465}),
            ctl("MD11_EFB_TOGGLE_FO", events={"LEFT_BUTTON_DOWN": 94465}),
        ])
        self.assertEqual(["MD11_THR_GA_BT", "MD11_EFB_TOGGLE"], [c["node_id"] for c in out])
        # And the surviving EFB row no longer claims a seat the events do not distinguish.
        self.assertEqual("EFB Toggle", out[1]["label"])

    def test_the_survivor_does_not_depend_on_the_order_nodes_were_collected_in(self):
        ev = {"LEFT_BUTTON_DOWN": 94465}
        pair = [ctl("MD11_EFB_TOGGLE_FO", events=ev), ctl("MD11_EFB_TOGGLE", events=ev)]
        self.assertEqual(["MD11_EFB_TOGGLE"], [c["node_id"] for c in g.finalize_controls(pair)])

    def test_a_control_with_no_events_is_never_deduped(self):
        # Annunciators and any node the exporter gave no events must not collapse onto each
        # other just because both maps are empty.
        out = g.finalize_controls([ctl("MD11_A_SW", kind="switch", events={}), ctl("MD11_B_SW", kind="switch", events={})])
        self.assertEqual(2, len(out))

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

    def test_curated_value_map_replaces_a_leaked_or_missing_one(self):
        # The parser cannot see these knobs' positions (their tooltips carry no %{case}), and on
        # two of them the freighter/pax WORDING used to leak into the map through the inline
        # if/else: {"1": "Courier Cabin", "0": "Forward Cabin"} on an 8-position knob. The parser
        # no longer lifts it (VariantWordingTests); a curated map still wins over any parsed one.
        out = g.finalize_controls([
            ctl("MD11_OVHD_PNEU_FWD_CAB_TEMP", kind="knob", label="Forward Cabin/Courier Cabin Temperature",
                value_map={"1": "Courier Cabin", "0": "Forward Cabin"}),
            ctl("MD11_OVHD_PNEU_COCKPIT_TEMP", kind="knob", label="Cockpit Temperature"),
            ctl("MD11_OVHD_PNEU_FWD_CARGO_TEMP", kind="knob", label="Forward Lower Cargo Temperature"),
            ctl("MD11_OVHD_PNEU_AFT_CARGO_TEMP", kind="knob", label="Aft Lower Cargo Temperature"),
        ])
        by = {c["node_id"]: c for c in out}
        self.assertEqual("1 (full cold)", by["MD11_OVHD_PNEU_FWD_CAB_TEMP"]["value_map"]["0"])
        self.assertEqual("8 (full hot)", by["MD11_OVHD_PNEU_FWD_CAB_TEMP"]["value_map"]["7"])
        self.assertEqual(8, len(by["MD11_OVHD_PNEU_COCKPIT_TEMP"]["value_map"]))
        self.assertEqual({"0": "1 (full cold)", "1": "2", "2": "3 (full hot)"},
                         by["MD11_OVHD_PNEU_FWD_CARGO_TEMP"]["value_map"])
        self.assertEqual(7, len(by["MD11_OVHD_PNEU_AFT_CARGO_TEMP"]["value_map"]))
        self.assertEqual("7 (full hot)", by["MD11_OVHD_PNEU_AFT_CARGO_TEMP"]["value_map"]["6"])
        # The zone is named by LABEL_FIXES too: TFDi's tooltip names it once per airframe variant.
        self.assertEqual("Forward Zone Temperature", by["MD11_OVHD_PNEU_FWD_CAB_TEMP"]["label"])

    def test_temperature_positions_are_generated_from_the_count(self):
        self.assertEqual({"0": "1 (full cold)", "1": "2", "2": "3 (full hot)"}, g.temperature_positions(3))
        self.assertEqual("4", g.temperature_positions(8)["3"])

    def test_the_fire_test_button_is_named_for_every_loop_it_tests(self):
        # TFDi's tooltip says "APU Fire Test"; their Systems Guide calls the button ENG/APU FIRE
        # TEST and says it lights ENG 1, 2, 3 and APU FIRE — and a live press (2026-09-06) lit all
        # four plus the master warning. A pilot told "APU" would think the engine loops are untestable.
        out = g.finalize_controls([ctl("MD11_AOVHD_FIRETEST_BT", label="APU Fire Test",
                                       events={"LEFT_BUTTON_DOWN": 73748, "LEFT_BUTTON_UP": 73749})])
        self.assertEqual("Engine and APU Fire Test", out[0]["label"])
        self.assertEqual("curated", out[0]["label_source"])


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

    def test_a_guard_whose_tooltip_reads_the_covered_button_still_reads_its_own_cover(self):
        # TFDi's Fuel Dump cover tooltip reads the BUTTON's var for its Open/Closed wording, so
        # parse_tooltip hands back MD11_OVHD_FUEL_DUMP_BT (that is the parser doing its job). A
        # guard's position is its own node id — the var TFDi animates the cover on — never the
        # control under it: keyed on the button, the auto-open read "valve closed" as "cover
        # closed" and lowered an open cover onto the press. Same shape on the Fuel Dump
        # Emergency Stop, Center Gear Uplock and Main Cargo Door Arm covers.
        label, state_var, value_map = g.parse_tooltip(
            "Fuel Dump (%((L:MD11_OVHD_FUEL_DUMP_BT))%{if}Open%{else}Closed%{end})")
        self.assertEqual("MD11_OVHD_FUEL_DUMP_BT", state_var)
        grd = ctl("MD11_OVHD_FUEL_DUMP_GRD", kind="guard", label=label, state_var=state_var,
                  value_map=value_map, events={"LEFT_BUTTON_DOWN": 2})
        dump = ctl("MD11_OVHD_FUEL_DUMP_BT", label="Fuel Dump", guard_id="MD11_OVHD_FUEL_DUMP_GRD",
                   value_map={"1": "Open", "0": "Closed"}, events={"LEFT_BUTTON_DOWN": 1})
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([dump, grd]))}
        self.assertEqual("MD11_OVHD_FUEL_DUMP_GRD", out["MD11_OVHD_FUEL_DUMP_GRD"]["state_var"])
        self.assertEqual({}, out["MD11_OVHD_FUEL_DUMP_GRD"]["value_map"])
        self.assertEqual({"var": "MD11_OVHD_FUEL_DUMP_GRD", "on": "Open", "off": "Closed"},
                         out["MD11_OVHD_FUEL_DUMP_GRD"]["state"]["latch"])
        # The button under it is untouched: its own var, its own latch words.
        self.assertEqual("MD11_OVHD_FUEL_DUMP_BT", out["MD11_OVHD_FUEL_DUMP_BT"]["state_var"])
        self.assertEqual({"var": "MD11_OVHD_FUEL_DUMP_BT", "on": "Open", "off": "Closed"},
                         out["MD11_OVHD_FUEL_DUMP_BT"]["state"]["latch"])

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

    def test_colliding_lamp_names_are_curated_apart(self):
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls(
            [self.lamp("MD11_GSL_MST_CAUT_LT"), self.lamp("MD11_GSR_MST_CAUT_LT")]))}
        self.assertEqual("Captain Master Caution CAUT light", out["MD11_GSL_MST_CAUT_LT"]["label"])
        self.assertEqual("First Officer Master Caution CAUT light", out["MD11_GSR_MST_CAUT_LT"]["label"])
        self.assertEqual("curated", out["MD11_GSL_MST_CAUT_LT"]["label_source"])
        # The override touches the label only: the legend and lit word still come from the tables.
        self.assertEqual("CAUT", out["MD11_GSL_MST_CAUT_LT"]["state"]["lamps"][0]["legend"])

    def test_two_lamps_with_one_spoken_name_refuse_to_generate(self):
        from unittest import mock
        # Force a collision through the override table itself: two different lamps curated onto
        # one name must stop the generator, not ship as two indistinguishable Ctrl+M rows.
        with mock.patch.dict(g.LAMP_NAME_OVERRIDES, {"MD11_A_LT": "Same light", "MD11_B_LT": "Same light"}):
            with self.assertRaises(ValueError):
                g.apply_state(g.finalize_controls([self.lamp("MD11_A_LT"), self.lamp("MD11_B_LT")]))

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


class VariantWordingTests(unittest.TestCase):
    """D1: an airframe variant flag (LABEL_ONLY_VARS) picks a tooltip's WORDING, never a position.
    A label that reads one has a wording per variant and the parser cannot choose between them:
    collapsed, the cabin knobs read "Forward Cabin/Courier Cabin Temperature" on every airframe."""

    FWD = "%((L:MD11_EFB_IS_CARGO))%{if}Courier Cabin%{else}Forward Cabin%{end} Temperature"

    def test_a_label_reading_a_variant_flag_refuses_to_generate_without_a_curated_name(self):
        with self.assertRaises(ValueError) as cm:
            g.parse_tooltip(self.FWD, node_id="MD11_OVHD_PNEU_NEW_CAB_TEMP")
        self.assertIn("MD11_OVHD_PNEU_NEW_CAB_TEMP", str(cm.exception))
        self.assertIn("LABEL_FIXES", str(cm.exception))

    def test_with_a_curated_name_the_variant_words_reach_neither_the_label_nor_the_map(self):
        label, state_var, value_map = g.parse_tooltip(self.FWD, node_id="MD11_OVHD_PNEU_FWD_CAB_TEMP")
        self.assertEqual("Temperature", label)
        self.assertIsNone(state_var)
        self.assertEqual({}, value_map)

    def test_a_variant_flag_in_the_state_expression_yields_no_positions(self):
        # Not in TFDi's package today. The rule is that a variant's words are never positions,
        # wherever the tooltip carries them; here the label itself reads no flag, so no error.
        label, state_var, value_map = g.parse_tooltip(
            "Cargo Heat (%((L:MD11_EFB_IS_CARGO))%{if}Installed%{else}Not installed%{end})")
        self.assertEqual("Cargo Heat", label)
        self.assertIsNone(state_var)
        self.assertEqual({}, value_map)

    def test_the_zone_and_standby_std_names_are_curated(self):
        out = g.finalize_controls([
            ctl("MD11_OVHD_PNEU_FWD_CAB_TEMP", kind="knob", label="Temperature"),
            ctl("MD11_OVHD_PNEU_MID_CAB_TEMP", kind="knob", label="Temperature"),
            ctl("MD11_MIP_ISFD_STD_BT"),
        ])
        named = {c["node_id"]: (c["label"], c["label_source"]) for c in out}
        self.assertEqual(("Forward Zone Temperature", "curated"), named["MD11_OVHD_PNEU_FWD_CAB_TEMP"])
        self.assertEqual(("Middle Zone Temperature", "curated"), named["MD11_OVHD_PNEU_MID_CAB_TEMP"])
        self.assertEqual(("Standby Altimeter STD", "curated"), named["MD11_MIP_ISFD_STD_BT"])


class InlineIfGuardTests(unittest.TestCase):
    """D1: an inline %{if}/%{else} lifts positions under the trailing path's rule -- the label
    names exactly one L:var. It used to lift whatever block it met first."""

    def test_the_fcp_mode_knobs_keep_their_mode_words_on_their_export(self):
        # Intended read-only mode rows (Md11ExportBacked): each label names ONE var, its mode
        # export, so the words stay; the button beside the row switches the mode. Passes before
        # and after the change -- it pins that the guard leaves these three alone.
        for tooltip, label, var, positions in (
            ("Autopilot %((L:MD11_AP_HDG_TRK))%{if}Track%{else}Heading%{end} Select",
             "Autopilot Heading/Track Select", "MD11_AP_HDG_TRK", {"1": "Track", "0": "Heading"}),
            ("Autopilot %((L:MD11_AP_IAS_MACH))%{if}MACH%{else}IAS%{end} Select",
             "Autopilot IAS/MACH Select", "MD11_AP_IAS_MACH", {"1": "MACH", "0": "IAS"}),
            ("Autopilot %((L:MD11_AP_VS_FPA))%{if}FPA%{else}VS%{end} Select",
             "Autopilot VS/FPA Select", "MD11_AP_VS_FPA", {"1": "FPA", "0": "VS"}),
        ):
            with self.subTest(var=var):
                self.assertEqual((label, var, positions), g.parse_tooltip(tooltip))

    def test_an_inline_block_beside_a_second_var_lifts_no_positions(self):
        # The minimums caps' shape, inline instead of trailing: the Baro/Radio words describe the
        # mode switch's var, not the value this control reads first.
        _, var, value_map = g.parse_tooltip(
            "Captain Minimums %((L:MD11_CAP_MINIMUMS))%!d! "
            "%((L:MD11_LECP_MINIMUMS_KB))%{if}Baro%{else}Radio%{end}")
        self.assertEqual("MD11_CAP_MINIMUMS", var)
        self.assertEqual({}, value_map)


class CaseLabelTests(unittest.TestCase):
    """D2: a %{case} position's label runs to the next position, not to the first '%'."""

    APU = ("APU Fire Handle (%((L:MD11_AOVHD_APUFIRE_KB))%{case}%{:0}Bottle 1"
           "%{:1}%((L:MD11_AOVHD_APUFIRE_SW))%{if}Shutoff%{else}Normal%{end}%{:2}Bottle 2%{end})")

    def test_a_nested_block_names_its_position_by_the_resting_word(self):
        # TFDi's APU fire handle tooltip, verbatim. Its centre used to vanish, leaving the combo
        # blank at rest with no way to select the centre.
        label, var, value_map = g.parse_tooltip(self.APU)
        self.assertEqual("APU Fire Handle", label)
        self.assertEqual("MD11_AOVHD_APUFIRE_KB", var)
        self.assertEqual({"0": "Bottle 1", "1": "Normal", "2": "Bottle 2"}, value_map)
        self.assertEqual(["0", "1", "2"], list(value_map))

    def test_a_literal_percent_sign_stays_in_a_position_name(self):
        _, _, value_map = g.parse_tooltip(
            "Thrust Cue (%((L:MD11_THR_X_SW))%{case}%{:0}Off%{:1}70%% N1%{end})")
        self.assertEqual({"0": "Off", "1": "70% N1"}, value_map)

    def test_an_empty_position_is_reported_and_kept_out_of_the_map(self):
        empty = []
        _, _, value_map = g.parse_tooltip(
            "Test Selector (%((L:MD11_OVHD_X_SEL_SW))%{case}%{:0}Off%{:1}%{:2}On%{end})",
            node_id="MD11_OVHD_X_SEL_SW", empty_cases=empty)
        self.assertEqual({"0": "Off", "2": "On"}, value_map)
        self.assertEqual(["1"], empty)

    def test_live_data_in_a_position_is_not_reported_as_empty(self):
        empty = []
        g.parse_tooltip("Readout (%((L:MD11_X_KB))%{case}%{:0}Off%{:1}%((L:MD11_X_KB) 10 *)%!d!%{end})",
                        empty_cases=empty)
        self.assertEqual([], empty)

    def test_the_engine_fire_handles_nested_case_is_unchanged(self):
        # TFDi's Engine 1 handle, verbatim: the rotation's whole %{case} sits inside position 2 of
        # the pull's, and the flat scan lets the nested 0/1/2 overwrite the pull's words -- which
        # is what the shipped map carries. Changing it is a position change on an operable
        # control, which the regeneration review stops on (spec D9); this rule leaves it alone.
        _, var, value_map = g.parse_tooltip(
            "Engine 1 Fire Handle (%((L:MD11_AOVHD_ENG1FIRE_SW))%{case}%{:0}Normal"
            "%{:1}Generator Field Disconnect%{:2}%((L:MD11_AOVHD_ENG1FIRE_KB))%{case}%{:0}Bottle 1"
            "%{:1}Fuel and Hydraulic Disconnect%{:2}Bottle 2%{end}%{end})")
        self.assertEqual("MD11_AOVHD_ENG1FIRE_SW", var)
        self.assertEqual({"0": "Bottle 1", "1": "Fuel and Hydraulic Disconnect", "2": "Bottle 2"},
                         value_map)


class EmptyCaseReportTests(unittest.TestCase):
    def test_main_counts_and_prints_every_empty_position(self):
        tooltip = "Test Selector (%((L:MD11_OVHD_X_SEL_SW))%{case}%{:0}Off%{:1}%{:2}On%{end})"
        with tempfile.TemporaryDirectory() as tmp:
            pkg, wasm = write_package(tmp, {"FlightDeck/Overhead.xml": use_template(
                "TFDi_Design_MD11_Switch_Template", NODE_ID="MD11_OVHD_X_SEL_SW", TOOLTIPID=tooltip,
                LEFT_BUTTON_DOWN="1", RIGHT_BUTTON_DOWN="2")})
            data, err = run_main(pkg, wasm, os.path.join(tmp, "map.json"))
        self.assertEqual(1, data["counts"]["empty_case_labels"])
        self.assertIn("MD11_OVHD_X_SEL_SW %{:1}", err)
        self.assertEqual({"0": "Off", "2": "On"}, data["controls"][0]["value_map"])


class TrailingStateTests(unittest.TestCase):
    """D7: the trailing state block ends where the tooltip's own text resumes."""

    def test_a_parenthetical_after_the_state_block_stays_in_the_label(self):
        label, var, value_map = g.parse_tooltip(
            "Nosewheel Steering (%((L:MD11_PED_NWS_SW))%{if}On%{else}Off%{end}) (Tiller)")
        self.assertEqual("Nosewheel Steering (Tiller)", label)
        self.assertEqual("MD11_PED_NWS_SW", var)
        self.assertEqual({"1": "On", "0": "Off"}, value_map)

    def test_a_parenthetical_inside_the_state_block_stays_in_its_position(self):
        # Every real tooltip's shape: the block runs to the end, and a lazy match must not stop at
        # the ')' inside a position's name. Passes before and after the change.
        label, _, value_map = g.parse_tooltip(
            "Slat Handle (%((L:MD11_X_SW))%{case}%{:0}Up (Retracted)%{:1}Down%{end})")
        self.assertEqual("Slat Handle", label)
        self.assertEqual({"0": "Up (Retracted)", "1": "Down"}, value_map)


class NestedTemplateTests(unittest.TestCase):
    """D6: the parse is flat, so a nested <UseTemplate> must stop the generator, not merge."""

    def test_a_nested_use_template_names_the_file_and_both_nodes(self):
        xml = ('<UseTemplate Name="TFDi_Design_MD11_Button_Template">'
               '<TOOLTIPID>Outer Button</TOOLTIPID>'
               + use_template("TFDi_Design_MD11_Annunciator", NODE_ID="MD11_OVHD_INNER_LT")
               + '<NODE_ID>MD11_OVHD_OUTER_BT</NODE_ID></UseTemplate>')
        with tempfile.TemporaryDirectory() as tmp:
            pkg, _ = write_package(tmp, {"FlightDeck/Panel.xml": xml})
            with self.assertRaises(ValueError) as cm:
                g.collect(pkg)
        message = str(cm.exception)
        self.assertIn("FlightDeck/Panel.xml", message)
        self.assertIn("MD11_OVHD_OUTER_BT", message)
        self.assertIn("MD11_OVHD_INNER_LT", message)

    def test_sibling_blocks_are_not_nesting(self):
        # TFDi's Lighting.xml writes 58 of its (skipped) blocks as Name = "..." (48
        # MD11_IntegralLighting_Template, 10 MD11_PA_Lights_Template). They sit BETWEEN the other
        # blocks, never inside one, and since D12 they are read too -- as the skipped templates
        # they are -- so the nesting check sees their bodies as well.
        xml = (use_template("TFDi_Design_MD11_Annunciator", NODE_ID="MD11_OVHD_A_LT")
               + '<UseTemplate Name = "MD11_IntegralLighting_Template">'
                 '<NODE_ID>MD11_OVHD_B_KB</NODE_ID></UseTemplate>'
               + use_template("TFDi_Design_MD11_Annunciator", NODE_ID="MD11_OVHD_C_LT"))
        with tempfile.TemporaryDirectory() as tmp:
            pkg, _ = write_package(tmp, {"FlightDeck/Lighting.xml": xml})
            controls, stats = g.collect(pkg)
        self.assertEqual(["MD11_OVHD_A_LT", "MD11_OVHD_C_LT"], [c["node_id"] for c in controls])
        self.assertEqual(1, stats["skipped_template:MD11_IntegralLighting_Template"])


class GuardLabelTests(unittest.TestCase):
    """D3 and D8: a guard is named after the FINAL label of the control it covers."""

    def test_a_guard_uses_its_controls_final_label(self):
        # finalize_controls repairs the covered control's label itself -- a LABEL_FIXES name, or a
        # derived label losing its " button" -- so the guard must be named after the repaired one.
        fixed = ctl("MD11_OVHD_L_RAIN_REPLNT_BT", guard_id="MD11_OVHD_L_RAIN_REPLNT_GRD",
                    events={"LEFT_BUTTON_DOWN": 1, "LEFT_BUTTON_UP": 2})
        derived = ctl("MD11_OVHD_FUEL_X_BT", guard_id="MD11_OVHD_FUEL_X_GRD",
                      events={"LEFT_BUTTON_DOWN": 3, "LEFT_BUTTON_UP": 4})
        derived["label"] = "Fuel x button"       # humanize()'s form, as collect() stores it
        out = g.finalize_controls([
            ctl("MD11_OVHD_L_RAIN_REPLNT_GRD", kind="guard", events={"LEFT_BUTTON_DOWN": 5}),
            fixed,
            ctl("MD11_OVHD_FUEL_X_GRD", kind="guard", events={"LEFT_BUTTON_DOWN": 6}),
            derived,
        ])
        labels = {c["node_id"]: c["label"] for c in out}
        self.assertEqual("Left Rain Repellent guard", labels["MD11_OVHD_L_RAIN_REPLNT_GRD"])
        self.assertEqual("Fuel x guard", labels["MD11_OVHD_FUEL_X_GRD"])

    def test_a_guard_over_a_breaker_or_an_mcdu_key_is_still_named_as_a_guard(self):
        # The breaker branch read the guard's own id as a grid position ("GRD ..."), and the MCDU
        # branch as a keycap ("CLR_GRD"), because both were tested before the guard branch.
        out = g.finalize_controls([
            ctl("MD11_BKR_BWU_C24", label="Tank 1 Transfer Pump Power Breaker",
                guard_id="MD11_BKR_BWU_C24_GRD", events={"LEFT_BUTTON_DOWN": 1}),
            ctl("MD11_BKR_BWU_C24_GRD", kind="guard", label="Breaker Guard", events={"LEFT_BUTTON_DOWN": 2}),
            ctl("MD11_LMCDU_CLR_BT", guard_id="MD11_LMCDU_CLR_GRD",
                events={"LEFT_BUTTON_DOWN": 3, "LEFT_BUTTON_UP": 4}),
            ctl("MD11_LMCDU_CLR_GRD", kind="guard", events={"LEFT_BUTTON_DOWN": 5}),
        ])
        labels = {c["node_id"]: c["label"] for c in out}
        self.assertEqual("C24 Tank 1 Transfer Pump Power guard", labels["MD11_BKR_BWU_C24_GRD"])
        self.assertEqual("CLR guard", labels["MD11_LMCDU_CLR_GRD"])


class LampOwnerTests(unittest.TestCase):
    """D5: a lamp under two knob/switch stems belongs to the longer, more specific one."""

    def test_a_lamp_under_two_stems_belongs_to_the_longer_one(self):
        short = ctl("MD11_OVHD_X_KB", kind="knob", label="X")
        longer = ctl("MD11_OVHD_X_TEST_KB", kind="knob", label="X Test")
        owner, rest = g._owner_by_stem("MD11_OVHD_X_TEST_ON_LT", [short, longer])
        self.assertEqual("MD11_OVHD_X_TEST_KB", owner["node_id"])
        self.assertEqual("ON", rest)
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls(
            [short, longer, ctl("MD11_OVHD_X_TEST_ON_LT", kind="annun")]))}
        self.assertEqual("X Test ON light", out["MD11_OVHD_X_TEST_ON_LT"]["label"])


class UseTemplateSpellingTests(unittest.TestCase):
    """D12: `Name = "..."` is the same attribute as `Name="..."`. TFDi's Lighting.xml spells 58
    blocks that way (48 MD11_IntegralLighting_Template, 10 MD11_PA_Lights_Template: skipped
    templates, so the map does not change), and a CONTROL spelt so used to vanish without a trace."""

    def test_a_control_written_with_spaces_around_the_equals_sign_is_collected(self):
        xml = "".join(
            f'<UseTemplate Name{equals}"TFDi_Design_MD11_Button_Template">'
            f"<TOOLTIPID>Test {n}</TOOLTIPID><NODE_ID>MD11_OVHD_X{n}_BT</NODE_ID>"
            f"<LEFT_BUTTON_DOWN>{n}</LEFT_BUTTON_DOWN></UseTemplate>"
            for n, equals in enumerate(("=", " = ", " =", "= "), start=1))
        with tempfile.TemporaryDirectory() as tmp:
            pkg, _ = write_package(tmp, {"FlightDeck/Overhead.xml": xml})
            controls, _ = g.collect(pkg)
        self.assertEqual(["MD11_OVHD_X1_BT", "MD11_OVHD_X2_BT", "MD11_OVHD_X3_BT", "MD11_OVHD_X4_BT"],
                         [c["node_id"] for c in controls])


_REAL_WALK = os.walk


def _walk_in_order(reverse):
    """os.walk with every folder and file list sorted ascending or descending -- two orders a file
    system is free to hand back. Sorting `dirs` in place steers the rest of the real walk."""
    def walk(top, *args, **kwargs):
        for root, dirs, files in _REAL_WALK(top, *args, **kwargs):
            dirs.sort(reverse=reverse)
            files.sort(reverse=reverse)
            yield root, dirs, files
    return walk


class DeterministicOutputTests(unittest.TestCase):
    """D4: the map depends on the package, never on the order the file system lists it in."""

    def test_the_surviving_lamp_does_not_depend_on_the_order_nodes_were_collected_in(self):
        twin = ctl("MD11_OVHD_ELEC_X_OFF_LT001", kind="annun", state_var="MD11_OVHD_ELEC_X_OFF_LT")
        own = ctl("MD11_OVHD_ELEC_X_OFF_LT", kind="annun")
        for order in ([twin, own], [own, twin]):
            with self.subTest(first=order[0]["node_id"]):
                kept = g.finalize_controls([dict(c) for c in order])
                self.assertEqual(["MD11_OVHD_ELEC_X_OFF_LT"], [c["node_id"] for c in kept])

    def test_the_lamp_named_after_its_var_beats_a_shorter_twin(self):
        # The audio panels' MD11_CPT_* nodes are SHORTER than the MD11_PED_* var they light, so
        # _second_clickspots' rule alone (shortest wins) would rename 34 of the shipped map's 37
        # deduplicated lamps; the var-named node is the one the map has always kept.
        own = ctl("MD11_PED_CPT_AUDIO_PNL_VHF1_MIC_LT", kind="annun")
        twin = ctl("MD11_CPT_AUDIO_PNL_VHF1_MIC_LT", kind="annun",
                   state_var="MD11_PED_CPT_AUDIO_PNL_VHF1_MIC_LT")
        kept = g.finalize_controls([twin, own])
        self.assertEqual(["MD11_PED_CPT_AUDIO_PNL_VHF1_MIC_LT"], [c["node_id"] for c in kept])

    def test_the_map_is_byte_identical_whatever_order_the_file_system_lists_folders_in(self):
        # Two folders, each holding one copy of a duplicated lamp and of a duplicated button node.
        # The lamp's survivor is _second_lamps' to choose; the button's copy is the first one read,
        # which is Alpha's only when folders are read in name order.
        files = {
            "Alpha/Panel.xml": use_template("TFDi_Design_MD11_Annunciator",
                                            NODE_ID="MD11_OVHD_ELEC_X_OFF_LT001",
                                            VIS_VAR="MD11_OVHD_ELEC_X_OFF_LT")
                               + use_template("TFDi_Design_MD11_Button_Template", TOOLTIPID="Y Alpha",
                                              NODE_ID="MD11_OVHD_ELEC_Y_BT",
                                              LEFT_BUTTON_DOWN="1", LEFT_BUTTON_UP="2"),
            "Beta/Panel.xml": use_template("TFDi_Design_MD11_Annunciator", NODE_ID="MD11_OVHD_ELEC_X_OFF_LT")
                              + use_template("TFDi_Design_MD11_Button_Template", TOOLTIPID="Y Beta",
                                             NODE_ID="MD11_OVHD_ELEC_Y_BT",
                                             LEFT_BUTTON_DOWN="1", LEFT_BUTTON_UP="2"),
        }
        outputs = []
        with tempfile.TemporaryDirectory() as tmp:
            pkg, wasm = write_package(tmp, files)
            for reverse in (False, True):
                out = os.path.join(tmp, f"map-{int(reverse)}.json")
                with mock.patch.object(os, "walk", _walk_in_order(reverse)):
                    data, _ = run_main(pkg, wasm, out)
                with open(out, "rb") as fh:
                    outputs.append(fh.read())
        self.assertEqual(outputs[0], outputs[1])
        by = {c["node_id"]: c for c in data["controls"]}
        self.assertEqual("Y Alpha", by["MD11_OVHD_ELEC_Y_BT"]["label"])
        self.assertIn("MD11_OVHD_ELEC_X_OFF_LT", by)
        self.assertNotIn("MD11_OVHD_ELEC_X_OFF_LT001", by)


if __name__ == "__main__":
    unittest.main()
