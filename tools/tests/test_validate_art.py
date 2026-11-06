"""Acceptance tests for tools/validate_art.py (P5-09) against tools/tests/fixtures/art.

Run: python -m unittest discover -s tools/tests
Fixtures carry no binaries: every listed .png is generated here (stdlib zlib + struct) after copying the
fixture to a temp dir, so the repo is never modified and the PNG parser is exercised on real files.
"""
import io
import json
import shutil
import struct
import sys
import tempfile
import unittest
import zlib
from contextlib import redirect_stdout
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))
import validate_art  # noqa: E402

FIXTURES = Path(__file__).resolve().parent / "fixtures" / "art"


def png_bytes(width: int, height: int) -> bytes:
    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    raw = b"".join(b"\x00" + b"\x00" * width for _ in range(height))
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 0, 0, 0, 0)
    return validate_art.PNG_SIGNATURE + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


class Package:
    """A fixture copied into a temp repo root: <tmp>/repo/Art/_incoming/<task_id>/."""

    def __init__(self, fixture: Path, make_pngs: bool = True):
        self._tmp = tempfile.TemporaryDirectory()
        self.repo = Path(self._tmp.name) / "repo"
        self.dir = self.repo / "Art" / "_incoming" / fixture.name
        shutil.copytree(fixture, self.dir)
        if make_pngs:
            for entry in self.manifest().get("files", []):
                p = entry.get("path") if isinstance(entry, dict) else None
                if isinstance(p, str) and p.endswith(".png") and validate_art._bad_path(p) is None:
                    size = (1024, 1024) if entry.get("kind") == "texture" else (64, 32)
                    self.write(p, png_bytes(*size))

    def manifest(self) -> dict:
        try:
            return json.loads((self.dir / "manifest.json").read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return {}

    def save_manifest(self, m: dict) -> None:
        (self.dir / "manifest.json").write_text(json.dumps(m), encoding="utf-8")

    def write(self, rel: str, data: bytes) -> None:
        p = self.dir / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)

    def add_listed(self, rel: str, data: bytes, kind: str = "view") -> None:
        m = self.manifest()
        m["files"].append({"path": rel, "kind": kind, "level": "L1"})
        self.save_manifest(m)
        self.write(rel, data)

    def codes(self) -> set:
        return {f.code for f in validate_art.validate(self.dir).findings}

    def run(self, *args) -> tuple[int, str]:
        buf = io.StringIO()
        with redirect_stdout(buf):
            code = validate_art.main([str(self.dir), "--repo-root", str(self.repo), *args])
        return code, buf.getvalue()

    def close(self):
        self._tmp.cleanup()


def valid(name_prefix: str) -> Path:
    return next(p for p in (FIXTURES / "valid").iterdir() if p.name.startswith(name_prefix + "-"))


class ValidFixtures(unittest.TestCase):
    def test_every_valid_fixture_is_compliant(self):
        fixtures = sorted((FIXTURES / "valid").iterdir())
        self.assertEqual(7, len(fixtures), "one fixture per task type")
        for fixture in fixtures:
            with self.subTest(fixture=fixture.name):
                pkg = Package(fixture)
                try:
                    findings = validate_art.validate(pkg.dir).findings
                    self.assertEqual([], [str(f) for f in findings])
                finally:
                    pkg.close()

    def test_png_sizes_are_reported(self):
        pkg = Package(valid("chr"))
        try:
            code, out = pkg.run()
            self.assertEqual(0, code)
            self.assertIn("images/chr_7f3a_main.png  64x32", out)
        finally:
            pkg.close()


class InvalidFixtures(unittest.TestCase):
    def test_each_fixture_reports_the_code_it_is_named_after(self):
        for fixture in sorted((FIXTURES / "invalid").iterdir()):
            code = fixture.name
            task = next(fixture.iterdir())
            with self.subTest(code=code):
                pkg = Package(task, make_pngs=code != "file_not_found")
                try:
                    self.assertIn(code, pkg.codes())
                finally:
                    pkg.close()


