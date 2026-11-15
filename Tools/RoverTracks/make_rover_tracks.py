"""
Builds Perseverance wheel-track data for Unity from the mission drive path (Mars 2020 GeoJSON).

Outputs (in Assets/MarsData/Overlays/Tracks/):
  rover_rut_albedo.png   - RGBA: dark tint + opacity of one wheel rut (tiles along the track)
  rover_rut_normal.png   - tangent-space normal map: rut depression, side berms, grouser grooves
  rover_tracks.json      - decal segments for every wheel rut and turn-in-place arc

Rover geometry (Perseverance):
  wheel diameter 0.525 m, 48 gently curved grousers  -> one grouser mark every pi*0.525/48 = 3.44 cm (published)
  wheel width ~0.40 m (Curiosity's value; Perseverance's are described as slightly narrower)  -> estimate
  rover width 2.7 m (published)  -> rut centre-to-centre ~2.3 m                              -> derived estimate
  front-rear axle spacing ~2.4 m (from the 3 m length)                                       -> estimate, only used for turns
Run:  python make_rover_tracks.py
"""
import json, math, os
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "MarsData", "Overlays", "Tracks")
INFO = os.path.join(ROOT, "Assets", "MarsData", "Heightmaps", "jezero_crater_info.json")   # the current map
DRIVES = os.path.join(ROOT, "MarsSources", "Rover Drive Path.geojson")

WHEEL_D, GROUSERS = 0.525, 48
WHEEL_W = 0.40
GAUGE = 2.30
WHEELBASE = 2.40
GROUSER_PITCH = math.pi * WHEEL_D / GROUSERS          # 0.0344 m
TILE_LEN = 12 * GROUSER_PITCH                         # texture repeats every 12 grousers (0.412 m)
DECAL_W = WHEEL_W + 0.10                              # rut plus berms on each side
RUT_DEPTH, BERM_H, GROOVE_D = 0.010, 0.004, 0.003      # metres (typical shallow Perseverance ruts in fine regolith)

# ------------------------------------------------------------------ 1. Texture: one rut, tiling along its length
def make_textures(size=512):
    u = (np.arange(size) + 0.5) / size * DECAL_W - DECAL_W / 2          # across (m), -0.25..0.25
    v = (np.arange(size) + 0.5) / size * TILE_LEN                        # along (m)
    U, V = np.meshgrid(u, v)                                             # rows = along (V), cols = across (U)
    half = WHEEL_W / 2
    inside = 1 / (1 + np.exp((np.abs(U) - half) / 0.004))               # soft rut edge
    h = -RUT_DEPTH * inside
    h += BERM_H * np.exp(-((np.abs(U) - half - 0.012) / 0.010) ** 2)     # pushed-up soil along the edges
    # Grouser grooves: gently curved bars across the tread, one every GROUSER_PITCH (periodic in V).
    bow = 0.010 * (U / half) ** 2                                        # ~1 cm curvature across the wheel
    phase = ((V - bow) / GROUSER_PITCH) % 1.0
    dist = np.minimum(phase, 1 - phase) * GROUSER_PITCH
    h -= GROOVE_D * np.exp(-(dist / 0.0045) ** 2) * inside
    rng = np.random.default_rng(7)                                       # fine regolith texture
    grain = gaussian_filter(rng.standard_normal((size, size)), 1.5, mode="wrap")
    h += 0.0006 * grain / grain.std()
    # Normal map (tangent space: +X across = decal U, +Y along = decal V).
    dpu, dpv = DECAL_W / size, TILE_LEN / size
    gx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) / (2 * dpu)
    gy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) / (2 * dpv)
    n = np.dstack([-gx, -gy, np.ones_like(h)]); n /= np.linalg.norm(n, axis=2, keepdims=True)
    normal = np.round((n * 0.5 + 0.5) * 255).astype(np.uint8)
    # Albedo: compacted soil a bit darker; grooves darker still (occlusion). Opacity fades out across the berms.
    groove_ao = np.exp(-(dist / 0.0045) ** 2) * inside
    alpha = 0.18 * inside + 0.08 * groove_ao + 0.03 * np.exp(-((np.abs(U) - half - 0.012) / 0.012) ** 2)
    rgba = np.dstack([np.full_like(h, 38), np.full_like(h, 29), np.full_like(h, 24), alpha * 255])
    os.makedirs(OUT, exist_ok=True)
    # Image row 0 is the top of the texture (V = 1 in Unity), so flip rows to keep V increasing upward.
    Image.fromarray(np.clip(np.round(rgba[::-1]), 0, 255).astype(np.uint8), "RGBA").save(os.path.join(OUT, "rover_rut_albedo.png"))
    Image.fromarray(normal[::-1], "RGB").save(os.path.join(OUT, "rover_rut_normal.png"))

