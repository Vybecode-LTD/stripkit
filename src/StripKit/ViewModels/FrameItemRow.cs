using CommunityToolkit.Mvvm.ComponentModel;

namespace StripKit.ViewModels;

/// <summary>
/// One row in the Assemble tab's frame list: a source frame's path plus its display label. The
/// 1-based <see cref="Position"/> reflects the strip's frame order (renumbered by the parent view
/// model on reorder/remove). Holds no bitmap — frames are decoded on demand to keep memory flat.
/// </summary>
public sealed partial class FrameItemRow : ObservableObject
{
    public FrameItemRow(string path) => Path = path;

    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private int _position;

    public string Display => $"{Position}.  {FileName}";
}
