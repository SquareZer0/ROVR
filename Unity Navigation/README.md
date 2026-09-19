# ROVR — LLM Navigation Pipeline

The Semantic Intent Resolution layer from Thesis Chapter 6: turns a natural-language
navigation command into deterministic Unity movement via a locally-hosted LLM, without any
cloud API dependency (Section 6.3.1).

For the whole-project setup (worlds and navigation together), see the [repository README](../README.md); the worlds themselves are documented in [`Unity Environments/`](../Unity%20Environments/README.md).

Voice input works through a local Whisper server (see [`Voice Server/`](../Voice%20Server/README.md)); typed commands go through the same pipeline.

## How a command flows

```
utterance
  -> InterruptModule        "stop" / "wait, I mean left": halts NOW, bypassing the LLM
  -> FOVMetadataGrounding   raycast matrix over the whole view -> what the user can see
  -> Ollama (Gemma 3n)      + previous command + pending question -> schema-constrained JSON
  -> engine checks          closed-world validation, "a bit" -> metres, clarify or execute
  -> NavigationController   CharacterController movement, reports how it ended
```

## Files

| File | Role |
|---|---|
| `NavigationCommand.cs` | Command schema (Action / Direction / Amount+Magnitude / Condition), the JSON schema sent to Ollama, and outcomes. Unusable model output becomes a clarification, never a silent no-op. |
| `OllamaClient.cs` | HTTP client for a local Ollama server. Keeps the model loaded, warms it up at start, times each request (`LastLatencySeconds`). |
| `FOVMetadataGrounding.cs` | Raycast matrix (21×13 over 100°×80°). Sees through door triggers, is blocked by solids, keeps each object separate, and only reports tags in `groundedTags`. |
| `InterruptModule.cs` | Halt detection. Only a *leading* halt word counts; only an explicit correction after it is kept. |
| `MovementHabits.cs` | How far "a bit" is: starts at 0.5 m, adapts to the user, resets per participant. |
| `NavigationController.cs` | Movement: fixed speed, smooth stop at a target, instant 90° snap-turns about the head, "turn until you see X", blocked detection. |
| `SemanticIntentResolver.cs` | Orchestrates all of it and enforces the rules below in code. |
| `VoiceInput.cs` | Voice input: microphone -> voice-activity detector -> Whisper -> `SemanticIntentResolver`. Always listening, with push-to-talk as a fallback. |
| `VoiceActivityDetector.cs` | Finds where each spoken utterance starts and ends, so Whisper only ever sees speech. Learns the background noise level. |
| `WhisperClient.cs` | HTTP client for the local Whisper server (`Voice Server/`). |
| `AudioUtil.cs`, `TranscriptFilter.cs` | WAV encoding and resampling; drops Whisper's non-speech annotations and stock hallucinations ("Thank you."). |
| `ROVRDebugConsole.cs` | On-screen box: typed commands, plus the microphone state and what was heard. |

## Setup

