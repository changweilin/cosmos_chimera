> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象為 r2 修正後工作樹。未修改任何檔案。

**P2-06 第三輪唯讀複審 · SOL**  
2026-11-06 · 對象：指定的工作樹五個檔案。

**總判定：PASS。** r2 的 N1、N2、N3 及 V3 全部 **fixed**；未發現有證據的新問題。

| 驗證 | 本輪實測 |
|---|---|
| 既有測試 | **62/62 通過** |
| M01–M55 | **55 killed／0 survived** |
| E01–E04 | **4 killed／0 survived** |
| 加入六組記憶體補充檢查 | **68/68 通過** |
| 結尾重跑既有測試 | **62/62 通過** |
| 五個受審檔案 SHA-256 | 前後全部一致 |

已依要求忽略 `HitVolume.cs` 與 `CombatReviewSolTests.cs`；未修改 repo、報告、roadmap 或建立暫存檔。

**方法與限制**

已讀 `AGENTS.md`、`CLAUDE.md`、`code-contracts.md`、測試規範、前兩輪報告及 phase-2 最後交接。

沿用上一輪方法：PowerShell 5.1、CLR `4.0.30319.42000`、Roslyn 3.7、C# 8、Release 最佳化並定義 `UNITY_EDITOR`。來源與每項獨立變異均編譯至 `MemoryStream`，載入後反射執行 NUnit 3.5 的 `[Test]` 與全部 `[TestCase]`。

`TeamStateTests`、`PlayerVitalsTests` 原樣執行。`BalanceFixture` 僅於記憶體移除依賴受排除檔案的 `HitDefaults`；其餘方法保留，實際讀取 `Data/balance.json`。以最小容器提供 `Application.dataPath`，使用原始 `Contract`、`Pcg32`、`DerivedStats`、`HitResolver` 與 Newtonsoft.Json 13。

未啟動 Unity；結果不代表正式 EditMode、Unity Mono、IL2CPP 或其他後端驗證。編譯器相依套件使用 managed assembly 相容性解析。集合檢查涵蓋正常 managed 公開介面，不包含反射讀寫私有欄位、unsafe 或記憶體破壞。補充檢查不構成全狀態空間證明。

**r2 逐項判定**

以下檔案均位於 `Assets/_Project/Features/Combat/`。

| ID | 判定 | 檔案：行號與證據 |
|---|---|---|
| N1 | **fixed** | [Team.cs:206](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:206) 將重複檢查與 Roster 資格檢查分成兩輪；`:208` 先檢查唯一性，`:216` 才檢查資格。`SetPool([99,99])` 實測回 `InvalidPool`。[TeamStateTests.cs:348](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/TeamStateTests.cs:348) 已固定此反例；補充複合條件檢查亦通過。 |
| N2 | **fixed** | [PlayerVitals.cs:41](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/PlayerVitals.cs:41) getter 改為 `Pre: none`，Post 明訂有效建構值至少 1、default 值為 0，與 auto-property 行為一致。[PlayerVitalsTests.cs:193](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/PlayerVitalsTests.cs:193) 驗證 default getter 回 0，已無未執行的 getter Pre。 |
| N3 | **fixed** | [TeamStateTests.cs:328](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/TeamStateTests.cs:328) 與 [PlayerVitalsTests.cs:141](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/PlayerVitalsTests.cs:141) 涵蓋八個時間入口的 `−1`、`MaxMs+1`、`long.MinValue`；並驗證拒絕後水位未污染。Team tests `:340` 直接驗證 `CombatTime.Require` 兩端與倒退拒絕。E01–E04 全部被新增測試殺死。 |
| V3 | **fixed** | `PlayerVitals.cs:41` 的 Post 現在涵蓋 struct default 行為；`:79` 仍拒絕以 default config 建立玩家。Player tests `:193` 驗證 getter，`:32` 驗證玩家建構拒絕；M38 仍為 killed。 |

**M01–M55 變異結果**

每項獨立套用於記憶體來源，執行完整 **62 個既有案例**；59 項變異均成功編譯，均因測試失敗而 killed，未將編譯失敗計為 kill。

沿用 r2 的等價對應：

- M21／M22：已開始冷卻時，錯誤地讓 `elapsed == duration` 仍受阻擋。
- M33：重現絕對結束時間窄化為 int：

```csharp
long elapsed = nowMs + _switchCooldownMs
    - unchecked((int)(_cooldownStartMs + _switchCooldownMs));
```

