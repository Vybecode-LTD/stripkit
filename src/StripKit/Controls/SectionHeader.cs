using Avalonia;
using Avalonia.Controls.Primitives;

namespace StripKit.Controls;

/// <summary>
/// A section header: a short dark label with a 3px accent divider beneath it. The
/// divider auto-sizes to the text and overhangs ~25% past it (a horizontal scale in the
/// template) for a small artistic flourish. Styled by a ControlTheme in App.axaml.
/// </summary>
public class SectionHeader : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SectionHeader, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
