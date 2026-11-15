using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Radiation and dust. RATE is the surface dose-equivalent rate measured by Curiosity's RAD instrument
/// (0.64 ± 0.12 mSv/day, Hassler et al. 2014, Science 343), the long-term surface measurement;
/// DOSE is what has built up on this walk; its bar is full at one sol's surface dose. DUST is the surface dust cover where the astronaut is standing, from the
/// TES Dust Cover Index (<see cref="MarsDustMap"/>, the same data the route planner and the Surface dust layer use).
/// </summary>
public class RadiationHudModule : HudModule
{
    [Tooltip("Surface dose-equivalent rate in µSv/h. Default: RAD's measured 0.64 mSv/day at Gale crater (= 26.7 µSv/h).")]
    [SerializeField] float doseRate = 640f / 24f;
    [Tooltip("Dose that fills the radiation bar (mSv). Default: one sol on the surface at RAD's average (0.64 mSv).")]
    [SerializeField, Min(0.001f)] float doseBarFullMSv = 0.64f;
    [Tooltip("Dust map to read. Defaults to the one in the scene.")]
    [SerializeField] MarsDustMap dustMap;
    [Tooltip("Dustiness (0..1) at or above which the reading is shown as a warning.")]
    [SerializeField, Range(0f, 1f)] float dustWarning = 0.75f;

    Label rate, total, doseNote, dust, dustNote;
    VisualElement doseFill, dustFill;
    double accumulated;   // µSv

    public float DoseRate { get => doseRate; set => doseRate = value; }

    /// <summary>Dose rate in µSv/h. Override to feed live data.</summary>
    protected virtual float CurrentDoseRate => doseRate;

    void Reset() => SetPlacement(HudRegion.BottomLeft, 0, HudModes.Hud);

    protected override void Build(VisualElement root)
    {
        AddLabel(root, "RADIATION & DUST", "hud-title");
        rate = AddReadout(root, "RATE");
        total = AddReadout(root, "DOSE");
        doseFill = AddBar(root, "hud-bar-fill-radiation");
        doseNote = AddLabel(root, "", "hud-small");
        doseNote.AddToClassList("hud-bar-note");
        dust = AddReadout(root, "DUST");
        dustFill = AddBar(root, "hud-bar-fill-dust");
        dustNote = AddLabel(root, "", "hud-small");
        dustNote.AddToClassList("hud-bar-note");
    }

    static VisualElement AddBar(VisualElement root, string fillClass)
    {
        var bar = new VisualElement { pickingMode = PickingMode.Ignore }; bar.AddToClassList("hud-bar");
        var fill = new VisualElement { pickingMode = PickingMode.Ignore };
        fill.AddToClassList("hud-bar-fill"); fill.AddToClassList(fillClass);
        bar.Add(fill); root.Add(bar);
        return fill;
    }

    public override void Tick(HudContext ctx)
    {
        float r = CurrentDoseRate;
        accumulated += r * ctx.deltaTime / 3600.0;
        rate.text = $"{r:0.0} µSv/h";
        total.text = accumulated < 1000 ? $"{accumulated:0.00} µSv" : $"{accumulated / 1000:0.000} mSv";
        float doseShare = (float)(accumulated / (doseBarFullMSv * 1000.0));
        doseFill.style.width = Length.Percent(Mathf.Clamp01(doseShare) * 100f);
        doseFill.EnableInClassList("hud-bar-warning", doseShare >= 1f);
        doseNote.text = $"{doseShare * 100f:0.0}% of one sol's surface dose";

        MarsDustMap map = dustMap != null ? dustMap : MarsDustMap.Current;
        if (map == null || !map.Loaded || ctx.geo == null || ctx.player == null)
        {
            dust.text = "no data"; dustFill.style.width = Length.Percent(0); dustNote.text = "";
            return;
        }
        ctx.geo.WorldToLonLat(ctx.player.position, out double lon, out double lat);
        float d = map.DustinessAt(lon, lat);
        float dci = map.DciAt(lon, lat);
        dust.text = d < 0.25f ? "Low" : d < 0.5f ? "Moderate" : d < 0.75f ? "High" : "Very high";
        dustFill.style.width = Length.Percent(d * 100f);
        bool warn = d >= dustWarning;
        dust.EnableInClassList("hud-warning", warn);
        dustFill.EnableInClassList("hud-bar-warning", warn);
        dustNote.text = $"Dust cover index {dci:0.000}  ·  {d * 100f:0}% of this map's range";
    }
}
