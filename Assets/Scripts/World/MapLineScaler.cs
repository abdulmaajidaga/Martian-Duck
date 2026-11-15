using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps the LineRenderers under this object (e.g. Perseverance's drive path) readable on the map: while the
/// top-down map camera is active their width is set to a fixed number of screen pixels, and their normal
/// on-the-ground width comes back when walking. The lines are also raised while the map is open, because the map
/// camera draws the terrain with less detail and hills would otherwise cover a line lying just above the ground.
/// While raised they are moved to a layer only the map camera draws, so the first-person view seen behind the map
/// doesn't show them floating in the sky. Lines created later (rebuilt overlays) are picked up automatically.
/// </summary>
public class MapLineScaler : MonoBehaviour
{
    [Tooltip("Line width on the map, in screen pixels.")]
    [SerializeField, Min(0.5f)] float mapWidthPixels = 3f;
    [Tooltip("How far the lines are raised while the map is open (m).")]
    [SerializeField, Min(0f)] float mapLift = 60f;
    [Tooltip("Unused layer the lines move to while the map is open; other cameras stop drawing it meanwhile.")]
    [SerializeField, Range(0, 31)] int mapOnlyLayer = 31;

    readonly Dictionary<LineRenderer, float> groundWidths = new Dictionary<LineRenderer, float>();
    readonly Dictionary<LineRenderer, Vector3[]> groundPositions = new Dictionary<LineRenderer, Vector3[]>();
    readonly Dictionary<GameObject, int> groundLayers = new Dictionary<GameObject, int>();
    readonly Dictionary<Camera, int> cameraMasks = new Dictionary<Camera, int>();
    LineRenderer[] lines = new LineRenderer[0];
    bool onMap;
    float lastWidth = -1f;

    void OnEnable() => Refresh();

    void OnDisable()
    {
        if (onMap) Restore();
        onMap = false;
    }

    void Refresh()
    {
        lines = GetComponentsInChildren<LineRenderer>(true);
        foreach (LineRenderer lr in lines)
            if (lr != null && !groundWidths.ContainsKey(lr)) groundWidths[lr] = lr.widthMultiplier;
        lastWidth = -1f;
    }

    void LateUpdate()
    {
        Camera mapCam = MapClickRoutePlanner.MapCamera();
        if (mapCam == null)
        {
            if (onMap) { Restore(); onMap = false; }
            return;
        }
        if (!onMap) { Refresh(); Lift(mapCam); onMap = true; }

        float metresPerPixel = 2f * mapCam.orthographicSize / Mathf.Max(1, mapCam.pixelHeight);
        float width = mapWidthPixels * metresPerPixel;
        if (Mathf.Abs(width - lastWidth) < lastWidth * 0.01f) return;   // only touch the lines when the zoom changes
        lastWidth = width;
        foreach (LineRenderer lr in lines)
            if (lr != null) lr.widthMultiplier = Mathf.Max(width, groundWidths.TryGetValue(lr, out float g) ? g : 0f);
    }

    void Lift(Camera mapCam)
    {
        groundPositions.Clear();
        groundLayers.Clear();
        cameraMasks.Clear();
        int bit = 1 << mapOnlyLayer;
        foreach (Camera c in Camera.allCameras)
            if (c != mapCam) { cameraMasks[c] = c.cullingMask; c.cullingMask &= ~bit; }
        foreach (LineRenderer lr in lines)
            if (lr != null && !groundLayers.ContainsKey(lr.gameObject)) { groundLayers[lr.gameObject] = lr.gameObject.layer; lr.gameObject.layer = mapOnlyLayer; }
        if (mapLift <= 0f) return;
        Vector3 up = Vector3.up * mapLift;
        foreach (LineRenderer lr in lines)
        {
            if (lr == null || !lr.useWorldSpace || lr.positionCount == 0) continue;
            var pts = new Vector3[lr.positionCount];
            lr.GetPositions(pts);
            groundPositions[lr] = pts;
            var lifted = new Vector3[pts.Length];
            for (int i = 0; i < pts.Length; i++) lifted[i] = pts[i] + up;
            lr.SetPositions(lifted);
        }
    }

    void Restore()
    {
        foreach (var kv in groundWidths)
            if (kv.Key != null) kv.Key.widthMultiplier = kv.Value;
        foreach (var kv in groundPositions)
            if (kv.Key != null && kv.Key.positionCount == kv.Value.Length) kv.Key.SetPositions(kv.Value);
        groundPositions.Clear();
        foreach (var kv in groundLayers)
            if (kv.Key != null) kv.Key.layer = kv.Value;
        groundLayers.Clear();
        foreach (var kv in cameraMasks)
            if (kv.Key != null) kv.Key.cullingMask = kv.Value;
        cameraMasks.Clear();
        lastWidth = -1f;
    }
}
