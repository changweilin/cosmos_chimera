> Gemini 4 Argon（agy，唯寫報告）· P5-08 第二輪 · 對象：分支 data/p5-08-copy-p5-06-relations 工作樹

# P5-08 全資料集一致性掃描報告（第二輪）

## 0. 摘要統計與全量核驗總覽

- **掃描範圍**：`p2-08-starters-draft-01`（9 宙獸、9 招式、3 特性、6 邊、文案）、`p5-03-creatures-draft-01`（21 宙獸、14 邊、文案；依指示忽略 `p5-03-creatures-draft-02`）、`p5-05-moves-draft-01..06`（200 招式）、`p5-05-traits-draft-01..03`（90 特性）、`p5-06-relations-draft-01`（23 跨線邊）、`p5-02-archetypes-draft-01`（84 原型：a 39＋b 45）、`Data/schemas/*.json`、`Data/balance.json`、`Data/retired-ids.txt`（0 筆退役 ID）。
- **第一輪（`A1`–`A22`，共 22 項）複核統計**：
  - `已修正`：**9 項**（`A1, A6, A7, A13, A14, A19, A20, A21, A22`；4 項 `high` 全數已修正）
  - `仍存在`：**1 項**（`A18`）
  - `待人工裁決（未改）`：**12 項**（`A2, A3, A4, A5, A8, A9, A10, A11, A12, A15, A16, A17`；其中 `A12` 與 `A16` 含部分子項已修、主體待裁決）
- **第二輪新發現（`N1`–`N10`，共 10 項）統計**：
  - 嚴重度：**high 0 項 · medium 5 項（`N1`–`N5`）· low 5 項（`N6`–`N10`）**
  - 處置分類：**機械性修正 4 項**（`N4, N5, N6, N10`）· **人工內容／設計裁決 5 項**（`N1, N2, N3, N8, N9`）· **混合（機械＋人工）1 項**（`N7`）
- **第二輪重點零缺陷通過項（手算核實）**：
  1. **更名舊 ID 殘留檢查（0 斷鏈）**：全範圍搜尋舊招式 ID `wood_photosynthesis`、舊特性 ID `trait_thunder_arc_discharge`（含先前更名之 `light_blinding_glare`），所有 JSON 引用與 README 統計表均已更新為 `wood_sunlight_mend`、`trait_thunder_crisis_arc`、`light_prismatic_exposure`（舊名僅見於 `moves-05/README.md:170` 與 `traits-03/README.md:175` 修訂紀錄；木線三階引用之 `"trait_wood_photosynthesis"` 與雷招 `"thunder_arc_discharge"` 各自保留於 `traits-01`／`moves-01`，跨類詞幹零衝突）。
  2. **P5-06 圖結構與手算度數／連通性（100% 吻合）**：聯集 20 條既有 `evolution_family` ＋ 23 條新增跨線邊 ＝ **43 條邊**。43/43 滿足字典序 `a < b`、0 自環、0 重複邊、端點 100% 存在於 30 隻宙獸中。7/7 條 `mentor` 邊皆確為「他線 `adult` 指導他線 `juvenile`」；7/7 條 `tool_kin` 邊皆未發生「雙純生物基底誤配」（6 條兩端皆含器械／載具，1 條 `(metal_clock_chick, sun_mirror_chick)` 雛鳥端含頭頂圓鏡，已於 `p5-06/README.md:96` R5 揭露）。逐點手算 30 隻度數：度數 `2` 共 8 隻、度數 `3` 共 18 隻、度數 `4` 共 4 隻（總和 `86 = 2×43`，平均 `2.87`），與 `p5-06/README.md:7-63` 度數表、9 條生成樹邊及「10 線商圖無橋邊（刪任一跨線邊仍連通）」完全一致。
  3. **文化原型與母題子集檢查（10/10 存在、30/30 子集成立）**：10 條進化線之 `concept.archetype` 均存在於 `p5-02-archetypes-draft-01/archetypes-{a,b}.json`，`region` 完全一致，現行皆標 `sensitivity: "ok"`（其中 `arch_edai_siabo_lakatoi` 於 `p5-02/README-b.md:19` 列為口傳邊界待人工確認），且 30 隻 `concept.motifs_used` 逐字皆屬對應原型 `motifs` 子集（`motifs_used ⊆ archetype.motifs`）。
  4. **無法於檔案內確認事項**：受禁跑 shell 約束未執行 `tools/validate-data`；另 `tools/prompt-forge/` 已遷出至外部專案 `../cosmos_chimera_forge`（`tools/prompt-forge/README.md:3-6`），故無法於本工作樹核對 `catalog.js`／`gen_designed.py` 現值。

