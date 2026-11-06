# P5-05 moves batch 05／traits batch 03 第二審 · TERRA

> 2026-11-06 · ChatGPT Terra（`codex exec -s read-only -m gpt-6.1-terra`），初稿來源 Gemini Flash。唯讀；未修改檔案。Claude Opus 以腳本核對下列 7 項引用的數值與文字皆屬實；README 三處文案已依建議修正，兩項數值建議待人工決定。

## 總結

第二審發現 **7 項問題**：2 項設計支配、3 項 README 文案不符、2 項命名重複或冗餘。兩批各屬性均有 3 項，未發現有根據的屬性分佈失衡或違反 Q-C2 的效果。

目標 JSON 和 README 均未提供獨立的中英文 `name`／`description` 欄位；因此本次只能審查 ID 命名和 README 文案，**無法完成玩家顯示文案的中英審查**。本次未重跑 schema、ID 或數值帶驗證，亦未修改檔案。

## 逐條發現

| ID | 類別 | 嚴重度 | 問題 | 建議修改後的具體值或文字 |
|---|---|---|---|---|
| `trait_earth_shattered_carapace` | 設計 | med | 與本批 `trait_wood_thorn_lash` 相比，兩者皆為低血量時射程 +20%；前者須低於 30%，且額外受到土抗性 -15，後者低於 35% 即生效且無副作用。若特性可自由比較，前者全面劣於後者。 | 將前者射程改為 `+25`，保留 30% 門檻與土抗性 -15，形成高報酬但有代價的選擇。 |
| `trait_light_saving_grace` | 設計 | med | 舊批 `trait_water_cleansing_tide` 同為低血量回復最大 HP 的 12%，但門檻為 30%；本項為 25%。相同觸發語意下，本項全面劣於舊項。 | 將本項回復量改為 `15`，保留 25% 門檻。HP 門檻是邊緣還是持續判定仍須由 P2-09 決定；README 提出的重複治療風險也依賴該決定。 |
| `fire_cinder_slash` | 文案 | med | README 稱「blinding enemy range」，容易解讀為致盲或降低命中；實際效果只使射程 `-20%`，持續 6 秒。 | 改為 **“an ash sweep that reduces enemy attack range by 20% for 6 seconds”**。 |
| `metal_magnet_strike` | 文案 | med | README 稱減速會將對手「pins … within melee range」；實際只是移動速度 `-20%`，不會定身或保證留在近戰範圍。 | 改為 **“slows agile opponents by 20% for 6 seconds, helping keep them within melee range”**。 |
| 10 招 `both` | 文案 | med | README 概述稱附帶「guaranteed secondary status debuff」。敵方招式仍須通過命中判定；`accuracy: 100` 亦受目標閃避影響。命中後效果如何處理仍待 P2-09。 | 改為 **“damage-plus-debuff moves; both require a successful hit”**，並保留效果處理待 P2-09 定案的註記。 |
| `wood_photosynthesis`、`trait_wood_photosynthesis` | 命名 | low | 本批招式與特性舊批使用相同英文名稱根詞，且兩者都是治療，玩家若看到相同顯示名稱容易混淆。 | 招式暫命名 **Photosynthetic Recovery／光合作用・回復**；特性保留 **Photosynthesis／光合作用**。 |
| `light_aureole_halo` | 命名 | low | `aureole` 與 `halo` 都有光暈之意，ID 名稱語意重複。 | 若仍處於可改 ID 的草稿階段，建議 `light_radiant_halo`；顯示名稱 **Radiant Halo／輝光環**。 |

## 可直接採用的建議修正

1. 將 `trait_earth_shattered_carapace` 的 `range_mod_percent.delta` 設為 **25**。
2. 將 `trait_light_saving_grace` 的 `heal_percent_max_hp.percent` 設為 **15**。
3. 依表中建議修正 README 三處文案；兩項名稱建議可供正式顯示文案及草稿 ID 定稿時採用。