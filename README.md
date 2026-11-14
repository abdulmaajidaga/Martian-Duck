# Martian Duck: walking on Jezero Crater

First-person astronaut walking on real Mars terrain: the Three Forks sample depot area of Jezero Crater,
built from a HiRISE elevation model (1 m/px) and the USGS Mars 2020 HiRISE color mosaic. Includes the
Perseverance drive path, sample sites, simulated rover tracks, and a helmet HUD with a map mode.

## Requirements
- **Unity 6000.6.1f1** (Unity 6.6) with URP. Open the project folder in Unity Hub; packages install automatically.
- **Git LFS** (images are stored with LFS). It ships with Git for Windows; run `git lfs install` once.
- Optional, only for the data tools in `Tools/`: Python 3.10 with numpy, scipy, Pillow, matplotlib and GDAL
  (`osgeo`). On Windows, GDAL comes from Christoph Gohlke's prebuilt wheel; set `PROJ_DATA` to the wheel's
  `osgeo/data/proj` folder.

## Getting started
```bash
git lfs install
git clone <repo-url>
```
1. Open the folder in Unity Hub (first import takes a few minutes while Unity rebuilds `Library/`).
2. Open `Assets/Scenes/SampleScene.unity` and press Play.

Everything needed to play is committed: the baked terrain (`Assets/MarsData/Baked/`), heightmap, color
texture, overlays and rover track decals. The raw source files in `MarsSources/` are **not** committed
(about 1.2 GB); you only need them to cut a new area. See [MarsSources/README.md](MarsSources/README.md).

## Controls
| Key | Action |
|---|---|
| WASD / mouse | Walk / look |
| Shift | Sprint |
| Space | Jump (Mars gravity, 3.71 m/s²) |
| M | Map mode (top-down, movement paused) |
| L | Data layers panel (1–9 toggle layers) |
| H | Help overlay |
| Esc | Release the cursor (click to recapture) |

## Project layout
| Path | Contents |
|---|---|
| `Assets/Scripts/` | `MarsTerrainGenerator` (builds/bakes the terrain), `MarsPlayerController`, `MarsRouteOverlay`, `MarsRoverTracks` |
| `Assets/Scripts/UI/` | Helmet HUD framework (UI Toolkit): HUD modules, map mode, data layers, waypoints and route |
| `Assets/UI/` | HUD stylesheet (`MarsHud.uss`), theme and panel settings |
| `Assets/MarsData/` | Heightmaps + `*_info.json` (real-world size, elevation, georeference), color textures, baked terrain, overlays, rover track textures |
| `MarsSources/` | Route GeoJSON (committed) and the raw HiRISE downloads (not committed) |
| `MarsPreviews/` | HUD screenshots (other preview/debug images are not committed) |
| `Tools/MarsCropPicker/` | Interactive picker to choose and export a new terrain area (`run_picker.bat`) |
| `Tools/RoverTracks/` | Generates the rover track textures and `rover_tracks.json` from the drive path |

## Extending
- **Data layer:** add a `GameObjectToggleLayer` to a GameObject and drag objects into Targets, or subclass
  `MarsDataLayer` and implement `ApplyState(bool on)`. It appears in the L panel automatically.
- **Waypoint:** add a `Waypoint` component, set name and longitude/latitude, and add it to the **Mars Route** list.
- **HUD panel:** subclass `HudModule`, implement `Build` and `Tick`, add it to the **Mars HUD** object.

Radiation and suit values in the HUD are placeholders.

## Notes for collaborators
- Unity's YAML merge tool helps with scene conflicts. To enable it, add to your git config (adjust the path
  to your Unity install):
  ```
  git config merge.unityyamlmerge.name "Unity SmartMerge"
  git config merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/6000.6.1f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
  ```
- Avoid two people editing `SampleScene.unity` at the same time; scene merges are painful.
