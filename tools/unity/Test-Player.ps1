#requires -Version 7.0
param(
    [Parameter(Mandatory=$true)][string]$Player,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [ValidateRange(15,1800)][int]$Seconds = 600
)
$ErrorActionPreference = 'Stop'
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
$startedAt = [DateTime]::UtcNow.ToString('o')
$info = [Diagnostics.ProcessStartInfo]::new()
$info.FileName = $executable
$info.UseShellExecute = $false
# The Player is the interactive artifact under test; it needs an actual visible window.
foreach ($argument in @('-screen-fullscreen','0','-screen-width','1280','-screen-height','800',
    '-evidenceDirectory',$evidence,'-smokeSeconds',"$Seconds",'-logFile',(Join-Path $evidence 'player.log'))) {
    $info.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::Start($info)
$process.WaitForExit()
$runtime = [ordered]@{
    player = $executable; playerSha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    startedAtUtc = $startedAt; endedAtUtc = [DateTime]::UtcNow.ToString('o')
    requestedSeconds = $Seconds; processId = $process.Id; exitCode = $process.ExitCode
    files = $files
}
$runtime | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'runtime-manifest.json') -Encoding utf8
if ($process.ExitCode -ne 0) { throw "Player failed with exit code $($process.ExitCode)." }
$result = Get-Content -LiteralPath (Join-Path $evidence 'player-result.json') -Raw | ConvertFrom-Json
if ($result.errors -ne 0 -or $result.unsupportedMaterials -ne 0 -or $result.maskedDrawables -le 0 -or
    $result.elapsedSeconds -lt ($Seconds - 1)) { throw 'Runtime probe failed or ended early; inspect evidence.' }
foreach ($name in @('player-03s.png','player-10s.png','frame-times.csv')) {
    if (-not (Test-Path -LiteralPath (Join-Path $evidence $name))) { throw "Missing runtime evidence: $name" }
}
Get-ChildItem -LiteralPath $evidence -File | Get-FileHash -Algorithm SHA256 |
    Select-Object @{Name='file';Expression={Split-Path $_.Path -Leaf}},Hash |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'evidence-hashes.json') -Encoding utf8
Write-Output "Runtime probe complete: $evidence. Visually inspect both captured Player images for texture, clipping and sorting; these counters alone do not establish visual correctness."
