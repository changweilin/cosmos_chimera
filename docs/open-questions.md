# 待決問題

需求說明「細節後續完善後再測試補上」。以下是實作前必須由使用者決定的點；**未決前，相關項目不得以猜測實作**。定案後移入對應設計文件並刪除此列。

## 宙獸 / 戰鬥
| ID | 問題 | 影響項目 | 暫定 |
|---|---|---|---|
| Q-C22 | AI 施招效用：控制/減益招（power 0）與 self/ally 治療、增益要不要有自己的觸發條件（例如自身 HP 低於某值才治療、脫戰也可補血），而不是永遠排在傷害招之後？（SOL P2-10 A10/A11） | P2-10 | 現行：傷害招優先、無敵人不施招 |
| Q-C12 | 超載的觸發條件（指令？能量條？HP 門檻？）、持續時間、CD；只有成年可超載，還是各階段都有超載型態？ | P2-05、P5-04 | 僅成年；指令觸發；持續 15 s；每場戰鬥一次 |
| Q-C13 | 收服道具類別轉換：「後期」何時解鎖、消耗哪些素材多少；蛋孵化的宙獸屬於哪個類別？ | P4-03 | 大廳執行；消耗專精屬性素材；孵化時玩家選類別 |
| Q-C33 | 玩家翻滾的輸入鍵（目標與 Downed 部分已定案，見下方註記）。 | P2-06、P3 | — |
| Q-C35 | 性格（`temperament`）的種類與各自領域距離？`timid` 是否「被攻擊才還手」？脫離領域多遠會脫戰？各物種填哪種性格？玩家角色的防禦（閃避、抗性、招式效果）？ | P2-10、P5（資料批次） | 已實作暫定：`timid` 0 m（被打或被集火才開戰）／`normal` 8 m／`fierce` 20 m；脫戰 40 m；草稿依階段填 幼 timid／少 normal／成 fierce；角色閃避 0、抗性 0、不吃效果 |
| Q-C36 | 昏厥恢復的道具與 NPC：種類、取得方式（掉落／商店／任務）、恢復量、能否在戰鬥中使用？ | P3、P4 | — |
| Q-A1 | 角色概念的用途：玩家可選的預設外觀、NPC，或兩者共用？玩家是否能捏臉？ | P4-01、P5-04 | 預設外觀與 NPC 共用；不捏臉 |

> Q-C1～Q-C8、Q-T4 已於 2026-11-06 定案，內容寫入 `design/creatures.md`、`design/combat.md`、`design/vision.md`。
> Q-C9、Q-C10、Q-C17 與 P2-09 疊加/觸發規則已於 2026-11-06 定案，見 `design/creatures.md#進化`、`#隊伍規則`、`design/combat.md#場地`、`#效果疊加`、`#特性觸發`。
> Q-M2、Q-M4、Q-W1（方案 A）、Q-T1（steering）已於 2026-11-08 定案，見 `design/lobby-meta.md`、`design/planet-world.md`、`architecture/planet-tech.md`。Q-M3 延後（D0 不做孵蛋）。
> Q-C19（dash 資料上限 36000 mm）已於 2026-11-07 定案，見 `design/combat.md`。
> Q-C14～Q-C16 已於 2026-11-06 定案：九種形狀及遷移、每招 base_range_mm、必填體型／超載延後，見 `design/combat.md`、`design/creatures.md`。
> Q-C28 與 Q-C29 的曲線／取整／進化部分已於 2026-11-08 定案，見 `design/level-progression.md`；Q-C10 的副手不成長規則已由同日使用者修訂取代。
> Q-C30 的主力權重／死亡分母／逐事件紀錄已於 2026-11-08 定案，見 `design/level-progression.md#經驗分配`。
> Q-C29、Q-C30 已於 2026-11-09 定案為方案 A，見 `design/level-progression.md#獎勵量q-c29q-c30-定案`；數值在 `Data/balance.json#progression`。
> Q-C31 已於 2026-11-09 定案：收服與任務經驗沿用觸發它的那場戰鬥的出場紀錄；升級效果在戰鬥結束後結算，見 `design/level-progression.md#經驗分配`。
> Q-C32 已於 2026-11-09 定案為方案 B（素材屬性依星球偏好），見 `design/creatures.md#進化`；數值在 `Data/balance.json#materials`。
> Q-C33 目標／Downed 與 D0-E2、D0-R1、D0-R2、D0-X3 已於 2026-11-10 定案：野生依物種性格決定領域、打最近入侵者（含玩家）、Downed＝失敗結算；昏厥持續整場 run；BOSS HP×2、體型與判定體×1.5；BOSS 等級 LV5–75 隨 d 線性。見 `design/combat.md#野生領域`、`design/planet-world.md`、`design/level-progression.md`。
> Q-C34 已於 2026-11-09 定案：素材庫存溢位時整場獎勵不入帳（不截斷），見 `design/creatures.md#進化`。

