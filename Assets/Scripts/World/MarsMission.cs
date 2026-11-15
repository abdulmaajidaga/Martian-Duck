using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The astronaut's missions, played in order from the rocket at the landing site.
///
/// Mission 1 - sample depot
///   1. Walk to the Three Forks sample depot.
///   2. Collect every sample tube there: stand next to a tube and press C.
///   3. Return to the rocket and press C to deposit the samples.
///
/// Mission 2 - "Follow the water": drill four real Perseverance sampling sites, in story order
///   4. South Séítah (igneous, olivine-rich: formed without water) -> Comet Geyser (silica-cemented carbonate:
///      formed in water) -> Sapphire Canyon (the "Cheyava Falls" rock, a potential biosignature) -> Green Gardens
///      (serpentinite: olivine altered by water). Press C at each and stay until the core is done.
///   5. Return to the rocket and press C to deposit the cores.
/// Site positions and rock types come from MarsSources/Sampling Locations.geojson (Mars 2020 mission data).
///
/// Guidance uses the existing route system (RouteManager + one runtime Waypoint), so the HUD's waypoint panel and map
/// show where to go; the waypoint name carries the instruction and progress ("Press C to drill ...", "Drilling 60%").
/// The UI can subscribe to <see cref="StageChanged"/>, <see cref="TubeCollected"/> and <see cref="SiteDrilled"/>
/// and read the public properties for its own mission display.
/// </summary>
public class MarsMission : MonoBehaviour
{
    public enum Stage { GoToDepot, CollectTubes, ReturnTubes, DrillSites, ReturnCores, AllComplete }

    [Serializable]
    public class DrillSite
    {
        public string name;
        [Tooltip("Shown while heading there, e.g. \"igneous rock, olivine-rich\".")]
        public string kind;
        [Tooltip("One line of science shown when the core is done.")]
        public string fact;
        public double longitude, latitude;
        [HideInInspector] public bool drilled;
        [NonSerialized] public GameObject marker;
    }

    [Header("Places")]
    [Tooltip("The rocket the astronaut arrived in (missions start and end here).")]
    [SerializeField] Transform rocket;
    [Tooltip("Depot centre (longitude/latitude, Mars 2020 frame).")]
    [SerializeField] double depotLongitude = 77.408292;
    [SerializeField] double depotLatitude = 18.45287053;
    [Tooltip("Points of interest whose display name starts with this are the tubes to collect.")]
    [SerializeField] string tubePrefix = "Depot tube";

    [Header("Mission 2: drill sites (visited in list order)")]
    [SerializeField] List<DrillSite> drillSites = DefaultDrillSites();
    [Tooltip("Seconds of drilling per site; leaving the site cancels the drill.")]
    [SerializeField, Min(0.5f)] float drillSeconds = 5f;
    [SerializeField, Min(1f)] float drillRadius = 6f;

    [Header("Distances (m)")]
    [SerializeField, Min(1f)] float depotArriveRadius = 35f;
    [SerializeField, Min(0.5f)] float pickupRadius = 2.5f;
    [SerializeField, Min(2f)] float rocketArriveRadius = 15f;

    [Header("Controls")]
    [Tooltip("Key that collects a tube / drills / deposits (read here so the mission does not depend on the UI's input code).")]
#if ENABLE_INPUT_SYSTEM
    [SerializeField] Key actionKey = Key.C;
#else
    [SerializeField] KeyCode actionKey = KeyCode.C;
#endif

    [Header("Look")]
    [Tooltip("Tube length (m). Real Mars 2020 sample tubes are about 0.2 m long; slightly larger reads better in game.")]
    [SerializeField, Min(0.05f)] float tubeLength = 0.3f;
    [SerializeField, Min(1f)] float siteMarkerHeight = 8f;

    public Stage CurrentStage { get; private set; } = Stage.GoToDepot;
    public int Collected { get; private set; }
    public int TotalTubes => tubes.Count;
    public int Drilled { get; private set; }
    public int TotalSites => drillSites.Count;
    /// <summary>0..1 while drilling, otherwise -1.</summary>
    public float DrillProgress => drilling != null ? drillTimer / drillSeconds : -1f;

    public static event Action<MarsMission> StageChanged;
    public static event Action<MarsMission, PointOfInterest> TubeCollected;
    public static event Action<MarsMission, DrillSite> SiteDrilled;

    class Tube { public PointOfInterest poi; public GameObject visual; public bool taken; }
    readonly List<Tube> tubes = new List<Tube>();
    Transform player;
    Waypoint guide;
    RouteManager route;
    Material tubeMaterial, markerMaterial, markerDoneMaterial, holeMaterial;
    DrillSite drilling;
    float drillTimer;
    string flash; float flashUntil;          // short message shown on the guide (e.g. "Drilling stopped")

