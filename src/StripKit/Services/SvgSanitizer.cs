using System.Xml.Linq;

namespace StripKit.Services;

/// <summary>
/// Turns a language model's raw reply into a clean, safe SVG document. Models often wrap the SVG in
/// markdown fences or prose, so we first carve out the <c>&lt;svg&gt;…&lt;/svg&gt;</c> span, then strip
/// anything active or external (scripts, event handlers, embedded raster, external references) before
/// it ever reaches the renderer. Pure (System.Xml.Linq only) — no Skia/Avalonia, so it unit-tests
/// trivially. The renderer (Svg.Skia, via <see cref="ILayeredImportService"/>) is the final validator.
/// </summary>
public static class SvgSanitizer
{
    // Whole elements that have no place in generated control art: <script> (active), <foreignObject>
    // (arbitrary embedded XML/HTML), and <image> (external/base64 raster — we want pure vector).
    private static readonly HashSet<string> StripElements =
        new(StringComparer.OrdinalIgnoreCase) { "script", "foreignObject", "image" };

    /// <summary>Carves the SVG document out of a (possibly chatty / fenced) model reply. Returns the
    /// substring from the first <c>&lt;svg</c> to the last <c>&lt;/svg&gt;</c>, or <c>null</c> if absent.</summary>
    public static string? Extract(string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) return null;

        int start = rawResponse.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;

        int closeAt = rawResponse.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
        if (closeAt < 0 || closeAt < start) return null;

        int end = closeAt + "</svg>".Length;
        return rawResponse[start..end].Trim();
    }

    /// <summary>
    /// Extracts, parses, and sanitizes the SVG from a model reply. On success <paramref name="svg"/>
    /// is a clean document string and <paramref name="error"/> is null; on failure the reverse.
    /// </summary>
    public static bool TryClean(string? rawResponse, out string svg, out string? error)
    {
        svg = "";
        error = null;

        var extracted = Extract(rawResponse);
        if (extracted is null)
        {
            error = "The model's reply did not contain an <svg> document.";
            return false;
        }

        XDocument doc;
        try
        {
            // Hardened parse: a DTD/entity payload (billion-laughs DoS, external-entity probe) is
            // rejected here rather than expanded — generated control art never carries a DTD.
            doc = SafeXml.Parse(extracted);
        }
        catch (Exception ex)
        {
            error = $"The model returned malformed SVG/XML ({ex.Message}).";
            return false;
        }

        var root = doc.Root;
        if (root is null || !root.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
        {
            error = "The model's reply was not a valid SVG (no <svg> root).";
            return false;
        }

        Sanitize(root);

        svg = root.ToString(SaveOptions.None);
        return true;
    }

    /// <summary>Removes active / external content in place: forbidden elements, event-handler
    /// attributes, and any non-local <c>href</c>/<c>xlink:href</c> reference.</summary>
    private static void Sanitize(XElement root)
    {
        // Drop forbidden elements (descendants first so removal never skips siblings).
        foreach (var el in root.DescendantsAndSelf().Where(e => StripElements.Contains(e.Name.LocalName)).ToList())
        {
            if (el != root) el.Remove();
        }

        foreach (var el in root.DescendantsAndSelf().ToList())
        {
            foreach (var attr in el.Attributes().ToList())
            {
                var local = attr.Name.LocalName;

                // on* event handlers (onclick, onload, …).
                if (local.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    attr.Remove();
                    continue;
                }

                // href / xlink:href that points anywhere but a local fragment (#id) — blocks
                // file://, http(s), javascript:, and data: URIs.
                if (local.Equals("href", StringComparison.OrdinalIgnoreCase)
                    && !attr.Value.TrimStart().StartsWith('#'))
                {
                    attr.Remove();
                }
            }
        }
    }
}
