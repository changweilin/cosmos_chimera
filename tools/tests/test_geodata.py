"""Offline acceptance tests for the P3-02 terrain baker."""

import hashlib
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch
from urllib.error import HTTPError
import zlib

import numpy as np
from PIL import Image
import tifffile

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "geodata"))
import bake  # noqa: E402


def _encode_predictor3(dem):
    # Independent inverse: big-endian byte planes, then a backwards byte diff.
    height, width = dem.shape
    raw = dem.astype(">f4").tobytes()
    samples = np.frombuffer(raw, dtype=np.uint8).reshape(height, width, 4)
    planes = samples.transpose(0, 2, 1).copy().reshape(height, width*4)
    diff = planes.copy()
    diff[:, 1:] = np.subtract(planes[:, 1:], planes[:, :-1], dtype=np.uint8)
    return diff.tobytes()


def _write_cog(path, dem, south=0, west=0, byteorder="<"):
    # A minimal TIFF fixture assembled with stdlib struct: no codec dependency,
    # no patching tifffile internals, and multiple padded tiles exercised.
    height, width = dem.shape
    tile_size = 16
    payloads = []
    for r in range(0, height, tile_size):
        for c in range(0, width, tile_size):
            tile = np.zeros((tile_size, tile_size), dtype=np.float32)
            part = dem[r:r+tile_size, c:c+tile_size]
            tile[:part.shape[0], :part.shape[1]] = part
            payloads.append(zlib.compress(_encode_predictor3(tile)))
    keys = (1, 1, 0, 3, 1024, 0, 1, 2, 1025, 0, 1, 2, 2048, 0, 1, 4326)
    tags = [(256, 4, (width,)), (257, 4, (height,)), (258, 3, (32,)),
            (259, 3, (8,)), (262, 3, (1,)), (277, 3, (1,)), (284, 3, (1,)),
            (317, 3, (3,)), (322, 4, (tile_size,)), (323, 4, (tile_size,)),
            (324, 4, tuple(0 for _ in payloads)),
            (325, 4, tuple(map(len, payloads))), (339, 3, (3,)),
            (33550, 12, (1/width, 1/height, 0)),
            (33922, 12, (0, 0, 0, west, south+1, 0)), (34735, 3, keys)]
    tags.sort()
    start = 8 + 2 + len(tags)*12 + 4
    extra_size = sum(len(values)*{3: 2, 4: 4, 12: 8}[kind]
                     for _, kind, values in tags
                     if len(values)*{3: 2, 4: 4, 12: 8}[kind] > 4)
    position = start + extra_size
    offsets = []
    for payload in payloads:
        offsets.append(position)
        position += len(payload)
    directory, extra = bytearray(), bytearray()
    for tag, kind, values in tags:
        if tag == 324:
            values = offsets
        data = struct.pack(byteorder + {3: "H", 4: "I", 12: "d"}[kind]*len(values), *values)
        directory.extend(struct.pack(byteorder+"HHI", tag, kind, len(values)))
        if len(data) <= 4:
            directory.extend(data.ljust(4, b"\0"))
        else:
            directory.extend(struct.pack(byteorder+"I", start+len(extra)))
            extra.extend(data)
    header = (b"II" if byteorder == "<" else b"MM") + struct.pack(byteorder+"HI", 42, 8)
    path.write_bytes(header + struct.pack(byteorder+"H", len(tags)) + directory
                     + struct.pack(byteorder+"I", 0) + extra + b"".join(payloads))


def _region():
    return {"id": "synthetic_region", "name_en": "Synthetic region", "center_lat": 0.5,
            "center_lon": 0.5, "half_extent_km": 20, "terrain_tags": ["river_valley"]}


def _digest_tree(root):
    return {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(root.rglob("*")) if p.is_file()}


