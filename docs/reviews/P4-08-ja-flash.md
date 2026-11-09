# P4-08 日文補充文案交叉審核報告（FLASH）

審查日期：2026-11-09 · 審查者：Gemini Flash（agy）
審查目標：[demo-ui-supplement.json](../../Data/_incoming/d0-copy-flash-ja/demo-ui-supplement.json)
來源對照：[demo-ui-supplement.json](../../Data/_incoming/d0-copy-terra/demo-ui-supplement.json) (`zh-TW` / `en`)
規範依據：[README.md](../../Data/_incoming/d0-copy-flash-ja/README.md)（固定術語對照表）

## 審查結果彙總

- 審查鍵數：14 個鍵（13 個 Terra 新增/修改鍵 + 1 個同檔關聯放棄提示鍵）。
- 通過（ok）：11 處。
- 修正（fixed）：3 處（`ui.hud.no_quest_target`、`ui.hud.quest_complete`、`ui.hud.abandon_binding_confirm`）。
- 結構與佔位符檢查：所有鍵值佔位符集合、數字集合與 `\n` 換行數與來源完全一致。

| 鍵名 | 判定 | 說明 / 修正原因 |
|---|---|---|
| `ui.lobby.party_hint` | ok | 數字（2、4、5、6）與概念（出撃・交代・待機・主力）完整保留且符合術語規範。 |
| `ui.hud.bearing_ahead` | ok | 與視角導航前方語意一致，自然簡潔。 |
| `ui.hud.bearing_left` | ok | 佔位符 `{angle}` 與角度旋轉語意一致。 |
| `ui.hud.bearing_right` | ok | 佔位符 `{angle}` 與角度旋轉語意一致。 |
| `ui.hud.bearing_unavailable` | ok | 目標距離與方向未定語意一致。 |
| `ui.hud.no_quest_target` | fixed | 依術語表將英文字「BOSS」修正為官方指定日文術語「ボス」。 |
| `ui.hud.quest_complete` | fixed | 依術語表將英文字「BOSS」修正為官方指定日文術語「ボス」（佔位符 `{capture}` 保留）。 |
| `ui.hud.exploration_help` | ok | 8 行排版（7 個 `\n`）、數字（20、8、3）與鍵盤操作指引及術語（宙獣・主力・捕獲道具）完全一致。 |
| `ui.hud.capture_available` | ok | 2 行排版（1 個 `\n`）、4 個佔位符（`{capture}`、`{name}`、`{seconds}`、`{cycle}`）及術語（捕獲・捕獲道具）完全一致。 |
| `ui.hud.capture_no_target` | ok | 佔位符 `{capture}`、距離與秒數（20、8）及捕獲提示語意一致。 |
| `ui.hud.command_bindings` | ok | 4 個佔位符（`{focus}`、`{free}`、`{follow}`、`{retreat}`）與四指令簡稱（集中／自由／追従／撤退）完全一致。 |
| `ui.hud.abandon_binding_confirm` | fixed | 結尾「持ち帰れます」修正為「持ち帰ることができます」，與同檔 `abandon_confirm` 及全域 `ui.results.abandon_run` 句式保持一致。 |
| `ui.hud.gamepad_help` | ok | 9 行排版（8 個 `\n`）、數字（20、8、3）與手把操作指引及術語（宙獣・主力・捕獲道具・ロビー）完全一致。 |
| `ui.hud.abandon_confirm` | ok | 同檔 Esc 放棄提示複審，數字「3」、按鍵「Esc」及術語句式均正確一致。 |

## 2026-11-09 複核（Terra 第二批）

| 鍵名 | 判定 | 說明 / 修正原因 |
|---|---|---|
| `ui.hud.gamepad_help` | fixed | 手把 D-pad 稱呼由鍵盤「方向キー」修正為手把標準術語「十字キー」；前綴「メニューは」對齊來源（選單：／Menus:）修正為「メニュー：」；按鈕稱呼「下ボタン／右ボタン」補上「の」（「下のボタン／右のボタン」），與同字串第 3、4 行（下のボタン／上のボタン／右のボタン）維持完全一致。9 行排版（8 個 `\n`）、數字（20、8、3）與無佔位符狀態皆與來源完全一致。 |
| `ui.lobby.language` | ok | 「言語」用語準確自然；行數（無 `\n`）與佔位符（無）與來源完全一致。 |
