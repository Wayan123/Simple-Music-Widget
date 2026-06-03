# Music Widget - installer ringan (tanpa admin)
# - Publish exe self-contained-free (framework-dependent, kecil)
# - Pasang shortcut di Startup (auto-run saat Windows menyala)
# - Pasang shortcut di Start Menu (mudah dicari / pin ke taskbar)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# Update komponen YouTube (best-effort) agar fitur tidak mati karena versi usang
try {
  Write-Host "Updating yt-dlp / ffmpeg (best-effort)..."
  winget upgrade --id yt-dlp.yt-dlp -e --accept-source-agreements --accept-package-agreements 2>$null | Out-Null
} catch { }

Write-Host "Stopping old Music Widget process (if running)..."
Get-Process MusicWidget -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "Building (Release publish)..."
dotnet publish -c Release -o "$root\publish" | Out-Null
$exe = Join-Path $root 'publish\MusicWidget.exe'
if (-not (Test-Path $exe)) { throw "Publish gagal: $exe tidak ditemukan" }

$ws = New-Object -ComObject WScript.Shell

# 1) Startup shortcut -> auto-run saat boot
$startup = [Environment]::GetFolderPath('Startup')
$lnk1 = Join-Path $startup 'Music Widget.lnk'
$s1 = $ws.CreateShortcut($lnk1)
$s1.TargetPath = $exe
$s1.Arguments = '--autostart'
$s1.WorkingDirectory = (Split-Path $exe)
$s1.IconLocation = "$exe,0"
$s1.Description = 'Music Widget - now playing overlay'
$s1.Save()
Write-Host "Startup shortcut : $lnk1"

# 2) Start Menu shortcut -> mudah dijalankan / pin ke taskbar
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Music Widget.lnk'
$s2 = $ws.CreateShortcut($startMenu)
$s2.TargetPath = $exe
$s2.WorkingDirectory = (Split-Path $exe)
$s2.IconLocation = "$exe,0"
$s2.Description = 'Music Widget - now playing overlay'
$s2.Save()
Write-Host "Start Menu       : $startMenu"

# Jalankan sekarang juga
Start-Process $exe
Write-Host ""
Write-Host "Selesai. Widget berjalan di background (ikon di system tray)."
Write-Host "Auto-muncul saat ada musik diputar; klik ikon tray untuk menampilkan/cari."
Write-Host "Pin ke taskbar: cari 'Music Widget' di Start Menu, klik kanan -> Pin to taskbar."
