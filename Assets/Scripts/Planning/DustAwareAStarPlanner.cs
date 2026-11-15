using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Terrain- and dust-aware path planner: A* over the terrain height grid, with walking time as the cost.
///
/// Cost of a step between two grid cells:
///   time   = 3D length / (walk speed × Tobler(grade))           Tobler's hiking function: fastest on a gentle
///                                                                downhill, slower uphill and on steep downhill
///   × (1 + slopeWeight · (slope / max slope)²)                    keeps a margin from the slope limit
///   × (1 + dustWeight · dustiness)                                avoids dusty ground (TES Dust Cover Index)
/// Cells steeper than the request's max slope, and steps whose grade is steeper, cannot be walked.
///
/// Several stops: with <c>allowReorder</c> the planner measures the real walking cost between every pair of stops
/// (one Dijkstra search per stop) and picks the best visiting order (exact up to <see cref="exactOrderUpTo"/>
/// stops, nearest-neighbour + 2-opt above). The grid path is then straightened where a direct line is walkable
/// and no more expensive, and resampled every <see cref="outputSpacingM"/> metres on the surface.
///
/// Large areas are searched on a coarser grid so a search never exceeds <see cref="maxSearchCells"/> cells; work is
/// spread over frames in Play mode.
/// </summary>
public class DustAwareAStarPlanner : MarsPathPlanner
{
    [Header("Search")]
    [Tooltip("Largest search grid (cells). Bigger areas are searched with a coarser step (2, 3, ... samples).")]
    [SerializeField, Min(10000)] int maxSearchCells = 2_500_000;
    [Tooltip("Ground around the start and stops that the search may use (m, at least).")]
    [SerializeField, Min(50f)] float minMarginM = 800f;
    [Tooltip("Extra ground around the start and stops as a fraction of their spread.")]
    [SerializeField, Range(0f, 1f)] float marginFraction = 0.35f;
    [Tooltip("1 = exact A*. Slightly above 1 is much faster on long routes and only rarely gives a longer one.")]
    [SerializeField, Range(1f, 2f)] float heuristicWeight = 1.05f;
    [Tooltip("Grid cells expanded per frame in Play mode (keeps the game responsive).")]
    [SerializeField, Min(1000)] int expansionsPerFrame = 150_000;

    [Header("Cost")]
    [Tooltip("Penalty for walking near the slope limit: cost × (1 + w·(slope/limit)²).")]
    [SerializeField, Min(0f)] float slopeWeight = 1.5f;
    [Tooltip("Penalty for dusty ground: cost × (1 + w·dustiness), dustiness 0..1 from the dust map. 0 = ignore dust.")]
    [SerializeField, Min(0f)] float dustWeight = 0.6f;
    [Tooltip("Dust data (TES Dust Cover Index). If empty, the MarsDustMap in the scene is used.")]
    [SerializeField] MarsDustMap dustMap;

    [Header("Stops")]
    [Tooltip("A stop or start on ground that is too steep is moved to the nearest walkable ground within this distance (m).")]
    [SerializeField, Min(0f)] float snapRadiusM = 200f;
    [Tooltip("Leave out stops that cannot be reached (and say so) instead of failing the whole plan.")]
    [SerializeField] bool skipUnreachableStops = true;
    [Tooltip("Exact best order up to this many stops; above it, nearest-neighbour + 2-opt.")]
    [SerializeField, Range(2, 16)] int exactOrderUpTo = 15;

    [Header("Output")]
    [Tooltip("Replace zig-zags on the grid with straight lines where that is walkable and not more expensive.")]
    [SerializeField] bool smoothPath = true;
    [Tooltip("Distance between output points on the surface (m).")]
    [SerializeField, Min(0.5f)] float outputSpacingM = 5f;

    [Header("Requests made by the planning components")]
    [Tooltip("Walking speed for time estimates (m/s). MarsPlayerController walks at 4 m/s.")]
    [SerializeField, Min(0.1f)] float defaultWalkSpeed = 4f;
    [Tooltip("Steepest ground routes may cross (degrees). 30° is a real suited astronaut's limit; the player controller allows 40°.")]
    [SerializeField, Range(5f, 60f)] float defaultMaxSlopeDeg = 30f;

    [Header("Sample collection")]
    [Tooltip("Points of interest with one of these categories count as samples to pick up.")]
    [SerializeField] List<string> sampleCategories = new List<string> { "Depot tube", "Sample site" };
    [SerializeField] bool sampleRouteReturnsToStart;

    [Header("Debug")]
    [SerializeField] bool drawLastPlan = true;
    [SerializeField] bool logPlans = true;

