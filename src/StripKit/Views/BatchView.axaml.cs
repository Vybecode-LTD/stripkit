using Avalonia.Controls;
using Avalonia.Interactivity;
using StripKit.Helpers;
using StripKit.ViewModels;

namespace StripKit.Views;

/// <summary>
/// The Batch tab. Logic is in <c>BatchViewModel</c>; the colour-swatch flyouts (a view-only concern,
/// shared with Create/Generate via <see cref="ColorFlyoutHelper"/>) live here — no drag-drop (batch
/// works on chosen folders).
/// </summary>
public partial class BatchView : UserControl
{
    public BatchView() => InitializeComponent();

    private BatchViewModel? Vm => DataContext as BatchViewModel;

    private void OnMeterOnColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || Vm is null) return;
        ColorFlyoutHelper.Show(btn, Vm.OnColorHex, alphaAware: true, hex => Vm.OnColorHex = hex);
    }

    private void OnMeterOffColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || Vm is null) return;
        ColorFlyoutHelper.Show(btn, Vm.OffColorHex, alphaAware: true, hex => Vm.OffColorHex = hex);
    }
}
