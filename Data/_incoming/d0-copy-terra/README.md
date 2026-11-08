# D0 Demo 文案初稿

本批提供區域詞綴、區域主題、主線任務模板、星球 BOSS 稱號與 UI 的繁中／英文文案，供 D0 整合、人工審核與日文翻譯使用；JSON 為待審初稿。

沿用「宙獸」、「收服」、「輪替」、四個戰鬥指令及三種收服道具名稱。英文以自然遊戲用語表達；稱號放在宙獸名前。晨昏星使用星球類型草稿的 `twilight_planet`。迷霧沒有指定數值，因此只描述視距與 HP 顯示效果。`affix_pack` 的 +1 指菁英群數量；任務步驟以 `steps` 的類型及三個 `variants` 表示，主線標題與開場放在 `chains`。

`demo-ui-supplement.json` 是依目前 Demo 畫面補寫的待審稿：六種星球名稱沿用 P3 星球類型草稿，十種屬性供選星及任務使用；另有主力選擇、道具切換、指令鍵與放棄確認提示。主力選擇是選擇已在場的主力，與 Tab 輪替不同。放棄提示對應再次按 Esc 的三秒期限，收服結果仍會帶回大廳。此檔尚未接入 `merge.py` 或 Demo，亦未有日文翻譯。

整合交接：選星畫面目前顯示星球 ID 與 `Element` enum；任務的 `{element}` 也顯示 enum，請依實際 ID 對照本檔，勿直接向玩家顯示內部識別字。HUD 目前列出 `[F/R/T/G]` 卻只顯示當前指令，未說明各鍵；`[1/2] ★` 與 `[V]` 也未說明用途。放棄提示目前只附 `(Esc)`，未說明需在三秒內再按。既有日文初稿未併入執行期資產，`DemoCopy` 目前僅支援繁中與英文；需由實作者完成接線，再交 FLASH 與人工審核。

驗收：執行 `python Data/_incoming/d0-copy-terra/check.py`，結果如下：

```text
PASS: 5 UTF-8 JSON files; localized fields are complete
PASS: 14 affix IDs and both-language numerical descriptions (fog has no specified number)
PASS: 8 region themes and description length limits
PASS: 5 step types, 3 variants each, required placeholders, 3 chains
PASS: 6 source planet types and 3 epithets each
PASS: 40 UI keys, naming and required items, capture placeholders
```
- 2026-11-09 · TERRA · done · 修正 ui.lobby.party_hint、reserve、move_up、remove_from_party、add_to_party、party_min、party_full 繁中文案。 · 下一步：人工審核
