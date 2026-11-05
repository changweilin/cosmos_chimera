---
paths:
  - "Assets/**/*.cs"
---
# Unity / C# 規則

- 規則層（戰鬥計算、屬性、進化、生成）= 純 C# `sealed class` / `struct`，放在 feature 的 `Runtime/Model/`；`MonoBehaviour` 只做輸入、表現與膠水。
- 不在 `Update` 中配置記憶體（LINQ、閉包、`new List`）；熱路徑用預先配置的 buffer。
- 序列化欄位：`[SerializeField] private`，不用 public 欄位。
- 不用 `FindObjectOfType` / `GameObject.Find` 做執行期查找；以組合根（`Bootstrap` scene）注入依賴。
- 物理：自訂重力（朝星球中心）。Rigidbody `useGravity = false`，由 `PlanetGravity` 施力。
- 亂數：生成邏輯用 `Core/Random/Pcg32`（可重現），表現層特效才可用 `UnityEngine.Random`。
- 非同步：Unity 6 `Awaitable` 優先；不要混用 coroutine 與 async 處理同一流程。
- 新套件引入需寫 ADR（`docs/decisions/`），不要直接改 `Packages/manifest.json`。
- 格式與命名交給 `.editorconfig` + Roslyn analyzer（P0-03），不要在此重複。
