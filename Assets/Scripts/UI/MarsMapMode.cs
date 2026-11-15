using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

/// <summary>
/// Map mode (M): a top-down orthographic camera over the whole terrain (north up) with the player's position and
/// facing, waypoint markers, points of interest, a north arrow, a scale bar and the player's longitude/latitude.
/// Data layers that draw on the terrain (e.g. Elevation tint) appear here too.
///
/// The map is drawn over the (paused) first-person view, which shows through a translucent backdrop around the
/// terrain. Zoom with the scroll wheel (towards the cursor) or +/-, drag with the left mouse button to pan, R to reset.
///
/// Points of interest are clickable: clicking one adds it to an ordered list of stops (click again to remove).
/// Other code can read the stops (<see cref="SelectedStops"/>, <see cref="SelectionChanged"/>) and turn them into a
/// <see cref="PathPlanRequest"/> with <see cref="BuildPlanRequest"/>.
/// </summary>
public class MarsMapMode : HudModule
{
    [Tooltip("Extra space around the terrain in the map view (1 = terrain fills the screen).")]
    [SerializeField, Range(1f, 1.5f)] float padding = 1.08f;
    [SerializeField] float cameraHeightAboveTerrain = 600f;
    [Tooltip("Show the first-person view behind the map (dimmed by Backdrop). Off = solid Background colour.")]
    [SerializeField] bool showWorldBehindMap = true;
    [Tooltip("Tint laid over the first-person view around the terrain.")]
    [SerializeField] Color backdrop = new Color(0.04f, 0.03f, 0.03f, 0.62f);
    [Tooltip("Camera clear colour, used only when the world isn't shown behind the map.")]
    [SerializeField] Color background = new Color(0.03f, 0.05f, 0.07f, 1f);

    [Header("Zoom")]
    [Tooltip("Maximum magnification relative to the whole-terrain view.")]
    [SerializeField, Min(1f)] float maxZoom = 25f;
    [Tooltip("Zoom factor per scroll-wheel notch.")]
    [SerializeField, Range(1.05f, 2f)] float zoomPerNotch = 1.25f;
    [Tooltip("Zoom factor per second while + or - is held.")]
    [SerializeField, Range(1.1f, 8f)] float keyZoomPerSecond = 2.5f;
    [Tooltip("How quickly the view eases to the target zoom (higher = snappier).")]
    [SerializeField, Min(1f)] float zoomSmoothing = 14f;

    /// <summary>The active map module (null if none).</summary>
    public static MarsMapMode Current { get; private set; }

    /// <summary>Points of interest chosen on the map, in the order they were picked.</summary>
    public IReadOnlyList<PointOfInterest> SelectedStops => stops;
    /// <summary>The point whose details are shown on the map (last one clicked), or null.</summary>
    public PointOfInterest Focused { get; private set; }
    /// <summary>Raised whenever the stop list or the focused point changes.</summary>
    public event Action SelectionChanged;

    Camera mapCam;
    Camera playerCam;
    bool stacked;                  // map camera is an overlay in the player camera's URP stack
    HudArrow playerMarker;
    VisualElement markerLayer, scrimTop, scrimBottom, scrimLeft, scrimRight, terrainFrame;
    VisualElement scaleBar;
    Label scaleLabel, latLabel, lonLabel, elevLabel;
    // selection card
    VisualElement selectCard, focusBox, stopList;
    Label focusEyebrow, focusDistance, focusTitle, focusWhen, focusActivity, stopsHeader;
    Button focusButton;

    readonly List<(VisualElement root, Label label)> wpMarkers = new List<(VisualElement, Label)>();
    readonly List<(VisualElement root, Label label)> poiMarkers = new List<(VisualElement, Label)>();
    readonly List<PointOfInterest> stops = new List<PointOfInterest>();
    bool selectionDirty = true;

    // Zoom / pan state
    float fitSize;                 // orthographic size that shows the whole terrain
    float targetSize;              // size the view is easing towards
    Vector2 zoomAnchor;            // screen point that stays fixed while zooming
    Vector3 terrainMin, terrainMax;
    bool dragging;
    Vector2 lastPointer;

    void Reset() => SetPlacement(HudRegion.FullScreen, 0, HudModes.Map);

