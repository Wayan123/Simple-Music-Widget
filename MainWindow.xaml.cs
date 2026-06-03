using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MusicWidget;

/// <summary>A row in the list: a YouTube result, a played-history track, or a search suggestion.</summary>
public sealed class PlayerItem
{
    public required string Display { get; init; }
    public YtResult? Track { get; init; }      // playable (result or played history)
    public string? Query { get; init; }         // search suggestion
    public bool Deletable { get; init; }        // history items show the x button
    public string? Subtitle { get; init; }
    public Visibility DeleteVisibility => Deletable ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PreviewVisibility => Track is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SubtitleVisibility => string.IsNullOrWhiteSpace(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
    public string PreviewGlyph => "♫";
    public override string ToString() => Display;  // accessible name = visible text
}

public partial class MainWindow : Window
{
    private readonly MediaService _media = new();
    private LocalPlayer? _local;
    private TrayIcon? _tray;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private double _durationSec;
    private bool _autoStart;

    // Queue of tracks to auto-advance through; index of the current track.
    private readonly List<YtResult> _queue = new();
    private int _queueIndex = -1;
    private YtResult? _lastTrack;   // for Repeat
    private bool _queueActive;      // our own queue is loading/playing; don't auto-hide
    private bool _ownedPlaybackActive; // local/YouTube playback started by this widget
    private int _playGeneration;    // ignores stale async YouTube/local load completions
    private int _searchGeneration;  // ignores stale YouTube search completions
    private bool _updatingVolumeUi; // prevents slider sync from setting volume recursively
    private double _widgetVolume = 1.0; // persisted slider value for the widget-owned player
    private bool _userMoved;        // user dragged the window; keep their position, just don't clip

    public MainWindow()
    {
        InitializeComponent();
        // Boot auto-start (from Startup shortcut) stays hidden until music; manual launch shows now.
        _autoStart = Environment.GetCommandLineArgs().Any(a =>
            a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        Loaded += OnLoaded;
        Closed += (_, _) => { _timer.Stop(); _media.Dispose(); _local?.Dispose(); _tray?.Dispose(); };
        _timer.Tick += OnTick;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionNearTray();
        _tray = new TrayIcon(onShow: SummonWidget, onExit: () => System.Windows.Application.Current.Shutdown());
        if (_autoStart) Hide();          // boot: wait for music
        else SummonWidget();             // manual launch: show immediately (usable even when idle)

        // Create the local/stream player (SMTC) after the window exists; guard against early init.
        try
        {
            _local = new LocalPlayer(new WindowInteropHelper(this).EnsureHandle()) { Volume = _widgetVolume };
            AttachLocalPlayerHandlers(_local);
        }
        catch { /* SMTC for local playback unavailable; external-source control still works */ }

        _media.Changed += snap => Dispatcher.Invoke(() => Render(snap));
        try
        {
            await _media.InitializeAsync();
        }
        catch (Exception ex)
        {
            TitleText.Text = "SMTC tidak tersedia";
            ArtistText.Text = ex.Message;
        }
        UpdateVolumeDisplay();
        _timer.Start();
        YouTubeService.UpdateInBackground(); // keep yt-dlp fresh so YouTube keeps working
        _ = CheckUpdatesAsync();
        TrimWorkingSet(); // release startup memory back to the OS
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

    private static void TrimWorkingSet()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
        }
        catch { }
    }

    private async System.Threading.Tasks.Task CheckUpdatesAsync()
    {
        var tag = await UpdateService.CheckAsync();
        if (tag is not null)
            _tray?.Notify("Music Widget", $"Versi baru {tag} tersedia. Klik untuk update.",
                          UpdateService.OpenReleasesPage);
    }

    private void AttachLocalPlayerHandlers(LocalPlayer player)
    {
        player.Ended += () => Dispatcher.Invoke(OnOwnedPlaybackEnded);
        player.Failed += message => Dispatcher.Invoke(() => HandleOwnedPlaybackFailure(message));
    }

    private void OnOwnedPlaybackEnded()
    {
        if (_queueIndex >= 0 && _queueIndex + 1 < _queue.Count) PlayNextInQueue();
        else { _local?.Stop(); _queueActive = false; _ownedPlaybackActive = false; }
    }

    private void HandleOwnedPlaybackFailure(string message)
    {
        TitleText.Text = "Gagal memutar media";
        ArtistText.Text = message;
        PlayBtn.Content = "\uE768";
        if (_queueIndex >= 0 && _queueIndex + 1 < _queue.Count) PlayNextInQueue();
        else { _queueActive = false; _ownedPlaybackActive = false; }
    }

