# Q-C32 進化素材來源：A／B 數值提案

2026-11-09 · SOL（ChatGPT Sol） · **未定案，兩份 JSON 均為 `status: proposal`、`confirmed: false`**。由人工選方案，OPUS 審查後接線 P2-05；本批只提供 Python 模型與提案，沒有修改 Unity、正式平衡表或 roadmap。

## 選擇重點

- **A：沿用 Q-C32 暫定方向，素材跟被擊倒者。** 可讓玩家獵取指定宙獸，但 D0 野生池沒有光／影／雷主專精；御三家兩段進化的素材在任何 D0 星球都拿不到。調高機率或數量不能補救零來源。若選 A，還須另外核准新增野生來源或其他來源，才具備接線後可進化的條件；本提案不偷偷補入御三家敵人、不混用 B。
- **B：沿用 creatures.md〈進化〉原文，素材跟星球偏好。** 晨昏星提供光／影、風暴星提供雷／風；十屬性都可取得。用相同機率控制比較變因，滿編御三家的素材節奏接近等級門檻，但月、日仍會稍等素材。
- 兩案均有稀疏掉落的隨機尾端；表列平均規劃值不保證每個玩家準時取得。單主力升級比滿編快很多，固定掉率無法同時貼合全部隊伍規模。若要單主力也同步，還需另選提高掉率、依進度調率或保底；本批不實作這些額外規則。

## 盤點邊界與輸入

宙獸取 `Data/` 下所有 `creatures/*.json`，**排除路徑含 `_incoming` 的任何檔案**。目前沒有 `Data/creatures/`；符合條件的實際 D0 staged 資料只有：

- `Data/_demo/creatures/p2-08-starters-draft-01--creatures.json`
- `Data/_demo/creatures/p5-03-creatures-draft-01--creatures.json`

因此本批的「正式」指非 incoming、目前供 D0 使用的資料，沒有把它們宣告為內容人工定稿。共 10 線、30 型態；每條線只計一次由當前型態離開時的需求，成年 `evolution: null` 計零。幼年各需主屬性 tier1 ×3，少年各需主屬性 tier2 ×5；十屬性總計 tier1 30、tier2 50、tier3 0。成年獲得的副專精不倒推成新的進化材料需求；星線少年雖有風副專精，其進化資料也只要求雷。

等級門檻另讀 `Data/_incoming/p2-12-level-proposal/evolution-levels.json`，檢查覆蓋全部 20 隻非成年、與 staged 資料一致；這些門檻仍待人工確認。下方 CLI 第一、二表保留逐線需求與逐屬性總計。

星球偏好以 `docs/design/planet-world.md` 六類表為依據，從一致的 `Data/_demo/planet-types/*.json` 讀取：翠綠＝木水風、熔岩＝火土、冰晶＝冰水、風暴＝雷風、晶礦＝金土、晨昏＝光影。顯示名稱與 D0 文案的青翠／曦影差異不影響 ID 或屬性。

## 掉落規則（待選）

兩案共用：普通野生擊倒 → tier1，成功率 10%；精英擊倒 → tier2，成功率 60%；BOSS 擊倒 → tier3，成功率 100%。**成功給 1 個、失敗給 0 個**。角色互斥，BOSS 不疊普通或精英獎勵；收服成功／失敗、任務步驟／整鏈、區域拾取均不另外發素材。每個 spawn 的擊倒只發一次；收服不再發第二次。

令角色機率整數為 `c`，外部提供均勻整數 `u∈[0,99]`：`qty = 1 if u < c else 0`。屬性規則：

- A：取被擊倒物種**最高專精值**的屬性；同值取資料中先出現者，沿用 `RunPlanner.MainElement`。多專精也只產一種素材，不按百分比乘量。這是「依專精」的明確子提案，不宣稱已定案。對各 stage 的非御三家野生池，偏好主屬性的物種抽取權重 3、其他 1，沿用 D0 `RunPlanner.Weight`，不是十屬性均分。
- B：成功時，從該星球 `element_bias` 等機率選**一種**。偏好數 `b`，外部提供均勻整數索引 `v∈[0,b−1]`；`element=element_bias[v]`。不是每個偏好各發 1 個。未列入偏好的屬性在該星球是零，但六種星球的聯集涵蓋全部十屬性。

