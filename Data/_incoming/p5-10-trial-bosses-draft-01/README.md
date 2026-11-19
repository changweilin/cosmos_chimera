# P5-10 地形試煉 BOSS 宙獸初稿 01 (p5-10-trial-bosses-draft-01)

依 `docs/design/trainer-battles.md#地形試煉`、`docs/design/field-utilities.md#3-進階地形免疫` 與 `tools/prompts/trial-boss-batch.md` 產出之 10 隻**單一型態**（`stage: "adult"`，不做進化線；依 `open-questions.html` Q-G10 暫定方案 A 先行）地形試煉 BOSS 宙獸。

## 1. 檔案清單
- `creatures/creatures.json`：10 隻單一成年型態宙獸，各自持有對應進階地形免疫 `field_utility: immune_<地形>`。
- `relations/edges.json`：20 條跨線關係邊（每隻 BOSS 皆有 ≥ 2 條邊，`a < b` 字典序）。
- `copy-terra.json`：中英命名與短文（供 `cosmos_chimera_forge/gen_designed.py` 讀取）。
- `encyclopedia/entries.json`：10 筆圖鑑條目（符合 `Data/schemas/encyclopedia.schema.json`，`status: "review_required"`）。

## 2. 十大進階地形 × 試煉 BOSS 對照表

| ID | 名稱 | 主屬性 | `field_utility` | 進階地形 | 文化原型 ID | 弱點 | 精通主動技 | 被動技 |
|---|---|---|---|---|---|---|---|---|
| `metal_astrolabe_automaton` | 羅盤星儀巨偶 | metal | `immune_laboratory` | 機房／工廠／研究室 | `arch_piri_reis` | fire | `metal_siege_bombard` (115) | `trait_metal_siege_anchoring` |
| `wood_jade_tortoise` | 翠波石塔龜 | wood | `immune_rainforest` | 雨林 | `arch_kim_quy_hoan_kiem` | ice | `wood_thorn_ballista` (105) | `trait_wood_overgrowth_surge` |
| `water_coral_serpent` | 銀潮珊瑚巨鰻 | water | `immune_deep_sea` | 深海 | `arch_sina_and_eel` | thunder | `water_maelstrom_lance` (110) | `trait_water_torrential_deluge` |
| `fire_basalt_drake` | 熔岩雙輪蚺 | fire | `immune_volcano` | 火山／火山口 | `arch_illuyanka` | water | `fire_meteor_rush` (105) | `trait_fire_smoldering_core` |
| `earth_citadel_lion` | 岩門石獅獸 | earth | `immune_cave` | 礦坑／地底／洞穴 | `arch_kashyapa_sigiriya` | wood | `earth_monolith_quarry` (105) | `trait_earth_monolith_posture` |
| `ice_sextant_leviathan` | 冰桅六分鯨 | ice | `immune_ice_cap` | 冰棚／冰冠／永久凍土 | `arch_ernest_shackleton` | metal | `ice_polar_sweep` (100) | `trait_ice_permafrost_shell` |
| `thunder_obelisk_chimera` | 塔紋方尖獸 | thunder | `immune_tower` | 高塔／摩天樓層 | `arch_ezana_aksum` | earth | `thunder_dynamo_charge` (105) | `trait_thunder_plasma_charge` |
| `wind_tristar_albatross` | 三翼星航鵬 | wind | `immune_space` | 太空設施／浮空島 | `arch_kingsford_smith` | shadow | `wind_tailwind_dive` (95) | `trait_wind_tempest_headway` |
| `shadow_lantern_kraken` | 千燈墨潮獸 | shadow | `immune_labyrinth` | 迷宮 | `arch_rannamaari` | light | `shadow_singularity_maw` (120) | `trait_shadow_nightfall_creed` |
| `light_seal_unicorn` | 印紋聖角獸 | light | `immune_temple` | 神廟 | `arch_harappan_unicorn` | wind | `light_zenith_lance` (105) | `trait_light_radiant_core` |

## 3. 驗證
合併 `Data/_demo` 與 `Data/_incoming` 全部批次執行 `python tools/validate_data.py --root <merged>`，確認 `0 error(s), 0 warning(s)`；同步至 `../cosmos_chimera_forge/Data/_incoming/p5-10-trial-bosses-draft-01/` 並執行 `python gen_designed.py`。
