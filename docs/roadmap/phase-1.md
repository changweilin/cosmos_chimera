# P1 星球垂直切片

**出口條件**：在 R=640 m 程序化星球上以第三/第一人稱行走、跳躍、繞星球一圈回到原點；LOD 無裂縫；卡通渲染達到目標畫風雛形；60 fps @ 目標 PC，並每階段在 Steam Deck 實機量一次（目標 30 fps 以上）。
讀：`../architecture/planet-tech.md`、`../decisions/ADR-0001-cube-sphere.md`

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P1-01 | `PlanetConfig`、`PlanetFrame`（up/east/north）、方向↔座標轉換 | OPUS | SOL | 單元測試：任一點三軸正交且 up 指離球心 | 🟦 |
| P1-02 | Cube-sphere 四叉樹 + 等面積映射 + LOD 分裂/合併 | OPUS | SONNET | 繞行一圈無裂縫、無跳變（錄影檢查） | 🟦 |
| P1-03 | 區塊網格生成（Jobs + Burst）、附近區塊 MeshCollider | SONNET | OPUS | 主執行緒每幀 < 2 ms；碰撞只存在 ~128 m 內 | 🟨 |
| P1-04 | 高度函數 `height(dir)`：多層 3D 噪聲（暫不接真實地圖） | SONNET | SOL | 快照測試；同 seed 同結果 | 🟨 |
| P1-05 | 球面角色控制器（Rigidbody 自訂重力、坡度、跳躍）+ Cinemachine 第三/第一人稱 | SONNET | OPUS | 在極點與面交界處行走無抖動、無翻轉 | ⬜ |
| P1-06 | 導航原型：局部 NavMesh vs steering（回答 Q-T1） | OPUS | SONNET | 比較報告 + 決定寫入 planet-tech.md | ⬜ |
| P1-07 | URP 卡通著色（地形/角色）、角色反向外殼描邊、大氣霧 | SONNET | 人工 | 美術比對參考截圖通過 | ⬜ |
| P1-08 | 天空：日夜循環 + 星海 skybox（與大廳共用） | SONNET | 人工 | 日夜轉換無接縫 | ⬜ |
| P1-09 | 草/植被 GPU instancing + 風場 | SONNET | OPUS | 10 萬株草 60 fps | ⬜ |
| P1-10 | 不同 R（320/640/1280）參數切換測試 | SONNET | — | 三種尺寸皆可繞行，地平線符合公式 | ⬜ |

