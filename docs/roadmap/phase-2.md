# P2 宙獸與戰鬥核心

**出口條件**：攜帶御三家（各 3 階段）進場，可召喚、輪替、配副手、施放招式，對木樁與 2 種簡單敵人戰鬥；所有數值來自 `Data/`。
讀：`../design/creatures.md`、`../design/combat.md`
**已定案**：Q-C1～Q-C8（2026-11-06）。**仍待定**：Q-C9（影響 P2-05）、Q-C10（影響 P2-07）、Q-C11（影響 P2-10）

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P2-01 | 宙獸/招式/特性/關係 資料模型（Api + DbC）與 schema 細化 | OPUS | SOL | 契約審查通過；schema 與 C# 型別一致 | ✅ |
| P2-02 | 能力值→衍生值函數（HP、範圍、CD、移速、閃避）+ 上下限 | SOL | OPUS | 曲線圖 + 不變式測試 | ✅ |
| P2-03 | 傷害計算（威力×專精×抗性，無相剋表、無攻防值） | SONNET | OPUS | EditMode 測試含邊界（免疫、弱點） | ✅ |
| P2-04 | 兩段命中（幾何→擲骰，evasion 上限、hit_floor）、MISS 回饋、九種招式形狀與獨立突進位移（見 combat.md） | SONNET | OPUS | 固定 seed 下命中率統計誤差 < 1%；判定體與 STR 範圍縮放一致 | 🟦 |
| P2-05 | 進化系統：經驗門檻 + 屬性素材，三階段切換、模型替換 | SONNET | OPUS | 經驗或素材任一不足時拒絕並說明缺什麼 | ⬜ |
| P2-06 | 隊伍：6 主力 / 4 上場池 / 同時 2 隻在場、切換冷卻、退場規則；玩家角色 HP 與翻滾無敵幀 | SONNET | OPUS | 規則層測試涵蓋所有上限 | ⬜ |
| P2-07 | 副手：從倉庫挑選、關係表資格檢查、每隻副手招式/特性二選一 | SONNET | OPUS | 無關係邊時拒絕並給 UI 理由 | ⬜ |
| P2-08 | 御三家日月星概念與資料（9 隻） | OPUS（概念）+ TERRA（文案） | 人工 | 三條線定稿入 `Data/creatures/` | ⬜ |
| P2-09 | 特性系統：觸發條件 + 效果資料驅動 | SONNET | OPUS | 新增特性只需改 JSON | ⬜ |
| P2-10 | 宙獸戰鬥 AI（行為樹或效用 AI）+ 玩家指令（2 隻在場的指令分派，依 Q-C11） | OPUS | SONNET | 日=保距、月=繞背、星=突進，行為可辨識；8 隻同時 AI 仍達幀率預算 | ⬜ |
| P2-11 | 戰鬥 HUD（HP、CD、輪替列、副手提示） | SONNET | 人工 | 4 隻輪替狀態一眼可讀 | ⬜ |

## 交接紀錄