    // "Follow the water": real Perseverance sampling sites (positions/rock types from Sampling Locations.geojson).
    static List<DrillSite> DefaultDrillSites() => new List<DrillSite>
    {
        new DrillSite { name = "South Séítah", kind = "igneous rock, olivine-rich", longitude = 77.44305448, latitude = 18.43398032,
                        fact = "Olivine-rich igneous rock that cooled from magma: rock formed without water" },
        new DrillSite { name = "Comet Geyser", kind = "silica-cemented carbonate", longitude = 77.3272192, latitude = 18.49186664,
                        fact = "Silica and carbonate: minerals that form in water" },
        new DrillSite { name = "Sapphire Canyon", kind = "the Cheyava Falls rock", longitude = 77.30514915, latitude = 18.49747434,
                        fact = "Cheyava Falls: its 'leopard spots' are a potential sign of ancient life" },
        new DrillSite { name = "Green Gardens", kind = "serpentinite", longitude = 77.26465918, latitude = 18.46059266,
                        fact = "Serpentinite: olivine-rich rock changed by water" },
    };

    // ------------------------------------------------------------------ setup
    void Start()
    {
        var pc = FindAnyObjectByType<MarsPlayerController>();
        player = pc != null ? pc.transform : (Camera.main != null ? Camera.main.transform : null);
        route = RouteManager.Current;
        CreateMaterials();

        foreach (PointOfInterest p in FindObjectsByType<PointOfInterest>())
            if (p.displayName.StartsWith(tubePrefix, StringComparison.OrdinalIgnoreCase))
                tubes.Add(new Tube { poi = p, visual = MakeTube(p) });
        tubes.Sort((a, b) => string.CompareOrdinal(a.poi.displayName, b.poi.displayName));

        foreach (DrillSite s in drillSites) { s.drilled = false; s.marker = MakeSiteMarker(s); s.marker.SetActive(false); }

        var go = new GameObject("Mission waypoint") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        guide = go.AddComponent<Waypoint>();
        guide.placeByLonLat = false;
        guide.arriveRadius = 0.01f;           // the mission decides when a place is reached, not the route system
        if (route != null) route.SetRoute(new[] { guide });

        SetStage(Stage.GoToDepot);
        Debug.Log($"[Mission] Mission 1: walk to the Three Forks depot, collect {TotalTubes} sample tubes, return to the rocket.", this);
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        if (player == null || CurrentStage == Stage.AllComplete) return;
        Vector3 p = player.position;
        bool action = ActionPressed();

        switch (CurrentStage)
        {
            case Stage.GoToDepot:
                if (Flat(p, DepotPosition()) <= depotArriveRadius || NearestTube(p, pickupRadius) != null) SetStage(Stage.CollectTubes);
                break;

            case Stage.CollectTubes:
            {
                Tube inReach = NearestTube(p, pickupRadius);
                if (inReach != null && action) Collect(inReach);
                if (Collected >= TotalTubes) SetStage(Stage.ReturnTubes);
                else GuideToNearestTube(p);
                break;
            }

            case Stage.ReturnTubes:
                if (AtRocket(p))
                {
                    Guide(RocketPosition(), $"Press C to deposit the samples  ·  {Collected}/{TotalTubes} tubes");
                    if (action) { Debug.Log("[Mission] Mission 1 complete: samples deposited.", this); SetStage(Stage.DrillSites); }
                }
                else Guide(RocketPosition(), $"Return to the rocket  ·  {Collected}/{TotalTubes} tubes");
                break;

            case Stage.DrillSites:
                UpdateDrilling(p, action);
                break;

            case Stage.ReturnCores:
                if (AtRocket(p))
                {
                    Guide(RocketPosition(), $"Press C to deposit the cores  ·  {Drilled}/{TotalSites} cores");
                    if (action) SetStage(Stage.AllComplete);
                }
                else Guide(RocketPosition(), Flashing() ? flash : $"Return to the rocket  ·  {Drilled}/{TotalSites} cores");
                break;
        }
    }

