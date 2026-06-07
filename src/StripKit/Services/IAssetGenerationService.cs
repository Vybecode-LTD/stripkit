using StripKit.Models;

namespace StripKit.Services;

/// <summary>
/// Orchestrates AI SVG generation for the Generate tab: builds the StripKit-aware prompt, dispatches
/// to the chosen <see cref="IAssetGenerationProvider"/>, then extracts and sanitizes the SVG from the
/// reply. The view model talks only to this. App-only (no Avalonia); networked + non-deterministic,
/// unlike the rest of StripKit's services.
/// </summary>
public interface IAssetGenerationService
{
    /// <summary>Providers wired in, in display order (used to populate the picker).</summary>
    IReadOnlyList<AiProvider> AvailableProviders { get; }

    /// <summary>The default model id for a provider (shown pre-filled, and used when the user
    /// hasn't pinned one).</summary>
    string DefaultModelFor(AiProvider provider);

    /// <summary>The suggested model ids for a provider (shown in the model dropdown).</summary>
    IReadOnlyList<string> ModelsFor(AiProvider provider);

    /// <summary>Generates one control SVG. Never throws for expected failures — returns a
    /// <see cref="GenerationResult"/> carrying either the sanitized SVG or a user-facing error.
    /// Cancellation (via <paramref name="ct"/>) propagates as <see cref="OperationCanceledException"/>.</summary>
    Task<GenerationResult> GenerateAsync(GenerationRequest request, AiProvider provider, string apiKey, string model, CancellationToken ct);
}
