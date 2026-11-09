"""P2-12 exact reference for approved level rules; not Unity runtime wiring.

REQ: docs/design/level-progression.md; coefficients: Data/balance.json#progression.
Fractional allocation remains exact here; level_rewards handles integer settlement.
Run directly to print the deterministic integer XP table for a C# port.
"""

import argparse
from bisect import bisect_right
from fractions import Fraction
from functools import lru_cache
import json
from pathlib import Path

from derived_stats import INT32_MAX, REPO, _ceil_div, _integer, _unique_object


CONFIG = REPO / "Data/balance.json"


class LevelProgression:
    """Map accumulated XP and current-form stats to approved level outputs.

    Pre: path contains the version-1 approved curve and integer coefficients.
    Post: thresholds strictly increase and fit Int32; outputs are deterministic.
    Side effects: reads path once and allocates an instance-local integer table.
    """

    def __init__(self, path=CONFIG):
        document = json.loads(Path(path).read_text(encoding="utf-8"),
                              object_pairs_hook=_unique_object)
        version = _integer(document["model_version"], "model_version", 1, 1)
        c = document["progression"]
        self.level_max = _integer(c["level_max"], "level_max", 100, 100)
        self.appeared_weight = _integer(c["appeared_weight"], "appeared_weight", 1)
        self.reserve_weight = _integer(c["reserve_weight"], "reserve_weight", 1)

        first_cost = _integer(c["xp_first_level_cost"], "xp_first_level_cost", 1)
        numerator = _integer(c["xp_growth_numerator"], "xp_growth_numerator", 1)
        denominator = _integer(c["xp_growth_denominator"], "xp_growth_denominator", 1)
        if numerator <= denominator:
            raise ValueError("exponential XP growth must exceed one")

        thresholds = [0]
        costs = []
        for exponent in range(self.level_max - 1):
            # Never grow a rounded previous cost: that compounds quantization error.
            cost = _ceil_div(first_cost * numerator ** exponent, denominator ** exponent)
            total = thresholds[-1] + cost
            if total > INT32_MAX:
                raise ValueError("cumulative XP exceeds the current Int32 storage contract")
            costs.append(cost)
            thresholds.append(total)
        self._version = version
        self._costs = tuple(costs) + (0,)
        self._thresholds = tuple(thresholds)

    def scaled_stat(self, base_stat, level):
        """Return one scaled current-form CON, STR, SPI or AGI value.

        Pre: base_stat is an integer in 0..Int32.MaxValue; level is 1..100.
        Post: half-up rounding of base_stat*(level+10)/110; 0..base_stat;
              monotone in level; equals base_stat at level 100.
        Side effects: none.
        """
        _integer(base_stat, "base_stat")
        _integer(level, "level", 1, self.level_max)
        return (base_stat * (level + 10) + 55) // 110

    def xp_threshold(self, level):
        """Return the minimum accumulated XP needed for a level.

        Pre: level is an integer in 1..100.
        Post: non-negative Int32; zero iff level==1; strictly increases by level.
        Side effects: none.
        """
        _integer(level, "level", 1, self.level_max)
        return self._thresholds[level - 1]

    def xp_to_next(self, level):
        """Return the full cost from this level to the next, zero at the cap.

        Pre: level is an integer in 1..100.
        Post: positive Int32 for level<100; zero for level==100.
        Side effects: none.
        """
        _integer(level, "level", 1, self.level_max)
        return self._costs[level - 1]

    def level_from_xp(self, xp):
        """Return the highest level whose accumulated XP threshold is met.

        Pre: xp is an integer in 0..Int32.MaxValue.
        Post: result in 1..100; T(result)<=xp; result==100 or xp<T(result+1).
              XP beyond T(100) maps to 100; stored XP is neither changed nor capped.
        Side effects: none.
        """
        _integer(xp, "xp")
        return bisect_right(self._thresholds, xp)


@lru_cache(maxsize=1)
def _default_curve():
    return LevelProgression()


