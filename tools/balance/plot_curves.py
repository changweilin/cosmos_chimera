"""Render deterministic SVG curves with stdlib only; no matplotlib required.

Only the presentation layer uses float coordinates. Every curve value comes
from the integer reference model. Run from any cwd: python tools/balance/plot_curves.py.
"""

from html import escape

from derived_stats import DerivedStats, PERMILLE, PROPOSAL, REPO
import json

OUTPUT = REPO / "docs/design/derived-stats"
COLORS = ("#2563eb", "#b45309", "#7c3aed", "#047857")


def _panel(parts, x0, y0, title, unit, xmax, ymax, series):
    left, top, width, height = x0 + 65, y0 + 72, 390, 180
    parts.append(f'<text x="{x0 + 15}" y="{y0 + 25}" class="heading">{escape(title)}</text>')
    parts.append(f'<text x="{left}" y="{top - 12}">{escape(unit)}</text>')
    for i in range(5):
        x = left + width * i / 4
        y = top + height * (1 - i / 4)
        parts.append(f'<path d="M {left} {y:.2f} h {width}" class="grid"/>')
        parts.append(f'<text x="{left - 9}" y="{y + 4:.2f}" text-anchor="end">{ymax * i / 4:g}</text>')
        parts.append(f'<text x="{x:.2f}" y="{top + height + 19}" text-anchor="middle">{xmax * i // 4}</text>')
    parts.append(f'<path d="M {left} {top} v {height} h {width}" class="axis"/>')
    parts.append(f'<text x="{left + width}" y="{top + height + 38}" text-anchor="end">stat</text>')
    for index, (label, values) in enumerate(series):
        color = COLORS[index]
        points = " ".join(f"{left + width * x / xmax:.2f},{top + height * (1 - y / ymax):.2f}"
                          for x, y in values)
        parts.append(f'<polyline points="{points}" fill="none" stroke="{color}" stroke-width="2.5"/>')
        legend_y = top + height + 59 + index * 17
        parts.append(f'<path d="M {left} {legend_y - 4} h 20" stroke="{color}" stroke-width="3"/>')
        parts.append(f'<text x="{left + 28}" y="{legend_y}">{escape(label)}</text>')


def _document(title, panels, xmax, footer):
    parts = ['<svg xmlns="http://www.w3.org/2000/svg" width="1020" height="1180" viewBox="0 0 1020 1180" role="img">',
             f'<title>{escape(title)}</title>',
             '<desc>Proposed balance coefficients, pending human approval. Curves use the integer reference model.</desc>',
             '<style>text{font:13px sans-serif;fill:#243247}.heading{font-weight:700;font-size:16px}.grid{stroke:#dce3ed;stroke-width:1}.axis{stroke:#738198;fill:none}</style>',
             '<rect width="1020" height="1180" fill="#fff"/>',
             f'<text x="30" y="32" class="heading">{escape(title)}</text>',
             '<text x="30" y="55">PROPOSAL - pending human approval. Fixed-point outputs; plateaus are intentional.</text>']
    for i, (heading, unit, ymax, series) in enumerate(panels):
        _panel(parts, 20 + (i % 2) * 500, 75 + (i // 2) * 355,
               heading, unit, xmax, ymax, series)
    parts.append(f'<text x="30" y="1160">{escape(footer)}</text>')
    parts.append('</svg>')
    return "\n".join(parts) + "\n"


def main():
    """Write design-band and saturation SVGs from the proposal.

    Pre: proposal is valid. Post: SVGs contain deterministic reference curves.
    Side effects: reads proposal; creates OUTPUT and replaces two named SVGs.
    """
    model = DerivedStats()
    c = json.loads(PROPOSAL.read_text(encoding="utf-8"))
    OUTPUT.mkdir(parents=True, exist_ok=True)
    anchors = " / ".join(f'{stage} {band["total_stats"] / 4:g}' for stage, band in c["stage_bands"].items())
    footer = f'Design anchors per stat: {anchors}; effective stat cap {c["stat_effective_cap"]}.'
    for name, xmax in (("design-band", max(1, c["stage_bands"]["adult"]["max_stat"])),
                       ("saturation", c["stat_effective_cap"])):
        xs = list(range(xmax + 1)) if xmax <= 1000 else sorted(set(range(0, xmax + 1, 25)) | {1, 25, 50, 100, 200, xmax})
        rows = [(x, model.derive(x, x, x, x)) for x in xs]

        def curve(key, divisor=1):
            return [(x, row[key] / divisor) for x, row in rows]

        panels = [
            ("CON / maximum HP", "HP", c["hp"]["base"] + c["hp"]["gain"], [("HP", curve("max_hp"))]),
            ("STR / geometry multipliers", "multiplier", 1 + max(c["melee_volume"]["gain_permille"], c["ranged_distance"]["gain_permille"]) / PERMILLE, [
                ("Melee volume", curve("melee_volume_permille", PERMILLE)),
                ("Melee linear dimension (cube root)", curve("melee_linear_permille", PERMILLE)),
                ("Ranged distance", curve("ranged_multiplier_permille", PERMILLE))]),
            ("SPI / effective cooldown", "ms", max(800, c["reference_move"]["cooldown_ms"], c["cd"]["floor_ms"]) * 1.25, [
                (f"Base {base} ms", [(x, model.derive(0, 0, x, 0, cooldown_ms=base)["effective_cd_ms"]) for x in xs])
                for base in (800, c["reference_move"]["cooldown_ms"], 0)]),
            ("AGI / movement", "m/s", (c["speed"]["base_mm_s"] + c["speed"]["gain_mm_s"]) / 1000,
             [("Move speed", curve("move_speed_mm_s", 1000))]),
            ("AGI / evasion and conditional hit", "% after geometric contact", 100, [
                ("Evasion", curve("evasion_permille", 10)),
                ("Hit, accuracy 100%", curve("hit_chance_permille", 10)),
                ("Hit, accuracy 50%", [(x, model.derive(0, 0, 0, x, accuracy_percent=50)["hit_chance_permille"] / 10) for x in xs])]),
            ("STR / ranged distance", "m (surface path)", c["ranged_distance"]["cap_mm"] / 1000 * 1.1, [
                ("Base 10 m (illustrative mid)", [(x, model.derive(0, x, 0, 0, base_range_mm=10000)["ranged_distance_mm"] / 1000) for x in xs]),
                ("Base 24 m (reference long)", curve("ranged_distance_mm", 1000)),
                ("Hard cap, including oversized base range", [(x, c["ranged_distance"]["cap_mm"] / 1000) for x in xs])]),
        ]
        path = OUTPUT / f"{name}.svg"
        path.write_text(_document(f"P2-02 derived stats / {name}", panels, xmax, footer), encoding="utf-8", newline="\n")
        print(path.relative_to(REPO).as_posix())


if __name__ == "__main__":
    main()
