# D0 Demo 文案初稿

本批提供區域詞綴、區域主題、主線任務模板、星球 BOSS 稱號與 UI 的繁中／英文文案，供 D0 整合、人工審核與日文翻譯使用；JSON 為待審初稿。

沿用「宙獸」、「收服」、「輪替」、四個戰鬥指令及三種收服道具名稱。英文以自然遊戲用語表達；稱號放在宙獸名前。晨昏星使用星球類型草稿的 `twilight_planet`。迷霧沒有指定數值，因此只描述視距與 HP 顯示效果。`affix_pack` 的 +1 指菁英群數量；任務步驟以 `steps` 的類型及三個 `variants` 表示，主線標題與開場放在 `chains`。

驗收：執行 `python Data/_incoming/d0-copy-terra/check.py`，結果如下：

```text
PASS: 5 UTF-8 JSON files; localized fields are complete
PASS: 14 affix IDs and both-language numerical descriptions (fog has no specified number)
PASS: 8 region themes and description length limits
PASS: 5 step types, 3 variants each, required placeholders, 3 chains
PASS: 6 source planet types and 3 epithets each
PASS: 40 UI keys, naming and required items, capture placeholders
```
