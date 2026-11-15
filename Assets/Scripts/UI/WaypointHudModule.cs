using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Next waypoint: name, distance and a direction arrow (relative to where you are facing).
/// When the mission moves on to a new task, the panel is shown large in the middle of the screen for a few seconds,
/// then slides back to its usual place (top right).
/// </summary>
public class WaypointHudModule : HudModule
{
    [Header("New task spotlight")]
    [Tooltip("Seconds the panel stays large in the middle of the screen when the task changes.")]
    [SerializeField, Min(0.5f)] float spotlightSeconds = 3.5f;
    [Tooltip("Size of the panel while in the middle of the screen.")]
    [SerializeField, Range(1f, 3f)] float spotlightScale = 1.7f;
    [Tooltip("Where the panel's centre goes, as a fraction of the screen height (0.5 = middle).")]
    [SerializeField, Range(0.2f, 0.8f)] float spotlightHeight = 0.42f;

    bool spotlightPending, spotlightOn;
    float spotlightUntil;

    Label title, wpName, distance, detail;
    HudArrow arrow;
    VisualElement body;

    void Reset() => SetPlacement(HudRegion.TopRight, 0, HudModes.Hud);

    protected override void OnEnable()
    {
        base.OnEnable();
        MarsMission.StageChanged += OnTaskChanged;
        MarsMission.SiteDrilled += OnSiteDrilled;
    }

    protected override void OnDisable()
    {
        MarsMission.StageChanged -= OnTaskChanged;
        MarsMission.SiteDrilled -= OnSiteDrilled;
        base.OnDisable();
    }

    void OnTaskChanged(MarsMission m) => spotlightPending = true;
    void OnSiteDrilled(MarsMission m, MarsMission.DrillSite s) => spotlightPending = true;   // next drill site is a new task

    /// <summary>Show the panel large in the middle of the screen for a few seconds (also callable by other code).</summary>
    public void Spotlight() => spotlightPending = true;

    protected override void Build(VisualElement root)
    {
        root.AddToClassList("waypoint-panel");
        title = AddLabel(root, "NEXT WAYPOINT", "hud-title");
        body = AddRow(root);
        arrow = new HudArrow();
        arrow.AddToClassList("waypoint-arrow");
        body.Add(arrow);
        var text = new VisualElement { pickingMode = PickingMode.Ignore };
        text.AddToClassList("waypoint-text");
        body.Add(text);
        wpName = AddLabel(text, "-", "hud-value");
        distance = AddLabel(text, "-", "hud-label");
        detail = AddLabel(root, "", "hud-small");
    }

    public override void Tick(HudContext ctx)
    {
        UpdateSpotlight();
        RouteManager route = ctx.route;
        if (route == null || !route.HasRoute)
        {
            Show(false, "No route loaded"); return;
        }
        Waypoint next = route.Next;
        if (next == null)
        {
            Show(false, "Route complete"); return;
        }
        Show(true, null);
        wpName.text = next.displayName;
        Vector3 d = next.Position - ctx.player.position; d.y = 0f;
        float dist = d.magnitude;
        distance.text = dist < 1000f ? $"{dist:0} m" : $"{dist / 1000f:0.00} km";
        float bearing = MarsGeoReference.Bearing(ctx.player.position, next.Position);
        arrow.SetAngle(Mathf.DeltaAngle(ctx.headingDeg, bearing));   // 0 = straight ahead
        int index = 0; for (int i = 0; i < route.Waypoints.Count; i++) if (route.Waypoints[i] == next) index = i;
        detail.text = $"BRG {Mathf.RoundToInt(bearing) % 360:000}°   ·   {index + 1} of {route.Waypoints.Count}";
    }

    void UpdateSpotlight()
    {
        if (Root == null || Root.panel == null) return;
        if (spotlightPending)
        {
            Rect me = Root.worldBound, screen = Root.panel.visualTree.worldBound;
            if (float.IsNaN(me.width) || me.width <= 0f) return;            // not laid out yet; try next frame
            spotlightPending = false;
            Vector2 target = new Vector2(screen.center.x, screen.yMin + screen.height * spotlightHeight);
            Vector2 delta = target - me.center;
            Root.style.translate = new Translate(delta.x, delta.y);
            Root.style.scale = new Scale(Vector2.one * spotlightScale);
            Root.AddToClassList("waypoint-spotlight");
            title.text = "NEW TASK";
            spotlightOn = true;
            spotlightUntil = Time.unscaledTime + spotlightSeconds;
        }
        else if (spotlightOn && Time.unscaledTime >= spotlightUntil)
        {
            spotlightOn = false;
            Root.style.translate = new Translate(0f, 0f);                 // the USS transition slides it back
            Root.style.scale = new Scale(Vector2.one);
            Root.RemoveFromClassList("waypoint-spotlight");
            title.text = "NEXT WAYPOINT";
        }
    }

    void Show(bool hasTarget, string message)
    {
        arrow.style.display = hasTarget ? DisplayStyle.Flex : DisplayStyle.None;
        distance.style.display = hasTarget ? DisplayStyle.Flex : DisplayStyle.None;
        detail.style.display = hasTarget ? DisplayStyle.Flex : DisplayStyle.None;
        if (!hasTarget) wpName.text = message;
    }
}