    protected override void OnEnable() { Current = this; base.OnEnable(); }
    protected override void OnDisable()
    {
        if (Current == this) Current = null;
        ExitMap();
        base.OnDisable();
    }

    // ------------------------------------------------------------------ build
    protected override void Build(VisualElement root)
    {
        wpMarkers.Clear(); poiMarkers.Clear();
        selectionDirty = true;

        // Translucent backdrop around the terrain (four strips, so the map itself isn't tinted) + a thin frame.
        scrimTop = Scrim(root); scrimBottom = Scrim(root); scrimLeft = Scrim(root); scrimRight = Scrim(root);
        terrainFrame = Box(root, "map-frame");

        markerLayer = Box(root, "hud-fullscreen");

        playerMarker = new HudArrow { fill = new Color(1f, 1f, 1f, 0.95f), outline = new Color(0.95f, 0.55f, 0.25f, 1f) };
        playerMarker.AddToClassList("map-player");
        root.Add(playerMarker);

        // ---- left column: title + controls, then the selected stops
        VisualElement left = Box(root, "map-column map-column-left");

        VisualElement titleCard = Card(left);
        Text(titleCard, "poi-eyebrow", "MAP  ·  MOVEMENT PAUSED");
        Text(titleCard, "poi-title", "Jezero Crater");
        VisualElement keys = Box(titleCard, "map-keys");
        KeyRow(keys, "Click", "Add / remove a stop");
        KeyRow(keys, "Scroll", "Zoom   (or  + / -)");
        KeyRow(keys, "Drag", "Move the map");
        KeyRow(keys, "R", "Reset view");
        KeyRow(keys, "M", "Back to walking");

        selectCard = Card(left);
        focusBox = Box(selectCard, "map-focus");
        VisualElement head = Box(focusBox, "poi-row");
        focusEyebrow = Text(head, "poi-eyebrow");
        focusDistance = Text(head, "poi-distance");
        focusTitle = Text(focusBox, "poi-title");
        focusWhen = Text(focusBox, "poi-when");
        focusActivity = Text(focusBox, "poi-activity map-focus-activity");
        focusButton = new Button(() => { if (Focused != null) ToggleStop(Focused); });
        focusButton.AddToClassList("map-btn"); focusButton.AddToClassList("map-btn-primary");
        focusBox.Add(focusButton);

        VisualElement stopsHead = Box(selectCard, "poi-row map-stops-head");
        stopsHeader = Text(stopsHead, "poi-eyebrow");
        var clear = new Button(ClearStops) { text = "Clear" };
        clear.AddToClassList("map-btn"); clear.AddToClassList("map-btn-quiet");
        stopsHead.Add(clear);
        stopList = Box(selectCard, "map-stop-list");

        // ---- north arrow (north is always up in map mode)
        VisualElement north = Box(root, "map-north");
        var arrow = new HudArrow { fill = new Color(0.98f, 0.96f, 0.93f, 1f), outline = new Color(0.94f, 0.59f, 0.31f, 1f) };
        arrow.AddToClassList("map-north-arrow"); north.Add(arrow);
        Text(north, "map-north-label", "N");

        // ---- scale bar + position
        VisualElement info = Card(root, "map-info");
        Text(info, "poi-eyebrow", "SCALE");
        VisualElement scaleRow = Box(info, "poi-row map-scale-row");
        scaleBar = Box(scaleRow, "map-scalebar");
        scaleLabel = Text(scaleRow, "map-scale-label", "-");
        Text(info, "poi-eyebrow map-info-gap", "YOUR POSITION");
        latLabel = Readout(info, "Latitude");
        lonLabel = Readout(info, "Longitude");
        elevLabel = Readout(info, "Elevation");
    }

    // ------------------------------------------------------------------ modes / camera
    public override void OnModeChanged(HudModes mode, HudContext ctx)
    {
        if (mode == HudModes.Map) EnterMap(ctx);
        else ExitMap();
    }

