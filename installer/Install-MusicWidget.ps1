$ErrorActionPreference = 'Stop'

$appName = 'Music Widget'
$appExe = 'MusicWidget.exe'
$appDir = Join-Path $env:LOCALAPPDATA 'Programs\MusicWidget'
$startMenuDir = [Environment]::GetFolderPath('Programs')
$startupDir = [Environment]::GetFolderPath('Startup')
$sourceDir = $PSScriptRoot

if (-not (Test-Path (Join-Path $sourceDir $appExe))) {
  throw "Installer payload is missing $appExe"
}

Get-Process MusicWidget -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

New-Item -ItemType Directory -Force -Path $appDir | Out-Null
Get-ChildItem -Path $sourceDir -Force |
  Where-Object { $_.Name -notin @('Install-MusicWidget.ps1', 'Uninstall-MusicWidget.ps1') } |
  Copy-Item -Destination $appDir -Recurse -Force

$ws = New-Object -ComObject WScript.Shell
$startMenuShortcut = Join-Path $startMenuDir 'Music Widget.lnk'
$s = $ws.CreateShortcut($startMenuShortcut)
$s.TargetPath = Join-Path $appDir $appExe
$s.WorkingDirectory = $appDir
$s.IconLocation = "$(Join-Path $appDir $appExe),0"
$s.Description = 'Music Widget - now playing overlay'
$s.Save()

# Preserve existing Startup behavior if the previous dev installer already enabled it,
# but do not force autostart from this no-UI fallback installer.
$startupShortcut = Join-Path $startupDir 'Music Widget.lnk'
if (Test-Path $startupShortcut) {
  $s2 = $ws.CreateShortcut($startupShortcut)
  $s2.TargetPath = Join-Path $appDir $appExe
  $s2.Arguments = '--autostart'
  $s2.WorkingDirectory = $appDir
  $s2.IconLocation = "$(Join-Path $appDir $appExe),0"
  $s2.Description = 'Music Widget - now playing overlay'
  $s2.Save()
}

$uninstallScript = Join-Path $appDir 'Uninstall-MusicWidget.ps1'
Copy-Item -Path (Join-Path $PSScriptRoot 'Uninstall-MusicWidget.ps1') -Destination $uninstallScript -Force

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MusicWidget'
New-Item -Force -Path $uninstallKey | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value $appName -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value (Join-Path $appDir $appExe) -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $appDir -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name Publisher -Value 'Wayan123' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name URLInfoAbout -Value 'https://github.com/Wayan123/Simple-Music-Widget' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`"" -PropertyType String -Force | Out-Null

Start-Process (Join-Path $appDir $appExe)
