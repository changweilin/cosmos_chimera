# ACG 角色規格（ADR-0013）

每個 `<id>.json` 由 `tools/acg-character/build.py` 讀取，重跑即重建模型；不手改輸出的 FBX。

| 欄位 | 內容 |
|---|---|
| `height_cm` | 身高（不含頭髮、鞋） |
| `macro` / `targets` | MPFB 巨觀參數（`age` 取比設定年齡更小：MakeHuman 越年輕頭身比越大且四肢比例不變；`height` 維持 0.5，調矮主要是縮短大腿與手臂，與日系長腿相反；最後整體等比縮放到 `height_cm`）與 MakeHuman target 權重（CC0）；頭型（`head-oval`／`round`…）、額頭（`forehead-*`）、整顆頭的前後傾（`head-angle-in`，下巴與眉線對齊）、肩寬、腰臀、四肢肌肉皆用 target 分角色調 |
| `proportions` | 日系比例：`heads`（目標頭身比，頭頂到下巴，依 `docs/design/characters.md` 的約 7 頭身；`macro` 不足的部分由程式解出整顆頭的等比放大量，記錄於 log 的 `ACG_HEADS`，誤差約 ±0.1）或直接給 `head_scale`、`leg_length`、`thigh_length`（膝到胯的額外拉長）、`leg_thin`、`neck_thin`、`hand_scale`、`foot_scale`、`waist_thin` |
| `face` | 眼睛畫在皮膚上（眼窩先填平，`socket_radius`）：外形 `eye_w`（×原眼寬）、`eye_h`（×眼寬）、`eye_tilt`、`eye_dx`／`eye_dz`；虹膜 `iris_w`、`iris_h`；睫毛 `lash_w`、`lash_flick`；眉 `brow_*`；耳朵縮小 `ear_scale`；下巴 `chin_forward`、`chin_down`、`chin_taper`（公尺／比例）、臉頰填平 `cheek_fill`；嘴線寬度與弧度由 `mouth_w`、`mouth_smile` 控制，`mouth_open` 為露齒笑的開口高度（公尺） |
| `look` | 膚色、虹膜、眉、髮色（root／mid／tip 漸層） |
| `hair.groups` | 髮束群：根部方位角 `az`／仰角 `el`（0°＝正面、90°＝角色左側），`flow`、`length`、`width`、`flick`、`wave`（波浪幅度，`wave_freq`）；`anchor: [az, el, k]` 讓整群從頭外一點長出（馬尾、髮髻，`anchor_r` 束口半徑）；`colors` 給挑染；`hair.scale` 等比縮放髮束與髮飾尺寸（頭變小時用） |
| `hair.ornaments` | 髮飾（髮圈、髮夾、蝴蝶結、護目鏡）：`outfit` 的 `prim`，但以頭部橢球的 `az`／`el`／`k` 定位，頭型改變時自動跟隨 |
| `idle_pose` | 立繪站姿（待機動畫 `Idle_Art`）：`aim: [[骨, [x, y, z]], …]` 依序把骨頭轉向身體座標方向（角色面向 -Y、+X 為角色左手側），`level: [骨…]` 恢復該骨的靜止朝向（雙腳張開後腳掌放平），`curl: [[骨, 度], …]` 手指朝掌心彎（握道具）；綁定姿勢仍是 T-pose |
| `props` | 手持道具：每人都建 `cube`（魔術方塊）、`bead`（琉璃珠）、`talisman`（符籙）三件，綁 `hand_l`；`hold` 為預設顯示者，其餘匯出時帶 FBX 使用者屬性 `acg_hidden=1`，Unity 由 `AcgPropImport` 停用其 Renderer。更換道具＝切換 `prop_<kind>_<id>` 的 Renderer；`size: {kind: 公尺}` 可覆寫尺寸 |
| `outfit` | 服裝：`parts`+`z`+`sleeve` 從身體切殼（繼承權重），`x: [lo, hi]`（|x| 範圍，`x_side` 取單側）與 `face: front/back` 切出滾邊、拼色、側條；`prim`（box／cyl／torus／sphere／paw／ribbon／hood）綁單一骨，座標為 T-pose 世界座標相對骨頭；`paw` 是一體式肉球貼布，`ribbon` 為有折脊的束口緞帶，`hood` 的 `lining` 指定帽領內裡色，鏡射袖片的 `box` 可用 `hem_rise` 指定內側下擺抬高量（公尺）；布料類 `prim` 加 `"soft": true` 改用最近身體表面的混合權重（跨關節跟著彎），並用柔和三階明暗；切殼服裝在關節（肘、肩、膝、胯、腰）自動起布褶、有 `flare` 的下擺起波浪，`folds` 設褶深（公尺，預設 0.0045，0 關閉）；垂掛布料 `prim` 加 `swing: {roots: [[dx,dy,dz]…]（相對 `at` 中心）, dir, length, segments}` 長出 `sw_` 骨鏈並沿鏈重新蒙皮，`swing_with: <名稱>` 共用另一件的骨鏈（垂片上的飾條）；裙子用 `swing: {ring: 鏈數, segments, from: z 運算式}` 在骨盆周圍長一圈骨鏈（裙子本身用 `"open": true` 的 `cyl` 開口圓錐，不從身體切殼，否則會變成兩條褲管）；`hair.groups` 加 `"swing": 段數` 讓該髮束沿平均中心線長 `sw_hair_` 骨鏈；Unity 匯入時自動加 `Cosmimera.Player.SpringBones`，並由身體網格量出軀幹、頸、四肢的碰撞膠囊半徑；擁有者呼叫 `UsePlanet(planet)`（之後每幀取星球當地下方）或自行設 `Gravity`；髮束與裙子各有較硬的預設參數（`sw_hair_`、`sw_skirt` 前綴）；Blender 預覽的待機圖把骨鏈直接朝下擺 |