# ------------------------------------------------------------------ 2. Geometry
def wrap(a): return (a + 180.0) % 360.0 - 180.0

def heading(p, q):  # degrees clockwise from north, p/q in metres (east, north)
    return math.degrees(math.atan2(q[0] - p[0], q[1] - p[1]))

def right_of(h):  # unit vector to the right of travel heading h
    r = math.radians(h); return np.array([math.cos(r), -math.sin(r)])

def clean(pts, min_step=0.25):
    out = [pts[0]]
    for p in pts[1:]:
        if np.hypot(*(p - out[-1])) >= min_step: out.append(p)
    return np.array(out)

def split_turns(pts, look=1.0, turn_deg=45.0, reverse_deg=150.0):
    """Split a cleaned polyline at turn-in-place points. Returns runs and turns [(point, h_in, h_out)]."""
    if len(pts) < 3: return [pts], []
    cum = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(pts, axis=0).T))])
    def head_back(i):
        j = i
        while j > 0 and cum[i] - cum[j] < look: j -= 1
        return heading(pts[j], pts[i]) if j < i else None
    def head_fwd(i):
        j = i
        while j < len(pts) - 1 and cum[j] - cum[i] < look: j += 1
        return heading(pts[i], pts[j]) if j > i else None
    cuts, turns, i = [], [], 1
    while i < len(pts) - 1:
        hb, hf = head_back(i), head_fwd(i)
        if hb is not None and hf is not None:
            d = wrap(hf - hb)
            if turn_deg <= abs(d) <= reverse_deg:
                # take the vertex with the largest local turn within the look window as the pivot
                cuts.append(i); turns.append((pts[i].copy(), hb, hf))
                while i < len(pts) - 1 and cum[i + 1] - cum[cuts[-1]] < look: i += 1
        i += 1
    runs, start = [], 0
    for c in cuts: runs.append(pts[start:c + 1]); start = c
    runs.append(pts[start:])
    return [r for r in runs if len(r) >= 2], turns

def smooth(pts, passes=2):
    p = pts.copy()
    for _ in range(passes):
        if len(p) < 3: break
        p[1:-1] = 0.25 * p[:-2] + 0.5 * p[1:-1] + 0.25 * p[2:]
    return p

JOIN_OVERLAP = 0.005   # metres each piece extends past its neighbours so no light gap shows at the joins

def segments_along(poly, max_len=1.5, max_bend=1.5, uv0=0.0, extra_width=None):
    """Cut a rut polyline into straight decal segments: [cx, cy, yaw, length, width, uvBias]."""
    segs, uv = [], uv0
    i = 0
    while i < len(poly) - 1:
        j, length = i + 1, np.hypot(*(poly[i + 1] - poly[i]))
        h0 = heading(poly[i], poly[i + 1])
        while j < len(poly) - 1:
            nl = length + np.hypot(*(poly[j + 1] - poly[j]))
            if nl > max_len or abs(wrap(heading(poly[j], poly[j + 1]) - h0)) > max_bend: break
            length, j = nl, j + 1
        a, b = poly[i], poly[j]; L = float(np.hypot(*(b - a)))
        if L > 0.02:
            w = DECAL_W + (extra_width[i] if extra_width is not None else 0.0)
            segs.append([float((a[0] + b[0]) / 2), float((a[1] + b[1]) / 2), heading(a, b), L + 2 * JOIN_OVERLAP, w,
                         (uv - JOIN_OVERLAP / TILE_LEN) % 1.0])
            uv += L / TILE_LEN
        i = j
    return segs, uv

def extend_ends(run, ahead=WHEELBASE / 2):
    """The drive path is the rover centre; its front wheels run ahead and rear wheels behind by half the wheelbase,
    so ruts start that far behind the first centre point and end that far past the last one."""
    def unit(a, b):
        v = b - a; n = np.hypot(*v); return v / n if n > 1e-9 else np.array([0.0, 0.0])
    k = min(len(run) - 1, 3)
    start = run[0] - unit(run[0], run[k]) * ahead
    end = run[-1] + unit(run[-1 - k], run[-1]) * ahead
    return np.vstack([start, run, end])

