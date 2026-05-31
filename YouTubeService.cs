using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MusicWidget;

public record YtResult(string Title, string Id)
{
    public override string ToString() => Title;
}

/// <summary>
/// Thin wrapper over yt-dlp: search YouTube and resolve a direct audio stream URL.
/// No download, no browser. Requires yt-dlp on PATH or installed via winget.
/// </summary>
public static class YouTubeService
{
    private static string? _ytdlp;
    private static string? _ffmpeg;

    public static string ResolveFfmpeg()
    {
        if (_ffmpeg is not null) return _ffmpeg;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                var c = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(c)) return _ffmpeg = c;
            }
            catch { }
        }
        var pkgs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(pkgs))
        {
            foreach (var f in Directory.EnumerateFiles(pkgs, "ffmpeg.exe", SearchOption.AllDirectories))
                return _ffmpeg = f;
        }
        return _ffmpeg = "ffmpeg.exe";
    }

    private static string ResolveYtDlp()
    {
        if (_ytdlp is not null) return _ytdlp;

        // 1) PATH
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                var c = Path.Combine(dir.Trim(), "yt-dlp.exe");
                if (File.Exists(c)) return _ytdlp = c;
            }
            catch { }
        }
        // 2) winget package location
        var pkgs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(pkgs))
        {
            foreach (var f in Directory.EnumerateFiles(pkgs, "yt-dlp.exe", SearchOption.AllDirectories))
                return _ytdlp = f;
        }
        return _ytdlp = "yt-dlp.exe"; // last resort, let it fail with a clear error
    }

    public static async Task<List<YtResult>> SearchAsync(string query, int max = 6)
    {
        var lines = await RunAsync(new[]
        {
            "--no-warnings", "--flat-playlist", "--print", "%(title)s|%(id)s",
            $"ytsearch{max}:{query}"
        });
        var list = new List<YtResult>();
        foreach (var l in lines)
        {
            var i = l.LastIndexOf('|');
            if (i > 0) list.Add(new YtResult(l[..i], l[(i + 1)..]));
        }
        return list;
    }

    public static async Task<string?> GetAudioUrlAsync(string videoId)
    {
        var lines = await RunAsync(new[]
        {
            "--no-warnings", "-f", "bestaudio[ext=m4a]/bestaudio", "--get-url",
            $"https://www.youtube.com/watch?v={videoId}"
        });
        return lines.Count > 0 ? lines[0] : null;
    }

    /// <summary>Self-update yt-dlp in the background so YouTube keeps working. Best-effort.</summary>
    public static void UpdateInBackground()
    {
        try { _ = RunAsync(new[] { "-U" }); } catch { }
    }

    private static Task<List<string>> RunAsync(string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveYtDlp(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var tcs = new TaskCompletionSource<List<string>>();
        var output = new List<string>();
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) output.Add(e.Data); };
        proc.Exited += (_, _) => { proc.Dispose(); tcs.TrySetResult(output); };
        try
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }
        catch (Exception ex) { tcs.TrySetException(ex); }
        return tcs.Task;
    }
}
