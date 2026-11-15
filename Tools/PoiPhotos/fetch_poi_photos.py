"""
Downloads the real Perseverance photos used for the points of interest and writes
Assets/MarsData/POI/three_forks_poi.json, which the Unity "Build Points Of Interest" step reads.

Points of interest:
  * the 5 sampling sites inside the terrain (from MarsSources/Sampling Locations.geojson)
  * the 10 sample tubes dropped at the Three Forks depot (sols 653-687)

Photos come from NASA's public raw-image feed (https://mars.nasa.gov/mars2020/multimedia/raw-images/),
credit NASA/JPL-Caltech (Mastcam-Z and WATSON: NASA/JPL-Caltech/ASU and NASA/JPL-Caltech/MSSS). They are raw,
unprocessed frames: engineering cameras (Hazcam/Navcam) have a yellow-green cast (not colour-calibrated).

How the photos were chosen (by eye, from contact sheets of every frame on the relevant sols):
  * Sampling sites: sols when the rover was parked at the site (rover within ~2.5 m of the sample location in
    MarsSources/Rover Waypoints.geojson). Kukaklek's listed sampling sol is 631, but the rover had already
    driven off by then, so its photos are from sols 612-626 at the site. Photo 2 is a WATSON close-up of the
    abrasion patch (the rock ground flat for analysis); for the regolith sample it shows the regolith itself.
  * Depot tubes: CacheCam imaged a tube on exactly 10 sols at the depot (653, 655, 661, 666, 668, 672, 675, 680,
    682, 687), each while the rover was parked at a different spot. WATSON then photographed the tube on the
    ground under the rover and the Front Hazcam saw it after the rover backed away. The tube positions below are
    the rover's parking positions (Rover Waypoints.geojson), so they are approximate to ~1-2 m. Which sample
    each tube holds is not in this data, so the tubes are numbered in drop order.

Usage (from the project root):  python Tools/PoiPhotos/fetch_poi_photos.py
Needs: Python 3, curl on PATH, Pillow. Existing photos are not downloaded again.
"""
import json
import os
import re
import subprocess

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "MarsData", "POI")
PHOTO_DIR = os.path.join(OUT_DIR, "Photos")
SAMPLES = os.path.join(ROOT, "MarsSources", "Sampling Locations.geojson")
API = "https://mars.nasa.gov/rss/api/?feed=raw_images&category=mars2020&feedtype=json&sol={sol}"
RAW_PAGE = "https://mars.nasa.gov/mars2020/multimedia/raw-images/{id}"

# sample_name in the GeoJSON -> (file prefix, [(raw image id, caption), ...]). The first photo is shown first.
SAMPLE_SITES = {
    "Swift Run + Skyland": ("swift_run_skyland", [
        ("FLF_0490_0710451929_926ECM_N0261004FHAZ00203_07_195J", "Front Hazcam: the robotic arm at the outcrop"),
        ("SIF_0482_0709737440_375EBY_N0261004SRLC01034_0000LMJ", "WATSON close-up: the abrasion patch, rock ground flat for analysis"),
        ("NLF_0490_0710456117_926ECM_N0261004NCAM00709_04_095J", "Navcam: layered rock and wind ripples at the site"),
    ]),
    "Hazeltop + Bearwallow": ("hazeltop_bearwallow", [
        ("FLF_0509_0712138514_736ECM_N0261222FHAZ00206_01_295J", "Front Hazcam: the robotic arm at the outcrop"),
        ("SIF_0504_0711696775_578EBY_N0261222SRLC01034_0000LMJ", "WATSON close-up: the abrasion patch, rock ground flat for analysis"),
        ("NLF_0509_0712140650_691ECM_N0261222NCAM00709_01_095J", "Navcam: the outcrop with an abrasion patch and drill hole"),
    ]),
    "Shuyak + Mageik": ("shuyak_mageik", [
        ("FLF_0575_0717994928_099ECM_N0290000FHAZ00215_01_275J", "Front Hazcam: the robotic arm at the outcrop"),
        ("SIF_0568_0717381452_105EBY_N0290000SRLC01031_0000LMJ", "WATSON close-up: the abrasion patch, rock ground flat for analysis"),
        ("NLF_0575_0717997335_774ECM_N0290000NCAM00709_01_095J", "Navcam: the outcrop with drill holes"),
    ]),
    "Kukaklek": ("kukaklek", [
        ("FLF_0626_0722519102_441ECM_N0301172FHAZ00203_07_195J", "Front Hazcam: looking out from the sampling site"),
        ("SIF_0612_0721282153_601EBY_N0301172SRLC01028_0000LMJ", "WATSON close-up: the abrasion patch, rock ground flat for analysis"),
        ("FLF_0626_0722521052_479ECM_N0301172FHAZ02008_01_095J", "Front Hazcam: the outcrop with an abrasion patch"),
    ]),
    "Atmo Mountain + Crosswind Lake": ("atmo_mountain_crosswind_lake", [
        ("FLF_0634_0723236167_619ECM_N0310000FHAZ02267_01_195J", "Front Hazcam: the robotic arm over the sand"),
        ("SIF_0633_0723144210_632EBY_N0310000SRLC00746_0000LMJ", "WATSON close-up: the regolith (pebbles and sand) that was sampled"),
        ("FLF_0634_0723236167_619ECM_N0310000FHAZ02267_10_195J", "Front Hazcam: wheel scuff in the regolith"),
    ]),
}

