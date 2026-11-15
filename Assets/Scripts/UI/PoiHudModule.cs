using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Point-of-interest panel: when the astronaut is near a PointOfInterest, shows its name, details and real
/// photos (P = next photo). Hidden when no point is in range.
/// </summary>
public class PoiHudModule : HudModule
{
    [Tooltip("Photo width on screen (px at 1920x1080).")]
    [SerializeField] float photoWidth = 480f;

    Label poiName, subtitle, details, caption, credit, footer, distance;
    Image photo;
    PointOfInterest current;
    int photoIndex;

    void Reset() => SetPlacement(HudRegion.RightMiddle, 0, HudModes.Hud);

    protected override void Build(VisualElement root)
    {
        root.AddToClassList("poi-panel");
        var head = AddRow(root);
        AddLabel(head, "POINT OF INTEREST", "hud-title", "poi-title");
        distance = AddLabel(head, "", "hud-small", "poi-distance");
        poiName = AddLabel(root, "-", "hud-value");
        subtitle = AddLabel(root, "", "hud-label", "poi-subtitle");
        details = AddLabel(root, "", "hud-small", "poi-details");
        photo = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
        photo.AddToClassList("poi-photo");
        root.Add(photo);
        caption = AddLabel(root, "", "hud-label", "poi-caption");
        credit = AddLabel(root, "", "hud-small");
        footer = AddLabel(root, "", "hud-small", "poi-footer");
        root.style.display = DisplayStyle.None;
    }

    public override void OnModeChanged(HudModes mode, HudContext ctx)
    {
        current = null;   // re-evaluate (and re-hide if nothing is near) on the next tick
    }

    public override void Tick(HudContext ctx)
    {
        PointOfInterest near = ctx.player != null ? PointOfInterest.NearestInRange(ctx.player.position, out float dist) : null;
        if (near == null)
        {
            current = null;
            Root.style.display = DisplayStyle.None;
            return;
        }
        Root.style.display = DisplayStyle.Flex;
        if (near != current) { current = near; photoIndex = 0; ShowPoint(); }

        int count = current.photos.Count;
        if (count > 1 && MarsInput.NextPhoto) { photoIndex = (photoIndex + 1) % count; ShowPhoto(); }
        Vector3 d = current.Position - ctx.player.position; d.y = 0f;
        distance.text = $"{d.magnitude:0} m";
    }

    void ShowPoint()
    {
        poiName.text = current.displayName;
        subtitle.text = current.subtitle;
        subtitle.style.display = string.IsNullOrEmpty(current.subtitle) ? DisplayStyle.None : DisplayStyle.Flex;
        details.text = current.details;
        details.style.display = string.IsNullOrEmpty(current.details) ? DisplayStyle.None : DisplayStyle.Flex;
        ShowPhoto();
    }

    void ShowPhoto()
    {
        int count = current.photos.Count;
        PoiPhoto p = count > 0 ? current.photos[Mathf.Clamp(photoIndex, 0, count - 1)] : null;
        bool has = p != null && p.image != null;
        photo.image = has ? p.image : null;
        photo.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
        if (has)
        {
            photo.style.width = photoWidth;
            photo.style.height = photoWidth * p.image.height / Mathf.Max(1f, p.image.width);
        }
        caption.text = p?.caption ?? "";
        credit.text = p?.credit ?? "";
        footer.text = count > 1 ? $"P  next photo   ·   {photoIndex + 1} of {count}" : "";
        footer.style.display = count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
