# Starts the local speech-to-text server (whisper.cpp) that Unity's WhisperClient talks to.
# Run setup.ps1 first. Leave this window open while you use voice input.
#
#   powershell -ExecutionPolicy Bypass -File start.ps1                  # base.en on port 8080
#   powershell -ExecutionPolicy Bypass -File start.ps1 -Model small.en  # more accurate, slower
param(
    [string]$Model = "base.en",
    [int]$Port = 8080,
    [int]$Threads = 8,
    [int]$AudioContext = 768   # 0 = full 30 s window. 768 covers ~15 s of speech at about half the cost
)

$ErrorActionPreference = "Stop"
$tools = Join-Path $PSScriptRoot "tools"

$server = Get-ChildItem (Join-Path $tools "whisper-bin") -Recurse -Filter whisper-server.exe -ErrorAction SilentlyContinue | Select-Object -First 1
$modelFile = Join-Path $tools "ggml-$Model.bin"
if (-not $server -or -not (Test-Path $modelFile)) {
    Write-Host "Missing files. Run setup.ps1 first (model: $Model)."
    exit 1
}

Write-Host "Whisper server: model $Model on http://127.0.0.1:$Port/inference (Ctrl+C to stop)"

# 127.0.0.1, not localhost: on Windows localhost tries IPv6 first and adds ~2 s per request.
# -nt: no timestamps in the text. -sns: don't emit [BLANK_AUDIO]-style annotations.
# -ac: Whisper normally pads every clip to a 30 s window; commands are a few seconds, so a shorter
# audio context is about twice as fast with the same accuracy on our test commands.
& $server.FullName -m $modelFile --host 127.0.0.1 --port $Port -l en -t $Threads -ac $AudioContext -nt -sns
