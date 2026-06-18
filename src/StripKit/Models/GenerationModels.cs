namespace StripKit.Models;

/// <summary>
/// The AI provider used to generate SVG control art on the Generate tab. The user picks one (their
/// own API key) — each maps to a concrete <c>IAssetGenerationProvider</c>. Pure data, no deps.
/// </summary>
public enum AiProvider
{
    /// <summary>Anthropic Claude (Messages API).</summary>
    Claude,

    /// <summary>OpenAI (Chat Completions API).</summary>
    OpenAI,

    /// <summary>Google Gemini (generateContent API).</summary>
    Gemini,

    /// <summary>Any OpenAI-compatible chat-completions endpoint (OpenRouter, Azure OpenAI, a local
    /// Ollama/LM Studio server, …). The base URL is set by the user; the wire format matches OpenAI.</summary>
    Custom,
}

/// <summary>A coarse visual style preset folded into the generation prompt. Free-text style notes
/// refine it further; this just gives the model a strong starting adjective.</summary>
public enum GenerationStyle
{
    /// <summary>Clean modern synth knob (Serum/Vital-flavoured) — the default.</summary>
    Modern,

    /// <summary>Minimal flat outline, few details.</summary>
    Minimal,

    /// <summary>Skeuomorphic — moulded plastic/metal with shading and depth.</summary>
    Skeuomorphic,

    /// <summary>Vintage hardware (knurled, brushed metal, cream/black).</summary>
    Vintage,

    /// <summary>Flat material-design style, solid fills.</summary>
    Flat,
}

/// <summary>
/// One request to generate a control's SVG. Provider/model/key are passed alongside (the key is
/// never stored on the request). Kept Skia/Avalonia-free so the service and its tests stay pure.
/// </summary>
public sealed record GenerationRequest
{
    /// <summary>What to draw. v1 targets <see cref="ComponentType.RotaryKnob"/> (the layered path).</summary>
    public ComponentType ComponentType { get; init; } = ComponentType.RotaryKnob;

    /// <summary>The visual style preset.</summary>
    public GenerationStyle Style { get; init; } = GenerationStyle.Modern;

    /// <summary>Free-text refinement (e.g. "amber LED, knurled edge"). Optional.</summary>
    public string StyleNotes { get; init; } = "";

    /// <summary>Free-text "avoid" / negative direction folded into the prompt (e.g. "no text, no
    /// numbers, no drop shadow, no photorealism"). Optional.</summary>
    public string Avoid { get; init; } = "";

    /// <summary>Accent / indicator colour as <c>#RRGGBB</c> (defaults to the StripKit accent).</summary>
    public string AccentColor { get; init; } = "#E8440A";

    /// <summary>Knob body / face colour as <c>#RRGGBB</c>. Empty = let the model choose.</summary>
    public string BodyColor { get; init; } = "";

    /// <summary>Square canvas edge in px for the SVG's viewBox.</summary>
    public int CanvasSize { get; init; } = 512;

    /// <summary>When true (the v1 default for knobs) the prompt asks for a static <c>body</c> group +
    /// a separate <c>pointer</c> group so only the pointer rotates. When false, a single flat drawing.</summary>
    public bool Layered { get; init; } = true;

    // ---- style effects ----
    public bool HasDropShadow { get; init; }
    public bool HasOuterGlow { get; init; }
    public bool HasBevel { get; init; }
    public bool HasMetallicSheen { get; init; }

    /// <summary>Meter only: draw a wide landscape meter that fills left→right, instead of the default
    /// tall portrait meter that fills bottom→top. Ignored for non-meter types.</summary>
    public bool MeterHorizontal { get; init; }
}

/// <summary>
/// The outcome of a generation: a sanitized SVG on success, or a friendly error. <see cref="RawResponse"/>
/// keeps the model's full reply for the "show raw response" diagnostic when extraction fails.
/// </summary>
public sealed record GenerationResult
{
    public bool Success { get; init; }

