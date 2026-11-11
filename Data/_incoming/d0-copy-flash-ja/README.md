# D0 Demo 文案日文翻譯初稿

本目錄提供 D0 Demo 區域詞綴、區域主題、主線任務模板、星球 BOSS 稱號與 UI 字串的日文翻譯初稿（對應 `Data/_incoming/d0-copy-terra/` 的繁中與英文版本）。
資料格式維持完全一致的 JSON 階層與鍵值，僅將 `{"zh-TW": ..., "en": ...}` 轉換為 `{"ja": ...}`。

## 固定術語對照表

| 原文（zh-TW） | 英文（en） | 日文（ja） | 說明 / 規範 |
|---|---|---|---|
| 宙獸 | Cosmimera | 宙獣（ちゅうじゅう） / 宙獣 | 僅在 `ui.lobby.choose_starter` 初次出現時標記讀音「宙獣（ちゅうじゅう）」，其餘統一寫為「宙獣」 |
| 收服 | Capture | 捕獲 | 遊戲核心捕獲行為 |
| 輪替 | Switch | 交代 | 戰鬥在場換人 |
| 符籙卡片 | Talisman Card | 符籙カード | 收服道具 1 |
| 琉璃珠 | Glass Bead | 瑠璃玉 | 收服道具 2 |
| 魔術方塊 | Magic Cube | マジックキューブ | 收服道具 3 |
| BOSS | Boss | ボス | 首領、星球 BOSS |
| HP | HP | HP | 生命值，原樣保留 |
| CD | Cooldown | CT | 冷卻時間（クールタイム） |
| 稱號 | Epithet | 〜の | 接在宙獸名前之形式（如「森冠の」「赤岩の」），不寫成獨立全名 |

## 檔案清單與結構

1. `affixes.json`：14 個區域詞綴（10 種屬性湧現、狂奔、群聚、迷霧、豐收）。
2. `region-themes.json`：8 個區域地形主題（遺跡、森林、河岸、高地、海岸、クレーター、氷棚、峡谷）。
3. `quest-templates.json`：5 類任務步驟（每類 3 個變體）與 3 條主線任務鏈。
4. `boss-epithets.json`：6 種星球類型各 3 個 BOSS 稱號（全數採「〜の」形式）。
5. `strings.json`：41 個 UI 字串鍵值（大廳、選星、HUD、道具、結算）。
6. `demo-ui-supplement.json`：78 個補充 UI 鍵值（星球名稱、屬性名稱、戰鬥 HUD、大廳隊伍、素材與進化、目標切換、近身互動、性格與階段、玩家等級與 BOSS 解鎖提示等）。
7. `check.py`：同步驗證來源與輸出的比對腳本。

---

## 驗收結果

`python Data/_incoming/d0-copy-flash-ja/check.py` 驗收結果：**ALL PASS**（6 檔共 202 欄位結構、佔位符、數字與術語完全相符）。

比對細項與欄位數：
- `affixes.json`：14 筆詞綴（name + desc = 28 個欄位）。
- `region-themes.json`：8 筆區域主題（name + desc = 16 個欄位）。
- `quest-templates.json`：5 種步驟類型各 3 個變體（15 欄位）+ 3 條任務鏈（title + intro = 6 欄位），共 21 個欄位。
- `boss-epithets.json`：6 種星球類型各 3 個稱號，共 18 個欄位。
- `strings.json`：41 個 UI 字串鍵值（共 41 個欄位）。
- `demo-ui-supplement.json`：78 個補充 UI 鍵值（共 78 個欄位）。

總計比對欄位數：202 個本地化欄位。

---

## 補充檔案驗收：demo-ui-supplement.json（78 鍵值）

- 鍵值數量：78 個鍵值（共 78 個本地化欄位）。
- `check.py` 比對結果：全部通過（鍵值集合、鍵值順序、無多餘/缺漏佔位符、數字與術語完全相符）。
- 既有鍵值校稿處置：
  - `ui.hud.target_cycle`（`{key} ターゲット切替：{target}`）與 `ui.hud.target_none`（`なし`）：用語簡潔明確，符合日文遊戲 HUD 慣例與上下文，予以保留。
- 本次新增 3 個鍵值（D0-B2 玩家等級 / BOSS 階段解鎖提示）：
  - 玩家等級（1 欄位）：
    - `ui.lobby.player_level`：「`プレイヤーレベル LV{level}（EXP {exp}／{next}）`」。「玩家」依規譯為「プレイヤー」，「經驗」比照 HP、CT 及 LV 等 UI 縮寫規範對齊 en 版之「EXP」，佔位符與全形分隔號完整保留。
  - 結算玩家經驗（1 欄位）：
    - `ui.results.player_exp`：「`プレイヤーEXP +{exp}（LV{level}）`」。「玩家經驗」精簡對齊為「プレイヤーEXP」，符合結果畫面呈現慣例。
  - 選星 BOSS 階段解鎖提示（1 欄位）：
    - `ui.star_select.boss_unlock_hint`：「`プレイヤー LV{adolescent} で少年期ボス、LV{adult} で成年期ボスが解放`」。「玩家」譯「プレイヤー」，BOSS 譯「ボス」，階段沿用既有 `ui.stage` 規範「少年期」「成年期」，解鎖採標準日系遊戲提示語態「〜で…が解放」。
