"""
Builds the Unity heightmap + colour texture for a lon/lat box from the Mars 2020 CTX products, and converts the
existing overlay / rover-track data (stored as fractions of the previous map) to the new map.

Inputs (in MarsSources/, not in git; download links in MarsSources/README.md):
  M20_JezeroCrater_CTXDEM_20m.tif          elevation (m, Float32), 20 m/px, Mars 2020 frame
  M20_JezeroCrater_CTXortho_mosaic_5m.tif  greyscale CTX photo mosaic, 5 m/px, same frame
Reference for colour: the existing colourised HiRISE texture of the Three Forks map (old box), used to learn a
grey -> Mars colour mapping that is then applied to the whole CTX mosaic.

Outputs:
  Assets/MarsData/Heightmaps/<name>_height.png   16-bit, RES x RES, north up, min..max elevation -> 0..65535
  Assets/MarsData/Heightmaps/<name>_info.json    real size, elevation range, bounds
  Assets/MarsData/Textures/<name>_color.jpg      COLOR_RES x COLOR_RES, north up
  (with --convert-overlays) rewrites three_forks_overlays.json and rover_tracks.json positions for the new box

Map frame: equirectangular, sphere R = 3396190 m, true scale at the box's centre latitude.
Heightmap samples sit exactly on the box edges (sample 0 = west/south edge, sample RES-1 = east/north edge),
which is how Unity terrains sample heights.

Usage (from the project root):
  python Tools/RegionBuilder/build_region.py --name jezero_crater --west 77.0 --east 77.8 --south 17.7 --north 18.7 --convert-overlays
"""
import argparse
import json
import math
import os

import numpy as np
import osgeo
os.environ.setdefault("PROJ_DATA", os.path.join(os.path.dirname(osgeo.__file__), "data", "proj"))
from osgeo import gdal  # noqa: E402
from PIL import Image  # noqa: E402

gdal.UseExceptions()
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "MarsSources")
DEM = os.path.join(SRC, "M20_JezeroCrater_CTXDEM_20m.tif")
ORTHO = os.path.join(SRC, "M20_JezeroCrater_CTXortho_mosaic_5m.tif")
R = 3396190.0

# The previous map (Three Forks, 1.25 km): its bounds define what the old 0..1 fractions mean.
OLD_BOUNDS = dict(west=77.39670753, east=77.41902351, south=18.43816893, north=18.46422)
OLD_COLOR = os.path.join(ROOT, "Assets", "MarsData", "Textures", "jezero_color.png")
OVERLAYS = os.path.join(ROOT, "Assets", "MarsData", "Overlays", "three_forks_overlays.json")
TRACKS = os.path.join(ROOT, "Assets", "MarsData", "Overlays", "Tracks", "rover_tracks.json")


def frame(b):
    lat_ts = (b["south"] + b["north"]) / 2.0
    proj = f"+proj=eqc +lat_ts={lat_ts} +lat_0=0 +lon_0=0 +x_0=0 +y_0=0 +R={R} +units=m +no_defs"
    k = math.radians(1) * R
    x = lambda lon: lon * k * math.cos(math.radians(lat_ts))
    y = lambda lat: lat * k
    return proj, x, y


def warp(src, b, cols, rows, resample, nodata, corner_aligned):
    """Warp src to the box. corner_aligned=True puts sample centres on the box edges (Unity heightmap layout)."""
    proj, x, y = frame(b)
    x0, x1, y0, y1 = x(b["west"]), x(b["east"]), y(b["south"]), y(b["north"])
    if corner_aligned:
        dx, dy = (x1 - x0) / (cols - 1), (y1 - y0) / (rows - 1)
        bounds = [x0 - dx / 2, y0 - dy / 2, x1 + dx / 2, y1 + dy / 2]
    else:
        bounds = [x0, y0, x1, y1]
    ds = gdal.Warp("", src, format="MEM", outputBounds=bounds, dstSRS=proj, width=cols, height=rows,
                   resampleAlg=resample, dstNodata=nodata, multithread=True, warpMemoryLimit=2048)
    return ds


