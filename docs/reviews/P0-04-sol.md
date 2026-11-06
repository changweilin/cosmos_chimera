# P0-04 審查 · ChatGPT Sol（Codex CLI 0.166, gpt-6.1-sol, read-only）· 2026-11-06

> 原始輸出：`codex exec -s read-only`，對象 commit 9820eee。未修改任何檔案。

## 結論（不通過）

審查對象：`9820eee`。**PCG 核心公式正確，但 checked 編譯會破壞合法輸入；零初始化實例可造成無限迴圈。** 現有測試不足以守住無偏取樣與生命週期契約。

全程未修改任何檔案。驗證使用記憶體內編譯；未執行 IL2CPP／Burst 建置。

## 發現（依嚴重度排序）

### 1. 高：兩處運算依賴編譯器預設的 unchecked 行為

**位置：** [Pcg32.cs:33](Assets/_Project/Core/Random/Pcg32.cs:33)、同檔第 47 行。

**問題與反例：** 將原始碼以 `/checked+` 編譯後，已實測：

```csharp
new Pcg32(ulong.MaxValue, 0); // 建構子拋 OverflowException

var rng = new Pcg32(42, 54);
rng.NextUInt();              // 拋 OverflowException
```

- 第 33 行：第一次 `Step()` 後，`_state == _increment`；加上 seed 必須允許模 \(2^{64}\) 回繞。
- 第 47 行：官方 seed 的第一次轉型前數值為 `0x30ae015bd`，超過 `uint.MaxValue`；PCG 要求截斷成低 32 位。

`Step()` 內的 `unchecked` 不會涵蓋這兩處。這取決於 **Core 的編譯設定**，並非呼叫端包一層 `checked` 就會觸發。

**建議修正：** 對 seed 加法與 `ulong → uint` 截斷各自明示 `unchecked`，並以兩種 overflow 設定編譯同一組向量測試。

### 2. 高：零初始化實例可使有界取樣永久不返回

**位置：** [Pcg32.cs:39](Assets/_Project/Core/Random/Pcg32.cs:39)、同檔第 55、64–70、77、92 行。

**問題與反例：**

```csharp
var rng = default(Pcg32);
rng.NextUInt(3); // 無限迴圈
```

推導：

1. `_state == 0`、`_increment == 0`。
2. 每次 `Step()` 後仍為零，`NextUInt()` 永遠輸出零。
3. `bound == 3` 時，`threshold == 1`。
4. 每次輸出均被拒絕，迴圈無法終止。

`new Pcg32()`、陣列元素及未初始化欄位都能產生此狀態。`NextInt(0, 3)` 同樣受影響。

`NextUInt()` 註解一方面要求 seeding constructor，另一方面稱 default 為「valid」；其他抽樣方法又沒有列出初始化前置條件。Editor／Development 也沒有檢查此生命週期條件。

**建議修正：** 統一所有抽樣 API 的初始化契約，撤除 default 為有效序列的描述，並在 Editor／Development 檢查 `_increment` 為奇數。若要支援 default，必須另行定義確定性的初始化行為。

### 3. 中：測試無法偵測拒絕取樣被移除

**位置：** [Pcg32Tests.cs:29](Assets/_Project/Core/Tests/EditMode/Pcg32Tests.cs:29)、同檔第 78–88 行。

**問題與反例：** 在記憶體中的原始碼副本，將有界取樣改為：

```csharp
return NextUInt() % bound;
```

**既有 20 個測試仍全部通過。**

硬幣測試的 `bound == 2`，threshold 為零；其餘測試只驗證範圍，因此無法辨識模數偏差或錯誤的狀態消耗次數。

可重現的拒絕案例：`seed=42, stream=54, bound=0x80000001`。

- threshold：`0x7fffffff`
- 第一個結果：`559678134`
- 第二次呼叫必須拒絕原始值 `0x7b47f409`
- 第二個結果應為：`974992175`

**建議修正：** 加入固定向量，驗證拒絕後的結果及後續原始輸出；範圍檢查不能替代此測試。

### 4. 中：可變 struct 的防呆目前只有文字提醒

**位置：** [Pcg32.cs:10](Assets/_Project/Core/Random/Pcg32.cs:10)、[Pcg32Tests.cs:125](Assets/_Project/Core/Tests/EditMode/Pcg32Tests.cs:125)。

**問題與反例：** 已實測以下兩種存放方式，每次呼叫都回傳首筆 `0xa15c02b7`：

```csharp
private readonly Pcg32 _rng = new Pcg32(42, 54);
public uint Draw() => _rng.NextUInt(); // 修改 defensive copy
```

```csharp
public Pcg32 Rng { get; set; }
uint Draw() => Rng.NextUInt();         // 修改 getter 回傳的副本
```

現有複製測試只驗證兩份副本的第一筆相同，沒有涵蓋這些無聲遺失狀態更新的情境。契約也未明確禁止同一實例的並行存取；並行呼叫可能遺失更新，結果取決於排程。

**建議修正：** 保留 Burst 所需的 struct，但補上 readonly、屬性、`in`／傳值參數的使用限制與測試；要求每個 job 獨占實例。若要 API 層防呆，可採用接收 `ref Pcg32` 的抽樣介面或 analyzer。

### 5. 中／待確認：Burst 相容性尚未建立

**位置：** [Pcg32.cs:61](Assets/_Project/Core/Random/Pcg32.cs:61)、同檔第 83 行；[Contract.cs:33](Assets/_Project/Core/Contracts/Contract.cs:33)、第 50 行。

**問題與推導：** Editor／Development 會保留 Contract 呼叫，其路徑包含 managed `string` 參數、字串串接及自訂例外。

