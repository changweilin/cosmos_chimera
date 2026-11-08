# P5-06 關係表建議與連通性分析（草稿 01）

對象：`p2-08-starters-draft-01`（御三家 3 線 9 型態、既有邊 6）＋`p5-03-creatures-draft-01`（首批擴充 7 線 21 型態、既有邊 14），合計 **10 條進化線、30 個型態**。`relations/edges.json` 僅收錄本批新增之 **22 條跨線關係邊**（原 23 條，2026-11-08 刪除 R4）（不含既有 20 條 `evolution_family`）。

## 1. 各類型邊數統計

| 關係種類 (`kind`) | 既有邊數 | 新增提議邊數 | 聯集總邊數 | 依據準則 |
|---|---:|---:|---:|---|
| `evolution_family` | 20 | 0 | 20 | 維持既有線內進化邊，不新增 |
| `myth_kin` | 0 | 1 | 1 | 具真實共同考古／語族文化源流 |
| `rival_symbiosis` | 0 | 7 | 7 | 掠食／獵物天敵或地熱／生態互賴意象 |
| `tool_kin` | 0 | 7 | 7 | 共用精密儀器、連桿重甲、帆船飛行載具或彩繪雕飾器物構造 |
| `mentor` | 0 | 7 | 7 | 他線成年長者引導幼年學徒（守陣、控距、乘風、伺機） |
| **合計** | **20** | **22** | **42** | 平均總度數 2.80（2 度 9 隻／3 度 18 隻／4 度 3 隻），全體介於 2–4（上限 5） |

## 2. 30 型態總度數表（既有 + 新增）

| 進化線 | 型態 ID | 階段 | 既有 | 新增 | 總度數 | 新增跨線鄰點（種類） |
|---|---|---|---:|---:|---:|---|
| `starter_sun_line` | `sun_mirror_chick` | 幼年 | 1 | 2 | 3 | `fire_pyre_monarch`(mentor), `metal_clock_chick`(tool_kin) |
| `starter_sun_line` | `sun_heliostat_heron` | 少年 | 2 | 1 | 3 | `water_wave_elver`(rival_symbiosis) |
| `starter_sun_line` | `sun_array_skyheron` | 成年 | 1 | 2 | 3 | `wind_monsoon_galeon`(tool_kin), `wood_fern_chick`(mentor) |
| `starter_moon_line` | `moon_night_pup` | 幼年 | 1 | 1 | 2 | `star_blaze_foal`(rival_symbiosis) |
| `starter_moon_line` | `moon_shade_wolf` | 少年 | 2 | 0 | 2 | — |
| `starter_moon_line` | `moon_longnight_wolf` | 成年 | 1 | 2 | 3 | `fire_ember_cub`(mentor), `ice_rime_strider`(rival_symbiosis) |
| `starter_star_line` | `star_blaze_foal` | 幼年 | 1 | 2 | 3 | `moon_night_pup`(rival_symbiosis), `wind_monsoon_galeon`(mentor) |
| `starter_star_line` | `star_storm_colt` | 少年 | 2 | 1 | 3 | `fire_mane_stalker`(rival_symbiosis) |
| `starter_star_line` | `star_skyhoof_steed` | 成年 | 1 | 2 | 3 | `ice_frost_fledgling`(mentor), `ice_tundra_dreadnought`(myth_kin) |
| `line_metal_gearclock` | `metal_clock_chick` | 幼年 | 1 | 2 | 3 | `earth_auric_sovereign`(mentor), `sun_mirror_chick`(tool_kin) |
| `line_metal_gearclock` | `metal_gear_strider` | 少年 | 2 | 1 | 3 | `ice_rime_strider`(tool_kin) |
| `line_metal_gearclock` | `metal_chronos_engine` | 成年 | 1 | 3 | 4 | `earth_pebble_pawn`(mentor), `fire_pyre_monarch`(rival_symbiosis), `ice_tundra_dreadnought`(tool_kin) |
| `line_wood_vinecrest` | `wood_fern_chick` | 幼年 | 1 | 1 | 2 | `sun_array_skyheron`(mentor) |
| `line_wood_vinecrest` | `wood_plume_peafowl` | 少年 | 2 | 1 | 3 | `water_wave_elver`(rival_symbiosis) |
| `line_wood_vinecrest` | `wood_canopy_regent` | 成年 | 1 | 1 | 2 | `water_catamaran_serpent`(tool_kin) |
| `line_water_tidecraft` | `water_wave_elver` | 幼年 | 1 | 2 | 3 | `sun_heliostat_heron`(rival_symbiosis), `wood_plume_peafowl`(rival_symbiosis) |
| `line_water_tidecraft` | `water_skiff_eel` | 少年 | 2 | 1 | 3 | `wind_reed_skiff`(tool_kin) |
| `line_water_tidecraft` | `water_catamaran_serpent` | 成年 | 1 | 2 | 3 | `wind_monsoon_galeon`(tool_kin), `wood_canopy_regent`(tool_kin) |
| `line_fire_blazelion` | `fire_ember_cub` | 幼年 | 1 | 2 | 3 | `earth_pebble_pawn`(rival_symbiosis), `moon_longnight_wolf`(mentor) |
| `line_fire_blazelion` | `fire_mane_stalker` | 少年 | 2 | 1 | 3 | `star_storm_colt`(rival_symbiosis) |
| `line_fire_blazelion` | `fire_pyre_monarch` | 成年 | 1 | 2 | 3 | `metal_chronos_engine`(rival_symbiosis), `sun_mirror_chick`(mentor) |
| `line_earth_gildedgolem` | `earth_pebble_pawn` | 幼年 | 1 | 2 | 3 | `fire_ember_cub`(rival_symbiosis), `metal_chronos_engine`(mentor) |
| `line_earth_gildedgolem` | `earth_gilded_warden` | 少年 | 2 | 0 | 2 | — |
| `line_earth_gildedgolem` | `earth_auric_sovereign` | 成年 | 1 | 1 | 2 | `metal_clock_chick`(mentor) |
| `line_ice_frostbeast` | `ice_frost_fledgling` | 幼年 | 1 | 1 | 2 | `star_skyhoof_steed`(mentor) |
| `line_ice_frostbeast` | `ice_rime_strider` | 少年 | 2 | 2 | 4 | `metal_gear_strider`(tool_kin), `moon_longnight_wolf`(rival_symbiosis) |
| `line_ice_frostbeast` | `ice_tundra_dreadnought` | 成年 | 1 | 2 | 3 | `metal_chronos_engine`(tool_kin), `star_skyhoof_steed`(myth_kin) |
| `line_wind_fluteglider` | `wind_reed_skiff` | 幼年 | 1 | 1 | 2 | `water_skiff_eel`(tool_kin) |
| `line_wind_fluteglider` | `wind_flute_cruiser` | 少年 | 2 | 0 | 2 | — |
| `line_wind_fluteglider` | `wind_monsoon_galeon` | 成年 | 1 | 3 | 4 | `star_blaze_foal`(mentor), `sun_array_skyheron`(tool_kin), `water_catamaran_serpent`(tool_kin) |