    /// <summary>Mean dustiness (0..1) along the last successful plan.</summary>
    public float LastMeanDustiness { get; private set; }

    const float ToblerPeak = 1.19125f;   // exp(3.5 · 0.05): Tobler speed factor on the fastest (-2.9°) grade
    int generation;

    // =================================================================================================== public API

    /// <summary>A request with this planner's walking speed and slope limit (start = the player if not given).</summary>
    public PathPlanRequest NewRequest(Vector3? start = null) =>
        new PathPlanRequest { start = start ?? PlayerPosition(), walkSpeed = defaultWalkSpeed, maxSlopeDeg = defaultMaxSlopeDeg };

    /// <summary>A request from <paramref name="start"/>, using the active planner's defaults when it is this planner.</summary>
    public static PathPlanRequest CreateRequest(Vector3 start) =>
        MarsPathPlanner.Current is DustAwareAStarPlanner p ? p.NewRequest(start) : new PathPlanRequest { start = start };

    /// <summary>Plan from the player to one spot (e.g. a point clicked on the map).</summary>
    public void PlanTo(Vector3 target, Action<PlannedPath> onDone = null, string targetName = null)
    {
        PathPlanRequest req = NewRequest();
        req.stops.Add(new PathStop(targetName ?? DescribeSpot(target), MarsTerrainQuery.OnSurface(target)));
        RequestPlan(req, onDone ?? LogResult);
    }

    /// <summary>Plan the best walk from the player that picks up every sample (points of interest in Sample Categories).</summary>
    public void PlanSampleCollection(Action<PlannedPath> onDone = null)
    {
        PathPlanRequest req = NewRequest();
        req.allowReorder = true; req.returnToStart = sampleRouteReturnsToStart;
        foreach (PointOfInterest p in PointOfInterest.Registered)
            if (p != null && p.isActiveAndEnabled && IsSample(p)) req.stops.Add(PathStop.From(p));
        RequestPlan(req, onDone ?? LogResult);
    }

