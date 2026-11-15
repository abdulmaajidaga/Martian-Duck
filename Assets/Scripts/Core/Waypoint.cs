using UnityEngine;

/// <summary>
/// A navigation waypoint. Add it to a GameObject, give it a name (and optionally a description and image),
/// then add it to the RouteManager's list. With "Place By Lon Lat" on, it snaps to the given coordinates on the
/// terrain surface; otherwise its transform position is used.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class Waypoint : MonoBehaviour
{
    public string displayName = "Waypoint";
    [TextArea(2, 5)] public string description;
    [Tooltip("Optional image shown with the waypoint.")]
    public Texture2D image;

    [Header("Placement")]
    public bool placeByLonLat = true;
    public double longitude;
    public double latitude;

    [Tooltip("The waypoint counts as reached within this horizontal distance (m).")]
    [Min(0.5f)] public float arriveRadius = 5f;

    public Vector3 Position => transform.position;

    void OnEnable() => Snap();

    void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () => { if (this != null) Snap(); };
#endif
    }

    /// <summary>Move to the lon/lat on the terrain surface (if Place By Lon Lat is on).</summary>
    [ContextMenu("Snap To Lon Lat")]
    public void Snap()
    {
        if (!placeByLonLat) return;
        MarsGeoReference geo = MarsGeoReference.Current;
        if (geo == null || !geo.ResolveTerrain()) return;
        transform.position = geo.LonLatToWorld(longitude, latitude);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 1f, 1f, 0.9f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 6f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 6f, 0.6f);
        Gizmos.color = new Color(0.4f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, arriveRadius);
    }
}
