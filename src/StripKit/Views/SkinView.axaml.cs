using Avalonia.Controls;

namespace StripKit.Views;

/// <summary>The "Skin" tab: a multi-control <c>skin.json</c> builder. Markup-only code-behind
/// (no drag-drop — controls are added via the file picker or blank).</summary>
public partial class SkinView : UserControl
{
    public SkinView() => InitializeComponent();
}
