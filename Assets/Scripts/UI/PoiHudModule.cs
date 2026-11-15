using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Point-of-interest card + photo viewer.
///   Walking near a PointOfInterest shows a card: photo (with ◀ ▶ and dots), what kind of place it is, its name,
///   when the rover was there and what it did. ← / → change the photo.
///   E opens a full-screen photo viewer: walking pauses and the mouse is free, so the ◀ ▶ and ✕ buttons can be
///   clicked (← / → also work). E or Esc closes it.
/// </summary>
public class PoiHudModule : HudModule
{
    [Tooltip("Card width on screen (px at 1920x1080). The photo fills the top of the card.")]
    [SerializeField] float cardWidth = 440f;
    [SerializeField] float cardPhotoHeight = 270f;

    // card
    VisualElement cardPhotoBox, dots, keyHint;
    Image cardPhoto;
    Label eyebrow, distance, title, when, activity, cardCaption;
    // viewer
    VisualElement viewer, viewerDots;
    Image viewerPhoto;
    Label viewerTitle, viewerWhen, viewerCaption, viewerCredit, viewerCounter;

    PointOfInterest current;
    int photoIndex;
    bool viewerOpen;

    void Reset() => SetPlacement(HudRegion.RightMiddle, 0, HudModes.Hud);

    // ------------------------------------------------------------------ build
    protected override void Build(VisualElement root)
    {
        root.RemoveFromClassList("hud-panel");          // own look, not the generic HUD box
        root.AddToClassList("poi-card");
        root.style.width = cardWidth;

        cardPhotoBox = Box(root, "poi-card-photo");
        cardPhotoBox.style.height = cardPhotoHeight;
        cardPhoto = new Image { scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
        cardPhoto.AddToClassList("poi-fill");
        cardPhotoBox.Add(cardPhoto);
        cardPhotoBox.Add(ArrowButton(-1, "poi-arrow poi-arrow-card poi-arrow-left"));
        cardPhotoBox.Add(ArrowButton(+1, "poi-arrow poi-arrow-card poi-arrow-right"));
        dots = Box(cardPhotoBox, "poi-dots");

        VisualElement body = Box(root, "poi-card-body");
        VisualElement top = Box(body, "poi-row");
        eyebrow = Text(top, "poi-eyebrow");
        distance = Text(top, "poi-distance");
        title = Text(body, "poi-title");
        when = Text(body, "poi-when");
        activity = Text(body, "poi-activity");
        cardCaption = Text(body, "poi-caption");
        keyHint = Box(body, "poi-row poi-hint-row");
        Text(keyHint, "poi-key", "E");
        Text(keyHint, "poi-hint", "View photos");
        Text(keyHint, "poi-key poi-key-gap", "← →");
        Text(keyHint, "poi-hint", "Browse");

        BuildViewer();
        root.style.display = DisplayStyle.None;
    }

    void BuildViewer()
    {
        viewer?.RemoveFromHierarchy();                  // the HUD can rebuild modules; don't leave an old copy behind
        VisualElement layer = MarsHud.Current != null ? MarsHud.Current.OverlayLayer : null;
        if (layer == null) return;

        viewer = new VisualElement { pickingMode = PickingMode.Position };   // blocks clicks to anything behind
        viewer.AddToClassList("poi-viewer");
        layer.Add(viewer);

        VisualElement stage = Box(viewer, "poi-viewer-stage");
        stage.Add(ArrowButton(-1, "poi-arrow poi-arrow-big"));
        VisualElement frame = Box(stage, "poi-viewer-frame");
        viewerPhoto = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
        viewerPhoto.AddToClassList("poi-fill");
        frame.Add(viewerPhoto);
        stage.Add(ArrowButton(+1, "poi-arrow poi-arrow-big"));

        VisualElement info = Box(viewer, "poi-viewer-info");
        VisualElement head = Box(info, "poi-row");
        viewerTitle = Text(head, "poi-viewer-title");
        viewerCounter = Text(head, "poi-viewer-counter");
        viewerWhen = Text(info, "poi-when");
        viewerCaption = Text(info, "poi-viewer-caption");
        viewerCredit = Text(info, "poi-credit");
        viewerDots = Box(info, "poi-dots poi-dots-static");
        VisualElement hint = Box(info, "poi-row poi-hint-row");
        Text(hint, "poi-key", "← →"); Text(hint, "poi-hint", "Browse");
        Text(hint, "poi-key poi-key-gap", "Esc"); Text(hint, "poi-hint", "Close");

        var close = new Button(CloseViewer) { tooltip = "Close" };
        close.AddToClassList("poi-close");
        close.Add(new PoiIcon(PoiIcon.Shape.Close));
        viewer.Add(close);

        viewer.style.display = DisplayStyle.None;
    }

    // ------------------------------------------------------------------ per frame
    public override void Tick(HudContext ctx)
    {
        if (viewerOpen)
        {
            if (MarsInput.PhotoPrev) Step(-1);
            if (MarsInput.PhotoNext) Step(+1);
            if (MarsInput.Close || MarsInput.PoiViewerToggle) CloseViewer();
            return;
        }

        PointOfInterest near = ctx.player != null ? PointOfInterest.NearestInRange(ctx.player.position, out _) : null;
        if (near == null)
        {
            current = null;
            Root.style.display = DisplayStyle.None;
            return;
        }
        Root.style.display = DisplayStyle.Flex;
        if (near != current) { current = near; photoIndex = 0; ShowPoint(); }

        if (MarsInput.PhotoPrev) Step(-1);
        if (MarsInput.PhotoNext) Step(+1);
        if (MarsInput.PoiViewerToggle && current.photos.Count > 0) OpenViewer();

        Vector3 d = current.Position - ctx.player.position; d.y = 0f;
        distance.text = $"{d.magnitude:0} m away";
    }

    public override void OnModeChanged(HudModes mode, HudContext ctx)
    {
        if (viewerOpen) CloseViewer();
        current = null;
    }

    protected override void OnDisable()
    {
        if (viewerOpen) CloseViewer();
        viewer?.RemoveFromHierarchy();
        base.OnDisable();
    }

    // ------------------------------------------------------------------ content
    void ShowPoint()
    {
        eyebrow.text = string.IsNullOrEmpty(current.category) ? "Point of interest" : current.category;
        title.text = current.displayName;
        SetText(when, current.when);
        SetText(activity, string.IsNullOrEmpty(current.activity) ? current.details : current.activity);
        bool hasPhotos = current.photos.Count > 0;
        cardPhotoBox.style.display = hasPhotos ? DisplayStyle.Flex : DisplayStyle.None;
        keyHint.style.display = hasPhotos ? DisplayStyle.Flex : DisplayStyle.None;
        BuildDots(dots, current.photos.Count);
        ShowPhoto();
    }

    void Step(int delta)
    {
        if (current == null || current.photos.Count < 2) return;
        int n = current.photos.Count;
        photoIndex = ((photoIndex + delta) % n + n) % n;
        ShowPhoto();
    }

    void ShowPhoto()
    {
        if (current == null) return;
        int n = current.photos.Count;
        PoiPhoto p = n > 0 ? current.photos[Mathf.Clamp(photoIndex, 0, n - 1)] : null;
        cardPhoto.image = p?.image;
        SetText(cardCaption, p?.caption);
        foreach (VisualElement arrow in cardPhotoBox.Query(className: "poi-arrow").ToList())
            arrow.style.display = n > 1 ? DisplayStyle.Flex : DisplayStyle.None;
        HighlightDot(dots, photoIndex);

        if (viewer == null) return;
        viewerPhoto.image = p?.image;
        viewerTitle.text = current.displayName;
        viewerCounter.text = n > 1 ? $"{photoIndex + 1} / {n}" : "";
        SetText(viewerWhen, current.when);
        SetText(viewerCaption, p?.caption);
        SetText(viewerCredit, p?.credit);
        HighlightDot(viewerDots, photoIndex);
    }

    void OpenViewer()
    {
        if (viewer == null) BuildViewer();
        if (viewer == null || current == null) return;
        viewerOpen = true;
        BuildDots(viewerDots, current.photos.Count);
        ShowPhoto();
        viewer.style.display = DisplayStyle.Flex;
        Root.style.display = DisplayStyle.None;
        MarsHud.Current?.SetMovementPaused(this, true);       // frees the mouse for the buttons
    }

    void CloseViewer()
    {
        viewerOpen = false;
        if (viewer != null) viewer.style.display = DisplayStyle.None;
        MarsHud.Current?.SetMovementPaused(this, false);
    }

    // ------------------------------------------------------------------ helpers
    Button ArrowButton(int dir, string classes)
    {
        var b = new Button(() => Step(dir)) { tooltip = dir < 0 ? "Previous photo" : "Next photo" };
        foreach (string c in classes.Split(' ')) b.AddToClassList(c);
        b.Add(new PoiIcon(dir < 0 ? PoiIcon.Shape.Left : PoiIcon.Shape.Right));
        return b;
    }

    static void BuildDots(VisualElement row, int count)
    {
        if (row == null) return;
        row.Clear();
        row.style.display = count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
        for (int i = 0; i < count; i++) Box(row, "poi-dot");
    }

    static void HighlightDot(VisualElement row, int index)
    {
        if (row == null) return;
        List<VisualElement> all = row.Query(className: "poi-dot").ToList();
        for (int i = 0; i < all.Count; i++) all[i].EnableInClassList("poi-dot-on", i == index);
    }

    static VisualElement Box(VisualElement parent, string classes)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        foreach (string c in classes.Split(' ')) e.AddToClassList(c);
        parent.Add(e);
        return e;
    }

