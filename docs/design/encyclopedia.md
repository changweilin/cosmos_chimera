# 角色、宙獸與技能圖鑑

## 內容與來源
- 遊戲與 `cosmos_chimera_forge` 共用 `Data/schemas/encyclopedia.schema.json`；內容置於 `Data/encyclopedia/*.json`，AI 初稿先放 `Data/_incoming/<batch>/encyclopedia/`。
- 每筆以永久 `id` 識別；`subject_id` 對應 Forge 主體，`game_id` 對應遊戲定義。舊 ID 與設計 ID 必須依來源明確對照，禁止按名稱猜測。尚未確認對照時 `game_id: null`。
- 宙獸以主體加四階型態獨立建檔；角色與技能沒有型態。取得一個型態不解鎖其他型態。
- 文字依同一主體／型態的 Drive wiki、立繪、GLB 與設定整理，保留來源路徑、Drive file ID 與 SHA-256。缺漏內容可依造型與世界觀補寫，必須逐欄記入 `authored_fields`、關聯獨立待審設計批次，不冒充 wiki 原文。
- 不覆寫既有 wiki，不因建立圖鑑而核准任何立繪或模型。新內容為 `draft` 或 `review_required`，人工審核後才可為 `approved`。
- 未完成補寫的文字明確存 `null`，不生成佔位敘述或零數值。資料缺漏、待審設計與玩家尚未解鎖是不同狀態。

## 欄位
- 基本資料：中英文名稱、外觀描述／招式描述、簡介。
- 宙獸完整資料：生態學、習性、棲地；幼年記生育／幼體照護，其他階段記成長與階段變化。
- 角色完整資料：職業、生平、種族、身材、喜好、厭惡。種族與既有 `ethnicity` 分開，不互相推定。
- 技能完整資料：招式描述、功能與效果、生活效果、具體數值。區分主動技、被動技與生活能力。
- 技能數值以 `Data` 的遊戲定義為準，圖鑑只保存 JSON pointer 引用，顯示時讀取當前數值，避免平衡改動後文案過期。wiki 提及但遊戲尚無實作的效果須標示為設定，不能顯示成已實裝效果。
- `sources` 保留證據；`assets` 透過來源 ID 關聯 wiki、立繪、GLB，不複製私人 Drive URL 或登入憑證。

## 玩家解鎖
玩家進度與內容資料分開保存，以 `CodexKind` 加永久遊戲主體 ID 為 key，透過 `game_id` 對應內容。新主體為 `unknown`；**當前三位角色（葉澄汐 `chr_yeh_chengxi` → 洛恩 `chr_roen` → 雨森澄羽 `chr_amemori_sumiha` → 葉澄汐）皆作為玩家起始角色之一，依環狀順序決定關係：選擇其中一人後，下一順位為好友、再下一順位為宿敵**（不開局解鎖）：
- **自身角色**：新手任務完成後解鎖自己的角色圖鑑。
- **好友角色**：遊戲上半場完成後解鎖朋友的角色圖鑑。
- **宿敵角色**：遊戲下半場完成後解鎖宿敵的角色圖鑑。
- **戰鬥旅人職業與地形試煉主**：解鎖條件見 [trainer-battles.md](trainer-battles.md)（職業累積對戰 10 次；試煉主完成試煉）。
- **其他遊戲角色**：為**所有生活技能的解鎖任務**建立角色圖鑑；其中**進階地形解鎖**、**一條食物製作路線**、**一條裝備製作路線（武器／防具）**與**飛行技能任務**為主線，其餘為支線。

### 十屬性生活技能組合角色任務（食物／武器／防具路線＋飛行）
以**食物（釣魚 → 冷凍 → 清蒸鱸魚）**、**武器（單／雙手武擇一，無屬性）**、**防具（上／下身擇一，無屬性）**各自的「採集 → 加工 → 製作成品」加上**飛行**，組成剛好涵蓋十種屬性的宙獸生活技能組合，並為各階段解鎖任務建立對應角色圖鑑：

