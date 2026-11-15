using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Suit status: oxygen, EVA time and time left. Oxygen use follows the astronaut's work rate: a spacesuit's life
/// support is sized for about 8 hours of EVA at an average metabolic rate, and oxygen use rises and falls with effort
/// (resting, walking, running). TIME LEFT is the oxygen remaining at the current work rate.
/// </summary>
public class SuitHudModule : HudModule
{
    [Header("Suit")]
    [SerializeField, Range(0f, 100f)] float startOxygenPercent = 100f;
    [Tooltip("EVA length the oxygen supply is sized for, at the average work rate below (hours). Suits are designed for about 8 h.")]
    [UnityEngine.Serialization.FormerlySerializedAs("evaDurationHours")]
    [SerializeField, Min(0.1f)] float designEvaHours = 8f;

    [Header("Metabolic rate (watts)")]
    [Tooltip("Average EVA work rate the supply is sized for.")]
    [SerializeField, Min(1f)] float averageWatts = 250f;
    [SerializeField, Min(1f)] float restingWatts = 120f;
    [SerializeField, Min(1f)] float walkingWatts = 300f;
    [SerializeField, Min(1f)] float runningWatts = 550f;
    [Tooltip("Speeds up the clock for testing (1 = real time).")]
    [SerializeField, Min(0f)] float timeScale = 1f;

    Label oxygen, elapsed, remaining, effort;
    VisualElement barFill;
    float evaSeconds, oxygenPercent = -1f, watts;
    MarsPlayerController player;

    public float OxygenPercent => oxygenPercent < 0f ? startOxygenPercent : oxygenPercent;

    void Reset() => SetPlacement(HudRegion.BottomRight, 0, HudModes.Hud);

    protected override void Build(VisualElement root)
    {
        AddLabel(root, "SUIT", "hud-title");
        oxygen = AddReadout(root, "O2");
        var bar = new VisualElement { pickingMode = PickingMode.Ignore }; bar.AddToClassList("hud-bar");
        barFill = new VisualElement { pickingMode = PickingMode.Ignore }; barFill.AddToClassList("hud-bar-fill"); barFill.AddToClassList("hud-bar-fill-oxygen");
        bar.Add(barFill); root.Add(bar);
        elapsed = AddReadout(root, "EVA");
        remaining = AddReadout(root, "LEFT");
        effort = AddLabel(root, "", "hud-small");
    }

    public override void Tick(HudContext ctx)
    {
        if (oxygenPercent < 0f) oxygenPercent = startOxygenPercent;
        if (player == null && ctx.player != null) player = ctx.player.GetComponent<MarsPlayerController>();

        float dt = ctx.deltaTime * timeScale;
        evaSeconds += dt;

        // Work rate from how fast the astronaut is moving (explore mode is a travel aid, so it counts as walking).
        float walk = player != null ? player.WalkSpeed : 4f;
        bool explore = player != null && player.ExploreMode;
        string activity;
        float target;
        if (ctx.speed < 0.3f) { target = restingWatts; activity = "Resting"; }
        else if (explore || ctx.speed <= walk * 1.25f) { target = walkingWatts; activity = "Walking"; }
        else { target = runningWatts; activity = "Running"; }
        watts = watts <= 0f ? target : Mathf.Lerp(watts, target, 1f - Mathf.Exp(-dt / 4f));   // the body ramps up and down over seconds

        float percentPerSecondAtAverage = 100f / (designEvaHours * 3600f);
        oxygenPercent = Mathf.Max(0f, oxygenPercent - percentPerSecondAtAverage * (watts / averageWatts) * dt);

        oxygen.text = $"{oxygenPercent:0.0} %";
        barFill.style.width = Length.Percent(oxygenPercent);
        bool low = oxygenPercent < 25f;
        oxygen.EnableInClassList("hud-warning", low);
        barFill.EnableInClassList("hud-bar-warning", low);
        elapsed.text = Format(evaSeconds);
        float secondsLeft = oxygenPercent / (percentPerSecondAtAverage * Mathf.Max(0.01f, watts / averageWatts));
        remaining.text = Format(secondsLeft);
        effort.text = $"{activity}  ·  about {watts:0} W";
    }

    static string Format(float s)
    {
        int t = Mathf.FloorToInt(s);
        return $"{t / 3600:0}:{t / 60 % 60:00}:{t % 60:00}";
    }
}
