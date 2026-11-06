# P0-04 第二輪複審 · ChatGPT Sol（Codex CLI）

日期：2026-11-06。前次：[P0-04-sol.md](P0-04-sol.md)，對象 `9820eee`；本輪修正對象 **`ecc804f64194c41fd0e3385f6d7fe5b41d7b32fa`**。

## 結論：#1–#3 通過

三項修正與新增測試均有效。此結論依專案既定 DbC 模型：Editor／Development 檢查違約，Release 剝除檢查，呼叫者必須遵守初始化前置條件。**Release 的零初始化實例仍可能無限迴圈**；本輪通過不代表任何輸入、任何後端都保證返回。#4–#6 仍未結案。

只讀取 `git show ecc804f`、`git diff 9820eee ecc804f -- Assets/_Project/Core/` 與相關規約；未改任何 `.cs`、`Assets/`、`Data/` 或 `tools/`。編譯輸入由 `git show <commit>:<path>` 擷取，避免以工作目錄的其他變動代替受審 commit。

## #1：明示 unchecked — 通過

證據：[Pcg32.cs](../../Assets/_Project/Core/Random/Pcg32.cs) 建構子改為 `_state = unchecked(_state + seed)`，輸出截斷改為 `unchecked((uint)(((old >> 18) ^ old) >> 27))`。原有 `Step()`、有界 threshold 與 `NextInt` 的模數／跨正負範圍運算也保有 `unchecked`。

在相同編譯器、執行環境下重現：

| 輸入／設定 | `9820eee` | `ecc804f` |
|---|---|---|
| `/checked+`：`new Pcg32(ulong.MaxValue, 0)` | `OverflowException` | 正常建構；後續跨正負 `NextInt` 探針通過。 |
| `/checked+`：`new Pcg32(42, 54).NextUInt()` | `OverflowException` | 官方首筆 `0xa15c02b7`；完整六筆向量通過。 |
| checked／unchecked，Editor／Development | 未把原版整組測試當成本輪結論 | 修正版每一組 Core 測試均 25/25。 |
| checked／unchecked，無兩個條件符號（Release） | 未重跑原版 | 合法輸入探針均通過。 |

新增 `ExtremeSeeds_MatchReferenceModel` 覆蓋 `(ulong.MaxValue, ulong.MaxValue)` 與 `(0, 0)` 的前三筆輸出。Release 探針另檢查官方六筆向量、極端 seed、有界拒絕向量與跨 `int.MinValue..int.MaxValue` 的 1,000 次取樣；沒有因 `/checked+` 拋例外。

尚有限制：checked／unchecked 編譯矩陣仍是本輪暫存探針，沒有成為 repository 常駐測試。一般 Unity EditMode CI 無法自行守住不同 Core 編譯選項。

## #2：零初始化立即違約 — 通過（Editor／Development）

證據：`NextUInt()` 在讀取／推進狀態前執行：

```csharp
Contract.Requires((_increment & 1UL) == 1UL,
    "Pcg32: instance not seeded (default(Pcg32) is invalid)");
```

所有合法建構子都把 increment 設為奇數；零初始化的 increment 為 0。有界 `NextUInt(bound)`、`NextInt`、`NextFloat` 都會走進這個檢查。型別與四種取樣 API 的 Pre 已統一要求 seeding constructor，原先「default 為有效固定序列」的描述已移除。

`UnseededInstance_ThrowsContractExceptionInsteadOfLooping` 已實際執行：default 實例的四種取樣與零初始化陣列元素均拋 `ContractException`，Editor／Development × checked／unchecked 都通過。`NextUInt(3)` 在第一個原始取樣就被擋下，沒有陷入拒絕迴圈。逆序整數區間新增測試也通過。

**仍存在的 Release 限制：** 當 `UNITY_EDITOR`／`DEVELOPMENT_BUILD` 都不存在時，`Contract.Requires` 呼叫會被剝除。`default(Pcg32).NextUInt(3)` 的狀態保持 0、threshold 為 1，依程式推導仍不終止；本輪未在 Release 啟動此無界迴圈。這已在型別契約標為未定義使用，符合當前專案規約；若產品需要 Release 也保證 fail-fast，需另行決定保留初始化檢查或重新設計 API，不能宣稱本修正已提供此保證。

## #3：拒絕取樣回歸測試 — 通過

