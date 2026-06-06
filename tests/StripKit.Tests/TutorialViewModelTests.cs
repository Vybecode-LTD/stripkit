using FluentAssertions;
using StripKit.Services;
using StripKit.ViewModels;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The Getting Started overlay view model (P1 onboarding): step navigation, first-run auto-open
/// gated on persisted state, skip/finish persisting "seen", and the load-sample event.
/// </summary>
public class TutorialViewModelTests
{
    static (TutorialViewModel vm, string path) Build(bool seen = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"stripkit_tut_{Guid.NewGuid():N}.json");
        var settings = new SettingsService(path);
        settings.Settings.HasSeenTutorial = seen;
        if (seen) settings.Save();
        return (new TutorialViewModel(settings), path);
    }

    [Fact]
    public void First_run_opens_when_unseen()
    {
        var (vm, path) = Build(seen: false);
        try
        {
            vm.IsOpen.Should().BeFalse("not opened until MaybeShowOnFirstRun");
            vm.MaybeShowOnFirstRun();
            vm.IsOpen.Should().BeTrue();
            vm.ScreenName.Should().Be("Create", "first run shows the Create walkthrough");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void First_run_stays_closed_when_already_seen()
    {
        var (vm, path) = Build(seen: true);
        try
        {
            vm.MaybeShowOnFirstRun();
            vm.IsOpen.Should().BeFalse();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Next_advances_then_finishes_on_the_last_step()
    {
        var (vm, path) = Build();
        try
        {
            vm.OpenCommand.Execute(0);
            vm.IsFirstStep.Should().BeTrue();
            vm.CurrentIndex.Should().Be(0);

            vm.NextCommand.Execute(null);
            vm.CurrentIndex.Should().Be(1);

            while (!vm.IsLastStep) vm.NextCommand.Execute(null);
            vm.NextLabel.Should().Be("Done");

            vm.NextCommand.Execute(null);   // Done on the last step closes the tour
            vm.IsOpen.Should().BeFalse();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Previous_is_disabled_on_the_first_step()
    {
        var (vm, path) = Build();
        try
        {
            vm.OpenCommand.Execute(0);
            vm.PreviousCommand.CanExecute(null).Should().BeFalse();

            vm.NextCommand.Execute(null);
            vm.PreviousCommand.CanExecute(null).Should().BeTrue();
            vm.PreviousCommand.Execute(null);
            vm.CurrentIndex.Should().Be(0);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Skip_closes_and_persists_seen_so_it_never_auto_reopens()
    {
        var (vm, path) = Build();
        try
        {
            vm.OpenCommand.Execute(0);
            vm.SkipCommand.Execute(null);
            vm.IsOpen.Should().BeFalse();

            // A fresh VM reading the same settings file must not auto-open.
            var reopened = new TutorialViewModel(new SettingsService(path));
            reopened.MaybeShowOnFirstRun();
            reopened.IsOpen.Should().BeFalse("Skip persisted HasSeenTutorial");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Re_opening_from_the_help_button_always_starts_at_step_one()
    {
        var (vm, path) = Build(seen: true);
        try
        {
            vm.OpenCommand.Execute(0);
            vm.NextCommand.Execute(null);
            vm.SkipCommand.Execute(null);

            vm.OpenCommand.Execute(0);   // re-open from Help
            vm.IsOpen.Should().BeTrue();
            vm.CurrentIndex.Should().Be(0);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Open_shows_the_tutorial_for_the_requested_screen()
    {
        var (vm, path) = Build();
        try
        {
            vm.OpenCommand.Execute((int)TutorialScreen.Import);
            vm.IsOpen.Should().BeTrue();
            vm.ScreenName.Should().Be("Import");
            vm.CurrentIndex.Should().Be(0);
            vm.CurrentOffersSample.Should().BeFalse("only the Create walkthrough offers the sample knob");

            vm.OpenCommand.Execute((int)TutorialScreen.Skin);
            vm.ScreenName.Should().Be("Skin", "re-opening for another tab switches the walkthrough");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void First_step_offers_the_sample_and_load_sample_raises_the_event()
    {
        var (vm, path) = Build();
        try
        {
            vm.OpenCommand.Execute(0);
            vm.CurrentOffersSample.Should().BeTrue("step 1 offers the sample knob");
            vm.StepProgress.Should().Be($"Step 1 of {vm.Steps.Count}");

            bool raised = false;
            vm.LoadSampleRequested += () => raised = true;
            vm.LoadSampleCommand.Execute(null);
            raised.Should().BeTrue();
        }
        finally { File.Delete(path); }
    }
}
