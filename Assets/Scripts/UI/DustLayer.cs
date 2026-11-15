using UnityEngine;

/// <summary>
/// Data layer: surface dust from the planner's <see cref="MarsDustMap"/> (TES Dust Cover Index), drawn over the
/// terrain. Clean rock is blue, thick dust is pale cream (chosen to stand out from the orange ground). The same
/// values steer the dust-aware planner, so this shows why routes bend around some areas. The data is coarse (~3.6 km per sample), so expect broad patches.
/// </summary>
public class DustLayer : TerrainTintLayer
{
    [SerializeField] Gradient colours = DefaultGradient();
    [Tooltip("Dust map to draw. Defaults to the one in the scene.")]
    [SerializeField] MarsDustMap dustMap;

    public override string Legend => "<color=#5b8de0>Clean rock</color>  -  <color=#f7f0d2>Thick dust</color>";

    void Reset()
    {
        opacity = 0.65f;
        resolution = 256;   // the data is far coarser than this
    }

    protected override bool FillTint(MarsGeoReference geo, int res, Color[] px)
    {
        MarsDustMap map = dustMap != null ? dustMap : MarsDustMap.Current;
        if (map == null || !map.Loaded) return false;

        Vector3 size = geo.terrain.terrainData.size;
        Vector3 origin = geo.Origin;
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                Vector3 world = origin + new Vector3((x + 0.5f) / res * size.x, 0f, (y + 0.5f) / res * size.z);
                Color c = colours.Evaluate(map.DustinessAt(world));
                c.a *= opacity;
                px[y * res + x] = c;
            }
        return true;
    }

    static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(new[]
        {
            new GradientColorKey(new Color(0.16f, 0.36f, 0.78f), 0f),
            new GradientColorKey(new Color(0.55f, 0.70f, 0.85f), 0.45f),
            new GradientColorKey(new Color(0.97f, 0.94f, 0.82f), 1f),
        }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
