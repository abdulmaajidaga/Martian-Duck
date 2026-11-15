using System;
using UnityEngine;

/// <summary>
/// Surface dust for the path planner, from the global TES Dust Cover Index (DCI, Ruff &amp; Christensen 2002),
/// cropped to the map by <c>Planning ▸ Import Dust Index</c> (see Planning/Editor/DustIndexImporter.cs).
///
/// The DCI is a thermal-emission index: high values (~0.97+) mean bare rock / little dust, low values (~0.94 and
/// below) mean a mantle of fine dust. Dusty ground is slower and riskier to walk on (soft footing, poor traction,
/// dust on the suit and visor), so the planner adds a cost for it.
///
/// The data is coarse: 16 px/degree, about 3.6 km per pixel here, so it steers routes between broad areas of the
/// map rather than around individual dunes. Values are interpolated bilinearly.
/// </summary>
[DisallowMultipleComponent]
public class MarsDustMap : MonoBehaviour
{
    public enum Scale
    {
        [Tooltip("Stretch the dust values found on this map to 0..1 (dustiest 5% = 1, cleanest 5% = 0).")]
        RelativeToMap,
        [Tooltip("Use the global DCI thresholds (Dusty DCI = 1, Clear DCI = 0).")]
        Absolute,
    }

    [Tooltip("Cropped DCI grid (JSON) made by Planning ▸ Import Dust Index.")]
    [SerializeField] TextAsset dustIndexJson;
    [SerializeField] Scale scale = Scale.RelativeToMap;
    [Tooltip("DCI at or below this counts as fully dusty (Absolute scale).")]
    [SerializeField] float dustyDci = 0.94f;
    [Tooltip("DCI at or above this counts as dust-free (Absolute scale).")]
    [SerializeField] float clearDci = 0.975f;

    [Serializable]
    public class Grid
    {
        public string source;
        public string note;
        public float pixelsPerDegree = 16f;
        public double westEdge;          // longitude of the west edge of column 0
        public double northEdge;         // latitude of the north edge of row 0
        public int cols, rows;
        public float noData = 0.85f;
        public float[] values;           // row-major, rows north -> south
    }

    static MarsDustMap current;
    public static MarsDustMap Current
    {
        get
        {
            if (current == null) current = FindAnyObjectByType<MarsDustMap>();
            return current;
        }
    }

    Grid grid;
    float lo, hi;        // DCI range mapped to dustiness 1..0
    float fallbackDci;   // used where the data has no value

    public bool Loaded => EnsureLoaded();
    public Grid Data => EnsureLoaded() ? grid : null;

    void OnEnable() => current = this;
    void OnValidate() => grid = null;    // reload with the new settings

    bool EnsureLoaded()
    {
        if (grid != null) return true;
        if (dustIndexJson == null) return false;
        try { grid = JsonUtility.FromJson<Grid>(dustIndexJson.text); }
        catch (Exception e) { Debug.LogError($"[Dust] Could not read {dustIndexJson.name}: {e.Message}", this); grid = null; return false; }
        if (grid == null || grid.values == null || grid.values.Length != grid.cols * grid.rows || grid.cols < 2 || grid.rows < 2)
        {
            Debug.LogError($"[Dust] {dustIndexJson.name} is not a valid dust grid", this);
            grid = null; return false;
        }

        var valid = new System.Collections.Generic.List<float>(grid.values.Length);
        foreach (float v in grid.values) if (IsValid(v)) valid.Add(v);
        if (valid.Count == 0) { fallbackDci = clearDci; lo = dustyDci; hi = clearDci; return true; }
        valid.Sort();
        fallbackDci = valid[valid.Count / 2];
        if (scale == Scale.RelativeToMap)
        {
            lo = valid[Mathf.FloorToInt((valid.Count - 1) * 0.05f)];
            hi = valid[Mathf.CeilToInt((valid.Count - 1) * 0.95f)];
            if (hi - lo < 1e-4f) { lo -= 0.005f; hi += 0.005f; }
        }
        else { lo = dustyDci; hi = Mathf.Max(clearDci, dustyDci + 1e-4f); }
        return true;
    }

    bool IsValid(float v) => !float.IsNaN(v) && v > grid.noData + 1e-4f && v < 1.2f;

    /// <summary>Raw Dust Cover Index at a longitude/latitude (bilinear; NaN if no data is loaded).</summary>
    public float DciAt(double longitude, double latitude)
    {
        if (!EnsureLoaded()) return float.NaN;
        float fx = (float)((longitude - grid.westEdge) * grid.pixelsPerDegree) - 0.5f;
        float fy = (float)((grid.northEdge - latitude) * grid.pixelsPerDegree) - 0.5f;
        fx = Mathf.Clamp(fx, 0f, grid.cols - 1.001f);
        fy = Mathf.Clamp(fy, 0f, grid.rows - 1.001f);
        int x = (int)fx, y = (int)fy;
        float tx = fx - x, ty = fy - y;
        float sum = 0f, wsum = 0f;
        Accumulate(x, y, (1 - tx) * (1 - ty));
        Accumulate(x + 1, y, tx * (1 - ty));
        Accumulate(x, y + 1, (1 - tx) * ty);
        Accumulate(x + 1, y + 1, tx * ty);
        return wsum > 1e-6f ? sum / wsum : fallbackDci;

        void Accumulate(int cx, int cy, float w)
        {
            float v = grid.values[cy * grid.cols + cx];
            if (!IsValid(v) || w <= 0f) return;
            sum += v * w; wsum += w;
        }
    }

    /// <summary>Dustiness 0 (clean) .. 1 (dusty) at a longitude/latitude, using the chosen scale.</summary>
    public float DustinessAt(double longitude, double latitude)
    {
        float dci = DciAt(longitude, latitude);
        if (float.IsNaN(dci)) return 0f;
        return Mathf.Clamp01((hi - dci) / (hi - lo));
    }

    /// <summary>Dustiness 0 (clean) .. 1 (dusty) at a world position.</summary>
    public float DustinessAt(Vector3 world)
    {
        MarsGeoReference geo = MarsGeoReference.Current;
        if (geo == null) return 0f;
        geo.WorldToLonLat(world, out double lon, out double lat);
        return DustinessAt(lon, lat);
    }
}