- 既往新增 17 個鍵值（D0-W3 / D0-B2 / D0-W4）：
  - 互動提示 HUD（1 欄位）：
    - `ui.hud.interact_prompt`：「`{key} インタラクト：{target}`」，符合日文主流動作／RPG 遊戲互動 HUD 慣例。
  - 近身互動選單與回饋（6 欄位）：
    - `ui.interact.observe`：「`観察`」。
    - `ui.interact.provoke`：「`挑発`」，標準開戰挑釁用語。
    - `ui.interact.pet`：「`なでる`」，採用寵物／怪獸互動常見平假名。
    - `ui.interact.close`：「`閉じる`」。
    - `ui.interact.observe_detail`：「`{species}｜{stage}｜{element}｜性格：{temperament}｜Lv {level}`」，佔位符與全形分隔號完整保留。
    - `ui.interact.pet_reaction`：「`{target}は静かにあなたの手にすり寄ってきた。`」，通順自然之動物親暱反應。
  - 成長階段（3 欄位）：
    - `ui.stage.juvenile`：「`幼年期`」。
    - `ui.stage.adolescent`：「`少年期`」。
    - `ui.stage.adult`：「`成年期`」。
    - 均統一以「〜期」三字對齊，符合怪獸成長階段日文慣例，避免「少年」或「成年」單詞在日文中產生物種指涉歧義。
  - 野生性格梯度（6 欄位，timid 0m → apex 28m）：
    - `ui.temperament.timid`：「`臆病`」（0m 不主動開戰）。
    - `ui.temperament.cautious`：「`警戒`」（4m 近身警戒自衛）。
    - `ui.temperament.normal`：「`沈着`」（8m 常規沉穩守備）。
    - `ui.temperament.territorial`：「`縄張り`」（14m 強烈領域防衛，日文生態與遊戲標準用語）。
    - `ui.temperament.fierce`：「`凶暴`」（20m 主動遠距獵捕）。
    - `ui.temperament.apex`：「`覇者`」（28m 頂級掠食霸主威脅）。
    - 6 階性格梯度強弱分明，符合野生領域 AI 設計梯度。
  - 選星卡片 BOSS 階段（1 欄位）：
    - `ui.star_select.boss_stage`：「`ボス：{stage}`」，術語「ボス」與佔位符完整保留。

- 2026-11-10 · FLASH · done · 完成 D0-W3/B2/W4 17 鍵 ja 初稿，校稿保留 target_cycle/none，check.py 199 欄 ALL PASS · 下一步：OPUS 驗收
- 2026-11-10 · FLASH · done · 完成 D0-B2 玩家等級與 BOSS 階段解鎖提示 3 鍵 ja 初稿，check.py 202 欄 ALL PASS · 下一步：OPUS 驗收

---

## 2026-11-11 文案同步校稿（對應 TERRA 2026-11-11 改動）

對應 `Data/_incoming/d0-copy-terra/changed-keys-20261111.txt` 3 個鍵之日文同步處置：
- `ui.interact.observe_detail`：「`{species}｜{stage}｜{element}｜性格：{temperament}｜Lv {level}`」→「`{species}｜{stage}｜{element}｜性格：{temperament}｜LV{level}`」，等級標記格式對齊全域統一規範（「Lv {level}」→「LV{level}」）。
- `ui.temperament.territorial`：保留「`縄張り`」，日文「縄張り」在生態與遊戲 UI（置於「性格：」後）已明確表示領域意識習性，且 3 字簡潔符合野生性格梯度標籤規範。
- `ui.star_select.boss_unlock_hint`：「`プレイヤー LV{adolescent} で少年期ボス、LV{adult} で成年期ボスが解放`」→「`プレイヤー LV{adolescent} で少年期ボス、LV{adult} で成年期ボスが出現可能`」，語意由「解鎖（解放）」改為「可能出現（出現可能）」，忠實反映 BOSS 階段均等抽選機制並對齊 en（can appear）與繁中（可抽到）。

### 驗收結果

`python Data/_incoming/d0-copy-flash-ja/check.py` 輸出摘要：
```text
PASS: affixes.json (28 localized fields verified)
PASS: region-themes.json (16 localized fields verified)
PASS: quest-templates.json (21 localized fields verified)
PASS: boss-epithets.json (18 localized fields verified)
PASS: strings.json (41 localized fields verified)
PASS: demo-ui-supplement.json (78 localized fields verified)
ALL PASS: 6 files, 202 localized fields match perfectly.
```

- 2026-11-11 · FLASH · done · 同步 3 鍵 ja 文案（1 改等級標記、1 改出現可能語意、1 保留縄張り），check.py 202 欄 ALL PASS · 下一步：OPUS 驗收