class GeneratedDefects(unittest.TestCase):
    def setUp(self):
        self.pkg = Package(valid("chr"))

    def tearDown(self):
        self.pkg.close()

    def test_corrupt_png(self):
        self.pkg.write("images/chr_7f3a_main.png", b"not a png at all, just text padding....")
        self.assertIn("png_invalid", self.pkg.codes())

    def test_bad_ihdr_crc(self):
        data = bytearray(png_bytes(8, 8))
        data[30] ^= 0xFF
        self.pkg.write("images/chr_7f3a_main.png", bytes(data))
        self.assertIn("png_invalid", self.pkg.codes())

    def test_texture_must_be_square_power_of_two(self):
        self.pkg.add_listed("images/chr_7f3a_front_tex.png", png_bytes(512, 512), kind="texture")
        self.assertIn("texture_size", self.pkg.codes())

    def test_non_texture_images_have_no_size_rule(self):
        self.pkg.write("images/chr_7f3a_main.png", png_bytes(3, 7))
        self.assertEqual(set(), self.pkg.codes())

    def test_texture_of_accepted_size_passes_size_rule(self):
        self.pkg.add_listed("images/chr_7f3a_props.png", png_bytes(2048, 2048), kind="texture")
        self.assertEqual(set(), self.pkg.codes())

    def test_uppercase_name(self):
        self.pkg.add_listed("images/chr_7f3a_Main.png", png_bytes(4, 4))
        self.assertIn("name_not_snake_case", self.pkg.codes())

    def test_jpeg_is_rejected(self):
        self.pkg.add_listed("images/chr_7f3a_side.jpg", b"\xff\xd8\xff")
        self.assertIn("image_format", self.pkg.codes())

    def test_wrong_prefix(self):
        self.pkg.add_listed("images/cr_7f3a_side.png", png_bytes(4, 4))
        self.assertIn("name_prefix", self.pkg.codes())

    def test_unknown_view(self):
        self.pkg.add_listed("images/chr_7f3a_closeup.png", png_bytes(4, 4))
        self.assertIn("name_pattern", self.pkg.codes())

    def test_invalid_json(self):
        self.pkg.write("concept.json", b"{ nope")
        self.assertIn("json_invalid", self.pkg.codes())


class Apply(unittest.TestCase):
    def test_apply_moves_compliant_package(self):
        pkg = Package(valid("cr"))
        try:
            m = pkg.manifest()
            m["files"] = [f for f in m["files"] if f["path"] in ("concept.json", "images/cr_line_7f3a_juvenile_main.png")]
            pkg.save_manifest(m)
            for p in list(pkg.dir.rglob("*.png")):
                if p.name != "cr_line_7f3a_juvenile_main.png":
                    p.unlink()

            code, out = pkg.run("--apply")

            self.assertEqual(0, code, out)
            self.assertTrue((pkg.repo / "Data/_incoming" / pkg.dir.name / "concept.json").is_file())
            self.assertTrue((pkg.repo / "Assets/_Project/Art/Creatures/line_7f3a/juvenile/Concept/"
                             "cr_line_7f3a_juvenile_main.png").is_file())
            self.assertFalse((pkg.dir / "concept.json").exists())
        finally:
            pkg.close()

    def test_dry_run_moves_nothing(self):
        pkg = Package(valid("chr"))
        try:
            code, out = pkg.run("--apply", "--dry-run")
            self.assertEqual(0, code, out)
            self.assertIn("move  images/chr_7f3a_main.png -> Assets/_Project/Art/Characters/chr_7f3a/Concept/", out)
            self.assertTrue((pkg.dir / "images/chr_7f3a_main.png").is_file())
            self.assertFalse((pkg.repo / "Assets").exists())
        finally:
            pkg.close()

    def test_collision_refuses_and_moves_nothing(self):
        pkg = Package(valid("chr"))
        try:
            taken = pkg.repo / "Assets/_Project/Art/Characters/chr_7f3a/Concept/chr_7f3a_front.png"
            taken.parent.mkdir(parents=True)
            taken.write_bytes(b"existing")

            code, out = pkg.run("--apply")

            self.assertEqual(1, code)
            self.assertIn("destination_exists", out)
            self.assertEqual(b"existing", taken.read_bytes())
            self.assertTrue((pkg.dir / "images/chr_7f3a_main.png").is_file(), "all-or-nothing")
            self.assertFalse((pkg.repo / "Data").exists())
        finally:
            pkg.close()

    def test_blocked_parent_refuses(self):
        pkg = Package(valid("chr"))
        try:
            blocker = pkg.repo / "Assets/_Project/Art/Characters"
            blocker.parent.mkdir(parents=True)
            blocker.write_bytes(b"file where a folder should be")
            code, out = pkg.run("--apply")
            self.assertEqual(1, code)
            self.assertIn("destination_blocked", out)
        finally:
            pkg.close()

    def test_types_without_destination_rule_refuse(self):
        for prefix in ("mdl", "anim", "fx"):
            with self.subTest(task=prefix):
                pkg = Package(valid(prefix))
                try:
                    code, out = pkg.run("--apply")
                    self.assertEqual(1, code)
                    self.assertIn("no_destination_rule", out)
                    self.assertFalse((pkg.repo / "Assets").exists())
                finally:
                    pkg.close()

    def test_line_level_creature_image_has_no_destination(self):
        pkg = Package(valid("cr"))
        try:
            code, out = pkg.run("--apply")
            self.assertEqual(1, code)
            self.assertIn("[no_destination_rule] images/cr_line_7f3a_lineup.png", out)
            self.assertTrue((pkg.dir / "images/cr_line_7f3a_juvenile_main.png").is_file())
        finally:
            pkg.close()

    def test_findings_block_apply(self):
        pkg = Package(valid("chr"))
        try:
            pkg.write("concept.json", b"{ nope")
            code, _ = pkg.run("--apply")
            self.assertEqual(1, code)
            self.assertFalse((pkg.repo / "Assets").exists())
        finally:
            pkg.close()


