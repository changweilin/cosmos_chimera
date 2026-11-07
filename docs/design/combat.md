# 戰鬥

即時動作戰鬥。玩家角色有 HP，可以移動、翻滾閃避、投擲收服道具、下指令，但**不直接攻擊**（Q-C8 定案）；輸出全部來自宙獸。場上同時最多 2 隻宙獸，由 AI 施招，玩家用指令引導。

## 傷害公式（Q-C1、Q-C2 定案；係數是 tunable，放在 `Data/balance.json`）
```
raw     = move.power × (1 + attacker.mastery[move.element] / 100)
damage  = raw × (1 − clamp(target.resist[move.element], −100, 100) / 100)
```
- `resist < 0` 表示弱點（受到更多傷害）；`resist = 100` 表示免疫。
- 沒有攻擊/防禦數值，也沒有全域相剋表。成長來源只有：專精 %、進化後的招式威力、副手提供的招式與特性。
- 力量不進入傷害公式，只影響範圍/射程。
- 2026-11-06 使用者確認：以 Int64 計算中間值，只在最後四捨五入（half up）；威力非零且非免疫的命中至少 1 點。`power` 表示域為 0..100000，有效專精為 0..1000；這是資料／API 契約上限，非平衡目標。P2-09 必須保證修正後專精符合此域，負值先歸零；疊加規則另由 P2-09 定義。

## CD
`effective_cd = max(cd_floor, move.cooldown_ms × f(SPI))`，`f` 為遞減函數，`cd_floor` 防止極端精神值打破節奏。

## 命中與閃避
**兩段判定**（Q-C7 定案）：先做幾何命中（判定體/彈道碰到才算），碰到後再擲骰：
```
hit_chance = clamp(move.accuracy / 100 × (1 − target.evasion), hit_floor, 1)
```
- `target.evasion` 由敏捷推導，設上限（暫定 40%），避免敏捷堆滿後幾乎打不到。
- `hit_floor` 暫定 5%，讓任何幾何命中都有機會生效。
- 擲骰 miss 一定要有明顯回饋（「MISS」字樣、殘影閃避動畫、專屬音效），不然玩家會以為判定有 bug。
- 擲骰用主機端的 `Pcg32`（戰鬥用的 stream，跟生成用的分開），讓多人時結果由主機權威決定。
- 玩家角色的翻滾閃避是無敵幀，不走擲骰。

## 力量 → 範圍/射程
`range = move.base_range_mm × g(STR)`；每招必填非負 Int32 毫米值，不從 `range_class` 查表。近戰招式放大判定體積，遠程招式延長射程。

2026-11-06 使用者確認的初稿值：melee=2000、mid=12000、long=24000 mm，已逐招寫入初稿，之後可個別調整。`burst` 的數值表示爆發半徑；`origin` 只指定中心來源，目標點的選擇與施法距離判定屬 P2-04。`movement=dash` 時，數值表示突進的直線路徑長度，必須 1–36000（2026-11-07 使用者定案 Q-C19：與遠程 36 m 上限一致，schema／importer／validate-data 同步檢查；幾何層 `ResolveDashEnd` 另以施放者半徑·π/4 為正確性界）。

## 召喚與輪替
- 上場池最多 4 隻，**同時最多 2 隻在場**（Q-C4 定案）；切換有共用冷卻（暫定 3 s），避免無限換手來躲傷害。
- Q-C11 定案（2026-11-07）：兩隻在場時以「主控 / 支援」區分：玩家的指令只給主控，按鍵切換主控對象；規則見〈戰鬥 AI〉。
- 效能預算：4 人合作時場上最多 8 隻玩家宙獸 + 敵人，P2-10 的 AI 與 P6-04 的同步都以這個數量做壓力測試。
- 主力倒下 → 自動退場，不可在本場戰鬥中再上場（暫定）。
- P2-06 規則（2026-11-06 使用者確認，實作 `Combat/Runtime/Model/Team.cs`）：
  - 6 主力在大廳定案、整場 run 不變；上場池 1–4 隻，只能在**非戰鬥中**從 6 主力重選，戰鬥中只在池內輪替。
  - 共用切換冷卻（`Data/balance.json#team.switch_cooldown_ms`）由**換手**或**主動收回**開始；開場召喚與倒下後補位不觸發冷卻，但冷卻進行中一樣不能召喚（否則「收回 + 召喚」等於免冷卻換手）。
  - 倒下的空位不自動補，由玩家手動召喚；池中全部倒下且場上無宙獸 → `IsWipedOut`（後續戰敗處理屬 P3/P6）。
