using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace StripKit.Helpers;

/// <summary>Shows a <see cref="ColorView"/> flyout anchored to a button and round-trips the picked
/// colour back to a hex string. Shared by every colour-swatch button in the app (Generate, Create,
/// Batch) so the flyout behaviour and hex formatting can't drift between tabs — previously each hex
/// field was a plain unvalidated TextBox except Generate's hand-rolled swatch (audit finding, three
/// independent reviews).</summary>
public static class ColorFlyoutHelper
{
    /// <summary>Opens the flyout. <paramref name="alphaAware"/> selects the hex format: <c>#AARRGGBB</c>
    /// (Create/Batch's convention) when true, <c>#RRGGBB</c> (Generate's convention) when false.</summary>
    public static void Show(Button anchor, string? initialHex, bool alphaAware, Action<string> onColorPicked)
    {
        var colorPicker = new ColorView
        {
            Color = TryParseColor(initialHex),
            IsAlphaVisible = alphaAware,
            IsAlphaEnabled = alphaAware,
        };

        // Push the new color back to the VM as the user drags the picker.
        colorPicker.ColorChanged += (_, ev) =>
        {
            var c = ev.NewColor;
            onColorPicked(alphaAware
                ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"#{c.R:X2}{c.G:X2}{c.B:X2}");
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
