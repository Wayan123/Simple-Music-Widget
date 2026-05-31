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
    public Visibility DeleteVisibility => Deletable ? Visibility.Visible : Visibility.Collapsed;
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

    public MainWindow()
    {
        InitializeComponent();
        // Boot auto-start (from Startup shortcut) stays hidden until music; manual launch shows now.
        _autoStart = Environment.GetCommandLineArgs().Any(a =>
            a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        Loaded += OnLoaded;
        Closed += (_, _) => { _media.Dispose(); _local?.Dispose(); _tray?.Dispose(); };
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
            _local = new LocalPlayer(new WindowInteropHelper(this).EnsureHandle());
            _local.Ended += () => Dispatcher.Invoke(PlayNextInQueue);
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

    private void Render(MediaSnapshot s)
    {
        // Auto-hide when nothing is playing (unless the user opened the search panel).
        if (!s.HasSession)
        {
            TitleText.Text = "Tidak ada yang diputar";
            ArtistText.Text = "";
            Source.Text = "Music";
            Art.Source = null;
            Progress.Value = 0;
            PlayBtn.Content = "\uE768"; // play glyph
            if (SearchPanel.Visibility != Visibility.Visible && IsVisible) Hide();
            return;
        }

        if (!IsVisible) { Show(); _ = Dispatcher.BeginInvoke(PositionNearTray, DispatcherPriority.Loaded); }
        TitleText.Text = s.Title;
        ArtistText.Text = s.Artist;
        Source.Text = string.IsNullOrWhiteSpace(s.SourceApp) ? "Music" : s.SourceApp;
        if (s.Thumbnail is not null) Art.Source = s.Thumbnail;
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
        var s = _media.PollTimeline();
        if (s is null) return;
        PlayBtn.Content = s.IsPlaying ? "\uE769" : "\uE768";
        if (s.Duration.TotalSeconds > 0) _durationSec = s.Duration.TotalSeconds;
        UpdateProgress(s.Position.TotalSeconds);
    }

    private async void OnPlayPause(object sender, RoutedEventArgs e) => await _media.PlayPauseAsync();

    private async void OnPrev(object sender, RoutedEventArgs e)
    {
        if (_queueIndex > 0) { _queueIndex -= 2; PlayNextInQueue(); return; }
        await _media.PreviousAsync();
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_queueIndex >= 0 && _queueIndex + 1 < _queue.Count) { PlayNextInQueue(); return; }
        await _media.NextAsync();
    }

    // Bring the widget up on demand (from tray), opening search so it's usable when idle.
    private void SummonWidget()
    {
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
            PositionNearTray();
        }), DispatcherPriority.Loaded);
    }

    // Called when a second launch (shortcut/taskbar click) signals this running instance.
    public void SummonFromTray() => SummonWidget();

    private void OnClose(object sender, RoutedEventArgs e) => Hide(); // background app: hide, reappears on next music

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pilih file lagu lokal",
            Filter = "Audio|*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.wma|Semua file|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        _queue.Clear(); _queueIndex = -1;        // local file is standalone, not part of a YouTube queue
        EnsurePlayer()?.Play(dlg.FileName);
    }

    // Returns the player, creating it on demand if eager init failed.
    private LocalPlayer? EnsurePlayer()
    {
        if (_local is null)
        {
            try
            {
                _local = new LocalPlayer(new WindowInteropHelper(this).EnsureHandle());
                _local.Ended += () => Dispatcher.Invoke(PlayNextInQueue);
            }
            catch { return null; }
        }
        return _local;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
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
        _ = Dispatcher.BeginInvoke(PositionNearTray, DispatcherPriority.Loaded);
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
                Results.Items.Add(new PlayerItem { Display = "\u266A " + t.Title, Track = t, Deletable = true });
            return;
        }
        foreach (var q in HistoryStore.MatchSearches(prefix))
            Results.Items.Add(new PlayerItem { Display = "\uE721 " + q, Query = q, Deletable = true });
    }

    private async void DoSearch()
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return;
        HistoryStore.AddSearch(q);
        Results.Items.Clear();
        Results.Items.Add(new PlayerItem { Display = "Mencari...", Deletable = false });
        try
        {
            var hits = await YouTubeService.SearchAsync(q);
            Results.Items.Clear();
            if (hits.Count == 0) { Results.Items.Add(new PlayerItem { Display = "Tidak ada hasil" }); return; }
            foreach (var h in hits)
                Results.Items.Add(new PlayerItem { Display = h.Title, Track = h, Deletable = false });
        }
        catch (Exception ex)
        {
            Results.Items.Clear();
            Results.Items.Add(new PlayerItem { Display = "Gagal: yt-dlp tidak ditemukan" });
            ArtistText.Text = ex.Message;
        }
    }

    private void OnTabResults(object sender, RoutedEventArgs e) => ShowSuggestions(SearchBox.Text);

    private void OnTabPlayed(object sender, RoutedEventArgs e)
    {
        Results.Items.Clear();
        foreach (var t in HistoryStore.Played)
            Results.Items.Add(new PlayerItem { Display = "\u266A " + t.Title, Track = t, Deletable = true });
    }

    // Pick a row: play a track, or run a stored search query.
    private void OnResultPick(object sender, SelectionChangedEventArgs e)
    {
        if (Results.SelectedItem is not PlayerItem item) return;
        if (item.Query is not null)
        {
            _suppressTextChange = true; SearchBox.Text = item.Query; _suppressTextChange = false;
            DoSearch();
            return;
        }
        if (item.Track is null) return;

        // Build a queue from the currently shown playable tracks, starting at the picked one.
        var tracks = Results.Items.OfType<PlayerItem>().Where(i => i.Track is not null)
                            .Select(i => i.Track!).ToList();
        int start = tracks.FindIndex(t => t.Id == item.Track.Id);
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
        PlayNextInQueue();
        SearchPanel.Visibility = Visibility.Collapsed;
        _ = Dispatcher.BeginInvoke(PositionNearTray, DispatcherPriority.Loaded);
    }

    private void PlayNextInQueue()
    {
        if (_queueIndex + 1 >= _queue.Count) return;   // queue finished
        _queueIndex++;
        _ = PlayTrackAsync(_queue[_queueIndex]);
    }

    private async System.Threading.Tasks.Task PlayTrackAsync(YtResult r)
    {
        _lastTrack = r;
        HistoryStore.AddPlayed(r);
        TitleText.Text = "Memuat..."; ArtistText.Text = r.Title; Source.Text = "YouTube";
        try
        {
            var url = await YouTubeService.GetAudioUrlAsync(r.Id);
            if (url is null) { TitleText.Text = "Gagal memuat audio"; return; }
            TitleText.Text = "Buffering...";
            var pl = EnsurePlayer();
            if (pl is null) { TitleText.Text = "Pemutar tidak tersedia"; return; }
            await pl.PlayStreamAsync(url, r.Title, "YouTube");
        }
        catch (Exception ex) { TitleText.Text = "Gagal memuat"; ArtistText.Text = ex.Message; }
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

    // Park the widget at bottom-right, just above the taskbar.
    private void PositionNearTray()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 12;
        Top = wa.Bottom - Height - 12;
    }
}