    void EnterMap(HudContext ctx)
    {
        EnsureCamera(ctx);
        if (mapCam == null) return;
        playerCam = ctx.playerCamera;

        // Preferred: render the map as a URP overlay on top of the player camera, so the world shows behind it.
        stacked = false;
        var mapData = mapCam.GetUniversalAdditionalCameraData();
        if (showWorldBehindMap && playerCam != null)
        {
            var baseData = playerCam.GetUniversalAdditionalCameraData();
            if (baseData != null && baseData.renderType == CameraRenderType.Base)
            {
                mapData.renderType = CameraRenderType.Overlay;
                if (!baseData.cameraStack.Contains(mapCam)) baseData.cameraStack.Add(mapCam);
                stacked = true;
            }
        }
        if (!stacked)
        {
            mapData.renderType = CameraRenderType.Base;
            mapCam.clearFlags = CameraClearFlags.SolidColor;
            mapCam.backgroundColor = background;
            if (playerCam != null) { mapCam.depth = playerCam.depth + 1; playerCam.enabled = false; }
        }
        SetScrimVisible(stacked);
        mapCam.enabled = true;
    }

    void ExitMap()
    {
        dragging = false;
        if (mapCam != null)
        {
            mapCam.enabled = false;
            if (playerCam != null)
            {
                var baseData = playerCam.GetUniversalAdditionalCameraData();
                if (baseData != null) baseData.cameraStack.Remove(mapCam);
            }
        }
        if (playerCam != null) playerCam.enabled = true;
        playerCam = null;
        stacked = false;
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
        fitSize = Mathf.Max(size.z * 0.5f, size.x * 0.5f / aspect) * padding;
        mapCam.orthographicSize = targetSize = fitSize;
        terrainMin = geo.Origin;
        terrainMax = geo.Origin + size;
        dragging = false;
    }

    // ------------------------------------------------------------------ per frame
    public override void Tick(HudContext ctx)
    {
        if (mapCam == null || !mapCam.enabled || Root.panel == null || ctx.player == null || ctx.geo == null) return;
        UpdateZoomAndPan(ctx.deltaTime);
        IPanel panel = Root.panel;
        Vector2 ToPanel(Vector3 w) => RuntimePanelUtils.CameraTransformWorldToPanel(panel, w, mapCam);

        // Backdrop strips around the terrain rectangle
        if (stacked)
        {
            Vector2 a = ToPanel(new Vector3(terrainMin.x, terrainMin.y, terrainMax.z));   // north-west corner
            Vector2 b = ToPanel(new Vector3(terrainMax.x, terrainMin.y, terrainMin.z));   // south-east corner
            LayoutScrim(a, b);
        }

        // Player marker: position + facing (screen up = north)
        Vector2 pp = ToPanel(ctx.player.position);
        playerMarker.style.left = pp.x - 13f; playerMarker.style.top = pp.y - 15f;
        playerMarker.SetAngle(ctx.headingDeg);

        // Points of interest (drawn first so waypoints sit on top)
        if (stops.RemoveAll(p => p == null || !p.isActiveAndEnabled) > 0) selectionDirty = true;
        if (Focused != null && !Focused.isActiveAndEnabled) { Focused = null; selectionDirty = true; }
        var pois = PointOfInterest.Registered;
        while (poiMarkers.Count < pois.Count) poiMarkers.Add(NewPoiMarker());
        for (int i = 0; i < poiMarkers.Count; i++)
        {
            PointOfInterest p = i < pois.Count ? pois[i] : null;
            var (el, label) = poiMarkers[i];
            bool show = p != null && p.isActiveAndEnabled;
            el.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            el.userData = show ? p : null;
            if (!show) continue;
            Vector2 sp = ToPanel(p.Position);
            el.style.left = sp.x - 14f; el.style.top = sp.y - 14f;
            int stop = stops.IndexOf(p);
            string name = string.IsNullOrEmpty(p.shortName) ? p.displayName : p.shortName;
            label.text = stop >= 0 ? $"{stop + 1}  ·  {name}" : name;
            el.EnableInClassList("map-poi-selected", stop >= 0);
            el.EnableInClassList("map-poi-focused", p == Focused);
        }

        // Waypoints
        RouteManager route = ctx.route;
        int count = route != null ? route.Waypoints.Count : 0;
        while (wpMarkers.Count < count) wpMarkers.Add(NewMarker("map-waypoint", "map-waypoint-dot", "map-waypoint-label"));
        for (int i = 0; i < wpMarkers.Count; i++)
        {
            Waypoint w = i < count ? route.Waypoints[i] : null;
            var (el, label) = wpMarkers[i];
            el.style.display = w != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (w == null) continue;
            Vector2 wp = ToPanel(w.Position);
            el.style.left = wp.x - 8f; el.style.top = wp.y - 8f;
            label.text = $"{i + 1}. {w.displayName}";
            bool isNext = w == route.Next;
            el.EnableInClassList("map-waypoint-next", isNext);
            el.EnableInClassList("map-waypoint-done", i < route.CurrentIndex);
        }

        // Scale bar: pick a round length that is at least 110 px on screen (short lengths for deep zoom)
        float pxPerMetre = (ToPanel(ctx.geo.Center + Vector3.right * 100f) - ToPanel(ctx.geo.Center)).magnitude / 100f;
        float metres = 1f;
        foreach (float m in new[] { 1f, 2f, 5f, 10f, 20f, 50f, 100f, 200f, 250f, 500f, 1000f, 2000f, 5000f, 10000f, 20000f })
        { metres = m; if (m * pxPerMetre >= 110f) break; }
        scaleBar.style.width = metres * pxPerMetre;
        scaleLabel.text = metres >= 1000f ? $"{metres / 1000f:0.#} km" : $"{metres:0} m";

        ctx.geo.WorldToLonLat(ctx.player.position, out double lon, out double lat);
        latLabel.text = MarsGeoReference.FormatLatitude(lat);
        lonLabel.text = MarsGeoReference.FormatLongitude(lon);
        elevLabel.text = $"{ctx.geo.ElevationAt(ctx.player.position):0} m";

        if (selectionDirty) RefreshSelectionCard();
        if (Focused != null)
        {
            Vector3 d = Focused.Position - ctx.player.position; d.y = 0f;
            float m = d.magnitude;
            focusDistance.text = m >= 1000f ? $"{m / 1000f:0.0} km away" : $"{m:0} m away";
        }
    }

