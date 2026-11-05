# P0-05 Sample Fixture Defect Mapping

| File | Intended Defect | Violated Rule |
|---|---|---|
| `bad_id_case.json` | `id` in CamelCase (`SampleMoveStrike`) | `data-content.md`: ID must be snake_case |
| `unknown_element.json` | Unsupported element `"poison"` | `creatures.md`: Ten elements only |
| `float_number.json` | Float value for `power` (`12.5`) | `data-content.md`: Numeric fields must be integers |
| `missing_resist_key.json` | `resist` map missing `"shadow"` | `creatures.md`: Resist requires all 10 elements |
| `mastery_too_many.json` | 4 elements in `mastery` | `creatures.md`: Mastery limited to 1–3 elements |
| `edge_wrong_order.json` | Edge vertices `a > b` | `data-content.md`: Undirected edges require `a < b` |
| `dangling_signature_move.json` | `signature_move` not found in moves | `creatures.md`: Must reference valid move ID |
| `evolves_to_on_adult.json` | Adult stage with non-null `evolves_to` | `creatures.md`: Adult `evolves_to` must be null |

## Open Questions
- Top-level schema shape: Should invalid fixture files be arrays `[...]` or single objects `{...}`? (Drafted as 1-element arrays to match `valid/*.json`).
- Material element constraints: Does `evolution.materials[*].element` strictly require matching creature mastery or any of the 10 elements?
