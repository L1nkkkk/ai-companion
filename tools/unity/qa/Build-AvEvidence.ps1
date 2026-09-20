#requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$locator = 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = (& $locator -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
if (-not $visualStudio) { throw 'Existing Visual Studio C++ tools are required; nothing is installed.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$executable = Join-Path $output 'EncodeAvEvidence.exe'
if (Test-Path -LiteralPath $executable) { throw 'Use a new build directory.' }
$source = Join-Path $PSScriptRoot 'EncodeAvEvidence.cpp'
$batch = Join-Path $output 'compile-av-evidence.cmd'
$developer = Join-Path $visualStudio 'Common7/Tools/VsDevCmd.bat'
@"
@echo off
set VSCMD_SKIP_SENDTELEMETRY=1
call "$developer" -no_logo -arch=x64 -host_arch=x64 -winsdk=10.0.26100.0
if errorlevel 1 exit /b %errorlevel%
cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN "$source" /Fe:"$executable" /Fo:"$output\EncodeAvEvidence.obj" /link ole32.lib windowscodecs.lib mfplat.lib mfreadwrite.lib mfuuid.lib
"@ | Set-Content -LiteralPath $batch -Encoding ascii
$process = Start-Process -FilePath $env:ComSpec -ArgumentList @('/d','/c',('"' + $batch + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'compile.log') -RedirectStandardError (Join-Path $output 'compile-errors.log')
if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Build exceeded 60 seconds.' }
if ($process.ExitCode -ne 0) { throw "Build failed; see $output/compile.log." }
@{executable=$executable; sha256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant();sdk='10.0.26100.0';captureApiPresent=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'tool-build.json') -Encoding utf8
Write-Output "Built offline-only encoder: $executable"