Python 不使用 RNG；`event_drop` 接收呼叫者提供的整數擲值以重現單事件，節奏由精確機率計算。OPUS 若接線，應使用專屬、可重現的素材隨機序列並確保均勻 bounded sampling，避免新增素材抽樣改變既有遭遇／戰鬥 RNG 結果。種子與事件去重由呼叫端負責，此工具不冒充執行期事件系統。

素材提議為整份大廳存檔共用，不按主力／副手或出場權重分配；從第一 run 起可囤積所有 tier，尚未進化也能拿 tier2／3。高 tier 不代低 tier、沒有合成／拆解。兩案都會在每個完整 run 平均發 1 個 tier3，現有需求為零；這是囤積與介面負擔的取捨，不能把這些素材算進成年進化可用量，超載需求留 Q-C12。

## D0 校準與取整

沿用 `level_rewards.py` 的 Q-C29 已核定 A 經驗公式、逐事件最大餘額分配，讀 `Data/balance.json#progression`；不使用歷史經驗方案 B。遭遇數直接沿用該工具的 `rewards.json.d0_run_model`：普通 t0／t1／t2 各 4／6／3，t1 成年精英 1、成年 BOSS 1；另有固定代表結果 3 次成功收服、5 真正完成步驟、1 整鏈供計算 XP，均不觸發素材。

此為完整成功路線的**模型假設**，不是全圖必打數、實測時間或每 seed 保證。相同 15 次擊倒預算投影到其他星球，以隔離素材來源差異；不聲稱那些星球的實際任務路線也恰好只有 13 普通敵人。基礎經驗模型原本用翠綠星校準；本批沒有為不同星球修改既有經驗假設。

全部收款主力從 LV10 起，無死亡、無換隊、固定 ID1／2 每事件出場，其餘後備；新收服者進倉庫，不稀釋當場 XP。ID1 在 1／2／6 主力下每 run 分別得 3171／1587／793 XP。進化保留經驗且不改此固定來源 XP，故可延伸至成年門檻。星球難度固定 d=1；RunPlanner v2 已改為挑可完成的任務屬性，沒有因此新增光／影／雷野生物種。

**實發數量、庫存、機率百分比、等級、run 規劃數均為整數。** 期望值不是實發數，以下只用整數分子／分母的精確比率表示，禁止把分數存進庫存或每次先取整。例如普通每次期望 `1/10`，不等於每次發零；兩偏好星球某屬性每 run tier1 期望 `13/20`。表內每屬性三元組依序為 `(tier1, tier2, tier3)`。

每次擊倒的總期望分別為 tier1 `1/10`、tier2 `3/5`、tier3 `1`；固定完整 run 是 `13/10、3/5、1`，總期望 `29/10`，三 tier 期望比例 **13:6:10**。A／B 的總發行相同，只有屬性分布不同；下方列全部六種星球的精確分布。

素材湊齊時間不能直接當成 `ceil(需求 / 每 run 期望)`。令每 run 有 `n` 次同機率試驗、單次目標素材機率為 `p`，尚缺 `k` 個；`P(j)=C(n,j)×p^j×(1−p)^(n−j)`。精確期望完整 run 數：

```text
E(0)=0
E(k)=[1+Σ P(j)×E(k−j), j=1..min(k−1,n)] / [1−P(0)]
p=0 且 k>0 → 不可達；p=1 → ceil(k/n)
P80=min{r≥0: Pr[Binomial(r×n,p)≥k]≥80/100}
```

只有顯示用 `ceil(E)` 向上取整為整數 run；計算過程用 `Fraction`，不以浮點或 Monte Carlo 近似。當前資料各 stage 的主屬性分布相同，符合二項模型；若日後不同，工具會明確拒絕不適用的節奏計算，而不暗套平均機率。P80 是不同指標，不是硬保底。

