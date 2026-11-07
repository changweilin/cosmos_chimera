> 原始輸出：`agy -p --model gemini-4-argon-high --mode accept-edits`（shell 拒絕，唯寫本檔），對象 `fdb7152`＋工作樹。Claude Opus 抽驗 A1、A13、A14 以腳本重現屬實；A12 後半（`creatures.md` 漏列 `ai_style`）已於 `919d532` 之後修正。其餘為內容決策，未套用。

# P5-08 全資料集一致性掃描報告（第一輪）

- **掃描者**：Gemini Argon（`ARGON`）
- **日期**：2026-11-07
- **掃描範圍**：`Data/_incoming/` 下全部批次（`p2-08-starters-draft-01`、`p5-02-archetypes-draft-01`、`p5-03-creatures-draft-01`、`p5-05-moves-draft-01`～`06`、`p5-05-traits-draft-01`～`03`、`p3-planet-types-draft-01`，並順帶核對測試夾 `p0-05-samples`）及 `Data/retired-ids.txt`；對照 `docs/design/{creatures,combat,planet-world,vision,creature-concept-pipeline}.md`、`Data/schemas/*.schema.json`、`Data/balance.json` 與 `tools/prompt-forge/{catalog.js,gen_designed.py}`。
- **執行限制**：依任務規範僅讀寫本報告檔，未執行任何 shell 指令，亦未修改其他任何檔案。
- **總覽結論**：共檢出 **22 項有明確證據之問題（`A1`–`A22`：high 4、medium 7、low 11）**。全資料集在同種類 ID 唯一性、退役 ID 避讓（0 衝突）、進化鏈與無向關係邊字典序（20/20 條滿足 `a < b`）、文化原型不重複指派（10 線對應 10 個不同原型且皆不含原型真名）均通過；主要高風險問題集中於 **跨種類（招式 vs 特性）詞幹撞名**、**御三家月線定位／圖鑑承諾未實作之隱蔽與後撤機制**、**御三家招式與 `p5-05` 基礎池完全同數值**，以及 **無屬性參數之特性跨批次 100% 完全重複**。

---

## 1. 命名（`A1`–`A5`）

### A1 · high · 跨種類（招式 vs 特性）ID 英文詞幹完全撞名（2 組）
- **位置與 ID**：
  1. `Data/_incoming/p5-05-moves-draft-01/moves/moves.json:337-346`（`thunder_arc_discharge`）vs `Data/_incoming/p5-05-traits-draft-03/traits/traits.json:441-457`（`trait_thunder_arc_discharge`）
  2. `Data/_incoming/p5-05-moves-draft-05/moves/moves.json:59-74`（`wood_photosynthesis`）vs `Data/_incoming/p5-05-traits-draft-01/traits/traits.json:69-84`（`trait_wood_photosynthesis`，已由 `p5-03` 木線三階引用）
- **證據引文**：
  - 招式 `"id": "thunder_arc_discharge"`（中距扇形雷傷 60）與特性 `"id": "trait_thunder_arc_discharge"`（HP < 30% 時雷專精 +25）去掉 `trait_` 前綴後完全同為 `thunder_arc_discharge`（此組在先前單批審查未被檢出）。
  - 招式 `"id": "wood_photosynthesis"`（自身回復 25% HP）與特性 `"id": "trait_wood_photosynthesis"`（上場回復 8% HP）共享完全相同詞幹且同為治療效果。
- **建議**：保留已被宙獸引用的 `trait_wood_photosynthesis` 及基礎池 `thunder_arc_discharge`；將 `trait_thunder_arc_discharge` 改名為 `trait_thunder_crisis_arc`（或 `trait_thunder_low_volt_surge`），將招式 `wood_photosynthesis` 改名為 `wood_sunlight_mend`（或 `wood_chlorophyll_surge`）。若舊 ID 已入正式版控，須同步登記 `Data/retired-ids.txt`。

### A2 · medium · 招式 ID 英文詞幹與《寶可夢》官方招式英文名稱逐字相同（2 招）
- **位置與 ID**：
  1. `Data/_incoming/p5-05-moves-draft-01/moves/moves.json:95-104`（`water_aqua_cutter`，同時為 `p5-03` `water_wave_elver` 之 `signature_move`）
  2. `Data/_incoming/p5-05-moves-draft-03/moves/moves.json:279-288`（`wind_air_slash`）
- **證據引文**：
  - `"id": "water_aqua_cutter"` 去屬性前綴後為 `Aqua Cutter`（《寶可夢 朱／紫》第九世代官方招式英文名「水流斬」）。
  - `"id": "wind_air_slash"` 去屬性前綴後為 `Air Slash`（《寶可夢》第四世代起官方招式英文名「空氣斬」）。
- **建議**：將 `water_aqua_cutter` 改為 `water_crest_slicer`（同步更新 `p5-03` `water_wave_elver.signature_move` 與 `README.md`），將 `wind_air_slash` 改為 `wind_zephyr_cutter`；或於後續補齊玩家顯示名 `name_en`/`name_zh` 時強制避開 `Aqua Cutter / 水流斬` 與 `Air Slash / 空氣斬`。

### A3 · low · 跨批次宙獸英文名稱字尾高度重複（`-vast` 三線共用、`-raven` 兩線共用且帶渡鴉歧義）
- **位置與 ID**：
  - `Data/_incoming/p2-08-starters-draft-01/copy-terra.json:44-47, 61-64`（`moon_longnight_wolf` = `Nivervast`、`star_storm_colt` = `Voltraven`）
  - `Data/_incoming/p5-03-creatures-draft-01/copy-terra.json:68-71, 132-143`（`water_catamaran_serpent` = `Brinavast`、`ice_rime_strider` = `Rimbraven`、`ice_tundra_dreadnought` = `Rimbravast`）
