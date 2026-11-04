# a330-cdu-ocr

Trains the glyph library the app uses to read the iniBuilds A330 (MSFS 2024) MCDU from pixels
(`MSFSBlindAssist/Resources/a330-cdu-glyphs.bin`). Copied from `tools/a300-cdu-ocr`; the method
is the same, the A330 changes are:

* grid (`seg.py`): y0 30.2, row pitch 23.8, column advance 15.0, ink threshold 40 (grey text
  peaks around 77 — a higher threshold drops it);
* sizes are RELATIVE (`ocr2.py`: base em 15.0, cap 18.3) so large and small font classify apart;
* no synthetic templates — the aircraft's fonts are inside its encrypted package;
* labels: boxes `#`, arrows `←→` `↑↓`, degree `°`.

Data: `data/big/sp/` scratchpad strings + `labels.tsv`, `data/big/truth.json` labelled pages.
Captures come from `tools/A300CduProbe popout-a330 keep capture.png [KEY,...]`, which pops the
MCDU out with the app's own code and reads it with the app's own reader.

    PYTHONIOENCODING=utf-8 python spacecheck.py      # truth vs segmentation, must be 0/0
    PYTHONIOENCODING=utf-8 python eval.py            # leave-one-page-out accuracy
    python export_glyphs.py ../../MSFSBlindAssist/Resources/a330-cdu-glyphs.bin

Retrain here; never patch the .bin.
