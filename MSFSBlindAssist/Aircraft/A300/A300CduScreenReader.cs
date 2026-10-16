namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>Size class of a CDU character: L large, M medium, C small capitals / labels, S small.</summary>
internal enum A300CduSize { Large, Medium, SmallCaps, Small }

internal sealed record A300CduChar(string Text, A300CduSize Size, float Distance, float Margin, string Colour = "")
{
    /// <summary>True when the match is weak enough that the character should be treated as unread.</summary>
    public bool Uncertain => Distance > A300CduScreenReader.UncertainDistance;
}

internal sealed record A300CduLine(int Row, string Text, IReadOnlyList<A300CduChar> Chars)
{
    public static readonly A300CduLine Empty = new(-1, "", Array.Empty<A300CduChar>());
}

/// <summary>
/// Reads the text of the popped-out iniBuilds A300 CDU window from its pixels.
///
/// The screen is 14 rows on a fixed vertical grid. Horizontally text is NOT always on the
/// 24-column grid (centred titles sit half a column off; "--- END OF ALTN F-PLN ---" is 25
/// characters), so glyphs are found by ink runs, not cells. The CDU draws at least five text
/// sizes (large 47 px, medium 39, small capitals 36 at the large pitch, small 31, subscripts â€”
/// all at the 3x capture size), so every glyph is scaled to one letter height before matching and
/// its size is reported separately; on the flight plan, size is what separates a constraint from
/// a prediction.
///
/// Spacing rules, each found on real pages:
///  * spaces come from the distance between cell centres in units of the character advance;
///  * narrow glyphs sit off-centre in their cell ('.' by -0.26 advance), so centres are corrected
///    per character (offsets learned from the scratchpad, where every cell is known);
///  * glyphs nearly touching are never split by a space (<c>*MIN</c>);
///  * a change of letter height between two letters/digits is a word break even with no gap
///    (<c>SIDfromEGNT</c> â†’ "SID FROM EGNT", <c>VAPP</c> â†’ "V APP") â€” better for listening.
///
/// Every constant below was measured; the whole pipeline is pinned by A300CduScreenReaderTests
/// against labelled captures (100% of characters and word breaks on pages never trained on).
/// Pure: takes a luminance array, does no I/O.
/// </summary>
internal sealed class A300CduScreenReader
{
    public const int Rows = 14;
    private const double FeatureScale = 1.5;
    private readonly CduLayout _layout;
    private double BaseY0 => _layout.BaseY0;
    private double BasePitchY => _layout.BasePitchY;
    private double BasePitchX => _layout.BasePitchX;
    private double BaseCap => _layout.BaseCap;
    private float Threshold => _layout.Threshold;
    /// <summary>The window title bar above the gauge, in capture pixels (not scaled with the window).</summary>
    public int TitleBarHeight => _layout.TitleBarHeight;
    public const float UncertainDistance = 0.45f;
    /// <summary>Glyph-library label of the overlapped "&gt;&lt;" pair (see <see cref="Read"/>).</summary>
    public const string OverlappedMarkers = "×";
    private const float SyntheticCandidateHeightTolerance = 3.5f;
    private const int Prefilter = 64;

    private readonly A300CduGlyphLibrary _lib;
    private readonly Dictionary<ulong, (string ch, float d, float m)> _cache = new();

    public A300CduScreenReader(A300CduGlyphLibrary library, CduLayout layout)
    {
        _lib = library;
        _layout = layout;
    }

    private sealed class Glyph
    {
        public int X0, X1, Y0, Y1;
        public float[] Crop = Array.Empty<float>();
        public int CropW, CropH;
        public double Em, Cap;
        public double? WBot;
        public bool Full;
        public string Colour = "";
        public string Ch = "";
        public float Dist, Margin;
        public int H => Y1 - Y0 + 1;
        public double Cx => (X0 + X1) / 2.0;
    }

