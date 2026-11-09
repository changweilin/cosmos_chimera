import importlib.util
import math
from pathlib import Path
import struct

import pytest

spec = importlib.util.spec_from_file_location("repro_compare", Path(__file__).with_name("compare.py"))
rc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rc)


def report(angle=0.0, radius=640, bits=None):
    if bits is None:
        bits = ",".join(struct.pack(">f", v).hex() for v in (math.cos(angle), math.sin(angle), 0))
    return f"# cosmimera repro-probe v3\nlava 1 {radius} a b c d 5 p q {bits}\n"


@pytest.mark.parametrize("radius", [320, 640, 1280])
@pytest.mark.parametrize("metres, passed", [(0, True), (0.0029, True), (0.0031, False), (10, False)])
def test_policy_b_tolerates_landing_geometry_but_detects_moved_landing(radius, metres, passed):
    a, b = report(radius=radius), report(metres / radius, radius=radius)
    status, diffs, _ = rc.compare(a, b)
    assert status == (0 if metres == 0 else 1)
    assert diffs["planDiscrete"] == 0
    distance = rc.landing_max_distance(a, b)
    assert distance == pytest.approx(metres, abs=1e-3)
    assert rc.policy_b(diffs, {"landing": [distance, 0, 0]})[0] == passed


@pytest.mark.parametrize("bits", ["", "3f800000,00000000", "3f800000,00000000,zzzzzzzz",
                                      "7fc00000,00000000,00000000", "7f800000,00000000,00000000",
                                      "00000000,00000000,00000000"])
def test_invalid_landing_evidence_is_rejected(bits):
    with pytest.raises(ValueError):
        rc.compare(report(bits=bits), report(bits=bits))


@pytest.mark.parametrize("metres, expected", [(0.0029, 0), (0.0031, 1)])
def test_cli_policy_b_checks_landing_even_with_identical_height_samples(tmp_path, metres, expected):
    a, b = tmp_path / "a.txt", tmp_path / "b.txt"
    a.write_text(report(), encoding="utf-8")
    b.write_text(report(metres / 640), encoding="utf-8")
    samples = "".join(f"lava 1 {k} 00000000 00000000\n" for k in range(rc.SAMPLES_PER_ROW))
    for path in (a, b):
        Path(str(path) + ".samples").write_text(samples, encoding="utf-8")
    assert rc.main(["compare.py", str(a), str(b)]) == 1
    assert rc.main(["compare.py", str(a), str(b), "--policy", "b"]) == expected
