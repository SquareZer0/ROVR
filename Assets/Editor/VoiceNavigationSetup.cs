// Assets/Editor/VoiceNavigationSetup.cs
// Menu: Tools > ROVR > Set Up Voice Navigation
// Prepares the player in the open scene for voice + LLM navigation. Run it once; running it again
// is harmless. Every change can be undone (Ctrl+Z) and is listed in the Console.
//
//   1. Finds the player: the object with WhisperVoiceMovement (or else the one with a CharacterController).
//   2. Adds and connects FOVMetadataGrounding, NavigationController, OllamaClient,
//      SemanticIntentResolver and ROVRDebugConsole, and points WhisperVoiceMovement at them.
//   3. Fixes the rig so the navigation code works:
//      - the player's feet go on the floor and the CharacterController rests on it (a capsule sunk into
//        the floor jumps up the first time it moves);
//      - the XR rig's origin goes to the feet, so eye height comes from the headset, not a fixed offset
//        (an eye above the house's 3 m walls means the LLM sees nothing in the house).
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ROVR;

public static class VoiceNavigationSetup
{
    [MenuItem("Tools/ROVR/Set Up Voice Navigation")]
    static void RunFromMenu()
    {
        string report = Run();
        Debug.Log(report);
        if (!Application.isBatchMode) EditorUtility.DisplayDialog("ROVR voice navigation", report, "OK");
    }

    public static string Run()
    {
        var log = new StringBuilder("[ROVR setup]\n");

        var voice = Object.FindFirstObjectByType<WhisperVoiceMovement>();
        GameObject player = voice != null ? voice.gameObject : null;
        if (player == null)
        {
            var controller = Object.FindFirstObjectByType<CharacterController>();
            if (controller != null) player = controller.gameObject;
        }
        if (player == null)
            return log + "No player found. Add WhisperVoiceMovement (or a CharacterController) to your player object first.";

        Camera head = player.GetComponentInChildren<Camera>(true);
        if (head == null) head = Camera.main;
        if (head == null)
            return log + "No camera found under '" + player.name + "' or tagged MainCamera.";

        log.AppendLine("Player: '" + player.name + "', head camera: '" + head.name + "'");

        FixRig(player, head, log);
        Wire(player, head, voice, log);

        EditorSceneManager.MarkSceneDirty(player.scene);
        log.AppendLine("Done. Save the scene, and have Ollama running (gemma3n:e4b) before pressing Play.");
        return log.ToString();
    }

    static void FixRig(GameObject player, Camera head, StringBuilder log)
    {
        var cc = player.GetComponent<CharacterController>();
        if (cc == null)
        {
            cc = Undo.AddComponent<CharacterController>(player);
            log.AppendLine("Added a CharacterController.");
        }

        Physics.SyncTransforms();
        float? floor = FindFloor(player);
        if (floor.HasValue)
        {
            float dy = floor.Value - player.transform.position.y;
            if (Mathf.Abs(dy) > 0.001f)
            {
                Undo.RecordObject(player.transform, "ROVR setup");
                player.transform.position += new Vector3(0f, dy, 0f);
                log.AppendLine("Moved the player's feet onto the floor at y " + floor.Value.ToString("F2") + " (by " + dy.ToString("+0.00;-0.00") + " m).");
            }
        }
        else
        {
            log.AppendLine("WARNING: no floor found under the player, so its height was left alone.");
        }

        var center = new Vector3(0f, cc.height / 2f + cc.skinWidth, 0f);
        if ((cc.center - center).sqrMagnitude > 1e-6f)
        {
            Undo.RecordObject(cc, "ROVR setup");
            cc.center = center;
            log.AppendLine("Rested the CharacterController on the floor (center y " + center.y.ToString("F2") + ").");
        }

        // Everything between the player and the camera (XR rig, camera offset) belongs at the feet;
        // the XR rig and head tracking add the eye height at runtime.
        for (Transform t = head.transform.parent; t != null && t != player.transform; t = t.parent)
        {
            if (Mathf.Abs(t.localPosition.y) < 0.001f) continue;
            Undo.RecordObject(t, "ROVR setup");
            log.AppendLine("Moved '" + t.name + "' from local height " + t.localPosition.y.ToString("F2") + " to 0, so the XR rig's origin is at the player's feet.");
            t.localPosition = new Vector3(t.localPosition.x, 0f, t.localPosition.z);
        }
        ReportEyeHeight(player, head, log);

        Physics.SyncTransforms();
        Vector3 c = player.transform.TransformPoint(cc.center);
        float half = Mathf.Max(0f, cc.height / 2f - cc.radius);
        foreach (var hit in Physics.OverlapCapsule(c + Vector3.up * half, c - Vector3.up * half, cc.radius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit == cc || hit.transform.IsChildOf(player.transform)) continue;
            log.AppendLine("WARNING: the player overlaps '" + hit.name + "'. Move it to open floor (e.g. a world's Start point).");
            break;
        }
    }

