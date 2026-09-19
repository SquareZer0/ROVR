// Assets/Scripts/ROVR/WhisperClient.cs
// Speech-to-text for voice input. Talks to a locally hosted whisper.cpp server (see `Voice Server/`),
// so audio never leaves the machine (Section 6.3.1: no cloud dependency).
//
// Why a separate step at all: the thesis's plan of sending audio straight into Gemma does not work
// through Ollama, which rejects audio for gemma3n ("model does not support multimodal requests").
// So speech is transcribed here first and the text goes into the same pipeline as typed commands.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace ROVR
{
    public class WhisperClient : MonoBehaviour
    {
        // 127.0.0.1, not "localhost": on Windows "localhost" tries IPv6 first and adds ~2 s per request.
        [SerializeField] string endpoint = "http://127.0.0.1:8080/inference";
        [SerializeField] int timeoutSeconds = 20;
        // Optional vocabulary hint sent with each clip. It made no difference on our test commands, so
        // it's off; it may help with unusual words or accents.
        [SerializeField] string initialPrompt = "";

        [Serializable]
        class WhisperResponse
        {
            public string text;
            public string error;
        }

        public float LastLatencySeconds { get; private set; }

        string Endpoint => endpoint.Replace("//localhost", "//127.0.0.1");

        // `samples` are mono float audio at 16 kHz.
        public virtual void Transcribe(float[] samples, Action<string> onText, Action<string> onError)
        {
            StartCoroutine(TranscribeCoroutine(samples, onText, onError));
        }

        // The first request after the server starts pays one-off setup costs; spend it on silence now.
        public void WarmUp()
        {
            Transcribe(new float[VoiceInput.SampleRate], _ => { }, _ => { });
        }

        IEnumerator TranscribeCoroutine(float[] samples, Action<string> onText, Action<string> onError)
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", AudioUtil.EncodeWav16(samples, VoiceInput.SampleRate), "audio.wav", "audio/wav"),
                new MultipartFormDataSection("temperature", "0.0"),
                new MultipartFormDataSection("response_format", "json"),
                new MultipartFormDataSection("language", "en"),
            };
            if (!string.IsNullOrEmpty(initialPrompt))
                form.Add(new MultipartFormDataSection("prompt", initialPrompt));

            var request = UnityWebRequest.Post(Endpoint, form);
            try
            {
                request.timeout = timeoutSeconds;

                float sentAt = Time.realtimeSinceStartup;
                yield return request.SendWebRequest();
                LastLatencySeconds = Time.realtimeSinceStartup - sentAt;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke("Speech-to-text request failed (" + request.result + "): " + request.error +
                                    ". Is the Whisper server running? (Voice Server/start.ps1)");
                    yield break;
                }

                WhisperResponse response;
                try
                {
                    response = JsonUtility.FromJson<WhisperResponse>(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    onError?.Invoke("Could not read the Whisper response: " + e.Message + "\nRaw: " + request.downloadHandler.text);
                    yield break;
                }

                if (response == null || !string.IsNullOrEmpty(response.error))
                {
                    onError?.Invoke("Whisper returned an error: " + (response != null ? response.error : "empty response"));
                    yield break;
                }

                onText?.Invoke(response.text ?? string.Empty);
            }
            finally
            {
                request.Dispose();
            }
        }
    }
}
