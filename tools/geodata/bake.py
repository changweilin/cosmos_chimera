"""Deterministic GLO-90 terrain baking; see README.md for coordinates and limits."""

import argparse
from collections import deque
import heapq
import json
import math
from pathlib import Path
import re
import sys
from urllib.error import HTTPError
from urllib.request import urlopen
import zlib

import numpy as np
from PIL import Image
import tifffile

REPO = Path(__file__).resolve().parents[2]
EARTH_RADIUS_KM = 6371.0088
BASE_URL = "https://copernicus-dem-90m.s3.amazonaws.com"
MAX_CACHE_BYTES = 300_000_000
TERRAIN_TAGS = frozenset(("forest", "river_valley", "volcano", "rift_valley",
                          "ice_field", "fjord", "plateau", "archipelago",
                          "canyon", "cave", "eternal_day", "eternal_night"))
NEIGHBORS = ((-1, 0), (-1, 1), (0, 1), (1, 1),
             (1, 0), (1, -1), (0, -1), (-1, -1))


def decode_predictor3(raw, height, width):
    """Restore one single-band float32 TIFF predictor-3 tile (TIFF Tech Note 3).

    Pre: raw is DEFLATE-decoded bytes, exactly height * width * 4; dimensions > 0.
    Post: returns a native float32 array with the original IEEE-754 bit patterns.
    Side effects: allocates arrays; no I/O.
    """
    if height <= 0 or width <= 0 or len(raw) != height * width * 4:
        raise ValueError("Invalid predictor-3 tile byte count or dimensions")
    encoded = np.frombuffer(raw, dtype=np.uint8).reshape(height, width * 4)
    # Accumulation crosses byte-plane boundaries, but NEVER scanline boundaries.
    # Predictor bytes are MSB-first regardless of the TIFF directory byte order.
    planes = np.cumsum(encoded, axis=1, dtype=np.uint8).reshape(height, 4, width)
    interleaved = np.ascontiguousarray(planes.transpose(0, 2, 1))
    return interleaved.view(">f4").reshape(height, width).astype(np.float32)


