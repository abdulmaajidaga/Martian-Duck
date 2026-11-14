using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Radiation: dose rate and dose accumulated on this walk. PLACEHOLDER: the rate comes from a setting until real
/// data is connected (override CurrentDoseRate in a subclass, or set PlaceholderDoseRate from another script).
/// </summary>
public class RadiationHudModule : HudModule
{
    [Tooltip("PLACEHOLDER dose rate in µSv/h. ~28 µSv/h (≈0.67 mSv/day) is the surface average measured by Curiosity's RAD at Gale crater.")]
    [SerializeField] float placeholderDoseRate = 28f;

    Label rate, total;
    double accumulated;   // µSv

    public float PlaceholderDoseRate { get => placeholderDoseRate; set => placeholderDoseRate = value; }

    /// <summary>Dose rate in µSv/h. Replace with live data later.</summary>
    protected virtual float CurrentDoseRate => placeholderDoseRate;

    void Reset() => SetPlacement(HudRegion.BottomLeft, 0, HudModes.Hud);

    protected override void Build(VisualElement root)
    {
        AddLabel(root, "RADIATION", "hud-title");
        AddPlaceholderTag(root);
        rate = AddReadout(root, "RATE");
        total = AddReadout(root, "DOSE");
    }

    public override void Tick(HudContext ctx)
    {
        float r = CurrentDoseRate;
        accumulated += r * ctx.deltaTime / 3600.0;
        rate.text = $"{r:0.0} µSv/h";
        total.text = accumulated < 1000 ? $"{accumulated:0.00} µSv" : $"{accumulated / 1000:0.000} mSv";
    }
}
