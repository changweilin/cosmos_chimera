# P5-08 A9 體型一致性複驗

- 日期：2026-11-08；執行者：ChatGPT Codex。
- 對象：`data/p5-08-copy-p5-06-relations` 工作樹，以及相鄰 `cosmos_chimera_forge` 的 `catalog.js`、`gen_designed.py`、`designed.js`。
- 結論：**A9 部分修正，仍有 4 筆尺寸越界**。級距界線已一致，既有型態尺寸尚未調整；未變更資料或生成器。

## 證據

唯一規格來源為 [creatures.md 體型級距](../design/creatures.md#體型級距)：M 上限 1.4 m、L 上限 2.5 m。forge `catalog.js` 的 `SIZE_CLASSES` 已採同值。

以 Python AST 讀取 `gen_designed.py` 的 `LINES` 常數，對照本專案御三家 JSON 的 `size_class`，再解析 `designed.js` 的 `locked.stages`；未執行生成器或修改產物。9 個非超載型態中，5 筆符合區間、4 筆超過上限；生成器尺寸與已生成內容逐筆相同。

| 型態 ID | 階段 | 資料級距 | 生成尺寸 | 級距上限 | 生成器來源行 |
|---|---|---|---|---|---|
| `moon_shade_wolf` | 少年 | M | 1.45 m | 1.4 m | `gen_designed.py:59` |
| `moon_longnight_wolf` | 成年 | L | 2.6 m | 2.5 m | `gen_designed.py:56,60` |
| `star_storm_colt` | 少年 | M | 1.50 m | 1.4 m | `gen_designed.py:81` |
| `star_skyhoof_steed` | 成年 | L | 2.7 m | 2.5 m | `gen_designed.py:78,82` |

`gen_designed.py:211–212` 分別從宙獸資料取級距、從 `stage_dims` 取尺寸，沒有驗證兩者的區間關係。因此重新產生 `designed.js` 會保留矛盾；僅修改 `catalog.js` 無法修正這 4 筆。

本次不判定恰好落在共同邊界的型態歸屬；上列 4 筆皆嚴格超過原級距上限，不受端點定義影響。超載規則仍依 Q-C12，未納入本次複驗。

## 待裁決 Q-C24

1. **建議：保留資料級距，縮小生成尺寸。** 月／星少年皆改 1.4 m，成年皆改 2.5 m；成年同步改 `adult_dims` 與 `stage_dims.adult`，其餘尺寸欄位不變，再重產 `designed.js`。
2. **保留生成尺寸，升級資料級距。** 月／星少年 M → L，成年 L → XL；依雙專案同步規則更新兩份御三家資料及衍生文件，再重產 `designed.js`。這會改變美術面數預算與撫摸動作級距。

A9 既有裁決只指定級距界線的唯一來源，未指定上述兩種修法。依 `AGENTS.md` §2，先記入 `docs/open-questions.md`，等待使用者選定後實作。

## 範圍限制

這是 A9 定向複驗，不代表完成第三輪全資料集掃描。沒有重跑與此次唯讀檢查無關的 Unity／工具測試；A3／A4／A5／A10／A11／A12／A18 等項目仍依先前報告與後續交接處理。