`z` 可寫地標運算式：`pelvis`、`waist`、`chest`、`neck`、`head`、`hip`、`knee`、`ankle`、`toe`、`shoulder`、`elbow`、`wrist`，例：`"knee+0.04"`（公尺）。

匯出：`blender -b --python tools/acg-character/build.py -- --spec tools/acg-character/specs/<id>.json --blend <out.blend> --render <dir> --fbx Assets/_Project/Art/Characters/<id>/ACG/<id>_acg.fbx`（有 `idle_pose` 時另輸出 `<id>_acg@Idle_Art.fbx`，只含骨架與待機動畫，Unity 以模型的 Avatar 匯入），再於 Unity 執行 `Cosmimera.EditorTools.AcgCharacterImport.Configure`（Humanoid）與 `.Capture`（四視角與兩張待機姿勢到 `Logs/`）。

比例檢查：加 `--bare` 只建裸身基底（無髮、無服裝，不可與 `--fbx` 併用）。

收身：完整包覆的切殼服裝（不含 `x`／`face` 拼片與 `prim`）底下的皮膚往內縮 `tuck` 公尺（預設 0.012，0 關閉；邊緣保留兩圈頂點），關節彎曲時身體不會穿出衣服。切殼服裝也可用 `swing: {ring, segments, from}`（同裙子）讓下擺擺動。

材質：`--fbx` 另寫 `<id>_acg_materials.json`（每個材質的亮色、暗色、色階數、貼圖）；Unity `AcgCharacterImport.Configure` 據此在 `ACG/Materials/` 建 `Cosmimera/ToonOutlined` 材質（含描邊，臉部貼片除外）並重新對應，皮膚與衣服才分得開。

綁定姿勢：T-pose 會把前臂與手掌拉直到上臂延長線上（MakeHuman 前臂原本前彎約 45°），袖筒與袖口才會包住前臂、手才會從袖口伸出。膝蓋與手肘以 Taubin 平滑抹掉寫實的膝蓋骨凹凸；匯出時每頂點骨權重限 4 根（Unity 上限）。`idle_pose` 不要把腳掌「放平」：Humanoid 的腳沒有內外翻，Unity 會把它轉嫁成小腿扭轉，膝蓋看起來像斷掉。Unity 材質另設 `_OutlineDepthBias`，把描邊外殼沿視線往後推，關節凹處不會透出裂痕般的線。

## UMA 種族形變（ADR-0014、ADR-0015）
`tools/acg-character/race.py` 在裸身基底上烘焙 DNA 用的 shape key：`blender -b --python tools/acg-character/race.py -- --spec <spec> --blend <out.blend> [--fbx <out.fbx>] [--report <out.json>] [--only 通道,…]`。

- 通道與來源的唯一對照：`tools/acg-character/race_channels.json`（與 `NpcMeshes.UmaChannels` 一一對應，EditMode `UmaRecipeGateTests.RaceChannelTableMatchesTheMapper` 檢查）。`bone` 在 Unity 端以骨骼 DNA 做、不烘；`target`／`param` 由本腳本烘；`slot`、`shader`、`sculpt` 另做。
- 每個 key 都把整個裸身建置重跑一次再與基底相減，眼、眉、嘴貼片與頭部平滑、下巴塑形都算進去；頭身比的頭部縮放固定為基底解出的值，避免頭部 target 被重新求解抵消。
- 一組 incr／decr target 視為一個淨權重軸（上限 ±1）；基底 spec 已用掉的部分會讓該方向的步幅變小，實際步幅寫在報告 `step`，轉換器要除以它。報告也列出每個 key 在各網格的最大位移（mm），< 0.1 mm 標 `vanished`。

## 路人種族基底（npc-uma-looks.md#種族基底）
不取自任何主角；28 個人形基底（14 族 × 男女）全由抽樣規則推導，重跑即重建：
1. `unity test` 跑 `UmaRaceBaseTests.ExportRaceBases`（Explicit）→ `npc/race_bases.json`。
2. `python tools/acg-character/npc_bases.py specs` → 人類男女基底；`npc_bases.py bake --blender <blender.exe> --only npc_human_male`（人類男性的報告即校正）。
3. `python tools/acg-character/npc_bases.py specs --calibration Logs/npc/npc_human_male.json` → 其餘基底；`CLIPPED` 列出族平均超出 MakeHuman ±1 的通道。
4. `npc_bases.py bake --blender <blender.exe> --jobs 4` → `Logs/npc/<id>.blend|.fbx|.json` 與 `npc/race_keys.json`；EditMode `UmaRaceBaseTests.BakedKeysReachAndShowEveryResident` 檢查涵蓋與可見門檻。
