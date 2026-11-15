using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Builds a Unity Terrain from Mars elevation data (a grayscale Texture2D or a 16-bit RAW file
/// in StreamingAssets) and drapes a single color/orthophoto texture over it.
///
/// Two ways to use it:
///  - Runtime: with "Generate On Start" on, the terrain and player are built when Play starts.
///  - Baked:   "Bake Terrain To Scene" (Inspector button or context menu) builds them in Edit Mode,
///             saves the TerrainData and TerrainLayer as assets in Assets/MarsData/Baked/, and turns
///             "Generate On Start" off so Play uses the baked scene objects as they are.
///
/// Orientation convention: terrain row 0 is the south edge (Z = 0).
///  - Texture2D pixel rows already run bottom-to-top, so they map directly.
///  - RAW files are assumed to be stored top-to-bottom (row 0 = north), the usual convention
///    for exported DEMs, and are flipped on read so north ends up at +Z.
/// Use "Flip Vertically" if the heightmap ends up mirrored relative to the color texture.
/// </summary>
[DisallowMultipleComponent]
public class MarsTerrainGenerator : MonoBehaviour
{
    public enum HeightSource { Texture, RawFile }
    public enum ByteOrder { LittleEndian, BigEndian }

    const int MinResolution = 33;
    const int MaxResolution = 4097;
    const string TerrainObjectName = "Mars Terrain";
    const string LogPrefix = "[MarsTerrainGenerator] ";

    [Header("Height Input")]
    [SerializeField] HeightSource heightSource = HeightSource.Texture;

    [Tooltip("Grayscale heightmap. Read/Write must be enabled in its import settings. " +
             "R16 textures are read at full 16-bit precision.")]
    [SerializeField] Texture2D heightmapTexture;

    [Tooltip("16-bit unsigned RAW file, relative to Assets/StreamingAssets (e.g. \"Mars/gale_crater.raw\").")]
    [SerializeField] string rawFileName = "mars_heightmap.raw";
    [SerializeField, Min(2)] int rawWidth = 1025;
    [SerializeField, Min(2)] int rawHeight = 1025;
    [Tooltip("Little Endian is often called 'Windows' or 'PC' byte order; Big Endian is 'Mac' or 'Motorola'.")]
    [SerializeField] ByteOrder rawByteOrder = ByteOrder.LittleEndian;

    [Tooltip("Flip the heightmap north-south. Use this if the terrain looks mirrored relative to the color texture.")]
    [SerializeField] bool flipVertically;

    [Header("Height Processing")]
    [Tooltip("Map the lowest sample to Min Height and the highest to Max Height. " +
             "When off, source values (0..1 of the full 8- or 16-bit range) map directly onto the height range.")]
    [SerializeField] bool normalizeHeights = true;

    [Tooltip("Terrain heightmap resolution. Must be 2^n + 1 between 33 and 4097 (e.g. 513, 1025, 2049). " +
             "Invalid values snap to the nearest valid one.")]
    [SerializeField] int terrainResolution = 1025;

    [Header("Real-World Size (meters)")]
    [Tooltip("East-west extent (X).")]
    [SerializeField, Min(1f)] float terrainWidth = 10000f;
    [Tooltip("North-south extent (Z).")]
    [SerializeField, Min(1f)] float terrainLength = 10000f;
    [Tooltip("Elevation of the lowest point.")]
    [SerializeField] float minHeight = 0f;
    [Tooltip("Elevation of the highest point.")]
    [SerializeField] float maxHeight = 1000f;
    [Tooltip("Offset the terrain by Min Height so world Y equals real elevation. " +
             "When off, the lowest point sits at this object's Y position.")]
    [SerializeField] bool placeAtTrueElevation;

    [Header("Appearance")]
    [Tooltip("Color/orthophoto image covering exactly the same area as the heightmap. Draped once over the whole terrain.")]
    [SerializeField] Texture2D colorTexture;

    [Tooltip("Terrain material for the active render pipeline, e.g. one using " +
             "'Universal Render Pipeline/Terrain/Lit' (URP) or 'HDRP/TerrainLit' (HDRP). " +
             "Leave empty for the Built-in Render Pipeline.")]
    [SerializeField] Material terrainMaterial;

    [Tooltip("Surface smoothness (gloss). Martian dust and sand are matte, so keep this at or near 0.")]
    [SerializeField, Range(0f, 1f)] float surfaceSmoothness = 0f;

    [Header("Player")]
    [Tooltip("Place a player on the terrain when it is built at runtime or baked.")]
    [SerializeField] bool spawnPlayer = true;

    [Tooltip("Optional player prefab. If it has a CharacterController, the bottom of its capsule is placed on the " +
             "surface; otherwise its pivot is. If empty, a capsule astronaut is created with the Main Camera at eye height.")]
    [SerializeField] GameObject playerPrefab;

    [Tooltip("Spawn point as a fraction of the terrain: (0, 0) = south-west corner, (1, 1) = north-east corner.")]
    [SerializeField] Vector2 spawnPosition = new Vector2(0.5f, 0.5f);

    [Tooltip("Extra clearance above the surface, in meters.")]
    [SerializeField, Min(0f)] float spawnClearance = 0.1f;

    [Tooltip("Model used as the default astronaut's body, scaled to the astronaut's height with its feet at the pivot. " +
             "If empty, a capsule is used.")]
    [SerializeField] GameObject astronautModel;

    [Tooltip("Camera height above the feet for the default capsule astronaut, in meters.")]
    [SerializeField, Range(0.5f, 1.8f)] float eyeHeight = 1.65f;

    [Tooltip("How far in front of the body's centre the camera sits, in meters, so the view starts just in front " +
             "of the helmet instead of inside the model.")]
    [SerializeField, Range(0f, 1.5f)] float eyeForward = 0.5f;

    [Header("Generation")]
    [Tooltip("Build the terrain (and player) when Play starts. Baking turns this off so Play uses the baked objects.")]
    [SerializeField] bool generateOnStart = true;

    [Header("Baking")]
    [Tooltip("File name prefix for baked assets, e.g. \"jezero\" gives jezero_terrain.asset and jezero_layer.terrainlayer. " +
             "If empty, it is taken from the heightmap name (minus a _height suffix).")]
    [SerializeField] string bakeName = "";

    [Tooltip("Terrain created by the last bake. Set automatically.")]
    [SerializeField] Terrain bakedTerrain;

    [Tooltip("Player placed by the last bake. Set automatically. Re-baking moves this player back to the spawn point; " +
             "delete it before baking to create a fresh one.")]
    [SerializeField] GameObject bakedPlayer;

    const float AstronautHeight = 1.8f;
    const float AstronautRadius = 0.3f;

    Terrain generatedTerrain;
    TerrainData generatedData;
    TerrainLayer generatedLayer;

    GameObject spawnedPlayer;
    Camera borrowedCamera;          // Scene camera moved into the default astronaut; restored on regenerate.
    Transform borrowedCameraParent;
    Vector3 borrowedCameraPosition;
    Quaternion borrowedCameraRotation;
    float borrowedCameraNear, borrowedCameraFar;

    public Terrain GeneratedTerrain => generatedTerrain;
    public GameObject SpawnedPlayer => spawnedPlayer;
    public Terrain BakedTerrain => bakedTerrain;
    public GameObject BakedPlayer => bakedPlayer;

    public bool GenerateOnStart
    {
        get => generateOnStart;
        set => generateOnStart = value;
    }

    void Start()
    {
        if (generateOnStart)
            Generate();
    }

    void OnDestroy()
    {
        // The terrain GameObject is a child and is destroyed with us; the data objects are not.
        DestroyGenerated(generatedLayer);
        DestroyGenerated(generatedData);
    }

    /// <summary>
    /// Builds a temporary terrain. In Play Mode this is the runtime terrain; in Edit Mode it is an
    /// unsaved preview (use Bake Terrain To Scene for a terrain that is saved with the scene).
    /// </summary>
    [ContextMenu("Generate")]
    public void Generate()
    {
        if (!Application.isPlaying && bakedTerrain != null)
        {
            Debug.LogWarning($"{LogPrefix}This object has a baked terrain. Use 'Bake Terrain To Scene' to rebuild it; " +
                             "'Generate' in Edit Mode only makes an unsaved preview and would replace the baked terrain.", this);
            return;
        }

        if (TryComputeHeights(out float[,] heights, out int resolution))
            BuildTerrain(heights, resolution);
    }

    /// <summary>Reads the height input and turns it into a [resolution, resolution] array of 0..1 heights.</summary>
    bool TryComputeHeights(out float[,] heights, out int resolution)
    {
        heights = null;
        resolution = 0;

        if (maxHeight <= minHeight)
        {
            LogError($"Max Height ({maxHeight} m) must be greater than Min Height ({minHeight} m).");
            return false;
        }

        bool ok = heightSource == HeightSource.Texture
            ? TryReadTexture(out float[] samples, out int width, out int height)
            : TryReadRaw(out samples, out width, out height);
        if (!ok)
            return false;

        if (width < 2 || height < 2)
        {
            LogError($"Heightmap is {width} x {height}; it must be at least 2 x 2.");
            return false;
        }

        if (flipVertically)
            FlipRows(samples, width, height);

        if (normalizeHeights)
            Normalize(samples);

        resolution = terrainResolution;
        if (!IsValidResolution(resolution))
        {
            resolution = NearestValidResolution(resolution);
            Debug.LogWarning($"{LogPrefix}Terrain Resolution {terrainResolution} is not 2^n + 1 in [{MinResolution}, {MaxResolution}]; using {resolution}.", this);
        }

        heights = Resample(samples, width, height, resolution);
        return true;
    }

    // ---------------------------------------------------------------- Reading input

    bool TryReadTexture(out float[] samples, out int width, out int height)
    {
        samples = null;
        width = height = 0;

        if (heightmapTexture == null)
        {
            LogError("Height Source is Texture but no Heightmap Texture is assigned.");
            return false;
        }

        if (!heightmapTexture.isReadable)
        {
            LogError($"Heightmap texture '{heightmapTexture.name}' is not readable. Select it in the Project window, " +
                     "enable 'Read/Write' in its import settings and click Apply.");
            return false;
        }

        width = heightmapTexture.width;
        height = heightmapTexture.height;
        samples = new float[width * height];

        if (heightmapTexture.format == TextureFormat.R16)
        {
            // Read the raw 16-bit values directly to keep full precision.
            var pixels = heightmapTexture.GetPixelData<ushort>(0);
            for (int i = 0; i < samples.Length; i++)
                samples[i] = pixels[i] / 65535f;
        }
        else
        {
            Color[] pixels = heightmapTexture.GetPixels();
            bool alphaOnly = heightmapTexture.format == TextureFormat.Alpha8;
            for (int i = 0; i < samples.Length; i++)
                samples[i] = alphaOnly ? pixels[i].a : pixels[i].r;
        }

        return true;
    }

    bool TryReadRaw(out float[] samples, out int width, out int height)
    {
        samples = null;
        width = rawWidth;
        height = rawHeight;

        if (string.IsNullOrWhiteSpace(rawFileName))
        {
            LogError("Height Source is Raw File but no RAW File Name is set.");
            return false;
        }

        // Note: File IO works on desktop and in the Editor. On Android and WebGL, StreamingAssets
        // is not a regular folder and would need UnityWebRequest instead.
        string path = Path.Combine(Application.streamingAssetsPath, rawFileName);
        if (!File.Exists(path))
        {
            LogError($"RAW file not found: '{path}'.");
            return false;
        }

        byte[] bytes = File.ReadAllBytes(path);
        long expected = (long)width * height * 2;
        if (bytes.LongLength != expected)
        {
            string hint = "";
            if (bytes.LongLength % 2 == 0)
            {
                long sampleCount = bytes.LongLength / 2;
                long side = (long)Math.Round(Math.Sqrt(sampleCount));
                if (side * side == sampleCount)
                    hint = $" The file size would match a square {side} x {side} 16-bit heightmap.";
            }

            LogError($"RAW file size mismatch for '{path}': the file is {bytes.LongLength:N0} bytes, but " +
                     $"{width} x {height} samples x 2 bytes = {expected:N0} bytes. " +
                     $"Check RAW Width / RAW Height and that the file is 16-bit.{hint}");
            return false;
        }

        samples = new float[width * height];
        bool littleEndian = rawByteOrder == ByteOrder.LittleEndian;

        for (int row = 0; row < height; row++)
        {
            int targetRow = height - 1 - row; // RAW rows run north -> south; terrain rows run south -> north.
            int srcRowStart = row * width * 2;
            int dstRowStart = targetRow * width;

            for (int x = 0; x < width; x++)
            {
                int i = srcRowStart + x * 2;
                int value = littleEndian
                    ? bytes[i] | (bytes[i + 1] << 8)
                    : (bytes[i] << 8) | bytes[i + 1];
                samples[dstRowStart + x] = value / 65535f;
            }
        }

        return true;
    }

    // ---------------------------------------------------------------- Processing

    static void FlipRows(float[] samples, int width, int height)
    {
        var temp = new float[width];
        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            Array.Copy(samples, top * width, temp, 0, width);
            Array.Copy(samples, bottom * width, samples, top * width, width);
            Array.Copy(temp, 0, samples, bottom * width, width);
        }
    }

    void Normalize(float[] samples)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (float s in samples)
        {
            if (s < min) min = s;
            if (s > max) max = s;
        }

        float range = max - min;
        if (range <= 0f)
        {
            Debug.LogWarning($"{LogPrefix}Heightmap is completely flat; skipping normalization.", this);
            Array.Clear(samples, 0, samples.Length);
            return;
        }

        for (int i = 0; i < samples.Length; i++)
            samples[i] = (samples[i] - min) / range;
    }

    /// <summary>Bilinearly resamples a row-major source grid to a square [res, res] grid ([z, x] order).</summary>
    static float[,] Resample(float[] src, int width, int height, int res)
    {
        var dst = new float[res, res];
        float scaleX = (width - 1) / (float)(res - 1);
        float scaleY = (height - 1) / (float)(res - 1);

        for (int y = 0; y < res; y++)
        {
            float fy = y * scaleY;
            int y0 = Mathf.Min((int)fy, height - 2);
            float ty = fy - y0;
            int row0 = y0 * width;
            int row1 = row0 + width;

            for (int x = 0; x < res; x++)
            {
                float fx = x * scaleX;
                int x0 = Mathf.Min((int)fx, width - 2);
                float tx = fx - x0;

                float bottom = Mathf.Lerp(src[row0 + x0], src[row0 + x0 + 1], tx);
                float top = Mathf.Lerp(src[row1 + x0], src[row1 + x0 + 1], tx);
                dst[y, x] = Mathf.Clamp01(Mathf.Lerp(bottom, top, ty));
            }
        }

        return dst;
    }

    static bool IsValidResolution(int r) =>
        r >= MinResolution && r <= MaxResolution && ((r - 1) & (r - 2)) == 0;

    static int NearestValidResolution(int r)
    {
        int best = MinResolution;
        for (int candidate = MinResolution; candidate <= MaxResolution; candidate = (candidate - 1) * 2 + 1)
        {
            if (Mathf.Abs(candidate - r) < Mathf.Abs(best - r))
                best = candidate;
        }
        return best;
    }

    // ---------------------------------------------------------------- Building the terrain

    void BuildTerrain(float[,] heights, int resolution)
    {
        RemoveGeneratedTerrain();

        TerrainLayer layer = null;
        if (colorTexture != null)
        {
            layer = new TerrainLayer { name = "Mars Color Overlay" };
            ConfigureLayer(layer);
        }

        var data = new TerrainData { name = "Mars TerrainData" };
        ConfigureTerrainData(data, heights, resolution, layer);

        Terrain terrain = CreateTerrainObject(data);

        if (!Application.isPlaying)
        {
            // Edit Mode preview: TerrainData and TerrainLayer are not saved as assets, so keep the
            // preview out of the saved scene to avoid broken references. Baking saves them properly.
            terrain.gameObject.hideFlags = HideFlags.DontSave;
            data.hideFlags = HideFlags.DontSave;
            if (layer != null)
                layer.hideFlags = HideFlags.DontSave;
        }

        generatedTerrain = terrain;
        generatedData = data;
        generatedLayer = layer;

        Debug.Log($"{LogPrefix}Generated {resolution} x {resolution} terrain, " +
                  $"{terrainWidth:N0} x {terrainLength:N0} m, heights {minHeight:N0} to {maxHeight:N0} m.", this);

        if (spawnPlayer)
        {
            if (Application.isPlaying)
                SpawnPlayer(terrain);
            else
                Debug.Log($"{LogPrefix}Skipping player spawn for the Edit Mode preview (baking places a player).", this);
        }
    }

    /// <summary>A single layer whose tile covers the whole terrain = one image draped exactly once.</summary>
    void ConfigureLayer(TerrainLayer layer)
    {
        // Clamp so the edges of the image don't bleed or repeat across the terrain border.
        colorTexture.wrapMode = TextureWrapMode.Clamp;

        layer.diffuseTexture = colorTexture;
        layer.tileSize = new Vector2(terrainWidth, terrainLength);
        layer.tileOffset = Vector2.zero;

        // Dusty regolith is matte. The default smoothness source reads the colour texture's alpha channel, which is
        // fully opaque (= maximum smoothness) for an RGB image, making the ground mirror-like. Use a constant instead.
        layer.smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly;
        layer.smoothness = surfaceSmoothness;
        layer.metallic = 0f;
    }

    void ConfigureTerrainData(TerrainData data, float[,] heights, int resolution, TerrainLayer layer)
    {
        data.heightmapResolution = resolution; // Set before size: changing the resolution resets the size.
        data.size = new Vector3(terrainWidth, maxHeight - minHeight, terrainLength);
        data.SetHeights(0, 0, heights);
        data.terrainLayers = layer != null ? new[] { layer } : Array.Empty<TerrainLayer>();

        if (layer == null)
            Debug.LogWarning($"{LogPrefix}No Color Texture assigned; the terrain will have no surface texture.", this);
    }

    Terrain CreateTerrainObject(TerrainData data)
    {
        GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
        terrainObject.name = TerrainObjectName;
        terrainObject.transform.SetParent(transform, false);
        terrainObject.transform.position = transform.position + (placeAtTrueElevation ? Vector3.up * minHeight : Vector3.zero);

        Terrain terrain = terrainObject.GetComponent<Terrain>();
        terrain.drawInstanced = true;
        // Keep the full-resolution overlay visible at all distances instead of the low-res basemap.
        terrain.basemapDistance = Mathf.Max(terrainWidth, terrainLength) * 2f;

        if (terrainMaterial != null)
        {
            terrain.materialTemplate = terrainMaterial;
        }
        else if (GraphicsSettings.currentRenderPipeline != null)
        {
            Debug.LogWarning($"{LogPrefix}A Scriptable Render Pipeline is active but no Terrain Material is assigned, so the terrain may render pink. " +
                             "Assign a material using 'Universal Render Pipeline/Terrain/Lit' (URP) or 'HDRP/TerrainLit' (HDRP).", this);
        }

        return terrain;
    }

    // ---------------------------------------------------------------- Player

    void SpawnPlayer(Terrain terrain)
    {
        // Make sure the new TerrainCollider is in the physics scene before the player touches it.
        Physics.SyncTransforms();

        // A baked player already in the scene is reused rather than spawning a second one.
        if (bakedPlayer != null)
        {
            PlacePlayerOnSurface(bakedPlayer, terrain);
            return;
        }

        GameObject player;
        if (playerPrefab != null)
        {
            player = Instantiate(playerPrefab);
            player.name = playerPrefab.name;
        }
        else
        {
            player = CreateAstronaut(terrain);
        }

        PlacePlayerOnSurface(player, terrain);
        spawnedPlayer = player;
    }

    /// <summary>Puts the player's feet on the terrain surface at the normalized spawn position.</summary>
    void PlacePlayerOnSurface(GameObject player, Terrain terrain)
    {
        // Work out how high the pivot must sit above the surface for the feet (plus the
        // controller's skin) to rest on it, and how wide the footprint is.
        float pivotAboveSurface = 0f;
        float footprintRadius = 0f;
        if (player.TryGetComponent(out CharacterController cc))
        {
            Vector3 scale = player.transform.lossyScale;
            pivotAboveSurface = -(cc.center.y - cc.height * 0.5f) * scale.y + cc.skinWidth;
            footprintRadius = cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        Vector3 size = terrain.terrainData.size;
        Vector3 origin = terrain.transform.position;
        Vector3 spawn = new Vector3(
            origin.x + Mathf.Clamp01(spawnPosition.x) * size.x,
            0f,
            origin.z + Mathf.Clamp01(spawnPosition.y) * size.z);

        float surface = SurfaceHeight(terrain, spawn, footprintRadius);
        spawn.y = surface + pivotAboveSurface + spawnClearance;

        player.transform.SetPositionAndRotation(spawn, Quaternion.identity);
        Physics.SyncTransforms(); // A CharacterController doesn't see transform changes until synced.

        // Make sure the player's camera can see across the whole terrain (it may have been set up for a smaller one).
        float terrainDiagonal = size.magnitude;
        foreach (Camera cam in player.GetComponentsInChildren<Camera>(true))
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, terrainDiagonal);

        Debug.Log($"{LogPrefix}Placed '{player.name}' at {spawn} (surface {surface:N1} m).", this);
    }

    /// <summary>
    /// Highest terrain height under a circular footprint, so a capsule on a slope doesn't start inside the ground.
    /// Terrain.SampleHeight is relative to the terrain's position, so the terrain's Y is added back.
    /// </summary>
    static float SurfaceHeight(Terrain terrain, Vector3 center, float radius)
    {
        float height = terrain.SampleHeight(center);
        if (radius > 0f)
        {
            const int Samples = 8;
            for (int i = 0; i < Samples; i++)
            {
                float angle = i * Mathf.PI * 2f / Samples;
                Vector3 p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                height = Mathf.Max(height, terrain.SampleHeight(p));
            }
        }
        return height + terrain.transform.position.y;
    }

    GameObject CreateAstronaut(Terrain terrain)
    {
        var player = new GameObject("Mars Astronaut");

        // Pivot at the feet: the capsule runs from y = 0 to y = AstronautHeight.
        var cc = player.AddComponent<CharacterController>();
        cc.height = AstronautHeight;
        cc.radius = AstronautRadius;
        cc.center = new Vector3(0f, AstronautHeight * 0.5f, 0f);
        cc.stepOffset = 0.3f;

        CreateBody(player.transform, cc.center);

        // Use the scene's Main Camera if there is one, otherwise create a camera.
        Camera cam = Camera.main;
        if (cam != null)
        {
            borrowedCamera = cam;
            borrowedCameraParent = cam.transform.parent;
            borrowedCameraPosition = cam.transform.position;
            borrowedCameraRotation = cam.transform.rotation;
            borrowedCameraNear = cam.nearClipPlane;
            borrowedCameraFar = cam.farClipPlane;
        }
        else
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        cam.transform.SetParent(player.transform, false);
        cam.transform.localPosition = new Vector3(0f, eyeHeight, eyeForward);
        cam.transform.localRotation = Quaternion.identity;
        cam.nearClipPlane = 0.05f; // Just in front of the helmet, so the body never clips into view.
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, terrain.terrainData.size.magnitude);

        // Added last so the controller finds the camera in Awake.
        player.AddComponent<MarsPlayerController>();
        return player;
    }

    /// <summary>
    /// Adds the astronaut's visual body under the player: the astronaut model scaled to AstronautHeight with its
    /// feet at the pivot, or a capsule if no model is set. The CharacterController is the only collider.
    /// </summary>
    GameObject CreateBody(Transform player, Vector3 capsuleCenter)
    {
        if (astronautModel == null)
        {
            // The default capsule mesh is 2 m tall with a 0.5 m radius.
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Body";
            DestroyImmediate(capsule.GetComponent<Collider>());
            capsule.transform.SetParent(player, false);
            capsule.transform.localPosition = capsuleCenter;
            capsule.transform.localScale = new Vector3(AstronautRadius * 2f, AstronautHeight * 0.5f, AstronautRadius * 2f);
            return capsule;
        }

        GameObject body = Instantiate(astronautModel, player, false);
        body.name = "Body";
        foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
            DestroyImmediate(c);
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = Vector3.one;

        // Fit the model's height to the astronaut and stand its lowest point on the pivot.
        Bounds bounds = default;
        bool any = false;
        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Bounds rb = r is SkinnedMeshRenderer smr && smr.sharedMesh != null
                ? TransformBounds(smr.transform, smr.sharedMesh.bounds)
                : r.bounds;
            if (any) bounds.Encapsulate(rb); else { bounds = rb; any = true; }
        }
        if (any && bounds.size.y > 0.01f)
        {
            float scale = AstronautHeight / bounds.size.y;
            body.transform.localScale = Vector3.one * scale;
            float feet = player.InverseTransformPoint(bounds.min).y * scale;
            body.transform.localPosition = new Vector3(0f, -feet, 0f);
        }
        return body;
    }

    static Bounds TransformBounds(Transform t, Bounds local)
    {
        var result = new Bounds(t.TransformPoint(local.center), Vector3.zero);
        Vector3 e = local.extents;
        for (int i = 0; i < 8; i++)
            result.Encapsulate(t.TransformPoint(local.center + Vector3.Scale(e,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return result;
    }

    void RemovePlayer()
    {
        // Give a borrowed scene camera back before its temporary parent is destroyed.
        if (borrowedCamera != null)
        {
            Transform camTransform = borrowedCamera.transform;
            camTransform.SetParent(borrowedCameraParent, true);
            camTransform.SetPositionAndRotation(borrowedCameraPosition, borrowedCameraRotation);
            borrowedCamera.nearClipPlane = borrowedCameraNear;
            borrowedCamera.farClipPlane = borrowedCameraFar;
        }
        borrowedCamera = null;

        DestroyGenerated(spawnedPlayer);
        spawnedPlayer = null;
    }

    void RemoveGeneratedTerrain()
    {
        RemovePlayer();

        // Fall back to a name lookup in case the references were lost (e.g. after a script reload).
        if (generatedTerrain == null)
        {
            Transform existing = transform.Find(TerrainObjectName);
            if (existing != null)
                generatedTerrain = existing.GetComponent<Terrain>();
        }

        if (generatedTerrain != null)
        {
            TerrainData oldData = generatedTerrain.terrainData;
            // Only destroy data this script created, never a TerrainData asset someone assigned by hand.
            if (oldData != null && oldData.name == "Mars TerrainData")
            {
                foreach (TerrainLayer oldLayer in oldData.terrainLayers)
                    DestroyGenerated(oldLayer);
                DestroyGenerated(oldData);
            }
            DestroyGenerated(generatedTerrain.gameObject);
        }

        DestroyGenerated(generatedLayer);
        DestroyGenerated(generatedData);

        generatedTerrain = null;
        generatedData = null;
        generatedLayer = null;
    }

    static void DestroyGenerated(UnityEngine.Object obj)
    {
        if (obj == null)
            return;

#if UNITY_EDITOR
        // Never delete asset files (e.g. baked TerrainData/TerrainLayer) from cleanup code.
        if (EditorUtility.IsPersistent(obj))
            return;
#endif

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    void LogError(string message) => Debug.LogError(LogPrefix + message, this);

    // ---------------------------------------------------------------- Baking (Editor only)

#if UNITY_EDITOR
    const string BakeFolder = "Assets/MarsData/Baked";

    /// <summary>
    /// Builds the terrain in Edit Mode from the current settings, saves the TerrainData and TerrainLayer
    /// as assets in Assets/MarsData/Baked/, places the player on the surface, and turns off Generate On Start.
    /// Re-baking overwrites the same asset files and replaces the scene terrain instead of adding another.
    /// </summary>
    [ContextMenu("Bake Terrain To Scene")]
    public void BakeTerrainToScene()
    {
        if (Application.isPlaying)
        {
            LogError("Exit Play Mode before baking.");
            return;
        }

        if (!gameObject.scene.IsValid())
        {
            LogError("Bake from an object in an open scene, not from a prefab asset.");
            return;
        }

        if (!TryComputeHeights(out float[,] heights, out int resolution))
            return;

        string baseName = GetBakeName();
        EnsureFolder(BakeFolder);
        string dataPath = $"{BakeFolder}/{baseName}_terrain.asset";
        string layerPath = $"{BakeFolder}/{baseName}_layer.terrainlayer";

        // 1. Assets. Existing files are updated in place, which keeps their GUIDs (and any references) intact.
        TerrainLayer layer = null;
        if (colorTexture != null)
        {
            SetImporterWrapToClamp(colorTexture);
            layer = LoadOrCreateAsset<TerrainLayer>(layerPath, out _);
            ConfigureLayer(layer);
            EditorUtility.SetDirty(layer);
        }

        TerrainData data = LoadOrCreateAsset<TerrainData>(dataPath, out bool replacedData);
        ConfigureTerrainData(data, heights, resolution, layer);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        // 2. Scene terrain. Remove the previous baked terrain and any unsaved previews, then create a fresh one.
        if (bakedTerrain != null)
            DestroyImmediate(bakedTerrain.gameObject);
        bakedTerrain = null;
        for (int i = 0; i < 16 && transform.Find(TerrainObjectName) != null; i++)
            RemoveGeneratedTerrain();

        Terrain terrain = CreateTerrainObject(data);
        bakedTerrain = terrain;

        // 3. Player. Reuse the previously baked player if it still exists, otherwise create one.
        if (spawnPlayer)
        {
            if (bakedPlayer == null)
                bakedPlayer = CreateBakedPlayer(terrain);
            PlacePlayerOnSurface(bakedPlayer, terrain);
        }

        // 4. Play should use the baked objects as they are.
        generateOnStart = false;

        EditorUtility.SetDirty(this);
        EditorSceneManager.MarkSceneDirty(gameObject.scene);

        Debug.Log($"{LogPrefix}Baked {resolution} x {resolution} terrain ({(replacedData ? "replaced" : "created")} {dataPath}" +
                  (layer != null ? $", {layerPath}" : "") + "). Generate On Start is now off. Save the scene to keep the result.", this);
    }

    GameObject CreateBakedPlayer(Terrain terrain)
    {
        if (playerPrefab != null)
            return (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, gameObject.scene);

        GameObject astronaut = CreateAstronaut(terrain);
        // Baked: the Main Camera stays inside the astronaut and is saved with the scene, so don't restore it later.
        borrowedCamera = null;
        return astronaut;
    }

    string GetBakeName()
    {
        string result = bakeName;
        if (string.IsNullOrWhiteSpace(result))
        {
            result = heightSource == HeightSource.Texture
                ? (heightmapTexture != null ? heightmapTexture.name : "")
                : Path.GetFileNameWithoutExtension(rawFileName);

            foreach (string suffix in new[] { "_heightmap", "_height", "_dtm", "_dem" })
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length);
                    break;
                }
            }
        }

        foreach (char c in Path.GetInvalidFileNameChars())
            result = result.Replace(c, '_');
        result = result.Trim();
        return string.IsNullOrEmpty(result) ? "mars" : result;
    }

    static T LoadOrCreateAsset<T>(string path, out bool existed) where T : UnityEngine.Object, new()
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        existed = asset != null;
        if (!existed)
        {
            asset = new T();
            AssetDatabase.CreateAsset(asset, path);
        }
        return asset;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    /// <summary>Makes the Clamp wrap mode permanent in the texture's import settings.</summary>
    static void SetImporterWrapToClamp(Texture2D texture)
    {
        if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter importer &&
            importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
    }
#endif
}
