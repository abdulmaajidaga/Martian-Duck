<p align="center"><img src="3_martian_ducklings.jpeg" alt="Three Martian ducklings" width="700"></p>

# Martian Duck: walking on Jezero Crater

A first-person astronaut walk on real Mars terrain: the western half of Jezero Crater (45 × 59 km), including the
delta and the Three Forks area where Perseverance collected samples and built its sample depot. The ground is the
Mars 2020 team's CTX elevation model at true scale with a colourised CTX photo mosaic. You walk under Mars gravity, follow Perseverance's tracks, visit the real sample sites
and depot tubes (with real rover photos), and use a helmet HUD with a top-down map.

**Next up:** the player chooses the places to visit, and a path planner finds a walkable route over the terrain.

---

## 1. Setup (everyone)

1. Install **Unity 6000.6.1f1** (exactly this version) through Unity Hub, and **Git** (Git LFS comes with Git for Windows).
2. Clone with LFS (images and terrain data are stored in LFS):
   ```bash
   git lfs install
   git clone https://github.com/abdulmaajidaga/Martian-Duck.git
   ```
3. Open the folder in Unity Hub. The first import takes a few minutes.
4. Open `Assets/Scenes/SampleScene.unity` and press **Play**.
5. (Recommended) Enable Unity's smart merge for scenes and prefabs (adjust the path to your Unity install):
   ```bash
   git config merge.unityyamlmerge.name "Unity SmartMerge"
   git config merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/6000.6.1f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
   ```

### Controls
| Key | Action |
|---|---|
| W A S D / mouse | Walk / look |
| Shift | Sprint |
| Space | Jump (Mars gravity, 3.71 m/s²) |
| M | Map mode (top-down, movement paused, mouse free) |
| L | Data layers panel (1–9 toggle layers) |
| P | Next photo at a point of interest |
| H | Help overlay |
| Esc | Release the mouse (click to recapture) |

---

## 2. Who works on what

We all commit to `main`, so **each person owns their own files**. Stay inside your area; for shared files, read the
rules in section 3.

| | Path planning | HUD & UI | Scene & world |
|---|---|---|---|
| **Your scene / prefab** | `Assets/Prefabs/Planning/Path Planner.prefab`<br>`Assets/Scenes/PlannerTest.unity` | `Assets/Prefabs/UI/Mars HUD.prefab` | `Assets/Scenes/SampleScene.unity` |
| **Your code** | `Assets/Scripts/Planning/` | `Assets/Scripts/UI/` | `Assets/Scripts/World/` |
| **Your assets** | anything new under `Assets/Planning/` (create it when needed) | `Assets/UI/` (styles, theme, panel settings) | `Assets/MarsData/`, `Assets/Settings/`, `Tools/`, `MarsSources/` |
| **Shared (ask first)** | `Assets/Scripts/Core/` | `Assets/Scripts/Core/` | `Assets/Scripts/Core/` |

### Path planning: what to do
Goal: given the stops the player picked, return a route the astronaut can actually walk (not too steep, short,
sensible).

1. Open **`Assets/Scenes/PlannerTest.unity`**. It's the same terrain and HUD without the 11,000 rover-track decals, so it starts fast.
2. Create your planner in `Assets/Scripts/Planning/`, deriving from **`MarsPathPlanner`** (see `Core/PathPlanning.cs`):
   ```csharp
   public class AStarPathPlanner : MarsPathPlanner
   {
       protected override IEnumerator PlanRoutine(PathPlanRequest request, Action<PlannedPath> done)
       {
           var plan = new PlannedPath { request = request };
           // ... search over the terrain, fill plan.points, plan.visitOrder, plan.stopPointIndex ...
           // yield return null;  every so often, to keep the game responsive
           ComputeStats(plan, request);          // fills length, climb, descent, max slope, time
           plan.success = true; plan.message = $"{plan.lengthM:0} m";
           done(plan);                           // call exactly once
       }
   }
   ```
