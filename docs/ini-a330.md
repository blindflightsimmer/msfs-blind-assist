# iniBuilds A330 (MSFS 2024)

The A330-200 that ships INSIDE Microsoft Flight Simulator 2024, built by iniBuilds (streamed,
encrypted package `fs24-microsoft-aircraft-a330`, title "A330-200 (RR)"). Aircraft code
`INI_A330`, `Aircraft/IniA330Definition*.cs` + `Aircraft/A330/`. It shares the A300's autoflight
code base (`INI_*` L:vars, A300 mode numbers) but NOT its control transport. Not to be confused
with the LatinVFR A330 (`LvfrA330Definition`), a different aircraft.

The pixel-read MCDU classes (`Aircraft/A300/` — `A300CduScreenReader`, `A300CduPopout`,
`A300CduGlyphLibrary`, `CduLayout`) and the windows in `Forms/IniA300/` carry the A300's name
because the technique was first built for the iniBuilds A300 (FS2020), which has no text export
either. They are shared iniBuilds infrastructure: on this branch they serve the A330 and the A321LR
(`docs/ini-a321.md`), each with its own profile, layout and glyph library.

## Mechanisms (each measured live, 2026-10-16)

* **Controls are FS2024 input events** (`B:AIRLINER_*`, 649 of them), not writable L:vars. The
  `INI_*` L:vars are the state the WASM reports. Two write forms, measured per control:
  * selectors and latching switches: `SetInputEvent(hash, value)` where the value is the POSITION;
  * momentary keys only react to the calculator form `(>B:NAME_Push)` / `(>B:NAME_Release)` —
    SetInputEvent with 1 then 0 does nothing on them.
  Conventions (measured, encoded in the map's `positions`): a push-button's input 0 = ON (its
  L:var reads 1), a 3-position switch is numbered from the TOP (0 = top), a 2-position switch has
  1 = top. Never assume the L:var value equals the input value — use `A330ControlMap.InputFor`.
* **The panel map is GENERATED** — `tools/a330-gen/generate_a330_map.py` →
  `Resources/a330_control_map.json` (281 controls), from a differential probe that fired every
  input event and diffed all 1,936 L:vars. Regenerate it; never hand-edit. A control's `read` is
  an L:var, a stock SimVar (`A:NAME, unit`), or — where no L:var moves — its own input event
  (`B:NAME`).
* **Input-event READS** are a generic SimConnectManager feature (`SimVarType.InputEvent`,
  `SimConnectManager.InputEventReads.cs`): `SubscribeInputEvent` + `GetInputEvent`, request ids
  701–890 rotating. They are skipped by data-definition registration and the continuous batch
  (they cost no data-def slot) and are re-subscribed after every input-event enumeration.
* **FCU:** writing the dial L:vars directly works (`INI_HEADING_DIAL` 270 → AP heading 270), as on
  the A300. Pulses: `INI_FCU_SELECTED/MANAGED_SPEED/HEADING_BUTTON`,
  `INI_FCU_ALTITUDE_PULL/PUSH_COMMAND`, `INI_AP1/AP2_BUTTON`, `INI_LOCALIZER_BUTTON`,
  `INI_APPROACH_BUTTON`, `INI_FCU_HDG_VS_COMMAND` (TRK/FPA). V/S pull = SetInputEvent
  `AIRLINER_MCU_VS_PULL` 1 then 0. SPD/MACH and METRIC are input events.
* **Flight modes:** A300 mode numbers in the `INI_*` FMA L:vars, A330 words (`A330Fma`). An
  unknown number is spoken as "vertical mode N", never guessed; every change is logged raw to
  `fma.log` so the words can be checked after a flight.
* **FCU dialogs** (Ctrl+H/S/A/V) carry every button beside that knob on the real FCU, each showing
  its live state: heading — pull, push, LOC, HDG-V/S / TRK-FPA; speed — pull, push, SPD/MACH, A/THR;
  altitude — pull, push, ALT, knob steps (100/1000 ft), metric, AP 1, AP 2; V/S — pull, push,
  HDG-V/S / TRK-FPA, APPR. No EXPED (this aircraft has no such input event). Live-verified: all four
  open with their buttons; the knob-steps button drives the aircraft and its label follows.
* **ECAM pages** (`INI_ECAM_ACTIVE_PAGE`): ENG 1, HYD 2, EL/AC 3, EL/DC 4, BLEED 5, COND 6,
  PRESS 7, FUEL 8, APU 9, F/CTL 10, DOOR 11, WHEEL 12, STS 14.
* **MCDU — read from pixels**, the A300 method generalised: there is no text export
  (`iniAirbusMCDU_1` answers ILLEGAL_OPERATION; `INI_MCDU_OPTION` changes nothing). Shift+M forces
  the pop-out (`A300CduPopout` with `CduPopoutProfile.A330`): instrument view 12 (captain MCDU
  head-on; F/O 13 — 0-based), wait for the camera to stop moving, bring the sim to the foreground
  (Alt-tap, AttachThreadInput, SwitchToThisWindow, then WScript `AppActivate` — the A330 runs in a
  2024 window that refuses the plain call from a background process), Right-Alt click the DARK
  GLASS found by flooding from the expected screen point (the A300's green-cluster finder fails
  here: the menu page is mostly white/grey), 2 rounds. Measured: 5/5 first-click successes from a
  background console starting in the pilot view, and Shift+M from the deployed app with NO pop-out
  open created it and read the page; page keys, Page Up/Down and Alt shortcuts verified in the window. Grid `CduLayout.A330` (y0 30.2, pitch 23.8,
  advance 15.0, cap height 18.3 (must equal the trainer's `BASE_CAP` — pinned by a test), ink threshold 40 — grey text peaks ~77, so the A300's higher
  threshold loses it). Colour per character (`CduColour`); grey words are spoken
  "(unavailable)" (`CduLineText`). Glyphs: `Resources/a330-cdu-glyphs.bin`, trained in
  `tools/a330-cdu-ocr` (39 labelled pages; leave-one-page-out 99.79 % (4237/4246), residual
  confusions O/Q, R/P, long dash vs '='). The three F-PLN pages added 2026-10-17 taught `(`, `)` and
  the ditto `"` — before them "(T/C)" read "1T/C1" and a repeated speed read "." — retrain there, never patch the .bin.
* **MCDU keys:** write 1 to `INI_MCDU1_<KEY>`. Verified: DIR PROG PERF INIT DATA FPLAN RADNAV
  AIRPORT MENU FUEL SEC ATC PREV NEXT, letters, digits. Slew keys verified in flight 2026-10-17 and,
  like the A300's, named BACKWARDS: `UARROW` moves on down the F-PLN, `DOWN` back toward the top. The
  window follows the A320 convention: Page Up/Down and Alt+Up/Down scroll, Alt+Left/Right change page.
  From the iniBuilds A320 naming, unverified: OVERFLY, DECIMAL, SPACE, PLUS (the +/- key), CLR.
* **Camera restore after the MCDU pop-out** is written, read back and repeated until it holds, from
  the UI thread: a single type+index write sent from the pop-out's pool thread was reported sent and
  left the camera on the MCDU close-up (live 2026-10-17), which silences the cockpit ambience.
* **EFB:** Coherent HTML page "iniEfbA330", `Resources/coherent-a330-efb-agent.js` (fork of the
  A300 agent) + `CoherentPmdgEfbClient.ForIniA330()`. Side-bar buttons, door state from the
  `door-open`/`door-transit` class or the tile's gradient, ground equipment from `INI_GPU_AVAIL`,
  `INI_CHOCKS_ENABLED`, `INI_COVERS_ENABLED`. Live-verified scrape + click (Ground tab) and the
  Payload page. `A.FIELDS` must have ONE key per element id — an object literal keeps the last
  duplicate, and an A300 leftover `zfw` entry renamed the cargo slider "Zero fuel weight". Units come
  from the page's unit box (kg or lb), never from the field name.
* **Camera views (0-based):** ECAM 3, PFD/ND 6, EFB 4/5, captain MCDU 12, F/O MCDU 13, pedestal 22.

## Unverified (needs a flight)
* FMA words for each mode number (`fma.log` has the raw values).
* A/THR, AP1/AP2, LOC and APPR pulses do nothing on the ground — expected, but unproven in the air.
* Parking brake direction (`AIRLINER_PARKINGBRAKE` input 0 = set), landing-light position words,
  dome/wiper labels, several `B:` reads.
* MCDU OVERFLY (needs a flight plan).

## Probe side effect left in the aircraft
* Probing left RMP 1's NAV backup ON (`INI_RMP1_ILS_STATE`/`VOR_STATE` = 1) and it would not
  toggle back from outside. Press NAV on the captain's RMP to clear it.
