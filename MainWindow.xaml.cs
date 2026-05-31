using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MusicWidget;

public partial class MainWindow : Window
{
    private readonly MediaService _media = new();
    private LocalPlayer? _local;
    private TrayIcon? _tray;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private double _durationSec;
    private bool _autoStart;

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
    private async void OnPrev(object sender, RoutedEventArgs e) => await _media.PreviousAsync();
    private async void OnNext(object sender, RoutedEventArgs e) => await _media.NextAsync();

    // Bring the widget up on demand (from tray), opening search so it's usable when idle.
    private void SummonWidget()
    {
        Show();
        if (SearchPanel.Visibility != Visibility.Visible) OnToggleSearch(this, new RoutedEventArgs());
        Activate();
        _ = Dispatcher.BeginInvoke(PositionNearTray, DispatcherPriority.Loaded);
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
        _local ??= new LocalPlayer(new WindowInteropHelper(this).Handle);
        _local.Play(dlg.FileName);
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnToggleSearch(object sender, RoutedEventArgs e)
    {
        // If the panel is open with a query, the button runs the search; otherwise toggles.
        if (SearchPanel.Visibility == Visibility.Visible && SearchBox.Text.Trim().Length > 0)
        {
            DoSearch();
            return;
        }
        bool show = SearchPanel.Visibility != Visibility.Visible;
        SearchPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) SearchBox.Focus();
        // Re-anchor after the window resizes to fit the panel.
        _ = Dispatcher.BeginInvoke(PositionNearTray, DispatcherPriority.Loaded);
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DoSearch();
    }

    private async void DoSearch()
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return;
        Results.Items.Clear();
        Results.Items.Add("Mencari...");
        try
        {
            var hits = await YouTubeService.SearchAsync(q);
            Results.Items.Clear();
            if (hits.Count == 0) { Results.Items.Add("Tidak ada hasil"); return; }
            foreach (var h in hits) Results.Items.Add(h);
        }
        catch (Exception ex)
        {
            Results.Items.Clear();
            Results.Items.Add("Gagal: yt-dlp tidak ditemukan");
            ArtistText.Text = ex.Message;
        }
    }

    private async void OnResultPick(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Results.SelectedItem is not YtResult r) return;
        TitleText.Text = "Memuat..."; ArtistText.Text = r.Title; Source.Text = "YouTube";
        try
        {
            var url = await YouTubeService.GetAudioUrlAsync(r.Id);
            if (url is null) { TitleText.Text = "Gagal memuat audio"; return; }
            _local ??= new LocalPlayer(new WindowInteropHelper(this).Handle);
            TitleText.Text = "Buffering...";
            await _local.PlayStreamAsync(url, r.Title, "YouTube");
            SearchPanel.Visibility = Visibility.Collapsed;
            _ = Dispatcher.BeginInvoke(PositionNearTray, DispatcherPriority.Loaded);
        }
        catch (Exception ex) { TitleText.Text = "Gagal memuat"; ArtistText.Text = ex.Message; }
    }

    // Park the widget at bottom-right, just above the taskbar.
    private void PositionNearTray()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 12;
        Top = wa.Bottom - Height - 12;
    }
}