---

## 1. 第一輪問題（`A1`–`A22`）狀態複核表

| ID | 原嚴重度 | 狀態 | 查核位置（`file:field`）與現行值證據引文 |
|---|---|---|---|
| **A1** | high | `已修正` | `Data/_incoming/p5-05-moves-draft-05/moves/moves.json:59` `id`: `"wood_sunlight_mend"`；`Data/_incoming/p5-05-traits-draft-03/traits/traits.json:442` `id`: `"trait_thunder_crisis_arc"`。全範圍無舊 ID 斷鏈；草稿未入正式區故 `Data/retired-ids.txt:1` 維持空白註解。 |
| **A2** | medium | `待人工裁決（未改）` | `Data/_incoming/p5-05-moves-draft-01/moves/moves.json:95` `id`: `"water_aqua_cutter"`（同為 `p5-03/creatures/creatures.json:347` `water_wave_elver.signature_move`）；`p5-05-moves-draft-03/moves/moves.json:279` `id`: `"wind_air_slash"`。 |
| **A3** | low | `待人工裁決（未改）` | `p2-08-starters-draft-01/copy-terra.json:46,62` `name_en`: `"Nivervast"` / `"Voltraven"`；`p5-03-creatures-draft-01/copy-terra.json:70,134,142` `name_en`: `"Brinavast"` / `"Rimbraven"` / `"Rimbravast"`；`p5-03/creatures/creatures.json:57,824` `id`: `"metal_gear_strider"` / `"ice_rime_strider"`。 |
| **A4** | low | `待人工裁決（未改）` | `p2-08-starters-draft-01/copy-terra.json:53-54,61-62,69-70` `name_zh`/`name_en`: `"星蹄駒"`/`"Voltfoal"`、`"星蹄騏"`/`"Voltraven"`、`"星蹄天驥"`/`"Voltrazent"`。 |
| **A5** | low | `待人工裁決（未改）` | `p5-05-moves-draft-05/moves/moves.json:460` `id`: `"light_aureole_halo"`；跨批同詞幹如 `moves-02:163` `"fire_cinder_slide"`、`moves-05:204` `"fire_cinder_slash"`、`traits-03:223` `"trait_fire_cinder_screen"` 維持原值。 |
| **A6** | high | `已修正` | `p2-08-starters-draft-01/creatures/creatures.json:198,206,252,260,298,306` 月線三階 `stage_look`/`role` 已改為繞背貼身（如成年 `"role": "繞背突進：沿直線突進並降低命中敵人的影抗性，隨後貼身作戰。"`）；`p2-08/copy-terra.json:32-49,161-165` 圖鑑與特性文案已無隱蔽／後撤描述；`docs/design/creatures.md:79` 已同步為 `"刺客、貼身纏鬥"`（`p2-08/README.md:10` 殘留見 `N6`）。 |
| **A7** | medium | `已修正` | `p5-03-creatures-draft-01/copy-terra.json:96-97` `fire_pyre_monarch` `dex_zh`: `"牠與敵人保持距離，朝遠處目標引爆一團擴散的烈焰。"`、`dex_en`: `"It keeps its distance and detonates a spreading blaze around a distant target."`（已移除「守在前鋒」；`issues` 陣列舊紀錄見 `N7`）。 |
| **A8** | medium | `待人工裁決（未改）` | `p2-08-starters-draft-01/creatures/creatures.json:361,417,464` 星線三階 `concept.role` 仍同為 `"衝鋒、群體壓制：近中距突進與範圍放電"`；`docs/design/creatures.md:78` 日線特長仍為 `"遠距離持續直線傷害（光束、引導施法）"`。 |
| **A9** | medium | `待人工裁決（未改）` | `docs/design/creatures.md:63-66` 仍為 `M: 0.6–1.4 m`、`L: 1.4–2.5 m`、`XL: > 2.5 m`；`tools/prompt-forge/{catalog.js,gen_designed.py}` 已遷往 `../cosmos_chimera_forge`（`tools/prompt-forge/README.md:3-6`），無法於本 repo 核對另一端，且 `docs/roadmap/phase-5.md:41` 列為待人工裁決。 |
| **A10** | low | `待人工裁決（未改）` | `p5-03-creatures-draft-01/creatures/creatures.json:121,131-133` `metal_chronos_engine` `resist.water: 0`、`mastery: {"metal": 40, "earth": 15}`；`:432,438-440` `water_catamaran_serpent` `resist.wind: 0`、`mastery.wind: 15`；`:886,898-900` `ice_tundra_dreadnought` `resist.metal: 0`、`mastery.metal: 15`；`p2-08/creatures/creatures.json:121` `sun_array_skyheron` `resist.water: -15`。 |
| **A11** | low | `待人工裁決（未改）` | `p2-08/creatures/creatures.json:47-51`（`sun_mirror_chick` 含 `"汲雲氣球管道"`）與 `p5-03/creatures/creatures.json:47-51,508-512`（`metal_clock_chick` 含 `"報時機械巨象"`、`fire_ember_cub` 含 `"三首四臂雄獅像"` 等）全 10 線三階 `motifs_used` 仍整線照抄。 |
| **A12** | low | `待人工裁決（未改）` | **子項二已修**：`docs/design/creatures.md:9` 已補列 ``| `ai_style` | 戰鬥 AI 走位風格... |``；**子項一未改（待 P3 裁決）**：`Data/_incoming/p3-planet-types-draft-01/planet-types/planet-types.json:35` `twilight_planet.terrain_tags` 仍為 `["eternal_day", "eternal_night"]`。 |
| **A13** | high | `已修正` | `p2-08-starters-draft-01/moves/moves.json:2,4,5` 已改為 `kind: "both"` 並調威力（經 SOL 複核）：`light_mirror_glint` `"power": 38`＋`evasion_mod_percent: -10`（`3000ms`）；`light_solar_array_lance` `"power": 95`＋`resist_mod: light -10`（`6000ms`）；`shadow_nightpaw_swipe` `"power": 30`＋`resist_mod: shadow -5`（`4000ms`）。`p2-08/copy-terra.json:82-83,98-99,106-107` 招式文案已同步。 |
| **A14** | high | `已修正` | 4 個跨批同效特性已改綁屬性／門檻（經 SOL 複核）：`traits-03/traits/traits.json:460-475` `trait_thunder_supercharged_stride`（`HP>80%` → `cd_mod_percent: thunder -10`）；`traits-02/traits/traits.json:145-160` `trait_water_undertow_escape`（`HP<20%` → `evasion_mod_percent: 15`）；`traits-03:146-162` `trait_water_revitalizing_spring`（`on_move_used water 5000ms` → `heal_percent_max_hp: 4`）；`traits-03:521-537` `trait_light_guiding_beam`（`on_ally_swap 5000ms` → `mastery_mod: light 15`）。全池 93 特性執行期同效對數為 0。 |
| **A15** | medium | `待人工裁決（未改）` | 5 組嚴格支配＋1 組近似特性數值未改：`traits-01:586` `trait_shadow_dusk_flicker`（`HP>90%` 移速 `+15` vs `traits-03:206` `HP>80%` `+15`）、`traits-01:544` `trait_shadow_void_embrace`（`HP<25%` 閃避 `+10` vs `traits-01:404` `HP<40%` `+10`）、`traits-02:221` `trait_fire_conflagration_flare` 與 `traits-03:241` `trait_earth_shattered_carapace`（vs `traits-03:87` `trait_wood_thorn_lash`）、`traits-03:503` `trait_light_saving_grace`（`25%` 回 `12%` vs `traits-01:168` `30%` 回 `12%`）、`traits-01:211` `trait_fire_heat_exhaust`（vs `p2-08` `trait_shadow_longnight_stalker`）、`traits-03:283` `trait_earth_faultline_shift`（vs `traits-03:27` `trait_metal_ballistic_pivot`）。 |
| **A16** | low | `待人工裁決（未改）` | **文案／README 子項已修**：`p2-08/copy-terra.json:106-107` 改 `"向前直擊"`/`"Strike straight forward"`、`moves-05/README.md:71` 改 `"lowers light resist; no blind status"`、`moves-06/README.md:80` 改 `"lowering evasion (no root/immobilize status)"`。**命名／形狀子項待裁決未改**：`p2-08/moves/moves.json:5` & `copy-terra.json:104` 仍為 `"shadow_nightpaw_swipe"` / `"Nightpaw Swipe"` + `"shape": "front"`；`traits-02:499` 仍為 `"trait_light_blinding_arrival"`；`moves-02:336` `"thunder_chain_needle"` 與 `moves-03:71` `"wood_vine_grapple"` 未改。 |
| **A17** | low | `待人工裁決（未改）` | 9 個瞬間治療特性之無作用 `trigger.params.duration_ms` 仍維持 `1000`–`6000` 不等（如 `traits-01:73` `trait_wood_photosynthesis` 為 `6000`、`traits-03:151` `trait_water_revitalizing_spring` 為 `5000`、`traits-03:508` `trait_light_saving_grace` 為 `1000`）。 |
| **A18** | medium | `仍存在` | `Data/_incoming/p5-03-creatures-draft-01/` 與新增之 `p5-06-relations-draft-01/` 均依賴跨目錄資料（`p5-05`、`p2-08`），單批獨立驗證仍會報 `dangling_ref`；`p5-02-archetypes-draft-01/` 仍停留於 `_incoming/`，且 `tools/prompt-forge/catalog.js` 已遷出本 repo（見 `N8`）。 |
| **A19** | medium | `已修正` | `p5-05-moves-draft-01..04/README.md` 已補齊由 `moves.json` 產生的九形狀分佈表（核對 40/40/40/30 筆全數正確）並加頂部說明；`traits-01/README.md:24,52,83` 與 `traits-03/README.md:9,24,30,108,166` 已補入 `on_hp_cross_below` 統計並關閉 Q-C18 舊問題（A14 修改引發之效果表新漂移另列於 `N4`、`N5`）。 |
| **A20** | low | `已修正` | `Data/_incoming/p5-05-moves-draft-04/README.md:1-141` 已精簡為 **141 行**（符合 `< 200` 行規範）。 |
| **A21** | low | `已修正` | `p5-03-creatures-draft-01/copy-terra.json:8,16,112` 純漢字數已補至 **45、46、46 字**（全 30 隻宙獸繁中圖鑑皆落於 45–60 純漢字）；英文複合修飾語已補連字號（`:17` `"half-suit"`、`:65` `"Wood-grained"`、`:73` `"twin-hulled"`、`:137` `"gold-trimmed"`、`:153` `"spindle-shaped"`）；兩檔頂層鍵統一為 `creatures/moves/traits/issues/source` 並移除 `opus_note`（`issues` 內層型別差異見 `N7`）。 |
| **A22** | low | `已修正` | `Data/_incoming/p5-03-creatures-draft-01/README.md:32-52,58,71,75-76` 已於抗性欄完整列出非零次要抗性與風線次要弱點 `thunder -5/-10/-10`，更新 §4.1 P2-01 敘述，並移除原 §4.4 自相矛盾段落。 |

