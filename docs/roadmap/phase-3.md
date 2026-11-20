# P3 世界生成

**出口條件**：選定 seed 與星球類型 → 生成含程序化地貌、道路與聚落、生物群系、區域圖、地城、BOSS 的星球；可從降落玩到 BOSS 結束。
讀：`../design/planet-world.md`、`../architecture/planet-tech.md`
**開工前需定案**：Q-M2（Q-W1 已因 ADR-0008 不再需要）

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P3-01 | ~~真實地圖資料授權調查與署名方案（Q-W1）~~ 退役：地貌改程序化（ADR-0008） | ARGON | OPUS + 人工 | — | ⛔ |
| P3-02 | 程序化地貌合成 `LandformSynth`（12 種地貌標籤 → 高程/水系格網，整數運算；取代 DEM 烘焙，ADR-0008） | OPUS | SOL | 每種標籤可辨識；同輸入跨後端逐位元相同 | 🟨 |
| P3-03 | 地貌印章 gnomonic 投影拼貼 + 外緣混合 | OPUS | SOL | 印章內地貌可辨識；接縫無斷崖 | 🟨 |
| P3-11 | 道路與建築：規則層 `SettlementPlanner`（生成樹道路、聚落地塊）＋表現層 | OPUS（規則）+ SONNET（表現） | SOL | 每區域道路可達；建築不落海、不互相重疊；快照測試 | 🟨 |
| P3-04 | 生物群系（緯度×海拔×濕度×星球類型） | SONNET | SOL | 6 種星球類型視覺差異明顯 | 🟦 |
| P3-04a | 當地環境接線：地貌印章對應地形移動修正；依 seed×地貌×180 秒時段輪替自然天氣，接入角色移速／衝刺／翻滾／視野與宙獸戰鬥規則，生活能力切換保留環境修正 | Codex | OPUS | 地貌核心／印章外設定正確；天氣進入實際 RunSession；時段切換與晴天視野恢復有回歸；新增 API 契約須審查 | ✅ |
| P3-04b | 主動操縱天氣與戰術資訊（`design/field-utilities.md#主動操縱天氣`）：合格隊伍在手冊選八種天氣，覆寫施放區域至下一個自然時段，共用冷卻 180 秒；HUD／手冊顯示當地天氣、來源、倒數與戰鬥修正，繁中／英／日文案外部化 | Codex | OPUS | 無能力／冷卻中拒絕且不改狀態；離區不攜帶、返回仍有效；到期回自然天氣且不清冷卻；API 契約與三語接線有回歸 | ✅ |
| P3-05 | 區域圖（Voronoi + 連通保證）+ 區域詞綴 | OPUS | SONNET | 1000 個 seed 測試：100% 可達 BOSS | ✅ |
| P3-06 | POI 佈置、宙獸棲地、野生宙獸生成（依 Q-M2） | SONNET | OPUS | POI 密度符合每 km² 預算 | ✅ |
| P3-07 | 暗黑式地城：房間模組 + 拼接規則 + 詞綴。**規則層已做**：`WorldGen.RoomDungeonPlanner` → `RoomDungeon`（每地形獨立亂數流、房間樹、守關房→BOSS 死路、寶藏死路、開放程度環路、菁英詞綴、模組外形＋旋轉）與 `AdvancedTerrains` 十地形表。**獨立場景已接線**：`RoomDungeonGeometry`（房間網格鋪在同半徑球面、牆＝`PlanetWalls` Voronoi 脊、門＝走廊缺口、競技場環形封印）＋ `RunSession` 進出切換（隱藏地表、暫停地表野生、平地 `PlanetTerrain`＋房間牆、出口墊回地表）。**室內氛圍已接**：`DungeonAtmosphere`（每地形 `DungeonMood`：壓暗環境光與太陽、短距霧、降飽和＋暗角 Volume、每房一盞燈＋點光源，`ToonTerrain` 點光源色階）。**牆與頂的美術已接（Q-W46）**：`RoomDungeonGeometry.WallRuns`（牆段＋門缺口）→ `DungeonDressing`（10 地形程序化牆板與裝飾、室內天花板、6 種燈造型），地表迷宮套地形色霧＋暗角；`ToonScenery`／`ToonOutlined` 也接點光源。**地城文化已接**：`DungeonCulture`（每座地城抽地區／年代／副風格／外觀變體／新舊毀損）→ 色調、年代物件、柱頂、毀損，地表迷宮牆面與牆頂的地形物件（附生植物、熔岩與凝結岩塊、冰柱雪簷、鎖鍊影晶）；牆腳物件（圖騰、麻袋、木箱木桶、鐵箱、鍋爐、控制台、油桶、石像、碎石）是 `WorldObjects` 的可破壞物件。**未做**：寶藏房容器、菁英詞綴效果與名牌 | OPUS（規則）+ SONNET（實作） | SOL | 快照測試；無封死房間 | 🟦 |
| P3-08 | 任務鏈生成與 BOSS 戰框架 | SONNET | OPUS | 每星球至少 1 主線 + 1 BOSS | ✅ |
| P3-09 | 散佈系統（植被/岩石/互動物件 per biome） | SONNET | — | 無浮空/穿地物件 | ✅ |
| P3-12 | 可破壞場景物件（`design/destructibles.md`、ADR-0009、ADR-0010）：規則層物件表／物質反應／破壞集合／素材與珠寶掉落／夜間影素材；散佈位置移到規則層，樹／灌木／岩石阻擋；表現層剛體喚醒、球面重力、碎片、地上素材；目標切換可鎖定物件；1 m 自動拾取（Q-M6）、休眠寫回規則層（Q-M7）、迷霧隱蔽（Q-C37）與草叢隱蔽已接線 | OPUS（規則）+ SONNET（表現） | SOL／OPUS（草叢接線） | 每種物質與反應各有測試；十種屬性都有掉落來源；同 seed 破壞後 LOD 重建不復活；PlayMode：建築／樹破壞後角色與宙獸可通過 | 🟨 |
| P3-13 | 進階地形地城（原地表地牢調整）：僅 10 屬性進階地形為隨機程序化地城（地表封閉迷宮 `DungeonPlanner`＋`PlanetWalls` ∥ P3-07 獨立房間拼接），一般地表改為開放式程序化地形（依地貌設定障礙物密度與行動修正 `TerrainProfile`）；寶藏房依 Q-W3 定案（守關菁英＋可破壞容器、依區域階級分級）已接線 | OPUS（規則）+ SONNET（表現） | SOL | 900 個 seed：地城模式每條道路可通行、每個房間可達；一般地表無全域牆體且依地貌區分障礙物密度與行動修正；快照測試 | 🟨 |
| P3-14 | 城牆（`design/settlement-structures.md#城牆`）：城邦石牆／小鎮木柵／村落籬笆／遺跡殘牆，規則層牆段與城門，可破壞 | OPUS（規則）+ SONNET（表現） | SOL | 角色與宙獸只能由城門進出；牆段可被打壞後通行；快照測試（升 `GeneratorVersion`）；牆高石牆 3–10 m、木柵 1–3 m | ⬜ |
| P3-15 | 工廠（`#工廠`）：`BuildingKind.Factory`（都市、遺跡廢棄工廠）＋機房／工廠地城的地表入口（接 P3-07） | OPUS（規則）+ SONNET（表現） | SOL | 都市必有工廠地塊且不重疊；快照測試 | ⬜ |
| P3-16 | 隧道（`#隧道`）：高山／峽谷地貌的洞口，傳送到獨立室內場景（依賴 P3-07） | OPUS（規則）+ SONNET（實作） | SOL | 兩端洞口互通；宙獸隨行；無封死 | ⬜ |
| P3-17 | NPC 載具（`#交通工具`）：五類載具沿道路移動，規則層路徑與碰撞，可破壞掉素材 | OPUS（規則）+ SONNET（表現） | SOL | 同 seed 各客戶端位置一致；可被打壞並掉落；不卡住角色與宙獸 | 🟦 |
| P3-18 | 路人 NPC：依地區×時代＋主／副風格程序化生成外觀，星球人口與職業基準、區域→聚落→個人織物有界常態抽樣（規格 `design/characters.md#路人-npc`；基底模型等美術匯入 P1-16） | Claude Fable | SOL | 同 seed 各客戶端路人一致；同聚落內個體差異可見且不離開風格 | 🟦 |
| P3-19 | 自然生物：依環境生成，個體造型／年齡／健康有界常態抽樣（規格 `design/planet-world.md#自然生物`：植物年齡／健康、氛圍動物可被打壞掉素材、野生宙獸個體差異） | 待定 | SOL | 同 seed 一致；每種地貌有可辨識的生物組合 | 🟦 |
| P3-20 | 植被下層（`design/planet-world.md#散佈`）：`ScatterPlanner` 第二層（灌木／箭竹／芒草，依星球類型×海拔帶，`GeneratorVersion` 2，冠層逐位元不變）＋ `SceneryMeshes` 三種程序化網格 | FABLE | SOL | 冠層快照不變；每種下層植被在其海拔帶出現；芒草不擋、灌木與箭竹擋；同 seed 一致 | 🟨 |
| P3-21 | 作物田（稻／麥／甘蔗／玉米／煙草／可可／咖啡／茶／樟／葡萄／蘋果／橄欖；規格 `design/planet-world.md#作物田`：規則層地塊、每聚落隨機、草原型可隱匿／樹林型給樹果） | 待定 | SOL | 田地不落海、不與建築重疊；快照測試（升 `GeneratorVersion`）；作物分型與尺寸未定 | ⬜ |
| P3-22 | 火山地熱地物（熔岩流／火山灰／泥火山／間歇噴泉／硫磺；規格 `#火山地熱`：規則層危險地形，熔岩可走但持續傷害）＋地震噴發事件（`#地震噴發`：前震→裂縫→噴發→熔岩流冷卻，地形不變形） | 待定 | SOL | 每種地物各有規則測試；同 seed 各客戶端位置與週期一致；數值未定 | ⬜ |
| P3-23 | 海底地物（珊瑚礁／深海火山／鯨落／沉船；規格 `#海底`：一般海域可潛水，需先立水下系統設計文件）。**潛水視覺（純表現）不受阻擋，可先做** | 待定 | SOL | 待水下系統設計文件 | ⛔ |
| P3-24 | 地質變體（洞穴／山谷／山脈的岩性；規格 `#地質變體`）：**(a)** 每塊印章抽基岩（`ScatterGeology`：喀斯特／玄武岩／花崗岩／板岩），只換一般岩石與地表岩色，`ScatterPlanner.GeneratorVersion` 5、新物種 `granite_tor`；**(b)** `LandformSynth` v2 四種基岩地貌 `karst`／`basalt_terrace`／`granite_domes`／`slate_cliffs`（基岩固定、各有地形設定與物種表），並補上 `grassland`／`desert`／`swamp`／`mountain` 的生成器；親和標籤（`TemplatePlanner.Kinds`） | FABLE | SOL | (a) 每種地質可辨識（規則測試；實景未經目視）；(b) v1 十二種地貌逐位元不變、快照依版本重訂；形狀只看過離線 hillshade，實景未經目視 | 🟨 |
| P3-25 | 散佈依地貌印章：冠層密度依當地印章（`TemplatePlanner.ScatterDensity`）；**選種依地貌**（Q-W15：`ScatterBiomes` 物種目錄＋16 張地貌表、林型＝闊葉／針葉／混合／人工／次生、礦脈／石材／寒帶物種、下層密度依地貌；`TemplatePlanner.ScatterBiome`；`ScatterPlanner.GeneratorVersion` 4）＋ `SceneryMeshes` 11 個新造型 | FABLE | SOL | 每個地貌只長自己表內的物種；林班內林型一致、人工林單一樹種樹齡；下層物種守海拔帶；同 seed 一致；快照依版本重訂；新造型未經目視 | ✅ |
| P3-26 | 季節（規格 `design/planet-world.md#季節`：每顆星球由 seed 抽一季、整場不變；外觀＋天氣＋物種與可採集物）。**已做**：`WorldGen.Seasons`（規則層抽季節）＋四季外觀（`ScatterField` 葉色／冬季落葉與積雪、`PlanetLook` 草地變色）。**未做**：天氣（Q-W20）、物種與可採集物（Q-W21）、HUD 顯示季節 | FABLE | SOL | 同 seed 各客戶端季節一致；四季外觀可辨識（`Logs/season-*.png`）；四季物件與碰撞相同 | 🟦 |
| P3-27 | 礦物與採礦（規格 `#礦物`：寶石↔十屬性珠寶、生活主動技「採礦」、土／金屬性主動技採礦、一般主動技掉落極少；天然氣併入 P3-22） | 待定 | SOL | 每種寶石掉對應屬性珠寶；採礦與一般主動技掉落量有別；對應表與數值未定 | ⬜ |
| P3-28 | 寒帶地形（`#寒帶地形`）：`LandformSynth` v2 `tundra`／`glacier`／`cirque`／`moraine`；冰晶星偏好＋所有星球寒帶（\|y\| > 0.7）改抽寒帶地貌；各有地形設定、物種表、天氣偏向；表現層每塊印章自己的雪線。**海上冰山等 P3-23** | FABLE | SOL | 冰河地貌只在冰晶星與寒帶出現（規則測試）；快照依版本重訂；形狀只看過離線 hillshade，實景與雪線未經目視 | 🟨 |
| P3-29 | 奇觀地標（`design/planet-world.md#奇觀地標`）：規則層 `LandmarkPlanner`（八種：高塔／神廟／空島／太空電梯／太空站／迷宮／坑道／地城門；避開海、道路、建築、生成點；實心者擋角色與宙獸、散佈避開）＋表現層 `LandmarkMeshes`（地區×時代×功能×年齡的程序化網格）＋ `LandmarkView`，已接進 `RunSession`。v4 新增 5 種進階地形入口奇觀（運算中心／神木／海溝口／火山口／冰冠城堡，材質配色）與每星球恰好一個地城入口（`DungeonEntrance` 依星球屬性比例抽地形）。**未做**：內部與進入（P3-07、ADR-0011）、互動／破壞（Q-W25）、遠景 LOD 與霧外可見性 | FABLE | SOL | 同 seed 一致（快照）；奇觀不落海、不壓道路建築生成點、彼此不重疊；每種造型可辨識（`Logs/landmark-*.png`）；實景未經目視 | 🟨 |
| P3-30 | 地城暗黑氛圍表現層（`design/combat-feel.md` 〈地城氛圍〉）：點光源照明、暗角、降飽和、音場、菁英詞綴名牌、擊倒爆散與掉落噴灑；只套進階地形地城 | SONNET | 人工 | 一般地表外觀不變；Steam Deck 幀率；人工試玩 | ⬜ |
| P3-31 | 野生宙獸生態（`design/creature-ecology.md`）：物種候選依出生環境抽棲位→習性；四種遊蕩模式與速度／移動／停留的個體間、行動間標準差；同棲位異種活動區不重疊；交戰上限 2 vs 2。資料、importer、`RunEcology`、`Encounter`、觀察、存檔 v11（保留棲位／習性、進化重抽、舊存檔遷移）與 `ReproProbe` v7 已接線 | Codex（設計／提案）、Claude Opus（實作） | SOL／人工 | 提案通過 schema 與語義檢查；正式資料、importer、生成、戰鬥領域、觀察、收服／存檔完整遷移，EditMode／PlayMode 與重現性驗收 | 🟦 |
| P3-32 | 戰鬥旅人（[trainer-battles.md](../design/trainer-battles.md)#戰鬥旅人）：10+ 職業資料（32 職業／14 職務／6 職能）、各星球生成（獨立亂數流、`maxStage` 限制、`TrainerPlanner.Hash` 鎖版）、強度介於野生與 BOSS、視線挑戰（`IsInSight`／`StepPursuit`）與 25 m 迴避、`Observant` 互動觸發；外觀用路人 NPC 程序化佔位（`NpcMeshes.SpecForTrainer`／`TrainerView`）；三語職業／職務／職能與 HUD 文案、已接線 `RunSession` | OPUS（規則）+ SONNET（表現） | SOL＋ARGON（重現性必審） | 同 seed 同結果；既有生成序列不變；等級夾在野生與 BOSS 之間 | 🟨 |
| P3-33 | 地形試煉（[trainer-battles.md](../design/trainer-battles.md)#地形試煉）：10 位試煉主（固定 seed 路人佔位）、引導入地城（免除免疫限制）→守關菁英→試煉主＋地城 BOSS 對玩家→必定收服 BOSS（P5-10 新物種，定稿前用現有持有物種替身）、進階器皿配方＋試煉章、再戰；依賴 P3-13／P3-07、P2-18。**規則層已做**：`TrialPlanner`（BOSS＝持免疫線的星球同階型態：矮行星少年／行星成年／超巨星成年＋超載，試煉主隊同屬性同型態同等級）、`TrialProgress`（對話→守關→競技場→最終戰→勝利／落敗保留步驟、試煉中免除免疫、必定收服一次、首勝獎勵）、`Encounter.AddTrialBoss`（BOSS 先上場、倒下不補位、超巨星上場即超載）。**室內 6 種已接進 `RunSession`**：試煉主（`NpcMeshes.SpecForTrialMaster` 佔位＋屬性色光環）站在入口奇觀前，對話→進地城→擊倒守關菁英解封→進競技場開最終戰→勝利首勝寫試煉章、必定收服 BOSS（直接收服、不消耗器皿）、雙方全回復；落敗回地表入口並回復。**地表迷宮型 4 種已接線（Q-W45）**：`RoomDungeonPlanner.GenerateSurfaceMaze`（中央廣場＝奇觀＋競技場、守關房、向外長的房間樹、外緣入口對外門）＋ `AdvancedTerrains.GridStyle`（每種地形的格寬／門寬／環路開放程度／開闊格局，暫定）＋ `LandmarkPlanner` v5（迷宮外接圓內無道路建築生成點降落點）；試煉主站在對外門外，對話後對外門解封；封印（競技場、對外門）有實體碰撞。**未做**：進階器皿配方發放（尚無配方資料）、試煉主角色圖鑑 basic／complete、試煉外憑免疫進入 | SONNET | OPUS | 每種進階地形一條完整流程 PlayMode 測試 | 🟦 |
| P3-34 | 天氣與時段改版（`design/weather.md`）：規則層 `DayPhase`（當地太陽仰角 → 清晨／白天／黃昏／夜晚）、`WeatherField`（整數擬布朗＋均值回歸的 8 基礎＋10 地形異象向量）、`WeatherDynamics`（75 門檻、主導天氣、強度縮放修正）；`WeatherKind` 加 10 異象、時段光／影修正與夜晚視野；地表累積（1 秒固定步長整數積分）影響移速與跳躍；天氣操縱改為 180 秒傾向覆寫並可召喚（限原生地貌）／驅散異象；表現層太陽與 `LocalWeatherView` 改吃向量。**未做**：異象特殊效果（傷害、落雷、漏斗、致盲等）、輻射塵的聚落周邊、晝夜活動性（待 Q-W36） | OPUS | SOL＋ARGON（重現性必審） | 同 (seed, 區域, timeMs) 跨後端逐位元相同；異象不出現在非指定地貌（規則測試）；時段由太陽方向推得且與畫面一致；`ReproProbe` 覆蓋；`GeneratorVersion` 遞增 | 🟨 |
| P3-35 | 風向與陣風（`design/weather.md#風向`）：規則層每區域風向＋陣風；表現層草／樹／雲／粒子吃風向；暴風碎片（樹葉、斷枝、破損人造物）與陣風同步 | Claude Opus | SOL＋ARGON（重現性必審） | 同 (seed, 區域, timeMs) 各客戶端一致；`ReproProbe` 覆蓋（v9）；截圖驗收（未拍） | 🟨 |
| P3-36 | 規則層落雷（`design/weather.md#落雷`）：雷暴與離子風暴依權重打高處／金屬／孤樹，預警圈、傷害、點燃 | Claude Opus | SOL＋ARGON（重現性必審） | 落點與時間各客戶端一致；預警圈內才受傷；雷抗 ≥ 100 % 免疫；`ReproProbe` 覆蓋 | 🟨 |
| P3-37 | 延燒與火的類型（`design/destructibles.md#延燒`）：鄰接延燒、風向、雨雪抑制、六種火的外觀、焦土 | Claude Opus | SOL | 1 秒固定步長整數模擬可重現；下風延燒較快；大雨熄火 | ⬜ |
| P3-10 | 生成可重現性測試（跨機器雜湊一致） | SONNET | OPUS | Windows 兩台機器 `tools/repro/compare.py --policy b` PASS（離散內容相同、高度差 ≤ 3 mm，ADR-0007） | 🟦 |

## 未收尾紀錄
[log/phase-3.md](log/phase-3.md)（partial／blocked 才寫；舊紀錄為封存）。
