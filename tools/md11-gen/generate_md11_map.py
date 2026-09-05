#!/usr/bin/env python3
"""
Generate the TFDi MD-11 control map from the aircraft's ModelBehaviorDefs XML.

The MD-11's ModelBehaviorDefs are emitted by TFDi's own ModelBehaviorsExporter and
carry everything MSFSBA needs to build panels, in one place:

    <UseTemplate Name="TFDi_Design_MD11_Button_Template">
      <TOOLTIPID>Captain ND Map Mode</TOOLTIPID>
      <NODE_ID>MD11_LECP_MAP_BT</NODE_ID>          <- the L:var
      <LEFT_BUTTON_DOWN>86018</LEFT_BUTTON_DOWN>   <- CEVENT id (press)
      <LEFT_BUTTON_UP>86019</LEFT_BUTTON_UP>       <- CEVENT id (release)
    </UseTemplate>

TOOLTIPID is richer than it looks: beyond the label it embeds the aircraft's own
value->label state map as an RPN formatting expression, e.g.

    Flaps/Slats (%((L:MD11_FLAP_RNG))%{case}%{:0}Up/Retracted%{:20}Up/Extended%{end})

so the detent names, switch position names and annunciator wording all come straight
from TFDi rather than being invented here. That map becomes ValueDescriptions on the
generated SimVarDefinition, which is what a screen reader ends up speaking.

Output: md11_control_map.json (consumed by the C# definition + checked in for review).

Usage:
    python generate_md11_map.py [--pkg <community/tfdidesign-aircraft-md11>]
                                [--wasm <md11host.wasm>]
                                [--out md11_control_map.json]
"""

import argparse
import html
import json
import os
import re
import sys
from collections import Counter, defaultdict

import md11_paths

# No hardcoded package path. The MD-11 is FOUND (md11_paths) across FS2020 and
# FS2024, MS Store, Steam and external/custom package folders. The previous
# default was one developer's absolute FS2020 Store path and worked nowhere
# else; the previous wasm path guess omitted the "common" level that a real
# FS2024 install has, and a miss silently produced a map with NO wasm-derived
# L:vars (the PFD speed tape and V-speeds, which have no other source).

# ---------------------------------------------------------------------------
# Template classification.
#
# Kind drives how MSFSBA renders the control:
#   button   -> momentary; press/release CEVENT pair (see PRESS-RELEASE note below)
#   knob     -> rotary; WHEEL_UP/WHEEL_DOWN step events
#   knob_pp  -> rotary + push/pull (the FCP's SPD/HDG/ALT selectors)
#   switch   -> multi-position; discrete inc/dec events
#   annun    -> read-only indicator lamp (no events, L:var only)
#   guard    -> hinged cover over another control
#   lever    -> flap / spoiler levers
#   handle   -> fire handles (pull + rotate)
# ---------------------------------------------------------------------------
TEMPLATE_KINDS = {
    "TFDi_Design_MD11_Button_Template": "button",
    "TFDi_Design_MD11_Knob_Template": "knob",
    "TFDi_Design_MD11_Volume_Knob": "knob",
    "TFDi_Design_MD11_Infinite_Knob": "knob",
    "TFDi_Design_MD11_ELEV_FEEL_Knob": "knob",
    "TFDi_Design_MD11_Knob_PushPull": "knob_pp",
    "TFDi_Design_MD11_Knob_Push": "knob_push",
    "TFDi_Design_MD11_Switch_Template": "switch",
    "TFDi_Design_MD11_Switch_SingleEvent_Template": "switch",
    "TFDi_Design_MD11_3Pos_Switch_Hold": "switch",
    "TFDi_Design_MD11_3Pos_Knob_Hold": "switch",
    "TFDi_Design_MD11_Annunciator": "annun",
    "TFDi_Design_MD11_Guard_Template": "guard",
    "MD11_Flap_Lever": "lever",
    "TFDi_Design_MD11_SpoilerLever": "lever",
    "MD11_Long_Trim_Switch": "switch",
    "TFDi_Design_MD11_ENG_Fire_Handle": "handle",
    "TFDi_Design_MD11_APU_Fire_Handle": "handle",
    "TFDi_Design_MD11_Clickspot": "button",
    "TFDi_Design_MD11_Clickspot_UD": "button",
    "TFDi_Design_MD11_Range_Template": "knob",
}

# Cockpit-area prefixes, from the NODE_ID's second underscore token. TFDi's own
# naming; the labels here are what MSFSBA shows as panel section names.
# L:vars that describe the AIRFRAME, never a control's state. A tooltip may reference one to pick
# its wording between variants; that is not the control's position. Keep this list tiny and
# evidence-based — each entry needs a reason, because wrongly excluding a real state var silently
# repoints a control at its own node id.
LABEL_ONLY_VARS = {
    # The freighter/pax split. Used by the cabin-temperature knobs to say "Courier Cabin" /
    # "Main Cargo Deck" on the MD-11F where the passenger jet says "Forward Cabin" / "Middle
    # Cabin". Confirmed in Overhead.xml: both knobs carry NUM_STATES=8 and an ANIM_NAME equal to
    # their own node id, so the temperature — not the variant — is what they select.
    "MD11_EFB_IS_CARGO",
}

AREA_LABELS = {
    "OVHD": "Overhead",
    "AOVHD": "Aft Overhead",
    "PED": "Pedestal",
    "CGS": "Glareshield (Flight Control Panel)",
    "LECP": "Captain EFIS Control Panel",
    "RECP": "F/O EFIS Control Panel",
    "MIP": "Main Instrument Panel",
    "THR": "Throttle Quadrant",
    "BKR": "Circuit Breakers",
    "LSIDE": "Captain Side Panel",
    "RSIDE": "F/O Side Panel",
    "CTR": "Center Instrument",
    "EXT": "Doors and Exterior",
    "CARGO": "Cargo",
    "LTS": "Lighting",
    "LMCDU": "MCDU (Left)",
    "CMCDU": "MCDU (Center)",
    "RMCDU": "MCDU (Right)",
    "LYOKE": "Captain Yoke",
    "RYOKE": "F/O Yoke",
    "FLAP": "Flaps",
    "DIALAFLAP": "Dial-A-Flap",
    "SPDBRK": "Speedbrake",
    "GSL": "Glareshield (Captain)",
    "GSR": "Glareshield (First Officer)",
    "CAB": "Cabin",
    "WIPER": "Wipers",
    "TOEBRAKE": "Toe Brakes",
    "STBY": "Standby Instruments",
    "ASU": "Air Start Unit",
    "CPT": "Audio Panel (Captain)",
    "FO": "Audio Panel (F/O)",
    "OBS": "Audio Panel (Observer)",
    "OPT": "Aircraft Options",
    "EFB": "EFB",
    "FLIGHTDECK": "Flight Deck Door",
    "YOKE": "Yoke",
}

# Raw 3D node names carry no MD11_<AREA> token. Place them by hand.
AREA_FIXES = {
    **{n: "Doors and Exterior" for n in (
        "1L_DN", "1L_UP", "1R_DN", "1R_UP", "2L_DN", "2L_UP", "2R_DN", "2R_UP",
        "Object7524", "Object7525", "Object7526", "Object7527", "Object7530", "Object7531",
        "Object7536", "Object7537", "Cylinder11762_03", "Cylinder11813", "Cylinder11904",
        "Cylinder12038", "Cylinder12057_08", "Cylinder12058", "Cylinder12061", "Cylinder12064")},
    "GA_BT_ALT": "Throttle Quadrant",
    "knob_kohlsman": "Main Instrument Panel",
    "l_window_shade_pull": "Captain Side Panel",
    "Mirror_l_window_shade_pull": "Captain Side Panel",
    "MANF_DRAIN_LT": "Overhead",
    "MD11_OVHD_1_PAX_LOAD_SW": "Aircraft Options",
    "MD11_OVHD_10_PAX_LOAD_SW": "Aircraft Options",
    "MD11_OVHD_100_PAX_LOAD_SW": "Aircraft Options",
}

# ---------------------------------------------------------------------------
# Curated overrides.
#
# The generic TOOLTIPID parser handles %{case} maps, but a few controls encode
# their state as an RPN *range* test rather than discrete cases, and the parser
# cannot see those. Rather than teach it to interpret arbitrary RPN, pin the
# handful of affected controls here with the values read out of the same tooltip.
#
# The flap lever is the important one. Its tooltip is:
#   %(38 65 (L:MD11_FLAP_RNG) rng)%{if}Dial-A-Flap %(10 (L:MD11_DIALAFLAP_IND_RNG) 6.6667 / +)%!d!/Extended
#   %{else}%((L:MD11_FLAP_RNG))%{case}%{:0}Up/Retracted%{:20}Up/Extended%{:70}28/Extended...
# i.e. FLAP_RNG in [38,65] IS the Dial-A-Flap detent -- a range, so %{case} misses
# it entirely and the lever reads as 5 positions instead of the real 6.
#
# The MD-11 handle is combined flap+slat and runs, clean to fully extended:
#   UP/RET -> 0/EXT -> DIAL-A-FLAP -> 28 -> 35 -> 50
# with a physical gate at 28 so the handle cannot slip straight between the
# take-off range and the landing range. A go-around from 35/50 retracts to 28
# first, which is why 28 is its own detent and not just a step on the way up.
CURATED = {
    "MD11_FLAP_LATCH": {
        "label": "Flaps/Slats",
        "detents": [
            {"value": 0, "name": "Flap Up / Slat Retracted"},
            {"value": 20, "name": "Flap 0 / Slat Extended"},
            # Handle in the variable take-off detent; the angle itself comes from
            # the Dial-A-Flap thumbwheel, not the handle position.
            {"value": 50, "range": [38, 65], "name": "Dial-A-Flap", "dial": True},
            {"value": 70, "name": "Flap 28"},
            {"value": 82, "name": "Flap 35"},
            {"value": 100, "name": "Flap 50"},
        ],
        "notes": "Combined flap/slat handle. Gate at 28 blocks take-off<->landing range slips.",
    },
    # Thumbwheel selecting the take-off flap angle used by the DIAL-A-FLAP detent.
    # Angle = 10 + IND_RNG / 6.6667  =>  IND_RNG 0..100 spans 10..25 degrees.
    "MD11_DIALAFLAP_WHEEL_RNG": {
        "label": "Dial-A-Flap Take-off Angle",
        "dial_a_flap": {
            "state_var": "MD11_DIALAFLAP_IND_RNG",
            "min_deg": 10,
            "max_deg": 25,
            "units_per_deg": 6.6667,
            "formula": "degrees = 10 + MD11_DIALAFLAP_IND_RNG / 6.6667",
        },
    },
}