    // ------------------------------------------------------------------ selection (stops)
    /// <summary>Show a point's details and add it to the stops, or remove it if it is already a stop.</summary>
    public void ToggleStop(PointOfInterest p)
    {
        if (p == null) return;
        if (!stops.Remove(p)) stops.Add(p);
        Focused = p;
        NotifySelection();
    }

    public void RemoveStop(PointOfInterest p)
    {
        if (!stops.Remove(p)) return;
        if (Focused == p) Focused = null;
        NotifySelection();
    }

    public void ClearStops()
    {
        stops.Clear();
        Focused = null;
        NotifySelection();
    }

    /// <summary>A planning request from <paramref name="start"/> through the selected stops, in the order picked.</summary>
    public PathPlanRequest BuildPlanRequest(Vector3 start)
    {
        var req = new PathPlanRequest { start = start };
        foreach (PointOfInterest p in stops) if (p != null) req.stops.Add(PathStop.From(p));
        return req;
    }

    void NotifySelection()
    {
        selectionDirty = true;
        SelectionChanged?.Invoke();
    }

    void OnPoiClicked(ClickEvent e)
    {
        if (e.currentTarget is VisualElement el && el.userData is PointOfInterest p)
        {
            // Clicking a stop that isn't focused just focuses it; clicking the focused one toggles it.
            if (stops.Contains(p) && Focused != p) { Focused = p; NotifySelection(); }
            else ToggleStop(p);
        }
        e.StopPropagation();
    }

    void RefreshSelectionCard()
    {
        selectionDirty = false;
        if (selectCard == null) return;
        bool any = Focused != null || stops.Count > 0;
        selectCard.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;

        focusBox.style.display = Focused != null ? DisplayStyle.Flex : DisplayStyle.None;
        if (Focused != null)
        {
            focusEyebrow.text = (string.IsNullOrEmpty(Focused.category) ? "Point of interest" : Focused.category).ToUpperInvariant();
            focusTitle.text = Focused.displayName;
            SetText(focusWhen, Focused.when);
            SetText(focusActivity, string.IsNullOrEmpty(Focused.activity) ? Focused.details : Focused.activity);
            focusButton.text = stops.Contains(Focused) ? "Remove from stops" : "Add as stop";
        }

        stopsHeader.text = stops.Count == 0 ? "NO STOPS YET" : $"STOPS  ·  {stops.Count}";
        stopList.Clear();
        for (int i = 0; i < stops.Count; i++)
        {
            PointOfInterest p = stops[i];
            var row = new VisualElement();
            row.AddToClassList("map-stop-row");
            row.EnableInClassList("map-stop-row-focused", p == Focused);
            row.RegisterCallback<ClickEvent>(_ => { if (stops.Contains(p)) { Focused = p; NotifySelection(); } });
            Text(row, "map-stop-num", (i + 1).ToString());
            Text(row, "map-stop-name", p.displayName);
            var remove = new Button(() => RemoveStop(p)) { tooltip = "Remove stop" };
            remove.AddToClassList("map-stop-remove");
            remove.Add(new PoiIcon(PoiIcon.Shape.Close));
            row.Add(remove);
            stopList.Add(row);
        }
    }

