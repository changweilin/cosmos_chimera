"""Q-C32 A/B proposals only; stdlib, exact integer/Fraction arithmetic.

Run: python tools/balance/material_drops.py
No RNG, Unity dependencies, save writes or approved balance mutations.
CLI output is the acceptance artifact embedded verbatim in proposal.md.
"""

from fractions import Fraction
from functools import cached_property
import json
from math import comb
from pathlib import Path

from derived_stats import REPO, _integer, _unique_object
from level_rewards import Creature, LevelRewards


PROPOSAL = REPO / "Data/_incoming/p2-05-material-proposal"
LEVELS = REPO / "Data/_incoming/p2-12-level-proposal/evolution-levels.json"
ROLES = ("normal", "elite", "boss")
STAGES = ("juvenile", "adolescent", "adult")


def _read(path):
    return json.loads(Path(path).read_text(encoding="utf-8"),
                      object_pairs_hook=_unique_object)


def _ceil(value):
    return -(-value.numerator // value.denominator)


def _probability(value):
    if type(value) not in (int, Fraction):
        raise TypeError("probability must be an integer or Fraction")
    if not 0 <= value <= 1:
        raise ValueError("probability outside [0, 1]")
    return Fraction(value)


class Catalog:
    """Read staged/formal creatures, excluding every incoming batch.

    Pre: repository data and the separate level proposal are readable/valid.
    Post: unique complete three-form lines; all requirements have fixed gates;
          demand includes outgoing costs once, with adults contributing zero.
    Side effects: reads JSON files; allocates an instance-local catalog.
    """

    def __init__(self):
        self.elements = tuple(_read(REPO / "Data/schemas/common.schema.json")[
            "$defs"]["element"]["enum"])
        levels = _read(LEVELS)
        if levels["status"] != "proposal" or levels["confirmed"] is not False:
            raise ValueError("level input must remain an unconfirmed proposal")
        self.levels = levels["levels"]
        self.creatures = {}
        self.sources = []
        for path in sorted((REPO / "Data").rglob("*.json")):
            if "_incoming" in path.parts or path.parent.name != "creatures":
                continue
            self.sources.append(path.relative_to(REPO).as_posix())
            for creature in _read(path):
                key = creature["id"]
                if key in self.creatures:
                    raise ValueError(f"duplicate formal creature {key}")
                self.creatures[key] = creature
        if not self.creatures:
            raise ValueError("no formal/staged creature data")
        self.lines = {}
        for creature in self.creatures.values():
            self.lines.setdefault(creature["line_id"], []).append(creature)
            mastery = creature["mastery"]
            if not mastery or not set(mastery) <= set(self.elements):
                raise ValueError("invalid mastery elements")
            for value in mastery.values():
                _integer(value, "mastery")
        expected = set()
        for line in self.lines.values():
            line.sort(key=lambda c: STAGES.index(c["stage"]))
            if [c["stage"] for c in line] != list(STAGES):
                raise ValueError("each line must contain exactly three stages")
            for index, creature in enumerate(line):
                evolution = creature["evolution"]
                if index == 2:
                    if evolution is not None or creature["evolves_to"] is not None:
                        raise ValueError("adult has outgoing evolution")
                    continue
                expected.add(creature["id"])
                if creature["evolves_to"] != line[index + 1]["id"]:
                    raise ValueError("evolution chain is disconnected")
                gate = self.levels[creature["id"]]
                low, high = ((20, 30), (35, 50))[index]
                _integer(gate, "proposed evolution level", low, high)
                if evolution["level"] != gate:
                    raise ValueError("staged and proposed evolution gates differ")
                seen = set()
                for requirement in evolution["materials"]:
                    element, tier, qty = (requirement[k] for k in ("element", "tier", "qty"))
                    _integer(tier, "tier", 1, 3)
                    _integer(qty, "qty", 1)
                    if element not in creature["mastery"] or (element, tier) in seen:
                        raise ValueError("duplicate material or material outside mastery")
                    seen.add((element, tier))
        if expected != set(self.levels):
            raise ValueError("level proposal must cover all non-adults exactly")
        self.wild = tuple(c for c in self.creatures.values()
                          if not c["line_id"].startswith("starter_"))
        self.starters = tuple(line for key, line in sorted(self.lines.items())
                              if key.startswith("starter_"))
        self.planets = {}
        for path in sorted((REPO / "Data/_demo/planet-types").glob("*.json")):
            for planet in _read(path):
                bias = planet["element_bias"]
                if (planet["id"] in self.planets or not bias or len(bias) != len(set(bias))
                        or not set(bias) <= set(self.elements)):
                    raise ValueError("invalid planet preference")
                self.planets[planet["id"]] = tuple(bias)
        if not self.planets:
            raise ValueError("no D0 planet types")

    def demand(self):
        """Sum outgoing material costs by tier/element across all lines.

        Pre: validated catalog. Post: every tier/element present, integer >= 0.
        Side effects: allocates a new dictionary; does not mutate data.
        """
        result = {(e, t): 0 for t in (1, 2, 3) for e in self.elements}
        for creature in self.creatures.values():
            if creature["evolution"] is not None:
                for item in creature["evolution"]["materials"]:
                    result[item["element"], item["tier"]] += item["qty"]
        return result


def _main_element(creature):
    # Match RunPlanner.MainElement: highest mastery, first data entry on ties.
    return max(creature["mastery"], key=creature["mastery"].get)


class MaterialDrops:
    """Evaluate one unconfirmed drop proposal against the current D0 pool.

    Pre: option A/B; optional path has this proposal's supported contract.
    Post: deterministic exact yields; A never invents absent victim elements.
    Side effects: reads configuration/catalog/approved XP model; no writes.
    """

    def __init__(self, option="A", path=None):
        if option not in ("A", "B"):
            raise ValueError("option must be A or B")
        self.option = option
        self.document = _read(path if path is not None else PROPOSAL / f"drops-{option.lower()}.json")
        d = self.document
        if (type(d["proposal_version"]) is not int or d["proposal_version"] != 1
                or d["status"] != "proposal" or d["confirmed"] is not False
                or d["option"] != option or d["question"] != "Q-C32"):
            raise ValueError("requires an unconfirmed matching Q-C32 v1 proposal")
        expected_source = "victim_main_mastery" if option == "A" else "uniform_planet_bias"
        if d["element_source"] != expected_source or set(d["rules"]) != set(ROLES):
            raise ValueError("unsupported source or roles")
        if option == "A" and d["mastery_tie_break"] != "first_in_data_order":
            raise ValueError("unsupported mastery tie break")
        if (d["other_triggers"] != [] or d["duplicate_policy"] != "one_defeat_per_spawn_no_role_stacking"
                or d["inventory_scope"] != "shared_lobby_save"
                or d["bank_before_evolution"] is not True or d["tier_substitution"] is not False
                or d["decision_owner"] != "human"):
            raise ValueError("unsupported grant or inventory policy")
        for tier, role in enumerate(ROLES, 1):
            rule = d["rules"][role]
            _integer(rule["tier"], "tier", tier, tier)
            _integer(rule["chance_percent"], "chance_percent", 0, 100)
            _integer(rule["qty"], "qty", 1, 1)
        self.catalog = Catalog()
        self.rewards = LevelRewards("A")
        self.run = self.rewards.document["d0_run_model"]

    def element_probabilities(self, planet, stage):
        """Return the exact conditional element distribution of a drop.

        Pre: planet is a current D0 type; stage is integer 0..2, not bool.
        Post: nonnegative Fractions over ten elements, sum == 1; no RNG.
        Side effects: allocates a dictionary.
        """
        _integer(stage, "stage", 0, 2)
        if planet not in self.catalog.planets:
            raise ValueError("unknown planet")
        bias = self.catalog.planets[planet]
        result = dict.fromkeys(self.catalog.elements, Fraction(0))
        if self.option == "B":
            for element in bias:
                result[element] = Fraction(1, len(bias))
        else:
            pool = [c for c in self.catalog.wild if c["stage"] == STAGES[stage]]
            if not pool:
                raise ValueError("calibration needs a wild form at each stage")
            weights = [(c, 3 if _main_element(c) in bias else 1) for c in pool]
            total = sum(w for _, w in weights)
            for creature, weight in weights:
                result[_main_element(creature)] += Fraction(weight, total)
        return result

    def event_drop(self, planet, role, victim_id, drop_roll, element_roll=0):
        """Resolve one already-deduplicated defeat using caller-owned rolls.

        Pre: known planet/role/victim; drop_roll integer 0..99; element_roll
             0..len(bias)-1 for B, exactly 0 for A. No bools. Caller guarantees
             role correctness and grants once per spawn, even across retries.
        Post: empty dict or exactly one (element,tier):1; no other tier granted.
        Side effects: allocates result only; never samples or records events.
        """
        if planet not in self.catalog.planets or role not in ROLES:
            raise ValueError("unknown planet or role")
        if victim_id not in {c["id"] for c in self.catalog.wild}:
            raise ValueError("victim must belong to the D0 wild pool")
        _integer(drop_roll, "drop_roll", 0, 99)
        bias = self.catalog.planets[planet]
        _integer(element_roll, "element_roll", 0, len(bias) - 1 if self.option == "B" else 0)
        rule = self.document["rules"][role]
        if drop_roll >= rule["chance_percent"]:
            return {}
        element = (bias[element_roll] if self.option == "B"
                   else _main_element(self.catalog.creatures[victim_id]))
        return {(element, rule["tier"]): rule["qty"]}

    def _trials(self, role):
        if role == "normal":
            return tuple(enumerate(self.run["normal_victims_by_region_tier"]))
        if role == "elite":
            return tuple((entry["stage"], 1) for entry in self.run["elite_victims"])
        return ((self.run["boss"]["stage"], 1),)

    def run_yield(self, planet):
        """Return exact expected inventory gain for the reference D0 route.

        Pre: known D0 planet; route counts held constant across planet types.
        Post: ten elements x three tiers of nonnegative Fractions; actual
              per-event drops are integers, never rounded expected inventory.
        Side effects: allocates dictionaries; reads no additional files.
        """
        result = {(e, t): Fraction(0) for t in (1, 2, 3) for e in self.catalog.elements}
        for role in ROLES:
            rule = self.document["rules"][role]
            for stage, count in self._trials(role):
                for element, probability in self.element_probabilities(planet, stage).items():
                    result[element, rule["tier"]] += (
                        count * probability * rule["chance_percent"] * rule["qty"] / 100)
        return result

    def completion(self, planet, requirement):
        """Return mean completed runs and 80%-completion run for one cost.

        Pre: requirement has valid element, tier 1..3, qty positive Int32.
        Post: (Fraction mean, integer p80) or (None,None) iff inaccessible;
              assumes independent event rolls and a fixed stage distribution.
        Side effects: allocates probabilities; no simulation or RNG.
        """
        element, tier, qty = (requirement[k] for k in ("element", "tier", "qty"))
        _integer(tier, "tier", 1, 3)
        _integer(qty, "qty", 1)
        if element not in self.catalog.elements:
            raise ValueError("unknown element")
        role = ROLES[tier - 1]
        trials = self._trials(role)
        probabilities = [self.element_probabilities(planet, stage)[element]
                         * self.document["rules"][role]["chance_percent"] / 100
                         for stage, count in trials for _ in range(count)]
        if len(set(probabilities)) != 1:
            raise ValueError("homogeneous-binomial calibration no longer applies")
        p = probabilities[0]
        return (expected_runs(qty, len(probabilities), p),
                completion_quantile(qty, len(probabilities), p))

    @cached_property
    def xp_per_run(self):
        """Return exact active ID1 gains for 1/2/6 mains using approved A.

        Pre: documented route; all start LV10 and survive, IDs1/2 appeared.
        Post: positive integer first-run gains with per-event remainder rules.
        Side effects: allocates settlement ledgers; caches gains on instance.
        """
        result = {}
        start = self.rewards.curve.xp_threshold(10)
        for party in (1, 2, 6):
            mains = [Creature(i, start) for i in range(1, party + 1)]
            appeared = [i < 2 for i in range(party)]
            groups = {c.id: [] for c in mains}
            result[party] = sum(self.rewards.settle(xp, mains, appeared, groups)["paid"][1]
                                for _, xp in self.rewards.d0_events())
        return result


def expected_runs(qty, encounters, probability):
    """Compute E[completed runs until qty unit successes] exactly.

    Pre: qty >= 0, encounters >= 1, both Int32 integers; p exact in [0,1].
    Post: Fraction >= 0; None iff qty > 0 and p == 0. Solves the triangular
          binomial state recurrence, not ceil(qty / expected yield).
    Side effects: allocates a small state vector; no RNG.
    """
    _integer(qty, "qty")
    _integer(encounters, "encounters", 1)
    p = _probability(probability)
    if qty == 0:
        return Fraction(0)
    if p == 0:
        return None
    if p == 1:
        return Fraction(-(-qty // encounters))
    mass = [comb(encounters, j) * p ** j * (1 - p) ** (encounters - j)
            for j in range(min(qty, encounters + 1))]
    means = [Fraction(0)]
    for remaining in range(1, qty + 1):
        continuation = sum((mass[j] * means[remaining - j]
                            for j in range(1, min(remaining, len(mass)))), Fraction(0))
        means.append((1 + continuation) / (1 - mass[0]))
    return means[qty]


def completion_quantile(qty, encounters, probability, percentile=80):
    """Find the earliest run with at least percentile% chance of completion.

    Pre: same qty/encounters/p domain as expected_runs; percentile int 1..99.
    Post: nonnegative integer, or None for inaccessible; exact binomial CDF.
    Side effects: none; no RNG. Intended for small proposal material costs.
    """
    _integer(qty, "qty")
    _integer(encounters, "encounters", 1)
    _integer(percentile, "percentile", 1, 99)
    p = _probability(probability)
    if qty == 0:
        return 0
    if p == 0:
        return None
    if p == 1:
        return -(-qty // encounters)

    def reached(runs):
        n = runs * encounters
        failure = sum((comb(n, j) * p ** j * (1 - p) ** (n - j)
                       for j in range(min(qty, n + 1))), Fraction(0))
        return 1 - failure >= Fraction(percentile, 100)

    high = 1
    while not reached(high):
        high *= 2
    low = 0
    while low + 1 < high:
        middle = (low + high) // 2
        if reached(middle):
            high = middle
        else:
            low = middle
    return high


def pacing_table():
    """Render all acceptance tables from the same model used by the CLI.

    Pre: valid unconfirmed A/B proposals and repository inputs.
    Post: deterministic Markdown inventory/yield/access/pacing tables; mean
          completion counts rounded UP only for integer planning display.
    Side effects: reads inputs; no file writes.
    """
    models = [MaterialDrops(option) for option in ("A", "B")]
    catalog = models[0].catalog
    lines = ["| 進化線 | 幼年→少年：LV；素材 | 少年→成年：LV；素材 | 成年 |",
             "|---|---|---|---|"]
    for line_id, line in sorted(catalog.lines.items()):
        cells = []
        for creature in line[:2]:
            cost = " + ".join(f"{r['element']}/t{r['tier']}×{r['qty']}"
                              for r in creature["evolution"]["materials"])
            cells.append(f"{catalog.levels[creature['id']]}；{cost}")
        lines.append(f"| {line_id} | {cells[0]} | {cells[1]} | 無需求 |")
    lines.extend(["", "| tier | " + " | ".join(catalog.elements) + " | 合計 |",
                  "|---|" + "---|" * (len(catalog.elements) + 1)])
    demand = catalog.demand()
    for tier in (1, 2, 3):
        values = [demand[element, tier] for element in catalog.elements]
        lines.append(f"| {tier} | " + " | ".join(map(str, values)) + f" | {sum(values)} |")
    lines.extend(["", "| 方案 | 觸發 | tier | 成功率 % | 成功數量 | 每次擊倒期望 |",
                  "|---|---|---|---|---|---|"])
    for model in models:
        for role in ROLES:
            rule = model.document["rules"][role]
            mean = Fraction(rule["chance_percent"] * rule["qty"], 100)
            lines.append(f"| {model.option} | {role} | {rule['tier']} | {rule['chance_percent']} | {rule['qty']} | {mean} |")
    lines.extend(["", "| 方案／星球 | 每 run t1／t2／t3 總期望 | 每屬性 t1／t2／t3 期望（未列者皆 0） |",
                  "|---|---|---|"])
    for model in models:
        for planet in sorted(catalog.planets):
            yields = model.run_yield(planet)
            totals = [sum((yields[e, t] for e in catalog.elements), Fraction(0)) for t in (1, 2, 3)]
            by_element = "; ".join(e + ":(" + ", ".join(str(yields[e, t]) for t in (1, 2, 3)) + ")"
                                    for e in catalog.elements if any(yields[e, t] for t in (1, 2, 3)))
            lines.append(f"| {model.option}/{planet} | {' / '.join(map(str, totals))} | {by_element} |")
    lines.extend(["", "| 方案 | D0 所有星球聯集不可達屬性（每個 tier） |",
                  "|---|---|"])
    for model in models:
        unreachable = [e for e in catalog.elements
                       if all(model.run_yield(p)[e, 1] == 0 for p in catalog.planets)]
        lines.append(f"| {model.option} | {', '.join(unreachable) or '無'} |")
    lines.extend(["", "| 方案 | 線／目標星球 | 階段 | LV | 等級 run（1／2／6 主力） | 素材 run：ceil(均值)／P80 | 滿編規劃 run／距上階段 |",
                  "|---|---|---|---|---|---|---|"])
    for model in models:
        for line in catalog.starters:
            previous = 0
            for stage, creature in enumerate(line[:2]):
                requirement, = creature["evolution"]["materials"]
                element = requirement["element"]
                # Choose a matching type explicitly; never substitute a new victim pool for A.
                planet = next(p for p, bias in catalog.planets.items() if element in bias)
                mean, p80 = model.completion(planet, requirement)
                gate = catalog.levels[creature["id"]]
                delta = model.rewards.curve.xp_threshold(gate) - model.rewards.curve.xp_threshold(10)
                level_runs = [_ceil(Fraction(delta, model.xp_per_run[n])) for n in (1, 2, 6)]
                material = f"{_ceil(mean)} / {p80}" if mean is not None else "不可達 / 不可達"
                planned = max(level_runs[-1], _ceil(mean), previous) if mean is not None else None
                planning = f"{planned} / +{planned - previous}" if planned is not None else "不可達"
                lines.append(f"| {model.option} | {creature['line_id']}/{planet} | {STAGES[stage]} | {gate} | {' / '.join(map(str, level_runs))} | {material} | {planning} |")
                if planned is not None:
                    previous = planned
    return "\n".join(lines)


def main():
    """Print proposal acceptance tables only.

    Pre: repository inputs readable. Post: stdout matches pacing_table().
    Side effects: reads files, writes stdout; no filesystem changes.
    """
    print(pacing_table())


if __name__ == "__main__":
    main()