def build_heightmap(b, res, name):
    print(f"Heightmap {res}x{res} from CTX DEM ...")
    ds = warp(DEM, b, res, res, "cubic", -9999.0, corner_aligned=True)
    z = ds.GetRasterBand(1).ReadAsArray().astype(np.float64)
    bad = z < -9000
    if bad.any():
        print(f"  filling {bad.sum()} missing samples")
        band = ds.GetRasterBand(1)
        mask = gdal.GetDriverByName("MEM").Create("", res, res, 1, gdal.GDT_Byte)
        mask.GetRasterBand(1).WriteArray((~bad).astype(np.uint8))
        gdal.FillNodata(band, mask.GetRasterBand(1), maxSearchDist=200, smoothingIterations=2)
        z = band.ReadAsArray().astype(np.float64)
    zmin, zmax = float(z.min()), float(z.max())
    u16 = np.round((z - zmin) / (zmax - zmin) * 65535).astype(np.uint16)
    out = os.path.join(ROOT, "Assets", "MarsData", "Heightmaps", f"{name}_height.png")
    Image.fromarray(u16).save(out)          # row 0 = north (image top), as the generator expects
    print(f"  {zmin:.1f} .. {zmax:.1f} m  ->  {out}")
    return zmin, zmax


def build_color(b, res, name):
    print(f"Colour {res}x{res} from CTX ortho (5 m) ...")
    ds = warp(ORTHO, b, res, res, "average", 0, corner_aligned=False)
    g = ds.GetRasterBand(1).ReadAsArray().astype(np.float32)
    hole = g <= 0
    if hole.any():
        print(f"  filling {hole.mean() * 100:.2f}% blank pixels")
        band = ds.GetRasterBand(1)
        mask = gdal.GetDriverByName("MEM").Create("", res, res, 1, gdal.GDT_Byte)
        mask.GetRasterBand(1).WriteArray((~hole).astype(np.uint8))
        gdal.FillNodata(band, mask.GetRasterBand(1), maxSearchDist=400, smoothingIterations=3)
        g = band.ReadAsArray().astype(np.float32)

    # Learn grey -> RGB from the existing colourised Three Forks texture (same ground, Mars 2020 frame).
    lut = learn_lut()
    gi = np.clip(np.round(g), 0, 255).astype(np.uint8)
    rgb = lut[gi]
    out = os.path.join(ROOT, "Assets", "MarsData", "Textures", f"{name}_color.jpg")
    Image.fromarray(rgb).save(out, quality=92, optimize=True)
    print(f"  -> {out} ({os.path.getsize(out) / 1e6:.1f} MB)")


def learn_lut():
    ob = OLD_BOUNDS
    ref = np.asarray(Image.open(OLD_COLOR).convert("RGB"))
    n = 256                                             # compare at ~5 m (the CTX resolution)
    ref_small = np.asarray(Image.fromarray(ref).resize((n, int(n * 1544 / 1254)), Image.BOX)).astype(np.float32)
    ds = warp(ORTHO, ob, ref_small.shape[1], ref_small.shape[0], "average", 0, corner_aligned=False)
    g = ds.GetRasterBand(1).ReadAsArray().astype(np.float32)
    ok = g > 0
    gv, cv = g[ok], ref_small[ok]
    lut = np.zeros((256, 3), np.float32)
    lo, hi = np.percentile(gv, 0.5), np.percentile(gv, 99.5)
    centres, colours = [], []
    for v in np.linspace(lo, hi, 24):
        sel = np.abs(gv - v) < (hi - lo) / 24
        if sel.sum() > 30:
            centres.append(v); colours.append(np.median(cv[sel], axis=0))
    centres, colours = np.array(centres), np.array(colours)
    for c in range(3):                                  # linear fit per channel, extrapolates beyond Three Forks' range
        a, b0 = np.polyfit(centres, colours[:, c], 1)
        lut[:, c] = a * np.arange(256) + b0
    print(f"  colour mapping learned from {ok.sum()} pixel pairs: grey {lo:.0f}..{hi:.0f} -> "
          f"RGB {lut[int(lo)].round()} .. {lut[int(hi)].round()}")
    return np.clip(lut, 0, 255).astype(np.uint8)


