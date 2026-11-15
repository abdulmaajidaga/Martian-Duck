using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Best walking route from the astronaut to the places chosen on the map (M):
///   • points of interest picked with the map's own stop list (click a marker; MarsMapMode.SelectedStops), and
///   • clicking the map locks on to the nearest point of interest (within POI Snap Pixels of the click) and
///     adds/removes it as a stop, so you don't have to hit the small marker exactly. A click, not a drag
///     (drag still pans).
///   • only with "Allow Ground Spots" on: a click far from any point of interest picks that spot on the ground
///     (Shift + click adds another spot instead of replacing it).
///   • Right click clears the clicked spots and the map's stop list.
/// The route is re-planned whenever the choice changes and drawn by PlannedPathRenderer. When there is no
/// MarsMission guiding the player, a successful plan also becomes the active route (RouteManager), so the HUD's
/// waypoint panel guides the walk; with a mission running the mission keeps the route and the path is only drawn.
/// </summary>
public class MapClickRoutePlanner : MonoBehaviour
{
    [Tooltip("Make a successful plan the active route (waypoint panel + map markers), unless a MarsMission is guiding.")]
    [SerializeField] bool followPlannedRoute = true;
    [Tooltip("Let the planner choose the best order for the stops (off = the order they were picked).")]
    [SerializeField] bool reorderStops = true;
    [Tooltip("Distance (m) at which a planned stop counts as reached.")]
    [SerializeField, Min(1f)] float arriveRadius = 8f;
    [Tooltip("A press that moves more than this (screen pixels) is a drag (map pan), not a click.")]
    [SerializeField, Min(1f)] float clickMaxMovePixels = 6f;
    [Tooltip("A click on the map locks on to the nearest point of interest within this distance (screen pixels).")]
    [SerializeField, Min(4f)] float poiSnapPixels = 60f;
    [Tooltip("Let a click far from every point of interest pick a spot on the bare ground. Off = points of interest only.")]
    [SerializeField] bool allowGroundSpots;
    [SerializeField] bool logPlans = true;

    /// <summary>The active instance (null if none).</summary>
    public static MapClickRoutePlanner Current { get; private set; }

    /// <summary>True while the player has chosen places on the map (other planners, e.g. MissionPathGuide, wait).</summary>
    public bool HasStops => spots.Count > 0 || SelectedPois().Count > 0;

    readonly List<PathStop> spots = new List<PathStop>();     // clicked ground spots
    PathPlanRequest pending;                                  // asked for while the planner was busy
    MarsMapMode subscribedMap;
    bool pressOnMap, wasShowingPlan;
    Vector2 pressPos;

    void OnEnable() => Current = this;

    void OnDisable()
    {
        if (Current == this) Current = null;
        Subscribe(null);
    }

