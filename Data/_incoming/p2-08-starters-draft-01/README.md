# P2-08 御三家「日月星」草稿 01

OPUS 概念與數值初配；命名與圖鑑文案由 TERRA 補在 `copy-terra.json`；人工定稿後才移入 `Data/creatures/` 等正式資料夾。
規格：`docs/design/creatures.md#御三家「日月星」（Q-C6 定案）`。驗證：`tools/validate-data --root Data/_incoming/p2-08-starters-draft-01`。

## 三條線
| 線 | 專精 | 文化原型（P5-02 考據庫，敏感度 ok） | 外觀基底 | 定位 |
|---|---|---|---|---|
| `starter_sun_line` 日 | 光（成年加火） | `arch_sultanas_dream` 蘇丹娜之夢（南亞）：聚光太陽鏡陣、汲雲氣球管 | 鷺鳥 × 太陽鏡陣飛行器 | 後排、遠距直線光束 |
| `starter_moon_line` 月 | 影（成年加冰） | `arch_amarok` 極夜巨狼（極地）：孤行巨狼、極夜星光、震地長尾 | 狼 | 刺客、近距突進 |
| `starter_star_line` 星 | 雷（少年起加風） | `arch_tulpar` 金翼天馬（中亞/北亞）：黃金雙翼、彎月額角、疾風長鬃 | 馬 × 雙翼 | 衝鋒、範圍放電 |

三個地區不重複；只取意象，id 不用原型真名（data-content.md）。

## 數值（balance.json `stage_bands` 總點數 100 / 200 / 400，全部落在 min/max 內）
| 線 | 配點方向 | 理由 |
|---|---|---|
| 日 | STR、SPI 高，AGI 低 | STR 只加遠程射程（`ranged_distance`），SPI 縮短 CD；站得遠所以不需要閃避 |
| 月 | AGI 最高（成年 150） | 移速與閃避；近戰判定體隨 STR 縮放，所以 STR 維持中等 |
| 星 | CON 最高 | 衝鋒在前排吃傷害；搭配特性「受擊疊雷專精」 |

衍生值（`tools/balance/derived_stats.py`，招式＝各自的 signature move）：

| id | 階段 | HP | 移速 m/s | 閃避‰ | 招式 CD ms | 招式射程/判定 |
|---|---|---|---|---|---|---|
| sun_mirror_chick | 幼年 | 780 | 4.26 | 36 | 1293 | 26.76 m |
| sun_heliostat_heron | 少年 | 879 | 4.518 | 75 | 3100 | 28.488 m |
| sun_array_skyheron | 成年 | 1026 | 4.823 | 127 | 5384 | 30.528 m |
| moon_night_pup | 幼年 | 750 | 4.518 | 75 | 900 | 判定體 ×1.06（線性） |
| moon_shade_wolf | 少年 | 857 | 4.823 | 127 | 2117 | 突進 6 m（dash 不隨 STR 縮放） |
| moon_longnight_wolf | 成年 | 985 | 5.2 | 200 | 5075 | 突進 8 m（dash 不隨 STR 縮放） |
| star_blaze_foal | 幼年 | 807 | 4.333 | 47 | 1170 | 突進 4 m（dash 不隨 STR 縮放） |
| star_storm_colt | 少年 | 937 | 4.62 | 92 | 3482 | 判定體 ×1.11 |
| star_skyhoof_steed | 成年 | 1090 | 4.947 | 150 | 5872 | 突進 12 m（dash 不隨 STR 縮放；SOL B6） |

## 需要人工決定的點
1. **抗性呈軟三角**：日弱影（−20/−30/−40）、月弱雷、星弱光。這讓三隻互相克制、選哪隻開局都有明確的弱點，但等於在御三家之間放了一個小型相剋表。Q-C1 說「沒有全域相剋表」，這裡只是三隻自己的 `resist`，不違反資料契約；若不想要，把三個負值改成各自不同的屬性即可。
2. **日的「引導施法」**：combat.md 沒有引導／持續施法機制，三招都先做成一般 `line` 光束；「持續」只放在演出描述。若要真的引導，需要新的招式欄位（另開問題）。
3. **特性每條線共用一個**（三階段引用同一個 trait id）；也可以改成每階段升級版。
4. 只放了同線 `evolution_family` 邊；跨線關係留給 P5-06。
5. 星成年的突進招 `range_class: mid`、路徑 12 m：大於近戰突進，仍遠小於 Q-C19 的 36 m 上限。範圍放電由少年招式（caster burst）負責；成年精通招是直線突進（TERRA 指出，與「突進＋範圍放電」的定位相符，但成年本身不帶範圍招）。
6. 中英名稱與圖鑑文案在 `copy-terra.json`（TERRA）。Opus 驗收：數量 9/9/3、id 一致、不含原型真名／音譯、繁中 49–52 字、英文 30–33 字；`star_blaze_foal` 英文名原案 Voltrot 與既有作品角色過近，改為 Voltfoal。TERRA 另指出月成年外觀原寫「瞬間消失、從背後現身」與招式（直線突進）不符，已改成雪幕掩護下貼地疾衝。

## ai_style（2026-11-07，P2-10 / Q-C21，待人工）
三線依使用者定案逐階一致：日 `keep_distance`、月 `flank`、星 `charge`（`docs/design/combat.md#戰鬥-ai`）。

## 修訂 2026-11-07（P5-08 A6/A13，使用者裁決）
- A13 精通招與 P5-05 招式池去重（Claude Opus 提案，SOL 已複核）：`shadow_nightpaw_swipe` damage 35 → both 30＋目標影抗 −5／4 s（原與 `shadow_dusk_fang` 八欄相同；改 sweep 會與 `shadow_umbral_fang` 相同，故保留 front）；`light_mirror_glint` 40 → both 38＋目標閃避 −10%／3 s；`light_solar_array_lance` 110 → both 95＋目標光抗 −10／6 s（威力 38／95 依 SOL 複核：35 低於招式池帶 −22%、100 計入隊友共享降抗後 +22.5%）。月線三招因此一致帶「削弱影抗」。
- A6 月線三階的 `concept.role`、`stage_look`、圖鑑與 `trait_shadow_longnight_stalker` 文案由 TERRA 改寫為繞背、直線突進、貼身作戰（戰鬥沒有潛行或自動後撤）；`docs/design/creatures.md` 月線定位同步。
