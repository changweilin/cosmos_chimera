# 訓練家對戰：地形試煉與戰鬥旅人

與「有主人的宙獸」對戰的共用規則，以及兩種來源：10 種進階地形的**地形試煉**（類似道館戰）與各星球的**戰鬥旅人**。新手任務的好友 1vs1 初戰與「BOSS＋宿敵 vs 玩家＋好友」2vs2 戰（[beginner-quest.md](beginner-quest.md)）也套用本檔共用規則。
標「暫定（Q-Gn）」者為 agent 先行決定、使用者可在 `../open-questions.html` 推翻；其餘為使用者指定。數值一律暫定，以 `Data/` 為準。

## 共用規則
- **訓練家所有**：對手宙獸用野生 AI 驅動，但不可收服、不可挑釁／撫摸，擊倒後不掉收服機會。
- **場上上限 2 vs 2**（[combat.md#交戰上限](combat.md#交戰上限)）：雙方各自最多 2 隻在場，其餘待命個體在同伴倒下時依隊伍順序遞補；訓練家對戰期間野生不加入，也不佔野生交戰名額。
- **不可脫戰**：開戰後直到一方全倒才結束。玩家全倒依現行野生戰全滅規則處理（新手任務另有規則）。
- 戰後雙方全回復；同一場 run 中已被擊敗的訓練家不再挑戰。
- 經驗與素材依 [level-progression.md](level-progression.md) 的來源表另立 `XpSource.Trainer`（數值暫定）。

## <a id="地形試煉"></a>地形試煉（10 種進階地形的角色任務）
每種屬性進階地形（[planet-world.md#進階地形與通行限制](planet-world.md#進階地形與通行限制)）有一位**試煉主**角色，對應 [encyclopedia.md#玩家解鎖](encyclopedia.md#玩家解鎖) 的「進階地形解鎖」主線任務角色。

| 屬性 | 進階地形 | 星球階級 | 試煉主隊伍（暫定） |
|---|---|---|---|
| 金／木／水／火／土 | 機房／雨林／深海／火山／礦坑 | 矮行星（少年） | 2 隻 |
| 冰／雷／風 | 冰棚／高塔／太空設施 | 行星（成年） | 3 隻 |
| 影／光 | 迷宮／神廟 | 超巨星（超載） | 4 隻 |

- **出現**：試煉主站在該進階地形地城的入口（地表標記處）。
- **引導進入**：與試煉主對話（角色圖鑑 `basic`）後，由試煉主帶領進入地城；**試煉進行中免除進階地形免疫的進入限制**（[planet-world.md#進階地形與通行限制](planet-world.md#進階地形與通行限制)），試煉外仍需免疫。
- **地城內任務**：試煉主跟隨玩家、不參戰，在步驟切換時以對話框出現。步驟：①擊倒守關菁英 → ②抵達 BOSS 競技場。地城本身由 P3-13／P3-07 生成，本檔不另造關卡。
- **最終戰：試煉主＋地城 BOSS vs 玩家**：抵達競技場後試煉主與地城 BOSS 組隊。敵方場上為 BOSS＋1 隻試煉主宙獸，試煉主其餘宙獸依序遞補；BOSS 倒下後不再補位。全倒即勝利，完成該角色任務（角色圖鑑 `complete`）。BOSS 的 HP 與狂暴照 [combat.md#BOSS-階段](combat.md#BOSS-階段)。
- **收服地城 BOSS**：勝利後可收服該 BOSS，**必定成功**。地城 BOSS 物種固定為持有該地形免疫生活技能（`immune_<地形>`，[field-utilities.md](field-utilities.md)）的物種，收服後即可自行進入同類進階地形。
- **試煉 BOSS 物種（全新設計）**：10 種地形各一條**專屬新進化線**，不沿用既有物種（御三家線等原有地形免疫持有者不變）。與一般宙獸相同有**四型態**（幼年→少年→成年，成年可[超載](creatures.md#超載)），**在哪一階星球就出同階層型態**：矮行星＝少年、行星＝成年、超巨星＝成年＋超載。該屬性專精；`immune_<地形>` 由少年起持有（與 [field-utilities.md#3-進階地形免疫](field-utilities.md) 持有者表「少年+」一致，幼年不持有）。**可收服、可進化**：收服的是遭遇時的型態，之後照一般進化規則（[creatures.md](creatures.md)）。概念走 [creature-concept-pipeline.md](creature-concept-pipeline.md)，人工定稿。新物種定稿前，實作與測試暫用現有持有物種作資料替身（只影響 `Data/`）。
- **試煉主宙獸**：全隊為該屬性專精；型態階段與等級＝該星球 BOSS。
- **獎勵**：首次勝利另取得該屬性**進階器皿**配方（[lobby-meta.md#收服器皿分階](lobby-meta.md#收服器皿分階)）＋試煉章（收藏用，無數值效果）。
- **重複遭遇**：任務完成後，其他星球同屬性進階地形入口仍有試煉主，可選擇再戰（流程相同；無首勝獎勵，BOSS 仍可收服）。
- 落敗：回地城入口，可在同一場 run 內再挑戰，已完成的地城內步驟保留。

## <a id="戰鬥旅人"></a>戰鬥旅人
每顆星球（含新手的搖籃星）在道路與野外散佈帶著宙獸的旅人，依**職業**決定屬性傾向與強度。

### 職業（名稱為草稿，文案由 TERRA 定稿）
| ID | 職業 | 屬性傾向 | 強度係數 `t` |
|---|---|---|---|
| `trv_rookie` | 見習旅人 | 不限 | 0.2 |
| `trv_hiker` | 登山客 | 土、金 | 0.5 |
| `trv_angler` | 釣客 | 水、冰 | 0.5 |
| `trv_ranger` | 森林巡守 | 木、風 | 0.5 |
| `trv_brawler` | 拳術家 | 火、土 | 0.5 |
| `trv_scholar` | 學者 | 雷、金 | 0.5 |
| `trv_bard` | 吟遊詩人 | 風、光 | 0.5 |
| `trv_pilgrim` | 巡禮祭司 | 光、影 | 0.5 |
| `trv_nightwalker` | 夜行者 | 影、冰 | 0.5 |
| `trv_bounty` | 賞金獵人 | 不限 | 0.8 |

十屬性皆至少由兩個職業涵蓋。新增職業只加列，不改既有 ID。

### 強度：介於該星球野生與 BOSS 之間
- 等級 `= 野生普通等級上限 + round(t × (BOSS 等級 − 野生普通等級上限))`，夾在兩者之間（[level-progression.md](level-progression.md)）。
- 型態階段 ≤ 該星球 BOSS 階段；物種從該星球野生池抽，屬性傾向加權（傾向屬性不在本星球池時退回整池）。
- 隊伍數（暫定）：小行星 1–2、矮行星 2、行星 2–3、超巨星 3–4；場上最多 2。

### 生成與遭遇
- 數量（暫定）：小行星 3、矮行星 6、行星 10、超巨星 14。位置在道路旁與區域交界，不在聚落內、不在進階地形地城內、不在 BOSS 巢穴附近。
- 規則層新增獨立亂數流（同 seed + 同 `GeneratorVersion` → 同結果），不改動既有生成的序列。
- **挑戰**：旅人視線內依職能觸發對戰（見下節〈職能〉）；開戰前玩家離開 25 m 可迴避。

### <a id="職務與職能"></a>職務與職能（等同野生宙獸棲位與習性）
比照 [creature-ecology.md](creature-ecology.md)，訓練家個體以**職務（`TrainerDuty`，等同棲位）**與**職能（`TrainerFunction`，等同習性）**決定野外活動模式、反應距離與出現地貌；不直接以單一職業名稱固定遊蕩半徑。

#### 職能（`TrainerFunction`，對應習性 `Habit`）
| ID | 名稱 | 主動挑戰距離 | 行為 |
|---|---|---|---|
| `observant` | 考據／專注 | 0 m | 專注考據、採集或調律，不主動攔路開戰，僅由玩家主動對話／互動觸發對戰 |
| `escort` | 護衛／戒備 | 4 m | 看守據點或護送行囊，近身 4 m 才主動迎戰 |
| `sparring` | 歷練／切磋 | 8 m | 標準感應距離 8 m 內發現玩家即上前切磋 |
| `warden` | 巡守／管轄 | 14 m | 中遠距 14 m 主動盤查對戰 |
| `pursuit` | 追獵／懸賞 | 20 m | 遠距 20 m 主動衝鋒挑戰 |
| `champion` | 征討／霸主 | 28 m | 超廣域 28 m 壓制追擊 |

#### 職務（`TrainerDuty`，對應棲位 `Niche`）
共 14 種野外／道路職務，各自定義允許地貌 `habitat_kinds`、職能權重 `function_weights`、四種遊蕩模式（`shuttle`／`loop`／`bounded_random`／`unbounded_random`）與兩層有界常態分布（速度千分比、移動預算 ms、停留時間 ms 之 `mean`／`individual_sd`／`action_sd`／`min`／`max`）：
- **驛道／巡守類**：`duty_road_courier`（古道驛傳，`shuttle`）、`duty_circuit_patrol`（山林巡守，`loop`）、`duty_frontier_trailblazing`（荒原拓荒，`unbounded_random`）、`duty_trial_wardenship`（試煉鎮守，`bounded_random`）
- **採集／勘探／考據類**：`duty_waterside_angling`（水域垂釣，`bounded_random`）、`duty_prospect_mining`（礦脈勘探，`shuttle`）、`duty_botanical_foraging`（植群採集，`loop`）、`duty_ruin_excavation`（遺蹟考據，`bounded_random`）、`duty_leyline_tuning`（以太調律，`bounded_random`）
- **武藝／巡禮／遊歷／追獵類**：`duty_martial_training`（荒野苦修，`bounded_random`）、`duty_shrine_pilgrimage`（聖地巡禮，`loop`）、`duty_roving_minstrel`（雲遊傳唱，`unbounded_random`）、`duty_night_watch`（暗夜潛行，`bounded_random`）、`duty_bounty_hunting`（懸賞追獵，`unbounded_random`）

#### 職業體系（遊戲內職業＋地區×時代職業）
每位訓練家的職業來源涵蓋三軸，並各自提供候選職務與權重（所有地貌聯集須涵蓋全部 24 種地貌標籤）：
1. **遊戲內職業**：上表 10 種戰鬥旅人（`trv_*`）、地形試煉主、十屬性生活技能導師、探索者／研究員／孵蛋師／行商。
2. **星球地區（6 類）× 聚落時代（8 時代）職業**：`era_primitive_totem_hunter`（原始圖騰獵師）、`era_agrarian_field_warden`（農耕巡田武者）、`era_mercantile_caravan_guard`（商貿護隊鏢師）、`era_industrial_rail_inspector`（工業鐵道巡檢）、`era_steampunk_airship_navigator`（蒸汽飛艇領航）、`era_future_orbital_ranger`（未來星軌巡航）、`era_wasteland_salvage_hunter`（廢土拾荒獵人）、`era_ancient_relic_keeper`（遠古遺蹟守衛）。
3. **跨文化地區（12 地區）× 文化時間線（15 時代）職業**（對應 `catalog.js`）：東亞符劍修者（`cul_east_asia_talisman_swordsman`）、南亞氣脈苦行者（`cul_south_asia_prana_ascetic`）、東南亞季風領航者（`cul_southeast_asia_monsoon_wayfinder`）、西亞星盤學者（`cul_middle_east_astrolabe_scholar`）、非洲太陽能地脈師（`cul_africa_solarpunk_geomancer`）、歐洲高奇幻魔導騎士（`cul_europe_high_fantasy_mage_knight`）、美洲峽谷空騎手（`cul_americas_canyon_sky_rider`）、大洋洲星象航海師（`cul_oceania_star_navigator`）、中亞草原獵鷹師（`cul_central_asia_steppe_falconer`）、極地極光雪橇斥候（`cul_polar_aurora_sled_scout`）、世界都市賽博駭客（`cul_cosmopolis_cyber_netrunner`）、星際曲率開拓者（`cul_interstellar_warp_pioneer`）。

- **生成順序**：依出生點地貌篩選職業候選職務 → 依權重抽定個體職務 → 自該職務的 `function_weights` 抽職能 → 兩層 Irwin–Hall 常態抽出個體遊蕩平均參數；**同職務不同職業的活動區不可重疊**（競爭排斥，重疊時環狀移位）。
- **城內外邊界**：訓練家與野生宙獸只生成並活動於有人居住的聚落（部落／小鎮／都市／城邦／村落）範圍之外；生成避開聚落範圍，遊蕩與戰鬥移動皆不會進入城內。

### <a id="旅人圖鑑"></a>職業圖鑑
- 以**職業**為圖鑑主體（`CodexKind.Character`，ID 為職業 ID），不是個別旅人。
- 首次對戰該職業 → `basic`；跨所有星球旅途**累積對戰 10 次**（勝負皆計）→ `complete`。
- 對戰次數存在大廳存檔（每職業一個計數），run 中的次數在結算時寫回；中途放棄的 run 不計（沿用 roguelite 規則）。

## 外觀佔位
試煉主與戰鬥旅人在角色立繪與 GLB 完成前，用路人 NPC 程序化生成（[characters.md#路人-npc](characters.md#路人-npc)）：
- 戰鬥旅人：每位個別隨機（種族、性別、年齡、服裝依該星球時代）；職業道具借用最接近的 `NpcRole`（登山客→礦工、釣客→漁夫、森林巡守→獵人、學者→學者、巡禮祭司→祭司、吟遊詩人→藝人、拳術家／賞金獵人→守衛、其餘→搬運工）。
- 試煉主：以角色 ID 雜湊為固定 seed，**每顆星球外觀相同**；加屬性色光環以區別路人。
- 正式美術到位後替換表現層；規則層 ID 不變。
