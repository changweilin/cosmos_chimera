# Planet Types Draft 01 (`p3-planet-types-draft-01`)

- **來源規範**：[`docs/design/planet-world.md`](file:///docs/design/planet-world.md)（§星球類型）
- **關聯項目**：Roadmap P3-04 資料初稿
- **Schema 驗證目標**：[`Data/schemas/planet-type.schema.json`](file:///Data/schemas/planet-type.schema.json)
- **產出模型**：FLASH (Gemini Flash)

---

## 1. 星球類型對照表 (Planet Types)

| id | 繁中名 | English Name | 日本語名 | 尺寸 (size) | 屬性偏好 (element_bias) | 地貌標籤 (terrain_tags) |
|---|---|---|---|---|---|---|
| `verdant_planet` | 翠綠星 | Verdant Planet | 翠緑星 | `medium` | `["wood", "water", "wind"]` | `["forest", "river_valley"]` |
| `lava_planet` | 熔岩星 | Lava Planet | 溶岩星 | `small` | `["fire", "earth"]` | `["volcano", "rift_valley"]` |
| `ice_crystal_planet` | 冰晶星 | Ice Crystal Planet | 氷晶星 | `medium` | `["ice", "water"]` | `["ice_field", "fjord"]` |
| `storm_planet` | 風暴星 | Storm Planet | 嵐星 | `large` | `["thunder", "wind"]` | `["plateau", "archipelago"]` |
| `mineral_planet` | 晶礦星 | Mineral Planet | 晶鉱星 | `small` | `["metal", "earth"]` | `["canyon", "cave"]` |
| `twilight_planet` | 晨昏星 | Twilight Planet | 晨昏星 | `large` | `["light", "shadow"]` | `["eternal_day", "eternal_night"]` |

---

## 2. 地貌標籤詞彙表 (Terrain Tag Vocabulary)

全域共用單一詞彙表（snake_case 識別碼，共 12 項，無數值欄位）：

| Tag ID | 對應繁中 | 一句話定義 |
|---|---|---|
| `forest` | 森林 | 茂密植被覆蓋的林地與樹冠帶。 |
| `river_valley` | 河谷 | 由主水系侵蝕沖刷而成的帶狀平緩低谷。 |
| `volcano` | 火山 | 熔岩噴湧、火山錐與高熱岩漿匯聚之高地。 |
| `rift_valley` | 裂谷 | 地殼構造張裂形成的深邃斷裂帶與岩石落差。 |
| `ice_field` | 冰原 | 廣袤平坦的永久凍土與厚重冰冠平原。 |
| `fjord` | 峽灣 | 冰川侵蝕切割形成、海水深入兩側陡壁的狹長水域。 |
| `plateau` | 高原 | 頂部開闊平緩、周緣具劇烈起伏的高海拔臺地。 |
| `archipelago` | 群島 | 大量島嶼散佈環繞的水體與海域景觀。 |
| `canyon` | 峽谷 | 岩層劇烈風化切割形成的深狹岩壁與乾涸岩溝。 |
| `cave` | 洞窟 | 地下溶洞、玄武岩空腔或天然結晶岩穴網絡。 |
| `eternal_day` | 半永晝 | 潮汐鎖定陽面，受恆星直射無黑夜的強光乾熱半球。 |
| `eternal_night` | 半永夜 | 潮汐鎖定背光面，無日照照射的極寒深邃暗影半球。 |

---

## 3. 待決問題與設計備註 (Open Issues & Ambiguities)

1. **命名選詞備案**：
   - `ice_crystal_planet`：若追求極簡亦可考慮 `glacial_planet` 或 `cryo_planet`；但原案為「冰晶」，保留 `ice_crystal_planet` 最貼合文本與區分度。
   - `mineral_planet`：若強調晶體亦可選 `crystalline_planet`；選用 `mineral_planet` 可避免與 `ice_crystal_planet` 共享「crystal」字首造成混淆。
   - `twilight_planet`：潮汐鎖定之「晨昏星」，天文學常用 terminator planet 或 twilight planet，此處選用 `twilight_planet` 兼顧科幻感與文藝意象。
2. **天候/光照標籤與地貌生成（`eternal_day` / `eternal_night`）**：
   - 晨昏星的兩組標籤本質為全球光照與半球劃分機制，而非純粹高度場模板（如峽灣或高原）。在 P3-03 / P3-04 生成管線中，需確認是否作為特殊遮罩處理。
3. **標籤未來擴充性**：
   - 目前維持各星球嚴格 2 項地貌偏好標籤，且不含數值；後續 P3-04 導入生物群系（Biome）生成時，是否由生成器自行疊加次級地貌（Secondary Scatter/Biome）待確認。