# (drop sol, rover parking lon, lat, WATSON id, Front Hazcam id, hazcam caption)
DEPOT_TUBES = [
    (653, 77.407756, 18.452627, "SIF_0653_0724915290_933EBY_N0320148SRLC08040_0000LMJ", "FLF_0654_0725000545_237ECM_N0320160FHAZ08111_07_095J", None),
    (655, 77.407847, 18.452523, "SIF_0655_0725092075_902EBY_N0320250SRLC08040_0000LMJ", "FLF_0658_0725352351_005ECM_N0320262FHAZ08111_06_095J", None),
    (661, 77.408056, 18.452670, "SIF_0661_0725622430_519EBY_N0320378SRLC08040_0000LMJ", "FLF_0663_0725808410_441ECM_N0320390FHAZ08111_07_095J", None),
    (666, 77.408078, 18.452766, "SIF_0666_0726070752_851EBY_N0320482SRLC08040_0000LMJ", "FLF_0667_0726154941_130ECM_N0320494FHAZ08111_07_095J", None),
    (668, 77.408200, 18.452724, "SIF_0668_0726247809_582EBY_N0320580SRLC08040_0000LMJ", "FLF_0670_0726420719_881ECM_N0320592FHAZ08111_06_095J",
     "Front Hazcam: this tube came to rest standing on its end"),
    (672, 77.408244, 18.452813, "SIF_0672_0726603916_031EBY_N0320672SRLC08040_0000LMJ", "FLF_0674_0726775447_987ECM_N0320684FHAZ08111_06_095J", None),
    (675, 77.408336, 18.452728, "SIF_0675_0726866477_093EBY_N0320774SRLC08040_0000LMJ", "FLF_0678_0727130167_130ECM_N0320786FHAZ08111_06_095J", None),
    (680, 77.408505, 18.453005, "SIF_0680_0727311496_128EBY_N0320896SRLC08040_0000LMJ", "FLF_0681_0727399873_270ECM_N0320908FHAZ08111_06_095J", None),
    (682, 77.408601, 18.453066, "SIF_0682_0727493101_644EBY_N0321038SRLC08040_0000LMJ", "FLF_0684_0727664299_645ECM_N0321050FHAZ08111_07_095J", None),
    (687, 77.408447, 18.453372, "SIF_0690_0728205464_664EBY_N0321184SRLC08040_0000LMJ", "FLF_0693_0728459560_816ECM_N0321196FHAZ08111_06_095J", None),
]

CAMERA_NAMES = {
    "FRONT_HAZCAM_LEFT_A": "Front Hazcam (left)", "NAVCAM_LEFT": "Navcam (left)", "MCZ_LEFT": "Mastcam-Z (left)",
    "SHERLOC_WATSON": "WATSON",
}
CREDITS = {"MCZ_LEFT": "NASA/JPL-Caltech/ASU", "SHERLOC_WATSON": "NASA/JPL-Caltech/MSSS"}

_sol_cache = {}


def curl_json(url):
    out = subprocess.run(["curl", "-sL", "--max-time", "90", "--retry", "3", url], capture_output=True, check=True)
    return json.loads(out.stdout)


def trim_to_multiple_of_4(path):
    """Unity can only block-compress (BC7) textures whose sides are multiples of 4; otherwise it keeps them as
    uncompressed RGBA32 (4x the memory). Trim the 1-3 extra pixel rows/columns (centred)."""
    from PIL import Image
    with Image.open(path) as im:
        w, h = im.size
        nw, nh = w - w % 4, h - h % 4
        if (nw, nh) == (w, h):
            return
        left, top = (w - nw) // 2, (h - nh) // 2
        cropped = im.convert("RGB").crop((left, top, left + nw, top + nh))
    cropped.save(path, quality=95)


def fetch_photo(image_id, fname, caption):
    sol = int(image_id.split("_")[1])
    if sol not in _sol_cache:
        _sol_cache[sol] = {i["imageid"]: i for i in curl_json(API.format(sol=sol))["images"]}
    meta = _sol_cache[sol][image_id]
    path = os.path.join(PHOTO_DIR, fname)
    if not os.path.exists(path):
        subprocess.run(["curl", "-sL", "--max-time", "120", "--retry", "3", "-o", path,
                        meta["image_files"]["large"]], check=True)
        trim_to_multiple_of_4(path)
    cam = meta["camera"]["instrument"]
    print(f"{fname:40s} sol {sol}  {cam}")
    return {
        "file": f"Photos/{fname}", "caption": caption, "camera": CAMERA_NAMES.get(cam, cam), "sol": sol,
        "utc": meta["date_taken_utc"], "image_id": image_id, "source": RAW_PAGE.format(id=image_id),
        "credit": CREDITS.get(cam, "NASA/JPL-Caltech"),
    }


