# ADR-0005 · 星球幾何使用 Unity.Mathematics

## 狀態
提議（2026-11-06，Claude Opus 於 P1-01/P1-02 實作；待使用者確認）

## 背景
P1-01/P1-02 是純 C# 的球面數學（`PlanetFrame`、cube-sphere 映射、四叉樹 LOD）。P1-03 要把同一套映射放進 Jobs + Burst 產生網格。若現在用 `UnityEngine.Vector3`，P1-03 必須重寫一份 Burst 版本，兩份實作的浮點結果只要差 1 ulp，面與面的接縫就會出現細縫（見 `CubeSphere` 的接縫精確性契約）。

## 決定
- `Cosmimera.Planet`（與其測試）以名稱引用 `Unity.Mathematics`，幾何 API 一律用 `float3`。
- 在 `Packages/manifest.json` 明確釘選 `com.unity.mathematics` **1.3.3**。它原本就是 Burst / Collections 的遞移依賴，lock 檔已解析為 1.3.3，所以**沒有新增下載**。
- Burst 與 Collections 等 P1-03 真正使用 Jobs 時再另立 ADR（需要決定 `FloatMode` / `FloatPrecision`）。

## 後果
- 執行期與 Burst 共用同一份幾何函式，接縫精確性只需在一處保證。
- `Vector3` ↔ `float3` 有隱式轉換，MonoBehaviour 膠水層不受影響。
- 規則「禁止用 `Vector3.up` 表示上方」同樣適用 `math.up()`，已在 `PlanetFrame` 註明。

## 替代方案
- **`UnityEngine.Vector3`**：Burst 可用，但數學函式（`Mathf`）不是 Burst intrinsic，且 P1-03 會出現第二份實作。
- **`System.Numerics.Vector3`**：Burst 不支援。
- **自寫 double3**：精度不需要（`planet-tech.md#浮點精度`），且無法直接進 Burst 網格管線。