    public bool IsSample(PointOfInterest p)
    {
        foreach (string c in sampleCategories)
            if (!string.IsNullOrEmpty(c) && string.Equals(p.category, c, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static string DescribeSpot(Vector3 world)
    {
        MarsGeoReference geo = MarsGeoReference.Current;
        if (geo == null) return "Map point";
        geo.WorldToLonLat(world, out double lon, out double lat);
        return $"Map point {MarsGeoReference.FormatLatitude(lat)} {MarsGeoReference.FormatLongitude(lon)}";
    }

    public static Vector3 PlayerPosition()
    {
        var player = FindAnyObjectByType<MarsPlayerController>();
        if (player != null) return player.transform.position;
        return MarsGeoReference.Current != null ? MarsGeoReference.Current.Center : Vector3.zero;
    }

    void LogResult(PlannedPath p)
    {
        PlannedPathRenderer.ShowPlan(p);
        if (logPlans)
            Debug.Log($"[Planner] {(p.success ? "OK" : "FAILED")}: {p.message} · {p.points.Count} points · climb {p.climbM:0} m · " +
                      $"max slope {p.maxSlopeDeg:0}° · dust {LastMeanDustiness:0.00} · {p.computeMs:0} ms", this);
    }

    [ContextMenu("Test: collect all samples (best order)")]
    void TestSamples() => PlanSampleCollection();

    [ContextMenu("Test: start at player, visit all points of interest")]
    void TestAllPois()
    {
        PathPlanRequest req = NewRequest();
        foreach (PointOfInterest p in PointOfInterest.Registered)
            if (p != null && p.isActiveAndEnabled && !p.name.StartsWith("Depot tube")) req.stops.Add(PathStop.From(p));
        RequestPlan(req, LogResult);
    }

    [ContextMenu("Test: player to the map centre")]
    void TestCentre()
    {
        if (MarsGeoReference.Current != null) PlanTo(MarsGeoReference.Current.Center);
    }

    // =================================================================================================== planning

    class Point
    {
        public PathStop stop;
        public Vector3 surface;     // where the player asked to go, on the ground
        public int cell = -1;       // walkable cell used for the search
    }

    protected override IEnumerator PlanRoutine(PathPlanRequest request, Action<PlannedPath> done)
    {
        int myGen = ++generation;
        if (!MarsTerrainQuery.Available || ActiveTerrain() == null) { done(PlannedPath.Failed(request, "No terrain")); yield break; }
        if (request.stops == null || request.stops.Count == 0) { done(PlannedPath.Failed(request, "No stops chosen")); yield break; }

        var notes = new List<string>();
        var pts = new List<Point> { new Point { stop = new PathStop("Start", request.start), surface = MarsTerrainQuery.OnSurface(request.start) } };
        if (!MarsTerrainQuery.InBounds(request.start)) { done(PlannedPath.Failed(request, "Start is outside the map")); yield break; }
        foreach (PathStop s in request.stops)
        {
            if (MarsTerrainQuery.InBounds(s.position)) pts.Add(new Point { stop = s, surface = MarsTerrainQuery.OnSurface(s.position) });
            else notes.Add($"{s.name} is outside the map");
        }
        if (pts.Count < 2) { done(PlannedPath.Failed(request, "No stops inside the map")); yield break; }

        // ---- search grid around everything
        var bboxMin = new Vector2(float.MaxValue, float.MaxValue);
        var bboxMax = new Vector2(float.MinValue, float.MinValue);
        foreach (Point p in pts)
        {
            bboxMin = Vector2.Min(bboxMin, new Vector2(p.surface.x, p.surface.z));
            bboxMax = Vector2.Max(bboxMax, new Vector2(p.surface.x, p.surface.z));
        }
        float margin = Mathf.Max(minMarginM, (bboxMax - bboxMin).magnitude * marginFraction);
        SearchGrid grid = BuildGrid(bboxMin - Vector2.one * margin, bboxMax + Vector2.one * margin, request);
        yield return null;
        if (myGen != generation) { done(Superseded(request)); yield break; }

        // ---- put the start and stops on walkable cells
        for (int i = 0; i < pts.Count; i++)
        {
            pts[i].cell = grid.SnapToWalkable(pts[i].surface, snapRadiusM);
            if (pts[i].cell >= 0) continue;
            if (i == 0) { done(PlannedPath.Failed(request, $"No walkable ground within {snapRadiusM:0} m of the start")); yield break; }
            notes.Add($"{pts[i].stop.name}: no walkable ground nearby");
        }
        var stopsOk = new List<int>();
        for (int i = 1; i < pts.Count; i++) if (pts[i].cell >= 0) stopsOk.Add(i);
        if (stopsOk.Count == 0) { done(PlannedPath.Failed(request, string.Join("; ", notes))); yield break; }

        // ---- visiting order
        List<int> order = stopsOk;
        if (request.allowReorder && stopsOk.Count >= 2)
        {
            int m = pts.Count;
            var cost = new float[m, m];
            for (int a = 0; a < m; a++)
                for (int b = 0; b < m; b++) cost[a, b] = a == b ? 0f : float.PositiveInfinity;
            var targets = new List<int>();
            foreach (Point p in pts) targets.Add(p.cell);
            var all = new List<int> { 0 }; all.AddRange(stopsOk);
            foreach (int a in all)
            {
                var costs = new float[m];
                IEnumerator s = grid.Search(pts[a].cell, targets, false, 1f, expansionsPerFrame, costs);
                while (s.MoveNext())
                {
                    yield return null;
                    if (myGen != generation) { done(Superseded(request)); yield break; }
                }
                for (int b = 0; b < m; b++) if (b != a && pts[b].cell >= 0) cost[a, b] = costs[b];
            }

            var reachable = new List<int>();
            foreach (int i in stopsOk)
            {
                if (!float.IsInfinity(cost[0, i])) reachable.Add(i);
                else notes.Add($"{pts[i].stop.name} can't be reached");
            }
            if (reachable.Count == 0 || (!skipUnreachableStops && reachable.Count < stopsOk.Count))
            { done(PlannedPath.Failed(request, string.Join("; ", notes))); yield break; }
            order = StopOrder.Best(cost, reachable, request.returnToStart, exactOrderUpTo);
        }

        // ---- walk the legs
        var plan = new PlannedPath { request = request };
        var sequence = new List<int>(order);
        if (request.returnToStart) sequence.Add(0);
        int fromIdx = 0;
        plan.points.Add(pts[0].surface);
        float dustSum = 0f; int dustN = 0;
        SearchGrid fallback = null;

        foreach (int toIdx in sequence)
        {
            var nodes = new List<int>();
            SearchGrid used = grid;
            IEnumerator leg = grid.FindPath(pts[fromIdx].cell, pts[toIdx].cell, heuristicWeight, expansionsPerFrame, nodes);
            while (leg.MoveNext())
            {
                yield return null;
                if (myGen != generation) { done(Superseded(request)); yield break; }
            }
            if (nodes.Count == 0 && !grid.CoversWholeMap)
            {
                // The way round may lie outside the search area: try once more over the whole map (coarser).
                Vector3 o = MarsTerrainQuery.Origin, size = MarsTerrainQuery.Size;
                fallback ??= BuildGrid(new Vector2(o.x, o.z), new Vector2(o.x + size.x, o.z + size.z), request);
                int a = fallback.SnapToWalkable(plan.points[plan.points.Count - 1], snapRadiusM);
                int b = fallback.SnapToWalkable(pts[toIdx].surface, snapRadiusM);
                if (a >= 0 && b >= 0)
                {
                    used = fallback;
                    leg = fallback.FindPath(a, b, heuristicWeight, expansionsPerFrame, nodes);
                    while (leg.MoveNext())
                    {
                        yield return null;
                        if (myGen != generation) { done(Superseded(request)); yield break; }
                    }
                }
            }

            if (nodes.Count == 0)
            {
                string what = toIdx == 0 ? "the way back to the start" : pts[toIdx].stop.name;
                notes.Add($"no walkable route to {what}");
                if (!skipUnreachableStops) { done(PlannedPath.Failed(request, $"No walkable route to {what}")); yield break; }
                continue;   // stay where we are, try the next stop
            }

            List<Vector3> line = used.ToWorld(nodes);
            if (smoothPath) line = used.Smooth(nodes, line);
            line[0] = plan.points[plan.points.Count - 1];         // continue from where the last leg ended
            line[line.Count - 1] = pts[toIdx].surface;           // finish exactly at the stop
            AppendSurfacePolyline(plan.points, line, outputSpacingM);

            foreach (int n in nodes) { dustSum += used.dust[n]; dustN++; }
            plan.visitOrder.Add(pts[toIdx].stop);
            plan.stopPointIndex.Add(plan.points.Count - 1);
            fromIdx = toIdx;
            if (used != grid && pts[toIdx].cell < 0) pts[toIdx].cell = grid.SnapToWalkable(pts[toIdx].surface, snapRadiusM);
            yield return null;
            if (myGen != generation) { done(Superseded(request)); yield break; }
        }

        if (plan.visitOrder.Count == 0) { done(PlannedPath.Failed(request, "No walkable route: " + string.Join("; ", notes))); yield break; }

        ComputeStats(plan, request);
        LastMeanDustiness = dustN > 0 ? dustSum / dustN : 0f;
        plan.success = true;
        int reached = plan.visitOrder.Count - (request.returnToStart && fromIdx == 0 ? 1 : 0);
        string stopsText = request.stops.Count == 1 ? "" : $"{reached}/{request.stops.Count} stops · ";
        plan.message = $"{stopsText}{FormatDistance(plan.lengthM)}, about {FormatTime(plan.estimatedSeconds)} · " +
                       $"max {plan.maxSlopeDeg:0}° · dust {DustWord(LastMeanDustiness)}" +
                       (notes.Count > 0 ? " · " + string.Join("; ", notes) : "");
        done(plan);
    }

    PlannedPath Superseded(PathPlanRequest r) => PlannedPath.Failed(r, "Cancelled: a newer plan was requested");

    static string FormatDistance(float m) => m >= 1000f ? $"{m / 1000f:0.0} km" : $"{m:0} m";
    static string FormatTime(float s) => s >= 3600f ? $"{Mathf.FloorToInt(s / 3600f)} h {Mathf.RoundToInt(s % 3600f / 60f)} min" : $"{Mathf.Max(1, Mathf.RoundToInt(s / 60f))} min";
    static string DustWord(float d) => d < 0.25f ? "low" : d < 0.55f ? "moderate" : "high";

    static void AppendSurfacePolyline(List<Vector3> output, List<Vector3> line, float spacing)
    {
        for (int i = 1; i < line.Count; i++)
        {
            Vector3 a = line[i - 1], b = line[i];
            float dist = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            int n = Mathf.Max(1, Mathf.CeilToInt(dist / spacing));
            for (int k = 1; k <= n; k++) output.Add(MarsTerrainQuery.OnSurface(Vector3.Lerp(a, b, (float)k / n)));
        }
    }

    static Terrain ActiveTerrain()
    {
        MarsGeoReference geo = MarsGeoReference.Current;
        if (geo != null && geo.ResolveTerrain()) return geo.terrain;
        return Terrain.activeTerrain;
    }

    SearchGrid BuildGrid(Vector2 min, Vector2 max, PathPlanRequest request)
    {
        if (dustMap == null) dustMap = MarsDustMap.Current;
        if (dustMap != null && !dustMap.Loaded) dustMap = null;
        return new SearchGrid(ActiveTerrain(), min, max, maxSearchCells, request.maxSlopeDeg, Mathf.Max(0.1f, request.walkSpeed),
                              slopeWeight, dustMap, dustWeight);
    }

    // =================================================================================================== search grid

    /// <summary>A window of the terrain height grid, possibly coarsened, with per-cell walkability and cost.</summary>
    sealed class SearchGrid
    {
        public readonly int w, h, step, ix0, iy0;
        public readonly float cellX, cellZ;          // metres between cells
        public readonly bool CoversWholeMap;
        readonly Vector3 origin;                     // terrain south-west corner
        readonly Vector2 nativeCell;
        readonly float tanMax, maxSlope, speed;
        public readonly float[] height;              // world Y
        public readonly float[] dust;                // 0..1
        readonly float[] mul;                        // cost multiplier, +inf = can't stand here

        // search state (reused)
        float[] g; int[] parent; byte[] state;      // state: 0 new, 1 open, 2 closed

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        public SearchGrid(Terrain terrain, Vector2 min, Vector2 max, int maxCells, float maxSlopeDeg, float walkSpeed,
                          float slopeWeight, MarsDustMap dustMap, float dustWeight)
        {
            TerrainData td = terrain.terrainData;
            int res = td.heightmapResolution;
            origin = terrain.transform.position;
            nativeCell = new Vector2(td.size.x / (res - 1), td.size.z / (res - 1));
            maxSlope = Mathf.Clamp(maxSlopeDeg, 1f, 89f);
            tanMax = Mathf.Tan(maxSlope * Mathf.Deg2Rad);
            speed = walkSpeed;

            int x0 = Mathf.Clamp(Mathf.FloorToInt((min.x - origin.x) / nativeCell.x), 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((max.x - origin.x) / nativeCell.x), 0, res - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((min.y - origin.z) / nativeCell.y), 0, res - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((max.y - origin.z) / nativeCell.y), 0, res - 1);
            CoversWholeMap = x0 == 0 && y0 == 0 && x1 == res - 1 && y1 == res - 1;
            int nw = x1 - x0 + 1, nh = y1 - y0 + 1;
            step = 1;
            while ((long)((nw - 1) / step + 1) * ((nh - 1) / step + 1) > maxCells) step++;
            w = (nw - 1) / step + 1; h = (nh - 1) / step + 1;
            ix0 = x0; iy0 = y0;
            cellX = nativeCell.x * step; cellZ = nativeCell.y * step;

            float[,] raw = td.GetHeights(x0, y0, nw, nh);      // [row south->north, col west->east], 0..1
            float hs = td.size.y;
            int count = w * h;
            height = new float[count]; dust = new float[count]; mul = new float[count];

            // longitude/latitude of each column/row for the dust lookup (the map frame is linear in both)
            var lon = new double[w]; var lat = new double[h];
            MarsGeoReference geo = MarsGeoReference.Current;
            bool useDust = dustMap != null && geo != null && dustWeight > 0f;
            if (useDust)
            {
                for (int c = 0; c < w; c++) { geo.WorldToLonLat(CellWorldXZ(c, 0), out lon[c], out _); }
                for (int r = 0; r < h; r++) { geo.WorldToLonLat(CellWorldXZ(0, r), out _, out lat[r]); }
            }

            for (int r = 0; r < h; r++)
            {
                int ly = r * step;
                int yl = Mathf.Max(0, ly - 1), yh = Mathf.Min(nh - 1, ly + 1);
                for (int c = 0; c < w; c++)
                {
                    int lx = c * step;
                    int i = r * w + c;
                    height[i] = origin.y + raw[ly, lx] * hs;

                    // slope at this sample from the full-resolution neighbours
                    int xl = Mathf.Max(0, lx - 1), xh = Mathf.Min(nw - 1, lx + 1);
                    float dzdx = (raw[ly, xh] - raw[ly, xl]) * hs / Mathf.Max(1, xh - xl) / nativeCell.x;
                    float dzdy = (raw[yh, lx] - raw[yl, lx]) * hs / Mathf.Max(1, yh - yl) / nativeCell.y;
                    float slope = Mathf.Atan(Mathf.Sqrt(dzdx * dzdx + dzdy * dzdy)) * Mathf.Rad2Deg;

                    float d = useDust ? dustMap.DustinessAt(lon[c], lat[r]) : 0f;
                    dust[i] = d;
                    if (slope > maxSlope) { mul[i] = float.PositiveInfinity; continue; }
                    float s = slope / maxSlope;
                    mul[i] = (1f + slopeWeight * s * s) * (1f + dustWeight * d);
                }
            }
        }

        Vector3 CellWorldXZ(int c, int r) =>
            new Vector3(origin.x + (ix0 + c * step) * nativeCell.x, 0f, origin.z + (iy0 + r * step) * nativeCell.y);

        public Vector3 CellWorld(int i)
        {
            Vector3 p = CellWorldXZ(i % w, i / w);
            p.y = height[i];
            return p;
        }

        public bool Walkable(int i) => !float.IsInfinity(mul[i]);

        /// <summary>Nearest walkable cell within radius (m), or -1.</summary>
        public int SnapToWalkable(Vector3 world, float radius)
        {
            float fx = ((world.x - origin.x) / nativeCell.x - ix0) / step;
            float fy = ((world.z - origin.z) / nativeCell.y - iy0) / step;
            int c0 = Mathf.RoundToInt(fx), r0 = Mathf.RoundToInt(fy);
            if (c0 < 0 || r0 < 0 || c0 >= w || r0 >= h) return -1;
            if (Walkable(r0 * w + c0)) return r0 * w + c0;
            int maxRing = Mathf.CeilToInt(radius / Mathf.Min(cellX, cellZ));
            int best = -1; float bestD = float.MaxValue;
            for (int ring = 1; ring <= maxRing; ring++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Mathf.Abs(dx) != ring && Mathf.Abs(dy) != ring) continue;
                        int c = c0 + dx, r = r0 + dy;
                        if (c < 0 || r < 0 || c >= w || r >= h || !Walkable(r * w + c)) continue;
                        float d = (dx * cellX) * (dx * cellX) + (dy * cellZ) * (dy * cellZ);
                        if (d < bestD) { bestD = d; best = r * w + c; }
                    }
                if (best >= 0 && Mathf.Sqrt(bestD) <= ring * Mathf.Min(cellX, cellZ)) break;   // nothing closer in later rings
            }
            return best >= 0 && Mathf.Sqrt(bestD) <= radius ? best : -1;
        }

        /// <summary>Walking cost (s, weighted) of one straight step between two cells/points, +inf if not walkable.</summary>
        float StepCost(float run, float dh, float mulA, float mulB)
        {
            if (float.IsInfinity(mulA) || float.IsInfinity(mulB) || run <= 0f) return float.PositiveInfinity;
            float t = dh / run;
            if (Mathf.Abs(t) > tanMax) return float.PositiveInfinity;
            float v = speed * ToblerPeak * Mathf.Exp(-3.5f * Mathf.Abs(t + 0.05f));
            return Mathf.Sqrt(run * run + dh * dh) / v * 0.5f * (mulA + mulB);
        }

        void EnsureSearchBuffers()
        {
            int n = w * h;
            if (g == null || g.Length != n) { g = new float[n]; parent = new int[n]; state = new byte[n]; }
            Array.Fill(g, float.PositiveInfinity);
            Array.Clear(state, 0, n);
        }

        /// <summary>
        /// A* (heuristic on) or Dijkstra (off) from <paramref name="start"/>. Stops when every goal is closed.
        /// Writes the cost to each goal into <paramref name="goalCosts"/> (+inf if unreachable). Yields every
        /// <paramref name="perFrame"/> expansions.
        /// </summary>
        public IEnumerator Search(int start, List<int> goals, bool heuristic, float hWeight, int perFrame, float[] goalCosts)
        {
            EnsureSearchBuffers();
            var goalSet = new Dictionary<int, int>();       // cell -> remaining count
            foreach (int gc in goals) if (gc >= 0) goalSet[gc] = goalSet.TryGetValue(gc, out int k) ? k + 1 : 1;
            int remaining = goalSet.Count;
            Vector3 goalPos = heuristic && goals.Count == 1 && goals[0] >= 0 ? CellWorld(goals[0]) : Vector3.zero;
            float hScale = hWeight / (speed * ToblerPeak);   // admissible at hWeight = 1: nothing is faster than this
            float[] diag = { cellX, cellX, cellZ, cellZ, 0, 0, 0, 0 };
            float dlen = Mathf.Sqrt(cellX * cellX + cellZ * cellZ);
            for (int k = 4; k < 8; k++) diag[k] = dlen;

            var open = new MinHeap(1024);
            g[start] = 0f; parent[start] = -1; state[start] = 1;
            open.Push(start, H(start));
            int expanded = 0;

            while (open.Count > 0 && remaining > 0)
            {
                int cur = open.Pop();
                if (state[cur] == 2) continue;
                state[cur] = 2;
                if (goalSet.TryGetValue(cur, out _)) { goalSet.Remove(cur); remaining--; if (remaining == 0) break; }

                int cx = cur % w, cy = cur / w;
                float gc = g[cur], hc = height[cur], mc = mul[cur];
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int ni = ny * w + nx;
                    if (state[ni] == 2) continue;
                    float c = StepCost(diag[d], height[ni] - hc, mc, mul[ni]);
                    if (float.IsInfinity(c)) continue;
                    float ng = gc + c;
                    if (ng >= g[ni]) continue;
                    g[ni] = ng; parent[ni] = cur; state[ni] = 1;
                    open.Push(ni, ng + H(ni));
                }
                if (++expanded % perFrame == 0) yield return null;
            }

            for (int i = 0; i < goals.Count && i < goalCosts.Length; i++)
                goalCosts[i] = goals[i] >= 0 && state[goals[i]] == 2 ? g[goals[i]] : float.PositiveInfinity;

            float H(int i)
            {
                if (!heuristic) return 0f;
                Vector3 p = CellWorldXZ(i % w, i / w);
                float dx = p.x - goalPos.x, dz = p.z - goalPos.z;
                return Mathf.Sqrt(dx * dx + dz * dz) * hScale;
            }
        }

        /// <summary>A* from cell a to cell b; fills <paramref name="nodes"/> with the cells (empty = no route).</summary>
        public IEnumerator FindPath(int a, int b, float hWeight, int perFrame, List<int> nodes)
        {
            nodes.Clear();
            if (a < 0 || b < 0) yield break;
            if (a == b) { nodes.Add(a); nodes.Add(b); yield break; }
            var costs = new float[1];
            IEnumerator s = Search(a, new List<int> { b }, true, hWeight, perFrame, costs);
            while (s.MoveNext()) yield return null;
            if (float.IsInfinity(costs[0])) yield break;
            for (int i = b; i >= 0; i = parent[i]) nodes.Add(i);
            nodes.Reverse();
            pathCumCost = new float[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) pathCumCost[i] = g[nodes[i]];
        }

        float[] pathCumCost;     // cost from the start of the last FindPath to each of its nodes

        public List<Vector3> ToWorld(List<int> nodes)
        {
            var list = new List<Vector3>(nodes.Count);
            foreach (int n in nodes) list.Add(CellWorld(n));
            return list;
        }

        /// <summary>
        /// String-pulling: from each kept node, jump to the farthest later node (within a look-ahead) whose straight
        /// line is walkable and costs no more than following the grid path there.
        /// </summary>
        public List<Vector3> Smooth(List<int> nodes, List<Vector3> line)
        {
            if (nodes.Count < 3 || pathCumCost == null || pathCumCost.Length != nodes.Count) return line;
            const int lookAhead = 48;
            var result = new List<Vector3> { line[0] };
            int i = 0, last = nodes.Count - 1;
            while (i < last)
            {
                int best = i + 1;
                int limit = Mathf.Min(last, i + lookAhead);
                for (int k = i + 2; k <= limit; k++)
                {
                    float direct = LineCost(line[i], line[k]);
                    if (direct <= (pathCumCost[k] - pathCumCost[i]) * 1.01f) best = k;
                }
                result.Add(line[best]);
                i = best;
            }
            return result;
        }

        float LineCost(Vector3 a, Vector3 b)
        {
            float dist = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            float spacing = Mathf.Min(cellX, cellZ) * 0.5f;
            int n = Mathf.Max(1, Mathf.CeilToInt(dist / spacing));
            float run = dist / n, total = 0f;
            Sample(a, out float hPrev, out float mPrev);
            for (int k = 1; k <= n; k++)
            {
                Sample(Vector3.Lerp(a, b, (float)k / n), out float hk, out float mk);
                total += StepCost(run, hk - hPrev, mPrev, mk);
                if (float.IsInfinity(total)) return total;
                hPrev = hk; mPrev = mk;
            }
            return total;
        }

        /// <summary>Bilinear height and nearest-cell cost multiplier at a world XZ.</summary>
        void Sample(Vector3 p, out float hgt, out float m)
        {
            float fx = Mathf.Clamp(((p.x - origin.x) / nativeCell.x - ix0) / step, 0f, w - 1.001f);
            float fy = Mathf.Clamp(((p.z - origin.z) / nativeCell.y - iy0) / step, 0f, h - 1.001f);
            int x = (int)fx, y = (int)fy;
            float tx = fx - x, ty = fy - y;
            int i = y * w + x;
            hgt = Mathf.Lerp(Mathf.Lerp(height[i], height[i + 1], tx), Mathf.Lerp(height[i + w], height[i + w + 1], tx), ty);
            m = mul[Mathf.RoundToInt(fy) * w + Mathf.RoundToInt(fx)];
        }
    }

