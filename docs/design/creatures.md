# 宙獸系統

## 資料模型（schema：`Data/schemas/creature.schema.json`，P0-05 建立）
| 欄位 | 說明 |
|---|---|
| `id` | 全域唯一 snake_case |
| `line_id` / `stage` | 進化線 ID；資料契約目前為 `juvenile` 幼年 / `adolescent` 少年 / `adult` 成年；超載見〈超載〉（待 P2-05 接線） |
| `size_class` | 體型級距 `XS` / `S` / `M` / `L` / `XL`（見〈體型級距〉） |
| `ai_style` | 戰鬥 AI 走位風格 `keep_distance` / `flank` / `charge` / `hold`，逐階必填（Q-C21，見 [combat.md#戰鬥 AI](combat.md)） |
| `temperament` | 性格，決定野生時的領域距離；逐階固定、不隨機（種類與距離見 Q-C35、[combat.md#野生領域](combat.md)）。schema 必填 `timid` (0m)／`cautious` (4m)／`normal` (8m)／`territorial` (14m)／`fierce` (20m)／`apex` (28m)；D0 30 物種逐一指定（`Data/_incoming/q-c35-temperament-terra/proposal.json`：timid 8／cautious 7／normal 4／territorial 8／fierce 2／apex 1；同線不降階，apex 僅火成年霸獅）；新物種批次須逐隻填寫 |
| `evolves_to` | 下一階段 ID（成年為 null） |
| `evolution` | 現行契約 `{exp, materials: [{element, tier, qty}]}`；P2-12 待改為等級門檻，成年仍為 null（見〈進化〉） |
| `resist` | 十屬性抗性：`{metal, wood, water, fire, earth, ice, wind, thunder, light, shadow}` 整數 % |
| `mastery` | 1–3 個屬性專精 `{element: %}`（加成同屬性招式） |
| `stats` | `con` 體質 / `str` 力量 / `spi` 精神 / `agi` 敏捷 |
| `signature_move` | 一個精通招式 ID |
| `trait` | 一個特性 ID |
| `field_utility` | 最多一個場外生活招式／特性（預設 `none`，規格見 [field-utilities.md](field-utilities.md)） |
| `concept` | 外觀組合 + 文化原型（見 `creature-concept-pipeline.md`） |

## 十大屬性
金 metal · 木 wood · 水 water · 火 fire · 土 earth · 冰 ice · 風 wind · 雷 thunder · 光 light · 影 shadow
**沒有全域相剋表**：相剋完全由每隻宙獸自己的 `resist` 表達（負值＝弱點）。設計批次時以抗性分布檢查平衡，而不是查相剋表。（Q-C1 定案）

外觀意象、屬性專精與屬性抗性各自設計；外觀含某元素，不強制具有該屬性專精或正抗性；副專精也不強制伴隨同屬性正抗性（P5-08 A10／Q-C27）。金線成年保留土副專精，水線成年風抗 0、冰線成年金抗 0、日線成年水抗 −15 等現有數值均沿用。

## 四大能力值
| 能力 | 影響 | 備註 |
|---|---|---|
| 體質 CON | 最大 HP | |
| 力量 STR | 攻擊範圍 / 射程 | **不影響傷害**；傷害只看招式威力 × 專精 × 抗性（Q-C2 定案） |
| 精神 SPI | 招式 CD 速度 | CD 需設下限，避免無限連發 |
| 敏捷 AGI | 移動速度、閃避率 | 閃避率需設上限 |

## 等級與成長
等級範圍、御三家起始等級、四維線性成長、各物種解鎖區間及指數經驗曲線統一定義於 [level-progression.md](level-progression.md)。只縮放 CON／STR／SPI／AGI；屬性、基礎抗性、專精與招式威力不隨等級改變。

數值取整、經驗係數與分配（Q-C28～Q-C30）已定案並接入資料契約與執行期，見 [level-progression.md](level-progression.md)。超載規則見〈超載〉（Q-C12 定案）。

## 招式（`Data/moves/`）
`id, element, kind(damage|effect|both), power, effect{...}, cooldown_ms, accuracy(%), range_class(melee|mid|long), shape, base_range_mm, movement?, origin?`

形狀、爆發中心、位移與每招基礎範圍契約統一定義於 `combat.md#招式形狀（動畫與特效分類用）` 及〈力量 → 範圍/射程〉；`power` 與 `mastery` 的表示域見〈傷害公式〉，其餘非負整數受 C# Int32 上限約束。

（P5-08 A2）草稿招式 ID `water_aqua_cutter` → `water_crest_slicer`、`wind_air_slash` → `wind_zephyr_cutter`，避開既有作品招式名稱；舊 ID 登記於 `Data/retired-ids.txt`，不得重用。

## 特性（`Data/traits/`）
被動效果；以「觸發條件 + 效果」資料描述（例：`on_hp_below:30 → mastery.shadow +20`），不寫死在程式碼。
資料形狀：`{id, trigger: {type, params}, effects: [{type, params}]}`；`type` 詞彙於 P2 收斂為 enum。招式 `effect` 用同一形狀。
P2-01 封閉詞彙：觸發 `always` / `on_hp_below{percent}` / `on_hp_above{percent}` / `on_summon{duration_ms}` / `on_hit_taken{element|any, duration_ms}` / `on_move_used{element, duration_ms}` / `on_ally_swap{duration_ms}` / `on_hp_cross_below{percent, duration_ms}`（Q-C18）；效果 `mastery_mod{element, delta}` / `resist_mod{element, delta}` / `cd_mod_percent{element|any, delta}` / `evasion_mod_percent{delta}` / `move_speed_mod_percent{delta}` / `range_mod_percent{delta}` / `heal_percent_max_hp{percent}`。正式定義在 `common.schema.json#/$defs/trigger`、`#/$defs/effect`；新增一種 = schema + `GameData` enum + P2-09 處理器。觸發與疊加的判定見 `combat.md#特性觸發`、`#效果疊加`（P2-09）。

純瞬間治療特性（`effects` 全為 `heal_percent_max_hp`）的 `trigger.params.duration_ms` 統一填 `1000`，僅滿足事件觸發的 schema 必填條件，不代表治療持續時間或冷卻；玩家文案不得寫成「持續 N 秒回血」或顯示這個佔位秒數（P5-08 A17）。混合持續效果的特性保留實際效果時長。

## 隊伍規則
- 一場遊戲最多攜帶 **6 隻主力**；其中最多 **4 隻**組成上場池，**同時最多 2 隻在場**，其餘在池中輪替（Q-C4 定案）。
- 每隻主力可帶最多 **4 隻副手**，副手**從倉庫挑選**，不佔 6 隻主力名額（Q-C5 定案）。
- 副手提供給主力的內容由它的**收服道具類別**決定（符籙卡片→招式／琉璃珠→特性／魔術方塊→超載），每隻副手只提供一項；作為主力時道具類別無差別。場外生活能力（`field_utility`）亦同：主力全數生效，副手僅於器皿類別匹配時生效（符籙卡片→Move、琉璃珠→Trait；詳見 [field-utilities.md](field-utilities.md)）。規則細節見 `lobby-meta.md#收服道具`。
- **只有關係表有連線**的兩隻宙獸才能成為主/副搭配。
- 同一隻宙獸不能同時是主力與副手，也不能同時當兩隻主力的副手；副手現在可取得經驗與升級，經驗以所屬主力的 1/2 為基準，分配細節見 [level-progression.md#經驗分配](level-progression.md#經驗分配)（取代 Q-C10 的不成長規則）。
- **副手階段 ≤ 主力階段**（三種收服道具皆適用，P5-08 N3 方案 c）：傷害只看招式威力 × 專精 × 抗性，不擋的話幼年主力可直接借成年 95–120 威力招。等級提升不會自動切換型態；副手進化後必須重新檢查階段限制。
- 符籙卡片副手提供的招式＝副手物種的 `signature_move`（資料中唯一的招式欄位）。關係邊以型態 id 為準，進化後原搭配可能失效：系統列出失效配對，不自動移除，由玩家在大廳處理。規則層：`Features/Creatures`（`PartnerLoadout`、`Provision`）。

## 進化（Q-C3 定案；新增等級條件見〈等級與成長〉）
- 幼年 → 少年 → 成年，每段需要：**等級達該物種門檻** ＋ **屬性素材**。
- 素材由星球掉落，屬性對應星球類型的屬性偏好（見 `planet-world.md`），所以玩家要去不同類型的星球才能讓不同宙獸進化。
- 掉落規則（Q-C32，方案 B）：**擊倒野生**時判定，每個 spawn 一次、角色不疊加——普通 tier1 10%、精英 tier2 60%、BOSS tier3 100%，成功各給 1 個；屬性從該星球類型的 `element_bias` 等機率抽一種。另一個來源是**破壞場景物件**：tier1 素材與 tier3 珠寶，屬性依物件的物質（見 [destructibles.md#破壞與掉落](destructibles.md)）；節奏模型需把這個來源算進去重估。收服、任務不給素材。素材為整份大廳存檔共用的庫存，與經驗、收服一樣在 run 結束（勝、敗、放棄）時帶回；可提早囤積高 tier。數值在 `Data/balance.json#materials`，提案與節奏模型見 `Data/_incoming/p2-05-material-proposal/proposal.md`。
- 庫存上限（Q-C34）：每種素材上限 int.MaxValue。結算時若任一種會超過上限，**整場獎勵（經驗、收服、素材）都不入帳**，不截斷、不丟棄超量；結算頁停留並指出哪種素材已滿，可重試。正常流程不可達。實作：`LobbySave.WouldOverflow`、`Bootstrap/Demo/LobbyTransactions.Settle`。
- 所需素材屬性＝該宙獸的專精屬性，`tier` 隨階段提高；**只在大廳執行**（Q-C9）。
- 進化保留等級與經驗，不消耗經驗；素材在成功時才扣除，任一不足時一次列出所有缺少項（等級差額、每種素材的持有/需求），不扣任何東西。高階素材不能代替低階。規則層：`Features/Creatures`（`EvolutionRules`、`MaterialDrops`）。

## 超載
- 第四型態，**只在戰鬥中短暫進化**，結束後回到原階段；不在 `evolves_to` 鏈上，以 `overload_of` 指向來源階段。
- 主力需要至少一隻以**魔術方塊**收服的副手，才能解鎖**自己的**超載型態。
- **僅成年**（暫定）可超載，由**玩家指令**（暫定）觸發，**持續至戰鬥結束**、每場戰鬥一次（暫定）（Q-C12 定案）。

## 體型級距
| 級距 | 身高/身長 | 典型階段 | 用途 |
|---|---|---|---|
| XS | < 0.3 m | 幼年（手掌／昆蟲級） | 角色托在掌心輕撫（`chr_pet_xs`）；面數 3–5k、貼圖 512² |
| S | 0.3–0.6 m | 幼年 | 撫摸動作配對（角色蹲下）、模型面數預算 |
| M | 0.6–1.4 m | 少年 | 角色彎腰撫摸 |
| L | 1.4–2.5 m | 成年 | 角色抬手撫摸 |
| XL | > 2.5 m | 成年大型 / 超載 | 角色抬手撫摸前肢或頭部降低的宙獸 |

美術規格（面數、骨架、動作清單）見 `art-asset-pipeline.md`。
上表界線是唯一來源（P5-08 A9）；forge `catalog.js` 的 `SIZE_CLASSES` 身長範圍跟隨本表（catalog@2.2.0），改界線時兩邊同步。
御三家月／星線的少年固定為 M、1.4 m，成年固定為 L、2.5 m（Q-C24 方案 1）；forge `gen_designed.py` 的 `adult_dims` 與 `stage_dims` 採相同尺寸。

`size_class` 必填，JSON 使用大寫 XS／S／M／L／XL（XS：上限 0.3 m、面數與撫摸動作取自 forge `catalog.js`）；狐狸樣本三階段分別 S／M／L。體型由內容指定，不由階段自動推算；超載欄位留給 P2-05（Q-C12 已定案）。

## 關係表（`Data/relations/edges.json`）
無向圖：`{a, b, kind, note}`；`kind` 為封閉 enum：`evolution_family` 同進化線、`myth_kin` 神話同源、`rival_symbiosis` 天敵共生、`tool_kin` 同工具系、`mentor` 師徒；新增種類需改 `relation.schema.json`。用途：副手資格、圖鑑關係網、劇情台詞。
`note` 是**開發考據備註**（P5-08 N1）：不顯示給玩家，可寫原型真名、地球族名／地名與設計術語；理由必須與兩端宙獸的現行設定相符，引用未採用的母題要明講。玩家端關係描述與劇情台詞另寫在文案檔，不從 `note` 生成。

## 御三家「日月星」（Q-C6 定案）
| 代號 | 專精 | 特長 | 戰鬥定位 |
|---|---|---|---|
| 日 | 光 | 遠距離瞬發直線光束；幼年削弱閃避、成年削弱光抗 | 後排輸出、控距 |
| 月 | 影 | 近距離繞背傷害（繞背、直線突進、削弱影抗） | 刺客、貼身纏鬥（P5-08 A6：戰鬥無潛行／自動後撤機制） |
| 星 | 雷 | 幼年／成年直線突進；少年以自身為中心範圍放電 | 衝鋒；少年群體壓制 |

三隻各有完整三段進化線；概念由 `creature-concept-pipeline.md` 流程產出，人工定稿。
（P5-08 A8）定位按各階段現行精通招式描述；日線不採引導施法，星線幼年／成年不承諾範圍放電。

## 英文命名

（P5-08 A3／A4、Q-C25）水線成年 `water_catamaran_serpent` 英文名採 `Brinamar`，冰線成年 `ice_tundra_dreadnought` 採 `Rimbragard`，星線少年 `star_storm_colt` 採 `Voltrmane`，區分跨線重複字尾。星線英文保留 `Volt-` 雷電詞根（`Voltfoal → Voltrmane → Voltrazent`），繁中保留星蹄意象；其餘名稱與 ID 沿用，允許不同 ID 共用 `_strider` 字尾。

## 各種表（遊戲內 UI，P4）
圖鑑、招式表、特性表、宙獸關係表 — 全部由 `Data/` 生成，不另外維護內容。宙獸、招式、特性是**各自獨立的圖鑑**：招式/特性不屬於任何宙獸，多隻宙獸可引用同一個 `signature_move` / `trait` ID。