def main_reward_weights(appeared, dead, *, curve=None):
    """Return the approved XP weight for each main creature in party order.

    Pre: appeared and dead are lists/tuples of bool, same length in 1..6;
         appeared records whether each individual ever entered during the
         current event, including creatures since swapped out; curve is None
         (cached approved configuration) or a LevelProgression instance.
    Post: each weight is 0 if dead, otherwise the configured appeared/reserve weight;
          repeated entries or time spent on field cannot multiply the weight.
    Side effects: allocates a tuple; reads approved data on first default use.
    """
    for name, flags in (("appeared", appeared), ("dead", dead)):
        if not isinstance(flags, (list, tuple)):
            raise TypeError(f"{name} must be a list or tuple of bool")
        _integer(len(flags), f"{name} length", 1, 6)
        if any(type(flag) is not bool for flag in flags):
            raise TypeError(f"{name} must contain only bool")
    if len(appeared) != len(dead):
        raise ValueError("appearance and death flags must refer to the same party")
    if curve is None:
        curve = _default_curve()
    if not isinstance(curve, LevelProgression):
        raise TypeError("curve must be a LevelProgression instance")
    return tuple(0 if is_dead else curve.appeared_weight if has_appeared else curve.reserve_weight
                 for has_appeared, is_dead in zip(appeared, dead))


def allocate_main_xp(total_xp, appeared, dead, *, curve=None):
    """Divide event XP exactly among living mains by configured weights.

    Pre: total_xp is an integer in 0..Int32.MaxValue; flag arguments satisfy
         main_reward_weights's contract and reflect the current event; curve
         supplies the configured weights, or None selects approved defaults.
    Post: immutable tuple of non-negative Fractions in party order; dead mains
          get zero and are excluded from the denominator. If any main lives,
          rewards sum to total_xp and reward/weight is identical for survivors;
          if all mains are dead, all rewards are zero. No rounding is applied.
    Side effects: allocates values; reads approved data on first default use.
    """
    _integer(total_xp, "total_xp")
    weights = main_reward_weights(appeared, dead, curve=curve)
    denominator = sum(weights)
    if denominator == 0:
        return tuple(Fraction(0) for _ in weights)
    return tuple(Fraction(total_xp * weight, denominator) for weight in weights)


class EventParticipation:
    """Track each main's appearance once within one battle or reward event.

    Pre: main_ids is a list/tuple of 1..6 unique positive Int32 instance IDs;
         callers use separate instances for overlapping event scopes.
    Post: party order is copied and stable; no appearances initially recorded.
    Side effects: allocates an instance-local participation set.
    """

    def __init__(self, main_ids):
        if not isinstance(main_ids, (list, tuple)):
            raise TypeError("main_ids must be a list or tuple")
        _integer(len(main_ids), "party size", 1, 6)
        for instance_id in main_ids:
            _integer(instance_id, "instance_id", 1)
        if len(set(main_ids)) != len(main_ids):
            raise ValueError("party instance IDs must be unique")
        self._main_ids = tuple(main_ids)
        self._appeared = set()

    def record_entry(self, instance_id):
        """Remember a main entering the field without counting repeat entries.

        Pre: instance_id is a positive Int32 ID from this party.
        Post: this main remains marked until reset_event, even after swapping out;
              other mains' flags are unchanged. Repeated calls are idempotent.
        Side effects: mutates this event's participation set only.
        """
        _integer(instance_id, "instance_id", 1)
        if instance_id not in self._main_ids:
            raise ValueError("instance_id is not in this party")
        self._appeared.add(instance_id)

    def reset_event(self):
        """Clear appearances before the next event in the same scope.

        Pre: no unresolved settlement still depends on this event's appearances.
        Post: every main is unmarked; party order and external death state unchanged.
        Side effects: clears this event's participation set only.
        """
        self._appeared.clear()

    def allocate(self, total_xp, dead):
        """Compute exact event rewards using current participation and death flags.

        Pre: total_xp satisfies allocate_main_xp; dead contains one bool per main
             in the original party order, reflecting death at settlement.
        Post: same guarantees as allocate_main_xp; appearance history preserved
              so independent death snapshots do not lose swap history.
        Side effects: allocates rewards and a flag tuple; no state mutations.
        """
        appeared = tuple(instance_id in self._appeared for instance_id in self._main_ids)
        return allocate_main_xp(total_xp, appeared, dead)


def main():
    """Print an integer XP table or resolve a supplied XP total.

    Pre: --config meets LevelProgression's contract; --xp meets its XP contract.
    Post: stdout contains JSON matching the model.
    Side effects: reads configuration and writes stdout; no file mutations.
    """
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=CONFIG)
    parser.add_argument("--xp", type=int)
    args = parser.parse_args()
    model = LevelProgression(args.config)
    if args.xp is not None:
        output = {"xp": args.xp, "level": model.level_from_xp(args.xp)}
    else:
        output = {"model_version": model._version, "levels": [
            {"level": level, "total_xp": model.xp_threshold(level),
             "next_level_xp": model.xp_to_next(level)}
            for level in range(1, 101)
        ]}
    print(json.dumps(output, indent=2))


if __name__ == "__main__":
    main()
