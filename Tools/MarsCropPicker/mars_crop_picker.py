"""
Mars Crop Picker - choose an area of the HiRISE Jezero DTM interactively and export it for Unity.

    Click or drag on the map   place the square (its centre follows the mouse)
    Area size slider           size of the square in metres
    Unity resolution           heightmap resolution of the exported terrain (2^n + 1)
    Elevation / Photo          switch the map and preview between shaded elevation and the orthoimage
    T key                      toggle Elevation / Photo
    Export to Unity            writes <name>_height.png (16-bit) and <name>_color.png (8-bit) into
                               Assets/MarsData/Heightmaps and Assets/MarsData/Textures, plus <name>_info.json

The square turns green when it is fully inside valid DTM data and red when it contains no-data.
The magnifier/pan buttons in the toolbar zoom the map; turn them off again to place the square.

Run it with run_picker.bat (double-click), or:  python mars_crop_picker.py [--dtm FILE] [--ortho FILE]
"""
import argparse
import glob
import importlib.util
import json
import os
import re
import sys

import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RAW = os.path.join(ROOT, "MarsSources")  # source data lives outside Assets so Unity doesn't import it
DEFAULT_DTM = os.path.join(RAW, "DTEEC_045994_1985_046060_1985_U01.IMG")
DEFAULT_ORTHO = os.path.join(RAW, "ESP_046060_1985_RED_C_01_ORTHO.JP2")
DEFAULT_OUT = os.path.join(ROOT, "Assets", "MarsData")

# The area already used in Unity (exported before this tool existed).
KNOWN_AREAS = {"jezero": (-2618.5, 1093377.5, -1593.5, 1094402.5)}
RESOLUTIONS = ["513", "1025", "2049", "4097"]


def ensure_proj_data():
    """GDAL wheels ship PROJ's database inside the osgeo package; point PROJ at it before GDAL loads."""
    if os.environ.get("PROJ_DATA"):
        return
    spec = importlib.util.find_spec("osgeo")
    if spec and spec.submodule_search_locations:
        path = os.path.join(list(spec.submodule_search_locations)[0], "data", "proj")
        if os.path.isdir(path):
            os.environ["PROJ_DATA"] = path


ensure_proj_data()
from osgeo import gdal, osr  # noqa: E402

gdal.UseExceptions()


# ------------------------------------------------------------------------------------------ imaging helpers

def hillshade(z, px, exag, az=315, alt=40):
    zf = np.where(np.isnan(z), np.nanmedian(z), z) * exag
    gy, gx = np.gradient(zf, px)
    n = np.dstack([-gx, gy, np.ones_like(zf)])  # gradient rows run south, so north slope is -gy
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    a, e = np.radians(az), np.radians(alt)
    light = np.array([np.sin(a) * np.cos(e), np.cos(a) * np.cos(e), np.sin(e)])
    return np.clip(n @ light, 0, 1)


def color_relief(z, px, exag, vmin, vmax):
    import matplotlib.pyplot as plt
    from matplotlib.colors import Normalize
    rgb = plt.get_cmap("viridis")(Normalize(vmin, vmax)(z))[..., :3]
    rgb = rgb * (0.35 + 0.75 * hillshade(z, px, exag)[..., None])
    return np.dstack([np.clip(rgb, 0, 1), ~np.isnan(z)])


def stretch(a):
    vals = a[~np.isnan(a)]
    if vals.size == 0:
        return np.zeros(a.shape + (4,))
    lo, hi = np.percentile(vals, [0.5, 99.5])
    g = np.clip((a - lo) / max(hi - lo, 1e-6), 0, 1)
    return np.dstack([g, g, g, ~np.isnan(a)])


# ------------------------------------------------------------------------------------------ data access

