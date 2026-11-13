# 生活招式與特性（Field Utilities）

每隻宙獸型態最多擁有 **1 個**場外生活能力（`field_utility`，可為 `none`），分為**生活招式（Move）**與**生活特性（Trait）**。設計核心為「**生活 × 屬性 × 戰鬥**」三位一體契合：生活能力不獨立於戰鬥之外，而是順應屬性的物理與生態法則，透過**副手器皿配對**與**天氣／地形／物質反應**回饋戰鬥決策。

> 規則層實作：`Creatures.FieldUtilityRules`（`Assets/_Project/Features/Creatures/Api/FieldUtilityRules.cs`）；資料列舉：`Data/schemas/common.schema.json#/$defs/fieldUtility`。

## 1. 隊伍與副手生效規則
- **主力（6 隻）**：直接貢獻各自的 `field_utility`（不論 Move 或 Trait）。
- **副手**：僅在**收服道具類別與能力類型匹配**時才對隊伍生效（`FieldUtilityRules.CollectTeamUtilities`）：
  - **符籙卡片**（`talisman_card`）→ 生效 **Move**（同時提供戰鬥 `signature_move`）
  - **琉璃珠**（`glass_bead`）→ 生效 **Trait**（同時提供戰鬥 `trait`）
  - **魔術方塊**（`magic_cube`）→ 僅解鎖主力超載，**不**提供 `field_utility`
- **設計意圖**：玩家挑選副手時，必須同時權衡「關係表連線 × 跨屬性戰鬥招式／特性互補 × 生活能力／地形免疫補齊」。

## 2. 十大屬性「生活—戰鬥—環境」定位
| 屬性 | 生活與生態核心 | 戰鬥／環境聯動契合點 |
|---|---|---|
| **金** `metal` | 磁力回收、精密鎖定、機械設施適應 | 粉碎易碎物（×2）→ 磁吸雙倍拾取素材；高抗控場配合收服鎖定 |
| **木** `wood` | 植物採集、乳品補給、雨林適應 | 雨天威力+10%／CD−10%；進場回血配採集／料理提供無損續航 |
| **水** `water` | 釣魚、潛水、深海適應 | 降雨天氣（+20%/−15%）主場；滅火；將海面／水域轉為資源來源 |
| **火** `fire` | 烹飪料理、恆溫孵蛋、營火復甦、火山適應 | 點燃可燃物與煤油爆燃；將採集／釣魚食材轉為**戰前食物 Buff** |
| **土** `earth` | 破除路障、地質尋寶、礦坑適應 | 粉碎岩石／結晶（×2）＋掉落率提升，構成破壞開路與資源引擎 |
| **冰** `ice` | 凍結留敵、冰原騎乘／疾行、冰冠適應 | 凍結物件變易碎（配土／金粉碎）；暴雪天防止稀有野生逃逸 |
| **風** `wind` | 天氣操縱、漂浮、高空飛翔、太空適應 | **主動切換天氣**（改寫雙方威力/CD/領域/彈道），吹飛輕物件 |
| **雷** `thunder` | 高速騎乘、瞬步脫戰、高塔適應 | 騎乘高速切入接 `charge` 突進開戰；雷暴天移動／導電物質雙加成 |
| **光** `light` | 潛力偵查、光翼飛翔、村落傳送、神廟適應 | **戰前洞察弱點抗性與潛力**，指導換手與投擲收服道具決策 |
| **影** `shadow` | 隊伍隱身、暗影偷竊、迷宮適應 | 迷霧天氣隱蔽疊加；繞過雜魚或偷襲繞背（`flank`）先手掠奪素材 |

