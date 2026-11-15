using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One real photo shown at a point of interest.</summary>
[Serializable]
public class PoiPhoto
{
    public Texture2D image;
    [Tooltip("What the photo shows.")]
    public string caption;
    [Tooltip("Where it came from, e.g. \"Front Hazcam · Sol 490 · NASA/JPL-Caltech\".")]
    public string credit;
}

/// <summary>
/// A point of interest on the terrain (e.g. a sampling site) with real photos. When the astronaut walks within
/// Reveal Radius, the POI panel on the HUD shows its name, details and photos (P = next photo); in map mode it
/// is drawn as a marker. Add it to any GameObject; it registers itself, no other setup needed.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class PointOfInterest : MonoBehaviour
{
    public string displayName = "Point of interest";
    [Tooltip("Label used on the map (falls back to Display Name when empty).")]
    public string shortName;
    [Tooltip("Kind of place, shown above the name, e.g. \"Sample site\".")]
    public string category;
    [Tooltip("When it happened, e.g. \"Sols 490 & 495 · Jul 7 & 12, 2022\".")]
    public string when;
    [Tooltip("What the rover did here, in a sentence or two.")]
    [TextArea(2, 6)] public string activity;
    [Tooltip("Extra line (sample type, rock type, ...). Optional.")]
    public string subtitle;
    [TextArea(2, 6)] public string details;
    public List<PoiPhoto> photos = new List<PoiPhoto>();

    [Header("Placement")]
    public bool placeByLonLat = true;
    public double longitude;
    public double latitude;

    [Tooltip("The HUD shows this point's panel when the astronaut is within this horizontal distance (m).")]
    [Min(1f)] public float revealRadius = 15f;

    public Vector3 Position => transform.position;

    public static readonly List<PointOfInterest> Registered = new List<PointOfInterest>();

    void OnEnable()
    {
        if (!Registered.Contains(this)) Registered.Add(this);
        Snap();
    }

    void OnDisable() => Registered.Remove(this);

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

    /// <summary>The closest registered point within its own reveal radius of <paramref name="from"/>, or null.</summary>
    public static PointOfInterest NearestInRange(Vector3 from, out float distance)
    {
        PointOfInterest best = null;
        distance = float.MaxValue;
        foreach (PointOfInterest p in Registered)
        {
            if (p == null || !p.isActiveAndEnabled) continue;
            Vector3 d = p.Position - from; d.y = 0f;
            float m = d.magnitude;
            if (m <= p.revealRadius && m < distance) { best = p; distance = m; }
        }
        return best;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.25f, 0.9f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 5f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 5f, Vector3.one * 0.8f);
        Gizmos.color = new Color(1f, 0.55f, 0.25f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, revealRadius);
    }
}
