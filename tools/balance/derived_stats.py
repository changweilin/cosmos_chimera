"""P2-02 integer reference; Python 3.13, stdlib only.

REQ: docs/design/derived-stats.md (approved 2026-11-06; coefficients in Data/balance.json).
All divisions are on non-negative integers: // floors, ceil_div rounds up.
Permille is a wire unit fixed at 1000, not a balance coefficient. Quantize each
formula in the documented order; do not fuse divisions in a C# port. Promote
operands to Int64 *before* addition/multiplication; never use floating roots.
Stats may be arbitrarily large non-negative Python ints (schema has no maximum).
Other move inputs are non-negative Int32; accuracy is an integer percent.
"""

import argparse
import json
from pathlib import Path

PERMILLE = 1000
INT32_MAX = (1 << 31) - 1
REPO = Path(__file__).resolve().parents[2]
PROPOSAL = REPO / "Data/balance.json"  # name kept: plot_curves imports it
GOLDEN = Path(__file__).with_name("golden_derived_stats.json")


def _integer(value, name, minimum=0, maximum=INT32_MAX):
    if type(value) is not int:
        raise TypeError(f"{name} must be an integer (not bool)")
    if value < minimum or (maximum is not None and value > maximum):
        raise ValueError(f"{name} outside [{minimum}, {maximum}]")
    return value


def _ceil_div(n, d):
    return (n + d - 1) // d


def _cube_root_floor(n):
    low, high = 0, 2 * PERMILLE + 1
    while low + 1 < high:
        middle = (low + high) // 2
        if middle * middle * middle <= n:
            low = middle
        else:
            high = middle
    return low


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate coefficient: {key}")
        result[key] = value
    return result


