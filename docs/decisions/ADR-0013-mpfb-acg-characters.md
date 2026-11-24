# ADR-0013：主角模型改由 Blender＋MPFB 基底改造成日系 ACG

## 狀態
已採納（2026-11-22 定案 Q-T8）。部分取代 ADR-0012 決定 3（主角改為 UMA preset）。規格見 `docs/design/characters.md`。

## 背景
- ADR-0012 的 UMA 寫實種族只能在剪影與配色上接近日系立繪；頭身比、臉型、眼睛差距大。
- 開放授權的日系基底中：VRoid Studio 素材非 CC0，且禁止以其網格做角色生成／捏臉功能（需另向 pixiv 申請授權）；CharacterStudio／loot-assets 素材未標授權；UMA 唯一的動漫種族只有女性。

## 決定
1. 三位主角的 3D 模型以 **MPFB（MakeHuman 的 Blender 外掛，程式 GPL-3、素材與輸出 CC0）** 產生人體基底，於 Blender 改造成日系 ACG 外觀後輸出給 Unity。
2. 風格目標（以立繪為準）：約 **7 頭身**（全身高 ÷ 頭頂到下巴；2026-11 依立繪三視圖由 6 改 7）；大眼、小鼻、小嘴、尖下巴、細頸；髮型為成束的片狀網格；卡通渲染（平塗＋描邊）。
3. MPFB 只是 Blender 端的製作工具，不進 Unity 專案、不進執行期；GPL 只約束外掛本身，產出網格為 CC0。
4. 改造步驟與參數放在 `tools/acg-character/`（Blender Python），以腳本重跑，不手改輸出檔。

## 後果
- 頭身比與臉部需自行雕塑／變形，非現成預設；服裝與配件（背包、耳機、和服袖等）需自行建模。
- 骨架需對應 Unity Humanoid，既有動作才能共用。
- 捏臉接法：產出模型轉成 UMA 自訂種族，見 ADR-0014。

## 替代方案
- VRoid Studio：主角固定不可捏可合法使用，但與捏臉衝突；使用者未採用。
- UMA `Anime Elf Female 3.0`：無男性基底；使用者未採用。
- 申請 pixiv 角色生成授權：可能被拒；使用者未採用。