class Predictor3Tests(unittest.TestCase):
    def test_round_trip_is_bit_exact_including_ieee_special_values(self):
        rng = np.random.default_rng(321)
        for height, width in ((1, 1), (3, 7), (17, 31), (16, 16)):
            bits = rng.integers(0, 2**32, size=(height, width), dtype=np.uint32)
            special = [0, 0x80000000, 0x7f800000, 0xff800000, 0x7fc12345,
                       0x00000001, 0x7f7fffff]
            bits.ravel()[:min(bits.size, len(special))] = special[:bits.size]
            source = bits.view(np.float32)
            restored = bake.decode_predictor3(_encode_predictor3(source), height, width)
            np.testing.assert_array_equal(restored.view(np.uint32), bits)

    def test_accumulation_crosses_planes_but_resets_at_each_row(self):
        source = np.array([[1.25, -2.5, 8900.125], [0.0, -0.0, -499.5]], np.float32)
        actual = bake.decode_predictor3(_encode_predictor3(source), 2, 3)
        self.assertEqual(actual.tobytes(), source.tobytes())

    def test_invalid_byte_counts_and_dimensions_fail(self):
        for raw, h, w in ((b"", 0, 1), (b"abc", 1, 1), (b"12345678", 1, 1)):
            with self.assertRaises(ValueError):
                bake.decode_predictor3(raw, h, w)

    def test_multitile_cog_crops_padding_and_ignores_directory_endianness(self):
        source = np.arange(19*23, dtype=np.float32).reshape(19, 23) / 4 - 20
        with tempfile.TemporaryDirectory() as tmp:
            for order in ("<", ">"):
                path = Path(tmp)/"fixture.tif"
                _write_cog(path, source, south=47, west=-124, byteorder=order)
                actual, geo = bake.read_cog(path)
                np.testing.assert_array_equal(actual, source)
                self.assertEqual((geo["west"], geo["north"]), (-124, 48))
                self.assertAlmostEqual(geo["dx"]*23, 1)
                with tifffile.TiffFile(path) as tif:
                    self.assertEqual(tif.pages[0].geotiff_tags["GeographicTypeGeoKey"], 4326)

    def test_corrupt_or_unsupported_cog_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp)/"fixture.tif"
            tifffile.imwrite(path, np.ones((16, 16), np.float32))
            with self.assertRaisesRegex(ValueError, "Unsupported"):
                bake.read_cog(path)
            _write_cog(path, np.full((16, 16), 9999, np.float32))
            with self.assertRaisesRegex(ValueError, "elevations"):
                bake.read_cog(path)
            path.write_bytes(path.read_bytes()[:-5])
            with self.assertRaisesRegex(ValueError, "Truncated"):
                bake.read_cog(path)


class ProjectionTests(unittest.TestCase):
    def test_center_edges_orientation_and_angular_distance(self):
        lat, lon = bake.gnomonic_grid(0, 0, 20, 5)
        self.assertEqual(lat[2, 2], 0)
        self.assertEqual(lon[2, 2], 0)
        angle = np.rad2deg(np.arctan(20/bake.EARTH_RADIUS_KM))
        self.assertAlmostEqual(lat[0, 2], angle)
        self.assertAlmostEqual(lon[2, 0], -angle)
        self.assertTrue(np.all(np.diff(lat, axis=0) < 0))
        self.assertTrue(np.all(np.diff(lon, axis=1) > 0))
        a, b = bake.gnomonic_inverse(78.2, 15.5, 0, 0)
        self.assertAlmostEqual(float(a), 78.2)
        self.assertAlmostEqual(float(b), 15.5)

    def test_round_trip_to_forward_gnomonic_at_high_latitude_and_dateline(self):
        for lat0, lon0 in ((64, -16), (78, 179.9), (-45, -179.9)):
            lat, lon = bake.gnomonic_grid(lat0, lon0, 80, 9)
            p, l, p0 = np.deg2rad(lat), np.deg2rad(lon-lon0), np.deg2rad(lat0)
            denominator = np.sin(p0)*np.sin(p) + np.cos(p0)*np.cos(p)*np.cos(l)
            x = bake.EARTH_RADIUS_KM*np.cos(p)*np.sin(l)/denominator
            y = bake.EARTH_RADIUS_KM*(np.cos(p0)*np.sin(p)
                - np.sin(p0)*np.cos(p)*np.cos(l))/denominator
            expected_x, expected_y = np.meshgrid(np.linspace(-80, 80, 9), np.linspace(80, -80, 9))
            np.testing.assert_allclose(x, expected_x, atol=1e-9)
            np.testing.assert_allclose(y, expected_y, atol=1e-9)

    def test_projection_contracts(self):
        for args in ((90, 0, 20, 5), (0, 180, 20, 5), (0, 0, 19, 5), (0, 0, 20, 1)):
            with self.assertRaises(ValueError):
                bake.gnomonic_grid(*args)

    def test_bilinear_plane_and_exact_edges(self):
        rows, cols = np.indices((4, 5))
        plane = 3*rows+2*cols-7
        r, c = np.array([0, 1.25, 3]), np.array([0, 2.5, 4])
        np.testing.assert_allclose(bake.bilinear_sample(plane, r, c), 3*r+2*c-7)
        for r, c in ((-0.1, 0), (0, 5), (np.nan, 0)):
            with self.assertRaises(ValueError):
                bake.bilinear_sample(plane, r, c)