證據：[Pcg32Tests.cs](../../Assets/_Project/Core/Tests/EditMode/Pcg32Tests.cs) 新增 `NextUIntBounded_RejectsBelowThreshold_MatchesReferenceModel`，以 `(seed=42, stream=54, bound=0x80000001)` 斷言三筆結果：

```text
559678134, 974992175, 64156306
下一筆無界結果：0xbfa4784b
```

threshold 為 `0x7fffffff`；第二筆原始值 `0x7b47f409` 必須被拒絕。三筆有界結果共消耗四筆原始輸出，後續狀態斷言也能守住拒絕時的推進次數。

在暫存原始碼中，把整段 threshold／while 改成 `return NextUInt() % bound;`，保留前置條件。以 `/checked+`、`UNITY_EDITOR` 編譯並執行相同 Core 測試：**24 通過、1 失敗**，失敗者正是新增的拒絕測試：

```text
FAIL NextUIntBounded_RejectsBelowThreshold_MatchesReferenceModel
bounded #1: Expected 974992175; But was 2068313097
```

本輪確認新測試會殺死前次漏掉的 `% bound` 變異，不再只是檢查結果落在範圍內。

## 驗證方法與範圍

- .NET SDK 9.0.304 的 Roslyn `csc.dll`，以 `/checked+` 或 `/checked-` 編譯受審 Core 三個來源檔與原有兩個測試檔；引用 Unity 附的 Mono 4.8 API 與 `com.unity.ext.nunit` 的真實 `nunit.framework.dll`。
- Unity 6000.3.28f1 附的 Mono 執行暫存程式；小型 reflection runner 展開 `[Test]`／`[TestCase]` 並使用 NUnit 原斷言。兩個測試類別沒有需要另外套用的 setup／teardown。
- 分別定義 `UNITY_EDITOR`、`DEVELOPMENT_BUILD`，各跑 checked／unchecked，四組均 **25/25**。這是 Core 的測例數；先前交接的 Unity **28/28** 另含 3 個 Architecture 測試，本輪未執行它們。
- Release 無條件符號只執行合法輸入探針，沒有把依賴啟用契約的例外測試當成 Release 測試。
- 原版 checked 反例與 `% bound` 變異均按預期失敗；暫存來源、程式與複製的 NUnit DLL 透過 `TemporaryDirectory` 清除，repository 沒有留下探針。
- **未執行 Unity Editor／GameCI、IL2CPP 或 Burst 編譯。** Mono 下的 NUnit 斷言結果不能推論這些後端已通過。

## 未結案的 #4–#6 與建議優先順序

建議 **#4 → #5 → #6**；若下一項工作要立即使用 Burst，#5 應提升為該工作開始前的必要驗證。

| 順序 | 項目／現況 | 建議與完成條件 |
|---|---|---|
| 1 | **#4 可變 struct 狀態所有權（中）**：本次只統一初始化契約，沒有處理 readonly defensive copy、屬性 getter、`in`／傳值及並行存取風險；原有複製測試仍只比較首筆相同。 | 在首個 RNG 呼叫端落地前補使用限制、傳值與 `ref` 差異測試；明訂每個 job／執行緒獨占狀態。先以可寫欄位／區域變數及 `ref` 規範降低無聲重複序列風險，再評估 analyzer／API 防呆。 |
| 2 | **#5 Burst 相容性（中／待驗證）**：`Contract` 的 managed string／自訂例外路徑沒有更動；本次新增的初始化檢查也進入同一路徑。 | 用執行期 bound 的最小 Burst job 覆蓋所有取樣 API，分別驗證 Editor／Development／Release。實測前只能稱型別無 managed 欄位，不能宣稱整條呼叫鏈可 Burst 編譯；本輪沒有新證據可將此項關閉。 |
| 3 | **#6 DbC 文件缺口（低）**：初始化文件已隨 #2 補齊，但執行緒限制、message 表達式副作用規範與 public 測試方法 XML 契約仍缺。 | 明訂 condition 與 message 兩個表達式都不得承擔必要副作用；補測試方法文件，或由規約明定豁免。不可因只剝除「檢查」而假設 message 仍會求值。 |

本輪沒有發現 #1–#3 修正造成新的合法輸入回歸；剩餘風險與後端驗證需在主 session 交接時保留。
