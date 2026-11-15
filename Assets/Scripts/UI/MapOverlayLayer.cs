using UnityEngine;

/// <summary>
/// Map overlay toggle: shows or hides one kind of map content (waypoint markers, points of interest, the planned
/// route). Listed under "Map overlays" in the layer panel. Content with no MapOverlayLayer in the scene is always shown.
/// Points of interest can be split into several toggles by category (sample sites, landmarks, Ingenuity airfields...).
/// </summary>
public class MapOverlayLayer : MarsDataLayer
{
    public enum Content
    {
        Waypoints,
        PointsOfInterest,
        PlannedRoute,
    }

    [SerializeField] Content content;
    [Tooltip("Points of interest only: the POI categories this toggle controls (e.g. \"Sample site\"). Empty = all.")]
    [SerializeField] string[] categories;

    public override LayerGroup Group => LayerGroup.MapOverlay;

    protected override void ApplyState(bool on) { }   // the map polls IsShown each frame

    /// <summary>True if this content should be drawn on the map.</summary>
    public static bool IsShown(Content content)
    {
        foreach (MarsDataLayer l in Registered)
            if (l is MapOverlayLayer o && o.content == content && o.isActiveAndEnabled)
                return o.IsOn;
        return true;
    }

    /// <summary>True if this point of interest should be drawn (and clickable) on the map.</summary>
    public static bool IsShown(PointOfInterest poi)
    {
        if (poi == null) return false;
        foreach (MarsDataLayer l in Registered)
            if (l is MapOverlayLayer o && o.content == Content.PointsOfInterest && o.isActiveAndEnabled && o.Covers(poi) && !o.IsOn)
                return false;
        return true;
    }

    bool Covers(PointOfInterest poi)
    {
        if (categories == null || categories.Length == 0) return true;
        foreach (string c in categories)
            if (string.Equals(c, poi.category, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