## 3. 連通性證明（10 條進化線之生成樹）

每條進化線內部已由 2 條 `evolution_family` 邊連成 3 點路徑；以下 **9 條跨線邊**在 10 條進化線構成的商圖上形成一棵無環生成樹，證明 30 個型態聯集圖為**單一連通分量**（且每線皆連向至少 2 條他線，商圖無橋邊、刪去任一跨線邊仍保持連通）：

1. `starter_sun_line` — `line_metal_gearclock`：`(metal_clock_chick, sun_mirror_chick)` [`tool_kin`]
2. `line_metal_gearclock` — `line_earth_gildedgolem`：`(earth_auric_sovereign, metal_clock_chick)` [`mentor`]
3. `line_earth_gildedgolem` — `line_fire_blazelion`：`(earth_pebble_pawn, fire_ember_cub)` [`rival_symbiosis`]
4. `line_fire_blazelion` — `starter_moon_line`：`(fire_ember_cub, moon_longnight_wolf)` [`mentor`]
5. `starter_moon_line` — `starter_star_line`：`(moon_night_pup, star_blaze_foal)` [`rival_symbiosis`]
6. `starter_star_line` — `line_ice_frostbeast`：`(ice_tundra_dreadnought, star_skyhoof_steed)` [`myth_kin`]
7. `starter_sun_line` — `line_wind_fluteglider`：`(sun_array_skyheron, wind_monsoon_galeon)` [`tool_kin`]
8. `line_wind_fluteglider` — `line_water_tidecraft`：`(water_skiff_eel, wind_reed_skiff)` [`tool_kin`]
9. `line_water_tidecraft` — `line_wood_vinecrest`：`(water_catamaran_serpent, wood_canopy_regent)` [`tool_kin`]

## 4. 缺口與理由強度邊界（Gaps / Weak-reason Edges）

