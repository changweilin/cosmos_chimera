# P3-02 GLO-90 terrain baker

Requires only Python 3, numpy, Pillow and tifffile already installed on the host.
No imagecodecs, GDAL, scipy, network service, or package installation is needed.

```powershell
python tools/geodata/bake.py
python tools/geodata/bake.py --offline
python tools/geodata/bake.py --regions tools/geodata/regions.json --out Data/geo-templates --cache tools/geodata/_cache --size 256 --river-fraction 0.005
python -m pytest tools/tests/test_geodata.py
```

Only requested one-degree geocells and their actually sampled interpolation halos
are downloaded. Existing TIFFs are reused. A confirmed HTTP 404 is persisted as
`*.ocean.json` (zero elevation); offline mode needs either the TIFF or this marker.
Other HTTP failures propagate. Downloads are atomic and the complete TIFF cache
must stay strictly below 300,000,000 bytes, including the previous run's cache.
The final console line reports new download count/MB and total cache count/MB.

The first full-resolution TIFF page is decoded from its tile offsets/byte counts.
stdlib zlib restores DEFLATE bytes. A per-row uint8 cumulative sum (modulo 256)
crosses the four byte planes, followed by MSB-first byte interleaving into float32.
This independently implements the inverse of TIFF Technical Note 3 / libtiff
[`fpDiff` / `fpAcc`](https://github.com/libsdl-org/libtiff/blob/master/libtiff/tif_predict.c).
It supports single-band float32 predictor-3 tiled DEFLATE only and fails on other
formats. GeoKeys must identify WGS84, geographic, PixelIsPoint; pixel scales must
span one degree. Every decoded tile is checked for finite -500..9000 m elevations.

Projection uses a sphere of radius **6371.0088 km**. Samples include both square
edges: `x = -half_extent .. +half_extent`, `y = +half_extent .. -half_extent`.
For size 256 the center lies between the four central samples. Rows run north to
south, columns west to east. Geographic source bilinear interpolation follows
PixelIsPoint tiepoints, including the dropped east/south COG edge samples; halos
are fetched from neighboring geocells and account for changing longitudinal
sample spacing. The tangent-plane origin is exactly the region center.

Priority-flood fills pits to spill height. D8 chooses greatest descent divided by
neighbor distance, with N, NE, E, SE, S, SW, W, NW breaking ties. Flat cells follow
the priority-flood parent (deterministic heap order by height then row-major cell
index). All boundary cells are outlets. Accumulation includes the cell itself;
river threshold is `ceil(size*size*river_fraction)`. This routes filled lakes
without adding an elevation epsilon. **Original resampled elevations** are stored;
the filled surface is used only for hydrology. Every receiver drains to a boundary.
Sea uses four-neighbor boundary connectivity at elevation <= 0 and overrides rivers.

Each package contains `height.r16` (row-major uint16 little-endian), `water.u8`
(0 land / 1 sea / 2 river), `meta.json`, and RGB `preview.png`. Heights map linearly
between the stored extrema. Constant DEMs use all-zero r16 and equal extrema.
`sea_level_norm = (0-min)/(max-min)` is intentionally **unclamped**: on all-land
regions it can be negative. A constant DEM uses 0. `tiles` is the sorted list of
source geocell names, including any confirmed 404 ocean cells. Source names are
not timestamps; the metadata and PNG contain no run time. Custom river thresholds
are CLI inputs and must be kept identical when comparing deterministic outputs.

The twelve region tags match the planet-type drafts. `cave` selects the surface
karst around Guilin: a heightfield cannot describe underground cavities.
`eternal_day` and `eternal_night` select Arctic terrain with seasonal polar day
and night; they are selection tags, not DEM-derived lighting or a promise of
year-round polar conditions. P3-03/P3-04 must implement the planet lighting mask.
Rivers are DEM-derived drainage proxies, including on ice, rather than observed
perennial rivers. No WorldCover/OSM water or vegetation labels are inferred.

Coverage was checked against the [official Copernicus collection description](https://dataspace.copernicus.eu/explore-data/data-collections/copernicus-contributing-missions/collections-description/COP-DEM)
and [AWS documentation](https://copernicus-dem-30m.s3.amazonaws.com/readme.html).
GLO-90 is worldwide; the AWS GLO-30 country restrictions do not apply to this
dataset. The chosen areas nevertheless avoid Armenia, Azerbaijan and Moldova.
The AWS documentation specifies zero elevation for absent ocean geocells and
explains removed south/east samples and the latitude-dependent source widths.
See [the packaged bilingual attribution](../../Data/geo-templates/ATTRIBUTION.md).
