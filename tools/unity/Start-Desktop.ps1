#requires -Version 7.0
param(
    [string]$Player,
    [string]$Python,
    [string]$RuntimeDirectory,
    [string]$UserDataDirectory,
    [string]$EvidenceDirectory,
    [int]$TestSeconds = 0,
    [ValidateSet('fixture','fault','late-audio','generation')][string]$TestMode = 'fixture',
    [string]$ExpectedError,
    [int]$Width = 1280,
    [int]$Height = 800,
    [ValidateSet('normal','tts_failure','partial_stream','seq_duplicate','seq_gap','wrong_ids','late_audio','slow_generation','slow_download','corrupt_audio','truncated_audio','expired_audio','budget_exceeded','provider_timeout')]
    [string]$Scenario = 'normal',
    [switch]$NoScreenshots
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$packageRoot = $PSScriptRoot
$isBundle = Test-Path -LiteralPath (Join-Path $packageRoot 'NeuroSaki.exe')
if ($isBundle) {
    if (-not $Player) { $Player = Join-Path $packageRoot 'NeuroSaki.exe' }
    if (-not $Python) { $Python = Join-Path $packageRoot 'python/python.exe' }
    $apiDirectory = Join-Path $packageRoot 'backend'
} else {
    $repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
    if (-not $Python) { $Python = Join-Path $repository '.venv/Scripts/python.exe' }
    $apiDirectory = Join-Path $repository 'services/api'
    if (-not $Player) {
        $latestPath = Join-Path $repository '.bootstrap/desktop/latest-build.json'
        if (-not (Test-Path -LiteralPath $latestPath)) { throw 'Build the desktop preview first, or pass -Player with the full NeuroSaki.exe path.' }
        $Player = (Get-Content -LiteralPath $latestPath -Raw | ConvertFrom-Json).player
    }
}
if (-not (Test-Path -LiteralPath $Player -PathType Leaf)) { throw 'NeuroSaki.exe is missing. Keep the complete Player folder together.' }
if (-not (Test-Path -LiteralPath $Python -PathType Leaf)) { throw 'Python is missing. Follow the desktop setup instructions.' }
if (-not $RuntimeDirectory) { $RuntimeDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'NeuroSaki/preview-runtime' }
New-Item -ItemType Directory -Force -Path $RuntimeDirectory | Out-Null
$RuntimeDirectory = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$launcherLock = $null; $backendProcess = $null; $playerProcess = $null; $configPath = $null
try {
    # A separate launcher cannot rotate the token under an already-running Player.
    $launcherLock = [IO.File]::Open((Join-Path $RuntimeDirectory 'launcher.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $probe = [Net.Sockets.TcpClient]::new()
    try {
        $connect = $probe.ConnectAsync('127.0.0.1',8000)
        try { if ($connect.Wait(300) -and $probe.Connected) { throw 'PORT_IN_USE' } } catch {
            if ($_.ToString().Contains('PORT_IN_USE')) { throw 'Port 8000 is already in use. Stop its owning service before starting this preview; no existing process was stopped.' }
        }
    } finally { $probe.Dispose() }
    $bytes = [byte[]]::new(32)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $token = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
    $configPath = Join-Path $RuntimeDirectory 'config.json'
    @{ protocol='unity-preview/1'; base_url='http://127.0.0.1:8000'; token=$token } | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding utf8
    if ($IsWindows) {
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl = [Security.AccessControl.FileSecurity]::new()
        $acl.SetOwner($sid)
        $acl.SetAccessRuleProtection($true,$false)
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','Allow'))
        Set-Acl -LiteralPath $configPath -AclObject $acl
    }
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Python; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.WorkingDirectory = $apiDirectory
    $info.Environment['PYTHONPATH'] = $apiDirectory
    $info.Environment['PYTHONDONTWRITEBYTECODE'] = '1'
    $info.Environment['U01_PREVIEW_CONFIG'] = $configPath
    $info.Environment['U01_PREVIEW_MODE'] = 'fixture'
    $info.Environment['U01_PREVIEW_SCENARIO'] = $Scenario
    $info.ArgumentList.Add('-m'); $info.ArgumentList.Add('app.unity_preview')
    $backendProcess = [Diagnostics.Process]::Start($info)
    $ready = $false
    for ($attempt=0; $attempt -lt 60; $attempt++) {
        if ($backendProcess.HasExited) { throw 'The local preview service stopped during startup.' }
        try {
            $capabilities = Invoke-RestMethod 'http://127.0.0.1:8000/preview/unity/v1/capabilities' -Headers @{Authorization="Bearer $token"} -TimeoutSec 1
            if ($capabilities.protocol -eq 'unity-preview/1') { $ready=$true; break }
        } catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $ready) { throw 'The local preview service did not become ready in time.' }
    $launch = [Diagnostics.ProcessStartInfo]::new()
    $launch.FileName = [IO.Path]::GetFullPath($Player); $launch.UseShellExecute=$false
    $launch.Environment['U01_PREVIEW_CONFIG']=$configPath
    foreach ($item in @('-screen-fullscreen','0','-screen-width',"$Width",'-screen-height',"$Height")) { $launch.ArgumentList.Add($item) }
    if ($UserDataDirectory) { $launch.ArgumentList.Add('-userDataPath'); $launch.ArgumentList.Add([IO.Path]::GetFullPath($UserDataDirectory)) }
    if ($EvidenceDirectory) {
        New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
        $launch.ArgumentList.Add('-evidenceDirectory'); $launch.ArgumentList.Add([IO.Path]::GetFullPath($EvidenceDirectory))
        $launch.ArgumentList.Add('-logFile'); $launch.ArgumentList.Add((Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) 'player.log'))
    }
    if ($TestSeconds -gt 0) {
        if (-not $EvidenceDirectory -or -not $UserDataDirectory) { throw 'Tests require separate evidence and user-data directories.' }
        foreach ($item in @('-desktopTest',$TestMode,'-smokeSeconds',"$TestSeconds")) { $launch.ArgumentList.Add($item) }
        if ($ExpectedError) { $launch.ArgumentList.Add('-expectedError'); $launch.ArgumentList.Add($ExpectedError) }
    }
    if ($NoScreenshots) { $launch.ArgumentList.Add('-noScreenshots'); $launch.ArgumentList.Add('true') }
    Write-Output 'Desktop preview ready. Replies and sound are labelled demonstrations. Closing the Player stops this local service.'
    $playerProcess = [Diagnostics.Process]::Start($launch)
    if ($TestSeconds -gt 0) {
        if (-not $playerProcess.WaitForExit(($TestSeconds + 45) * 1000)) { throw 'The Player exceeded the external test deadline.' }
        if ($playerProcess.ExitCode -ne 0) { throw "Player checks failed (exit $($playerProcess.ExitCode)); inspect the evidence directory." }
    } else { $playerProcess.WaitForExit() }
} finally {
    foreach ($ownedProcess in @($playerProcess,$backendProcess)) {
        if ($null -ne $ownedProcess) {
            if (-not $ownedProcess.HasExited) { $ownedProcess.Kill(); $ownedProcess.WaitForExit(5000) | Out-Null }
            $ownedProcess.Dispose()
        }
    }
    if ($null -ne $launcherLock) {
        if ($configPath -and (Test-Path -LiteralPath $configPath)) { Remove-Item -LiteralPath $configPath }
        $launcherLock.Dispose()
    }
}