    void UpdateDrilling(Vector3 p, bool action)
    {
        if (drilling != null)
        {
            if (Flat(p, SitePosition(drilling)) > drillRadius)
            {
                drilling = null;
                Flash("Drilling stopped  ·  stay at the site until the core is done");
            }
            else
            {
                drillTimer += Time.deltaTime;
                if (drillTimer >= drillSeconds) FinishDrill(drilling);
                else { Guide(SitePosition(drilling), $"Drilling {drilling.name}  ·  {Mathf.FloorToInt(DrillProgress * 100f)}%"); return; }
            }
        }
        if (Drilled >= TotalSites) { SetStage(Stage.ReturnCores); return; }

        DrillSite target = NextSite();
        if (target == null) return;
        bool atSite = Flat(p, SitePosition(target)) <= drillRadius;
        if (atSite && action) { drilling = target; drillTimer = 0f; return; }
        string where = atSite ? $"Press C to drill {target.name}" : $"Drill site: {target.name} ({target.kind})";
        Guide(SitePosition(target), Flashing() ? flash : $"{where}  ·  {Drilled}/{TotalSites} cores");
    }

    void FinishDrill(DrillSite s)
    {
        s.drilled = true;
        Drilled++;
        drilling = null;
        MakeBorehole(SitePosition(s));
        if (s.marker != null) foreach (var r in s.marker.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = markerDoneMaterial;
        Debug.Log($"[Mission] Drilled {s.name} ({Drilled}/{TotalSites}).", this);
        Flash(string.IsNullOrEmpty(s.fact) ? $"Core collected: {s.name}  ·  {Drilled}/{TotalSites}"
                                           : $"Core {Drilled}/{TotalSites}  ·  {s.fact}", 6f);
        SiteDrilled?.Invoke(this, s);
    }

    void Collect(Tube t)
    {
        t.taken = true;
        Collected++;
        if (t.visual != null) t.visual.SetActive(false);
        Transform marker = t.poi.transform.Find("Marker");
        if (marker != null) marker.gameObject.SetActive(false);
        Debug.Log($"[Mission] Picked up {t.poi.displayName} ({Collected}/{TotalTubes}).", this);
        TubeCollected?.Invoke(this, t.poi);
    }

    void SetStage(Stage s)
    {
        CurrentStage = s;
        switch (s)
        {
            case Stage.GoToDepot:
                Guide(DepotPosition(), $"Three Forks depot  ·  collect {TotalTubes} sample tubes");
                break;
            case Stage.CollectTubes:
                if (player != null) GuideToNearestTube(player.position);
                break;
            case Stage.ReturnTubes:
                Guide(RocketPosition(), $"Return to the rocket  ·  {Collected}/{TotalTubes} tubes");
                Debug.Log("[Mission] All tubes collected - return to the rocket.", this);
                break;
            case Stage.DrillSites:
                foreach (DrillSite d in drillSites) if (d.marker != null) d.marker.SetActive(true);
                Flash($"Mission 2 · Follow the water: drill {TotalSites} sites, then return to the rocket", 5f);
                Debug.Log($"[Mission] Mission 2: drill {TotalSites} sites and return the cores.", this);
                break;
            case Stage.ReturnCores:
                // Keep the last core's science line on screen for its full time before switching the instruction.
                Guide(RocketPosition(), Flashing() ? flash : $"Return to the rocket  ·  {Drilled}/{TotalSites} cores");
                Debug.Log("[Mission] All sites drilled - return to the rocket.", this);
                break;
            case Stage.AllComplete:
                Guide(RocketPosition(), $"All missions complete  ·  {Collected} tubes and {Drilled} cores returned");
                Debug.Log("[Mission] All missions complete!", this);
                break;
        }
        StageChanged?.Invoke(this);
    }

    // ------------------------------------------------------------------ helpers
    void GuideToNearestTube(Vector3 from)
    {
        Tube best = NearestTube(from, float.MaxValue);
        if (best == null) return;
        bool inReach = Flat(from, best.poi.Position) <= pickupRadius;
        Guide(best.poi.Position, inReach
            ? $"Press C to collect {best.poi.displayName}  ·  {Collected}/{TotalTubes}"
            : $"{best.poi.displayName}  ·  {Collected}/{TotalTubes} collected");
    }

    Tube NearestTube(Vector3 from, float maxDistance)
    {
        Tube best = null; float bestD = maxDistance;
        foreach (Tube t in tubes)
        {
            if (t.taken) continue;
            float d = Flat(from, t.poi.Position);
            if (d <= bestD) { bestD = d; best = t; }
        }
        return best;
    }

    DrillSite NextSite()
    {
        foreach (DrillSite s in drillSites) if (!s.drilled) return s;
        return null;
    }

    void Guide(Vector3 position, string label)
    {
        if (guide == null) return;
        guide.transform.position = position;
        guide.displayName = label;
        // The route system auto-advances past a reached waypoint; keep the mission's single moving waypoint current.
        // (Only for the mission's own route, so a route planned on the map isn't overwritten.)
        if (route != null && route.Next != guide && ContainsGuide(route)) route.SetRoute(new[] { guide });
    }

    bool ContainsGuide(RouteManager r)
    {
        foreach (Waypoint w in r.Waypoints) if (w == guide) return true;
        return false;
    }

    void Flash(string text, float seconds = 3f) { flash = text; flashUntil = Time.time + seconds; if (guide != null) guide.displayName = text; }
    bool Flashing() => Time.time < flashUntil;

    bool AtRocket(Vector3 p) => rocket != null && Flat(p, rocket.position) <= rocketArriveRadius;
    Vector3 RocketPosition() => rocket != null ? rocket.position : DepotPosition();

    Vector3 DepotPosition() => LonLat(depotLongitude, depotLatitude);
    Vector3 SitePosition(DrillSite s) => LonLat(s.longitude, s.latitude);

    Vector3 LonLat(double lon, double lat)
    {
        MarsGeoReference geo = MarsGeoReference.Current;
        return geo != null && geo.ResolveTerrain() ? geo.LonLatToWorld(lon, lat) : transform.position;
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    bool ActionPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        return kb != null && kb[actionKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(actionKey);
#endif
    }

    // ------------------------------------------------------------------ visuals
    void CreateMaterials()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Material Make(string n, Color c, Color emission, float metal, float smooth)
        {
            var m = new Material(lit) { name = n };
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission);
            return m;
        }
        tubeMaterial = Make("Sample tube (runtime)", new Color(0.92f, 0.93f, 0.95f), new Color(0.25f, 0.25f, 0.28f), 0.8f, 0.7f);
        markerMaterial = Make("Drill site marker (runtime)", new Color(0.25f, 0.65f, 1f), new Color(0.15f, 0.45f, 0.9f), 0f, 0.3f);
        markerDoneMaterial = Make("Drill site done (runtime)", new Color(0.35f, 0.85f, 0.4f), new Color(0.1f, 0.5f, 0.15f), 0f, 0.3f);
        holeMaterial = Make("Borehole (runtime)", new Color(0.08f, 0.06f, 0.05f), Color.black, 0f, 0.1f);
    }