- **證據引文**：
  - 10 條進化線的成年體中有 3 隻以 `-vast` 結尾（`Nivervast`、`Brinavast`、`Rimbravast`，其中後兩者同在 `p5-03` 且同為 `-avast`）。
  - 少年體 `Voltraven`（金翼天馬原型 `arch_tulpar`）與 `Rimbraven`（鹿角獅鷲）同以 `-raven` 結尾，且 `Voltraven` 內嵌英文 `"raven"`（渡鴉），與馬形外觀不符。另 `p5-03` 兩條不同線少年 ID 同用 `_strider`（`metal_gear_strider`、`ice_rime_strider`）。
- **建議**：保留御三家 `Nivervast`；將 `p5-03` 的 `Brinavast` 改為 `Brinamar`、`Rimbravast` 改為 `Rimbragard`；評估將馬形少年的 `Voltraven` 改為 `Voltrmane` 或 `Voltraco`。

### A4 · low · 御三家星線 ID／繁中詞根（`star_`／`星蹄`）與英文詞根（`Volt-`）意象脫鉤
- **位置與 ID**：`Data/_incoming/p2-08-starters-draft-01/copy-terra.json:52-75`（`star_blaze_foal`、`star_storm_colt`、`star_skyhoof_steed`）
- **證據引文**：
  - 日線（`sun_*` / `鏡*` / `Mirr-`）與月線（`moon_*` / `夜痕*` / `Niv-`）中英詞根一致對應核心意象；唯星線中文名為 `"星蹄駒" → "星蹄騏" → "星蹄天驥"`、圖鑑強調 `"額上的白色星斑"` 與 `"星點電光"`，英文名卻改為純電學詞根 `"Voltfoal" → "Voltraven" → "Voltrazent"`，完全不含星或天馬意象。
- **建議**：定稿時評估改用帶星象詞根之命名（如 `Astracolt` / `Stellhoof` 系），或由人工確認接受英文名僅取雷電意象。

### A5 · low · 招式 ID `light_aureole_halo` 同義詞疊用，及多組跨批次招式／特性共用相近詞幹
- **位置與 ID**：
  - `Data/_incoming/p5-05-moves-draft-05/moves/moves.json:460-477`（`light_aureole_halo`）
  - 跨批同詞幹群：`fire_cinder_slide`（`moves-02:163`）／`fire_cinder_slash`（`moves-05:204`）／`trait_fire_cinder_screen`（`traits-03:222`）；`wind_tailwind_dive`（`moves-03:313`）／`wind_tailwind_boon`（`moves-05:333`）／`trait_wind_tailwind_glide`（`traits-01:363`）；`light_halo_burst`（`moves-03:394`）／`trait_light_halo_rebound`（`traits-02:517`）。
- **證據引文**：
  - `"id": "light_aureole_halo"` 中 `aureole` 與 `halo` 皆指光暈／聖環，語意重複。
- **建議**：將 `light_aureole_halo` 改為 `light_solar_aureole` 或 `light_crown_radiance`；其餘同詞幹群在撰寫正式中英顯示名時應刻意區分用詞。

---

## 2. 設定矛盾（`A6`–`A12`）

### A6 · high · 御三家月線三階之 `concept.role`、`stage_look` 與圖鑑文案描述「隱蔽／退回暗處／爆發後撤」，與實際招式、特性及 `combat.md` 矛盾
- **位置與 ID**：
  - `Data/_incoming/p2-08-starters-draft-01/creatures/creatures.json:157-309`（`moon_night_pup`、`moon_shade_wolf`、`moon_longnight_wolf`）
  - `Data/_incoming/p2-08-starters-draft-01/copy-terra.json:28-50`
- **證據引文**：
  - 三階 `concept.role` 均寫 `"刺客：近距隱蔽突進、爆發後撤"`（`creatures.json:206, 260, 306`）；成年 `stage_look` 寫 `"趁對手視線被遮時貼地疾衝穿過、衝出後立刻拉開距離"`（`:298`）；`copy-terra.json` 寫幼年 `"用爪子迅速一拍，再無聲地退回暗處"`（`:32`）、成年 `"突襲後迅速撤離"`（`:48`）。
  - 實際資料：幼年精通招 `shadow_nightpaw_swipe` 為 `shape: "front"` 無位移（並非突進）；少年／成年僅為直線向前 `dash` 6 m／8 m；特性 `trait_shadow_longnight_stalker` 僅在上場 5 秒內提升閃避 +20% 與移速 +15%。
  - 對照 `docs/design/combat.md:47-60`：`ai_style: "flank"` 僅定義繞至目標背後 `最短可及距離 × ai.close_in_permille` 處持續作戰，**戰鬥系統並無潛行／隱形（隱蔽）機制，亦無攻擊後自動後撤／脫離機制**（對比 `p5-03` 已將 21 隻 `role` 改寫為對齊實際招式，`p2-08` 月線尚未改寫）。
- **建議**：將月線各階 `concept.role` 改寫為實際機制（幼年「繞背近戰：高敏捷前方爪擊，上場加速閃避」；少年／成年「繞背突襲：直線突進／降影抗，上場加速閃避」），並修正 `stage_look` 與 `dex_zh`/`dex_en` 中「退回暗處／拉開距離／迅速撤離」之描述（同步檢視 `docs/design/creatures.md:78`「潛行、背刺、閃現／爆發後撤」文案）。

