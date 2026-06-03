using Avalonia.Controls;

namespace StripKit.Views;

/// <summary>
/// The Batch tab. All logic is in <c>BatchViewModel</c>; this is markup-only
/// (no drag-drop — batch works on chosen folders).
/// </summary>
public partial class BatchView : UserControl
{
    public BatchView() => InitializeComponent();
}
