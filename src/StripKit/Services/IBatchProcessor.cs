using StripKit.Models;

namespace StripKit.Services;

/// <summary>
/// Renders a batch of source images into filmstrips off the UI thread, reporting
/// progress and honouring cancellation. UI-agnostic (composes the renderer, image
/// load, export, and manifest services).
/// </summary>
public interface IBatchProcessor
{
    /// <summary>
    /// Processes every <see cref="BatchOptions.InputFiles"/> entry into a strip in
    /// the output directory. Reports after each item via <paramref name="progress"/>.
    /// A failed item is recorded and the run continues; cancellation stops between
    /// items and returns a result with <see cref="BatchResult.Cancelled"/> set — it
    /// does not throw.
    /// </summary>
    Task<BatchResult> ProcessAsync(BatchOptions options, IProgress<BatchProgress>? progress,
                                   CancellationToken cancellationToken = default);
}
