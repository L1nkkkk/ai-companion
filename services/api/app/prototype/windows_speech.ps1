$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$requestData = [Console]::In.ReadToEnd() | ConvertFrom-Json
Add-Type -AssemblyName System.Speech
$speech = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    if ($requestData.mode -eq 'voices') {
        @($speech.GetInstalledVoices() | Where-Object Enabled | ForEach-Object {
            @{ name = $_.VoiceInfo.Name; language = $_.VoiceInfo.Culture.Name }
        }) | ConvertTo-Json -Compress
    } else {
        if ($requestData.voice) { $speech.SelectVoice([string]$requestData.voice) }
        $wave = New-Object System.IO.MemoryStream
        try {
            $speech.SetOutputToWaveStream($wave)
            $speech.Speak([string]$requestData.text)
            $speech.SetOutputToNull()
            [Console]::Write([Convert]::ToBase64String($wave.ToArray()))
        } finally { $wave.Dispose() }
    }
} finally { $speech.Dispose() }