## 交接紀錄
- 2026-11-06 · Claude Opus · done · P1-01：`Planet/Api` 新增 `PlanetConfig`（Radius 唯一來源、Center、`HorizonDistance`、`ToWorld`/`ToDirection`/`Altitude`）、`PlanetFrame`（East/North/Up 正交右手系，`At`/`FromUp`/`LocalToWorld`/`WorldToLocal`/`ProjectOnTangent`；極點 East 退回 (0,0,−1)）、`LatLon`（atan2 版，極點不失精度）。幾何改用 `Unity.Mathematics` 的 `float3`，見 ADR-0005（**提議中，待使用者確認**；manifest 釘選 1.3.3，lock 檔原本就解析為此版，無新下載）。驗證：EditMode 72/72（新增 31 項 Planet 測試），0 個警告；2000 個隨機點 + 極點/角落/近極點皆正交，地平線與 planet-tech.md 表一致 · 下一步：SOL 審查 P1-01
- 2026-11-06 · Claude Opus · partial · P1-02（邏輯層）：`CubeSphere`（等角 tan 映射 + 反函數、6 面軸表、由軸表推導的跨面鄰接表）、`ChunkId`（64-bit key、父子、同級鄰居含跨面）、`Runtime/Model/ChunkQuadtree`（距離分裂 k=2、合併遲滯 25%、**2:1 平衡含跨面**、每葉 4-bit 較粗鄰居遮罩、差異清單依 key 排序以保證各客戶端一致）、`ChunkLodSettings.ForPlanet`（深度由 R 推導：320/640/1280 → 4/5/6，最細 0.98 m）。**偏離原文**：planet-tech.md 寫「等面積修正映射」，實作採等角映射（面積比 1.38，理論 √2；嚴格等面積 QSC 在對角線有摺痕、反函數要迭代），已更新 planet-tech.md，請使用者確認。測試：接縫逐位元相同（6 面×4 邊×2049 點）、繞含兩極與面角的大圓 720 步每步檢查覆蓋/無重疊/2:1/遮罩/差異可重建/腳下為最細層（最多 444 葉）、遲滯不抖動、同路徑同結果、R=320/1280。變異探針：把 `Warp` 改成單純 `tan` 在 Mono 上**仍全過**（float tan(π/4) 剛好捨入成 1），所以 FENCE 是為 Burst 而設，需在 P1-03 以 Burst 路徑重跑接縫測試。**未做**：錄影驗收（需要 P1-03 網格）、縫合索引本身、高度邊界（`HeightMargin` 先由呼叫端給，P1-04 定）· 下一步：SONNET 審 P1-02 邏輯層；P1-03 依遮罩產生 16 種縫合索引並完成錄影驗收
- 2026-11-06 · ChatGPT Sol（Codex CLI，唯讀，對象 `431edb5`）· done · P1-01/P1-02 邏輯層審查**不通過**：F1（高）`ProjectOnTangent` 用絕對閾值判斷退化，`Up*100` 會回傳幾乎沿 Up 的向量；F2/F3 輸入容差與數值範圍契約不一致（近單位 up、巨大 Center、下溢）；F4 `ulong` key 的字典雜湊系統性碰撞（D=8 時 Update 約 1 s）；F5 分裂決策只在同後端可重現；F6/F7 DbC 缺口、`Warp` 非嚴格遞增、反子午線 ±0。跨面鄰接、接縫（2,516 萬對位元比較）、平衡收斂、遲滯都有通過的探針。8 種變異仍能讓現有測試全過。全文：`docs/reviews/P1-01-P1-02-sol.md` · 下一步：OPUS 修 F1–F3、F5–F7 並補上存活變異的測試；F4 先換掉 hash comparer
- 2026-11-06 · Claude Opus · done · P1-04：`Planet/Api/PlanetHeight`（6 octave 的 3D simplex fBm，只依方向取樣；offset 由 `Pcg32(seed, 'H')` 推導；振幅 0.03R；`MaxAbsHeight` 是幾何級數上界並以 clamp 保證；`GeneratorVersion = 1`）。快照 `Tests/Snapshots/planet-height-v1.txt`（4096 個樣本的 FNV-1a）。數值是 P1 的佔位，不是美術定案 · 下一步：SOL 審查；P3 疊加真實地圖模板
- 2026-11-06 · Claude Opus · done · P1-03：Burst/Collections 改為明確釘選（ADR-0006，**提議中**；沒有新下載）。`CubeSphere` 抽出 `FaceToDirectionCore`/`WarpCore`（不碰靜態陣列、不呼叫 Contract），公開 API 與 Burst 共用同一份實作，FENCE 保留。`ChunkStitching`：16 種縫合索引，做法是把粗邊上的奇數頂點收合到相鄰偶數頂點（只改索引，沒有 T 形接點）。`ChunkMeshJob`：每批一個 `IJobParallelFor`，直接寫入 `Mesh.MeshDataArray`。`PlanetTerrain`：quadtree 差異 → 背景 job → 整批一次套用（不會出現半套的葉集合），碰撞用 `Physics.BakeMesh` 在背景烘焙，只給 128 m 內的葉。驗證：EditMode 94/94、PlayMode 2/2、0 個警告；**FENCE 驗收**：Burst（Default/Standard、x64、動態 face）下 6 面×4 邊×2049 點的接縫逐位元相同，跨面與 2:1 鄰居的共用頂點也逐位元相同；Burst 與 Mono 的頂點差 0.53 mm（ADR-0006 已註明不要求跨後端一致）；繞行 150 m 時主執行緒 p95 0.94 ms、最大 1.53 ms，最遠碰撞 128.0 m。**限制**：時間是 `-nographics` batchmode 量的（沒有 GPU 上傳），不是目標 PC 的 profiler 數字；沒有測 Fast 模式和 ARM64；沒有錄影、場景與材質（需要 P1-05/P1-07） · 下一步：SONNET 審 P1-03；用 Unity Profiler 在 PC 和 Steam Deck 量一次
