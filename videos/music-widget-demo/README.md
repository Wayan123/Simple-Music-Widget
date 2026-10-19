# Music Widget — demo & social media

Actual Windows WPF footage with an original local demo track. Charcoal–emerald staging belongs to the video, not the application UI. Indonesian headlines and separate captions for each platform.

## Deliverables

| Destination | File | Canvas | Length |
|---|---|---|---|
| GitHub README | [`../../docs/music-widget-demo.gif`](../../docs/music-widget-demo.gif) | 960 × 540 | 18 s |
| Instagram Reels | `dist/social-media/music-widget-instagram-reels.mp4` | 1080 × 1920 | 24 s |
| Threads feed | `dist/social-media/music-widget-threads.mp4` | 1080 × 1350 | 20 s |
| TikTok | `dist/social-media/music-widget-tiktok.mp4` | 1080 × 1920 | 22 s |
| YouTube Shorts | `dist/social-media/music-widget-youtube-shorts.mp4` | 1080 × 1920 | 23 s |

MP4 paths are relative to the repository root. Generated posting files and the ZIP live in gitignored `dist/social-media/`; they are not release installers. The GIF and media source belong in Git. This repository does not automatically upload MP4s to any social platform.

Copy: [CAPTIONS.md](CAPTIONS.md), including a YouTube title/description. Files use different opening hooks and timing, not merely renamed exports. The caption URL targets `fix/window-lifecycle-resize`, the branch recorded here; update it after merging or publishing another build.

## What the demo proves

Real play/pause; show/change/hide volume; expand/compact; hide; restore through `MainWindow.SummonFromTray`; open/close the search panel. Ring highlights are editorial annotations of actual control invocations. There is no filmed tray click, fabricated search result, fake metadata integration, or personal desktop footage. The MP4 soundtrack is an original promotional music bed rather than live system audio; the GIF is silent.

## Rebuild locally (WSL/Linux)

Needs Python 3 + NumPy, FFmpeg with libvpx-vp9, and the installed HyperFrames suite wrapper (pinned CLI 0.8.81). Captured media, fonts, and local GSAP are included in `assets/`; rendering needs no model provider or cloud service.

```bash
cd videos/music-widget-demo
HF="$HOME/.agents/skills/hyperframes-suite/scripts/hf.sh"
python3 tools/compose.py
bash "$HF" check platforms/readme --snapshots
bash "$HF" render platforms/readme --format gif --fps 15 --quality delivery --workers 2 \
  --output ../../docs/music-widget-demo.gif
bash "$HF" check platforms/instagram --snapshots
bash "$HF" render platforms/instagram --fps 30 --quality delivery --workers 2 \
  --output ../../dist/social-media/music-widget-instagram-reels.mp4
```

Repeat the final two commands for `threads`, `tiktok`, and `youtube-shorts`, changing output names accordingly. `python3 tools/compose.py` creates self-contained platform folders and regenerates the root `index.html` as the Instagram preview. Edit the generator rather than diverging generated HTML. `npm run dev` opens the local Studio preview; it uses the installed WSL wrapper. Inspect the Studio project URL printed by the CLI, then stop the preview explicitly when finished.

To regenerate only the original instrumental:

```bash
python3 tools/generate-audio.py assets/demo-track.wav
```

## Capture again (optional, Windows)

Requires .NET 8 SDK, FFmpeg on Windows PATH, and a real built/installed `MusicWidget.dll` matching this branch. From Windows PowerShell:

```powershell
.\videos\music-widget-demo\tools\record.ps1
# Or pass -Assembly C:\path\to\MusicWidget.dll
```

The recorder opens its own real WPF window and plays the original demo WAV. It bypasses normal app startup to avoid personal history/background startup behavior; it does not stop the user's running widget. Keep the visible demo window unobstructed during recording. Temporary frames are retained at the path printed by the script until you inspect and remove them. `Recorder.cs.txt` intentionally has a non-`.cs` suffix so the main application's recursive C# build does not include it.

Captured actions occur at defined frame indices (480 frames at 30 fps). PNG encoding can take longer than the 16-second exported source duration on slower machines; this is a time-compressed control demonstration, not an input-latency benchmark.

## Provenance & inspection

- [BRIEF.md](BRIEF.md): scope, audience, and output decisions.
- [frame.md](frame.md): stage, composition, and actual interaction timing.
- [ASSET-LICENSES.md](ASSET-LICENSES.md): original audio and third-party font/GSAP terms.
- `assets/events.json`: actual invoked controls and annotation coordinates.
- `tools/compose.py`: deterministic, paused GSAP timelines with local assets.
- Final checks: HyperFrames runtime/layout/contrast audit, rendered-frame visual inspection, full FFmpeg decode, codec/dimension/duration checks, and GIF frame/loop checks. Platform upload previews must still be reviewed before posting because platform chrome can change.
