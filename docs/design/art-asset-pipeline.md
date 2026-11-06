# 美術資產外包管線（ChatGPT / Gemini 對話版）

用 `tools/prompt-forge/index.html` 產生「一鍵交付」的任務 prompt，貼到 ChatGPT 對話版或 Gemini Argon 任務模式執行，取回 zip 後解壓到固定位置，由 Claude/SONNET 驗證並搬入 Unity。

## 流程
```
prompt-forge（選/隨機概念 → 修改 → 複製 prompt）
   │  任務標頭帶 task_id、seed、各版本號
   ▼
ChatGPT / Gemini Argon（產圖、規格、腳本 → 打包 <task_id>.zip）
   ▼
下載 → 解壓到 Art/_incoming/  →  得到 Art/_incoming/<task_id>/manifest.json …
   ▼
Claude Code：「驗收 Art/_incoming/<task_id>」→ 檢查 manifest、命名、尺寸
   ▼
人工定稿 → 搬入 Assets/_Project/Art/…（概念 JSON 進 Data/_incoming/<task_id>/）
```

## 任務類型
| 任務 ID | 內容 | 建議平台 | 依賴 |
|---|---|---|---|
| `character-concept` | 角色概念、立繪、T-pose 三視圖、表情集、道具 | ChatGPT | — |
| `creature-concept` | 宙獸四型態（幼/少/成/超載）立繪、三視圖、進化排列、體型對照 | ChatGPT | — |
| `model-rig` | 建模用正交視圖、骨架規格、Blender 綁骨腳本（L2：GLB/FBX） | Gemini Argon | 概念 JSON |
| `animation-set` | 動作規格（幀數/事件）、關鍵姿勢分鏡、Blender 關鍵幀腳本（L2：FBX 動畫） | Gemini Argon | 概念 + 骨架規格 |
| `move-vfx` | 招式特效分鏡、flipbook 序列圖、Unity 參數表 | ChatGPT | 可選宙獸概念 |
| `capture-vessel` | 三類收服道具外觀、圖示、投擲/收服分鏡與特效 | ChatGPT | — |
| `culture-research` | 文化原型考據（意象、敏感度、來源） | Gemini Argon | — |

**交付層級**：`L1` 對話式 AI 必定做得到（圖、JSON、腳本），未交付即任務失敗；`L2` 視平台能力（3D 網格、FBX 動畫），做不到時列入 `manifest.missing`，不得以假檔充數。3D 工具選型 → Q-T3。

## 放置位置
| 階段 | 路徑 | Git |
|---|---|---|
| 下載的 zip | `%USERPROFILE%\Downloads\<task_id>.zip` | — |
| 解壓（待驗收） | `Art/_incoming/<task_id>/` | 忽略（只提交 README） |
| 角色定稿 | `Assets/_Project/Art/Characters/<char_id>/{Concept,Model,Anim}/` | LFS |
| 宙獸定稿 | `Assets/_Project/Art/Creatures/<line_id>/<stage>/{Concept,Model,Anim}/` | LFS |
| 招式特效 | `Assets/_Project/Art/VFX/<element>/<move_id>/` | LFS |
| 收服道具 | `Assets/_Project/Art/Vessels/<vessel_id>/` | LFS |
| 概念/考據 JSON | `Data/_incoming/<task_id>/` → 驗證後進 `Data/` | 提交 |

`Art/_incoming/` 放在 `Assets/` 外，因為未驗收的檔案若進 `Assets/` 會被 Unity 匯入並產生 `.meta`，污染專案。

