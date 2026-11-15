using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Crops the global TES Dust Cover Index (PDS3 QUBE, e.g. MarsSources/dci_lo_ice_dust_16ppd_shifted.pds) to the
/// area of the open scene's MarsGeoReference and saves it as JSON for MarsDustMap.
///
/// File layout (checked against known areas: Syrtis Major and Acidalia come out dust-free, Tharsis dusty):
/// one band of 4-byte little-endian floats (PC_REAL), rows from 90°N to 90°S. In the "_shifted" product column 0
/// starts at 180°W (longitudes -180..180); otherwise at 0°E (0..360). 0.85 marks missing data.
/// </summary>
public static class DustIndexImporter
{
    const string DefaultSource = "MarsSources/dci_lo_ice_dust_16ppd_shifted.pds";
    const int MarginPixels = 2;

    [MenuItem("Tools/Mars Planning/Import Dust Index (TES DCI)...")]
    static void Import()
    {
        string src = File.Exists(DefaultSource) ? DefaultSource
            : EditorUtility.OpenFilePanel("TES Dust Cover Index (.pds)", "MarsSources", "pds");
        if (string.IsNullOrEmpty(src)) return;

        MarsGeoReference geo = UnityEngine.Object.FindAnyObjectByType<MarsGeoReference>();
        if (geo == null) { EditorUtility.DisplayDialog("Import Dust Index", "Open a scene with a MarsGeoReference first.", "OK"); return; }

        string dst = EditorUtility.SaveFilePanelInProject("Save dust grid", $"{TerrainName(geo)}_dust_index", "json",
                                                          "Where to save the cropped dust grid", "Assets/Planning");
        if (string.IsNullOrEmpty(dst)) return;

        try
        {
            MarsDustMap.Grid grid = Crop(src, geo.westLongitude, geo.eastLongitude, geo.southLatitude, geo.northLatitude);
            File.WriteAllText(dst, JsonUtility.ToJson(grid, true));
            AssetDatabase.ImportAsset(dst);
            Debug.Log($"[Dust] Saved {grid.cols}×{grid.rows} dust grid to {dst}", AssetDatabase.LoadAssetAtPath<TextAsset>(dst));
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Import Dust Index", e.Message, "OK");
        }
    }

    static string TerrainName(MarsGeoReference geo) =>
        geo.ResolveTerrain() && geo.terrain.terrainData != null ? geo.terrain.terrainData.name.Replace("_terrain", "") : "map";

    public static MarsDustMap.Grid Crop(string path, double west, double east, double south, double north)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        // PDS3 label: read enough of the header to find the keywords we need
        byte[] head = br.ReadBytes((int)Math.Min(fs.Length, 16384));
        string label = Encoding.ASCII.GetString(head);
        int endLabel = label.IndexOf("\nEND", StringComparison.Ordinal);
        if (endLabel > 0) label = label.Substring(0, endLabel);
        int recordBytes = Int(label, @"RECORD_BYTES\s*=\s*(\d+)");
        int qubeRecord = Int(label, @"\^QUBE\s*=\s*(\d+)");
        Match items = Regex.Match(label, @"CORE_ITEMS\s*=\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
        if (!items.Success) throw new Exception("CORE_ITEMS not found in the PDS label");
        int bands = int.Parse(items.Groups[1].Value), width = int.Parse(items.Groups[2].Value), height = int.Parse(items.Groups[3].Value);
        if (bands != 1) throw new Exception($"Expected 1 band, found {bands}");
        if (!Regex.IsMatch(label, @"CORE_ITEM_TYPE\s*=\s*PC_REAL")) throw new Exception("Expected CORE_ITEM_TYPE = PC_REAL");
        if (Int(label, @"CORE_ITEM_BYTES\s*=\s*(\d+)") != 4) throw new Exception("Expected 4-byte floats");

        float ppd = width / 360f;
        if (Mathf.Abs(height / 180f - ppd) > 1e-3f) throw new Exception($"Not a global grid: {width}×{height}");
        double lonStart = Path.GetFileName(path).Contains("shifted") ? -180.0 : 0.0;
        long dataStart = (long)(qubeRecord - 1) * recordBytes;

        double Lon(double l) { l -= lonStart; l %= 360.0; return l < 0 ? l + 360.0 : l; }
        int c0 = (int)Math.Floor(Lon(west) * ppd) - MarginPixels;
        int c1 = (int)Math.Ceiling(Lon(east) * ppd) - 1 + MarginPixels;
        if (c1 < c0) c1 += width;                       // crosses the seam
        int r0 = Math.Max(0, (int)Math.Floor((90.0 - north) * ppd) - MarginPixels);
        int r1 = Math.Min(height - 1, (int)Math.Ceiling((90.0 - south) * ppd) - 1 + MarginPixels);

        var grid = new MarsDustMap.Grid
        {
            source = $"TES Dust Cover Index (Ruff & Christensen 2002): {Path.GetFileName(path)}",
            note = "Higher DCI = less dust. Rows run north->south, columns west->east. Pixel (r,c) centre: " +
                   "lon = westEdge + (c+0.5)/ppd, lat = northEdge - (r+0.5)/ppd.",
            pixelsPerDegree = ppd,
            westEdge = lonStart + c0 / (double)ppd,
            northEdge = 90.0 - r0 / (double)ppd,
            cols = c1 - c0 + 1,
            rows = r1 - r0 + 1,
            noData = 0.85f,
        };
        grid.values = new float[grid.cols * grid.rows];
        for (int r = 0; r < grid.rows; r++)
            for (int c = 0; c < grid.cols; c++)
            {
                int col = ((c0 + c) % width + width) % width;
                fs.Position = dataStart + ((long)(r0 + r) * width + col) * 4;
                byte[] b = br.ReadBytes(4);
                if (!BitConverter.IsLittleEndian) Array.Reverse(b);
                grid.values[r * grid.cols + c] = (float)Math.Round(BitConverter.ToSingle(b, 0), 5);
            }
        return grid;
    }

    static int Int(string label, string pattern)
    {
        Match m = Regex.Match(label, pattern);
        if (!m.Success) throw new Exception($"'{pattern}' not found in the PDS label");
        return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
