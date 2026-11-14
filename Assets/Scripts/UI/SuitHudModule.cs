using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Suit status: oxygen and EVA time. PLACEHOLDER: simple linear drain from settings.</summary>
public class SuitHudModule : HudModule
{
    [Header("PLACEHOLDER suit model")]
    [SerializeField, Range(0f, 100f)] float startOxygenPercent = 100f;
    [Tooltip("Oxygen used per minute of EVA (%). 0.2 %/min lasts about 8 h.")]
    [SerializeField, Min(0f)] float oxygenDrainPercentPerMinute = 0.2f;
    [Tooltip("Planned EVA length in hours.")]
    [SerializeField, Min(0.1f)] float evaDurationHours = 8f;
    [Tooltip("Speeds up the placeholder drain for testing (1 = real time).")]
    [SerializeField, Min(0f)] float timeScale = 1f;

    Label oxygen, elapsed, remaining;
    VisualElement barFill;
    float evaSeconds;

    public float OxygenPercent => Mathf.Max(0f, startOxygenPercent - oxygenDrainPercentPerMinute * evaSeconds / 60f);

    void Reset() => SetPlacement(HudRegion.BottomRight, 0, HudModes.Hud);

    protected override void Build(VisualElement root)
    {
        AddLabel(root, "SUIT", "hud-title");
        AddPlaceholderTag(root);
        oxygen = AddReadout(root, "O2");
        var bar = new VisualElement { pickingMode = PickingMode.Ignore }; bar.AddToClassList("hud-bar");
        barFill = new VisualElement { pickingMode = PickingMode.Ignore }; barFill.AddToClassList("hud-bar-fill");
        bar.Add(barFill); root.Add(bar);
        elapsed = AddReadout(root, "EVA");
        remaining = AddReadout(root, "LEFT");
    }

    public override void Tick(HudContext ctx)
    {
        evaSeconds += ctx.deltaTime * timeScale;
        float o2 = OxygenPercent;
        oxygen.text = $"{o2:0.0} %";
        barFill.style.width = Length.Percent(o2);
        bool low = o2 < 25f;
        oxygen.EnableInClassList("hud-warning", low);
        barFill.EnableInClassList("hud-bar-warning", low);
        elapsed.text = Format(evaSeconds);
        remaining.text = Format(Mathf.Max(0f, evaDurationHours * 3600f - evaSeconds));
    }

    static string Format(float s)
    {
        int t = Mathf.FloorToInt(s);
        return $"{t / 3600:0}:{t / 60 % 60:00}:{t % 60:00}";
    }
}