    /// <param name="lum">Max(R,G,B) per pixel of the whole captured window, row-major.</param>
    /// <param name="rgb">Optional R,G,B bytes per pixel (same layout); when given, every character
    /// carries the colour it is drawn in (<see cref="CduColour"/>).</param>
    public IReadOnlyList<A300CduLine> Read(float[] lum, int width, int height, byte[]? rgb = null)
    {
        double scale = width / _layout.BaseWidth;
        double y0 = BaseY0 * scale, pitchY = BasePitchY * scale, pitchX = BasePitchX * scale;
        int top = TitleBarHeight, h = height - top;
        var rows = Segment(lum, width, top, h, y0, pitchY, pitchX);
        if (rgb != null)
            for (int r = 0; r < Rows; r++)
                foreach (var g in rows[r]) g.Colour = CduColour.Of(rgb, lum, width, top, g.X0, g.X1, g.Y0, g.Y1, Threshold);
        var lines = new List<A300CduLine>(Rows);
        var work = new List<(Glyph g, int r)>();
        for (int r = 0; r < Rows; r++)
        {
            Annotate(rows[r], pitchX, _layout.TinyMarksJoinRuns);
            foreach (var g in rows[r]) work.Add((g, r));
        }
        Parallel.ForEach(work, w => Classify(w.g, w.r, scale, y0, pitchY));
        for (int r = 0; r < Rows; r++)
        {
            var gs = rows[r];
            var chars = new List<A300CduChar>(gs.Count);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < gs.Count; i++)
            {
                var g = gs[i];
                if (i > 0) sb.Append(' ', SpacesBetween(gs[i - 1], g, pitchX));
                // "×" is the trained label for ONE glyph the CDU draws as two: on the STAR page the
                // left column's "<SEL>" and the right column's "<SEL>" share a cell, so the ">" and
                // the "<" are painted on top of each other (it read "<SEL>SEL>").
                sb.Append(g.Ch == OverlappedMarkers ? "><" : g.Ch);
                chars.Add(new A300CduChar(g.Ch, SizeOf(g.Cap / scale), g.Dist, g.Margin, g.Colour));
            }
            lines.Add(new A300CduLine(r, sb.ToString(), chars));
        }
        return lines;
    }

    // ---------------- segmentation ----------------

    private static List<(int a, int b)> Runs(bool[] v)
    {
        var o = new List<(int, int)>();
        int s = -1;
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i] && s < 0) s = i;
            else if (!v[i] && s >= 0) { o.Add((s, i - 1)); s = -1; }
        }
        if (s >= 0) o.Add((s, v.Length - 1));
        return o;
    }

    private List<Glyph>[] Segment(float[] lum, int width, int top, int h, double y0, double pitchY, double pitchX)
    {
        var rows = new List<Glyph>[Rows];
        for (int i = 0; i < Rows; i++) rows[i] = new List<Glyph>();
        var rowAny = new bool[h];
        for (int y = 0; y < h; y++)
        {
            int o = (y + top) * width;
            for (int x = 0; x < width; x++) if (lum[o + x] > Threshold) { rowAny[y] = true; break; }
        }
        foreach (var (a, b) in Runs(rowAny))
        {
            if (b - a + 1 < 2) continue;
            int r = (int)Math.Round(((a + b) / 2.0 - y0) / pitchY);
            if (r < 0 || r >= Rows) continue;
            var colAny = new bool[width];
            for (int y = a; y <= b; y++)
            {
                int o = (y + top) * width;
                for (int x = 0; x < width; x++) if (lum[o + x] > Threshold) colAny[x] = true;
            }
            foreach (var (sa, sbx) in Runs(colAny))
            {
                int w = sbx - sa + 1;
                int n = w > pitchX * 1.35 ? Math.Max(1, (int)Math.Round(w / pitchX)) : 1;
                for (int k = 0; k < n; k++)
                {
                    int xa = sa + (int)Math.Round(k * (double)w / n);
                    int xb = sa + (int)Math.Round((k + 1) * (double)w / n) - 1;
                    int ga = -1, gb = -1;
                    for (int y = a; y <= b; y++)
                    {
                        int o = (y + top) * width;
                        for (int x = xa; x <= xb; x++)
                            if (lum[o + x] > Threshold) { if (ga < 0) ga = y; gb = y; break; }
                    }
                    if (ga < 0) continue;
                    int cw = xb - xa + 1, ch = gb - ga + 1;
                    var crop = new float[cw * ch];
                    for (int y = 0; y < ch; y++)
                        Array.Copy(lum, (ga + y + top) * width + xa, crop, y * cw, cw);
                    rows[r].Add(new Glyph { X0 = xa, X1 = xb, Y0 = ga, Y1 = gb, Crop = crop, CropW = cw, CropH = ch });
                }
            }
        }
        for (int i = 0; i < Rows; i++)
        {
            var sorted = rows[i].Select((g, idx) => (g, idx)).OrderBy(t => t.g.X0).ThenBy(t => t.idx).Select(t => t.g).ToList();
            rows[i] = sorted;
        }
        return rows;
    }

    // ---------------- size / words ----------------

    private static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(v => v).ToArray();
        if (a.Length == 0) return 0;
        int m = a.Length / 2;
        return a.Length % 2 == 1 ? a[m] : (a[m - 1] + a[m]) / 2.0;
    }

    private static void Annotate(List<Glyph> gs, double pitchX, bool tinyMarksJoinRuns = false)
    {
        int n = gs.Count;
        if (n == 0) return;
        var cx = gs.Select(g => g.Cx).ToArray();
        var hs = gs.Select(g => (double)g.H).ToArray();
        var em0 = new double?[n];
        for (int i = 0; i < n; i++)
        {
            double? best = null;
            foreach (int j in new[] { i - 1, i + 1 })
            {
                if (j < 0 || j >= n) continue;
                double adv = Math.Abs(cx[j] - cx[i]);
                bool full = hs[i] >= 0.7 * pitchX;
                if (adv < 1.6 * pitchX && (!full || adv <= 1.4 * hs[i]))
                    best = best is null ? adv : Math.Min(best.Value, adv);
            }
            em0[i] = best;
        }
        var known = em0.Where(e => e.HasValue).Select(e => e!.Value).ToList();
        double rowEm = known.Count > 0 ? Median(known) : pitchX;
        var em = new double[n];
        for (int i = 0; i < n; i++)
            em[i] = em0[i] ?? (hs[i] >= 0.7 * pitchX ? 0.95 * hs[i] : rowEm);

        var words = new List<List<int>>();
        var cur = new List<int> { 0 };
        for (int i = 1; i < n; i++)
        {
            if (cx[i] - cx[i - 1] < 1.5 * Math.Min(em[i], em[i - 1])) cur.Add(i);
            else { words.Add(cur); cur = new List<int> { i }; }
        }
        words.Add(cur);

        foreach (var w in words)
        {
            double wh = w.Max(i => hs[i]);
            double wem = Median(w.Select(i => em[i]));
            bool hasFull = wh >= 0.6 * wem;
            double cap;
            double? wbot;
            if (hasFull)
            {
                var refs = w.Where(j => hs[j] >= 0.75 * wh).ToList();
                cap = Median(refs.Select(j => hs[j]));
                wbot = Median(refs.Select(j => (double)gs[j].Y1));
            }
            else
            {
                cap = 1.13 * wem;
                wbot = null;
            }
            foreach (int i in w)
            {
                gs[i].Em = em[i]; gs[i].Cap = cap; gs[i].WBot = wbot;
                gs[i].Full = hasFull && hs[i] >= 0.75 * wh;
            }
            if (!hasFull) continue;
            // a run of >=2 adjacent equal-height letters of another size inside the word (V APP)
            // with TinyMarksJoinRuns a decimal point never splits a run (the A321's medium "12.1" beside large
            // "/FF+FQ" — tools/a321-cdu-ocr annotates the same way, so the library and the reader agree)
            var tw = tinyMarksJoinRuns ? w.Where(i => hs[i] >= 0.4 * wem).ToList() : w;
            int k = 0;
            while (k < tw.Count)
            {
                double h0 = hs[tw[k]];
                int j = k;
                while (j + 1 < tw.Count && Math.Abs(hs[tw[j + 1]] - h0) <= 0.08 * h0) j++;
                var run = tw.GetRange(k, j - k + 1);
                if (run.Count >= 2 && h0 >= 0.55 * wem && Math.Abs(h0 - cap) > 0.12 * cap)
                {
                    double rc = Median(run.Select(i => hs[i]));
                    double rb = Median(run.Select(i => (double)gs[i].Y1));
                    foreach (int i in run) { gs[i].Cap = rc; gs[i].WBot = rb; gs[i].Full = true; }
                }
                k = j + 1;
            }
        }
    }

    // Size classes, stated as fractions of the large font's letter height (the A300's were
    // measured at 14.5 / 12.5 / 11.3 native px against its 15.67).
    private A300CduSize SizeOf(double nativeCap) =>
        nativeCap >= 0.925 * BaseCap ? A300CduSize.Large : nativeCap >= 0.798 * BaseCap ? A300CduSize.Medium
        : nativeCap >= 0.721 * BaseCap ? A300CduSize.SmallCaps : A300CduSize.Small;

    // ---------------- features / matching ----------------

    private (float[] canvas, float[] geo, ulong key) Feature(Glyph g, int row, double scale, double y0, double pitchY)
    {
        int H = _lib.CanvasHeight, W = _lib.CanvasWidth;
        float mx = 0;
        foreach (var v in g.Crop) if (v > mx) mx = v;
        // the reference quantises to bytes before resizing; do the same so templates and queries agree
        var bytes = new byte[g.Crop.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            float v = mx > 0 ? g.Crop[i] / mx : g.Crop[i];
            bytes[i] = (byte)Math.Clamp((int)(v * 255f), 0, 255);
        }
        double cap = g.Cap > 0 ? g.Cap : BaseCap * scale;
        double k = FeatureScale * BaseCap / cap;
        int ch = g.CropH, cw = g.CropW;
        float[] img;
        if (Math.Abs(k - 1) > 1e-3)
        {
            int nh = Math.Max(1, (int)Math.Round(ch * k)), nw = Math.Max(1, (int)Math.Round(cw * k));
            img = BoxResize(bytes, cw, ch, nw, nh);
            cw = nw; ch = nh;
        }
        else
        {
            img = new float[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) img[i] = bytes[i] / 255f;
        }
        var canvas = new float[H * W];
        int hh = Math.Min(ch, H - 2), ww = Math.Min(cw, W - 2);
        for (int y = 0; y < hh; y++)
            for (int x = 0; x < ww; x++)
                canvas[(1 + y) * W + 1 + x] = img[y * cw + x];
        double u = BaseCap / cap;
        double refY = g.WBot ?? (y0 + row * pitchY + 0.5 * cap);
        var geo = new[]
        {
            (float)((g.X1 - g.X0 + 1) * u), (float)((g.Y1 - g.Y0 + 1) * u),
            (float)((g.Y0 - refY) * u), (float)((g.Y1 - refY) * u),
        };
        // cache key: the exact pixels plus the normalisation that was applied to them
        ulong key = 1469598103934665603UL;
        void Mix(ulong v) { key ^= v; key *= 1099511628211UL; }
        Mix((ulong)g.CropW); Mix((ulong)g.CropH);
        foreach (var b in bytes) Mix(b);
        foreach (var gv in geo) Mix((ulong)BitConverter.SingleToInt32Bits((float)Math.Round(gv, 2)));
        Mix((ulong)BitConverter.DoubleToInt64Bits(Math.Round(cap, 1)));
        return (canvas, geo, key);
    }

    /// <summary>Area-average resize (PIL's BOX filter): each output pixel averages the input area it covers.</summary>
    internal static float[] BoxResize(byte[] src, int sw, int sh, int dw, int dh)
    {
        var tmp = new float[sh * dw];
        double sx = (double)sw / dw;
        for (int x = 0; x < dw; x++)
        {
            double a = x * sx, b = (x + 1) * sx;
            int i0 = (int)Math.Floor(a), i1 = Math.Min(sw - 1, (int)Math.Ceiling(b) - 1);
            for (int y = 0; y < sh; y++)
            {
                double acc = 0, wsum = 0;
                for (int i = i0; i <= i1; i++)
                {
                    double wgt = Math.Min(b, i + 1) - Math.Max(a, i);
                    if (wgt <= 0) continue;
                    acc += src[y * sw + i] * wgt; wsum += wgt;
                }
                tmp[y * dw + x] = (float)(wsum > 0 ? acc / wsum : 0);
            }
        }
        var dst = new float[dh * dw];
        double sy = (double)sh / dh;
        for (int y = 0; y < dh; y++)
        {
            double a = y * sy, b = (y + 1) * sy;
            int j0 = (int)Math.Floor(a), j1 = Math.Min(sh - 1, (int)Math.Ceiling(b) - 1);
            for (int x = 0; x < dw; x++)
            {
                double acc = 0, wsum = 0;
                for (int j = j0; j <= j1; j++)
                {
                    double wgt = Math.Min(b, j + 1) - Math.Max(a, j);
                    if (wgt <= 0) continue;
                    acc += tmp[j * dw + x] * wgt; wsum += wgt;
                }
                dst[y * dw + x] = (float)Math.Round(wsum > 0 ? acc / wsum : 0) / 255f;
            }
        }
        return dst;
    }

    private static readonly (int dy, int dx)[] Shifts =
        { (-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 0), (0, 1), (1, -1), (1, 0), (1, 1) };

    /// <summary>
    /// Normalised L1 shape distance. Comparing the query rolled by -s against the template equals
    /// comparing the query against the template rolled by +s (the reference's definition): a roll is
    /// a permutation applied to both terms of every pixel pair. Rolling the QUERY once per shift keeps
    /// the inner loop contiguous, so it vectorises.
    /// </summary>
    private static float ShapeDistance(float[] a, float[] t)
    {
        var vn = System.Numerics.Vector<float>.Zero;
        var vd = System.Numerics.Vector<float>.Zero;
        int k = 0, n = a.Length, step = System.Numerics.Vector<float>.Count;
        for (; k <= n - step; k += step)
        {
            var va = new System.Numerics.Vector<float>(a, k);
            var vb = new System.Numerics.Vector<float>(t, k);
            vn += System.Numerics.Vector.Abs(va - vb);
            vd += System.Numerics.Vector.Max(va, vb);
        }
        float num = System.Numerics.Vector.Sum(vn), den = System.Numerics.Vector.Sum(vd);
        for (; k < n; k++) { num += Math.Abs(a[k] - t[k]); den += Math.Max(a[k], t[k]); }
        return num / Math.Max(den, 1e-6f);
    }

    private float[] Roll(float[] a, int dy, int dx)
    {
        int H = _lib.CanvasHeight, W = _lib.CanvasWidth;
        var r = new float[a.Length];
        for (int y = 0; y < H; y++)
        {
            int sy = ((y + dy) % H + H) % H;
            for (int x = 0; x < W; x++)
                r[y * W + x] = a[sy * W + ((x + dx) % W + W) % W];
        }
        return r;
    }

    private float[][]? _thumbs;
    private const int ThumbFactor = 3;

    private float[] Thumb(float[] c)
    {
        int H = _lib.CanvasHeight, W = _lib.CanvasWidth, th = H / ThumbFactor, tw = W / ThumbFactor;
        var t = new float[th * tw];
        for (int y = 0; y < th * ThumbFactor; y++)
            for (int x = 0; x < tw * ThumbFactor; x++)
                t[(y / ThumbFactor) * tw + x / ThumbFactor] += c[y * W + x];
        return t;
    }

    private float[][] Thumbs => _thumbs ??= _lib.Canvas.Select(Thumb).ToArray();

    private void Classify(Glyph g, int row, double scale, double y0, double pitchY)
    {
        var (canvas, geo, key) = Feature(g, row, scale, y0, pitchY);
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var hit))
            {
                (g.Ch, g.Dist, g.Margin) = hit;
                return;
            }
        }
        int n = _lib.Labels.Length;
        var geoPen = new float[n];
        var cand = new List<int>();
        for (int i = 0; i < n; i++)
        {
            var t = _lib.Geo[i];
            float d0 = Math.Abs(t[0] - geo[0]), d1 = Math.Abs(t[1] - geo[1]);
            float d2 = Math.Abs(t[2] - geo[2]), d3 = Math.Abs(t[3] - geo[3]);
            geoPen[i] = 0.04f * (d0 + d1) + 0.02f * (d2 + d3);
            if (d1 <= SyntheticCandidateHeightTolerance) cand.Add(i);
        }
        if (cand.Count == 0) cand.AddRange(Enumerable.Range(0, n));
        // cheap thumbnail prefilter, then the exact shifted distance only among the closest
        var qt = Thumb(canvas);
        var thumbs = Thumbs;
        var pre = cand.Select(i => (i, d: ShapeDistance(qt, thumbs[i]) + geoPen[i] + _lib.Penalty[i]))
                      .OrderBy(t => t.d).Take(Prefilter).ToList();
        var rolled = Shifts.Select(s => Roll(canvas, s.dy, s.dx)).ToArray();
        var full = new List<(int i, float d)>(pre.Count);
        foreach (var (i, _) in pre)
        {
            float best = float.MaxValue;
            foreach (var q in rolled)
                best = Math.Min(best, ShapeDistance(q, _lib.Canvas[i]));
            full.Add((i, best + geoPen[i] + _lib.Penalty[i]));
        }
        full.Sort((p, q) => p.d.CompareTo(q.d));
        string ch = _lib.Labels[full[0].i];
        float d = full[0].d;
        float second = 9f;
        foreach (var t in full)
            if (_lib.Labels[t.i] != ch) { second = t.d; break; }
        (g.Ch, g.Dist, g.Margin) = (ch, d, second - d);
        lock (_cache)
        {
            if (_cache.Count > 20000) _cache.Clear();
            _cache[key] = (g.Ch, g.Dist, g.Margin);
        }
    }

    // ---------------- spacing ----------------

    private static bool AsciiAlnum(string c) => c.Length == 1 && c[0] < 128 && char.IsLetterOrDigit(c[0]);

    private double CellCentre(Glyph g, string ch) =>
        g.Cx - (_lib.Offsets.TryGetValue(ch, out var o) ? o : 0f) * g.Em;

    private int SpacesBetween(Glyph a, Glyph b, double pitchX)
    {
        double em = Math.Min(a.Em > 0 ? a.Em : pitchX, b.Em > 0 ? b.Em : pitchX);
        int n = Math.Max(0, (int)Math.Round((CellCentre(b, b.Ch) - CellCentre(a, a.Ch)) / em) - 1);
        if (n > 0 && b.X0 - a.X1 < 0.55 * em) n = 0;
        if (n == 0 && AsciiAlnum(a.Ch) && AsciiAlnum(b.Ch))
        {
            double ha = a.H, hb = b.H;
            if (Math.Abs(ha - hb) > 0.12 * Math.Max(ha, hb)) n = 1;
        }
        return n;
    }
}
