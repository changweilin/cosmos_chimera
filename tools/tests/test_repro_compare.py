import importlib.util
from pathlib import Path

import pytest

spec = importlib.util.spec_from_file_location("repro_compare", Path(__file__).parents[1] / "repro" / "compare.py")
rc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rc)

HEAD = "# cosmimera repro-probe v1\n# PlanetHeight.GeneratorVersion 3 RunPlanner.GeneratorVersion 1\n"
ROW = "lava 1 640 a b c d 5 p q\n"


def report(env="mono", rows=ROW, head=HEAD):
    return head + f"# env {env}\n" + rows


def test_identical_ignores_env_line():
    status, diffs, _ = rc.compare(report("mono"), report("il2cpp"))
    assert status == 0 and not any(diffs.values())


def test_counts_differences_per_column():
    status, diffs, _ = rc.compare(report(), report(rows="lava 1 640 X b c d 5 Q q\n"))
    assert status == 1 and diffs["baseBits"] == 1 and diffs["plan"] == 1 and diffs["baseMm"] == 0


def test_header_mismatch_is_not_comparable():
    status, _, _ = rc.compare(report(), report(head=HEAD.replace("Version 1", "Version 2")))
    assert status == 2


def test_row_set_mismatch_is_not_comparable():
    status, _, _ = rc.compare(report(), report(rows=ROW + "lava 2 640 a b c d 5 p q\n"))
    assert status == 2


@pytest.mark.parametrize("bad", ["lava 1 640 a b\n", ROW + ROW])
def test_malformed_or_duplicate_rows_raise(bad):
    with pytest.raises(ValueError):
        rc.parse(report(rows=bad))


def test_sample_stats_measure_magnitude():
    one = "3f800000"  # 1.0
    near = "3f800001"  # 1.0 + 1 ulp
    far = "3f80c49c"  # ~1.006
    a = f"t 1 0 {one} {one}\nt 1 1 {one} {one}\n"
    b = f"t 1 0 {near} {one}\nt 1 1 {one} {far}\n"
    stats, n = rc.sample_stats(a, b)
    assert n == 2
    assert stats["base"][2] == 1 and stats["base"][1] == 0
    assert stats["surf"][1] == 1 and stats["surf"][0] > 1e-3


def test_sample_stats_reject_misaligned_dumps():
    with pytest.raises(ValueError):
        rc.sample_stats("t 1 0 0 0\n", "t 1 1 0 0\n")


@pytest.mark.parametrize("text", ["", HEAD, "# env mono\n" + ROW])
def test_empty_header_only_or_unmarked_reports_are_rejected(text):
    with pytest.raises(ValueError):
        rc.parse(text)


# ---- Q-T6 (b), ADR-0007 ----

def _stats(base, surf):
    return {"base": [base, 0, 0], "surf": [surf, 0, 0]}


def test_policy_b_passes_geometry_within_tolerance_when_content_agrees():
    _, diffs, _ = rc.compare(report(), report(rows="lava 1 640 X Y Z W 5 Q q\n"))
    assert rc.policy_b(diffs, _stats(0.0022, 0.003)) == (True, [])


@pytest.mark.parametrize("rows, base", [("lava 1 640 a b c d 6 p q\n", 0.0), ("lava 1 640 a b c d 5 p Q\n", 0.0),
                                        (ROW, 0.0031), (ROW, float("nan"))])
def test_policy_b_fails_on_content_or_excess_geometry(rows, base):
    _, diffs, _ = rc.compare(report(), report(rows=rows))
    passed, reasons = rc.policy_b(diffs, _stats(base, 0.0))
    assert not passed and reasons


def test_policy_b_cli_requires_samples(tmp_path):
    a, b = tmp_path / "a.txt", tmp_path / "b.txt"
    a.write_text(report(), encoding="utf-8")
    b.write_text(report("il2cpp"), encoding="utf-8")
    assert rc.main(["compare.py", str(a), str(b)]) == 0
    assert rc.main(["compare.py", str(a), str(b), "--policy", "b"]) == 2
    full = "".join(f"lava 1 {k} 3f800000 3f800000\n" for k in range(rc.SAMPLES_PER_ROW))
    (tmp_path / "a.txt.samples").write_text(full, encoding="utf-8")
    (tmp_path / "b.txt.samples").write_text(full, encoding="utf-8")
    assert rc.main(["compare.py", str(a), str(b), "--policy", "b"]) == 0


# SOL P2-12 R2 witnesses: empty, single, duplicated or foreign sample dumps must not PASS.
@pytest.mark.parametrize("dump", [
    "",
    "lava 1 0 3f800000 3f800000\n",
    "lava 1 0 3f800000 3f800000\n" * 2048,
    "".join(f"other_plan 999 {k} 3f800000 3f800000\n" for k in range(2048)),
], ids=["empty", "single", "duplicated", "foreign"])
def test_policy_b_rejects_incomplete_sample_dumps(tmp_path, dump):
    a, b = tmp_path / "a.txt", tmp_path / "b.txt"
    a.write_text(report(), encoding="utf-8")
    b.write_text(report("il2cpp"), encoding="utf-8")
    (tmp_path / "a.txt.samples").write_text(dump, encoding="utf-8")
    (tmp_path / "b.txt.samples").write_text(dump, encoding="utf-8")
    assert rc.main(["compare.py", str(a), str(b), "--policy", "b"]) == 2


@pytest.mark.parametrize("bad", ["t 1 0 7fc00000 3f800000", "t 1 0 3f800000 7f800000", "t 1 0 3f800000"])
def test_sample_stats_reject_non_finite_or_short_lines(bad):
    with pytest.raises(ValueError):
        rc.sample_stats(bad, "t 1 0 3f800000 3f800000")
