#requires -Version 7.0
param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe',
    [string]$OutputDirectory,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $repository 'apps/unity'
$version = '6000.3.11f1'
if (-not (Test-Path -LiteralPath $UnityEditor -PathType Leaf)) {
    throw "Unity $version Editor is missing at $UnityEditor. Install the official version with a valid license, then pass -UnityEditor. Unity 2022 is not a substitute."
}
$actual = (Get-Item -LiteralPath $UnityEditor).VersionInfo.ProductVersion
if (-not $actual.StartsWith($version)) {
    throw "Expected Unity $version; executable reports $actual. No project was opened."
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project "Builds/Windows-x64-$stamp" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) {
    throw "Output directory must be new or empty to avoid packaging stale binaries: $output"
}
$evidence = "$output-evidence"
New-Item -ItemType Directory -Path $output,$evidence -Force | Out-Null
$sourceSha = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit.' }

function Invoke-UnityStage([string]$Method, [string]$LogName, [string]$ExpectedMarker) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $UnityEditor
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.Environment['COMPANION_BUILD_OUTPUT'] = $output
    foreach ($item in @('-batchmode','-quit','-projectPath',$project,'-buildTarget','Win64',
        '-executeMethod',$Method,'-logFile',(Join-Path $evidence $LogName))) {
        $info.ArgumentList.Add($item)
    }
    $process = [Diagnostics.Process]::Start($info)
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "$Method exited $($process.ExitCode); inspect $evidence/$LogName" }
    if (-not (Select-String -LiteralPath (Join-Path $evidence $LogName) -SimpleMatch $ExpectedMarker -Quiet)) {
        throw "$Method returned without its success marker; inspect $evidence/$LogName"
    }
}

if ($PrepareOnly) {
    Invoke-UnityStage 'Companion.Foundation.Editor.FoundationBuild.Prepare' 'import-prepare.log' 'U01-00_PREPARE_SUCCEEDED'
    Write-Output "Prepared candidate scene. Review and commit generated settings and .meta files before building: $project"
    exit 0
}
Invoke-UnityStage 'Companion.Foundation.Editor.FoundationBuild.BuildWindows' 'build-windows.log' 'U01-00_BUILD_RESULT Succeeded'
$player = Join-Path $output 'AICompanion.Foundation.exe'
if (-not (Test-Path -LiteralPath $player)) { throw 'Unity reported success but the Player is missing.' }
Copy-Item -LiteralPath (Join-Path $project 'Assets/Live2D/Cubism/LICENSE.md') -Destination (Join-Path $output 'LIVE2D-LICENSE.md')
Copy-Item -LiteralPath (Join-Path $project 'Assets/ThirdParty/Fonts/OFL.txt') -Destination (Join-Path $output 'FONT-OFL.txt')
Copy-Item -LiteralPath (Join-Path $repository 'assets/manifest/unity-foundation-resources.md') -Destination (Join-Path $output 'RESOURCE-NOTICES.md')
$zip = "$output.zip"
Compress-Archive -LiteralPath $output -DestinationPath $zip
$dirty = [bool](& git -C $repository status --porcelain --untracked-files=normal)
$files = @(Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\','/')
        bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$receipt = [ordered]@{
    task = 'U01-00'; sourceSha = $sourceSha; sourceDirty = $dirty
    unity = $actual; architecture = 'x86_64'; backend = 'Mono'; graphicsApi = 'Direct3D11'
    zip = $zip; zipSha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    playerSha256 = (Get-FileHash -LiteralPath $player -Algorithm SHA256).Hash.ToLowerInvariant()
    builtAtUtc = [DateTime]::UtcNow.ToString('o'); playerStarted = $false
    files = $files
}
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'build-manifest.json') -Encoding utf8
Write-Output "Built $zip. Receipt: $evidence/build-manifest.json. Run Test-Player.ps1 for runtime evidence."
if ($dirty) { Write-Warning 'Source includes uncommitted files. Commit generated Unity assets/settings and rebuild before claiming an exact tested SHA.' }
