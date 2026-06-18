using System.Globalization;
using System.Text;
using StripKit.Models;

namespace StripKit.Services;

/// <inheritdoc />
public sealed class AssetGenerationService : IAssetGenerationService
{
    // Display / picker order — independent of DI registration order.
    private static readonly AiProvider[] Order = [AiProvider.Claude, AiProvider.OpenAI, AiProvider.Gemini];

    private readonly Dictionary<AiProvider, IAssetGenerationProvider> _providers;

    public AssetGenerationService(IEnumerable<IAssetGenerationProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Provider);
        AvailableProviders = Order.Where(_providers.ContainsKey).ToList();
    }

    public IReadOnlyList<AiProvider> AvailableProviders { get; }

    public string DefaultModelFor(AiProvider provider) =>
        _providers.TryGetValue(provider, out var p) ? p.DefaultModel : "";

    public IReadOnlyList<string> ModelsFor(AiProvider provider) =>
        _providers.TryGetValue(provider, out var p) ? p.SuggestedModels : Array.Empty<string>();

    public async Task<GenerationResult> GenerateAsync(GenerationRequest request, AiProvider provider, string apiKey, string model, CancellationToken ct)
    {
        if (!_providers.TryGetValue(provider, out var impl))
            return GenerationResult.Fail($"{provider} is not available.");
        if (string.IsNullOrWhiteSpace(apiKey))
            return GenerationResult.Fail($"Enter your {provider} API key first.");

        var useModel = string.IsNullOrWhiteSpace(model) ? impl.DefaultModel : model.Trim();

        string raw;
        try
        {
            raw = await impl.CompleteAsync(BuildSystemPrompt(request), BuildUserPrompt(request), apiKey, useModel, ct);
        }
        catch (GenerationException ge)
        {
            return GenerationResult.Fail(ge.Message);
        }
        catch (OperationCanceledException)
        {
            throw;   // user cancelled — let the VM treat it as a non-error
        }
        catch (Exception ex)
        {
            return GenerationResult.Fail($"Unexpected error talking to {provider}: {ex.Message}");
        }

        if (!SvgSanitizer.TryClean(raw, out var svg, out var error))
            return GenerationResult.Fail(
                $"{error} The model may have added commentary or returned non-SVG — try Regenerate or a stronger model.",
                raw);

        return GenerationResult.Ok(svg, raw);
    }

    // ---- prompt building (encodes StripKit's filmstrip conventions) ----

    private static string BuildSystemPrompt(GenerationRequest r)
    {
        // Meters fill along their length and are revealed by clipping, so they want a portrait canvas
        // the art spans edge-to-edge — a different shape from the square, margined knob/button canvas.
        if (r.ComponentType == ComponentType.Meter)
            return BuildMeterSystemPrompt(r);

        int size = Math.Clamp(r.CanvasSize, 64, 2048);
        var half = (size / 2.0).ToString("0.#", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.AppendLine("You are an expert SVG illustrator producing production-ready vector art for audio-plugin GUI controls.");
        sb.AppendLine("Output ONLY one self-contained SVG document — no markdown, no code fences, no commentary before or after it.");
        sb.AppendLine();
        sb.AppendLine("Hard requirements:");
        sb.AppendLine($"- Root element: <svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {size} {size}\" width=\"{size}\" height=\"{size}\">.");
        sb.AppendLine("- Fully transparent background — do NOT draw a full-canvas opaque rectangle.");
        sb.AppendLine("- Keep ALL artwork within the central 80% of the canvas (~10% transparent margin on every side) so nothing clips when the control is rotated.");
        sb.AppendLine($"- Centre the control exactly at ({half}, {half}).");
        sb.AppendLine("- Pure vector only: path, circle, ellipse, rect, line, polygon, polyline, linearGradient, radialGradient, filter.");
        sb.AppendLine("- Do NOT use <image>, <script>, <foreignObject>, external file/URL references, href to anything but a local #id, or event handlers.");

        if (r.Layered && r.ComponentType == ComponentType.RotaryKnob)
        {
            sb.AppendLine("- Structure the drawing as EXACTLY two top-level groups, in this order:");
            sb.AppendLine("    <g id=\"body\"> the entire STATIC knob — outer ring, face, indented well, tick marks, highlights and shadows </g>");
            sb.AppendLine("    <g id=\"pointer\"> ONLY the moving indicator (a line, notch, triangle or dot) drawn pointing straight UP toward 12 o'clock from the centre </g>");
            sb.AppendLine("  The body never moves. The pointer is rotated programmatically about the centre to show the value, so draw it AT REST (12 o'clock) and bake NO rotation into it.");
        }
        else if (r.ComponentType is ComponentType.Button or ComponentType.Toggle)
        {
            string noun = r.ComponentType == ComponentType.Toggle ? "switch" : "button";
            sb.AppendLine("- Structure the drawing as EXACTLY two top-level groups, in this order:");
            sb.AppendLine($"    <g id=\"off\"> the {noun} in its OFF / inactive state — dim, unlit, or depressed </g>");
            sb.AppendLine($"    <g id=\"on\"> the {noun} in its ON / active state — glowing, lit, or raised </g>");
            sb.AppendLine($"  Draw the complete {noun} artwork in both groups; only one group is shown at a time depending on the parameter value.");
        }
        else
        {
            sb.AppendLine("- Put the whole drawing in a single <g id=\"body\"> group.");
        }

        return sb.ToString();
    }

    /// <summary>The system prompt for a meter: a tall portrait canvas the meter art spans top-to-bottom,
    /// drawn as an unlit <c>off</c> group + a fully-lit <c>on</c> group of identical geometry. The
    /// renderer reveals the <c>on</c> group up to the value, so the art must fill the full height with no
    /// vertical margin (a gap would misread the level).</summary>
    private static string BuildMeterSystemPrompt(GenerationRequest r)
    {
        int size = Math.Clamp(r.CanvasSize, 64, 2048);
        bool horiz = r.MeterHorizontal;
        int thin = Math.Max(64, (int)Math.Round(size / 3.0));
        int w = horiz ? size : thin;     // horizontal = wide landscape; vertical = tall portrait
        int h = horiz ? thin : size;

        // The lit art is revealed by clipping along the fill axis, so the meter must fill that axis
        // edge-to-edge with no margin (a gap would misread the value).
        string axis = horiz ? "width" : "height";
        string fromEdge = horiz ? $"the very left edge (x=0) to the very right edge (x={w})"
                                : $"the very top edge (y=0) to the very bottom edge (y={h})";
        string crossMargin = horiz ? "vertical" : "horizontal";
        string layout = horiz ? "horizontally: low values at the LEFT, high values at the RIGHT"
                              : "vertically: low values at the BOTTOM, high values at the TOP";
        string revealedFrom = horiz ? "the left" : "the bottom";

        var sb = new StringBuilder();
        sb.AppendLine("You are an expert SVG illustrator producing production-ready vector art for audio-plugin GUI controls.");
        sb.AppendLine("Output ONLY one self-contained SVG document — no markdown, no code fences, no commentary before or after it.");
        sb.AppendLine();
        sb.AppendLine("Hard requirements:");
        sb.AppendLine($"- Root element: <svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {w} {h}\" width=\"{w}\" height=\"{h}\">.");
        sb.AppendLine("- Fully transparent background — do NOT draw a full-canvas opaque rectangle.");
        sb.AppendLine($"- The meter MUST fill the full {axis}, from {fromEdge}, with NO margin along that axis — the level is shown by clipping the lit art along the {axis}, so any gap at either end misreads the value. A small {crossMargin} margin is fine.");
        sb.AppendLine($"- Lay it out {layout} — e.g. a row/stack of LED segments, or a continuous bar.");
        sb.AppendLine("- Pure vector only: path, circle, ellipse, rect, line, polygon, polyline, linearGradient, radialGradient, filter.");
        sb.AppendLine("- Do NOT use <image>, <script>, <foreignObject>, external file/URL references, href to anything but a local #id, or event handlers.");
        sb.AppendLine("- Structure the drawing as EXACTLY two top-level groups, in this order:");
        sb.AppendLine($"    <g id=\"off\"> the ENTIRE meter in its UNLIT / resting state — dim or empty segments, dark track; full {axis} </g>");
        sb.AppendLine($"    <g id=\"on\"> the SAME meter fully LIT — bright / glowing segments; identical geometry and position; full {axis} </g>");
        sb.AppendLine($"  Both groups span the full {axis} and occupy exactly the same place. The lit group is revealed from {revealedFrom} to show the level, so the two MUST line up pixel-for-pixel.");

        return sb.ToString();
    }

    private static string BuildUserPrompt(GenerationRequest r)
    {
        int size = Math.Clamp(r.CanvasSize, 64, 2048);
        var accent = string.IsNullOrWhiteSpace(r.AccentColor) ? "#E8440A" : r.AccentColor.Trim();

        var sb = new StringBuilder();
        sb.AppendLine($"Draw a {StyleWord(r.Style)} {ControlNoun(r.ComponentType)} for an audio plugin.");
        sb.AppendLine($"- Accent / highlight colour: {accent}.");
        if (!string.IsNullOrWhiteSpace(r.BodyColor))
            sb.AppendLine($"- Body / face colour: {r.BodyColor.Trim()}.");
        if (r.ComponentType == ComponentType.Meter)
            sb.AppendLine(r.MeterHorizontal
                ? "- A wide horizontal meter that fills the full width: low at the left, high at the right, segments or a bar spanning edge to edge."
                : "- A tall vertical meter that fills the full height: low at the bottom, high at the top, segments or a bar spanning edge to edge.");
        else
            sb.AppendLine($"- Canvas: {size}x{size}px, control centred with about a 10% transparent margin.");

        var effects = new List<string>();
        if (r.HasDropShadow)     effects.Add("drop shadow");
        if (r.HasOuterGlow)      effects.Add("outer glow");
        if (r.HasBevel)          effects.Add("bevel with 3D depth");
        if (r.HasMetallicSheen)  effects.Add("metallic sheen on the face");
        if (effects.Count > 0)
            sb.AppendLine($"- Add these visual effects: {string.Join(", ", effects)}.");

        if (!string.IsNullOrWhiteSpace(r.StyleNotes))
            sb.AppendLine($"- Extra direction: {r.StyleNotes.Trim()}.");

        if (r.Layered && r.ComponentType == ComponentType.RotaryKnob)
            sb.AppendLine("Return the SVG with a static <g id=\"body\"> and a separate <g id=\"pointer\"> pointing straight up.");
        else if (r.ComponentType is ComponentType.Button or ComponentType.Toggle)
            sb.AppendLine("Return the SVG with an <g id=\"off\"> group for the inactive state and a <g id=\"on\"> group for the active/lit state.");
        else if (r.ComponentType == ComponentType.Meter)
            sb.AppendLine(r.MeterHorizontal
                ? "Return the SVG with an <g id=\"off\"> group (the unlit meter) and an <g id=\"on\"> group (the same meter fully lit), both spanning the full width."
                : "Return the SVG with an <g id=\"off\"> group (the unlit meter) and an <g id=\"on\"> group (the same meter fully lit), both spanning the full height.");
        else
            sb.AppendLine("Return the SVG with the drawing inside a single <g id=\"body\"> group.");

        return sb.ToString();
    }

    private static string StyleWord(GenerationStyle style) => style switch
    {
        GenerationStyle.Modern => "clean, modern (Serum/Vital-style)",
        GenerationStyle.Minimal => "minimal, flat-outline",
        GenerationStyle.Skeuomorphic => "skeuomorphic, three-dimensional moulded",
        GenerationStyle.Vintage => "vintage hardware, knurled brushed-metal",
        GenerationStyle.Flat => "flat material-design",
        _ => "clean, modern",
    };

    private static string ControlNoun(ComponentType type) => type switch
    {
        ComponentType.RotaryKnob => "rotary knob",
        ComponentType.VerticalFader => "vertical fader thumb (the cap that slides up and down — just the cap, not the track)",
        ComponentType.HorizontalSlider => "horizontal slider thumb (the handle that slides left to right — just the handle, not the track)",
        ComponentType.Meter => "vertical level meter (LED-segment or continuous-bar style)",
        ComponentType.Button => "push button",
        ComponentType.Toggle => "on/off toggle switch (a rocker or slide switch)",
        _ => "rotary knob",
    };
}