    /// <summary>Binary min-heap of (priority, cell), duplicates allowed (stale entries are skipped by the search).</summary>
    sealed class MinHeap
    {
        float[] keys; int[] vals;
        public int Count { get; private set; }

        public MinHeap(int capacity) { keys = new float[capacity]; vals = new int[capacity]; }

        public void Push(int v, float key)
        {
            if (Count == keys.Length) { Array.Resize(ref keys, Count * 2); Array.Resize(ref vals, Count * 2); }
            int i = Count++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (keys[p] <= key) break;
                keys[i] = keys[p]; vals[i] = vals[p]; i = p;
            }
            keys[i] = key; vals[i] = v;
        }

        public int Pop()
        {
            int top = vals[0];
            float key = keys[--Count]; int v = vals[Count];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1;
                if (l >= Count) break;
                int c = l + 1 < Count && keys[l + 1] < keys[l] ? l + 1 : l;
                if (keys[c] >= key) break;
                keys[i] = keys[c]; vals[i] = vals[c]; i = c;
            }
            if (Count > 0) { keys[i] = key; vals[i] = v; }
            return top;
        }
    }

    // =================================================================================================== debug

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

/// <summary>
/// Best order to visit stops given the walking cost between every pair (asymmetric: uphill costs more).
/// Index 0 is the start. Exact (Held-Karp dynamic programming) for small sets, nearest neighbour + 2-opt otherwise.
/// </summary>
public static class StopOrder
{
    public static List<int> Best(float[,] cost, List<int> stops, bool returnToStart, int exactUpTo)
    {
        if (stops.Count <= 1) return new List<int>(stops);
        return stops.Count <= exactUpTo ? HeldKarp(cost, stops, returnToStart) : Heuristic(cost, stops, returnToStart);
    }

