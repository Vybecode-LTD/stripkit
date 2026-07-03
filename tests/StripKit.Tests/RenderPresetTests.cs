using System.IO;
using System.Linq;
using FluentAssertions;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Save/load render presets: a named snapshot of the Create tab's full render setup, persisted
/// via <see cref="ISettingsService"/> and restored in one click. Covers the JSON round-trip and
/// the view-model's save/apply/delete commands (including overwrite-by-name and CanExecute gating).
/// </summary>
public class RenderPresetTests
{
    static string TempSettingsPath() => Path.Combine(Path.GetTempPath(), $"stripkit_preset_test_{Guid.NewGuid():N}.json");

    [Fact]
    public void A_saved_preset_round_trips_through_settings_json()
    {
        var path = TempSettingsPath();
        try
        {
            var svc = new SettingsService(path);
            svc.Settings.RenderPresets.Add(new RenderPreset
            {
                Name = "My Knob",
                ComponentType = ComponentType.RotaryKnob,
                FrameCount = 96,
                FrameWidth = 100,
                FrameHeight = 100,
                Layout = StripLayout.Grid,
                GridColumns = 6,
                MappingCurve = FrameMappingCurve.Skew,
                MappingSkew = 2.5,
            });
            svc.Save();

            var reloaded = new SettingsService(path);
            reloaded.Settings.RenderPresets.Should().ContainSingle();
            var p = reloaded.Settings.RenderPresets[0];
            p.Name.Should().Be("My Knob");
            p.FrameCount.Should().Be(96);
            p.Layout.Should().Be(StripLayout.Grid);
            p.GridColumns.Should().Be(6);
            p.MappingCurve.Should().Be(FrameMappingCurve.Skew);
            p.MappingSkew.Should().Be(2.5);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SavePresetCommand_is_disabled_until_a_name_is_entered()
    {
        var vm = TestFakes.MainVm(TestFakes.TempSettings());
        vm.SavePresetCommand.CanExecute(null).Should().BeFalse();

        vm.NewPresetName = "  ";
        vm.SavePresetCommand.CanExecute(null).Should().BeFalse();

        vm.NewPresetName = "Bright Knob";
        vm.SavePresetCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Saving_a_preset_adds_it_to_the_list_and_persists_it()
    {
        var settings = TestFakes.TempSettings();
        var vm = TestFakes.MainVm(settings);

        vm.ComponentType = ComponentType.Meter;
        vm.FrameCount = 40;
        vm.NewPresetName = "My Meter";
        vm.SavePresetCommand.Execute(null);

        vm.Presets.Should().ContainSingle(p => p.Name == "My Meter" && p.FrameCount == 40);
        settings.Settings.RenderPresets.Should().ContainSingle(p => p.Name == "My Meter");
        vm.NewPresetName.Should().BeEmpty(); // cleared after a successful save
    }

    [Fact]
    public void Saving_a_preset_with_an_existing_name_overwrites_it_instead_of_duplicating()
    {
        var vm = TestFakes.MainVm(TestFakes.TempSettings());

        vm.FrameCount = 32;
        vm.NewPresetName = "Standard";
        vm.SavePresetCommand.Execute(null);

        vm.FrameCount = 128;
        vm.NewPresetName = "standard"; // case-insensitive match
        vm.SavePresetCommand.Execute(null);

        vm.Presets.Should().ContainSingle();
        vm.Presets[0].FrameCount.Should().Be(128);
    }

    [Fact]
    public void Preset_commands_require_a_selection()
    {
        var vm = TestFakes.MainVm(TestFakes.TempSettings());
        vm.ApplyPresetCommand.CanExecute(null).Should().BeFalse();
        vm.DeletePresetCommand.CanExecute(null).Should().BeFalse();

        vm.NewPresetName = "P1";
        vm.SavePresetCommand.Execute(null);
        vm.SelectedPreset = vm.Presets[0];

        vm.ApplyPresetCommand.CanExecute(null).Should().BeTrue();
        vm.DeletePresetCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Applying_a_preset_restores_the_full_render_setup()
    {
        var vm = TestFakes.MainVm(TestFakes.TempSettings());

        vm.ComponentType = ComponentType.RotaryKnob;
        vm.FrameCount = 48;
        vm.Layout = StripLayout.Grid;
        vm.GridColumns = 6;
        vm.MappingCurve = FrameMappingCurve.Logarithmic;
        vm.MappingLogBase = 4.0;
        vm.ShowValueArc = true;
        vm.ArcColorHex = "#FF00FF00";
        vm.NewPresetName = "Log Grid Knob";
        vm.SavePresetCommand.Execute(null);

        // Mutate everything away from the saved preset.
        vm.ComponentType = ComponentType.Meter;
        vm.FrameCount = 10;
        vm.Layout = StripLayout.Strip;
        vm.MappingCurve = FrameMappingCurve.Linear;
        vm.ShowValueArc = false;
        vm.ArcColorHex = "#FFFFFFFF";

        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "Log Grid Knob");
        vm.ApplyPresetCommand.Execute(null);

        vm.ComponentType.Should().Be(ComponentType.RotaryKnob);
        vm.FrameCount.Should().Be(48);
        vm.Layout.Should().Be(StripLayout.Grid);
        vm.GridColumns.Should().Be(6);
        vm.MappingCurve.Should().Be(FrameMappingCurve.Logarithmic);
        vm.MappingLogBase.Should().Be(4.0);
        vm.ShowValueArc.Should().BeTrue();
        vm.ArcColorHex.Should().Be("#FF00FF00");
    }

    [Fact]
    public void Deleting_a_duplicate_named_preset_removes_only_the_selected_one_by_reference()
    {
        // A hand-edited settings.json (or any path that bypasses SavePreset's overwrite guard)
        // could carry two distinct RenderPreset objects that happen to share a name. Delete must
        // remove exactly the selected reference from BOTH collections, not every same-named entry,
        // or the UI list and the persisted settings silently drift apart.
        var path = Path.Combine(Path.GetTempPath(), $"stripkit_preset_dup_{Guid.NewGuid():N}.json");
        try
        {
            var first = new SettingsService(path);
            var dup1 = new RenderPreset { Name = "Dup", FrameCount = 32 };
            var dup2 = new RenderPreset { Name = "Dup", FrameCount = 64 };
            first.Settings.RenderPresets.Add(dup1);
            first.Settings.RenderPresets.Add(dup2);
            first.Save();

            var reloaded = new SettingsService(path);
            var vm = TestFakes.MainVm(reloaded);
            vm.Presets.Should().HaveCount(2);

            vm.SelectedPreset = vm.Presets.First(p => p.FrameCount == 32);
            vm.DeletePresetCommand.Execute(null);

            vm.Presets.Should().ContainSingle(p => p.FrameCount == 64);
            reloaded.Settings.RenderPresets.Should().ContainSingle(p => p.FrameCount == 64);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Deleting_a_preset_removes_it_from_the_list_and_settings()
    {
        var settings = TestFakes.TempSettings();
        var vm = TestFakes.MainVm(settings);

        vm.NewPresetName = "Temp";
        vm.SavePresetCommand.Execute(null);
        vm.SelectedPreset = vm.Presets[0];

        vm.DeletePresetCommand.Execute(null);

        vm.Presets.Should().BeEmpty();
        settings.Settings.RenderPresets.Should().BeEmpty();
        vm.SelectedPreset.Should().BeNull();
    }

    [Fact]
    public void Presets_saved_in_an_earlier_session_are_loaded_on_construction()
    {
        var path = TempSettingsPath();
        try
        {
            var first = new SettingsService(path);
            first.Settings.RenderPresets.Add(new RenderPreset { Name = "Carried Over", FrameCount = 64 });
            first.Save();

            var second = new SettingsService(path);
            var vm = TestFakes.MainVm(second);

            vm.Presets.Should().ContainSingle(p => p.Name == "Carried Over");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
