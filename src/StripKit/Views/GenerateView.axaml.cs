using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using StripKit.ViewModels;

namespace StripKit.Views;

/// <summary>The "Generate" tab: AI-generated layered SVGs. Code-behind handles the clipboard copy
/// (a top-level concern) and the colour-picker flyouts (pure view concerns — Avalonia types stay
/// out of the view model).</summary>
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
        ShowColorFlyout(btn, Vm.BodyColorHex, hex => Vm.BodyColorHex = hex);
    }

    private void OnAccentColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || Vm is null) return;
        ShowColorFlyout(btn, Vm.AccentColorHex, hex => Vm.AccentColorHex = hex);
    }

    private static void ShowColorFlyout(Button anchor, string? initialHex, Action<string> onColorPicked)
    {
        var colorPicker = new ColorView
        {
            Color = TryParseColor(initialHex),
            IsAlphaVisible = false,
            IsAlphaEnabled = false,
        };

        // Push the new color back to the VM as the user drags the picker.
        colorPicker.ColorChanged += (_, ev) =>
        {
            var c = ev.NewColor;
            onColorPicked($"#{c.R:X2}{c.G:X2}{c.B:X2}");
        };

        var flyout = new Flyout
        {
            Content = colorPicker,
            Placement = PlacementMode.Bottom,
        };

        FlyoutBase.SetAttachedFlyout(anchor, flyout);
        FlyoutBase.ShowAttachedFlyout(anchor);
    }

    private static Color TryParseColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try { return Color.Parse(hex); }
            catch { /* invalid — fall through */ }
        }
        return Color.Parse("#888888");
    }
}
