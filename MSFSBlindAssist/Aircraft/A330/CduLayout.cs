namespace MSFSBlindAssist.Aircraft.A330;

/// <summary>
/// The pixel grid of the iniBuilds A330 MCDU gauge, as the pixel reader (<see cref="A330CduScreenReader"/>)
/// needs it. Every value is measured on a capture, stated at the gauge's native width
/// (<see cref="BaseWidth"/>) and scaled to the actual capture: <see cref="BaseY0"/> and
/// <see cref="BasePitchY"/> place the 14 rows (first row centre, row pitch, below the title bar),
/// <see cref="BasePitchX"/> is one character advance of the large font, <see cref="BaseCap"/> its
/// letter height, <see cref="Threshold"/> the brightness (max of R,G,B) above which a pixel is ink.
/// </summary>
internal sealed record CduLayout(int TitleBarHeight, double BaseWidth, double BaseY0, double BasePitchY,
    double BasePitchX, double BaseCap, float Threshold)
{
    /// <summary>
    /// The A330 MCDU (FS2024, measured 2026-10-16 on the MENU and DATA INDEX pages at 1350x1138):
    /// rows 23.8 apart from 30.2, a 15.0 advance, large letters 18.3 high (the trainer's BASE_CAP — the two must match, or every glyph is normalised off its templates). The threshold is low
    /// (a green-only screen reads at 90) because the A330 draws unavailable options in GREY, which peaks at a
    /// brightness of 77 against a background of about 15 — at 90 those lines vanished whole.
    /// </summary>
    public static readonly CduLayout A330 = new(31, 450.0, 30.2, 23.8, 15.0, 18.3, 40f);
}

/// <summary>
/// The colour a CDU character is drawn in, from the mean of its ink pixels. Airbus MCDUs carry
/// meaning in colour (amber = a required entry, cyan = a field that can be changed, green = data
/// the FMS computed, magenta = a constraint, grey = not available yet), which a screen reader user
/// otherwise never learns. Pure.
/// </summary>
internal static class CduColour
{
    public const string White = "white", Grey = "grey", Amber = "amber", Green = "green",
        Cyan = "cyan", Magenta = "magenta", Yellow = "yellow", Red = "red";

    public static string Of(byte[] rgb, float[] lum, int width, int top, int x0, int x1, int y0, int y1, float threshold)
    {
        double r = 0, g = 0, b = 0, peak = 0;
        int n = 0;
        for (int y = y0; y <= y1; y++)
        {
            int row = (y + top) * width;
            for (int x = x0; x <= x1; x++)
            {
                int i = row + x;
                if (lum[i] <= threshold) continue;
                // weight by brightness so the anti-aliased edge does not wash the colour out
                double w = lum[i];
                r += rgb[i * 3] * w; g += rgb[i * 3 + 1] * w; b += rgb[i * 3 + 2] * w;
                peak = Math.Max(peak, lum[i]);
                n++;
            }
        }
        if (n == 0) return "";
        double s = r + g + b;
        if (s <= 0) return "";
        return Classify(r / s * 3, g / s * 3, b / s * 3, peak);
    }

    /// <param name="r">Red relative to the grey level (1 = neutral), likewise g and b.</param>
    /// <param name="peak">The brightest ink pixel (0-255).</param>
    internal static string Classify(double r, double g, double b, double peak)
    {
        double spread = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
        if (spread < 0.25) return peak < 130 ? Grey : White;
        if (r > g && g > b && r - b > 0.6) return g > 0.85 * r ? Yellow : Amber;
        if (r > 1.3 && b > 1.0 && g < 0.8) return Magenta;
        if (g > r && b > r && b > 0.9) return Cyan;
        if (g > r && g > b) return Green;
        if (r > g && r > b) return Red;
        return White;
    }
}

/// <summary>
/// How to create and recognise the MCDU pop-out (<see cref="A330CduPopout"/>): the cockpit camera
/// that frames it (cameras.cfg instrument views, 0-based), the pop-out window's client aspect (the
/// only property that tells it from the aircraft's other gauges), and where the screen sits in that
/// view (where the dark-glass search starts, and the click when it finds nothing).
/// </summary>
internal sealed record CduPopoutProfile(int CaptainViewIndex, int FirstOfficerViewIndex, double ClientAspect,
    double FallbackX, double FallbackY,
    int WindowWidth = A330CduPopout.TargetWidth, int WindowHeight = A330CduPopout.TargetHeight)
{
    /// <summary>
    /// A330 (FS2024, measured 2026-10-16 by sweeping all 38 instrument views): views 12 and 13 face
    /// the captain's and first officer's MCDU head-on, the screen in the upper middle (view 22 is an
    /// oblique pedestal view with both MCDUs in it). The pop-out's client is 434x361. White text on
    /// black, so the screen is found as the dark glass nearest where it sits in view 12 (the fallback
    /// click, measured in a 1294x1399 window).
    /// </summary>
    public static readonly CduPopoutProfile A330 = new(12, 13, 434.0 / 361.0, 0.547, 0.393);
}
