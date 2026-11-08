# P5-08 A3／A4 英文命名裁決與複驗

- 日期：2026-11-08；執行者：ChatGPT Codex。
- 對象：`data/p5-08-copy-p5-06-relations` 工作樹的御三家與 P5-03 第一批文案。
- 狀態：Q-C25 已由使用者選定三筆替換並保留 Volt-；文案已套用、forge 已同步並重產。

## 裁決與處理

A3 原先的 `-vast` 字尾同時出現在月、水、冰三線成年英文名；星線少年 `Voltraven` 與冰線少年 `Rimbraven` 共用 `-raven`。A4 的星線採 `Volt-` 雷電詞根。這些是命名風格選擇，不是 schema 或引用錯誤。

已採以下三筆替換，保留其餘名稱：

| 宙獸 ID | 原英文名 | 定案英文名 | 理由 |
|---|---|---|---|
| `water_catamaran_serpent` | Brinavast | Brinamar | 保留 Brin- 水線詞根，區分成年字尾 |
| `ice_tundra_dreadnought` | Rimbravast | Rimbragard | 保留 Rimbr- 冰線詞根，區分成年字尾 |
| `star_storm_colt` | Voltraven | Voltrmane | 保留 Volt- 雷電詞根，以 mane 對應少年長鬃 |

使用者同時確認 A4 接受中英文各取不同意象：繁中沿用星蹄，英文沿用雷電。定案值見 [creatures.md 英文命名](../design/creatures.md#英文命名)。A3 提及的兩個 `_strider` ID 保留；其玩家顯示名為 `Clepsward`／`Rimbraven`，目前沒有同名。

全部保留舊名、星線整線改取星象詞根兩方案未採用。

## 已完成的驗證

- 正式 `Data/` 與 13 批合併草稿各自執行 `tools/validate_data.py`，皆為 0 錯誤、0 警告；合併草稿含 30 宙獸、209 招式、93 特性、42 關係邊、6 星球類型。依第二輪範圍排除 P5-03 第二批及正反測試樣本。
- 改名前逐 ID 確認三筆舊名；改名後，30 個宙獸英文名以不分大小寫比較皆唯一，`-vast` 結尾只剩月線 `Nivervast`。
- 兩份文案與改名前快照結構比對，只有三個 `name_en` 改變；ID、繁中名、圖鑑及其他欄位皆相同。
- forge 重產後與快照結構比對，只有 `star_storm_colt.name_en` 改變；生成器目前只收錄御三家三線，水／冰線文案已同步但不在此產物內。
- 兩專案 `Data/_incoming/` 與 `Data/retired-ids.txt` 以 `git diff --no-index --ignore-cr-at-eol` 比對一致。
- 驗證紀錄：`Logs/p5-08-naming-14bl0tp7/{verification,applied-verification}.json`（本機暫存，不納入版控）。名稱未做外部作品撞名調查，不代表命名權利審查。

## 變更範圍

兩份 `copy-terra.json` 僅改三個 `name_en`，依 N8 同步 forge 副本並重產 `designed.js`。顯示名調整不產生退役 ID。Q-C25 已從待決問題移除；原始兩輪掃描報告保留作為歷史紀錄。

本次為 A3／A4 定向複驗，P5-08 維持待審；A5／A10／A11／A12／A18 與 N7 剩餘人工事項尚未結案。