### A7 · medium · `fire_pyre_monarch`（燼鬃焰獅）圖鑑文案殘留「守在前鋒」，與 `concept.role`（重砲手）及 `ai_style: "keep_distance"` 直接矛盾
- **位置與 ID**：
  - `Data/_incoming/p5-03-creatures-draft-01/creatures/creatures.json:572-614`（`fire_pyre_monarch`）
  - `Data/_incoming/p5-03-creatures-draft-01/copy-terra.json:92-98, 202-204`
- **證據引文**：
  - `creatures.json` 已將該獸改為 `"ai_style": "keep_distance"`（`:576`）與 `"role": "重砲手：高力量，遠距指定點範圍爆發，受火屬性攻擊後縮短火招 CD"`（`:612`）。
  - 但 `copy-terra.json:96-97` 未隨 `role` 改寫同步調整，仍寫：`"牠守在前鋒，朝遠處敵群引爆一團擴散的烈焰。"` / `"It guards the vanguard while detonating a spreading blaze around a distant target."`。
- **建議**：將 `copy-terra.json` 中 `fire_pyre_monarch` 的「守在前鋒／guards the vanguard」改為符合 `keep_distance` 重砲手定位之後排壓制描述。

### A8 · medium · 御三家星線與日線之階段定位與單階招式／設計文件存在落差
- **位置與 ID**：
  - `Data/_incoming/p2-08-starters-draft-01/creatures/creatures.json:311-467`（`star_blaze_foal`、`star_storm_colt`、`star_skyhoof_steed`）
  - `docs/design/creatures.md:77` vs `Data/_incoming/p2-08-starters-draft-01/moves/moves.json:2-4`（`starter_sun_line` 三招）
- **證據引文**：
  - 星線三階 `concept.role` 全數複製貼上 `"衝鋒、群體壓制：近中距突進與範圍放電"`（`creatures.json:361, 417, 464`），但幼年 `thunder_starblaze_charge` 與成年 `thunder_skyhoof_stampede` 均為單純直線 `dash`，**不具範圍放電**（僅少年 `thunder_galloping_discharge` 為 `caster` `burst`）。
  - `docs/design/creatures.md:77` 定義日線特長為 `"遠距離持續直線傷害（光束、引導施法）"`，而 `p2-08/moves/moves.json:2-4` 三招均為單次瞬發 `damage` `line`，`combat.md` 無引導施法機制（`p2-08/README.md:38` 已註記）。
- **建議**：星線比照 `p5-03` 按各階段實際精通招式分開描述 `concept.role`；日線若確定不實作引導施法，應修訂 `docs/design/creatures.md:77` 以消除設計文件與實作資料之長期不一致。

### A9 · medium · `creatures.md` 體型級距界線與 `prompt-forge` 不一致，導致御三家月／星成年（`size_class: "L"`）尺寸越界為 `XL`
- **位置與 ID**：
  - `docs/design/creatures.md:60-65`
  - `tools/prompt-forge/catalog.js:64-69`
  - `tools/prompt-forge/gen_designed.py:34, 42`（`moon_longnight_wolf`、`star_skyhoof_steed`）
- **證據引文**：
  - `creatures.md:63-65` 明訂：`M` = `0.6–1.4 m`、`L` = `1.4–2.5 m`、`XL` = `> 2.5 m`。
  - `catalog.js` 卻寫 `M` = `0.6–1.5 m`、`L` = `1.5–2.8 m`、`XL` = `2.8–12.5 m`；且 `gen_designed.py` 將 `size_class: "L"` 的 `moon_longnight_wolf` 設為 `"height_length": "2.6 m"`、`star_skyhoof_steed` 設為 `"height_length": "2.7 m"`，依 `creatures.md` 已超過 `L` 上限 `2.5 m`。
- **建議**：對齊 `creatures.md` 與 `catalog.js` 的級距公尺數；若維持 `L ≤ 2.5 m`，將 `gen_designed.py` 中月／星成年尺寸調為 `≤ 2.5 m`（或將該兩隻 `size_class` 升為 `XL`）。

### A10 · low · 部分宙獸外觀意象與副專精／抗性屬性配置不一致
- **位置與 ID**：
  - `Data/_incoming/p5-03-creatures-draft-01/creatures/creatures.json:118-154, 425-440, 885-900`（`metal_chronos_engine`、`water_catamaran_serpent`、`ice_tundra_dreadnought`）
  - `Data/_incoming/p2-08-starters-draft-01/creatures/creatures.json:118-154`（`sun_array_skyheron`）
- **證據引文**：
  - `metal_chronos_engine` 外觀與圖鑑核心為水力計時（`"水力平衡連桿"`、`"沉水浮碗水力計時輪系"`、`"腹中水輪隨浮碗沉降"`），全無土石意象，成年副專精卻配 `"earth": 15` 而非 `water`，且 `"water": 0`。
  - `sun_array_skyheron` 成年外觀與圖鑑強調 `"背負環形鏡陣與汲雲氣球管"`、`"細長管道汲取雲間水氣"`，但水抗卻隨進化由 `-5 → -10 → -15` 惡化為弱點。
  - `water_catamaran_serpent`（副專精 `wind: 15`，但 `resist.wind: 0`）與 `ice_tundra_dreadnought`（外觀強調鍛鐵關節／金屬尖爪、副專精 `metal: 15`，但三階 `resist.metal: 0`），與御三家「副專精必伴隨正抗性」慣例不同。
- **建議**：評估將 `metal_chronos_engine` 副專精改為 `water: 15`（或補入岩基配重設定），並確認副專精與抗性是否需維持一致性規則。

