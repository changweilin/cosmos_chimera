"""Writes the NPC race base specs and gathers their bake reports (docs/design/npc-uma-looks.md#種族基底, ADR-0015).

  python tools/acg-character/npc_bases.py specs [--calibration <report.json>]
      npc/race_bases.json (UmaRaceBaseTests.ExportRaceBases) + npc/template.json -> npc/bases/<id>.json, one per race
      and sex. The human bases are the template itself at the human means and need no calibration; the others need
      race.py's report on npc_human_male, which says how far one key moves each channel and so turns a race's
      departure from the human mean into target weights and spec values.
  python tools/acg-character/npc_bases.py bake --blender <blender.exe> [--jobs N] [--only <id>,...]
      runs race.py on every npc/bases/<id>.json in parallel (outputs in Logs/npc/), then gathers the reports as `keys`.
  python tools/acg-character/npc_bases.py sheet <lineup dir> <out.png>
      npc_lineup.py renders -> one contact sheet: per base its front and side at one common scale, then its face.
  python tools/acg-character/npc_bases.py keys <report.json>...
      race.py reports of the bases -> npc/race_keys.json, read by UmaRaceBaseTests.BakedKeysReachAndShowEveryResident.

Pre: Python 3, no Blender needed. Post: outputs rewritten in full, sorted and LF. Side effects: writes files under npc/.
"""
import argparse
import copy
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
NPC = os.path.join(HERE, "npc")
# Template value of ACG proportions.leg_length; the template stands for the human mean.
TEMPLATE_LEG = 1.06
# Crown to chin of the procedural head in head radii (NpcMeshes.HeadShape), so heads = 1 / (1.98 x headRatio).
HEAD_SPAN = 1.98
# Same cap as race.py TARGET_CAP (暫定): MakeHuman targets extrapolate linearly this far.
TARGET_CAP = 3.0


def _load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def _write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=1, sort_keys=True, ensure_ascii=False)
        f.write("\n")


def _unit_per(report, name, sign):
    """Channel change per unit (target net weight 1, or one channel step of a param) from the calibration report,
    whose keys may have been baked at several units to reach the human extremes."""
    k = report["keys"].get(name + ("+" if sign > 0 else "-"))
    if k is None or not k.get("per") or not k.get("units"):
        return None
    return k["per"] / abs(k["units"])


def _shift(spec, channel, name, delta, report, clipped):
    """Moves the base by delta (race mean minus human mean) along one baked channel."""
    sign = 1 if delta > 0 else -1
    per = _unit_per(report, name, sign)
    if per is None or per <= 0:
        clipped.append("%s: no calibration" % name)
        return
    if channel["kind"] == "target":
        w = delta / per
        targets = spec.setdefault("targets", {})
        neg = channel.get("neg", [])
        for i, pos in enumerate(channel["pos"]):
            opp = neg[i] if i < len(neg) else None
            net = targets.get(pos, 0.0) - (targets.get(opp, 0.0) if opp else 0.0) + w
            lo = -TARGET_CAP if opp else 0.0
            if net > TARGET_CAP or net < lo:
                clipped.append("%s: race mean needs net weight %.2f" % (name, net))
            net = min(TARGET_CAP, max(lo, net))
            targets[pos] = round(max(net, 0.0), 4)
            if opp:
                targets[opp] = round(max(-net, 0.0), 4)
        for t in [t for t, v in targets.items() if v == 0.0]:
            del targets[t]
        return
    section, key = channel["key"].split(".", 1)
    block = spec.setdefault(section, {})
    value = block.get(key, channel.get("default", 0.5 if section == "macro" else None))
    value += delta / per * channel["step"]
    if section == "macro":
        if not 0.0 <= value <= 1.0:
            clipped.append("%s: race mean needs macro %.2f" % (name, value))
        value = min(1.0, max(0.0, value))
    block[key] = round(value, 5)


def specs(calibration):
    table = _load(os.path.join(NPC, "race_bases.json"))["bases"]
    channels = _load(os.path.join(HERE, "race_channels.json"))["channels"]
    template = _load(os.path.join(NPC, "template.json"))
    report = _load(calibration) if calibration else None
    notes = {}
    for bid, row in sorted(table.items()):
        if report is None and row["race"] != "Human":
            continue
        human = table["npc_human_" + row["sex"].lower()]
        spec = copy.deepcopy(template)
        spec.pop("_doc", None)
        spec["id"] = bid
        spec["macro"]["gender"] = 1.0 if row["sex"] == "Male" else 0.0
        spec["height_cm"] = round(100.0 * row["body.height"][0], 1)
        prop = spec["proportions"]
        prop["heads"] = round(1.0 / (HEAD_SPAN * row["body.headRatio"][0]), 3)
        # leg_length stretches everything below the crotch, so the leg share scales roughly with it.
        prop["leg_length"] = round(TEMPLATE_LEG * row["body.legShare"][0] / human["body.legShare"][0], 4)
        clipped = []
        for name, ch in channels.items():
            if ch["kind"] not in ("target", "param"):
                continue
            delta = row[name][0] - human[name][0]
            if abs(delta) > 1e-6:
                _shift(spec, ch, name, delta, report, clipped)
        spec["npc"] = {"race": row["race"], "sex": row["sex"],
                       "dna": {n: v[0] for n, v in row.items() if isinstance(v, list)},
                       "reach": {n: [v[1], v[2]] for n, v in row.items() if isinstance(v, list)}}
        _write(os.path.join(NPC, "bases", bid + ".json"), spec)
        if clipped:
            notes[bid] = clipped
    for bid, c in notes.items():
        print("CLIPPED", bid, "; ".join(c))
    print("SPECS_OK", "all" if report else "human only")