## 3. 進階地形免疫（Trait × 10）
對應 [planet-world.md#進階地形與通行限制](planet-world.md#進階地形與通行限制) 的 10 種屬性進階地形地城，皆屬 **Trait（琉璃珠副手可提供）**，必須嚴格匹配對應屬性物種：

| `field_utility` ID | 屬性 | 解除進入限制之進階地形 | 建議階級 | D0 持有物種 |
|---|---|---|---|---|
| `immune_laboratory` | 金 | 機房／工廠／研究室 | 矮行星（少年+） | `metal_gear_strider` |
| `immune_rainforest` | 木 | 雨林 | 矮行星（少年+） | `wood_plume_peafowl` |
| `immune_deep_sea` | 水 | 深海 | 矮行星（少年+） | `water_skiff_eel` |
| `immune_volcano` | 火 | 火山／火山口 | 矮行星（少年+） | `fire_mane_stalker` |
| `immune_cave` | 土 | 礦坑／地底／洞穴 | 矮行星（少年+） | `earth_gilded_warden` |
| `immune_ice_cap` | 冰 | 冰棚／冰冠／永久凍土 | 行星（成年+） | `ice_tundra_dreadnought` |
| `immune_tower` | 雷 | 高塔／摩天樓層 | 行星（成年+） | `star_skyhoof_steed` |
| `immune_space` | 風 | 太空設施／浮空島 | 行星（成年+） | `wind_monsoon_galeon` |
| `immune_labyrinth` | 影 | 迷宮 | 超巨星（超載） | `moon_longnight_wolf` |
| `immune_temple` | 光 | 神廟 | 超巨星（超載） | `sun_array_skyheron` |

## 4. 主動生活招式（Move × 13，符籙卡片副手可提供）
數值與冷卻為設計暫定值，實作時收斂至 `Data/balance.json`。

| ID | 契合屬性 | 場外生活效果 | 與戰鬥／環境／屬性之契合聯動 | D0 持有物種 |
|---|---|---|---|---|
| `weather_control` | 風（／水／火） | 主動切換當前區域天氣至目標天氣（有冷卻） | **核心戰術橋樑**：戰前切至雷暴/降雨/高溫/暴雪/迷霧，直接套用 `WeatherProfiles` 的威力（+15~25%）、CD（−10~15%）與迷霧隱蔽 | `wind_reed_skiff` |
| `party_stealth` | 影（／木／風） | 開啟隊伍隱身，野生領域偵測距離大幅縮小 | 結合 `Fog` 迷霧減半規則可零距離繞背先手；適合 `flank` 繞背刺客避開無謂消耗、直取目標 | `moon_night_pup` |
| `steal_item` | 影（／雷／風） | 對未開戰或開戰中目標發動竊取，取得 1 個屬性素材或消耗品 | 不擊倒目標即可先掠奪素材，適合與 `prevent_escape` 搭配「先偷再收服」 | `moon_shade_wolf` |
| `cook_fire` | 火 | 消耗採集／釣魚食材現場生火料理 | 回復隊伍 HP 並賦予**限時戰前食物 Buff**（如對應屬性專精/抗性或翻滾 CD 減免），串起採集與戰鬥 | `fire_ember_cub` |
| `campfire_revive` | 火／光 | 紮營點燃營火，復甦 1 隻昏厥的主力宙獸（每場 run 限次或耗材） | 解決 D0-R1「昏厥持續整場 run」的續航痛點；僅限火/光療癒系持有，不跨屬硬塞 | *(預留 P5 火/光療癒種)* |
| `open_obstacle` | 土（／金／火） | 直接破壞阻擋道路或捷徑的巨岩／封鎖物 | 與 `destructibles.md` 土/金粉碎易碎物同源；打通地城捷徑並依物質觸發素材掉落 | `earth_auric_sovereign` |
| `mount` | 雷／冰／土／風 | 騎乘宙獸高速移動，免疫地貌基礎移動懲罰 | 縮短大星球（640–1280 m）趕路時間；遇敵解鞍立即銜接衝鋒/進場觸發（`on_summon`） | `star_storm_colt`、`ice_rime_strider` |
| `fly` | 風／光／火 | 短時間高空滑翔／飛越地形高低差與海面 | 跨越峽谷、裂谷與水域群島的立體機動，搶佔遠程高地視線 | `sun_heliostat_heron` |
| `levitate` | 風（／光／電） | 隊伍懸浮移動，無視沼澤、深沙與冰原低摩擦打滑 | 專門反制 `planet-world.md#地貌行動影響` 的地形減速與滑行慣性懲罰 | `wind_flute_cruiser` |
| `dive` | 水 | 潛入深水／海底區域探索水下 POI 與資源 | 開放水系／海溝區域探索動線，於水域群島與峽灣取得稀有水屬性素材 | `water_catamaran_serpent` |
| `fishing` | 水 | 於海面或河流釣取水產食材與低階素材 | 供給 `cook_fire` 高品質食材，將水系地形轉為穩定補給站 | `water_wave_elver` |
| `quick_escape` | 雷／風／影 | 強制脫離野生戰鬥並拉開安全距離（BOSS 戰不可用） | 遇見屬性嚴重不利或高階菁英時保命止損，保留已獲素材與收服成果 | `star_blaze_foal` |
| `return_village` | 光／木／風 | 引導隊伍傳送回最近的已探索村落或起始城鎮 | 大星球探索滿載後快速回補／安全撤離至聚落節點 | `wood_canopy_regent` |

## 5. 被動生活特性（Trait × 8，琉璃珠副手可提供）
| ID | 契合屬性 | 場外生活效果 | 與戰鬥／環境／屬性之契合聯動 | D0 持有物種 |
|---|---|---|---|---|
| `scout_potential` | 光（／金／雷） | 接近未開戰野生時顯示詳細弱點抗性、個體潛力與掉落預覽 | **戰前情報決策**：針對目標負抗性切換主力／副手屬性，並判斷該「擊倒刷素材」或「留手收服」 | `sun_mirror_chick` |
| `catch_rate_up` | 金／光／木 | 提升投擲三類收服道具的基礎收服成功率 | 搭配 `prevent_escape` 與控血戰術，顯著降低稀有菁英／BOSS 捕獲失敗風險 | `metal_chronos_engine` |
| `prevent_escape` | 冰／土／影 | 凍結／封鎖野生宙獸退路，大幅降低其脫戰或逃跑機率 | 與冰屬性控場意象一致；確保高價值目標不因拉扯過遠（>40 m）而脫戰重置 | `ice_frost_fledgling` |
| `drop_rate_up` | 土（／金／影） | 提升擊倒野生與破壞物件時的素材／珠寶掉落率 | 與 `destructibles.md` 破壞掉落聯動，加速進化素材與 Tier 3 珠寶累積 | `earth_pebble_pawn` |
| `magnet_pickup_double` | 金（／雷） | 自動磁吸周圍掉落物，並機率／固定使拾取的碎片素材數量雙倍 | 戰鬥中順帶打壞建築／岩石後無須繞場撿取，直接擴大破壞經濟收益 | `metal_clock_chick` |
| `forage_berries` | 木 | 經過森林／草叢散佈物時自動或互動採集回復漿果 | 提供戰間低成本 HP 回復與 `cook_fire` 基礎食材來源 | `wood_fern_chick` |
| `produce_milk` | 木／土（草食哺乳系） | 定期產出營養乳品（可直接使用或作為料理基底） | 提供高回復量戰間補給，僅限符合生態原型的草食哺乳類宙獸持有 | *(預留 P5 草食哺乳種)* |
| `egg_hatch_boost` | 火（／光） | 縮短大廳孵蛋所需的場次或步數條件 | 恆溫孵化意象；讓火屬性主力／副手兼具高爆發與育成加速價值 | `fire_pyre_monarch` |

## 6. 多屬性戰鬥與生活搭配範例（Multi-Element Combat-Paired）
以下展示如何透過「主力（戰鬥核心）+ 副手（跨屬性招式/特性 + 生活能力）」組成高契合隊伍：

1. **風雷極速控場組**：主力 `star_storm_colt`（雷+風，高突進 `charge` + `mount` 騎乘）＋ 符籙副手 `wind_reed_skiff`（風，`weather_control` + 風招式）。戰前切**雷暴**天氣，騎乘高速切入開戰，享雷招 +25% 威力／−15% CD；遇雷抗敵人則改用副手風招。
2. **光影情報掠奪組**：主力 `moon_longnight_wolf`（影+冰，`flank` 繞背 + `immune_labyrinth`）＋ 琉璃珠副手 `sun_mirror_chick`（光，`scout_potential` + 光射程特性）或 `metal_chronos_engine`（金+土，`catch_rate_up`）。先偵查弱點、迷霧隱身繞背偷襲，高潛力目標直接提昇捕獲率收服。
3. **火木永續烹飪組**：主力 `fire_ember_cub`（火，`cook_fire` + 燃燒反應）＋ 琉璃珠副手 `wood_fern_chick`（木，`forage_berries` + `trait_wood_photosynthesis` 進場回血）。木補火打水/土盲區，森林採果現場生火料理轉化為戰前 Buff。
4. **土金破障尋寶組**：主力 `earth_auric_sovereign`（土，`open_obstacle` + 粉碎易碎物）＋ 琉璃珠副手 `metal_clock_chick`（金，`magnet_pickup_double` + 合金抗性）。大範圍重擊轟碎岩石建築，自動磁吸雙倍進化素材。