### A11 · low · `concept.motifs_used` 於三階段整線照抄，導致幼年／少年階段標註了未出現的成年母題
- **位置與 ID**：`p2-08-starters-draft-01/creatures/creatures.json`（全 3 線）與 `p5-03-creatures-draft-01/creatures/creatures.json`（全 7 線）
- **證據引文**：
  - 例：`metal_clock_chick`（圓弧幼雛，`p5-03:47-51`）之 `motifs_used` 列有 `"報時機械巨象"`、`"盤龍平衡桿"`；`fire_ember_cub`（赤褐小獅，`p5-03:508-512`）列有 `"三首四臂雄獅像"`、`"盤蛇綻放蓮花"`、`"陡頂砂岩金字塔"`（甚至成年 `fire_pyre_monarch` 外觀亦僅「三面」而無「四臂」與「盤蛇」）；`sun_mirror_chick`（`p2-08:47-51`）幼鳥列有 `"汲雲氣球管道"`。
- **建議**：改為依各階段實際呈現之外觀元素填寫 `motifs_used`，或明確定義該欄代表整條進化線共用之母題池，避免美術生成單階立繪時誤畫入高階特徵。

### A12 · low · `twilight_planet` 地貌標籤混入日照半球機制，且 `creatures.md` 資料模型表漏列必填欄位 `ai_style`
- **位置與 ID**：
  - `Data/_incoming/p3-planet-types-draft-01/planet-types/planet-types.json:32-37`（`twilight_planet`）
  - `docs/design/creatures.md:3-17` vs `Data/schemas/creature.schema.json:12, 18-21`
- **證據引文**：
  - `twilight_planet` 的 `"terrain_tags": ["eternal_day", "eternal_night"]` 代表潮汐鎖定日照分區，而 `docs/design/planet-world.md` §生成管線定義地貌模板為真實地理高度遮罩（如 `volcano`、`fjord`、`canyon`）。
  - `creature.schema.json:12` 已將 `"ai_style"` 列為 `required` 必填欄位，但 `docs/design/creatures.md:4-16` 的資料模型欄位表尚未補入 `ai_style`。
- **建議**：於 P3-04 前決定 `eternal_day`/`eternal_night` 是否拆為獨立光照屬性並替換為實際地形高度標籤；於 `creatures.md` 補上 `ai_style` 列。

---

## 3. 招式／特性（`A13`–`A17`）

### A13 · high · 御三家招式與 `p5-05` 基礎招式池發生完全重複與近乎相同數值（3 組，含 1 組八欄 100% 相同）
- **位置與 ID**：
  1. `Data/_incoming/p2-08-starters-draft-01/moves/moves.json:5`（`shadow_nightpaw_swipe`）vs `Data/_incoming/p5-05-moves-draft-03/moves/moves.json:417-426`（`shadow_dusk_fang`）
  2. `Data/_incoming/p2-08-starters-draft-01/moves/moves.json:2`（`light_mirror_glint`）vs `Data/_incoming/p5-05-moves-draft-01/moves/moves.json:371-380`（`light_prism_beam`）
  3. `Data/_incoming/p2-08-starters-draft-01/moves/moves.json:4`（`light_solar_array_lance`）vs `Data/_incoming/p5-05-moves-draft-03/moves/moves.json:406-415`（`light_zenith_lance`）
- **證據引文**：
  - **100% 完全相同**：`shadow_nightpaw_swipe` 與 `shadow_dusk_fang` 皆為 `element: "shadow", kind: "damage", power: 35, cooldown_ms: 1000, accuracy: 100, range_class: "melee", shape: "front", base_range_mm: 2000`。
  - **近乎相同（違反同屬性＋同形狀＋同距離威力差 > 10 規則）**：`light_mirror_glint`（`P40 / CD1500 / Acc100 / long / line / 24000mm`）與 `light_prism_beam`（`P45 / CD1500 / Acc100 / long / line / 24000mm`）僅差 5 點威力；`light_solar_array_lance`（`P110 / CD8000 / Acc85 / long / line / 24000mm`）與 `light_zenith_lance`（`P105 / CD7800 / Acc80 / long / line / 24000mm`）僅差 5 點威力、200 ms CD。
- **建議**：將 `shadow_nightpaw_swipe` 改為 `shape: "sweep"`（一併修正 A16 形狀不符）；為御三家日線招式加入次要效果（`kind: "both"`）或調整形狀／數值帶以區隔泛用招式。

### A14 · high · 特性無內建屬性欄位，導致跨屬性主題但「不綁定屬性參數」之特性在執行期 100% 完全重複（3 組完全同效＋1 組僅差 1 秒）
- **位置與 ID**：
  1. `Data/_incoming/p5-05-traits-draft-03/traits/traits.json:205-220`（`trait_fire_scorching_momentum`）vs 同檔 `:459-474`（`trait_thunder_supercharged_stride`）
  2. `Data/_incoming/p5-05-traits-draft-02/traits/traits.json:145-160`（`trait_water_undertow_escape`）vs `Data/_incoming/p5-05-traits-draft-03/traits/traits.json:402-417`（`trait_wind_desperation_updraft`）
  3. `Data/_incoming/p5-05-traits-draft-01/traits/traits.json:69-84`（`trait_wood_photosynthesis`）vs `Data/_incoming/p5-05-traits-draft-03/traits/traits.json:146-161`（`trait_water_revitalizing_spring`）
  4. `Data/_incoming/p5-05-traits-draft-01/traits/traits.json:363-378`（`trait_wind_tailwind_glide`）vs `Data/_incoming/p5-05-traits-draft-03/traits/traits.json:519-534`（`trait_light_guiding_beam`）
