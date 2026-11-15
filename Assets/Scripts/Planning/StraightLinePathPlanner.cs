using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PLACEHOLDER planner: walks straight lines between the stops (in the given order) and reports where the ground is
/// too steep. It exists so the UI can be built against real PlannedPath results before the real planner is ready.
/// Replace it with a terrain-aware planner (e.g. A* over MarsTerrainQuery.GetHeightGrid with a slope cost): create a
/// new class deriving from MarsPathPlanner, put it on the "Path Planner" prefab and disable/remove this one.
/// </summary>
public class StraightLinePathPlanner : MarsPathPlanner
{
    [Tooltip("Distance between path samples (m).")]
    [SerializeField, Min(0.25f)] float sampleSpacing = 1f;

    [Header("Debug")]
    [SerializeField] bool drawLastPlan = true;

    protected override IEnumerator PlanRoutine(PathPlanRequest request, Action<PlannedPath> done)
    {
        if (!MarsTerrainQuery.Available) { done(PlannedPath.Failed(request, "No terrain")); yield break; }
        if (request.stops == null || request.stops.Count == 0) { done(PlannedPath.Failed(request, "No stops chosen")); yield break; }

        var plan = new PlannedPath { request = request };
        var legs = new List<PathStop>(request.stops);
        if (request.returnToStart) legs.Add(new PathStop("Start", request.start));

        Vector3 from = MarsTerrainQuery.OnSurface(request.start);
        plan.points.Add(from);
        int steep = 0;
        foreach (PathStop stop in legs)
        {
            Vector3 to = MarsTerrainQuery.OnSurface(stop.position);
            float dist = new Vector2(to.x - from.x, to.z - from.z).magnitude;
            int n = Mathf.Max(1, Mathf.CeilToInt(dist / sampleSpacing));
            for (int i = 1; i <= n; i++)
            {
                Vector3 p = MarsTerrainQuery.OnSurface(Vector3.Lerp(from, to, (float)i / n));
                if (MarsTerrainQuery.SlopeAt(p) > request.maxSlopeDeg) steep++;
                plan.points.Add(p);
            }
            plan.visitOrder.Add(stop);
            plan.stopPointIndex.Add(plan.points.Count - 1);
            from = to;
            yield return null;                                // spread long routes over frames
        }

        ComputeStats(plan, request);
        plan.success = steep == 0;
        plan.message = plan.success
            ? $"Straight-line route: {plan.lengthM:0} m, about {plan.estimatedSeconds / 60f:0} min"
            : $"Straight-line route crosses {steep * sampleSpacing:0} m of ground steeper than {request.maxSlopeDeg:0}°";
        done(plan);
    }

    [ContextMenu("Test: start at player, visit all points of interest")]
    void TestAllPois()
    {
        var player = FindAnyObjectByType<MarsPlayerController>();
        var req = new PathPlanRequest { start = player != null ? player.transform.position : MarsGeoReference.Current.Center };
        foreach (PointOfInterest p in FindObjectsByType<PointOfInterest>(FindObjectsSortMode.None))
            if (!p.name.StartsWith("Depot tube")) req.stops.Add(PathStop.From(p));
        RequestPlan(req, p => Debug.Log($"[Planner] {(p.success ? "OK" : "FAILED")}: {p.message} · {p.points.Count} points · " +
                                         $"climb {p.climbM:0} m · max slope {p.maxSlopeDeg:0}° · {p.computeMs:0} ms", this));
    }

    void OnDrawGizmos()
    {
        PlannedPath p = LastPlan;
        if (!drawLastPlan || p == null || p.points.Count < 2 || p.request == null) return;
        for (int i = 1; i < p.points.Count; i++)
        {
            bool tooSteep = MarsTerrainQuery.SlopeAt(p.points[i]) > p.request.maxSlopeDeg;
            Gizmos.color = tooSteep ? Color.red : Color.green;
            Gizmos.DrawLine(p.points[i - 1] + Vector3.up * 0.5f, p.points[i] + Vector3.up * 0.5f);
        }
        Gizmos.color = Color.yellow;
        foreach (int i in p.stopPointIndex) Gizmos.DrawWireSphere(p.points[i], 2f);
    }
}
