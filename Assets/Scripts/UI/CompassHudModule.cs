using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Compass strip at the top: N/E/S/W, intermediate marks and the heading in degrees (0 = north = +Z).</summary>
public class CompassHudModule : HudModule
{
    [SerializeField] float stripWidth = 620f;
    [SerializeField] float pixelsPerDegree = 4.2f;

    readonly List<(VisualElement el, float deg, float halfWidth)> marks = new List<(VisualElement, float, float)>();
    Label heading;

    void Reset() => SetPlacement(HudRegion.TopCenter, 0, HudModes.Hud);

    protected override void Build(VisualElement root)
    {
        root.AddToClassList("compass-panel");
        marks.Clear();
        var strip = new VisualElement { pickingMode = PickingMode.Ignore };
        strip.AddToClassList("compass-strip");
        strip.style.width = stripWidth;
        root.Add(strip);

        string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        for (int d = 0; d < 360; d += 5)
        {
            bool major = d % 15 == 0;
            var tick = new VisualElement { pickingMode = PickingMode.Ignore };
            tick.AddToClassList("compass-tick");
            if (major) tick.AddToClassList("compass-tick-major");
            if (d % 90 == 0) tick.AddToClassList("compass-tick-cardinal");
            strip.Add(tick);
            marks.Add((tick, d, 0.5f));
            if (d % 15 != 0) continue;
            string text = d % 45 == 0 ? names[d / 45] : d.ToString();
            var lbl = new Label(text) { pickingMode = PickingMode.Ignore };
            lbl.AddToClassList("compass-label");
            if (d % 90 == 0) lbl.AddToClassList("compass-cardinal");
            else if (d % 45 == 0) lbl.AddToClassList("compass-intercardinal");
            strip.Add(lbl);
            marks.Add((lbl, d, 20f));
        }

        var caret = new HudArrow { notched = false };
        caret.AddToClassList("compass-caret");
        caret.SetAngle(180f);
        strip.Add(caret);

        heading = AddLabel(root, "000°", "compass-heading");
    }

    public override void Tick(HudContext ctx)
    {
        float h = ctx.headingDeg;
        float half = stripWidth * 0.5f;
        foreach (var (el, deg, hw) in marks)
        {
            float x = half + Mathf.DeltaAngle(h, deg) * pixelsPerDegree;
            bool visible = x > hw + 4f && x < stripWidth - hw - 4f;
            el.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible) el.style.left = x - hw;
        }
        heading.text = $"{Mathf.RoundToInt(h) % 360:000}°";
    }
}
