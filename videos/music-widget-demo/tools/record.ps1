param(
    [string]$Assembly = "$env:LOCALAPPDATA\Programs\MusicWidget\MusicWidget.dll",
    [string]$YtDlpDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path $Assembly)) { throw "Build/install Music Widget first or pass -Assembly <MusicWidget.dll>." }
$work = Join-Path ([IO.Path]::GetTempPath()) ('musicwidget-demo-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Recorder.cs.txt') (Join-Path $work 'Program.cs')
$escapedAssembly = [Security.SecurityElement]::Escape((Resolve-Path $Assembly).Path)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows10.0.19041.0</TargetFramework><UseWPF>true</UseWPF><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><Reference Include="MusicWidget"><HintPath>$escapedAssembly</HintPath></Reference></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $work 'Recorder.csproj')
$originalPath = $env:PATH
try {
    if ($YtDlpDirectory) {
        if (-not (Test-Path (Join-Path $YtDlpDirectory 'yt-dlp.exe'))) { throw 'The supplied extractor directory has no yt-dlp.exe.' }
        $env:PATH = "$YtDlpDirectory;$originalPath"
    }
    & dotnet run --project (Join-Path $work 'Recorder.csproj') -c Release -- (Join-Path $work 'frames')
    if ($LASTEXITCODE -ne 0) { throw "Recorder failed; workspace retained: $work" }
} finally {
    $env:PATH = $originalPath
}
$frames = Join-Path $work 'frames'
& ffmpeg -hide_banner -loglevel warning -y -framerate 15 -i (Join-Path $frames '%05d.png') -c:v libvpx-vp9 -lossless 1 -pix_fmt yuva420p -auto-alt-ref 0 -an (Join-Path $root 'assets\widget-source.webm')
if ($LASTEXITCODE -ne 0) { throw "Encoding failed; workspace retained: $work" }
Copy-Item (Join-Path $frames '00320.png') (Join-Path $root 'assets\widget-final.png') -Force
Copy-Item (Join-Path $frames 'events.json') (Join-Path $root 'assets\events.json') -Force
Copy-Item (Join-Path $frames 'provenance.json') (Join-Path $root 'assets\capture-provenance.json') -Force
Copy-Item (Join-Path $frames 'frames.json') (Join-Path $root 'assets\capture-frames.json') -Force
Write-Host "Capture complete. Temporary frames remain in $work; remove them after verifying the output."
