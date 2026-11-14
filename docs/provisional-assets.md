# 臨時性資產清單（Provisional Assets）

本文件記錄自 `cosmos_chimera_forge` 整合進遊戲的「暫時採用（`provisional`）」資產與其替代狀態。定稿資產通過驗收後，依 `docs/design/art-asset-pipeline.md` 取代之。

## 1. 3D 角色模型（已整合）

來源為 `cosmos_chimera_forge/Art/_incoming/mdl-20261109-f048-1847-*`（`model-rig@4.3.0`，均標記 `provisional`）：

| 角色 ID | 顯示名稱 | 來源任務 | 資產路徑 | 狀態 |
|---|---|---|---|---|
| `chr_amemori_sumiha` | 雨森澄羽 | `mdl-20261109-f048-1847-chr_1286_1855` | `Assets/_Project/Art/Characters/chr_amemori_sumiha/` | 暫時採用（已接入 `CharacterModelTable`） |
| `chr_roen` | 羅恩 | `mdl-20261109-f048-1847-chr_3d04` | `Assets/_Project/Art/Characters/chr_roen/` | 暫時採用（已接入 `CharacterModelTable`） |
| `chr_yeh_chengxi` | 葉承熙 | `mdl-20261109-f048-1847-chr_1286` | `Assets/_Project/Art/Characters/chr_yeh_chengxi/` | 暫時採用（已接入 `CharacterModelTable`） |

- 每位角色包含：`.fbx` / `.glb` 模型、32 支 `.fbx` 動作、`Model/rig_spec.json`、`Anim/anim_spec.json`、`manifest.json`、立繪概念圖。
- Unity 整合產物：`Anim/<char_id>.controller`、`<char_id>_Provisional.prefab`、`Assets/_Project/Art/Characters/CharacterModelTable.asset`。
- 由 `Cosmimera.Bootstrap.Demo.CharacterModelTableBuilder.Build`（選單 `COSMIMERA/Build Provisional Character Assets`）產生，並在 `Demo.unity` 大廳提供切換（含退回佔位膠囊選項）。
- `PlanetRig.Build` 支援選用 `characterModelPrefab`，透過 `CharacterAnimatorDriver` 依 `SphericalCharacter` 切向速度與落地狀態驅動 `Idle` / `Walk` / `Run` / `Jump`。

## 2. 宙獸（尚無 3D 模型）

- `cosmos_chimera_forge` 目前僅有 `starter_sun_line`（`cr-20261107-4a9a-2022`）之 2D 概念立繪（已置於 `Assets/_Project/Art/Creatures/starter_sun_line/`），尚無任何宙獸 3D 模型交付包。
- 遊戲內宙獸持續由 `PlaceholderCreatureViewFactory`（`ICreatureViewFactory` 替換介面）以卡通著色幾何體佔位。
