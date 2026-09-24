// Assets/STT/WhisperVoiceMovement.cs
// Voice input for ROVR: whisper.unity (speech-to-text inside Unity) feeding the LLM navigation
// pipeline (SemanticIntentResolver). Speech no longer moves the player directly; every sentence
// goes through the same pipeline as typed commands, so grounding, clarification questions,
// "a bit", "until the wall", turning and collision all apply.
//
// Class name and the two Whisper fields are unchanged so existing scenes keep their wiring.
// Run Tools > ROVR > Set Up Voice Navigation once to add and connect the rest.
//
// Listening is always on by default. "Stop" is caught early: the sentence in progress is
// re-transcribed every `partialUpdateSec`, and a leading halt word stops the player before
// the sentence ends.
using UnityEngine;
using UnityEngine.UI;
using Whisper;
using Whisper.Utils;
using ROVR;

public class WhisperVoiceMovement : MonoBehaviour
{
    [Header("Whisper References")]
    public WhisperManager whisperManager;
    public MicrophoneRecord microphoneRecord;

    [Header("ROVR Pipeline")]
    [Tooltip("Found on this GameObject if left empty.")]
    public SemanticIntentResolver resolver;

    [Header("Listening")]
    [Tooltip("Start listening as soon as the Whisper model is loaded. Off: call StartListening().")]
    public bool listenOnStart = true;

    [Tooltip("Seconds between re-transcriptions of the sentence in progress, which is what lets \"stop\" " +
             "act early. 0 keeps WhisperManager's own setting (3 s by default, too slow for stopping). " +
             "Lower is faster to stop but costs more compute.")]
    public float partialUpdateSec = 0.5f;

    [Header("Optional Feedback")]
    [Tooltip("A UI Text (e.g. on a world-space canvas) that shows what was heard and any question the system asks.")]
    public Text statusText;

    public bool IsListening => listening;
    public string LastHeard => router != null ? router.LastHeard : null;

    WhisperStream stream;
    VoiceCommandRouter router;
    bool listening;
    string heardLine = "", messageLine = "";

    async void Start()
    {
        if (resolver == null) resolver = GetComponent<SemanticIntentResolver>();
        if (whisperManager == null || microphoneRecord == null || resolver == null)
        {
            Debug.LogError("[ROVR voice] Needs a WhisperManager, a MicrophoneRecord and a SemanticIntentResolver. " +
                           "Run Tools > ROVR > Set Up Voice Navigation.");
            enabled = false;
            return;
        }

        router = new VoiceCommandRouter(resolver.HaltNow, SubmitHeard, () => Time.time);
        resolver.OnClarificationNeeded += ShowMessage;
        resolver.OnStatus += ShowMessage;
        resolver.OnError += ShowMessage;

        // Keep recording past MicrophoneRecord's 60 s buffer. Without this the microphone stops after
        // a minute, and whisper.unity stops the whole stream with it.
        microphoneRecord.loop = true;
        if (partialUpdateSec > 0f) whisperManager.stepSec = partialUpdateSec;

        stream = await whisperManager.CreateStream(microphoneRecord);
        if (this == null) return; // destroyed while the model was loading

        if (stream == null)
        {
            ShowMessage("Speech recognition failed to load. Is the Whisper model file in StreamingAssets?");
            enabled = false;
            return;
        }

        stream.OnSegmentUpdated += HandleSegmentUpdated;
        stream.OnSegmentFinished += HandleSegmentFinished;

        if (listenOnStart) StartListening();
    }

    public void StartListening()
    {
        if (stream == null || listening) return;
        listening = true;
        if (!microphoneRecord.enabled) microphoneRecord.enabled = true;
        if (!microphoneRecord.IsRecording) microphoneRecord.StartRecord();
        stream.StartStream();
        ShowHeard("");
    }

    public void StopListening()
    {
        if (stream == null || !listening) return;
        listening = false;
        stream.StopStream();
        if (microphoneRecord.IsRecording) microphoneRecord.StopRecord();
    }

    public void ToggleListening()
    {
        if (listening) StopListening(); else StartListening();
    }

    void Update()
    {
        // Safety net: if the microphone stopped on its own (whisper.unity then stops the stream too),
        // start both again. A disabled MicrophoneRecord counts as deliberately muted.
        if (listening && stream != null && microphoneRecord.enabled && !microphoneRecord.IsRecording)
        {
            microphoneRecord.StartRecord();
            stream.StartStream();
        }
    }

    void HandleSegmentUpdated(WhisperResult segment)
    {
        if (segment == null) return;
        router.OnPartial(segment.Result);
    }

    void HandleSegmentFinished(WhisperResult segment)
    {
        if (segment == null) return;
        router.OnFinal(segment.Result);
    }

    void SubmitHeard(string text)
    {
        ShowHeard(text);
        resolver.SubmitUtterance(text);
    }

    void ShowHeard(string text)
    {
        heardLine = text;
        messageLine = "";
        Refresh();
    }

    void ShowMessage(string message)
    {
        messageLine = message;
        Refresh();
    }

    void Refresh()
    {
        if (statusText == null) return;
        statusText.text = (string.IsNullOrEmpty(heardLine) ? "" : "Heard: " + heardLine) +
                          (string.IsNullOrEmpty(messageLine) ? "" : "\n" + messageLine);
    }

    bool resumeOnEnable;

    void OnEnable()
    {
        if (resumeOnEnable) { resumeOnEnable = false; StartListening(); }
    }

    void OnDisable()
    {
        resumeOnEnable = listening;
        StopListening();
    }

    void OnDestroy()
    {
        if (stream != null)
        {
            stream.OnSegmentUpdated -= HandleSegmentUpdated;
            stream.OnSegmentFinished -= HandleSegmentFinished;
        }
        if (resolver != null)
        {
            resolver.OnClarificationNeeded -= ShowMessage;
            resolver.OnStatus -= ShowMessage;
            resolver.OnError -= ShowMessage;
        }
    }
}