---

## 2. 第二輪新發現問題（`N1`–`N10`）

### N1 · medium · `P5-06` 關係邊引用了端點宙獸未採用的原型母題，且 `note` 混入地球族名／地名／原型真名與後設術語（與既有邊 `note` 體例亦不一致）
- **位置**：
  - `Data/_incoming/p5-06-relations-draft-01/relations/edges.json:72, 126, 132, 138`（邊 `water_wave_elver`–`wood_plume_peafowl`、`ice_tundra_dreadnought`–`star_skyhoof_steed`、`water_catamaran_serpent`–`wood_canopy_regent`、`water_skiff_eel`–`wind_reed_skiff` 之 `note` 欄位）
  - 對照 `Data/_incoming/p5-03-creatures-draft-01/creatures/creatures.json:255-259`（`wood_plume_peafowl.concept.motifs_used`）及 `p2-08/relations/edges.json:6`、`p5-03/relations/edges.json:6`
- **證據引文**：
  - `p5-06/relations/edges.json:138`：`"note": "薩里馬諾克具爪銜鮮魚母題，硬鱗爪彩禽與貼浪銀鰻具捕食意象"`。查 `p5-03/creatures/creatures.json:255-259`，`wood_plume_peafowl` 的 `"motifs_used"` 為 `["流線捲紋羽冠", "雙重虹彩長尾", "黃銅雕花底座"]`，恰**未採用** `arch_sarimanok` 的 `"爪銜鮮魚"` 母題，其 `stage_look` 與 `copy-terra.json:40-41` 亦完全無捕魚設定。
  - 同檔 `:72`（`"巴澤雷克冰塚有翼神獸與哈薩克圖爾帕爾..."`）、`:126`（`"莫圖族蟹爪帆舟傳奇與馬拉瑙彩鳳神話同源南島語族..."`）、`:132`（`"...與奇納布河梭形渡舟..."`，而 `wind_reed_skiff` 本體設定已去地球地名改為浮空小舟）、`:138`（`"薩里馬諾克...母題"`）均在 `note` 直接寫出地球族名／河名／原型真名與設計術語；反觀 `p2-08`／`p5-03` 既有 20 條邊的 `note` 則為 `"starter_sun_line juvenile → adolescent"` 英文開發字串。依 `docs/design/creatures.md:73, 85`，關係表直接用於生成遊戲內「宙獸關係表」與劇情台詞。
