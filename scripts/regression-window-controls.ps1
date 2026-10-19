param(
    [ValidateSet('All', 'Window', 'Presentation')]
    [string]$Suite = 'All',
    [string]$SnapshotDirectory,
    [switch]$IncludePlayback
)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'The WPF regression suite requires Windows and the .NET 8 SDK.'
}

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'tests\MusicWidget.Regression.csproj'
$arguments = @('run', '--project', $project, '-c', 'Release', '--', '--suite', $Suite)
if ($SnapshotDirectory) {
    $arguments += @('--snapshots', $SnapshotDirectory)
}
if ($IncludePlayback) { $arguments += '--playback' }

# The default runner owns its WPF windows without App.OnStartup, SMTC, or yt-dlp.
# IncludePlayback adds a disposable SMTC player with synthetic silent WAV files.
# Neither mode signals the personal singleton, persists history, or kills processes.
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Music Widget regression suite failed (exit $LASTEXITCODE)." }
