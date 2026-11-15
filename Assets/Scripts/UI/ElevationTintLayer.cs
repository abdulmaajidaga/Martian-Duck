using UnityEngine;

/// <summary>
/// Data layer: colours the terrain by elevation (low = blue, high = red/white), drawn as one semi-transparent
/// decal over the whole terrain so it shows in both the walking view and map mode.
/// </summary>
public class ElevationTintLayer : TerrainTintLayer
{
    [SerializeField] Gradient colours = DefaultGradient();

    float minElev, maxElev;
    bool built;

    public override string Legend => !built ? "low  -  high"
        : $"{minElev:0} m (blue)  to  {maxElev:0} m (red)";

    protected override bool FillTint(MarsGeoReference geo, int res, Color[] px)
    {
        TerrainData td = geo.terrain.terrainData;
        Vector3 size = td.size;

        // Colour every texel from the terrain's normalised height (0..1 of the height range).
        float lo = 1f, hi = 0f;
        var h = new float[res * res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float v = td.GetInterpolatedHeight((x + 0.5f) / res, (y + 0.5f) / res) / size.y;
                h[y * res + x] = v; lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
            }
        for (int i = 0; i < px.Length; i++)
        {
            Color c = colours.Evaluate(Mathf.InverseLerp(lo, hi, h[i]));
            c.a = opacity;
            px[i] = c;
        }
        minElev = geo.minElevation + lo * size.y;
        maxElev = geo.minElevation + hi * size.y;
        built = true;
        return true;
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
