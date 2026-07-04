using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using GSMTCSM = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using GSMTCS = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace MusicWidget;

/// <summary>Immutable snapshot of the currently playing media across the whole system.</summary>
public record MediaSnapshot(
    string Title,
    string Artist,
    string SourceApp,
    bool IsPlaying,
    bool HasSession,
    BitmapImage? Thumbnail,
    TimeSpan Position,
    TimeSpan Duration);

/// <summary>
/// Wraps Windows System Media Transport Controls (SMTC). One service that surfaces
/// whatever app the user is most likely controlling (browser/YouTube, Spotify, etc.).
/// </summary>
public sealed class MediaService : IDisposable
{
    private GSMTCSM? _manager;
    private GSMTCS? _session;
    private int _publishSequence;

    public event Action<MediaSnapshot>? Changed;

    public async Task InitializeAsync()
    {
        _manager = await GSMTCSM.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => HookCurrentSession();
        HookCurrentSession();
    }

    private void HookCurrentSession()
    {
        // Detach old session handlers.
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnAnyChanged;
            _session.PlaybackInfoChanged -= OnAnyChanged;
            _session.TimelinePropertiesChanged -= OnAnyChanged;
        }

        _session = _manager?.GetCurrentSession();

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnAnyChanged;
            _session.PlaybackInfoChanged += OnAnyChanged;
            _session.TimelinePropertiesChanged += OnAnyChanged;
        }
        _ = PublishAsync();
    }

    private void OnAnyChanged(object? sender, object args) => _ = PublishAsync();

    private async Task PublishAsync()
    {
        var sequence = Interlocked.Increment(ref _publishSequence);
        var snap = await BuildSnapshotAsync();
        if (sequence == Volatile.Read(ref _publishSequence)) Changed?.Invoke(snap);
    }

    private async Task<MediaSnapshot> BuildSnapshotAsync()
    {
        var s = _session;
        if (s is null)
            return new MediaSnapshot("No media playing", "", "", false, false, null,
                                     TimeSpan.Zero, TimeSpan.Zero);

        string title = "", artist = "", source = "";
        bool playing = false;
        TimeSpan pos = TimeSpan.Zero, dur = TimeSpan.Zero;
        BitmapImage? art = null;

        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            title = props.Title ?? "";
            artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist ?? "" : props.Artist;
            art = await ToBitmapAsync(props.Thumbnail);
        }
        catch { /* some apps briefly throw while switching tracks */ }

        try
        {
            var info = s.GetPlaybackInfo();
            playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch { }

        try
        {
            var tl = s.GetTimelineProperties();
            pos = tl.Position;
            dur = tl.EndTime - tl.StartTime;
        }
        catch { }

        try { source = FriendlyName(s.SourceAppUserModelId); } catch { }

        return new MediaSnapshot(
            string.IsNullOrWhiteSpace(title) ? "No title" : title,
            artist, source, playing, true, art, pos, dur);
    }

    private static async Task<BitmapImage?> ToBitmapAsync(IRandomAccessStreamReference? thumbRef)
    {
        if (thumbRef is null) return null;
        try
        {
            using var stream = await thumbRef.OpenReadAsync();
            uint size = (uint)stream.Size;
            if (size == 0) return null;
            var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            var ms = new System.IO.MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze(); // cross-thread safe
            return bmp;
        }
        catch { return null; }
    }

    private static string FriendlyName(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "Music";
        var lower = aumid.ToLowerInvariant();
        if (lower.Contains("chrome")) return "Chrome";
        if (lower.Contains("msedge") || lower.Contains("edge")) return "Edge";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("brave")) return "Brave";
        // Strip exe/packaged suffixes for a clean label.
        var name = aumid.Split('!')[0].Split('\\')[^1];
        return name.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
    }

    public async Task PlayPauseAsync() { if (_session is not null) try { await _session.TryTogglePlayPauseAsync(); } catch { } }
    public async Task NextAsync() { if (_session is not null) try { await _session.TrySkipNextAsync(); } catch { } }
    public async Task PreviousAsync() { if (_session is not null) try { await _session.TrySkipPreviousAsync(); } catch { } }

    public MediaSnapshot? PollTimeline()
    {
        var s = _session;
        if (s is null) return null;
        try
        {
            var tl = s.GetTimelineProperties();
            var info = s.GetPlaybackInfo();
            bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            return new MediaSnapshot("", "", "", playing, true, null, tl.Position, tl.EndTime - tl.StartTime);
        }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnAnyChanged;
            _session.PlaybackInfoChanged -= OnAnyChanged;
            _session.TimelinePropertiesChanged -= OnAnyChanged;
        }
    }
}