- 玩家角色（`PlayerVitals.cs`，數值 `Data/balance.json#player`，暫定）：固定 HP 1000，不由能力值推導；翻滾 600 ms，其中前 400 ms 無敵，翻滾冷卻 800 ms（從開始算）。無敵區間為半開 [開始, 開始+400)。HP 0 → Downed，不能翻滾。
- 副手不上場，只提供招式 / 特性 / 超載解鎖給主力（由副手的收服道具類別決定，見 `lobby-meta.md#收服道具`）。
- 超載：已解鎖的主力可在戰鬥中短暫進化為超載型態，結束後回到原階段（觸發/持續/CD → Q-C12）。

## 戰鬥 AI（P2-10；Q-C11、Q-C20、Q-C21 於 2026-11-07 使用者定案）
- 指令（Q-C20）：`集火目標`（指定一個敵人）、`自由戰鬥`（預設）、`跟隨待命`、`撤退`。指令只給**主控**；**支援**不聽指令，依自身風格自由戰鬥，目標優先取主控當下的目標（Q-C11）。
- 走位風格（Q-C21）：creature 資料逐階填 `ai_style`（schema 必填、封閉 enum），不由招式推導。御三家：日=`keep_distance`、月=`flank`、星=`charge`。
  | ai_style | 站位（相對目標，沿大圓） |
  |---|---|
  | `keep_distance` 保距 | 站在最長敵對招式可及距離 × `ai.keep_distance_permille` 處 |
  | `flank` 繞背 | 站在目標背後（-forward 切向）最短可及距離 × `ai.close_in_permille` 處 |
  | `charge` 突進 | 直接逼近到最短可及距離 × `ai.close_in_permille` |
  | `hold` 站樁 | 目標在最長可及距離 × `ai.hold_permille` 內就不動，否則逼近到該距離 |
- 目標：集火目標存活就打它；否則（支援）主控的目標；否則最近的存活敵人（同距取 id 小者）。集火目標倒下自動回到自由戰鬥。
- 施招：可用（CD 好了）且目標中心在可及距離內的敵對招式中取威力最高者（同威力取 slot 小者）；沒有時才用 self / ally 招（ally 需隊友在射程內）。AI 只「選」，命中仍走 `MoveGeometry` → `Combatant`。
- 跟隨待命：離玩家超過 `ai.follow_leash_mm` 才往回走；只施放不必移動就打得到的招式。撤退：走向玩家，不施招。
- 輸出是「目的地方向（單位向量）+ 目標 + 招式 slot」；地面高度、尋路（Q-T1）、轉向與動畫屬表現層。可及距離 = 該招判定體能碰到目標中心的最大地表距離（aim-point 招 = 施放距離 + 半徑）。
- 同一 tick 先算主控、再把主控的 `TargetId` 交給支援（整合層負責順序，避免支援讀到上一 tick 的目標）。跟隨待命不延續先前的集火目標，取最近存活敵人。
- 沒有存活敵人時不施放任何招式（含 self / ally 治療、增益）；敵對招一律優先於 self / ally，效果型敵對招（power 0）排在傷害招之後 — 暫定，見 Q-C22。
- 規則層：`Combat/Runtime/Model/CombatAi.cs`，純函式、無配置記憶體、無亂數（同輸入同輸出，可在主機權威模擬）。

## 判定體（P2-04，2026-11-06 使用者確認「全域預設，之後可逐招覆寫」）
幾何使用域（2026-11-06，SOL 三輪審查後）：只支援 R ≥ 640；所有戰鬥座標須在距球心 R/2～2R 的殼層內；離錨點超過四分之一圓周（夾角 > 90°）的目標一律不命中，不論判定體多大。
尺寸預設在 `Data/balance.json#hit_volume`（鍵值見 [balance-keys.md](derived-stats/balance-keys.md)），目前 schema 沒有逐招覆寫欄位。實作：`Combat/Runtime/Model/HitVolume.cs`（`MoveGeometry`）。
| shape | 判定體 | 位置 | 長度 / 半徑 | 其他尺寸 |
|---|---|---|---|---|
| front | 矩形 | 施放者前方 | base_range | `front_width_mm` |
| line | 矩形 | 施放者前方 | base_range | `line_width_mm` |
| sweep / cone | 扇形 | 施放者，朝 forward | base_range | `sweep_angle_deg` / `cone_angle_deg`（全角） |
| burst caster | 圓 | 施放者 | base_range（半徑） | — |
| burst target | 圓 | 瞄準點，限制在 g(STR)×`target_cast_{mid,long}_mm` 內 | base_range（半徑，不受 STR） | 只能 mid/long |
| arc | 圓（落點） | 瞄準點，限制在射程 base_range 內 | `arc_landing_radius_mm` | 拋物線軌跡屬表現層 |
| field | 圓 | 瞄準點，限制在射程 base_range 內 | `field_radius_mm` | 壽命＝招式 `field_duration_ms`，結算見〈場地〉 |
| self / ally | 無 / 隊友距離 ≤ 射程 | — | — | ally 不看高度 |

