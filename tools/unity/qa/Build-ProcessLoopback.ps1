#requires -Version 7.0
param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$WindowsSdkVersion = '10.0.26100.0'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$locator = 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $locator)) { throw 'Visual Studio C++ tools are required; this script installs nothing.' }
$visualStudio = (& $locator -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
if (-not $visualStudio) { throw 'No existing Visual Studio C++ toolchain was found.' }
$sdk = Join-Path 'C:/Program Files (x86)/Windows Kits/10/Include' $WindowsSdkVersion
if (-not (Test-Path -LiteralPath (Join-Path $sdk 'um/audioclientactivationparams.h'))) { throw 'The fixed Windows SDK process-loopback header is unavailable.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$executable = Join-Path $output 'ProcessLoopback.exe'
if (Test-Path -LiteralPath $executable) { throw 'Use a new output directory; existing evidence tools are not overwritten.' }
$source = Join-Path $PSScriptRoot 'ProcessLoopback.cpp'
$batch = Join-Path $output 'compile-process-loopback.cmd'
$developer = Join-Path $visualStudio 'Common7/Tools/VsDevCmd.bat'
# Quoted static tool paths only. No filesystem deletion or moving is delegated across shells.
@"
@echo off
set VSCMD_SKIP_SENDTELEMETRY=1
call "$developer" -no_logo -arch=x64 -host_arch=x64 -winsdk=$WindowsSdkVersion
if errorlevel 1 exit /b %errorlevel%
cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN "$source" /Fe:"$executable" /Fo:"$output\ProcessLoopback.obj" /link ole32.lib mmdevapi.lib
"@ | Set-Content -LiteralPath $batch -Encoding ascii
$process = Start-Process -FilePath $env:ComSpec -ArgumentList @('/d','/c',('"' + $batch + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'compile.log') -RedirectStandardError (Join-Path $output 'compile-errors.log')
if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'C++ build exceeded its 60-second deadline.' }
if ($process.ExitCode -ne 0) { throw "C++ build failed. See $output/compile.log and compile-errors.log." }
& $executable --self-test
if ($LASTEXITCODE -ne 0) { throw 'Non-recording self-test failed.' }
@{executable=$executable; sha256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant(); sdk=$WindowsSdkVersion; architecture='x64'; recordingStarted=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'tool-build.json') -Encoding utf8
Write-Output "Built process-limited capture tool without starting audio capture: $executable"
