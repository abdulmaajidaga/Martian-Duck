using UnityEngine;

/// <summary>
/// Single source of truth for the map frame: the area's longitude/latitude bounds, the DTM's minimum elevation,
/// and conversions between Unity world positions and Mars coordinates. The terrain spans the bounds exactly,
/// west->east along +X and south->north along +Z (north = +Z).
/// </summary>
[DisallowMultipleComponent]
public class MarsGeoReference : MonoBehaviour
{
    [Header("Area bounds (degrees, Mars 2020 frame, planetocentric, east longitude)")]
    public double westLongitude = 77.39670753;
    public double eastLongitude = 77.41902351;
    public double southLatitude = 18.43816893;
    public double northLatitude = 18.46422;

    [Header("Elevation")]
    [Tooltip("Lowest DTM elevation in the area (metres above the Mars 2000 areoid). The terrain's base (its Y position) " +
             "corresponds to this value, so elevation = this + height above the terrain base.")]
    public float minElevation = -2554.768f;

    [Tooltip("Terrain covering the area. If empty, the first Terrain in the scene is used.")]
    public Terrain terrain;

    static MarsGeoReference current;

    /// <summary>The geo reference in the scene (found automatically).</summary>
    public static MarsGeoReference Current
    {
        get
        {
            if (current == null) current = FindAnyObjectByType<MarsGeoReference>();
            return current;
        }
    }

    void OnEnable()
    {
        current = this;
        ResolveTerrain();
    }

    public bool ResolveTerrain()
    {
        if (terrain == null) terrain = FindAnyObjectByType<Terrain>();
        return terrain != null;
    }

    public Vector3 Origin => terrain != null ? terrain.transform.position : Vector3.zero;
    public Vector3 Size => terrain != null ? terrain.terrainData.size : Vector3.one;
    public Vector3 Center => Origin + new Vector3(Size.x * 0.5f, 0f, Size.z * 0.5f);

    /// <summary>World position -> fraction of the area (x west->east 0..1, y south->north 0..1).</summary>
    public Vector2 WorldToNormalized(Vector3 world)
    {
        Vector3 o = Origin, s = Size;
        return new Vector2((world.x - o.x) / s.x, (world.z - o.z) / s.z);
    }

    public void WorldToLonLat(Vector3 world, out double longitude, out double latitude)
    {
        Vector2 n = WorldToNormalized(world);
        longitude = westLongitude + n.x * (eastLongitude - westLongitude);
        latitude = southLatitude + n.y * (northLatitude - southLatitude);
    }

    /// <summary>Longitude/latitude -> world position, optionally placed on the terrain surface.</summary>
    public Vector3 LonLatToWorld(double longitude, double latitude, bool onSurface = true)
    {
        Vector3 o = Origin, s = Size;
        float nx = (float)((longitude - westLongitude) / (eastLongitude - westLongitude));
        float ny = (float)((latitude - southLatitude) / (northLatitude - southLatitude));
        var p = new Vector3(o.x + nx * s.x, o.y, o.z + ny * s.z);
        if (onSurface && terrain != null) p.y = o.y + terrain.SampleHeight(p);
        return p;
    }

    /// <summary>Real Mars elevation (m) of a world position: DTM minimum + height above the terrain base.</summary>
    public float ElevationAt(Vector3 world) => minElevation + (world.y - Origin.y);

    /// <summary>Compass heading of a direction in degrees: 0 = north (+Z), 90 = east (+X).</summary>
    public static float Heading(Vector3 direction)
    {
        float h = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        return (h + 360f) % 360f;
    }

    public static float Bearing(Vector3 from, Vector3 to) => Heading(to - from);

    public static string FormatLatitude(double lat) => $"{System.Math.Abs(lat):0.00000}°{(lat >= 0 ? "N" : "S")}";
    public static string FormatLongitude(double lon) => $"{System.Math.Abs(lon):0.00000}°{(lon >= 0 ? "E" : "W")}";
}
