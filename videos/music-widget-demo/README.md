# Music Widget — demo & social media

Actual Windows WPF footage of live YouTube search and playback. All on-screen promotional copy and the recorded app UI are English. The charcoal–emerald stage belongs to the video, not the application UI.

## Deliverables

| Destination | File | Canvas | Length |
|---|---|---|---|
| GitHub README | [`../../docs/music-widget-youtube-demo.gif`](../../docs/music-widget-youtube-demo.gif) | 960 × 640 | 30 s |
| Instagram Reels | `dist/social-media/music-widget-instagram-reels.mp4` | 1080 × 1920 | 36 s |
| Threads feed | `dist/social-media/music-widget-threads.mp4` | 1080 × 1350 | 33 s |
| TikTok | `dist/social-media/music-widget-tiktok.mp4` | 1080 × 1920 | 34 s |
| YouTube Shorts | `dist/social-media/music-widget-youtube-shorts.mp4` | 1080 × 1920 | 35 s |

MP4 paths are relative to the repository root. Generated posting files and the ZIP live in gitignored `dist/social-media/`; they are not release installers. The GIF and media source belong in Git. This repository does not automatically upload MP4s to any social platform.

Copy: [CAPTIONS.md](CAPTIONS.md), with separate English captions and a YouTube title/description. Openings and timing differ by platform. Captions link the repository on main and include the required Scott Buckley credit.

## What the demo proves

Type “Scott Buckley Hiraeth”, run the app's real YouTube search, select the actual official result [XPZL2yFOeA8](https://www.youtube.com/watch?v=XPZL2yFOeA8), and verify advancing real stream playback. Then pause/resume, change volume, compact/expand, hide, and restore through `MainWindow.SummonFromTray`. Rings annotate actual control invocations; a tray mouse click is not filmed. No results, stream URLs, track titles, or playback positions are fabricated. Longer real network waits are shortened and disclosed; this is not a latency benchmark.

The GIF is silent. MP4 sound transitions from an original intro bed to an edited **Hiraeth** excerpt after confirmed playback. It follows the pause/volume demonstration but is not live system audio. **'Hiraeth' by Scott Buckley - released under CC-BY 4.0. www.scottbuckley.com.au**. [Source and permission](https://www.scottbuckley.com.au/library/hiraeth/); [license](https://creativecommons.org/licenses/by/4.0/). Keep attribution in social captions, especially the YouTube description.

## Rebuild locally (WSL/Linux)

Needs Python 3 + NumPy, FFmpeg with libvpx-vp9, and the installed HyperFrames suite wrapper (pinned CLI 0.8.81). Captured media, licensed excerpts, fonts, and local GSAP are included in `assets/`; rendering uses no model provider or cloud service.

```bash
cd videos/music-widget-demo
HF="$HOME/.agents/skills/hyperframes-suite/scripts/hf.sh"
python3 tools/compose.py
bash "$HF" check platforms/readme --snapshots
bash "$HF" render platforms/readme --format gif --fps 15 --quality delivery --workers 2 \
  --output ../../docs/music-widget-youtube-demo.gif
bash "$HF" check platforms/instagram --snapshots
bash "$HF" render platforms/instagram --fps 30 --quality delivery --workers 2 \
  --output ../../dist/social-media/music-widget-instagram-reels.mp4
```

Repeat the final two commands for `threads`, `tiktok`, and `youtube-shorts`, changing output names accordingly. `python3 tools/compose.py` creates self-contained platform folders and regenerates the root `index.html` as the Instagram preview. Edit the generator rather than diverging generated HTML. `npm run dev` opens the local Studio preview; it uses the installed WSL wrapper. Inspect the Studio project URL printed by the CLI, then stop the preview explicitly when finished.

To regenerate the original intro instrumental:

```bash
python3 tools/generate-audio.py assets/demo-track.wav
```

## Capture again (optional, Windows)

Requires .NET 8 SDK, Windows FFmpeg, a working recent yt-dlp/runtime, public YouTube access, and a real built/installed `MusicWidget.dll`. The successful recording used official yt-dlp **2026.08.19** (SHA-256 `66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a`). The installed 2026.06.09 version resolved search results but its stream failed with HTTP 403. The newer extractor was used in a temporary process PATH; no globally installed executable was replaced. From Windows PowerShell:

The dedicated extractor folder also contained a portable `yt-dlp.conf` with `--encoding utf-8`, matching the application's UTF-8 stdout decoder. This prevents corrupted non-ASCII result titles; neither the installed executable nor global configuration was overwritten. Result titles remain exactly as returned by YouTube, not translated or replaced.

```powershell
.\videos\music-widget-demo\tools\record.ps1
# Or pass -Assembly C:\path\to\MusicWidget.dll
# Optional: -YtDlpDirectory C:\path\to\verified-extractor-folder
```

The recorder opens its own real WPF window, uses the app's unmodified YouTube service/player, and checks actual playback progress before finishing. Normal startup is bypassed to avoid background services. Autocomplete is suppressed only during scripted typing so saved personal suggestions do not enter the footage; the History tab is never shown and stored history is not cleared. Real search/play may add this demonstration query/track to normal history. The user's running widget is not stopped.

Temporary frames remain at the path printed by the script until inspected and removed. `Recorder.cs.txt` intentionally is not `.cs`, so the main application's recursive build does not include it. Capture uses 420 frames at 15 fps on a 1120×960 transparent canvas. Long search/buffering waits pause frame export until the real result/playback condition succeeds; PNG encoding also adds wall time. Read `assets/capture-provenance.json` and `assets/capture-frames.json` for actual timing, result count, video ID, and player positions. No signed audio URLs are retained.

## Provenance & inspection

- [BRIEF.md](BRIEF.md): scope, audience, and output decisions.
- [frame.md](frame.md): stage, composition, and actual interaction timing.
- [ASSET-LICENSES.md](ASSET-LICENSES.md): original audio and third-party font/GSAP terms.
- `assets/events.json`: actual controls, result selection, and annotation coordinates.
- `assets/capture-provenance.json`, `assets/capture-frames.json`: live search/playback evidence and timing.
- `tools/compose.py`: English copy, deterministic paused timelines, and licensed audio envelopes.
- Final checks: runtime/layout/contrast audit, rendered-frame review, full FFmpeg decode, codec/dimension/duration checks, audio mute/fades, and GIF frame/loop checks. Review each platform's upload preview before posting.
