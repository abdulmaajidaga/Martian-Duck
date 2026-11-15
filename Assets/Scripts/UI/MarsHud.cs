using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The astronaut interface (UI Toolkit). Owns the screen regions, HUD/Map modes, the data-layer panel and the
/// help overlay, and feeds every registered HudModule a shared HudContext each frame.
/// Extend it by adding HudModule components (panels) and MarsDataLayer components (layers); this class does
/// not need to change.
///   M = map mode, L = layer panel, H = help, 1-9 = toggle layers while the layer panel is open.
/// </summary>
[RequireComponent(typeof(UIDocument))]
[DisallowMultipleComponent]
public class MarsHud : MonoBehaviour
{
    [SerializeField] StyleSheet styleSheet;
    [Tooltip("The walking player. If empty, the MarsPlayerController in the scene is used.")]
    [SerializeField] Transform player;
    [SerializeField] Camera playerCamera;
    [Tooltip("Show the controls overlay for a few seconds when the walk starts.")]
    [SerializeField] float showHelpAtStartSeconds = 6f;

    public HudModes Mode { get; private set; } = HudModes.Hud;

    /// <summary>Full-screen layer on top of all panels, for pop-ups such as the photo viewer.</summary>
    public VisualElement OverlayLayer { get; private set; }

    /// <summary>True while a pop-up has paused walking (the mouse is free for clicking).</summary>
    public bool MovementPaused => pauseOwners.Count > 0;
    readonly HashSet<object> pauseOwners = new HashSet<object>();
    public static MarsHud Current { get; private set; }

    MarsPlayerController playerController;
    VisualElement root, layerPanel, layerList, help, hint;
    Label layerFoot;
    VisualElement exploreBadge;
    readonly Dictionary<HudRegion, VisualElement> regions = new Dictionary<HudRegion, VisualElement>();
    readonly Dictionary<MarsDataLayer, Toggle> layerToggles = new Dictionary<MarsDataLayer, Toggle>();
    readonly HudContext ctx = new HudContext();
    bool modulesDirty = true, layersDirty = true, layersOpenInHud, helpOpen;
    float helpAutoHideAt = -1f;
    Vector3 lastPos;

    void OnEnable()
    {
        Current = this;
        HudModule.RegistryChanged += MarkModulesDirty;
        MarsDataLayer.RegistryChanged += MarkLayersDirty;
        MarsDataLayer.LayerToggled += OnLayerToggled;
    }

    void OnDisable()
    {
        HudModule.RegistryChanged -= MarkModulesDirty;
        MarsDataLayer.RegistryChanged -= MarkLayersDirty;
        MarsDataLayer.LayerToggled -= OnLayerToggled;
    }

    void MarkModulesDirty() => modulesDirty = true;
    void MarkLayersDirty() => layersDirty = true;

    void Start()
    {
        playerController = player != null ? player.GetComponent<MarsPlayerController>() : FindAnyObjectByType<MarsPlayerController>();
        if (player == null && playerController != null) player = playerController.transform;
        if (playerCamera == null && player != null) playerCamera = player.GetComponentInChildren<Camera>();
        lastPos = player != null ? player.position : Vector3.zero;

        BuildRoot();
        if (showHelpAtStartSeconds > 0f) { SetHelp(true); helpAutoHideAt = Time.time + showHelpAtStartSeconds; }
    }

    // ------------------------------------------------------------------ layout
    void BuildRoot()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        root.Clear();
        if (styleSheet != null && !root.styleSheets.Contains(styleSheet)) root.styleSheets.Add(styleSheet);
        root.AddToClassList("hud-root");
        root.pickingMode = PickingMode.Ignore;

        regions.Clear();
        foreach (HudRegion r in System.Enum.GetValues(typeof(HudRegion)))
        {
            var c = new VisualElement { name = "region-" + r, pickingMode = PickingMode.Ignore };
            c.AddToClassList("hud-region");
            c.AddToClassList("region-" + r.ToString().ToLowerInvariant());
            regions[r] = c;
        }
        root.Add(regions[HudRegion.FullScreen]);   // map overlay sits underneath the edge panels
        foreach (var kv in regions) if (kv.Key != HudRegion.FullScreen) root.Add(kv.Value);

        layerPanel = new VisualElement { name = "layer-panel" };
        layerPanel.AddToClassList("hud-panel");
        layerPanel.AddToClassList("layer-panel");
        var title = new Label("DATA LAYERS"); title.AddToClassList("hud-title"); title.AddToClassList("layer-title"); layerPanel.Add(title);
        layerList = new VisualElement(); layerPanel.Add(layerList);
        layerFoot = new Label("1-9 toggle   ·   L close"); layerFoot.AddToClassList("hud-small"); layerFoot.AddToClassList("layer-foot"); layerPanel.Add(layerFoot);
        root.Add(layerPanel);

