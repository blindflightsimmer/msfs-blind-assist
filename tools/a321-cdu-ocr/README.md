# a321-cdu-ocr

Trains the glyph library the app uses to read the iniBuilds A321LR (MSFS 2024) MCDU from pixels
(`MSFSBlindAssist/Resources/a321-cdu-glyphs.bin`). Copied from `tools/a330-cdu-ocr`; the A321 changes:

* grid (`seg.py`): y0 54.8, row pitch 25.53, column advance 16.77 at the 450 base (measured in a 1350x1360
  pop-out window), ink threshold 40;
* font size (`ocr2.py`): base em 16.77, cap 20.3;
* `annotate` lets a decimal point join a same-size run (the C# reader does the same only for
  `CduLayout.TinyMarksJoinRuns`, so the A330 library is untouched).

Data: `data/big/sp/labels.tsv` (36 scratchpad strings), `data/big/truth.json` (37 labelled pages) and
`data/big/q/truth.json` (unseen pages) are committed; the capture PNGs they label (~64 MB) are NOT —
regenerate them with the `harness` (`key`/`type`/`clr`/`wait`/`cap` script; CLR is `INI_MCDU1_CLR`, never
`INI_MCDU1_CLR_KEY`) against a live sim, saving each page under the name its truth entry uses.

    PYTHONIOENCODING=utf-8 python spacecheck.py      # must report 0/0
    PYTHONIOENCODING=utf-8 python testset.py q       # unseen-page accuracy (data/big/q)
    python export_glyphs.py ../../MSFSBlindAssist/Resources/a321-cdu-glyphs.bin

Measured: 97.7 % of characters on unseen pages with half the pages; the app's tests pin three later captures exactly.
