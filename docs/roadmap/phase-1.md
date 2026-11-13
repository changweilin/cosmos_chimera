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
| P1-09 | 草/植被 GPU instancing + 風場 | SONNET | OPUS | 10 萬株草 60 fps | ⬜ |
| P1-10 | 不同 R（320/640/1280）參數切換測試 | SONNET | — | 三種尺寸皆可繞行，地平線符合公式 | ✅ |

## 未收尾紀錄
[log/phase-1.md](log/phase-1.md)（partial／blocked 才寫；舊紀錄為封存）。
