using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicWidget;

internal static class WindowRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly Type WindowType = typeof(MainWindow);
    private static string? _snapshotDirectory;
    private static int _failures;
    private static int _passed;

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var snapshotIndex = Array.IndexOf(args, "--snapshots");
        if (snapshotIndex >= 0) _snapshotDirectory = args[snapshotIndex + 1];
        var suiteIndex = Array.IndexOf(args, "--suite");
        var suite = suiteIndex < 0 ? "All" : args[suiteIndex + 1];
        // These collections stay in memory. No tests call history/language persistence.
        HistoryStore.Searches.Clear();
        HistoryStore.Played.Clear();
        if (suite is "All" or "Window")
        {
            Run("minimize suppresses media popups until explicit restore", MinimizeUntilSummoned);
            Run("manual hide survives media events", HideUntilSummoned);
            Run("summon recovers native minimize followed by automatic hide", NativeMinimizeRecovery);
            Run("restore preserves custom width and position", RestoreCustomGeometry);
            Run("minimum width keeps all controls reachable", MinimumWidth);
            Run("left and right grips resize without clipping controls", ResizeBothEdges);
            Run("left-edge resize preserves the opposite edge at maximum width", ResizeAtWorkAreaLimit);
            Run("search and volume panels remain inside the work area", PanelsRemainReachable);
            Run("repeated expand/minimize/restore preserves geometry", RepeatedTransitions);
        }
        if (suite is "All" or "Presentation")
        {
            Run("owned playback rejects foreign metadata and timeline", OwnedMetadata);
            Run("session loss does not erase owned loading metadata", OwnedLoading);
            Run("paused external media does not summon an auto-hidden widget", PausedMediaStaysHidden);
            Run("search results survive a history round trip", SearchHistoryRoundTrip);
            Run("background search completion does not replace history", BackgroundSearch);
            Run("stale search completion cannot replace a newer query", StaleSearch);
            Run("language refresh preserves playable search results", SearchLanguage);
            Run("editing suggestions retains the last completed results", SearchSuggestions);
            Run("search error remains separate from history", SearchError);
        }
        if (suite is not ("All" or "Window" or "Presentation")) throw new ArgumentException("Unknown suite");
        if (args.Contains("--playback"))
            Run("real playback preserves volume and metadata across source changes", PlaybackVolume);
        app.Shutdown();
        Console.WriteLine($"Regression: {_passed} passed, {_failures} failed");
        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action<MainWindow> scenario)
    {
        MainWindow? window = null;
        try
        {
            HistoryStore.Searches.Clear();
            HistoryStore.Played.Clear();
            window = new MainWindow();
            // Exercise the real WPF window, without startup SMTC, tray, network, or singleton effects.
            window.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), window,
                WindowType.GetMethod("OnLoaded", Private)!);
            Set(window, "_language", UiLanguage.En);
            Call(window, "ApplyLanguage");
            Call(window, "SetTitleText", "Regression fixture track");
            window.Topmost = false;
            window.Show();
            Settle(window);
            scenario(window);
            Console.WriteLine($"PASS {name}");
            _passed++;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"FAIL {name}: {(e is TargetInvocationException ? e.InnerException : e)?.Message}");
            _failures++;
        }
        finally { window?.Close(); }
    }

    private static void MinimizeUntilSummoned(MainWindow w)
    {
        var width = w.ActualWidth;
        Click(w, "MinimizeBtn");
        Assert(!w.IsVisible, "Minimize did not hide the widget");
        Publish(w, playing: false);
        Publish(w, playing: true);
        Assert(!w.IsVisible, "A media event reopened an explicitly minimized widget");
        w.SummonFromTray();
        Settle(w);
        AssertNear(w.ActualWidth, width, "Restore changed the width");
        AssertReachable(w);
    }

    private static void NativeMinimizeRecovery(MainWindow w)
    {
        Set(w, "_userMoved", true);
        w.Width = 430;
        w.Left = 100;
        w.Top = 100;
        Settle(w);
        w.WindowState = WindowState.Minimized;
        Settle(w);
        Call(w, "Render", new MediaSnapshot("", "", "", false, false, null, TimeSpan.Zero, TimeSpan.Zero));
        w.SummonFromTray();
        Settle(w);
        AssertReachable(w);
        AssertNear(w.ActualWidth, 430, "Native minimize recovery lost the normal width");
        AssertNear(w.Left, 100, "Native minimize recovery lost the normal position");
    }

    private static void HideUntilSummoned(MainWindow w)
    {
        Click(w, "HideBtn");
        Publish(w, playing: true);
        Assert(!w.IsVisible, "A media event reopened a manually hidden widget");
        w.SummonFromTray();
        Settle(w);
        AssertReachable(w);
    }

    private static void RestoreCustomGeometry(MainWindow w)
    {
        Set(w, "_userMoved", true);
        w.Width = 430;
        w.Left = 100;
        w.Top = 100;
        Settle(w);
        Click(w, "MinimizeBtn");
        w.SummonFromTray();
        Settle(w);
        AssertNear(w.ActualWidth, 430, "Custom width was lost on restore");
        AssertNear(w.Left, 100, "Custom horizontal position was lost on restore");
        AssertNear(w.Top, 100, "Custom vertical position was lost on restore");
        Click(w, "MaximizeBtn");
        Settle(w);
        Assert(w.ActualWidth > 430, "Expand did not widen the custom-sized widget");
        Click(w, "MaximizeBtn");
        Settle(w);
        AssertNear(w.ActualWidth, 430, "Compact restore discarded the pre-expand width");
        AssertReachable(w);
    }

    private static void MinimumWidth(MainWindow w)
    {
        w.Width = 160;
        Settle(w);
        AssertReachable(w);
        Capture(w, "minimum-width");
    }

    private static void ResizeBothEdges(MainWindow w)
    {
        Set(w, "_userMoved", true);
        w.Left = 200;
        w.Width = 430;
        Settle(w);
        var right = w.Left + w.ActualWidth;
        DragGrip(w, "LeftResizeGrip", -40);
        AssertNear(w.Left, 160, "Dragging the left grip did not move the left edge");
        AssertNear(w.ActualWidth, 470, "Dragging the left grip did not widen the widget");
        AssertNear(w.Left + w.ActualWidth, right, "Left-edge resize moved the opposite edge");
        DragGrip(w, "RightResizeGrip", 50);
        AssertNear(w.ActualWidth, 520, "Dragging the right grip did not widen the widget");
        AssertNear(w.Left, 160, "Right-edge resize moved the opposite edge");
        DragGrip(w, "LeftResizeGrip", 1000);
        AssertReachable(w);
        DragGrip(w, "RightResizeGrip", -1000);
        AssertReachable(w);
    }

    private static void ResizeAtWorkAreaLimit(MainWindow w)
    {
        Set(w, "_userMoved", true);
        w.Width = 430;
        Settle(w);
        w.Left = SystemParameters.WorkArea.Right - w.ActualWidth;
        Settle(w);
        var right = w.Left + w.ActualWidth;
        DragGrip(w, "LeftResizeGrip", -10000);
        AssertNear(w.Left + w.ActualWidth, right, "The maximum-width limit moved the opposite edge");
        AssertReachable(w);
    }

    private static void PanelsRemainReachable(MainWindow w)
    {
        var height = w.ActualHeight;
        Click(w, "SearchBtn");
        Settle(w);
        Assert(w.ActualHeight > height, "Search panel did not grow the window");
        height = w.ActualHeight;
        Click(w, "VolumeToggleBtn");
        Settle(w);
        Assert(w.ActualHeight > height, "Volume panel did not grow the window");
        AssertReachable(w);
        Capture(w, "search-volume");
        Click(w, "SearchBtn");
        Click(w, "VolumeToggleBtn");
        Settle(w);
        AssertReachable(w);
    }

    private static void RepeatedTransitions(MainWindow w)
    {
        var compactWidth = w.ActualWidth;
        for (var i = 0; i < 5; i++)
        {
            Click(w, "MaximizeBtn");
            Settle(w);
            var expandedWidth = w.ActualWidth;
            Assert(expandedWidth > compactWidth, "Expand did not widen the widget");
            Click(w, "MinimizeBtn");
            Publish(w, playing: true);
            Assert(!w.IsVisible, "Playback reopened the widget during a minimize cycle");
            w.SummonFromTray();
            Settle(w);
            AssertNear(w.ActualWidth, expandedWidth, "Expanded width was lost after minimize");
            AssertReachable(w);
            Click(w, "MaximizeBtn");
            Settle(w);
            AssertNear(w.ActualWidth, compactWidth, "Compact width drifted across transitions");
        }
        Capture(w, "restored");
    }

    private static void PlaybackVolume(MainWindow w)
    {
        var directory = Path.Combine(Path.GetTempPath(), "musicwidget-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "First fixture.wav");
            var second = Path.Combine(directory, "Second fixture.wav");
            using (var file = File.Create(first))
            using (var writer = new BinaryWriter(file, System.Text.Encoding.ASCII))
            {
                const int bytes = 3 * 16000 * 2;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(bytes + 36);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1);
                writer.Write(16000); writer.Write(32000); writer.Write((ushort)2); writer.Write((ushort)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
                file.SetLength(bytes + 44);
            }
            File.Copy(first, second);
            Control<Slider>(w, "VolumeSlider").Value = 20;
            Call(w, "PlayLocalFileAsync", first);
            var player = (LocalPlayer)WindowType.GetField("_local", Private)!.GetValue(w)!;
            WaitFor(() => player.Duration.TotalSeconds >= 2 && player.IsPlaying, "The silent WAV did not open");
            Assert(Math.Abs(player.Volume - 0.2) < 0.001, "Starting playback discarded the selected volume");
            Control<Slider>(w, "VolumeSlider").Value = 0;
            Call(w, "PlayLocalFileAsync", second);
            WaitFor(() => player.Duration.TotalSeconds >= 2 && player.IsPlaying, "The second silent WAV did not open");
            Assert(player.Volume == 0, "Changing the source discarded mute");
            Publish(w, playing: true);
            Assert(Control<TextBlock>(w, "TitleText").Tag as string == "Second fixture", "Real owned playback lost its track identity");
        }
        finally
        {
            (WindowType.GetField("_local", Private)!.GetValue(w) as LocalPlayer)?.Stop();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WaitFor(Func<bool> condition, string failure)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        var deadline = DateTime.UtcNow.AddSeconds(8);
        timer.Tick += (_, _) =>
        {
            if (condition() || DateTime.UtcNow >= deadline) { timer.Stop(); frame.Continue = false; }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        Assert(condition(), failure);
    }

    private static void OwnedMetadata(MainWindow w)
    {
        Set(w, "_ownedPlaybackActive", true);
        Call(w, "SetTitleText", "Owned fixture");
        Call(w, "SetArtistText", "Owned artist");
        Control<TextBlock>(w, "Source").Text = "Owned source";
        Control<ProgressBar>(w, "Progress").Value = 0.25;
        Publish(w, playing: true);
        Assert(Control<TextBlock>(w, "TitleText").Tag as string == "Owned fixture", "Foreign event replaced the owned title");
        Assert(Control<TextBlock>(w, "ArtistText").Tag as string == "Owned artist", "Foreign event replaced the owned artist");
        Assert(Control<TextBlock>(w, "Source").Text == "Owned source", "Foreign event replaced the controlled source");
        Assert(Math.Abs(Control<ProgressBar>(w, "Progress").Value - 0.25) < 0.001, "Foreign timeline replaced owned progress");
    }

    private static void OwnedLoading(MainWindow w)
    {
        Set(w, "_ownedPlaybackActive", true);
        Call(w, "SetTitleText", "Fixture loading");
        Call(w, "Render", new MediaSnapshot("", "", "", false, false, null, TimeSpan.Zero, TimeSpan.Zero));
        Assert(Control<TextBlock>(w, "TitleText").Tag as string == "Fixture loading", "Missing external session erased owned loading state");
        Assert(w.IsVisible, "Missing external session hid owned playback");
    }

    private static void PausedMediaStaysHidden(MainWindow w)
    {
        w.Hide();
        Publish(w, playing: false);
        Assert(!w.IsVisible, "A paused external session caused an automatic popup");
        Publish(w, playing: true);
        Settle(w);
        Assert(w.IsVisible, "Automatic show stopped working for playing external media");
    }

    private static readonly YtResult[] Hits = [new("Fixture result A", "fixture-a"), new("Fixture result B", "fixture-b")];
    private static void Complete(MainWindow w, IReadOnlyList<YtResult> hits, string? error = null, int? generation = null)
    {
        Control<StackPanel>(w, "SearchPanel").Visibility = Visibility.Visible;
        Control<ListBox>(w, "Results").Visibility = Visibility.Visible;
        Call(w, "CompleteSearch", generation ?? (int)WindowType.GetField("_searchGeneration", Private)!.GetValue(w)!, hits, error);
        Settle(w);
    }
    private static void AssertTracks(MainWindow w, params string[] ids) =>
        Assert(Control<ListBox>(w, "Results").Items.Cast<PlayerItem>().Where(i => i.Track is not null)
            .Select(i => i.Track!.Id).SequenceEqual(ids), "The visible playable tracks do not match the selected list");

    private static void SearchHistoryRoundTrip(MainWindow w)
    {
        Complete(w, Hits);
        HistoryStore.Played.Clear();
        HistoryStore.Played.Add(new("Fixture history", "history-only"));
        Click(w, "TabPlayed");
        AssertTracks(w, "history-only");
        Click(w, "TabResults");
        AssertTracks(w, "fixture-a", "fixture-b");
        Capture(w, "search-results");
    }

    private static void BackgroundSearch(MainWindow w)
    {
        HistoryStore.Played.Clear();
        HistoryStore.Played.Add(new("Fixture history", "history-only"));
        Click(w, "TabPlayed");
        Complete(w, Hits);
        AssertTracks(w, "history-only");
        Click(w, "TabResults");
        AssertTracks(w, "fixture-a", "fixture-b");
    }

    private static void StaleSearch(MainWindow w)
    {
        Control<TextBox>(w, "SearchBox").Text = "new fixture query";
        Complete(w, Hits);
        Complete(w, [new YtResult("Stale result", "stale")], generation: 0);
        Click(w, "TabResults");
        AssertTracks(w, "fixture-a", "fixture-b");
    }

    private static void SearchLanguage(MainWindow w)
    {
        Complete(w, Hits);
        Set(w, "_language", UiLanguage.Id);
        Call(w, "ApplyLanguage");
        AssertTracks(w, "fixture-a", "fixture-b");
    }

    private static void SearchSuggestions(MainWindow w)
    {
        Complete(w, Hits);
        HistoryStore.Searches.Add("fixture saved query");
        Control<TextBox>(w, "SearchBox").Text = "fixture";
        Assert(Control<ListBox>(w, "Results").Items.Cast<PlayerItem>().Any(i => i.Query == "fixture saved query"), "Typing did not show saved-query suggestions");
        Click(w, "TabResults");
        AssertTracks(w, "fixture-a", "fixture-b");
    }

    private static void SearchError(MainWindow w)
    {
        Complete(w, Array.Empty<YtResult>(), "Fixture search failure");
        HistoryStore.Played.Clear();
        HistoryStore.Played.Add(new("Fixture history", "history-only"));
        Click(w, "TabPlayed");
        AssertTracks(w, "history-only");
        Click(w, "TabResults");
        var rows = Control<ListBox>(w, "Results").Items.Cast<PlayerItem>().ToArray();
        Assert(rows.Any(i => i.Track is null && i.Subtitle == "Fixture search failure"), "Returning to results erased the search error");
    }

    private static void DragGrip(MainWindow w, string name, double delta)
    {
        var grip = Control<Thumb>(w, name);
        AssertHitTarget(w, grip);
        grip.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        grip.RaiseEvent(new DragDeltaEventArgs(delta, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        grip.RaiseEvent(new DragCompletedEventArgs(delta, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Settle(w);
    }

    private static void AssertReachable(MainWindow w)
    {
        Assert(w.IsVisible, "Widget is not visible after restore");
        var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        Assert(!IsIconic(hwnd), "WPF window is visible but native window is still minimized");
        Assert(GetWindowRect(hwnd, out var rect), "Cannot inspect the native window rectangle");
        var scale = GetDpiForWindow(hwnd) / 96d;
        AssertNear((rect.Right - rect.Left) / scale, w.ActualWidth, "Native and layout widths disagree");
        foreach (var name in new[] { "LangBtn", "SearchBtn", "OpenBtn", "MinimizeBtn", "MaximizeBtn", "HideBtn",
                                    "PrevBtn", "PlayBtn", "NextBtn", "RepeatBtn", "LoopBtn", "VolumeToggleBtn" })
            AssertHitTarget(w, Control<FrameworkElement>(w, name));
    }

    private static void AssertHitTarget(MainWindow w, FrameworkElement element)
    {
        var corner = element.TranslatePoint(new Point(), w);
        Assert(corner.X >= -1 && corner.Y >= -1 && corner.X + element.ActualWidth <= w.ActualWidth + 1 &&
               corner.Y + element.ActualHeight <= w.ActualHeight + 1, $"{element.Name} is outside the window");
        DependencyObject? hit = w.InputHitTest(element.TranslatePoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2), w)) as DependencyObject;
        while (hit is not null && hit != element) hit = VisualTreeHelper.GetParent(hit);
        Assert(hit == element, $"{element.Name} is covered by another control or cannot be hit");
    }

    private static void Click(MainWindow w, string name)
    {
        var button = Control<Button>(w, name);
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
        Settle(w);
    }

    private static void Publish(MainWindow w, bool playing) => Call(w, "Render",
        new MediaSnapshot("Fixture external track", "Fixture artist", "Fixture player", playing, true, null,
                          TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(2)));

    private static T Control<T>(MainWindow w, string name) where T : class =>
        w.FindName(name) as T ?? throw new InvalidOperationException($"Control not found: {name}");
    private static void Call(MainWindow w, string method, params object?[] args) =>
        (WindowType.GetMethod(method, Private) ?? throw new InvalidOperationException($"Missing presentation boundary: {method}")).Invoke(w, args);
    private static void Set(MainWindow w, string name, object value) => WindowType.GetField(name, Private)!.SetValue(w, value);
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void AssertNear(double actual, double expected, string message) => Assert(Math.Abs(actual - expected) < 1.5, $"{message}: expected {expected}, actual {actual}");

    private static void Settle(MainWindow w)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(30) };
        string? previous = null;
        var stable = 0;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        timer.Tick += (_, _) =>
        {
            var state = $"{w.IsVisible},{w.WindowState},{w.ActualWidth},{w.ActualHeight},{w.Left},{w.Top}";
            stable = state == previous ? stable + 1 : 0;
            previous = state;
            if (stable >= 3 || DateTime.UtcNow >= deadline) { timer.Stop(); frame.Continue = false; }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Capture(MainWindow w, string name)
    {
        if (_snapshotDirectory is null) return;
        System.IO.Directory.CreateDirectory(_snapshotDirectory);
        var dpi = VisualTreeHelper.GetDpi(w);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(w.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(w);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(System.IO.Path.Combine(_snapshotDirectory, name + ".png"));
        encoder.Save(file);
    }
}
