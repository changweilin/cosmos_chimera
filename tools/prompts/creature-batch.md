# 宙獸批次 prompt 模板（P5-01）

給 FLASH／TERRA 的批次初稿 prompt。使用前先讀 `AGENTS.md`；本檔只負責「這一批要產什麼」，規格來源不複製：
`Data/schemas/creature.schema.json`、`Data/balance.json#stage_bands`、`docs/design/creatures.md`、`docs/design/creature-concept-pipeline.md`。

## 人類操作（派工前）
1. `python tools/prompts/quota.py --list-archetypes`：看缺口，挑 10–20 條線（一批 30–60 隻）。
2. 填下方「配額列」；`{{…}}` 全部替換後貼給模型。
3. 輸出放 `Data/_incoming/p5-03-creatures-draft-NN/{creatures/creatures.json,relations/edges.json,README.md}`。
4. 驗證（兩步都要 0 違規／0 錯誤才交 SONNET 審）：
   ```
   python tools/prompts/check_batch.py Data/_incoming/<batch>/creatures/creatures.json
   python tools/validate_data.py --root <含所有 _incoming 草稿的合併目錄>
   ```
   合併方式見既有批次 README §3（`tools/demo-data/merge.py` 或手動疊合）。
5. 依 N8 同步 `../cosmos_chimera_forge/Data/_incoming/`。

---

## PROMPT（從這裡開始複製）

先讀 `AGENTS.md`。你要產出 {{N}} 條完整三段進化線（幼年 `juvenile`／少年 `adolescent`／成年 `adult`，共 {{N*3}} 隻宙獸）的 JSON 初稿。
**只輸出兩個純 JSON 陣列（creatures、edges），不要散文、不要 markdown 說明。**

### 配額列（每條線一列，不得更動）
| line_id（你命名） | 主屬性 | 地區 | 文化原型 ID | 弱點屬性 |
|---|---|---|---|---|
{{QUOTA_ROWS}}

弱點屬性在本批內須讓每種屬性至多出現 {{WEAK_MAX}} 次。

### 輸入
- 已存在的 ID（不得重用；含 `Data/retired-ids.txt`）：{{EXISTING_IDS}}
- 原型資料（只用上表指定的 ID，取**意象**，id／名稱不得含神祇或人物真名）：{{ARCHETYPE_ROWS}}
- 可引用的招式 ID（**未被任何宙獸使用**的）：{{FREE_MOVES}}
- 可引用的特性 ID（**未被任何宙獸使用**的）：{{FREE_TRAITS}}
- 命名風格：英文 snake_case、`<屬性>_<名詞>[_<名詞>]`，同線三階詞根可辨識；id 全域唯一、`^[a-z][a-z0-9]*(_[a-z0-9]+)*$`、≤ 64 字元。

### creatures 規則（每隻都必須符合，違反者整批退回）
欄位完全依 `creature.schema.json`（不得多欄位）。數值為整數，不得出現小數。

| | 幼年 | 少年 | 成年 |
|---|---|---|---|
| 四維總和（con+str+spi+agi） | 100 | 200 | 400 |
| 單項範圍 | 10–50 | 25–100 | 50–200 |
| 主屬性專精（`mastery`） | 15 | 25 | 40，可再加**一個**副屬性 15 |
| 本屬性抗性 | +20 | +35 | +50 |
| 弱點（**恰好一個**負值） | −20 | −30 | −40 |
| `evolution.level` | 20–30 | 35–50 | `evolution: null` |

- 四維每一項隨階段**單調不減**；`resist` 十個鍵全填；其餘抗性為 0 或小幅正值（≤ 本屬性抗性的 1/2），不得再有負值。
- `evolves_to` 指向下一階 id，成年為 `null`；`evolution.materials` 為主屬性素材：幼年 `tier 1`、少年 `tier 2`，`qty` 3–5。
- `size_class`：幼年 XS／S、少年 M（可 L）、成年 L／XL；以身長界線為準（`creatures.md#體型級距`）。
- `ai_style` 對齊 `signature_move` 的 `movement`／`range_class`（遠距→`keep_distance`、突進→`charge`、繞背→`flank`、近戰定點→`hold`）。
- `temperament` 逐隻指定，同線只升不降；**不得使用 `apex`**。批內分布大致：timid 20%／cautious 25%／normal 25%／territorial 20%／fierce 10%。
- `signature_move`：從「可引用的招式」挑，**屬性與主屬性相同**，威力逐階遞增，整批不重複；`trait`：同線三階共用一個特性，整批不重複。
- `concept` 必填鍵：`base`（外觀基底，生物／人形／器械／載具可複合）、`stage_look`、`archetype`（上表 ID）、`region`（上表地區原文）、`motifs_used`（陣列，僅意象）、`role`、`palette`。**不得填 `sensitivity`**，原型為 `review` 者本來就不在配額內。
- 外觀意象與專精／抗性各自設計，不必強制一一對應（Q-C27）。

### edges 規則
- 每條線 2 條 `evolution_family`（幼→少、少→成），其餘跨線關係邊留給 ARGON（P5-06），本批不產。
- 格式 `{"a","b","kind","note"}`，**`a` 字典序小於 `b`**，無重複；`note` 為開發備註，說明同線進化即可。

### 自檢清單（輸出前逐項確認，不輸出）
四維總和／範圍 · 弱點恰一個 · 抗性十鍵 · id 唯一且未被使用 · 招式屬性與威力遞增 · 特性整批不重複 · 無 `apex` · 無真名 id · edges `a < b`。

## PROMPT 結束

---

## 退回規則
`check_batch.py` 或 `validate_data.py` 有任何違規 → 把輸出的錯誤行原文貼回給**同一模型**要求只修那些條目；兩輪仍不過則人工處理。不要請模型「重新整批生成」（會洗掉已通過的條目）。