3. Read the terrain through **`MarsTerrainQuery`** (`Core/MarsTerrainQuery.cs`):
   * `GetHeightGrid(step)`: full height grid in metres, `[row south→north, col west→east]`. It is 4097 × 4097 (11 m × 14.5 m cells over 45 × 59 km), so use `step` 2–4 or a coarse-to-fine search.
   * `CellSize` (11.0 m × 14.5 m per sample), `WorldToGrid`, `GridToWorld`, `HeightAt`, `SlopeAt` (degrees), `NormalAt`, `GradeBetween`, `InBounds`.
   * Respect `request.maxSlopeDeg` (30°, the astronaut's real limit; steeper ground makes the player slide).
4. On the **Path Planner** prefab, add your component and remove/disable `StraightLinePathPlanner` (the placeholder).
5. Test: in the placeholder, right-click the component → **Test: start at player, visit all points of interest**
   (copy that method into yours). The last plan is drawn as gizmos: green = walkable, red = too steep, yellow = stops.
   The placeholder currently gives about 1.8 km / ~18 min for the 5 sample sites.
6. Useful extra data: `Assets/MarsData/Heightmaps/jezero_info.json` (real size, elevation range, georeference) and
   Perseverance's real drive path in `MarsSources/Rover Drive Path.geojson` (a route the rover proved drivable).

### HUD & UI: what to do
Goal: a better-looking helmet interface, plus the screen where the player **chooses the stops** and sees the plan.

1. Work in the **Mars HUD prefab** (double-click `Assets/Prefabs/UI/Mars HUD.prefab`), not in the scene.
2. Panels are `HudModule` components (`Scripts/UI/*HudModule.cs`): implement `Build` (create UI) and `Tick` (update
   every frame), choose a screen region in the Inspector. Styles are in `Assets/UI/MarsHud.uss`.
3. The stop-picking screen:
   * Choices: the points of interest (`PointOfInterest.Registered`) and/or clicks on the map (`MarsMapMode`; the mouse is free in map mode). Interactive UI elements need `pickingMode = PickingMode.Position` (current panels use `Ignore`).
   * Ask for a plan: `MarsPathPlanner.Current.RequestPlan(request)`, with `start` = the player's position and `stops` = the picks.
   * Listen to `MarsPathPlanner.PlanStarted` / `PlanCompleted`. Show `message`, `lengthM`, `estimatedSeconds`, `maxSlopeDeg` and `climbM`, and draw `points` on the map. `RuntimePanelUtils.CameraTransformWorldToPanel` turns a world point into a map position, as `MarsMapMode` already does for markers.
   * When the player accepts, call `RouteManager.Current.SetRoute(plan)`. The existing waypoint panel and map then guide them stop by stop.
4. Keys live in `Scripts/UI/MarsInput.cs` (you own it) and the help overlay list is in `MarsHud.cs`. Teammates
   ask you for new keys.
5. Radiation and Suit panels show **placeholder** values (labelled on screen).

### Scene & world: what to do
Everything in the world: terrain, lighting, astronaut, rover tracks, points of interest, editor-only reference
overlays, data and tools. You are the only one who saves `SampleScene.unity`.
* Terrain is built by `MarsTerrainGenerator` on **MarsWorld** (Bake button in its Inspector).
* Points of interest come from `Assets/MarsData/POI/three_forks_poi.json` (made by `Tools/PoiPhotos/fetch_poi_photos.py`);
  rebuild with right-click **Mars Points of Interest → Build Points Of Interest**.
* **Mars Overlays** (drive path, depot zone, red markers) is tagged **EditorOnly**: visible in the editor and Play
  mode for reference, left out of builds. Toggle it with its checkbox.
* Known issue: the astronaut spawns on the depot waypoint, so the route shows "Route complete" right away.

---

## 3. Working together on `main`

1. **Pull before you start** and before you push: `git pull --rebase`. Close Unity (or at least save) before pulling big changes.
2. **Commit small and often**, only your own files. Before committing, read `git status`: if you see files from
   someone else's area (especially `SampleScene.unity`), you didn't mean to change them, so revert them:
   `git checkout -- <file>`.
3. **Only the scene owner commits `SampleScene.unity`.** Planning and UI work happens in prefabs. Opening the
   scene and pressing Play is fine; just don't commit it. If Unity asks to save the scene, say **Don't Save**.
4. **Always commit `.meta` files together with their asset**, and never delete or regenerate them. Unity uses them
   to keep references intact.
5. **Never push code that doesn't compile.** All scripts are in one assembly, so one error stops everyone. Check
   the Console is free of red errors before you push.
6. **Shared `Core/` files** (`MarsGeoReference`, `MarsTerrainQuery`, `PathPlanning`, `RouteManager`, `Waypoint`,
   `PointOfInterest`): tell the others before changing them, prefer **adding** to **changing**, and never rename
   something others use.
7. **Project settings and packages** (`ProjectSettings/`, `Packages/manifest.json`): announce it first; the scene owner
   commits them.
8. Don't move or rename other people's files. Move files inside Unity (not in Explorer) so references survive.

---

## 4. How the parts connect

```
 HUD & UI                                Path planning                         Scene & world
 ─────────                               ─────────────                         ─────────────
 player picks stops ──RequestPlan()──▶  MarsPathPlanner (your subclass)  ◀──  terrain (MarsTerrainQuery)
                                          │
 shows path + stats ◀──PlanCompleted──────┘
 player accepts ─────▶ RouteManager.SetRoute(plan) ─▶ waypoint panel + map guide the walk
```

**World facts the code relies on**
* Map: **77.0–77.8° E, 17.7–18.7° N** (Mars 2020 frame): the western half of Jezero crater with its west and
  south rim, the river valley, the delta and the surrounding plains.
* Size: 45.05 km (west→east) × 59.27 km (south→north), true scale. Elevation −3095 m to −1378 m (Mars areoid):
  about 1.7 km of relief. Walking straight across takes hours, so plan routes accordingly.
* Unity axes: **+X = east, +Z = north, +Y = up**, 1 unit = 1 metre. North is up on the map. The terrain's
  south-west corner is at the world origin.
* `MarsGeoReference` (in the scene) converts world ↔ longitude/latitude and gives true elevation.
* Height grid: 4097 × 4097 samples (11.0 m × 14.5 m apart), from the Mars 2020 CTX DEM (20 m/px). Ground detail
  smaller than about 20 m is **not** in the data, and slopes are gentler than the real metre-scale ground.
* Ground texture: 8192 × 8192 (5.5 m × 7.2 m per pixel) from the CTX photo mosaic (5 m/px), colourised.
* Rebuild the map from the raw files with `Tools/RegionBuilder/build_region.py` (see the top of that file);
  the previous 1.25 km HiRISE map of Three Forks is still in `Assets/MarsData` as `jezero_*`.

---

## 5. Project map

| Path | What's in it |
|---|---|
| `Assets/Scenes/SampleScene.unity` | The game scene (scene owner) |
| `Assets/Scenes/PlannerTest.unity` | Light scene for planner work (planning owner) |
| `Assets/Prefabs/UI/Mars HUD.prefab` | The whole helmet interface + its data layers (UI owner) |
| `Assets/Prefabs/Planning/Path Planner.prefab` | The active path planner (planning owner) |
| `Assets/Scripts/Core/` | Shared: geo reference, terrain queries, planning contract, route, waypoints, points of interest |
| `Assets/Scripts/Planning/` | Path planners (`StraightLinePathPlanner` is the placeholder) |
| `Assets/Scripts/UI/` | HUD core (`MarsHud`, `HudModule`, `MarsInput`), panels, map mode, data layers |
| `Assets/Scripts/World/` | Terrain generator, player controller, rover tracks, reference overlays, POI builder |
| `Assets/UI/` | HUD stylesheet (`MarsHud.uss`), theme, panel settings |
| `Assets/MarsData/` | Heightmaps + `*_info.json`, colour textures, baked terrain, overlays, rover tracks, POI photos |
| `MarsSources/` | Route GeoJSON (committed); raw HiRISE downloads (not committed, see its README) |
| `MarsPreviews/` | HUD screenshots |
| `Tools/` | Python tools: area crop picker, rover-track generator, POI photo fetcher |

### Quick how-tos
* **New HUD panel:** subclass `HudModule`, add it to the Mars HUD prefab.
* **New data layer:** subclass `MarsDataLayer` (implement `ApplyState`) and add it under **Data Layers** in the Mars
  HUD prefab, or use `GameObjectToggleLayer` in the scene for scene objects. It appears in the L panel automatically.
* **New point of interest:** add a `PointOfInterest` component (name, lon/lat, photos), or add it to the JSON and rebuild.
* **Fixed route by hand:** add `Waypoint` components and list them on **Mars Route**.

---

## Credits
Terrain and images: NASA/JPL-Caltech/University of Arizona (HiRISE), USGS Astrogeology (Mars 2020 TRN mosaic),
NASA/JPL-Caltech, ASU (Mastcam-Z) and MSSS (WATSON) for the Perseverance raw images. Raw rover images are
unprocessed engineering products.
