namespace StripKit.Models;

/// <summary>
/// Persisted user preferences — a small JSON in the app-data folder. Kept deliberately minimal;
/// StripKit is otherwise stateless. Read once at startup, written on change.
/// </summary>
public sealed class AppSettings
{
    /// <summary>True once the user has finished or skipped the first-run Getting Started tutorial,
    /// so it does not auto-open again (it stays re-openable from the Help button).</summary>
    public bool HasSeenTutorial { get; set; }
}
