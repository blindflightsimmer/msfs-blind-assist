"""Generates MSFSBlindAssist/Resources/a321_control_map.json — the iniBuilds A321LR (MSFS 2024) panels.

The A321LR shares the A330's iniBuilds code base (1,834 of its 2,732 panel-state L:vars carry the same
names) but NOT its control transport: its FS2024 input events do nothing when set or pushed from outside
(probed 2026-10-16: 240 events set by value, then pressed with _Push — no L:var moved). What DOES work is
writing the INI_* L:vars directly, the A300's way: a direct-write probe (input/lw*.out) wrote each
candidate, waited 1.5 s, recorded whether it held and what moved DOWNSTREAM (a held value alone can be a
dead mirror), then restored it.

So this map is the A330 map (tools/a330-gen) re-pointed: every A330 control whose read L:var exists on the
A321 and was proven writable becomes a direct write of the position's VALUE ("mode": "rpn",
"{value} (>L:VAR)"), keeping the A330's measured labels; A330-only systems are dropped (DROP); the
A321's own controls are added at the end. Every L:var name is validated against the A321 panel-state
list and the build FAILS on an unknown one.

Momentary keys whose state lives elsewhere use "mode": "toggle" with an "rpn": press (write the key) only
when the pick differs from the read state.

Run:  python tools/a321-gen/generate_a321_map.py
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
A330 = os.path.join(ROOT, "MSFSBlindAssist", "Resources", "a330_control_map.json")
OUT = os.path.join(ROOT, "MSFSBlindAssist", "Resources", "a321_control_map.json")
NAMES = os.path.join(HERE, "a321_lvars.txt")   # the A321's panel-state L:var names (1 per line)

LVARS = {l.strip().upper() for l in open(NAMES, encoding="utf-8") if l.strip()}

SECTIONS = [
    ("Overhead", ["ADIRS", "Flight Controls", "Fuel", "Electrical", "Hydraulics", "Fire Tests", "APU", "Bleed Air",
                  "Air Conditioning", "Pressurisation", "Ventilation", "Anti-Ice", "Exterior Lights", "Signs",
                  "Interior Lights", "Wipers", "Engine Start", "Oxygen", "Calls", "Cargo"]),
    ("Glareshield", ["Warnings", "Captain EFIS", "First officer EFIS"]),
    ("Main panel", ["Brakes", "GPWS and Terrain", "Instrument Switching", "Clocks", "Standby Instrument"]),
    ("Pedestal", ["ECAM", "Captain Radio Panel", "Transponder and TCAS", "Weather Radar"]),
]

# A330 controls with no A321 counterpart, or whose L:var exists but drives nothing here (direct-write probe;
# the single INI_LANDING_LIGHT_SWITCH reads 0 with both landing lights on — the A321 has a switch per side)
DROP = {
    "A330_PRIM1", "A330_PRIM2", "A330_PRIM3", "A330_SEC1", "A330_SEC2",
    "A330_FUEL_LSTBY", "A330_FUEL_RSTBY", "A330_FUEL_TRIM_MODE", "A330_FUEL_TK_XFR", "A330_FUEL_TK_XFR_COVER",
    "A330_INR_TK_L", "A330_INR_TK_L_COVER", "A330_INR_TK_R", "A330_INR_TK_R_COVER",
    "A330_BAT1", "A330_BAT2", "A330_EXT_A", "A330_EXT_B",
    "A330_HYD_GREEN_ENG2", "A330_HYD_BLUE", "A330_HYD_GREEN_ELEC_OFF", "A330_HYD_GREEN_ELEC_ON",
    "A330_HOT_AIR2", "A330_FWD_CARGO_TEMP", "A330_BULK_TEMP", "A330_BULK_HOT_AIR", "A330_BULK_ISO",
    "A330_PRESS_MODE", "A330_LDG_ELEV", "A330_EXTRACT", "A330_LDG_LT", "A330_LANDING_LT", "A330_CLOCK_ET",
}
# dangerous to offer as a plain write (fire, IDG, RAT, engine masters) — never re-pointed
DANGER = re.compile(r"FIRE|AGENT|IDG|RAT|ENG\d_MASTER|DITCH|IRS\d_MODE")

CONTROLS = []
SEEN = set()


def check(var):
    if var.upper() not in LVARS:
        sys.exit(f"unknown A321 L:var {var}")


def write_rpn(var):
    return "{value} (>L:" + var + ")"


def add(key, section, panel, label, kind, read=None, positions=None, mode="rpn", rpn=None, **extra):
    if key in SEEN:
        sys.exit("duplicate " + key)
    SEEN.add(key)
    for v in [read] + [l["read"] for l in extra.get("lamps", [])]:
        if v and not v.startswith(("A:", "B:")):
            check(v.split(":")[-1] if v.startswith("L:") else v)
    c = {"key": key, "section": section, "panel": panel, "label": label, "kind": kind, "mode": mode}
    if rpn:
        c["rpn"] = rpn
    if read:
        c["read"] = read
    if positions:
        c["positions"] = [{"value": v, "input": v, "label": l} for (v, l) in positions]
    c.update(extra)
    CONTROLS.append(c)


def select(key, panel, label, var, positions, section="Overhead", **extra):
    add("A321_" + key, section, panel, label, "select", var, positions, rpn=write_rpn(var), **extra)


def toggle(key, panel, label, read, press_var, positions, section="Overhead", **extra):
    add("A321_" + key, section, panel, label, "select", read, positions, mode="toggle",
        rpn="1 (>L:" + press_var + ")", **extra)
    check(press_var)


def button(key, panel, label, press_var, section="Overhead", value=1, **extra):
    check(press_var)
    add("A321_" + key, section, panel, label, "button", rpn=f"{value} (>L:{press_var})", **extra)


def status(key, panel, label, read, positions=None, section="Overhead"):
    add("A321_" + key, section, panel, label, "status", read, positions or [(0, "Off"), (1, "Lit")], mode="set")


ONOFF = [(0, "Off"), (1, "On")]
AUTO_OFF = [(1, "Auto"), (0, "Off")]
ON_OFF = [(1, "On"), (0, "Off")]

# ---------------------------------------------------------------- the A330 base, re-pointed
section_of = {p: s for s, ps in SECTIONS for p in ps}
PANEL_RENAME = {"Engine 1 Fire": None, "Engine 2 Fire": None, "APU Fire": None, "Cargo Smoke": None,
                "Recorders": None, "Display Brightness": None, "Engine Masters": None, "Captain Audio": None}
for c in json.load(open(A330, encoding="utf-8"))["controls"]:
    key, read = c["key"], c.get("read") or ""
    if key in DROP or DANGER.search(key):
        continue
    if c["panel"] in PANEL_RENAME:
        continue
    if c["kind"] == "button" or not read or read.startswith(("A:", "B:")):
        continue                       # momentary keys and input-event reads are re-done below
    var = read.split(":")[-1] if read.startswith("L:") else read
    if var.upper() not in LVARS:
        continue
    k = "A321_" + key[5:]
    lamps = [l for l in c.get("lamps") or [] if l["read"].upper() in LVARS]
    extra = {"lamps": lamps} if lamps else {}
    positions = [(p["value"], p["label"]) for p in c.get("positions") or []]
    if c["kind"] == "status":
        add(k, section_of.get(c["panel"], c["section"]), c["panel"], c["label"], "status", var,
            positions or [(0, "Off"), (1, "Lit")], mode="set")
    elif c["kind"] == "select":
        add(k, section_of.get(c["panel"], c["section"]), c["panel"], c["label"], "select", var, positions,
            rpn=write_rpn(var), **extra)
    elif c["kind"] == "continuous":
        continue                       # A330 sliders drive input-event ranges; the A321's are re-done below

# ---------------------------------------------------------------- A321 additions
O, G, M, P = "Overhead", "Glareshield", "Main panel", "Pedestal"

for n in (1, 2):
    select(f"ELAC{n}", "Flight Controls", f"ELAC {n}", f"A320_ELAC{n}_STATUS", ON_OFF,
           lamps=[{"read": f"A320_ELAC{n}_FAILURE", "legend": "fault"}])
for n in (1, 2, 3):
    var = f"A320_SEC{n}_STATUS"
    if var.upper() in LVARS:
        select(f"SEC{n}", "Flight Controls", f"SEC {n}", var, ON_OFF,
               lamps=[{"read": f"A320_SEC{n}_FAILURE", "legend": "fault"}] if f"A320_SEC{n}_FAILURE" in LVARS else [])
for n in (1, 2):
    select(f"FAC{n}", "Flight Controls", f"FAC {n}", f"A320_FAC{n}_STATUS", ON_OFF,
           lamps=[{"read": f"A320_FAC{n}_FAILURE", "legend": "fault"}])

select("FUEL_CTR1", "Fuel", "Centre tank pump 1", "INI_CENTER_TANK_LEFT", ON_OFF)
select("FUEL_CTR2", "Fuel", "Centre tank pump 2", "INI_CENTER_TANK_RIGHT", ON_OFF)

for n in (1, 2):
    select(f"BAT{n}", "Electrical", f"Battery {n}", f"INI_OVHD_ELEC_BAT_{n}_PB_IS_AUTO_SWITCH", AUTO_OFF)
select("EXT_PWR", "Electrical", "External power", "INI_EXT_PWR_ON", ONOFF,
       lamps=[{"read": "INI_EXT_PWR_AVAILABLE_AC", "legend": "available"}])

toggle("PRESS_MODE", "Pressurisation", "Cabin pressure mode", "INI_CABIN_MAN_PRESS", "INI_CABIN_MAN_PRESS_BUTTON",
       [(0, "Auto"), (1, "Manual")])
select("BLOWER", "Ventilation", "Avionics blower", "INI_VENT_BLOWER_STATE", [(0, "Auto"), (1, "Override")])
select("EXTRACT", "Ventilation", "Avionics extract", "INI_VENT_EXTRACT", [(0, "Auto"), (1, "Override")])
select("PROBE_HEAT", "Anti-Ice", "Probe and window heat", "INI_PROBE_HEAT", [(0, "Auto"), (1, "On")])

for side, var in [("Left", "A320_LANDING_LIGHT_SWITCH_LEFT"), ("Right", "A320_LANDING_LIGHT_SWITCH_RIGHT")]:
    # measured with the lights on: both read 0, and writing 2 started the retract animation (0 = ON, the top)
    select(f"LDG_LT_{side[0]}", "Exterior Lights", f"{side} landing light", var, [(0, "On"), (1, "Off"), (2, "Retract")])

select("CPT_WIPER", "Wipers", "Captain wiper", "CPT_WIPER_SWITCH", [(0, "Off"), (1, "Slow"), (2, "Fast")])
select("FO_WIPER", "Wipers", "First officer wiper", "FO_WIPER_SWITCH", [(0, "Off"), (1, "Slow"), (2, "Fast")])

select("AFT_CRG_HEAT", "Cargo", "Aft cargo heat", "INI_AFT_CRG_BUTTON", ON_OFF)

for key, label, var in [("CKPT_TEMP", "Cockpit temperature", "INI_AIRCOND_LVL_1"),
                        ("FWD_CAB_TEMP", "Forward cabin temperature", "INI_AIRCOND_LVL_2"),
                        ("AFT_CAB_TEMP", "Aft cabin temperature", "INI_AIRCOND_LVL_3")]:
    # measured: 3 asks 24 degrees, 0 asks 18 — two degrees a step, 18 to 30
    select(key, "Air Conditioning", label, var, [(i, f"{18 + 2 * i} degrees") for i in range(7)])

# ---------------------------------------------------------------- momentary keys
button("MASTER_WARN", "Warnings", "Master warning (silence)", "INI_MASTER_WARNING_COMMAND", section=G)
button("MASTER_CAUT", "Warnings", "Master caution (silence)", "INI_MASTER_CAUTION_COMMAND", section=G)
button("CPT_CHRONO", "Warnings", "Captain chrono", "INI_CPT_CHRONO_BUTTON", section=G)
button("APU_START", "APU", "APU start", "INI_APU_START_BUTTON",
       lamps=[{"read": "INI_APU_AVAILABLE", "legend": "available"}] if "INI_APU_AVAILABLE" in LVARS else [])
button("SMOKE_TEST", "Fire Tests", "Cargo smoke test", "INI_SMOKE_TEST_BUTTON", holdMs=3000)
button("CALL_ALL", "Calls", "Call all", "INI_CALL_ALL_BUTTON")
button("CALL_FWD", "Calls", "Call forward", "INI_CALL_FWD_BUTTON")
button("CLOCK_CHR", "Clocks", "Clock chrono", "INI_CHR_START_COMMAND", section=M)
button("CLOCK_RST", "Clocks", "Clock chrono reset", "INI_CHR_RESET_COMMAND", section=M)
button("ISIS_BUGS", "Standby Instrument", "Standby bugs", "INI_ISIS_BUGS_COMMAND", section=M)
for radio, var in [("VHF 1", "INI_VHF1_CPT_BUTTON"), ("VHF 2", "INI_VHF2_CPT_BUTTON"), ("VHF 3", "INI_VHF3_CPT_BUTTON")]:
    button("CPT_" + radio.replace(" ", ""), "Captain Radio Panel", f"Radio panel {radio}", var, section=P)
button("CPT_TRANSFER", "Captain Radio Panel", "Radio panel transfer (swap active and standby)",
       "INI_CPT_VHF_TRANSFER_SWITCH_COMMAND", section=P)

# ECAM page keys: the page is the L:var itself (direct write measured: the module took page 8 and moved
# its previous-page record) — page numbers as the A330's
for label, page in [("ENG", 1), ("BLEED", 5), ("PRESS", 7), ("EL/AC", 3), ("EL/DC", 4), ("HYD", 2), ("APU", 9),
                    ("COND", 6), ("DOOR", 11), ("WHEEL", 12), ("F/CTL", 10), ("FUEL", 8), ("STS", 14)]:
    add("A321_ECAM_" + re.sub(r"\W", "", label), P, "ECAM", f"ECAM {label}", "button",
        rpn=f"{page} (>L:INI_ECAM_ACTIVE_PAGE)")

# ---------------------------------------------------------------- emit
by_panel = {p: [] for _, ps in SECTIONS for p in ps}
for c in CONTROLS:
    if c["panel"] not in by_panel:
        sys.exit(f"{c['key']}: panel {c['panel']} not in SECTIONS")
    by_panel[c["panel"]].append(c)
used = [(s, [p for p in ps if by_panel[p]]) for s, ps in SECTIONS]
doc = {"version": 1, "sections": [{"name": s, "panels": ps} for s, ps in used if ps],
       "controls": [c for _, ps in SECTIONS for p in ps for c in by_panel[p]]}
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(doc, f, ensure_ascii=False, indent=1)
print(f"{len(doc['controls'])} controls -> {OUT}")
