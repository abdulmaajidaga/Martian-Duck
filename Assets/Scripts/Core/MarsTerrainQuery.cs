using UnityEngine;

/// <summary>
/// Read-only questions about the Mars terrain, for the path planner (and anything else): surface height, slope,
/// bounds, and a copy of the full height grid in metres for grid-based search (A*, Dijkstra, ...).
/// Uses the terrain referenced by MarsGeoReference. World axes: +X = east, +Z = north, +Y = up; metres throughout.
/// </summary>
public static class MarsTerrainQuery
{
    static Terrain T
    {
        get
        {
            MarsGeoReference geo = MarsGeoReference.Current;
            if (geo != null && geo.ResolveTerrain()) return geo.terrain;
            return Terrain.activeTerrain;
        }
    }

    public static bool Available => T != null;

    /// <summary>Terrain corner (south-west, base height) in world space.</summary>
    public static Vector3 Origin => T != null ? T.transform.position : Vector3.zero;
    /// <summary>Terrain size in metres (x = east-west, y = height range, z = north-south).</summary>
    public static Vector3 Size => T != null ? T.terrainData.size : Vector3.zero;

    public static bool InBounds(Vector3 world)
    {
        Vector3 o = Origin, s = Size;
        return world.x >= o.x && world.x <= o.x + s.x && world.z >= o.z && world.z <= o.z + s.z;
    }

    /// <summary>World Y of the ground at this X/Z.</summary>
    public static float HeightAt(Vector3 world) => T != null ? T.SampleHeight(world) + T.transform.position.y : 0f;

    /// <summary>The same point moved onto the ground.</summary>
    public static Vector3 OnSurface(Vector3 world) { world.y = HeightAt(world); return world; }

    /// <summary>Ground steepness in degrees (0 = flat). The astronaut can walk up to MarsPlayerController's slope limit (30°).</summary>
    public static float SlopeAt(Vector3 world)
    {
        if (T == null) return 0f;
        Vector2 n = Normalized(world);
        return T.terrainData.GetSteepness(Mathf.Clamp01(n.x), Mathf.Clamp01(n.y));
    }

    public static Vector3 NormalAt(Vector3 world)
    {
        if (T == null) return Vector3.up;
        Vector2 n = Normalized(world);
        return T.terrainData.GetInterpolatedNormal(Mathf.Clamp01(n.x), Mathf.Clamp01(n.y));
    }

    /// <summary>Grade along a direction (degrees, + = uphill) between two points, using the ground heights.</summary>
    public static float GradeBetween(Vector3 a, Vector3 b)
    {
        float ha = HeightAt(a), hb = HeightAt(b);
        float run = new Vector2(b.x - a.x, b.z - a.z).magnitude;
        return run > 1e-4f ? Mathf.Atan2(hb - ha, run) * Mathf.Rad2Deg : 0f;
    }

    // ------------------------------------------------------------------ grid access (for grid-based planners)

    /// <summary>Samples per side of the height grid (2049 for the Jezero terrain).</summary>
    public static int GridResolution => T != null ? T.terrainData.heightmapResolution : 0;

    /// <summary>Metres between grid samples (x = east-west, y = north-south). About 0.61 m x 0.75 m here.</summary>
    public static Vector2 CellSize
    {
        get
        {
            int r = GridResolution;
            return r > 1 ? new Vector2(Size.x / (r - 1), Size.z / (r - 1)) : Vector2.zero;
        }
    }

    /// <summary>
    /// Copy of the whole height grid in world-space metres, indexed [row = south->north, col = west->east].
    /// About 16 MB for 2049x2049: call once and keep it, or downsample with <paramref name="step"/> (2 = every
    /// other sample, ...).
    /// </summary>
    public static float[,] GetHeightGrid(int step = 1)
    {
        if (T == null) return new float[0, 0];
        TerrainData td = T.terrainData;
        int r = td.heightmapResolution;
        float[,] raw = td.GetHeights(0, 0, r, r);        // [y, x], 0..1
        step = Mathf.Max(1, step);
        int n = (r - 1) / step + 1;
        var grid = new float[n, n];
        float scale = td.size.y, baseY = T.transform.position.y;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                grid[y, x] = baseY + raw[y * step, x * step] * scale;
        return grid;
    }

    /// <summary>World position of grid cell [row, col] for a grid made with the given step.</summary>
    public static Vector3 GridToWorld(int row, int col, int step = 1)
    {
        Vector2 c = CellSize * Mathf.Max(1, step);
        Vector3 w = Origin + new Vector3(col * c.x, 0f, row * c.y);
        return OnSurface(w);
    }

    /// <summary>Nearest grid cell for a world position (row = south->north, col = west->east).</summary>
    public static Vector2Int WorldToGrid(Vector3 world, int step = 1)
    {
        Vector2 c = CellSize * Mathf.Max(1, step);
        if (c.x <= 0f) return Vector2Int.zero;
        Vector3 o = Origin;
        return new Vector2Int(Mathf.RoundToInt((world.z - o.z) / c.y), Mathf.RoundToInt((world.x - o.x) / c.x));
    }

    static Vector2 Normalized(Vector3 world)
    {
        Vector3 o = Origin, s = Size;
        return new Vector2((world.x - o.x) / s.x, (world.z - o.z) / s.z);
    }
}
