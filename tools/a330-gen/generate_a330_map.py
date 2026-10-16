"""Generates MSFSBlindAssist/Resources/a330_control_map.json — the iniBuilds A330 (MSFS 2024) panels.

The aircraft is a streamed, ENCRYPTED package, so unlike the A300 (tools/a300-gen, generated from the
behaviour XML) nothing here is read from its files. Every control is one of the aircraft's own FS2024
input events (SimConnect EnumerateInputEvents, 649 of them), and every read-back and position below
was MEASURED live on 2026-10-16 by the differential probe (input/probe-*.txt): set the input event,
snapshot all 1,936 INI_* L:vars the panel-state file names, record what moved, put it back.

Conventions the probe found (do not "correct" them by intuition):
  * a push-button's INPUT is the opposite of its L:var: input 0 = pushed in (on/auto, light off) and
    the L:var reads 1 — PACK 1, the batteries, generators, bleeds, hydraulic pumps, PRIMs/SECs;
  * a THREE-position switch numbers from the TOP: 0 = the top position (strobe ON, no-smoking ON,
    nose light T.O., annunciator light TEST); a TWO-position switch has 1 = the top (beacon ON);
  * a momentary key (ECAM page keys, master warning/caution, calls, agents) ignores a raw value: it
    is pressed with the event's own _Push / _Release actions ("mode": "push");
  * where a switch moves no L:var at all (probe heat, the centre tank pumps, the wipers...), its read
    is the input event's own value ("B:EVENT") — the app subscribes to it.

Deliberately NOT probed (dangerous to toggle with engines running) and taken from their current
values + the HubHop preset set instead: engine masters, engine start selector, ADIRS, IR/ADR, fire
handles and agents, IDG, RAT, gear lever, parking brake. Their positions follow the conventions above.

Run:  python tools/a330-gen/generate_a330_map.py
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "MSFSBlindAssist", "Resources", "a330_control_map.json")

SECTIONS = [
    ("Overhead", ["ADIRS", "Flight Controls", "Fuel", "Electrical", "Hydraulics", "Engine 1 Fire", "Engine 2 Fire",
                  "APU Fire", "Cargo Smoke", "APU", "Bleed Air", "Air Conditioning", "Pressurisation",
                  "Ventilation", "Anti-Ice", "Exterior Lights", "Signs", "Interior Lights", "Wipers",
                  "Engine Start", "Oxygen", "Calls", "Recorders"]),
    ("Glareshield", ["Warnings", "Captain EFIS", "First officer EFIS"]),
    ("Main panel", ["Brakes", "GPWS and Terrain", "Instrument Switching", "Clocks", "Standby Instrument",
                    "Display Brightness"]),
    ("Pedestal", ["Engine Masters", "ECAM", "Captain Radio Panel", "Captain Audio", "Transponder and TCAS",
                  "Weather Radar"]),
]

CONTROLS = []


def add(key, section, panel, label, kind, event=None, read=None, positions=None, mode="set", **extra):
    c = {"key": "A330_" + key, "section": section, "panel": panel, "label": label, "kind": kind,
         "event": ("AIRLINER_" + event) if event and not event.startswith(("AIRLINER_", "ENGINE_THROTTLE")) else event,
         "mode": mode}
    if read:
        c["read"] = read
    if positions:
        c["positions"] = [{"value": v, "input": i, "label": l} for (v, i, l) in positions]
    c.update(extra)
    CONTROLS.append(c)


def direct(*labels):
    """Positions whose read-back equals the input (0..n-1)."""
    return [(i, i, l) for i, l in enumerate(labels)]


def pb(on="On", off="Off"):
    """A push-button: input 0 = pushed in (read 1), input 1 = released (read 0)."""
    return [(1, 0, on), (0, 1, off)]


def pb_direct(on="On", off="Off"):
    """A push-button whose read follows its input (input 1 = on, read 1)."""
    return [(0, 0, off), (1, 1, on)]


def select(key, sec, panel, label, event, read, positions, **extra):
    add(key, sec, panel, label, "select", event, read, positions, **extra)


def button(key, sec, panel, label, event, hold_ms=None, **extra):
    if hold_ms:
        extra["holdMs"] = hold_ms
    add(key, sec, panel, label, "button", event, mode="push", **extra)


def guard(key, sec, panel, label, event):
    select(key, sec, panel, label + " guard", event, "B:AIRLINER_" + event, direct("Closed", "Open"))


def status(key, sec, panel, label, read, positions=None):
    add(key, sec, panel, label, "status", read=read, positions=positions or [(0, 0, "Off"), (1, 1, "Lit")])


O, G, M, P = "Overhead", "Glareshield", "Main panel", "Pedestal"

# ---------------------------------------------------------------- ADIRS
for n in (1, 2, 3):
    select(f"IRS{n}_MODE", O, "ADIRS", f"IRS {n} mode", f"ADIRS{n}_MODE", f"INI_IRS{n}_STATE", direct("Off", "Nav", "Attitude"))
for n in (1, 2, 3):
    select(f"IR{n}", O, "ADIRS", f"IR {n}", f"IR{n}", f"INI_IR{n}_STATE", pb(), lamps=[{"read": f"INI_IR{n}_FAULT", "legend": "fault"}])
for n in (1, 2, 3):
    select(f"ADR{n}", O, "ADIRS", f"ADR {n}", f"ADIR{n}", f"INI_ADR{n}_STATE", pb())
for n in (1, 2, 3):
    status(f"IRS{n}_ALIGNED", O, "ADIRS", f"IRS {n} aligned", f"INI_IRS{n}_ALIGNED", [(0, 0, "No"), (1, 1, "Yes")])

# ---------------------------------------------------------------- Flight controls
for label, ev, var in [("PRIM 1", "FLTCTL_PRIM1", "INI_PRIM1_STATUS"), ("PRIM 2", "FTL_CTL_PRIM2", "INI_PRIM2_STATUS"),
                       ("PRIM 3", "FTL_CTL_PRIM3", "INI_PRIM3_STATUS"), ("SEC 1", "FLTCTL_SEC1", "INI_SEC1_STATUS"),
                       ("SEC 2", "FTL_CTL_SEC2", "INI_SEC2_STATUS")]:
    select(label.replace(" ", ""), O, "Flight Controls", label, ev, var, pb())

# ---------------------------------------------------------------- Fuel
for label, ev, var in [("Left pump 1", "FUEL_ENG1_L1", "INI_OUTER_TANK_LEFT"), ("Left pump 2", "FUEL_ENG1_L2", "INI_INNER_TANK_LEFT"),
                       ("Left standby pump", "FUEL_ENG1_LSTBY", "INI_OUTER_LEFT_STANDBY_PUMP"),
                       ("Right pump 1", "FUEL_ENG2_R1", "INI_INNER_TANK_RIGHT"), ("Right pump 2", "FUEL_ENG2_R2", "INI_OUTER_TANK_RIGHT"),
                       ("Right standby pump", "FUEL_ENG2_RSTBY", "INI_OUTER_RIGHT_STANDBY_PUMP")]:
    select(ev.replace("FUEL_ENG", "FUEL").replace("_", ""), O, "Fuel", label, ev, var, pb())
for label, ev in [("Centre tank left pump", "FUEL_CTR_L"), ("Centre tank right pump", "FUEL_CTR_R"),
                  ("Centre tank transfer", "FUEL_CTR_XFR")]:
    select(ev, O, "Fuel", label, ev, "B:AIRLINER_" + ev, [(0, 0, "On"), (1, 1, "Off")])
select("FUEL_XFEED", O, "Fuel", "Crossfeed", "FUEL_XFEED", "INI_XFEED_TRANSFER_ON", pb_direct("Open", "Shut"))
select("FUEL_TRIM_MODE", O, "Fuel", "Trim tank mode", "FUEL_MODE", "INI_TRIM_TANK_MODE", direct("Auto", "Forward"))
guard("FUEL_TK_XFR_COVER", O, "Fuel", "Outer tank transfer", "FUEL_TK_XFR_COVER")
select("FUEL_TK_XFR", O, "Fuel", "Outer tank transfer", "FUEL_TK_XFR", "INI_FUEL_XFR_BUTTON", pb_direct("On", "Auto"))
guard("INR_TK_L_COVER", O, "Fuel", "Inner tank left split", "INR_TK_L_COVER")
select("INR_TK_L", O, "Fuel", "Inner tank left split", "INR_TK_L", "INI_INR_TK_L_STATE", pb_direct("On", "Off"))
guard("INR_TK_R_COVER", O, "Fuel", "Inner tank right split", "INR_TK_R_COVER")
select("INR_TK_R", O, "Fuel", "Inner tank right split", "INR_TK_R", "INI_INR_TK_R_STATE", pb_direct("On", "Off"))

# ---------------------------------------------------------------- Electrical
select("BAT1", O, "Electrical", "Battery 1", "ELEC_BAT1", "INI_OVHD_ELEC_BAT_1_PB_IS_AUTO", pb("Auto"))
select("BAT2", O, "Electrical", "Battery 2", "ELEC_BAT2", "INI_OVHD_ELEC_BAT_2_PB_IS_AUTO", pb("Auto"))
select("APU_BAT", O, "Electrical", "APU battery", "ELEC_APU_BAT", "INI_OVHD_ELEC_BAT_3_PB_IS_AUTO", pb("Auto"))
select("BAT_DISPLAY", O, "Electrical", "Battery voltage display", "ELEC_BATT_TOGGLE", "INI_BATTERY_OVERHEAD_SELECTOR", direct("Battery 1", "Battery 2", "APU battery"))
select("GEN1", O, "Electrical", "Generator 1", "ELEC_GEN1", "INI_ENG1_GEN_ON", pb())
select("GEN2", O, "Electrical", "Generator 2", "ELEC_GEN2", "INI_ENG2_GEN_ON", pb())
select("APU_GEN", O, "Electrical", "APU generator", "ELEC_APU_GEN", "INI_APU_GEN_ON", pb())
select("EXT_A", O, "Electrical", "External power A", "ELEC_EXTA", "INI_GEN_EXT_A_ONLINE", pb_direct("On", "Off"),
       lamps=[{"read": "INI_GPU_AVAIL", "legend": "external power available"}])
select("EXT_B", O, "Electrical", "External power B", "ELEC_EXTB", "INI_GEN_EXT_B_ONLINE", pb_direct("On", "Off"))
select("BUS_TIE", O, "Electrical", "Bus tie", "ELEC_BUS_TIE", "INI_BUS_TIE", pb("Auto"))
guard("AC_ESS_COVER", O, "Electrical", "AC essential feed", "ELEC_AC_ESS_COVER")
select("AC_ESS", O, "Electrical", "AC essential feed", "ELEC_AC_ESS", "INI_AC_ESS_FEED", pb_direct("Alternate", "Normal"))
select("GALLEY", O, "Electrical", "Galley", "ELEC_GALLEY", "INI_GALLEY_CAB", pb("Auto"))
select("COMMERCIAL", O, "Electrical", "Commercial", "ELEC_COMMERCIAL", "INI_COMMERCIAL_ON", pb())
for n in (1, 2):
    guard(f"IDG{n}_COVER", O, "Electrical", f"IDG {n} disconnect", f"ELEC_IDG{n}_COVER")
    button(f"IDG{n}", O, "Electrical", f"IDG {n} disconnect", f"ELEC_IDG{n}",
           lamps=[{"read": f"INI_IDG{n}_FAULT", "legend": "fault"}])

# ---------------------------------------------------------------- Hydraulics
for label, ev, var in [("Green engine 1 pump", "HYD_GREEN", "INI_ENG1_GREEN_PUMP_ON"),
                       ("Green engine 2 pump", "HYD_GREEN_ENG2", "INI_ENG2_GREEN_PUMP_ON"),
                       ("Blue engine 1 pump", "HYD_BLUE", "INI_ENG1_BLUE_PUMP_ON"),
                       ("Yellow engine 2 pump", "HYD_YELLOW", "INI_ENG2_YELLOW_PUMP_ON")]:
    guard(ev + "_COVER", O, "Hydraulics", label, ev + "_COVER")
    select(ev, O, "Hydraulics", label, ev, var, pb())
for colour, side in [("Green", "ENG1_GREEN"), ("Blue", "ENG1_BLUE"), ("Yellow", "ENG2_YELLOW")]:
    up = colour.upper()
    select(f"HYD_{up}_ELEC_OFF", O, "Hydraulics", f"{colour} electric pump", f"HYD_{up}_OFF", f"INI_{side}_ELEC_PUMP_LOWER_PB", pb("Auto"))
    guard(f"HYD_{up}_ELEC_COVER", O, "Hydraulics", f"{colour} electric pump on", f"HYD_{up}_ELEC_COVER")
    select(f"HYD_{up}_ELEC_ON", O, "Hydraulics", f"{colour} electric pump on", f"HYD_{up}_ELEC", f"INI_{side}_ELEC_PUMP_PB", pb_direct("On", "Off"))
guard("RAT_COVER", O, "Hydraulics", "RAT manual on", "HYD_RAT_MAN_COVER")
button("RAT", O, "Hydraulics", "RAT manual on", "HYD_RAT_MAN")

# ---------------------------------------------------------------- Fire
for n in (1, 2):
    p = f"Engine {n} Fire"
    guard(f"ENG{n}_FIRE_COVER", O, p, f"Engine {n} fire handle", f"ENG{n}_FIRE_HANDLE_COVER")
    select(f"ENG{n}_FIRE", O, p, f"Engine {n} fire handle", f"ENG{n}_FIRE_HANDLE", f"INI_FIRE_HANDLE_ENG{n}", direct("Normal", "Pulled"),
           lamps=[{"read": f"INI_ENG{n}_IS_FIRE", "legend": "fire"}])
    for a in (1, 2):
        button(f"ENG{n}_AGENT{a}", O, p, f"Engine {n} agent {a}", f"ENG{n}_AGENT{a}",
               lamps=[{"read": f"INI_ENG{n}_AGENT{a}_SQUIB", "legend": "squib"}, {"read": f"INI_ENG{n}_AGENT{a}_DISCH", "legend": "discharged"}])
button("ENG_FIRE_TEST", O, "Engine 1 Fire", "Engine fire test", "ENGINE_FIRE_TEST", hold_ms=5000)
guard("APU_FIRE_COVER", O, "APU Fire", "APU fire handle", "APU_FIRE_HANDLE_COVER")
select("APU_FIRE", O, "APU Fire", "APU fire handle", "APU_FIRE_HANDLE", "INI_FIRE_HANDLE_APU", direct("Normal", "Pulled"))
button("APU_AGENT", O, "APU Fire", "APU agent", "APU_AGENT",
       lamps=[{"read": "INI_APU_SQUIB", "legend": "squib"}, {"read": "INI_APU_DISCH", "legend": "discharged"}])
button("APU_FIRE_TEST", O, "APU Fire", "APU fire test", "APU_AGENT_TEST", hold_ms=5000)
button("CARGO_SMOKE_TEST", O, "Cargo Smoke", "Cargo smoke test", "CARGO_SMOKE_TEST", hold_ms=5000)
for side, ev in [("Forward", "FWD"), ("Aft", "AFT")]:
    guard(f"{ev}_CARGO_AGENT_COVER", O, "Cargo Smoke", f"{side} cargo agent", f"{ev}_AGENT_COVER")
    button(f"{ev}_CARGO_AGENT", O, "Cargo Smoke", f"{side} cargo agent", f"{ev}_AGENT")
select("FWD_ISO", O, "Cargo Smoke", "Forward cargo isolation valve", "FWD_ISO_VALVE", "B:AIRLINER_FWD_ISO_VALVE", direct("Auto", "Shut"))
select("AFT_ISO", O, "Cargo Smoke", "Aft cargo isolation valve", "AFT_ISO_VALVE", "B:AIRLINER_AFT_ISO_VALVE", direct("Auto", "Shut"))
select("BULK_ISO", O, "Cargo Smoke", "Bulk cargo isolation valve", "BULK_ISO_VALVE", "INI_BULK_ISOL_VALVE", pb("Auto", "Shut"))

# ---------------------------------------------------------------- APU
select("APU_MASTER", O, "APU", "APU master", "APU_MASTER", "INI_APU_MASTER_SWITCH", pb_direct("On", "Off"),
       lamps=[{"read": "INI_APU_MASTER_SWITCH_FAULT", "legend": "fault"}])
button("APU_START", O, "APU", "APU start", "APU_START",
       lamps=[{"read": "INI_APU_START_BUTTON_CMD", "legend": "start"}, {"read": "INI_APU_AVAILABLE", "legend": "available"}])

# ---------------------------------------------------------------- Bleed
select("ENG1_BLEED", O, "Bleed Air", "Engine 1 bleed", "ENG1_BLEED", "INI_ENGINE1_BLEED", pb())
select("ENG2_BLEED", O, "Bleed Air", "Engine 2 bleed", "ENG2_BLEED", "INI_ENGINE2_BLEED", pb())
select("APU_BLEED", O, "Bleed Air", "APU bleed", "AIR_APU_BLEED", "INI_APU_BLEED_BUTTON", pb_direct("On", "Off"))
select("XBLEED", O, "Bleed Air", "Crossbleed", "XBLEED", "INI_XBLEED_MODE", direct("Shut", "Auto", "Open"))

# ---------------------------------------------------------------- Air conditioning
select("PACK1", O, "Air Conditioning", "Pack 1", "PACK1", "INI_PACK1_BUTTON", pb(), lamps=[{"read": "INI_PACK1_FAULT", "legend": "fault"}])
select("PACK2", O, "Air Conditioning", "Pack 2", "PACK2", "INI_PACK2_BUTTON", pb(), lamps=[{"read": "INI_PACK2_FAULT", "legend": "fault"}])
select("PACK_FLOW", O, "Air Conditioning", "Pack flow", "PACK_FLOW", "INI_PACK_FLOW", direct("Low", "Normal", "High"))
select("HOT_AIR1", O, "Air Conditioning", "Hot air 1", "HOT_AIR1", "INI_HOT_AIR_SUPPLY1_ON", pb())
select("HOT_AIR2", O, "Air Conditioning", "Hot air 2", "HOT_AIR2", "INI_HOT_AIR_SUPPLY2_ON", pb())
for key, label, ev, var in [("CKPT_TEMP", "Cockpit temperature", "COCKPIT_TEMP", "INI_AIRCOND_LVL_1"),
                            ("CABIN_TEMP", "Cabin temperature", "CABIN_TEMP", "INI_AIRCOND_LVL_2"),
                            ("BULK_TEMP", "Bulk cargo temperature", "CARGO_AIR_TEMP", "INI_AIRCOND_LVL_3"),
                            ("FWD_CARGO_TEMP", "Forward cargo temperature", "CARGO_AIR_TEMP1", "INI_AIRCOND_LVL_4")]:
    add(key, O, "Air Conditioning", label + " (0 cold, 50 middle, 100 hot)", "continuous", ev, var, min=0, max=100, step=5)
select("CARGO_HOT_AIR", O, "Air Conditioning", "Cargo hot air", "HOT_AIR", "INI_BULK_HOT_AIR_STATE", pb())
guard("RAM_AIR_COVER", O, "Air Conditioning", "Ram air", "RAM_AIR_COVER")
select("RAM_AIR", O, "Air Conditioning", "Ram air", "RAM_AIR", "INI_RAM_AIR_ON", pb_direct("On", "Off"))

# ---------------------------------------------------------------- Pressurisation
select("PRESS_MODE", O, "Pressurisation", "Pressurisation mode", "MODE_SEL", "INI_CABIN_PRESS_MODE", pb("Auto", "Manual"))
add("LDG_ELEV", O, "Pressurisation", "Landing elevation knob (0 auto, 100 = 14000 feet)", "continuous", "LDG_ELEV",
    "INI_LDG_ELEV_KNOB", min=0, max=100, step=5)
status("LDG_ELEV_FT", O, "Pressurisation", "Landing elevation selected (feet)", "INI_LANDING_ELEVATION", [])
guard("DITCHING_COVER", O, "Pressurisation", "Ditching", "DITCHING_COVER")
select("DITCHING", O, "Pressurisation", "Ditching", "DITCHING", "B:AIRLINER_DITCHING", direct("Off", "On"))

# ---------------------------------------------------------------- Ventilation
select("CAB_FANS", O, "Ventilation", "Cabin fans", "VENT_CAB", "INI_CABIN_FANS_BUTTON", pb())
select("EXTRACT", O, "Ventilation", "Avionics extract", "VENT_EXTRACT", "INI_CABIN_EXTRACT_VALVE_BUTTON", pb_direct("Override", "Auto"))
select("GND_COOL", O, "Ventilation", "Ground cooling", "GND_COOL", "B:AIRLINER_GND_COOL", direct("Auto", "Off"))

# ---------------------------------------------------------------- Anti-ice
select("WING_ANTI_ICE", O, "Anti-Ice", "Wing anti-ice", "WING_ICE", "INI_WING_ANTI_ICE", pb_direct("On", "Off"))
select("ENG1_ANTI_ICE", O, "Anti-Ice", "Engine 1 anti-ice", "ENG1_ICE", "INI_ENG1_ANTI_ICE", pb_direct("On", "Off"))
select("ENG2_ANTI_ICE", O, "Anti-Ice", "Engine 2 anti-ice", "ENG2_ICE", "INI_ENG2_ANTI_ICE", pb_direct("On", "Off"))
select("PROBE_HEAT", O, "Anti-Ice", "Probe and window heat", "PROBE_HEAT", "B:AIRLINER_PROBE_HEAT", direct("Auto", "On"))

# ---------------------------------------------------------------- Exterior lights
L = "Exterior Lights"
select("STROBE", O, L, "Strobe", "STROBE_TOGGLE", "INI_STROBE_LIGHT_SWITCH", direct("On", "Auto", "Off"))
select("BEACON", O, L, "Beacon", "BEACON_TOGGLE", "INI_BEACON_LIGHT_SWITCH", direct("Off", "On"))
select("WING_LT", O, L, "Wing lights", "WING_TOGGLE", "INI_WING_LIGHT_SWITCH", direct("Off", "On"))
select("NAV_LOGO", O, L, "Navigation and logo lights", "NAVLOGO_TOGGLE", "INI_LOGO_LIGHT_SWITCH", direct("2", "1", "Off"))
select("RWY_TURNOFF", O, L, "Runway turn-off lights", "RWYTURN_TOGGLE", "INI_TURNOFF_LIGHT_SWITCH", direct("Off", "On"))
select("LANDING_LT", O, L, "Landing lights", "LDGLIGHT_TOGGLE", "INI_LANDING_LIGHT_SWITCH", direct("Off", "On"))
select("NOSE_LT", O, L, "Nose light", "TAXILIGHT_TOGGLE", "INI_TAXI_LIGHT_SWITCH", direct("Take-off", "Taxi", "Off"))

# ---------------------------------------------------------------- Signs
select("SEAT_BELTS", O, "Signs", "Seat belts", "SEATBELTS_TOGGLE", "INI_SEATBELTS_SWITCH", direct("Off", "On"))
select("NO_SMOKING", O, "Signs", "No smoking", "NOSMOKING_TOGGLE", "INI_NO_SMOKING_SWITCH", direct("On", "Auto", "Off"))
select("EMER_EXIT_LT", O, "Signs", "Emergency exit lights", "EMER_EXIT_TOGGLE", "INI_EMER_EXIT_SWITCH", direct("On", "Armed", "Off"))

# ---------------------------------------------------------------- Interior lights
select("DOME", O, "Interior Lights", "Dome light", "CTL_TOGGLE", "INI_DOME_LIGHT_SWITCH", direct("Bright", "Dim", "Off"))
select("ANN_LT", O, "Interior Lights", "Annunciator lights", "ANNLT_TOGGLE", "INI_ANNLT_SWITCH", direct("Test", "Bright", "Dim"))
select("STBY_COMPASS_LT", O, "Interior Lights", "Standby compass light", "STBYCOMPASS_LIGHT", "INI_STANDBY_COMPASS_SWITCH", direct("Off", "On"))
select("CPT_CONSOLE_LT", O, "Interior Lights", "Captain console and floor light", "CPT_CONSOLE_TOGGLE", "INI_CPT_CONSOLE_SWITCH", direct("Bright", "Dim", "Off"))
select("FO_CONSOLE_LT", O, "Interior Lights", "First officer console and floor light", "FO_CONSOLE_TOGGLE", "B:AIRLINER_FO_CONSOLE_TOGGLE", direct("Bright", "Dim", "Off"))

# ---------------------------------------------------------------- Wipers
select("CPT_WIPER", O, "Wipers", "Captain wiper", "CPT_WIPER_SWITCH", "B:AIRLINER_CPT_WIPER_SWITCH", direct("Off", "Slow", "Fast"))
select("FO_WIPER", O, "Wipers", "First officer wiper", "FO_WIPER_SWITCH", "B:AIRLINER_FO_WIPER_SWITCH", direct("Off", "Slow", "Fast"))
button("CPT_RAIN_RPL", O, "Wipers", "Captain rain repellent", "CPT_RAIN_RPL")

# ---------------------------------------------------------------- Engine start (masters on the pedestal)
select("ENG_MODE", O, "Engine Start", "Engine mode selector", "ENG_START", "INI_IGNITION_KNOB", direct("Crank", "Normal", "Ignition start"))
for n in (1, 2):
    guard(f"MAN_START{n}_COVER", O, "Engine Start", f"Engine {n} manual start", f"MAN_START_{n}_COVER")
    select(f"MAN_START{n}", O, "Engine Start", f"Engine {n} manual start", f"MAN_START_{n}", f"INI_ENG{n}_MAN_START", pb_direct("On", "Off"))
for n in (1, 2):
    guard(f"FADEC{n}_COVER", O, "Engine Start", f"FADEC {n} ground power", f"FADEC_ENG{n}_COVER")
    select(f"FADEC{n}", O, "Engine Start", f"FADEC {n} ground power", f"FADEC_ENG{n}", f"INI_FADEC{n}_GND_POWER_STATE", pb_direct("On", "Off"))

# ---------------------------------------------------------------- Oxygen
select("CREW_OXY", O, "Oxygen", "Crew oxygen supply", "OXYGEN_CREW", "INI_CREW_SUPPLY", pb())

# ---------------------------------------------------------------- Calls
for ev, label in [("CALL_ALL", "Call all"), ("CALL_FWD", "Call forward"), ("CALL_MID", "Call mid"), ("CALL_AFT", "Call aft"),
                  ("CALL_PURS", "Call purser"), ("CALL_EXIT", "Call exit"), ("MECH_CALL", "Call mechanic")]:
    button(ev, O, "Calls", label, ev)

# ---------------------------------------------------------------- Recorders
select("CVR_GND_CTL", O, "Recorders", "Recorder ground control", "GND_CTL", "B:AIRLINER_GND_CTL", direct("Auto", "On"))
button("DFDR_EVENT", O, "Recorders", "DFDR event", "DFDR_EVENT")

# ---------------------------------------------------------------- Glareshield
W = "Warnings"
button("MASTER_WARN", G, W, "Master warning (silence)", "CPT_MASTER_WARN")
button("MASTER_CAUT", G, W, "Master caution (silence)", "CPT_MASTER_CAUT")
status("MASTER_WARN_LT", G, W, "Master warning", "INI_MASTER_WARNING_ACTIVE")
status("MASTER_CAUT_LT", G, W, "Master caution", "INI_MASTER_CAUTION_ACTIVE")
button("CHRONO", G, W, "Captain chrono", "CPT_CHRONO")
button("ATC_MSG", G, W, "ATC message", "CPT_ATC_MSG")
status("AUTOLAND_LT", G, W, "Autoland warning", "INI_AUTOLAND_LIGHT")

for side, s, fd, ls, terr, sfx in [("Captain", "CPT", "INI_FD1_ON", "INI_LS_CAPTAIN", "INI_TERR_ON_CAPT", ""),
                                   ("First officer", "FO", "INI_FD2_ON", "INI_LS_FO", "INI_TERR_ON_FO", "2")]:
    p = f"{side} EFIS"
    k = "CPT_" if s == "CPT" else "FO_"
    select(k + "FD", G, p, f"{side} flight director", f"{s}_FD", fd, pb())
    select(k + "LS", G, p, f"{side} LS", f"{s}_LS", ls, pb_direct("On", "Off"))
    side_sw = "CAPT" if s == "CPT" else "FO"
    select(k + "ND_MODE", G, p, f"{side} ND mode", f"EFIS_{s}_MODE", f"INI_MAP_MODE_{side_sw}_SWITCH", direct("LS", "VOR", "NAV", "ARC", "PLAN"))
    select(k + "ND_RANGE", G, p, f"{side} ND range", f"EFIS_{s}_RANGE", f"INI_MAP_RANGE_{side_sw}_SWITCH",
           direct("10 miles", "20 miles", "40 miles", "80 miles", "160 miles", "320 miles"))
    for f, var, label in [("CSTR", "INI_SHOW_CONSTRAINTS", "constraints"), ("WPT", "INI_SHOW_FIXES", "waypoints"),
                          ("VOR", "INI_SHOW_VORS", "VOR and DME"), ("NDB", "INI_SHOW_NDBS", "NDBs"), ("ARPT", "INI_SHOW_AIRPORTS", "airports")]:
        select(k + "ND_" + f, G, p, f"{side} ND shows {label}", f"EFIS_{s}_{f}", var + sfx, direct("Off", "On"))
    for n in (1, 2):
        st = f"INI_VOR_ADF{n}_{'CAPT' if s == 'CPT' else 'FO'}_STATE"
        select(f"{k}ND_NAV{n}", G, p, f"{side} ND needle {n}", f"EFIS_{s}_VOR{n}", st, direct("ADF", "Off", "VOR"))
    select(k + "TERR_ON_ND", G, p, f"{side} terrain on ND", f"{s}_TERR_ON_ND", terr, pb_direct("On", "Off"))
    select(k + "BARO_UNIT", G, p, f"{side} altimeter unit", f"{s}_BARO_TOGGLE", f"B:AIRLINER_{s}_BARO_TOGGLE", direct("inches", "hectopascals"))
    button(k + "PFD_ND_XFR", G, p, f"{side} PFD/ND transfer", f"{s}_PFD_ND")

# ---------------------------------------------------------------- Main panel
select("ANTISKID", M, "Brakes", "Anti-skid and nosewheel steering", "ANTISKD", "INI_ANTISKID_SWITCH", direct("Off", "On"))
select("BRAKE_FAN", M, "Brakes", "Brake fans", "BRK_FAN", "INI_BRAKE_FAN_ON", pb_direct("On", "Off"))
status("BRAKE_SYSTEM", M, "Brakes", "Brake system", "INI_BRAKE_SYSTEM", [(0, 0, "Normal"), (1, 1, "Alternate"), (2, 2, "Alternate, no anti-skid")])

GP = "GPWS and Terrain"
select("GPWS_SYS", M, GP, "GPWS system", "GPWS_SYS", "INI_GPWS_SYS_STATE", pb())
select("GPWS_TERR", M, GP, "Terrain", "GPWS_TERR", "INI_GPWS_TERR_STATE", pb())
select("GPWS_GS", M, GP, "Glide slope mode", "GS_MODE", "INI_GPWS_GS_STATE", pb())
select("GPWS_FLAP", M, GP, "Flap mode", "FLAP_MODE", "INI_GPWS_FLAP_STATE", pb())
select("LAND_RECOVERY", M, GP, "Landing recovery", "LAND_RECOVERY", "INI_LAND_RECOVERY", pb_direct("On", "Off"))

IS = "Instrument Switching"
select("ATT_HDG", M, IS, "ATT HDG switching", "ATT_HDG_SELECT", "INI_ATT_HDG_STATE", direct("Captain on 3", "Normal", "First officer on 3"))
select("AIR_DATA", M, IS, "Air data switching", "AIR_DATA_SELECT", "INI_AIR_DATA_STATE", direct("Captain on 3", "Normal", "First officer on 3"))
select("AUDIO_SW", M, IS, "Audio switching", "AUDIO_SELECT", "INI_AUDIO_STATE", direct("Captain on 3", "Normal", "First officer on 3"))
select("FM_SW", M, IS, "FMS switching", "FM_SELECT", "INI_FM_STATE", direct("Both on 1", "Normal", "Both on 2"))
select("ECAM_ND_XFR", M, IS, "ECAM/ND transfer", "ECAM_SWITCH", "INI_ECAM_ND_NORM_MODE", direct("Captain", "Normal", "First officer"))
select("ECAM_DMC", M, IS, "ECAM DMC", "ECAM_DMC_AUTO", "INI_ECAM_DMC_MODE", direct("1", "Auto", "2"))
select("EFIS_DMC", M, IS, "EFIS DMC", "CPT_EFIS_DMC_NORM", "INI_EFIS_DMC_NORM_STATE", direct("Normal", "Captain on 3", "First officer on 3"))

CL = "Clocks"
button("CLOCK_CHR", M, CL, "Clock chrono", "CLOCK_CHR")
button("CLOCK_RST", M, CL, "Clock reset", "CLOCK_RST")
select("CLOCK_ET", M, CL, "Elapsed time", "CLOCK_RUN", "INI_CLOCK_RUN_STATE", direct("Run", "Stop", "Reset"))

SI = "Standby Instrument"
button("ISIS_LS", M, SI, "Standby LS", "ISIS_LS")
button("ISIS_BUGS", M, SI, "Standby bugs", "ISIS_BUGS")
button("ISIS_RST", M, SI, "Standby attitude reset", "ISIS_RST")

DB = "Display Brightness"
for key, label, ev, hi in [("CPT_PFD_BRT", "Captain PFD", "CPT_PFD_BRT", 100), ("CPT_ND_BRT", "Captain ND", "CPT_ND_BRT", 100),
                           ("FO_PFD_BRT", "First officer PFD", "FO_PFD_BRT", 100), ("FO_ND_BRT", "First officer ND", "FO_ND_BRT", 100),
                           ("ECAM_UPPER_BRT", "Upper ECAM", "ECAM_UPDISP", 100), ("ECAM_LOWER_BRT", "Lower ECAM", "ECAM_LOWDISP", 100)]:
    add(key, M, DB, f"{label} brightness", "continuous", ev, "B:AIRLINER_" + ev, min=0, max=hi, step=10)
for key, label, ev in [("CPT_MCDU_BRT", "Captain MCDU", "CPT_MCDU_BRT"), ("FO_MCDU_BRT", "First officer MCDU", "FO_MCDU_BRT")]:
    add(key, M, DB, f"{label} brightness (the MCDU reader needs it on)", "continuous", ev, "B:AIRLINER_" + ev, min=0, max=1, step=0.1)

# ---------------------------------------------------------------- Pedestal
for n in (1, 2):
    select(f"ENG{n}_MASTER", P, "Engine Masters", f"Engine {n} master", f"ENG{n}_CUTOFF", f"A:GENERAL ENG FUEL VALVE:{n}, bool", direct("Off", "On"))

EC = "ECAM"
for ev, label in [("ECAM_ENG", "ENG"), ("ECAM_BLEED", "BLEED"), ("ECAM_PRESS", "PRESS"), ("ECAM_ELAC", "EL/AC"), ("ECAM_ELDC", "EL/DC"),
                  ("ECAM_HYD", "HYD"), ("ECAM_CB", "C/B"), ("ECAM_APU", "APU"), ("ECAM_COND", "COND"), ("ECAM_DOOR", "DOOR"),
                  ("ECAM_WHEEL", "WHEEL"), ("ECAM_FCTL", "F/CTL"), ("ECAM_FUEL", "FUEL"), ("ECAM_ALL", "ALL"), ("ECAM_STS", "STS"),
                  ("ECAM_RCL", "RCL"), ("ECAM_CLR", "CLR (left)"), ("ECAM_CLR1", "CLR (right)")]:
    button(ev, P, EC, f"ECAM {label}", ev)
guard("ECAM_EMER_CANC_COVER", P, EC, "Emergency cancel", "ECAM_EMERCANC_GAURD")
button("ECAM_EMER_CANC", P, EC, "Emergency cancel", "ECAM_EMERCANC")
button("TO_CONFIG", P, EC, "Take-off config test", "ECAM_TOCONFIG", hold_ms=3000)
status("ECAM_PAGE", P, EC, "System display page", "INI_ECAM_ACTIVE_PAGE",
       [(1, 1, "ENG"), (2, 2, "HYD"), (3, 3, "EL/AC"), (4, 4, "EL/DC"), (5, 5, "BLEED"), (6, 6, "COND"), (7, 7, "PRESS"),
        (8, 8, "FUEL"), (9, 9, "APU"), (10, 10, "F/CTL"), (11, 11, "DOOR"), (12, 12, "WHEEL"), (13, 13, "CRUISE"), (14, 14, "STATUS")])

RP = "Captain Radio Panel"
select("RMP1_POWER", P, RP, "Radio panel power", "CPT_VHF_ONOFF", "INI_RMP1_STATE", direct("Off", "On"))
for ev, label in [("CPT_COM_VHF1", "VHF 1"), ("CPT_COM_VHF2", "VHF 2"), ("CPT_COM_VHF3", "VHF 3"), ("CPT_COM_HF1", "HF 1"),
                  ("CPT_COM_HF2", "HF 2"), ("CPT_COM_AM", "AM"), ("CPT_BTN_TRANSFER", "Transfer (swap active and standby)")]:
    button(ev, P, RP, f"Radio panel {label}", ev)
status("RMP1_SELECTED", P, RP, "Radio panel selected radio", "INI_SELECTED_RADIO_CPT",
       [(1, 1, "VHF 1"), (2, 2, "VHF 2"), (3, 3, "VHF 3"), (4, 4, "HF 1"), (5, 5, "HF 2")])
guard("RMP1_NAV_COVER", P, RP, "Radio panel NAV", "CPT_COM_NAV_COVER")
button("RMP1_NAV", P, RP, "Radio panel NAV (backup navigation)", "CPT_COM_NAV")
for ev, label in [("CPT_COM_VOR", "VOR"), ("CPT_COM_LS", "LS"), ("CPT_COM_ADF", "ADF"), ("CPT_COM_BFO", "BFO")]:
    button(ev, P, RP, f"Radio panel {label}", ev)

AU = "Captain Audio"
for ev, label in [("CPT_BTN_VHF1", "VHF 1"), ("CPT_BTN_VHF2", "VHF 2"), ("CPT_BTN_VHF3", "VHF 3"), ("CPT_BTN_HF1", "HF 1"),
                  ("CPT_BTN_HF2", "HF 2"), ("CPT_BTN_INT", "Interphone"), ("CPT_BTN_CAB", "Cabin"), ("CPT_BTN_PA", "PA")]:
    button(ev, P, AU, f"Transmit {label}", ev)

TC = "Transponder and TCAS"
select("XPDR_MODE", P, TC, "Transponder mode", "TCAS_MODE", "INI_TCAS_STBY_STATE", direct("Standby", "Auto", "On"))
select("TCAS_MODE", P, TC, "TCAS mode", "TCAS_STBY", "INI_TCAS_MODE_PEDESTAL", direct("Standby", "TA only", "TA/RA"))
select("TCAS_TRAFFIC", P, TC, "TCAS traffic display", "TCAS_ABV", "INI_TCAS_MODE", direct("Threat", "All", "Above", "Below"))
select("XPDR_ALT_RPTG", P, TC, "Altitude reporting", "TCAS_ALT", "B:AIRLINER_TCAS_ALT", direct("Off", "On"))
select("XPDR_SYSTEM", P, TC, "Transponder system", "TCAS_ATC", "B:AIRLINER_TCAS_ATC", direct("1", "2"))
button("XPDR_IDENT", P, TC, "Ident", "TCAS_IDENT")

WX = "Weather Radar"
select("WX_SYS", P, WX, "Weather radar system", "WX_SYS", "INI_WX_SYS_SWITCH", direct("1", "Off", "2"))
select("WX_MODE", P, WX, "Weather radar mode", "WX_MODE", "INI_WXR_MODE", direct("WX", "WX plus turbulence", "Turbulence", "Map"))
select("WX_PWS", P, WX, "Predictive windshear", "WX_PWS", "INI_WX_PWS_SWITCH", direct("Off", "Auto"))
add("WX_GAIN", P, WX, "Weather radar gain", "continuous", "WX_GAIN", "B:AIRLINER_WX_GAIN", min=0, max=100, step=10,
    notes="the input event's own 0-100 (INI_WXR_GAIN reads the same setting as 0-10)")
add("WX_TILT", P, WX, "Weather radar tilt (15 = level)", "continuous", "WX_TILT", "B:AIRLINER_WX_TILT", min=0, max=30, step=1)


def main():
    keys = [c["key"] for c in CONTROLS]
    dup = {k for k in keys if keys.count(k) > 1}
    if dup:
        raise SystemExit(f"duplicate keys: {sorted(dup)}")
    sections = [{"name": n, "panels": p} for n, p in SECTIONS]
    used = {(c["section"], c["panel"]) for c in CONTROLS}
    for s in sections:
        for p in s["panels"]:
            if (s["name"], p) not in used:
                raise SystemExit(f"empty panel {s['name']}/{p}")
    doc = {"version": 1, "sections": sections, "controls": CONTROLS}
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(f"wrote {len(CONTROLS)} controls to {os.path.normpath(OUT)}")


if __name__ == "__main__":
    main()
