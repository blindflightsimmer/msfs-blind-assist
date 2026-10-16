---
paths:
  - "MSFSBlindAssist/Aircraft/IniA330Definition*.cs"
  - "MSFSBlindAssist/Aircraft/IniA321Definition.cs"
  - "MSFSBlindAssist/Aircraft/A330/**"
  - "MSFSBlindAssist/Aircraft/A300/**"
  - "MSFSBlindAssist/Forms/IniA300/**"
  - "MSFSBlindAssist/MainForm.IniBuilds.cs"
  - "MSFSBlindAssist/SimConnect/SimConnectManager.InputEventReads.cs"
  - "MSFSBlindAssist/Resources/coherent-a330-efb-agent.js"
  - "MSFSBlindAssist/Resources/a330_control_map.json"
  - "MSFSBlindAssist/Resources/a321_control_map.json"
  - "tests/MSFSBlindAssist.Tests/**/*IniA3*.cs"
  - "tools/a330-gen/**"
  - "tools/a321-gen/**"
  - "tools/a330-cdu-ocr/**"
  - "tools/a321-cdu-ocr/**"
---
# iniBuilds A330 / A321LR (MSFS 2024) rules

Loaded when Claude reads matching code. Background: docs/ini-a330.md and docs/ini-a321.md. Full text of each rule: docs/invariants/inibuilds.md.

- [INI-1] A330 controls are FS2024 input events (`B:AIRLINER_*`): selectors take the POSITION by value, momentary keys work only through the calculator `_Push`/`_Release`. Push-button 0 = ON, 3-position 0 = top, 2-position 1 = top: always translate through `A330ControlMap.InputFor`, never send the read value. Full: docs/invariants/inibuilds.md#ini-1
- [INI-2] `a330_control_map.json` and `a321_control_map.json` are GENERATED (`tools/a330-gen`, `tools/a321-gen`): regenerate, never hand-edit. Full: docs/invariants/inibuilds.md#ini-2
- [INI-4] The MCDUs are READ FROM PIXELS (`A300CduScreenReader`) with a per-airframe `CduPopoutProfile` and `CduLayout`: dark-glass screen finder, camera-still wait, the four-step foreground chain, ink threshold 40 (grey text). `CduLayout.BaseCap` must equal the trainer's BASE_CAP. Retrain the glyphs, never patch the .bin. Full: docs/invariants/inibuilds.md#ini-4
- [INI-5] `IniA321Definition` SUBCLASSES `IniA330Definition`; everything that differs goes through the A330 def's virtual hooks, each default exactly the A330's behaviour (pinned by a test). Never fork the A330 code for the A321. Full: docs/invariants/inibuilds.md#ini-5
- [INI-6] The A321's input events do nothing from outside: its controls are direct `INI_*` L:var writes; only the FCU knob pulls/pushes answer a value press. Judge a control by what moved DOWNSTREAM — several A321 L:vars hold a write and drive nothing. Full: docs/invariants/inibuilds.md#ini-6
- [INI-7] MCDU CLR is the `CLR` key; never write `INI_MCDU1_CLR_KEY`, a HELD state that wipes everything typed while it stays 1. `CduLayout.TinyMarksJoinRuns` is A321-only. Full: docs/invariants/inibuilds.md#ini-7
- [INI-8] Never confuse these with the LatinVFR A330 (LvfrA330Definition): different aircraft, different transport. Full: docs/invariants/inibuilds.md#ini-8
