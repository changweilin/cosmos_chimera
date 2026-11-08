# P5-08 A10 外觀意象與屬性配置裁決

- 日期：2026-11-08；執行者：ChatGPT Codex。
- 狀態：Q-C27 已由使用者選定保留數值、三者不強制綁定；定案寫入設計文件，資料未變。

## 現行資料

| 宙獸 ID | 專精 | 被指出的抗性／意象差異 |
|---|---|---|
| `metal_chronos_engine` | metal 40、earth 15 | 水力計時外觀，但 water 抗性 0、earth 抗性 15 |
| `water_catamaran_serpent` | water 40、wind 15 | wind 抗性 0 |
| `ice_tundra_dreadnought` | ice 40、metal 15 | metal 抗性 0 |
| `sun_array_skyheron` | light 40、fire 15 | 外觀汲取雲間水氣，但 water 抗性 −15 |

A10 原建議為評估金線成年副專精由土改水，並裁定副專精是否必須伴隨正抗性。這會改變現有副手搭配收益，不能只視為外觀文案修正。

## 關係表影響

以目前 42 條邊與 30 隻宙獸交叉解析，金線成年有四個可合法借招的副手物種（階段均不高於成年）：

| 副手物種 | 關係 | 精通招式 | 屬性／威力 |
|---|---|---|---|
| `metal_gear_strider` | evolution_family | `metal_iron_cleave` | metal／60 |
| `earth_pebble_pawn` | mentor | `earth_pebble_shot` | earth／30 |
| `fire_pyre_monarch` | rival_symbiosis | `fire_inferno_cataclysm` | fire／120 |
| `ice_tundra_dreadnought` | tool_kin | `ice_blizzard_vortex` | ice／110 |

現有 earth 15 能加成借來的土招；改為 water 15 會移除這項加成，而現有關係網沒有金線成年可借的水招。這只描述目前資料，不代表未來水副專精無用途。既有邊與資料皆未改。

## 裁決與處理

1. **已採用：保留全部數值，明定三者不強制綁定。** 定案見 [creatures.md 十大屬性](../design/creatures.md#十大屬性)。接受上列四個案例，不新增岩基配重等外觀設定。
2. **未採用：金線成年副專精 earth 15 → water 15，其餘保留。** 不調整宙獸 JSON 或 README，不改變既有借招收益。

關係解析證據：`Logs/p5-08-naming-14bl0tp7/a10-current-evidence.json`（本機暫存）。Q-C27 已從待決問題移除；本次只改文件，未重跑資料／對戰平衡測試，亦未宣告其他待決項目結案。
