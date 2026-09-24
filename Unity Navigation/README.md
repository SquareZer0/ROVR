# ROVR — LLM Navigation Pipeline

The Semantic Intent Resolution layer from Thesis Chapter 6: turns a natural-language
navigation command into deterministic Unity movement via a locally-hosted LLM, without any
cloud API dependency (Section 6.3.1).

For the whole-project setup (worlds and navigation together), see the [repository README](../README.md); the worlds themselves are documented in [`Unity Environments/`](../Unity%20Environments/README.md).

On this branch the scripts live in `Assets/ROVR/`. Voice comes from whisper.unity through `Assets/STT/WhisperVoiceMovement.cs`, and every sentence goes through the same pipeline as typed commands.

## How a command flows

```
speech -> whisper.unity -> VoiceCommandRouter   (typed commands skip this)
            sentence in progress: leading "stop" / "wait" / "ops" halts right away
            finished sentence: cleaned, de-duplicated, then:
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
| `VoiceCommandRouter.cs` | Routes speech-to-text output: halts early on a leading halt word in the sentence in progress, drops Whisper's noise ("(upbeat music)", "[click]", "Thank you."), drops a sentence reported twice within 1 s, and submits the rest. |
| `TranscriptFilter.cs` | Strips Whisper's sound annotations and stock hallucinations. |
| `ROVRDebugConsole.cs` | On-screen box in the Game view: type commands and see replies. |
| `../STT/WhisperVoiceMovement.cs` | Connects whisper.unity to the pipeline. Listens from the start, keeps the microphone going past its 60 s buffer, and can show what it heard and any question on a UI Text in the headset. |

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
4. Open `Assets/main.unity` and run **Tools > ROVR > Set Up Voice Navigation**. It finds the player (the object with `WhisperVoiceMovement`) and:
   - adds and connects `FOVMetadataGrounding`, `NavigationController`, `OllamaClient`, `SemanticIntentResolver` and `ROVRDebugConsole`;
   - puts the player's feet on the floor and rests its `CharacterController` there (a capsule sunk into the floor jumps up the first time it moves);
   - puts the XR rig's origin at the feet, so eye height comes from the headset. The old rig put the eye at 3.9 m, above the house's 3 m walls, where the LLM saw nothing.

   Every change is listed in the Console and can be undone. Running it again changes nothing.
5. Generate the worlds if needed (**Tools > ROVR > Generate All Three Worlds**). The tags the LLM can see are set by `groundedTags` on `FOVMetadataGrounding` — by default **Wall, Door, Chair, Tree**. Furniture and Goal are deliberately left out (Thesis 7.1.2: the House's metadata is doors, walls and chairs; the maze has no semantic objects). Add a tag to that list to expose it.
6. Enter Play mode and speak, or type commands, e.g. `move forward 5 meters`, `move forward until you reach the door`, `a bit more`, `turn around until you see the tree`, `stop`.

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

**Voice (this branch):** tested in headless Unity with the real resolver, the real model and a copy of the scene's rig; only whisper.unity's output was simulated, since the package wasn't installed on the test machine. The partial "Stop" halted the player at once, and "stop" said before the LLM answered cancelled that command. Noise from a real session transcript ("(upbeat music)", "[click]") was ignored. Mute and unmute work, and the world-switch panel stops the current move.

**Typed:** verified end to end with `gemma3n:e4b` through the real `OllamaClient`, driving the avatar in the real worlds: stopping 0.5 m short of a wall and of a chair, asking when several chairs are in view or none are, "a bit" / "a bit more" / "back a bit" adapting, "turn around until you see the tree", and "wait, I mean left" halting instantly then going left. On an RTX 5070 a command takes about **0.5 s** round trip.

**Use `127.0.0.1`, not `localhost`, for the Ollama endpoint.** On Windows `localhost` tries IPv6 first and adds about 2 s to every request; the client's default and an automatic rewrite of `localhost` already handle this.

## Known gaps / next steps

- **Voice hasn't been tried with a real microphone or headset.** How early "stop" acts depends on whisper.unity's timing, which couldn't be measured here. `WhisperVoiceMovement` re-transcribes the sentence in progress every 0.5 s (`partialUpdateSec`; whisper.unity's default of 3 s is too slow to catch "stop"). Tune it on the real machine: lower is faster but costs more GPU, which VR rendering also needs.
- **Ollama can't take audio** for `gemma3n:e4b` ("model does not support multimodal requests"), so speech is transcribed separately, by whisper.unity, before the LLM sees it. This differs from Thesis 6.3.1.
- **If the LLM answers slower than you speak**, a newer sentence replaces an older unanswered one, so a very fast string of repeats can collapse into fewer moves.
- **One conversational gap:** after "which chair?", a reply like "the left one" can't aim at that chair (turns are 90° snaps), and the model sometimes turns left instead of asking the user to face it. The engine's message tells users to face the object and repeat the command, which works.
- **Prompt-driven behaviour is model-dependent.** It was tuned and tested against `gemma3n:e4b` (41 of 42 scripted commands correct; the one miss is the "the left one" case above). Re-run those checks if you change the model or the prompt.
- **No controller (joystick) arm yet** for the between-subjects control condition (Section 7.1) — it must move through `CharacterController.Move` with the same step offset, and use the same snap-turn and speed settings.
- **No Task Completion Time logging** (Section 7.1.3) hooked up yet. `OllamaClient.LastLatencySeconds` covers LLM latency only.
- Ground-following isn't simulated (no gravity, no stairs) — vertical motion only happens on explicit up/down commands.
