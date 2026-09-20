#requires -Version 7.0
param(
    [Parameter(Mandatory=$true)][string]$Player,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [ValidateRange(15,1800)][int]$Seconds = 600,
    [ValidateRange(1,300)][int]$GraceSeconds = 30
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$executable = (Resolve-Path -LiteralPath $Player).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if ((Test-Path -LiteralPath $evidence) -and (Get-ChildItem -LiteralPath $evidence -Force | Select-Object -First 1)) {
    throw 'Use a new evidence directory so old screenshots cannot masquerade as this run.'
}
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$playerFolder = Split-Path $executable -Parent
$files = @(Get-ChildItem -LiteralPath $playerFolder -Recurse -File | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($playerFolder, $_.FullName).Replace('\','/')
        bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$playerHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
$timeoutSeconds = $Seconds + $GraceSeconds
$runtime = [ordered]@{
    player = $executable; playerSha256 = $playerHash
    startedAtUtc = $null; endedAtUtc = $null; elapsedWallSeconds = 0
    requestedSeconds = $Seconds; graceSeconds = $GraceSeconds; timeoutSeconds = $timeoutSeconds
    processId = $null; exitCode = $null; status = 'starting'; timedOut = $false
    terminationAttempted = $false; processExited = $false; terminationError = $null
    failureReason = $null; files = $files
}

function Save-RuntimeEvidence {
    $runtime | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $evidence 'runtime-manifest.json') -Encoding utf8
    # Hash logs and partial results on failure too; never replace or fabricate Player output.
    @(Get-ChildItem -LiteralPath $evidence -File | Where-Object Name -ne 'evidence-hashes.json' |
        Get-FileHash -Algorithm SHA256 |
        Select-Object @{Name='file';Expression={Split-Path $_.Path -Leaf}},Hash) |
        ConvertTo-Json -AsArray |
        Set-Content -LiteralPath (Join-Path $evidence 'evidence-hashes.json') -Encoding utf8
}

$info = [Diagnostics.ProcessStartInfo]::new()
$info.FileName = $executable
$info.UseShellExecute = $false
# The Player is the interactive artifact under test; it needs an actual visible window.
foreach ($argument in @('-screen-fullscreen','0','-screen-width','1280','-screen-height','800',
    '-evidenceDirectory',$evidence,'-smokeSeconds',"$Seconds",'-logFile',(Join-Path $evidence 'player.log'))) {
    $info.ArgumentList.Add($argument)
}
$process = $null
$watch = [Diagnostics.Stopwatch]::new()
$scriptExitCode = 1
try {
    $runtime.startedAtUtc = [DateTime]::UtcNow.ToString('o')
    $watch.Start()
    $process = [Diagnostics.Process]::Start($info)
    $runtime.processId = $process.Id
    $runtime.status = 'running'
    # This wait uses the host clock, independent of Unity Update/timeScale or Player arguments.
    $remainingMs = [Math]::Max(0, ($timeoutSeconds * 1000) - [int]$watch.ElapsedMilliseconds)
    if (-not $process.WaitForExit($remainingMs)) {
        $runtime.status = 'timeout'
        $runtime.timedOut = $true
        $runtime.failureReason = "Player exceeded external deadline: $Seconds seconds + $GraceSeconds seconds grace."
        $runtime.terminationAttempted = $true
        try {
            # Kill this Process object only; never enumerate names or kill other Unity processes/trees.
            $process.Kill()
        } catch {
            # A natural exit can race the timeout; it is still a timed-out test.
            if (-not $process.HasExited) { $runtime.terminationError = $_.Exception.Message }
        }
        $runtime.processExited = $process.WaitForExit(5000)
        if (-not $runtime.processExited -and -not $runtime.terminationError) {
            $runtime.terminationError = 'The launched process did not exit within 5 seconds after termination.'
        }
        $scriptExitCode = 124
    } else {
        $runtime.processExited = $true
        $runtime.exitCode = $process.ExitCode
        if ($process.ExitCode -ne 0) { throw "Player failed with exit code $($process.ExitCode)." }
        $result = Get-Content -LiteralPath (Join-Path $evidence 'player-result.json') -Raw | ConvertFrom-Json
        if ($result.errors -ne 0 -or $result.unsupportedMaterials -ne 0 -or $result.maskedDrawables -le 0 -or
            $result.elapsedSeconds -lt ($Seconds - 1)) { throw 'Runtime probe failed or ended early; inspect evidence.' }
        foreach ($name in @('player-03s.png','player-10s.png','frame-times.csv')) {
            if (-not (Test-Path -LiteralPath (Join-Path $evidence $name))) { throw "Missing runtime evidence: $name" }
        }
        $runtime.status = 'passed'
        $scriptExitCode = 0
    }
} catch {
    if (-not $runtime.timedOut) { $runtime.status = 'failed' }
    $runtime.failureReason = $_.Exception.Message
    # Also clean up this owned process if process supervision itself throws unexpectedly.
    if ($process -and -not $process.HasExited) {
        $runtime.terminationAttempted = $true
        try {
            $process.Kill()
            $runtime.processExited = $process.WaitForExit(5000)
            if (-not $runtime.processExited) { $runtime.terminationError = 'Failure cleanup timed out after 5 seconds.' }
        } catch {
            if (-not $process.HasExited) { $runtime.terminationError = $_.Exception.Message }
        }
    }
} finally {
    $watch.Stop()
    $runtime.elapsedWallSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
    $runtime.endedAtUtc = [DateTime]::UtcNow.ToString('o')
    if ($process) {
        if ($process.HasExited) { $runtime.exitCode = $process.ExitCode; $runtime.processExited = $true }
        $process.Dispose()
    }
    Save-RuntimeEvidence
}
if ($scriptExitCode -ne 0) {
    [Console]::Error.WriteLine("Player test $($runtime.status): $($runtime.failureReason) Evidence: $evidence")
    exit $scriptExitCode
}
Write-Output "Runtime probe complete: $evidence. Visually inspect both captured Player images for texture, clipping and sorting; these counters alone do not establish visual correctness."
