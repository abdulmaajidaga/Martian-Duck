using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================================================================
// PATH PLANNING CONTRACT (shared by the Planning and UI owners; change only by agreement, see TEAM.md)
//
//   UI (choose stops)  --RequestPlan(PathPlanRequest)-->  MarsPathPlanner (Planning)  --PlanCompleted(PlannedPath)-->
//   UI (draw path, show stats)  +  RouteManager.SetRoute(PlannedPath)  ->  existing HUD waypoint guidance
//
// The UI never needs to know how the planner works, and the planner never touches UI code.
// =====================================================================================================================

/// <summary>One place the player wants to visit.</summary>
[Serializable]
public struct PathStop
{
    public string name;
    public Vector3 position;              // world position (on the terrain surface)
    public PointOfInterest poi;           // optional: the point of interest this stop came from

    public PathStop(string name, Vector3 position, PointOfInterest poi = null)
    {
        this.name = name; this.position = position; this.poi = poi;
    }

    public static PathStop From(PointOfInterest p) => new PathStop(p.displayName, p.Position, p);
}

/// <summary>What the UI asks the planner for.</summary>
[Serializable]
public class PathPlanRequest
{
    public Vector3 start;                                     // usually the player's position
    public List<PathStop> stops = new List<PathStop>();       // in the order the player chose them
    [Tooltip("Planner may reorder the stops to make the walk shorter/easier.")]
    public bool allowReorder;
    public bool returnToStart;
    [Tooltip("Steepest ground the astronaut can walk on (degrees). Matches MarsPlayerController's slope limit.")]
    public float maxSlopeDeg = 30f;
    [Tooltip("Walking speed used for time estimates (m/s). Matches MarsPlayerController's walk speed.")]
    public float walkSpeed = 2.5f;
}

/// <summary>What the planner returns.</summary>
[Serializable]
public class PlannedPath
{
    public bool success;                                      // false = no walkable route found (see message)
    public string message = "";                               // short, human-readable (shown in the UI)
    public PathPlanRequest request;

    public List<Vector3> points = new List<Vector3>();        // the full path, world positions on the surface
    public List<PathStop> visitOrder = new List<PathStop>();  // stops in the order the path reaches them
    public List<int> stopPointIndex = new List<int>();        // index into points where each visitOrder stop is reached

    public float lengthM;                                     // horizontal + vertical length along the path
    public float climbM;                                      // total ascent
    public float descentM;                                    // total descent
    public float maxSlopeDeg;                                 // steepest ground the path crosses
    public float estimatedSeconds;                            // walking time estimate
    public float computeMs;                                   // how long planning took

    public static PlannedPath Failed(PathPlanRequest req, string why) =>
        new PlannedPath { success = false, message = why, request = req };
}

/// <summary>
/// Base class for path planners. The Planning owner implements PlanRoutine; everyone else only calls RequestPlan and
/// listens to PlanCompleted. Put exactly one active planner in the scene (the "Path Planner" prefab).
/// </summary>
public abstract class MarsPathPlanner : MonoBehaviour
{
    static MarsPathPlanner current;

    /// <summary>The active planner in the scene (found automatically).</summary>
    public static MarsPathPlanner Current
    {
        get
        {
            if (current == null) current = FindAnyObjectByType<MarsPathPlanner>();
            return current;
        }
    }

    /// <summary>Raised when planning starts (UI can show a "planning..." state).</summary>
    public static event Action<PathPlanRequest> PlanStarted;
    /// <summary>Raised when a plan is ready (successful or not).</summary>
    public static event Action<PlannedPath> PlanCompleted;

    public PlannedPath LastPlan { get; private set; }
    public bool IsPlanning { get; private set; }

    protected virtual void OnEnable() => current = this;

    /// <summary>
    /// Ask for a plan. In Play mode this runs as a coroutine (planners may spread work over frames by yielding);
    /// in Edit mode it runs to completion immediately. The result arrives via <paramref name="onDone"/> and PlanCompleted.
    /// </summary>
    public void RequestPlan(PathPlanRequest request, Action<PlannedPath> onDone = null)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        IsPlanning = true;
        PlanStarted?.Invoke(request);
        float t0 = Time.realtimeSinceStartup;
        void Done(PlannedPath p)
        {
            p ??= PlannedPath.Failed(request, "Planner returned nothing");
            p.request ??= request;
            p.computeMs = (Time.realtimeSinceStartup - t0) * 1000f;
            IsPlanning = false;
            LastPlan = p;
            onDone?.Invoke(p);
            PlanCompleted?.Invoke(p);
        }

        IEnumerator routine = PlanRoutine(request, Done);
        if (Application.isPlaying && isActiveAndEnabled) StartCoroutine(routine);
        else while (routine.MoveNext()) { }
    }

    /// <summary>Compute the path and call <paramref name="done"/> exactly once. Yield to spread work over frames.</summary>
    protected abstract IEnumerator PlanRoutine(PathPlanRequest request, Action<PlannedPath> done);

    /// <summary>Fill in length / climb / descent / max slope / time from <c>plan.points</c>.</summary>
    protected static void ComputeStats(PlannedPath plan, PathPlanRequest request)
    {
        plan.lengthM = plan.climbM = plan.descentM = plan.maxSlopeDeg = 0f;
        for (int i = 0; i < plan.points.Count; i++)
        {
            plan.maxSlopeDeg = Mathf.Max(plan.maxSlopeDeg, MarsTerrainQuery.SlopeAt(plan.points[i]));
            if (i == 0) continue;
            Vector3 d = plan.points[i] - plan.points[i - 1];
            plan.lengthM += d.magnitude;
            if (d.y > 0f) plan.climbM += d.y; else plan.descentM -= d.y;
        }
        // Simple estimate: walking speed, plus Naismith-style extra time for climbing (1 min per 10 m up).
        float speed = Mathf.Max(0.1f, request.walkSpeed);
        plan.estimatedSeconds = plan.lengthM / speed + plan.climbM * 6f;
    }
}
