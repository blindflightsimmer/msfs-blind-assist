# a330-cdu-ocr

Trains the glyph library the app uses to read the iniBuilds A330 (MSFS 2024) MCDU from pixels
(`MSFSBlindAssist/Resources/a330-cdu-glyphs.bin`). The method is a fixed-font template
matcher trained on the aircraft's own rendering (first built for the iniBuilds A300); the A330 specifics:

* grid (`seg.py`): y0 30.2, row pitch 23.8, column advance 15.0, ink threshold 40 (grey text
  peaks around 77 — a higher threshold drops it);
* sizes are RELATIVE (`ocr2.py`: base em 15.0, cap 18.3) so large and small font classify apart;
* no synthetic templates — the aircraft's fonts are inside its encrypted package;
* labels: boxes `#`, arrows `←→` `↑↓`, degree `°`.

Data: `data/big/truth.json` (labelled pages) and `data/big/sp/labels.tsv` (scratchpad strings) are
committed; the capture PNGs they label (~47 MB) are NOT — regenerate them with the `harness`
(`key`/`type`/`clr`/`wait`/`cap` script) against a live sim with the MCDU popped out, saving each page
under the name its truth entry uses.

    PYTHONIOENCODING=utf-8 python spacecheck.py      # truth vs segmentation, must be 0/0
    PYTHONIOENCODING=utf-8 python eval.py            # leave-one-page-out accuracy
    python export_glyphs.py ../../MSFSBlindAssist/Resources/a330-cdu-glyphs.bin

Retrain here; never patch the .bin.
