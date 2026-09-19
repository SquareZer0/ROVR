// Assets/Scripts/ROVR/ROVRDebugConsole.cs
// Text-input stand-in for the voice channel. Lets the whole intent -> command -> movement
// pipeline be exercised and debugged in Play mode before real microphone capture and speech
// recognition are wired up. Not meant to ship in the study build -- swap this component out for
// the real audio input path once that's ready; SemanticIntentResolver.SubmitUtterance is the
// same entry point either way, so nothing downstream needs to change.
using UnityEngine;

namespace ROVR
{
    public class ROVRDebugConsole : MonoBehaviour
    {
        [SerializeField] SemanticIntentResolver resolver;
        [SerializeField] VoiceInput voice; // optional: shows the microphone state and what was heard

        string input = "";
        string lastStatus = "Type a command and press Send.";
        string lastHeard = "";
        bool talkHeld;

        void OnEnable()
        {
            resolver.OnClarificationNeeded += HandleClarification;
            resolver.OnStatus += HandleStatus;
            resolver.OnError += HandleError;
            resolver.OnCommandResolved += HandleResolved;
            if (voice != null) voice.OnTranscript += HandleHeard;
        }

        void OnDisable()
        {
            resolver.OnClarificationNeeded -= HandleClarification;
            resolver.OnStatus -= HandleStatus;
            resolver.OnError -= HandleError;
            resolver.OnCommandResolved -= HandleResolved;
            if (voice != null) voice.OnTranscript -= HandleHeard;
        }

        void HandleHeard(string text) { lastHeard = text; }

        void HandleClarification(string message) { lastStatus = "[asks] " + message; }
        void HandleStatus(string message) { lastStatus = "[note] " + message; }
        void HandleError(string message) { lastStatus = "[error] " + message; }

        void HandleResolved(NavigationCommand command)
        {
            if (!command.IsClarification)
                lastStatus = "[ok] " + command.steps.Length + " step(s) - LLM " + resolver.LastLatencySeconds.ToString("F2") + " s";
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 520, voice != null ? 220 : 150), GUI.skin.box);
            GUILayout.Label(voice != null ? "ROVR voice + typed commands" : "ROVR text-command stub (stand-in for voice input)");

            if (voice != null)
            {
                GUILayout.Label("Mic: " + voice.State + (voice.LastLatencySeconds > 0f ? "  (speech-to-text " + voice.LastLatencySeconds.ToString("F2") + " s)" : ""));
                GUILayout.Label("Heard: " + lastHeard);

                if (voice.Mode == ListenMode.PushToTalk)
                {
                    bool held = GUILayout.RepeatButton("Hold to talk");
                    if (held && !talkHeld) voice.BeginPushToTalk();
                    if (!held && talkHeld && Event.current.type == EventType.Repaint) voice.EndPushToTalk();
                    talkHeld = held;
                }
            }

            GUI.SetNextControlName("ROVRInput");
            input = GUILayout.TextField(input);

            bool enterPressed = Event.current.isKey
                && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Return
                && GUI.GetNameOfFocusedControl() == "ROVRInput";

            if (GUILayout.Button("Send") || enterPressed)
            {
                resolver.SubmitUtterance(input);
                input = "";
            }

            GUILayout.Label(lastStatus);
            GUILayout.Label("\"a bit\" = " + resolver.Habits.BitMeters.ToString("F2") + " m");
            GUILayout.EndArea();
        }
    }
}