| ID | 單點變異 | 結果 |
|---|---|---|
| M01 | MaxRoster：6→5 | killed |
| M02 | MaxPool：4→3 | killed |
| M03 | MaxActive：2→3 | killed |
| M04 | 移除 roster null 檢查 | killed |
| M05 | 移除 roster 唯一性檢查 | killed |
| M06 | SetPool 忽略戰鬥鎖定 | killed |
| M07 | SetPool 忽略重複成員 | killed |
| M08 | SetPool 忽略 Roster 資格 | killed |
| M09 | SetPool 允許排除 Active | killed |
| M10 | SetPool 不清除舊池 | killed |
| M11 | BeginCombat 不設戰鬥旗標 | killed |
| M12 | EndCombat 不清除退休集合 | killed |
| M13 | Summon 忽略場上數量上限 | killed |
| M14 | Summon 改插入 Active 首位 | killed |
| M15 | Summon 也開始冷卻 | killed |
| M16 | Recall 不移除 Active | killed |
| M17 | Recall 不開始冷卻 | killed |
| M18 | Switch 改為移除後附加，失去原位置 | killed |
| M19 | Switch 不開始冷卻 | killed |
| M20 | 允許退休成員重新進場 | killed |
| M21 | 進場在冷卻結束當下仍被阻擋 | killed |
| M22 | Recall 在冷卻結束當下仍被阻擋 | killed |
| M23 | Faint 不移除 Active | killed |
| M24 | Faint 不加入退休集合 | killed |
| M25 | Faint 清除正在進行的冷卻 | killed |
| M26 | Faint 開始冷卻 | killed |
| M27 | 冷卻多加 1 ms | killed |
| M28 | Team 命令不更新時間水位 | killed |
| M29 | EndCombat 額外清除冷卻 | killed |
| M30 | EndCombat 額外清空 Active | killed |
| M31 | BeginCombat 額外清除冷卻 | killed |
| M32 | Switch 的 NotActive 分支不推進時間 | killed |
| M33 | 冷卻結束時間窄化為 int，等價變異 | killed |
| M34 | 移除 maxHp 下限檢查 | killed |
| M35 | 移除負無敵長度檢查 | killed |
| M36 | 移除無敵長度 ≤ 動畫長度檢查 | killed |
| M37 | 移除動畫長度 ≤ 冷卻檢查 | killed |
| M38 | 允許 default config 建立玩家 | killed |
| M39 | 初始 HP 少 1 | killed |
| M40 | IsDowned 固定 false | killed |
| M41 | TryRoll 忽略 Downed | killed |
| M42 | 翻滾冷卻比較 `<`→`<=` | killed |
| M43 | 翻滾冷卻誤用動畫長度 | killed |
| M44 | 無敵／動畫視窗改成閉區間 | killed |
| M45 | 無敵視窗誤用動畫長度 | killed |
| M46 | 動畫視窗誤用無敵長度 | killed |
| M47 | ApplyDamage 不檢查無敵 | killed |
| M48 | 傷害不限制至剩餘 HP | killed |
| M49 | 移除負傷害檢查 | killed |
| M50 | Player 命令不更新時間水位 | killed |
| M51 | 零冷卻時拒絕後續翻滾 | killed |
| M52 | 零長度視窗誤給 1 ms | killed |
| M53 | Downed 且仍有冷卻時回 OnCooldown | killed |
| M54 | 翻滾冷卻 elapsed 窄化為 int | killed |
| M55 | 無敵／動畫 elapsed 窄化為 int | killed |

| ID | 補充變異 | 結果與直接證據 |
|---|---|---|
| E01 | Require 移除 `nowMs >= 0` 下界 | **killed**；Team 的 N3 參數化測試在 `−1`、`long.MinValue` 失敗，共 60 pass／2 fail。 |
| E02 | Recall 保留水位檢查，移除時間域檢查 | **killed**；Team N3 的 `MaxMs+1` 案例失敗，61 pass／1 fail。 |
| E03 | Switch 第一個檢查只保留水位檢查 | **killed**；outId 不在場上的 `MaxMs+1` 案例失敗，61 pass／1 fail。 |
| E04 | IsRolling 對超過 MaxMs 的查詢直接回 false | **killed**；Player N3 的 `MaxMs+1` 案例失敗，61 pass／1 fail。 |

**修正本身的對抗式檢查**

六組補充檢查僅存在於記憶體；加上既有測試，結果為 **68/68 通過**。

