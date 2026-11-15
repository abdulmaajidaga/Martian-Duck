using System;
using UnityEngine;

/// <summary>
/// Draws Perseverance's drive path, the sample depot zone and sampling sites onto the Mars terrain.
/// Positions come from a JSON TextAsset (made from the mission GeoJSON files) stored as fractions of the
/// terrain (x west->east 0..1, y south->north 0..1), so they stay correct if the terrain is re-baked or resized.
/// Use the component's context menu "Build Overlays" (Edit Mode) to (re)create the objects; they are saved with the scene.
/// </summary>
[DisallowMultipleComponent]
public class MarsRouteOverlay : MonoBehaviour
{
    [Serializable] class Drive { public int sol; public float length_m; public float[] points; }
    [Serializable] class Sample { public string name; public string date; public string sol; public string location; public string type; public float x; public float y; }
    [Serializable] class OverlayData { public Drive[] drives; public float[] depot_zone; public Sample[] samples; }

    const string LogPrefix = "[MarsRouteOverlay] ";
    static readonly string[] GroupNames = { "Drive Path", "Depot Zone", "Sampling Sites" };

    [Header("Data")]
    [Tooltip("three_forks_overlays.json (terrain-relative positions).")]
    [SerializeField] TextAsset overlayData;
    [Tooltip("Terrain to drape onto. If empty, the first Terrain in the scene is used.")]
    [SerializeField] Terrain terrain;

    [Header("Appearance")]
    [SerializeField] Material pathMaterial;
    [SerializeField] Material depotMaterial;
    [SerializeField] Material markerMaterial;
    [Tooltip("Width of the drive path line in metres (Perseverance's wheels are about 2.7 m apart).")]
    [SerializeField, Min(0.05f)] float pathWidth = 0.5f;
    [SerializeField, Min(0.05f)] float depotLineWidth = 0.4f;
    [Tooltip("How far lines float above the ground to avoid flickering with the terrain.")]
    [SerializeField, Min(0f)] float heightOffset = 0.25f;
    [Tooltip("Maximum spacing between line points in metres, so lines follow the ground instead of cutting through bumps.")]
    [SerializeField, Min(0.1f)] float maxPointSpacing = 0.5f;
    [SerializeField, Min(0.5f)] float markerHeight = 4f;
    [SerializeField] bool showLabels = true;
    [SerializeField, Min(0.01f)] float labelSize = 0.12f;

    // Visibility: switch the whole overlay, or its "Drive Path" / "Depot Zone" / "Sampling Sites" children, on or
    // off with their checkboxes in the Hierarchy/Inspector. This script never changes them after building, so the
    // state you set is what you get in Play mode. (The object is tagged EditorOnly, so it is left out of builds.)