def convert_fraction(fx, fy, b):
    ob = OLD_BOUNDS
    lon = ob["west"] + fx * (ob["east"] - ob["west"])
    lat = ob["south"] + fy * (ob["north"] - ob["south"])
    return (lon - b["west"]) / (b["east"] - b["west"]), (lat - b["south"]) / (b["north"] - b["south"])


def convert_overlays(b):
    for path in (OVERLAYS, TRACKS):
        d = json.load(open(path, encoding="utf-8"))
        if d.get("bounds") == b:
            print(f"  {os.path.basename(path)} already in this frame"); continue
        if "bounds" in d and d["bounds"] != OLD_BOUNDS:
            raise SystemExit(f"{path} is in an unexpected frame {d['bounds']}; refusing to convert")

        def conv_flat(pts, stride):
            out = list(pts)
            for i in range(0, len(out), stride):
                nx, ny = convert_fraction(out[i], out[i + 1], b)
                out[i], out[i + 1] = round(nx, 7), round(ny, 7)
            return out

        if path == OVERLAYS:
            for dr in d["drives"]: dr["points"] = conv_flat(dr["points"], 2)
            d["depot_zone"] = conv_flat(d["depot_zone"], 2)
            for s in d["samples"]: s["x"], s["y"] = [round(v, 7) for v in convert_fraction(s["x"], s["y"], b)]
        else:
            stride = d["segment_stride"]
            for r in d["ruts"]: r["segments"] = conv_flat(r["segments"], stride)
            for t in d["turns"]: t["x"], t["y"] = [round(v, 7) for v in convert_fraction(t["x"], t["y"], b)]
        d["bounds"] = b
        d["note"] = d.get("note", "") + f" Positions are fractions of the map bounds in 'bounds' (lon/lat, Mars 2020 frame)."
        json.dump(d, open(path, "w", encoding="utf-8"), separators=(",", ":"))
        print(f"  converted {os.path.basename(path)}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", required=True)
    ap.add_argument("--west", type=float, required=True); ap.add_argument("--east", type=float, required=True)
    ap.add_argument("--south", type=float, required=True); ap.add_argument("--north", type=float, required=True)
    ap.add_argument("--res", type=int, default=4097, help="heightmap size, 2^n+1 (Unity max 4097)")
    ap.add_argument("--color-res", type=int, default=8192)
    ap.add_argument("--convert-overlays", action="store_true")
    a = ap.parse_args()
    b = dict(west=a.west, east=a.east, south=a.south, north=a.north)
    _, x, y = frame(b)
    width_m, length_m = x(a.east) - x(a.west), y(a.north) - y(a.south)
    print(f"Box {b}: {width_m / 1000:.2f} km x {length_m / 1000:.2f} km")

    zmin, zmax = build_heightmap(b, a.res, a.name)
    build_color(b, a.color_res, a.name)
    info = {
        "name": a.name, "area": "Jezero crater (west half) and surroundings",
        "heightmap": f"Assets/MarsData/Heightmaps/{a.name}_height.png",
        "color": f"Assets/MarsData/Textures/{a.name}_color.jpg",
        "resolution": a.res, "width_m": round(width_m, 3), "length_m": round(length_m, 3),
        "sample_spacing_m": [round(width_m / (a.res - 1), 3), round(length_m / (a.res - 1), 3)],
        "min_elevation_m": round(zmin, 3), "max_elevation_m": round(zmax, 3), "height_range_m": round(zmax - zmin, 3),
        "bounds": b, "elevation_datum": "metres, Mars areoid (MOLA), as in the Mars 2020 CTX DEM",
        "source_dtm": "Mars 2020 Science Investigation CTX DEM Mosaic, 20 m (M20_JezeroCrater_CTXDEM_20m.tif)",
        "source_color": "Mars 2020 CTX ortho mosaic, 5 m (greyscale), colourised from the Three Forks HiRISE colour",
        "frame_note": f"Equirectangular, sphere R={R:.0f} m, true scale at latitude {(a.south + a.north) / 2}. "
                      "Heightmap samples lie on the box edges.",
    }
    path = os.path.join(ROOT, "Assets", "MarsData", "Heightmaps", f"{a.name}_info.json")
    json.dump(info, open(path, "w", encoding="utf-8"), indent=1)
    print("wrote", path)
    if a.convert_overlays:
        convert_overlays(b)


if __name__ == "__main__":
    main()