    // ------------------------------------------------------------------ zoom & pan
    void UpdateZoomAndPan(float dt)
    {
        if (fitSize <= 0f) return;
        Vector2 pointer = MarsInput.PointerPosition;
        bool overUi = IsPointerOverUi(pointer);

        if (MarsInput.MapResetView)
        {
            targetSize = fitSize;
            zoomAnchor = new Vector2(mapCam.pixelWidth * 0.5f, mapCam.pixelHeight * 0.5f);
        }

        float minSize = fitSize / maxZoom;
        float scroll = overUi ? 0f : MarsInput.MapScroll;
        if (Mathf.Abs(scroll) > 0.001f)
        {
            targetSize = Mathf.Clamp(targetSize / Mathf.Pow(zoomPerNotch, scroll), minSize, fitSize);
            zoomAnchor = pointer;
        }
        float keys = MarsInput.MapZoomKeys;
        if (keys != 0f)
        {
            targetSize = Mathf.Clamp(targetSize / Mathf.Pow(keyZoomPerSecond, keys * dt), minSize, fitSize);
            zoomAnchor = new Vector2(mapCam.pixelWidth * 0.5f, mapCam.pixelHeight * 0.5f);
        }

        // Ease towards the target size, keeping the world point under the anchor fixed on screen.
        float size = mapCam.orthographicSize;
        if (!Mathf.Approximately(size, targetSize))
        {
            float next = Mathf.Abs(size - targetSize) < targetSize * 0.001f
                ? targetSize
                : Mathf.Lerp(size, targetSize, 1f - Mathf.Exp(-zoomSmoothing * dt));
            Vector3 before = mapCam.ScreenToWorldPoint(zoomAnchor);
            mapCam.orthographicSize = next;
            Vector3 after = mapCam.ScreenToWorldPoint(zoomAnchor);
            Vector3 shift = before - after;
            mapCam.transform.position += new Vector3(shift.x, 0f, shift.z);
        }

        // Drag to pan (only when the press starts on the map, not on a panel or marker).
        if (MarsInput.DragPressed && !overUi) { dragging = true; lastPointer = pointer; }
        if (dragging && !MarsInput.DragHeld) dragging = false;
        if (dragging)
        {
            float metresPerPixel = 2f * mapCam.orthographicSize / Mathf.Max(1, mapCam.pixelHeight);
            Vector2 d = pointer - lastPointer;
            mapCam.transform.position -= new Vector3(d.x, 0f, d.y) * metresPerPixel;   // screen up = north (+Z)
            lastPointer = pointer;
        }

        ClampCameraToTerrain();
    }

    /// <summary>Keep the view over the terrain; when the view is wider than the terrain, centre it.</summary>
    void ClampCameraToTerrain()
    {
        float halfH = mapCam.orthographicSize, halfW = halfH * mapCam.aspect;
        Vector3 p = mapCam.transform.position;
        p.x = ClampAxis(p.x, terrainMin.x, terrainMax.x, halfW);
        p.z = ClampAxis(p.z, terrainMin.z, terrainMax.z, halfH);
        mapCam.transform.position = p;
    }

    float ClampAxis(float v, float min, float max, float halfView)
    {
        float centre = (min + max) * 0.5f;
        float slack = (max - min) * 0.5f * padding - halfView;   // how far the centre may move each way
        return slack <= 0f ? centre : Mathf.Clamp(v, centre - slack, centre + slack);
    }

