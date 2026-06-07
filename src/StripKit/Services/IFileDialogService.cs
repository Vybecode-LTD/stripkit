namespace StripKit.Services;

/// <summary>
/// Abstracts the open/save file pickers so view models stay free of Avalonia UI
/// types. The implementation lives in the app layer where a top-level window is
/// available.
/// </summary>
public interface IFileDialogService
{
    /// <summary>Prompts for an image to open; returns its local path or <c>null</c> if cancelled.</summary>
    Task<string?> OpenImageAsync();

    /// <summary>Prompts for a layered source file (.svg / .psd / .psb); returns its local path or
    /// <c>null</c> if cancelled.</summary>
    Task<string?> OpenLayeredFileAsync();

    /// <summary>Prompts for a PNG save location; returns the chosen path or <c>null</c> if cancelled.</summary>
    Task<string?> SavePngAsync(string suggestedName);

    /// <summary>Prompts for an SVG save location; returns the chosen path or <c>null</c> if cancelled.</summary>
    Task<string?> SaveSvgAsync(string suggestedName);

    /// <summary>Prompts for a folder; returns its local path or <c>null</c> if cancelled.</summary>
    Task<string?> OpenFolderAsync(string title);
}
