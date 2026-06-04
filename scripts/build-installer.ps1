param(
  [string]$Configuration = 'Release',
  [string]$Runtime = 'win-x64',
  [switch]$SelfContained,
  [switch]$SkipInstaller,
  [switch]$PreferIExpress
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$project = Get-Content (Join-Path $root 'MusicWidget.csproj')
$version = $project.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) { $version = '0.0.0' }

$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts "publish\$Runtime"
$distDir = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $publishDir, $distDir | Out-Null

Write-Host "Publishing Music Widget $version ($Runtime)..."
$publishArgs = @(
  'publish', (Join-Path $root 'MusicWidget.csproj'),
  '-c', $Configuration,
  '-r', $Runtime,
  '-o', $publishDir,
  '-p:PublishSingleFile=false'
)
if ($SelfContained) { $publishArgs += '--self-contained'; $publishArgs += 'true' }
else { $publishArgs += '--self-contained'; $publishArgs += 'false' }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$zipPath = Join-Path $distDir "MusicWidget-portable-$version-$Runtime.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force
Write-Host "Portable ZIP: $zipPath"

if ($SkipInstaller) { return }

function Find-InnoSetup {
  $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  $candidates = @(
    "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
  )
  foreach ($candidate in $candidates) {
    if ($candidate -and (Test-Path $candidate)) { return $candidate }
  }
  return $null
}

function Build-InnoInstaller([string]$iscc) {
  Write-Host "Building Inno Setup installer..."
  $iss = Join-Path $root 'installer\MusicWidget.iss'
  & $iscc "/DMyAppVersion=$version" "/DSourceDir=$publishDir" "/DOutputDir=$distDir" $iss
  if ($LASTEXITCODE -ne 0) { throw 'Inno Setup build failed' }
}

function Build-IExpressInstaller {
  $iexpress = Get-Command iexpress.exe -ErrorAction SilentlyContinue
  if (-not $iexpress) { throw 'Neither Inno Setup (iscc.exe) nor IExpress is available. Install Inno Setup 6 and retry.' }

  Write-Host "Inno Setup not found; building fallback IExpress installer..."
  $stage = Join-Path $artifacts 'iexpress-staging'
  if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
  New-Item -ItemType Directory -Force -Path $stage | Out-Null
  Copy-Item -Path (Join-Path $publishDir '*') -Destination $stage -Recurse -Force
  Copy-Item -Path (Join-Path $root 'installer\Install-MusicWidget.ps1') -Destination $stage -Force
  Copy-Item -Path (Join-Path $root 'installer\Uninstall-MusicWidget.ps1') -Destination $stage -Force

  $target = Join-Path $distDir "MusicWidgetSetup-$version-$Runtime.exe"
  if (Test-Path $target) { Remove-Item -Force $target }
  $sed = Join-Path $artifacts 'MusicWidgetSetup.sed'
  $files = Get-ChildItem -Path $stage -File | Sort-Object Name
  $fileList = for ($i = 0; $i -lt $files.Count; $i++) { "%FILE$i%=$($files[$i].Name)" }
  $sourceEntries = for ($i = 0; $i -lt $files.Count; $i++) { "FILE$i=`"$($files[$i].Name)`"" }

  $sedContent = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=Music Widget has been installed.
TargetName=$target
FriendlyName=Music Widget Setup
AppLaunched=powershell.exe -NoProfile -ExecutionPolicy Bypass -File Install-MusicWidget.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=powershell.exe -NoProfile -ExecutionPolicy Bypass -File Install-MusicWidget.ps1
UserQuietInstCmd=powershell.exe -NoProfile -ExecutionPolicy Bypass -File Install-MusicWidget.ps1
SourceFiles=SourceFiles
[Strings]
$(($sourceEntries -join "`r`n"))
[SourceFiles]
SourceFiles0=$stage\
[SourceFiles0]
$(($fileList -join "`r`n"))
"@
  Set-Content -Path $sed -Value $sedContent -Encoding ASCII
  $proc = Start-Process -FilePath $iexpress.Source -ArgumentList @('/N', '/Q', $sed) -Wait -PassThru
  if ($proc.ExitCode -ne 0) { throw "IExpress build failed with exit code $($proc.ExitCode)" }
  for ($i = 0; $i -lt 180 -and -not (Test-Path $target); $i++) { Start-Sleep -Milliseconds 500 }
  if (-not (Test-Path $target)) {
    $created = Get-ChildItem -Path $distDir -Filter "MusicWidgetSetup-$version-$Runtime.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $created) { throw "IExpress did not create $target" }
    $target = $created.FullName
  }
  Write-Host "Fallback installer EXE: $target"
}

$iscc = Find-InnoSetup
if ($iscc -and -not $PreferIExpress) { Build-InnoInstaller $iscc }
else { Build-IExpressInstaller }

Write-Host "Artifacts written to: $distDir"