TAPER_PIECES, PIECE = 3, 0.1   # last 30 cm of each rut fades out in three 10 cm steps

def resample(poly, values, step):
    cum = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(poly, axis=0).T))])
    s = np.arange(0, cum[-1], step); s = np.append(s, cum[-1]) if cum[-1] - s[-1] > 1e-6 else s
    pts = np.column_stack([np.interp(s, cum, poly[:, 0]), np.interp(s, cum, poly[:, 1])])
    return pts, np.interp(s, cum, values)

def rut_segments(poly, ex):
    """Decal pieces for one rut: tapered ends (fade 0.25/0.5/0.75) and full-strength middle pieces."""
    dense, dex = resample(poly, ex, PIECE)
    n = len(dense)
    if n < 2 * TAPER_PIECES + 3:
        s, _ = segments_along(dense, extra_width=dex); return [x + [1.0] for x in s]
    out, uv = [], 0.0
    for k in range(TAPER_PIECES):                      # start taper
        s, uv = segments_along(dense[k:k + 2], extra_width=dex[k:k + 2], uv0=uv); out += [x + [(k + 1) / (TAPER_PIECES + 1)] for x in s]
    core0, core1 = TAPER_PIECES, n - 1 - TAPER_PIECES
    s, uv = segments_along(dense[core0:core1 + 1], max_bend=2.5, extra_width=dex[core0:core1 + 1], uv0=uv); out += [x + [1.0] for x in s]
    for k in range(TAPER_PIECES):                      # end taper
        a = core1 + k
        s, uv = segments_along(dense[a:a + 2], extra_width=dex[a:a + 2], uv0=uv); out += [x + [(TAPER_PIECES - k) / (TAPER_PIECES + 1)] for x in s]
    return out

def offtracking(run):
    """Extra rut width on curves: front/rear wheels track wider than the middle wheels (~ (wb/2)^2 / 2R)."""
    ex = np.zeros(len(run))
    for i in range(1, len(run) - 1):
        a, b, c = run[i - 1], run[i], run[i + 1]
        ab, bc, ca = np.hypot(*(b - a)), np.hypot(*(c - b)), np.hypot(*(a - c))
        area2 = abs((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]))
        if area2 > 1e-9:
            R = ab * bc * ca / (2 * area2)
            ex[i] = min((WHEELBASE / 2) ** 2 / (2 * max(R, GAUGE / 2 + 0.3)), 0.25)
    ex[0], ex[-1] = ex[1] if len(ex) > 2 else 0, ex[-2] if len(ex) > 2 else 0
    # Smooth along the run so the rut widens and narrows gradually (no steps between decal pieces).
    if len(ex) >= 5:
        k = np.array([1, 2, 3, 2, 1], float); k /= k.sum()
        ex = np.convolve(np.pad(ex, 2, mode="edge"), k, mode="valid")
    return ex

def turn_arcs(p, h_in, h_out, step=0.2):
    """Rolling arcs of all six wheels while the rover rotates about its centre p from h_in to h_out."""
    d = wrap(h_out - h_in); arcs = []
    for lx in (-GAUGE / 2, GAUGE / 2):
        for ly in (WHEELBASE / 2, 0.0, -WHEELBASE / 2):
            r = math.hypot(lx, ly); n = max(2, int(math.ceil(abs(math.radians(d)) * r / step)) + 1)
            pts = []
            for k in range(n):
                h = h_in + d * k / (n - 1); hr = math.radians(h)
                fwd = np.array([math.sin(hr), math.cos(hr)]); rgt = right_of(h)
                pts.append(p + rgt * lx + fwd * ly)
            arcs.append(np.array(pts))
    return arcs

TURN_TILE_M = 2 * (math.hypot(GAUGE / 2, WHEELBASE / 2) + DECAL_W / 2) + 0.2   # square covering all six wheel arcs (~4.0 m)
TURN_TILE_PX = 256
TURN_BIN_DEG = 5.0                                    # turns share atlas tiles by angle, rounded to this

