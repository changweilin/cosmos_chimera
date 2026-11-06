# P1 星球垂直切片

**出口條件**：在 R=640 m 程序化星球上以第三/第一人稱行走、跳躍、繞星球一圈回到原點；LOD 無裂縫；卡通渲染達到目標畫風雛形；60 fps @ 目標 PC，並每階段在 Steam Deck 實機量一次（目標 30 fps 以上）。
讀：`../architecture/planet-tech.md`、`../decisions/ADR-0001-cube-sphere.md`

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P1-01 | `PlanetConfig`、`PlanetFrame`（up/east/north）、方向↔座標轉換 | OPUS | SOL | 單元測試：任一點三軸正交且 up 指離球心 | 🟨 |
| P1-02 | Cube-sphere 四叉樹 + 等面積映射 + LOD 分裂/合併 | OPUS | SONNET | 繞行一圈無裂縫、無跳變（錄影檢查） | 🟦 |
| P1-03 | 區塊網格生成（Jobs + Burst）、附近區塊 MeshCollider | SONNET | OPUS | 主執行緒每幀 < 2 ms；碰撞只存在 ~128 m 內 | ⬜ |
| P1-04 | 高度函數 `height(dir)`：多層 3D 噪聲（暫不接真實地圖） | SONNET | SOL | 快照測試；同 seed 同結果 | ⬜ |
| P1-05 | 球面角色控制器（Rigidbody 自訂重力、坡度、跳躍）+ Cinemachine 第三/第一人稱 | SONNET | OPUS | 在極點與面交界處行走無抖動、無翻轉 | ⬜ |
| P1-06 | 導航原型：局部 NavMesh vs steering（回答 Q-T1） | OPUS | SONNET | 比較報告 + 決定寫入 planet-tech.md | ⬜ |
| P1-07 | URP 卡通著色（地形/角色）、角色反向外殼描邊、大氣霧 | SONNET | 人工 | 美術比對參考截圖通過 | ⬜ |
| P1-08 | 天空：日夜循環 + 星海 skybox（與大廳共用） | SONNET | 人工 | 日夜轉換無接縫 | ⬜ |
| P1-09 | 草/植被 GPU instancing + 風場 | SONNET | OPUS | 10 萬株草 60 fps | ⬜ |
| P1-10 | 不同 R（320/640/1280）參數切換測試 | SONNET | — | 三種尺寸皆可繞行，地平線符合公式 | ⬜ |

## 交接紀錄
- 2026-11-06 · Claude Opus · done · P1-01：`Planet/Api` 新增 `PlanetConfig`（Radius 唯一來源、Center、`HorizonDistance`、`ToWorld`/`ToDirection`/`Altitude`）、`PlanetFrame`（East/North/Up 正交右手系，`At`/`FromUp`/`LocalToWorld`/`WorldToLocal`/`ProjectOnTangent`；極點 East 退回 (0,0,−1)）、`LatLon`（atan2 版，極點不失精度）。幾何改用 `Unity.Mathematics` 的 `float3`，見 ADR-0005（**提議中，待使用者確認**；manifest 釘選 1.3.3，lock 檔原本就解析為此版，無新下載）。驗證：EditMode 72/72（新增 31 項 Planet 測試），0 個警告；2000 個隨機點 + 極點/角落/近極點皆正交，地平線與 planet-tech.md 表一致 · 下一步：SOL 審查 P1-01
- 2026-11-06 · Claude Opus · partial · P1-02（邏輯層）：`CubeSphere`（等角 tan 映射 + 反函數、6 面軸表、由軸表推導的跨面鄰接表）、`ChunkId`（64-bit key、父子、同級鄰居含跨面）、`Runtime/Model/ChunkQuadtree`（距離分裂 k=2、合併遲滯 25%、**2:1 平衡含跨面**、每葉 4-bit 較粗鄰居遮罩、差異清單依 key 排序以保證各客戶端一致）、`ChunkLodSettings.ForPlanet`（深度由 R 推導：320/640/1280 → 4/5/6，最細 0.98 m）。**偏離原文**：planet-tech.md 寫「等面積修正映射」，實作採等角映射（面積比 1.38，理論 √2；嚴格等面積 QSC 在對角線有摺痕、反函數要迭代），已更新 planet-tech.md，請使用者確認。測試：接縫逐位元相同（6 面×4 邊×2049 點）、繞含兩極與面角的大圓 720 步每步檢查覆蓋/無重疊/2:1/遮罩/差異可重建/腳下為最細層（最多 444 葉）、遲滯不抖動、同路徑同結果、R=320/1280。變異探針：把 `Warp` 改成單純 `tan` 在 Mono 上**仍全過**（float tan(π/4) 剛好捨入成 1），所以 FENCE 是為 Burst 而設，需在 P1-03 以 Burst 路徑重跑接縫測試。**未做**：錄影驗收（需要 P1-03 網格）、縫合索引本身、高度邊界（`HeightMargin` 先由呼叫端給，P1-04 定）· 下一步：SONNET 審 P1-02 邏輯層；P1-03 依遮罩產生 16 種縫合索引並完成錄影驗收
