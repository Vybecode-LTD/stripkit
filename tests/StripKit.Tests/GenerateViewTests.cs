using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using StripKit.Views;

namespace StripKit.Tests;

/// <summary>
/// Runtime smoke test for the Generate tab's markup: loading the view exercises its compiled
/// bindings, design-token resources, the element-name reveal binding, and the StringConverters
/// usage — catching XAML load errors that a headless build can't (e.g. an unknown resource or type).
/// </summary>
public class GenerateViewTests
{
    [AvaloniaFact]
    public void Generate_view_loads_and_realizes_with_its_view_model()
    {
        var window = new Window { Content = new GenerateView { DataContext = TestFakes.GenerateVm() } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.Content.Should().BeOfType<GenerateView>("the tab content loaded without a XAML error");
    }
}
