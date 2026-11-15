# Data sources and credits

Every outside source used to build Martian Duck's world, data layers, points of interest and in-game facts.
Items marked *(previous map)* were used only for the earlier 1.25 km Three Forks map, which is still in the
project but not in the game.

## Terrain (the Jezero map, 77.0–77.8°E, 17.7–18.7°N)

| Used for | Source | Credit |
|---|---|---|
| Elevation (heightmap) | Mars 2020 Science Investigation CTX DEM mosaic, 20 m/px (`M20_JezeroCrater_CTXDEM_20m.tif`) — https://asc-pds-services.s3.us-west-2.amazonaws.com/mosaic/mars2020_trn/CTX/ScienceInvestigationMaps_JPL/ | NASA/JPL-Caltech/MSSS/USGS |
| Ground image | Mars 2020 CTX orthophoto mosaic, 5 m/px (`M20_JezeroCrater_CTXortho_mosaic_5m.tif`), same location | NASA/JPL-Caltech/MSSS/USGS |
| Ground colour | Colourised from the Mars 2020 TRN HiRISE orthomosaic `JEZ_hirise_soc_006` (25 cm/px) — https://asc-pds-services.s3.us-west-2.amazonaws.com/mosaic/mars2020_trn/HiRISE/JEZ_hirise_soc_006_orthoMosaic_25cm_Eqc_latTs0_lon0_first.tif | NASA/JPL-Caltech/University of Arizona, USGS Astrogeology |
| Reference | Fergason, R. L. et al. (2020). *Mars 2020 Terrain Relative Navigation flight product generation: digital terrain model and orthorectified image mosaics.* 51st Lunar and Planetary Science Conference, abstract 2020. | |
| Reference | Malin, M. C. et al. (2007). Context Camera investigation on board the Mars Reconnaissance Orbiter. *JGR Planets* 112, E05S04. | |

*(previous map)* HiRISE DTM `DTEEC_045994_1985_046060_1985_U01`, orthoimage `ESP_046060_1985_RED_C_01_ORTHO`, colour
`ESP_045994_1985_MRGB` and image `ESP_083512_1985_RED`, from https://hirise.lpl.arizona.edu/PDS/
(NASA/JPL-Caltech/University of Arizona; McEwen, A. S. et al. (2007), *JGR Planets* 112, E05S02). Elevations on the
Mars 2000 areoid (Smith, D. E. et al. (2001), *JGR Planets* 106(E10), 23689–23722).

## Perseverance mission data

