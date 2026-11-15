# MarsSources

Raw source data. **The big files are not in git** (they are public and too large for GitHub).
The Unity project does **not** need them to run: the terrain heightmap and color texture are already
converted and committed in `Assets/MarsData/`. You only need these files to re-crop a new area or
regenerate data with the tools in `Tools/`.

## Committed here
- `Rover Drive Path.geojson`, `Rover Waypoints.geojson`, `Sample Depot Zone.geojson`, `Sampling Locations.geojson`
  (Mars 2020 route data used for the overlays and the rover tracks)
- `m20_heli_waypoints.json`, `m20_heli_flight_path.json`: Ingenuity's 72 flights and airfields, from NASA's Mars 2020
  mission map (`https://mars.nasa.gov/mmgis-maps/M20/Layers/json/<name>`). The waypoints feed the Ingenuity points
  of interest; the flight path isn't drawn in the game yet.

## Points of interest
- `Tools/PoiPhotos/fetch_poi_photos.py` -> `Assets/MarsData/POI/three_forks_poi.json` (Three Forks sites and depot tubes,
  photos picked by eye).
- `Tools/PoiPhotos/make_jezero_pois.py` -> `Assets/MarsData/POI/jezero_poi.json`: the rest of the map: the other
  sampling sites, landing site, Van Zyl Overlook, Perseverance's latest position, Ingenuity's airfields and the
  IAU-named features inside the map (USGS Gazetteer of Planetary Nomenclature). Photos are picked automatically
  from NASA's raw-image feed. In Unity, run "Build Points Of Interest" on the "Jezero Points of Interest" object
  after regenerating.

## Current map: Jezero crater, 77.0–77.8°E, 17.7–18.7°N (not committed)
Needed only to rebuild it with `Tools/RegionBuilder/build_region.py`:

| File | Size | What it is |
|---|---|---|
| `M20_JezeroCrater_CTXDEM_20m.tif` | 90 MB | Mars 2020 CTX elevation model, 20 m/px, covers 77.00–78.58°E, 17.58–19.29°N |
| `M20_JezeroCrater_CTXortho_mosaic_5m.tif` | 360 MB | Matching CTX photo mosaic (greyscale), 5 m/px |

```bash
cd MarsSources
B=https://asc-pds-services.s3.us-west-2.amazonaws.com/mosaic/mars2020_trn/CTX/ScienceInvestigationMaps_JPL
curl -L -C - -O $B/M20_JezeroCrater_CTXDEM_20m.tif
curl -L -C - -O $B/M20_JezeroCrater_CTXortho_mosaic_5m.tif
```

## Previous map (Three Forks HiRISE, 1.25 km) sources (not committed)
Put each file directly in this folder with the same name.

| File | Size | What it is |
|---|---|---|
| `DTEEC_045994_1985_046060_1985_U01.IMG` | 381 MB | HiRISE DTM (heights), 1 m/px |
| `ESP_046060_1985_RED_C_01_ORTHO.JP2` | 34 MB | Orthoimage aligned to the DTM (used for registration) |
| `ESP_045994_1985_MRGB.JP2` | 202 MB | HiRISE merged color (alternative color) |
| `ESP_083512_1985_RED.JP2` | 546 MB | HiRISE 2024 image (optional, tried for rover tracks) |

```bash
cd MarsSources
curl -L -C - -O https://hirise.lpl.arizona.edu/PDS/DTM/ESP/ORB_045900_045999/ESP_045994_1985_ESP_046060_1985/DTEEC_045994_1985_046060_1985_U01.IMG
curl -L -C - -O https://hirise.lpl.arizona.edu/PDS/DTM/ESP/ORB_045900_045999/ESP_045994_1985_ESP_046060_1985/ESP_046060_1985_RED_C_01_ORTHO.JP2
curl -L -C - -O https://hirise.lpl.arizona.edu/PDS/EXTRAS/RDR/ESP/ORB_045900_045999/ESP_045994_1985/ESP_045994_1985_MRGB.JP2
curl -L -C - -O https://hirise.lpl.arizona.edu/PDS/RDR/ESP/ORB_083500_083599/ESP_083512_1985/ESP_083512_1985_RED.JP2
```
(`-C -` resumes an interrupted download.)

The color texture in Unity (`Assets/MarsData/Textures/jezero_color.png`) comes from the USGS Mars 2020 TRN
HiRISE orthomosaic, read remotely with GDAL (no download needed):
`https://asc-pds-services.s3.us-west-2.amazonaws.com/mosaic/mars2020_trn/HiRISE/JEZ_hirise_soc_006_orthoMosaic_25cm_Eqc_latTs0_lon0_first.tif`

`jezero_color_HiRISE_ESP_083512_2024.png` was a texture exported from the 2024 image; it is not used.
