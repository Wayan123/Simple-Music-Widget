# Contributing

Thanks for helping improve Music Widget.

## Development setup

Requirements:

- Windows 10/11
- .NET 8 SDK
- Optional for YouTube playback: `yt-dlp` and `ffmpeg`

```powershell
dotnet restore MusicWidget.csproj
dotnet build MusicWidget.csproj -v:minimal
dotnet run -c Debug
```

## Pull request checklist

Before opening a PR:

- run `dotnet build MusicWidget.csproj -v:minimal`
- keep UI changes compact and lightweight
- update README/screenshots when user-facing behavior changes
- update `<Version>` in `MusicWidget.csproj` for release-facing changes
- verify installer-related changes with `scripts/build-installer.ps1` when possible

## Release artifacts

Use:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1 -SelfContained
```

This creates a portable ZIP, installer EXE, and SHA256 checksum file in `dist/`.
