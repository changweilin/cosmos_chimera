# P1 星球垂直切片

**出口條件**：在 R=640 m 程序化星球上以第三/第一人稱行走、跳躍、繞星球一圈回到原點；LOD 無裂縫；卡通渲染達到目標畫風雛形；60 fps @ 目標 PC，並每階段在 Steam Deck 實機量一次（目標 30 fps 以上）。
讀：`../architecture/planet-tech.md`、`../decisions/ADR-0001-cube-sphere.md`

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P1-01 | `PlanetConfig`、`PlanetFrame`（up/east/north）、方向↔座標轉換 | OPUS | SOL | 單元測試：任一點三軸正交且 up 指離球心 | 🟦 |
| P1-02 | Cube-sphere 四叉樹 + 等面積映射 + LOD 分裂/合併 | OPUS | SONNET | 繞行一圈無裂縫、無跳變（錄影檢查） | 🟦 |
| P1-03 | 區塊網格生成（Jobs + Burst）、附近區塊 MeshCollider | SONNET | OPUS | 主執行緒每幀 < 2 ms；碰撞只存在 ~128 m 內 | ✅ |
| P1-04 | 高度函數 `height(dir)`：多層 3D 噪聲（暫不接真實地圖） | SONNET | SOL | 快照測試；同 seed 同結果 | 🟦 |
| P1-05 | 球面角色控制器（Rigidbody 自訂重力、坡度、跳躍）+ Cinemachine 第三/第一人稱 | SONNET | OPUS | 在極點與面交界處行走無抖動、無翻轉 | 🟨 |
| P1-06 | 導航原型：局部 NavMesh vs steering（回答 Q-T1） | OPUS | SONNET | 比較報告 + 決定寫入 planet-tech.md | ✅ |
| P1-07 | URP 卡通著色（地形/角色）、角色反向外殼描邊、大氣霧 | SONNET | 人工 | 美術比對參考截圖通過 | 🟦 |
| P1-08 | 天空：日夜循環 + 星海 skybox（與大廳共用） | SONNET | 人工 | 日夜轉換無接縫 | 🟦 |
| P1-09 | 草/植被 GPU instancing + 風場 | SONNET | OPUS | 10 萬株草 60 fps | 🟨 |
| P1-10 | 不同 R（320/640/1280）參數切換測試 | SONNET | — | 三種尺寸皆可繞行，地平線符合公式 | ✅ |
| P1-11 | 地形材質混合＋程式生成筆觸貼圖（`design/vision.md#美術投資` 第 2 項）：`StrokeTexture`（r 筆觸／g 長紋／b 顆粒，可拼接）、`CosmiBiome.hlsl` 三平面取樣；草地雙色筆觸、沙紋、岩面鑿痕與裂紋、雪堆、草與岩之間的裸土帶 | FABLE | 人工 | 測試台截圖由使用者挑選；草與地面同一筆觸場；`_StrokeStrength` 0 時無筆觸 | 🟨 |
| P1-12 | GPU instancing 植被：草海密度 ×10（第 1 項）＋花海 | 待定 | OPUS | Steam Deck 800p ≥ 30 fps；截圖驗收 | ⬜ |
| P1-13 | 水面：岸邊泡沫、折射、焦散（第 3 項）；風浪與浪花（`design/planet-world.md#海浪`：浪向＝風向、浪高 ∝ 風²、白頭浪、岸邊碎浪） | Claude Opus | 人工 | 風浪＋浪花已完成：`SeaWavesTests`（浪高 ∝ 風²、深水波速順風、轉向不跳）；截圖 `Logs/sea-{calm,gale,gale-cliff}.png`（`SeaCaptureTests`，`COSMIMERA_CAPTURE=1`）；折射、焦散未做 | 🟨 |
| P1-14 | 體積霧與光束（第 4 項） | 待定 | 人工 | 截圖驗收；Steam Deck ≥ 30 fps | ⬜ |
| P1-15 | 遠景 LOD／impostor（第 5 項）：`FarScenery` 把 90 m 外到霧裡（≤ 260 m）的樹／大岩／石柱／晶簇合併成低面數輪廓分塊網格（`FarSceneryMeshes`，每物件 10–42 面，每幀最多建 2 塊），`ToonScenery` `_FarFade` 在近景範圍與外緣縮回地面；建築原本就全星球常駐（`SettlementView`） | FABLE | OPUS | 90 m 外仍看得到樹與建築輪廓；截圖驗收（`Logs/geology-*-far{,-before}.png`）；Steam Deck 幀率未量 | ✅ |
| P1-16 | 匯入美術模型與貼圖取代程序網格（第 6 項；`design/art-asset-pipeline.md`，3D 工具選型已移交其他專案） | 待定 | 人工 | 逐類資產驗收 | ⬜ |
| P1-17 | 程序化器物外觀（`design/gear-looks.md`）：a 規則層 `GearLooks.Sample`；b 背包逐件實例＋外觀 seed 存檔遷移；c 佔位網格（NPC 穿戴、紙娃娃、虛擬大手、場景擺設）；d 格擋護盾特徵與宙獸飾品掛飾；e 手工部件庫（隨 P1-16） | OPUS | SOL | a 同 seed 同結果且各軸可辨的 EditMode 測試；c 起截圖驗收 | 🟦 |
| P1-18 | 程序化地形與場景物件打磨：岩石造型擴充、樹種擴充、懸崖岩面、河床（卵石、沙洲、濕岸）。已做：樹種（樺、柳、松、棕櫚）、岩石造型（岩堆、板岩片、砂岩層、卵石堆、溶溝），河床（`../design/planet-world.md#河床`）；未做：懸崖岩面 | Claude Opus | 人工 | 同 seed 同結果；測試台截圖由使用者挑選 | 🟨 |

## 未收尾紀錄
[log/phase-1.md](log/phase-1.md)（partial／blocked 才寫；舊紀錄為封存）。
