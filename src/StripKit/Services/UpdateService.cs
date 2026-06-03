using Velopack;
using Velopack.Sources;

namespace StripKit.Services;

/// <summary>
/// Checks the GitHub Releases feed for a newer version and applies it on next exit.
/// No-ops unless the app is running as a Velopack-installed build, so dev runs
/// (<c>dotnet run</c>), headless tests, and the portable build are unaffected. All
/// failures (offline, no feed yet) are swallowed — updates must never crash the app.
/// </summary>
public static class UpdateService
{
    // The GitHub repo whose Releases host the StripKit update feed.
    private const string RepoUrl = "https://github.com/Vybecode-LTD/stripkit";

    public static async Task CheckAndApplyAsync()
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));

            // Only meaningful for an installed Velopack build; dev/portable runs skip.
            if (!manager.IsInstalled)
                return;

            var updates = await manager.CheckForUpdatesAsync();
            if (updates is null)
                return; // already on the latest version

            await manager.DownloadUpdatesAsync(updates);

            // Seamless: stage the update and apply it the next time the user closes
            // the app, so the running session is never interrupted.
            manager.WaitExitThenApplyUpdates(updates);
        }
        catch
        {
            // Offline, no feed configured yet, or a transient error — ignore.
        }
    }
}
