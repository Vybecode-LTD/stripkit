using FluentAssertions;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>The minimal settings persistence backing first-run state: round-trips, and degrades to
/// defaults for a missing or corrupt file (settings are best-effort and never crash the app).</summary>
public class SettingsServiceTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), $"stripkit_settings_{Guid.NewGuid():N}.json");

    [Fact]
    public void Missing_file_yields_defaults()
    {
        var svc = new SettingsService(TempPath());
        svc.Settings.HasSeenTutorial.Should().BeFalse();
    }

    [Fact]
    public void Save_then_reload_round_trips()
    {
        var path = TempPath();
        try
        {
            var a = new SettingsService(path);
            a.Settings.HasSeenTutorial = true;
            a.Save();

            new SettingsService(path).Settings.HasSeenTutorial.Should().BeTrue();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Corrupt_file_yields_defaults()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ this is not valid json");
        try
        {
            new SettingsService(path).Settings.HasSeenTutorial.Should().BeFalse();
        }
        finally { File.Delete(path); }
    }
}