    static Label Text(VisualElement parent, string classes, string text = "")
    {
        var l = new Label(text) { pickingMode = PickingMode.Ignore };
        foreach (string c in classes.Split(' ')) l.AddToClassList(c);
        parent.Add(l);
        return l;
    }

    static void SetText(Label l, string text)
    {
        l.text = text ?? "";
        l.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
    }
}

/// <summary>Small vector icons (chevrons, close) so the UI doesn't depend on font glyphs.</summary>
public class PoiIcon : VisualElement
{
    public enum Shape { Left, Right, Close }
    readonly Shape shape;

    public PoiIcon(Shape shape)
    {
        this.shape = shape;
        pickingMode = PickingMode.Ignore;
        AddToClassList("poi-icon");
        generateVisualContent += Draw;
    }

    void Draw(MeshGenerationContext mgc)
    {
        Rect r = contentRect;
        float w = r.width, h = r.height;
        var p = mgc.painter2D;
        p.strokeColor = resolvedStyle.color;
        p.lineWidth = Mathf.Max(2f, w * 0.12f);
        p.lineCap = LineCap.Round;
        p.lineJoin = LineJoin.Round;
        p.BeginPath();
        switch (shape)
        {
            case Shape.Left:
                p.MoveTo(new Vector2(w * 0.62f, h * 0.2f)); p.LineTo(new Vector2(w * 0.32f, h * 0.5f)); p.LineTo(new Vector2(w * 0.62f, h * 0.8f));
                break;
            case Shape.Right:
                p.MoveTo(new Vector2(w * 0.38f, h * 0.2f)); p.LineTo(new Vector2(w * 0.68f, h * 0.5f)); p.LineTo(new Vector2(w * 0.38f, h * 0.8f));
                break;
            default:
                p.MoveTo(new Vector2(w * 0.25f, h * 0.25f)); p.LineTo(new Vector2(w * 0.75f, h * 0.75f));
                p.MoveTo(new Vector2(w * 0.75f, h * 0.25f)); p.LineTo(new Vector2(w * 0.25f, h * 0.75f));
                break;
        }
        p.Stroke();
    }
}
