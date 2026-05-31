using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using Windows.Media;
using Windows.Storage.Streams;

namespace MusicWidget;

/// <summary>
/// Plays a local audio file via WPF MediaPlayer and reports it to Windows SMTC,
/// so the same widget (which reads SMTC) controls it like any other source.
/// </summary>
public sealed class LocalPlayer : IDisposable
{
    private readonly MediaPlayer _player = new();
    private readonly SystemMediaTransportControls _smtc;
    private Process? _ffmpeg;
    private string? _streamTemp;

    /// <summary>When true, the current track restarts on end instead of advancing.</summary>
    public bool Loop { get; set; }

    /// <summary>Raised on the UI thread when a track finishes (and Loop is off).</summary>
    public event Action? Ended;

    public LocalPlayer(IntPtr hwnd)
    {
        // SMTC for a Win32/WPF window must be obtained through the interop helper.
        _smtc = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
        _smtc.IsEnabled = true;
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsNextEnabled = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
        _smtc.ButtonPressed += OnButtonPressed;
        _player.MediaEnded += OnMediaEnded;
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        if (Loop) { _player.Position = TimeSpan.Zero; _player.Play(); return; }
        _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
        Ended?.Invoke();
    }

    public void Play(string path, string? title = null, string? artist = null)
    {
        _player.Open(new Uri(path));
        _player.Play();
        _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;

        var updater = _smtc.DisplayUpdater;
        updater.Type = MediaPlaybackType.Music;
        updater.MusicProperties.Title = title ?? Path.GetFileNameWithoutExtension(path);
        updater.MusicProperties.Artist = artist ?? "File lokal";
        updater.Update();
    }

    /// <summary>
    /// Plays a remote stream URL (e.g. YouTube) reliably: ffmpeg transcodes it to a
    /// growing local file which MediaPlayer plays progressively after a short buffer.
    /// WPF MediaPlayer cannot open googlevideo URLs directly, so ffmpeg is the bridge.
    /// </summary>
    public async Task PlayStreamAsync(string streamUrl, string title, string artist)
    {
        StopStream();
        var ffmpeg = YouTubeService.ResolveFfmpeg();
        var temp = Path.Combine(Path.GetTempPath(), "mw_" + Guid.NewGuid().ToString("N") + ".mp3");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in new[] { "-y", "-loglevel", "error", "-i", streamUrl,
                                  "-c:a", "libmp3lame", "-q:a", "4", temp })
            psi.ArgumentList.Add(a);
        _ffmpeg = Process.Start(psi);
        _streamTemp = temp;

        // Wait until ffmpeg has buffered enough to start playback.
        for (int i = 0; i < 60 && (!File.Exists(temp) || new FileInfo(temp).Length < 96 * 1024); i++)
            await Task.Delay(150);

        Play(temp, title, artist);
    }

    private void StopStream()
    {
        try { if (_ffmpeg is { HasExited: false }) _ffmpeg.Kill(true); } catch { }
        _ffmpeg = null;
        _player.Close();
        if (_streamTemp is not null)
        {
            try { File.Delete(_streamTemp); } catch { }
            _streamTemp = null;
        }
    }

    private void OnButtonPressed(SystemMediaTransportControls sender,
                                 SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        // SMTC events arrive off the UI thread; marshal player calls back.
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    _player.Play();
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    _player.Pause();
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
                    break;
                case SystemMediaTransportControlsButton.Next:
                    Ended?.Invoke();
                    break;
            }
        });
    }

    public void Dispose()
    {
        _smtc.ButtonPressed -= OnButtonPressed;
        _player.MediaEnded -= OnMediaEnded;
        StopStream();
        _player.Close();
    }
}