## 世界 / Meta
| ID | 問題 | 影響項目 | 暫定 |
|---|---|---|---|
| Q-D1 | 小型星球半徑：planet-world.md 寫 320 m，但戰鬥判定只核准 R ≥ 640（`MoveGeometry.MinSupportedRadiusM`；ranged cap 36 m 會超過 R=320 的地平線）。要 (a) 小型改 640、中型 960、大型 1280，還是 (b) 為 R=320 另訂較短的射程上限？ | P1-10、P2-04、P4-02 | D0 暫以 640 m 遊玩小型星球 |
| Q-M1 | 可否輸入/分享 seed 重玩同一顆星球？ | P4-02 | 可 |
| Q-M3 | 蛋的孵化條件？ | P4-05 | — |

## 多人
| ID | 問題 | 影響項目 | 暫定 |
|---|---|---|---|
| Q-N1 | 合作模式的收服/蛋如何分配？ | P6-04 | 每人各自一份 |
| Q-N2 | 對抗模式的形式：競技場、野外 PvP、或搶 BOSS？ | P6-05 | 競技場 |

## 技術 / 工具
| ID | 問題 | 影響項目 | 暫定 |
|---|---|---|---|
| Q-T3 | 概念圖/貼圖/3D 模型由哪個工具產出？ | P5-04 | 概念圖、三視圖、特效分鏡：ChatGPT（圖像生成）/ Gemini Argon 任務模式，經 `tools/prompt-forge`。**3D 網格/綁骨/動畫仍未定**：對話式 AI 只保證交付三視圖 + 骨架規格 + Blender 腳本，真正的 image-to-3D 工具待選 |
| Q-T5 | `PlanetFrame` 規定 cross(East, North) == Up（右手公式），但 Unity 是左手系：面向 North 時 East 在**畫面左邊**，等於鏡像地圖。要 (a) 改成 cross(North, East) == Up 讓 East 在右邊（P1-01 契約破壞性變更，`LatLon` 經度方向一併翻轉），還是 (b) 維持數學慣例、由 P3 貼真實地圖模板時鏡像？ | P1-01、P3-03（真實地圖會左右顛倒）、HUD 指南針 | 建議 (a)；P1-05 移動與攝影機只用 cross(up, forward)，不受影響 |
| Q-T7 | `code-contracts.md` 要求每個 public 成員有 Pre/Post/Side effects；enum **成員**是否也要逐一寫，還是型別層級的契約即涵蓋（目前 repo 全部 enum 都是後者）？Sol P2-05/07 審查 C3 列為缺漏 | 全部 Api enum | 型別層級涵蓋成員，在規則中明文豁免 |

> Q-T6 已於 2026-11-09 定案為 (b)：同後端逐位元、跨後端高度容差 3 mm、內容決策不讀浮點高度、握手比對離散內容雜湊，見 `decisions/ADR-0007-generation-reproducibility.md`。
