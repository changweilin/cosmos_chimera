"""Compare two P3-10 repro-probe reports (DemoGame -reproProbe) column by column.

Why per column: Q-T6 needs to know *which* layer diverges across machines/backends — raw float bits (option a),
1 mm-rounded heights (option b), or the discrete RunPlan content that gameplay and netcode must agree on.
Exit code 0 = identical rows, 1 = some column differs, 2 = the reports are not comparable (header/rows mismatch).
With --policy b (Q-T6 decided 2026-11-09, docs/decisions/ADR-0007): 0 = PASS, i.e. discrete content identical and
every height and landing displacement within TOLERANCE_M; 1 = FAIL; 2 = not comparable (including missing .samples dumps).
"""
import math
import os
import struct
import sys

COLUMNS = ["baseBits", "baseMm", "surfBits", "surfMm", "seaCount", "plan", "planDiscrete", "landingBits"]
# ADR-0007: content must agree exactly; geometry may differ by up to 3 mm across backends (IL2CPP measured 2.2 mm).
DISCRETE_COLUMNS = ["seaCount", "planDiscrete"]
TOLERANCE_M = 0.003
# Must equal ReproProbe.SampleCount: policy b only counts as evidence when every report row has its full sample set.
SAMPLES_PER_ROW = 2048
# v5 rows: type stage seed radius <COLUMNS>; samples: type stage seed k baseBits surfBits. A row key without the
# boss stage would collide, since juvenile and adolescent bosses share R=640 (ReproProbe.BossStages).
KEY_COLUMNS = 4
RADIUS_INDEX = 3
SAMPLE_KEY_COLUMNS = 3  # type stage seed
SAMPLE_K_INDEX = 3
SAMPLE_COLUMNS = 6


def parse(text):
    """Returns (header lines without '# env', {(type, stage, seed, radius): [column values]})."""
    header, rows = [], {}
    for line in text.splitlines():
        if not line.strip():
            continue
        if line.startswith("#"):
            if not line.startswith("# env "):
                header.append(line)
            continue
        parts = line.split()
        if len(parts) != KEY_COLUMNS + len(COLUMNS):
            raise ValueError(f"malformed row: {line!r}")
        key = tuple(parts[:KEY_COLUMNS])
        if key in rows:
            raise ValueError(f"duplicate row: {key}")
        _landing(parts[-1])
        radius = float(key[RADIUS_INDEX])
        if not math.isfinite(radius) or radius <= 0:
            raise ValueError(f"invalid radius: {key[RADIUS_INDEX]}")
        rows[key] = parts[KEY_COLUMNS:]
    # Two empty or header-only files would otherwise compare IDENTICAL (SOL P3-10 S3).
    if not header or not header[0].startswith("# cosmimera repro-probe "):
        raise ValueError("not a repro-probe report (missing magic header)")
    if not rows:
        raise ValueError("report has no data rows")
    return header, rows


def compare(a_text, b_text):
    """Returns (status, {column: differing row count}, messages)."""
    ha, ra = parse(a_text)
    hb, rb = parse(b_text)
    if ha != hb:
        return 2, {}, ["headers differ (probe/generator version or data set):", *ha, "vs", *hb]
    if ra.keys() != rb.keys():
        return 2, {}, [f"row keys differ: only-a={sorted(ra.keys() - rb.keys())} only-b={sorted(rb.keys() - ra.keys())}"]
    diffs = {c: 0 for c in COLUMNS}
    for key in ra:
        for i, c in enumerate(COLUMNS):
            if ra[key][i] != rb[key][i]:
                diffs[c] += 1
    status = 0 if not any(diffs.values()) else 1
    msgs = [f"{c}: {n}/{len(ra)} rows differ" for c, n in diffs.items()]
    return status, diffs, msgs


def _f32(hex_bits):
    return struct.unpack("<f", struct.pack("<I", int(hex_bits, 16)))[0]


def _landing(bits):
    """Decode v3 landingBits; reject missing/non-finite/zero directions before any policy verdict."""
    parts = bits.split(",")
    if len(parts) != 3 or any(len(p) != 8 or any(c not in "0123456789abcdefABCDEF" for c in p) for p in parts):
        raise ValueError(f"malformed landingBits: {bits!r}")
    direction = tuple(_f32(p) for p in parts)
    length = math.sqrt(sum(v * v for v in direction))
    if not math.isfinite(length) or length == 0:
        raise ValueError(f"non-finite or zero landing direction: {bits!r}")
    return tuple(v / length for v in direction)


def landing_max_distance(a_text, b_text):
    """ADR-0007 geometry: chord displacement in metres at report radius, independent of height and content."""
    _, ra = parse(a_text)
    _, rb = parse(b_text)
    if ra.keys() != rb.keys():
        raise ValueError("landing row keys differ")
    return max(float(key[RADIUS_INDEX]) * math.dist(_landing(ra[key][-1]), _landing(rb[key][-1])) for key in ra)


