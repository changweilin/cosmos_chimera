# 角色（玩家與 NPC）

玩家角色不直接攻擊（`combat.md`），角色的存在感來自外觀、動作與跟宙獸的互動。用途（玩家預設外觀 / NPC 共用）→ Q-A1。

## 設計概念維度
| 維度 | 規則 |
|---|---|
| 人種 | 東亞 / 白人 / 各地原住民 / 其他，預設**各 25%** 加權隨機；原住民與「其他」再細分地區 |
| 性別 | 男 / 女（預設各 50%）；中性外觀為可選項，預設權重 0 |
| 年齡 | **青少年（13–19）佔 50%**；其餘：兒童 10%、青年 25%、中年 10%、長者 5% |
| 性格 / 風格 / 興趣 / 喜好 / 厭惡 | 各自從選項池抽取（性格 2、興趣 2、喜好 2、厭惡 2） |
| 定位 | 玩家角色預設、隊友 NPC、大廳 NPC、星球原住民 NPC、對手 |
| 慣用收服道具 | 符籙卡片 / 琉璃珠 / 魔術方塊（影響道具外觀與投擲動作，見 `lobby-meta.md#收服道具`） |

選項池與權重的唯一來源：`../cosmos_chimera_forge/catalog.js`。
資料格式唯一規範：`Data/schemas/character.schema.json`。

## 數據格式與跨專案共同維護
- **格式一致性**：角色數據結構與 `cosmos_chimera_forge`（立繪鍛造台 `catalog.js`、`tasks.js` 與交付包 `concept.json`）完全相同。
- **規範定義**：`Data/schemas/character.schema.json`（定義 `id`、姓名中英、`role`、`ethnicity`、`gender`、`age_band`、`age`、`height_cm`、`personality`、`vessel`、`biography` 等標準欄位）。
- **共同維護協定**：
  1. `cosmos_chimera`（本專案）與 `cosmos_chimera_forge` 雙向同步 `Data/schemas/`、`Data/_incoming/` 及基礎資料。
  2. 任何一邊改動角色或宙獸資料／Schema，必須在同一次作業中同步至另一專案。
  3. 驗證標準：兩邊執行 `tools/validate-data` 皆須為 0 error，且 forge 跑 `python gen_designed.py` 產出正常。

## 文化呈現準則
- 原住民與各族裔服飾取**當代與傳統融合**的意象，不做刻板化（羽毛頭飾＝印第安人、斗笠＝亞洲人之類）的符號堆疊。
- 涉及特定族群的神聖紋樣、儀式服飾：標記 `sensitivity: review`，人工審核後才採用。

## 動作清單
完整 clip ID、幀數與事件見 `art-asset-pipeline.md#動作`。涵蓋：移動（待機/走/跑/潛行/翻滾閃避/跳躍）、格擋、命令（指向/攻擊/收回）、召喚與收回宙獸、使用道具（自身/他者）、佈署場地、開背包、對話、撫摸宙獸（依 XS/S/M/L/XL 體型）、投擲三類收服道具、受擊/倒地/起身、勝利/戰敗。
