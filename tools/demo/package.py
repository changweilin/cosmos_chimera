"""Package a previously built and tested D0 IL2CPP player without overwriting releases.

Pre: receipts and player come from the same checkout, built before this command.
Post: a verified ZIP, file manifest and archive checksum exist, or the command fails.
Side effects: reads build/test evidence; writes only a new package under Builds/Demo.
Receipt validation is not an attestation that a binary was built from this checkout.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


REQUIRED = (
    "Cosmimera.exe", "UnityPlayer.dll", "GameAssembly.dll",
    "Cosmimera_Data/globalgamemanagers",
    "Cosmimera_Data/il2cpp_data/Metadata/global-metadata.dat",
)


def sha256(path):
    """Pre: readable file. Post: SHA-256 hex digest. Side effects: reads file."""
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def test_receipt(path):
    """Pre: NUnit XML. Post: all positive-count tests passed without skips. Side effects: reads XML."""
    node = ET.parse(path).getroot()
    counts = {key: int(node.attrib[key]) for key in ("total", "passed", "failed", "skipped")}
    if (node.tag != "test-run" or node.get("result") != "Passed" or counts["total"] <= 0
            or counts["passed"] != counts["total"] or counts["failed"] != 0 or counts["skipped"] != 0):
        raise ValueError(f"Incomplete or failed tests: {path}")
    return counts


def package(root, name, build_log, editmode, playmode, preview=None):
    """Pre: valid name and matching receipts/player. Post: verified archive path. Side effects: writes a new package."""
    if not re.fullmatch(r"COSMIMERA-D0-[A-Za-z0-9-]{1,100}-win64", name):
        raise ValueError("Name must be COSMIMERA-D0-<version>-win64 (letters, digits and hyphens)")
    root = root.resolve()
    player = root / "Builds/Repro/il2cpp"
    output = root / "Builds/Demo"
    folder = output / name
    archive = output / (name + ".zip")
    checksum = output / (name + ".zip.sha256")
    if any(path.exists() for path in (folder, archive, checksum)):
        raise ValueError("Refusing to overwrite an existing demo package")

    log = build_log.read_text(encoding="utf-8")
    builds = re.findall(r"ReproBuild il2cpp: ([^\r\n]+)", log)
    if not builds or builds[-1].strip() != "Succeeded, 0 errors":
        raise ValueError("Missing successful IL2CPP build receipt")
    tests = {"editmode": test_receipt(editmode), "playmode": test_receipt(playmode)}
    files = []
    for path in sorted(player.rglob("*")):
        relative = path.relative_to(player)
        if any(part.endswith(("_DoNotShip", "_ButDontShipItWithYourGame")) for part in relative.parts):
            continue
        if path.is_symlink() or path.is_junction():
            raise ValueError(f"Player contains a link: {relative}")
        if path.is_file():
            files.append((path, relative.as_posix()))
    names = {relative for _, relative in files}
    for required in REQUIRED:
        if required not in names:
            raise ValueError("Missing IL2CPP player file: " + required)
    attachments = [
        (root / "docs/demo-runbook.md", "README.md"),
        (root / "Data/geo-templates/ATTRIBUTION.md", "ATTRIBUTION.md"),
    ]
    if preview is not None:
        attachments.append((preview, "demo-preview.png"))
    reserved = {"build-info.json", "SHA256SUMS.txt", *(name for _, name in attachments)}
    lowered = [name.casefold() for _, name in files]
    if len(lowered) != len(set(lowered)) or set(lowered) & {name.casefold() for name in reserved}:
        raise ValueError("Duplicate or reserved package filename")
    for path, _ in attachments:
        if not path.is_file():
            raise ValueError(f"Missing attachment: {path}")

    # Capture packaging provenance, without claiming that these receipts prove binary/source correspondence.
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    patch = subprocess.check_output(["git", "diff", "HEAD", "--", "Assets", "Data", "Packages", "ProjectSettings"], cwd=root)
    info = {
        "product": "COSMIMERA D0", "version": name, "backend": "IL2CPP", "platform": "Windows x64",
        "packaging_checkout_commit": revision, "packaging_checkout_diff_sha256": hashlib.sha256(patch).hexdigest(),
        "unity": (root / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(": ", 1)[1],
        "scene": "Assets/_Project/Bootstrap/Demo.unity", "tests": tests,
        "receipts_sha256": {"build_log": sha256(build_log), "editmode": sha256(editmode), "playmode": sha256(playmode)},
        "scope": "Local demo candidate. Packaging receipts do not attest binary/source correspondence. "
                 "Full quest/BOSS playthrough, controls and difficulty require gameplay acceptance.",
    }

    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".package-", dir=output) as temporary:
        staging = Path(temporary) / name
        staging.mkdir()
        for source, relative in files + attachments:
            target = staging / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
        (staging / "build-info.json").write_text(json.dumps(info, indent=2) + "\n", encoding="utf-8")
        hashes = {path.relative_to(staging).as_posix(): sha256(path) for path in sorted(staging.rglob("*")) if path.is_file()}
        (staging / "SHA256SUMS.txt").write_text("".join(f"{digest}  {path}\n" for path, digest in hashes.items()), encoding="utf-8")
        staged_zip = Path(temporary) / archive.name
        with zipfile.ZipFile(staged_zip, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as stream:
            for path in sorted(staging.rglob("*")):
                if path.is_file():
                    stream.write(path, name + "/" + path.relative_to(staging).as_posix())
        with zipfile.ZipFile(staged_zip) as stream:
            if stream.testzip() is not None or len(stream.namelist()) != len(hashes) + 1:
                raise ValueError("ZIP integrity verification failed")
            for path, expected in hashes.items():
                if hashlib.sha256(stream.read(name + "/" + path)).hexdigest() != expected:
                    raise ValueError("ZIP checksum mismatch: " + path)
        digest = sha256(staged_zip)
        # Reserve the final name exclusively before publishing the verified package.
        with checksum.open("x", encoding="utf-8") as stream:
            stream.write(digest + "  " + archive.name + "\n")
        staging.rename(folder)
        staged_zip.rename(archive)
    return archive


def main():
    """Pre: CLI arguments. Post: exit 0 only on verified output. Side effects: as package; prints the archive path."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--name", required=True)
    parser.add_argument("--build-log", type=Path, required=True)
    parser.add_argument("--editmode", type=Path, required=True)
    parser.add_argument("--playmode", type=Path, required=True)
    parser.add_argument("--preview", type=Path)
    args = parser.parse_args()
    try:
        print(package(Path(__file__).resolve().parents[2], args.name, args.build_log, args.editmode, args.playmode, args.preview))
    except (OSError, ValueError, KeyError, ET.ParseError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"Package failed: {error}\n")


if __name__ == "__main__":
    main()
