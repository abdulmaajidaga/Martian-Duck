using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Screen areas a HUD module can sit in. Edges only, so the centre of the view stays clear.</summary>
public enum HudRegion { TopCenter, TopLeft, TopRight, BottomLeft, BottomRight, LeftMiddle, FullScreen, RightMiddle }

/// <summary>Interface modes. A module chooses which modes it is visible in.</summary>
[Flags] public enum HudModes { Hud = 1, Map = 2 }

/// <summary>Per-frame data shared with every module.</summary>
public class HudContext
{
    public Transform player;
    public Camera playerCamera;
    public MarsGeoReference geo;
    public RouteManager route;
    public HudModes mode;
    public bool helpOpen;             // the help overlay is showing
    public float headingDeg;          // 0 = north (+Z), clockwise
    public float speed;               // m/s, smoothed
    public float distanceWalked;      // m this session
    public float sessionTime;         // s since the walk started
    public float deltaTime;
}

/// <summary>
/// Base class for every HUD panel. To add a panel: create a new component deriving from this, add it to any
/// GameObject in the scene, and it registers itself with the HUD. No changes to MarsHud are needed.
/// </summary>
public abstract class HudModule : MonoBehaviour
{
    [Header("Placement")]
    [SerializeField] HudRegion region = HudRegion.TopLeft;
    [Tooltip("Lower numbers come first within the region.")]
    [SerializeField] int order;
    [SerializeField] HudModes visibleIn = HudModes.Hud;

    public HudRegion Region => region;
    public int Order => order;
    public HudModes VisibleIn => visibleIn;

    /// <summary>Default placement for a module type; call from Reset() so it applies when the component is added.</summary>
    protected void SetPlacement(HudRegion r, int o, HudModes modes)
    {
        region = r; order = o; visibleIn = modes;
    }
    public VisualElement Root { get; private set; }

    public static readonly List<HudModule> Registered = new List<HudModule>();
    public static event Action RegistryChanged;

    protected virtual void OnEnable()
    {
        if (!Registered.Contains(this)) Registered.Add(this);
        RegistryChanged?.Invoke();
    }

    protected virtual void OnDisable()
    {
        Registered.Remove(this);
        RegistryChanged?.Invoke();
    }

    /// <summary>Called by the HUD to create this module's UI inside a region.</summary>
    public VisualElement BuildInto(VisualElement parent)
    {
        Root = new VisualElement();
        Root.AddToClassList(region == HudRegion.FullScreen ? "hud-fullscreen" : "hud-panel");
        Root.pickingMode = PickingMode.Ignore;
        Build(Root);
        parent.Add(Root);
        return Root;
    }

    /// <summary>Create the module's visual elements.</summary>
    protected abstract void Build(VisualElement root);

    /// <summary>Update the module's values. Called every frame while it is visible.</summary>
    public abstract void Tick(HudContext ctx);

    /// <summary>Called when the interface switches between HUD and map mode.</summary>
    public virtual void OnModeChanged(HudModes mode, HudContext ctx) { }

    // ---- small helpers for building panels ----
    protected static Label AddLabel(VisualElement parent, string text, params string[] classes)
    {
        var l = new Label(text) { pickingMode = PickingMode.Ignore };
        foreach (string c in classes) l.AddToClassList(c);
        parent.Add(l);
        return l;
    }

    protected static VisualElement AddRow(VisualElement parent)
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.AddToClassList("hud-row");
        parent.Add(row);
        return row;
    }

    /// <summary>Label + value pair on one row; returns the value label.</summary>
    protected static Label AddReadout(VisualElement parent, string label)
    {
        VisualElement row = AddRow(parent);
        AddLabel(row, label, "hud-label");
        return AddLabel(row, "-", "hud-value");
    }

    protected static void AddPlaceholderTag(VisualElement parent, string text = "PLACEHOLDER DATA")
        => AddLabel(parent, text, "hud-placeholder");
}

/// <summary>A filled arrow/triangle drawn with the vector API (no font glyphs needed). Points up at 0 degrees.</summary>
public class HudArrow : VisualElement
{
    public Color fill = new Color(0.55f, 0.97f, 1f, 0.95f);
    public Color outline = new Color(0f, 0.08f, 0.12f, 0.8f);
    public bool notched = true;

    public HudArrow()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    void Draw(MeshGenerationContext mgc)
    {
        Rect r = contentRect;
        float w = r.width, h = r.height;
        var p = mgc.painter2D;
        p.BeginPath();
        p.MoveTo(new Vector2(w * 0.5f, 0f));
        p.LineTo(new Vector2(w, h));
        if (notched) p.LineTo(new Vector2(w * 0.5f, h * 0.72f));
        p.LineTo(new Vector2(0f, h));
        p.ClosePath();
        p.fillColor = fill;
        p.Fill();
        p.strokeColor = outline;
        p.lineWidth = 1.2f;
        p.Stroke();
    }

    /// <summary>Rotate the arrow (degrees clockwise).</summary>
    public void SetAngle(float degrees) => style.rotate = new StyleRotate(new Rotate(new Angle(degrees, AngleUnit.Degree)));
}
