# a330-cdu-ocr

Trains the glyph library the app uses to read the iniBuilds A330 (MSFS 2024) MCDU from pixels
(`MSFSBlindAssist/Resources/a330-cdu-glyphs.bin`):

* grid (`seg.py`): y0 30.2, row pitch 23.8, column advance 15.0, ink threshold 40 (grey text
  peaks around 77 — a higher threshold drops it);
* sizes are RELATIVE (`ocr2.py`: base em 15.0, cap 18.3 — `CduLayout.A330.BaseCap` must equal
  `BASE_CAP`, a test pins it) so large and small font classify apart;
* no synthetic templates — the aircraft's fonts are inside its encrypted package;
* labels: boxes `#`, arrows `←→` `↑↓`, degree `°`.

Data: `data/big/sp/labels.tsv` (scratchpad strings) and `data/big/truth.json` (labelled pages). The
captures they label (39 pages and 36 scratchpad strings, about 49 MB of PNG) are kept outside the
repository for size; only three are committed (`data/big/r_fpln_*.png`, the F-PLN pages that taught
`(`, `)` and the ditto mark), and `A330CduScreenReaderTests` reads them end to end with the app's
own reader. To retrain, put the captures back in `data/big/` and `data/big/sp/` under the names
`truth.json` and `labels.tsv` give them.

Captures: pop the MCDU out (Shift+M in the app does it, or Right-Alt + click the MCDU), then drive it
with the harness, which presses `INI_MCDU1_*` keys over the MobiFlight channel and saves the
`WASMINSTRUMENT` pop-out as a PNG:

    dotnet run --project harness -- script.txt      # lines: key NAME | type TEXT | clr N | wait MS | cap PATH | raw RPN

Training:

    PYTHONIOENCODING=utf-8 python spacecheck.py      # truth vs segmentation, must be 0/0
    PYTHONIOENCODING=utf-8 python eval.py            # leave-one-page-out accuracy
    python export_glyphs.py ../../MSFSBlindAssist/Resources/a330-cdu-glyphs.bin

Retrain here; never patch the .bin.