| Used for | Source | Credit |
|---|---|---|
| Rover drive path, wheel tracks, "Rover drive path" layer | `Rover Drive Path.geojson` — Mars 2020 mission map layer `M20_traverse` (https://mars.nasa.gov/mmgis-maps/M20/Layers/json/M20_traverse.json) | NASA/JPL-Caltech |
| Rover positions (landing site, Van Zyl Overlook, latest position) | `Rover Waypoints.geojson` — layer `M20_waypoints` (https://mars.nasa.gov/mmgis-maps/M20/Layers/json/M20_waypoints.json) | NASA/JPL-Caltech |
| Sampling sites (all 22 samples) and Mission 2 sites | `Sampling Locations.geojson` — Mars 2020 mission map data | NASA/JPL-Caltech |
| Three Forks depot zone | `Sample Depot Zone.geojson` — Mars 2020 mission map data | NASA/JPL-Caltech |
| Rover photos at every point of interest | Mars 2020 raw images, https://mars.nasa.gov/mars2020/multimedia/raw-images/ (each photo's image ID and link is in `Assets/MarsData/POI/*.json`) | NASA/JPL-Caltech; Mastcam-Z: NASA/JPL-Caltech/ASU; WATSON: NASA/JPL-Caltech/MSSS |
| Rover size for the wheel tracks (wheel 52.5 cm, 48 grousers, width 2.7 m) | NASA Mars 2020 rover specifications, https://science.nasa.gov/mission/mars-2020-perseverance/ | NASA/JPL-Caltech |
| Sapphire Canyon / "Cheyava Falls" description | Hurowitz, J. A. et al. (2025). Redox-driven mineral and organic associations in Jezero Crater, Mars. *Nature*; NASA announcement of 10 Sept 2025 | NASA/JPL-Caltech |

Raw images are unprocessed engineering products (the Hazcam and Navcam colour is not calibrated).

## Ingenuity Mars Helicopter

| Used for | Source | Credit |
|---|---|---|
| Airfields, flight numbers, dates, distances (Wright Brothers Field, final airfield, 42 others) | `m20_heli_waypoints.json` — https://mars.nasa.gov/mmgis-maps/M20/Layers/json/m20_heli_waypoints.json | NASA/JPL-Caltech |
| Flight path (saved, not drawn yet) | `m20_heli_flight_path.json` — https://mars.nasa.gov/mmgis-maps/M20/Layers/json/m20_heli_flight_path.json | NASA/JPL-Caltech |

## Place names

| Used for | Source |
|---|---|
| Jezero, Neretva Vallis, Una Vallis, Belva, Hartwell, Marysville, Sedona, Nili Planum (names, centres, sizes, name origins) | IAU / USGS Gazetteer of Planetary Nomenclature, https://planetarynames.wr.usgs.gov (`MARS_nomenclature_center_pts.zip`) |
| Informal names (sampling sites, outcrops, Séítah, Witch Hazel Hill, Valinor Hills, …) | Mars 2020 team names, as given in the NASA data above |

## Data layers

| Layer | Source |
|---|---|
| Aqueous minerals | Ehlmann, B. L. & Edwards, C. S. (2014). Mineralogy of the Martian surface. *Annual Review of Earth and Planetary Sciences* 42, 291–315. Read through NASA Mars Trek (https://trek.nasa.gov/mars/), layer "Aqueous Mineral Distribution". |
| Hydrous mineral detections | Carter, J. et al. (2013). Hydrous minerals on Mars as seen by the CRISM and OMEGA imaging spectrometers: updated global view. *JGR Planets* 118, 831–858. Through NASA Mars Trek, layer "Hydrous Mineral Detections (OMEGA, CRISM)". |
| Surface dust (also used by the route planner) | TES Dust Cover Index: Ruff, S. W. & Christensen, P. R. (2002). Bright and dark regions on Mars: particle size and mineralogical characteristics based on Thermal Emission Spectrometer data. *JGR Planets* 107(E12), 5127. File `dci_lo_ice_dust_16ppd_shifted.pds` (Mars Global Surveyor TES, ASU). |
| Elevation tint | Computed from the terrain above. |

## In-game numbers

| Value | Source |
|---|---|
| Radiation dose rate (0.64 mSv/day = 26.7 µSv/h, Curiosity RAD surface average) | Hassler, D. M. et al. (2014). Mars' surface radiation environment measured with the Mars Science Laboratory's Curiosity rover. *Science* 343, 1244797. |
| Dust level on the visor | TES Dust Cover Index (see Data layers above). |
| Suit oxygen: 8 h of EVA at an average ~250 W work rate; ~120 W resting, ~300 W walking, ~550 W running | Typical spacesuit life-support design length and EVA metabolic-rate ranges (approximate values, not from one source). |
| Walking speed | Game-design value (faster than a real suited astronaut so the 45 km map is playable). |

## 3D models

| Asset | Source |
|---|---|
| Astronaut, rocket and other models in `Assets/Floreswa/` | Third-party asset pack "Floreswa" — check its store page for the licence and author credit. |
| Rocket in `Assets/Aseed/Stylized Rocket/` | Third-party asset pack "Stylized Rocket" by Aseed — check its store page for the licence and author credit. |

## Software used to process the data

GDAL (reading the CTX and HiRISE rasters), Python with Pillow, NumPy and SciPy (scripts in `Tools/`), Unity.
