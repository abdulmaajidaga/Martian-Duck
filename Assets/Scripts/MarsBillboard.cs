using UnityEngine;

/// <summary>Keeps a label facing the active camera (Game view in Play Mode, Scene view camera in Edit Mode).</summary>
[ExecuteAlways]
public class MarsBillboard : MonoBehaviour
{
    void LateUpdate()
    {
        Camera cam = Camera.main;
#if UNITY_EDITOR
        if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null)
            cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
        if (cam == null) return;
        Vector3 away = transform.position - cam.transform.position;
        if (away.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(away, Vector3.up);
    }
}
