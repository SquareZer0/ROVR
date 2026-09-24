# ROVR

LLM-driven voice navigation in VR. A thesis prototype (De La Salle University) comparing joystick movement against spoken commands, interpreted by a local LLM, in three environments: a **plain**, a **maze** and a **house**.

This branch (`combined-voice`) joins the two halves: **whisper.unity** listens (speech-to-text inside Unity), and the **LLM pipeline** decides what the command means and moves the player.

| Folder | What it is |
|---|---|
| `Assets/main.unity` | The scene: three worlds, the XR player rig, and the world-switch panel |
| `Assets/Editor/` | World generators, collision check, and the voice setup tool |
| `Assets/ROVR/` | The LLM pipeline that turns a command into player movement ([docs](Unity%20Navigation/README.md)) |
| `Assets/STT/` | Voice input: whisper.unity feeding the pipeline |
| `Assets/GUI/` | In-VR panel: switch worlds, mute the microphone |
| `Unity Environments/` | World documentation and floor plans ([docs](Unity%20Environments/README.md)) |

## Status

Done: the three worlds (solid collision), the language-to-movement pipeline (about 0.5 s per command with the real model), and voice input through whisper.unity connected to it. Tested in Unity with the real model; the speech-to-text part was simulated, since whisper.unity wasn't installed on the test machine.

Not done: a test with a real microphone and headset, the joystick comparison arm, task timing and logging, questionnaires.

## Requirements

- **Unity 6** (6000.4.10f1) with **XR Interaction Toolkit**, **OpenXR** and **whisper.unity** (`https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity`)
- **[Ollama](https://ollama.com)** and the model `gemma3n:e4b` (about 7.5 GB). Tested on Windows 11 with an RTX 5070.
- A Whisper model in `Assets/StreamingAssets/`, e.g. [`ggml-base.en.bin`](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin) (141 MB; English-only, which suits the study). Models are git-ignored, so each machine downloads its own.
- A Meta Quest connected by Link or Air Link.

> **Not yet committed:** `ProjectSettings/` and `Packages/`. Until they are, a fresh clone doesn't open as a working project. Whoever has the working project should commit both folders.

## Setup

1. Pull the LLM:
   ```bash
   ollama pull gemma3n:e4b
   ```
2. Put the Whisper model in `Assets/StreamingAssets/` and set **WhisperManager > Model Path** to its file name.
3. Open `Assets/main.unity` and run **Tools > ROVR > Set Up Voice Navigation**, then save the scene. It adds the pipeline to `PlayerBody`, connects the voice script, and fixes the rig. Every change is listed in the Console and can be undone.
4. In the **Meta Quest Link** app, set it as the active OpenXR runtime (Settings > General).
5. Press Play and speak. Listening starts by itself; the panel's mute button pauses it.

To regenerate the worlds, use **Tools > ROVR > Generate All Three Worlds**, then **Check Collisions** (it should report `All worlds solid.`).

## Sample commands

| Say this | What should happen |
|---|---|
| "move forward 5 meters" | Moves 5 m forward |
| "move forward until you hit a wall" | Stops 0.5 m short of the wall |
| "move forward a bit", then "a bit more" | Moves 0.5 m, then slightly further (the "bit" adapts to you) |
| "go to the door" | Moves toward the door and stops 0.5 m short. Asks which one if several are in different directions |
| "turn around until you see the tree" | Turns in 90° steps until the tree is in view (Plain) |
| "wait, I mean left" (while moving) | Stops as soon as "wait" is heard, then moves left |
| "stop" | Stops. It's checked while the sentence is still being transcribed, so it acts early; how early hasn't been measured with a real microphone yet |

If it can't do what you asked, it says why: in the Game view's box, or on a UI text you assign to **WhisperVoiceMovement > Status Text** for the headset.

## Troubleshooting

- **Black screen in the headset:** set Meta Quest Link as the active OpenXR runtime (step 4). If SteamVR is installed, it often takes that slot.
- **"Speech recognition failed to load":** the Whisper model isn't in `Assets/StreamingAssets/`, or the Model Path doesn't match its name.
- **"Ollama request failed":** check that Ollama is running and `ollama list` shows the model.
- **The player sees nothing / can't find doors:** run the setup tool. An eye height above the house's 3 m walls leaves the LLM with nothing in view.
- **No `Tools > ROVR` menu:** there are compile errors; check the Console.