「等級 run」為 `ceil((T(門檻)−T(10))/逐事件結算後的每run XP)`。「滿編規劃 run」是 `max(6主力等級run, ceil(素材E), 上一階段規劃run)`；成年欄從 LV10 起累計，tier2 已從第一 run 開始囤，**不能再把 tier2 的 17 run 加到少年進化之後**。「距上階段」只減這兩個整數規劃值，並非隨機階段停留時間的精確期望；`max(期望)` 也不是兩條件聯合達標時間的期望。

## 對齊程度與限制

A：御三家少年／成年兩段素材均不可達，所以沒有有限 run 數，無法對齊等級節奏。七種可達屬性會因星球權重而有不同採集效率，詳見逐星球產量。偏好雷的風暴星仍只增加風系野生抽取率；晨昏星的光／影偏好在野生抽取上均未命中，不能誤算成可掉光影。

B：在雙偏好目標星球，tier1 3 個的 `ceil(E)` 為 6 run、P80 為 7；tier2 5 個的 `ceil(E)` 為 17 run、P80 為 21。滿編月／日／星等級門檻分別在幼年 4／5／6 run、成年 14／15／18 run；規劃進化為月 6→17、日 6→17、星 6→18，兩段間約再 11／11／12 run。月、日素材慢約 1–3 run，星接近同步；少量材料會提早齊，尾端也可能超過 P80。

這個全域 10%／60% 是折衷，不按物種訂製掉率。單主力達少年只需 1–2 run、成年 4–5 run；雙主力少年 2–3、成年 7–9 run，故同一採集曲線對小隊更慢。選 B 也不代表這項取捨已獲核准。表中每條線單獨、零庫存、只培養該線並持續去目標星球；同養三家時光影可共用晨昏行程，但雷要另去風暴星，不能將三線的獨立預算當成一份同時完成的行程。

更多探索、提早 BOSS、任務跳過、死亡、換人、非目標星球、共享庫存被其他進化消耗，都會改變對齊結果。素材對死亡主力不分配權重；提案按帳號庫存記錄已獲掉落，回大廳統一結算，成功路線以外的保留政策須由人工一併確認。本表只校準成功路線。

## 存檔 2→3：欄位提案，不實作

沿用 `Features/Creatures` 已有的 `MaterialKey`、`MaterialStack`、`MaterialInventory`（`Snapshot`／`TryConsume`），不要新增第二套庫存規則。

- 根欄 `version: 3`；新增根陣列 `materials: [{"element":"light","tier":1,"qty":3}]`。庫存屬大廳存檔，不放進 `owned` 的個體資料。
- `element` 用十屬性穩定字串、`tier` 整數 1–3、`qty` 正整數且不超過 Int32 上限；零數量不存，同 `(element,tier)` 僅一筆。載入拒絕未知屬性、重複鍵、非整數／負數／溢位；輸出依既有元素 enum 次序再按 tier 排序。
- v2 一次遷移為 v3：`materials=[]`，保留現有累計 `exp`、owned、party、starter 與 instance 游標；**不重跑 v1→2 經驗換算、不補發舊 run 素材**。先備份 `.v2.bak`，成功後原子換檔；v3 不再次遷移，更高版本拒絕讀寫。
- run 中僅保留素材增量與每 spawn 的發放紀錄，結算回廳才與庫存合併並保存；扣素材與切換型態必須同一筆成功進化保存，失敗全部不變。加總先用寬整數檢查容量，不能默默 wrap、截斷或部分入帳。
- 若接線允許同份 run 結算重試，還需要根欄 `last_material_settlement_id`（全域唯一 run ID）隨庫存一起原子保存以拒絕重播；若仍是單次同步回呼，可用現有 run 生命週期去重，這個額外欄位是否需要由 OPUS 檢查實際流程後決定。掉落 RNG 種子／狀態不需放進目前僅存大廳的檔案；若之後支援 run 續存，需另設計，不在本批實作。

## CLI 實跑輸出

命令：`python tools/balance/material_drops.py`。以下區塊由同一次 CLI stdout 原樣貼入；unittest 強制 CLI、`pacing_table()`、本區塊三者完全一致。

