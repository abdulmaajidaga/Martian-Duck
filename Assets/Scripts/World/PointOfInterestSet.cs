using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Creates PointOfInterest children from a JSON file made by Tools/PoiPhotos/fetch_poi_photos.py
/// (Assets/MarsData/POI/three_forks_poi.json). Use the context menu "Build Points Of Interest" in the editor;
/// the result is saved with the scene, so nothing is loaded at runtime. To add your own point by hand, just add
/// a PointOfInterest component to any GameObject instead.
/// Every point gets a marker on the ground (orange pole + label) so it can be found while walking: tall for the
/// sampling sites, short for the depot tubes. (The red reference markers in "Mars Overlays" are editor-only.)
/// </summary>
public class PointOfInterestSet : MonoBehaviour
{
    [Tooltip("JSON with the points and photo paths (relative to the JSON's folder).")]
    [SerializeField] TextAsset data;

    [Header("Ground markers")]
    [SerializeField] bool addMarkers = true;
    [UnityEngine.Serialization.FormerlySerializedAs("tubeMarkerMaterial")]
    [SerializeField] Material markerMaterial;
    [SerializeField, Min(0.2f)] float siteMarkerHeight = 3f;
    [SerializeField, Min(0.2f)] float tubeMarkerHeight = 1.4f;
    [SerializeField, Min(0.01f)] float labelSize = 0.035f;

    [Serializable] class PhotoData { public string file; public string caption; public string camera; public int sol; public string utc; public string credit; }
    [Serializable] class PointData
    {
        public string kind; public string category; public string name; public string short_name; public string subtitle; public string details;
        public string activity; public string when;
        public double lon; public double lat; public float reveal_radius_m; public PhotoData[] photos;
    }
    [Serializable] class FileData { public PointData[] points; }

#if UNITY_EDITOR
    [ContextMenu("Build Points Of Interest")]
    public void Build()
    {
        if (data == null) { Debug.LogError("[POI] Assign the JSON data file first.", this); return; }
        string folder = Path.GetDirectoryName(UnityEditor.AssetDatabase.GetAssetPath(data)).Replace('\\', '/');
        FileData file = JsonUtility.FromJson<FileData>(data.text);

        for (int i = transform.childCount - 1; i >= 0; i--)
            UnityEditor.Undo.DestroyObjectImmediate(transform.GetChild(i).gameObject);

        int photoCount = 0, markers = 0;
        foreach (PointData p in file.points)
        {
            var go = new GameObject(p.name);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Build Points Of Interest");
            go.transform.SetParent(transform, false);
            var poi = go.AddComponent<PointOfInterest>();
            poi.displayName = p.name;
            poi.shortName = string.IsNullOrEmpty(p.short_name) ? p.name : p.short_name;
            poi.category = p.category;
            poi.when = p.when;
            poi.activity = p.activity;
            poi.subtitle = p.subtitle;
            poi.details = p.details;
            poi.longitude = p.lon; poi.latitude = p.lat;
            if (p.reveal_radius_m > 0f) poi.revealRadius = p.reveal_radius_m;
            poi.photos.Clear();
            foreach (PhotoData ph in p.photos)
            {
                var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/{ph.file}");
                if (tex == null) Debug.LogWarning($"[POI] Photo not found: {folder}/{ph.file}", this);
                // "Front Hazcam: the robotic arm at the outcrop" -> caption "The robotic arm at the outcrop"
                string caption = ph.caption ?? "";
                int colon = caption.IndexOf(": ", StringComparison.Ordinal);
                if (colon >= 0) caption = caption.Substring(colon + 2);
                if (caption.Length > 0) caption = char.ToUpperInvariant(caption[0]) + caption.Substring(1);
                string camera = (ph.camera ?? "").Replace(" (left)", "");
                poi.photos.Add(new PoiPhoto { image = tex, caption = caption, credit = $"{camera}  ·  Sol {ph.sol}  ·  {ph.credit}" });
                photoCount++;
            }
            poi.Snap();
            if (addMarkers)
            {
                bool tube = p.kind == "depot_tube";
                AddMarker(go.transform, poi.shortName, tube ? tubeMarkerHeight : siteMarkerHeight, tube ? 1f : 2f);
                markers++;
            }
        }
        UnityEditor.EditorUtility.SetDirty(gameObject);
        Debug.Log($"[POI] Built {file.points.Length} points of interest with {photoCount} photos ({markers} ground markers).", this);
    }

    void AddMarker(Transform parent, string label, float height, float scale)
    {
        var root = new GameObject("Marker");
        root.transform.SetParent(parent, false);

        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(pole.GetComponent<Collider>());
        pole.name = "Pole";
        pole.transform.SetParent(root.transform, false);
        pole.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        pole.transform.localScale = new Vector3(0.05f * scale, height * 0.5f, 0.05f * scale);
        if (markerMaterial != null) pole.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;

        GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(top.GetComponent<Collider>());
        top.name = "Top";
        top.transform.SetParent(root.transform, false);
        top.transform.localPosition = new Vector3(0f, height, 0f);
        top.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
        top.transform.localScale = Vector3.one * 0.22f * scale;
        if (markerMaterial != null) top.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;

        var text = new GameObject("Label");
        text.transform.SetParent(root.transform, false);
        text.transform.localPosition = new Vector3(0f, height + 0.3f * scale, 0f);
        var tm = text.AddComponent<TextMesh>();
        tm.text = label;
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 64;
        tm.characterSize = labelSize * scale;
        tm.color = new Color(1f, 0.85f, 0.7f);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null) { tm.font = font; text.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
        text.AddComponent<MarsBillboard>();
    }
#endif
}
