using System.Runtime.InteropServices;
using System.Xml.Linq;
using ImageMagick;
using SkiaSharp;
using StripKit.Models;
using SvgSkia = Svg.Skia.SKSvg;

namespace StripKit.Services;

/// <inheritdoc cref="ILayeredImportService"/>
public sealed class LayeredImportService : ILayeredImportService
{
    /// <summary>Cap the rasterized canvas long edge so a poster-sized source can't blow up memory;
    /// the renderer contain-fits into the (usually small) frame cell anyway.</summary>
    private const int MaxCanvasEdge = 2048;

    // Strong indicator words → Rotate; everything else stays Static. A body that wrongly spins is a
    // worse default than a missed pointer the user re-tags, so the list is deliberately narrow.
    private static readonly string[] RotateNameHints =
        ["pointer", "needle", "indicator", "tick", "marker", "hand", "arrow", "notch", "pip", "dial"];

    // Exact-match (trimmed, case-insensitive) layer names that indicate discrete button states →
    // Frame behavior. Substring matching is intentionally avoided because "on" appears inside many
    // unrelated words (indicator, knob, mono, …).
    private static readonly HashSet<string> FrameExactHints =
        new(StringComparer.OrdinalIgnoreCase) { "off", "on" };

    public bool CanImport(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".svg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".psd", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".psb", StringComparison.OrdinalIgnoreCase);
    }

    public LayeredImportResult? Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var ext = Path.GetExtension(path);
        try
        {
            if (ext.Equals(".svg", StringComparison.OrdinalIgnoreCase))
                return ImportSvg(path);
            if (ext.Equals(".psd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".psb", StringComparison.OrdinalIgnoreCase))
                return ImportPsd(path);
        }
        catch
        {
            return null;   // unreadable / corrupt / unsupported — the caller surfaces a friendly error
        }
        return null;
    }

    /// <summary>Guesses a layer's behaviour from its name.</summary>
    internal static LayerBehavior Guess(string name)
    {
        var n = name.ToLowerInvariant();
        foreach (var hint in RotateNameHints)
            if (n.Contains(hint, StringComparison.Ordinal)) return LayerBehavior.Rotate;
        if (FrameExactHints.Contains(name.Trim())) return LayerBehavior.Frame;
        return LayerBehavior.Static;
    }

    // ---------------- SVG (Svg.Skia — vector groups, MIT) ----------------

    private static LayeredImportResult? ImportSvg(string path)
    {
        var text = File.ReadAllText(path);

        // Hardened parse FIRST — before the text ever reaches Svg.Skia. The file picker accepts
        // arbitrary user SVG with no sanitizer pass, and Svg.Skia's underlying svg-net parser
        // processes DTDs by default (DtdProcessing.Parse, no entity-character cap), so handing it the
        // raw text would expand a "billion-laughs" entity bomb before any of our checks ran. Gating
        // here means a DTD / entity payload is rejected up front — the throw is caught by Import() and
        // surfaced as "no usable layers"; svg-net never sees it. Legitimate control art carries no
        // DTD, so this never fires on the happy path (and the raw text below is then DTD-free).
        var doc = SafeXml.Parse(text);
        var root = doc.Root;
        if (root is null) return null;

        // Render the whole document once to fix the canonical canvas box + coordinate origin, so
        // each isolated layer lands in exactly the same place. Safe now that the DTD gate passed.
        using var full = new SvgSkia();
        full.FromSvg(text);
        if (full.Picture is null) return null;
        var cull = full.Picture.CullRect;
        if (cull.Width <= 0 || cull.Height <= 0) return null;

        float scale = 1f;
        float edge = Math.Max(cull.Width, cull.Height);
        if (edge > MaxCanvasEdge) scale = MaxCanvasEdge / edge;
        int canvasW = Math.Max(1, (int)Math.Round(cull.Width * scale));
        int canvasH = Math.Max(1, (int)Math.Round(cull.Height * scale));

        // Root-level <defs>/<style>/<symbol> are cloned into each per-layer document so url(#id)
        // references (gradients, filters, clip paths) still resolve once a group is isolated.
        var sharedDefs = root.Elements()
            .Where(e => e.Name.LocalName is "defs" or "style" or "symbol")
            .Select(e => new XElement(e))
            .ToList();

        // Top-level <g> elements are the layers (AI/Inkscape/Figma export layers as groups), in
        // document order = paint order = bottom-first (the order the renderer composites).
        var groups = root.Elements().Where(e => e.Name.LocalName == "g").ToList();

        var layers = new List<ImportedLayer>();

        if (groups.Count == 0)
        {
            // No groups — the whole drawing is one static layer.
            layers.Add(new ImportedLayer
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Art = Rasterize(full.Picture, cull, scale, canvasW, canvasH),
                SuggestedBehavior = LayerBehavior.Static,
            });
        }
        else
        {
            int i = 0;
            foreach (var g in groups)
            {
                i++;
                var name = LayerName(g) ?? $"Layer {i}";
                // A standalone SVG = the original root (its attributes + namespaces) + shared defs +
                // just this one group; rasterized through the shared transform so layers register.
                var standalone = new XElement(root.Name, root.Attributes(), sharedDefs, new XElement(g));
                var svgString = new XDocument(standalone).ToString(SaveOptions.DisableFormatting);

                using var layerSvg = new SvgSkia();
                layerSvg.FromSvg(svgString);
                if (layerSvg.Picture is null) continue;

                layers.Add(new ImportedLayer
                {
                    Name = name,
                    Art = Rasterize(layerSvg.Picture, cull, scale, canvasW, canvasH),
                    SuggestedBehavior = Guess(name),
                });
            }
        }

        if (layers.Count == 0) return null;
        return new LayeredImportResult
        {
            Layers = layers, CanvasWidth = canvasW, CanvasHeight = canvasH, SourceFormat = "SVG",
        };
    }

