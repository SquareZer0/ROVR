// Assets/Scripts/ROVR/VoiceInput.cs
// Voice input for ROVR: microphone -> voice-activity detector -> Whisper -> SemanticIntentResolver.
// It replaces typing (ROVRDebugConsole stays as a fallback); everything after SubmitUtterance is the
// same pipeline the typed commands use.
//
// Always listening (Section 6.3.3): the microphone runs continuously, and the detector cuts it into
// utterances so Whisper only ever sees speech. A finished utterance is transcribed and submitted.
// Push-to-talk is available as a fallback for noisy rooms (call BeginPushToTalk/EndPushToTalk from
// whatever input you use; this class deliberately doesn't read the keyboard, so it works with either
// Unity input backend).
//
// Not here yet: halt words are only recognised once the whole utterance has been transcribed, which
// costs roughly a second. A dedicated always-on halt-word detector is the planned next step.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ROVR
{
    public enum VoiceState { Off, Listening, Hearing, Transcribing, NoMicrophone }
    public enum ListenMode { AlwaysListening, PushToTalk }

    public class VoiceInput : MonoBehaviour
    {
        public const int SampleRate = 16000;

        [SerializeField] SemanticIntentResolver resolver;
        [SerializeField] WhisperClient whisper;
        [SerializeField] ListenMode mode = ListenMode.AlwaysListening;
        [SerializeField] string microphoneDevice = ""; // empty = the default microphone
        [SerializeField] bool useMicrophone = true;    // false: audio is supplied through FeedSamples

        public event Action<VoiceState> OnStateChanged;
        public event Action<string> OnTranscript;
        public event Action<string> OnError;

        public VoiceState State { get; private set; } = VoiceState.Off;
        public string LastTranscript { get; private set; }
        public ListenMode Mode => mode;
        public float LastLatencySeconds => whisper != null ? whisper.LastLatencySeconds : 0f;
        public float MicLevel => vad != null ? vad.LastRms : 0f;

        // Mute while the system is speaking, so it can't hear itself.
        public bool Muted
        {
            get { return muted; }
            set
            {
                muted = value;
                if (muted) { vad.Reset(); pttHeld = false; pttBuffer.Clear(); }
                SetState(IdleState());
            }
        }

        VoiceActivityDetector vad;
        AudioClip micClip;
        string device;
        int micReadPos;
        bool muted, busy, pttHeld;
        readonly Queue<float[]> queue = new Queue<float[]>();
        readonly List<float> pttBuffer = new List<float>();

        void Awake()
        {
            if (resolver == null) resolver = GetComponent<SemanticIntentResolver>();
            if (whisper == null) whisper = GetComponent<WhisperClient>();

            vad = new VoiceActivityDetector(SampleRate, maxUtteranceMs: 14000); // Whisper is run on a ~15 s window
            vad.OnSpeechStarted += () => { if (!muted) SetState(VoiceState.Hearing); };
            vad.OnUtterance += clip => Enqueue(clip);
        }

        void OnEnable()
        {
            if (useMicrophone) StartMicrophone();
            else SetState(VoiceState.Listening);
        }

        void OnDisable()
        {
            if (micClip != null)
            {
                Microphone.End(device);
                micClip = null;
            }
            SetState(VoiceState.Off);
        }

        void Start()
        {
            if (whisper != null) whisper.WarmUp();
        }

        void StartMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                SetState(VoiceState.NoMicrophone);
                Fail("No microphone found.");
                return;
            }

            device = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;
            micClip = Microphone.Start(device, true, 10, SampleRate);
            micReadPos = 0;
            SetState(VoiceState.Listening);
        }

        void Update()
        {
            if (micClip == null) return;

            int pos = Microphone.GetPosition(device);
            if (pos < 0 || pos == micReadPos) return;

            int total = micClip.samples;
            if (pos > micReadPos)
            {
                ReadMic(micReadPos, pos - micReadPos);
            }
            else
            {
                ReadMic(micReadPos, total - micReadPos); // the buffer wrapped around
                if (pos > 0) ReadMic(0, pos);
            }
            micReadPos = pos;
        }

        void ReadMic(int offset, int count)
        {
            var buffer = new float[count * micClip.channels];
            micClip.GetData(buffer, offset);
            FeedSamples(AudioUtil.ToMono(buffer, micClip.channels), micClip.frequency);
        }

        // Where audio enters. The microphone loop calls this; so can a test with recorded speech.
        public void FeedSamples(float[] samples, int sampleRate)
        {
            if (sampleRate != SampleRate) samples = AudioUtil.Resample(samples, sampleRate, SampleRate);
            if (muted) return;

            if (mode == ListenMode.PushToTalk)
            {
                if (pttHeld) pttBuffer.AddRange(samples);
                return;
            }

            vad.Feed(samples, samples.Length);
        }

        public void BeginPushToTalk()
        {
            if (mode != ListenMode.PushToTalk || muted) return;
            pttHeld = true;
            pttBuffer.Clear();
            SetState(VoiceState.Hearing);
        }

        public void EndPushToTalk()
        {
            if (!pttHeld) return;
            pttHeld = false;

            if (pttBuffer.Count >= SampleRate / 4) Enqueue(pttBuffer.ToArray()); // ignore taps under 0.25 s
            pttBuffer.Clear();
            SetState(IdleState());
        }

        void Enqueue(float[] clip)
        {
            if (muted) return;
            queue.Enqueue(clip);
            ProcessNext();
        }

        void ProcessNext()
        {
            if (busy || queue.Count == 0 || whisper == null) return;

            busy = true;
            SetState(VoiceState.Transcribing);
            whisper.Transcribe(queue.Dequeue(), OnText, Fail);
        }

        void OnText(string text)
        {
            busy = false;

            string clean = TranscriptFilter.Clean(text);
            if (clean != null)
            {
                LastTranscript = clean;
                OnTranscript?.Invoke(clean);
                if (resolver != null) resolver.SubmitUtterance(clean);
            }

            SetState(IdleState());
            ProcessNext();
        }

        void Fail(string message)
        {
            busy = false;
            Debug.LogWarning("[ROVR voice] " + message);
            OnError?.Invoke(message);
            SetState(IdleState());
            ProcessNext();
        }

        VoiceState IdleState()
        {
            if (State == VoiceState.NoMicrophone || State == VoiceState.Off && !isActiveAndEnabled) return State;
            if (busy || queue.Count > 0) return VoiceState.Transcribing;
            if (pttHeld || (vad != null && vad.IsSpeaking)) return VoiceState.Hearing;
            return VoiceState.Listening;
        }

        void SetState(VoiceState next)
        {
            if (next == State) return;
            State = next;
            OnStateChanged?.Invoke(next);
        }
    }
}
