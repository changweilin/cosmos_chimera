# ADR-0006 · 區塊網格使用 Burst + Collections

## 狀態
提議（2026-11-06，Claude Opus 於 P1-03/P1-04 實作；待使用者確認）

## 背景
P1-03 要求區塊網格在背景執行緒生成，主執行緒每幀 < 2 ms。ADR-0005 已讓幾何 API 使用 `float3`，並把 Burst/Collections 的決定延後到這一項。

## 決定
- 在 `Packages/manifest.json` 明確釘選 `com.unity.burst` **1.8.31**、`com.unity.collections` **2.7.1**。兩者原本就是遞移依賴（lock 檔已解析為這兩個版本，`Library/PackageCache` 已有），所以**沒有新增下載**。
- `Cosmimera.Planet` 以名稱引用 `Unity.Burst`、`Unity.Collections`。
- 網格 job 使用 Burst 預設 `FloatMode.Default` / `FloatPrecision.Standard`。**不**要求 Burst 與 Mono 的結果逐位元相同：
  - 接縫精確性只依賴「兩個面用同一份程式、同一組輸入」——同一個執行環境內必然一致（`CubeSphere` FENCE；`ChunkMeshTests.*_burst` 以 Burst 路徑重跑接縫測試，2026-11-06 通過）。
  - 跨客戶端的可重現性（`GeneratorVersion`）是針對**內容決定**（區塊 id、生成物位置、生物群系），那些在純 C# 層用整數或基本浮點運算完成；網格頂點在不同後端或 CPU 上可能差到約 1 mm（實測 Burst vs Mono 0.53 mm，可能來自 Burst 把乘加合併成 FMA），不影響遊戲。
- Burst 內不呼叫 `Contract.Requires`（字串串接與受控例外無法進 Burst）。公開 API 先檢查契約，再呼叫同一份 `internal ...Core` 實作；job 的輸入在主執行緒排程時檢查。

## 後果
- 網格計算與執行期查詢共用一份數學（`CubeSphere.FaceToDirectionCore`、`PlanetHeight.SampleCore`）。
- 若日後伺服器權威模擬需要頂點級逐位元一致（例如網格碰撞判定在伺服器重算），需改為 `FloatMode.Deterministic` 或改用高度函數直接查詢，而不是網格。

## 替代方案
- **不用 Burst，只用 Jobs**：Mono 版本沒有 SIMD 與內聯最佳化（未實測倍數），33×33 頂點 × 5 次取樣 × 6 octave 的區塊在 Mono 上無法於數幀內完成初始載入。
- **Compute shader 生成**：碰撞需要 CPU 端網格，資料要回讀；且 EditMode 測試困難。
