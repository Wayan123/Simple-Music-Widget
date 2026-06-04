using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MusicWidget;

public enum UiLanguage { En, Id }

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
    private UiLanguage _language = LoadLanguage();
    private bool IsIndonesian => _language == UiLanguage.Id;
    private string? _titleMarqueeKey;
    private string? _artistMarqueeKey;
    private bool _userMoved;        // user dragged the window; keep their position, just don't clip

    public MainWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        // Boot auto-start (from Startup shortcut) stays hidden until music; manual launch shows now.
        _autoStart = Environment.GetCommandLineArgs().Any(a =>
            a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        Loaded += OnLoaded;
        Closed += (_, _) => { _timer.Stop(); _media.Dispose(); _local?.Dispose(); _tray?.Dispose(); };
        _timer.Tick += OnTick;
        TitleViewport.SizeChanged += (_, _) => QueueMarqueeUpdate();
        ArtistViewport.SizeChanged += (_, _) => QueueMarqueeUpdate();
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
            TitleText.Text = Text("SMTC unavailable", "SMTC tidak tersedia");
            ArtistText.Text = ex.Message;
        }
        UpdateVolumeDisplay();
        _timer.Start();
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

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicWidget");
    private static readonly string LanguageFile = Path.Combine(SettingsDir, "language.txt");

    private static UiLanguage LoadLanguage()
    {
        try
        {
            var value = File.Exists(LanguageFile) ? File.ReadAllText(LanguageFile).Trim() : "en";
            return value.Equals("id", StringComparison.OrdinalIgnoreCase) ? UiLanguage.Id : UiLanguage.En;
        }
        catch { return UiLanguage.En; }
    }

    private void SaveLanguage()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(LanguageFile, IsIndonesian ? "id" : "en");
        }
        catch { }
    }

    private string Text(string en, string id) => IsIndonesian ? id : en;

    private void ApplyLanguage()
    {
        if (LangBtn is null) return;

        LangBtn.Content = IsIndonesian ? "EN" : "ID";
        LangBtn.ToolTip = IsIndonesian ? "Switch to English" : "Ganti ke Bahasa Indonesia";
        SearchBtn.ToolTip = Text("Search YouTube", "Cari YouTube");
        OpenBtn.ToolTip = Text("Open local media file", "Buka file media lokal");
        CloseBtn.ToolTip = Text("Close", "Tutup");
        RepeatBtn.ToolTip = Text("Repeat last track", "Putar ulang lagu terakhir");
        LoopBtn.ToolTip = Text("Loop this track", "Loop lagu ini");
        VolumeToggleBtn.ToolTip = Text("Show/hide volume", "Tampilkan/sembunyikan volume");
        VolumeSlider.ToolTip = Text("Drag to adjust volume", "Geser untuk mengatur volume");
        VolumeLabel.Text = Text("Vol", "Vol");
        TabResults.Content = Text("Results", "Hasil");
        TabPlayed.Content = Text("History", "Riwayat");
        PlayAllBtn.Content = Text("\u25B6 Play all", "\u25B6 Putar semua");
        PlayAllBtn.ToolTip = Text("Play all from the list", "Putar semua dari daftar");
        UpdateResultsToggleText();
        QueueMarqueeUpdate();
    }

    private void OnToggleLanguage(object sender, RoutedEventArgs e)
    {
        _language = IsIndonesian ? UiLanguage.En : UiLanguage.Id;
        SaveLanguage();
        ApplyLanguage();

        // Refresh lightweight suggestion/history labels after language changes.
        if (SearchPanel.Visibility == Visibility.Visible && Results.Items.Count > 0)
            ShowSuggestions(SearchBox.Text);
    }

    private async System.Threading.Tasks.Task CheckUpdatesAsync()
    {
        var tag = await UpdateService.CheckAsync();
        if (tag is not null)
            _tray?.Notify("Music Widget", Text($"New version {tag} is available. Click to update.",
                                             $"Versi baru {tag} tersedia. Klik untuk update."),
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
        TitleText.Text = Text("Playback failed", "Gagal memutar media");
        ArtistText.Text = message;
        QueueMarqueeUpdate();
        PlayBtn.Content = "\uE768";
        if (_queueIndex >= 0 && _queueIndex + 1 < _queue.Count) PlayNextInQueue();
        else { _queueActive = false; _ownedPlaybackActive = false; }
    }

    private void Render(MediaSnapshot s)
    {
        // Auto-hide when nothing is playing (unless searching or our own queue is active).
        if (!s.HasSession)
        {
            TitleText.Text = Text("No media playing", "Tidak ada yang diputar");
            ArtistText.Text = "";
            Source.Text = Text("Music", "Musik");
            Art.Source = null;
            Progress.Value = 0;
            PlayBtn.Content = "\uE768"; // play glyph
            QueueMarqueeUpdate();
            if (SearchPanel.Visibility != Visibility.Visible && !_queueActive && !_ownedPlaybackActive && IsVisible) Hide();
            return;
        }

        if (!IsVisible) { _userMoved = false; Show(); }
        TitleText.Text = s.Title;
        ArtistText.Text = s.Artist;
        Source.Text = string.IsNullOrWhiteSpace(s.SourceApp) ? Text("Music", "Musik") : s.SourceApp;
        Art.Source = s.Thumbnail;
        PlayBtn.Content = s.IsPlaying ? "\uE769" : "\uE768"; // pause : play
        QueueMarqueeUpdate();

        _durationSec = s.Duration.TotalSeconds;
        UpdateProgress(s.Position.TotalSeconds);
    }

    private void UpdateProgress(double posSec)
    {
        Progress.Value = _durationSec > 0 ? Math.Clamp(posSec / _durationSec, 0, 1) : 0;
    }

    private void QueueMarqueeUpdate()
    {
        _ = Dispatcher.BeginInvoke(new Action(UpdateMarquees), DispatcherPriority.Loaded);
    }

    private void UpdateMarquees()
    {
        UpdateMarquee(TitleViewport, TitleText, TitleTransform, ref _titleMarqueeKey, speedPixelsPerSecond: 26);
        UpdateMarquee(ArtistViewport, ArtistText, ArtistTransform, ref _artistMarqueeKey, speedPixelsPerSecond: 22);
    }

    private static void UpdateMarquee(Border viewport, TextBlock text, TranslateTransform transform,
                                      ref string? previousKey, double speedPixelsPerSecond)
    {
        if (viewport.ActualWidth <= 0 || string.IsNullOrWhiteSpace(text.Text))
        {
            StopMarquee(transform, ref previousKey);
            return;
        }

        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = text.DesiredSize.Width;
        var viewportWidth = viewport.ActualWidth;
        var key = $"{text.Text}|{viewportWidth:0.0}|{textWidth:0.0}";
        if (key == previousKey) return;
        previousKey = key;

        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;

        var overflow = textWidth - viewportWidth;
        if (overflow <= 4) return;

        var travel = overflow + 36; // small pause/gap before the loop resets.
        var seconds = Math.Clamp(travel / speedPixelsPerSecond, 6, 24);
        var animation = new DoubleAnimation
        {
            From = 0,
            To = -travel,
            Duration = TimeSpan.FromSeconds(seconds),
            BeginTime = TimeSpan.FromSeconds(1.2),
            RepeatBehavior = RepeatBehavior.Forever,
            FillBehavior = FillBehavior.Stop
        };
        transform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private static void StopMarquee(TranslateTransform transform, ref string? previousKey)
    {
        previousKey = null;
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
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
                Results.Visibility = Visibility.Collapsed;
                UpdateResultsToggleText();
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
            Title = Text("Choose local music/video file", "Pilih file musik/video lokal"),
            Filter = LocalPlayer.OpenDialogFilter
        };
        if (dlg.ShowDialog() != true) return;

        var generation = ++_playGeneration;
        _queue.Clear(); _queueIndex = -1; _queueActive = false;  // local file is standalone
        _ownedPlaybackActive = true;
        _durationSec = 0;
        Art.Source = null;
        TitleText.Text = Text("Loading local file...", "Memuat file lokal...");
        ArtistText.Text = System.IO.Path.GetFileName(dlg.FileName);
        Source.Text = Text("Local file", "File lokal");
        QueueMarqueeUpdate();
        try
        {
            var pl = EnsurePlayer();
            if (pl is null) { HandleOwnedPlaybackFailure(Text("Player unavailable", "Pemutar tidak tersedia")); return; }
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
        if (show)
        {
            Results.Visibility = Visibility.Collapsed;
            UpdateResultsToggleText();
            SearchBox.Focus();
            ShowSuggestions(SearchBox.Text);
        }
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
                Results.Items.Add(new PlayerItem { Display = "\u266A " + t.Title, Track = t, Deletable = true, Subtitle = Text("YouTube • music template", "YouTube • template musik") });
            return;
        }
        foreach (var q in HistoryStore.MatchSearches(prefix))
            Results.Items.Add(new PlayerItem { Display = "\uE721 " + q, Query = q, Deletable = true, Subtitle = Text("Saved search", "Pencarian tersimpan") });
    }

    private async void DoSearch()
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return;
        var generation = ++_searchGeneration;
        HistoryStore.AddSearch(q);
        Results.Items.Clear();
        Results.Items.Add(new PlayerItem { Display = Text("Searching...", "Mencari..."), Deletable = false });
        try
        {
            var hits = await YouTubeService.SearchAsync(q);
            if (generation != _searchGeneration) return;
            Results.Visibility = Visibility.Visible;
            UpdateResultsToggleText();
            Results.Items.Clear();
            if (hits.Count == 0) { Results.Items.Add(new PlayerItem { Display = Text("No results", "Tidak ada hasil") }); return; }
            foreach (var h in hits)
                Results.Items.Add(new PlayerItem { Display = h.Title, Track = h, Deletable = false, Subtitle = Text("YouTube • music template preview", "YouTube • preview template musik") });
        }
        catch (Exception ex)
        {
            if (generation != _searchGeneration) return;
            Results.Visibility = Visibility.Visible;
            UpdateResultsToggleText();
            Results.Items.Clear();
            Results.Items.Add(new PlayerItem { Display = Text("YouTube search failed", "Gagal mencari YouTube"), Subtitle = ex.Message });
            ArtistText.Text = ex.Message;
        }
    }

    private void OnTabResults(object sender, RoutedEventArgs e)
    {
        _searchGeneration++;
        Results.Visibility = Visibility.Visible;
        UpdateResultsToggleText();
        ShowSuggestions(SearchBox.Text);
    }

    private void OnTabPlayed(object sender, RoutedEventArgs e)
    {
        _searchGeneration++;
        Results.Visibility = Visibility.Visible;
        UpdateResultsToggleText();
        Results.Items.Clear();
        foreach (var t in HistoryStore.Played)
            Results.Items.Add(new PlayerItem { Display = "\u266A " + t.Title, Track = t, Deletable = true, Subtitle = Text("YouTube • music template", "YouTube • template musik") });
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
        TitleText.Text = Text("Loading...", "Memuat..."); ArtistText.Text = r.Title; Source.Text = "YouTube";
        QueueMarqueeUpdate();
        try
        {
            var url = await YouTubeService.GetAudioUrlAsync(r.Id);
            if (generation != _playGeneration) return;
            if (url is null) { HandleTrackLoadFailure(Text("Empty audio URL", "URL audio kosong")); return; }

            TitleText.Text = "Buffering...";
            var pl = EnsurePlayer();
            if (pl is null) { HandleTrackLoadFailure(Text("Player unavailable", "Pemutar tidak tersedia")); return; }
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
        TitleText.Text = Text("Load failed", "Gagal memuat");
        ArtistText.Text = message;
        QueueMarqueeUpdate();
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

    private void OnToggleResults(object sender, RoutedEventArgs e)
    {
        Results.Visibility = Results.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (Results.Visibility == Visibility.Visible && Results.Items.Count == 0) ShowSuggestions(SearchBox.Text);
        UpdateResultsToggleText();
    }

    private void UpdateResultsToggleText()
    {
        if (ResultsToggleBtn is null) return;
        ResultsToggleBtn.Content = Results.Visibility == Visibility.Visible
            ? Text("Hide", "Sembunyi")
            : Text("List", "Daftar");
    }

    private void OnToggleVolume(object sender, RoutedEventArgs e)
    {
        VolumePanel.Visibility = VolumePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        VolumeToggleBtn.Foreground = VolumePanel.Visibility == Visibility.Visible
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.White;
        if (VolumePanel.Visibility == Visibility.Visible) UpdateVolumeDisplay();
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
