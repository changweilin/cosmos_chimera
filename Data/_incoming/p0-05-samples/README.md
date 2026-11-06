# P0-05 Sample Fixtures

Acceptance fixtures for `Data/schemas/*.schema.json` + `tools/validate-data`
(tests: `python -m unittest discover -s tools/tests`).

Layout mirrors `Data/` (`creatures/`, `moves/`, `traits/`, `relations/`, `planet-types/`) so this
folder can be validated directly: `tools/validate-data --root Data/_incoming/p0-05-samples/valid`.
Each `invalid/<kind>/<file>.json` is overlaid onto a copy of `valid/` in the tests.

| File | Intended Defect | Reported as |
|---|---|---|
| `moves/bad_id_case.json` | `id` in CamelCase (`SampleMoveStrike`) | `schema` @ `/0/id` |
| `moves/unknown_element.json` | Unsupported element `"poison"` | `schema` @ `/0/element` |
| `moves/float_number.json` | Float value for `power` (`12.5`) | `float_literal` |
| `creatures/missing_resist_key.json` | `resist` missing `"shadow"` | `schema` @ `/0/resist` |
| `creatures/mastery_too_many.json` | 4 elements in `mastery` | `schema` @ `/0/mastery` |
| `creatures/evolves_to_on_adult.json` | Adult stage with non-null `evolves_to` | `schema` @ `/0/evolves_to` (also reuses id `sample_fox_lord` → `duplicate_id`) |
| `creatures/dangling_signature_move.json` | `signature_move` not found in moves | `dangling_ref` |
| `relations/edge_wrong_order.json` | Edge vertices `a > b` | `edge_order` |

## Resolved questions (2026-11-06)
- Top-level shape: every data file is a JSON **array**; all files in a kind folder are merged.
- Material element ≠ mastery element: **warning** `material_not_mastery` (Q-C9 is provisional), not an error.
