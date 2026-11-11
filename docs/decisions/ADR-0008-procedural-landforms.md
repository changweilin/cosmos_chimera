# ADR-0008：地貌、道路、建築全部程序化生成（退役真實地圖模板）

## 狀態
已採納（2026-11-11，使用者定案：「植被、地貌、道路、建築等等全部都遊戲隨機生成」；真實地圖模板「完全移除」；道路與建築做「設計＋規則層」）。
取代：ADR-0001 中「真實地圖模板以 gnomonic 局部投影貼附」的資料來源部分（gnomonic 印章機制保留）、Q-W1 方案 A、P3-01／P3-02。

## 背景
- P3-02 從 Copernicus GLO-90 烘焙了 12 塊 256×256 真實地理模板（`Data/geo-templates/`、`tools/geodata/`），P3-03 以 `PlanetTemplates` 用 gnomonic 投影把 4／6 塊拼到球面。
- 植被（`ScatterField`）、區域圖、POI、生成點、任務本來就是程序化；只有骨架層地貌依賴真實資料，道路與建築尚無生成層。
- 真實資料帶來授權署名、發行前律師確認、Q-T5 鏡像問題、81 MB 下載快取與離線烘焙流程，而 12 塊固定模板讓同類型星球的地貌重複。

## 決定
1. **骨架層改為程序化地貌印章**：`WorldGen.LandformSynth` 依地貌標籤（12 種，與 `planet-type.schema.json` 的 terrain tags 一一對應）合成 256×256 高程＋水系格網；`TemplatePlanner.Build` 依星球類型選種類與位置，每個印章各自以 `Pcg32(seed, 0x4C460000 + i)` 生成（同星球兩座火山不會相同）。
2. **沿用 `PlanetTemplates` 印章機制**（gnomonic 投影、外緣 35 % 混入噪聲、海／河下切、`IsSea`、Burst 路徑、LOD margin）。型別名稱不改，避免無謂的破壞性變更；它們現在承載的是程序化格網。
3. **合成器只用整數運算**（Q16 定點、整數雜湊值噪聲、整數開根號）。水系遮罩直接決定 `IsSea`（生成點／降落陸地判定、探針 `seaCount`），因此格網必須在 Mono／IL2CPP／其他機器逐位元相同；高度最終仍經 `TemplateView` 的浮點混合，維持 ADR-0007 的 3 mm 容差。
4. **道路與建築為純 C# 規則層** `WorldGen.SettlementPlanner` → `RunLayout`：道路是區域圖的生成樹（主路線＝Main，其餘區域以 Trail 接到 BFS 父節點）；聚落為降落點 Outpost、主路線中段 Village、BOSS 周圍 Lair、約半數支線區域 Ruins。只讀區域圖與 `isLand`，不讀高度（ADR-0007 第 3 條）。尚未接表現層。
5. 刪除 `Data/geo-templates/`、`tools/geodata/`、`Assets/_Project/Data/GeoTemplates/`、`GeoTemplateLoader`、demo 包的 `ATTRIBUTION.md`。

## 後果
- 不再有第三方地理資料：發行不需署名、Q-W1 律師風險消失；Q-T5 不再影響地貌（程序化內容沒有慣用手方向）。
- 版本：新增 `LandformSynth.GeneratorVersion = 1`、`SettlementPlanner.GeneratorVersion = 1`；repro 探針升 v6（標頭記錄 LandformSynth 版本、所有列都帶程序化印章）。v5 以前的報告不可比。
- `RunPlanner` 演算法不變（v4），但 demo 的 `isLand` 改由程序化印章提供，所以同 seed 的降落點與部分生成點會變；`isLand == null` 的快照不變。
- `RunPlanner.MinLandCapRad` 的論證仍成立：海只存在於 ≤ 6 塊半角 0.45 rad 的印章內（且合成器把水系限制在印章中心 82 % 內）。
- 已知限制：道路以球面大圓＋低頻擺動連接區域中心，未依坡度繞行；跨海段只標 `Bridge`。建築與其他聚落的道路、野生生成點之間不做碰撞避讓。

## 替代方案
- **保留模板但停用**：程式與 81 MB 資料留在 repo，持續要維護授權文件與載入路徑；使用者選擇完全移除。
- **程序化為主、模板當素材**：仍需授權與烘焙流程，只換來少數可辨識地貌；不採用。
- **浮點噪聲合成**：較簡單，但海岸線取樣可能跨後端翻轉，`seaCount` 與生成點陸地判定失去一致性；不採用。
