using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FluentAssertions;
using StripKit.Views;

namespace StripKit.Tests;

/// <summary>
/// View-level test: the preview area must opt into drops, or the Drop event never
/// fires (the single most common drag-and-drop bug). Runs on the headless Avalonia
/// UI thread via [AvaloniaFact].
/// </summary>
public class DropZoneViewTests
{
    [AvaloniaFact]
    public void Preview_border_opts_into_file_drops()
    {
        var window = new MainWindow();

        var border = window.FindControl<Border>("PreviewBorder");

        border.Should().NotBeNull("the preview area (the drop zone) must exist");
        DragDrop.GetAllowDrop(border!).Should()
            .BeTrue("the preview must enable AllowDrop or dropped files are rejected");
    }
}