def read_cog(path):
    """Read the full-resolution GLO-90 float32 COG without imagecodecs.

    Pre: path is a tiled, north-up WGS84 PixelIsPoint, DEFLATE/predictor-3 COG.
    Post: returns (finite DEM, georeferencing dict); rejects unsupported encodings.
    Side effects: reads path; allocates full image and one padded tile at a time.
    """
    with tifffile.TiffFile(path) as tif:
        page = tif.pages[0]
        if (not page.is_tiled or page.samplesperpixel != 1 or page.bitspersample != 32
                or int(page.sampleformat) != 3 or int(page.compression) not in (8, 32946)
                or int(page.predictor) != 3 or int(page.planarconfig) != 1):
            raise ValueError(f"Unsupported GLO-90 TIFF encoding: {path}")
        geo = page.geotiff_tags
        if (not geo or int(geo.get("GeographicTypeGeoKey", 0)) != 4326
                or int(geo.get("GTRasterTypeGeoKey", 0)) != 2
                or int(geo.get("GTModelTypeGeoKey", 0)) != 2
                or "ModelTransformation" in geo):
            raise ValueError(f"Expected WGS84 PixelIsPoint GeoKeys: {path}")
        dx, dy, _ = geo["ModelPixelScale"]
        tie_x, tie_y, _, tie_lon, tie_lat, _ = geo["ModelTiepoint"]
        west, north = tie_lon - tie_x * dx, tie_lat + tie_y * dy
        height, width = page.shape
        if (dx <= 0 or dy <= 0 or not math.isclose(dx * width, 1, abs_tol=1e-8)
                or not math.isclose(dy * height, 1, abs_tol=1e-8)):
            raise ValueError(f"Expected one-degree GLO-90 geocell: {path}")
        th, tw = page.tilelength, page.tilewidth
        columns = math.ceil(width / tw)
        if len(page.dataoffsets) != math.ceil(height / th) * columns:
            raise ValueError(f"Invalid TIFF tile count: {path}")
        dem = np.empty((height, width), dtype=np.float32)
        with Path(path).open("rb") as stream:
            for index, (offset, count) in enumerate(zip(page.dataoffsets, page.databytecounts)):
                stream.seek(offset)
                compressed = stream.read(count)
                if len(compressed) != count:
                    raise ValueError(f"Truncated TIFF tile: {path}")
                tile = decode_predictor3(zlib.decompress(compressed), th, tw)
                row, col = (index // columns) * th, (index % columns) * tw
                rows, cols = min(th, height - row), min(tw, width - col)
                dem[row:row + rows, col:col + cols] = tile[:rows, :cols]
        if not np.isfinite(dem).all() or dem.min() < -500 or dem.max() > 9000:
            raise ValueError(f"Invalid DEM elevations (expected -500..9000 m): {path}")
        return dem, {"west": west, "north": north, "dx": dx, "dy": dy,
                     "width": width, "height": height}


def gnomonic_inverse(center_lat, center_lon, x_km, y_km):
    """Map east/north tangent-plane coordinates to geographic degrees.

    Pre: finite coordinates; -90 < center_lat < 90; -180 <= center_lon < 180.
    Post: returns broadcast latitude/longitude arrays; origin equals the center.
    Side effects: allocates arrays; no I/O.
    """
    if not (-90 < center_lat < 90 and -180 <= center_lon < 180):
        raise ValueError("Invalid projection center")
    x, y = np.broadcast_arrays(np.asarray(x_km, float), np.asarray(y_km, float))
    if not (np.isfinite(x).all() and np.isfinite(y).all()):
        raise ValueError("Projection coordinates must be finite")
    lat0, lon0 = np.deg2rad([center_lat, center_lon])
    # The local basis avoids the rho=0 singularity in the usual inverse formula.
    length = np.sqrt(EARTH_RADIUS_KM ** 2 + x * x + y * y)
    z = (EARTH_RADIUS_KM * np.sin(lat0) + y * np.cos(lat0)) / length
    lat = np.rad2deg(np.arcsin(np.clip(z, -1, 1)))
    lon = np.rad2deg(lon0 + np.arctan2(x, EARTH_RADIUS_KM * np.cos(lat0)
                                     - y * np.sin(lat0)))
    return lat, (lon + 180) % 360 - 180


def gnomonic_grid(center_lat, center_lon, half_extent_km, size):
    """Return a north-to-south, west-to-east grid including the square edges.

    Pre: valid projection center; 20 <= half_extent_km <= 80; integer size >= 2.
    Post: returns size x size lat/lon arrays spanning exactly 2*half_extent_km.
    Side effects: allocates arrays; no I/O.
    """
    if not (20 <= half_extent_km <= 80) or type(size) is not int or size < 2:
        raise ValueError("Expected extent 20..80 km and integer size >= 2")
    axis = np.linspace(-half_extent_km, half_extent_km, size)
    x, y = np.meshgrid(axis, -axis)
    return gnomonic_inverse(center_lat, center_lon, x, y)


def bilinear_sample(dem, rows, cols):
    """Sample a finite regular raster at fractional array indices.

    Pre: finite 2D DEM (both axes >= 2); broadcast indices inside its closed bounds.
    Post: returns float64 bilinear samples; integer indices preserve source values.
    Side effects: allocates arrays; no mutation or I/O.
    """
    dem = _dem_array(dem)
    rows, cols = np.broadcast_arrays(np.asarray(rows, float), np.asarray(cols, float))
    h, w = dem.shape
    if (not np.isfinite(rows).all() or not np.isfinite(cols).all()
            or np.any(rows < 0) or np.any(rows > h - 1)
            or np.any(cols < 0) or np.any(cols > w - 1)):
        raise ValueError("Bilinear indices outside raster")
    r0, c0 = np.floor(rows).astype(int), np.floor(cols).astype(int)
    r1, c1 = np.minimum(r0 + 1, h - 1), np.minimum(c0 + 1, w - 1)
    fy, fx = rows - r0, cols - c0
    return ((1 - fy) * ((1 - fx) * dem[r0, c0] + fx * dem[r0, c1])
            + fy * ((1 - fx) * dem[r1, c0] + fx * dem[r1, c1]))


def _dem_array(dem):
    dem = np.asarray(dem, dtype=np.float64)
    if dem.ndim != 2 or min(dem.shape) < 2 or not np.isfinite(dem).all():
        raise ValueError("Expected a finite 2D DEM with both dimensions >= 2")
    return dem


def _tile_name(south, west):
    return (f"Copernicus_DSM_COG_30_{'N' if south >= 0 else 'S'}{abs(south):02d}_00_"
            f"{'E' if west >= 0 else 'W'}{abs(west):03d}_00_DEM")


class _TileStore:
    def __init__(self, cache, offline):
        self.cache, self.offline = Path(cache), offline
        self.cache.mkdir(parents=True, exist_ok=True)
        self.arrays = {}
        self.used = set()
        self.downloaded = 0
        self.downloaded_bytes = 0
        self.cache_bytes = sum(p.stat().st_size for p in self.cache.glob("*.tif"))
        if self.cache_bytes >= MAX_CACHE_BYTES:
            raise ValueError("GLO-90 cache must stay below 300 MB")

    def _fetch(self, name):
        path = self.cache / (name + ".tif")
        marker = self.cache / (name + ".ocean.json")
        url = f"{BASE_URL}/{name}/{name}.tif"
        if path.exists():
            return path
        if marker.exists():
            if json.loads(marker.read_text(encoding="utf-8")) != {"status": 404, "url": url}:
                raise ValueError(f"Invalid ocean cache marker: {marker}")
            return None
        if self.offline:
            raise FileNotFoundError(f"Offline cache missing tile: {name}")
        part = path.with_suffix(".tif.part")
        try:
            try:
                response = urlopen(url, timeout=90)
            except HTTPError as error:
                if error.code != 404:
                    raise
                marker.write_text(json.dumps({"status": 404, "url": url}, sort_keys=True)
                                  + "\n", encoding="utf-8")
                print(f"ocean (404): {name}", flush=True)
                return None
            count = 0
            with response, part.open("wb") as target:
                while chunk := response.read(1024 * 1024):
                    count += len(chunk)
                    if self.cache_bytes + count >= MAX_CACHE_BYTES:
                        raise ValueError("Download would exceed the <300 MB cache budget")
                    target.write(chunk)
            part.replace(path)
            self.cache_bytes += count
            self.downloaded += 1
            self.downloaded_bytes += count
            print(f"download: {name} ({count / 1e6:.6f} MB)", flush=True)
            return path
        finally:
            part.unlink(missing_ok=True)

    def _get(self, south, west):
        key = (south, west)
        name = _tile_name(south, west)
        self.used.add(name)
        if key not in self.arrays:
            path = self._fetch(name)
            if path is None:
                self.arrays[key] = None
            else:
                dem, geo = read_cog(path)
                if not (math.isclose(geo["west"], west, abs_tol=1e-8)
                        and math.isclose(geo["north"], south + 1, abs_tol=1e-8)):
                    raise ValueError(f"GeoTIFF bounds disagree with tile name: {name}")
                self.arrays[key] = (dem, geo)
        return self.arrays[key]

    def sample(self, lat, lon, depth=0):
        if depth > 3:
            raise ValueError("Could not resolve neighboring GLO-90 sample")
        lat, lon = np.broadcast_arrays(np.asarray(lat, float), np.asarray(lon, float))
        shape = lat.shape
        lat, lon = lat.ravel(), ((lon.ravel() + 180) % 360) - 180
        # The AWS COG drops the south/east shared samples. Integer latitudes
        # belong to the southern tile's north row; integer longitudes to the east.
        south = np.ceil(lat).astype(int) - 1
        west = np.floor(lon).astype(int)
        output = np.zeros(lat.size, dtype=float)
        for s, w in sorted(set(zip(south.tolist(), west.tolist()))):
            select = np.flatnonzero((south == s) & (west == w))
            entry = self._get(s, w)
            if entry is None:
                continue
            dem, geo = entry
            rows = (geo["north"] - lat[select]) / geo["dy"]
            cols = (lon[select] - geo["west"]) / geo["dx"]
            rows = np.where(abs(rows - np.rint(rows)) < 1e-8, np.rint(rows), rows)
            cols = np.where(abs(cols - np.rint(cols)) < 1e-8, np.rint(cols), cols)
            r0, c0 = np.floor(rows).astype(int), np.floor(cols).astype(int)
            fy, fx = rows - r0, cols - c0
            values = np.zeros(select.size)
            for dr, dc, weight in ((0, 0, (1-fy)*(1-fx)), (0, 1, (1-fy)*fx),
                                   (1, 0, fy*(1-fx)), (1, 1, fy*fx)):
                rr, cc = r0 + dr, c0 + dc
                needed = weight > 0
                inside = needed & (rr < dem.shape[0]) & (cc < dem.shape[1])
                values[inside] += weight[inside] * dem[rr[inside], cc[inside]]
                outside = needed & ~inside
                if np.any(outside):
                    # Adjacent latitude bands can have different longitude spacing;
                    # sample the halo geographically instead of clamping the edge.
                    edge_lat = geo["north"] - rr[outside] * geo["dy"]
                    edge_lon = geo["west"] + cc[outside] * geo["dx"]
                    values[outside] += weight[outside] * self.sample(edge_lat, edge_lon, depth+1)
            output[select] = values
        return output.reshape(shape)


def fill_depressions(dem):
    """Priority-flood a DEM to its minimum boundary-draining surface.

    Pre: finite 2D DEM with both dimensions >= 2.
    Post: returns (filled, parent, rank); filled >= dem, boundaries unchanged;
          each non-boundary parent is an earlier-visited D8 neighbor at <= height.
    Side effects: allocates arrays and heap; input is unchanged.
    """
    filled = _dem_array(dem).copy()
    h, w = filled.shape
    seen = np.zeros((h, w), bool)
    parent = np.full((h, w), -1, dtype=np.int64)
    rank = np.empty((h, w), dtype=np.int64)
    heap = []
    for r in range(h):
        for c in range(w):
            if r in (0, h-1) or c in (0, w-1):
                seen[r, c] = True
                heapq.heappush(heap, (filled[r, c], r*w+c))
    order = 0
    while heap:
        level, index = heapq.heappop(heap)
        r, c = divmod(index, w)
        rank[r, c] = order
        order += 1
        for dr, dc in NEIGHBORS:
            nr, nc = r+dr, c+dc
            if not (0 <= nr < h and 0 <= nc < w) or seen[nr, nc]:
                continue
            seen[nr, nc] = True
            filled[nr, nc] = max(filled[nr, nc], level)
            parent[nr, nc] = index
            heapq.heappush(heap, (filled[nr, nc], nr*w+nc))
    return filled, parent, rank


def d8_flow(dem):
    """Compute boundary-draining D8 receivers and contributing cell counts.

    Pre: finite 2D DEM with both dimensions >= 2; grid cells are square.
    Post: returns (filled, receiver, accumulation); receivers are flattened indices,
          boundary outlets are -1; each count includes itself; no cycles or pits.
    Side effects: allocates arrays; no input mutation or I/O.
    """
    filled, parent, rank = fill_depressions(dem)
    h, w = filled.shape
    receiver = parent.copy()
    for r in range(1, h-1):
        for c in range(1, w-1):
            best_slope = 0.0
            for dr, dc in NEIGHBORS:
                nr, nc = r+dr, c+dc
                slope = (filled[r, c] - filled[nr, nc]) / math.hypot(dr, dc)
                if slope > best_slope:
                    best_slope = slope
                    receiver[r, c] = nr*w+nc
            # Flat cells keep the priority-flood parent. This drains filled lakes
            # without perturbing the DEM by an arbitrary elevation epsilon.
    accumulation = np.ones(h*w, dtype=np.int64)
    order = np.lexsort((rank.ravel(), filled.ravel()))[::-1]
    for index in order:
        target = receiver.ravel()[index]
        if target >= 0:
            accumulation[target] += accumulation[index]
    return filled, receiver, accumulation.reshape(h, w)


def sea_mask(dem):
    """Find nonpositive cells connected to the image boundary by four-neighbor paths.

    Pre: finite 2D DEM with both dimensions >= 2.
    Post: returns bool mask; enclosed depressions at/below zero are not sea.
    Side effects: allocates arrays and queue; no input mutation or I/O.
    """
    below = _dem_array(dem) <= 0
    h, w = below.shape
    sea = np.zeros_like(below)
    queue = deque()
    for r in range(h):
        for c in range(w):
            if (r in (0, h-1) or c in (0, w-1)) and below[r, c]:
                sea[r, c] = True
                queue.append((r, c))
    while queue:
        r, c = queue.popleft()
        for dr, dc in ((-1, 0), (0, 1), (1, 0), (0, -1)):
            nr, nc = r+dr, c+dc
            if 0 <= nr < h and 0 <= nc < w and below[nr, nc] and not sea[nr, nc]:
                sea[nr, nc] = True
                queue.append((nr, nc))
    return sea


def write_template(region, dem, tiles, out, river_fraction=0.005):
    """Write one deterministic height/water/metadata/preview template package.

    Pre: region follows regions.json; square finite DEM >= 2; 0 < river_fraction <= 1.
    Post: writes four files under out/region.id; returns the metadata dict.
    Side effects: creates directories, replaces those files, allocates hydrology arrays.
    """
    _validate_region(region)
    dem = _dem_array(dem)
    if dem.shape[0] != dem.shape[1] or not 0 < river_fraction <= 1:
        raise ValueError("Expected square DEM and river fraction in (0, 1]")
    low, high = float(dem.min()), float(dem.max())
    span = high-low
    normalized = (dem-low)/span if span else np.zeros_like(dem)
    height = np.rint(normalized * 65535).astype("<u2")
    sea = sea_mask(dem)
    _, _, accumulation = d8_flow(dem)
    water = np.zeros(dem.shape, dtype=np.uint8)
    water[accumulation >= math.ceil(dem.size * river_fraction)] = 2
    water[sea] = 1
    meta = {"id": region["id"], "generator_version": 1, "size": dem.shape[0],
            "center_lat": region["center_lat"], "center_lon": region["center_lon"],
            "half_extent_km": region["half_extent_km"], "elev_min_m": low,
            "elev_max_m": high, "sea_level_norm": (0-low)/span if span else 0.0,
            "terrain_tags": region["terrain_tags"], "sources": ["copernicus_glo90"],
            "tiles": sorted(set(tiles))}
    target = Path(out) / region["id"]
    target.mkdir(parents=True, exist_ok=True)
    (target / "height.r16").write_bytes(height.tobytes(order="C"))
    (target / "water.u8").write_bytes(water.tobytes(order="C"))
    (target / "meta.json").write_text(json.dumps(meta, indent=2, ensure_ascii=False,
                                               allow_nan=False) + "\n", encoding="utf-8")
    spacing = 2 * region["half_extent_km"] * 1000 / (dem.shape[0]-1)
    dy, dx = np.gradient(dem, spacing)
    light = np.array([-1, -1, 1]) / math.sqrt(3)
    shade = np.clip((-dx*light[0] - dy*light[1] + light[2])
                    / np.sqrt(1+dx*dx+dy*dy), 0, 1)
    base = np.stack((0.30+0.45*normalized, 0.42+0.35*normalized,
                     0.22+0.48*normalized), axis=-1)
    rgb = np.rint(np.clip(base*(0.35+0.65*shade[..., None]), 0, 1)*255).astype(np.uint8)
    rgb[water == 1] = (30, 95, 155)
    rgb[water == 2] = (35, 165, 225)
    Image.fromarray(rgb).save(target / "preview.png", compress_level=9)
    return meta


def _validate_region(region):
    if not isinstance(region, dict):
        raise ValueError("Region must be an object")
    if (not isinstance(region.get("id"), str)
            or not re.fullmatch(r"[a-z][a-z0-9]*(?:_[a-z0-9]+)*", region["id"])):
        raise ValueError("Region id must be snake_case")
    if not isinstance(region.get("name_en"), str) or not region["name_en"].strip():
        raise ValueError("Region needs name_en")
    for key, lo, hi in (("center_lat", -89, 89), ("center_lon", -180, 180),
                        ("half_extent_km", 20, 80)):
        value = region.get(key)
        if type(value) not in (int, float) or not math.isfinite(value) or not lo <= value <= hi:
            raise ValueError(f"Invalid region {key}")
    if region["center_lon"] == 180:
        raise ValueError("center_lon must be in [-180, 180)")
    tags = region.get("terrain_tags")
    if (not isinstance(tags, list) or not tags or any(type(t) is not str for t in tags)
            or len(set(tags)) != len(tags) or not set(tags) <= TERRAIN_TAGS):
        raise ValueError("Invalid region terrain_tags")
    # No polar crossing: the one-degree source grid needs a defined east/north basis.
    if abs(region["center_lat"]) + math.degrees(math.atan(
            math.sqrt(2)*region["half_extent_km"]/EARTH_RADIUS_KM)) >= 90:
        raise ValueError("Region crosses a geographic pole")


def main(argv=None):
    """Bake selected GLO-90 regions from the command line.

    Pre: argv follows --help; required Python dependencies are installed.
    Post: returns 0 on success; errors propagate with a diagnostic and nonzero exit.
    Side effects: reads configuration/cache, downloads missing tiles unless offline,
                  writes template packages and prints download/elevation/water statistics.
    """
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--regions", type=Path, default=Path(__file__).with_name("regions.json"))
    parser.add_argument("--out", type=Path, default=REPO / "Data/geo-templates")
    parser.add_argument("--cache", type=Path, default=Path(__file__).with_name("_cache"))
    parser.add_argument("--size", type=int, default=256)
    parser.add_argument("--offline", action="store_true")
    parser.add_argument("--river-fraction", type=float, default=0.005,
                        help="minimum contributing cells / total cells (default: 0.005)")
    args = parser.parse_args(argv)
    if args.size < 2 or not 0 < args.river_fraction <= 1:
        parser.error("size must be >= 2; river-fraction must be in (0, 1]")
    regions = json.loads(args.regions.read_text(encoding="utf-8"))
    if not isinstance(regions, list) or not regions:
        raise ValueError("regions.json must contain a nonempty array")
    for region in regions:
        _validate_region(region)
    if len({r["id"] for r in regions}) != len(regions):
        raise ValueError("Duplicate region id")
    store = _TileStore(args.cache, args.offline)
    for region in regions:
        store.used.clear()
        store.arrays.clear()
        lat, lon = gnomonic_grid(region["center_lat"], region["center_lon"],
                                 region["half_extent_km"], args.size)
        dem = store.sample(lat, lon)
        meta = write_template(region, dem, store.used, args.out, args.river_fraction)
        water = np.fromfile(args.out / region["id"] / "water.u8", dtype=np.uint8)
        print(f"{region['id']}: {meta['elev_min_m']:.3f}..{meta['elev_max_m']:.3f} m; "
              f"sea={np.mean(water == 1):.3%}; river={np.mean(water == 2):.3%}; "
              f"tiles={len(store.used)}", flush=True)
    print(f"downloaded={store.downloaded} tiles / {store.downloaded_bytes / 1e6:.6f} MB; "
          f"cache={len(list(args.cache.glob('*.tif')))} tiles / {store.cache_bytes / 1e6:.6f} MB",
          flush=True)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, OSError, zlib.error) as error:
        print(f"geodata: {error}", file=sys.stderr)
        sys.exit(1)
