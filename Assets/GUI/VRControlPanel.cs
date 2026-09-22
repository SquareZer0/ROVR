using UnityEngine;
using UnityEngine.UI;
using Whisper.Utils;

public class VRControlPanel : MonoBehaviour
{
    [Header("References")]
    public Transform playerBody; // Your root PlayerBody object
    public MicrophoneRecord microphoneRecord;

    [Header("Teleport Destinations (Assign Transforms in Scene)")]
    public Transform plainDestination;
    public Transform mazeDestination;
    public Transform houseDestination;

    [Header("UI Buttons")]
    public Button plainButton;
    public Button mazeButton;
    public Button houseButton;
    public Button micMuteButton;

    private bool _isMicMuted = false;
    private Text micButtonText;

    private void Start()
    {
        if (playerBody == null) playerBody = transform;

        // Hook up button listeners
        if (plainButton != null) plainButton.onClick.AddListener(() => TeleportTo(plainDestination));
        if (mazeButton != null) mazeButton.onClick.AddListener(() => TeleportTo(mazeDestination));
        if (houseButton != null) houseButton.onClick.AddListener(() => TeleportTo(houseDestination));

        if (micMuteButton != null)
        {
            micButtonText = micMuteButton.GetComponentInChildren<Text>();
            micMuteButton.onClick.AddListener(ToggleMicMute);
        }
    }

    private void TeleportTo(Transform destination)
    {
        if (destination != null)
        {
            // Temporarily disable character controller if present, then move
            playerBody.position = destination.position;
            playerBody.rotation = destination.rotation;
            Debug.Log($"<color=blue><b>[Teleport]</b> Moved player to {destination.name}</color>");
        }
        else
        {
            Debug.LogWarning("[Teleport] Destination transform is not assigned!");
        }
    }

    private void ToggleMicMute()
    {
        if (microphoneRecord == null) return;

        _isMicMuted = !_isMicMuted;

        if (_isMicMuted)
        {
            if (microphoneRecord.IsRecording)
            {
                microphoneRecord.StopRecord();
            }
            microphoneRecord.enabled = false;
            Debug.Log("<color=yellow><b>[Mic Muted]</b> Microphone paused.</color>");
            
            if (micButtonText != null) micButtonText.text = "Unmute Mic";
        }
        else
        {
            microphoneRecord.enabled = true;
            microphoneRecord.StartRecord();
            Debug.Log("<color=green><b>[Mic Unmuted]</b> Microphone active.</color>");
            
            if (micButtonText != null) micButtonText.text = "Mute Mic";
        }
    }
}