<p align="center"><img src="3_martian_ducklings.jpeg" alt="Three Martian ducklings" width="700"></p>

# Martian Duck: an astronaut's walk on Jezero Crater

**Martian Duck is a first-person Mars EVA simulator built on real mission data.** You are an astronaut who has just
landed in Jezero Crater, the same place NASA's Perseverance rover has been exploring since 2021. You walk the real
terrain at true scale, under Mars gravity, with a helmet display that tracks your oxygen, radiation dose and the dust
under your boots. You plan routes over the terrain the way a real surface team would: avoid slopes that are too
steep, steer around dusty ground, and work out the best order to visit several places.

Then you do the job Perseverance is preparing for. You walk to the real **Three Forks sample depot**, pick up the
sample tubes the rover left there, and carry them back to your rocket. After that you follow the water story of
Jezero by drilling at four of the rover's real sampling sites.

(The name? We're three ducklings who wanted to go to Mars. That's all there is to it.)

> 📸 **[Hero image placeholder]** A wide first-person shot across the crater floor with the HUD visible, ideally
> with the delta or the crater rim on the horizon.
> *Suggested file:* `docs/images/hero.png`

---

## Contents

1. [The idea](#1-the-idea)
2. [What makes it real](#2-what-makes-it-real)
3. [Playing it](#3-playing-it)
4. [The missions](#4-the-missions)
5. [The helmet display (HUD)](#5-the-helmet-display-hud)
6. [The map, data layers and overlays](#6-the-map-data-layers-and-overlays)
7. [Route planning: how the astronaut finds a way](#7-route-planning-how-the-astronaut-finds-a-way)
8. [Points of interest: the real places on the map](#8-points-of-interest-the-real-places-on-the-map)
9. [Building the world](#9-building-the-world)
10. [Design decisions: why we did it this way](#10-design-decisions-why-we-did-it-this-way)
11. [Limits and what's next](#11-limits-and-whats-next)
12. [Setup](#12-setup)
13. [For developers](#13-for-developers)
14. [Credits and sources](#14-credits-and-sources)

---

## 1. The idea

Humans will walk on Mars one day, and the first crews will face a very practical question: **how do you get from
here to there on foot, safely, with a limited supply of oxygen?** On Mars there are no trails or signposts, and
there's no GPS. The ground is uneven, some slopes can't be climbed in a suit, and dust can hide soft, slippery
footing.

Robots are already doing the scouting. Perseverance has driven over 45 km across Jezero, photographed and drilled
dozens of rocks, and in 2022–23 it left ten sealed sample tubes on the ground at a site called **Three Forks**, a
backup cache for a future mission to bring back to Earth.

We wanted to connect the two: **take everything the robots have learned about Jezero and let a person stand in it.**
Martian Duck is:

* **A place to explore.** It's the real crater, at real size, with the real rover's path and the real sites and
  photos. You can see what Perseverance saw, from where it saw it.
* **A planning tool.** An EVA lasts hours, not minutes. The game makes you think about distance, slope, dust,
  oxygen and radiation the way mission planners do.
* **A story.** The missions follow the science: collect the depot, then trace the history of water in Jezero
  through the rocks that recorded it.

> 📸 **[Screenshot placeholder]** Side by side: a real Perseverance photo of a site (from the photo viewer) next to
> the same spot in the game.
> *Suggested file:* `docs/images/real_vs_game.png`

---

## 2. What makes it real

Almost everything you see and measure in the game comes from real data (full list in [SOURCES.md](SOURCES.md)):

| In the game | Where it comes from |
|---|---|
| The ground: 45 × 59 km of Jezero crater, its delta, rim and the plains around it | Mars 2020 team's CTX elevation model (20 m per sample) and photo mosaic (5 m per pixel), used at **true scale** |
| Elevation on the HUD (around −2,566 m at the landing site) | The same elevation model, in true Mars elevations |
| Gravity: you jump about 2.6× higher than on Earth | Mars gravity, 3.71 m/s² |
| Perseverance's drive path (about 45 km) and its wheel tracks | NASA's Mars 2020 traverse data |
| 22 sampling sites, the Three Forks depot and its sample tubes | NASA's sampling and depot data |
| Over 60 photos at the points of interest | Perseverance's own raw images (Hazcam, Navcam, Mastcam-Z, WATSON) |
| Ingenuity's 72 flights and airfields | NASA's Ingenuity flight data |
| Crater and valley names (Jezero, Belva, Neretva Vallis, …) | IAU / USGS Gazetteer of Planetary Nomenclature |
| Mineral layers | Published orbital detections (Ehlmann & Edwards 2014; Carter et al. 2013) |
| Dust layer and dust reading | TES Dust Cover Index from orbit (Ruff & Christensen 2002) |
| Radiation rate | Curiosity's measured surface average (Hassler et al. 2014) |

Where we had to make something up, we say so (for example walking speed, see [section 10](#10-design-decisions-why-we-did-it-this-way)).

> 📸 **[Screenshot placeholder]** The map with Perseverance's path, the sample sites and the landmarks turned on.
> *Suggested file:* `docs/images/map_overview.png`

---

## 3. Playing it

You start next to your rocket at Perseverance's landing site, **Octavia E. Butler Landing**. A help card shows the
controls for a few seconds; when it closes, your first task appears in the middle of the screen and then slides into
the top-right corner.

### Controls

| Key | Walking | Map (M) |
|---|---|---|
| **W A S D / mouse** | Walk / look around | |
| **Shift** | Run | |
| **Space** | Jump (Mars gravity) | |
| **C** | Collect a tube, drill, deposit samples (missions) | |
| **E** | Open the photo viewer at a point of interest | |
| **← / →** | Previous / next photo | |
| **M** | Open the map (walking pauses) | Back to walking |
| **L** | Data layer panel | (always open in map mode) |
| **1–9** | Toggle layers while the panel is open | Toggle layers |
| **X** | Explore mode: very fast travel across the map | |
| **H** | Show / hide the controls | |
| **Esc** | Free the mouse (click to recapture) | |
| **Click** | | Add or remove a point of interest as a stop |
| **Right-click** | | Clear all stops |
| **Scroll / + −** | | Zoom (towards the cursor) |
| **Drag** | | Move the map |
| **R** | | Reset the map view |

> 📸 **[Screenshot placeholder]** The controls card on screen at the start.
> *Existing image you could reuse:* `MarsPreviews/hud_help.png` (may be out of date)

### How far is everything?

The map is real size, so distances are real: the depot is **2.4 km** from the rocket, the farthest sampling sites
are over 20 km away, and the crater is 45 km wide. Walking is 4 m/s (Shift: 12 m/s), already about three times
faster than a real astronaut in a suit. For crossing the whole map there's **explore mode (X)**, which works like a
creative-mode fly-over.

---

## 4. The missions

The missions run in order from the rocket. The HUD's waypoint panel always shows the next step, and an orange route
on the ground (and on the map) shows the best way to get there.

### Mission 1: the sample depot

Perseverance left ten sealed sample tubes on the ground at Three Forks as a backup set for a future Mars Sample
Return mission. Your job:

1. **Walk to the Three Forks depot** (2.4 km). The route planner works out the best order to visit every tube.
2. **Collect every tube.** Stand next to one and press **C**. The tubes are where the rover really dropped them,
   each with the photos Perseverance took of it on the ground.
3. **Return to the rocket** and press **C** to deposit the samples.

> 📸 **[Screenshot placeholder]** The rocket at the start of the mission.
> *Existing image you could reuse:* `MarsPreviews/mission_start_rocket.png`

> 📸 **[Screenshot placeholder]** Standing over a sample tube at the depot, about to press C.
> *Existing image you could reuse:* `MarsPreviews/mission_tube.png`

### Mission 2: follow the water

Jezero was a lake 3.5 billion years ago. Its rocks record how water came, changed the rocks and left. You drill at
four of Perseverance's real sampling sites, in story order:

| Stop | Rock | What it tells us |
|---|---|---|
| **South Séítah** | Igneous, olivine-rich | Volcanic rock that formed **without** water: the "before" |
| **Comet Geyser** | Silica-cemented carbonate | Minerals that formed **in** water at the lake's margin |
| **Sapphire Canyon** | The "Cheyava Falls" rock | Leopard-spot markings NASA called a potential sign of ancient life (2025) |
| **Green Gardens** | Serpentinite | Olivine altered by water deep underground |

At each site press **C** and stay inside the drill area until the core is done; the waypoint panel shows the
progress. Then bring the cores back to the rocket.

> 📸 **[Screenshot placeholder]** Drilling at a site: the drill-area ring, the marker and the "Drilling 60%"
> waypoint panel.
> *Suggested file:* `docs/images/mission2_drilling.png`

**Why these missions?** Mission 1 is about logistics: distance, the order of stops, getting back with oxygen to
spare. Mission 2 is about science: why astronauts would go to *these* rocks and not others. Together they cover the
two things a real EVA plan has to balance.

---

## 5. The helmet display (HUD)

The HUD is designed like a visor: four cards in the corners, a compass at the top, and the middle of the view left
clear.

> 📸 **[Screenshot placeholder]** The full HUD while walking, all four corner cards visible.
> *Suggested file:* `docs/images/hud_full.png`

| Card | What it shows | Why |
|---|---|---|
| **Compass** (top) | Heading in degrees, N/E/S/W | On Mars there's no GPS and no magnetic field to navigate by; heading plus map is how you find your way |
| **Navigation** (top left) | True elevation, speed, distance walked, latitude/longitude | Elevation is real Mars elevation (below the "sea level" datum, hence negative) |
| **Next waypoint** (top right) | Where to go next, distance, an arrow relative to where you're facing, bearing | When a new task starts it pops up large in the middle for a few seconds, after the help card closes |
| **Radiation & dust** (bottom left) | Dose rate, dose so far (with a bar), and the dust level under your feet (with a bar) | Mars has no magnetic field and a thin atmosphere; radiation is a real limit on EVA time. Dust affects footing and the suit |
| **Suit** (bottom right) | Oxygen (with a bar), EVA time, time left | Oxygen use follows your effort: resting, walking and running burn it at different rates |

**Three bars, three colours:** blue for oxygen, yellow for radiation and tan for dust. Each one turns red when it
reaches a warning level.

* **Radiation** uses Curiosity's measured surface average, 0.64 mSv per day (26.7 µSv/h). The bar fills up to
  one sol's surface dose, so a full 8-hour EVA fills about a third of it.
* **Dust** is read live from orbital dust data at your exact position: Low, Moderate, High or Very high.
* **Oxygen** is sized like real suit life support: 8 hours of EVA at an average work rate of about 250 W.
  Resting (~120 W) stretches it to over 16 hours, while running (~550 W) roughly halves it. "Time left" is
  calculated at your current effort.

> 📸 **[Screenshot placeholder]** Close-up of the radiation & dust card and the suit card, with all three bars
> partly filled.
> *Suggested file:* `docs/images/hud_bars.png`

### Points of interest on the HUD

Walk near a real site and a card slides in with a photo, what the place is, when the rover was there and what it did.
Press **E** for a full-screen photo viewer.

> 📸 **[Screenshot placeholder]** A point-of-interest card next to a sampling site.
> *Existing image you could reuse:* `MarsPreviews/hud_poi_card.png`

> 📸 **[Screenshot placeholder]** The full-screen photo viewer showing a real Perseverance image.
> *Existing image you could reuse:* `MarsPreviews/hud_poi_viewer.png`

---

## 6. The map, data layers and overlays

Press **M** for the map: a top-down view of the whole terrain, north up, with your position and facing, a scale bar
and your coordinates. Walking pauses and the mouse is free. You can zoom in from the whole 45 km map down to about
100 m across.

> 📸 **[Screenshot placeholder]** The map fully zoomed out, with the layer panel on the right.
> *Suggested file:* `docs/images/map_full.png`

> 📸 **[Screenshot placeholder]** The map zoomed in on Three Forks, showing tubes, waypoints and the planned route.
> *Suggested file:* `docs/images/map_zoomed_depot.png`

The panel on the right has two sections.

### Data layers (drawn on the ground, visible on the map and while walking)

| Layer | What it shows | Why it's useful |
|---|---|---|
| **Elevation tint** | Height, from blue (low) to red (high) | See valleys, the delta and the rim at a glance |
| **Aqueous minerals** | Orbital detections of minerals that formed in water (silica, phyllosilicates, carbonates, …) | Where the water history is, i.e. where to go for science |
| **Hydrous mineral detections** | Where hydrated minerals were detected from orbit | Same idea, a second independent dataset |
| **Surface dust** | Dust cover from orbit: blue = clean rock, cream = thick dust | Why the route planner avoids some areas; where footing may be bad |

> 📸 **[Screenshot placeholder]** The map with the Surface dust layer on.
> *Suggested file:* `docs/images/layer_dust.png`

> 📸 **[Screenshot placeholder]** The map with the Elevation tint layer on.
> *Existing image you could reuse:* `MarsPreviews/hud_map_elevation_tint.png`

> 📸 **[Screenshot placeholder]** The map with a mineral layer on.
> *Existing image you could reuse:* `MarsPreviews/minerals_map.png`

### Map overlays (markers and routes)

| Overlay | What it shows |
|---|---|
| **Perseverance's path** | The rover's real 45 km drive from landing to sol 2022, plus the depot zone |
| **Waypoints** | The current route's waypoints |
| **Sample sites & depot** | All sampling sites and the depot tubes (click them to plan a route) |
| **Landmarks** | Landing site, Van Zyl Overlook, named craters and valleys, Ingenuity's first and last airfields |
| **Ingenuity airfields** | Every place the helicopter landed (off by default because there are a lot) |
| **Planned route** | The route the planner found |

Every overlay can be switched off, so the map shows only what you need. When zoomed out, labels that would overlap
are hidden (the dot stays), and they come back as you zoom in.

> 📸 **[Screenshot placeholder]** The map with Perseverance's path and the Ingenuity airfields on.
> *Suggested file:* `docs/images/overlay_perseverance_ingenuity.png`

**Why a layer system?** Real mission planning stacks many maps (terrain, minerals, hazards, traverses) and switches
between them. We built the layers as plug-ins: any new dataset becomes a new layer without touching the HUD code.

---

## 7. Route planning: how the astronaut finds a way

This is the core of the simulator. Pick places on the map (click points of interest), and the planner finds the
**fastest walkable route** through all of them over the real terrain, drawn in orange on the map and on the ground.
During missions the same planner guides you automatically and re-plans if you wander off the path.

> 📸 **[Screenshot placeholder]** The map with three or four stops picked and the planned route drawn through them.
> *Suggested file:* `docs/images/route_planning.png`

> 📸 **[Screenshot placeholder]** The orange route on the ground in the walking view.
> *Suggested file:* `docs/images/route_ground.png`

### How it works

The planner is an **A\* search** over the terrain's height grid, where the cost of every step is **walking time**,
not distance:

1. **Slope sets the speed.** We use **Tobler's hiking function**, a standard model from field geography. People
   walk fastest on a slight downhill and slow down going uphill and on steep downhill. Climbing a hill can take
   longer than walking around it, and the planner knows that.
2. **Steep ground is off limits.** Anything steeper than **30°** (about the limit for a suited astronaut) is left
   out of routes. Slopes close to the limit get an extra penalty, so routes keep a
   safety margin.
3. **Dust costs extra.** Ground with more dust in the orbital dust data costs more, so routes prefer clean,
   firm rock when it doesn't mean a big detour.
4. **Best order of stops.** With several stops, the planner measures the real walking cost between every pair
   (uphill costs more than downhill, so A→B ≠ B→A) and picks the best order: an exact solution (Held–Karp) for
   small sets, and nearest-neighbour plus 2-opt for larger ones.
5. **Clean-up.** The grid path is straightened wherever a direct line is just as good, then resampled on the
   surface.

The map is 4097 × 4097 height samples (16.8 million cells), so large searches run on a coarser grid and the work
is spread over several frames to keep the game smooth.

**Why time and not distance?** Because oxygen runs out over time, not distance. A route that is 10% longer but
avoids a 200 m climb is the better choice on an EVA. That's how real traverse planning works, and it's what Tobler's
function captures.

**Why dust?** Dust hides soft ground and slippery footing, coats suits and visors, and was a real problem for Apollo
astronauts on the Moon. The orbital dust data is coarse (about 3.6 km per sample), so it steers routes between broad
areas rather than around single dunes. Turn on the **Surface dust** layer to see why a route bends.

---

## 8. Points of interest: the real places on the map

There are 85 points of interest, all from real data:

| Group | How many | What you'll find |
|---|---|---|
| **Sampling sites** | 21 | Every one of Perseverance's sampling sites, from the first core (Montdenier) to Sapphire Canyon and the crater-rim samples. Each has its sample numbers, sols, dates, rock type and real rover photos |
| **Depot tubes** | 9 | The sample tubes at Three Forks, where the rover dropped them, with the WATSON and Hazcam photos of each one on the ground |
| **Landmarks** | 11 | Octavia E. Butler Landing, Van Zyl Overlook, Perseverance's latest position, and the named features: Jezero, Neretva Vallis, Una Vallis, Belva, Hartwell, Marysville and Sedona craters, Nili Planum |
| **Ingenuity** | 44 | Wright Brothers Field (first powered flight on another planet), the final airfield where flight 72 ended, and all the airfields in between |

> 📸 **[Screenshot placeholder]** Walking up to Wright Brothers Field or the landing site marker.
> *Suggested file:* `docs/images/poi_landmark.png`

> 📸 **[Screenshot placeholder]** The Sapphire Canyon point-of-interest card.
> *Suggested file:* `docs/images/poi_sapphire_canyon.png`

**Why real photos?** The aim is to put you where the rover was. Seeing Perseverance's own image of a rock while
you stand at that rock connects the game to the real mission. The photos are raw engineering images exactly as NASA
publishes them, so the Hazcam and Navcam colours aren't calibrated.

---

## 9. Building the world

### The terrain

The ground is the Mars 2020 team's own elevation model of Jezero (CTX stereo, 20 m per sample), cropped to the
western half of the crater: **77.0–77.8° E, 17.7–18.7° N**, 45 × 59 km and about 1.7 km of relief. It's turned into
a Unity terrain of 4097 × 4097 height samples, at true scale (1 Unity unit = 1 metre, north = +Z). The ground
texture is the matching CTX photo mosaic (8192 × 8192), colourised using HiRISE colour from Three Forks.

> 📸 **[Screenshot placeholder]** The crater rim seen from the crater floor, or the view from the rim into the crater.
> *Existing images you could reuse:* `MarsPreviews/crater_view_from_rim.png`, `MarsPreviews/crater_view_west.png`

> 📸 **[Screenshot placeholder]** The source elevation and photo data next to the Unity terrain.
> *Existing images you could reuse:* `MarsPreviews/2_jezero_elevation_2m.png`, `MarsPreviews/4_unity_area_3d.png`

**Why this area and this data?** Jezero is where Perseverance is, so every rover dataset lines up with it. We first
built a 1.25 km map of Three Forks from a 1 m HiRISE elevation model. It was very detailed, but too small for real
route planning, so we switched to the 20 m CTX model of the whole western crater. That traded close-up detail for a
map where distance, oxygen and route choice actually matter.

### Perseverance's wheel tracks

The rover's real drive path is turned into wheel ruts and turn-in-place arcs, projected onto the ground as decals,
with rut sizes from the rover's real wheel size (52.5 cm, 48 grousers) and width. The full traverse would be about
100,000 decals, far too many to keep in the scene at once, so the tracks are **streamed**: only the cells near you
are created as you walk.

> 📸 **[Screenshot placeholder]** Wheel tracks at eye level near the landing site.
> *Existing images you could reuse:* `MarsPreviews/rover_tracks_v4_along.png`, `MarsPreviews/tracks_landing_site.png`

### The astronaut

You're a suited astronaut model with a first-person camera just in front of the helmet. Movement uses Mars gravity
(3.71 m/s²), a slope limit (you slide off anything too steep), and ground snapping so you don't float off small bumps
in the low gravity.

### Sky and light

A procedural sky tinted to Mars's dusty orange, with a small, soft sun.

> 📸 **[Screenshot placeholder]** A view across the delta with the sky and the crater rim.
> *Suggested file:* `docs/images/sky.png`

---

## 10. Design decisions: why we did it this way

| Decision | Why |
|---|---|
| **True scale, no shrinking** | The whole point is to feel real distances. A shrunken map would make every plan meaningless |
| **Walking speed 4 m/s (about 3× realistic)** | A real suited astronaut walks at about 1–1.5 m/s. At true speed the 2.4 km to the depot would take over 30 minutes. We kept true distances and sped up the walk instead, and added explore mode for crossing the map |
| **Route cost = time (Tobler), not distance** | Oxygen is a time budget. The fastest route is the safest |
| **30° slope limit for routes** | About the steepest slope a suited astronaut can safely climb. The player can scramble up to 40° (steeper and you slide), so every planned route has a margin |
| **Dust in the route cost and on the HUD** | Dust is one of the most practical surface hazards (footing, suits, equipment), and real orbital data exists for it |
| **Oxygen tied to effort** | Real life support is sized in hours at an average work rate; running costs more. That makes "rest or run?" a real choice |
| **Radiation shown as dose built up** | The rate on the surface is nearly constant, so the meaningful number is the total dose, compared with one sol's dose |
| **Real raw photos, uncorrected** | Authenticity over polish: they are exactly what the rover sent back |
| **Map pauses walking** | Planning is a separate activity from walking, as on a real EVA, and it frees the mouse for the map |
| **Layers and HUD panels as plug-ins** | New datasets and panels can be added without touching the core, which let three people work in parallel |
| **Points of interest generated from data, not placed by hand** | 85 points with photos and facts would be slow and error-prone by hand. The scripts in `Tools/` rebuild them from the source data |
| **Labels decluttered on the map** | 85 labels on a 45 km map overlap. Showing only the ones that fit keeps the map readable, and zooming in reveals the rest |

---

## 11. Limits and what's next

**What isn't real (yet):**

* Detail smaller than about 20 m isn't in the elevation data, so the close-up ground is smoother than real Mars
  and slopes are gentler than the metre-scale reality.
* The dust data is coarse (about 3.6 km per sample).
* Walking speed is faster than reality on purpose (see above).
* Oxygen and radiation are simple models: no shielding, no solar storms, no suit failures.
* The sample-tube positions are where the rover parked to drop each tube (about 1–2 m accuracy).
* Ingenuity's flight path is downloaded but not yet drawn as an overlay.

**Ideas for next steps:**

* Draw Ingenuity's flight path as an overlay.
* Small rocks and boulders near the player for a better sense of speed and scale.
* Dust storms that change visibility and the dust reading.
* A traverse report at the end of a mission: distance, time, oxygen used, dose received, compared with the plan.
* Higher-resolution HiRISE terrain streamed in around the player for close-up detail.

---

## 12. Setup

1. Install **Unity 6000.6.1f1** (exactly this version) through Unity Hub, and **Git** (Git LFS comes with Git for
   Windows).
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

---

## 13. For developers

### How the parts connect

```
 HUD & UI                                Path planning                         Scene & world
 ─────────                               ─────────────                         ─────────────
 player picks stops ──RequestPlan()──▶  MarsPathPlanner                 ◀──  terrain (MarsTerrainQuery)
   (MarsMapMode)                         (DustAwareAStarPlanner)          ◀──  dust (MarsDustMap)
                                          │
 shows path + stats ◀──PlanCompleted──────┘──▶ PlannedPathRenderer (ground + map line)
 player accepts ─────▶ RouteManager.SetRoute(plan) ─▶ waypoint panel + map guide the walk

 MarsMission ──▶ RouteManager (one guide waypoint) ──▶ MissionPathGuide re-plans the best path to it
```

### World facts the code relies on

* Map: **77.0–77.8° E, 17.7–18.7° N** (Mars 2020 frame). Size 45.05 km (west→east) × 59.27 km (south→north).
  Elevation −3095 m to −1378 m (Mars areoid).
* Unity axes: **+X = east, +Z = north, +Y = up**, 1 unit = 1 metre. The terrain's south-west corner is at the origin.
* `MarsGeoReference` converts world ↔ longitude/latitude and gives true elevation.
* Height grid: 4097 × 4097 samples (11.0 m × 14.5 m apart). `MarsTerrainQuery` gives heights, slopes and grades.

### Project map

| Path | What's in it |
|---|---|
| `Assets/Scenes/SampleScene.unity` | The game scene |
| `Assets/Scenes/PlannerTest.unity` | Light scene for planner work (no rover-track decals) |
| `Assets/Prefabs/UI/Mars HUD.prefab` | The helmet interface and its data layers |
| `Assets/Prefabs/Planning/Path Planner.prefab` | The route planner and the dust map |
| `Assets/Scripts/Core/` | Shared: geo reference, terrain queries, planning contract, route, waypoints, points of interest |
| `Assets/Scripts/Planning/` | `DustAwareAStarPlanner`, `MissionPathGuide`, `MapClickRoutePlanner`, `PlannedPathRenderer`, `MarsDustMap` |
| `Assets/Scripts/UI/` | HUD core (`MarsHud`, `HudModule`, `MarsInput`), panels, map mode, data layers and overlays |
| `Assets/Scripts/World/` | Terrain generator, player, missions, rover tracks, drive-path overlay, POI builder |
| `Assets/UI/` | HUD stylesheet (`MarsHud.uss`), theme, panel settings |
| `Assets/MarsData/` | Heightmaps and `*_info.json`, textures, baked terrain, overlays, rover tracks, POI data and photos |
| `MarsSources/` | Mission GeoJSON and Ingenuity data (committed); big raw rasters (not committed, see its README) |
| `Tools/` | Python tools: region builder, mineral layers, route overlay, rover tracks, POI generators |

### Quick how-tos

* **New HUD panel:** subclass `HudModule` (implement `Build` and `Tick`), add it to the Mars HUD prefab, pick a
  screen region.
* **New data layer:** subclass `MarsDataLayer` (implement `ApplyState`), or `TerrainTintLayer` for a full-map
  colour layer. It appears in the panel automatically. For scene objects, use `GameObjectToggleLayer`.
* **New map overlay toggle:** add a `MapOverlayLayer` (waypoints, planned route, or points of interest filtered by
  category).
* **New point of interest:** add a `PointOfInterest` component, or add it to a POI JSON and run **Build Points Of
  Interest** on the matching object.
* **Regenerate the points of interest:** `python Tools/PoiPhotos/make_jezero_pois.py` (whole map) or
  `fetch_poi_photos.py` (Three Forks), then rebuild in Unity.
* **Rebuild the terrain:** `Tools/RegionBuilder/build_region.py` (see the top of the file) and **Bake** on
  `MarsTerrainGenerator`.

### Working together on `main`

1. **Pull before you start** and before you push: `git pull --rebase`.
2. **Commit small and often**, only your own files. Check `git status` and revert files you didn't mean to change
   (`git checkout -- <file>`), especially `SampleScene.unity`.
3. **Always commit `.meta` files with their asset**, and never delete or regenerate them.
4. **Never push code that doesn't compile.** All scripts are in one assembly, so one error stops everyone.
5. **Shared `Core/` files:** tell the others before changing them, prefer adding to changing, never rename what
   others use.
6. Move files inside Unity, not in Explorer, so references survive.

---

## 14. Credits and sources

Every data source, paper and asset is listed in **[SOURCES.md](SOURCES.md)**.

Terrain and images: NASA/JPL-Caltech, MSSS and USGS Astrogeology (Mars 2020 CTX and HiRISE products), University of
Arizona (HiRISE). Rover images: NASA/JPL-Caltech, ASU (Mastcam-Z) and MSSS (WATSON). Raw rover images are
unprocessed engineering products. Ingenuity data: NASA/JPL-Caltech. Feature names: IAU / USGS Gazetteer of Planetary
Nomenclature.
