# P5-08 A5 招式命名裁決與複驗

- 日期：2026-11-08；執行者：ChatGPT Codex。
- 使用者裁決 Q-C26：只改單招 ID，其他共用詞根保留。定案見 [combat.md 招式效果](../design/combat.md#招式效果)。
- `p5-05-moves-draft-05/moves/moves.json`：`light_aureole_halo` → `light_crown_radiance`。
- 同批 README 四處引用同步；舊 ID 以 A5 原因註記加入 `Data/retired-ids.txt`。

## 驗證

- 與改名前 JSON 結構比對，只有一個 `id` 改變；威力、CD、命中、形狀、專精增益與時長等欄位均相同。
- README 與快照比對，只有四處 ID 替換；退役清單中舊 ID 恰好一筆，新 ID 未被退役。
- 正式 `Data/` 與 13 批合併草稿驗證皆為 0 錯誤、0 警告；合併範圍含 30 宙獸、209 招式、93 特性、42 關係邊、6 星球類型，排除 P5-03 第二批與測試樣本。
- `Data/`、`Assets/`、`tools/` 的 JSON／C#／Python／Markdown 中無舊 ID 引用；退役文字清單及歷史審查紀錄保留舊名。
- 依 N8 同步 forge；兩專案 incoming／退役 ID 忽略 CRLF 比對一致，重跑 `gen_designed.py` 後 `designed.js` 與改名前逐位元相同。
- 兩專案 diff 檢查通過。本機證據為 `Logs/p5-08-naming-14bl0tp7/a5-applied-verification.json`，不納入版控。

Q-C26 已結案；本次只涵蓋 A5，P5-08 其餘待決項目維持待審。
