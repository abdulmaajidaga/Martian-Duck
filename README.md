<p align="center"><img src="3_martian_ducklings.jpeg" alt="Three Martian ducklings" width="600"></p>

# Martian Duck

**Plan and walk a Marswalk in Jezero Crater, on real Mars data, at true scale.**

![View from the western rim of Jezero across the crater, with the helmet HUD](docs/images/hero.jpg)

| | |
|---|---|
| **Where** | The western half of Jezero Crater, 45 × 59 km, true scale, where NASA's Perseverance rover landed |
| **Data** | Mars 2020 CTX terrain · CRISM/OMEGA minerals · TES dust · Perseverance's real path, samples and photos |
| **Planner** | A* over a 4097 × 4097 height grid, cost = walking time (Tobler's hiking function), slope and dust aware |
| **Missions** | Retrieve the Three Forks sample tubes · "Follow the water" through four real sampling sites |

---

## What it does

Martian Duck is a layered, interactive 3D map for planning and carrying out a Marswalk in Jezero Crater, where NASA's
Perseverance rover landed. It covers the western half of the crater (45 × 59 km) at true scale, including the delta,
the crater rim and the Three Forks sample depot. The explorer walks it in first person under Mars gravity.

## How it works

**One map, many missions' data.** Data from several missions sits in one georeferenced frame. The terrain is the
Mars 2020 CTX elevation model and photo mosaic (Mars Reconnaissance Orbiter). Toggleable layers show hydrous- and
aqueous-mineral detections from NASA Mars Trek (CRISM/OMEGA) and the TES Dust Cover Index (Mars Global Surveyor).
Perseverance's real drive path is rendered as rover tracks, with its sampling sites, the sample depot, and raw rover
images at each stop.

<p>
  <img src="docs/images/map_overview.jpg" alt="Map with Perseverance's path, sampling sites and landmarks" width="49%">
  <img src="docs/images/layer_minerals.jpg" alt="Aqueous and hydrous mineral layers on the map" width="49%">
</p>

**A route planner that thinks like a hiker.** On the map, the explorer picks destinations. An A* route planner then
finds the fastest safe walk over the 4097 × 4097 height grid. Walking time is the cost, using Tobler's hiking
function. Ground steeper than 30° is excluded and ground near that limit is penalised. Dusty ground costs extra. With
several stops, the planner chooses the best visiting order. It reports distance, climb, maximum slope and time, and
re-plans when the explorer strays. A helmet HUD guides the walk with navigation, a compass, waypoints and data layers.

<p>
  <img src="docs/images/route_planning.jpg" alt="Four stops picked on the map, reordered and routed: 8.2 km, about 57 min, computed in 1.4 s" width="49%">
  <img src="docs/images/route_ground.jpg" alt="The planned route on the ground in the walking view, with the HUD" width="49%">
</p>

*Left: four sites picked on the map; the planner reorders them and routes 8.2 km (about 57 min, steepest slope 13°)
in 1.4 s. Right: the same kind of route on the ground while walking.*

**Real places, real photos.** Walk up to any of 86 points of interest (sampling sites, depot tubes, landmarks,
Ingenuity's airfields) to see what Perseverance did there and its own images of the spot.

<p>
  <img src="docs/images/poi_sapphire_canyon.jpg" alt="Point-of-interest card at Sapphire Canyon, the Cheyava Falls rock" width="49%">
  <img src="docs/images/poi_viewer.jpg" alt="Full-screen viewer with Perseverance's own Hazcam image" width="49%">
</p>

## Missions

Two missions give the walk a science purpose. The first retrieves the depot's sample tubes. The second,
**"Follow the water"**, drills four real Perseverance sites in order, from igneous rock, through water-formed carbonate,
to the potential biosignature rock Cheyava Falls.

<p>
  <img src="docs/images/mission_tube.jpg" alt="Collecting a sample tube at the Three Forks depot" width="49%">
  <img src="docs/images/mission2_drilling.jpg" alt="Drilling at South Séítah during Follow the water" width="49%">
</p>

## Benefits and impact

Astronauts on Mars will have limited time, oxygen and margin for error. This map shows in one view whether a route is
walkable, how long it takes, and what science lies along it. It works both for mission rehearsal and for public
outreach.

## Creativity

Most Mars maps are static 2D layers. This one puts you on the ground at true scale. The planner models human walking
(speed, slope, footing) rather than straight lines, and the science mission follows the history of water at Jezero.

## Factors considered

The 20 m elevation model smooths out small hazards, so the planner keeps a safety margin from the slope limit. The
dust data is coarse (about 3.6 km per pixel), so it steers between broad regions only. Searches are coarsened and
spread across frames so the game stays responsive. Every layer comes from public data and is credited.

## Tools

Unity 6 (URP), C#, the Input System and UI Toolkit. A Python pipeline (GDAL, NumPy, Pillow) crops the orbital data,
builds the mineral and rover-track layers, and fetches rover images from NASA's public feed. Git LFS stores the large
assets. It runs on a standard PC or Mac.

## Next steps

The radiation and suit panels use simple models (Curiosity's average surface dose rate and a work-rate oxygen model)
rather than live data. We plan to connect them to real data (Curiosity RAD dose rates, MEDA weather) so the map also
covers current conditions.

---

## Try it

1. Install **Unity 6000.6.1f1** and **Git** (with Git LFS).
2. `git lfs install` then `git clone https://github.com/abdulmaajidaga/Martian-Duck.git`
3. Open the folder in Unity Hub, open `Assets/Scenes/SampleScene.unity`, press **Play**.

| Key | Action |
|---|---|
| **W A S D**, mouse, **Shift**, **Space** | Walk, look, run, jump |
| **M** | Map: click sites to plan a route, scroll to zoom, drag to pan |
| **L**, **1–9** | Data layers and map overlays |
| **C** | Collect / drill / deposit (missions) |
| **E**, **← →** | Photo viewer at a point of interest |
| **X** | Explore mode (fast travel) |
| **H** | All controls |

## More

* **[Full details](docs/DETAILS.md):** how every part works, design decisions, limits, and the developer guide.
* **[Sources](SOURCES.md):** every dataset, paper and asset, with credits.

Data: NASA/JPL-Caltech, MSSS, USGS Astrogeology, University of Arizona (HiRISE), ASU (Mastcam-Z), NASA Mars Trek,
ESA/IAS (OMEGA). Feature names: IAU / USGS Gazetteer of Planetary Nomenclature.