    [ContextMenu("Build Overlays")]
    public void Build()
    {
        if (overlayData == null) { Debug.LogError(LogPrefix + "No Overlay Data assigned.", this); return; }
        if (terrain == null) terrain = FindAnyObjectByType<Terrain>();
        if (terrain == null) { Debug.LogError(LogPrefix + "No Terrain found to drape onto.", this); return; }

        OverlayData data;
        try { data = JsonUtility.FromJson<OverlayData>(overlayData.text); }
        catch (Exception e) { Debug.LogError(LogPrefix + "Could not read overlay data: " + e.Message, this); return; }

        Clear();
        Vector3 size = terrain.terrainData.size;
        Vector3 origin = terrain.transform.position;

        Vector3 Ground(float nx, float ny)
        {
            var p = new Vector3(origin.x + nx * size.x, 0f, origin.z + ny * size.z);
            p.y = origin.y + terrain.SampleHeight(p) + heightOffset;
            return p;
        }

        // Turn a flat [x0,y0,x1,y1,...] list into ground points no more than maxPointSpacing apart.
        Vector3[] Drape(float[] flat, bool closed)
        {
            var result = new System.Collections.Generic.List<Vector3>();
            int n = flat.Length / 2;
            int segments = closed ? n : n - 1;
            for (int i = 0; i < segments; i++)
            {
                float x0 = flat[2 * i], y0 = flat[2 * i + 1];
                float x1 = flat[2 * ((i + 1) % n)], y1 = flat[2 * ((i + 1) % n) + 1];
                float metres = Mathf.Sqrt(Mathf.Pow((x1 - x0) * size.x, 2) + Mathf.Pow((y1 - y0) * size.z, 2));
                int steps = Mathf.Max(1, Mathf.CeilToInt(metres / maxPointSpacing));
                for (int k = 0; k < steps; k++)
                    result.Add(Ground(Mathf.Lerp(x0, x1, (float)k / steps), Mathf.Lerp(y0, y1, (float)k / steps)));
            }
            if (!closed) result.Add(Ground(flat[2 * (n - 1)], flat[2 * (n - 1) + 1]));
            return result.ToArray();
        }

        int lines = 0, points = 0;
        Transform drives = NewGroup(GroupNames[0]);
        foreach (Drive d in data.drives ?? Array.Empty<Drive>())
        {
            if (d.points == null || d.points.Length < 4) continue;
            var go = new GameObject($"Sol {d.sol}");
            go.transform.SetParent(drives, false);
            Vector3[] pos = Drape(d.points, false);
            AddLine(go, pathMaterial, pathWidth, pos, false);
            lines++; points += pos.Length;
        }

        Transform depot = NewGroup(GroupNames[1]);
        if (data.depot_zone != null && data.depot_zone.Length >= 6)
        {
            var go = new GameObject("Three Forks Sample Depot");
            go.transform.SetParent(depot, false);
            AddLine(go, depotMaterial, depotLineWidth, Drape(data.depot_zone, true), true);
        }

        Transform sites = NewGroup(GroupNames[2]);
        foreach (Sample s in data.samples ?? Array.Empty<Sample>())
            AddMarker(sites, s, Ground(s.x, s.y) - Vector3.up * heightOffset);

        Debug.Log($"{LogPrefix}Built {lines} drive lines ({points} points), depot zone and {data.samples?.Length ?? 0} sampling sites.", this);
    }

    [ContextMenu("Clear Overlays")]
    public void Clear()
    {
        foreach (string n in GroupNames)
        {
            Transform t = transform.Find(n);
            if (t == null) continue;
            if (Application.isPlaying) Destroy(t.gameObject); else DestroyImmediate(t.gameObject);
        }
    }

    Transform NewGroup(string groupName)
    {
        var go = new GameObject(groupName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    static void AddLine(GameObject go, Material mat, float width, Vector3[] positions, bool loop)
    {
        // Lay the ribbon flat on the ground: TransformZ alignment with the object's Z axis pointing down.
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.alignment = LineAlignment.TransformZ;
        lr.widthMultiplier = width;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
        lr.loop = loop;
        lr.sharedMaterial = mat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.positionCount = positions.Length;
        lr.SetPositions(positions);
    }

    void AddMarker(Transform parent, Sample s, Vector3 ground)
    {
        var root = new GameObject(s.name);
        root.transform.SetParent(parent, false);
        root.transform.position = ground;

        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        RemoveCollider(pole);
        pole.name = "Pole";
        pole.transform.SetParent(root.transform, false);
        pole.transform.localPosition = new Vector3(0f, markerHeight * 0.5f, 0f);
        pole.transform.localScale = new Vector3(0.12f, markerHeight * 0.5f, 0.12f);
        SetMaterial(pole, markerMaterial);

        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        RemoveCollider(ball);
        ball.name = "Top";
        ball.transform.SetParent(root.transform, false);
        ball.transform.localPosition = new Vector3(0f, markerHeight, 0f);
        ball.transform.localScale = Vector3.one * 0.6f;
        SetMaterial(ball, markerMaterial);

        if (!showLabels) return;
        var label = new GameObject("Label");
        label.transform.SetParent(root.transform, false);
        label.transform.localPosition = new Vector3(0f, markerHeight + 0.9f, 0f);
        var tm = label.AddComponent<TextMesh>();
        tm.text = $"{s.name}\n{s.date}";
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 64;
        tm.characterSize = labelSize;
        tm.color = Color.white;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            tm.font = font;
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
        label.AddComponent<MarsBillboard>();
    }

    static void RemoveCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) DestroyImmediate(c);
    }

    static void SetMaterial(GameObject go, Material mat)
    {
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
    }
}
