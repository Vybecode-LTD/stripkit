using Avalonia.Controls;
using Avalonia.Interactivity;
using StripKit.ViewModels;

namespace StripKit.Views;

/// <summary>The "Generate" tab: AI-generated layered knob SVGs. Code-behind holds only the
/// clipboard copy (a top-level concern), like the Create tab's code-snippet copy.</summary>
public partial class GenerateView : UserControl
{
    public GenerateView() => InitializeComponent();

    private GenerateViewModel? Vm => DataContext as GenerateViewModel;

    // Copy the generated SVG to the clipboard. Clipboard access is a view (top-level) concern,
    // so it lives here rather than in the view model.
    private async void OnCopySvg(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || string.IsNullOrEmpty(Vm.GeneratedSvg)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(Vm.GeneratedSvg);
        Vm.StatusMessage = "Copied the generated SVG to the clipboard.";
    }
}
