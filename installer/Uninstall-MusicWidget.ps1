$ErrorActionPreference = 'SilentlyContinue'

$appDir = Join-Path $env:LOCALAPPDATA 'Programs\MusicWidget'
$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Music Widget.lnk'
$startupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Music Widget.lnk'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MusicWidget'

Get-Process MusicWidget -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -Force $startMenuShortcut
Remove-Item -Force $startupShortcut
Remove-Item -Recurse -Force $uninstallKey

# Delete the install directory after this script exits so PowerShell is not executing
# from the folder it is trying to remove.
$deleteCmd = "timeout /t 1 /nobreak >nul & rmdir /s /q `"$appDir`""
Start-Process -WindowStyle Hidden -FilePath cmd.exe -ArgumentList "/c $deleteCmd"
