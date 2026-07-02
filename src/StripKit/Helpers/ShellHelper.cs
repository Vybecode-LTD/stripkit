using System.Diagnostics;

namespace StripKit.Helpers;

/// <summary>Small OS-shell conveniences (best-effort, Windows). Not UI types, so a view model may call
/// these directly for post-export actions.</summary>
public static class ShellHelper
{
    /// <summary>Open the file manager with <paramref name="path"/> selected (Explorer <c>/select</c>).
    /// Best-effort — swallows any failure (the file may have been moved, etc.).</summary>
    public static void RevealInFolder(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch { /* best-effort */ }
    }
}
