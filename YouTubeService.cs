using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicWidget;

public record YtResult(string Title, string Id, string? ThumbnailUrl = null)
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

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                var c = Path.Combine(dir.Trim(), "yt-dlp.exe");
                if (File.Exists(c)) return _ytdlp = c;
            }
            catch { }
        }

        var pkgs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(pkgs))
        {
            foreach (var f in Directory.EnumerateFiles(pkgs, "yt-dlp.exe", SearchOption.AllDirectories))
                return _ytdlp = f;
        }
        return _ytdlp = "yt-dlp.exe";
    }

    public static async Task<List<YtResult>> SearchAsync(string query, int max = 6)
    {
        var lines = await RunAsync(new[]
        {
            "--no-warnings", "--flat-playlist", "--print", "%(title)s|%(id)s|%(thumbnail)s",
            $"ytsearch{max}:{query}"
        });

        var list = new List<YtResult>();
        foreach (var l in lines)
        {
            var last = l.LastIndexOf('|');
            var prev = last > 0 ? l.LastIndexOf('|', last - 1) : -1;
            if (prev <= 0 || last <= prev) continue;

            var title = l[..prev].Trim();
            var id = l[(prev + 1)..last].Trim();
            var thumb = l[(last + 1)..].Trim();
            if (title.Length == 0 || id.Length == 0) continue;
            if (thumb.Length == 0 || thumb.Equals("NA", StringComparison.OrdinalIgnoreCase)) thumb = null;
            list.Add(new YtResult(title, id, thumb));
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
        _ = Task.Run(async () =>
        {
            try { await RunAsync(new[] { "-U" }, timeoutSeconds: 120); }
            catch { }
        });
    }

    private static async Task<List<string>> RunAsync(string[] args, int timeoutSeconds = 45)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveYtDlp(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start()) throw new InvalidOperationException("yt-dlp gagal dijalankan");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("yt-dlp tidak ditemukan atau gagal dijalankan", ex);
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        var waitTask = proc.WaitForExitAsync();
        var finished = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        if (finished != waitTask)
        {
            try { proc.Kill(true); } catch { }
            throw new TimeoutException("yt-dlp terlalu lama merespons");
        }

        await waitTask;
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(CleanProcessError(stderr, "yt-dlp gagal memproses permintaan"));

        return stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                     .Where(l => !string.IsNullOrWhiteSpace(l))
                     .Select(l => l.Trim())
                     .ToList();
    }

    private static string CleanProcessError(string stderr, string fallback)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return fallback;
        var lines = stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0 ? fallback : lines[^1].Trim();
    }
}
