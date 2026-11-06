# 宙獸系統

## 資料模型（schema：`Data/schemas/creature.schema.json`，P0-05 建立）
| 欄位 | 說明 |
|---|---|
| `id` | 全域唯一 snake_case |
| `line_id` / `stage` | 進化線 ID；`juvenile` 幼年 / `adolescent` 少年 / `adult` 成年 / `overload` 超載（只在戰鬥中短暫存在，見〈超載〉） |
| `size_class` | 體型級距 `S` / `M` / `L` / `XL`（見〈體型級距〉） |
| `evolves_to` | 下一階段 ID（成年為 null） |
| `evolution` | `{exp, materials: [{element, tier, qty}]}`；成年為 null（見〈進化〉） |
| `resist` | 十屬性抗性：`{metal, wood, water, fire, earth, ice, wind, thunder, light, shadow}` 整數 % |
| `mastery` | 1–3 個屬性專精 `{element: %}`（加成同屬性招式） |
| `stats` | `con` 體質 / `str` 力量 / `spi` 精神 / `agi` 敏捷 |
| `signature_move` | 一個精通招式 ID |
| `trait` | 一個特性 ID |
| `concept` | 外觀組合 + 文化原型（見 `creature-concept-pipeline.md`） |

## 十大屬性
金 metal · 木 wood · 水 water · 火 fire · 土 earth · 冰 ice · 風 wind · 雷 thunder · 光 light · 影 shadow
**沒有全域相剋表**：相剋完全由每隻宙獸自己的 `resist` 表達（負值＝弱點）。設計批次時以抗性分布檢查平衡，而不是查相剋表。（Q-C1 定案）

## 四大能力值
| 能力 | 影響 | 備註 |
|---|---|---|
| 體質 CON | 最大 HP | |
| 力量 STR | 攻擊範圍 / 射程 | **不影響傷害**；傷害只看招式威力 × 專精 × 抗性（Q-C2 定案） |
| 精神 SPI | 招式 CD 速度 | CD 需設下限，避免無限連發 |
| 敏捷 AGI | 移動速度、閃避率 | 閃避率需設上限 |

## 招式（`Data/moves/`）
`id, element, kind(damage|effect|both), power, effect{...}, cooldown_ms, accuracy(%), range_class(melee|mid|long), shape(line|cone|aoe|dash|...)`

## 特性（`Data/traits/`）
被動效果；以「觸發條件 + 效果」資料描述（例：`on_hp_below:30 → mastery.shadow +20`），不寫死在程式碼。
資料形狀：`{id, trigger: {type, params}, effects: [{type, params}]}`；`type` 詞彙於 P2 收斂為 enum（2026-11-06 定案）。招式 `effect` 用同一形狀。
P2-01 已封閉詞彙（採用 P5-05 初稿的暫定集合，**待人工確認**）：觸發 `always` / `on_hp_below{percent}` / `on_hp_above{percent}` / `on_summon{duration_ms}` / `on_hit_taken{element|any, duration_ms}` / `on_move_used{element, duration_ms}` / `on_ally_swap{duration_ms}`；效果 `mastery_mod{element, delta}` / `resist_mod{element, delta}` / `cd_mod_percent{element|any, delta}` / `evasion_mod_percent{delta}` / `move_speed_mod_percent{delta}` / `range_mod_percent{delta}` / `heal_percent_max_hp{percent}`。正式定義在 `common.schema.json#/$defs/trigger`、`#/$defs/effect`；新增一種 = schema + `GameData` enum + P2-09 處理器。觸發是邊緣或持續判定、效果如何疊加，由 P2-09 定。

## 隊伍規則
- 一場遊戲最多攜帶 **6 隻主力**；其中最多 **4 隻**組成上場池，**同時最多 2 隻在場**，其餘在池中輪替（Q-C4 定案）。
- 每隻主力可帶最多 **4 隻副手**，副手**從倉庫挑選**，不佔 6 隻主力名額（Q-C5 定案）。
- 副手提供給主力的內容由它的**收服道具類別**決定（符籙卡片→招式／琉璃珠→特性／魔術方塊→超載），每隻副手只提供一項；作為主力時道具類別無差別。規則細節見 `lobby-meta.md#收服道具`。
- **只有關係表有連線**的兩隻宙獸才能成為主/副搭配。
- 同一隻宙獸不能同時是主力與副手，也不能同時當兩隻主力的副手 → 暫定，見 Q-C10。

## 進化（Q-C3 定案）
- 幼年 → 少年 → 成年，每段需要：**經驗值達門檻** ＋ **屬性素材**。
- 素材由星球掉落，屬性對應星球類型的屬性偏好（見 `planet-world.md`），所以玩家要去不同類型的星球才能讓不同宙獸進化。
- 暫定：所需素材屬性＝該宙獸的專精屬性，`tier` 隨階段提高。
- 進化在哪裡執行（大廳或場內）、素材規則是否如上 → Q-C9。

## 超載
- 第四型態，**只在戰鬥中短暫進化**，結束後回到原階段；不在 `evolves_to` 鏈上，以 `overload_of` 指向來源階段。
- 主力需要至少一隻以**魔術方塊**收服的副手，才能解鎖**自己的**超載型態。
- 觸發條件、持續時間、哪些階段可超載 → Q-C12。

## 體型級距
| 級距 | 身高/身長 | 典型階段 | 用途 |
|---|---|---|---|
| S | < 0.6 m | 幼年 | 撫摸動作配對（角色蹲下）、模型面數預算 |
| M | 0.6–1.4 m | 少年 | 角色彎腰撫摸 |
| L | 1.4–2.5 m | 成年 | 角色抬手撫摸 |
| XL | > 2.5 m | 成年大型 / 超載 | 角色抬手撫摸前肢或頭部降低的宙獸 |

美術規格（面數、骨架、動作清單）見 `art-asset-pipeline.md`。

## 關係表（`Data/relations/edges.json`）
無向圖：`{a, b, kind, note}`；`kind` 為封閉 enum（2026-11-06 定案）：`evolution_family` 同進化線、`myth_kin` 神話同源、`rival_symbiosis` 天敵共生、`tool_kin` 同工具系、`mentor` 師徒；新增種類需改 `relation.schema.json`。用途：副手資格、圖鑑關係網、劇情台詞。

## 御三家「日月星」（Q-C6 定案）
| 代號 | 專精 | 特長 | 戰鬥定位 |
|---|---|---|---|
| 日 | 光 | 遠距離持續直線傷害（光束、引導施法） | 後排輸出、控距 |
| 月 | 影 | 近距離隱蔽傷害（潛行、背刺、閃現） | 刺客、爆發後撤 |
| 星 | 雷 | 近中距離爆發攻擊（突進、範圍放電） | 衝鋒、群體壓制 |

三隻各有完整三段進化線；概念由 `creature-concept-pipeline.md` 流程產出，人工定稿。

## 各種表（遊戲內 UI，P4）
圖鑑、招式表、特性表、宙獸關係表 — 全部由 `Data/` 生成，不另外維護內容。
