# AI 批次設計宙獸概念

## 概念公式
```
宙獸 = 1–3 屬性 × 外觀基底（生物 | 人形 | 器械 | 載具，可兩兩組合）× 文化原型（神祇 | 英雄 | 歷史名人 | 傳說的特徵意象）
```
例：`雷 + 風` × `鷹 + 戰車` × `北歐雷神的錘與山羊車` → 幼年：小羊角雛鷹；少年：拖著雷輪的鷹；成年：鷹首戰車；超載：雷雲環繞、雙輪噴電的戰車鷹神。

選項池（屬性數量權重、外觀子類、原型清單與敏感度）由外部專案 `../cosmos_chimera_forge/catalog.js` 維護（`tools/prompt-forge/` 已遷出，見其 README），美術交付流程見 `art-asset-pipeline.md`。
文化原型考據庫（`Data/_incoming/p5-02-archetypes-draft-01/`）與宙獸批次資料**兩個專案都維護**（2026-11-08 定案，P5-08 N8）：本專案 `Data/` 給 validate-data／importer，forge 的 `Data/` 給 `gen_designed.py`。任何一邊修改都要在同一次工作中同步另一邊，並以 `git diff --no-index --ignore-cr-at-eol` 確認兩份內容相同（forge 開啟 autocrlf，換行符可能不同）；forge 端改完跑 `python gen_designed.py`。

## 管線
| 步驟 | 產出 | AI（見 `ai-agents.md`） | 人工關卡 |
|---|---|---|---|
| 1. 組合矩陣 | 屬性 × 外觀 × 文化地區的配額表（確保分布平均） | Claude Sonnet | 確認配額 |
| 2. 文化考據 | 原型清單：特徵、象徵物、敏感度 | Gemini Argon（長上下文考據）→ Claude Opus 審 | 敏感度審核 |
| 3. 批次初稿 | 每批 20–50 隻 JSON（符合 schema） | Gemini Flash / ChatGPT Terra | — |
| 4. 一致性 | 去重、命名衝突、屬性分布、關係邊建議 | Claude Sonnet + `tools/validate-data` | — |
| 5. 數值初配 | 能力值、抗性、專精 | ChatGPT Sol（數值模型）→ Claude Opus 審 | 平衡試玩 |
| 6. 視覺 prompt | 四型態（三段進化＋超載）的概念圖 prompt，用 `tools/prompt-forge` 產生 | ChatGPT Terra | 美術定稿 |
| 7. 定稿 | 移入 `Data/creatures/` | — | ✅ 必須人工 |

## 文化原型準則
- 取**意象**（象徵物、配色、姿態、傳說情節），不直接用神祇/人物真名命名宙獸。
- 涵蓋全球：每批次各地區配額，避免集中於希臘/北歐/日本。
- 仍在信仰中的宗教主神、近代爭議人物：`sensitivity: review`，預設不採用。
- 不做嘲諷或貶低特定族群的設計。

## 批次 prompt 骨架（存於 `tools/prompts/creature-batch.md`，P5-01 建立）
輸入：配額列、schema、已存在 ID 清單、命名風格。輸出：純 JSON 陣列，無散文。
