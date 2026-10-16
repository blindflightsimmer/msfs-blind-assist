# iniBuilds A321LR (MSFS 2024)

The A321LR that ships INSIDE Microsoft Flight Simulator 2024, built by iniBuilds (streamed, encrypted package
`fs24-microsoft-aircraft-a321`, title "A321"). Aircraft code `INI_A321`, `Aircraft/IniA321Definition.cs` — a
subclass of the A330's definition (`IniA330Definition`), which carries virtual hooks for everything that differs.

## Mechanisms (each measured live, 2026-10-16)

* **Same code base as the A330, different control transport.** 1,834 of the A321's 2,732 panel-state L:vars
  share the A330's names, and the autoflight is the A300's (FCU dials, press flags, FMA numbers), so readouts,
  FMA, dialogs, call-outs and the monitor manager are the A330's code. BUT the A321's FS2024 input events
  (581) do NOTHING from outside: 304 were set by value and 240 pressed with `_Push`/`_Release`, and no L:var
  moved. The `INI_*` L:vars ARE the inputs, as on the A300.
* **Panels** come from the GENERATED `Resources/a321_control_map.json` (`tools/a321-gen`, 168 controls):
  the A330 map re-pointed — every A330 control whose L:var exists here and was proven writable becomes a direct
  write (`"mode": "rpn"`, `{value} (>L:VAR)`) with the A330's measured labels — plus the A321's own controls.
  A direct-write probe wrote each candidate, waited 1.5 s and recorded what moved DOWNSTREAM (a held value alone
  can be a dead mirror). Never hand-edit the JSON; every name is validated against `tools/a321-gen/a321_lvars.txt`.
* **Toggling keys** whose state lives elsewhere use `"mode": "toggle"` with an `rpn` press (cabin pressure
  manual: press `INI_CABIN_MAN_PRESS_BUTTON` only when `INI_CABIN_MAN_PRESS` differs from the pick).
* **Measured semantics worth knowing:** battery switches are `INI_OVHD_ELEC_BAT_n_PB_IS_AUTO_SWITCH` (the
  `…_PB_IS_AUTO` twin reverts a write); landing lights are one switch per side (`A320_LANDING_LIGHT_SWITCH_LEFT/RIGHT`,
  0 = ON, 1 = OFF, 2 = RETRACT — the single `INI_LANDING_LIGHT_SWITCH` reads 0 with both lights on and is dead);
  ELAC/SEC/FAC are `A320_*_STATUS` (writing ELAC 1 off changed the flight-control laws); cabin temperature
  `INI_AIRCOND_LVL_n` is 2 °C a step (0 = 18, 3 = 24).
* **FCU:** the knob pulls/pushes are the one place input events work — `INSTRUMENT_FCU_*_PULL/PUSH` answer a value
  press (heading pull engaged HDG, V/S pull V/S). AP 1/2, LOC, APPR are the A330's L:var press flags; V/S push is
  `INI_FCU_LEVEL_OFF_COMMAND`; EXPED `A320_FCU_EXPEDITE_COMMAND`; SPD/MACH `INI_SPD_MACH_BUTTON` (toggles);
  TRK/FPA `INI_FCU_HDG_VS_COMMAND` (toggles); metric `INI_FCU_METRIC_STATE` (direct write). There is no ALT button
  (an A330 key) and the 100/1000 selector is not reachable from outside, so neither is offered. The FCU windows are
  set by direct dial writes as on the A330 (heading 270 written through the Ctrl+H dialog, read back 270).
* **Autobrake:** MAX is a toggling key (`A320_AUTOBRAKE_MAX_COMMAND`, level 0 → 3 → 0); LO/MED write
  `INI_AUTOBRAKE_LEVEL`. Parking brake: `LANDING_GEAR_PARKINGBRAKE` reads 100 with the brake set.
* **Speeds / fuel:** green dot `FMGEC_GD_SPD`, S `FCPC_S_SPEED`, F `FCPC_F_SPEED`; tanks in kg from
  `INI_FUEL_WEIGHT_LEFT_INNER/RIGHT_INNER/CENTER`; the LR's additional centre tanks are not published and are the
  remainder of the sim's total.
* **MCDU — read from pixels** (no `iniAirbusMCDU_1` text export here either). `CduPopoutProfile.A321`: views 12/13
  (the A330's), screen at (0.50, 0.29) in view 12, a nearly SQUARE pop-out (2324x2301 as it first opens), parked at
  a 1350x1360 window (a profile size — the A330's 1350x1138 would squash it and the finder would stop recognising it).
  `CduLayout.A321`: y0 54.8, pitch 25.53, advance 16.77, cap 20.3 at the 450 base, threshold 40, and
  `TinyMarksJoinRuns` (a decimal point never splits a same-size run: medium "12.1" beside large "/FF+FQ" left the
  lone "1" measured against the large letters and read as 8). Glyphs `Resources/a321-cdu-glyphs.bin`, trained in
  `tools/a321-cdu-ocr` (36 scratchpad strings + 37 pages); the A330's glyphs misread O/D, B/R, 8/B, Z/7, 5/S here.
  Keys are the A330's `INI_MCDU1_*` names; CLR is `INI_MCDU1_CLR` — NEVER write `INI_MCDU1_CLR_KEY`, a HELD state that
  wipes everything typed while it stays 1.
* **EFB:** "iniEfbA321LR" — the A330's tablet page for page, read by `coherent-a330-efb-agent.js` unchanged
  (`CoherentPmdgEfbClient.ForIniA321()`). Its doors are 1 and 4 on the page itself (2 and 3 are over-wing exits).
* **Camera views (0-based):** ECAM 3, PFD/ND 23, captain MCDU 12, F/O MCDU 13, EFB 4/5.

## Unverified (needs a flight)
* A/THR (`AP8_BUTTON`, the A300's numbering) — a press arms nothing on the ground, as on the real aircraft.
* AP 1/2, LOC, APPR engage; FMA words (A330Fma; `fma.log` has the raw numbers).
* Autobrake LO/MED by level write; parking brake write direction; APU start press; RMP buttons; ECAM page keys
  beyond the direct write measured.

## Probe side effects (all restored)
The probing toggled and then restored the cockpit; a few input-event probes left anti-skid, beacon, turn-off and
wing lights, both RMPs and the baro units changed, and EXPED/manual pressurisation engaged — each was put back and
verified (flaps 1, all lights on, modes cleared by cycling the FDs).