    bool IsPointerOverUi(Vector2 screenPos)
    {
        IPanel panel = Root?.panel;
        if (panel == null) return false;
        Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
        return panel.Pick(panelPos) != null;
    }

    // ------------------------------------------------------------------ backdrop
    VisualElement Scrim(VisualElement parent)
    {
        VisualElement s = Box(parent, "map-scrim");
        s.style.backgroundColor = backdrop;
        return s;
    }

    void SetScrimVisible(bool on)
    {
        if (scrimTop == null) return;
        DisplayStyle d = on ? DisplayStyle.Flex : DisplayStyle.None;
        scrimTop.style.display = scrimBottom.style.display = scrimLeft.style.display = scrimRight.style.display = d;
        terrainFrame.style.display = d;
    }

    /// <summary>Place the four backdrop strips around the terrain rectangle (a = top-left, b = bottom-right, panel px).</summary>
    void LayoutScrim(Vector2 a, Vector2 b)
    {
        Rect full = Root.layout;
        float w = full.width, h = full.height;
        float l = Mathf.Clamp(a.x, 0f, w), r = Mathf.Clamp(b.x, 0f, w);
        float t = Mathf.Clamp(a.y, 0f, h), btm = Mathf.Clamp(b.y, 0f, h);

        Place(scrimTop, 0f, 0f, w, t);
        Place(scrimBottom, 0f, btm, w, h - btm);
        Place(scrimLeft, 0f, t, l, btm - t);
        Place(scrimRight, r, t, w - r, btm - t);
        Place(terrainFrame, a.x, a.y, b.x - a.x, b.y - a.y);
    }

    static void Place(VisualElement e, float x, float y, float w, float h)
    {
        e.style.left = x; e.style.top = y;
        e.style.width = Mathf.Max(0f, w); e.style.height = Mathf.Max(0f, h);
    }

    // ------------------------------------------------------------------ markers
    (VisualElement, Label) NewPoiMarker()
    {
        var (el, label) = NewMarker("map-poi", "map-poi-dot", "map-poi-label");
        el.pickingMode = PickingMode.Position;           // clickable (dot + label)
        el.tooltip = "Click to add / remove as a stop";
        el.RegisterCallback<ClickEvent>(OnPoiClicked);
        return (el, label);
    }

    (VisualElement, Label) NewMarker(string rootClass, string dotClass, string labelClass)
    {
        var el = new VisualElement { pickingMode = PickingMode.Ignore };
        el.AddToClassList(rootClass);
        var dot = new VisualElement { pickingMode = PickingMode.Ignore }; dot.AddToClassList(dotClass); el.Add(dot);
        var label = new Label { pickingMode = PickingMode.Ignore }; label.AddToClassList(labelClass); el.Add(label);
        markerLayer.Add(el);
        return (el, label);
    }

    // ------------------------------------------------------------------ small builders
    static VisualElement Box(VisualElement parent, string classes)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        foreach (string c in classes.Split(' ')) e.AddToClassList(c);
        parent.Add(e);
        return e;
    }

    /// <summary>A warm glass card (same look as the point-of-interest card). Blocks clicks/drags to the map.</summary>
    static VisualElement Card(VisualElement parent, string extraClass = null)
    {
        VisualElement c = Box(parent, "map-card");
        if (extraClass != null) c.AddToClassList(extraClass);
        c.pickingMode = PickingMode.Position;
        return c;
    }

    static Label Text(VisualElement parent, string classes, string text = "")
    {
        var l = new Label(text) { pickingMode = PickingMode.Ignore };
        foreach (string c in classes.Split(' ')) l.AddToClassList(c);
        parent.Add(l);
        return l;
    }

    static void KeyRow(VisualElement parent, string key, string what)
    {
        VisualElement row = Box(parent, "poi-row map-key-row");
        Text(row, "poi-key map-key", key);
        Text(row, "map-key-hint", what);
    }

    static Label Readout(VisualElement parent, string label)
    {
        VisualElement row = Box(parent, "poi-row map-readout");
        Text(row, "map-readout-label", label);
        return Text(row, "map-readout-value", "-");
    }

    static void SetText(Label l, string text)
    {
        l.text = text ?? "";
        l.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    void OnDestroy()
    {
        ExitMap();
        if (mapCam != null) Destroy(mapCam.gameObject);
    }
}