    GameObject MakeTube(PointOfInterest p)
    {
        var tube = Primitive(PrimitiveType.Capsule, "Sample tube", tubeMaterial);
        float r = tubeLength * 0.09f;
        tube.transform.localScale = new Vector3(r * 2f, tubeLength * 0.5f, r * 2f);
        float yaw = Mathf.Abs(p.displayName.GetHashCode() % 360);           // lying on the ground, varied direction
        tube.transform.SetPositionAndRotation(p.Position + Vector3.up * r, Quaternion.Euler(0f, yaw, 90f));
        return tube;
    }

    GameObject MakeSiteMarker(DrillSite s)
    {
        Vector3 pos = SitePosition(s);
        var root = new GameObject($"Drill site: {s.name}") { hideFlags = HideFlags.DontSave };
        root.transform.SetParent(transform, false);
        root.transform.position = pos;
        var pole = Primitive(PrimitiveType.Cylinder, "Pole", markerMaterial, root.transform);
        pole.transform.localPosition = new Vector3(0f, siteMarkerHeight * 0.5f, 0f);
        pole.transform.localScale = new Vector3(0.12f, siteMarkerHeight * 0.5f, 0.12f);
        var top = Primitive(PrimitiveType.Cube, "Top", markerMaterial, root.transform);
        top.transform.localPosition = new Vector3(0f, siteMarkerHeight, 0f);
        top.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
        top.transform.localScale = Vector3.one * 0.7f;
        var ring = Primitive(PrimitiveType.Cylinder, "Drill area", markerMaterial, root.transform);   // flat disc = drill radius
        ring.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        ring.transform.localScale = new Vector3(drillRadius * 2f, 0.01f, drillRadius * 2f);
        ring.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return root;
    }

    void MakeBorehole(Vector3 pos)
    {
        var hole = Primitive(PrimitiveType.Cylinder, "Borehole", holeMaterial);
        hole.transform.position = pos + Vector3.up * 0.04f;
        hole.transform.localScale = new Vector3(0.12f, 0.02f, 0.12f);       // ~5 cm core drill hole, slightly enlarged
    }

    GameObject Primitive(PrimitiveType type, string name, Material mat, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.hideFlags = HideFlags.DontSave;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent != null ? parent : transform, false);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    void OnDestroy()
    {
        foreach (var m in new[] { tubeMaterial, markerMaterial, markerDoneMaterial, holeMaterial })
            if (m != null) Destroy(m);
    }
}
