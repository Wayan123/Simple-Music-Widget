using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MusicWidget;

/// <summary>
/// Checks GitHub Releases for a newer version. Best-effort, non-blocking.
/// Does not silently replace the running exe; surfaces the release to the user.
/// </summary>
public static class UpdateService
{
    private const string LatestApi =
        "https://api.github.com/repos/Wayan123/Simple-Music-Widget/releases/latest";
    public const string ReleasesPage =
        "https://github.com/Wayan123/Simple-Music-Widget/releases/latest";

    /// <summary>Returns the newer version tag if an update is available, else null.</summary>
    public static async Task<string?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MusicWidget");
            var json = await http.GetStringAsync(LatestApi);
            var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            if (!m.Success) return null;

            var latest = ParseVersion(m.Groups[1].Value);
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return latest is not null && latest > current ? m.Groups[1].Value : null;
        }
        catch { return null; }
    }

    public static void OpenReleasesPage()
    {
        try { Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true }); }
        catch { }
    }

    private static Version? ParseVersion(string tag)
    {
        var m = Regex.Match(tag, @"(\d+)\.(\d+)\.(\d+)");
        return m.Success
            ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))
            : null;
    }
}
