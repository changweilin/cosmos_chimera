# P5-05 moves batch 06 第二審 · TERRA

> 2026-11-07 · ChatGPT Terra（`codex exec -s read-only -m gpt-6.1-terra`），初稿來源 Gemini Flash。唯讀；未修改檔案。Claude Opus 核對：#3–#6 引用的公式與 README 原文屬實，已改 README 文案並把 `light_blinding_glare` 改名為 `light_prismatic_exposure`；#1（場地總威力偏低）、#2（與舊批近似）屬平衡/定位判斷，**待人工決定**。
## 總結

發現 **6 項問題**：1 項場地價值偏低、1 項舊批近似招式、1 項命名誤導，以及 3 項 README 機制敘述錯誤或尚未定案。

十招疊層在基礎 CD 下皆可於到期前重施，**沒有不可達上限**；重施成功會刷新整疊。未發現 batch 06 內可排除屬性、射程與形狀差異的全面支配組合。未重跑 schema 或數值帶檢查。

## 逐條發現

### 1. `metal_caltrop_zone`、`wood_tangle_grove` · 場地價值偏低 · med

兩招分別為 **48 威力／10,000 ms CD**、**32 威力／10,500 ms CD**；後者另附閃避 −10%。比較舊批 `metal_siege_bombard` 的 **115 威力／8,800 ms CD**，即使目標吃滿四次場地結算，金屬場地總威力仍低於一半，且回收較慢。`wood_tangle_grove` 的傷害更低。

場地有持續封鎖與較高命中率的價值，並非嚴格支配；但半徑 3 m、目標可提前離開，使這兩招值得優先試玩。

**建議：**優先模擬目標停留 1–4 次結算的情境；若封鎖價值不足，縮短 CD 或提高單次威力。

### 2. 舊批近似招式 · 設計 · med

下列招式符合相同屬性、效果與方向，定位接近；場地持續判定或疊層節奏仍構成差異，故不視為全面支配：

- `water_flow_state` 與 `water_vapor_veil`：自身閃避增益；前者最高 +35%、需五次施放，後者立即 +25%、持續 8 秒。
- `earth_lithic_plating` 與 `earth_granite_stance`：自身土抗增益；前者最高 +30%、需三次施放，後者立即 +30%、持續 12 秒。
- `wind_ballistic_focus` 與 `wind_tailwind_boon`：自身射程增益；前者最高 +32%、需四次施放，後者立即 +25%、持續 8 秒。
- `ice_frigid_breath` 與 `ice_numbing_haze`：冰屬性敵方減速；前者有傷害、最高 −32%，後者立即 −25%、持續 8 秒。
- `thunder_voltage_tracer` 與 `thunder_draining_bolt`：雷屬性長距直線傷害並增加敵方全屬性 CD；前者最高 +32%、需四次命中，後者立即 +20%。
- `shadow_stygian_pit` 與 `shadow_enervating_mire`：暗屬性範圍削弱暗專精；前者反覆施加 −10，後者立即施加 −25、持續 8 秒。

**建議：**確認是否保留「立即生效」對「持續佈署／逐次累積」的定位差異；若內容配額要求避免近似，優先調整效果或戰術用途。

### 3. `light_blinding_glare（已改名 light_prismatic_exposure）` · 命名 · med

`blinding` 暗示致盲或降低命中，但 JSON 只有 **光抗性 −15**，持續 3 秒；沒有命中率減益。

**建議：**草稿 ID 可改為 `light_prismatic_exposure`，並使用對應的曝光／削弱抗性命名。

### 4. `wood_vernal_growth` · README 數值解讀 · med

README 稱滿層木專精 **+32 等於原始傷害 +32%**。依公式 `power × (1 + mastery / 100)`，這是增加 32 個威力基準的百分點，實際相對增幅取決於原本專精；例如原專精 100 時，原始傷害由 2 倍變 2.32 倍，增加 **16%**。