- **STR 縮放**：melee 所有尺寸（長、寬、高、半徑）乘 `MeleeLinearPermille`；mid/long 只把長度 / 射程 / 施法距離換成 `RangedDistanceMm`（含 36 m 上限），寬度、角度、AoE 半徑不變；dash 的路徑長度與位移固定等於 base_range，不縮放。
- **球面量測**：水平距離是以施放者半徑量的大圓距離（等距方位座標），高度是到球心距離的差（`height_mm/2` + 目標半徑內算命中）。R=640 時 36 m 外的地面比切平面低約 1 m，用平面判定會漏掉站在同一地面上的目標。
- **兩段命中**：`HitResolver.Resolve`；只有幾何碰到、且目標不在翻滾無敵幀時才擲骰，並且只用掉戰鬥 stream 的一次抽樣，讓各端依相同碰撞順序重播時保持同步。

## 招式效果
2026-11-06 使用者確認：`kind` 為 effect/both 的招式，除 `heal_percent_max_hp`（瞬間）外必須填 `effect_duration_ms`（1..600000），表示效果在每個被命中目標上持續的時間；heal 與 damage 招式不得填。作用對象由 `shape` 決定：`self` → 施放者、`ally` → 指定隊友、其他形狀 → 判定體命中的敵方目標。同一效果重複施加時的刷新/疊加規則由 P2-09 定。

## 場地（Q-C17 定案 2026-11-06）
- `shape: field` 的招式必填 `field_duration_ms`（1..600000），其他形狀不得填。場地存在於 [放置, 放置+壽命)。
- 放置當下結算第一次，之後每 `Data/balance.json#field.tick_ms`（1000 ms）結算一次，共 ceil(壽命/間隔) 次；每次結算對判定體內每個目標各做一次完整招式結算（命中擲骰＋傷害/效果），所以同一目標每次結算最多被作用一次。
- 規則層：`FieldTimeline`（`Combat/Runtime/Model/Effects.cs`）；模擬步進以 (上一步, 這一步] 計數，任何步長都不重複、不漏算。

## 效果疊加（P2-09，2026-11-06 使用者確認）
- 疊加鍵＝（來源招式/特性 id, 效果序號），**與施放者無關**：兩隻同招的隊友不會讓同一增益翻倍；不同來源一律相加。
- 每個效果可在資料填 `max_stacks`（1..10，預設 1）。1＝重複施加只刷新時間；>1＝每次 +1 層直到上限，之後只刷新。刷新會重置整疊的剩餘時間（整疊一起消失）。治療是瞬間效果，不可填 `max_stacks`。
- 單位：`mastery_mod` / `resist_mod` 是百分點（直接加到專精/抗性）；`cd_mod_percent` / `evasion_mod_percent` / `move_speed_mod_percent` / `range_mod_percent` 是衍生值的**相對**百分比：同類全部先加總，倍率 100+Σ 夾在 `Data/balance.json#effects` 的 [10%, 300%]，只乘一次，再套回原本的上下限（閃避上限、射程 36 m 上限、CD 下限 250 ms）。先加總再乘，讓結果與施加順序無關（各端施加順序不保證一致）。
- 有效專精夾在 0..1000（傷害 API 域）；有效抗性夾在 ±100。CD 乘完後向上取整（與 P2-02 相同：CD 不得提早轉好）。
- `range_mod_percent` 只改「打多遠」：front/line/sweep/cone 的長度，以及瞄準點/友方招式的施法距離；AoE 半徑、寬度、角度不變；dash 不受影響。
- 規則層：`EffectBook`（每個戰鬥單位一份）、`EffectMath`。