<!-- CLI-BEGIN -->
| 進化線 | 幼年→少年：LV；素材 | 少年→成年：LV；素材 | 成年 |
|---|---|---|---|
| line_earth_gildedgolem | 30；earth/t1×3 | 50；earth/t2×5 | 無需求 |
| line_fire_blazelion | 26；fire/t1×3 | 44；fire/t2×5 | 無需求 |
| line_ice_frostbeast | 25；ice/t1×3 | 43；ice/t2×5 | 無需求 |
| line_metal_gearclock | 28；metal/t1×3 | 48；metal/t2×5 | 無需求 |
| line_water_tidecraft | 22；water/t1×3 | 38；water/t2×5 | 無需求 |
| line_wind_fluteglider | 20；wind/t1×3 | 35；wind/t2×5 | 無需求 |
| line_wood_vinecrest | 24；wood/t1×3 | 40；wood/t2×5 | 無需求 |
| starter_moon_line | 23；shadow/t1×3 | 40；shadow/t2×5 | 無需求 |
| starter_star_line | 27；thunder/t1×3 | 45；thunder/t2×5 | 無需求 |
| starter_sun_line | 25；light/t1×3 | 42；light/t2×5 | 無需求 |

| tier | metal | wood | water | fire | earth | ice | wind | thunder | light | shadow | 合計 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 30 |
| 2 | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 50 |
| 3 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

| 方案 | 觸發 | tier | 成功率 % | 成功數量 | 每次擊倒期望 |
|---|---|---|---|---|---|
| A | normal | 1 | 10 | 1 | 1/10 |
| A | elite | 2 | 60 | 1 | 3/5 |
| A | boss | 3 | 100 | 1 | 1 |
| B | normal | 1 | 10 | 1 | 1/10 |
| B | elite | 2 | 60 | 1 | 3/5 |
| B | boss | 3 | 100 | 1 | 1 |

| 方案／星球 | 每 run t1／t2／t3 總期望 | 每屬性 t1／t2／t3 期望（未列者皆 0） |
|---|---|---|
| A/ice_crystal_planet | 13/10 / 3/5 / 1 | metal:(13/110, 3/55, 1/11); wood:(13/110, 3/55, 1/11); water:(39/110, 9/55, 3/11); fire:(13/110, 3/55, 1/11); earth:(13/110, 3/55, 1/11); ice:(39/110, 9/55, 3/11); wind:(13/110, 3/55, 1/11) |
| A/lava_planet | 13/10 / 3/5 / 1 | metal:(13/110, 3/55, 1/11); wood:(13/110, 3/55, 1/11); water:(13/110, 3/55, 1/11); fire:(39/110, 9/55, 3/11); earth:(39/110, 9/55, 3/11); ice:(13/110, 3/55, 1/11); wind:(13/110, 3/55, 1/11) |
| A/mineral_planet | 13/10 / 3/5 / 1 | metal:(39/110, 9/55, 3/11); wood:(13/110, 3/55, 1/11); water:(13/110, 3/55, 1/11); fire:(13/110, 3/55, 1/11); earth:(39/110, 9/55, 3/11); ice:(13/110, 3/55, 1/11); wind:(13/110, 3/55, 1/11) |
| A/storm_planet | 13/10 / 3/5 / 1 | metal:(13/90, 1/15, 1/9); wood:(13/90, 1/15, 1/9); water:(13/90, 1/15, 1/9); fire:(13/90, 1/15, 1/9); earth:(13/90, 1/15, 1/9); ice:(13/90, 1/15, 1/9); wind:(13/30, 1/5, 1/3) |
| A/twilight_planet | 13/10 / 3/5 / 1 | metal:(13/70, 3/35, 1/7); wood:(13/70, 3/35, 1/7); water:(13/70, 3/35, 1/7); fire:(13/70, 3/35, 1/7); earth:(13/70, 3/35, 1/7); ice:(13/70, 3/35, 1/7); wind:(13/70, 3/35, 1/7) |
| A/verdant_planet | 13/10 / 3/5 / 1 | metal:(1/10, 3/65, 1/13); wood:(3/10, 9/65, 3/13); water:(3/10, 9/65, 3/13); fire:(1/10, 3/65, 1/13); earth:(1/10, 3/65, 1/13); ice:(1/10, 3/65, 1/13); wind:(3/10, 9/65, 3/13) |
| B/ice_crystal_planet | 13/10 / 3/5 / 1 | water:(13/20, 3/10, 1/2); ice:(13/20, 3/10, 1/2) |
| B/lava_planet | 13/10 / 3/5 / 1 | fire:(13/20, 3/10, 1/2); earth:(13/20, 3/10, 1/2) |
| B/mineral_planet | 13/10 / 3/5 / 1 | metal:(13/20, 3/10, 1/2); earth:(13/20, 3/10, 1/2) |
| B/storm_planet | 13/10 / 3/5 / 1 | wind:(13/20, 3/10, 1/2); thunder:(13/20, 3/10, 1/2) |
| B/twilight_planet | 13/10 / 3/5 / 1 | light:(13/20, 3/10, 1/2); shadow:(13/20, 3/10, 1/2) |
| B/verdant_planet | 13/10 / 3/5 / 1 | wood:(13/30, 1/5, 1/3); water:(13/30, 1/5, 1/3); wind:(13/30, 1/5, 1/3) |

