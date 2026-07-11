using Avalonia.Controls;
using Avalonia.Interactivity;
using StripKit.Helpers;
using StripKit.ViewModels;

namespace StripKit.Views;

/// <summary>The "Generate" tab: AI-generated layered SVGs. Code-behind handles the clipboard copy
/// (a top-level concern) and the colour-picker flyouts (pure view concerns — Avalonia types stay
/// out of the view model; the flyout itself lives in <see cref="ColorFlyoutHelper"/>, shared with
/// the Create and Batch tabs).</summary>
public partial class GenerateView : UserControl
{
    public GenerateView() => InitializeComponent();

    private GenerateViewModel? Vm => DataContext as GenerateViewModel;

    private async void OnCopySvg(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || string.IsNullOrEmpty(Vm.GeneratedSvg)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(Vm.GeneratedSvg);
        Vm.StatusMessage = "Copied the generated SVG to the clipboard.";
    }

    private void OnBodyColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || Vm is null) return;
        ColorFlyoutHelper.Show(btn, Vm.BodyColorHex, alphaAware: false, hex => Vm.BodyColorHex = hex);
    }

    private void OnAccentColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || Vm is null) return;
        ColorFlyoutHelper.Show(btn, Vm.AccentColorHex, alphaAware: false, hex => Vm.AccentColorHex = hex);
    }
}