class DerivedStats:
    """Provide a bounded integer model from a proposal file.

    Pre: path has the proposal's documented keys and integer coefficients.
    Post: accepted coefficients fit the documented C# Int64 safety envelope.
    Side effects: reads path once; retains an instance-local configuration.
    """

    def __init__(self, path=PROPOSAL):
        self._config = json.loads(Path(path).read_text(encoding="utf-8"),
                                  object_pairs_hook=_unique_object)
        self._validate()

    def _validate(self):
        def walk(node, name):
            if isinstance(node, dict):
                for key, value in node.items():
                    walk(value, f"{name}.{key}")
            else:
                _integer(node, name, maximum=1_000_000)

        c = self._config
        walk(c, "proposal")
        if c["model_version"] != 1:
            raise ValueError("unsupported model_version")
        _integer(c["stat_effective_cap"], "stat_effective_cap", 1, 1_000_000)
        for group in ("hp", "melee_volume", "ranged_distance", "cd", "speed", "evasion"):
            _integer(c[group]["half_stat"], f"{group}.half_stat", 1, 1_000_000)
        for group in ("melee_volume", "ranged_distance"):
            _integer(c[group]["gain_permille"], f"{group}.gain_permille", 0, PERMILLE)
        _integer(c["hp"]["base"], "hp.base", 1, 1_000_000)
        _integer(c["speed"]["base_mm_s"], "speed.base_mm_s", 1, 1_000_000)
        _integer(c["cd"]["min_permille"], "cd.min_permille", 1, PERMILLE)
        _integer(c["cd"]["floor_ms"], "cd.floor_ms", 1, 1_000_000)
        _integer(c["ranged_distance"]["cap_mm"], "ranged_distance.cap_mm", 1, 1_000_000)
        _integer(c["evasion"]["cap_permille"], "evasion.cap_permille", 0, PERMILLE - 1)
        _integer(c["hit"]["floor_permille"], "hit.floor_permille", 1, PERMILLE)
        _integer(c["reference_move"]["accuracy_percent"], "accuracy_percent", 0, 100)
        for stage, band in c["stage_bands"].items():
            if not (band["min_stat"] <= band["max_stat"] <= c["stat_effective_cap"]
                    and 4 * band["min_stat"] <= band["total_stats"] <= 4 * band["max_stat"]):
                raise ValueError(f"infeasible stage band: {stage}")

    def derive(self, con, str, spi, agi, *, cooldown_ms=None,
               accuracy_percent=None, base_range_mm=None):
        """Return HP, multipliers, mm-based speed/range, CD and hit permille.

        Pre: stats are non-negative ints (bool rejected); move dimensions/CD
        are non-negative Int32, accuracy is 0..100; None uses reference_move.
        Post: results satisfy the invariants in derived-stats.md; stats above
        stat_effective_cap are equivalent; result values fit signed Int32.
        Side effects: allocates a result dict; no I/O or mutation.
        """
        c = self._config
        stats = [con, str, spi, agi]
        for name, value in zip(("con", "str", "spi", "agi"), stats):
            _integer(value, name, maximum=None)
        con, strength, spi, agi = [min(x, c["stat_effective_cap"]) for x in stats]
        move = c["reference_move"]
        cooldown_ms = move["cooldown_ms"] if cooldown_ms is None else cooldown_ms
        accuracy_percent = move["accuracy_percent"] if accuracy_percent is None else accuracy_percent
        base_range_mm = move["base_range_mm"] if base_range_mm is None else base_range_mm
        _integer(cooldown_ms, "cooldown_ms")
        _integer(accuracy_percent, "accuracy_percent", 0, 100)
        _integer(base_range_mm, "base_range_mm")

        hp = c["hp"]
        melee = c["melee_volume"]
        ranged = c["ranged_distance"]
        cd = c["cd"]
        speed = c["speed"]
        evasion = c["evasion"]
        volume_p = PERMILLE + melee["gain_permille"] * strength // (strength + melee["half_stat"])
        distance_p = PERMILLE + ranged["gain_permille"] * strength // (strength + ranged["half_stat"])
        cd_p = PERMILLE - (PERMILLE - cd["min_permille"]) * spi // (spi + cd["half_stat"])
        evasion_p = evasion["cap_permille"] * agi // (agi + evasion["half_stat"])
        return {
            "max_hp": hp["base"] + hp["gain"] * con // (con + hp["half_stat"]),
            "melee_volume_permille": volume_p,
            "melee_linear_permille": _cube_root_floor(volume_p * PERMILLE * PERMILLE),
            "ranged_multiplier_permille": distance_p,
            "ranged_distance_mm": min(ranged["cap_mm"], base_range_mm * distance_p // PERMILLE),
            "cd_multiplier_permille": cd_p,
            "effective_cd_ms": max(cd["floor_ms"], _ceil_div(cooldown_ms * cd_p, PERMILLE)),
            "move_speed_mm_s": speed["base_mm_s"] + speed["gain_mm_s"] * agi // (agi + speed["half_stat"]),
            "evasion_permille": evasion_p,
            "hit_chance_permille": max(c["hit"]["floor_permille"],
                                       min(PERMILLE, accuracy_percent * (PERMILLE - evasion_p) // 100)),
        }


def golden_document(model):
    """Build deterministic C# interoperability vectors, not balance assertions.

    Pre: model is a validated DerivedStats instance.
    Post: contains boundary, stage, asymmetric and extreme cases in stable order.
    Side effects: allocates JSON-compatible objects; none otherwise.
    """
    rows = []
    values = (0, 1, 2, 9, 10, 24, 25, 26, 49, 50, 51, 99, 100, 101,
              149, 150, 151, 199, 200, 201, 999, 1000, 9999, 10000, 10001,
              INT32_MAX - 1, INT32_MAX)
    inputs = [dict(con=x, str=x, spi=x, agi=x) for x in values]
    inputs += [dict(zip(("con", "str", "spi", "agi"), stats)) for stats in
               ((40, 20, 20, 20), (20, 20, 20, 40), (80, 40, 40, 40),
                (40, 40, 40, 80), (160, 80, 80, 80), (80, 80, 80, 160),
                (80, 160, 80, 80), (80, 80, 160, 80))]
    inputs.append(dict(con=1, str=101, spi=100, agi=151, cooldown_ms=1001,
                       accuracy_percent=73, base_range_mm=10001))
    for move in (
        {"cooldown_ms": 0, "accuracy_percent": 0, "base_range_mm": 0},
        {"cooldown_ms": 1, "accuracy_percent": 1, "base_range_mm": 1},
        {"cooldown_ms": 251, "accuracy_percent": 99, "base_range_mm": 10000},
        {"cooldown_ms": INT32_MAX, "accuracy_percent": 100, "base_range_mm": INT32_MAX},
    ):
        inputs.append(dict(con=INT32_MAX, str=INT32_MAX, spi=INT32_MAX, agi=INT32_MAX, **move))
    for args in inputs:
        rows.append({"input": args, "output": model.derive(**args)})
    return {"model_version": 1, "vectors": rows}


def golden_text(model):
    """Serialize vectors canonically with UTF-8-compatible LF text.

    Pre: model is validated. Post: same configuration gives byte-identical text.
    Side effects: allocates text only.
    """
    return json.dumps(golden_document(model), ensure_ascii=False, indent=2) + "\n"


def main():
    """Generate the explicitly requested golden file or print reference outputs.

    Pre: CLI arguments and proposal are valid. Post: outputs match the model.
    Side effects: reads proposal; --write-golden replaces GOLDEN, else stdout.
    """
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-golden", action="store_true")
    args = parser.parse_args()
    model = DerivedStats()
    if args.write_golden:
        GOLDEN.write_text(golden_text(model), encoding="utf-8", newline="\n")
    else:
        print(json.dumps(model.derive(100, 100, 100, 100), indent=2))


if __name__ == "__main__":
    main()