**建議：**改稱「木專精 +32；對原始傷害的相對增幅取決於原本專精」。

### 5. `metal_corrosion_edge` 等疊層、場地待決問題 · README 機制 · med

README 將整疊到期方式與場地半徑 STR 縮放列為待確認；`combat.md` 已定案：重施刷新整疊、到期整疊消失；遠程場地的施放距離受 STR 縮放，**3,000 mm 半徑不變**。README 另聲稱各招上限 ≤40 可防止與其他來源合併時的數學溢位；單招上限本身不足以證明跨來源加總不溢位。

**建議：**將前兩項改為已定規則；將溢位敘述改為「限制單一招式的效果幅度」。

### 6. 場地閃避敘述 · README 文案 · low

README 稱高閃避或適時翻滾可「安全穿越」場地。高閃避仍有命中機率下限；400 ms 無敵窗也僅保護與其重疊的結算，不能保證跨越整個場地。

**建議：**改為「高閃避可降低受擊機率；適時翻滾可避開落在無敵窗內的結算」。

## 場地與疊層核對

場地總威力是假設同一目標留在場內、每次均命中的理論值；實際傷害另受命中、專精、抗性與逐次傷害結算影響。

| 場地 ID | 結算次數 | 單次威力 × 次數 | CD |
|---|---:|---:|---:|
| `metal_caltrop_zone` | 4 | 12 × 4 = 48 | 10,000 ms |
| `wood_tangle_grove` | 4 | 8 × 4 = 32 | 10,500 ms |
| `water_vortex_basin` | 5 | 10 × 5 = 50 | 12,000 ms |
| `fire_ember_carpet` | 5 | 14 × 5 = 70 | 11,500 ms |
| `earth_quicksand_sink` | 6 | 12 × 6 = 72 | 13,500 ms |
| `ice_tundra_expanse` | 6 | 16 × 6 = 96 | 13,000 ms |
| `wind_monsoon_domain` | 5 | 0 × 5 = 0 | 11,000 ms |
| `thunder_conductive_grid` | 7 | 18 × 7 = 126 | 15,000 ms |
| `light_blinding_glare（已改名 light_prismatic_exposure）` | 6 | 0 × 6 = 0 | 13,000 ms |
| `shadow_stygian_pit` | 7 | 14 × 7 = 98 | 15,500 ms |

高端的 `thunder_conductive_grid` 理論威力 126，與舊批 `thunder_megavolt_burst` 的 **115／8,800 ms** 相比，須停留七次結算且 CD 為 15,000 ms；尚不足以判定場地支配瞬發招。兩招零威力場地依減益與區域控制衡量，不宜單憑傷害判為無用。

以下滿層時間以基礎 CD、首次立即施放及之後每次成功施加計算；實際 CD 受精神與效果修正。

| 疊層 ID | 滿層幅度 | 基礎滿層時間 | 每次刷新後持續 |
|---|---:|---:|---:|
| `metal_corrosion_edge` | −32 | 6,600 ms | 6,000 ms |
| `wood_vernal_growth` | +32 | 12,000 ms | 8,000 ms |
| `water_flow_state` | +35% | 14,000 ms | 7,000 ms |
| `fire_slag_rake` | −35 | 10,000 ms | 6,000 ms |
| `earth_lithic_plating` | +30 | 9,000 ms | 8,000 ms |
| `ice_frigid_breath` | −32% | 7,200 ms | 5,000 ms |
| `wind_ballistic_focus` | +32% | 11,400 ms | 8,000 ms |
| `thunder_voltage_tracer` | +32% | 9,600 ms | 5,000 ms |
| `light_harmonic_cadence` | −32% | 12,600 ms | 8,000 ms |
| `shadow_pitch_lob` | −30% | 5,600 ms | 6,000 ms |

雖然多數滿層時間長於單次持續時間，每次成功重施都會刷新整疊，且各招基礎 CD 均短於持續時間；攻擊招未命中或延遲過久仍可能使整疊到期。