class TileSamplingTests(unittest.TestCase):
    def test_neighbor_halos_and_different_latitude_band_widths(self):
        with tempfile.TemporaryDirectory() as tmp:
            cache = Path(tmp)
            for south, width in ((0, 32), (-1, 16)):
                for west in (0, 1):
                    rr, cc = np.indices((32, width))
                    # Geographic plane remains linear across geocells with different dx.
                    dem = (20*(south+1-rr/32)+10*(west+cc/width)+100).astype(np.float32)
                    _write_cog(cache/(bake._tile_name(south, west)+".tif"), dem, south, west)
            store = bake._TileStore(cache, True)
            lat = np.array([0.01, -0.01, 0, 0.7, 0.01])
            lon = np.array([0.99, 0.99, 1, 0.995, 1.001])
            np.testing.assert_allclose(store.sample(lat, lon), 20*lat+10*lon+100, atol=1e-5)
            self.assertEqual(len(store.used), 4)

    def test_offline_missing_error_and_persisted_404_ocean(self):
        with tempfile.TemporaryDirectory() as tmp:
            store = bake._TileStore(tmp, True)
            with self.assertRaisesRegex(FileNotFoundError, "Offline cache missing"):
                store.sample([0.5], [0.5])
            store = bake._TileStore(tmp, False)
            with patch.object(bake, "urlopen", side_effect=HTTPError("url", 404, "", {}, None)):
                np.testing.assert_array_equal(store.sample([0.5], [0.5]), [0])
            with patch.object(bake, "urlopen", side_effect=AssertionError("network forbidden")):
                np.testing.assert_array_equal(bake._TileStore(tmp, True).sample([0.5], [0.5]), [0])

    def test_server_errors_are_not_ocean(self):
        with tempfile.TemporaryDirectory() as tmp:
            store = bake._TileStore(tmp, False)
            with patch.object(bake, "urlopen", side_effect=HTTPError("url", 503, "", {}, None)):
                with self.assertRaises(HTTPError):
                    store.sample([0.5], [0.5])
            self.assertEqual(list(Path(tmp).iterdir()), [])


class HydrologyTests(unittest.TestCase):
    def test_fill_known_pit_without_changing_boundary(self):
        dem = np.array([[8, 8, 8, 8, 8], [8, 1, 1, 1, 8], [8, 1, 0, 1, 4],
                        [8, 1, 1, 1, 8], [8, 8, 8, 8, 8]], float)
        expected = dem.copy()
        expected[1:4, 1:4] = 4
        filled, _, _ = bake.fill_depressions(dem)
        np.testing.assert_array_equal(filled, expected)
        self.assertEqual(dem[2, 2], 0)

    def test_known_d8_receivers_and_counts(self):
        dem = np.tile([3, 2, 1], (3, 1))
        filled, receivers, accumulation = bake.d8_flow(dem)
        np.testing.assert_array_equal(filled, dem)
        np.testing.assert_array_equal(receivers, [[-1, -1, -1], [-1, 5, -1], [-1, -1, -1]])
        np.testing.assert_array_equal(accumulation, [[1, 1, 1], [1, 1, 2], [1, 1, 1]])

    def test_diagonal_steepest_descent_is_distance_weighted(self):
        dem = np.array([[10, 10, 0], [10, 10, 2], [10, 10, 10]], float)
        _, receivers, _ = bake.d8_flow(dem)
        self.assertEqual(receivers[1, 1], 5)  # 8 > 10/sqrt(2)

    def test_flats_and_pits_drain_without_cycles_and_conserve_area(self):
        for dem in (np.ones((7, 7)), np.pad(np.zeros((5, 5)), 1, constant_values=4)):
            filled, receiver, accumulation = bake.d8_flow(dem)
            flat = receiver.ravel()
            self.assertEqual(accumulation.ravel()[flat < 0].sum(), dem.size)
            for start in range(dem.size):
                seen, cell = set(), start
                while cell >= 0:
                    self.assertNotIn(cell, seen)
                    seen.add(cell)
                    target = flat[cell]
                    if target >= 0:
                        self.assertGreaterEqual(filled.ravel()[cell], filled.ravel()[target])
                        self.assertGreater(accumulation.ravel()[target], accumulation.ravel()[cell])
                    cell = target

    def test_only_boundary_connected_nonpositive_cells_are_sea(self):
        dem = np.ones((5, 5))
        dem[0, 0] = dem[1, 0] = dem[2, 0] = 0
        dem[2, 2] = -2
        mask = bake.sea_mask(dem)
        self.assertEqual(mask.sum(), 3)
        self.assertFalse(mask[2, 2])
        self.assertTrue(bake.sea_mask(np.zeros((2, 2))).all())
        self.assertFalse(bake.sea_mask(np.ones((2, 2))).any())

    def test_invalid_dems_fail(self):
        for dem in ([1, 2], [[np.nan, 0], [0, 0]], [[1]]):
            for function in (bake.fill_depressions, bake.d8_flow, bake.sea_mask):
                with self.assertRaises(ValueError):
                    function(dem)