    static float? FindFloor(GameObject player)
    {
        Vector3 origin = player.transform.position + Vector3.up * 1.5f;
        float best = float.NegativeInfinity;
        foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(player.transform) && hit.point.y > best) best = hit.point.y;
        return float.IsNegativeInfinity(best) ? (float?)null : best;
    }

    static void ReportEyeHeight(GameObject player, Camera head, StringBuilder log)
    {
        for (Transform t = head.transform.parent; t != null && t != player.transform.parent; t = t.parent)
            foreach (var component in t.GetComponents<MonoBehaviour>())
            {
                if (component == null) continue;
                var so = new SerializedObject(component);
                var offset = so.FindProperty("m_CameraYOffset");
                var mode = so.FindProperty("m_TrackingOriginMode");
                if (offset == null || mode == null) continue;
                log.AppendLine("XR rig '" + t.name + "': in Floor tracking mode, eye height comes from the headset. In Device mode it is fixed at " +
                               offset.floatValue.ToString("F2") + " m (the rig's Camera Y Offset).");
                return;
            }
    }

    static void Wire(GameObject player, Camera head, WhisperVoiceMovement voice, StringBuilder log)
    {
        var grounding = Ensure<FOVMetadataGrounding>(player, log);
        var navigation = Ensure<NavigationController>(player, log);
        var ollama = Ensure<OllamaClient>(player, log);
        var resolver = Ensure<SemanticIntentResolver>(player, log);
        var console = Ensure<ROVRDebugConsole>(player, log);

        Set(grounding, "pov", head.transform);
        Set(navigation, "pov", head.transform);
        Set(navigation, "grounding", grounding);
        Set(resolver, "ollama", ollama);
        Set(resolver, "grounding", grounding);
        Set(resolver, "controller", navigation);
        Set(console, "resolver", resolver);

        if (voice == null)
        {
            log.AppendLine("No WhisperVoiceMovement on the player, so voice input isn't connected (typed commands still work).");
            return;
        }

        Undo.RecordObject(voice, "ROVR setup");
        voice.resolver = resolver;
        if (voice.whisperManager == null) voice.whisperManager = Object.FindFirstObjectByType<Whisper.WhisperManager>();
        if (voice.microphoneRecord == null) voice.microphoneRecord = Object.FindFirstObjectByType<Whisper.Utils.MicrophoneRecord>();
        if (voice.whisperManager == null) log.AppendLine("WARNING: no WhisperManager in the scene.");
        if (voice.microphoneRecord == null) log.AppendLine("WARNING: no MicrophoneRecord in the scene.");
        log.AppendLine("Connected WhisperVoiceMovement to the LLM pipeline.");
    }

    static T Ensure<T>(GameObject go, StringBuilder log) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c != null) return c;
        c = Undo.AddComponent<T>(go);
        log.AppendLine("Added " + typeof(T).Name + ".");
        return c;
    }

    static void Set(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning("[ROVR setup] " + target.GetType().Name + " has no field '" + field + "'."); return; }
        p.objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }
}