    static List<int> HeldKarp(float[,] cost, List<int> stops, bool ret)
    {
        int n = stops.Count, full = (1 << n) - 1;
        var dp = new float[1 << n, n];
        var from = new sbyte[1 << n, n];
        for (int m = 0; m <= full; m++) for (int j = 0; j < n; j++) { dp[m, j] = float.PositiveInfinity; from[m, j] = -1; }
        for (int j = 0; j < n; j++) dp[1 << j, j] = cost[0, stops[j]];
        for (int m = 1; m <= full; m++)
            for (int j = 0; j < n; j++)
            {
                if ((m & (1 << j)) == 0 || float.IsInfinity(dp[m, j])) continue;
                for (int k = 0; k < n; k++)
                {
                    if ((m & (1 << k)) != 0) continue;
                    float c = dp[m, j] + cost[stops[j], stops[k]];
                    int nm = m | (1 << k);
                    if (c < dp[nm, k]) { dp[nm, k] = c; from[nm, k] = (sbyte)j; }
                }
            }
        int best = -1; float bestC = float.PositiveInfinity;
        for (int j = 0; j < n; j++)
        {
            float c = dp[full, j] + (ret ? cost[stops[j], 0] : 0f);
            if (c < bestC) { bestC = c; best = j; }
        }
        if (best < 0) return Heuristic(cost, stops, ret);    // some pair unreachable: fall back
        var order = new List<int>();
        for (int m = full, j = best; j >= 0;)
        {
            order.Add(stops[j]);
            int pj = from[m, j];
            m &= ~(1 << j); j = pj;
        }
        order.Reverse();
        return order;
    }