- **`myth_kin` 稀缺（僅 1 條；原第 2 條莫圖族—馬拉瑙族於 2026-11-08 改為 `tool_kin`，見 §7 R2）**：P2-08 與 P5-03 刻意分散於 8 個地區、10 個獨立原型，具真實歷史／神話同源者極少。僅「巴澤雷克有翼鹿角獅鷲—哈薩克圖爾帕爾天馬」（同屬歐亞草原有翼神獸母題，屬母題比較，非直系傳承）與「巴布亞莫圖族陶船—菲律賓馬拉瑙彩鳳」（南島語族海洋圖騰圈，屬語族級宏觀同源）成立；南亞雙線（《蘇丹娜之夢》與《希爾與蘭賈》）雖同處次大陸，惟一屬近代科幻、一屬旁遮普愛情詩，無共同神話母體，故以氣動航行器歸入 `tool_kin`，不強湊 `myth_kin`。
- **少年期零跨線邊（2 型態）**：`moon_shade_wolf`、`earth_gilded_warden` 總度數皆靠線內進化邊維持 2，因跨線意象已由同線幼／成年精準承載，依精簡原則不硬湊重複邊。
- **待人工審視之延伸意象（2 條）**：`(earth_pebble_pawn, fire_ember_cub)` 原取「火山湖綠寶石晶核 × 砂岩熔核」之地質熱液共生，2026-11-08 改為「岩殼護燼」單一意象（§7 R3）；`(metal_gear_strider, wind_flute_cruiser)` 原取「鳴鐘音板 × 排笛風孔」之器樂發聲機關，2026-11-08 已刪除（§7 R4）。

## 5. 副手平衡警示（Balance Flags）

- **御三家同主屬性配對**：本批 10 線各佔 1 種主屬性，**無任何跨線邊讓御三家連到同主屬性他線型態**（僅既有線內 `evolution_family` 邊可同屬互配，如 `sun_array_skyheron` 帶 `sun_mirror_chick` 以短 CD `light_mirror_glint` 加速疊滿 `trait_light_heliostat_focus`）。
- **副專精招式解鎖（高協同）**：
  - `(ice_rime_strider, moon_longnight_wolf)`：讓具 `ice: 15` 副專精且全池最高 AGI（150）的成年月狼透過符籙取得遠程直線 `ice_glacier_lance`（威力 85、24 m），同時吃副專精加成並補足純近戰刺客的遠程破綻。
  - `(water_catamaran_serpent, wind_monsoon_galeon)`：`water_catamaran_serpent`（`wind: 15`、STR 150）可借 `wind_tempest_sphere`（威力 105、24 m 爆發），兼具風專精與高 STR 射程放大。
  - `(ice_tundra_dreadnought, metal_chronos_engine)`：`ice_tundra_dreadnought`（`metal: 15`）可借 `metal_lance_charge`（威力 110 突進）獲得長距位移破陣手段。
- **跨階 `mentor` 邊之越階借招**：7 條 `mentor` 邊連結幼年與他線成年（如 `sun_mirror_chick` ↔ `fire_pyre_monarch`、`star_blaze_foal` ↔ `wind_monsoon_galeon`、`fire_ember_cub` ↔ `moon_longnight_wolf`）。因傷害公式只看招式威力 × 專精 × 抗性（不看能力值），幼年主力若裝備成年符籙副手即可施放威力 95–120 的終結技；請人工裁決是否允許越階裝備符籙副手。**已裁決 2026-11-08（P5-08 N3 方案 c）**：副手階段須 ≤ 主力階段，三種收服道具皆適用（`creatures.md#隊伍規則`、`PartnerLoadout.TryAssign` → `SubStageAboveMain`）；7 條 mentor 邊只能由成年當主力、幼年當副手，其餘跨階邊同理。
- **強力琉璃珠特性轉移與補盲**：`sun_array_skyheron`（弱影 −40）連 `wind_monsoon_galeon` 可借 `trait_wind_tailwind_glide`（換手移速 +20%／6 s）強化拉距；反之風天舟可借 `light_solar_array_lance`（威力 95＋光抗 −10）重創弱光的星線（光抗 −40）。`moon_longnight_wolf` 的 `trait_shadow_longnight_stalker`（上場閃避 +20%、移速 +15%）轉給 `ice_rime_strider` 亦具高泛用性。

## 6. 文化敏感度備註（Cultural Sensitivity）