    /// <summary>Draws one SVG picture into a canvas-sized bitmap using the full document's cull box
    /// as the shared origin (so isolated layers overlay exactly).</summary>
    private static SKBitmap Rasterize(SKPicture picture, SKRect cull, float scale, int canvasW, int canvasH)
    {
        var bmp = new SKBitmap(canvasW, canvasH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        c.Translate(-cull.Left * scale, -cull.Top * scale);
        c.Scale(scale);
        c.DrawPicture(picture);
        return bmp;
    }

    /// <summary>A human label (Inkscape) → id → data-name; namespace on the attribute is ignored.</summary>
    private static string? LayerName(XElement g)
    {
        var label = g.Attributes().FirstOrDefault(a => a.Name.LocalName == "label");
        if (label is not null && !string.IsNullOrWhiteSpace(label.Value)) return label.Value;
        var id = (string?)g.Attribute("id");
        if (!string.IsNullOrWhiteSpace(id)) return id;
        var dataName = g.Attributes().FirstOrDefault(a => a.Name.LocalName == "data-name");
        if (dataName is not null && !string.IsNullOrWhiteSpace(dataName.Value)) return dataName.Value;
        return null;
    }

    // ---------------- PSD / PSB (Magick.NET — raster layers, Apache-2.0) ----------------

    private static LayeredImportResult? ImportPsd(string path)
    {
        using var coll = new MagickImageCollection(path);
        if (coll.Count == 0) return null;

        // ImageMagick reads a PSD as [merged composite, layer, layer, …]; the composite carries the
        // full canvas. Treat the first image as the composite only when it is unlabeled while real
        // (named) layers follow — robust whether or not a composite is present.
        int canvasW = (int)coll[0].Width;
        int canvasH = (int)coll[0].Height;
        if (canvasW <= 0 || canvasH <= 0) return null;

        bool firstIsComposite = coll.Count > 1
            && string.IsNullOrEmpty(coll[0].GetAttribute("label"))
            && coll.Skip(1).Any(im => !string.IsNullOrEmpty(im.GetAttribute("label")));
        var sources = firstIsComposite ? coll.Skip(1).ToList() : coll.ToList();

        var layers = new List<ImportedLayer>();
        int i = 0;
        foreach (var img in sources)
        {
            i++;
            var name = img.GetAttribute("label");
            if (string.IsNullOrWhiteSpace(name)) name = $"Layer {i}";

            int lw = (int)img.Width, lh = (int)img.Height;
            if (lw <= 0 || lh <= 0) continue;
            int offX = img.Page.X, offY = img.Page.Y;   // where the layer sits on the canvas

            img.Alpha(AlphaOption.Set);
            using var pixels = img.GetPixels();
            byte[] rgba = pixels.ToByteArray(PixelMapping.RGBA) ?? [];
            if (rgba.Length < lw * lh * 4) continue;

            using var layerOwnSize = new SKBitmap(new SKImageInfo(lw, lh, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            Marshal.Copy(rgba, 0, layerOwnSize.GetPixels(), lw * lh * 4);

            // Composite onto the full canvas at the layer's offset, so every layer registers.
            var art = new SKBitmap(canvasW, canvasH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var c = new SKCanvas(art))
            {
                c.Clear(SKColors.Transparent);
                c.DrawBitmap(layerOwnSize, offX, offY);
            }
            layers.Add(new ImportedLayer { Name = name, Art = art, SuggestedBehavior = Guess(name) });
        }

        if (layers.Count == 0) return null;
        return new LayeredImportResult
        {
            Layers = layers, CanvasWidth = canvasW, CanvasHeight = canvasH, SourceFormat = "PSD",
        };
    }
}
