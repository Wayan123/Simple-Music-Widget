<div align="center">

<img src="docs/icon.png" width="120" alt="Music Widget icon"/>

# 🎵 Simple Music Widget

**A tiny, futuristic now-playing overlay for Windows.**
One widget controls *everything* — YouTube & Spotify in your browser, VLC, Windows Media Player, and more.

<img src="docs/search.png" width="360" alt="Music Widget with YouTube search"/>

</div>

---

## ✨ Why this widget?

Most "now playing" widgets only work with one app. This one reads Windows **System Media Transport Controls (SMTC)** — the same layer the OS uses for its taskbar media flyout — so it shows and controls **whatever you're playing**, from any app, with a single tiny window. It also adds its own **built-in YouTube audio player** (search & play without opening a browser).

- 🪶 **Lightweight** — ~85 MB working set, single small `.exe`, no heavy frameworks.
- 🌐 **Bilingual UI** — English by default with a compact `ID/EN` toggle saved between sessions.
- 🎛️ **Universal control** — prev / play-pause / next + live progress bar for any source.
- 🔊 **Hideable widget volume slider** — adjust the widget-owned local/YouTube player volume safely, then hide it to keep the overlay minimal.
- 📁 **Broad local media support** — play common audio/video containers (`mp3`, `mp4`, `mkv`, `webm`, `flac`, `ogg`, and more) by extracting audio when needed.
- 🔎 **YouTube search & play (audio only)** — no browser needed, with clean music-template previews and a hideable results/history list.
- 🕘 **History** — past searches autocomplete; played tracks are saved and replayable.
- ▶️ **Play all** — queue an entire result list or your history (auto-advances).
- 🔁 **Repeat & Loop** — replay the last track, or loop the current one forever.
- 🗑️ **One-tap delete** — remove any history item with its little ✕.
- 🚀 **Auto-start + auto-show** — runs at boot, appears only when music plays.
- 🔔 **Auto-update** — keeps `yt-dlp` fresh and notifies on new app releases.

---

## 📸 Screenshots

| Compact widget | YouTube search & results | Volume + template preview |
|---|---|---|
| <img src="docs/compact-widget.png" width="300"/> | <img src="docs/search.png" width="300"/> | <img src="docs/volume-preview.png" width="300"/> |

---

## ▶️ Demo (how it works)

1. **Play music anywhere** — YouTube in Chrome/Edge, Spotify, VLC, Windows Media Player. The widget pops up at the bottom-right with the title, artist, artwork, and working controls.
2. **Open local media** — click the file button and choose common audio/video media (`mp3`, `mp4`, `mkv`, `webm`, `flac`, `ogg`, etc.). Video containers are played as audio.
3. **Search YouTube** — click 🔎, type a song, press Enter. Pick a result to play its audio right inside the widget (no browser).
4. **Use volume safely** — click the volume icon only when needed, move the **Vol** slider, then hide it again for a compact widget.
5. **Play all** — hit **▶ Putar semua** to queue the whole list; it auto-advances track to track.
6. **History** — open the search box: empty can show recently **played** tracks; use **Daftar** to show/hide the list, and switch tabs with **Hasil** / **Riwayat**.
7. **Repeat / Loop** — 🔁 replays the last track; the loop button repeats the current one endlessly.
8. **Switch language** — click the tiny `ID/EN` button to switch between English and Indonesian.
9. **Delete** — click the small ✕ on any history row to remove it.

---

## 🚀 Install (Windows)

**Requirements:** Windows 10/11 + [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). For the YouTube feature: `yt-dlp` + `ffmpeg`.

```powershell
winget install Microsoft.DotNet.SDK.8        # to build
winget install yt-dlp.yt-dlp Gyan.FFmpeg     # for YouTube audio
```

### Recommended: Windows installer `.exe`

Download `MusicWidgetSetup-*-win-x64.exe` from [**Releases**](https://github.com/Wayan123/Simple-Music-Widget/releases/latest), run it, then launch **Music Widget** from the Start Menu.

Release installers are self-contained for Windows x64, so users do not need to install the .NET Desktop Runtime separately. The installer is per-user (no admin required), adds a Start Menu shortcut, supports uninstall from Windows Settings, and can optionally add Desktop/Startup shortcuts when built with Inno Setup.

> Note: builds are currently unsigned until a code-signing certificate is available.

### Developer one-click setup

```powershell
git clone https://github.com/Wayan123/Simple-Music-Widget.git
cd Simple-Music-Widget
powershell -ExecutionPolicy Bypass -File install.ps1
```

`install.ps1` stops any old running widget first, builds the app, adds a **Startup** shortcut (auto-run at boot), a **Start Menu** shortcut (right-click → *Pin to taskbar*), and launches it.

### Build release artifacts

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1 -SelfContained
```

This publishes the app, creates a portable ZIP, and builds an installer `.exe`. If Inno Setup is unavailable locally, the script falls back to a no-admin IExpress installer.

### Or just run

```powershell
dotnet run -c Release
```

---

## 🧠 How it stays "one widget for all"

It does **not** talk to YouTube/Spotify directly for control. It reads **SMTC** (`Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`), the central layer every modern media app reports to. For its own YouTube player, `yt-dlp` resolves the audio stream and `ffmpeg` bridges it into a local file that WPF `MediaPlayer` plays (WPF can't open googlevideo URLs directly).

> ⚠️ Playing YouTube audio outside a browser is against YouTube's ToS and can break when YouTube changes. `yt-dlp` is auto-updated to reduce breakage.

---

## 🗂️ Project structure

| File | Role |
|------|------|
| `MediaService.cs` | SMTC wrapper: read active session, snapshot, prev/play/next |
| `LocalPlayer.cs` | MediaPlayer for local files & YouTube (ffmpeg bridge), loop, queue events |
| `YouTubeService.cs` | yt-dlp search + audio-URL resolve; yt-dlp/ffmpeg path resolve |
| `HistoryStore.cs` | JSON persistence for searches & played tracks |
| `UpdateService.cs` | GitHub Releases version checker |
| `TrayIcon.cs` | System-tray icon (show / exit) |
| `App.xaml.cs` | Single-instance entry point (summons running instance) |
| `MainWindow.xaml(.cs)` | UI overlay, search/history, queue, repeat/loop, auto show-hide |
| `install.ps1` | Developer publish + Startup/Start-Menu shortcuts + yt-dlp update |
| `scripts/build-installer.ps1` | Release artifact builder: portable ZIP + installer `.exe` |
| `installer/MusicWidget.iss` | Inno Setup installer definition |
| `.github/workflows/windows-release.yml` | CI workflow for installer artifacts and tag releases |
| `make_icon.py` | Generates the futuristic 3D icon |

---

## 📦 Releasing a new version (maintainer)

1. Bump `<Version>` in `MusicWidget.csproj`.
2. Open a PR and verify the Windows installer workflow passes.
3. Merge, tag, and push:
   ```powershell
   git tag -a v1.3.0 -m "v1.3.0"
   git push origin v1.3.0
   ```
4. GitHub Actions builds `MusicWidgetSetup-*-win-x64.exe` and the portable ZIP, then attaches both to the GitHub Release.
5. Running widgets detect the new release and show a tray notification.

---

## 🐧 Linux?

Not yet. The widget is built on Windows-only tech (SMTC + WPF). A Linux version would need a separate app (MPRIS/D-Bus + GTK/Qt) and is planned as a future, separate project.

---

## 📄 License

MIT — free to use, modify, and share.
