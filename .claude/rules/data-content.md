---
paths:
  - "Data/**"
  - "tools/**"
---
# 遊戲資料規則

- `Data/` 是宙獸、招式、特性、關係表、星球類型的**唯一真實來源**；格式由 `Data/schemas/*.schema.json` 定義。
- ID 規則：`snake_case`、全域唯一、**永不重用**（刪除的 ID 移到 `Data/retired-ids.txt`），因為存檔與連線封包以 ID 引用。
- AI 批次產出先放 `Data/_incoming/<batch-id>/`，通過 `tools/validate-data` + 人工審核才移入正式資料夾。
- 數值欄位用整數或明確的百分比整數（`resist_fire: 25` 表 25%），避免浮點在不同工具間的格式差異。
- 關係表是**無向圖**：只存一次邊（`a < b` 字典序），由 importer 展開；避免兩端不一致。
- 文化原型（神祇/英雄/歷史人物）：只取**特徵意象**，不直接使用真名作宙獸名；宗教敏感對象需標記 `sensitivity: review`。見 `docs/design/creature-concept-pipeline.md`。