def sample_stats(a_text, b_text):
    """Max |a-b| in metres and count of samples whose |a-b| > 1 mm, for base and template surface heights.

    Raises ValueError when the dumps do not list the same samples in the same order.
    """
    stats = {"base": [0.0, 0, 0], "surf": [0.0, 0, 0]}  # max abs diff, > 1 mm count, bit-different count
    la, lb = a_text.splitlines(), b_text.splitlines()
    if len(la) != len(lb):
        raise ValueError(f"sample dumps differ in length: {len(la)} vs {len(lb)}")
    for x, y in zip(la, lb):
        pa, pb = x.split(), y.split()
        if len(pa) != SAMPLE_COLUMNS or len(pb) != SAMPLE_COLUMNS:
            raise ValueError(f"malformed sample line: {x!r} / {y!r}")
        if pa[:-2] != pb[:-2]:
            raise ValueError(f"sample keys differ: {pa[:-2]} vs {pb[:-2]}")
        for col, name in ((-2, "base"), (-1, "surf")):
            va, vb = _f32(pa[col]), _f32(pb[col])
            # max() silently drops NaN, which would report a broken height as "0 m apart" (SOL P3-10 S4).
            if not (math.isfinite(va) and math.isfinite(vb)):
                raise ValueError(f"non-finite {name} height in sample {pa[:-2]}")
            if pa[col] == pb[col]:
                continue
            d = abs(va - vb)
            st = stats[name]
            st[0] = max(st[0], d)
            st[1] += d > 1e-3
            st[2] += 1
    return stats, len(la)


def check_sample_coverage(report_text, samples_text):
    """Raises ValueError unless the dump holds exactly k = 0..SAMPLES_PER_ROW-1 once for every report row.

    Why: sample_stats only checks that two dumps align with each other, so two empty or mismatched dumps would
    otherwise "pass" the height tolerance with no evidence (SOL P2-12 R2). Side effects: none.
    """
    _, rows = parse(report_text)
    expected = {key[:SAMPLE_KEY_COLUMNS] for key in rows}
    seen = {}
    for line in samples_text.splitlines():
        parts = line.split()
        if len(parts) != SAMPLE_COLUMNS:
            raise ValueError(f"malformed sample line: {line!r}")
        key = tuple(parts[:SAMPLE_KEY_COLUMNS])
        if key not in expected:
            raise ValueError(f"sample for {key} has no report row")
        ks = seen.setdefault(key, set())
        k = parts[SAMPLE_K_INDEX]
        if k in ks:
            raise ValueError(f"duplicate sample {key} k={k}")
        ks.add(k)
    want = {str(k) for k in range(SAMPLES_PER_ROW)}
    for key in expected:
        if seen.get(key) != want:
            raise ValueError(f"row {key} has {len(seen.get(key, ()))} of {SAMPLES_PER_ROW} expected samples")


def policy_b(diffs, stats):
    """Q-T6 (b) verdict: (passed, reasons). Pre: diffs from compare(); stats from sample_stats(). Side effects: none."""
    reasons = [f"{c} differs in {diffs[c]} rows" for c in DISCRETE_COLUMNS if diffs[c]]
    for name, (mx, _, _) in stats.items():
        if not mx <= TOLERANCE_M:  # also rejects NaN
            reasons.append(f"{name} max |diff| {mx:.6g} m exceeds {TOLERANCE_M} m")
    return not reasons, reasons


def main(argv):
    policy = None
    if len(argv) == 5 and argv[3] == "--policy" and argv[4] == "b":
        policy, argv = "b", argv[:3]
    if len(argv) != 3:
        print("usage: python tools/repro/compare.py <report-a> <report-b> [--policy b]")
        return 2
    with open(argv[1], encoding="utf-8") as fa, open(argv[2], encoding="utf-8") as fb:
        report_a, report_b = fa.read(), fb.read()
        try:
            status, diffs, msgs = compare(report_a, report_b)
        except ValueError as e:
            print(f"NOT COMPARABLE: {e}")
            return 2
    print("\n".join(msgs))
    sa, sb = argv[1] + ".samples", argv[2] + ".samples"
    stats = None
    if status != 2 and os.path.exists(sa) and os.path.exists(sb):
        with open(sa, encoding="utf-8") as fa, open(sb, encoding="utf-8") as fb:
            try:
                samples_a, samples_b = fa.read(), fb.read()
                if policy == "b":
                    check_sample_coverage(report_a, samples_a)
                    check_sample_coverage(report_b, samples_b)
                stats, n = sample_stats(samples_a, samples_b)
            except ValueError as e:
                print(f"NOT COMPARABLE: {e}")
                return 2
        for name, (mx, over, bits) in stats.items():
            print(f"{name} heights: {bits}/{n} samples bit-different, {over} differ by > 1 mm, max |diff| = {mx:.6g} m")
    print("IDENTICAL" if status == 0 else "DIFFERENT" if status == 1 else "NOT COMPARABLE")
    if policy == "b" and status != 2:
        if stats is None:
            print("Q-T6(b) NOT COMPARABLE: both .samples dumps are required to check the height tolerance")
            return 2
        landing = landing_max_distance(report_a, report_b)
        print(f"landing directions: max displacement = {landing:.6g} m")
        stats["landing"] = [landing, 0, 0]
        passed, reasons = policy_b(diffs, stats)
        print("Q-T6(b) PASS" if passed else "Q-T6(b) FAIL: " + "; ".join(reasons))
        return 0 if passed else 1
    return status


if __name__ == "__main__":
    sys.exit(main(sys.argv))
