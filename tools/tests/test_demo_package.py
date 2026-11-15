import importlib.util
from contextlib import contextmanager
import json
from pathlib import Path
from unittest.mock import Mock
import zipfile

import pytest


spec = importlib.util.spec_from_file_location("demo_package", Path(__file__).parents[1] / "demo/package.py")
dp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dp)


@pytest.fixture
def checkout(tmp_path, monkeypatch):
    for name in dp.REQUIRED:
        path = tmp_path / "Builds/Repro/il2cpp" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"player payload")
    for name in ("docs/demo-runbook.md", "ProjectSettings/ProjectVersion.txt"):
        path = tmp_path / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("m_EditorVersion: 6000.3.28f1\n", encoding="utf-8")
    (tmp_path / "build.log").write_text("ReproBuild il2cpp: Succeeded, 0 errors", encoding="utf-8")
    for mode in ("editmode", "playmode"):
        (tmp_path / f"{mode}.xml").write_text(
            '<test-run result="Passed" total="2" passed="2" failed="0" skipped="0"/>', encoding="utf-8")
    monkeypatch.setattr(dp.subprocess, "check_output", lambda args, **kwargs: "a" * 40 + "\n" if "rev-parse" in args else b"")
    return tmp_path


def pack(root, name="COSMIMERA-D0-test-win64"):
    return dp.package(root, name, root / "build.log", root / "editmode.xml", root / "playmode.xml")


def test_zip_is_complete_hash_verified_and_excludes_debug_payload(checkout):
    player = checkout / "Builds/Repro/il2cpp"
    debug = player / "Cosmimera_BurstDebugInformation_DoNotShip/secret.pdb"
    debug.parent.mkdir()
    debug.write_bytes(b"debug")
    debug2 = player / "Cosmimera_BackUpThisFolder_ButDontShipItWithYourGame/backup.cpp"
    debug2.parent.mkdir()
    debug2.write_bytes(b"debug")
    before = {p.relative_to(player): p.read_bytes() for p in player.rglob("*") if p.is_file()}
    archive = pack(checkout)
    with zipfile.ZipFile(archive) as stream:
        assert stream.testzip() is None
        assert not any("DoNotShip" in p or "DontShipItWithYourGame" in p for p in stream.namelist())
        prefix = "COSMIMERA-D0-test-win64/"
        manifest = stream.read(prefix + "SHA256SUMS.txt").decode()
        for line in manifest.splitlines():
            digest, name = line.split("  ", 1)
            assert dp.hashlib.sha256(stream.read(prefix + name)).hexdigest() == digest
        info = json.loads(stream.read(prefix + "build-info.json"))
        assert info["tests"]["playmode"]["passed"] == 2
        assert info["receipts_sha256"]["build_log"] == dp.sha256(checkout / "build.log")
    assert archive.with_suffix(".zip.sha256").read_text().startswith(dp.sha256(archive))
    assert before == {p.relative_to(player): p.read_bytes() for p in player.rglob("*") if p.is_file()}
    with pytest.raises(ValueError, match="overwrite"):
        pack(checkout)


@pytest.mark.parametrize("change", ["missing-player", "missing-readme", "failed-build", "later-failed-build", "failed-test", "empty-test", "skipped-test", "reserved"])
def test_invalid_inputs_publish_nothing(checkout, change):
    if change == "missing-player":
        (checkout / "Builds/Repro/il2cpp/GameAssembly.dll").unlink()
    elif change == "missing-readme":
        (checkout / "docs/demo-runbook.md").unlink()
    elif change == "failed-build":
        (checkout / "build.log").write_text("ReproBuild il2cpp: Failed, 1 errors")
    elif change == "later-failed-build":
        with (checkout / "build.log").open("a") as stream:
            stream.write("\nReproBuild il2cpp: Failed, 1 errors")
    elif change == "reserved":
        (checkout / "Builds/Repro/il2cpp/readme.MD").write_text("collision")
    else:
        text = (checkout / "playmode.xml").read_text()
        text = text.replace('result="Passed"', 'result="Failed"') if change == "failed-test" else text
        text = text.replace('total="2" passed="2"', 'total="0" passed="0"') if change == "empty-test" else text
        text = text.replace('skipped="0"', 'skipped="1"') if change == "skipped-test" else text
        (checkout / "playmode.xml").write_text(text)
    with pytest.raises(ValueError):
        pack(checkout)
    assert not (checkout / "Builds/Demo").exists()


@pytest.mark.parametrize("name", ["../escape", "COSMIMERA-D0-../bad-win64", "COSMIMERA-D0-a/b-win64", "", "CON"])
def test_invalid_name_fails_before_io(tmp_path, name):
    with pytest.raises(ValueError, match="Name"):
        pack(tmp_path, name)
    assert not list(tmp_path.iterdir())


@pytest.mark.parametrize("step", ["checksum", "folder", "archive"])
def test_publication_failure_leaves_no_partial_release_and_allows_retry(checkout, monkeypatch, step):
    original_open, original_rename = Path.open, Path.rename

    @contextmanager
    def fail_checksum(path, *args, **kwargs):
        with original_open(path, *args, **kwargs) as stream:
            if step == "checksum" and path.suffix == ".sha256" and args == ("x",):
                def fail_write(data):
                    stream.write("partial checksum")
                    raise OSError("publication failed")

                receipt = Mock(wraps=stream)
                receipt.write.side_effect = fail_write
                yield receipt
            else:
                yield stream

    def fail_rename(path, target):
        if ((step == "folder" and path.is_dir())
                or (step == "archive" and path.suffix == ".zip")):
            raise OSError("publication failed")
        return original_rename(path, target)

    with monkeypatch.context() as patch:
        patch.setattr(Path, "open", fail_checksum)
        patch.setattr(Path, "rename", fail_rename)
        with pytest.raises(OSError, match="publication failed"):
            pack(checkout)
    assert not list((checkout / "Builds/Demo").iterdir())
    assert pack(checkout).is_file()


def test_checksum_reservation_collision_preserves_the_other_release(checkout, monkeypatch):
    original_open = Path.open

    def collide(path, *args, **kwargs):
        if path.suffix == ".sha256" and args == ("x",):
            with original_open(path, "w", encoding="utf-8") as stream:
                stream.write("another publisher")
        return original_open(path, *args, **kwargs)

    monkeypatch.setattr(Path, "open", collide)
    with pytest.raises(FileExistsError):
        pack(checkout)
    files = list((checkout / "Builds/Demo").iterdir())
    assert len(files) == 1 and files[0].read_text() == "another publisher"
