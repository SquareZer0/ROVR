# Downloads whisper.cpp (official Windows CPU build) and English Whisper models into ./tools
# (git-ignored). Safe to re-run: anything already downloaded is skipped.
#
#   powershell -ExecutionPolicy Bypass -File setup.ps1                 # base.en and small.en
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -Models base.en # just one
param([string[]]$Models = @("base.en", "small.en"))

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # the progress bar makes Invoke-WebRequest far slower

$WhisperVersion = "v1.9.2"
$tools = Join-Path $PSScriptRoot "tools"
New-Item -ItemType Directory -Force $tools | Out-Null

$binDir = Join-Path $tools "whisper-bin"
if (-not (Get-ChildItem $binDir -Recurse -Filter whisper-server.exe -ErrorAction SilentlyContinue)) {
    $zip = Join-Path $tools "whisper-bin-x64.zip"
    Write-Host "Downloading whisper.cpp $WhisperVersion (7.8 MB)..."
    Invoke-WebRequest "https://github.com/ggml-org/whisper.cpp/releases/download/$WhisperVersion/whisper-bin-x64.zip" -OutFile $zip
    Expand-Archive $zip -DestinationPath $binDir -Force
    Remove-Item $zip
}

foreach ($m in $Models) {
    $file = Join-Path $tools "ggml-$m.bin"
    if (Test-Path $file) { Write-Host "Model $m already downloaded."; continue }
    Write-Host "Downloading model $m..."
    Invoke-WebRequest "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$m.bin" -OutFile $file
}

Write-Host "Done. Start the server with: powershell -ExecutionPolicy Bypass -File start.ps1"
