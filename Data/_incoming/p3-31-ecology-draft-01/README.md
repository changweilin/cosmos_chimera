# P3-31 生態棲位（draft-01）

14 種棲位，符合 `Data/schemas/niche.schema.json`；由 `tools/demo-data/merge.py` 複製到 `Data/_demo/niches/`。規則唯一來源：`docs/design/creature-ecology.md`。

- 目錄與數值沿用已定案提案（Q-C50）；`temperament_weights` 改名 `habit_weights`，鍵改為習性 ID（Q-C61）。
- 新增 `exclusion_radius_mm`（三個無限制隨機棲位的出生區排他半徑，Q-C62）：食腐 20 m、頂級掠食 30 m、機會型雜食 15 m。**暫定值，待審**。
- `habitat_kinds` 擴充以讓每個物種形態的候選地貌聯集涵蓋全部 24 種地貌（物種挑選不看地貌）：食腐與機會型雜食為全地貌泛化種，其餘只加生態上相鄰的地貌。**待審**。
- 現有 30 個形態的候選棲位（`creatures.json` 的 `niches`）由 agent 依外觀基底與角色草擬（Q-C64：直接匯入、事後審）。

驗證：`python tools/validate_data.py --root Data/_demo`（含引用、分布順序與地貌覆蓋檢查）。