- **建議修法**：明確定義 `edges.json` 之 `note` 為「純開發考據備註」或「玩家端顯示文案」。若供玩家端顯示，統一將 43 條邊 `note` 改寫為世界觀內宙獸名稱與生態描述（移除地球專名、原型真名與 `"母題"`）；並將 `(water_wave_elver, wood_plume_peafowl)` 改寫為不依賴 `"爪銜鮮魚"` 之理由，或將該母題補入木線設定。
- **處置性質**：人工內容／設計裁決。

### N2 · medium · `P5-06` 共 4 條跨線邊之理由（`note`／`kind`）與端點宙獸實際 `ai_style`、`role`、`stage_look` 或文化原型物種不符
- **位置**：
  - `Data/_incoming/p5-06-relations-draft-01/relations/edges.json:15-19`（`earth_pebble_pawn`–`metal_chronos_engine`，`note`）
  - `Data/_incoming/p5-06-relations-draft-01/relations/edges.json:69-73` 與 `README.md:67`（`ice_tundra_dreadnought`–`star_skyhoof_steed`，`kind`／`note`）
  - `Data/_incoming/p5-06-relations-draft-01/relations/edges.json:123-127`（`water_catamaran_serpent`–`wood_canopy_regent`，`kind`／`note`）
  - `Data/_incoming/p5-06-relations-draft-01/relations/edges.json:9-13`（`earth_pebble_pawn`–`fire_ember_cub`，`note`）
