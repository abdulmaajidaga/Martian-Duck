using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Map mode (M): a top-down orthographic camera over the whole terrain (north up) with the player's position and
/// facing, waypoint markers, a north arrow, a scale bar and the player's longitude/latitude. Data layers that
/// draw on the terrain (e.g. Elevation tint) appear here too.
/// </summary>
public class MarsMapMode : HudModule
{
    [Tooltip("Extra space around the terrain in the map view (1 = terrain fills the screen).")]
    [SerializeField, Range(1f, 1.5f)] float padding = 1.08f;
    [SerializeField] float cameraHeightAboveTerrain = 600f;
    [SerializeField] Color background = new Color(0.03f, 0.05f, 0.07f, 1f);

    Camera mapCam;
    Camera hiddenPlayerCam;
    HudArrow playerMarker;
    VisualElement waypointLayer, scaleBar;
    Label scaleLabel, coords;
    readonly List<(VisualElement root, Label label)> wpMarkers = new List<(VisualElement, Label)>();

    void Reset() => SetPlacement(HudRegion.FullScreen, 0, HudModes.Map);

    protected override void Build(VisualElement root)
    {
        waypointLayer = new VisualElement { pickingMode = PickingMode.Ignore };
        waypointLayer.AddToClassList("hud-fullscreen");
        root.Add(waypointLayer);

        playerMarker = new HudArrow { fill = new Color(1f, 1f, 1f, 0.95f), outline = new Color(0f, 0.6f, 0.8f, 1f) };
        playerMarker.AddToClassList("map-player");
        root.Add(playerMarker);

        // Top bar: title
        var top = new VisualElement { pickingMode = PickingMode.Ignore };
        top.AddToClassList("hud-panel"); top.AddToClassList("map-title");
        AddLabel(top, "MAP  ·  THREE FORKS AREA, JEZERO CRATER", "hud-title");
        AddLabel(top, "Movement paused  ·  M to return", "hud-small");
        root.Add(top);

        // North arrow (north is always up in map mode)
        var north = new VisualElement { pickingMode = PickingMode.Ignore };
        north.AddToClassList("hud-panel"); north.AddToClassList("map-north");
        var arrow = new HudArrow(); arrow.AddToClassList("map-north-arrow"); north.Add(arrow);
        AddLabel(north, "N", "map-north-label");
        root.Add(north);

        // Scale bar + coordinates
        var bottom = new VisualElement { pickingMode = PickingMode.Ignore };
        bottom.AddToClassList("hud-panel"); bottom.AddToClassList("map-info");
        scaleBar = new VisualElement { pickingMode = PickingMode.Ignore }; scaleBar.AddToClassList("map-scalebar");
        bottom.Add(scaleBar);
        scaleLabel = AddLabel(bottom, "-", "hud-label");
        coords = AddLabel(bottom, "-", "hud-value");
        root.Add(bottom);
    }

    public override void OnModeChanged(HudModes mode, HudContext ctx)
    {
        bool map = mode == HudModes.Map;
        if (map)
        {
            EnsureCamera(ctx);
            if (mapCam == null) return;
            hiddenPlayerCam = ctx.playerCamera;
            if (hiddenPlayerCam != null) { mapCam.depth = hiddenPlayerCam.depth + 1; hiddenPlayerCam.enabled = false; }
            mapCam.enabled = true;
        }
        else
        {
            if (mapCam != null) mapCam.enabled = false;
            if (hiddenPlayerCam != null) hiddenPlayerCam.enabled = true;
            hiddenPlayerCam = null;
        }
    }

    void EnsureCamera(HudContext ctx)
    {
        MarsGeoReference geo = ctx.geo ?? MarsGeoReference.Current;
        if (geo == null || !geo.ResolveTerrain()) return;
        if (mapCam == null)
        {
            var go = new GameObject("Map Camera") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            mapCam = go.AddComponent<Camera>();
            mapCam.orthographic = true;
            mapCam.clearFlags = CameraClearFlags.SolidColor;
            mapCam.backgroundColor = background;
            mapCam.enabled = false;
        }
        Vector3 size = geo.Size, centre = geo.Center;
        mapCam.transform.SetPositionAndRotation(new Vector3(centre.x, geo.Origin.y + size.y + cameraHeightAboveTerrain, centre.z),
                                                Quaternion.Euler(90f, 0f, 0f));      // looking down, north (+Z) up
        mapCam.nearClipPlane = 1f;
        mapCam.farClipPlane = cameraHeightAboveTerrain + size.y + 100f;
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        mapCam.orthographicSize = Mathf.Max(size.z * 0.5f, size.x * 0.5f / aspect) * padding;
    }

    public override void Tick(HudContext ctx)
    {
        if (mapCam == null || !mapCam.enabled || Root.panel == null || ctx.player == null || ctx.geo == null) return;
        IPanel panel = Root.panel;
        Vector2 ToPanel(Vector3 w) => RuntimePanelUtils.CameraTransformWorldToPanel(panel, w, mapCam);

        // Player marker: position + facing (screen up = north)
        Vector2 pp = ToPanel(ctx.player.position);
        playerMarker.style.left = pp.x - 11f; playerMarker.style.top = pp.y - 13f;
        playerMarker.SetAngle(ctx.headingDeg);

        // Waypoints
        RouteManager route = ctx.route;
        int count = route != null ? route.Waypoints.Count : 0;
        while (wpMarkers.Count < count) wpMarkers.Add(NewWaypointMarker());
        for (int i = 0; i < wpMarkers.Count; i++)
        {
            Waypoint w = i < count ? route.Waypoints[i] : null;
            var (el, label) = wpMarkers[i];
            el.style.display = w != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (w == null) continue;
            Vector2 wp = ToPanel(w.Position);
            el.style.left = wp.x - 7f; el.style.top = wp.y - 7f;
            label.text = $"{i + 1}. {w.displayName}";
            bool isNext = w == route.Next;
            el.EnableInClassList("map-waypoint-next", isNext);
            el.EnableInClassList("map-waypoint-done", i < route.CurrentIndex);
        }

        // Scale bar: pick a round length that is 90-220 px on screen
        float pxPerMetre = (ToPanel(ctx.geo.Center + Vector3.right * 100f) - ToPanel(ctx.geo.Center)).magnitude / 100f;
        float metres = 10f;
        foreach (float m in new[] { 10f, 20f, 50f, 100f, 200f, 250f, 500f, 1000f, 2000f })
        { metres = m; if (m * pxPerMetre >= 90f) break; }
        scaleBar.style.width = metres * pxPerMetre;
        scaleLabel.text = metres >= 1000f ? $"{metres / 1000f:0.#} km" : $"{metres:0} m";

        ctx.geo.WorldToLonLat(ctx.player.position, out double lon, out double lat);
        coords.text = $"{MarsGeoReference.FormatLatitude(lat)}   {MarsGeoReference.FormatLongitude(lon)}   ·   {ctx.geo.ElevationAt(ctx.player.position):0} m";
    }

    (VisualElement, Label) NewWaypointMarker()
    {
        var el = new VisualElement { pickingMode = PickingMode.Ignore };
        el.AddToClassList("map-waypoint");
        var dot = new VisualElement { pickingMode = PickingMode.Ignore }; dot.AddToClassList("map-waypoint-dot"); el.Add(dot);
        var label = new Label { pickingMode = PickingMode.Ignore }; label.AddToClassList("map-waypoint-label"); el.Add(label);
        waypointLayer.Add(el);
        return (el, label);
    }

    void OnDestroy()
    {
        if (mapCam != null) Destroy(mapCam.gameObject);
    }
}
