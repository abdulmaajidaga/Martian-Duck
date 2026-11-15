"""
Regenerates the route overlay data (Perseverance's drive path, the sample depot zone and the sampling sites) for the
current map from the mission GeoJSON files in MarsSources/.

Output: Assets/MarsData/Overlays/jezero_route_overlay.json, read by MarsRouteOverlay ("Build Overlays").
Positions are fractions of the map bounds (x west->east 0..1, y south->north 0..1); the bounds are stored in the file.
The sampling sites are written to the file but the overlay does not draw markers for them by default, because every
site also has an in-game point-of-interest marker.

Usage (from the project root):  python Tools/RegionBuilder/make_route_overlay.py
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "MarsSources")
INFO = os.path.join(ROOT, "Assets", "MarsData", "Heightmaps", "jezero_crater_info.json")
OUT = os.path.join(ROOT, "Assets", "MarsData", "Overlays", "jezero_route_overlay.json")


def main():
    b = json.load(open(INFO, encoding="utf-8"))["bounds"]
    fx = lambda lon: (lon - b["west"]) / (b["east"] - b["west"])
    fy = lambda lat: (lat - b["south"]) / (b["north"] - b["south"])

    drives = []
    for f in json.load(open(os.path.join(SRC, "Rover Drive Path.geojson"), encoding="utf-8"))["features"]:
        g = f["geometry"]
        parts = [g["coordinates"]] if g["type"] == "LineString" else g["coordinates"]
        pts = []
        for part in parts:
            for lon, lat, *_ in part:
                pts += [round(fx(lon), 7), round(fy(lat), 7)]
        drives.append({"sol": int(f["properties"]["sol"]), "length_m": float(f["properties"].get("length") or 0), "points": pts})
    drives.sort(key=lambda d: d["sol"])

    dz = json.load(open(os.path.join(SRC, "Sample Depot Zone.geojson"), encoding="utf-8"))["features"][0]["geometry"]
    ring = dz["coordinates"][0] if dz["type"] == "Polygon" else dz["coordinates"][0][0]
    depot = [v for lon, lat, *_ in ring for v in (round(fx(lon), 7), round(fy(lat), 7))]

    samples = []
    for f in json.load(open(os.path.join(SRC, "Sampling Locations.geojson"), encoding="utf-8"))["features"]:
        p = f["properties"]
        samples.append({"name": p["sample_name"], "date": p["date"], "sol": p["sol"], "location": p["sample_location"],
                        "type": p["sample_type"], "x": round(fx(p["lon"]), 7), "y": round(fy(p["lat"]), 7)})

    out = {"note": "Made by Tools/RegionBuilder/make_route_overlay.py from MarsSources/*.geojson (Mars 2020 frame). "
                   "Positions are fractions of 'bounds'.",
           "bounds": b, "drives": drives, "depot_zone": depot, "samples": samples}
    json.dump(out, open(OUT, "w", encoding="utf-8"), separators=(",", ":"))
    km = sum(d["length_m"] for d in drives) / 1000
    print(f"{len(drives)} drives (sols {drives[0]['sol']}-{drives[-1]['sol']}, {km:.1f} km), "
          f"{sum(len(d['points']) for d in drives) // 2} points, {len(samples)} sampling sites -> {OUT}")


if __name__ == "__main__":
    main()