class MarsData:
    def __init__(self, dtm_path, ortho_path):
        print("Opening", os.path.basename(dtm_path))
        self.dtm = gdal.Open(dtm_path)
        self.ort = gdal.Open(ortho_path)
        self.gt = gt = self.dtm.GetGeoTransform()
        self.W, self.H = self.dtm.RasterXSize, self.dtm.RasterYSize
        self.nd = self.dtm.GetRasterBand(1).GetNoDataValue()
        if (self.ort.RasterXSize, self.ort.RasterYSize) != (self.W, self.H) or \
                abs(self.ort.GetGeoTransform()[1] - gt[1]) > 1e-9:
            sys.exit("The orthoimage is not on the same pixel grid as the DTM; this tool expects a matching DTM/ortho pair.")

        # The ortho's header origin and CRS differ from the DTM's by a metadata quirk, but the pixels share
        # one grid. Give the ortho the DTM's georeferencing (in memory) so both crop to the same map window.
        self.ort_fixed = gdal.Translate("/vsimem/ortho_on_dtm_grid.vrt", self.ort, format="VRT",
                                        outputSRS=self.dtm.GetProjection(),
                                        outputBounds=[gt[0], gt[3], gt[0] + self.W * gt[1], gt[3] + self.H * gt[5]])

        srs = osr.SpatialReference(self.dtm.GetProjection())
        srs.SetAxisMappingStrategy(osr.OAMS_TRADITIONAL_GIS_ORDER)
        geo = srs.CloneGeogCS()
        geo.SetAxisMappingStrategy(osr.OAMS_TRADITIONAL_GIS_ORDER)
        self.to_lonlat = osr.CoordinateTransformation(srs, geo)

        print("Loading full-resolution DTM and photo into memory (a few seconds)...")
        z = self.dtm.GetRasterBand(1).ReadAsArray().astype(np.float32)
        z[(z < -1e30) | (z == self.nd)] = np.nan
        self.z = z
        self.o = self.ort.GetRasterBand(1).ReadAsArray()

    # Map <-> full-resolution pixel coordinates
    @property
    def bounds(self):
        gt = self.gt
        return gt[0], gt[3] + self.H * gt[5], gt[0] + self.W * gt[1], gt[3]  # xmin, ymin, xmax, ymax

    def pixel_window(self, te):
        gt = self.gt
        c0 = int(np.floor((te[0] - gt[0]) / gt[1]))
        c1 = int(np.ceil((te[2] - gt[0]) / gt[1]))
        r0 = int(np.floor((gt[3] - te[3]) / gt[1]))
        r1 = int(np.ceil((gt[3] - te[1]) / gt[1]))
        return max(c0, 0), max(r0, 0), min(c1, self.W), min(r1, self.H)

    def overview(self, f):
        w, h = int(self.W / f), int(self.H / f)
        z = gdal.Warp("", self.dtm, format="MEM", width=w, height=h, srcNodata=self.nd, dstNodata=np.nan,
                      outputType=gdal.GDT_Float32, resampleAlg="average").ReadAsArray().astype(float)
        z[(z < -1e30) | (z == self.nd)] = np.nan
        o = gdal.Warp("", self.ort, format="MEM", width=w, height=h, srcNodata=0, dstNodata=0,
                      outputType=gdal.GDT_Float32, resampleAlg="average").ReadAsArray().astype(float)
        o[o <= 0] = np.nan
        return z, o

    def stats(self, te, preview_px=480):
        c0, r0, c1, r1 = self.pixel_window(te)
        z = self.z[r0:r1, c0:c1]
        o = self.o[r0:r1, c0:c1]
        nodata = int(np.count_nonzero(np.isnan(z)))
        step = max(1, int(np.ceil(max(z.shape) / preview_px)))
        zs = z[::step, ::step]
        s = {"nodata": nodata, "photo_black_pct": 100.0 * np.count_nonzero(o == 0) / max(o.size, 1)}
        if nodata < z.size:
            s["zmin"], s["zmax"] = float(np.nanmin(z)), float(np.nanmax(z))
            gy, gx = np.gradient(np.where(np.isnan(zs), np.nanmean(zs), zs), self.gt[1] * step)
            s["slope"] = float(np.degrees(np.arctan(np.hypot(gx, gy))).mean())
            s["slope_max"] = float(np.degrees(np.arctan(np.hypot(gx, gy))).max())
        s["preview_z"] = zs.astype(float)
        os_ = o[::step, ::step].astype(float)
        os_[os_ == 0] = np.nan
        s["preview_o"] = os_
        s["preview_px"] = self.gt[1] * step
        return s

    def lonlat(self, x, y):
        lon, lat, _ = self.to_lonlat.TransformPoint(x, y)
        return lon, lat

    def export(self, te, res, name, out_root):
        size = te[2] - te[0]
        pixel = size / res  # output pixel size in metres
        down = pixel > self.gt[1] * 1.05
        common = dict(format="MEM", outputBounds=list(te), width=res, height=res, multithread=True,
                      outputType=gdal.GDT_Float32)
        z = gdal.Warp("", self.dtm, resampleAlg="average" if down else "bilinear", srcNodata=self.nd,
                      dstNodata=self.nd, **common).ReadAsArray().astype(np.float64)
        o = gdal.Warp("", self.ort_fixed, resampleAlg="average" if down else "cubic", srcNodata=0, dstNodata=0,
                      **common).ReadAsArray()
        bad = int(np.count_nonzero((z == self.nd) | (z < -1e30) | ~np.isfinite(z)))
        if bad:
            raise ValueError(f"the exported area contains {bad} no-data pixels")

        from PIL import Image
        zmin, zmax = float(z.min()), float(z.max())
        h16 = np.round((z - zmin) / max(zmax - zmin, 1e-6) * 65535).astype(np.uint16)
        o8 = np.clip(np.round(o), 0, 255).astype(np.uint8)

        hdir = os.path.join(out_root, "Heightmaps")
        tdir = os.path.join(out_root, "Textures")
        os.makedirs(hdir, exist_ok=True)
        os.makedirs(tdir, exist_ok=True)
        hpath = os.path.join(hdir, f"{name}_height.png")
        cpath = os.path.join(tdir, f"{name}_color.png")
        overwrote = os.path.exists(hpath) or os.path.exists(cpath)
        Image.fromarray(h16).save(hpath)  # 16-bit grayscale, row 0 = north
        Image.fromarray(o8).save(cpath)   # 8-bit grayscale, row 0 = north

        cx, cy = (te[0] + te[2]) / 2, (te[1] + te[3]) / 2
        lon, lat = self.lonlat(cx, cy)
        info = {
            "name": name,
            "heightmap": os.path.relpath(hpath, ROOT).replace("\\", "/"),
            "color": os.path.relpath(cpath, ROOT).replace("\\", "/"),
            "resolution": res,
            "width_m": size, "length_m": size,
            "min_elevation_m": round(zmin, 3), "max_elevation_m": round(zmax, 3),
            "height_range_m": round(zmax - zmin, 3),
            "map_window_xmin_ymin_xmax_ymax": [round(v, 3) for v in te],
            "center_lat": round(lat, 6), "center_lon_east": round(lon, 6),
            "source_dtm": os.path.basename(self.dtm.GetDescription()),
            "source_ortho": os.path.basename(self.ort.GetDescription()),
        }
        with open(os.path.join(hdir, f"{name}_info.json"), "w") as f:
            json.dump(info, f, indent=2)
        info["overwrote"] = overwrote
        return info