1. Install [Ollama](https://ollama.com).
2. Pull the model the thesis specifies (Gemma 3n E4B — the paper's "Gemma 4 E4B" refers to this):
   ```bash
   ollama pull gemma3n:e4b
   ```
3. Start the server (usually already running as a background service after install; if not):
   ```bash
   ollama serve
   ```
4. In Unity, add all the scripts under `Assets/Scripts/ROVR/`.
5. On a GameObject with a `CharacterController` (your avatar/rig), add:
   - `FOVMetadataGrounding` (point `pov` at the HMD camera)
   - `NavigationController` (same `pov`; it finds `FOVMetadataGrounding` on the same object)
   - `OllamaClient`
   - `SemanticIntentResolver` (it finds the other three on the same object)
   - `ROVRDebugConsole` (wire its `resolver` field)
6. Generate the worlds (`Unity Environments/`). The tags the LLM can see are set by `groundedTags` on `FOVMetadataGrounding` — by default **Wall, Door, Chair, Tree**. Furniture and Goal are deliberately left out (Thesis 7.1.2: the House's metadata is doors, walls and chairs; the maze has no semantic objects). Add a tag to that list to expose it.
7. **Voice (optional):** start the Whisper server (`Voice Server/start.ps1`), add `WhisperClient` and `VoiceInput` to the same object, and set the `voice` field of `ROVRDebugConsole`. The microphone is then always listening; set `VoiceInput`'s mode to push-to-talk in a noisy room and call `BeginPushToTalk()` / `EndPushToTalk()` from your own input (the debug console has a "Hold to talk" button). Start it while the room is quiet: the detector learns the background level in its first half second.
8. Enter Play mode and type or speak commands, e.g. `move forward 5 meters`, `move forward until you reach the door`, `a bit more`, `turn around until you see the tree`, `stop`.

Call `SemanticIntentResolver.ResetSession()` between participants: it clears the previous command and resets "a bit" to 0.5 m.

## Behaviour worth knowing

- **Closed world, enforced by the engine.** A move may only target something in the current view. If the model returns a target that isn't (or isn't a known object type), the engine replaces it with a clarification question. The one exception is `turn ... until you see X` — that is how X gets found.
- **One command of memory.** The previous command, how it ended (completed / halted / blocked), and any question still waiting for an answer go back to the model, so "a bit more" and "the other way" work. Object knowledge never comes from that memory, only from the current view. *This relaxes the strict statelessness in Thesis 5.5.4 by exactly one command — the thesis text should say so.*
- **"A bit".** The model only labels a move `small`; the engine converts it. Repeating a nudge in the same direction soon after grows it (×1.15), reversing right after shrinks it (×0.85), bounded to 0.2–2 m.
- **Halts always win.** A halt cancels any request still waiting on the LLM, so a late reply can't restart movement. A newer utterance also supersedes an older one that hasn't been answered yet. "Wait, I mean left" halts immediately, then runs "left"; "stop moving" or "wait a bit" never restart anything.
- **Turns** are whole 90° snaps (`turn around` = two), instant, and pivot about the head so the view doesn't shift.
- **Stopping at a target** is measured from the avatar's body surface (stop gap 0.5 m, eased over the last metre), so it works for any rig radius. A step that can't make progress ends as *Blocked* and aborts the rest of the chain, with a message to the user.
- **Nothing fails silently.** Unusable model output, unknown or unseen objects, blocked moves, and turns that find nothing all produce a message.
- The step offset is forced to 0 so the avatar can't hop onto props and hover (the worlds are flat and there is no gravity).

## Tested against the real model

Verified end to end with `gemma3n:e4b` through the real `OllamaClient`, driving the avatar in the real worlds: stopping 0.5 m short of a wall and of a chair, asking when several chairs are in view or none are, "a bit" / "a bit more" / "back a bit" adapting, "turn around until you see the tree", and "wait, I mean left" halting instantly then going left. On an RTX 5070 a command takes about **0.6 s** round trip.

**Use `127.0.0.1`, not `localhost`, for the Ollama endpoint.** On Windows `localhost` tries IPv6 first and adds about 2 s to every request; the client's default and an automatic rewrite of `localhost` already handle this.

## Known gaps / next steps

- **Voice is untested with a real microphone or headset.** The microphone capture, the push-to-talk button and the headset's 48 kHz audio path were not exercised (no microphone available); everything downstream was tested with recorded and synthesized speech fed in as audio.
- **Halts are slow by voice.** The Interrupt Module only acts once the whole utterance is transcribed: about 1.2–1.5 s from the start of "stop", during which the avatar moves about 2.5 m. Section 5.5.2 assumes a halt can land mid-utterance. Planned next step: an always-on halt-word detector (or an early transcription of the first half second) that stops the avatar without waiting.
- **The voice-activity detector is energy-based**, not a speech model. It handles steady background noise, but a loud non-speech sound (a door slam) can still be sent to Whisper; its output is then filtered out.
- **Ollama can't take audio** for `gemma3n:e4b`, so the thesis's single-model audio path doesn't work; speech is transcribed by Whisper first.
- **One conversational gap:** after "which chair?", a reply like "the left one" can't aim at that chair (turns are 90° snaps), and the model sometimes turns left instead of asking the user to face it. The engine's message tells users to face the object and repeat the command, which works.
- **Prompt-driven behaviour is model-dependent.** It was tuned and tested against `gemma3n:e4b` (41 of 42 scripted commands correct; the one miss is the "the left one" case above). Re-run those checks if you change the model or the prompt.
- **No controller (joystick) arm yet** for the between-subjects control condition (Section 7.1) — it must move through `CharacterController.Move` with the same step offset, and use the same snap-turn and speed settings.
- **No Task Completion Time logging** (Section 7.1.3) hooked up yet. `OllamaClient.LastLatencySeconds` covers LLM latency only.
- Ground-following isn't simulated (no gravity, no stairs) — vertical motion only happens on explicit up/down commands.