    static List<int> Heuristic(float[,] cost, List<int> stops, bool ret)
    {
        var left = new List<int>(stops);
        var order = new List<int>();
        int at = 0;
        while (left.Count > 0)
        {
            int bi = 0;
            for (int i = 1; i < left.Count; i++) if (cost[at, left[i]] < cost[at, left[bi]]) bi = i;
            at = left[bi]; order.Add(at); left.RemoveAt(bi);
        }
        // 2-opt (segment reversal), re-evaluating the full cost since costs are asymmetric
        bool improved = true;
        float bestC = TourCost(cost, order, ret);
        for (int pass = 0; improved && pass < 50; pass++)
        {
            improved = false;
            for (int i = 0; i < order.Count - 1; i++)
                for (int k = i + 1; k < order.Count; k++)
                {
                    order.Reverse(i, k - i + 1);
                    float c = TourCost(cost, order, ret);
                    if (c + 1e-3f < bestC) { bestC = c; improved = true; }
                    else order.Reverse(i, k - i + 1);
                }
        }
        return order;
    }

    static float TourCost(float[,] cost, List<int> order, bool ret)
    {
        float c = cost[0, order[0]];
        for (int i = 1; i < order.Count; i++) c += cost[order[i - 1], order[i]];
        if (ret) c += cost[order[order.Count - 1], 0];
        return c;
    }
}
