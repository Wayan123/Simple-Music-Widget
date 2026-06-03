using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using Windows.Media;

namespace MusicWidget;

/// <summary>
/// Plays local media and YouTube streams through WPF MediaPlayer. Unsupported or
/// video-container files are bridged through ffmpeg into a temporary MP3 so the
/// widget can play broad audio/video extensions without showing video UI.
/// </summary>
public sealed class LocalPlayer : IDisposable
{
    private static readonly string[] DirectExtensions =
        [".mp3", ".wav", ".wma", ".m4a", ".aac"];

    public static readonly string[] SupportedExtensions =
    [
        ".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".ogg", ".oga", ".opus",
        ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".3gp", ".3g2"
    ];

    public static string OpenDialogFilter =>
        "Media audio/video|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac;*.ogg;*.oga;*.opus;*.mp4;*.m4v;*.mov;*.mkv;*.webm;*.avi;*.wmv;*.3gp;*.3g2|" +
        "Audio umum|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.wma|" +
        "Video dengan audio|*.mp4;*.m4v;*.mov;*.mkv;*.webm;*.avi;*.wmv;*.3gp;*.3g2|" +
        "Semua file|*.*";

    private readonly MediaPlayer _player = new();
    private readonly SystemMediaTransportControls _smtc;
    private Process? _ffmpeg;
    private string? _tempMedia;
    private bool _isPlaying;
    private int _sourceGeneration;

    /// <summary>When true, the current track restarts on end instead of advancing.</summary>
    public bool Loop { get; set; }

