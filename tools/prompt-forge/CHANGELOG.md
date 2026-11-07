# Changelog

格式：`日期 · 元件@版本 · 變更`。版本規則見 README.md。

- 2026-11-07 · tool@1.8.0 · catalog@2.0.0 · template@(creature-concept@4.1.0, character-concept@2.2.0, model-rig@3.2.0) · 外觀基底大擴充、單一子類化、XS體型與組合、文化原型非人形轉化、命名格式規範與御三家全欄位補全：
  - **外觀基底父類別追加與拆解**：追加自然·星體、物質·珠寶、物質·礦物、造物·積木、裝備·兵器、裝備·防具；植物拆解為樹木/神木、花卉/多肉、草本/藤蔓、食蟲/肉食、蕈菇/真菌、水生/藻類 6 個獨立父類別；古生物拆解為獸腳恐龍、植食恐龍、翼龍/飛行爬蟲、古海洋爬蟲/巨魚、早古節肢/無脊椎、新生代史前哺乳 6 個獨立父類別；幻想生物按文化分拆為東亞神獸、歐洲奇幻、北歐/塞爾特、中東/西亞、美洲神話、海洋/極地、異界/異形 7 個獨立父類別。
  - **子類別單一化與差異擴充**：徹底移除斜線複合多種生物的子類別，全數拆為單一具體物種/裝備/實體，大幅增加跨類別差異與多樣性。
  - **體型變化加入 XS 與多樣組合**：加入 XS 體型（< 0.3m、3–5k tris、512² 貼圖），配套撫摸動作 `pet_xs` 與常態對數生成數值；擴充 14 種體型進階變化組合（涵蓋微型妖精、標準成長至泰坦巨靈）。
  - **文化原型非人形化轉化**：110+ 文化原型全面重寫為神話象徵、神獸聖物、自然天象、法寶神兵與精神意義，並在 prompt 明確規範「嚴禁生成為人形角色，必須動物化/獸化/構裝化」。
  - **角色與宙獸命名任務與格式規範**：Prompt 明確要求 AI 依原創命名格式生成中英文名稱（已命名定案者原樣保留），繁中名 2~4 字、英文名 PascalCase，並同步於 concept.json 格式規範。
  - **御三家全欄位補全**：補齊日月星三線 4 型態全部尺寸重量數據（幼/少/成/超載）、配色語言、象徵意象與精神意義、英文圖鑑文案、命名由來與超載必殺完整規格，定案型態 prompt 同步輸出完整概念欄位無遺漏。

- 2026-11-07 · tool@1.7.0 · catalog@1.3.0 · template@(creature-concept@4.0.0, character-concept@2.1.0, model-rig@3.1.0) · Prompt Forge 與成果審查台全面升級：
  - **Prompt 精簡化**：大幅移除與立繪生成無關的戰鬥系統規範、遊戲引擎細節、voice lines 等冗餘段落，使生成焦點回歸角色與宙獸立繪。
  - **色系多選機制**：角色服裝、頭髮色系與宙獸外觀配色改為 chips 多選方式；宙獸色系由屬性（elements）嚴格限定並排除不合屬性之配色。
  - **宙獸型態獨立與分開打包**：四種型態（幼年、少年、成年、超載）分開生成獨立 prompt、獨立交付包（`cr-*-<stage>.zip`），img-to-3D 獨立處理。
  - **審查台自動分組與型態切換**：依交付包名稱自動識別並聚合至同一個主體名冊項目，支援 `[幼] [少] [成] [超]` 快速切換與舞台型態按鍵切換。
  - **移至備份區時間戳**：審查台新增移至備份區功能，自動附帶 `YYYYMMDD_HHmmss` 時間戳記（`Art/_backup/<task_id>_<timestamp>`），支援一鍵複製 PowerShell 搬移指令。
  - **御三家超載型態補齊**：日線（鏡冕天鷺）、月線（夜痕蝕月狼）、星線（星蹄雷皇驥）完整規格納入 `gen_designed.py` 與 `designed.js`。

- 2026-11-07 · template@(character-concept@2.0.0, creature-concept@3.0.0, model-rig@3.0.0) · 精簡產出圖像為三種（主立繪、單張正面正交灰底、三視圖合圖）；宙獸 4 型態改為由大到小（超載 → 成年 → 少年 → 幼年）分輪推進，每輪對話嚴格只處理 1 個型態並等待「繼續」指令，img-to-3D 比照辦理。

