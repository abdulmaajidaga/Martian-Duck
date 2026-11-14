using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Data layer: colours the terrain by elevation (low = blue, high = red/white), drawn as one semi-transparent
/// decal over the whole terrain so it shows in both the walking view and map mode.
/// </summary>
public class ElevationTintLayer : MarsDataLayer
{
    [Tooltip("Decal shader (Shader Graphs/Decal). Referenced here so it is included in builds.")]
    [SerializeField] Shader decalShader;
    [SerializeField, Range(0f, 1f)] float opacity = 0.55f;
    [SerializeField, Range(64, 1024)] int resolution = 512;
    [SerializeField] Gradient colours = DefaultGradient();

    DecalProjector projector;
    Material material;
    Texture2D tint;
    float minElev, maxElev;

    public override string Legend => tint == null ? "low  -  high"
        : $"{minElev:0} m (blue)  to  {maxElev:0} m (red)";

    protected override void ApplyState(bool on)
    {
        if (on && projector == null) Create();
        if (projector != null) projector.gameObject.SetActive(on);
    }

    void Create()
    {
        MarsGeoReference geo = MarsGeoReference.Current;
        if (geo == null || !geo.ResolveTerrain()) { Debug.LogWarning("[ElevationTintLayer] No terrain/MarsGeoReference found.", this); return; }
        Shader shader = decalShader != null ? decalShader : Shader.Find("Shader Graphs/Decal");
        if (shader == null) { Debug.LogWarning("[ElevationTintLayer] Decal shader not found.", this); return; }

        TerrainData td = geo.terrain.terrainData;
        Vector3 size = td.size;
        minElev = geo.minElevation;
        maxElev = geo.minElevation + size.y;

        // Colour every texel from the terrain's normalised height (0..1 of the height range).
        tint = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Elevation tint" };
        var px = new Color[resolution * resolution];
        float lo = 1f, hi = 0f;
        var h = new float[resolution * resolution];
        for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float v = td.GetInterpolatedHeight((x + 0.5f) / resolution, (y + 0.5f) / resolution) / size.y;
                h[y * resolution + x] = v; lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
            }
        for (int i = 0; i < px.Length; i++)
        {
            Color c = colours.Evaluate(Mathf.InverseLerp(lo, hi, h[i]));
            c.a = opacity;
            px[i] = c;
        }
        minElev = geo.minElevation + lo * size.y;
        maxElev = geo.minElevation + hi * size.y;
        tint.SetPixels(px);
        tint.Apply(true, true);

        material = new Material(shader) { name = "Elevation tint (runtime)" };
        material.SetTexture("Base_Map", tint);
        material.SetFloat("Normal_Blend", 0f);
        material.enableInstancing = true;

        var go = new GameObject("Elevation tint decal") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        Vector3 centre = geo.Origin + new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
        go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(Vector3.down, Vector3.forward));   // texture up = north
        projector = go.AddComponent<DecalProjector>();
        projector.material = material;
        projector.pivot = Vector3.zero;
        projector.size = new Vector3(size.x, size.z, size.y + 50f);
        projector.drawDistance = 100000f;
        projector.fadeScale = 1f;
        projector.startAngleFade = 89f;
        projector.endAngleFade = 90f;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (projector != null) Destroy(projector.gameObject);
        if (material != null) Destroy(material);
        if (tint != null) Destroy(tint);
        projector = null; material = null; tint = null;
    }

    static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(new[]
        {
            new GradientColorKey(new Color(0.15f, 0.25f, 0.85f), 0f),
            new GradientColorKey(new Color(0.10f, 0.75f, 0.85f), 0.25f),
            new GradientColorKey(new Color(0.30f, 0.85f, 0.30f), 0.5f),
            new GradientColorKey(new Color(0.95f, 0.85f, 0.20f), 0.75f),
            new GradientColorKey(new Color(0.90f, 0.20f, 0.15f), 1f),
        }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
