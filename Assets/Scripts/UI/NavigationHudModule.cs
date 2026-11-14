using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Elevation (real Mars elevation from the DTM), speed, distance walked and position.</summary>
public class NavigationHudModule : HudModule
{
    Label elevation, speed, distance, position;

    void Reset() => SetPlacement(HudRegion.TopLeft, 0, HudModes.Hud);

    protected override void Build(VisualElement root)
    {
        AddLabel(root, "NAVIGATION", "hud-title");
        elevation = AddReadout(root, "ELEV");
        speed = AddReadout(root, "SPEED");
        distance = AddReadout(root, "DIST");
        position = AddLabel(root, "-", "hud-small");
    }

    public override void Tick(HudContext ctx)
    {
        if (ctx.player == null || ctx.geo == null) return;
        Vector3 p = ctx.player.position;
        elevation.text = $"{ctx.geo.ElevationAt(p):0.0} m";
        speed.text = $"{ctx.speed:0.0} m/s";
        distance.text = ctx.distanceWalked < 1000f ? $"{ctx.distanceWalked:0} m" : $"{ctx.distanceWalked / 1000f:0.00} km";
        ctx.geo.WorldToLonLat(p, out double lon, out double lat);
        position.text = $"{MarsGeoReference.FormatLatitude(lat)}   {MarsGeoReference.FormatLongitude(lon)}";
    }
}