NUMBER_WORDS = {1: "one", 2: "two", 3: "three"}
MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]


def tidy_date(text):
    """'Jul. 7&12, 2022' -> 'Jul 7 & 12, 2022'."""
    return re.sub(r"\s*&\s*", " & ", text.replace(".", "")).strip()


def sols_text(text):
    return ("Sols " if "&" in text else "Sol ") + re.sub(r"\s*&\s*", " & ", text)


def earth_date_of_sol(sol):
    """Earth date (UTC) of any raw image taken on that sol, e.g. 'Dec 21, 2022'."""
    if sol not in _sol_cache:
        _sol_cache[sol] = {i["imageid"]: i for i in curl_json(API.format(sol=sol))["images"]}
    any_image = next(iter(_sol_cache[sol].values()))
    y, m, d = any_image["date_taken_utc"][:10].split("-")
    return f"{MONTHS[int(m) - 1]} {int(d)}, {y}"


def sample_activity(s):
    names = [n.strip() for n in s["sample_name"].split("+")]
    count = NUMBER_WORDS.get(len(names), str(len(names)))
    listed = names[0] if len(names) == 1 else ", ".join(names[:-1]) + " and " + names[-1]
    kind = s["sample_type"].lower()
    if kind == "rock core":
        what = f"Drilled {count} rock core{'s' if len(names) > 1 else ''}, {listed}, from the {s['outcrop_name']} outcrop"
    elif kind == "regolith":
        what = f"Scooped {count} regolith sample{'s' if len(names) > 1 else ''} (loose soil and pebbles), {listed}, near {s['outcrop_name']}"
    elif kind == "atmospheric":
        what = f"Sealed a sample of the Martian atmosphere, {listed}"
    else:
        what = f"Collected {listed} ({s['sample_type']})"
    rock = s["rock_type"]
    tail = "" if rock in ("", "n/a") else f" The material is {rock[0].lower() + rock[1:]}{'' if rock.lower().endswith('grains') else ' rock'}."
    return f"{what} and sealed {'them in sample tubes' if len(names) > 1 else 'it in a sample tube'}.{tail}"


def main():
    os.makedirs(PHOTO_DIR, exist_ok=True)
    samples = {f["properties"]["sample_name"]: f["properties"]
               for f in json.load(open(SAMPLES, encoding="utf-8"))["features"]}
    points = []
    for name, (prefix, photos) in SAMPLE_SITES.items():
        s = samples[name]
        rock = "" if s["rock_type"] in ("", "n/a") else f"  ·  {s['rock_type']}"
        many = "s" if "&" in s["sample_num"] else ""
        points.append({
            "kind": "sample_site", "category": "Sample site", "name": name, "short_name": name,
            "activity": sample_activity(s),
            "when": f"{sols_text(s['sol'])}  ·  {tidy_date(s['date'])}",
            "subtitle": f"{s['sample_type']}{rock}",
            "details": f"Sample{many} {s['sample_num']}  ·  {s['outcrop_name']} outcrop, {s['sample_location']}\n"
                       f"Sol {s['sol']}  ·  {s['date']}  ·  sample length {float(s['sample_ht_cm']):.1f} cm",
            "lon": s["lon"], "lat": s["lat"], "reveal_radius_m": 15,
            "photos": [fetch_photo(i, f"{prefix}_{n}.jpg", c) for n, (i, c) in enumerate(photos, 1)],
        })
    for n, (sol, lon, lat, watson, hazcam, hazcam_caption) in enumerate(DEPOT_TUBES, 1):
        ph = [fetch_photo(watson, f"depot_tube_{n:02d}_1.jpg", "WATSON: the tube on the ground under the rover, just after the drop"),
              fetch_photo(hazcam, f"depot_tube_{n:02d}_2.jpg", hazcam_caption or "Front Hazcam: the tube on the ground after the rover backed away")]
        points.append({
            "kind": "depot_tube", "category": "Depot tube", "name": f"Depot tube {n}", "short_name": f"Tube {n}",
            "activity": (f"Perseverance set sample tube {n} of 10 down here while building the Three Forks depot, "
                         "a backup set of samples for a future Mars Sample Return mission."),
            "when": f"Sol {sol}  ·  {earth_date_of_sol(sol)}",
            "subtitle": "Sample tube left at the Three Forks depot",
            "details": f"Dropped on sol {sol} ({n} of 10)  ·  photographed sol {ph[0]['sol']} ({ph[0]['utc'][:10]})\n"
                       f"Position: where the rover parked to drop it (about 1-2 m)",
            "lon": lon, "lat": lat, "reveal_radius_m": 4,
            "photos": ph,
        })
    out = {"note": "Real Perseverance raw images of the sampling sites and depot tubes. "
                   "Generated by Tools/PoiPhotos/fetch_poi_photos.py", "points": points}
    with open(os.path.join(OUT_DIR, "three_forks_poi.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1, ensure_ascii=False)
    print("wrote", os.path.join(OUT_DIR, "three_forks_poi.json"), "-", len(points), "points")


if __name__ == "__main__":
    main()