- **證據引文**：
  - `trait.schema.json` 不含 `element` 欄位，ID 中的屬性僅為命名主題：
    - 第 1 組（同在 batch 03）皆為 `on_hp_above {percent: 80}` → `move_speed_mod_percent {delta: 15}`，JSON 結構與數值 100% 一致。
    - 第 2 組皆為 `on_hp_below {percent: 35}` → `move_speed_mod_percent {delta: 20}`，100% 一致。
    - 第 3 組皆為 `on_summon` → `heal_percent_max_hp {percent: 8}`；依 `combat.md:99` 治療為瞬間生效，`duration_ms`（`6000` vs `5000`）對純治療無任何作用，執行期效果 100% 相同。
    - 第 4 組皆為 `on_ally_swap` → `move_speed_mod_percent {delta: 20}`，僅持續時間差 1 秒（`6000` vs `5000`）。
- **建議**：對所有不帶屬性篩選參數的特性執行跨屬性全池去重；將重複項改綁各自屬性之觸發或效果（如 `on_move_used` / `on_hit_taken` 指定屬性，或改加屬性專精／抗性）。

### A15 · medium · 跨批次特性存在嚴格支配（Strictly Dominated，共 5 組＋1 組同批高度近似）
- **位置與 ID**：
  1. `trait_shadow_dusk_flicker`（`traits-01:586`，`HP > 90%` 移速 `+15%`）嚴格劣於 `trait_fire_scorching_momentum`／`trait_thunder_supercharged_stride`（`traits-03:205, 459`，`HP > 80%` 移速 `+15%`）。
  2. `trait_shadow_void_embrace`（`traits-01:544`，`HP < 25%` 閃避 `+10%`）嚴格劣於 `trait_wind_feather_grace`（`traits-01:404`，`HP < 40%` 閃避 `+10%`）。
  3. `trait_fire_conflagration_flare`（`traits-02:221`，`HP < 30%` 射程 `+20%`）與 `trait_earth_shattered_carapace`（`traits-03:240`，`HP < 30%` 射程 `+20%` 且土抗 `-15`）皆嚴格劣於 `trait_wood_thorn_lash`（`traits-03:87`，`HP < 35%` 射程 `+20%` 無副作用）。
  4. `trait_light_saving_grace`（`traits-03:501`，`on_hp_cross_below 25%` 回血 `12%`）在 Q-C18 單次跌破門檻規則下嚴格劣於 `trait_water_cleansing_tide`（`traits-01:168`，`on_hp_cross_below 30%` 回血 `12%`）。
  5. `trait_fire_heat_exhaust`（`traits-01:211`，`on_summon 5000ms` 移速 `+15%`）嚴格劣於御三家 `trait_shadow_longnight_stalker`（`p2-08/traits/traits.json:4`，`on_summon 5000ms` 移速 `+15%` **且**閃避 `+20%`）。
  6. 同批近似：`trait_earth_faultline_shift`（`traits-03:282`，`on_ally_swap 5000ms` 射程 `+15%`）vs `trait_metal_ballistic_pivot`（`traits-03:27`，`on_ally_swap 4000ms` 射程 `+20%`）。
- **證據引文**：副手透過琉璃珠可跨物種提供特性（`creatures.md:43`），上述屬性無關特性在同一候選池中直接構成上下位替代。
- **建議**：提高嚴苛門檻或帶副作用特性的正向數值（如 `trait_shadow_dusk_flicker` 移速改 `+20%`、`trait_shadow_void_embrace` 閃避改 `+15%`、`trait_earth_shattered_carapace` 射程改 `+25..+30%`、`trait_light_saving_grace` 回血改 `15%`）。

### A16 · low · 招式／特性名稱或 README 描述與實際幾何形狀、效果或 `combat.md` 機制不符（5 項）
- **位置與 ID**：
  1. `p2-08-starters-draft-01/moves/moves.json:5` & `copy-terra.json:103-109`（`shadow_nightpaw_swipe`）
  2. `p5-05-traits-draft-02/traits/traits.json:499-514`（`trait_light_blinding_arrival`）
  3. `p5-05-moves-draft-05/README.md:71` vs `moves/moves.json:479-496`（`light_dazzling_crescent`）
  4. `p5-05-moves-draft-06/README.md:80` vs `moves/moves.json:35-52`（`wood_tangle_grove`）
  5. `p5-05-moves-draft-02/moves/moves.json:336-345`（`thunder_chain_needle`）與 `p5-05-moves-draft-03/moves/moves.json:71-81`（`wood_vine_grapple`）
- **證據引文**：
  - `shadow_nightpaw_swipe` 名稱含 `swipe` 且文案寫 `"揮出裹著暗影的爪擊"`，但 `shape` 為正前方矩形 `"front"` 而非扇形橫掃 `"sweep"`。
  - `trait_light_blinding_arrival` 含 `blinding`（致盲敵方），實際效果為自身 `evasion_mod_percent: +8`。
  - `moves-05/README.md:71` 對 `light_dazzling_crescent` 寫 `"blinding crescent sweep"`，實際效果為降光抗 `resist_mod: light -20`。
  - `moves-06/README.md:80` 對 `wood_tangle_grove` 寫 `"pinning evasive targets"`（定身），實際效果為降閃避 `evasion_mod_percent: -10`。
  - `thunder_chain_needle`（連鎖）與 `wood_vine_grapple`（抓鉤）皆為純單次 `line`／`dash` 傷害，`combat.md` 無連鎖彈跳或拉拽機制。