    /// <summary>The cleaned, validated SVG document (only when <see cref="Success"/>).</summary>
    public string? Svg { get; init; }

    /// <summary>A user-facing error message (only when not <see cref="Success"/>).</summary>
    public string? Error { get; init; }

    /// <summary>The model's raw text reply, kept for diagnostics.</summary>
    public string? RawResponse { get; init; }

    public static GenerationResult Ok(string svg, string? raw = null) =>
        new() { Success = true, Svg = svg, RawResponse = raw };

    public static GenerationResult Fail(string error, string? raw = null) =>
        new() { Success = false, Error = error, RawResponse = raw };
}

/// <summary>One control in a matching-set generation: the control type that was requested and the
/// result of generating it. The set generates every chosen type with the same style inputs so the
/// family stays visually consistent.</summary>
public sealed record GenerationSetItem(ComponentType ComponentType, GenerationResult Result);

/// <summary>The outcome of describing a reference image (vision, for "match this style"): the text
/// description on success, or a friendly error.</summary>
public sealed record ReferenceDescription(bool Success, string? Text, string? Error)
{
    public static ReferenceDescription Ok(string text) => new(true, text, null);
    public static ReferenceDescription Fail(string error) => new(false, null, error);
}

/// <summary>A named, reusable bundle of the Generate tab's style inputs (a "prompt seed") — style
/// preset, colours, canvas size, effects, extra direction, and avoid-list. Saved seeds are persisted
/// in <c>AppSettings</c>; a handful ship built in. Mutable for JSON round-tripping.</summary>
public sealed class GenerationSeed
{
    public string Name { get; set; } = "";
    public GenerationStyle Style { get; set; } = GenerationStyle.Modern;
    public string StyleNotes { get; set; } = "";
    public string Avoid { get; set; } = "";
    public string AccentColor { get; set; } = "#E8440A";
    public string BodyColor { get; set; } = "";
    public int CanvasSize { get; set; } = 512;
    public bool HasDropShadow { get; set; }
    public bool HasOuterGlow { get; set; }
    public bool HasBevel { get; set; }
    public bool HasMetallicSheen { get; set; }

    /// <summary>Built-in seeds ship with the app and can't be deleted; user seeds can.</summary>
    public bool IsBuiltIn { get; set; }
}

/// <summary>The starter "library" of prompt seeds shipped with StripKit — sensible, on-brand starting
/// points the user can apply and then tweak (or save their own alongside).</summary>
public static class GenerationSeedLibrary
{
    public static IReadOnlyList<GenerationSeed> BuiltIn { get; } =
    [
        new() { Name = "Vital / Serum modern", Style = GenerationStyle.Modern, AccentColor = "#E8440A",
                HasBevel = true, StyleNotes = "clean soft-synth knob, subtle inner shadow", IsBuiltIn = true },
        new() { Name = "Vintage hardware", Style = GenerationStyle.Vintage, AccentColor = "#E0B050", BodyColor = "#2A2622",
                HasMetallicSheen = true, StyleNotes = "knurled edge, brushed aluminium, cream indicator", IsBuiltIn = true },
        new() { Name = "Minimal flat", Style = GenerationStyle.Minimal, AccentColor = "#E8440A",
                StyleNotes = "thin outline, no shadows, lots of negative space", IsBuiltIn = true },
        new() { Name = "Skeuomorphic metal", Style = GenerationStyle.Skeuomorphic, BodyColor = "#3A3A3D",
                HasBevel = true, HasMetallicSheen = true, HasDropShadow = true,
                StyleNotes = "moulded metal, machined grooves, realistic depth", IsBuiltIn = true },
        new() { Name = "Neon glow", Style = GenerationStyle.Modern, AccentColor = "#00E5FF",
                HasOuterGlow = true, StyleNotes = "dark face, glowing accent ring", Avoid = "text, numbers", IsBuiltIn = true },
    ];
}