| 任務線 | 階段 | 屬性 | 宙獸生活技能 | 任務目標／產出 | 主／支線 |
|---|---|---|---|---|---|
| 食物路線 | 採集 | 水 `water` | `fishing` | 海面／河流釣取**生鱸魚** | 主線 |
| 食物路線 | 加工 | 冰 `ice` | `freeze_prep`（急凍保鮮） | 將生鱸魚冷凍為**冷凍鱸魚**（保鮮／冷製處理；多路線設計見 [field-utilities.md#食物多路線](field-utilities.md#食物多路線)） | 主線 |
| 食物路線 | 製作成品 | 火 `fire` | `cook_fire` | 生火烹飪**清蒸鱸魚**（回復隊伍 HP 並賦予戰前食物 Buff） | 主線 |
| 武器路線 | 採集 | 土 `earth` | `drop_rate_up` | 採集煤、晶石與原礦 | 主線 |
| 武器路線 | 加工 | 雷 `thunder` | `craft_smelt_thunder` | 高壓電弧熔煉（原礦＋煤 → 礦錠） | 主線 |
| 武器路線 | 製作成品 | 金 `metal` | `magnet_pickup_double`／`craft_forge_metal` | 鍛造**無屬性武器**（單手劍／雙手大劍擇一，見下） | 主線 |
| 防具路線 | 採集 | 木 `wood` | `forage_berries` | 採集麻纖維與棉花 | 主線 |
| 防具路線 | 加工 | 影 `shadow` | `craft_weave_shadow` | 紡織加工（纖維 → 布料） | 主線 |
| 防具路線 | 製作成品 | 光 `light` | `craft_tailor_light` | 裁縫製作**無屬性防具**（上身戰衣／下身護腿擇一，見下） | 主線 |
| 探索機動 | 飛行 | 風 `wind` | `fly` | 高空滑翔／飛越地形高低差與海面 | 主線 |

#### 無屬性武器／防具製作規格（主線任務成品，數值暫定）
不附帶屬性專精或元素抗性，供所有屬性宙獸通用；`weapon.element` 需於實作時擴充支援 `"none"`（無屬性，`element_mastery: 0`）。
- **武器（二擇一）**：
  - **無屬性單手劍**（`one_handed`，目標 `creature`）：礦錠 ×2＋木材 ×1。倍率 `1.0`、攻速 `1.1`，提供命中率或暴擊率加成（無屬性專精）。
  - **無屬性雙手大劍**（`two_handed`，目標 `creature`）：礦錠 ×4＋木材 ×2（單手製作需求加倍）。基礎倍率同單手（+35% 雙手傷害＝135% DPS）、攻速 `0.9`，提供單手 2 倍的暴擊傷害或失衡值加成（無屬性專精）。
- **防具（二擇一）**：
  - **無屬性上身戰衣**（`upper`，目標 `both`）：布料 ×2＋縫紉針（不消耗）。玩家最大 HP +10%、陰／陽守加成；宙獸格擋減免／失衡上限加成（無元素抗性）。
  - **無屬性下身護腿**（`lower`，目標 `both`）：布料 ×2＋縫紉針（不消耗）。玩家移速 +5%、翻滾冷卻 −5%；宙獸移速／閃避加成（無元素抗性）。

同一主體不同宙獸型態分開記錄。

| 類別 | `unknown` → `basic` | `basic` → `complete` |
|---|---|---|
| 宙獸 | 初次遭遇該型態 | 捕獲該型態 |
| 角色 | 初次與該角色對話 | 完成該角色任務 |
| 技能 | 首次目擊該技能施放 | 取得該技能 |

- `unknown` 不公開名稱、外觀、文字、數值及關係網中的未解鎖名稱；搜尋與連結也不能繞過限制。
- `basic` 公開名稱、外觀／招式描述及簡介；完整欄位與具體數值保持鎖定。
- `complete` 公開全部已有依據的資料。來源缺漏仍顯示待補，不當成已解鎖內容。
- 完成條件先於基本條件發生時可直接成為 `complete`；重複事件冪等，狀態只升不降。失去持有物不清除歷史進度。
- 已持有的御三家、孵化與進化所得型態視為取得，開放完整資料及其技能；舊存檔只從仍持有的個體重建，不虛構過去遭遇。存檔 v10 儲存歷史知識，退役 ID 仍保留。
- Demo 以現行戰鬥解除距離（40 m）內的活動宙獸作為遭遇／目擊範圍，遠處生成與施放不揭露。角色對話與任務事件透過 `RecordCharacterDialogue`／`RecordCharacterQuest` 接入；三位起始角色（選角新手任務／上半場好友／下半場宿敵）與生活技能解鎖任務 NPC 本次只保留接口，待選角存檔、NPC／任務系統實作再接線，不新增劇情任務。
- Forge 編輯／審查使用內容全覽，不以作者是否在遊戲中遭遇作為編輯限制；可預覽三種玩家解鎖狀態。

## 整合界線
- 遊戲不得在執行時要求玩家登入作者的 Drive。經驗收的內容與媒體透過現有 importer／美術匯入管線打包；Forge 使用既有 Drive 授權讀取素材。
- 匯入應拒絕重複圖鑑 ID、重複主體／型態、來源 ID 懸空、跨型態素材與無法解析的數值引用。
- 同次作業同步兩邊 schema 與內容；來源尚不可讀時只交付結構，不以本機舊素材冒充 Drive 最新版本。
- Forge 的私有分類索引位於 Drive `characters/encyclopedia.json`、`creatures/encyclopedia.json`、`moves/encyclopedia.json`，皆為共用 schema 的陣列；數值快照在 `moves/encyclopedia-values.json`。這些是分類索引，不替代主體資料夾的 wiki／美術交付物。公開 Pages 只部署讀取介面，不部署私有內容。

## cr_* 遊戲資料草稿
- 15 條 Drive 系列 × 四階共 60 型態納入 demo；主／副屬性依來源，冰刀企鵝為冰／水，墨影碑靈為影／土。數值與候選棲位為新設計草稿，不是從 GLB 推算出的生物事實。
- 六能力值依 `balance.json#stat_budget` 400／500／600／720 分配，沿用既有同屬性原型的技能、抗性與候選棲位，保留 `concept.gameplay_archetype` 供審查；不宣稱已實作 wiki 中的全部武裝效果。
- 原始角色資料中同名 GLB 多版本全部保留來源，不自動選定或核准；圖鑑仍可使用獨立的主立繪。

## 重建與驗證
1. 透過 Drive 匯出精確 wiki bytes 與媒體索引（file ID、路徑、SHA-256），暫存至 `Logs/encyclopedia-drive/`。
2. `python tools/stage_encyclopedia.py` 只加入新批次的遊戲型態與名稱，不重建其他 demo 輸入。
3. `python tools/build_encyclopedia.py --wiki-cache Logs/encyclopedia-drive/wiki-cache.json --media-index Logs/encyclopedia-drive/media-index.json --authored Data/_incoming/p4-09-encyclopedia-authored-01/encyclopedia/entries.json`。
4. `python tools/validate_data.py --root Data/_demo` 驗證合併後的遊戲引用；補寫批次另以同工具驗證。`python tools/export_encyclopedia.py` 驗證數值來源雜湊與 JSON pointer，再匯出 Forge 索引。
5. Unity 選單 `Cosmimera/Data/Import Encyclopedia` 產生 Resources 中的 `EncyclopediaTable`。新遊戲型態另依既有 Data Importer 匯入；不手改 `.asset`。