- **證據引文**：
  - `:18` 寫 `"雙足如樁守陣的漏輪巨象指導慢步跟隨前排的金塵偶鎮守陣線"`，但 `metal_chronos_engine` 為 `"ai_style": "charge"`、`"role": "前排坦克：遠距直線突進破陣..."`（`p5-03/creatures.json:115, 152`），且學徒 `earth_pebble_pawn` 為 `"role": "中排：中距直線投石..."`（`:666`，12 m 遠射），並非鎮守前排。
  - `:72` 寫 `"巴澤雷克冰塚有翼神獸與哈薩克圖爾帕爾同源阿爾泰草原神駒傳統"`、`README.md:67` 稱 `"塞迦—突厥神駒直系"`，但 `arch_pazyryk_antlered_griffin`（`archetypes-b.json:364-367`）為 `"巴澤雷克鹿角獅鷲"`（`"鷹喙有翼猛獸"`）而非馬／神駒，考據庫亦無「直系」文獻依據（`p5-06/README.md:91` R1 已標註，惟 `edges.json` 與 §4 正文未改）。
  - `:125-126` 標 `"kind": "myth_kin"`（`"同源南島語族海洋文化圈"`），兩端僅具語族關聯而無共同神話母體，且 `wood_canopy_regent`（`:297` `"盤踞巨木頂端的七彩神禽"`）本身無海洋設定（`README.md:92` R2）。
  - `:12` 寫 `"火山湖晶核石偶與砂岩熔核幼獅具地熱孕晶與岩殼護燼共生意象"`，但幼年 `fire_ember_cub` 的 `stage_look`（`:505` `"滾燙赤褐小獅，頸圈生有一圈暗紅餘燼絨毛"`）尚無岩殼，且瓜塔維塔湖非公認火山口湖、地質意象亦非生態天敵共生（`README.md:93` R3）。
- **建議修法**：依 `p5-06/README.md` §7 R1–R3 逐條裁決改寫 `note`／改標 `kind` 或刪除（注意 `ice_tundra_dreadnought`–`star_skyhoof_steed` 與 `water_catamaran_serpent`–`wood_canopy_regent` 列於生成樹邊 6 與 9，但因商圖 2-邊連通，單刪其一仍連通；若同時刪除多條需重驗連通性與最小度數 ≥ 2）。
- **處置性質**：人工內容／設計裁決。