def keys(reports):
    channels = _load(os.path.join(HERE, "race_channels.json"))["channels"]
    out = {}
    for path in reports:
        r = _load(path)
        bases = {}
        for key, k in r["keys"].items():
            name, side = key[:-1], "plus" if key.endswith("+") else "minus"
            mm = max(k["max_mm"].values(), default=0.0)
            entry = {"per": k["per"], "room": 1.0, "mm": round(mm, 3)}
            if channels.get(name, {}).get("mm_exempt"):
                entry["mm_exempt"] = True
            bases.setdefault(name, {})[side] = entry
        out[r["spec"]] = bases
    path = os.path.join(NPC, "race_keys.json")
    old = _load(path)["bases"] if os.path.exists(path) else {}
    old.update(out)
    _write(path, {"_doc": "Per race base: for each baked DNA key, per = channel change of the whole key (weight 1), "
                          "mm = its largest surface move. Written by npc_bases.py keys from race.py reports.",
                  "bases": old})
    print("KEYS_OK", len(out))


def bake(blender, jobs, only):
    import subprocess
    from concurrent.futures import ThreadPoolExecutor
    root = os.path.dirname(os.path.dirname(HERE))
    out = os.path.join(root, "Logs", "npc")
    os.makedirs(out, exist_ok=True)
    ids = sorted(f[:-5] for f in os.listdir(os.path.join(NPC, "bases")) if f.endswith(".json"))
    if only:
        ids = [i for i in ids if i in only.split(",")]

    def one(bid):
        cmd = [blender, "-b", "--python", os.path.join(HERE, "race.py"), "--", "--spec",
               os.path.join(NPC, "bases", bid + ".json"), "--blend", os.path.join(out, bid + ".blend"),
               "--fbx", os.path.join(out, bid + ".fbx"), "--report", os.path.join(out, bid + ".json")]
        with open(os.path.join(out, bid + ".log"), "w", encoding="utf-8") as log:
            code = subprocess.call(cmd, stdout=log, stderr=subprocess.STDOUT)
        print("BAKED" if code == 0 else "FAILED", bid, flush=True)
        return bid, code

    with ThreadPoolExecutor(max_workers=jobs) as pool:
        results = list(pool.map(one, ids))
    done = [os.path.join(out, b + ".json") for b, c in results if c == 0]
    if done:
        keys(done)
    if any(c for _, c in results):
        raise SystemExit("some bases failed; see Logs/npc/<id>.log")


def sheet(src, out):
    from PIL import Image, ImageDraw
    ids = sorted({f.rsplit("_", 1)[0] for f in os.listdir(src) if f.startswith("npc_") and f.endswith("_front.png")})
    cols, cw, ch = 7, 300, 830
    rows = (len(ids) + cols - 1) // cols
    img = Image.new("RGB", (cols * cw, rows * ch), "white")
    draw = ImageDraw.Draw(img)
    for n, bid in enumerate(ids):
        x, y = (n % cols) * cw, (n // cols) * ch
        front = Image.open(os.path.join(src, bid + "_front.png")).convert("RGB").resize((150, 300))
        side = Image.open(os.path.join(src, bid + "_side.png")).convert("RGB").resize((150, 300))
        face = Image.open(os.path.join(src, bid + "_face.png")).convert("RGB").resize((240, 240))
        prof = Image.open(os.path.join(src, bid + "_faceside.png")).convert("RGB").resize((240, 240))
        img.paste(front, (x, y + 20))
        img.paste(side, (x + 150, y + 20))
        img.paste(face, (x + 30, y + 330))
        img.paste(prof, (x + 30, y + 575))
        draw.text((x + 6, y + 4), bid[4:], fill="black")
    img.save(out)
    print("SHEET_OK", len(ids), out)


def main():
    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="cmd", required=True)
    a = sub.add_parser("specs")
    a.add_argument("--calibration")
    b = sub.add_parser("keys")
    b.add_argument("reports", nargs="+")
    d = sub.add_parser("sheet")
    d.add_argument("src")
    d.add_argument("out")
    c = sub.add_parser("bake")
    c.add_argument("--blender", required=True)
    c.add_argument("--jobs", type=int, default=4)
    c.add_argument("--only")
    args = p.parse_args()
    if args.cmd == "specs":
        specs(args.calibration)
    elif args.cmd == "sheet":
        sheet(args.src, args.out)
    elif args.cmd == "bake":
        bake(args.blender, args.jobs, args.only)
    else:
        keys(args.reports)


if __name__ == "__main__":
    main()