- **建議**：`shadow_nightpaw_swipe` 改 `shape: "sweep"`；`trait_light_blinding_arrival` 改名 `trait_light_mirage_arrival`；修正 `moves-05`／`moves-06` README 用字；撰寫 `chain_needle`／`vine_grapple` 玩家文案時不得承諾彈跳或拉人。

### A17 · low · 全部 9 個瞬間治療特性皆帶有執行期無作用的 `trigger.params.duration_ms`
- **位置與 ID**：
  - `traits-draft-01/traits/traits.json`：`trait_wood_photosynthesis` (`6000`)、`trait_water_cleansing_tide` (`1000`)、`trait_thunder_grounding_circuit` (`5000`)
  - `traits-draft-02/traits/traits.json`：`trait_wood_spore_regeneration` (`3000`)、`trait_water_spring_surge` (`5000`)、`trait_light_halo_rebound` (`3000`)
  - `traits-draft-03/traits/traits.json`：`trait_wood_sap_absorption` (`4000`)、`trait_water_revitalizing_spring` (`5000`)、`trait_light_saving_grace` (`1000`)
- **證據引文**：`common.schema.json#/$defs/trigger` 強制所有事件觸發必填 `duration_ms`，但 `heal_percent_max_hp` 為瞬間結算（`combat.md:99`），該毫秒數完全不生效，各檔卻填寫 `1000`～`6000` 不等數值，易誤導後續文案作者寫成「持續 N 秒回血」。
- **建議**：將純瞬間治療特性的 `duration_ms` 統一固定為 `1000` 並於文案規範註明不得寫出持續秒數。

---

## 4. 引用完整性（`A18`）

### A18 · medium · `p5-03` 跨批次依賴 `p5-05` 招式／特性而無法單獨通過驗證，且 10 條進化線引用的 `concept.archetype` 尚未匯入 `catalog.js`
- **位置與 ID**：
  - `Data/_incoming/p5-03-creatures-draft-01/creatures/creatures.json`（21 隻宙獸）
  - `Data/_incoming/p2-08-starters-draft-01/creatures/creatures.json`（9 隻宙獸）
  - `Data/_incoming/p5-02-archetypes-draft-01/{archetypes-a.json,archetypes-b.json}` vs `tools/prompt-forge/catalog.js`
- **證據引文**：
  - **跨批次檢查全數存在（0 斷鏈）**：`p5-03` 引用的 21 個 `signature_move` 全數存在於 `p5-05-moves-draft-01/moves/moves.json`，7 個 `trait` 全數存在於 `p5-05-traits-draft-01/traits/traits.json`；`p2-08` 與 `p5-03` 的 `evolves_to`（20 處）與 `relations/edges.json`（6 + 14 = 20 條邊）端點全數存在且 100% 符合 `a < b` 字典序；`Data/retired-ids.txt` 為空（0 引用）。
  - **單批獨立性缺口**：`p5-03-creatures-draft-01/` 目錄內不含 `moves/` 與 `traits/`，單獨執行 `validate-data --root Data/_incoming/p5-03-creatures-draft-01` 會報 28 個 `dangling_ref`；且 `tools/prompt-forge/gen_designed.py:59` 只讀單一 `batch` 目錄下的 `moves/` 與 `traits/`，直接掛載 `p5-03` 會失敗。
  - **原型庫未合流**：`p2-08` 與 `p5-03` `concept.archetype` 引用的 10 個 `arch_*` ID（均為 `sensitivity: "ok"`）目前僅存在於 `Data/_incoming/p5-02-archetypes-draft-01/`，尚未併入 `tools/prompt-forge/catalog.js`（既有 `catalog.js` 條目亦尚無 `id` 欄位），且 `validate_data.py` 未驗證 `concept.archetype` 是否存在。
- **建議**：入庫 `Data/` 時強制先合入 `p5-02 → catalog.js` 與 `p5-05`（至少 batch 01），再搬入 `p2-08`／`p5-03`；更新 `gen_designed.py` 支援跨目錄解析招式／特性。

---

## 5. 文案規格（`A19`–`A22`）

### A19 · medium · `p5-05` 多份 `README.md` 在九形狀與 `on_hp_cross_below` 遷移後未同步更新，表列規格與實際 JSON 矛盾
- **位置與證據引文**：
  1. `Data/_incoming/p5-05-moves-draft-01/README.md:17, 35`、`moves-draft-02/README.md:47-68`、`moves-draft-03/README.md:48-60, 113-144`、`moves-draft-04/README.md:38-98, 130-203`：仍全篇記載舊四形狀（`line / cone / aoe / dash`），與同目錄 `moves.json` 已遷移之九形狀（`front / sweep / burst / line / cone` + `origin` + `movement`）不符。
  2. `Data/_incoming/p5-05-traits-draft-01/README.md:15-24, 51, 75`：`trait_water_cleansing_tide` 已改為 `on_hp_cross_below`（`duration_ms: 1000`），但觸發統計表漏列 `on_hp_cross_below`（實為 1），仍寫 `on_hp_below: 4`（實為 3），且 `:51` 參數欄漏寫 `1000ms`。
  3. `Data/_incoming/p5-05-traits-draft-03/README.md:9, 20-30, 76, 101, 144, 164`：`:9` 仍寫 `"7 trigger types"`（實為 8 種）；`:24, 101` 將 `on_hp_below` 誤計為 6（實為 5 + `on_hp_cross_below` 1）並誤稱 `"All ≥ 3"`；`:144` 缺口表與 `:164` 待決問題仍把 `trait_light_saving_grace` 列為已被 schema 禁止的 `on_hp_below + heal_percent_max_hp`。
