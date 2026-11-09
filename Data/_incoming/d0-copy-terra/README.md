# D0 Demo 文案初稿

本批提供區域詞綴、區域主題、主線任務模板、星球 BOSS 稱號與 UI 的繁中／英文文案，供 D0 整合、人工審核與日文翻譯使用；JSON 為待審初稿。

沿用「宙獸」、「收服」、「輪替」、四個戰鬥指令及三種收服道具名稱。英文以自然遊戲用語表達；稱號放在宙獸名前。晨昏星使用星球類型草稿的 `twilight_planet`。迷霧沒有指定數值，因此只描述視距與 HP 顯示效果。`affix_pack` 的 +1 指菁英群數量；任務步驟以 `steps` 的類型及三個 `variants` 表示，主線標題與開場放在 `chains`。

`demo-ui-supplement.json` 已經由 `merge.py` 接入 Demo：包含星球／屬性名稱、隊伍順序、戰鬥指令及放棄確認提示。2026-11-09 補入 demo 必需的方向引導、收服倒數與基本操作提示；B 切換收服道具，V 只切換視角。新增九鍵與隊伍提示由 TERRA 同步補日文初稿至 `d0-copy-flash-ja/`，待 FLASH 交叉審核。

日文初稿尚未併入執行期資產，`DemoCopy` 目前僅支援繁中與英文。隊伍提示以實際出戰規則為準：前兩隻開場出戰、前四隻可輪替，第五、六隻本場待命。

驗收：執行 `python Data/_incoming/d0-copy-terra/check.py`，結果如下：

```text
PASS: 6 UTF-8 JSON files; localized fields and placeholders are complete
PASS: 14 affix IDs and both-language numerical descriptions (fog has no specified number)
PASS: 8 region themes and description length limits
PASS: 5 step types, 3 variants each, required placeholders, 3 chains
PASS: 6 source planet types and 3 epithets each
PASS: 41 UI keys, naming and required items, capture placeholders
PASS: 39 supplement keys; no base-key collisions
```
- 2026-11-09 · TERRA · done · 修正 ui.lobby.party_hint、reserve、move_up、remove_from_party、add_to_party、party_min、party_full 繁中文案。 · 下一步：人工審核

探索操作已接 `DemoRun` action map。`command_bindings`、`abandon_binding_confirm` 及 `gamepad_help` 支援手把提示；收服及任務完成提示改用 binding 佔位符，避免固定顯示鍵盤按鍵。三語初稿共 163 個本地化欄位，日文仍待交叉審核與執行期接線。
- 2026-11-09 · TERRA · done · 補充存檔失敗、素材上限與重試文案 · 下一步：FLASH 補 ja