# Controls whose exported tooltip is missing or garbage. TFDi's wording where it exists,
# otherwise the cockpit's own placard wording. Discriminator first ("Left Rain Repellent").
LABEL_FIXES = {
    "MD11_OVHD_AICE_AUTO_BT": "Anti-Ice Auto",
    "MD11_OVHD_L_RAIN_REPLNT_BT": "Left Rain Repellent",
    "MD11_OVHD_R_RAIN_REPLNT_BT": "Right Rain Repellent",
    "MD11_OVHD_PNEU_OUTFLOW_VALVE_POS_SW": "Outflow Valve Position",
    "MD11_OVHD_1_PAX_LOAD_SW": "Passenger Load Units",
    "MD11_OVHD_10_PAX_LOAD_SW": "Passenger Load Tens",
    "MD11_OVHD_100_PAX_LOAD_SW": "Passenger Load Hundreds",
    "MD11_AOVHD_EVAC_GRD": "EVAC guard",
    "MD11_AOVHD_GPWS_GRD": "GPWS guard",
    "MD11_CTR_FLTNO1_SW": "Flight Number Digit 1",
    "MD11_CTR_FLTNO2_SW": "Flight Number Digit 2",
    "MD11_CTR_FLTNO3_SW": "Flight Number Digit 3",
    "MD11_CTR_FLTNO4_SW": "Flight Number Digit 4",
    "MD11_MIP_ISFD_STD_BT": "Standby Display STD",
    "MD11_PED_XPNDR_CLR_BT": "Transponder Clear",
    "MD11_CABIN_OXY_MASKS_DOOR": "Cabin Oxygen Masks Door",
    "MD11_EFB_TOGGLE": "EFB Toggle (Captain)",
    "MD11_EFB_TOGGLE_FO": "EFB Toggle (First Officer)",
    "MD11_FLIGHTDECK_DOOR": "Flight Deck Door",
    "l_window_shade_pull": "Left Window Shade",
    "Mirror_l_window_shade_pull": "Left Window Shade (mirror)",
    "GA_BT_ALT": "Go Around Mode (alternate button)",
    "MD11_LYOKE_TRIM_SW001": "First Officer Elevator Trim Switch",
    "MD11_THR_L_ATS_BT": "Left Autothrust Disconnect",
    "MD11_THR_R_ATS_BT": "Right Autothrust Disconnect",
    "MD11_EXT_DOOR_PAX_1L_ARMED_LVR_OBJ": "Door 1L Slides (cabin lever)",
    "MD11_EXT_DOOR_PAX_1R_ARMED_LVR_OBJ": "Door 1R Slides (cabin lever)",
}

# MCDU keys: the derived 'Lsk 1l button' / 'Dir intc button' forms, spelled as the keycap.
MCDU_KEY_LABELS = {
    "DIR_INTC": "DIR INTC", "FPLN": "F-PLN", "SEC_FPLN": "SEC F-PLN", "NAV_RAD": "NAV RAD",
    "NEXTPAGE": "NEXT PAGE", "TOAPPR": "TO/APPR", "ENG_OUT": "ENG OUT", "CLR": "CLR",
    "INIT": "INIT", "REF": "REF", "PERF": "PERF", "PROG": "PROG", "MENU": "MENU", "FIX": "FIX",
    "UP": "Up", "DOWN": "Down", "SP": "Space", "DOT": "Dot",
    "MINUS": "Minus", "PLUS": "Plus", "SLASH": "Slash",
    # NOTE: MD11_xMCDU_L_BT / R_BT are the LETTERS L and R (the humanizer's ABBREV table turned
    # them into "Left"/"Right"); they fall through to the key token itself.
}
MCDU_SIDES = {"LMCDU": "Left", "CMCDU": "Center", "RMCDU": "Right"}

# ---------------------------------------------------------------------------
# Control STATE. A blind pilot cannot see a legend light, so each button's state is composed
# by MSFSBA from (1) the legend lamps that belong to it, (2) its own L:var where TFDi's own
# tooltip reads state from it (a proven latch), (3) what "all legends dark" means. Everything
# below is TFDi wording from the Systems Guide; live evidence is in docs/md11.md.
# ---------------------------------------------------------------------------

# Legend token (the text printed on the light) -> plain state word spoken when it is lit.
LEGEND_MEANINGS = {
    "OFF": "Off", "ON": "On", "ARM": "Armed", "AVAIL": "Available", "FAULT": "Fault", "FAIL": "Fail",
    "DISAG": "Disagree", "LOW": "Low", "PRESS": "Low pressure", "FLOW": "Flow", "MANF": "Manifold hot",
    "TEMP_HI": "Temperature high", "SEL": "Select", "MAN": "Manual", "OVRD": "Override",
    "DISC": "Disconnected", "DISCONNECT": "Disconnected", "ALTN": "Alternate", "FILL": "Filling",
    "TRANS": "Transfer", "RESET": "Reset", "SMOKE": "Smoke", "HEAT": "Heat", "TEST": "Test",
    "DISARM": "Disarmed", "GREEN": "Down and locked", "RED": "Unsafe", "CALL": "Call", "MIC": "Selected",
    "VOL": "On", "IDENT": "Ident", "MSG": "Message", "DSPY": "Display", "OFST": "Offset", "AUTO": "Auto",
    "HIGH": "High", "NORM": "Normal", "USE_ENG_AIR": "Use engine air", "CAB_ALT": "Cabin altitude",
    "AVIONICS_OVHT": "Avionics overheat", "CLOSED": "Closed", "OPEN": "Open", "TEL": "Telephone",
    "TELL": "Telephone", "MECH": "Mech call", "INHIBIT": "Inhibited", "LOCK": "Locked",
    "UNLOCK": "Unlocked", "PWR": "Powered", "CLSD_READY": "Closed and ready", "DOOR": "Door",
    "FUEL": "Fuel low", "GEN": "Generator", "STOP": "Stop", "BLANK": "",
}

# Lamps named as a bare "<stem>_LT" whose legend is NOT "ON" (the default for a bare lamp), and
# lamps whose legend token differs from what is printed. From the Systems Guide.
LAMP_LEGEND_OVERRIDES = {
    "MD11_OVHD_LTS_NAV_LT": "OFF", "MD11_OVHD_LTS_BCN_LT": "OFF", "MD11_OVHD_LTS_HI_INT_LT": "OFF",
    "MD11_OVHD_WNDSHLD_AICE_DEFOG_LT": "OFF", "MD11_OVHD_PNEU_BLEED_1_OFF_LT": "OFF",
    "MD11_OVHD_PNEU_BLEED_2_OFF_LT": "OFF", "MD11_OVHD_PNEU_BLEED_3_OFF_LT": "OFF",
    "MD11_AOVHD_APU_GEN_LT": "OFF", "MD11_CTR_ANTISKID_LT": "OFF",
    "MD11_OVHD_ELEC_GALLEY_BUS_1_LT": "OFF", "MD11_OVHD_ELEC_GALLEY_BUS_2_LT": "OFF", "MD11_OVHD_ELEC_GALLEY_BUS_3_LT": "OFF",
    "MD11_OVHD_FUEL_TANK_1_TRANS_LT": "ON", "MD11_OVHD_FUEL_TANK_2_TRANS_LT": "ON", "MD11_OVHD_FUEL_TANK_3_TRANS_LT": "ON",
    "MD11_OVHD_FUEL_SYSTEM_SEL_LT": "SEL", "MD11_OVHD_PNEU_SYSTEM_SEL_LT": "SEL",
    "MD11_OVHD_PNEU_CABIN_SYSTEM_SEL_LT": "SEL", "MANF_DRAIN_LT": "OPEN", "MD11_OVHD_FUEL_DUMP_LT": "OPEN",
    "MD11_OVHD_FUEL_DUMP_STOP_LT": "STOP", "MD11_OVHD_HYD_TEST_LT": "TEST", "MD11_MIP_CTR_GEAR_LT": "UP",
    "MD11_CTR_SLAT_STOW_LT": "STOW", "MD11_GSL_MST_WRN_LT": "WARN", "MD11_GSR_MST_WRN_LT": "WARN",
    "MD11_GSL_MST_CAUT_LT": "CAUT", "MD11_GSR_MST_CAUT_LT": "CAUT", "MD11_OVHD_ENG_A_LT": "A",
    "MD11_OVHD_ENG_B_LT": "B", "MD11_OVHD_LTS_MECH_LT": "CALL", "MD11_OVHD_LTS_MECH_CALL_ON_LT": "CALL",
    "MD11_OVHD_LTS_FWD_ATTND_LT": "CALL", "MD11_OVHD_LTS_MID_ATTND_LT": "CALL", "MD11_OVHD_LTS_AFT_ATTND_LT": "CALL",
    "MD11_OVHD_LTS_OVW_ATTND_LT": "CALL", "MD11_OVHD_LTS_CREW_REST_LT": "CALL", "MD11_OVHD_GEN_BUS_1_RESET_LT": "FAULT",
    "MD11_OVHD_GEN_BUS_2_RESET_LT": "FAULT", "MD11_OVHD_GEN_BUS_3_RESET_LT": "FAULT",
    **{f"MD11_AOVHD_CRGSMK_{p}_AGNT{n}_LT": "FIRE" for p in ("FWD", "AFT") for n in (1, 2)},
    **{f"MD11_AOVHD_CRGSMK_{p}_AGNT{n}LO_LT": "LOW" for p in ("FWD", "AFT") for n in (1, 2)},
    **{f"MD11_PED_SD_{p}_LT": "ALERT" for p in ("AIR", "CONFIG", "ELEC", "ENG", "FUEL", "HYD", "MISC")},
    **{f"MD11_PED_{s}_RADIO_PNL_{r}_LT": "SEL" for s in ("CPT", "FO", "OBS") for r in ("VHF1", "VHF2", "VHF3", "HF1", "HF2")},
    # Rows attached to knobs/switches: the legend the guide prints on that light.
    "MD11_OVHD_FLTCTL_ELEVFEEL_LT": "MANUAL", "MD11_OVHD_FLTCTL_FLAPLIM_LT": "MANUAL",
    "MD11_OVHD_IRS_1_LT": "NAV_OFF", "MD11_OVHD_IRS_2_LT": "NAV_OFF", "MD11_OVHD_IRS_3_LT": "NAV_OFF",
    "MD11_THR_L_FUEL_LT": "FIRE", "MD11_THR_C_FUEL_LT": "FIRE", "MD11_THR_R_FUEL_LT": "FIRE",
    # Audio panel MIC / IDENT buttons light "MIC" / "IDENT", not "ON".
    **{f"{p}_{r}_MIC_LT": "MIC" for p in ("MD11_PED_CPT_AUDIO_PNL", "MD11_PED_FO_AUDIO_PNL", "MD11_OBS_AUDIO_PNL")
       for r in ("VHF1", "VHF2", "VHF3", "HF1", "HF2", "SAT", "INT", "CAB")},
    **{f"{p}_IDENT_LT": "IDENT" for p in ("MD11_PED_CPT_AUDIO_PNL", "MD11_PED_FO_AUDIO_PNL", "MD11_OBS_AUDIO_PNL")},
}
# Spoken word for the overriding legends above that LEGEND_MEANINGS does not carry.
LEGEND_MEANINGS.update({"UP": "Up", "STOW": "Stowed", "WARN": "Warning", "CAUT": "Caution", "A": "Selected",
                        "B": "Selected", "FIRE": "Fire", "ALERT": "Alert", "MANUAL": "Manual", "NAV_OFF": "NAV OFF",
                        "NO_MASKS": "No masks"})

