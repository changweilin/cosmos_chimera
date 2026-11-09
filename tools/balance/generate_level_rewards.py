"""Regenerate approved integer vectors: python tools/balance/generate_level_rewards.py.

REQ: docs/design/level-progression.md. One JSON row per case keeps the fixture
reviewable and below 400 KB without omitting settlement inputs or assignments.
"""

import argparse
import json
from pathlib import Path
from random import Random

from derived_stats import INT32_MAX
from level_rewards import Creature, LevelRewards


GOLDEN = Path(__file__).with_name("golden_level_rewards.json")
SEED = 20261109
RANDOM_CASES = 200


def _settlement(model, name, total, mains, groups):
    creatures = [Creature(c["id"], c["xp"], c["dead"]) for c in mains]
    sidekicks = {key: [Creature(**c) for c in group] for key, group in groups.items()}
    result = model.settle(total, creatures, [c["appeared"] for c in mains], sidekicks)
    output = {key: result[key] for key in ("paid", "main_assigned", "sidekick_assigned")}
    for key in ("main_loss", "sidekick_loss"):
        value = result[key]
        output[key] = f"{value.numerator}/{value.denominator}"
    return {"name": name, "inputs": {"E": total, "mains": mains, "sidekicks": groups},
            "outputs": output}


def _settle_cases(model):
    def main(instance_id, xp=0, dead=False, appeared=True):
        return {"id": instance_id, "xp": xp, "dead": dead, "appeared": appeared}

    def sidekick(instance_id, xp=0, dead=False):
        return {"id": instance_id, "xp": xp, "dead": dead}

    cap = model.cap
    cases = []

    def add(name, total, mains, groups=None):
        if groups is None:
            groups = {c["id"]: [] for c in mains}
        cases.append(_settlement(model, name, total, mains, groups))

    add("zero", 0, [main(1)], {1: [sidekick(2)]})
    add("single_main", 120, [main(1)])
    add("single_odd_actual", 7, [main(1)], {1: [sidekick(12), sidekick(11)]})
    add("all_dead", INT32_MAX, [main(9, dead=True), main(1, dead=True)],
        {9: [sidekick(20)], 1: [sidekick(21)]})
    add("six_mains", 120, [main(i) for i in (6, 5, 4, 3, 2, 1)])
    add("appeared_reserve", 121, [main(i, appeared=i <= 2) for i in range(1, 7)])
    add("death_excludes_weight", 120,
        [main(1, dead=True), main(2), main(3, appeared=False)],
        {1: [sidekick(11)], 2: [sidekick(12, dead=True)], 3: [sidekick(13)]})
    for total in (1, 2, 5, INT32_MAX):
        add(f"main_ties_{total}", total, [main(9), main(1), main(4)])
    add("int32_mixed_weights", INT32_MAX, [main(1), main(2, appeared=False),
                                          main(3, appeared=False)])
    add("capped_main_keeps_weight", 120, [main(1, cap), main(2, appeared=False)],
        {1: [sidekick(11)], 2: [sidekick(12)]})
    add("near_cap_odd_actual", INT32_MAX, [main(1, cap - 3)],
        {1: [sidekick(12), sidekick(11, cap)]})
    add("above_cap_normalizes", 120, [main(1, INT32_MAX)], {1: [sidekick(2)]})
    add("all_capped", INT32_MAX, [main(1, cap), main(2, cap)],
        {1: [sidekick(11)], 2: []})
    add("sidekick_partial_cap_no_redistribution", 121, [main(1)],
        {1: [sidekick(12, cap - 2), sidekick(11, cap)]})
    add("dead_sidekicks", 7, [main(1)],
        {1: [sidekick(12, dead=True), sidekick(11, dead=True)]})
    for count in range(5):
        group = [sidekick(i) for i in reversed(range(11, 11 + count))]
        add(f"sidekick_group_{count}", 11, [main(1)], {1: group})
        if count:
            group = [sidekick(c["id"], cap if i == 0 else 0, i == count - 1)
                     for i, c in enumerate(group)]
            add(f"sidekick_group_{count}_dead_capped", 11, [main(1)], {1: group})

    rng = Random(SEED)
    xp_edges = (0, model.curve.xp_threshold(10), cap - 3, cap - 1, cap, cap + 1, INT32_MAX)
    for index in range(RANDOM_CASES):
        ids = rng.sample(range(1, 10000), rng.randrange(1, 7))
        mains = [main(i, rng.choice(xp_edges) if rng.randrange(2) else rng.randrange(cap + 1),
                      bool(rng.randrange(4) == 0), bool(rng.randrange(2))) for i in ids]
        groups = {}
        for i in ids:
            side_ids = rng.sample(range(10001, 20000), rng.randrange(5))
            # Separate ranges per main preserve unique identities across groups.
            groups[i] = [sidekick(i * 20000 + sid, rng.choice(xp_edges),
                                  bool(rng.randrange(4) == 0)) for sid in side_ids]
        total = rng.choice((0, 1, 7, 120, INT32_MAX)) if index % 2 else rng.randrange(INT32_MAX + 1)
        add(f"seeded_{index:03d}", total, mains, groups)
    return cases