    void Update()
    {
        if (subscribedMap != MarsMapMode.Current) Subscribe(MarsMapMode.Current);

        MarsHud hud = MarsHud.Current;
        if (hud == null || hud.Mode != HudModes.Map) { pressOnMap = false; return; }

        Vector2 pointer = PointerPosition;
        if (RightClicked && !OverHud(hud, pointer)) { ClearAll(); return; }

        // A click is a press and release on the map without moving (moving = the map's drag-to-pan).
        if (LeftPressed) { pressOnMap = !OverHud(hud, pointer); pressPos = pointer; }
        if (pressOnMap && (pointer - pressPos).magnitude > clickMaxMovePixels) pressOnMap = false;
        if (!LeftReleased || !pressOnMap) return;
        pressOnMap = false;

        Camera cam = MapCamera();
        if (cam == null) return;
        // Lock on to a point of interest near the click (the map's stop list then raises SelectionChanged -> RequestPlan).
        PointOfInterest poi = NearestPoiOnScreen(cam, pointer, poiSnapPixels);
        if (poi != null)
        {
            if (subscribedMap != null) subscribedMap.ToggleStop(poi);
            return;
        }
        if (!allowGroundSpots) return;

        Vector3 w = cam.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, cam.nearClipPlane));
        if (!MarsTerrainQuery.InBounds(w)) return;
        w = MarsTerrainQuery.OnSurface(w);
        if (!ShiftHeld) spots.Clear();
        spots.Add(new PathStop(DustAwareAStarPlanner.DescribeSpot(w), w));
        RequestPlan();
    }

    void LateUpdate()
    {
        MarsPathPlanner planner = MarsPathPlanner.Current;
        if (pending != null && planner != null && !planner.IsPlanning)
        {
            PathPlanRequest r = pending; pending = null;
            planner.RequestPlan(r, OnPlanned);
        }
    }

    void Subscribe(MarsMapMode map)
    {
        if (subscribedMap != null) subscribedMap.SelectionChanged -= RequestPlan;
        subscribedMap = map;
        if (subscribedMap != null) subscribedMap.SelectionChanged += RequestPlan;
    }

    List<PointOfInterest> SelectedPois()
    {
        var list = new List<PointOfInterest>();
        if (subscribedMap != null) foreach (PointOfInterest p in subscribedMap.SelectedStops) if (p != null) list.Add(p);
        return list;
    }

    /// <summary>Plan from the astronaut through the map's selected points of interest and the clicked spots.</summary>
    public void RequestPlan()
    {
        MarsPathPlanner planner = MarsPathPlanner.Current;
        if (planner == null) return;
        PathPlanRequest req = DustAwareAStarPlanner.CreateRequest(DustAwareAStarPlanner.PlayerPosition());
        foreach (PointOfInterest p in SelectedPois()) req.stops.Add(PathStop.From(p));
        req.stops.AddRange(spots);

        if (req.stops.Count == 0)
        {
            pending = null;
            if (wasShowingPlan) ClearRouteAndLine();
            return;
        }
        req.allowReorder = reorderStops && req.stops.Count > 1;
        if (planner.IsPlanning) pending = req;     // replaces any older pending request; runs when the planner is free
        else planner.RequestPlan(req, OnPlanned);
    }

    /// <summary>Forget the clicked spots and the map's stop list, and hide the route.</summary>
    public void ClearAll()
    {
        spots.Clear();
        pending = null;
        if (subscribedMap != null && subscribedMap.SelectedStops.Count > 0) subscribedMap.ClearStops();   // raises SelectionChanged
        ClearRouteAndLine();
    }

    void ClearRouteAndLine()
    {
        wasShowingPlan = false;
        PlannedPathRenderer.ClearAll();
        RouteManager route = RouteManager.Current;
        if (route != null && route.CurrentPlan != null && !MissionRunning()) route.SetRoute((PlannedPath)null);
    }

    void OnPlanned(PlannedPath plan)
    {
        if (logPlans) Debug.Log($"[Map route] {(plan.success ? "OK" : "FAILED")}: {plan.message} · {plan.computeMs:0} ms", this);
        if (!plan.success || !HasStops) return;     // failed, or the choice was cleared while planning
        wasShowingPlan = true;
        PlannedPathRenderer.ShowPlan(plan);
        if (followPlannedRoute && !MissionRunning() && RouteManager.Current != null) RouteManager.Current.SetRoute(plan, arriveRadius);
    }

    static bool MissionRunning()
    {
        MarsMission m = FindAnyObjectByType<MarsMission>();
        return m != null && m.isActiveAndEnabled && m.CurrentStage != MarsMission.Stage.AllComplete;
    }

    /// <summary>True when the pointer is over any HUD element that takes the mouse (cards, POI markers, layer panel).</summary>
    static bool OverHud(MarsHud hud, Vector2 screenPos)
    {
        IPanel panel = hud.OverlayLayer != null ? hud.OverlayLayer.panel : null;
        if (panel == null) return false;
        return panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y))) != null;
    }

    /// <summary>The active point of interest whose map position is closest to <paramref name="screenPos"/>, within <paramref name="maxPixels"/>.</summary>
    static PointOfInterest NearestPoiOnScreen(Camera cam, Vector2 screenPos, float maxPixels)
    {
        PointOfInterest best = null;
        float bestD = maxPixels;
        foreach (PointOfInterest p in PointOfInterest.Registered)
        {
            if (p == null || !p.isActiveAndEnabled) continue;
            Vector3 sp = cam.WorldToScreenPoint(p.Position);
            if (sp.z < 0f) continue;
            float d = Vector2.Distance(screenPos, sp);
            if (d <= bestD) { bestD = d; best = p; }
        }
        return best;
    }

    /// <summary>The top-down map camera (enabled orthographic camera looking straight down).</summary>
    public static Camera MapCamera()
    {
        foreach (Camera c in Camera.allCameras)
            if (c.orthographic && c.isActiveAndEnabled && Vector3.Dot(c.transform.forward, Vector3.down) > 0.99f) return c;
        return null;
    }

#if ENABLE_INPUT_SYSTEM
    static bool LeftPressed => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    static bool LeftReleased => Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
    static bool RightClicked => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
    static Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    static bool ShiftHeld => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
#else
    static bool LeftPressed => Input.GetMouseButtonDown(0);
    static bool LeftReleased => Input.GetMouseButtonUp(0);
    static bool RightClicked => Input.GetMouseButtonDown(1);
    static Vector2 PointerPosition => Input.mousePosition;
    static bool ShiftHeld => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
}
