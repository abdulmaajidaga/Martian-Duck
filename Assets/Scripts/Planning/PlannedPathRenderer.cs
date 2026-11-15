using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the latest successful plan in the world: a thin line just above the ground while walking, and a wide line
/// (constant width in screen pixels) when the top-down map camera is active, so the route shows on the map.
/// Planning components call <see cref="ShowPlan"/>; with Draw Every Plan it also draws any plan from
/// MarsPathPlanner.PlanCompleted (e.g. plans the UI asks for). Needs no UI code.
/// </summary>
public class PlannedPathRenderer : MonoBehaviour
{
    [SerializeField] Material lineMaterial;
    [SerializeField] Color color = new Color(1f, 0.55f, 0.1f, 1f);
    [Tooltip("Line width on the ground (m) while walking.")]
    [SerializeField, Min(0.05f)] float groundWidth = 0.6f;
    [SerializeField] float groundLift = 0.4f;
    [Tooltip("Line width on the map (screen pixels).")]
    [SerializeField, Min(1f)] float mapWidthPixels = 4f;
    [Tooltip("Lift above the ground on the map so hills next to the path don't hide it (m).")]
    [SerializeField] float mapLift = 60f;
    [Tooltip("Keep at most this many points per line (long routes are thinned).")]
    [SerializeField, Min(100)] int maxLinePoints = 8000;
    [Tooltip("Also draw every successful plan anyone requests (not only the ones the planning components show).")]
    [SerializeField] bool drawEveryPlan;

    static readonly List<PlannedPathRenderer> all = new List<PlannedPathRenderer>();
    LineRenderer ground, map;

    void OnEnable()
    {
        all.Add(this);
        MarsPathPlanner.PlanCompleted += OnPlan;
    }

    void OnDisable()
    {
        all.Remove(this);
        MarsPathPlanner.PlanCompleted -= OnPlan;
    }

    /// <summary>Draw this plan (if it succeeded) on every renderer.</summary>
    public static void ShowPlan(PlannedPath plan)
    {
        if (plan == null || !plan.success) return;
        foreach (PlannedPathRenderer r in all) r.Show(plan.points);
    }

    /// <summary>Hide the drawn route.</summary>
    public static void ClearAll()
    {
        foreach (PlannedPathRenderer r in all) r.Show(null);
    }

    void OnPlan(PlannedPath plan)
    {
        if (drawEveryPlan && plan != null && plan.success) Show(plan.points);
    }

    public void Show(List<Vector3> points)
    {
        EnsureLines();
        if (points == null || points.Count < 2) { ground.positionCount = 0; map.positionCount = 0; return; }
        int stride = Mathf.Max(1, Mathf.CeilToInt(points.Count / (float)maxLinePoints));
        var g = new List<Vector3>(points.Count / stride + 2);
        var m = new List<Vector3>(g.Capacity);
        for (int i = 0; i < points.Count; i += stride) Add(points[i]);
        if ((points.Count - 1) % stride != 0) Add(points[points.Count - 1]);
        ground.positionCount = g.Count; ground.SetPositions(g.ToArray());
        map.positionCount = m.Count; map.SetPositions(m.ToArray());

        void Add(Vector3 p) { g.Add(p + Vector3.up * groundLift); m.Add(p + Vector3.up * mapLift); }
    }

    void LateUpdate()
    {
        if (ground == null) return;
        Camera mapCam = MapClickRoutePlanner.MapCamera();
        bool onMap = mapCam != null;
        ground.enabled = !onMap && ground.positionCount > 1;
        map.enabled = onMap && map.positionCount > 1;
        if (onMap)
        {
            float metresPerPixel = 2f * mapCam.orthographicSize / Mathf.Max(1, mapCam.pixelHeight);
            map.widthMultiplier = mapWidthPixels * metresPerPixel;
        }
    }

    void EnsureLines()
    {
        if (ground != null) return;
        ground = NewLine("Planned Path (ground)", groundWidth);
        map = NewLine("Planned Path (map)", 10f);
    }

    LineRenderer NewLine(string name, float width)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = width;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.textureMode = LineTextureMode.Stretch;
        if (lineMaterial != null) lr.sharedMaterial = lineMaterial;
        lr.startColor = lr.endColor = color;
        lr.positionCount = 0;
        lr.enabled = false;
        return lr;
    }
}
