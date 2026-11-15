using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Base for data layers that colour the whole terrain: subclasses fill a north-up texture covering the terrain and
/// it is drawn as one semi-transparent decal, so it shows in both the walking view and map mode.
/// </summary>
public abstract class TerrainTintLayer : MarsDataLayer
{
    [Tooltip("Decal shader (Shader Graphs/Decal). Referenced here so it is included in builds.")]
    [SerializeField] Shader decalShader;
    [SerializeField, Range(0f, 1f)] protected float opacity = 0.55f;
    [SerializeField, Range(64, 1024)] protected int resolution = 512;

    DecalProjector projector;
    Material material;
    Texture2D tint;

    /// <summary>
    /// Fill <paramref name="pixels"/> (resolution x resolution, row 0 = south edge, column 0 = west edge).
    /// Return false if the data isn't available.
    /// </summary>
    protected abstract bool FillTint(MarsGeoReference geo, int resolution, Color[] pixels);

    protected override void ApplyState(bool on)
    {
        if (on && projector == null) Create();
        if (projector != null) projector.gameObject.SetActive(on);
    }

    void Create()
    {
        string tag = $"[{GetType().Name}]";
        MarsGeoReference geo = MarsGeoReference.Current;
        if (geo == null || !geo.ResolveTerrain()) { Debug.LogWarning($"{tag} No terrain/MarsGeoReference found.", this); return; }
        Shader shader = decalShader != null ? decalShader : Shader.Find("Shader Graphs/Decal");
        if (shader == null) { Debug.LogWarning($"{tag} Decal shader not found.", this); return; }

        var px = new Color[resolution * resolution];
        if (!FillTint(geo, resolution, px)) { Debug.LogWarning($"{tag} '{DisplayName}' has no data to draw.", this); return; }

        tint = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = DisplayName };
        tint.SetPixels(px);
        tint.Apply(true, true);

        material = new Material(shader) { name = $"{DisplayName} (runtime)" };
        material.SetTexture("Base_Map", tint);
        material.SetFloat("Normal_Blend", 0f);
        material.enableInstancing = true;

        Vector3 size = geo.terrain.terrainData.size;
        var go = new GameObject($"{DisplayName} decal") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        Vector3 centre = geo.Origin + new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
        go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(Vector3.down, Vector3.forward));   // texture up = north
        projector = go.AddComponent<DecalProjector>();
        projector.material = material;
        projector.pivot = Vector3.zero;
        projector.size = new Vector3(size.x, size.z, size.y + 50f);
        projector.drawDistance = 100000f;
        projector.fadeScale = 1f;
        projector.startAngleFade = 89f;
        projector.endAngleFade = 90f;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (projector != null) Destroy(projector.gameObject);
        if (material != null) Destroy(material);
        if (tint != null) Destroy(tint);
        projector = null; material = null; tint = null;
    }
}
