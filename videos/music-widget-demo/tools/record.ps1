param(
    [string]$Assembly = "$env:LOCALAPPDATA\Programs\MusicWidget\MusicWidget.dll"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path $Assembly)) { throw "Build/install Music Widget first or pass -Assembly <MusicWidget.dll>." }
$audio = Join-Path $root 'assets\demo-track.wav'
if (-not (Test-Path $audio)) { throw 'Run python3 tools/generate-audio.py assets/demo-track.wav first.' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('musicwidget-demo-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Recorder.cs.txt') (Join-Path $work 'Program.cs')
Copy-Item $audio (Join-Path $work 'Ruang Fokus.wav')
$escapedAssembly = [Security.SecurityElement]::Escape((Resolve-Path $Assembly).Path)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows10.0.19041.0</TargetFramework><UseWPF>true</UseWPF><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><Reference Include="MusicWidget"><HintPath>$escapedAssembly</HintPath></Reference></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $work 'Recorder.csproj')
& dotnet run --project (Join-Path $work 'Recorder.csproj') -c Release -- (Join-Path $work 'frames') (Join-Path $work 'Ruang Fokus.wav')
if ($LASTEXITCODE -ne 0) { throw "Recorder failed; workspace retained: $work" }
$frames = Join-Path $work 'frames'
& ffmpeg -hide_banner -loglevel warning -y -framerate 30 -i (Join-Path $frames '%05d.png') -c:v libvpx-vp9 -lossless 1 -pix_fmt yuva420p -auto-alt-ref 0 -an (Join-Path $root 'assets\widget-source.webm')
if ($LASTEXITCODE -ne 0) { throw "Encoding failed; workspace retained: $work" }
Copy-Item (Join-Path $frames '00479.png') (Join-Path $root 'assets\widget-final.png') -Force
Copy-Item (Join-Path $frames 'events.json') (Join-Path $root 'assets\events.json') -Force
Write-Host "Capture complete. Temporary frames remain in $work; remove them after verifying the output."
