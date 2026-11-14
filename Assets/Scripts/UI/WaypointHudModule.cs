using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Next waypoint: name, distance and a direction arrow (relative to where you are facing).</summary>
public class WaypointHudModule : HudModule
{
    Label title, wpName, distance, detail;
    HudArrow arrow;
    VisualElement body;

    void Reset() => SetPlacement(HudRegion.TopRight, 0, HudModes.Hud);

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

    void Show(bool hasTarget, string message)
    {
        arrow.style.display = hasTarget ? DisplayStyle.Flex : DisplayStyle.None;
        distance.style.display = hasTarget ? DisplayStyle.Flex : DisplayStyle.None;
        detail.style.display = hasTarget ? DisplayStyle.Flex : DisplayStyle.None;
        if (!hasTarget) wpName.text = message;
    }
}