- 2026-11-06 · ChatGPT Sol（Codex CLI） · partial · 已提出整數衍生值模型、係數、兩張 SVG 與 40 組黃金向量，11 項不變式測試通過；資料驗證缺少 jsonschema，git add 因 worktree metadata 無寫入權限受阻，尚未提交 · 下一步：Claude Opus 審查公式與移植契約；具備依賴與 Git 寫入權限的環境重跑資料驗證並提交，人工確認數值
- 2026-11-06 · Claude Opus（驗收 Sol）· partial · 代為提交（Codex 沙箱無法寫入 worktree 的 gitdir）。本機重跑：`tools/balance` 11/11 通過、`tools/validate-data` 0 錯誤（本機有 jsonschema）。審查意見：(1) `ranged_distance.cap_mm` 固定 36 m，但 R=320 時眼高 1.7 m 的地平線只有 33 m，違反「尺寸由 `PlanetConfig.Radius` 推導」— 建議改成地平線比例（例如 0.77×horizon），或明訂射程上限只適用 R≥640；(2) 球面路徑長度與直線距離的差異在 36 m 內 < 0.01%，可忽略；(3) 整數/Int64 移植契約與黃金向量可直接給 C# 使用 · 下一步：使用者確認數值與 (1)；SONNET 依黃金向量移植成 C#（P2-01 定好 `base_range` 之後）
- 2026-11-06 · Claude Opus · partial · P2-01（特性/效果部分）：trigger/effect 詞彙封閉為 enum — 採 P5-05 初稿的 7 觸發 + 7 效果，每種 type 的 params 鍵與範圍寫進 `common.schema.json#/$defs/trigger`、`#/$defs/effect`（招式 effect 共用同一詞彙）；`GameData` 以 `TriggerDef`/`EffectDef` + `TriggerType`/`EffectType` 取代 `RuleDef`（ADR-0004 已加修訂），讀取該 type 沒有的參數是 Contract 違反；importer 在寫入前檢查 type、缺漏/多餘參數、範圍。`validate-data` 對 rule params 錯誤給「params do not match this rule type」（原本誤顯示 adult 提示）。現有 150 招 + 60 特性初稿全數通過新 schema。**未做（待決）**：招式 shape 兩套詞彙 Q-C14、`base_range` 來源 Q-C15、`size_class`/超載欄位 Q-C16；詞彙本身待人工確認 · 下一步：使用者回答 Q-C14～Q-C16；SOL 審 schema↔C# 一致性
- 2026-11-06 · Claude Opus · done · P2-03：`Combat/Runtime/Model/DamageModel.Compute(power, mastery%, resist%)`，純整數（Int64 中間值、最後一次四捨五入），resist 夾在 ±100；**提議**（待人工）：非免疫的命中至少 1 點，避免 99% 抗性被四捨五入成看起來像免疫。表示範圍上限 `MaxPower`=100000、`MaxMasteryPercent`=1000（非平衡值）；負的有效專精由呼叫端（P2-09）先夾到 0。新增 `Cosmimera.Combat.Tests.EditMode`（含單調性、零傷害 iff 無威力或免疫、溢位上界、Pre 違反）。驗證：EditMode 199/199、0 警告；URP `PC_RPAsset` 遷移照例還原 · 下一步：OPUS 以外的審查者（SOL）審 P2-03；P2-09 接上特性修正後的有效專精/抗性
- 2026-11-06 · Codex（SOL 審查） · partial · P2-01／P2-03 審查完成：既有 EditMode 35/35、傷害 decimal oracle 100455 組通過；發現 schema 合法數值超出 Int32／DamageModel 域的 R1，另記兩項合約外輸入補強，詳見 [審查報告](../reviews/P2-data-model-damage-review-sol.md)；P2-01 維持 🟦、P2-03 維持 🟨 · 下一步：OPUS／SONNET 統一資料與傷害 API 表示域後交 SOL 複審；使用者確認 Q-C14～Q-C16、詞彙與最低傷害／取整提案
- 2026-11-06 · Codex（SOL） · partial · P2-01／P2-03 已依使用者確認修正 R1／H1／H2、九種 shape、每招 base_range_mm、體型與 150 招遷移；完整 EditMode 250/250（含 3 個暫存探針）、語料 412/412、Python 15/15＋11/11，本機驗收通過，契約變更與證據見 [驗收報告](../reviews/P2-data-model-damage-review-sol.md) · 下一步：使用者授權將本次範圍透過 AGY 交 Claude OPUS 跨廠商複審，通過後更新為完成
- 2026-11-06 · Codex（SOL） · done · P2-01／P2-03 驗收通過：正式 EditMode 247/247、契約語料 412/412、資料驗證測試 15/15、平衡不變式 11/11；使用者已確認 Q-C14～Q-C16、詞彙與傷害契約，並指定由本對話完成最後複審（未執行外部模型審查），兩項更新為 ✅ · 下一步：SONNET 依已定案資料契約執行 P2-04；P2-09 保證修正後有效專精符合傷害 API 表示域
- 2026-11-06 · Claude Opus · done · P2-02：使用者定案係數（射程上限維持固定 36 m，**只支援 R≥640**）。提案搬為正式 `Data/balance.json`（鍵值說明移到 `docs/design/derived-stats/balance-keys.md`），`tools/balance` 改讀正式檔，黃金向量位元不變。C# `Combat/Runtime/Model/DerivedStats.cs`（`DerivedStatsCoefficients` 建構子檢查可移植包絡）以 40 組黃金向量逐位元比對，另測 0..10000 全域單調/上下限/飽和、Int32 極值、每個 Pre 違反、包絡外係數。**未做**：`balance.json` 尚未經 importer 進 ScriptableObject（目前只有測試讀 JSON），P2-06/P2-10 接執行期時補 · 下一步：SOL 對抗式審查 DerivedStats 移植
- 2026-11-06 · Claude Opus · partial · P2-04 規則層：`HitResolver.Resolve`（只在幾何碰到且無無敵幀時用掉一次 `Pcg32.NextUInt(1000)`；6 種命中率在固定 seed、20 萬次下誤差 < 1%）、`MoveGeometry` 九種形狀判定體（使用者確認全域預設放 `Data/balance.json#hit_volume`，規則見 combat.md#判定體）：melee 全尺寸乘 L(STR)、遠程只延長射程/施法距離、dash 不縮放；用球面等距方位座標 + 半徑差判高度（R=640 時 35.9 m 外的平地用切平面會誤判 miss，有測試鎖住）。資料契約新增：`effect_duration_ms`（非 heal 效果必填，對象依 shape；schema/validator/importer/MoveDef 同步）與「目標點 burst 不可 melee」。驗證：EditMode 297/297、0 警告；Python 17/17 + 11/11；全部 `_incoming` 招式批次 0 錯誤；URP `PC_RPAsset` 遷移照例還原。**未做**：MISS 表現回饋（需場景/HUD）、field 壽命與結算（Q-C17）、dash 實際位移與 collider 同步（PlayMode）、逐招覆寫欄位 · 下一步：使用者回答 Q-C17；SOL 審 P2-04 幾何與擲骰；SONNET 接 PlayMode 表現層