解壓（PowerShell，工具內會產生帶好 task_id 的版本）：
```powershell
Expand-Archive -Path "$HOME\Downloads\<task_id>.zip" -DestinationPath ".\Art\_incoming\" -Force
```
zip 內頂層必須是 `<task_id>/` 資料夾；若 AI 打包時少了這層，改解壓到 `.\Art\_incoming\<task_id>\`。

## 交付包結構
```
<task_id>/
├── manifest.json      # 必要，格式見下
├── concept.json       # 概念（AI 補完名稱、配色 hex、背景）
├── images/            # PNG；檔名見〈命名〉
├── rig/               # rig_spec.json、build_rig.py
├── anims/             # anim_spec.json、keyframes.py、（L2）*.fbx
├── models/            # （L2）*.glb / *.fbx
└── vfx/               # vfx_spec.json、flipbook PNG
```
```json
{
  "spec": "cosmimera-art-task/1",
  "task_header": "<原樣抄回 prompt 第一行>",
  "task_id": "cr-20261106-7f3a",
  "task_type": "creature-concept",
  "subject_id": "line_7f3a",
  "produced_by": "ChatGPT | Gemini Argon",
  "files": [{"path": "images/cr_line_7f3a_adult_front.png", "kind": "turnaround", "level": "L1"}],
  "missing": [{"item": "models/*.glb", "level": "L2", "reason": "平台無 3D 生成"}],
  "notes": ""
}
```
若平台無法把生成的圖片放進 zip：zip 只含文字檔，`manifest.files` 照樣列出完整檔名，使用者手動以該檔名存入 `images/`。

## 命名
全部小寫 snake_case，前綴：角色 `chr_`、宙獸 `cr_`、特效 `fx_`、道具 `vsl_`。
- 圖：`cr_<line_id>_<stage>_<view>.png`，`view ∈ main | front | side | back | three_quarter | lineup | scale`
- 動作 clip：`chr_<action>`、`cr_<stage>_<action>`
- 特效：`fx_<element>_<shape>_<tier>_<phase>.png`

## 技術規格
| 項目 | 角色 | 宙獸 |
|---|---|---|
| 畫風 | 柔和卡通著色 2–3 階明暗、邊緣光、深色描邊（`vision.md`） | 同左 |
| 三視圖 | T-pose，正/側/背/3/4，正交、中性灰底、無陰影透視 | 自然站姿（四足/飛行依身形），其餘同左 |
| 面數（LOD0） | 15–25k tris | S 5–8k · M 8–15k · L 15–25k · XL/超載 25–35k |
| 貼圖 | 2048² base color（+ emissive mask 選用） | 1024²（S/M）· 2048²（L/XL） |
| 座標 | Unity：Y 向上、面向 +Z、公尺、腳底中心為 pivot | 同左 |
| 骨架 | Unity Humanoid 相容命名 | Generic，`Root` 在地面 |
| 動畫 | 30 fps；移動類 in-place，閃避/突進類 root motion | 同左 |

Steam Deck 是效能下限（`vision.md`），面數上限以 4 人 × 2 隻宙獸同場為準。

### 插槽骨（特效與互動掛點，必備）
- 角色：`Socket_HandR_Prop`、`Socket_HandL_Prop`、`Socket_Back_Pack`、`Socket_Throw`、`Socket_Head_FX`
- 宙獸：`Socket_Mouth`、`Socket_Head`、`Socket_Core_FX`、`Socket_Front`（射擊出口）、`Socket_PetTouch`（被撫摸點）、`Socket_Ground_FX`

## 動作
clip ID、幀數、迴圈與事件的唯一來源是 `tools/prompt-forge/catalog.js` 的 `ACTIONS`；改動時同步 bump catalog 版本（見 `tools/prompt-forge/README.md`）。
- 事件名：`ev_hit`（傷害判定幀）、`ev_spawn_fx`、`ev_release`（投擲離手）、`ev_footstep`、`ev_end_lock`（可接下一動作）。
- 撫摸：角色依宙獸 `size_class` 播 `chr_pet_s|m|l|xl`；宙獸播對應的 `cr_<stage>_petted`，兩者以 `Socket_PetTouch` 對位。
- 攻擊動作依 `combat.md#招式形狀` 分類命名。

## 版本
- prompt 第一行為任務標頭：`[COSMIMERA-TASK spec=… tool=… catalog=… template=<id>@<ver> task_id=… seed=…]`，manifest 必須原樣抄回，日後可追溯是哪一版規格產出的。
- 規則與變更紀錄：`tools/prompt-forge/README.md`、`CHANGELOG.md`。
