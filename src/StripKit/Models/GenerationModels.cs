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
