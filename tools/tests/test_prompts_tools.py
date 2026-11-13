"""Tests for tools/prompts/quota.py and check_batch.py (P5-01). Run: python -m pytest tools/tests"""
import copy
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "prompts"))
import check_batch  # noqa: E402
import quota  # noqa: E402

BANDS = json.loads((REPO / "Data" / "balance.json").read_text(encoding="utf-8"))["stage_bands"]
ALL = ["metal", "wood", "water", "fire", "earth", "ice", "wind", "thunder", "light", "shadow"]


def _creature(cid, stage, nxt, stats, level, temperament="normal"):
    own, weak, main, _ = check_batch.PATTERN[stage]
    resist = {e: 0 for e in ALL}
    resist.update(fire=own, water=weak)
    return {
        "id": cid, "line_id": "line_x", "stage": stage, "evolves_to": nxt, "temperament": temperament,
        "stats": dict(zip(["con", "str", "spi", "agi"], stats)), "mastery": {"fire": main},
        "resist": resist, "evolution": {"level": level, "materials": []} if nxt else None,
    }


def _line():
    return [
        _creature("a1", "juvenile", "a2", [40, 20, 20, 20], 25),
        _creature("a2", "adolescent", "a3", [80, 40, 40, 40], 40),
        _creature("a3", "adult", None, [160, 80, 80, 80], 0),
    ]


def test_valid_line_passes():
    assert check_batch.check(_line(), BANDS) == []


def test_stat_total_and_band_violations_reported():
    line = _line()
    line[0]["stats"]["con"] = 60
    errs = check_batch.check(line, BANDS)
    assert any("stats total" in e for e in errs)
    assert any("stat outside" in e for e in errs)


def test_two_weaknesses_rejected():
    line = _line()
    line[1]["resist"]["ice"] = -5
    assert any("exactly one weakness" in e for e in check_batch.check(line, BANDS))


def test_evolution_level_and_chain_checked():
    line = copy.deepcopy(_line())
    line[0]["evolution"]["level"] = 31
    line[1]["evolves_to"] = "wrong"
    errs = check_batch.check(line, BANDS)
    assert any("evolution level" in e for e in errs)
    assert any("evolves_to" in e for e in errs)


def test_apex_and_temperament_drop_rejected():
    line = _line()
    line[1]["temperament"] = "apex"
    line[2]["temperament"] = "timid"
    errs = check_batch.check(line, BANDS)
    assert any("apex is reserved" in e for e in errs)
    assert any("temperament lowered" in e for e in errs)


def test_quota_counts_remaining(tmp_path):
    cdir = tmp_path / "_incoming" / "b1" / "creatures"
    cdir.mkdir(parents=True)
    (cdir / "c.json").write_text(json.dumps([
        {"line_id": "l1", "mastery": {"fire": 15}, "concept": {"region": "非洲", "archetype": "arch_a"}},
        {"line_id": "l1", "mastery": {"fire": 25}, "concept": {"region": "非洲", "archetype": "arch_a"}},
    ]), encoding="utf-8")
    adir = tmp_path / "_incoming" / "p5-02-archetypes-draft-01"
    adir.mkdir(parents=True)
    (adir / "archetypes-a.json").write_text(json.dumps([
        {"id": "arch_a", "region": "非洲", "sensitivity": "ok"},
        {"id": "arch_b", "region": "非洲", "sensitivity": "ok"},
        {"id": "arch_c", "region": "南亞", "sensitivity": "review"},
    ]), encoding="utf-8")
    rep = quota.compute(quota.load_lines(tmp_path), quota.load_archetypes(tmp_path), 20)
    assert rep["have"] == 1 and rep["need"] == 19
    assert rep["elements"]["fire"] == (1, 1)
    assert rep["regions"]["非洲"] == (1, 9, 1)  # target ceil(20 / 2 regions) = 10
    assert rep["free_archetypes"] == ["arch_b"]
