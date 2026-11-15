"""
Mineral data layers for the current map, from the NASA Mars Trek map services (public ArcGIS REST endpoints, found via
Trek's getLayerServices for each layer's UUID):

  Aqueous Mineral Distribution  (Ehlmann & Edwards 2014, Annu. Rev. Earth Planet. Sci. 42:291-315)
      points classed silica / phyllosilicate / sulfate / chloride / carbonate, coloured as in Mars Trek
  Hydrous Mineral Detections (OMEGA, CRISM)  (Carter et al. 2013, JGR Planets 118:831-858)
      points where hydrated minerals were detected (no mineral type); red as in Mars Trek

Both are global compilations: each point is one orbital detection site, so on a 45 x 59 km map there are only a
few. Each point is drawn as a circle of DETECTION_RADIUS_M (about the size of a CRISM observation footprint).

Outputs:
  MarsSources/minerals/<layer>_map.geojson                    raw features for the map area (not in git)
  Assets/MarsData/Overlays/Minerals/<layer>.png               north-up RGBA image covering the map bounds exactly
  Assets/MarsData/Overlays/Minerals/minerals_legend.json      class -> colour + counts (for the layer legend)

Usage (from the project root):  python Tools/RegionBuilder/make_mineral_layers.py
"""
import json
import math
import os
import subprocess
import urllib.parse

from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
INFO = os.path.join(ROOT, "Assets", "MarsData", "Heightmaps", "jezero_crater_info.json")
RAW = os.path.join(ROOT, "MarsSources", "minerals")
OUT = os.path.join(ROOT, "Assets", "MarsData", "Overlays", "Minerals")
SERVICE = "https://trek.nasa.gov/mars/trekarcgis/rest/services/{}/MapServer/0/query"
LAYERS = {
    "aqueous_minerals": "Aquieous_Mineral_Distribution_Options_clon0",    # (sic) the service name is misspelled
    "hydrous_minerals": "Hydrous_Mineral_Detections_OMEGA_CRISM_clon0",
}
DETECTION_RADIUS_M = 2500.0
PX_PER_KM = 24                      # output image resolution (about 42 m per pixel)


def query(service, b, margin=0.1):
    params = {
        "where": "1=1", "geometryType": "esriGeometryEnvelope", "inSR": "104905", "spatialRel": "esriSpatialRelIntersects",
        "geometry": f"{b['west'] - margin},{b['south'] - margin},{b['east'] + margin},{b['north'] + margin}",
        "outFields": "*", "returnGeometry": "true", "f": "geojson",
    }
    url = SERVICE.format(service) + "?" + urllib.parse.urlencode(params)
    out = subprocess.run(["curl", "-sL", "--max-time", "120", "--retry", "3", url], capture_output=True, check=True)
    data = json.loads(out.stdout)
    if "features" not in data:
        raise SystemExit(f"Mars Trek query failed for {service}: {str(data)[:300]}")
    return data


def rgba(text, alpha=None):
    r, g, b, a = (int(v) for v in text.split())
    return (r, g, b, a if alpha is None else alpha)


def main():
    info = json.load(open(INFO, encoding="utf-8"))
    b, W, L = info["bounds"], info["width_m"], info["length_m"]
    w_px, h_px = int(W / 1000 * PX_PER_KM), int(L / 1000 * PX_PER_KM)
    to_px = lambda lon, lat: ((lon - b["west"]) / (b["east"] - b["west"]) * w_px, (b["north"] - lat) / (b["north"] - b["south"]) * h_px)
    r_px = DETECTION_RADIUS_M / 1000 * PX_PER_KM
    os.makedirs(RAW, exist_ok=True); os.makedirs(OUT, exist_ok=True)
    legend = {}

    for name, service in LAYERS.items():
        data = query(service, b)
        json.dump(data, open(os.path.join(RAW, f"{name}_map.geojson"), "w", encoding="utf-8"))
        # duplicates (same class at the same spot) are common in the source; keep one
        seen, points = set(), []
        for f in data["features"]:
            lon, lat = f["geometry"]["coordinates"][:2]
            p = f["properties"]
            cls = p.get("Class") or p.get("Type") or "detection"
            key = (round(lon, 3), round(lat, 3), cls)
            if key in seen: continue
            seen.add(key)
            inside = b["west"] <= lon <= b["east"] and b["south"] <= lat <= b["north"]
            points.append((lon, lat, cls, p.get("jC_fillclr", "255 0 0 255"), inside))

        img = Image.new("RGBA", (w_px, h_px), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        counts = {}
        for lon, lat, cls, colour, inside in points:
            x, y = to_px(lon, lat)            # circles at the edge are drawn partly; the image is cropped to the map
            fill, ring = rgba(colour, 110), rgba(colour, 255)
            d.ellipse([x - r_px, y - r_px, x + r_px, y + r_px], fill=fill, outline=ring, width=max(2, int(r_px * 0.08)))
            if inside:
                c = counts.setdefault(cls, {"colour": colour, "count": 0}); c["count"] += 1
        img.save(os.path.join(OUT, f"{name}.png"))
        legend[name] = {"service": service, "detections_in_map": counts,
                        "detections_drawn_incl_edge": len(points)}
        print(f"{name}: {len(points)} unique detections drawn ({sum(c['count'] for c in counts.values())} inside the map) "
              f"-> {name}.png {w_px}x{h_px}  {counts}")
    json.dump(legend, open(os.path.join(OUT, "minerals_legend.json"), "w", encoding="utf-8"), indent=1)


if __name__ == "__main__":
    main()