### N3 · medium · `P5-06` 跨階裝備符籙副手問題涵蓋 4 條非 `mentor` 跨線邊與全部 20 條 `evolution_family` 邊，不僅限於 `README.md` 所列之 7 條 `mentor` 邊
- **位置**：`Data/_incoming/p5-06-relations-draft-01/README.md:78` vs `relations/edges.json:57-61, 111-115, 129-139`、`p2-08/relations/edges.json:1-38`、`p5-03/relations/edges.json:1-86`
- **證據引文**：`p5-06/README.md:78` 僅就 7 條 `mentor` 邊（`juvenile ↔ adult`）警示越階借招；手算全 43 條邊後，`p5-06/relations/edges.json` 另有 **4 條非 `mentor` 跨階邊**——`ice_rime_strider`（少年）↔ `moon_longnight_wolf`（成年，威力 95）、`sun_heliostat_heron`（少年，威力 70）↔ `water_wave_elver`（幼年）、`water_skiff_eel`（少年，威力 75）↔ `wind_reed_skiff`（幼年）、`water_wave_elver`（幼年）↔ `wood_plume_peafowl`（少年，威力 85）——且既有 **20 條 `evolution_family` 邊**全數連接 `juvenile ↔ adolescent` 或 `adolescent ↔ adult`。
- **建議修法**：人工裁決越階副手規則時，須明確區分三種方案：(a) 完全允許跨階裝備符籙；(b) 僅禁止跨兩階（`juvenile` 主力借 `adult` 符籙，僅影響 7 條 `mentor` 邊）；(c) 要求副手階段 `≤` 主力階段（將同時影響 20 條 `evolution_family` 與 11 條跨線邊在低階主力側的符籙可用性）。
- **處置性質**：人工內容／設計裁決。

### N4 · medium · `p5-05-traits-draft-02/README.md` 在 A14 修改後，參數範圍、效果次數統計與缺口覆蓋表與 `traits.json` 矛盾，且未重算聲明位置顛倒
- **位置**：`Data/_incoming/p5-05-traits-draft-02/README.md:13, 39-40, 58, 113-114, 138, 146, 172` vs `traits/traits.json:145-160`
- **證據引文**：
  - `traits.json:145-160` 已將 `trait_water_undertow_escape` 改為 `on_hp_below 20%` → `"evasion_mod_percent": {"delta": 15}`。
  - `README.md:39` 仍寫 `evasion_mod_percent` Parameter Range 為 `"-10 to +10%"`（實際含 `+15%`）；`:39-40` 與 `:113-114` 仍計 `evasion_mod_percent: 6`、`move_speed_mod_percent: 6`（手算 `traits.json` 實為 **`7` 與 `5`**）。
  - `:13, 138, 146` 仍稱新增 `"19 previously unrepresented (trigger, effect) combinations"` 並把 `trait_water_undertow_escape` 列在 `on_hp_below + move_speed_mod_percent`（該組合在本批實為 **0**，實際新增為 **18** 組）。
  - `:172` 位於檔案倒數第 2 行卻寫 `"下方分佈統計表是修訂前的數字，未重算"`（所有表格均在上方）。
- **建議修法**：以腳本重算 `traits-draft-02/README.md` 效果分佈表與缺口表（19→18），將 `:39` 範圍改為 `-10 to +15%`，並修正文末說明。
- **處置性質**：機械性修正。

### N5 · medium · `p5-05-traits-draft-03/README.md`（及 `traits-draft-01/README.md:84`）在 A1／A14／A19 更新後，效果統計表、缺口表、單列參數及最小觸發次數敘述與 `traits.json` 多處不符
- **位置**：
  - `Data/_incoming/p5-05-traits-draft-03/README.md:13, 37, 39, 41, 77, 112, 114, 116, 131, 145, 148, 152, 160, 171` vs `traits/traits.json`
  - `Data/_incoming/p5-05-traits-draft-01/README.md:84`