def build_vectors():
    """Return the complete deterministic approved-A interoperability fixture.

    Pre: balance.json contains approved supported progression coefficients.
    Post: all numeric values are integers; 200 seeded plus explicit edge events.
    Side effects: reads approved configuration; allocates data, no file writes.
    """
    model = LevelRewards()
    curve = model.curve
    boundary_xp = {0, model.cap, model.cap + 1, INT32_MAX}
    for level in range(2, curve.level_max + 1):
        boundary_xp.update((curve.xp_threshold(level) - 1, curve.xp_threshold(level)))
    source_roles = {"wild": ("normal", "elite"), "capture": ("normal", "elite", "boss"),
                    "quest_step": ("normal",), "quest_chain": ("boss",), "boss": ("boss",)}
    return {
        "format_version": 1,
        "option": "A",
        "seed": SEED,
        "notes": {
            "coefficients": "Data/balance.json#progression；方案 A 核定值",
            "thresholds": "T[L]=累計門檻，長度101；C[L]=升至下一級成本，長度100；索引0的0不使用。",
            "roles": "任務步驟取區域普通等級，整鏈取BOSS等級；收服三種角色皆無倍率。",
            "settle": "字典鍵為十進位instance ID；main_loss及sidekick_loss為約分後n/d（整數寫n/1）。",
            "random": "Python random.Random(20261109)，僅用整數randrange/sample/choice，200例；獨立於全域亂數。"
        },
        "thresholds": {"T": [0] + [curve.xp_threshold(l) for l in range(1, 101)],
                       "C": [0] + [curve.xp_to_next(l) for l in range(1, 100)]},
        "level_of_xp": [{"xp": xp, "level": curve.level_from_xp(xp)} for xp in sorted(boundary_xp)],
        "scaled_stat": [{"base": base, "level": level, "value": curve.scaled_stat(base, level)}
                        for base in (0, 1, 54, 55, 109, 110, 200, INT32_MAX // 110, INT32_MAX)
                        for level in (1, 10, 50, 99, 100)],
        "wild_level": [{"difficulty": difficulty, "tier": tier, "stage": 0, "role": role,
                        "level": model.initial_level(difficulty, tier, 0, role)}
                       for difficulty in range(1, 11) for tier in range(3)
                       for role in ("normal", "elite", "boss")],
        "source_xp": [{"source": source, "level": level, "stage": 0, "difficulty": 1,
                       "role": role, "xp": model.source_xp(source, level, 0, role=role)}
                      for source, roles in source_roles.items()
                      for level in (1, 9, 10, 11, 50, 98, 99, 100) for role in roles],
        "migrate": [{"old_xp": old, "stage": stage, "already_cumulative": cumulative,
                     "xp": model.migrate_xp(old, stage, cumulative)}
                    for stage in range(3) for old in (0, 1, 100, INT32_MAX)
                    for cumulative in (False, True)],
        "settle": _settle_cases(model)
    }


def render_vectors(vectors):
    """Encode canonical UTF-8 JSON with one line per vector.

    Pre: vectors is build_vectors() output. Post: deterministic JSON plus newline.
    Side effects: allocates text only.
    """
    lines = []
    for key, value in vectors.items():
        prefix = "  " + json.dumps(key) + ": "
        if isinstance(value, list):
            rows = ["    " + json.dumps(row, ensure_ascii=False, separators=(",", ":")) for row in value]
            lines.append(prefix + "[\n" + ",\n".join(rows) + "\n  ]")
        else:
            lines.append(prefix + json.dumps(value, ensure_ascii=False, separators=(",", ":")))
    return "{\n" + ",\n".join(lines) + "\n}\n"


def main():
    """Write reproducible vectors to --output (default golden_level_rewards.json).

    Pre: approved configuration readable; output parent exists and is writable.
    Post: output equals render_vectors(build_vectors()).
    Side effects: reads configuration; overwrites the named UTF-8 JSON file.
    """
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=GOLDEN)
    args = parser.parse_args()
    args.output.write_text(render_vectors(build_vectors()), encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