# Spoken word when lit, where the legend's generic word reads wrong for this lamp.
LAMP_LIT_OVERRIDES = {
    **{f"MD11_PED_{s}_RADIO_PNL_{r}_LT": "Selected" for s in ("CPT", "FO", "OBS") for r in ("VHF1", "VHF2", "VHF3", "HF1", "HF2")},
}

# Curated pairings where TFDi's lamp name does not start with the button's stem.
STATE_LAMPS = {
    **{f"MD11_OVHD_ELEC_AC_TIE{n}_BT": [(f"MD11_OVHD_ELEC_AC{n}_TIE_ARM_LT", "ARM"), (f"MD11_OVHD_ELEC_AC{n}_TIE_OFF_LT", "OFF")] for n in (1, 2, 3)},
    "MD11_OVHD_ELEC_DC_TIE1_BT": [("MD11_OVHD_ELEC_DC1_TIE_OFF_LT", "OFF")],
    "MD11_OVHD_ELEC_DC_TIE3_BT": [("MD11_OVHD_ELEC_DC3_TIE_OFF_LT", "OFF")],
    "MD11_OVHD_ELEC_CAB_BUS_BT": [("MD11_OVHD_ELEC_CABIN_BUS_OFF_LT", "OFF")],
    "MD11_OVHD_ELEC_SYSTEM_SEL_BT": [("MD11_OVHD_ELEC_SYS_SEL_LT", "SEL"), ("MD11_OVHD_ELEC_SYS_MANUAL_LT", "MAN")],
    **{f"MD11_OVHD_GALLEY_BUS_{n}_BT": [(f"MD11_OVHD_ELEC_GALLEY_BUS_{n}_LT", "OFF")] for n in (1, 2, 3)},
    "MD11_OVHD_HYD_HYD_TEST_BT": [("MD11_OVHD_HYD_TEST_LT", "TEST")],
    "MD11_OVHD_HYD_SYSTEM_SEL_BT": [("MD11_OVHD_HYD_SYS_SEL_LT", "SEL"), ("MD11_OVHD_HYD_SYS_MANUAL_LT", "MAN")],
    "MD11_OVHD_FUEL_SYSTEM_SEL_BT": [("MD11_OVHD_FUEL_SYSTEM_MAN_LT", "MAN")],
    "MD11_OVHD_PNEU_SYSTEM_SEL_BT": [("MD11_OVHD_PNEU_SYSTEM_MAN_LT", "MAN")],
    "MD11_OVHD_PNEU_CABIN_SYSTEM_SEL_BT": [("MD11_OVHD_PNEU_CABIN_SYSTEM_MAN_LT", "MAN")],
    "MD11_OVHD_AICE_SYSTEM_SEL_BT": [("MD11_OVHD_AICE_SYSTEM_MAN_LT", "MAN")],
    **{f"MD11_OVHD_FUEL_PUMP_TANK_{n}_BT": [(f"MD11_OVHD_FUEL_TANK_{n}_PUMP_OFF_LT", "OFF"), (f"MD11_OVHD_FUEL_TANK_{n}_PUMP_LOW_LT", "LOW")] for n in (1, 2, 3)},
    **{f"MD11_OVHD_FUEL_TRANS_TANK_{n}_BT": [(f"MD11_OVHD_FUEL_TANK_{n}_TRANS_LT", "ON"), (f"MD11_OVHD_FUEL_TANK_{n}_TRANS_LOW_LT", "LOW")] for n in (1, 2, 3)},
    **{f"MD11_OVHD_FUEL_XFEED_TANK_{n}_BT": [(f"MD11_OVHD_FUEL_TANK_{n}_XFEED_ON_LT", "ON"), (f"MD11_OVHD_FUEL_TANK_{n}_XFEED_DISAG_LT", "DISAG")] for n in (1, 2, 3)},
    **{f"MD11_OVHD_FUEL_FILL_TANK_{n}_BT": [(f"MD11_OVHD_FUEL_TANK_{n}_FILL_ARM_LT", "ARM"), (f"MD11_OVHD_FUEL_TANK_{n}_FILL_FILL_LT", "FILL")] for n in (1, 2, 3)},
    "MD11_OVHD_FUEL_FWDAUX_L_TRANS_BT": [("MD11_OVHD_FUEL_FWDAUX_LTRANS_ON_LT", "ON"), ("MD11_OVHD_FUEL_FWDAUX_LTRANS_LOW_LT", "LOW")],
    "MD11_OVHD_FUEL_FWDAUX_R_TRANS_BT": [("MD11_OVHD_FUEL_FWDAUX_RTRANS_ON_LT", "ON"), ("MD11_OVHD_FUEL_FWDAUX_RTRANS_LOW_LT", "LOW")],
    "MD11_OVHD_FUEL_MANF_DRAIN_BT": [("MANF_DRAIN_LT", "OPEN")],
    **{f"MD11_OVHD_FLTCTL_{c}_BT": [(f"MD11_OVHD_FLTCTL_{c}FAIL_LT", "FAIL"), (f"MD11_OVHD_FLTCTL_{c}FOFF_LT", "OFF")] for c in ("LLI", "LLO", "RLI", "RLO")},
    **{f"MD11_OVHD_FLTCTL_{c}_BT": [(f"MD11_OVHD_FLTCTL_{c}FAIL_LT", "FAIL"), (f"MD11_OVHD_FLTCTL_{c}OFF_LT", "OFF")] for c in ("LYDA", "LYDB", "UYDA", "UYDB")},
    **{f"MD11_OVHD_PNEU_BLEED_{n}_OFF_BT": [(f"MD11_OVHD_PNEU_BLEED_{n}_PRESS_LT", "PRESS")] for n in (1, 2, 3)},
    **{f"MD11_OVHD_PNEU_BLEED_{n}_MANF_TEMP_HI_BT": [(f"MD11_OVHD_PNEU_BLEED_{n}_MANF_LT", "MANF"), (f"MD11_OVHD_PNEU_BLEED_{n}_TEMP_HI_LT", "TEMP_HI")] for n in (1, 2, 3)},
    "MD11_OVHD_PNEU_1_2_ISOL_BT": [("MD11_OVHD_PNEU_ISOL_1_2_ON_LT", "ON"), ("MD11_OVHD_PNEU_ISOL_1_2_DISAG_LT", "DISAG")],
    "MD11_OVHD_PNEU_1_3_ISOL_BT": [("MD11_OVHD_PNEU_ISOL_1_3_ON_LT", "ON"), ("MD11_OVHD_PNEU_ISOL_1_3_DISAG_LT", "DISAG")],
    "MD11_OVHD_PNEU_APU_BLEED_BT": [("MD11_OVHD_PNEU_APU_ON_LT", "ON"), ("MD11_OVHD_PNEU_APU_USE_ENG_AIR_LT", "USE_ENG_AIR")],
    "MD11_OVHD_PNEU_MASKS_BT": [("MD11_OVHD_PNEU_NO_MASKS_LT", "NO_MASKS")],
    "MD11_OVHD_LTS_MECH_BT": [("MD11_OVHD_LTS_MECH_CALL_ON_LT", "CALL")],
    "MD11_OVHD_LTS_DOME_BT": [("MD11_LTS_DOME", "ON")],
    "MD11_AOVHD_APU_START_BT": [("MD11_AOVHD_APU_ON_LT", "ON"), ("MD11_AOVHD_APU_OFF_LT", "OFF")],
    **{f"MD11_AOVHD_CRGSMK_{p}_AGNT{n}_BT": [(f"MD11_AOVHD_CRGSMK_{p}_AGNT{n}LO_LT", "LOW")] for p in ("FWD", "AFT") for n in (1, 2)},
}

# What "every legend dark" means, where the legend-set rule (OFF→On; ON/AVAIL/ARM→Off;
# fault-class only→Normal) gives the wrong answer.
DARK_OVERRIDES = {
    "MD11_OVHD_ELEC_EXT_PWR_BT": "Not available", "MD11_OVHD_ELEC_APU_PWR_BT": "Not available",
    "MD11_OVHD_ELEC_GLY_EXT_PWR_BT": "Not available",
    **{f"MD11_OVHD_ELEC_AC_TIE{n}_BT": "Closed" for n in (1, 2, 3)},
    **{f"MD11_OVHD_ELEC_DC_TIE{n}_BT": "Closed" for n in (1, 3)},
    "MD11_OVHD_ELEC_CAB_BUS_BT": "Powered",
    **{f"MD11_OVHD_GALLEY_BUS_{n}_BT": "Powered" for n in (1, 2, 3)},
    "MD11_AOVHD_APU_START_BT": "Off",
    **{f"MD11_PED_SD_{p}_BT": "No alert" for p in ("AIR", "CONFIG", "ELEC", "ENG", "FUEL", "HYD", "MISC")},
    **{f"MD11_PED_{s}_RADIO_PNL_{r}_BT": "Not selected" for s in ("CPT", "FO", "OBS") for r in ("VHF1", "VHF2", "VHF3", "HF1", "HF2")},
}

