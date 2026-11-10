# D0-E2 第三輪窄範圍唯讀確認（SOL）

2026-11-10 · ChatGPT Sol · `review/sol-d0-e2-r3` · 基準 `d24db9f6a2eb89ee229e19936cf4ecf157ed8569`。

**PASS：0 high／0 med／0 low。r2 的 E2-SOL-01／02／05 關閉；MR／MF／MD／MO 均被對應新測試殺死，每次逐 byte 還原後全綠。**

範圍僅限 d24db9f 新增的五個 NUnit 案例（四個方法，field 含 power 1／0）與 baad68b 的退役 geo `.meta` 清理、六階 runtime balance staging。依 `AGENTS.md`、`CLAUDE.md`、相關規約及 [r2 報告](D0-E2-territory-sol-r2.md) 執行；未開 sub-agent，未重審其他項目。

## 回歸與突變

所有突變均單獨套用 r2 的原始變換，使用真正 Unity 6000.3.28f1 EditMode／`-nographics`，不是 stub 或作者既有結果。工作樹 Assets／Data 不寫入突變；repo 外副本的 1,488 個追蹤輸入（Assets、Data、Packages、ProjectSettings、tools）事先逐 byte 核對相同。每次只改副本 `Encounter.cs`，以原始 bytes 還原後重跑同一 focused 範圍：`EncounterTerritoryTests;EncounterTests;EncounterForwardTests;TangentTowardTests`。

| 突變／原項目 | 變換 | 突變結果 | 還原結果 |
|---|---|---|---|
| MR／E2-SOL-01 | 僅 Rect 恢復未 Engaged 野生排除 | 59 pass／1 fail | 60/60，0 fail |
| MF／E2-SOL-02 | 僅 Field 成功 Hit 不設 Engaged | 58 pass／2 fail | 60/60，0 fail |
| MD／E2-SOL-05 | 已 Downed 的後續 Tick 額外抽一次 C | 59 pass／1 fail | 60/60，0 fail |
| MO／E2-SOL-05 | 僅反轉 ResolveVolume 單位迭代順序 | 59 pass／1 fail | 60/60，0 fail |

- **01／MR**：`A_front_strike_aimed_at_an_engaged_wild_also_hits_and_engages_a_timid_one_behind_it` 因未觀察到 timid 的 Hit 失敗。原碼以真實 Tick 見證 front 接觸未交戰野生，補上 r2 的矩形路徑缺口。
- **02／MF**：`A_field_hit_engages_a_timid_wild(1)` 與 `(0)` 均因 Hit 後未開戰失敗。兩案無條件要求 Hit 發生，power 0 也成功開戰，關閉原先只走 MISS 的正向分支缺口。
- **05／MD**：`After_the_player_is_downed_a_lone_wild_draws_nothing` 的 C 後繼樣本斷言失敗；原碼真實 Downed 後連續 40 Tick 無事件且不移動串流。
- **05／MO**：`A_wild_burst_resolves_targets_in_id_order_with_the_player_last` 的逐目標順序斷言失敗；原碼要求 `[a.Id, b.Id, PlayerCharacterId]`。這是直接固定順序的 oracle，可捕捉同 seed 自我重播抓不到的反轉。

以上關閉判斷限定本輪指定的四個存活突變及五個回歸案例，不推論未執行的其他突變。

## baad68b 核對

- `_write_geo` 只對來源集合中已退役的 `.meta.json`／`.height.bytes`／`.water.bytes` 刪除本體及其對應 `.meta`；現役 GUID metadata 保留。既有 `test_demo_merge.py` 的 17 案全數通過。
- 另外將真實輸入 staging 到 repo 外：12 個 geo template／36 個來源檔內容一致，36 個現役 `.meta` 逐 byte 保留；植入的 3 個退役本體及 3 個 `.meta` 均清除，無關檔案保留，第二次 staging 不改現役檔 mtime。沒有在原工作樹執行 merge 寫入。
- baad68b 與目前 HEAD 的 `Assets/_Project/Data/Copy/balance.json` 均與各自 `Data/balance.json` **逐 byte 相同**；使用目前 merge 重新 staging 到外部輸出的 balance 也相同。SHA-256：`c4a85a0b697d32355301bd2c64409c137a30cb87a4853614729d3d3de1044be6`。
- `ai.territory_mm` 六階一致：timid=0、cautious=4000、normal=8000、territorial=14000、fierce=20000、apex=28000（mm）。baad68b 正確恢復退役 metadata 清理並消除三階 runtime 副本落後。

## 本輪實跑驗收

| 執行 | 結果 |
|---|---|
| Unity 6000.3.28f1 EditMode 全套，`-nographics` | 1011 passed／0 failed／0 skipped；exit 0，75.346 s |
| Unity 6000.3.28f1 PlayMode 全套，`-nographics` | 87 passed／0 failed／0 skipped；exit 0，25.898 s |
| 指定 Python 3.13 `-m pytest tools` | 192 passed；exit 0，11.78 s |
| 新增 territory 案例 | 5/5 原碼通過；四突變共使對應 5 案失敗 |
| MR／MF／MD／MO 還原 | 四次 focused run 各 60/60，0 failed／0 skipped；exit 0 |

Unity 執行檔：`C:/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe`。全套使用本工作樹，命令為 `-batchmode -nographics -projectPath <repo> -runTests -testPlatform <EditMode|PlayMode> -testResults <out>/<mode>.xml -logFile <out>/<mode>.log`。

Python 執行檔：`C:/Users/user/AppData/Local/Programs/Python/Python313/python.exe`；命令為 `-m pytest tools`，`PYTEST_DEBUG_TEMPROOT=<out>/pytest-temp`。

產物根目錄 `<out>`：`C:/Users/user/AppData/Local/Temp/cosmimera-d0-e2-sol-r3/`。保留 `editmode.xml`／`playmode.xml` 及 logs、`pytest.log`、各 `MR|MF|MD|MO.xml`／`*-restored.xml` 與 logs、`native-mutations.json`、各突變 `.patch.txt`、`mutation-final.json`、`staging-audit.json`、protected-hashes 與重現腳本 `run_full.py`／`prepare_copy.py`／`run_mutations.py`／`audit_staging.py`。

原工作樹 Assets／Data 的 1,431 個檔案在全套及突變確認結束後 SHA-256／檔案集合皆與執行前一致；永久變更僅本報告與 phase-2 末尾一行交接。