    private void Render(MediaSnapshot s)
    {
        // Auto-hide when nothing is playing (unless searching or our own queue is active).
        if (!s.HasSession)
        {
            TitleText.Text = "Tidak ada yang diputar";
            ArtistText.Text = "";
            Source.Text = "Music";
            Art.Source = null;
            Progress.Value = 0;
            PlayBtn.Content = "\uE768"; // play glyph
            if (SearchPanel.Visibility != Visibility.Visible && !_queueActive && !_ownedPlaybackActive && IsVisible) Hide();
            return;
        }

        if (!IsVisible) { _userMoved = false; Show(); }
        TitleText.Text = s.Title;
        ArtistText.Text = s.Artist;
        Source.Text = string.IsNullOrWhiteSpace(s.SourceApp) ? "Music" : s.SourceApp;
        Art.Source = s.Thumbnail;
        PlayBtn.Content = s.IsPlaying ? "\uE769" : "\uE768"; // pause : play

        _durationSec = s.Duration.TotalSeconds;
        UpdateProgress(s.Position.TotalSeconds);
    }

    private void UpdateProgress(double posSec)
    {
        Progress.Value = _durationSec > 0 ? Math.Clamp(posSec / _durationSec, 0, 1) : 0;
    }

    // Lightweight 500ms poll keeps the progress bar moving between SMTC events.
    private void OnTick(object? sender, EventArgs e)
    {
        if (_ownedPlaybackActive && _local is not null)
        {
            PlayBtn.Content = _local.IsPlaying ? "\uE769" : "\uE768";
            if (_local.Duration.TotalSeconds > 0) _durationSec = _local.Duration.TotalSeconds;
            UpdateProgress(_local.Position.TotalSeconds);
            return;
        }

        var s = _media.PollTimeline();
        if (s is null) return;
        PlayBtn.Content = s.IsPlaying ? "\uE769" : "\uE768";
        if (s.Duration.TotalSeconds > 0) _durationSec = s.Duration.TotalSeconds;
        UpdateProgress(s.Position.TotalSeconds);
    }

    private async void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (_ownedPlaybackActive && _local is not null) { _local.TogglePlayPause(); return; }
        await _media.PlayPauseAsync();
    }

