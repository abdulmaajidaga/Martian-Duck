using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the best walkable path for the current MarsMission objective (needs no change to the mission itself):
///   • going to the depot / collecting tubes: the best order through every tube still on the ground,
///   • any other stage: the path to the mission's guide waypoint (drill site, rocket, ...).
/// Re-plans when the mission moves on (stage change, tube collected, site drilled), when the objective moves, and
/// when the astronaut strays from the drawn path. Places chosen on the map (MapClickRoutePlanner) take priority;
/// clearing them brings the mission path back. The mission keeps control of the RouteManager waypoint.
/// </summary>
public class MissionPathGuide : MonoBehaviour
{
    [SerializeField] bool guideMission = true;
    [Tooltip("While going to the depot and collecting, plan the best order through all remaining tubes.")]
    [SerializeField] bool planTubeCollectionOrder = true;
    [Tooltip("Points of interest whose display name starts with this are the mission's sample tubes.")]
    [SerializeField] string tubePrefix = "Depot tube";
    [Tooltip("When a new mission starts, clear stops picked on the map so the path goes to the mission's next target.")]
    [SerializeField] bool clearMapStopsOnNewMission = true;

    [Header("When to re-plan")]
    [SerializeField, Min(0.1f)] float checkInterval = 0.5f;
    [Tooltip("Re-plan when the objective moves more than this (m).")]
    [SerializeField, Min(1f)] float targetMovedM = 15f;
    [Tooltip("Re-plan when the astronaut is further than this from the drawn path (m).")]
    [SerializeField, Min(5f)] float offRouteM = 40f;
    [Tooltip("At most one re-plan for straying / a moving objective in this many seconds.")]
    [SerializeField, Min(0.5f)] float minReplanSeconds = 4f;
    [SerializeField] bool logPlans = true;

    MarsMission mission;
    readonly HashSet<PointOfInterest> collected = new HashSet<PointOfInterest>();
    PlannedPath current;
    Vector3 lastTarget;
    bool dirty = true, showing, inFlight;
    float nextCheck, lastPlanTime = -999f;

    void OnEnable()
    {
        MarsMission.StageChanged += OnStage;
        MarsMission.TubeCollected += OnTube;
        MarsMission.SiteDrilled += OnSite;
        dirty = true;
    }

    void OnDisable()
    {
        MarsMission.StageChanged -= OnStage;
        MarsMission.TubeCollected -= OnTube;
        MarsMission.SiteDrilled -= OnSite;
        if (showing) PlannedPathRenderer.ClearAll();
        showing = false;
    }

    void OnStage(MarsMission m)
    {
        dirty = true;
        nextCheck = 0f;                       // plan on the next frame, not after the check interval
        // A new mission starts: drop stops left over from the map so the planner goes straight to the new target.
        if (clearMapStopsOnNewMission && (m.CurrentStage == MarsMission.Stage.DrillSites || m.CurrentStage == MarsMission.Stage.GoToDepot)
            && MapClickRoutePlanner.Current != null && MapClickRoutePlanner.Current.HasStops)
            MapClickRoutePlanner.Current.ClearAll();
    }
    void OnTube(MarsMission m, PointOfInterest p) { collected.Add(p); dirty = true; }
    void OnSite(MarsMission m, MarsMission.DrillSite s) { dirty = true; nextCheck = 0f; }

    void Update()
    {
        if (!guideMission || Time.time < nextCheck) return;
        nextCheck = Time.time + checkInterval;

        if (mission == null) mission = FindAnyObjectByType<MarsMission>();
        if (mission == null || !mission.isActiveAndEnabled) return;

        // The player's own choice on the map wins; come back to the mission when it is cleared.
        if (MapClickRoutePlanner.Current != null && MapClickRoutePlanner.Current.HasStops) { showing = false; dirty = true; return; }

        if (mission.CurrentStage == MarsMission.Stage.AllComplete)
        {
            if (showing) PlannedPathRenderer.ClearAll();
            showing = false;
            return;
        }

        MarsPathPlanner planner = MarsPathPlanner.Current;
        if (planner == null || planner.IsPlanning || inFlight) return;

        Vector3 player = DustAwareAStarPlanner.PlayerPosition();
        var stops = new List<PathStop>();
        bool collecting = planTubeCollectionOrder &&
                          (mission.CurrentStage == MarsMission.Stage.GoToDepot || mission.CurrentStage == MarsMission.Stage.CollectTubes);
        if (collecting)
            foreach (PointOfInterest p in PointOfInterest.Registered)
                if (p != null && p.isActiveAndEnabled && !collected.Contains(p) && p.displayName.StartsWith(tubePrefix, System.StringComparison.OrdinalIgnoreCase))
                    stops.Add(PathStop.From(p));
        if (stops.Count == 0)
        {
            collecting = false;
            Waypoint next = RouteManager.Current != null ? RouteManager.Current.Next : null;
            if (next == null) return;
            stops.Add(new PathStop(next.displayName, next.Position));
        }

        Vector3 target = stops[0].position;
        bool moved = !collecting && Flat(target, lastTarget) > targetMovedM;
        bool strayed = current != null && DistanceToPath(player, current.points) > offRouteM;
        bool due = Time.time - lastPlanTime >= minReplanSeconds;
        if (!(dirty || !showing || ((moved || strayed) && due))) return;

        dirty = false;
        lastPlanTime = Time.time;
        lastTarget = target;
        PathPlanRequest req = DustAwareAStarPlanner.CreateRequest(player);
        req.stops = stops;
        req.allowReorder = collecting && stops.Count > 1;
        inFlight = true;
        planner.RequestPlan(req, OnPlanned);
    }

    void OnPlanned(PlannedPath plan)
    {
        inFlight = false;
        if (MapClickRoutePlanner.Current != null && MapClickRoutePlanner.Current.HasStops) return;   // map choice took over
        if (!plan.success)
        {
            if (logPlans && !plan.message.StartsWith("Cancelled")) Debug.LogWarning($"[Mission path] {plan.message}", this);
            return;
        }
        current = plan;
        showing = true;
        PlannedPathRenderer.ShowPlan(plan);
        if (logPlans) Debug.Log($"[Mission path] {mission.CurrentStage}: {plan.message} · {plan.computeMs:0} ms", this);
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    /// <summary>Horizontal distance from a point to a polyline.</summary>
    static float DistanceToPath(Vector3 p, List<Vector3> pts)
    {
        if (pts == null || pts.Count == 0) return float.MaxValue;
        var q = new Vector2(p.x, p.z);
        float best = float.MaxValue;
        for (int i = 1; i < pts.Count; i++)
        {
            var a = new Vector2(pts[i - 1].x, pts[i - 1].z);
            var ab = new Vector2(pts[i].x, pts[i].z) - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
            best = Mathf.Min(best, (a + ab * t - q).sqrMagnitude);
        }
        return Mathf.Sqrt(best);
    }
}
