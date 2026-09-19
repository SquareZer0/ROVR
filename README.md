# ROVR

LLM-driven voice navigation in VR. A thesis prototype (De La Salle University) comparing joystick movement against spoken commands, interpreted by a local LLM, in three environments: a **plain**, a **maze** and a **house**.

| Folder | What it is |
|---|---|
| [`Unity Environments/`](Unity%20Environments/README.md) | Editor tools that generate the three worlds |
| [`Unity Navigation/`](Unity%20Navigation/README.md) | The LLM pipeline that turns a command into avatar movement |
| [`Voice Server/`](Voice%20Server/README.md) | Local speech-to-text (Whisper) for voice input |

## Status

Done: the three worlds (solid collision), the language-to-movement pipeline (about 0.6 s per command with a real model), and voice input through local Whisper, tested with synthesized speech.

Not done: voice has not been tried with a real microphone or headset, and halt words are slow (about 1.2 s after you start saying "stop"; a fast halt path is next). Also missing: the joystick comparison arm, VR rig and scenes, task timing and logging, questionnaires. Ollama can't take audio for this model, so speech is transcribed separately by Whisper before the LLM sees it.

## Requirements

- **Unity 6** (tested on 6000.4.10f1), 3D project
- **[Ollama](https://ollama.com)** and the model `gemma3n:e4b` (about 7.5 GB). Tested on Windows 11 with an RTX 5070.
- For voice input: about 600 MB more for Whisper (see below). It runs on the CPU.

## Setup

**1. Worlds**

1. Create `Assets/Editor/` in your Unity project and copy the six `.cs` files from `Unity Environments/` into it.
2. Open a scene and run **Tools > ROVR > Generate All Three Worlds**.
3. Run **Tools > ROVR > Check Collisions**. It should report `All worlds solid.`

**2. Navigation**

1. Pull the model:
   ```bash
   ollama pull gemma3n:e4b
   ```
2. Create `Assets/Scripts/ROVR/` (not an `Editor` folder) and copy the `.cs` files from `Unity Navigation/` into it.
3. Make a player: a GameObject with a `CharacterController` and a child camera. Add `FOVMetadataGrounding`, `NavigationController`, `OllamaClient`, `SemanticIntentResolver` and `ROVRDebugConsole` to it, and set each `pov` field to the camera.
4. Put the player on a world's `Start` object, enter Play mode, type a command in the on-screen box and press Send.

**Sample commands**

| Type this | What should happen |
|---|---|
| `move forward 5 meters` | Moves 5 m forward |
| `move forward until you hit a wall` | Stops 0.5 m short of the wall |
| `move forward a bit`, then `a bit more` | Moves 0.5 m, then slightly further (the "bit" adapts to you) |
| `go to the door` | Moves toward the door and stops 0.5 m short. Asks which one if several are in different directions |
| `turn around until you see the tree` | Turns in 90° steps until the tree is in view (Plain) |
| `wait, I mean left` (while moving) | Stops, then moves left |
| `stop` | Stops. Immediate when typed; about 1 s after you start saying it |

If it can't do what you asked, it says why in the on-screen box.

**3. Voice (optional)**

1. Download and start the local speech-to-text server, and leave it running:
   ```bash
   powershell -ExecutionPolicy Bypass -File "Voice Server/setup.ps1"
   powershell -ExecutionPolicy Bypass -File "Voice Server/start.ps1"
   ```
2. On the same player object, add `WhisperClient` and `VoiceInput`. In `ROVRDebugConsole`, set its `voice` field to the `VoiceInput`.
3. Enter Play mode and speak. The box shows `Mic:` (listening / hearing / transcribing) and what it heard. Typing still works.

## Troubleshooting

- **No `Tools > ROVR` menu:** the environment scripts must be in a folder named exactly `Editor`.
- **"Ollama request failed":** check that Ollama is running and `ollama list` shows the model.
- **"Speech-to-text request failed":** the Whisper server isn't running (`Voice Server/start.ps1`).
- **Walks through walls:** the player must move via `CharacterController.Move`, not by setting its position.