Burst 文件限制向使用者方法傳遞 managed string，且只支援有限的例外模式；目前測試沒有編譯任何 Burst 呼叫端，因此不能由「struct 沒有 managed 欄位」推論整條呼叫鏈可編譯。另依文件，Burst Player 中拋例外會中止應用程式，不能直接承諾 managed `ContractException` 的可捕捉行為。[字串限制](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-string-support.html)、[例外限制](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-hpc-overview.html)

**建議修正：** 用執行期 bound 編譯最小 Burst job，覆蓋所有抽樣方法及 Editor／Development／Release。若失敗，再提供使用固定字面值的 Burst 相容檢查路徑。**實際編譯結果待確認。**

### 6. 低：DbC 欄位形式齊全，但仍有文件缺口

**位置：** [Contract.cs:21](Assets/_Project/Core/Contracts/Contract.cs:21)、第 38 行；[ContractTests.cs:9](Assets/_Project/Core/Tests/EditMode/ContractTests.cs:9)、[Pcg32Tests.cs:19](Assets/_Project/Core/Tests/EditMode/Pcg32Tests.cs:19)。

**問題與反例：**

- Contract 只明文要求 condition 無副作用；message 表達式也會被 Release 剝除，例如 `BuildMessageAndAdvanceState()`。
- 公開測試方法均缺少規約要求的 XML `Summary／Pre／Post／Side effects`。
- RNG 的初始化與執行緒契約缺口見發現 2、4。

**建議修正：** 明訂兩個引數表達式都不得承擔必要副作用；補齊測試方法文件，或由專案規約明定測試方法豁免。

## 已驗證無誤的項目（附你如何驗證）

- **PCG 初始化及輸出公式：** 對照 [O’Neill 參考原始碼](https://raw.githubusercontent.com/imneme/pcg-c-basic/master/pcg_basic.c)。在 unchecked 編譯下，清零、設定奇數 increment、第一次推進、加 seed、第二次推進的順序一致；建構子只呼叫 `Step()` 不影響最終狀態。stream 最高位被丟棄也符合參考行為。
- **官方向量：** 獨立 BigInt 重算結果為：

  ```text
  a15c02b7 7b47f409 ba1d3330 83d2f293 bfa4784b cbed606e
  後續硬幣：1 1 0 0 0 1 0 1 1 1
  ```

- **交叉比對：** 原始 C# 與獨立 BigInt 模型共 5,507 筆輸出的摘要一致，涵蓋極端 seed／stream、多種 bound 及跨正負整數範圍。
- **XSH-RR／旋轉：** 位移量正確；使用反推 seed 實測全部 32 種 rotation。`rot=0` 時，C# 將 uint 位移量遮罩為五位，結果正確。
- **threshold／無偏性：** `unchecked(0u - bound) % bound == 2^32 mod bound`。接受集合大小可被 bound 整除，各餘數的原像數相同，沒有額外模數偏差。
- **NextInt：** 兩處 `unchecked` 正確處理大跨度。實測 `int.MinValue..int.MaxValue` 可回傳 `int.MinValue` 與 `int.MaxValue-1`，不包含上界；逆序區間在啟用契約時會拋例外。
- **NextFloat：** 高 24 位介於 `0..2^24-1`，轉 float 與乘 \(2^{-24}\) 都可精確表示，因此保證 `[0,1)`。已實測零、最大值 `1-2^-24` 及 10,000 筆量化格點。
- **Contract 剝除：** 記憶體內分別以 `UNITY_EDITOR`、`DEVELOPMENT_BUILD`、兩者皆無編譯。前兩者執行檢查；後者連 condition 與 message 的求值都消失。兩個 Conditional 為 OR，取決於呼叫端編譯符號。[C# 文件](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/general#conditional-attribute)
- **asmdef：** 實際 Core 與 EditMode `.rsp` 均含 `UNITY_EDITOR`；測試組件另有 `UNITY_INCLUDE_TESTS`。`noEngineReferences` 只控制引擎組件參照，不移除這些符號。[Unity 文件](https://docs.unity3d.com/6000.3/Documentation/Manual/class-AssemblyDefinitionImporter.html)
- **Ensures：** 作為「立即檢查實作者算出的後置條件」語意合理；它不會自動延後到方法出口執行。
- **既有測試：** 將原始測試與核心碼在記憶體內編譯，以 NUnit 斷言執行，20/20 通過。此結果不等同 IL2CPP／Burst 驗證。

## 建議補的測試

| 契約／風險 | 應補測試 |
|---|---|
| 任意 seed／stream 合法 | `0`、`ulong.MaxValue`、stream 最高位別名；checked／unchecked 編譯矩陣 |
| 必須初始化 | default、無參數 `new`、陣列元素應立即違約；避免讓測試直接陷入無限迴圈 |
| 無界輸出／推進一次 | 強制輸出 `0`、`uint.MaxValue`；固定後續序列 |
| 有界輸出／拒絕取樣 | 發現 3 的高拒絕率向量，並驗證拒絕後狀態 |
| `min < max` | 保留相等案例，補逆序案例 |
| 整數半開區間 | 固定命中下界與上界前一值，涵蓋兩端極值及單元素區間 |
| 浮點 Post | 固定命中 `0`、`1-2^-24`；驗證 `f × 2^24` 為整數 |
| struct 狀態所有權 | readonly、屬性 getter、`in`、傳值與 `ref` 的更新差異 |
| Release 剝除 | 獨立呼叫端編譯矩陣，同時驗證兩個引數均不求值 |
| Burst／IL2CPP | 各後端比較相同序列摘要；Burst 使用執行期參數，避免常數折疊掩蓋檢查路徑 |

`condition`「無副作用」屬呼叫端規則；方法收到 bool 後無法反查其求值過程，不宜假設它能以 `ContractException` 自動偵測。