---
paths:
  - "MSFSBlindAssist/Aircraft/IniA330Definition*.cs"
  - "MSFSBlindAssist/Aircraft/A330/**"
  - "MSFSBlindAssist/Forms/IniA330/**"
  - "MSFSBlindAssist/MainForm.IniBuilds.cs"
  - "MSFSBlindAssist/SimConnect/SimConnectManager.InputEventReads.cs"
  - "MSFSBlindAssist/Resources/coherent-a330-efb-agent.js"
  - "MSFSBlindAssist/Resources/a330_control_map.json"
  - "tests/MSFSBlindAssist.Tests/**/*IniA3*.cs"
  - "tools/a330-gen/**"
  - "tools/a330-cdu-ocr/**"
---
# iniBuilds A330 (MSFS 2024) rules

Loaded when Claude reads matching code. Background: docs/ini-a330.md. Full text of each rule: docs/invariants/inibuilds.md.

- [INI-1] A330 controls are FS2024 input events (`B:AIRLINER_*`): selectors take the POSITION by value, momentary keys work only through the calculator `_Push`/`_Release`. Push-button 0 = ON, 3-position 0 = top, 2-position 1 = top: always translate through `A330ControlMap.InputFor`, never send the read value. Full: docs/invariants/inibuilds.md#ini-1
- [INI-2] `a330_control_map.json` is GENERATED (`tools/a330-gen`): regenerate, never hand-edit. Its `SECTIONS` list is the one place the panel order lives (the definition's own panels as `OWN_PANELS`), in the A32NX/Fenix order. Full: docs/invariants/inibuilds.md#ini-2
- [INI-4] The MCDU is READ FROM PIXELS (`A330CduScreenReader`) with its `CduPopoutProfile` and `CduLayout`: dark-glass screen finder, camera-still wait, the four-step foreground chain, ink threshold 40 (grey text). `CduLayout.BaseCap` must equal the trainer's BASE_CAP. Retrain the glyphs, never patch the .bin. Full: docs/invariants/inibuilds.md#ini-4
- [INI-7] Never write `INI_MCDU1_CLR_KEY`, a HELD state that wipes everything typed while it stays 1; MCDU CLR is the `CLR` key. Full: docs/invariants/inibuilds.md#ini-7
- [INI-8] Never confuse the iniBuilds A330 with the Headwind A330-900neo (HeadwindA330Definition): different aircraft, different transport. Full: docs/invariants/inibuilds.md#ini-8
