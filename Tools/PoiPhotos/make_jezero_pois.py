"""
Builds Assets/MarsData/POI/jezero_poi.json: every point of interest on the Jezero map that is not already in
three_forks_poi.json (the Three Forks sampling sites and depot tubes). Unity's "Build Points Of Interest" step on the
"Jezero Points of Interest" object reads it.

Sources (all public):
  * MarsSources/Sampling Locations.geojson     Mars 2020 sampling sites (NASA/JPL MMGIS, "M20" map layers)
  * MarsSources/Rover Waypoints.geojson        rover positions per sol (same source)
  * MarsSources/m20_heli_waypoints.json        Ingenuity's 72 flights and airfields
      https://mars.nasa.gov/mmgis-maps/M20/Layers/json/m20_heli_waypoints.json
  * IAU / USGS Gazetteer of Planetary Nomenclature: approved feature names inside the map bounds
      https://planetarynames.wr.usgs.gov  (MARS_nomenclature_center_pts.zip)
  * NASA raw-image feed for the photos (credit NASA/JPL-Caltech, see fetch_poi_photos.py)

Photos are picked automatically (not by eye as in fetch_poi_photos.py): for a sampling site, the last full-frame
Front Hazcam image and a WATSON or Navcam image from the sampling sol; for Ingenuity, its own navigation camera on
the flight sol. Check them in Assets/MarsData/POI/Photos and swap ids in PHOTO_OVERRIDES if one is poor.

Usage (from the project root):  python Tools/PoiPhotos/make_jezero_pois.py
Needs: Python 3, curl on PATH, Pillow. Existing photos are not downloaded again.
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fetch_poi_photos as fp  # noqa: E402  (shared download, caption and wording helpers)

ROOT = fp.ROOT
SRC = os.path.join(ROOT, "MarsSources")
OUT = os.path.join(fp.OUT_DIR, "jezero_poi.json")
INFO = os.path.join(ROOT, "Assets", "MarsData", "Heightmaps", "jezero_crater_info.json")

fp.CAMERA_NAMES.update({"HELI_NAV": "Ingenuity navigation camera", "HELI_RTE": "Ingenuity colour camera",
                        "NAVCAM_LEFT": "Navcam (left)"})

# Already in three_forks_poi.json (kept by fetch_poi_photos.py).
ALREADY_THERE = set(fp.SAMPLE_SITES)

# Sites that share one position in the source data are merged into one point.
MERGE = {"Pelican Point": "Pelican Point + Lefroy Bay", "Lefroy Bay": "Pelican Point + Lefroy Bay"}

# Extra context for a few samples (one or two sentences, appended to the activity).
SAMPLE_NOTES = {
    "Roubion": "The first sampling attempt: the rock crumbled, so the tube sealed only Martian air.",
    "Montdenier + Montagnac": "The first rock cores collected on Mars.",
    "Sapphire Canyon": ("Drilled from the arrowhead-shaped rock nicknamed \"Cheyava Falls\", whose leopard-spot "
                        "markings NASA described in 2025 as a potential biosignature, the best candidate yet for "
                        "signs of ancient life."),
    "Comet Geyser": "Silica-cemented carbonate from the margin of the old lake.",
    "Green Gardens": "Serpentinite: rock altered by water deep underground.",
}

# Points with no sampling sol in the data get no photos.
PHOTO_OVERRIDES = {}  # name -> [(image id, caption), ...]


def load(path):
    return json.load(open(path, encoding="utf-8"))


def in_bounds(lon, lat, b):
    return b["west"] <= lon <= b["east"] and b["south"] <= lat <= b["north"]


def rover_position_on(waypoints, sol):
    """Rover position at the end of the given sol (last waypoint on or before it)."""
    best = None
    for p in waypoints:
        if p["sol"] <= sol and (best is None or (p["sol"], p["site"], p["drive"]) >= (best["sol"], best["site"], best["drive"])):
            best = p
    return best


def frame_size(img):
    m = re.match(r"\((\d+),(\d+)\)", (img.get("extended") or {}).get("dimension", "") or "")
    return (int(m.group(1)), int(m.group(2))) if m else (0, 0)


def frame_area(img):
    w, h = frame_size(img)
    return w * h


def photo_shaped(img):
    """Roughly 4:3 or square: skips panorama strips and tall slivers."""
    w, h = frame_size(img)
    return h > 0 and 0.9 <= w / h <= 1.7


def pick_image(sol, instruments, last=True):
    """Largest image from these cameras on that sol; among equals the latest (or earliest)."""
    if sol not in fp._sol_cache:
        try:
            fp._sol_cache[sol] = {i["imageid"]: i for i in fp.curl_json(fp.API.format(sol=sol))["images"]}
        except Exception as e:  # network or empty sol
            print(f"  sol {sol}: no image list ({e})")
            fp._sol_cache[sol] = {}
    imgs = [i for i in fp._sol_cache[sol].values()
            if i["camera"]["instrument"] in instruments and i.get("sample_type") == "Full" and photo_shaped(i)]
    if not imgs:
        return None
    biggest = max(frame_area(i) for i in imgs)
    imgs = sorted((i for i in imgs if frame_area(i) == biggest), key=lambda i: i["date_taken_utc"])
    return imgs[-1 if last else 0]["imageid"]


def ordinal(n):
    return f"{n}{'th' if 10 <= n % 100 <= 20 else {1: 'st', 2: 'nd', 3: 'rd'}.get(n % 10, 'th')}"


def photos_for(prefix, picks):
    out = []
    for n, (image_id, caption) in enumerate(picks, 1):
        if image_id:
            try:
                out.append(fp.fetch_photo(image_id, f"{prefix}_{n}.jpg", caption))
            except Exception as e:
                print(f"  photo {image_id} failed: {e}")
    return out


def first_of(values):
    return next((v for v in values if v), None)


def slug(name):
    return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")


def first_sol(text):
    m = re.search(r"\d+", text or "")
    return int(m.group(0)) if m else None


def sample_points(samples):
    merged = {}
    for s in samples:
        s = dict(s)
        s["sample_name"] = s["sample_name"].replace("_", " ")
        name = MERGE.get(s["sample_name"], s["sample_name"])
        if name in ALREADY_THERE:
            continue
        if name in merged:   # merge into the earlier entry
            m = merged[name]
            m["sample_num"] += f" & {s['sample_num']}"
            m["sol"] += f" & {s['sol']}"
            m["date"] += f" & {s['date']}"
            m["outcrop_name"] += f" / {s['outcrop_name']}"
            continue
        s["sample_name"] = name
        merged[name] = s

    points = []
    for name, s in merged.items():
        sol = first_sol(s["sol"])
        known = sol is not None and "TBD" not in s["sol"]
        rock = "" if s["rock_type"] in ("", "n/a") else f"  ·  {s['rock_type']}"
        many = "s" if "&" in s["sample_num"] else ""
        if "/" in s["outcrop_name"]:   # merged: two separate rock cores
            activity = (f"Drilled two rock cores here, {name.replace(' + ', ' and ')}, from the "
                        f"{s['outcrop_name'].replace(' / ', ' and ')} outcrops and sealed them in sample tubes.")
            if rock:
                activity += f" The material is {s['rock_type'][0].lower() + s['rock_type'][1:]} rock."
        else:
            activity = fp.sample_activity(s)
        if name in SAMPLE_NOTES:
            activity += " " + SAMPLE_NOTES[name]
        when = (f"{fp.sols_text(s['sol'])}  ·  {fp.tidy_date(s['date'])}" if known
                else "Collected after the latest date in this dataset")
        length = float(s["sample_ht_cm"] or 0)
        details = (f"Sample{many} {s['sample_num']}  ·  {s['outcrop_name']} outcrop, {s['sample_location']}\n"
                   + (f"Sol {s['sol']}  ·  {s['date']}" if known else "Sol and date not yet published")
                   + (f"  ·  sample length {length:.1f} cm" if length > 0 else ""))
        print(f"Sample site: {name} (sol {sol})")
        picks = PHOTO_OVERRIDES.get(name)
        if picks is None and known:
            sols = [int(x) for x in re.findall(r"\d+", s["sol"])]   # try every sampling sol in turn
            haz = (first_of(pick_image(x, {"FRONT_HAZCAM_LEFT_A"}) for x in sols)
                   or first_of(pick_image(x, {"NAVCAM_LEFT"}, last=False) for x in sols))
            close = (first_of(pick_image(x, {"SHERLOC_WATSON"}) for x in sols)
                     or first_of(pick_image(x, {"NAVCAM_LEFT"}) for x in sols))
            if close == haz:
                close = None
            close_cam = "WATSON close-up" if close and close.startswith("SI") else "Navcam"
            picks = [(haz, "Front Hazcam: the rover at the sampling site on the sampling sol"),
                     (close, f"{close_cam}: taken at the site on the sampling sol")]
        points.append({
            "kind": "sample_site", "category": "Sample site", "name": name, "short_name": name,
            "activity": activity, "when": when, "subtitle": f"{s['sample_type']}{rock}", "details": details,
            "lon": s["lon"], "lat": s["lat"], "reveal_radius_m": 15,
            "photos": photos_for(slug(name), picks or []),
        })
    return points


def ingenuity_points(heli):
    flights = sorted((f["properties"] for f in heli["features"]), key=lambda p: p["Flight"])
    by_field = {}
    for p in flights:
        if p["Flight"] == 0:
            continue
        by_field.setdefault(p["ToAirfld"], []).append(p)
    last = flights[-1]
    points = []
    for field, landings in by_field.items():
        p = landings[-1]
        nums = [str(x["Flight"]) for x in landings]
        first = landings[0]
        flights_text = (f"flight {nums[0]}" if len(nums) == 1 else f"flights {', '.join(nums[:-1])} and {nums[-1]}")
        if field == "Wright Brothers Field":
            name, short, kind = "Wright Brothers Field", "Wright Brothers Field", "ingenuity_landmark"
            activity = ("Ingenuity lifted off here on April 19, 2021 (sol 58): the first powered, controlled flight "
                        "on another planet. Flights 1 to 4 took off from this airfield, named after the Wright "
                        "brothers' first flight on Earth; a scrap of the 1903 Wright Flyer's wing fabric rode on "
                        "the helicopter.")
            when = "Sol 58  ·  Apr 19, 2021"
            sol_for_photo = 58
        elif field == "Airfield Chi" and last["ToAirfld"] == field:
            name, short, kind = "Ingenuity's final airfield (Airfield Chi)", "Ingenuity (final)", "ingenuity_landmark"
            activity = (f"Ingenuity's {ordinal(last['Flight'])} and last flight ended here on "
                        f"{fp.tidy_date(last['Earth_Date'])} (sol {last['Sol']}). A rotor blade was damaged and the "
                        f"mission ended after {last['Flight']} flights, {last['Total_km']:.1f} km and "
                        f"{last['total_min']:.0f} minutes in the air, far beyond the 5 test flights it was built "
                        f"for. The team nicknamed the area Valinor Hills.")
            when = f"Sol {last['Sol']}  ·  {fp.tidy_date(last['Earth_Date'])}"
            sol_for_photo = last["Sol"]
        else:
            name, short, kind = f"Ingenuity {field}", field.replace("Airfield ", "Airfield "), "airfield"
            activity = f"Ingenuity landed here on {flights_text}."
            when = f"Sol {first['Sol']}  ·  {fp.tidy_date(first['Earth_Date'])}"
            sol_for_photo = None
        alt = max(x["Max_Alt_m"] for x in landings)
        details = (f"Landing site of {flights_text}  ·  max altitude {alt:.0f} m\n"
                   f"Distance flown by then: {p['Total_km']:.2f} km")
        print(f"Ingenuity: {name}")
        photos = []
        if sol_for_photo is not None:
            img = pick_image(sol_for_photo, {"HELI_RTE"}) or pick_image(sol_for_photo, {"HELI_NAV"})
            if img:
                photos = photos_for(slug(name), [(img, "Ingenuity's own camera on the flight sol")])
        points.append({
            "kind": kind, "category": "Ingenuity" if kind != "airfield" else "Ingenuity airfield",
            "name": name, "short_name": short, "activity": activity, "when": when,
            "subtitle": "Mars Helicopter Ingenuity", "details": details,
            "lon": p["Lon"], "lat": p["Lat"], "reveal_radius_m": 12 if kind == "airfield" else 25,
            "photos": photos,
        })
    return points


def rover_points(waypoints):
    pts = []
    landing = min(waypoints, key=lambda p: (p["sol"], p["site"], p["drive"]))
    print("Landmark: Octavia E. Butler Landing")
    pts.append({
        "kind": "landmark", "category": "Landmark", "name": "Octavia E. Butler Landing",
        "short_name": "Landing site",
        "activity": ("Perseverance touched down here on February 18, 2021 after the \"seven minutes of terror\" "
                     "descent on its sky crane. NASA named the site after science-fiction author Octavia E. Butler."),
        "when": "Sol 0  ·  Feb 18, 2021", "subtitle": "Mars 2020 landing site",
        "details": "The rover's first parking position after landing, from the mission waypoint data.",
        "lon": landing["lon"], "lat": landing["lat"], "reveal_radius_m": 25,
        "photos": photos_for("landing_site", [(pick_image(2, {"FRONT_HAZCAM_LEFT_A"}),
                                               "Front Hazcam: the landing site in the rover's first days")]),
    })
    overlook = rover_position_on(waypoints, 58)
    print("Landmark: Van Zyl Overlook")
    pts.append({
        "kind": "landmark", "category": "Landmark", "name": "Van Zyl Overlook", "short_name": "Van Zyl Overlook",
        "activity": ("Perseverance parked here to film Ingenuity's first flight at Wright Brothers Field, about "
                     "65 m away. Named after Jakob van Zyl, a JPL leader who helped make the mission possible."),
        "when": "Sol 58  ·  Apr 19, 2021", "subtitle": "Rover position during Ingenuity's first flight",
        "details": f"Rover position at the end of sol {overlook['sol']} (site {overlook['site']}, drive {overlook['drive']}).",
        "lon": overlook["lon"], "lat": overlook["lat"], "reveal_radius_m": 15, "photos": [],
    })
    latest = max(waypoints, key=lambda p: (p["sol"], p["site"], p["drive"]))
    print("Landmark: latest rover position")
    pts.append({
        "kind": "landmark", "category": "Landmark", "name": "Perseverance (latest position)",
        "short_name": f"Perseverance, sol {latest['sol']}",
        "activity": (f"Where Perseverance was at sol {latest['sol']}, the latest position in this map's data, "
                     f"after driving {latest['dist_total_m'] / 1000:.1f} km since landing, outside the crater rim."),
        "when": f"Sol {latest['sol']}", "subtitle": "Mars 2020 rover",
        "details": f"Site {latest['site']}, drive {latest['drive']}  ·  {latest['dist_total_m'] / 1000:.2f} km driven",
        "lon": latest["lon"], "lat": latest["lat"], "reveal_radius_m": 25, "photos": [],
    })
    return pts


# IAU-approved names inside the map (USGS Gazetteer), with a short note on each.
IAU_FEATURES = [
    ("Jezero", "Jezero crater", 77.6873, 18.4082, 47.5, "Crater",
     "A 45-50 km crater that held a lake about 3.5 billion years ago. Rivers built a delta on its western side, and "
     "it was chosen for Perseverance because those lake and delta rocks could preserve signs of ancient life. "
     "Named after a town in Bosnia and Herzegovina; 'jezero' means 'lake'."),
    ("Neretva Vallis", "Neretva Vallis", 77.20, 18.55, 17.0, "River valley",
     "The ancient river valley that cut through the crater's western rim and fed the lake, building the delta. "
     "Perseverance explored the channel's mouth in 2024, where it found the \"Cheyava Falls\" rock."),
    ("Una Vallis", "Una Vallis", 77.07, 18.29, 5.0, "River valley",
     "A small valley west of Jezero, named after the Una river in Bosnia and Herzegovina and Croatia."),
    ("Belva", "Belva crater", 77.38, 18.48, 0.9, "Crater",
     "A 0.9 km impact crater in the western delta. Its walls expose layers of delta sediment; Perseverance "
     "photographed them from the rim in 2023. Named after a town in West Virginia, USA."),
    ("Hartwell", "Hartwell crater", 77.72, 18.53, 1.7, "Crater",
     "A 1.7 km impact crater on the crater floor, named after a town in Georgia, USA."),
    ("Marysville", "Marysville crater", 77.18, 18.13, 1.85, "Crater",
     "A 1.9 km impact crater west of Jezero, named after a town in California, USA."),
    ("Sedona", "Sedona crater", 77.54, 17.84, 7.4, "Crater",
     "A 7.4 km impact crater south of Jezero, named after a town in Arizona, USA."),
    ("Nili Planum", "Nili Planum", 77.10, 18.60, 128.5, "Plain",
     "The plains around Jezero. A classical albedo name; the plain is much larger than this map."),
]


def iau_points(bounds):
    pts = []
    for iau, name, lon, lat, km, kind, text in IAU_FEATURES:
        if not in_bounds(lon, lat, bounds):
            continue
        print(f"IAU: {name}")
        pts.append({
            "kind": "landmark", "category": "Landmark", "name": name, "short_name": name,
            "activity": text, "when": "", "subtitle": f"{kind}  ·  IAU name \"{iau}\"",
            "details": f"Size about {km:g} km  ·  centre from the USGS Gazetteer of Planetary Nomenclature",
            "lon": lon, "lat": lat,
            # Big features reveal over a wider area (capped so they don't fill the HUD everywhere).
            "reveal_radius_m": max(30.0, min(km * 1000 * 0.25, 1500.0)), "photos": [],
        })
    return pts


def main():
    os.makedirs(fp.PHOTO_DIR, exist_ok=True)
    bounds = load(INFO)["bounds"]
    samples = [f["properties"] for f in load(os.path.join(SRC, "Sampling Locations.geojson"))["features"]]
    waypoints = [f["properties"] for f in load(os.path.join(SRC, "Rover Waypoints.geojson"))["features"]]
    heli = load(os.path.join(SRC, "m20_heli_waypoints.json"))

    points = rover_points(waypoints) + sample_points(samples) + ingenuity_points(heli) + iau_points(bounds)
    points = [p for p in points if in_bounds(p["lon"], p["lat"], bounds)]
    out = {"note": "Points of interest across the Jezero map (sampling sites outside Three Forks, Ingenuity, "
                   "landmarks, IAU features). Generated by Tools/PoiPhotos/make_jezero_pois.py",
           "points": points}
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1, ensure_ascii=False)
    kinds = {}
    for p in points:
        kinds[p["category"]] = kinds.get(p["category"], 0) + 1
    print("wrote", OUT, "-", len(points), "points:", kinds, "-", sum(len(p["photos"]) for p in points), "photos")


if __name__ == "__main__":
    main()
