# P3-25 — SOL 對抗式審查

2026-11-15 · ChatGPT Sol（SOL）· 基準 `ef5b532123900cd13ee4472f8c46b88f42edccd7`，主要變更 `ccd0310`，含後續場景網格修正。範圍：`ScatterBiomes`、`ScatterPlanner`、`TemplatePlanner.ScatterBiome`、`RunSession` 接線、`ScatterField` 的選種與碰撞、相關測試。未修改核心程式。

**結論：pass with debt；high 0 / med 2 / low 0。** 依 `docs/ai-agents.md`，med 不阻擋結案；以下交原主責修正。實景美術、風擺、夜間發光與 Steam Deck 效能不在本次程式審查的驗收範圍。

## 發現

| ID | 嚴重度 | 位置 | 問題與證據 | 建議修正 |
|---|---|---|---|---|
| P3-25-S1 | med | `Assets/_Project/Features/WorldGen/Runtime/Model/ScatterPlanner.cs:695–701`；`Assets/_Project/Features/WorldGen/Tests/EditMode/ScatterPlannerTests.cs:284–285` | **人工林規格只套用樹位，直接列在地貌表的樹木與倒木繞過限制。** `planet-world.md#林型` 要求整個林班單一樹種、尺寸集中、無枯木倒木；目前只有 `TreeSlot` 進 `PickTree`，例如 forest 表的 `log`、grassland 表的 `umbrella` 仍直接生成。同 seed 12345、同林班 hash `0b1c225c`：forest 的 key 0 是 conifer、105 是 log；grassland 的 key 1 是 conifer、103 是 umbrella。現有測試還刻意容許 log，只斷言沒有 snag，因此全綠無法證明此規格成立。 | 林班限制應涵蓋直接選出的樹與倒木：於人工林過濾／轉換這些候選，保留礦石等非樹物種。以同林班分組驗證樹種、尺寸與無死木，不能只跨林班彙總或只驗 `PickTree`。改變生成結果須升 `ScatterPlanner.GeneratorVersion` 並重訂快照。 |
| P3-25-S2 | med | `Assets/_Project/Features/WorldGen/Runtime/Model/ScatterPlanner.cs:204`；`Assets/_Project/Bootstrap/Demo/ScatterField.cs:432–434`；`Assets/_Project/Bootstrap/Demo/WorldObjects.cs:233–237` | **倒木的規則阻擋圓未包住實體碰撞器。** `BlockRadiusM = 0.3 × SizeM`，表現層卻建立沿 local X、總長 `0.8 × SizeM` 的 CapsuleCollider，兩端距中心 `0.4 × SizeM`。規則層 `IsBlocked` 只比圓形距離，沿長軸的 `0.3S…0.4S` 範圍可被判為通行，但玩家物理仍被倒木擋住；下層植物避讓亦低估這個範圍。S=4 m 時，規則半徑 1.2 m，實體長軸半長 1.6 m，相差 0.4 m。此為尺寸與判定式推導，未做玩家／宙獸穿透的場景重現。 | 讓規則阻擋幾何與 CapsuleCollider 一致；若保留圓形近似，至少包住長軸兩端並明訂側面的保守阻擋。補沿長軸、短軸與不同 yaw 的規則／物理一致性測例。`TouchesCanopy` 也使用此半徑，改值須升生成版本。 |

## 驗證

- Unity CLI EditMode：`Logs/p3-25-sol-editmode.xml`，**15/15 通過**，實際執行 fixture 為 `Cosmimera.WorldGen.Tests.ScatterPlannerTests`。指令 filter 使用 `Cosmimera.WorldGen.Tests.ScatterPlannerTests|Cosmimera.WorldGen.Tests.TemplatePlannerTests`；後者在專案中不存在，不計入覆蓋。
- Unity CLI PlayMode：`Logs/p3-25-sol-playmode.xml`，`Cosmimera.Bootstrap.Tests.ScatterFieldTests` **7 通過、0 失敗、1 略過**。略過的是需 `COSMIMERA_CAPTURE=1` 的 `Capture_scenery_lineup_when_requested`，未將其當作目視驗收。
- .NET 9 探針直接引用本專案 `Library/ScriptAssemblies` 的 DLL，沒有重寫 PCG 或散佈演算法。16 地貌 × 高／低兩帶 × 10,000 rolls = **320,000 次** `PickUnderstory`，海拔帶違規 **0**。roll 為 `unchecked(i * 2654435761u)`，i=0…9999。
- 六種地貌的人工林 census 使用 R=640、seed=12345、`verdant_planet`、平地高度 0、無海／道路遮罩、固定密度 0.7、固定 biome，遍歷全部 61,206 個冠層 key。以 `ForestTypeOfCell` 篩人工林，再透過 `ForestOf` 的 out standHash 確認同林班，而非把不同人工林的不同樹種誤當違規。這是 CLR 呼叫結果，並非 Mono／IL2CPP 跨後端證明。

| 固定地貌 | 同林班 hash | 同林班生成見證 |
|---|---|---|
| forest | `0b1c225c` | conifer key 0；log key 105 |
| grassland | `0b1c225c` | conifer key 1；umbrella key 103 |
| plateau | `0b1c225c` | umbrella key 2；conifer key 409 |
| river_valley | `0b1c225c` | conifer key 1；log key 4 |
| archipelago | `0b1c225c` | umbrella key 1；conifer key 203 |
| eternal_day | `0b1c225c` | conifer key 0；umbrella key 2 |

固定案例的最小生成方式（引用上述 DLL；LINQ 以外無額外工具依賴）：

```csharp
var config = new PlanetConfig(640f, float3.zero);
var p = new ScatterPlanner(config, 12345ul, "verdant_planet", _ => 0f,
    null, null, _ => 0.7f, (d, roll) => ScatterBiomes.BiomeOf("forest"));
foreach (long key in new long[] { 0, 105 })
{
    p.TryGetItem(key, out var item);
    Console.WriteLine($"{key}: {p.ForestTypeOfCell(key)}, {p.Kinds[item.KindIndex].Id}");
}
```

## 覆蓋邊界

- 地貌表逐項對照設計，未發現表內物種或下層海拔標籤漏接。每張下層表皆保有不限海拔的候選，避免篩選後總權重為零。
- 每格獨立 PCG、預先取樣、冠層不讀下層的設計，以及既有順序／快照測試，支持同後端固定輸入的重現性；不能推廣成跨後端驗收。
- `RunSession` 有傳入 `TemplatePlanner.ScatterBiome` 與地貌密度；印章核心、外緣擲骰及 volcano／cave 對應的控制流程已靜態核對。現有 EditMode 測試未直接呼叫 `TemplatePlanner.ScatterBiome`，本次也未執行真實印章的跨後端比對。
- ADR-0007 的 Scatter 浮點邊界風險仍適用。既有 `ReproProbe` 的 `planDiscrete` 不含散佈物種、林型或散佈物件集合，不能把該欄相同當作 P3-25 的跨後端證明；沿用 `tech-debt.md` 的 Q-T6-R1，不另重複立案。
- 本次沒有修改 Data、場景或生成結果，無須升版本；人工林與倒木修正仍待原主責處理。
