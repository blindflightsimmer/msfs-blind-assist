# a321-gen

Generates `MSFSBlindAssist/Resources/a321_control_map.json`, the iniBuilds A321LR (MSFS 2024) panels.

    python tools/a321-gen/generate_a321_map.py

The A321 is driven by writing its `INI_*` L:vars directly (its input events do nothing from outside), so this
generator takes the A330 map (`tools/a330-gen`) and re-points every control whose L:var exists on the A321 to a
direct write of the position's value, keeping the A330's labels; drops A330-only systems (`DROP`) and anything
dangerous (`DANGER`); then adds the A321's own controls. `a321_lvars.txt` is the A321's panel-state L:var list
(from `data/panelstates/ready_for_takeoff.ini` in the streamed package) — the build fails on any name not in it.
Never hand-edit the JSON. See docs/ini-a321.md for what was measured.