- 現行考據庫中，本批 30 型態使用的 10 個原型皆標記為 `sensitivity: "ok"`，**`myth_kin` 邊未觸及現行標為 `review` 之原型**。
- 邊界提醒：`(water_catamaran_serpent, wood_canopy_regent)` 所涉 `arch_edai_siabo_lakatoi`（莫圖族陶船傳奇）於 P5-02 `README-b.md` 列為「原住民族口傳敘事敏感度待人工確認邊界」項目，且 `arch_sarimanok` 為馬拉瑙族重要文化象徵；改為 `tool_kin` 後 `note` 只陳述兩端的工藝外觀（彩繪側舷、雕花底座），不再主張兩族神話同源；兩族原型本身的敏感度仍依 P5-02 待人工確認。

## 7. 理由強度抽查（Claude Opus 代 SONNET，2026-11-08，全 23 條）

逐條比對兩端 `concept`（base／stage_look／archetype／role）。結構面無誤（mentor 兩端必為成年＋幼年、tool_kin 至少一端含器械或載具基底）。理由需人工決定或改寫者：

| # | 邊 | 問題 | 建議 |
|---|---|---|---|
| R1 | `(ice_tundra_dreadnought, star_skyhoof_steed)` myth_kin | §4 寫「塞迦—突厥神駒**直系**」：巴澤雷克（前 4 世紀塞迦）與圖爾帕爾（突厥／哈薩克）同屬草原馬崇拜是學界常見比較，「直系」無出處支撐；且巴澤雷克原型是有角格里芬，不是馬 | note／README 改為「同屬阿爾泰草原有翼神獸母題」，或補出處；否則降為 `rival_symbiosis`／刪除。**已處理 2026-11-08（使用者裁決改寫）**：保留 `myth_kin`，note 與 §4 改為「同屬歐亞草原有翼神獸母題」，不再稱直系、不再稱馬 |
| R2 | `(water_catamaran_serpent, wood_canopy_regent)` myth_kin | 莫圖族（巴布亞）與馬拉瑙族（民答那峨）只在語族層級同屬南島語系，沒有共同神話母體；`myth_kin` 的定義是共同神話源流 | 人工決定 myth_kin 是否接受「語族級」關聯；不接受則改 `tool_kin`（帆舟 × 彩舟意象不成立）或刪除，並確認兩族敏感度（§6）。**已處理 2026-11-08（使用者裁決改 `tool_kin`）**：理由改為彩繪側舷帆舟 × 雕花底座彩禽的手工雕飾器物；這是 8 條 tool_kin 中最弱的一條（彩禽端器物成分只有底座），兩端度數不變 |
| R3 | `(earth_pebble_pawn, fire_ember_cub)` rival_symbiosis | 「火山湖」是 P5-02 `arch_el_dorado_muisca` motif 的用語，但瓜塔維塔湖成因有爭議（常見說法為塌陷／溶蝕，並非公認火山口湖）；「地熱孕晶」因此缺乏依據 | 先改 P5-02 motif 為「高山聖湖綠寶石」，此邊改以「岩殼護燼」單一意象或刪除 。**已處理 2026-11-08（使用者裁決）**：note 改為單一「岩殼護燼」意象，取兩線基底（儀典岩鎧石偶 × 砂岩熔核甲胄），去掉「火山湖」「地熱孕晶」；P5-02 motif「火山湖綠寶石」未改 |
| R4 | `(metal_gear_strider, wind_flute_cruiser)` tool_kin | 敲擊音板 vs 氣鳴排笛，Argon 已自列為延伸意象（§4） | 同意 §4：待人工；兩端度數刪後仍 ≥ 2（4→3、3→2），可安全刪除。**已刪除 2026-11-08（使用者裁決）**：腳本重驗 30/30 連通、最小度數 2、10 線商圖仍無橋邊 |
| R5 | `(metal_clock_chick, sun_mirror_chick)` tool_kin | `sun_mirror_chick` 基底是「太陽鳥雛鳥 × 頭頂小鏡」，器械成分只有一面鏡子；理由（測時 × 追光儀器）成立但偏弱 | 保留；它是商圖生成樹邊 1，刪除前需另補 sun—metal 連通 |

其餘 18 條理由與雙方設定一致。R1 保留、R2 改 `tool_kin` 後，本批 `myth_kin` 剩 1 條。

- 2026-11-07 · Gemini Argon · partial · P5-06 草稿 01：為 30 型態提出 23 條跨線關係邊（總度數全數 2–4、10 線單一連通分量）並附平衡與敏感度分析 · 下一步：SONNET 跑 validate-data 並審查理由強度；人工裁決越階副手與副專精配對平衡