- **證據引文**：
  - `traits-03/README.md:37, 39, 41` 與 `:112, 114, 116` 寫 `mastery_mod: 5`、`cd_mod_percent: 3`、`move_speed_mod_percent: 9`；手算 `traits.json` 實為 **`mastery_mod: 6`、`cd_mod_percent: 4`、`move_speed_mod_percent: 7`**（觸發統計已重算，效果統計卻未重算）。
  - 缺口表 4 列失準：`:145`（`B01+B02 Count` 隨 B02 修改應為 `0`、合計 `2`，表寫 `1`／`3`）、`:148`（仍將已改為 `cd_mod_percent` 的 `trait_thunder_supercharged_stride` 計入移速 `+3`）、`:152`（仍將已改為 `on_move_used` 的 `trait_water_revitalizing_spring` 列於 `on_summon`）、`:160`（仍將已改為 `mastery_mod` 的 `trait_light_guiding_beam` 列於 `move_speed_mod_percent`）。
  - `:77` `trait_light_saving_grace` 參數欄寫 `"25%"`（漏寫 `traits.json:508` 的 `"duration_ms": 1000`）；`:13` 寫 `"Every trigger type appears ≥ 3 times"`（與 `:26` `on_summon: 2`、`:30` `on_hp_cross_below: 1` 矛盾；`traits-01/README.md:84` 同樣殘留 `"each trigger type appears >= 3 times"` 與 `:83` `on_hp_cross_below: 1` 矛盾）；`:131` 仍寫條件型閃避上限 `+10%`；`:171` 同樣於檔尾誤寫 `"下方分佈統計表"`。
- **建議修法**：以腳本完整重產 `traits-01`、`traits-03` 的統計表與缺口表，補上 `:77` 之 `1000ms` 並刪除兩檔 `"appears >= 3 times"` 舊句。
- **處置性質**：機械性修正。

### N6 · low · `p2-08-starters-draft-01` 中 `shadow_lone_hunter_ambush` 英文說明誤用 `"sweeping"`，且 `README.md`／`copy-terra.json#issues` 殘留 A6／A13 舊描述
- **位置**：`Data/_incoming/p2-08-starters-draft-01/copy-terra.json:123, 177` 與 `README.md:10, 38` vs `moves/moves.json:2, 4, 7`
- **證據引文**：
  - `moves.json:7` 定義 `shadow_lone_hunter_ambush` 為 `"shape": "line", "base_range_mm": 8000, "movement": "dash"`，但 `copy-terra.json:123` `desc_en` 寫 `"Rush forward in a sweeping shadow, damaging enemies along the path and temporarily lowering their shadow resistance."`（誤用對應扇形 `sweep` 的 `"sweeping"`，且未如其餘直線招寫明 `"straight"`）。
  - `copy-terra.json:177` `issues[0]` 仍寫 `"但三個光招式資料均為一般傷害招式"`、`README.md:38` 仍寫 `"三招都先做成一般 line 光束"`，未反映 A13 後 `light_mirror_glint` 與 `light_solar_array_lance` 已改為帶減益的 `kind: "both"`。
  - `README.md:10` 月線定位欄仍寫 `"刺客、近距突進"`（`creatures.md:79` 已改為 `"刺客、貼身纏鬥"`，且幼年招為無位移 `front`）。
- **建議修法**：將 `shadow_lone_hunter_ambush.desc_en` 改為 `"Rush straight forward as a darting shadow, damaging enemies along the path..."`；更新 `p2-08/README.md:10, 38` 與 `copy-terra.json:177` 用詞。
- **處置性質**：機械性修正。

### N7 · low · `p5-03-creatures-draft-01/copy-terra.json` 之 `issues` 內層型別與 `p2-08` 不一致、保留 15 筆已過期之舊 `role` 問題，且 `moves`／`traits` 本地化陣列為空
- **位置**：`Data/_incoming/p5-03-creatures-draft-01/copy-terra.json:172-235` vs `p2-08-starters-draft-01/copy-terra.json:76-180`、`p5-03/creatures/creatures.json`
- **證據引文**：
  - `p2-08/copy-terra.json:176-180` 的 `"issues"` 為 `string[]`（已解決項標註 `"（已解決...）"`），而 `p5-03/copy-terra.json:174-235` 的 `"issues"` 為 `{id, issue}[]` 物件陣列，且 15 筆全數針對 Opus 改寫 `creatures.json` 前的舊 `concept.role`（如 `:204-205` `"fire_pyre_monarch"` 仍寫 `"定位強調前鋒近戰突擊"`，與現行 `creatures.json:612` `"重砲手..."` 及同檔 `:96` 新圖鑑直接矛盾）。
  - 同檔 `:172-173` `"moves": [], "traits": []` 為空，使 `p5-03` 21 隻宙獸使用的 21 招與 7 特性（及 `p5-05` 全池）尚無玩家端中英譯名與說明文案。
