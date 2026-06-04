# Security Policy

## Supported versions

Security fixes are handled on the latest released version of Music Widget.

## Reporting a vulnerability

Please do **not** open a public issue for security-sensitive reports.

Instead, use GitHub private vulnerability reporting when available, or contact the maintainer through the repository owner profile.

When reporting, include:

- affected version
- Windows version
- steps to reproduce
- expected and actual behavior
- logs/screenshots if safe to share

## Distribution and trust

- Release installers are currently unsigned until a code-signing certificate is available.
- SHA256 checksum files are published with release artifacts.
- YouTube playback depends on user-installed `yt-dlp` and `ffmpeg`; install them from trusted package managers such as `winget`.