# Buttons proven live to latch their position in their own L:var although their tooltip does
# not read it (battery: 0→1 on press, stays 1, 2026-09-05). Tooltip-read buttons need no entry.
LATCH_FIXED = {"MD11_OVHD_ELEC_BATT_BT": ("On", "Off")}

# Lamps that belong to no button: spoken name, lit state, dark state.
STANDALONE_LAMPS = {
    **{f"MD11_OVHD_ELEC_AC{n}_OFF_LT": (f"AC Bus {n}", "Off", "Powered") for n in (1, 2, 3)},
    **{f"MD11_OVHD_ELEC_DC{n}_BUS_OFF_LT": (f"DC Bus {n}", "Off", "Powered") for n in (1, 2, 3)},
    "MD11_OVHD_ELEC_BATT_BUS_OFF_LT": ("Battery Bus", "Off", "Powered"),
    "MD11_OVHD_ELEC_L_EMER_AC_OFF_LT": ("Left Emergency AC Bus", "Off", "Powered"),
    "MD11_OVHD_ELEC_R_EMER_AC_OFF_LT": ("Right Emergency AC Bus", "Off", "Powered"),
    "MD11_OVHD_ELEC_L_EMER_DC_OFF_LT": ("Left Emergency DC Bus", "Off", "Powered"),
    "MD11_OVHD_ELEC_R_EMER_DC_OFF_LT": ("Right Emergency DC Bus", "Off", "Powered"),
    "MD11_OVHD_ELEC_AC_GND_SVC_OFF_LT": ("AC Ground Service Bus", "Off", "Powered"),
    "MD11_OVHD_ELEC_DC_GND_SVC_OFF_LT": ("DC Ground Service Bus", "Off", "Powered"),
    **{f"MD11_OVHD_HYD_SYS_{n}_PRESS_LT": (f"Hydraulic System {n} Pressure", "Abnormal", "Normal") for n in (1, 2, 3)},
    "MD11_OVHD_PNEU_OUTFLOW_CLOSED_LT": ("Outflow Valve", "Closed", "Not closed"),
    # MD11_OVHD_PNEU_NO_MASKS_LT is paired to MD11_OVHD_PNEU_MASKS_BT via STATE_LAMPS instead
    # (the manual-deploy button's own fault lamp, found missing by the Step 6 audit).
    "MD11_OVHD_ENG_IGN_OFF_LT": ("Engine Ignition", "Off", "Selected"),
    "MD11_OVHD_LOCK_AUTO_LT": ("Cockpit Door Lock AUTO light", "On", "Off"),
    "MD11_OVHD_LOCK_FAIL_LT": ("Cockpit Door Lock FAIL light", "On", "Off"),
    "MD11_OVHD_LTS_PAINUSE_LT": ("PA in use", "Yes", "No"),
    "MD11_OVHD_LTS_MOVIE_LT": ("Movie light", "On", "Off"),
    "MD11_AOVHD_APU_FUEL_LT": ("APU FUEL light", "On", "Off"),
    "MD11_AOVHD_APU_DOOR_LT": ("APU DOOR light", "On", "Off"),
    "MD11_AOVHD_APU_FAIL_LT": ("APU FAIL light", "On", "Off"),
    "MD11_AOVHD_APU_BLANK_LT": ("APU blank light", "", ""),
    "MD11_AOVHD_APUFIRE_LT": ("APU Fire", "Fire", "Normal"),
    **{f"MD11_AOVHD_ENG{n}FIRE_LT": (f"Engine {n} Fire", "Fire", "Normal") for n in (1, 2, 3)},
    **{f"MD11_AOVHD_ENG{n}AGENT{b}LO_LT": (f"Engine {n} Agent {b} LOW light", "On", "Off") for n in (1, 2, 3) for b in (1, 2)},
    **{f"MD11_AOVHD_CRGSMK_{p}_HEAT_LT": (f"{'Forward' if p == 'FWD' else 'Aft'} Cargo HEAT light", "On", "Off") for p in ("FWD", "AFT")},
    **{f"MD11_AOVHD_CRGSMK_{p}_SMOKE_LT": (f"{'Forward' if p == 'FWD' else 'Aft'} Cargo SMOKE light", "On", "Off") for p in ("FWD", "AFT")},
    **{f"MD11_AOVHD_CRGSMK_{p}_VENTDISAG_LT": (f"{'Forward' if p == 'FWD' else 'Aft'} Cargo Ventilation DISAG light", "On", "Off") for p in ("FWD", "AFT")},
    **{f"MD11_AOVHD_CRGSMK_{p}_VENTOFF_LT": (f"{'Forward' if p == 'FWD' else 'Aft'} Cargo Ventilation OFF light", "On", "Off") for p in ("FWD", "AFT")},
    "MD11_AOVHD_EMER_LT": ("Aft overhead EMER light", "On", "Off"),
    **{f"MD11_MIP_{g}_GREEN_LT": (f"{n} Gear GREEN light", "On", "Off") for g, n in (("NOSE", "Nose"), ("LEFT", "Left"), ("RIGHT", "Right"), ("CTR", "Center"))},
    **{f"MD11_MIP_{g}_RED_LT": (f"{n} Gear RED light", "On", "Off") for g, n in (("NOSE", "Nose"), ("LEFT", "Left"), ("RIGHT", "Right"), ("CTR", "Center"))},
    **{f"MD11_{s}_ABS_DISARM_LT": (f"{n} Autobrake DISARM light", "On", "Off") for s, n in (("GSL", "Captain"), ("GSR", "First Officer"))},
    **{f"MD11_{s}_BELOW_GS_LT": (f"{n} BELOW G/S light", "On", "Off") for s, n in (("GSL", "Captain"), ("GSR", "First Officer"))},
    **{f"MD11_{s}_ENG_FAIL_LT": (f"{n} ENG FAIL light", "On", "Off") for s, n in (("GSL", "Captain"), ("GSR", "First Officer"))},
    "MD11_PED_XPNDR_FAIL_LT": ("Transponder FAIL light", "On", "Off"),
    "MD11_PED_CKPTDOOR_AUTO_LT": ("Cockpit Door AUTO light", "On", "Off"),
    "MD11_PED_CKPTDOOR_FAIL_LT": ("Cockpit Door FAIL light", "On", "Off"),
    **{f"MD11_{m}MCDU_{l}_LT": (f"{n} MCDU {l} light", "On", "Off") for m, n in (("L", "Left"), ("C", "Center"), ("R", "Right")) for l in ("DSPY", "FAIL", "MSG", "OFST")},
    "MD11_CABIN_OXY_MASKS": ("Cabin Oxygen Masks", "Deployed", "Stowed"),
    "MD11_CABIN_POWER": ("Cabin Power", "On", "Off"),
    "MD11_LSIDE_OXY_FLOW_IND": ("Captain Oxygen Flow indicator", "Flow", "No flow"),
    "MD11_RSIDE_OXY_FLOW_IND": ("First Officer Oxygen Flow indicator", "Flow", "No flow"),
    "MD11_EXT_DOOR_CRG_MAIN_OPEN_LT": ("Main Cargo Door OPEN light", "On", "Off"),
    "MD11_EXT_DOOR_CRG_MAIN_CLSD_READY_LT": ("Main Cargo Door CLOSED READY light", "On", "Off"),
    "MD11_EXT_DOOR_CRG_MAIN_LOCK_LT": ("Main Cargo Door LOCK light", "On", "Off"),
    "MD11_EXT_DOOR_CRG_MAIN_UNLOCK_LT": ("Main Cargo Door UNLOCK light", "On", "Off"),
    "MD11_EXT_DOOR_CRG_MAIN_PWR_LT": ("Main Cargo Door PWR light", "On", "Off"),
    **{f"MD11_EXT_DOOR_PAXC_{d}_DISARM_LT": (f"Door {d} DISARM light", "On", "Off") for d in ("1L", "1R")},
    **{f"MD11_LTS_MAP_{n}": (f"Map Light {n}", "On", "Off") for n in (1, 2, 3)},
    # Audio panel call lights (the MIC/VOL lights belong to their button/knob; these do not).
    **{f"{p}_{r}_CALL_LT": (f"{seat} {r} CALL light", "On", "Off")
       for p, seat in (("MD11_PED_CPT_AUDIO_PNL", "Captain"), ("MD11_PED_FO_AUDIO_PNL", "First Officer"), ("MD11_OBS_AUDIO_PNL", "Observer"))
       for r in ("VHF1", "VHF2", "VHF3", "HF1", "HF2", "CAB")},
    **{f"{p}_INT_MECH_LT": (f"{seat} MECH call light", "On", "Off")
       for p, seat in (("MD11_PED_CPT_AUDIO_PNL", "Captain"), ("MD11_PED_FO_AUDIO_PNL", "First Officer"), ("MD11_OBS_AUDIO_PNL", "Observer"))},
    "MD11_PED_CPT_AUDIO_PNL_SAT_TEL_LT": ("Captain SAT TEL light", "On", "Off"),
    "MD11_PED_FO_AUDIO_PNL_SAT_TELL_LT": ("First Officer SAT TEL light", "On", "Off"),
    "MD11_OBS_AUDIO_PNL_SAT_TEL_LT": ("Observer SAT TEL light", "On", "Off"),
}
# Side-panel source-select lights: "<seat> <source> Source CAP 2 light" etc.
for _side, _seat in (("LSIDE", "Captain"), ("RSIDE", "First Officer")):
    for _src, _word in (("APPR", "ILS"), ("CADC", "Air Data"), ("FLTDIR", "Flight Director"), ("FMS", "FMS"), ("VOR", "VOR")):
        STANDALONE_LAMPS[f"MD11_{_side}_INP_{_src}CAP2_LT"] = (f"{_seat} {_word} Source CAP 2 light", "On", "Off")
        STANDALONE_LAMPS[f"MD11_{_side}_INP_{_src}FO1_LT"] = (f"{_seat} {_word} Source FO 1 light", "On", "Off")
    for _tok, _tail in (("EIS_CAP2", "CAP 2"), ("EIS_CAPAUX", "CAP AUX"), ("EIS_FO1", "FO 1"), ("EIS_FOAUX", "FO AUX")):
        STANDALONE_LAMPS[f"MD11_{_side}_INP_{_tok}_LT"] = (f"{_seat} EIS Source {_tail} light", "On", "Off")
    for _tok, _tail in (("IRS_CAPTAUX", "CAPT AUX"), ("IRS_FOAUX", "FO AUX")):
        STANDALONE_LAMPS[f"MD11_{_side}_INP_{_tok}_LT"] = (f"{_seat} IRS Source {_tail} light", "On", "Off")