- **建議**：以腳本從最新 `moves.json` / `traits.json` 重新產出各批 `README.md` 統計表，並刪除已定案關閉之舊 Open Issues。

### A20 · low · `p5-05-moves-draft-04/README.md` 超過單檔 200 行上限
- **位置與證據引文**：`Data/_incoming/p5-05-moves-draft-04/README.md:1-232` 共 **232 行**，違反 `.claude/rules/docs-and-progress.md`「單一文件 > 200 行時拆分」之規範。
- **建議**：精簡重複的前後比較表，或將 Batch 01–04 累計 150 招總表拆分為獨立文件。

### A21 · low · `p5-03/copy-terra.json` 有 3 筆繁中圖鑑以「純漢字數（不含標點）」計未達 45 字，且兩批 `copy-terra.json` 格式與英文連字號慣例不一致
- **位置與 ID**：
  - `Data/_incoming/p5-03-creatures-draft-01/copy-terra.json:8, 16, 112`（`metal_clock_chick`、`metal_gear_strider`、`earth_gilded_warden`）
  - `Data/_incoming/p2-08-starters-draft-01/copy-terra.json:58, 177-182`
- **證據引文**：
  - 任務約束（`docs/roadmap/phase-5.md:36`）要求「圖鑑繁中 45–60 字」。`p2-08/README.md:42` 驗收記錄 `"繁中 49–52 字"` 係採**不含標點之純漢字數**（含標點為 54–58）；若以同一純漢字口徑檢驗 `p5-03/copy-terra.json`，有 3 筆低於 45 字：`metal_clock_chick` **43 字**（含標點 47）、`metal_gear_strider` **44 字**（含標點 48）、`earth_gilded_warden` **44 字**（含標點 48）。
  - 兩份 `copy-terra.json` 頂層鍵不一致（`p2-08` 用 `issues` + `source`，且 `star_blaze_foal` 殘留臨時欄位 `"opus_note"`；`p5-03` 用 `issues_resolved` 且無 `source`）。
  - `p5-03/copy-terra.json` 英文圖鑑將複合修飾語全數拆開不加連字號（如 `:17` `"half suit"`、`:65` `"Wood grained"`、`:73` `"twin hulled"`、`:137` `"gold trimmed"`、`:153` `"spindle shaped"`），與 `p2-08`（`"lance-like"`、`"shadow-cloaked"`）體例不一。
- **建議**：明訂繁中字數是否含標點（若不含標點，將該 3 筆各補 2–3 字）；補齊英文複合形容詞連字號（確認不超出 40 詞上限）；統一文案 JSON schema 並移除 `"opus_note"`。

### A22 · low · `p5-03-creatures-draft-01/README.md` 前後段落自相矛盾，且抗性表漏列次要抗性與風線第二弱點
- **位置與證據引文**：`Data/_incoming/p5-03-creatures-draft-01/README.md:30-66, 71, 74-77`
  - `:74` §4.4 寫 `"待 TERRA 進行進一步文學潤飾後產出 copy-terra.json"`，`:77` 卻寫 `"中英名稱與圖鑑文案在 copy-terra.json"`，同檔自相矛盾；`:71` §4.1 亦仍寫 `"待 P2-01 / P2-09 正式定案狀態效果詞彙後"`。
  - `:30-52` 數值表「本身抗性與主要弱點」漏列所有非零次要抗性；且 `:58` 稱 `"無任一弱點元素在批次中被重複使用"` 僅對 `-20/-30/-40` 主弱點成立——風線（`creatures.json:948, 1002, 1047`）另帶次要弱點 `"thunder": -5 / -10 / -10`，與水線主弱點 `thunder` 重疊（其餘 6 線皆無第二弱點）。
- **建議**：移除 `p5-03/README.md` §4.4 過期敘述並更新 §4.1；於表格補齊次要抗性，並確認風線是否保留額外雷弱點。

---

## 6. 各批次掃描統計表

> 註：跨批次問題（如撞名、數值重複、外部引用）會同時計入相關之兩個批次列，故各列「涉及問題數」加總大於不重複問題總數（**22 項**）。