        OverlayLayer = new VisualElement { name = "overlay-layer", pickingMode = PickingMode.Ignore };
        OverlayLayer.AddToClassList("hud-fullscreen");
        root.Add(OverlayLayer);

        help = BuildHelp();
        root.Add(help);

        exploreBadge = new VisualElement { name = "explore-badge", pickingMode = PickingMode.Ignore };
        exploreBadge.AddToClassList("explore-badge");
        var eb = new Label("EXPLORE MODE") { pickingMode = PickingMode.Ignore }; eb.AddToClassList("explore-badge-title"); exploreBadge.Add(eb);
        var eh = new Label("Very fast movement  ·  Shift to sprint  ·  X to exit") { pickingMode = PickingMode.Ignore };
        eh.AddToClassList("explore-badge-hint"); exploreBadge.Add(eh);
        root.Add(exploreBadge);

        hint = new Label("H   Controls") { pickingMode = PickingMode.Ignore };
        hint.AddToClassList("hud-hint");
        root.Add(hint);

        modulesDirty = layersDirty = true;
    }

    VisualElement BuildHelp()
    {
        var panel = new VisualElement { name = "help", pickingMode = PickingMode.Ignore };
        panel.AddToClassList("hud-panel");
        panel.AddToClassList("help-panel");
        var t = new Label("CONTROLS"); t.AddToClassList("hud-title"); panel.Add(t);
        (string key, string what)[] rows =
        {
            ("W A S D", "Walk"), ("Mouse", "Look around"), ("Shift", "Sprint"), ("Space", "Jump"),
            ("M", "Map mode (movement paused)"), ("Scroll  + -", "Zoom map  ·  drag to pan  ·  R reset"), ("L", "Data layer panel"), ("1 - 9", "Toggle layers (panel open)"),
            ("C", "Collect / drill / deposit (missions)"), ("E", "Photo viewer at a point of interest"), ("← →", "Previous / next photo"),
            ("X", "Explore mode (very fast movement)"), ("H", "Show / hide this help"), ("Esc", "Release mouse  ·  click to recapture"),
        };
        foreach (var (key, what) in rows)
        {
            var row = new VisualElement(); row.AddToClassList("hud-row");
            var k = new Label(key); k.AddToClassList("help-key"); row.Add(k);
            var w = new Label(what); w.AddToClassList("hud-label"); row.Add(w);
            panel.Add(row);
        }
        return panel;
    }

    void BuildModules()
    {
        modulesDirty = false;
        foreach (var kv in regions) kv.Value.Clear();
        foreach (HudModule m in HudModule.Registered.Where(m => m != null && m.isActiveAndEnabled).OrderBy(m => m.Order))
        {
            m.BuildInto(regions[m.Region]);
            m.OnModeChanged(Mode, ctx);
        }
        ApplyVisibility();
    }

    void BuildLayerPanel()
    {
        layersDirty = false;
        layerList.Clear(); layerToggles.Clear();
        var layers = MarsDataLayer.Registered.Where(l => l != null && l.isActiveAndEnabled).ToList();
        if (layers.Count == 0) { var none = new Label("No data layers registered"); none.AddToClassList("hud-small"); layerList.Add(none); }
        for (int i = 0; i < layers.Count; i++)
        {
            MarsDataLayer layer = layers[i];
            var row = new VisualElement(); row.AddToClassList("layer-row");
            var toggle = new Toggle($"{(i < 9 ? (i + 1).ToString() : " ")}   {layer.DisplayName}") { value = layer.IsOn };
            toggle.AddToClassList("layer-toggle");
            toggle.RegisterValueChangedCallback(e => layer.SetOn(e.newValue));
            row.Add(toggle);
            if (!string.IsNullOrEmpty(layer.Description)) { var d = new Label(layer.Description); d.AddToClassList("layer-desc"); row.Add(d); }
            string legend = layer.Legend;
            if (!string.IsNullOrEmpty(legend)) { var lg = new Label(legend); lg.AddToClassList("hud-small"); lg.AddToClassList("layer-legend"); row.Add(lg); }
            layerList.Add(row);
            layerToggles[layer] = toggle;
        }
    }

    void OnLayerToggled(MarsDataLayer layer)
    {
        if (layerToggles.TryGetValue(layer, out Toggle t)) t.SetValueWithoutNotify(layer.IsOn);
        layersDirty = true;   // refresh legends (some are only known once the layer has been built)
    }

    // ------------------------------------------------------------------ modes
    public void SetMode(HudModes mode)
    {
        if (mode == Mode) return;
        Mode = mode;
        ctx.mode = mode;
        UpdateMovement();
        foreach (HudModule m in HudModule.Registered) if (m != null && m.Root != null) m.OnModeChanged(mode, ctx);
        ApplyVisibility();
    }

    public void ToggleMap() => SetMode(Mode == HudModes.Map ? HudModes.Hud : HudModes.Map);

    /// <summary>
    /// Pause or resume walking on behalf of a pop-up (e.g. the photo viewer). While paused the mouse is free for
    /// clicking UI. Several owners can pause at once; walking resumes when all have released.
    /// </summary>
    public void SetMovementPaused(object owner, bool paused)
    {
        if (paused) pauseOwners.Add(owner); else pauseOwners.Remove(owner);
        UpdateMovement();
    }

    // Walking is on only in HUD mode with no pop-up open (disabling the controller also frees the mouse cursor).
    void UpdateMovement()
    {
        if (playerController != null) playerController.enabled = Mode == HudModes.Hud && pauseOwners.Count == 0;
    }
    void SetHelp(bool open) { helpOpen = open; helpAutoHideAt = -1f; ApplyVisibility(); }

    void ApplyVisibility()
    {
        if (root == null) return;
        foreach (HudModule m in HudModule.Registered)
            if (m != null && m.Root != null)
                m.Root.style.display = (m.VisibleIn & Mode) != 0 ? DisplayStyle.Flex : DisplayStyle.None;
        bool map = Mode == HudModes.Map;
        root.EnableInClassList("map-mode", map);     // map mode restyles the shared layer panel (see MarsHud.uss)
        layerFoot.text = map ? "Click a layer or press 1-9" : "1-9 toggle   ·   L close";
        bool layersShown = map || layersOpenInHud;
        layerPanel.style.display = layersShown ? DisplayStyle.Flex : DisplayStyle.None;
        help.style.display = helpOpen ? DisplayStyle.Flex : DisplayStyle.None;
        bool explore = playerController != null && playerController.ExploreMode;
        exploreBadge.style.display = explore && Mode == HudModes.Hud ? DisplayStyle.Flex : DisplayStyle.None;
        hint.style.display = helpOpen ? DisplayStyle.None : DisplayStyle.Flex;
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        if (root == null) return;
        if (modulesDirty) BuildModules();
        if (layersDirty) BuildLayerPanel();

        if (MarsInput.MapToggle) ToggleMap();
        if (MarsInput.LayersToggle && Mode == HudModes.Hud) { layersOpenInHud = !layersOpenInHud; ApplyVisibility(); }
        if (MarsInput.HelpToggle) SetHelp(!helpOpen);
        if (MarsInput.ExploreToggle && Mode == HudModes.Hud && playerController != null)
        {
            playerController.ExploreMode = !playerController.ExploreMode;
            ApplyVisibility();
        }
        if (helpAutoHideAt > 0f && Time.time >= helpAutoHideAt) SetHelp(false);
        int n = MarsInput.NumberPressed;
        if (n > 0 && layerPanel.style.display == DisplayStyle.Flex)
        {
            var layers = MarsDataLayer.Registered.Where(l => l != null && l.isActiveAndEnabled).ToList();
            if (n <= layers.Count) layers[n - 1].Toggle();
        }

        UpdateContext();
        foreach (HudModule m in HudModule.Registered)
            if (m != null && m.Root != null && (m.VisibleIn & Mode) != 0)
                m.Tick(ctx);
    }

    void UpdateContext()
    {
        float dt = Time.deltaTime;
        ctx.player = player;
        ctx.playerCamera = playerCamera;
        ctx.geo = MarsGeoReference.Current;
        ctx.route = RouteManager.Current;
        ctx.mode = Mode;
        ctx.deltaTime = dt;
        ctx.sessionTime += dt;
        if (player == null) return;

        ctx.headingDeg = MarsGeoReference.Heading(player.forward);
        Vector3 p = player.position;
        float step = Vector3.Distance(p, lastPos);
        lastPos = p;
        if (step > 60f) step = 0f;                                   // ignore teleports / respawns (explore mode can cover ~3 m a frame)
        if (Mode == HudModes.Hud) ctx.distanceWalked += step;
        float instant = dt > 0f ? step / dt : 0f;
        ctx.speed = Mathf.Lerp(ctx.speed, instant, 1f - Mathf.Exp(-dt * 6f));   // smoothed
        if (ctx.speed < 0.02f) ctx.speed = 0f;
    }
}