def breaker_label(node_id, label):
    """'MD11_BKR_BWU_C24' + 'Tank 1 Transfer Pump Power Breaker' -> 'C24 Tank 1 Transfer Pump Power'.

    The grid position is how the real panel identifies a breaker and it is the only thing
    separating the two 'Tank 1 Transfer Pump Power' breakers (C24 and D24). The word
    'Breaker' is dropped because every row of the Circuit Breakers panel is one.
    """
    grid = node_id.rsplit("_", 1)[-1]
    text = re.sub(r"\s+Breaker$", "", label or "").strip() or grid
    return f"{grid} {text}"


def mcdu_key_label(node_id, label):
    """'MD11_LMCDU_LSK_1L_BT' -> 'LSK 1L'; 'MD11_LMCDU_A_BT' -> 'A'; brightness knob keeps its tooltip."""
    parts = node_id.split("_")
    if len(parts) < 3 or parts[1] not in MCDU_SIDES:
        return label
    key = "_".join(parts[2:-1]) if parts[-1] in ("BT", "KB") else "_".join(parts[2:])
    if parts[-1] == "KB":
        return label or f"{MCDU_SIDES[parts[1]]} MCDU Brightness"
    if key.startswith("LSK_"):
        return "LSK " + key[4:]
    return MCDU_KEY_LABELS.get(key, key)


def _strip_suffix(node_id):
    for suf in ("_BT001", "_BT_F", "_BT", "_GRD", "_SW", "_KB", "_LVR"):
        if node_id.endswith(suf):
            return node_id[: -len(suf)]
    return node_id


def finalize_controls(controls):
    """Labels, duplicates, areas and option flags — pure, so it is testable on fixtures.

    Order matters: duplicates are collapsed FIRST (so a guard names the surviving button),
    then areas, then labels.
    """
    # 1. Collapse duplicates: a second clickspot of one button (same kind, same events,
    #    node id = base + '001' / '_F'), and a second lamp node on the same L:var (VIS_VAR
    #    duplicates: the Captain/F-O audio panel families, the '_LT001' / '_LT_F' nodes).
    #    Two lamps on one var would also be two continuous batch entries with one Name,
    #    which shifts every later batch slot (the VarNameCollision invariant).
    kept, seen_lamp_var, by_id = [], set(), {c["node_id"]: c for c in controls}
    for c in controls:
        nid = c["node_id"]
        if c["kind"] == "annun":
            if c["state_var"] in seen_lamp_var:
                continue
            seen_lamp_var.add(c["state_var"])
        else:
            m = re.match(r"^(.*?)(001|_F)$", nid)
            base = by_id.get(m.group(1)) if m else None
            if base is not None and base["kind"] == c["kind"] and base["events"] == c["events"]:
                continue
        kept.append(c)

    labels = {c["node_id"]: c["label"] for c in kept}

    for c in kept:
        nid = c["node_id"]
        # 2. Option flags are not lamps: they describe the installed configuration.
        if nid.startswith("MD11_OPT_"):
            c["kind"] = "option"
        # 3. Areas for raw 3D names and the misplaced options.
        if nid in AREA_FIXES:
            c["area"] = AREA_FIXES[nid]
        # 4. Labels.
        if nid in LABEL_FIXES:
            c["label"], c["label_source"] = LABEL_FIXES[nid], "curated"
        elif nid.startswith("MD11_BKR_"):
            c["label"] = breaker_label(nid, c["label"])
        elif nid.split("_")[1:2] and nid.split("_")[1] in MCDU_SIDES and c["kind"] != "annun":
            c["label"] = mcdu_key_label(nid, c["label"])
        elif c["kind"] == "guard":
            covered = next((o for o in kept if o.get("guard_id") == nid), None)
            if covered is not None:
                c["label"] = f"{labels.get(covered['node_id']) or covered['node_id']} guard"
            elif c["label"] and c["label"].lower().endswith(" guard"):
                pass
            else:
                c["label"] = f"{c['label'] or humanize(nid)} guard"
        elif c["label_source"] == "derived":
            # collect() already runs humanize() when there is no tooltip, so in the real
            # pipeline `c["label"]` is never actually None here -- but finalize_controls is a
            # pure function tested on fixtures that skip collect() entirely, so it must be
            # able to derive the fallback itself rather than assume a caller already did.
            text = c["label"] or humanize(nid)
            if text and text.lower().endswith(" button"):
                text = text[: -len(" button")]
            c["label"] = text
        if c["label"]:
            c["label"] = speakable(c["label"])
    return kept


def _legend_from_id(lamp_id):
    """The printed legend of a lamp with no owner, read off its node id: '..._AC1_OFF_LT' -> 'OFF'.
    Falls back to 'ON' when the last token is not a known legend ('..._IRS_1_LT')."""
    m = re.search(r"_([A-Z0-9]+)_LT$", lamp_id)
    return m.group(1) if m and m.group(1) in LEGEND_MEANINGS else "ON"


def dark_text(legends, node_id):
    """Meaning of every legend dark: the legend-set rule, unless curated."""
    if node_id in DARK_OVERRIDES:
        return DARK_OVERRIDES[node_id]
    legends = list(legends)
    if not legends:
        return None
    if "OFF" in legends:
        return "On"
    if any(l in ("ON", "AVAIL", "ARM", "SEL", "A", "B", "OPEN", "UP", "STOW", "MIC", "VOL") for l in legends):
        return "Off"
    return "Normal"


def latch_for(control, node_ids):
    """The L:var that holds this button's position, ONLY where that is proven.

    TFDi's tooltip reading '(L:<node>)' for its state text is the proof for ~167 buttons
    (anti-ice, system-mode selectors, source selects, breakers…); the battery is proven live.
    A tooltip reading ANOTHER CONTROL's var (ECON reads the air-system mode button) is not a
    latch; a tooltip reading a plain state var that is no control (the door-slide levers read
    MD11_EXT_DOOR_PAX_1L_ARMED_LVR) is.
    """
    nid = control["node_id"]
    if control["kind"] == "guard":
        return {"var": nid, "on": "Open", "off": "Closed"}
    if control["kind"] != "button":
        return None
    if nid in LATCH_FIXED:
        on, off = LATCH_FIXED[nid]
        return {"var": nid, "on": on, "off": off}
    vm = control.get("value_map") or {}
    sv = control.get("state_var")
    if sv and "1" in vm and "0" in vm and (sv == nid or sv not in node_ids):
        return {"var": sv, "on": vm["1"], "off": vm["0"]}
    return None


def pair_lamps(controls):
    """lamp node id -> (owner button, legend). Curated pairings first, then the stem rule:
    <stem>_<LEGEND>_LT with LEGEND a key of LEGEND_MEANINGS, or the bare <stem>_LT (legend ON
    unless overridden). Only BUTTONS fold lamps into their state; a knob's or switch's lamps
    become named rows instead (see lamp_name)."""
    lamps = {c["node_id"]: c for c in controls if c["kind"] == "annun"}
    owners = {}

    def attach(owner, lamp_id, legend):
        if lamp_id in lamps and lamp_id not in owners:
            owners[lamp_id] = (owner, legend)

    for c in controls:
        if c["kind"] != "button":
            continue
        for lamp_id, legend in STATE_LAMPS.get(c["node_id"], []):
            attach(c, lamp_id, legend)
        stem = _strip_suffix(c["node_id"])
        for lamp_id in lamps:
            if not (lamp_id.startswith(stem + "_") and lamp_id.endswith("_LT")):
                continue
            rest = lamp_id[len(stem) + 1:-3]
            if rest and rest not in LEGEND_MEANINGS:
                continue
            attach(c, lamp_id, LAMP_LEGEND_OVERRIDES.get(lamp_id, rest or "ON"))
    return owners


def _owner_by_stem(lamp_id, non_buttons):
    """The knob/switch/handle/lever whose stem the lamp id starts with, if any."""
    for c in non_buttons:
        stem = _strip_suffix(c["node_id"])
        if lamp_id.startswith(stem + "_") and lamp_id.endswith("_LT"):
            return c, lamp_id[len(stem) + 1:-3]
    return None, None


def lamp_name(lamp, owners, non_buttons):
    """(label, lit, dark, label_source) for one lamp."""
    nid = lamp["node_id"]
    if nid in owners:
        owner, legend = owners[nid]
        legend_word = legend.replace("_", " ")
        lit = LAMP_LIT_OVERRIDES.get(nid, LEGEND_MEANINGS.get(legend, legend_word.title()))
        return f"{owner['label']} {legend_word} light", lit, None, "paired"
    if nid in STANDALONE_LAMPS:
        name, lit, dark = STANDALONE_LAMPS[nid]
        return name, lit, dark, "curated"
    owner, rest = _owner_by_stem(nid, non_buttons)
    if owner is not None:
        legend = LAMP_LEGEND_OVERRIDES.get(nid, rest or "ON").replace("_", " ")
        return f"{owner['label']} {legend} light", "On", "Off", "curated"
    return humanize(nid), "On", "Off", "derived"


