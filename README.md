# Music Widget (Windows)

Overlay "now playing" mini ala kontrol media taskbar Windows. **Satu widget untuk semua sumber**: apa pun yang terintegrasi dengan System Media Transport Controls (SMTC) Windows — YouTube di Chrome/Edge/Firefox, Spotify, Groove, dll — otomatis tampil dan bisa dikontrol.

![contoh](contoh tidak disertakan)

## Kenapa "1 widget untuk semua"

Widget ini **tidak** bicara langsung ke YouTube/Spotify. Ia membaca **SMTC** (`Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`) — lapisan terpusat Windows yang menyatukan semua pemutar. Selama aplikasi melaporkan media ke Windows (browser modern & Spotify melakukannya), widget ini menampilkannya. Itulah sebabnya satu kode mendukung semua sumber tanpa API per-layanan.

## Fitur

- Judul, artis, nama sumber (Chrome/Edge/Spotify/…), dan artwork album.
- Tombol Previous / Play-Pause / Next yang benar-benar mengontrol pemutar aktif.
- Progress bar yang bergerak (poll 500 ms + event SMTC).
- **Tombol Close (✕)** untuk menutup widget.
- **Buka file lokal (📄)**: pilih file audio (mp3/flac/wav/m4a/aac/ogg/wma) → diputar via MediaPlayer dan didaftarkan ke SMTC sehingga ikut muncul/terkontrol di widget.
- **Cari + putar YouTube (🔍)** tanpa browser: cari lagu, klik hasil, audio diputar langsung di widget.
- Overlay frameless, always-on-top, transparan, bisa digeser (drag), nempel di kanan-bawah dekat tray.

## Fitur YouTube — cara kerja & prasyarat

Widget memanggil **yt-dlp** untuk mencari (judul + ID) dan mengambil URL stream audio. Karena WPF `MediaPlayer` tidak bisa membuka URL stream googlevideo secara langsung, **ffmpeg** menjembatani: ia membaca stream dan mentranscode ke file MP3 lokal sementara yang diputar `MediaPlayer`. Tidak ada jendela browser, tidak ada download manual.

Prasyarat (install sekali via winget):

```powershell
winget install --id yt-dlp.yt-dlp -e
winget install --id Gyan.FFmpeg -e
```

Widget mencari `yt-dlp.exe`/`ffmpeg.exe` di PATH, lalu fallback ke folder paket winget.

> Catatan jujur: memutar audio YouTube di luar browser melanggar ToS YouTube dan rapuh terhadap perubahan YouTube/yt-dlp. Gunakan dengan kesadaran itu.

## Auto-update

- **yt-dlp** di-update otomatis (best-effort) saat widget start (`yt-dlp -U`), dan saat menjalankan `install.ps1` (`winget upgrade`). Ini menjaga fitur YouTube tetap jalan karena yt-dlp sering perlu update saat YouTube berubah.
- Update **aplikasi widget sendiri** belum diaktifkan (butuh sumber rilis seperti GitHub Releases). Untuk update kode, cukup jalankan `install.ps1` lagi setelah perubahan.

## Prasyarat build

Mesin ini punya **.NET 8 Desktop runtime** (bisa menjalankan), tapi **belum ada .NET SDK** (perlu untuk build). Install salah satu:

1. **.NET 8 SDK** (rekomendasi, ringan): https://dotnet.microsoft.com/download/dotnet/8.0
   — pilih "SDK x64". Setelah itu `dotnet` tersedia di PowerShell.
2. Atau via Visual Studio Installer → workload **".NET desktop development"**.

TFM `net8.0-windows10.0.19041.0` otomatis menarik proyeksi WinRT (`Windows.Media.Control`) saat build — **tanpa** paket NuGet tambahan.

## Build & jalankan

Buka **PowerShell** (bukan WSL) di folder ini:

```powershell
cd C:\Users\wayandadang\AI\music-widget
dotnet run -c Release
```

### Instalasi sekali klik (auto-run + akses mudah)

Untuk pemakaian sehari-hari (tanpa CMD/PowerShell berulang), jalankan installer:

```powershell
cd C:\Users\wayandadang\AI\music-widget
powershell -ExecutionPolicy Bypass -File install.ps1
```

Installer akan:
- Publish `publish\MusicWidget.exe` (ringan, framework-dependent).
- Pasang **shortcut Startup** → widget otomatis jalan di background tiap Windows menyala.
- Pasang **shortcut Start Menu** → cari "Music Widget", klik kanan → *Pin to taskbar* untuk akses cepat.
- Menjalankan widget langsung.

### Perilaku auto-detect

- Widget berjalan **di background** dengan ikon di **system tray**.
- **Auto-muncul** saat ada musik diputar (Windows Media Player, VLC, YouTube/Spotify di browser, dll — apa pun yang lapor ke SMTC).
- **Auto-sembunyi** saat musik berhenti/ditutup.
- Tombol **✕** menyembunyikan widget (tidak keluar) — akan muncul lagi saat ada musik.
- **Klik ikon tray** untuk menampilkan widget kapan saja (mis. untuk search YouTube saat idle); menu tray punya **Keluar** untuk benar-benar menutup.

## Cara pakai

1. Putar lagu/video di YouTube (browser), Spotify, dll.
2. Widget muncul di kanan-bawah dan ikut sumber yang sedang aktif.
3. Geser widget dengan drag. Tombol kontrol mengontrol pemutar yang sedang aktif.

## Catatan teknis

- Tidak butuh hak admin atau capability khusus untuk *desktop app* unpackaged (capability `globalMediaControl` hanya relevan untuk app UWP/packaged).
- Jika tidak ada yang diputar, widget menampilkan "Tidak ada yang diputar".
- Beberapa browser hanya melapor SMTC untuk tab dengan media aktif; pastikan tab tersebut benar memutar.

## Struktur

| File | Peran |
|------|-------|
| `MediaService.cs` | Wrapper SMTC: baca sesi aktif, snapshot, kontrol play/next/prev |
| `LocalPlayer.cs` | MediaPlayer untuk file lokal & stream YouTube (via ffmpeg) + integrasi SMTC |
| `YouTubeService.cs` | Search & resolve URL audio via yt-dlp; resolve path yt-dlp/ffmpeg |
| `MainWindow.xaml(.cs)` | UI overlay + tombol close/open/search + panel hasil + posisi tray + drag + auto show/hide |
| `TrayIcon.cs` | Ikon system tray: tampilkan widget / keluar |
| `install.ps1` | Publish + shortcut Startup (auto-run) + Start Menu (pin taskbar) + update yt-dlp |
| `icon.ico` / `make_icon.py` | Ikon futuristik 3D (gradient violet→cyan, equalizer neon) + generatornya |
| `MusicWidget.csproj` | TFM WinRT + WPF |
| `app.manifest` | Per-monitor DPI awareness |