    public bool IsPlaying => _isPlaying;
    public TimeSpan Position => _player.Position;
    public TimeSpan Duration => _player.NaturalDuration.HasTimeSpan
        ? _player.NaturalDuration.TimeSpan
        : TimeSpan.Zero;

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0, 1);
    }

    /// <summary>Raised on the UI thread when a track finishes (and Loop is off).</summary>
    public event Action? Ended;

    /// <summary>Raised when WPF MediaPlayer rejects a file or stream.</summary>
    public event Action<string>? Failed;

    public LocalPlayer(IntPtr hwnd)
    {
        _smtc = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
        _smtc.IsEnabled = true;
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsNextEnabled = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
        _smtc.ButtonPressed += OnButtonPressed;
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
        _player.Volume = 1.0;
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        if (Loop)
        {
            _player.Position = TimeSpan.Zero;
            _player.Play();
            _isPlaying = true;
            _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
            return;
        }

        _isPlaying = false;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
        Ended?.Invoke();
    }

    private void OnMediaFailed(object? sender, ExceptionEventArgs e)
    {
        var message = e.ErrorException?.Message ?? "Format media tidak bisa diputar";
        StopCurrentSource();
        _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
        Failed?.Invoke(message);
    }

    public async Task PlayFileAsync(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("File tidak ditemukan", path);

        var title = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (DirectExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            Play(path, title, "File lokal");
            return;
        }

        // For FLAC/OGG/OPUS and video containers, ffmpeg extracts/transcodes audio.
        try
        {
            await PlayViaFfmpegAsync(path, title, "File lokal", isRemote: false);
        }
        catch when (SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            // Last-resort native attempt: useful if ffmpeg is unavailable but Windows has codecs.
            Play(path, title, "File lokal");
        }
    }

    public void Play(string path, string? title = null, string? artist = null)
    {
        StartNewSource();
        OpenMedia(path, title ?? Path.GetFileNameWithoutExtension(path), artist ?? "File lokal");
    }

    /// <summary>
    /// Plays a remote stream URL (e.g. YouTube) reliably: ffmpeg transcodes it to a
    /// growing local file which MediaPlayer plays progressively after a short buffer.
    /// WPF MediaPlayer cannot open googlevideo URLs directly, so ffmpeg is the bridge.
    /// </summary>
    public Task PlayStreamAsync(string streamUrl, string title, string artist) =>
        PlayViaFfmpegAsync(streamUrl, title, artist, isRemote: true);

    public void TogglePlayPause()
    {
        if (_isPlaying) Pause();
        else PlayCurrent();
    }

    public void Pause()
    {
        _player.Pause();
        _isPlaying = false;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
    }

    public void PlayCurrent()
    {
        _player.Play();
        _isPlaying = true;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
    }

    public void Restart()
    {
        _player.Position = TimeSpan.Zero;
        PlayCurrent();
    }

    public void Stop()
    {
        StopCurrentSource();
        _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
        _isPlaying = false;
    }

    private async Task PlayViaFfmpegAsync(string input, string title, string artist, bool isRemote)
    {
        var generation = StartNewSource();

        var ffmpeg = YouTubeService.ResolveFfmpeg();
        var temp = Path.Combine(Path.GetTempPath(), "mw_" + Guid.NewGuid().ToString("N") + ".mp3");
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var a in new[] { "-y", "-nostdin", "-loglevel", "error", "-i", input,
                                  "-vn", "-map", "0:a:0", "-c:a", "libmp3lame", "-q:a", "4", temp })
            psi.ArgumentList.Add(a);

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            if (!proc.Start()) throw new InvalidOperationException("ffmpeg gagal dijalankan");
        }
        catch (Exception ex)
        {
            proc.Dispose();
            throw new InvalidOperationException("ffmpeg tidak tersedia untuk format ini", ex);
        }

        _ffmpeg = proc;
        _tempMedia = temp;
        var stderrTask = proc.StandardError.ReadToEndAsync();
        var minBytes = isRemote ? 96 * 1024 : 32 * 1024;

        for (int i = 0; i < 80; i++)
        {
            if (File.Exists(temp) && new FileInfo(temp).Length >= minBytes)
            {
                if (!IsCurrentSource(generation)) { CleanupFfmpegProcess(proc, temp, generation); return; }
                OpenMedia(temp, title, artist);
                return;
            }

            if (proc.HasExited)
            {
                if (File.Exists(temp) && new FileInfo(temp).Length > 0)
                {
                    if (!IsCurrentSource(generation)) { CleanupFfmpegProcess(proc, temp, generation); return; }
                    OpenMedia(temp, title, artist);
                    return;
                }

                var err = await stderrTask.WaitAsync(TimeSpan.FromSeconds(1));
                CleanupFfmpegProcess(proc, temp, generation);
                if (!IsCurrentSource(generation)) return;
                throw new InvalidOperationException(CleanFfmpegError(err));
            }

            await Task.Delay(150);
        }

        CleanupFfmpegProcess(proc, temp, generation);
        if (!IsCurrentSource(generation)) return;
        throw new TimeoutException("Buffer ffmpeg terlalu lama; coba file lain atau perbarui ffmpeg");
    }

    private void OpenMedia(string path, string title, string artist)
    {
        _player.Open(new Uri(path));
        _player.Play();
        _isPlaying = true;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;

        var updater = _smtc.DisplayUpdater;
        updater.Type = MediaPlaybackType.Music;
        updater.MusicProperties.Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title;
        updater.MusicProperties.Artist = string.IsNullOrWhiteSpace(artist) ? "Music Widget" : artist;
        updater.Update();
    }

    private static string CleanFfmpegError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return "ffmpeg gagal membaca audio dari media ini";
        var lines = stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0 ? "ffmpeg gagal membaca audio dari media ini" : lines[^1];
    }

    private int StartNewSource()
    {
        StopCurrentSource();
        return _sourceGeneration;
    }

    private bool IsCurrentSource(int generation) => generation == _sourceGeneration;

    private void CleanupFfmpegProcess(Process proc, string temp, int generation)
    {
        try { if (!proc.HasExited) proc.Kill(true); } catch { }
        try { proc.WaitForExit(1000); } catch { }
        try { proc.Dispose(); } catch { }
        try { if (File.Exists(temp)) File.Delete(temp); } catch { }

        if (IsCurrentSource(generation))
        {
            if (ReferenceEquals(_ffmpeg, proc)) _ffmpeg = null;
            if (string.Equals(_tempMedia, temp, StringComparison.OrdinalIgnoreCase)) _tempMedia = null;
            _isPlaying = false;
        }
    }

    private void StopCurrentSource()
    {
        _sourceGeneration++;
        var proc = _ffmpeg;
        var temp = _tempMedia;
        _ffmpeg = null;
        _tempMedia = null;
        _player.Close();
        _isPlaying = false;

        if (proc is not null)
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            try { proc.WaitForExit(1000); } catch { }
            try { proc.Dispose(); } catch { }
        }

        if (temp is not null)
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
    }

    private void OnButtonPressed(SystemMediaTransportControls sender,
                                 SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    PlayCurrent();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    Pause();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    Ended?.Invoke();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    Restart();
                    break;
            }
        });
    }

    public void Dispose()
    {
        _smtc.ButtonPressed -= OnButtonPressed;
        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
        StopCurrentSource();
        _player.Close();
    }
}
