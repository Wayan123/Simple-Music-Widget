$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase

$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'bin\Release\net8.0-windows10.0.19041.0\MusicWidget.exe'
if (!(Test-Path $exe)) {
    throw "MusicWidget.exe not found at $exe. Run dotnet build -c Release first."
}

function Get-MusicWidgetWindow([int]$processId, [int]$timeoutMs = 8000) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $processId)
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    do {
        $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($win -ne $null) { return $win }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Find-ByAutomationId($root, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-Button($window, [string]$id) {
    $button = Find-ByAutomationId $window $id
    if ($button -eq $null) { throw "Button '$id' was not found" }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Get-Width($window) {
    return $window.Current.BoundingRectangle.Width
}

Get-Process MusicWidget -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$p = $null
try {
    $p = Start-Process $exe -PassThru
    $win = Get-MusicWidgetWindow $p.Id
    if ($win -eq $null) { throw 'Main window did not appear on manual launch' }

    foreach ($id in @('HideBtn', 'MinimizeBtn', 'MaximizeBtn')) {
        if ((Find-ByAutomationId $win $id) -eq $null) { throw "Expected button missing: $id" }
    }

    $compactWidth = Get-Width $win
    Invoke-Button $win 'MaximizeBtn'
    Start-Sleep -Milliseconds 800
    $win = Get-MusicWidgetWindow $p.Id
    if ($win -eq $null) { throw 'Window disappeared after maximize toggle' }
    $expandedWidth = Get-Width $win
    if ($expandedWidth -lt ($compactWidth + 120)) {
        throw "Maximize did not expand width enough. Compact=$compactWidth Expanded=$expandedWidth"
    }

    Invoke-Button $win 'MaximizeBtn'
    Start-Sleep -Milliseconds 800
    $win = Get-MusicWidgetWindow $p.Id
    if ($win -eq $null) { throw 'Window disappeared after restore toggle' }
    $restoredWidth = Get-Width $win
    if ([Math]::Abs($restoredWidth - $compactWidth) -gt 24) {
        throw "Restore did not return near compact width. Compact=$compactWidth Restored=$restoredWidth"
    }

    Invoke-Button $win 'MinimizeBtn'
    Start-Sleep -Milliseconds 800
    if ((Get-MusicWidgetWindow $p.Id 1200) -ne $null) { throw 'Minimize button should hide/minimize the visible widget' }

    # Second launch should summon/unhide the existing instance.
    $summon = Start-Process $exe -PassThru
    $summon.WaitForExit(5000) | Out-Null
    $win = Get-MusicWidgetWindow $p.Id
    if ($win -eq $null) { throw 'Second launch did not restore minimized widget' }

    Invoke-Button $win 'HideBtn'
    Start-Sleep -Milliseconds 800
    if ((Get-MusicWidgetWindow $p.Id 1200) -ne $null) { throw 'Hide button should hide the visible widget' }

    $summon = Start-Process $exe -PassThru
    $summon.WaitForExit(5000) | Out-Null
    $win = Get-MusicWidgetWindow $p.Id
    if ($win -eq $null) { throw 'Second launch did not unhide manually hidden widget' }

    Write-Host 'PASS regression-window-controls'
}
finally {
    Get-Process MusicWidget -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process ffmpeg -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