# ------------------------------------------------------------------------------------------ user interface

class Picker:
    F = 4  # overview decimation (about 4 m per screen pixel at full zoom)

    def __init__(self, data, out_root, headless=False):
        import matplotlib.pyplot as plt
        from matplotlib.patches import Rectangle
        from matplotlib.widgets import Button, RadioButtons, Slider, TextBox

        self.plt = plt
        self.d = data
        self.out_root = out_root
        self.headless = headless
        gt = data.gt
        self.scale = 1 / (gt[1] * self.F)  # overview pixels per metre

        print("Building overview...")
        z, o = data.overview(self.F)
        self.coarse_valid = ~np.isnan(z)
        self.vmin, self.vmax = float(np.nanmin(z)), float(np.nanmax(z))
        self.img = {"Elevation": color_relief(z, gt[1] * self.F, 3, self.vmin, self.vmax), "Photo": stretch(o)}

        self.view = "Elevation"
        self.size_m = 1025.0
        self.res = 1025
        known = KNOWN_AREAS["jezero"]
        self.center = [(known[0] + known[2]) / 2 + 1500, (known[1] + known[3]) / 2]
        self.dragging = False
        self.stats = None
        self.message = ""

        bg, fg = "#151515", "#e8e8e8"
        plt.rcParams.update({"text.color": fg, "axes.labelcolor": fg, "xtick.color": fg, "ytick.color": fg})
        self.fig = fig = plt.figure(figsize=(15.5, 9.6), facecolor=bg)
        try:
            fig.canvas.manager.set_window_title("Mars Crop Picker - Jezero HiRISE DTM")
        except Exception:
            pass

        self.ax_map = fig.add_axes([0.005, 0.03, 0.40, 0.93], facecolor=bg)
        self.ax_prev = fig.add_axes([0.42, 0.40, 0.33, 0.55], facecolor=bg)
        self.ax_info = fig.add_axes([0.765, 0.40, 0.23, 0.55], facecolor=bg)
        for ax in (self.ax_map, self.ax_prev, self.ax_info):
            ax.set_xticks([]), ax.set_yticks([])
            for s in ax.spines.values():
                s.set_color("#444")
        self.ax_info.set_axis_off()

        self.map_im = self.ax_map.imshow(self.img[self.view], interpolation="lanczos")
        self.ax_map.set_title("Click or drag to place the area", color=fg, fontsize=11)

        # Previously exported areas (dashed outlines).
        for nm, te in self.known_areas().items():
            x0, y0 = self.to_disp(te[0], te[3])
            s = (te[2] - te[0]) * self.scale
            self.ax_map.add_patch(Rectangle((x0, y0), s, s, fill=False, ec="#ffd400", lw=1.2, ls="--"))
            self.ax_map.text(x0, y0 - 6, nm, color="#ffd400", fontsize=8, va="bottom")

        self.rect = Rectangle((0, 0), 1, 1, fill=False, ec="#33ff66", lw=2.2)
        self.ax_map.add_patch(self.rect)
        self.prev_im = self.ax_prev.imshow(np.zeros((2, 2, 4)), interpolation="lanczos")
        self.info_txt = self.ax_info.text(0, 1, "", va="top", ha="left", family="monospace", fontsize=9.5, color=fg)

        # Widgets
        wbg = "#262626"
        self.radio_view = RadioButtons(fig.add_axes([0.42, 0.20, 0.10, 0.10], facecolor=wbg), ["Elevation", "Photo"])
        fig.text(0.42, 0.305, "Map view", color=fg, fontsize=9)
        self.radio_res = RadioButtons(fig.add_axes([0.54, 0.15, 0.10, 0.15], facecolor=wbg), RESOLUTIONS, active=1)
        fig.text(0.54, 0.305, "Unity resolution (px)", color=fg, fontsize=9)
        self.slider = Slider(fig.add_axes([0.50, 0.095, 0.30, 0.03], facecolor=wbg), "Area size (m)",
                             200, 4000, valinit=self.size_m, valstep=5, color="#33aa55")
        self.textbox = TextBox(fig.add_axes([0.50, 0.03, 0.17, 0.045]), "Name ", initial="jezero_area2",
                               color=wbg, hovercolor="#333")
        self.button = Button(fig.add_axes([0.69, 0.03, 0.11, 0.045]), "Export to Unity", color="#2d6a3e",
                             hovercolor="#3c8a52")
        for w in (self.radio_view, self.radio_res):
            for lbl in w.labels:
                lbl.set_color(fg)
        self.slider.label.set_color(fg)
        self.slider.valtext.set_color(fg)
        self.textbox.label.set_color(fg)
        self.textbox.text_disp.set_color(fg)
        fig.text(0.67, 0.165, "Green = fully inside valid data\nRed = contains no-data (can't export)\n"
                              "Dashed yellow = already exported\nT = toggle view  |  zoom tool: toolbar",
                 color="#aaaaaa", fontsize=8.5, va="center")

        self.radio_view.on_clicked(self.on_view)
        self.radio_res.on_clicked(self.on_res)
        self.slider.on_changed(self.on_size)
        self.button.on_clicked(self.on_export)
        fig.canvas.mpl_connect("button_press_event", self.on_press)
        fig.canvas.mpl_connect("motion_notify_event", self.on_motion)
        fig.canvas.mpl_connect("button_release_event", self.on_release)
        fig.canvas.mpl_connect("key_press_event", self.on_key)

        self.update_box(exact=True)

    # ------------------------------------------------------------------ geometry
    def known_areas(self):
        areas = dict(KNOWN_AREAS)
        for path in glob.glob(os.path.join(self.out_root, "Heightmaps", "*_info.json")):
            try:
                with open(path) as f:
                    info = json.load(f)
                areas[info["name"]] = tuple(info["map_window_xmin_ymin_xmax_ymax"])
            except Exception:
                pass
        return areas

    def to_disp(self, x, y):
        gt = self.d.gt
        return (x - gt[0]) * self.scale, (gt[3] - y) * self.scale

    def from_disp(self, px, py):
        gt = self.d.gt
        return gt[0] + px / self.scale, gt[3] - py / self.scale

    def window(self):
        h = self.size_m / 2
        return (self.center[0] - h, self.center[1] - h, self.center[0] + h, self.center[1] + h)

    def clamp_center(self):
        xmin, ymin, xmax, ymax = self.d.bounds
        h = self.size_m / 2
        self.center[0] = min(max(self.center[0], xmin + h), xmax - h)
        self.center[1] = min(max(self.center[1], ymin + h), ymax - h)

    def coarse_ok(self, te):
        x0, y0 = self.to_disp(te[0], te[3])
        x1, y1 = self.to_disp(te[2], te[1])
        c0, r0 = max(int(np.floor(x0)) - 1, 0), max(int(np.floor(y0)) - 1, 0)
        c1, r1 = int(np.ceil(x1)) + 1, int(np.ceil(y1)) + 1
        win = self.coarse_valid[r0:r1, c0:c1]
        return win.size > 0 and bool(win.all())

    # ------------------------------------------------------------------ updates
    def update_box(self, exact):
        self.clamp_center()
        te = self.window()
        x0, y0 = self.to_disp(te[0], te[3])
        s = self.size_m * self.scale
        self.rect.set_xy((x0, y0))
        self.rect.set_width(s)
        self.rect.set_height(s)
        if exact:
            self.stats = self.d.stats(te)
            ok = self.stats["nodata"] == 0
            self.update_preview()
        else:
            ok = self.coarse_ok(te)
            self.stats = None
        self.rect.set_edgecolor("#33ff66" if ok else "#ff3b3b")
        self.update_info()
        self.fig.canvas.draw_idle()

    def update_preview(self):
        if not self.stats:
            return
        s = self.stats
        if self.view == "Elevation":
            zp = s["preview_z"]
            lo, hi = (s.get("zmin", 0), s.get("zmax", 1))
            img = color_relief(zp, s["preview_px"], 2, lo, hi if hi > lo else lo + 1)
        else:
            img = stretch(s["preview_o"])
        self.prev_im.set_data(img)
        self.prev_im.set_extent((0, img.shape[1], img.shape[0], 0))
        self.ax_prev.set_xlim(0, img.shape[1])
        self.ax_prev.set_ylim(img.shape[0], 0)
        self.ax_prev.set_title(f"Preview - {self.view.lower()} (north up)", color="#e8e8e8", fontsize=11)

    def update_info(self):
        te = self.window()
        lon, lat = self.d.lonlat(*self.center)
        pix = self.size_m / self.res
        lines = [
            "SELECTED AREA",
            f"Centre   {lat:8.4f} N  {lon:8.4f} E",
            f"Map X/Y  {self.center[0]:9.1f} / {self.center[1]:.1f}",
            f"Size     {self.size_m:.0f} x {self.size_m:.0f} m",
            f"Output   {self.res} x {self.res} px  ({pix:.2f} m/px)",
            "",
        ]
        if pix > 1.6:
            lines.insert(5, "         (coarser than the 1 m source)")
        s = self.stats
        if s is None:
            lines += ["Checking... (release the mouse)"]
        elif s["nodata"]:
            lines += [f"NOT VALID: {s['nodata']:,} no-data pixels.", "Move the square inside the strip."]
        else:
            rng = s["zmax"] - s["zmin"]
            lines += [
                "VALID - fully inside the DTM",
                f"Min elev {s['zmin']:9.2f} m",
                f"Max elev {s['zmax']:9.2f} m",
                f"Range    {rng:9.2f} m",
                f"Slope    mean {s['slope']:.1f} deg, max {s['slope_max']:.0f} deg",
                f"Photo    {s['photo_black_pct']:.2f}% black pixels",
                "",
                "UNITY SETTINGS",
                f"Terrain Width/Length  {self.size_m:.0f}",
                f"Min Height            {s['zmin']:.3f}",
                f"Max Height            {s['zmax']:.3f}",
                f"Terrain Resolution    {self.res}",
            ]
        if self.message:
            lines += ["", self.message]
        self.info_txt.set_text("\n".join(lines))

    # ------------------------------------------------------------------ events
    def toolbar_busy(self):
        tb = getattr(self.fig.canvas, "toolbar", None)
        return bool(getattr(tb, "mode", ""))

    def on_press(self, ev):
        if ev.inaxes is not self.ax_map or ev.button != 1 or self.toolbar_busy() or ev.xdata is None:
            return
        self.dragging = True
        self.message = ""
        self.center = list(self.from_disp(ev.xdata, ev.ydata))
        self.update_box(exact=False)

    def on_motion(self, ev):
        if self.dragging and ev.inaxes is self.ax_map and ev.xdata is not None:
            self.center = list(self.from_disp(ev.xdata, ev.ydata))
            self.update_box(exact=False)

    def on_release(self, ev):
        if self.dragging:
            self.dragging = False
            self.update_box(exact=True)

    def on_key(self, ev):
        if ev.key in ("t", "T"):
            self.on_view("Photo" if self.view == "Elevation" else "Elevation")

    def on_view(self, label):
        self.view = label
        self.map_im.set_data(self.img[label])
        self.update_preview()
        self.fig.canvas.draw_idle()

    def on_res(self, label):
        self.res = int(label)
        self.update_info()
        self.fig.canvas.draw_idle()

    def on_size(self, val):
        self.size_m = float(val)
        self.message = ""
        self.update_box(exact=True)

    def on_export(self, _ev=None):
        name = re.sub(r"[^A-Za-z0-9_\-]", "_", self.textbox.text.strip()) or "mars_area"
        if not self.stats or self.stats["nodata"]:
            self.message = "Can't export: the area contains no-data."
        else:
            self.message = "Exporting..."
            self.update_info()
            self.fig.canvas.draw()
            try:
                info = self.d.export(self.window(), self.res, name, self.out_root)
                self.message = (f"EXPORTED '{name}'{' (overwrote)' if info['overwrote'] else ''}\n"
                                f"  {info['heightmap']}\n  {info['color']}\n"
                                f"Exact values for Unity (exported):\n"
                                f"  Min Height {info['min_elevation_m']:.3f}\n"
                                f"  Max Height {info['max_elevation_m']:.3f}\n"
                                f"Next: set its import settings and\nbake it in Unity (ask Claude).")
                print(json.dumps(info, indent=2))
                x0, y0 = self.to_disp(info["map_window_xmin_ymin_xmax_ymax"][0], info["map_window_xmin_ymin_xmax_ymax"][3])
                from matplotlib.patches import Rectangle
                self.ax_map.add_patch(Rectangle((x0, y0), self.size_m * self.scale, self.size_m * self.scale,
                                                fill=False, ec="#ffd400", lw=1.2, ls="--"))
                self.ax_map.text(x0, y0 - 6, name, color="#ffd400", fontsize=8, va="bottom")
            except Exception as e:  # noqa: BLE001
                self.message = f"Export failed: {e}"
        self.update_info()
        self.fig.canvas.draw_idle()

    def show(self):
        self.plt.show()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dtm", default=DEFAULT_DTM)
    ap.add_argument("--ortho", default=DEFAULT_ORTHO)
    ap.add_argument("--out", default=DEFAULT_OUT, help="MarsData folder that receives Heightmaps/ and Textures/")
    ap.add_argument("--selftest", metavar="DIR", help="run without a window: export a test area to DIR and save a screenshot")
    args = ap.parse_args()

    import matplotlib
    if args.selftest:
        matplotlib.use("Agg")

    data = MarsData(args.dtm, args.ortho)
    if args.selftest:
        os.makedirs(args.selftest, exist_ok=True)
        p = Picker(data, args.selftest, headless=True)
        p.center = [-1000.0, 1091500.0]
        p.update_box(exact=True)
        p.textbox.set_val("selftest")
        p.on_export()
        p.fig.savefig(os.path.join(args.selftest, "picker_screenshot.png"), dpi=90, facecolor=p.fig.get_facecolor())
        print("Self-test message:", p.message)
        return
    Picker(data, args.out).show()


if __name__ == "__main__":
    main()
