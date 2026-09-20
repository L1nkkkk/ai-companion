#requires -Version 7.0
param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [string]$OutputDirectory,
    [ValidateSet('Build','ImportText','Prepare','Check')][string]$Stage='Build',
    [string]$CheckMethod
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if ((Get-Item -LiteralPath $UnityEditor).VersionInfo.ProductVersion -ne '2022.3.62f3c1_1623fc0bbb97') { throw 'Use Unity 2022.3.62f3c1 / 1623fc0bbb97.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository ('.bootstrap/desktop/build-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($Stage -eq 'Build' -and (Test-Path -LiteralPath $output) -and (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) { throw 'Build output must be new or empty.' }
$evidence = "$output-evidence"
New-Item -ItemType Directory -Force -Path $output,$evidence | Out-Null
$method = switch($Stage) {
    'ImportText' {'Companion.Foundation.Editor.DesktopBuild.ImportTextResources'}
    'Prepare' {'Companion.Foundation.Editor.DesktopBuild.Prepare'}
    'Build' {'Companion.Foundation.Editor.DesktopBuild.BuildWindows'}
    'Check' { if (-not $CheckMethod) {throw 'Pass -CheckMethod for checks.'}; $CheckMethod }
}
$info = [Diagnostics.ProcessStartInfo]::new()
$info.FileName=$UnityEditor; $info.UseShellExecute=$false; $info.CreateNoWindow=$true
$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
$info.Environment['COMPANION_BUILD_OUTPUT']=$output
$info.Environment['COMPANION_TEST_OUTPUT']=$evidence
foreach ($item in @('-batchmode','-quit','-projectPath',(Join-Path $repository 'apps/unity'),'-buildTarget','Win64','-logFile',(Join-Path $evidence "$Stage.log"))) { $info.ArgumentList.Add($item) }
if ($Stage -eq 'ImportText') {
    $package = Join-Path $repository 'apps/unity/Library/PackageCache/com.unity.textmeshpro@3.0.6/Package Resources/TMP Essential Resources.unitypackage'
    if (-not (Test-Path -LiteralPath $package)) { throw 'Resolve the locked Unity packages before importing TMP resources.' }
    $info.ArgumentList.Add('-importPackage'); $info.ArgumentList.Add($package)
} else { $info.ArgumentList.Add('-executeMethod'); $info.ArgumentList.Add($method) }
$process=[Diagnostics.Process]::Start($info)
try {
    if (-not $process.WaitForExit(1800000)) { $process.Kill(); $process.WaitForExit(5000) | Out-Null; throw 'Unity exceeded its external build deadline.' }
    if ($process.ExitCode -ne 0) { throw "Unity $Stage failed; inspect $evidence/$Stage.log" }
} finally { $process.Dispose() }
if ($Stage -ne 'Build') { Write-Output "Unity $Stage completed. Evidence: $evidence"; exit 0 }
$player=Join-Path $output 'NeuroSaki.exe'
if (-not (Test-Path -LiteralPath $player)) { throw 'Unity did not emit NeuroSaki.exe.' }
foreach ($pair in @(
    @('apps/unity/Assets/Live2D/Cubism/LICENSE.md','LIVE2D-LICENSE.md'),
    @('apps/unity/Assets/Live2D/Cubism/Plugins/LICENSE.md','LIVE2D-CORE-LICENSE.md'),
    @('apps/unity/Assets/ThirdParty/Fonts/OFL.txt','FONT-OFL.txt'),
    @('assets/manifest/unity-foundation-resources.md','RESOURCE-NOTICES.md')
)) { Copy-Item -LiteralPath (Join-Path $repository $pair[0]) -Destination (Join-Path $output $pair[1]) }
$zip="$output.zip"
Compress-Archive -LiteralPath $output -DestinationPath $zip
$manifest=[ordered]@{
    task='U01 desktop basics'; sourceSha=(& git -C $repository rev-parse HEAD).Trim()
    sourceDirty=[bool](& git -C $repository status --porcelain --untracked-files=normal)
    unity='2022.3.62f3c1'; sdk='5-r.4.1'; backend='Mono'; graphics='D3D11'; buildUtc=[DateTime]::UtcNow.ToString('o')
    player=$player; zip=$zip; zipSha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    files=@(Get-ChildItem -LiteralPath $output -File -Recurse | ForEach-Object { @{path=[IO.Path]::GetRelativePath($output,$_.FullName).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'build-manifest.json') -Encoding utf8
New-Item -ItemType Directory -Force -Path (Join-Path $repository '.bootstrap/desktop') | Out-Null
@{player=$player; evidence=$evidence; zip=$zip} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $repository '.bootstrap/desktop/latest-build.json') -Encoding utf8
Write-Output "Built Windows preview: $zip"
