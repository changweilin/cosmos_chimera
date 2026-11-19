# 地形試煉 BOSS 宙獸 prompt 模板（P5-10）

給 TERRA／HYDROGEN／ARGON 的地形試煉 BOSS 進化線（四型態）產出 prompt 與文件格式規格。使用前先讀 `AGENTS.md`；數值與契約來源不複製：
`Data/schemas/creature.schema.json`、`Data/schemas/encyclopedia.schema.json`、`Data/schemas/relation.schema.json`、`Data/balance.json#stat_budget`、`docs/design/trainer-battles.md#地形試煉`、`docs/design/field-utilities.md#3-進階地形免疫`、`docs/design/encyclopedia.md`。

## 交付目錄與檔案格式

輸出置於 `Data/_incoming/p5-10-trial-bosses-draft-NN/`（並依 N8 同步至 `../cosmos_chimera_forge/Data/_incoming/`）：
1. `creatures/creatures.json` — 符合 `Data/schemas/creature.schema.json` 的 10 條三階進化線（每線 `juvenile`→`adolescent`→`adult` 共 30 個型態；超載由 P2-05 另接，不在本批）；少年與成年 `field_utility: "immune_<地形>"`，幼年不持有。
2. `relations/edges.json` — 符合 `Data/schemas/relation.schema.json`，為每條試煉 BOSS 線的每個型態建立 ≥ 2 條跨線關係邊（`a < b` 字典序，不可與既有批次重複）。
3. `copy-terra.json` — 供 `cosmos_chimera_forge/gen_designed.py` 讀取的中英命名與圖鑑短文（含 `creatures`、`moves`、`traits` 陣列）。
4. `encyclopedia/entries.json` — 符合 `Data/schemas/encyclopedia.schema.json` 的圖鑑條目（`kind: "creature"`、`stage: "adult"`、`status: "review_required"`、`authored_fields` 逐欄列出補寫欄位、`sources` 指向設計文件與 SHA-256）。
5. `README.md` — 批次總覽、10 地形對應表、數值與驗證紀錄。

---

## PROMPT（從這裡開始複製）

先讀 `AGENTS.md`、`docs/design/trainer-battles.md#地形試煉` 與 `docs/design/field-utilities.md`。你要產出 10 條地形試煉 BOSS **進化線**（每線幼年／少年／成年三型態，成年可超載；試煉時依星球階層出同階型態：矮行星少年、行星成年、超巨星成年＋超載）的宙獸 JSON、關係邊 JSON、命名文案 `copy-terra.json` 與圖鑑 `encyclopedia/entries.json`。

### 配額列（10 進階地形 × 10 屬性，不得更動）
| line_id（你命名） | 主屬性 | 進階地形 | `field_utility` | 地區 | 文化原型 ID | 弱點屬性 |
|---|---|---|---|---|---|---|
{{QUOTA_ROWS}}

弱點屬性在本批內須讓 10 種屬性**各自恰好出現 1 次**。

### 輸入
- 已存在的 ID（不得重用；含 `Data/retired-ids.txt`）：{{EXISTING_IDS}}
- 原型資料（只用上表指定的 ID，取**意象**，id／名稱不得含神祇或人物真名）：{{ARCHETYPE_ROWS}}
- 可引用的主動技 ID（**未被任何宙獸使用**，高威力 `damage` 類、非 `field` 形狀）：{{FREE_MOVES}}
- 可引用的被動技 ID（**未被任何宙獸使用**，排除單屬性 `on_hit_taken`）：{{FREE_TRAITS}}
- 命名風格：英文 snake_case、`<屬性>_<名詞>[_<名詞>]`；`line_id` 為 `line_<屬性>_trial_boss`；id 全域唯一、`^[a-z][a-z0-9]*(_[a-z0-9]+)*$`、≤ 64 字元。

### creatures 規則（每隻都必須符合）
欄位完全依 `creature.schema.json`（不得多欄位）。數值為整數。

| 項目 | 試煉 BOSS 進化線（四型態） |
|---|---|
| `stage` | 每線 `"juvenile"`、`"adolescent"`、`"adult"` 各一（可收服、可進化） |
| `evolves_to` / `evolution` | 幼年→少年→成年照一般進化線填寫（格式比照 `Data/_demo` 既有線）；成年皆為 `null` |
| `field_utility` | 少年與成年填入上表 `immune_<地形>`；幼年不填 |
| 六大能力值總和（`hp + speed + yin_atk + yang_atk + yin_def + yang_def`） | 依 `balance.json#stat_budget` 各階段基準：幼年 **400**／少年 **500**／成年 **600**（±5% 內；每多 1 星 −10%） |
| 主屬性專精（`mastery`） | 40，可再加**一個**副屬性 15 |
| 本屬性抗性 | +50 |
| 弱點（**恰好一個**負值） | −40 |

- `resist` 十個鍵全填；其餘抗性為 0 或小幅正值（≤ 25），不得再有負值。
- `size_class`：少年 `M`–`L`、成年 `L` 或 `XL`（BOSS 壓迫感），幼年依造型（以 `creatures.md#體型級距` 為準）。
- `ai_style` 對齊 `signature_move` 的 `movement`／`range_class`（遠距→`keep_distance`、突進→`charge`、繞背→`flank`、近戰定點→`hold`）；能力值 `yin_atk` / `yang_atk` 傾向對齊 `signature_move` 的 `polarity`。
- `niches`：每隻 2–3 個候選棲位與正整數權重，**各候選棲位 `habitat_kinds` 聯集須涵蓋全部 24 種地貌**。
- `signature_move`：從「可引用的主動技」挑，**屬性與主屬性相同**，整批（30 型態）不重複；威力隨階段遞增；`trait`：從「可引用的被動技」挑，整批不重複。
- `concept` 必填鍵：`base`、`stage_look`、`archetype`（上表 ID）、`region`（上表地區原文）、`motifs_used`（陣列，僅意象）、`role`、`palette`。不得填 `sensitivity`。

### encyclopedia/entries.json 規則
- 每個型態產出 1 筆 `kind: "creature"`、`stage` 與該型態相同、`status: "review_required"`。
- `id`：`dex_<creature_id>`；`subject_id` / `game_id`：皆為 `<creature_id>`。
- `authored_fields` 列出所有補寫欄位（如 `["basic.name_zh","basic.name_en","basic.appearance","basic.summary","details.ecology","details.habits","details.habitat","details.growth"]`）。
- `details.reproduction` 依 `encyclopedia.schema.json` 的階段規則（`adult` 為 `null`）。
- `sources` 引用 `docs/design/trainer-battles.md` 與 `docs/design/field-utilities.md`（`storage: "repository"` + 實測 `sha256`）。

## PROMPT 結束