def apply_state(controls):
    """Attach the 'state' block and lamp names. Pure; call after finalize_controls."""
    owners = pair_lamps(controls)
    # Widened with STATE_LAMPS' own keys: those are hand-curated, always-real OTHER buttons
    # (e.g. the system-mode selectors ECON's tooltip borrows for its Off/On wording), so a
    # var equal to one is "another control's var", never this control's own latch, even on a
    # fixture too small to include that other button as a control of its own. A no-op against
    # the real ~1500-control run: every STATE_LAMPS key is already collected as a real button.
    node_ids = {c["node_id"] for c in controls} | set(STATE_LAMPS)
    non_buttons = [c for c in controls if c["kind"] in ("knob", "knob_push", "knob_pp", "switch", "handle", "lever")]
    by_lamp_owner = {}
    for lamp_id, (owner, legend) in owners.items():
        by_lamp_owner.setdefault(owner["node_id"], []).append((lamp_id, legend))

    for c in controls:
        nid = c["node_id"]
        if c["kind"] == "annun":
            label, lit, dark, source = lamp_name(c, owners, non_buttons)
            c["label"], c["label_source"] = label, source
            legend = owners[nid][1] if nid in owners else LAMP_LEGEND_OVERRIDES.get(nid, _legend_from_id(nid))
            state = {"lamps": [{"var": c["state_var"], "legend": legend, "lit": lit}]}
            if dark is not None:
                state["dark"] = dark
            c["state"] = state
            continue
        if c["kind"] not in ("button", "guard"):
            continue
        lamps = []
        for lamp_id, legend in by_lamp_owner.get(nid, []):
            lamp = next(l for l in controls if l["node_id"] == lamp_id)
            lamps.append({"var": lamp["state_var"], "legend": legend,
                          "lit": LAMP_LIT_OVERRIDES.get(lamp_id, LEGEND_MEANINGS.get(legend, legend.replace("_", " ").title()))})
        latch = latch_for(c, node_ids)
        dark = dark_text([l["legend"] for l in lamps], nid)
        if not lamps and latch is None and dark is None:
            continue            # a momentary button: no state block at all, so MainForm shows a bare label
        state = {"lamps": lamps}
        if latch:
            state["latch"] = latch
        if dark is not None:
            state["dark"] = dark
        c["state"] = state
    return controls


def kind_counts(controls):
    """Per-kind tallies for the JSON `counts.by_kind` block and the printed summary.

    Must be called on the FINALIZED control list, never the pre-finalize one
    `collect()` returns. `finalize_controls` reclassifies every `MD11_OPT_*` row from
    'annun' to 'option'; at the source-template level those rows are still 'annun',
    so tallying `collect()`'s pre-finalize `stats` and then patching an 'option'
    count in on the side counts each reclassified row twice -- once under 'annun'
    (still in the pre-finalize tally) and once under 'option' (the patch). Counting
    the finalized list instead means every control lands under exactly the one kind
    it actually renders as, and the tallies sum to `len(controls)`.
    """
    return Counter(c["kind"] for c in controls)


# Fields that carry a CEVENT id.
EVENT_FIELDS = (
    "LEFT_BUTTON_DOWN",
    "LEFT_BUTTON_UP",
    "RIGHT_BUTTON_DOWN",
    "RIGHT_BUTTON_UP",
    "WHEEL_UP",
    "WHEEL_DOWN",
    "PUSH_DOWN",
    "PUSH_UP",
    "PULL_DOWN",
    "PULL_UP",
)