| 批次／檔案路徑 | 檢查內容與筆數 | 1.命名 | 2.設定矛盾 | 3.招式/特性 | 4.引用完整性 | 5.文案規格 | 涉及問題數（對應 ID） |
|---|---|---:|---:|---:|---:|---:|---|
| `Data/_incoming/p2-08-starters-draft-01` | 9 宙獸、9 招式、3 特性、6 關係邊、21 文案、README | 2 | 5 | 3 | 1 | 1 | **12**（`A3, A4, A6, A8, A9, A10, A11, A13, A15, A16, A18, A21`） |
| `Data/_incoming/p5-02-archetypes-draft-01` | 84 原型（a: 39、b: 45；對照 `catalog.js` 83 筆）、2 README | 0 | 0 | — | 1 | 0 | **1**（`A18`） |
| `Data/_incoming/p5-03-creatures-draft-01` | 21 宙獸、14 關係邊、21 文案、README | 1 | 3 | — | 1 | 2 | **7**（`A3, A7, A10, A11, A18, A21, A22`） |
| `Data/_incoming/p5-05-moves-draft-01` | 40 招式、README | 2 | — | 1 | 0 | 1 | **4**（`A1, A2, A13, A19`） |
| `Data/_incoming/p5-05-moves-draft-02` | 40 招式、README | 1 | — | 1 | 0 | 1 | **3**（`A5, A16, A19`） |
| `Data/_incoming/p5-05-moves-draft-03` | 40 招式、README | 2 | — | 2 | 0 | 1 | **5**（`A2, A5, A13, A16, A19`） |
| `Data/_incoming/p5-05-moves-draft-04` | 30 招式、README | 0 | — | 0 | 0 | 2 | **2**（`A19, A20`） |
| `Data/_incoming/p5-05-moves-draft-05` | 30 招式、README | 2 | — | 1 | 0 | 0 | **3**（`A1, A5, A16`） |
| `Data/_incoming/p5-05-moves-draft-06` | 20 招式、README | 0 | — | 1 | 0 | 0 | **1**（`A16`） |
| `Data/_incoming/p5-05-traits-draft-01` | 30 特性、README | 2 | — | 3 | 0 | 1 | **6**（`A1, A5, A14, A15, A17, A19`） |
| `Data/_incoming/p5-05-traits-draft-02` | 30 特性、README | 1 | — | 4 | 0 | 0 | **5**（`A5, A14, A15, A16, A17`） |
| `Data/_incoming/p5-05-traits-draft-03` | 30 特性、README | 2 | — | 3 | 0 | 1 | **6**（`A1, A5, A14, A15, A17, A19`） |
| `Data/_incoming/p3-planet-types-draft-01` | 6 星球類型、README | 0 | 1 | — | 0 | 0 | **1**（`A12`） |
| `Data/_incoming/p0-05-samples`（測試樣本） | valid 12 筆 + invalid 12 檔（全 `sample_*`） | 0 | 0 | 0 | 0 | 0 | **0** |
| `Data/retired-ids.txt` | 0 筆退役 ID（1 行標頭註解） | 0 | — | — | 0 | 0 | **0** |
| **不重複問題總計** | **30 宙獸、84 原型、209 招式、93 特性、20 邊、6 星球類型** | **5** | **7** | **5** | **1** | **4** | **共 22 項（high 4 / medium 7 / low 11）** |

---

## 7. 待人工確認清單（邊界／設計意圖事項，不計入確定缺陷）

1. **H1 · 火獅線英文名與《寶可夢》火系角色之音節鄰近度**：`p5-03` 火線 `Cindrel → Cindrava → Cindraval` 與寶可夢 `Cyndaquil / Quilava / Cinderace` 共享 `Cind-` 與 `-ava` 音節結構（拼字不重複，由人工裁定語感距離是否可接受）。
2. **H2 · `wind_monsoon_galeon` 單 `l` 拼字是否為刻意造詞**：`p5-03` 風線成年 ID 寫為 `galeon`（西文拼法或 `gale` + `galleon` 混成詞）而非英文標準 `galleon`；入庫前請確認是否保留，避免入庫後改名衍生退役 ID。
3. **H3 · `p5-02` 文化原型敏感度邊界與同類動物基底選題提醒**：`p5-02/README-a.md` 與 `README-b.md` 已列待人工裁定項（既有 `catalog.js`「葛內舍象神智慧」「風之靈納瓦霍」「納努克白熊之主」是否由 `ok` 改 `review`；新增口傳／史詩條目 `arch_la_galigo`、`arch_lafaek_diak`、`arch_meduu_ribtal`、`arch_adaro`、`arch_edai_siabo_lakatoi`、`arch_kaassassuk`、`arch_palraiyuk` 是否維持 `ok`；`arch_douglas_mawson` 歸入極地）。另大洋洲同時收錄 `arch_edai_siabo_lakatoi`（海蝕洞巨鰻，已用於 `p5-03` 水線）與 `arch_sina_and_eel`（清泉銀鰻），極地 `arch_amarok`（已用於御三家月線）與既有中亞「阿史那灰狼」同為灰狼——文化來源不重疊，但後續 P5-03 批次建議避開再抽鰻魚或灰狼基底。
4. **H4 · 前審已立案待決之平衡與定位取捨**：含 `docs/reviews/P2-08-sol.md`（御三家 B1–B5 抗性軟三角與特性持續時間）、`docs/reviews/P5-05-batch06-terra.md`（`metal_caltrop_zone` 與 `wood_tangle_grove` 場地總威力偏低、6 組疊層招與 batch 05 立即增益招之區隔、同名場地重疊是否分別結算），以及 `p5-03` 是否要將部分純傷害精通招改指派為 `p5-05` batch 05/06 的控場／場地招以恢復原初定位構想。

---

## 8. 未檢查／無法確認清單

1. **U1 · Shell 自動化驗證與目錄列舉**：受「禁止執行 shell」約束，本輪未實際執行 `tools/validate-data`、`tools/balance` 或目錄列舉指令，所有檢查皆以靜態全量讀檔完成；若有未在 `docs/roadmap/*.md` 與測試檔提及之額外隱藏檔案於 `Data/_incoming/` 下，無法由工具列舉確認。
2. **U2 · `p5-05` 200 招式與 90 特性的玩家顯示中英文名稱與說明文案**：目前 `move.schema.json` 與 `trait.schema.json` 尚無本地化顯示欄位，`p5-05` 僅有英文 `id` 與數值參數（無 `copy-terra.json`），故無法檢查玩家端招式／特性中英譯名是否重複或文案字數是否合規。
3. **U3 · 超載（Overload）第四型態與跨線關係網連通性**：超載規則（Q-C12）尚未定案，`p2-08` 與 `p5-03` 均未含超載資料；跨線關係邊與全圖連通性屬 P5-06 範疇，且 `docs/design/lobby-meta.md` 不在本輪指定對照文件清單內。
4. **U4 · 2D／3D 美術交付資產**：`Art/_incoming/` 下之概念圖、貼圖與模型資產（P5-04／P5-09）不在本輪文字資料集掃描範圍內。
