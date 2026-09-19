# ROVR — Voice Server

Local speech-to-text for ROVR's voice input, using [whisper.cpp](https://github.com/ggml-org/whisper.cpp). Unity's `WhisperClient` sends it short audio clips and gets text back, which then goes into the same pipeline as typed commands. Nothing leaves the machine.

Speech-to-text is a separate step because Ollama can't take audio for `gemma3n:e4b` (it rejects it with "model does not support multimodal requests").

## Setup

Windows, PowerShell. From this folder:

```bash
powershell -ExecutionPolicy Bypass -File setup.ps1
powershell -ExecutionPolicy Bypass -File start.ps1
```

- `setup.ps1` downloads into a git-ignored `tools/` folder: the whisper.cpp v1.9.2 Windows CPU build (7.8 MB), the `base.en` model (141 MB) and the `small.en` model (465 MB). Add `-Models base.en` to fetch only one. Re-running skips what's already there.
- `start.ps1` starts the server on `http://127.0.0.1:8080/inference`. Leave it running while you use voice input.

Then, in Unity, add `WhisperClient` and `VoiceInput` to the player (see the [navigation README](../Unity%20Navigation/README.md)).

## Choosing a model

Measured on 96 short clips (48 commands, 2 Windows text-to-speech voices), on a 20-thread CPU. The audio was synthetic and clean, so treat the accuracy as an upper bound; real accented speech will do worse.

| Setup | Exact match | Time per clip |
|---|---|---|
| `base.en`, 4 threads | 97% | 0.54 s |
| `base.en`, 8 threads | 97% | 0.33 s |
| **`base.en`, 8 threads, short audio window (default)** | **97%** | **0.17 s** |
| `small.en`, 4 threads | 99% | 1.98 s |
| `small.en`, 12 threads, short audio window | 99% | 0.52 s |

The default is `base.en` for speed. If it mishears your participants, try the more accurate model:

```bash
powershell -ExecutionPolicy Bypass -File start.ps1 -Model small.en -Threads 12
```

The short audio window (`-AudioContext 768`, about 15 s) is about twice as fast because Whisper otherwise pads every clip to 30 s. Set `-AudioContext 0` to turn it off.

## Why CPU

The CUDA builds are 260–640 MB and target older GPUs than a 50-series card, and the GPU is already busy rendering VR. CPU inference keeps the two from competing.

## Known limits

- **Halt words** are only recognised after a whole utterance is transcribed, about 1.2–1.5 s after you start saying "stop" (the avatar keeps moving about 2.5 m meanwhile). A dedicated fast halt path is the next step.
- **"Ops"** is often heard as "Op" or "Off". The Interrupt Module treats those as halts too.
- **Untested with a real microphone or headset.** Everything was tested with recorded and synthesized speech fed in as audio.

## Troubleshooting

- **"Speech-to-text request failed":** the server isn't running (run `start.ps1`), or Unity's `WhisperClient` endpoint doesn't match the port.
- **Slow responses:** use `127.0.0.1`, not `localhost`; on Windows `localhost` adds about 2 s per request.
- **Nothing is heard:** check the `Mic:` line in the on-screen box. `NoMicrophone` means Unity found no input device.
