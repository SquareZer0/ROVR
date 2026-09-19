// Assets/Scripts/ROVR/VoiceActivityDetector.cs
// The "always listening" part of voice input (Section 6.3.3). Runs continuously on the microphone
// stream, costs almost nothing, and cuts it into utterances: it notices when speech starts, keeps
// recording until the speaker has been quiet for a moment, and only then hands the finished clip
// on (to Whisper). Whisper itself never sees silence, which matters because it invents text
// ("Thank you.") when fed silence or noise.
//
// Plain C# with no Unity types, so it can be unit-tested. Feed it mono audio at the sample rate
// given to the constructor, in chunks of any size.
//
// It is an energy detector: speech is anything clearly louder than the background noise, which it
// learns from the first half second (so start it while the room is quiet) and keeps tracking. A
// sound that stays loud and perfectly steady for the whole maximum length (a fan switching on) is
// treated as the new background, not speech. It is not a speech model, so a short loud non-speech
// sound (a door slam) can pass as an utterance; the transcript filter downstream drops what Whisper
// hears as nothing.
using System;
using System.Collections.Generic;

namespace ROVR
{
    public class VoiceActivityDetector
    {
        public const int FrameMs = 20;

        readonly int frameSize;
        readonly int preRollFrames, startFrames, hangoverFrames, minSpeechFrames, maxFrames, tailFrames;
        readonly float minThreshold, thresholdRatio;

        readonly List<float> pending = new List<float>();
        readonly Queue<float[]> preRoll = new Queue<float[]>();
        readonly List<float[]> utterance = new List<float[]>();

        const int CalibrationFrames = 25;   // first 0.5 s sets the background level
        const int SteadyCheckFrames = 50;   // the last second, for spotting steady noise

        bool speaking;
        int aboveCount, silenceCount, speechFrames;
        int calibrationLeft = CalibrationFrames;
        float calibrationSum;
        float noiseFloor = 0.005f;

        public event Action OnSpeechStarted;
        public event Action<float[]> OnUtterance;

        public bool IsSpeaking => speaking;
        public float NoiseFloor => noiseFloor;
        public float LastRms { get; private set; }

        public VoiceActivityDetector(int sampleRate = 16000, int preRollMs = 300, int startMs = 100,
            int hangoverMs = 600, int minSpeechMs = 200, int maxUtteranceMs = 15000, int tailMs = 200,
            float minThreshold = 0.01f, float thresholdRatio = 3f)
        {
            frameSize = sampleRate * FrameMs / 1000;
            preRollFrames = preRollMs / FrameMs;
            startFrames = Math.Max(1, startMs / FrameMs);
            hangoverFrames = Math.Max(1, hangoverMs / FrameMs);
            minSpeechFrames = Math.Max(1, minSpeechMs / FrameMs);
            maxFrames = Math.Max(1, maxUtteranceMs / FrameMs);
            tailFrames = tailMs / FrameMs;
            this.minThreshold = minThreshold;
            this.thresholdRatio = thresholdRatio;
        }

        public void Reset()
        {
            pending.Clear();
            preRoll.Clear();
            utterance.Clear();
            speaking = false;
            aboveCount = silenceCount = speechFrames = 0;
        }

        public void Feed(float[] samples, int count)
        {
            for (int i = 0; i < count; i++) pending.Add(samples[i]);

            while (pending.Count >= frameSize)
            {
                var frame = new float[frameSize];
                pending.CopyTo(0, frame, 0, frameSize);
                pending.RemoveRange(0, frameSize);
                ProcessFrame(frame);
            }
        }

        void ProcessFrame(float[] frame)
        {
            float rms = AudioUtil.Rms(frame);
            LastRms = rms;

            if (calibrationLeft > 0)
            {
                calibrationSum += rms;
                if (--calibrationLeft == 0) noiseFloor = ClampFloor(calibrationSum / CalibrationFrames);
                return;
            }

            float threshold = Math.Max(minThreshold, noiseFloor * thresholdRatio);

            if (!speaking)
            {
                if (rms > threshold)
                {
                    aboveCount++;
                }
                else
                {
                    aboveCount = 0;
                    // Only quiet frames teach it what "background" is.
                    noiseFloor = ClampFloor(noiseFloor + (rms - noiseFloor) * 0.05f);
                }

                preRoll.Enqueue(frame);
                while (preRoll.Count > preRollFrames + startFrames) preRoll.Dequeue();

                if (aboveCount >= startFrames)
                {
                    speaking = true;
                    utterance.Clear();
                    utterance.AddRange(preRoll);
                    preRoll.Clear();
                    silenceCount = 0;
                    speechFrames = aboveCount;
                    OnSpeechStarted?.Invoke();
                }
                return;
            }

            utterance.Add(frame);

            // Lower bar to stay "speaking" than to start, so the quieter ends of words don't cut it off.
            if (rms > threshold * 0.6f) { silenceCount = 0; speechFrames++; }
            else silenceCount++;

            if (silenceCount >= hangoverFrames) Finish(false);
            else if (utterance.Count >= maxFrames) Finish(true);
        }

        static float ClampFloor(float v) { return Math.Min(0.05f, Math.Max(0.0005f, v)); }

        void Finish(bool hitMaxLength)
        {
            speaking = false;
            aboveCount = 0;

            // Speech rises and falls; a full-length recording that never does is background noise.
            // Adopt it as the new background and throw it away.
            if (hitMaxLength && IsSteady())
            {
                noiseFloor = ClampFloor(RecentMeanRms());
                utterance.Clear();
                silenceCount = 0;
                speechFrames = 0;
                return;
            }

            int drop = Math.Max(0, silenceCount - tailFrames); // keep a short natural tail, drop the rest
            if (drop > 0 && drop < utterance.Count) utterance.RemoveRange(utterance.Count - drop, drop);

            bool longEnough = speechFrames >= minSpeechFrames;
            float[] clip = null;
            if (longEnough)
            {
                clip = new float[utterance.Count * frameSize];
                for (int i = 0; i < utterance.Count; i++)
                    Array.Copy(utterance[i], 0, clip, i * frameSize, frameSize);
            }

            utterance.Clear();
            silenceCount = 0;
            speechFrames = 0;

            if (clip != null) OnUtterance?.Invoke(clip);
        }

        float RecentMeanRms()
        {
            int from = Math.Max(0, utterance.Count - SteadyCheckFrames);
            float sum = 0;
            for (int i = from; i < utterance.Count; i++) sum += AudioUtil.Rms(utterance[i]);
            return sum / Math.Max(1, utterance.Count - from);
        }

        bool IsSteady()
        {
            int from = Math.Max(0, utterance.Count - SteadyCheckFrames);
            float max = 0, sum = 0;
            for (int i = from; i < utterance.Count; i++)
            {
                float r = AudioUtil.Rms(utterance[i]);
                sum += r;
                if (r > max) max = r;
            }
            float mean = sum / Math.Max(1, utterance.Count - from);
            return max < 1.25f * mean; // steady noise varies ~5%; speech varies far more
        }
    }
}
