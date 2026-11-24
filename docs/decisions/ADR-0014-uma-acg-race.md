# ADR-0014：日系 ACG 基底轉成 UMA 自訂種族

## 狀態
已採納（2026-11-22 定案 Q-T9）。補充 ADR-0012（沿用 UMA 執行期）與 ADR-0013（模型來源）；取代 ADR-0012 中「使用內建 HumanMale／HumanFemale 種族」的部分。路人 NPC 也使用本種族，見 ADR-0015。基底網格為 `tools/acg-character/`（MPFB）的輸出；`CharacterStudy_1123/` 不納入管線（2026-11-23 定案 Q-T10）。

## 背景
- ADR-0013 改由 Blender＋MPFB 產出日系主角模型；捏臉規格以 UMA DNA 滑桿＋貼花層描述，內建寫實種族畫風不符。
- 選項：(A) 烘成 blendshape 並移除 UMA；(B) 主角固定、自創角色走 UMA 寫實種族；(C) 轉成 UMA 自訂種族。使用者選 C。

## 決定
1. 以 `tools/acg-character/` 產出的日系男／女基底（同一骨架 `game_engine`，T-pose 綁定）建立兩個 UMA 自訂種族（`RaceData`＋身體 `SlotData`＋`OverlayData`）。
2. 頭髮、服裝、配件拆成獨立 slot（共用基底骨架權重），讓主角 preset 與玩家自創角色共用同一套部件。
3. 捏臉滑桿仍對應 UMA DNA（自訂 DNA converter，以骨骼縮放／位移為主，必要時加 blendshape DNA）；存檔格式不變（整數千分比滑桿值）。
4. 三位主角的預設外觀改為以新種族組成的 `UMAPreset`；目前的獨立 FBX（`Art/Characters/<id>/ACG/`）作為來源與比對用。

## 後果
- 需實作：Blender 端依部件拆分匯出、Unity 端 slot／overlay／race 建立腳本、DNA converter 與滑桿對照表。工時為三個選項中最大。
- 卡通渲染需另做 UMA 相容的 overlay 材質（URP toon）。
- UMA 內建 HumanMale／HumanFemale 不再用於主角與玩家角色；ADR-0012 的安裝流程保留。

## 替代方案
- A blendshape 並移除 UMA：單一管線最省工；使用者未採用。
- B 主角固定＋自創走寫實 UMA：兩套畫風；使用者未採用。