| 檢查 | 實測與證據 |
|---|---|
| ReadOnlyCollection 公開修改途徑 | 三個 view 的泛型與非泛型 `IList`：Add、Clear、Insert、Remove、RemoveAt、索引 setter 全拋 `NotSupportedException`。建構輸入、SetPool 輸入及兩種 CopyTo 副本的修改均不影響隊伍；SyncRoot 不是 IList，列舉內容為 int 值。未發現公開介面洩漏底層 List。wrapper 建立於 `Team.cs:119`，getter 位於 `:127`、`:131`、`:135`。 |
| CombatTime 的 0 與 MaxMs | 八個時間入口均接受兩端。`long.MinValue`、−1、MaxMs+1、long.MaxValue 均被拒絕；各入口單獨拒絕後，合法時間 0 仍可使用。域檢查位於 `Team.cs:54`。 |
| Team cooldown elapsed | 時長 0、1、3000、int.MaxValue；開始時間 0、MaxMs−時長、MaxMs；驗證同 tick、到期前 1 ms、精確到期與合法範圍內的到期後時間。剩餘量與進場結果均正確。減法及剩餘量計算位於 `Team.cs:394`。 |
| Player elapsed／半開視窗 | 全零、600／400／800、全 int.MaxValue 三組設定；驗證時間兩端、各視窗邊界及 `2^32` 間隔。動畫、無敵、傷害與再次翻滾結果均正確。計算位於 `PlayerVitals.cs:111`、`:162`。 |
| 水位契約 | Team／Player 查詢 MaxMs 後，仍接受未低於命令水位的較早命令。成功與拒絕命令均推進水位；涵蓋未知成員、非池成員、已在場、退休、滿場、冷卻、非 Active、Downed、零傷害及無敵中傷害。下一次低於命令水位的命令與查詢均被拒絕。與 `Team.cs:67`、`PlayerVitals.cs:65` 的契約一致。 |
| SetPool 複合拒絕順序 | 枚舉值域 `{−1,0,1,2,3,99}`、長度 0–5，共 **9,331 組輸入**；驗證數量→唯一性→Roster→Active 的順序、拒絕時保留集合，以及戰鬥鎖定優先。全部通過。 |

合法時間差位於 `0..2^62`，因此目前 elapsed 減法不會溢位。於 MaxMs 才開始的非零冷卻仍保留完整剩餘量，沒有在域尾端提前解鎖。

**Pre 對應測試核對**

下表行號均指既有測試，未把記憶體補充檢查當成 repo 已有覆蓋。

| 公開 API／條件 | 對應違反案例 |
|---|---|
| TeamState 建構：null、數量上下界、唯一性、非負 id、非負冷卻 | `TeamStateTests.cs:39` |
| SetPool：非 null | `TeamStateTests.cs:99` |
| BeginCombat：尚未戰鬥、已選非空池 | `TeamStateTests.cs:105`、`:384` |
| EndCombat：正在戰鬥 | `TeamStateTests.cs:105` |
| Faint：正在戰鬥、id 為 Active | `TeamStateTests.cs:232` |
| Team.Summon：時間域／水位 | `TeamStateTests.cs:328`／`:261` |
| Team.Recall：時間域／水位 | `TeamStateTests.cs:328`／`:261` |
| Team.Switch：時間域／水位 | `TeamStateTests.cs:328`／`:261` |
| Team.SwitchCooldownRemainingMs：時間域／水位 | `TeamStateTests.cs:328`／`:261` |
| CombatTime.Require：域與水位檢查的 Post | `TeamStateTests.cs:328`、`:340`；其宣告為 `Pre: none` |
| PlayerVitalsConfig：maxHp 與三項視窗不等式 | `PlayerVitalsTests.cs:22` |
| PlayerVitals：config 經有效建構 | `PlayerVitalsTests.cs:32` |
| Player.TryRoll：時間域／水位 | `PlayerVitalsTests.cs:141`／`:104` |
| Player.ApplyDamage：非負傷害、時間域、水位 | `PlayerVitalsTests.cs:104`、`:141` |
| Player.IsRolling：時間域／水位 | `PlayerVitalsTests.cs:141`／`:104` |
| Player.IsInvulnerable：時間域／水位 | `PlayerVitalsTests.cs:141`／`:104` |

其餘 getter、常數及查詢宣告 `Pre: none`，無需違反案例。本輪未找到缺少對應違反測試的公開 Pre。

**新發現**

**無。** 未建立 P1… 問題項目；本輪沒有可重現的新缺陷或尚未修復的 r2 問題。