- **建議修法**：統一兩份 `copy-terra.json` 的 `issues` 結構並標註已隨 `role` 改寫處理之項目；待本地化 schema 定案後補齊 `p5-05` 招式／特性中英文案。
- **處置性質**：混合（`issues` 結構與標註為機械性修正；招式／特性文案欄位與撰寫為人工內容／設計裁決）。

### N8 · low · `creature-concept-pipeline.md` 與 `p5-02/README-{a,b}.md` 指向已遷出本 repo 之 `tools/prompt-forge/catalog.js` 與 `forge.js`
- **位置**：`docs/design/creature-concept-pipeline.md:9, 19`、`Data/_incoming/p5-02-archetypes-draft-01/README-a.md:16, 18`、`README-b.md:15, 20` vs `tools/prompt-forge/README.md:3-8`
- **證據引文**：`tools/prompt-forge/README.md:3-4` 載明 `"本工具已獨立遷移至專屬專案：cosmos_chimera_forge（路徑：../cosmos_chimera_forge）"`，本 repo 內已無 `tools/prompt-forge/catalog.js` 與 `forge.js`；但 `creature-concept-pipeline.md:9` 仍寫 `"選項池（屬性數量權重、外觀子類、原型清單與敏感度）由 tools/prompt-forge/catalog.js 維護"`，且 `Data/schemas/` 無文化原型 schema。
- **建議修法**：決定文化原型庫的唯一真實來源（於本 repo 建立 `Data/archetypes/`＋schema，或改指外部 `cosmos_chimera_forge`），並更新 `creature-concept-pipeline.md` 連結。
- **處置性質**：人工內容／設計裁決。

### N9 · low · `creature.schema.json` 的 `size_class` enum 含 `"XS"`，但 `docs/design/creatures.md` 僅定義 `S / M / L / XL` 四級距
- **位置**：`Data/schemas/creature.schema.json:17`（`size_class`）vs `docs/design/creatures.md:8, 60-70`
- **證據引文**：`creature.schema.json:17` 為 `"size_class": { "enum": ["XS", "S", "M", "L", "XL"] }`；而 `creatures.md:8` 寫 ``體型級距 `S` / `M` / `L` / `XL` ``、`:60-66` 僅定義 `S (< 0.6 m)` 至 `XL (> 2.5 m)` 四級、`:70` 明載 `"JSON 使用大寫 S／M／L／XL"`。
- **建議修法**：確認遊戲是否支援 `XS` 體型；若不支援則自 `creature.schema.json` 移除 `"XS"`，若支援則於 `creatures.md` 補齊 `XS` 尺寸上限與撫摸配對規則。
- **處置性質**：人工內容／設計裁決。

### N10 · low · `p5-05` 早期批次 `README.md`（moves 01–04、traits 01–02）內文與 Open Issues 仍保留「P2 尚未封閉 schema／無 `base_range_mm`／無 `origin`」過期描述
- **位置**：`Data/_incoming/p5-05-moves-draft-01/README.md:8, 48-50`、`moves-draft-02/README.md:8, 97`、`moves-draft-03/README.md:8, 123`、`moves-draft-04/README.md:8`、`traits-draft-01/README.md:7, 87-89`、`traits-draft-02/README.md:168`
- **證據引文**：例：`moves-draft-01/README.md:48` 寫 `"The current move.schema.json only tracks abstract shape and range_class"`、`moves-draft-03/README.md:123` 寫 `"Currently, shape: 'aoe' encompasses both caster-centered novas ... and targeted remote ground reticles"`、`traits-draft-01/README.md:87` 寫 `"The trigger and effect type fields currently use the open string ID rule"`——均與現行已封閉之 `move.schema.json`、`common.schema.json` 不符（對照 `moves-draft-06/README.md:178-180` 已用刪除線標記結案）。
- **建議修法**：比照 `moves-draft-06/README.md` 與 `traits-draft-03/README.md:166`，將已被 P2-01／P2-04／P2-09 解決之舊 Open Issues 加刪除線結案。
- **處置性質**：機械性修正。