| 方案 | D0 所有星球聯集不可達屬性（每個 tier） |
|---|---|
| A | thunder, light, shadow |
| B | 無 |

| 方案 | 線／目標星球 | 階段 | LV | 等級 run（1／2／6 主力） | 素材 run：ceil(均值)／P80 | 滿編規劃 run／距上階段 |
|---|---|---|---|---|---|---|
| A | starter_moon_line/twilight_planet | juvenile | 23 | 2 / 4 / 7 | 不可達 / 不可達 | 不可達 |
| A | starter_moon_line/twilight_planet | adolescent | 40 | 6 / 12 / 24 | 不可達 / 不可達 | 不可達 |
| A | starter_star_line/storm_planet | juvenile | 27 | 3 / 5 / 10 | 不可達 / 不可達 | 不可達 |
| A | starter_star_line/storm_planet | adolescent | 45 | 9 / 17 / 33 | 不可達 / 不可達 | 不可達 |
| A | starter_sun_line/twilight_planet | juvenile | 25 | 2 / 4 / 8 | 不可達 / 不可達 | 不可達 |
| A | starter_sun_line/twilight_planet | adolescent | 42 | 7 / 14 / 27 | 不可達 / 不可達 | 不可達 |
| B | starter_moon_line/twilight_planet | juvenile | 23 | 2 / 4 / 7 | 6 / 7 | 7 / +7 |
| B | starter_moon_line/twilight_planet | adolescent | 40 | 6 / 12 / 24 | 17 / 21 | 24 / +17 |
| B | starter_star_line/storm_planet | juvenile | 27 | 3 / 5 / 10 | 6 / 7 | 10 / +10 |
| B | starter_star_line/storm_planet | adolescent | 45 | 9 / 17 / 33 | 17 / 21 | 33 / +23 |
| B | starter_sun_line/twilight_planet | juvenile | 25 | 2 / 4 / 8 | 6 / 7 | 8 / +8 |
| B | starter_sun_line/twilight_planet | adolescent | 42 | 7 / 14 / 27 | 17 / 21 | 27 / +19 |
<!-- CLI-END -->

## 驗收

本環境的系統 `python` 不在可用 PATH，既有安裝路徑也無法存取；使用可執行的 `C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe`（Python 3.13.13）執行相同命令參數，沒有安裝或修改系統依賴。

- `python -m unittest discover -s tools/balance -p "test_*.py"`：**67/67 通過**，包含既有 54 項與本批 13 項。覆蓋非 incoming 盤點、整數／非負、事件擲值邊界、每 tier 產量守恆、A 的缺屬性與 B 的全屬性可達性、精確等待時間／P80、核定 A 的逐事件 XP 分配、CLI／函式／文件完全一致。
- `python -m pytest tools/balance -q`：已嘗試；此可用直譯器未裝 pytest，回報 `No module named pytest`，未能執行 pytest。unittest 不依賴 pytest。
- CLI 已成功執行，stdout 原樣收錄於上方標記區塊，未使用手填節奏表。
