using System.Reflection;
using System.Text;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The trained glyph templates the iniBuilds pixel-read MCDU reader matches against, one embedded
/// library per airframe (<c>a330-cdu-glyphs.bin</c>, <c>a321-cdu-glyphs.bin</c>).
///
/// WHY A TEMPLATE LIBRARY — the iniBuilds MCDUs publish no text an external app can read: nothing
/// on the CommBus, and the FMGS is a WASM NanoVG gauge with no DOM (see docs/ini-a330.md). The only
/// source is the pixels of the popped-out MCDU window. Generic OCR (Windows.Media.Ocr) reads those
/// badly; a fixed-font matcher trained on the aircraft's own rendering reads them exactly.
///
/// Templates are normalised glyph canvases (letter height scaled to one size, so all of the CDU's
/// font sizes share them) plus four geometry features. Real samples come from labelled captures;
/// a smaller set rendered from the aircraft's own font files (INI_CDUA/INI_CDUB) carries a
/// distance penalty so it only decides for characters never seen on screen in that size.
/// The library is produced by the training tooling and verified by A300CduScreenReaderTests.
/// </summary>
internal sealed class A300CduGlyphLibrary
{
    public int CanvasHeight { get; }
    public int CanvasWidth { get; }
    public string[] Labels { get; }
    public float[] Penalty { get; }
    /// <summary>[template][4] = width, height, top, bottom (normalised native pixels).</summary>
    public float[][] Geo { get; }
    /// <summary>[template][H*W] canvas values 0..1.</summary>
    public float[][] Canvas { get; }
    /// <summary>Character -> glyph centre offset inside its cell, in character advances.</summary>
    public IReadOnlyDictionary<string, float> Offsets { get; }

    private A300CduGlyphLibrary(int h, int w, string[] labels, float[] pen, float[][] geo, float[][] canvas,
        Dictionary<string, float> offsets)
    {
        CanvasHeight = h; CanvasWidth = w; Labels = labels; Penalty = pen; Geo = geo; Canvas = canvas; Offsets = offsets;
    }

    /// <summary>The A330 MCDU's library (tools/a330-cdu-ocr), same format.</summary>
    public const string A330ResourceName = "MSFSBlindAssist.Resources.a330-cdu-glyphs.bin";

    private static readonly Lazy<A300CduGlyphLibrary> _a330 = new(() => Required(A330ResourceName));

    public static A300CduGlyphLibrary A330 => _a330.Value;

    /// <summary>The A321LR MCDU's library (tools/a321-cdu-ocr): the same iniBuilds font drawn larger, with
    /// shapes different enough from the A330's that its glyphs misread O/D, B/R, 8/B, Z/7, 5/S here.</summary>
    public const string A321ResourceName = "MSFSBlindAssist.Resources.a321-cdu-glyphs.bin";

    private static readonly Lazy<A300CduGlyphLibrary> _a321 = new(() => Required(A321ResourceName));

    public static A300CduGlyphLibrary A321 => _a321.Value;

    private static A300CduGlyphLibrary Required(string resource)
        => FromResource(resource) ?? throw new InvalidOperationException($"Embedded resource {resource} missing");

    public static A300CduGlyphLibrary? FromResource(string resource)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        return s == null ? null : Load(s);
    }

    public static A300CduGlyphLibrary Load(Stream stream)
    {
        using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "A3CG")
            throw new InvalidDataException("Not an A300 CDU glyph library");
        int version = br.ReadUInt16();
        if (version != 1) throw new InvalidDataException($"Unsupported glyph library version {version}");
        int h = br.ReadUInt16(), w = br.ReadUInt16();
        int count = (int)br.ReadUInt32();
        var labels = new string[count];
        var pen = new float[count];
        var geo = new float[count][];
        var canvas = new float[count][];
        for (int i = 0; i < count; i++)
        {
            labels[i] = Encoding.UTF8.GetString(br.ReadBytes(br.ReadByte()));
            pen[i] = br.ReadSingle();
            geo[i] = new[] { br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle() };
            var bytes = br.ReadBytes(h * w);
            var c = new float[h * w];
            for (int k = 0; k < c.Length; k++) c[k] = bytes[k] / 255f;
            canvas[i] = c;
        }
        var offsets = new Dictionary<string, float>();
        int n = br.ReadUInt16();
        for (int i = 0; i < n; i++)
        {
            var lab = Encoding.UTF8.GetString(br.ReadBytes(br.ReadByte()));
            offsets[lab] = br.ReadSingle();
        }
        return new A300CduGlyphLibrary(h, w, labels, pen, geo, canvas, offsets);
    }
}
