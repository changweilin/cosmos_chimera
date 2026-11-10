"""P2-12 approved option A reference; rejected option B retained for history.

Run: python tools/balance/level_rewards.py
Prints the exact Markdown pacing table reproduced in level-progression.md.
All arithmetic is integer/Fraction; only the CLI writes stdout.
"""

from dataclasses import dataclass
from functools import cached_property
from fractions import Fraction
import json
from pathlib import Path

from derived_stats import INT32_MAX, REPO, _integer, _unique_object
from level_progression import LevelProgression, allocate_main_xp


CONFIG = REPO / "Data/balance.json"
PROPOSAL = REPO / "Data/_incoming/p2-12-level-proposal/rewards.json"


@dataclass(frozen=True)
class Creature:
    """One reward recipient in an event snapshot.

    Pre: id is positive Int32, xp is nonnegative Int32, dead is bool (checked
         by settle); a sidekick may belong to only one main in a snapshot.
    Post: immutable identity, accumulated XP and settlement-time death flag.
    Side effects: none.
    """

    id: int
    xp: int
    dead: bool = False


def _apportion(exact, ids, conserve):
    amounts = [value.numerator // value.denominator for value in exact]
    if conserve:
        total = sum(exact, Fraction(0))
        if total.denominator != 1:
            raise ValueError("largest remainder requires an integer budget")
        remaining = int(total) - sum(amounts)
        order = sorted(range(len(ids)), key=lambda i: (-(exact[i] - amounts[i]), ids[i]))
        for index in order[:remaining]:
            amounts[index] += 1
    return tuple(amounts)


class LevelRewards:
    """Evaluate approved A or historical B against the approved curve.

    Pre: option is A/B; path is balance.json for A, the v1 proposal for B;
         None selects the corresponding repository file.
    Post: exact deterministic outputs; A coefficients come only from balance.json.
    Side effects: reads configuration files; no data or runtime state changes.
    """

    def __init__(self, option="A", path=None):
        if option not in ("A", "B"):
            raise ValueError("option must be A or B")
        self.option = option
        path = Path(path) if path is not None else CONFIG if option == "A" else PROPOSAL
        document = json.loads(path.read_text(encoding="utf-8"),
                              object_pairs_hook=_unique_object)
        self.curve = LevelProgression(path if option == "A" else CONFIG)
        self.cap = self.curve.xp_threshold(self.curve.level_max)
        if option == "A":
            progression = document["progression"]
            rewards = progression["rewards"]
            self.rules = {**rewards, "source_base_xp": rewards["base_xp"]}
            self.stage_floors = progression["migration_stage_floor_levels"]
            self.reference_level = _integer(rewards["reference_level"], "reference_level",
                                            1, self.curve.level_max - 1)
            self.sidekick_divisor = _integer(rewards["sidekick_share_divisor"],
                                             "sidekick_share_divisor", 1)
            wild = rewards["wild_level"]
            for key in ("boss_level_min", "boss_level_max", "per_tier"):
                _integer(wild[key], f"wild_level.{key}")
            if not 1 <= wild["boss_level_min"] <= wild["boss_level_max"] <= self.curve.level_max:
                raise ValueError("boss levels must satisfy 1 <= min <= max <= level_max")
            for role in ("normal", "elite", "boss"):
                _integer(wild["role_bonus"][role], f"role_bonus.{role}")
        else:
            if (document["proposal_version"] != 1 or document["status"] != "proposal"
                    or document["confirmed"] is not False):
                raise ValueError("historical B requires the unconfirmed v1 proposal")
            self.rules = document["options"][option]
            self.stage_floors = document["shared"]["migration_stage_floor_levels"]
            self.sidekick_divisor = 2
        if not isinstance(self.stage_floors, list) or len(self.stage_floors) != 3:
            raise ValueError("migration requires three stage floor levels")
        for floor in self.stage_floors:
            _integer(floor, "migration floor", 1, self.curve.level_max)
        if set(self.rules["source_base_xp"]) != {"wild", "capture", "quest_step", "quest_chain", "boss"}:
            raise ValueError("reward sources must match the five documented events")
        for value in self.rules["source_base_xp"].values():
            _integer(value, "source base XP")
        _integer(self.rules["elite_multiplier"], "elite_multiplier", 1, 100)

    @cached_property
    def document(self):
        """Load historical pacing assumptions only when explicitly requested.

        Pre: historical proposal exists. Post: decoded historical document.
        Side effects: reads the proposal once; approved reward math never uses it.
        """
        return json.loads(PROPOSAL.read_text(encoding="utf-8"), object_pairs_hook=_unique_object)

    def initial_level(self, difficulty, tier, stage, role="normal"):
        """Return wild level; captures preserve this level and start at T(L).

        Pre: difficulty 1..10 (new design input, not planet size), tier/stage
             0..2, role normal/elite/boss. Integers exclude bool.
        Post: integer 1..100; monotone in difficulty and tier; no RNG.
        Side effects: none.
        """
        _integer(difficulty, "difficulty", 1, 10)
        _integer(tier, "tier", 0, 2)
        _integer(stage, "stage", 0, 2)
        if role not in ("normal", "elite", "boss"):
            raise ValueError("unknown encounter role")
        if self.option == "A":
            # D0-X3 (2026-11-10): d=1..10 maps linearly onto boss LV min..max (half-up), and every
            # other role is derived down from it with the boss standing at the top tier (2).
            wild = self.rules["wild_level"]
            span = wild["boss_level_max"] - wild["boss_level_min"]
            boss = wild["boss_level_min"] + (2 * span * (difficulty - 1) + 9) // 18
            level = (boss - wild["role_bonus"]["boss"] - wild["per_tier"] * (2 - tier)
                     + wild["role_bonus"][role])
        else:
            floor = self.stage_floors[stage]
            level = floor + 5 * (difficulty - 1) + tier
        return min(self.curve.level_max, max(1, level))

    def migrate_xp(self, old_xp, stage, already_cumulative=False):
        """Evaluate a one-time migration, after a caller-owned version check.

        Pre: old_xp 0..Int32.MaxValue, stage 0..2, already_cumulative bool;
             caller backs up old save and atomically writes the new version.
        Post: 0..T(100); already-converted values only saturate; A ignores
              legacy evolution XP, B adds it once to the stage-floor threshold.
        Side effects: none; does not write a save or its version marker.
        """
        _integer(old_xp, "old_xp")
        _integer(stage, "stage", 0, 2)
        if type(already_cumulative) is not bool:
            raise TypeError("already_cumulative must be bool")
        if already_cumulative:
            return min(self.cap, old_xp)
        floor = self.stage_floors[stage]
        baseline = self.curve.xp_threshold(floor)
        return min(self.cap, baseline + (old_xp if self.option == "B" else 0))

    def source_xp(self, source, level, stage, difficulty=1, role="normal"):
        """Return one event's main pool, before party allocation.

        Pre: source wild/capture/quest_step/quest_chain/boss, level 1..100,
             stage 0..2, difficulty 1..10, role normal/elite/boss. Defeat source
             agrees with role; quest caller supplies the documented threat input.
        Post: 0..Int32.MaxValue; elite x2 applies only to defeat; boss A replaces
              ordinary XP, B explicitly includes it; capture uses no role factor.
        Side effects: none; caller must deduplicate event IDs and skipped steps.
        """
        _integer(level, "level", 1, self.curve.level_max)
        _integer(stage, "stage", 0, 2)
        _integer(difficulty, "difficulty", 1, 10)
        if source not in self.rules["source_base_xp"]:
            raise ValueError("unknown source")
        if role not in ("normal", "elite", "boss"):
            raise ValueError("unknown role")
        if (source == "boss" and role != "boss") or (source == "wild" and role == "boss"):
            raise ValueError("boss defeat must be one boss event, never a second wild event")
        base = self.rules["source_base_xp"][source]
        multiplier = self.rules["elite_multiplier"] if source == "wild" and role == "elite" else 1
        if self.option == "A":
            # LV100 has no next-level cost: use the last positive cost C(99).
            result = (base * self.curve.xp_to_next(min(level, self.curve.level_max - 1))
                      * multiplier // self.curve.xp_to_next(self.reference_level))
        else:
            factor = self.rules["stage_multipliers"][stage] * (difficulty + 1)
            result = base * factor * multiplier // 2
            if source == "boss":
                result += self.rules["source_base_xp"]["wild"] * factor // 2
        return _integer(result, "source XP")

    def settle(self, total_xp, mains, appeared, sidekicks):
        """Settle one already-deduplicated event with a loss/issuance ledger.

        Pre: total_xp 0..Int32.MaxValue; mains contains 1..6 Creatures; appeared
             bool sequence matches mains; sidekicks maps every main ID to a list
             of Creatures. All IDs unique across both roles. Input XP is cumulative.
        Post: integer payouts; dead recipients get 0; XP saturates at T(100).
              main_paid + main_loss == total_xp; sidekick_paid + sidekick_loss
              == sidekick_entitlement (Fraction). Main pool excludes sidekick
              extra issuance. No recursive share, banking or within-event cap
              redistribution. Prior XP above cap normalizes without earning XP.
        Side effects: allocates dictionaries; inputs remain unchanged.
        """
        _integer(total_xp, "total_xp")
        if not isinstance(mains, (list, tuple)):
            raise TypeError("mains must be a list or tuple")
        _integer(len(mains), "main count", 1, 6)
        if not isinstance(sidekicks, dict):
            raise TypeError("sidekicks must be a dictionary")
        recipients = list(mains)
        for group in sidekicks.values():
            if not isinstance(group, (list, tuple)):
                raise TypeError("sidekick groups must be lists or tuples")
            recipients.extend(group)
        for creature in recipients:
            if not isinstance(creature, Creature):
                raise TypeError("recipients must be Creatures")
            _integer(creature.id, "instance ID", 1)
            _integer(creature.xp, "cumulative XP")
            if type(creature.dead) is not bool:
                raise TypeError("death flag must be bool")
        ids = [c.id for c in recipients]
        if len(ids) != len(set(ids)):
            raise ValueError("an instance may receive at most one role/share per event")
        if any(type(key) is not int for key in sidekicks) or set(sidekicks) != {c.id for c in mains}:
            raise ValueError("sidekick mapping must match main IDs exactly")
        excluded = [c.dead or (self.option == "B" and c.xp >= self.cap) for c in mains]
        exact = allocate_main_xp(total_xp, appeared, excluded, curve=self.curve)
        assigned = _apportion(exact, [c.id for c in mains], self.option == "A")
        stored = {c.id: min(c.xp, self.cap) for c in recipients}
        paid = {}

        def credit(creature, amount):
            actual = min(amount, self.cap - stored[creature.id])
            stored[creature.id] += actual
            paid[creature.id] = actual
            return actual

        for creature, amount in zip(mains, assigned):
            credit(creature, amount)
        entitlement = Fraction(0)
        sidekick_assigned = {}
        for main in mains:
            group = sidekicks[main.id]
            living = [c for c in group if not c.dead]
            for creature in group:
                paid[creature.id] = 0
                sidekick_assigned[creature.id] = 0
            if not living:
                continue
            half = Fraction(paid[main.id], self.sidekick_divisor)
            if self.option == "A":
                entitlement += half
                budget = half.numerator // half.denominator
                shares = _apportion([Fraction(budget, len(living))] * len(living),
                                    [c.id for c in living], True)
            else:
                entitlement += half * len(living)
                shares = [half.numerator // half.denominator] * len(living)
            for creature, amount in zip(living, shares):
                sidekick_assigned[creature.id] = amount
                credit(creature, amount)
        main_paid = sum(paid[c.id] for c in mains)
        sidekick_paid = sum(paid[c.id] for c in recipients[len(mains):])
        return {"paid": paid, "stored": stored,
                "main_assigned": dict(zip([c.id for c in mains], assigned)),
                "sidekick_assigned": sidekick_assigned,
                "main_pool": total_xp, "main_paid": main_paid,
                "main_loss": total_xp - main_paid,
                "sidekick_entitlement": entitlement, "sidekick_paid": sidekick_paid,
                "sidekick_loss": entitlement - sidekick_paid,
                "extra_issued": sidekick_paid, "total_paid": main_paid + sidekick_paid}

    def d0_events(self, completed_steps=None):
        """Build ordered rewards for the documented representative quest route.

        Pre: d0_run_model follows documented D0 counts; completed_steps is None
             (all genuinely earned) or five bools, never auto-skipped credit.
        Post: 13 normal, 1 elite, 1 boss, 3 successful captures, 5 genuinely
              completed steps and 1 chain bonus by default; skipped steps pay
              zero and any skip suppresses the chain bonus. Integer pools.
        Side effects: none; does not reproduce Unity geometry/RNG or combat.
        """
        run = self.document["d0_run_model"]
        if completed_steps is None:
            completed_steps = (True,) * 5
        if (not isinstance(completed_steps, (list, tuple)) or len(completed_steps) != 5
                or any(type(flag) is not bool for flag in completed_steps)):
            raise ValueError("completed_steps must contain five genuine-completion bools")
        difficulty = run["difficulty"]
        events = []

        def add(source, tier, stage, role="normal"):
            level = self.initial_level(difficulty, tier, stage, role)
            events.append((source, self.source_xp(source, level, stage, difficulty, role)))

        def add_step(index):
            if completed_steps[index]:
                tier = run["quest_step_region_tiers"][index]
                add("quest_step", tier, tier)

        # Representative ordered route; kill/capture predicates have succeeded.
        add_step(0)
        for tier, count in enumerate(run["normal_victims_by_region_tier"]):
            for _ in range(count):
                add("wild", tier, tier)
        add_step(1)
        for elite in run["elite_victims"]:
            add("wild", elite["region_tier"], elite["stage"], "elite")
        add_step(2)
        for capture in run["captures"]:
            if capture["role"] != "boss":
                add("capture", capture["region_tier"], capture["stage"], capture["role"])
        add_step(3)
        boss = run["boss"]
        add("boss", boss["region_tier"], boss["stage"], "boss")
        add_step(4)
        if all(completed_steps):
            add("quest_chain", boss["region_tier"], boss["stage"], "boss")
        for capture in run["captures"]:
            if capture["role"] == "boss":
                add("capture", capture["region_tier"], capture["stage"], "boss")
        return tuple(events)


def simulate(option, party_size=6):
    """Repeat the fixed D0 route to measure earliest completed-run gates.

    Pre: option A/B; party_size 1..6. All recipients start at T(10); two mains
         (or the only main) participate anew in each event; all survive.
    Post: deterministic first-run gains and run counts to LV20/25/30; both
          sidekicks per main modeled as sensitivity, captures stored off-party.
    Side effects: reads proposal/curve; no file writes, no random calls.
    """
    _integer(party_size, "party_size", 1, 6)
    model = LevelRewards(option)
    start = model.curve.xp_threshold(10)
    mains = [Creature(i, start) for i in range(1, party_size + 1)]
    groups = {c.id: [Creature(100 + c.id * 2, start), Creature(101 + c.id * 2, start)] for c in mains}
    appeared = [i < 2 for i in range(party_size)]
    events = model.d0_events()
    gates = {}
    first = None
    for run_index in range(1, 1001):
        for _, amount in events:
            result = model.settle(amount, mains, appeared, groups)
            mains = [Creature(c.id, result["stored"][c.id]) for c in mains]
            groups = {key: [Creature(c.id, result["stored"][c.id]) for c in group]
                      for key, group in groups.items()}
        if first is None:
            first = {"pool": sum(amount for _, amount in events),
                     "active_xp": mains[0].xp - start,
                     "reserve_xp": mains[2].xp - start if party_size > 2 else None,
                     "sidekick_xp": [c.xp - start for c in groups[1]],
                     "level_after_one": model.curve.level_from_xp(mains[0].xp)}
        for gate in (20, 25, 30):
            if mains[0].xp >= model.curve.xp_threshold(gate):
                gates.setdefault(gate, run_index)
        if len(gates) == 3:
            return {"option": option, "party": party_size, **first, "gates": gates}
    raise ValueError("proposal does not reach juvenile gates within 1000 runs")


def pacing_table():
    """Return the Markdown acceptance table, shared by CLI/docs verification.

    Pre: proposal readable and supported. Post: exact six-row A/B pacing table.
    Side effects: reads proposal/curve through simulate; no file writes.
    """
    lines = ["| 方案 | 主力數 | 每 run 主力池 | 出場主力 XP | 後備 XP | 該主力副手各得 | run 1 等級 | 至 LV20 | 至 LV25 | 至 LV30 |",
             "|---|---|---|---|---|---|---|---|---|---|"]
    for option in ("A", "B"):
        for party in (1, 2, 6):
            row = simulate(option, party)
            reserve = row["reserve_xp"] if row["reserve_xp"] is not None else "—"
            sidekicks = "/".join(str(value) for value in row["sidekick_xp"])
            lines.append(f"| {option} | {party} | {row['pool']} | {row['active_xp']} | {reserve} | {sidekicks} | {row['level_after_one']} | {row['gates'][20]} | {row['gates'][25]} | {row['gates'][30]} |")
    return "\n".join(lines)


def main():
    """Print proposal pacing without modifying any data.

    Pre: proposal and approved progression readable. Post: stdout is Markdown.
    Side effects: reads files and writes stdout only.
    """
    print(pacing_table())


if __name__ == "__main__":
    main()