class ModelRigPipeline(unittest.TestCase):
    """model-rig@2: image-to-3D, rig and animation in one package; batch packages carry batch_id."""

    BATCH = "mdl-20261106-7f3a-2105"

    def batch_package(self, folder: str, batch_id) -> Package:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        src = Path(tmp.name) / folder
        shutil.copytree(valid("mdl"), src)
        m = json.loads((src / "manifest.json").read_text(encoding="utf-8"))
        m["task_id"] = folder
        if batch_id is not None:
            m["batch_id"] = batch_id
        (src / "manifest.json").write_text(json.dumps(m), encoding="utf-8")
        pkg = Package(src)
        self.addCleanup(pkg.close)
        return pkg

    def test_anim_outputs_are_accepted(self):
        pkg = Package(valid("mdl"))
        try:
            pkg.add_listed("images/cr_line_7f3a_adult_anim_sheet_01.png", png_bytes(64, 32), kind="storyboard")
            pkg.add_listed("anims/cr_adult_idle.fbx", b"fbx", kind="anim")
            pkg.add_listed("pipeline/README.md", b"# run", kind="report")
            self.assertEqual([], [str(f) for f in validate_art.validate(pkg.dir).findings])
        finally:
            pkg.close()

    def test_batch_package_matches_header_through_batch_id(self):
        pkg = self.batch_package(self.BATCH + "-line-7f3a", self.BATCH)
        self.assertNotIn("header_mismatch", pkg.codes())

    def test_batch_package_without_batch_id_is_mismatch(self):
        pkg = self.batch_package(self.BATCH + "-line-7f3a", None)
        self.assertIn("header_mismatch", pkg.codes())

    def test_batch_id_must_prefix_task_id(self):
        pkg = self.batch_package("mdl-20261106-0000-0000-line-7f3a", self.BATCH)
        self.assertIn("header_mismatch", pkg.codes())


class Cli(unittest.TestCase):
    def test_json_output_is_a_list_of_findings(self):
        pkg = Package((FIXTURES / "invalid" / "spec_mismatch").iterdir().__next__())
        try:
            code, out = pkg.run("--json")
            self.assertEqual(1, code)
            data = json.loads(out)
            self.assertTrue(any(f["code"] == "spec_mismatch" for f in data))
            self.assertEqual({"code", "path", "message"}, set(data[0]))
        finally:
            pkg.close()

    def test_missing_folder_is_usage_error(self):
        self.assertEqual(2, validate_art.main([str(REPO / "no_such_task_dir")]))


if __name__ == "__main__":
    unittest.main()
