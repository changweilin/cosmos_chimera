# Changelog

格式：`日期 · 元件@版本 · 變更`。版本規則見 README.md。

- 2026-11-07 · tool@1.3.0 · template@(model-rig@2.0.0, creature-concept@2.0.0, move-vfx@1.1.0) · `model-rig` 併入 `animation-set`（已退役）：image-to-3D → Blender → 綁骨 → 動作在同一個 prompt；立繪包從 Google 雲端硬碟 `project/cosmos_chimera/image_in` 讀取，成品 zip 上傳 `project/cosmos_chimera/3D_out`；新增批次模式（處理 image_in 全部立繪包，每包 task_id = `<批次 id>-<subject-id>`、manifest 加 `batch_id`／`source_pack`）。宙獸/招式/特性圖鑑獨立：creature-concept 的 `signature_move_idea`／`trait_idea_zh` 改為 `signature_move{move_id,…}`／`trait{trait_id,…}`；move-vfx 加 `move_id`，宙獸改為示範施放者。validate-art 接受 model-rig 內的動作檔與批次標頭。
- 2026-11-06 · tool@1.2.0 · catalog@1.2.0 · template@(character-concept@1.2.0, creature-concept@1.2.0) · 宙獸人形基底改為類人奇幻種族（精靈、矮人、獸人、龍人、妖精、天使、惡魔、水棲人、翼人、不死、傀儡、巨人共 12 種，舊版本人形追溯至精靈族）；擴充文化原型（110+）、戰鬥定位（14類）；追加文化時間線（含遠古神話至晶石賽博、太空歌劇、超弦維度等未來時間線）；角色人種與文化地區互相獨立並擴充至 18 種表型特徵；角色與宙獸共用文化地區與文化時間線；宙獸外觀色系與角色服裝色系合併共用（1~3色組合庫）；宙獸性情與角色性格合併共用特質庫。
- 2026-11-06 · tool@1.1.0 · catalog@1.1.0 · template@(character-concept@1.1.0, creature-concept@1.1.0) · 追加角色與宙獸參數（身高/體重/體脂率常態分佈、女角色罩杯、服裝/髮/瞳/宙獸色系、階層式外觀基底、全欄位手寫自訂與未知選項）；新增成果審查台（三欄排版參考 steel_vs_swarm 機體審查台，自動掃描 Art/_incoming、支援 Zip 讀取與一鍵解壓縮）。
- 2026-11-06 · tool@1.0.0 · catalog@1.0.0 · spec@1 · 初版：七個任務模板（character-concept、creature-concept、model-rig、animation-set、move-vfx、capture-vessel、culture-research，皆 @1.0.0）；seed 可重現隨機；任務標頭與 manifest 規格；交付路徑與解壓指令。