- 2026-11-07 · tool@1.6.0 · template@(model-rig@2.1.0) · Prompt Forge 的 img to 3D prompt 生成管線（model-rig）：
  - 來源立繪包（`source_pack`）可直接自審查台讀入的立繪開發包（`cr-*` / `chr-*`）下拉選擇，並支援快捷載入範例與 Zip；
  - 新增「指定輸入 img」選項與視覺化縮圖點選列，可精確指定立繪包內特定視圖或立繪（如正面、三視圖、主立繪等）作為 3D 建模參考；
  - 成果審查台新增「🧊 產出 3D Prompt」快捷按鈕與雙向連動，一鍵帶入當前檢閱之立繪包與當前預覽圖片至 3D 管線；
  - 當綁定審查台立繪包時，企劃概念區塊自動帶入該立繪包 `concept.json` 內容，並支援一鍵解除關聯。

- 2026-11-07 · tool@1.5.0 · template@(creature-concept@2.1.0) · 新增「已設計項目」面板：`designed.js`（由 `gen_designed.py` 從 `Data/_incoming` 批次與 TERRA 文案產生，首批為 P2-08 御三家日月星 3 條線 9 隻）一鍵產出立繪 prompt 或 img-to-3D prompt；已設計時宙獸概念不隨機、主體 ID＝`line_id`、model-rig 不再自動猜 `source_pack`（改依 subject_id 找最新立繪包）。creature-concept 對含 `locked` 的概念加上「定案 id／名稱原樣照抄、每型態 `stage_moves`」段落（新增選填段落 → minor）。

- 2026-11-07 · tool@1.4.0 · template@(model-rig@2.0.0) · 全站按鈕樣式與排版修復（解決平台選擇、名冊工具、視角標籤、鎖定鈕跑版）；新增一鍵將舊版本立繪/3D 壓縮包升級為最新版 prompt 與規格格式功能（自動補齊常態生理數值、基底追溯遷移、招式特性圖鑑化與關聯 source_pack）；確保 img-to-3d 管線與 manifest 輸出必須回溯參照立繪包（source_pack）；成果審查台新增 2D 立繪 ⇄ 3D 成果雙欄對比視圖（參考 steel_vs_swarm 機體審查台）與 3D 分類分頁；審查台新增 Prompt 項目完整顯示分頁（角色與宙獸全欄位明細及原始 prompt）。
- 2026-11-07 · tool@1.3.0 · template@(model-rig@2.0.0, creature-concept@2.0.0, move-vfx@1.1.0) · `model-rig` 併入 `animation-set`（已退役）：image-to-3D → Blender → 綁骨 → 動作在同一個 prompt；立繪包從 Google 雲端硬碟 `project/cosmos_chimera/image_in` 讀取，成品 zip 上傳 `project/cosmos_chimera/3D_out`；新增批次模式（處理 image_in 全部立繪包，每包 task_id = `<批次 id>-<subject-id>`、manifest 加 `batch_id`／`source_pack`）。宙獸/招式/特性圖鑑獨立：creature-concept 的 `signature_move_idea`／`trait_idea_zh` 改為 `signature_move{move_id,…}`／`trait{trait_id,…}`；move-vfx 加 `move_id`，宙獸改為示範施放者。validate-art 接受 model-rig 內的動作檔與批次標頭。
- 2026-11-06 · tool@1.2.0 · catalog@1.2.0 · template@(character-concept@1.2.0, creature-concept@1.2.0) · 宙獸人形基底改為類人奇幻種族（精靈、矮人、獸人、龍人、妖精、天使、惡魔、水棲人、翼人、不死、傀儡、巨人共 12 種，舊版本人形追溯至精靈族）；擴充文化原型（110+）、戰鬥定位（14類）；追加文化時間線（含遠古神話至晶石賽博、太空歌劇、超弦維度等未來時間線）；角色人種與文化地區互相獨立並擴充至 18 種表型特徵；角色與宙獸共用文化地區與文化時間線；宙獸外觀色系與角色服裝色系合併共用（1~3色組合庫）；宙獸性情與角色性格合併共用特質庫。
- 2026-11-06 · tool@1.1.0 · catalog@1.1.0 · template@(character-concept@1.1.0, creature-concept@1.1.0) · 追加角色與宙獸參數（身高/體重/體脂率常態分佈、女角色罩杯、服裝/髮/瞳/宙獸色系、階層式外觀基底、全欄位手寫自訂與未知選項）；新增成果審查台（三欄排版參考 steel_vs_swarm 機體審查台，自動掃描 Art/_incoming、支援 Zip 讀取與一鍵解壓縮）。
- 2026-11-06 · tool@1.0.0 · catalog@1.0.0 · spec@1 · 初版：七個任務模板（character-concept、creature-concept、model-rig、animation-set、move-vfx、capture-vessel、culture-research，皆 @1.0.0）；seed 可重現隨機；任務標頭與 manifest 規格；交付路徑與解壓指令。