    private async void OnPrev(object sender, RoutedEventArgs e)
    {
        if (_queueIndex > 0) { _queueIndex -= 2; PlayNextInQueue(); return; }
        if (_ownedPlaybackActive && _local is not null) { _local.Restart(); return; }
        await _media.PreviousAsync();
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_queueIndex >= 0 && _queueIndex + 1 < _queue.Count) { PlayNextInQueue(); return; }
        if (_ownedPlaybackActive && _local is not null) { _playGeneration++; _local.Stop(); _ownedPlaybackActive = false; _queueActive = false; return; }
        await _media.NextAsync();
    }

    // Bring the widget up on demand (from tray), opening search so it's usable when idle.
    private void SummonWidget()
    {
        _userMoved = false;   // a deliberate summon re-homes the widget to the tray corner
        Show();
        // Defer panel-open to after layout so SizeToContent grows the window reliably.
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (SearchPanel.Visibility != Visibility.Visible)
            {
                SearchPanel.Visibility = Visibility.Visible;
                SearchBox.Focus();
                ShowSuggestions(SearchBox.Text);
            }
            Activate();
            UpdateVolumeDisplay();
            PositionNearTray();
        }), DispatcherPriority.Loaded);
    }

    // Called when a second launch (shortcut/taskbar click) signals this running instance.
    public void SummonFromTray() => SummonWidget();

    private void OnClose(object sender, RoutedEventArgs e) { _queueActive = false; Hide(); } // hide; reappears on next music

    private async void OnOpenFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pilih file musik/video lokal",
            Filter = LocalPlayer.OpenDialogFilter
        };
        if (dlg.ShowDialog() != true) return;

        var generation = ++_playGeneration;
        _queue.Clear(); _queueIndex = -1; _queueActive = false;  // local file is standalone
        _ownedPlaybackActive = true;
        _durationSec = 0;
        Art.Source = null;
        TitleText.Text = "Memuat file lokal...";
        ArtistText.Text = System.IO.Path.GetFileName(dlg.FileName);
        Source.Text = "File lokal";
        try
        {
            var pl = EnsurePlayer();
            if (pl is null) { HandleOwnedPlaybackFailure("Pemutar tidak tersedia"); return; }
            await pl.PlayFileAsync(dlg.FileName);
            if (generation != _playGeneration) return;
        }
        catch (Exception ex) { if (generation == _playGeneration) HandleOwnedPlaybackFailure(ex.Message); }
    }

    // Returns the player, creating it on demand if eager init failed.
    private LocalPlayer? EnsurePlayer()
    {
        if (_local is null)
        {
            try
            {
                _local = new LocalPlayer(new WindowInteropHelper(this).EnsureHandle()) { Volume = _widgetVolume };
                AttachLocalPlayerHandlers(_local);
            }
            catch { return null; }
        }
        return _local;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        _userMoved = true;
    }

    // Window height changes (search panel, results filling in, tab switches) must never push
    // content off-screen. Re-anchor on every size change so the bottom stays on-screen.
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_userMoved) ClampOnScreen();
        else PositionNearTray();
    }

    private void OnToggleSearch(object sender, RoutedEventArgs e)
    {
        if (SearchPanel.Visibility == Visibility.Visible && SearchBox.Text.Trim().Length > 0)
        {
            DoSearch();
            return;
        }
        bool show = SearchPanel.Visibility != Visibility.Visible;
        SearchPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) { SearchBox.Focus(); ShowSuggestions(SearchBox.Text); }
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DoSearch();
    }

    // Autocomplete: as the user types, show matching past searches (or played history when empty).
    private bool _suppressTextChange;
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChange) return;
        _searchGeneration++;
        ShowSuggestions(SearchBox.Text);
    }

    private void ShowSuggestions(string prefix)
    {
        prefix = prefix.Trim();
        Results.Items.Clear();
        if (prefix.Length == 0)
        {
            // Empty box: surface recently played so they can be replayed.
            foreach (var t in HistoryStore.Played)
                Results.Items.Add(new PlayerItem { Display = "\u266A " + t.Title, Track = t, Deletable = true, Subtitle = "YouTube • template musik" });
            return;
        }
        foreach (var q in HistoryStore.MatchSearches(prefix))
            Results.Items.Add(new PlayerItem { Display = "\uE721 " + q, Query = q, Deletable = true, Subtitle = "Pencarian tersimpan" });
    }

    private async void DoSearch()
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return;
        var generation = ++_searchGeneration;
        HistoryStore.AddSearch(q);
        Results.Items.Clear();
        Results.Items.Add(new PlayerItem { Display = "Mencari...", Deletable = false });
        try
        {
            var hits = await YouTubeService.SearchAsync(q);
            if (generation != _searchGeneration) return;
            Results.Items.Clear();
            if (hits.Count == 0) { Results.Items.Add(new PlayerItem { Display = "Tidak ada hasil" }); return; }
            foreach (var h in hits)
                Results.Items.Add(new PlayerItem { Display = h.Title, Track = h, Deletable = false, Subtitle = "YouTube • preview template musik" });
        }
        catch (Exception ex)
        {
            if (generation != _searchGeneration) return;
            Results.Items.Clear();
            Results.Items.Add(new PlayerItem { Display = "Gagal mencari YouTube", Subtitle = ex.Message });
            ArtistText.Text = ex.Message;
        }
    }

    private void OnTabResults(object sender, RoutedEventArgs e)
    {
        _searchGeneration++;
        ShowSuggestions(SearchBox.Text);
    }

    private void OnTabPlayed(object sender, RoutedEventArgs e)
    {
        _searchGeneration++;
        Results.Items.Clear();
        foreach (var t in HistoryStore.Played)
            Results.Items.Add(new PlayerItem { Display = "\u266A " + t.Title, Track = t, Deletable = true, Subtitle = "YouTube • template musik" });
    }

    // Pick a row: play a track, or run a stored search query.
    private void OnResultPick(object sender, SelectionChangedEventArgs e)
    {
        if (Results.SelectedItem is not PlayerItem item) return;
        if (item.Query is not null)
        {
            _suppressTextChange = true; SearchBox.Text = item.Query; _suppressTextChange = false;
            Results.SelectedItem = null;
            DoSearch();
            return;
        }
        if (item.Track is null) return;

        // Build a queue from the currently shown playable tracks, starting at the picked one.
        var tracks = Results.Items.OfType<PlayerItem>().Where(i => i.Track is not null)
                            .Select(i => i.Track!).ToList();
        int start = tracks.FindIndex(t => t.Id == item.Track.Id);
        Results.SelectedItem = null;
        StartQueue(tracks, start < 0 ? 0 : start);
    }

    private void OnPlayAll(object sender, RoutedEventArgs e)
    {
        var tracks = Results.Items.OfType<PlayerItem>().Where(i => i.Track is not null)
                            .Select(i => i.Track!).ToList();
        if (tracks.Count > 0) StartQueue(tracks, 0);
    }

    private void StartQueue(List<YtResult> tracks, int index)
    {
        _queue.Clear();
        _queue.AddRange(tracks);
        _queueIndex = index - 1;
        _ownedPlaybackActive = true;
        PlayNextInQueue();
        SearchPanel.Visibility = Visibility.Collapsed;
    }

    private void PlayNextInQueue()
    {
        if (_queueIndex + 1 >= _queue.Count)
        {
            _playGeneration++;
            _local?.Stop();
            _queueActive = false;
            _ownedPlaybackActive = false;
            return;
        }
        _local?.Stop();
        _queueIndex++;
        _ = PlayTrackAsync(_queue[_queueIndex]);
    }

    private async System.Threading.Tasks.Task PlayTrackAsync(YtResult r)
    {
        var generation = ++_playGeneration;
        _lastTrack = r;
        _queueActive = true;
        _ownedPlaybackActive = true;
        _durationSec = 0;            // reset so progress bar starts fresh for the new track
        Art.Source = null;           // show the built-in music template instead of stale art
        HistoryStore.AddPlayed(r);
        TitleText.Text = "Memuat..."; ArtistText.Text = r.Title; Source.Text = "YouTube";
        try
        {
            var url = await YouTubeService.GetAudioUrlAsync(r.Id);
            if (generation != _playGeneration) return;
            if (url is null) { HandleTrackLoadFailure("URL audio kosong"); return; }

            TitleText.Text = "Buffering...";
            var pl = EnsurePlayer();
            if (pl is null) { HandleTrackLoadFailure("Pemutar tidak tersedia"); return; }
            await pl.PlayStreamAsync(url, r.Title, "YouTube");
            if (generation != _playGeneration) return;
        }
        catch (Exception ex)
        {
            if (generation == _playGeneration) HandleTrackLoadFailure(ex.Message);
        }
    }

    private void HandleTrackLoadFailure(string message)
    {
        TitleText.Text = "Gagal memuat";
        ArtistText.Text = message;
        if (_queueIndex >= 0 && _queueIndex + 1 < _queue.Count) PlayNextInQueue();
        else { _queueActive = false; _ownedPlaybackActive = false; }
    }

    private void OnDeleteItem(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PlayerItem item)
        {
            if (item.Query is not null) HistoryStore.RemoveSearch(item.Query);
            else if (item.Track is not null) HistoryStore.RemovePlayed(item.Track.Id);
            Results.Items.Remove(item);
            e.Handled = true;
        }
    }

    private void OnRepeat(object sender, RoutedEventArgs e)
    {
        if (_lastTrack is not null) { _queue.Clear(); _queueIndex = -1; _ = PlayTrackAsync(_lastTrack); }
    }

    private void OnToggleLoop(object sender, RoutedEventArgs e)
    {
        if (_local is null) return;
        _local.Loop = !_local.Loop;
        LoopBtn.Foreground = _local.Loop
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.White;
    }

    private void OnVolumeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVolumeUi || VolumeText is null || VolumeSlider is null) return;

        // Root-cause fix: do not call CoreAudio/native SetMasterVolumeLevelScalar here.
        // The previous implementation crashed with AccessViolation inside coreclr.dll on
        // some Windows audio endpoints. This slider is intentionally widget-player volume.
        var target = Math.Clamp(e.NewValue / 100.0, 0, 1);
        _widgetVolume = target;
        if (_local is not null) _local.Volume = target;
        UpdateVolumeDisplay(target);
    }

    private void UpdateVolumeDisplay(double? level = null)
    {
        if (VolumeText is null || VolumeSlider is null) return;

        level ??= _local?.Volume ?? _widgetVolume;
        _updatingVolumeUi = true;
        try
        {
            if (level is null)
            {
                VolumeText.Text = "--%";
                return;
            }

            var percent = Math.Clamp(Math.Round(level.Value * 100), 0, 100);
            VolumeSlider.Value = percent;
            VolumeText.Text = $"{percent:0}%";
        }
        finally { _updatingVolumeUi = false; }
    }

    // Park the widget at bottom-right, just above the taskbar. Uses ActualHeight because
    // SizeToContent leaves Height = NaN; if content is taller than the space above the
    // taskbar, grow upward (clamp Top to the work area) so the bottom is never clipped.
    private void PositionNearTray()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 12;
        Top = Math.Max(wa.Top, wa.Bottom - ActualHeight - 12);
    }

    // Keep the user's chosen position but pull it back on-screen if a height change would
    // push the bottom (or top) past the work area edges.
    private void ClampOnScreen()
    {
        var wa = SystemParameters.WorkArea;
        if (Top + ActualHeight > wa.Bottom) Top = wa.Bottom - ActualHeight;
        if (Top < wa.Top) Top = wa.Top;
        if (Left + ActualWidth > wa.Right) Left = wa.Right - ActualWidth;
        if (Left < wa.Left) Left = wa.Left;
    }
}