class PackageTests(unittest.TestCase):
    def test_format_quantization_and_repeated_bytes(self):
        dem = np.array([[-20, 0, 10], [10, 15, 30], [20, 25, 45]], float)
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            meta = bake.write_template(_region(), dem, ["b", "a", "a"], root)
            first = _digest_tree(root)
            bake.write_template(_region(), dem, ["a", "b"], root)
            self.assertEqual(first, _digest_tree(root))
            target = root/_region()["id"]
            data = (target/"height.r16").read_bytes()
            self.assertEqual(len(data), 18)
            self.assertEqual(data[:2], b"\0\0")
            self.assertEqual(data[-2:], b"\xff\xff")
            decoded = np.frombuffer(data, dtype="<u2").reshape(3, 3)/65535*65-20
            np.testing.assert_allclose(decoded, dem, atol=65/65535/2)
            self.assertEqual(len((target/"water.u8").read_bytes()), 9)
            self.assertEqual(meta["tiles"], ["a", "b"])
            self.assertEqual(meta["sources"], ["copernicus_glo90"])
            self.assertAlmostEqual(meta["sea_level_norm"], 20/65)
            self.assertEqual(json.loads((target/"meta.json").read_text()), meta)
            with Image.open(target/"preview.png") as image:
                self.assertEqual((image.mode, image.size), ("RGB", (3, 3)))

    def test_flat_all_land_and_all_sea_have_finite_metadata(self):
        with tempfile.TemporaryDirectory() as tmp:
            for value in (0, 10):
                meta = bake.write_template(_region(), np.full((3, 3), value), [], tmp)
                self.assertEqual(meta["elev_min_m"], meta["elev_max_m"])
                self.assertTrue(np.isfinite(meta["sea_level_norm"]))
                data = (Path(tmp)/_region()["id"]/"height.r16").read_bytes()
                self.assertEqual(data, b"\0"*18)

    def test_cli_offline_twice_on_synthetic_cog(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            cache = root/"cache"
            cache.mkdir()
            dem = np.arange(32*32, dtype=np.float32).reshape(32, 32)
            _write_cog(cache/(bake._tile_name(0, 0)+".tif"), dem)
            regions = root/"regions.json"
            regions.write_text(json.dumps([_region()]))
            args = ["--regions", str(regions), "--out", str(root/"out"), "--cache", str(cache),
                    "--size", "17", "--offline"]
            with patch.object(bake, "urlopen", side_effect=AssertionError("network forbidden")):
                self.assertEqual(bake.main(args), 0)
                first = _digest_tree(root/"out")
                self.assertEqual(bake.main(args), 0)
                self.assertEqual(first, _digest_tree(root/"out"))

    def test_region_tag_coverage_matches_planet_drafts(self):
        regions = json.loads((REPO/"tools/geodata/regions.json").read_text())
        required = set()
        for path in (REPO/"Data/_incoming/p3-planet-types-draft-01/planet-types").glob("*.json"):
            for planet in json.loads(path.read_text(encoding="utf-8")):
                required.update(planet["terrain_tags"])
        self.assertEqual(len(required), 12)
        self.assertGreaterEqual(len(regions), 12)
        self.assertEqual(len({r["id"] for r in regions}), len(regions))
        self.assertEqual({tag for r in regions for tag in r["terrain_tags"]}, required)
        for region in regions:
            bake._validate_region(region)

    def test_invalid_region_and_water_threshold_fail(self):
        with tempfile.TemporaryDirectory() as tmp:
            for change in ({"id": "../escape"}, {"terrain_tags": ["unknown"]},
                           {"center_lat": float("nan")}, {"half_extent_km": 10}):
                with self.assertRaises(ValueError):
                    bake.write_template(_region() | change, np.ones((3, 3)), [], tmp)
            with self.assertRaises(ValueError):
                bake.write_template(_region(), np.ones((3, 3)), [], tmp, 0)


if __name__ == "__main__":
    unittest.main()
