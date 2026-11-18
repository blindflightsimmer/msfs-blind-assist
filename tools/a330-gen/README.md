# a330-gen

Generates `MSFSBlindAssist/Resources/a330_control_map.json`, the iniBuilds A330 (MSFS 2024)
panel map, from the differential input-event probe results.

    python generate_a330_map.py

Every control names its input event (`event`), how it is written (`mode`: `set` = SetInputEvent
with a position value, `push` = calculator `(>B:NAME_Push)`/`_Release`, `rpn` = literal code), what
reads its state (`read`: an `INI_*` L:var, `A:SIMVAR, unit`, or `B:NAME` = the input event itself),
and its positions as measured (`value` = what the read shows, `input` = what to send, `label`).

Conventions measured on the live aircraft: push-button input 0 = on; 3-position switch numbered
from the top; 2-position switch 1 = top. The generator validates every name against the probed
catalogue and fails on an unknown one. Never hand-edit the JSON — fix the generator and rerun.