def read_xml(path):
    """Read a behavior XML leniently.

    These files are exporter-generated and contain raw '&', stray degree signs and
    other tokens a strict XML parser rejects, so parse with a regex rather than
    ElementTree -- we only need flat <UseTemplate> blocks, not a real tree.

    Encoding is mixed: most files are UTF-8, but some carry cp1252 degree signs in
    tooltips (the bank-angle limiter's '5°'). Decoding those as UTF-8 yields U+FFFD
    and the degree silently turns into a replacement char the screen reader spells
    out, so fall back to cp1252 rather than lossily replacing.
    """
    with open(path, "rb") as fh:
        raw = fh.read()
    if raw.startswith(b"\xef\xbb\xbf"):
        raw = raw[3:]
    for enc in ("utf-8", "cp1252", "latin-1"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return raw.decode("utf-8", errors="replace")


def speakable(text):
    """Normalize label text for a screen reader.

    Symbols that are fine to look at are noise to hear: NVDA reads a bare '°' as
    'degrees' only in some punctuation modes and skips it entirely in others, so
    spell it out here rather than depending on the reader's settings. TFDi's XML
    carries HTML entities ('1&lt;-&gt;2'); decode them, and render the arrow as
    'to' because no reader has a good reading for '<->'.
    """
    if not text:
        return text
    text = html.unescape(text)
    text = (
        text.replace("<->", " to ")
        .replace("°", " degrees")
        .replace("△", "delta")
        .replace("�", "")
        .replace("–", "-")
        .replace("—", "-")
    )
    return re.sub(r"\s+", " ", text).strip()


def strip_outer_parens(label):
    """Strip ONE wrapping pair of parentheses, never a lone one.

    The old ``label.strip("()")`` removed the closing parenthesis of a label that merely
    ENDS with a parenthetical — 'APU Generator (APU Panel)' became 'APU Generator (APU
    Panel', and four labels shipped that way.
    """
    label = label.strip()
    if label.startswith("(") and label.endswith(")") and label.count("(") == 1:
        return label[1:-1].strip()
    return label


USETEMPLATE_RE = re.compile(
    r'<UseTemplate\s+Name="([^"]+)"\s*(/>|>(.*?)</UseTemplate>)', re.S
)
FIELD_RE = re.compile(r"<([A-Z0-9_]+)>(.*?)</\1>", re.S)


def parse_tooltip(tooltip):
    """Split a TOOLTIPID into (label, state_var, value_map).

    TFDi tooltips come in two shapes.

    (a) Trailing state parenthetical -- the label proper, then the live state:
        'Engine 1 Fire Handle (%((L:MD11_..._SW))%{case}%{:0}Normal%{:1}GFD%{end})'
        -> label 'Engine 1 Fire Handle', value_map {0: Normal, 1: GFD}

    (b) Inline dynamic word -- the label itself changes with state:
        'Autopilot %((L:MD11_AP_HDG_TRK))%{if}Track%{else}Heading%{end} Select'
        -> label 'Autopilot Heading/Track Select'

    Shape (b) must not be treated as (a): there is no trailing parenthetical to
    strip, and naively cutting at the first '%' would throw away ' Select'. The
    inline block is collapsed to 'Heading/Track' so the spoken label stays a
    stable, complete phrase rather than flapping with the aircraft's state.

    Returns (label, state_var, value_map). value_map is lifted verbatim from the
    aircraft so detent/position wording is TFDi's, never invented here.
    """
    if not tooltip:
        return None, None, {}

    tooltip = re.sub(r"\s+", " ", tooltip.strip())

    # The first L:var mentioned anywhere is what the state text keys off.
    state_var = None
    m = re.search(r"L:([A-Za-z0-9_]+)", tooltip)
    if m:
        state_var = m.group(1)

    # ...unless it describes the AIRFRAME rather than the control. A shape-(b) tooltip can
    # reference a variant flag purely to choose its WORDING:
    #     '%((L:MD11_EFB_IS_CARGO))%{if}Courier Cabin%{else}Forward Cabin%{end} Temperature'
    # That is the freighter/pax split, not the knob's position — the knob is an 8-position
    # temperature selector whose real state is its own ANIM_NAME. Taking IS_CARGO as the state
    # makes the control read a 0/1 flag, so a walk to set it can never converge and every
    # selection reports "did not move". Falls through to VIS_VAR / node_id below, which is the
    # var the ANIM_NAME actually names.
    if state_var in LABEL_ONLY_VARS:
        state_var = None

    # --- (a) peel off a trailing '(<formatting expr>)' -------------------------
    expr = ""
    m = re.search(r"\s\((%.*)\)\s*$", tooltip, re.S)
    if m:
        expr = m.group(1)
        label = tooltip[: m.start()].strip()
    else:
        label = tooltip

    value_map = {}

    def _cases(text):
        out = {}
        for val, lbl in re.findall(r"%\{:\s*([-\d.]+)\s*\}([^%]*)", text):
            lbl = lbl.strip()
            if lbl:
                out[val] = lbl
        return out

    if expr:
        value_map = _cases(expr)
        if not value_map:
            m = re.search(r"%\{if\}([^%]*)%\{else\}([^%]*)%\{end\}", expr)
            if m:
                on, off = m.group(1).strip(), m.group(2).strip()
                if on and off:
                    value_map = {"1": on, "0": off}

    # --- (b) collapse inline dynamic blocks left in the label -------------------
    # '%(<rpn>)%{if}A%{else}B%{end}' -> 'B/A'  (false state first: it reads better
    # as the resting position, e.g. 'Heading/Track', 'IAS/Mach').
    def _inline_if(m):
        a, b = m.group(1).strip(), m.group(2).strip()
        if not value_map:
            value_map.update({"1": a, "0": b})
        return f"{b}/{a}" if a and b else (a or b)

    label = re.sub(
        r"%\([^)]*(?:\)[^)%]*)*?\)\s*%\{if\}([^%]*)%\{else\}([^%]*)%\{end\}",
        _inline_if,
        label,
    )

    # '%(<rpn>)%{case}%{:0}A%{:1}B%{end}' -> 'A/B'
    def _inline_case(m):
        cases = _cases(m.group(0))
        if cases and not value_map:
            value_map.update(cases)
        return "/".join(cases.values()) if cases else ""

    label = re.sub(
        r"%\([^)]*(?:\)[^)%]*)*?\)\s*%\{case\}.*?%\{end\}", _inline_case, label
    )

    # Any remaining numeric interpolation ('%(<rpn>)%!d!', '%!1.2f!') is live data,
    # not label text -- drop it.
    label = re.sub(r"%\([^)]*(?:\)[^)%]*)*?\)", "", label)
    label = re.sub(r"%![^!]*!", "", label)
    label = re.sub(r"%\{[^}]*\}", "", label)

    label = strip_outer_parens(re.sub(r"\s+", " ", label))
    label = re.sub(r"\s+([,/])", r"\1", label)
    label = speakable(label)
    value_map = {k: speakable(v) for k, v in value_map.items()}

    return (label or None), state_var, value_map


def area_of(node_id):
    """Cockpit area from an MD11_<AREA>_... node id."""
    if not node_id:
        return "Other"
    parts = node_id.split("_")
    if len(parts) >= 2 and parts[0] == "MD11":
        return AREA_LABELS.get(parts[1], parts[1].title())
    return "Other"


# Expansions for the node-id humanizer. Annunciators (and ~a third of the buttons)
# carry no TOOLTIPID at all, so their spoken label has to be derived from the node
# id -- 'MD11_OVHD_ELEC_GEN1_ARM_LT' -> 'Generator 1 Arm'. A screen reader reads
# these aloud, so expand the abbreviations rather than spelling out consonants.
ABBREV = {
    "LT": "light", "BT": "button", "KB": "knob", "SW": "switch", "GRD": "guard",
    "LVR": "lever", "IND": "indicator", "PB": "pushbutton", "ANN": "annunciator",
    "GEN": "generator", "APU": "APU", "ELEC": "electrical", "HYD": "hydraulic",
    "PNEU": "pneumatic", "PRESS": "pressurization", "TEMP": "temperature",
    "PWR": "power", "EXT": "external", "XFER": "transfer", "XFEED": "crossfeed",
    "ISOL": "isolation", "VLV": "valve", "PMP": "pump", "ENG": "engine",
    "FIRE": "fire", "AGENT": "agent", "DISCH": "discharged", "ARM": "arm",
    "AUTO": "auto", "MAN": "manual", "NORM": "normal", "OVRD": "override",
    "STBY": "standby", "EMER": "emergency", "BATT": "battery", "BUS": "bus",
    "AC": "AC", "DC": "DC", "XPNDR": "transponder", "NAV": "nav", "COM": "com",
    "ADF": "ADF", "VOR": "VOR", "ILS": "ILS", "DME": "DME", "RA": "radio altimeter",
    "FD": "flight director", "AP": "autopilot", "AT": "autothrottle",
    "ATS": "autothrottle", "SPD": "speed", "HDG": "heading", "ALT": "altitude",
    "VS": "vertical speed", "FPA": "flight path angle", "IAS": "IAS",
    "MACH": "Mach", "TRK": "track", "PROF": "profile", "FMS": "FMS",
    "APPR": "approach", "LAND": "land", "GA": "go around", "TO": "takeoff",
    "CLB": "climb", "CRZ": "cruise", "DES": "descent", "FLAP": "flap",
    "SLAT": "slat", "GEAR": "gear", "BRK": "brake", "SPDBRK": "speedbrake",
    "ANTISKID": "antiskid", "STEER": "steering", "TILLER": "tiller",
    "TRIM": "trim", "AIL": "aileron", "ELEV": "elevator", "RUD": "rudder",
    "STAB": "stabilizer", "LTS": "lights", "FLOOD": "flood", "PNL": "panel",
    "DOME": "dome", "BCN": "beacon", "STROBE": "strobe", "TAXI": "taxi",
    "RWY": "runway", "TURNOFF": "turnoff", "LOGO": "logo", "WING": "wing",
    "ICE": "ice", "ANTIICE": "anti-ice", "WAI": "wing anti-ice",
    "EAI": "engine anti-ice", "PROBE": "probe", "WSHLD": "windshield",
    "WIPER": "wiper", "RAIN": "rain", "OXY": "oxygen", "MASK": "mask",
    "PAX": "passenger", "CRG": "cargo", "DOOR": "door", "SLIDE": "slide",
    "CAB": "cabin", "PA": "PA", "INT": "interphone", "CALL": "call",
    "ATT": "attendant", "MECH": "mechanic", "GND": "ground", "SVC": "service",
    "PACK": "pack", "BLEED": "bleed", "DUCT": "duct", "FAN": "fan",
    "RECIRC": "recirculation", "COND": "conditioning", "OUTFLOW": "outflow",
    "CPT": "captain", "FO": "first officer", "OBS": "observer",
    "L": "left", "R": "right", "CTR": "center", "UPR": "upper", "LWR": "lower",
    "FWD": "forward", "AFT": "aft", "MAIN": "main", "TAIL": "tail",
    "MSTR": "master", "WARN": "warning", "CAUT": "caution", "FAIL": "fail",
    "INOP": "inoperative", "TEST": "test", "RST": "reset", "SEL": "select",
    "MODE": "mode", "DSPL": "display", "DU": "display unit", "MCDU": "MCDU",
    "EAD": "EAD", "SD": "system display", "PFD": "PFD", "ND": "ND",
    "ISFD": "standby display", "BRT": "brightness", "DIM": "dim",
    "FUEL": "fuel", "TANK": "tank", "QTY": "quantity", "BOOST": "boost",
    "MAGTRU": "magnetic/true", "TCAS": "TCAS", "WXR": "weather radar",
    "TERR": "terrain", "GPWS": "GPWS", "EVAC": "evacuation", "SMOKE": "smoke",
    "SEATBELT": "seatbelt", "NOSMOKING": "no smoking", "IRS": "IRS",
    "ADIRU": "ADIRU", "ALIGN": "align", "ATTD": "attitude",
}


def humanize(node_id):
    """Turn 'MD11_OVHD_ELEC_GEN1_ARM_LT' into 'Electrical generator 1 arm light'."""
    if not node_id:
        return None
    parts = node_id.split("_")
    if parts and parts[0] == "MD11":
        parts = parts[1:]
    # Drop the leading area token; the area is carried separately.
    if parts and parts[0] in AREA_LABELS:
        parts = parts[1:]
    words = []
    for p in parts:
        # Split a trailing digit run: GEN1 -> generator 1
        m = re.match(r"^([A-Z]+)(\d+)$", p)
        if m:
            stem, num = m.group(1), m.group(2)
            words.append(ABBREV.get(stem, stem.lower()))
            words.append(num)
            continue
        words.append(ABBREV.get(p, p.lower() if not p.isdigit() else p))
    if not words:
        return None
    text = " ".join(w for w in words if w)
    return text[:1].upper() + text[1:]


def collect(pkg_dir):
    base = os.path.join(pkg_dir, md11_paths.PACKAGE_MARKER)
    if not os.path.isdir(base):
        sys.exit(f"ModelBehaviorDefs not found under {base}")

    controls = []
    seen = set()
    stats = Counter()

    for root, _dirs, files in os.walk(base):
        # Templates/ holds the definitions, not the instances -- skip.
        if os.path.basename(root) == "Templates":
            continue
        for fname in sorted(files):
            if not fname.endswith(".xml"):
                continue
            path = os.path.join(root, fname)
            source = os.path.relpath(path, base).replace("\\", "/")
            text = read_xml(path)

            for m in USETEMPLATE_RE.finditer(text):
                tmpl = m.group(1)
                body = m.group(3) or ""
                kind = TEMPLATE_KINDS.get(tmpl)
                if kind is None:
                    stats["skipped_template:" + tmpl] += 1
                    continue

                fields = {}
                for fm in FIELD_RE.finditer(body):
                    fields[fm.group(1)] = (fm.group(2) or "").strip()

                node_id = fields.get("NODE_ID")
                if not node_id:
                    stats["no_node_id"] += 1
                    continue

                events = {}
                for ef in EVENT_FIELDS:
                    v = fields.get(ef)
                    if v and v.lstrip("-").isdigit():
                        events[ef] = int(v)

                # An annunciator with no events is a lamp; its lit state is the
                # L:var itself (VIS_VAR overrides which var drives visibility).
                label, state_var, value_map = parse_tooltip(fields.get("TOOLTIPID"))

                key = (node_id, kind, tuple(sorted(events.items())))
                if key in seen:
                    stats["duplicate"] += 1
                    continue
                seen.add(key)

                # Prefer TFDi's own wording; fall back to the node id only when the
                # exporter emitted no tooltip (every annunciator, ~a third of buttons).
                if label:
                    label_source = "tooltip"
                else:
                    label = humanize(node_id)
                    label_source = "derived"

                # Curated truth wins for the few controls whose state is an RPN
                # range test rather than a %{case} map (see CURATED).
                curated = CURATED.get(node_id)
                if curated:
                    if curated.get("label"):
                        label = curated["label"]
                        label_source = "curated"
                    stats["curated"] += 1

                num_states = fields.get("NUM_STATES")
                controls.append(
                    {
                        "node_id": node_id,
                        "kind": kind,
                        "template": tmpl,
                        "area": area_of(node_id),
                        "label": label,
                        "label_source": label_source,
                        **(
                            {k: v for k, v in curated.items() if k != "label"}
                            if curated
                            else {}
                        ),
                        "state_var": state_var or fields.get("VIS_VAR") or node_id,
                        "value_map": value_map,
                        "num_states": int(num_states)
                        if num_states and num_states.isdigit()
                        else None,
                        "events": events,
                        "guard_id": fields.get("GUARD_ID"),
                        "source": source,
                    }
                )
                stats["kind:" + kind] += 1

    return controls, stats


# Event-name suffixes. The wasm also embeds strings like
# 'MD11_FLAP_LATCH_WHEEL_UP' which are event names, not variables -- filter them
# out of the export-var scan or they read as phantom L:vars.
EVENT_NAME_SUFFIX = re.compile(
    r"_(WHEEL_UP|WHEEL_DOWN|LEFT_BUTTON_DOWN|LEFT_BUTTON_UP|RIGHT_BUTTON_DOWN"
    r"|RIGHT_BUTTON_UP|PUSH_UP|PUSH_DOWN|PULL_UP|PULL_DOWN)$"
)

# Prefixes of the documented integration/state surface (TFDi's Integration Guide
# 'Variables' page). These are NOT in the Aircraft::vars control table -- they are
# the read-only state and external-control exports, and they are exactly what the
# blind-pilot read-outs need (FCP window values, AP state, fuel, APU).
EXPORT_PREFIXES = (
    "MD11_AFS_",      # FCP selected SPD/HDG/ALT/VS windows
    "MD11_AP_",       # AP_STATE, IAS_MACH, HDG_TRK, VS_FPA, FT_M
    "MD11_ATS_",      # ATS_STATE, ATS_CLAMP (autothrottle)
    "MD11_APU_",      # APU N1/N2/STATE
    "MD11_EXTCTL_",   # writable external control (fuel, FCP, flap, spoiler, baro)
    "MD11_OVHD_TANK_",  # fuel tank quantities
    "MD11_YOKE_",     # normalized yoke position (added v1.1.18 for 3rd-party HW)
    "MD11_FLAPS_",    # FLAPS_MOVING
    "MD11_STBY_",     # standby instrument state
    "MD11_WBS_",      # weight and balance
    "MD11_CAP_",      # CAP_ALTIMETER, CAP_MINIMUMS
    "MD11_FO_",       # FO_ALTIMETER, FO_MINIMUMS
)

# Documented exports with no shared prefix to key on. The V-speeds are single
# tokens (MD11_V1, MD11_VR, ...) so a prefix rule would either miss them or, with
# a bare "MD11_V" prefix, drag in every unrelated MD11_VLV_*/MD11_VOR_* control.
# These are the take-off and retraction speeds a blind pilot cannot read off the
# PFD speed tape -- the DUs are WASM-rendered, so there is no other source.
EXPORT_EXACT = (
    "MD11_V1",        # take-off decision speed
    "MD11_VR",        # rotation speed
    "MD11_V2",        # take-off safety speed
    "MD11_VSR",       # slat retraction speed
    "MD11_VFR",       # flap retraction speed
)


def wasm_vars(wasm_path):
    """Pull the L:var surface out of md11host.wasm.

    The module embeds two distinct tables and they must not be conflated:

      * 'Aircraft::vars->MD11_...'  -- the ~1500 clickable-cockpit control vars
        that the ModelBehaviorDefs also reference.
      * bare 'MD11_...' strings     -- everything else, including the documented
        integration exports (MD11_AFS_SPD, MD11_AP_STATE, MD11_EXTCTL_*). None of
        these appear under Aircraft::vars, so a control-table-only scan misses the
        entire read-out surface.

    Returns (control_vars, export_vars).
    """
    if not wasm_path or not os.path.isfile(wasm_path):
        return set(), set()
    with open(wasm_path, "rb") as fh:
        data = fh.read()
    control = {m.decode() for m in re.findall(rb"Aircraft::vars->([A-Za-z0-9_]+)", data)}
    every = {m.decode() for m in re.findall(rb"\b(MD11_[A-Za-z0-9_]{2,60})\b", data)}
    export = {
        v
        for v in every - control
        if (v.startswith(EXPORT_PREFIXES) or v in EXPORT_EXACT)
        and not EVENT_NAME_SUFFIX.search(v)
    }
    return control, export


def _exit_no_wasm(package_dir):
    """One owner for the missing-wasm error: both the --pkg path and the
    discovery path reach it, and they must say the same thing."""
    sys.exit(
        "Found the MD-11 package but not %s inside it:\n  %s\n"
        "Searched every folder under SimObjects/. Pass --wasm to point at it "
        "directly." % (md11_paths.WASM_NAME, package_dir)
    )


def resolve_paths(pkg_arg, wasm_arg):
    """Settle the package + wasm paths, or exit with a message explaining why not.

    Every failure exits non-zero WITHOUT writing a map: a partial map is worse
    than none, because it silently drops the wasm-derived read-outs.
    """
    # An explicit --wasm is taken on faith below (`wasm_arg or ...`), and a
    # missing/unreadable file there degrades silently: wasm_vars() just returns
    # empty sets, so the map still gets written but with ZERO wasm-derived
    # L:vars -- the same silent-partial-map failure this function exists to
    # rule out, just arriving through the override instead of a bad guess.
    # The discovered path (find_wasm) is never checked here: it only ever
    # returns paths it found by walking, so it already exists.
    if wasm_arg and not os.path.isfile(wasm_arg):
        sys.exit(
            "--wasm does not exist: %s\n"
            "Expected an %s file at that path."
            % (wasm_arg, md11_paths.WASM_NAME)
        )

    if pkg_arg:
        pkg = pkg_arg
        if not os.path.isdir(os.path.join(pkg, md11_paths.PACKAGE_MARKER)):
            sys.exit(
                "Not an MD-11 package: %s\n"
                "Expected it to contain %s"
                % (pkg, md11_paths.PACKAGE_MARKER)
            )
        wasm = wasm_arg or md11_paths.find_wasm(pkg)
        if not wasm:
            _exit_no_wasm(pkg)
        return pkg, wasm

    finds = md11_paths.discover()

    if not finds:
        roots = md11_paths.describe_roots()
        searched = ("\n".join("  " + r for r in roots)
                    if roots else "  (no MSFS package folders found at all)")
        sys.exit(
            "Could not find the TFDi MD-11 on this PC.\n"
            "Searched these package folders (and up to %d levels below each) "
            "for a folder containing %s:\n%s\n"
            "If it lives somewhere else, pass --pkg <folder>."
            % (md11_paths.MAX_DEPTH, md11_paths.PACKAGE_MARKER, searched)
        )

    if len(finds) > 1:
        print("The MD-11 is installed in more than one place:")
        for i, f in enumerate(finds, 1):
            print("  %d) %s  %s" % (i, f.sim_label, f.package_dir))
        if not (sys.stdin and sys.stdin.isatty()):
            sys.exit(
                "Re-run with --pkg <folder> to choose one "
                "(no terminal attached, so cannot prompt)."
            )
        chosen = None
        while chosen is None:
            try:
                answer = input("Which one? [1-%d] " % len(finds))
            except (EOFError, KeyboardInterrupt):
                sys.exit("\nCancelled.")
            chosen = md11_paths.parse_choice(answer, len(finds))
            if chosen is None:
                print("Enter a number from 1 to %d." % len(finds))
        find = finds[chosen]
    else:
        find = finds[0]

    print("Using %s: %s" % (find.sim_label, find.package_dir))

    wasm = wasm_arg or find.wasm_path
    if not wasm:
        _exit_no_wasm(find.package_dir)
    return find.package_dir, wasm


def _exit_if_incomplete(out_path, pkg, wasm, controls, all_vars):
    """Refuse to write a map missing controls or the wasm-derived L:vars.

    The owner's ruling: refuse to write anything rather than emit a partial
    map. A map with no controls is missing every clickable cockpit control; a
    map with no wasm control vars is missing the PFD speed tape and V-speed
    read-outs, which have no other source. Either looks like an ordinary
    successful run (exit 0, a file written) unless something checks for it
    here -- collect() and wasm_vars() both degrade silently (an empty XML
    folder, or a wasm with no 'Aircraft::vars->' symbols, just produce empty
    results, not an exception).
    """
    problems = []
    if not controls:
        problems.append("no controls (found 0 in the package's ModelBehaviorDefs)")
    if not all_vars:
        problems.append(
            "no wasm control vars (found 0 'Aircraft::vars->MD11_...' symbols in the wasm)"
        )
    if not problems:
        return
    sys.exit(
        "Refusing to write %s: %s.\n"
        "Package : %s\n"
        "Wasm    : %s\n"
        "A partial map is worse than none, so nothing was written. This "
        "usually means --wasm points at the wrong file, the wasm is "
        "truncated or partial, or a TFDi update changed the symbol "
        "convention this script parses -- not that the aircraft genuinely "
        "has no controls." % (out_path, "; ".join(problems), pkg, wasm)
    )


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pkg", default=None,
                    help="MD-11 package folder. Omit to search this PC.")
    ap.add_argument("--wasm", default=None,
                    help="md11host.wasm path. Omit to use the one found next to --pkg "
                         "(or discovered automatically).")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "md11_control_map.json"))
    args = ap.parse_args()

    pkg, wasm = resolve_paths(args.pkg, args.wasm)

    controls, stats = collect(pkg)
    controls = finalize_controls(controls)
    controls = apply_state(controls)
    # by_kind (below) and the printed summary both come from the FINALIZED list via
    # kind_counts(), never from `stats` (collect()'s pre-finalize per-kind tally) --
    # see kind_counts()'s docstring for why patching stats on the side double-counts.
    by_kind = kind_counts(controls)
    all_vars, export_vars = wasm_vars(wasm)

    _exit_if_incomplete(args.out, pkg, wasm, controls, all_vars)

    referenced = {c["node_id"] for c in controls} | {
        c["state_var"] for c in controls if c["state_var"]
    }
    orphan_vars = sorted(v for v in all_vars if v not in referenced)

    by_area = defaultdict(list)
    for c in controls:
        by_area[c["area"]].append(c)

    out = {
        "_generated_by": "tools/md11-gen/generate_md11_map.py",
        "_source": "TFDi MD-11 ModelBehaviorDefs + md11host.wasm",
        "counts": {
            "controls": len(controls),
            "wasm_control_vars": len(all_vars),
            "export_vars": len(export_vars),
            "state_only_vars": len(orphan_vars),
            "by_kind": dict(sorted(by_kind.items())),
            "by_area": {a: len(v) for a, v in sorted(by_area.items())},
        },
        "controls": sorted(controls, key=lambda c: (c["area"], c["node_id"])),
        # The documented read-out / external-control surface (not clickable controls).
        "export_vars": sorted(export_vars),
        # Control-table vars no ModelBehaviorDefs control references -- animation
        # ranges (_RNG), exterior states, and the FCP push/pull latch vars.
        "state_only_vars": orphan_vars,
    }

    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(out, fh, indent=1, ensure_ascii=False)

    print(f"wrote {args.out}")
    print(f"  controls        : {len(controls)}")
    print(f"  wasm ctrl vars  : {len(all_vars)}")
    print(f"  export vars     : {len(export_vars)}")
    print(f"  state-only vars : {len(orphan_vars)}")
    print("  by kind         :")
    for k, v in sorted(by_kind.items()):
        print(f"    {k:10s} {v}")
    tip = sum(1 for c in controls if c["label_source"] == "tooltip")
    derived = sum(1 for c in controls if c["label_source"] == "derived")
    mapped = sum(1 for c in controls if c["value_map"])
    print(f"  label from TFDi : {tip}/{len(controls)}")
    print(f"  label derived   : {derived}/{len(controls)}")
    print(f"  with value map  : {mapped}/{len(controls)}")
    skipped = {k[len("skipped_template:"):]: v for k, v in stats.items() if k.startswith("skipped_template:")}
    if skipped:
        print("  skipped templates (not controls):")
        for k, v in sorted(skipped.items(), key=lambda kv: -kv[1])[:10]:
            print(f"    {v:5d}  {k}")


if __name__ == "__main__":
    main()
