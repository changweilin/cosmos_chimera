# Comment & Architecture Rules（永遠適用）

## 1. Design by Contract — 所有 public 介面
每個 `public` / `protected` 型別成員（以及 feature `Api/` 內所有型別）必須在 XML doc 中寫明：
（`Tests/` 下的 test fixture 與 `[Test]` 方法豁免：它們的名稱就是規格。）
（`enum` **成員**豁免逐一寫 Pre/Post/Side effects：由 enum 型別本身的契約涵蓋；成員只在語意非自明時加 `<summary>`。）

| 標籤 | 內容 |
|---|---|
| `<summary>` | 一句話：這個成員**承諾**什麼（不是怎麼做） |
| `Pre:` | 呼叫者必須保證的條件（參數範圍、執行緒、生命週期階段） |
| `Post:` | 回傳後保證成立的條件 |
| `Side effects:` | 修改的狀態、事件、配置記憶體、I/O；沒有就寫 `none` |

```csharp
/// <summary>Computes damage dealt by <paramref name="move"/> to <paramref name="target"/>.</summary>
/// <remarks>
/// Pre: move.Power >= 0; attacker and target are alive (Hp > 0).
/// Post: result >= 0; result == 0 iff move has no damage component or target is immune (resist >= 100%).
/// Side effects: none (pure). Caller applies the result.
/// </remarks>
public static int ComputeDamage(in CombatantStats attacker, in CombatantStats target, MoveDef move)
```

- 前置條件在 Editor / Development build 以 `Contract.Requires(...)`（`Core/Contracts`，P0-04 建立）檢查；Release build 剝除。不要用 `Debug.Assert` 取代，因為它不會在測試中失敗。
- 契約改變 = 破壞性變更：同步更新呼叫端與測試，並在交接紀錄註明。

## 2. Why, Not What — 行內註解
- 只寫**為何**：非顯而易見的取捨、數學推導、平台怪癖、效能理由、需求出處。
- 禁止複述語法：`// increment i`、`// loop over creatures`、`// constructor` 一律不准。
- 需求來源標記：`// REQ: docs/design/creatures.md#副手` — 讓日後知道規則不是隨意寫的。

```csharp
// ✗ 取得 up 向量
// ✓ Normalize after subtracting center: chunk-local positions drift up to ~1e-4 at R=1280 m,
//   and an unnormalized up tilts the character controller enough to trigger slope sliding.
```

## 3. Chesterton's Fence — 不拆看不懂的圍籬
- 看起來多餘、怪異或「可以更乾淨」的程式碼，若涉及**硬體、效能、引擎 bug 或舊版相容**，未經明確指示不得重構或刪除。
- 寫這類 workaround 的人必須標記：
  ```csharp
  // FENCE: <為何存在> · <如何驗證它仍需要> · <參考：issue/ADR/連結>
  ```
- 發現未標記但疑似圍籬的程式碼：保持原樣，在交接紀錄中提出，由人決定。