## 特性觸發（P2-09）
- 持續判定（查詢當下測試，不存計時）：`always`；`on_hp_below{p}` 在 HP×100 < p×最大HP 時成立；`on_hp_above{p}` 在 HP×100 > p×最大HP 時成立（皆為嚴格不等式）。
- 事件觸發（開啟 `duration_ms` 視窗，依效果的 `max_stacks` 疊加/刷新）：`on_summon`＝每次上場（開場召喚與換手上場都算）；`on_ally_swap`＝**換手上場的那一隻**（使用者確認）；`on_hit_taken{element|any}`＝命中確定（幾何＋擲骰）後，依招式屬性比對；`on_move_used{element}`＝自己使用該屬性的招式；`on_hp_cross_below{p, duration_ms}`＝HP 由 HP×100 ≥ p×最大HP 跌到 0 < HP×100 < p×最大HP 的那一刻（每次 HP 變動後以變動前後的值呼叫 `NotifyHpChanged`），**每場戰鬥最多一次**（`Clear` 重新武裝）；直接跌到 0 不觸發、也不消耗次數（不當復活用）。2026-11-07 使用者確認（Q-C18）。
- 事件觸發的治療效果立即回傳治療百分比給呼叫端；治療不可掛在持續觸發上。
- 結算順序（2026-11-07 使用者確認；規則層 `Combat/Runtime/Model/Cast.cs` 的 `Combatant`）：
  - 招式 CD 在施放被接受的瞬間起算；倒下或 CD 中的施放被拒絕且不觸發任何事件。
  - `on_move_used` 在施放時先觸發，**觸發它的這一招就吃到增益**（專精、CD、射程）；施放者的修正值在此刻凍結，之後每個目標與每次場地結算都用同一份。
  - 被命中：先以命中前的修正值擲骰、算傷害、扣血，之後才觸發 `on_hp_cross_below` / `on_hit_taken`，其增益**從下一擊起**生效。
  - HP 歸 0 的單位不再觸發任何特性事件、不再被施加效果，也不能被治療（不當復活用）。
  - `self` / `ally` 形狀的招式不擲骰、必定生效，也不消耗戰鬥亂數。若帶傷害（`damage` / `both`）就是**代價型**：傷害打在承受者（自己／隊友）身上，用施放者專精與承受者抗性照傷害公式計算，可以把承受者打倒（倒下後不再套效果）；這是 HP 變動所以可能觸發 `on_hp_cross_below`，但不是敵方命中，不觸發 `on_hit_taken`（2026-11-07 使用者確認，ARGON 審查 K3）。
- 暫定（未定案，待 P2-10/P3 戰鬥流程確認）：收回宙獸時不清除身上的計時效果（以絕對時間到期）；戰鬥結束呼叫 `Clear`。

## 招式形狀（動畫與特效分類用）
| 類別 | `range_class` | `shape` | 說明 |
|---|---|---|---|
| 近戰 | melee | `front` / `sweep` / `burst` | 正前方 / 橫掃 / 以自身為中心擴散 |
| 中遠距射擊 | mid, long | `line` / `cone` / `arc` | 直線 / 扇形 / 拋物線 |
| 目標點爆發 | mid, long | `burst` | 以目標點為中心的瞬間爆發 |
| 輔助 | — | `self` / `ally` | 對自身 / 對他者 |
| 佈署 | — | `field` | 在地面佈署持續場地 |

動作 clip 與特效以這個分類命名，見 `art-asset-pipeline.md`。

2026-11-06 使用者確認：九種詞彙統一套用於 schema、GameData 與內容初稿。只有 `burst` 必須填 `origin=caster|target`，其他形狀不得填。位移獨立為可省略的 `movement=none|dash`（省略代表 none）；dash 只搭配 line，沿路徑突進並判定命中。幾何、移動、持續場地等處理器仍屬 P2-04，新增詞彙不代表這些處理器已完成。

150 招初稿遷移：近戰 line→front（10）、近戰 cone→sweep（13）、中遠距 line/cone 保留（52）、aoe→burst（37，近戰 caster／其餘 target）、dash→line＋movement=dash（38）。保留 ID、威力、CD、準確率與 range_class。

## 屬性 × 環境互動（加分項，P5 以後）
水 + 雷 → 導電範圍擴大；火 + 木 → 燃燒地形；冰 + 水 → 結冰路面。以資料表驅動。
