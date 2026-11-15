using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Data layer that drapes a map image (north up, covering exactly the map's lon/lat bounds) over the terrain as one
/// decal, e.g. mineral detections or geology. Transparent parts of the image leave the ground untouched.
/// Make the image with a tool (see Tools/RegionBuilder/make_mineral_layers.py), assign it here, and the layer shows
/// up in the L panel. The legend supports rich text, e.g. "&lt;color=#b07cff&gt;Silica&lt;/color&gt;".
/// </summary>
public class ImageOverlayLayer : MarsDataLayer
{
    [Tooltip("North-up image covering the map bounds exactly (alpha = coverage).")]
    [SerializeField] Texture2D image;
    [Tooltip("Decal shader (Shader Graphs/Decal). Referenced here so it is included in builds.")]
    [SerializeField] Shader decalShader;
    [SerializeField, Range(0f, 1f)] float opacity = 0.7f;
    [TextArea(1, 4)] [SerializeField] string legend;

    DecalProjector projector;
    Material material;

    public override string Legend => legend;

    protected override void ApplyState(bool on)
    {
        if (on && projector == null) Create();
        if (projector != null) projector.gameObject.SetActive(on);
    }

    void Create()
    {
        MarsGeoReference geo = MarsGeoReference.Current;
        if (image == null) { Debug.LogWarning($"[ImageOverlayLayer] '{DisplayName}' has no image.", this); return; }
        if (geo == null || !geo.ResolveTerrain()) { Debug.LogWarning("[ImageOverlayLayer] No terrain/MarsGeoReference found.", this); return; }
        Shader shader = decalShader != null ? decalShader : Shader.Find("Shader Graphs/Decal");
        if (shader == null) { Debug.LogWarning("[ImageOverlayLayer] Decal shader not found.", this); return; }

        material = new Material(shader) { name = $"{DisplayName} (runtime)" };
        material.SetTexture("Base_Map", image);
        material.SetFloat("Normal_Blend", 0f);
        material.enableInstancing = true;

        Vector3 size = geo.Size;
        var go = new GameObject($"{DisplayName} decal") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        Vector3 centre = geo.Origin + new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
        go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(Vector3.down, Vector3.forward));   // image up = north
        projector = go.AddComponent<DecalProjector>();
        projector.material = material;
        projector.pivot = Vector3.zero;
        projector.size = new Vector3(size.x, size.z, size.y + 50f);
        projector.drawDistance = 100000f;
        projector.fadeScale = 1f;
        projector.startAngleFade = 89f;
        projector.endAngleFade = 90f;
        projector.fadeFactor = opacity;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (projector != null) { if (Application.isPlaying) Destroy(projector.gameObject); else DestroyImmediate(projector.gameObject); }
        if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
        projector = null; material = null;
    }
}