def make_turn_atlas(turns):
    """Draw each turn-in-place (all six wheels rolling around the rover centre) into one tile of an atlas.
    Tile frame = rover frame at the start heading: +x right, +y forward (texture up). Wheels partly skid in a
    point turn, so the arcs are scuffed ruts without crisp grouser marks."""
    n = len(turns); cols = 16; rows = max(1, int(math.ceil(n / cols)))
    rows = 1 << (rows - 1).bit_length()                                     # power of two -> compressible with mipmaps
    T = TURN_TILE_PX
    albedo = np.zeros((rows * T, cols * T, 4), np.float64); normal = np.zeros((rows * T, cols * T, 3), np.float64)
    normal[..., 2] = 1.0
    c = (np.arange(T) + 0.5) / T * TURN_TILE_M - TURN_TILE_M / 2
    X, Y = np.meshgrid(c, -c)                                               # row 0 = +y (top of the image = forward)
    rho, phi = np.hypot(X, Y), np.degrees(np.arctan2(X, Y))                 # phi clockwise from forward
    rng = np.random.default_rng(11)
    half = WHEEL_W / 2
    for i, (h_in, h_out) in enumerate(turns):
        d = wrap(h_out - h_in)
        dep = np.zeros_like(X); berm = np.zeros_like(X)
        for lx in (-GAUGE / 2, GAUGE / 2):
            for ly in (WHEELBASE / 2, 0.0, -WHEELBASE / 2):
                r = math.hypot(lx, ly); phi0 = math.degrees(math.atan2(lx, ly))
                rel = wrap(phi - phi0) if d >= 0 else -wrap(phi - phi0)          # angle travelled along this wheel's arc
                sweep = abs(d)
                # distance (m) from the arc's angular range (0 inside), so arc ends are rounded like a tyre footprint
                ang_out = np.where(rel < 0, -rel, np.where(rel > sweep, rel - sweep, 0.0))
                along = np.radians(ang_out) * r
                dr = np.abs(rho - r)
                dist = np.hypot(np.maximum(dr - half, 0), along)
                inside = np.where((dr <= half) & (along <= 0), 1.0, np.exp(-(dist / 0.02) ** 2) * (dr <= half + 0.02))
                dep = np.maximum(dep, inside)
                berm = np.maximum(berm, np.exp(-((dr - half - 0.012) / 0.012) ** 2) * (along < 0.15))
        berm *= (1 - dep)
        scuff = gaussian_filter(rng.standard_normal(X.shape), 1.2)
        h = -RUT_DEPTH * dep + BERM_H * berm + 0.0012 * scuff / scuff.std() * dep + 0.0005 * scuff / scuff.std()
        px = TURN_TILE_M / T
        gx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) / (2 * px)
        gy = -(np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) / (2 * px)   # rows go down the image, +y goes up
        nrm = np.dstack([-gx, -gy, np.ones_like(h)]); nrm /= np.linalg.norm(nrm, axis=2, keepdims=True)
        a = np.clip(0.16 * dep + 0.03 * berm, 0, 1)
        r0, c0 = (i // cols) * T, (i % cols) * T
        albedo[r0:r0 + T, c0:c0 + T] = np.dstack([np.full_like(h, 38), np.full_like(h, 29), np.full_like(h, 24), a * 255])
        normal[r0:r0 + T, c0:c0 + T] = nrm
    # Atlas row 0 (top of the image) holds tiles 0..cols-1; Unity's V runs upward, which the C# side accounts for.
    Image.fromarray(np.clip(np.round(albedo), 0, 255).astype(np.uint8), "RGBA").save(os.path.join(OUT, "rover_turn_atlas_albedo.png"))
    Image.fromarray(np.round((normal * 0.5 + 0.5) * 255).astype(np.uint8), "RGB").save(os.path.join(OUT, "rover_turn_atlas_normal.png"))
    return cols, rows

def main():
    make_textures()
    info = json.load(open(INFO))
    if "bounds" in info:
        b = info["bounds"]; (LON0, LON1), (LAT0, LAT1) = (b["west"], b["east"]), (b["south"], b["north"])
    else:
        (LON0, LON1), (LAT0, LAT1) = info["lon_range_east"], info["lat_range_north"]
    W, L = info["width_m"], info["length_m"]
    to_m = lambda lon, lat: np.array([(lon - LON0) / (LON1 - LON0) * W, (lat - LAT0) / (LAT1 - LAT0) * L])
    inside = lambda q: -5 <= q[0] <= W + 5 and -5 <= q[1] <= L + 5
    feats = json.load(open(DRIVES, encoding="utf-8"))["features"]
    ruts, arcs_out, stats = [], [], dict(drives=0, runs=0, turns=0, segments=0)
    prev_end = None
    for f in feats:
        pts = np.array([to_m(c[0], c[1]) for c in f["geometry"]["coordinates"]])
        if not any(inside(q) for q in pts): prev_end = None; continue
        pts = clean(pts)
        if len(pts) < 2: continue
        stats["drives"] += 1
        # turn in place between consecutive drives (end of the previous drive = start of this one)
        if prev_end is not None and np.hypot(*(pts[0] - prev_end[0])) < 0.5 and len(pts) >= 2:
            h_out = heading(pts[0], pts[min(len(pts) - 1, 2)])
            if 45 <= abs(wrap(h_out - prev_end[1])) <= 150:
                arcs_out.append((pts[0].copy(), prev_end[1], h_out, f["properties"]["sol"])); stats["turns"] += 1
        runs, turns = split_turns(pts)
        for (p, hi, ho) in turns:
            arcs_out.append((p, hi, ho, f["properties"]["sol"])); stats["turns"] += 1
        for run in runs:
            run = smooth(run)
            if len(run) < 2: continue
            run = extend_ends(run)
            stats["runs"] += 1
            # per-vertex heading (central differences) -> offset left/right wheel ruts
            hd = [heading(run[max(i - 1, 0)], run[min(i + 1, len(run) - 1)]) for i in range(len(run))]
            ex = offtracking(run)
            for side in (-1, 1):
                poly = np.array([run[i] + right_of(hd[i]) * side * GAUGE / 2 + right_of(hd[i]) * side * ex[i] / 2 for i in range(len(run))])
                s = rut_segments(poly, ex)
                ruts.append(dict(sol=f["properties"]["sol"], side="right" if side > 0 else "left", segments=s)); stats["segments"] += len(s)
        prev_end = (pts[-1], heading(pts[max(0, len(pts) - 3)], pts[-1]))
    def norm(groups):
        kept = []
        for g in groups:
            # keep only pieces whose centre lies on the terrain (drives that cross the edge are clipped)
            segs = [s for s in g["segments"] if 0.3 <= s[0] <= W - 0.3 and 0.3 <= s[1] <= L - 0.3]
            if not segs: continue
            g["segments"] = [round(v, 5) for s in segs for v in (s[0] / W, s[1] / L, s[2], s[3], s[4], s[5], s[6])]
            kept.append(g)
        return kept
    turns_kept = [t for t in arcs_out if 2 <= t[0][0] <= W - 2 and 2 <= t[0][1] <= L - 2]
    # A turn's arcs depend only on its turn angle, so turns share atlas tiles by angle rounded to TURN_BIN_DEG.
    # (One tile per turn would need a 4096 x 16384 atlas for the full traverse.)
    bins = sorted({int(round(wrap(t[2] - t[1]) / TURN_BIN_DEG)) for t in turns_kept})
    tile_of = {b: i for i, b in enumerate(bins)}
    cols, rows = make_turn_atlas([(0.0, b * TURN_BIN_DEG) for b in bins])
    turn_list = [dict(sol=t[3], x=round(float(t[0][0] / W), 6), y=round(float(t[0][1] / L), 6), heading=round(float(t[1]), 3),
                      turn_deg=round(float(wrap(t[2] - t[1])), 2), tile=tile_of[int(round(wrap(t[2] - t[1]) / TURN_BIN_DEG))])
                 for t in turns_kept]
    data = dict(note="Each rut segment = 7 numbers: centre x (0..1 west->east), centre y (0..1 south->north), heading (deg clockwise from north), "
                     "length (m), width (m), uvBias (texture offset along the rut), fade (1 = full, lower at rut ends). Each turn = one decal "
                     "using a tile of the turn atlas, drawn in the rover frame at its starting heading.",
                segment_stride=7,
                tile_length_m=TILE_LEN, wheel_width_m=WHEEL_W, gauge_m=GAUGE, wheelbase_m=WHEELBASE, grouser_pitch_m=GROUSER_PITCH,
                turn_tile_size_m=TURN_TILE_M, turn_atlas_cols=cols, turn_atlas_rows=rows,
                ruts=norm(ruts), turns=turn_list,
                bounds=dict(west=LON0, east=LON1, south=LAT0, north=LAT1))
    stats["segments"] = sum(len(g["segments"]) // 6 for g in data["ruts"]); stats["turn_decals"] = len(turn_list)
    json.dump(data, open(os.path.join(OUT, "rover_tracks.json"), "w"), separators=(",", ":"))
    print(json.dumps(stats), f"| grouser pitch {GROUSER_PITCH * 100:.2f} cm, texture tile {TILE_LEN:.3f} m")

if __name__ == "__main__":
    main()
