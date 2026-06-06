using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace StripKit.Services;

/// <inheritdoc />
/// <remarks>
/// <see cref="Owner"/> is assigned once, after the main window is created, in
/// <c>App.OnFrameworkInitializationCompleted</c>. Picker calls only happen at
/// runtime (button clicks), by which point the owner is set.
/// </remarks>
public sealed class FileDialogService : IFileDialogService
{
    public Window? Owner { get; set; }

    public async Task<string?> OpenImageAsync()
    {
        if (Owner?.StorageProvider is not { } storage)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open source image (transparent PNG)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PNG image") { Patterns = ["*.png"] },
                new FilePickerFileType("All images") { Patterns = ["*.png", "*.webp", "*.bmp", "*.jpg", "*.jpeg"] },
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> OpenLayeredFileAsync()
    {
        if (Owner?.StorageProvider is not { } storage)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a layered source (SVG / PSD)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Layered source") { Patterns = ["*.svg", "*.psd", "*.psb"] },
                new FilePickerFileType("SVG (vector)") { Patterns = ["*.svg"] },
                new FilePickerFileType("Photoshop (PSD/PSB)") { Patterns = ["*.psd", "*.psb"] },
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SavePngAsync(string suggestedName)
    {
        if (Owner?.StorageProvider is not { } storage)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export filmstrip PNG",
            SuggestedFileName = suggestedName,
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> OpenFolderAsync(string title)
    {
        if (Owner?.StorageProvider is not { } storage)
            return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
