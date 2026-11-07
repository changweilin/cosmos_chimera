# P3-01 真實地圖資料授權調查與署名方案（Q-W1）

> **免責聲明**：本報告僅供《宙獸紀 · COSMIMERA》工程與產品決策參考，**不構成正式法律意見或合規保證**。文中標註「⚠️ 不確定／待確認」之灰色地帶，發行前須由執業律師審閱確認。所有外部來源查詢日期均為 **2026-11-07**。

## 背景與使用情境
- **發行型態**：商業付費遊戲（Steam / Windows，單機為主＋1–4 人連線），離線地理模板包隨遊戲建置封包發行，執行期不連網。
- **資料流（`P3-02` → `P3-03`）**：離線工具擷取真實世界高程與地表特徵 → 剝除地名／屬性／拓撲並降採樣、風格化變形為 **≥ 12 個 2D 柵格地理模板**（高程、海岸線、水系、土地覆蓋遮罩）→ 執行期取 3–8 塊以 gnomonic 投影拼貼至 `R = 320–1280 m` 球面，疊加 3D 噪聲與接縫山脈。遊戲內**不顯示真實地名，亦不提供真實地圖瀏覽**。

---

## 1. OpenStreetMap（ODbL 1.0）深度評估

### 1.1 官方依據（每項查詢日期：2026-11-07）
- **ODbL 1.0 條文**（§1.0 定義、§4.3 產出物聲明、§4.4 同等分享、§4.5 集體資料庫界限、§4.6 衍生資料庫存取、§4.7 技術保護措施與平行散布）：<https://opendatacommons.org/licenses/odbl/1-0/>（查詢日期：2026-11-07）
- **OSM 版權頁**：<https://www.openstreetmap.org/copyright>（查詢日期：2026-11-07）
- **OSMF Attribution Guidelines（含 *Computer games and simulations* 專節）**：<https://osmfoundation.org/wiki/Licence/Attribution_Guidelines#Computer_games_and_simulations>（查詢日期：2026-11-07）
- **OSMF Produced Work - Guideline**：<https://osmfoundation.org/wiki/Licence/Community_Guidelines/Produced_Work>（查詢日期：2026-11-07）
- **OSMF Substantial - Guideline**：<https://osmfoundation.org/wiki/Licence/Community_Guidelines/Substantial>（查詢日期：2026-11-07）
- **OSMF Horizontal Map Layers - Guideline**：<https://osmfoundation.org/wiki/Licence/Community_Guidelines/Horizontal_Map_Layers>（查詢日期：2026-11-07）
- **OSMF Collective Database Guideline**：<https://osmfoundation.org/wiki/Licence/Community_Guidelines/Collective_Database_Guideline>（查詢日期：2026-11-07）

### 1.2 核心授權定性分析
1. **Substantial（實質性門檻）**：依 *Substantial - Guideline*，非系統性且單次擷取 **< 100 個地理特徵（features）** 方視為非實質（Insubstantial）。本作擷取 ≥ 12 個區域之海岸線、水系與土地覆蓋多邊形，總特徵數必遠超 100，**須按「實質擷取」遵循 ODbL 義務**。
2. **Produced Work（產出物）vs. Derivative Database（衍生資料庫）**：
   - 依 ODbL §1.0 與 *Produced Work - Guideline*，由資料庫查詢渲染出之 2D 柵格圖像、3D 場景或視聽作品屬於 **Produced Work**，前提是**下游無法從該產出物提取、複製或逆向重建（recreate）原始 OSM 實質資料**。
   - **有利因素**：`P3-02` 將向量大幅降採樣為低解析度風格化遮罩，且剝除所有座標基準、OSM ID、標籤與地名，客觀上無法還原原始 OSM 向量資料庫，傾向成立 **Produced Work**（僅需依 §4.3 署名，無須開放模板）。
   - **⚠️ 不確定處（機械可讀中間資產與 §4.6 陷阱）**：
     - **爭議 A**：隨遊戲發行的 12+ 張模板檔雖為柵格遮罩，但由 `P3-03` 程式動態讀取拼貼，保守法務見解可能將「供程式查詢取樣的結構化柵格包」視為 **Derivative Database**。
     - **爭議 B（ODbL §4.6）**：即使最終模板屬 Produced Work，若 `P3-02` 先將 OSM 與其他資料混編為中間 **Derivative Database** 再烘焙模板，§4.6 仍要求向受領者免費提供該衍生資料庫之機器可讀副本或修改腳本／差異檔（alterations file）。
3. **Share-Alike（§4.4）是否會波及遊戲資產或程式碼？**
   - **不會波及程式碼、美術與獨立圖層**。依 ODbL §4.5(b)、*Collective Database* 與 *Horizontal Map Layers* 指引，將 OSM 衍生資料與獨立來源（C# 程式碼、宙獸資料、3D 美術、獨立通道之 DEM 高程）組合為集體資料庫或水平圖層時，**Share-Alike 效力僅限於 OSM 該層本身**。
   - **工程安全港（Safe Harbor）**：若使用 OSM，(1) 在 `P3-02` 將 OSM 維持獨立單純柵格化通道，不在資料庫層與 DEM 混編；(2) 可依 ODbL §4.6 與 §4.7(b)（平行散布條款，解決 Steam 封包／DRM 疑慮）直接將「純 OSM 降採樣遮罩檔＋轉換腳本」以 ODbL 公開於官網／GitHub，即可零成本消除衍生資料庫爭議，且不洩露任何專有資產。
4. **署名位置與寫法**：
   - 依 OSMF *Attribution Guidelines (Computer games and simulations)*，電玩遊戲可擇一或組合放置於：**啟動畫面（Splash screen）、遊戲畫面、選單系統、或主選單可直達之製作人員名單（Credits 頁）**，須清晰可讀且停留時間足以閱讀。
   - 必備文字：`© OpenStreetMap contributors`，並標明資料以 ODbL 授權釋出及附上 `https://www.openstreetmap.org/copyright`（注意：離線烘焙須用 Planet OSM / Geofabrik PBF 或 `osmdata.openstreetmap.de`，**不得抓取 `tile.openstreetmap.org` 圖磚**以免觸發額外 Tile Usage Policy）。

---

## 2. 高程與水深資料（DEM / Bathymetry）商用比較

| 資料集 | 解析度 / 範圍 | 授權性質 | 商用 | 隨遊戲再散布模板 | 強制義務 |
|---|---|---|---|---|---|
| **Copernicus DEM GLO-30 / GLO-90** | 30m / 90m（全球） | 歐盟 Copernicus 專屬免費開放授權 | ✅ | ✅（含修改品） | **強制特定署名＋免責聲明**（Art. 6） |
| **SRTM v3 (SRTMGL1)** | 30m（60°N–56°S） | 美國政府公有領域（NASA/USGS） | ✅ | ✅ | 法律上免強制；LP DAAC 要求禮貌引用 |
| **NASADEM (v001)** | 30m（60°N–56°S） | 美國政府公有領域（NASA EOSDIS） | ✅ | ✅ | 同上（建議於 Credits 引用） |
| **ASTER GDEM v3** | 30m（83°N–83°S） | 美日（NASA/METI）開放資料政策 | ✅ | ✅（僅限 v3） | 要求標註 METI 與 NASA 共同產出 |
| **GEBCO Gridded Bathymetry** | 15″ ≈ 450m（全球海陸） | 公有領域（GEBCO 使用條款） | ✅ | ✅ | **強制來源署名**；禁航海用途、禁暗示背書 |
| **ETOPO 2022** | 15″/30″/60″（全球海陸） | 美國政府公有領域 / CC0 1.0 | ✅ | ✅ | 建議標準引用；禁航海用途 |

### 各項條款細節、署名文字與官方來源（查詢日期：2026-11-07）
1. **Copernicus DEM（GLO-30 / GLO-90）**
   - **條件**：依《Licence for Copernicus DEM》（Art. 4 & 5）全球免費授權重製、散布、修改及與其他資料結合，明確允許商用。注意僅限公開版 **GLO-30 / GLO-90**（10m 級 EEA-10 / WorldDEM 具商用限制；另 GLO-30 公開版缺亞美尼亞／亞塞拜然局部 2 圖磚）。Art. 6(c) 要求對公眾散布修改品時須於授權／法務警告中加入指定免責句；Art. 6(d) 禁止暗示官方背書；Art. 6(e) 要求若賦予下游再散布權須傳遞義務。
   - **修改品署名（Art. 6(b)）**：`"Produced using Copernicus WorldDEM-30 © DLR e.V. 2010-2014 and © Airbus Defence and Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA; all rights reserved."`（若用 GLO-90 則對應標示 `WorldDEM-90`）。
   - **法務免責句（Art. 6(c)）**：`"The organisations in charge of the Copernicus programme by law or by delegation do not incur any liability for any use of the Copernicus WorldDEM-30."`
   - **來源**：<https://spacedata.copernicus.eu/collections/copernicus-digital-elevation-model> · 授權全文：<https://docs.sentinel-hub.com/api/latest/static/files/data/dem/resources/license/License-COPDEM-30.pdf>（查詢日期：2026-11-07）
2. **SRTM（NASA / USGS）**
   - **條件**：美國聯邦政府作品（17 U.S.C. § 105）與 NASA EOSDIS / USGS 開放政策，無商用或再散布限制。缺點為無 >60°N 高緯度峽灣（如挪威、冰島）。
   - **建議署名**：`"Elevation data courtesy of NASA JPL and the U.S. Geological Survey (SRTM)."`
   - **來源**：<https://lpdaac.usgs.gov/data/data-citation-and-policies/> · <https://lpdaac.usgs.gov/products/srtmgl1v003/>（查詢日期：2026-11-07）
3. **NASADEM（NASA JPL）**
   - **條件**：SRTM 重處理填補空洞版，同屬 NASA EOSDIS 開放政策（無商用／再散布限制，範圍仍限 60°N–56°S）。
   - **建議署名**：`"NASADEM Merged DEM Global 1 arc second V001, courtesy of NASA JPL / NASA EOSDIS LP DAAC."`
   - **來源**：<https://www.earthdata.nasa.gov/engage/open-data-services-and-software/data-and-information-policy> · <https://lpdaac.usgs.gov/products/nasadem_hgtv001/>（查詢日期：2026-11-07）
4. **ASTER GDEM v3（METI / NASA）**
   - **條件**：2019 年釋出之 **Version 3 (`ASTGTM.003`)** 已取消再散布限制並允許商用（⚠️ **嚴禁誤用舊版 v2**，v2 禁止原始資料再散布）。惟光學立體匹配於水面與陡岸偽影／坑洞顯著多於雷達 DEM。
   - **署名文字**：`"ASTER GDEM is a product of METI and NASA."`
   - **來源**：<https://lpdaac.usgs.gov/products/astgtmv003/> · <https://ssl.jspacesystems.or.jp/ersdac/GDEM/E/>（查詢日期：2026-11-07）
5. **GEBCO（全球海底地形網格）**
   - **條件**：置於公有領域，明確允許商業開發、修改與打包散布；嚴禁作真實航海安全用途，不得暗示 IHO/IOC 背書。
   - **強制署名**：`"Bathymetric data derived from the GEBCO Grid (GEBCO Compilation Group, https://www.gebco.net)."`
   - **來源**：<https://www.gebco.net/about_us/terms_of_use/> · <https://www.gebco.net/data_and_products/gridded_bathymetry_data/>（查詢日期：2026-11-07）
6. **ETOPO 2022（NOAA NCEI）**
   - **條件**：美國政府公有領域／CC0 1.0，商用與再散布無限制（禁航海用途）。注意其底層融合 Copernicus DEM 與 GEBCO 等來源，15″（~450m）適合群島大陸棚宏觀輪廓，局部細節仍需 30m DEM。
   - **建議署名**：`"NOAA National Centers for Environmental Information (2022): ETOPO 2022 15 Arc-Second Global Relief Model (https://doi.org/10.25921/fd45-gt74)."`
   - **來源**：<https://www.ncei.noaa.gov/products/etopo-global-relief-model>（查詢日期：2026-11-07）

---

## 3. 其他可替代或互補資料集（查詢日期：2026-11-07）

1. **Natural Earth（1:10m / 1:50m 向量與柵格）**
   - **授權與條件**：**公有領域（Public Domain）**，商用與再散布完全自由，連署名皆為選用。惟最高比例尺 1:10m（精度約 1–2 km）僅適合大陸／群島宏觀輪廓，對單一峽灣或三角洲細部遮罩過粗。
   - **選用署名**：`"Made with Natural Earth."`
   - **來源**：<https://www.naturalearthdata.com/about/terms-of-use/>（查詢日期：2026-11-07）
2. **ESA WorldCover（10m 全球土地覆蓋，v100 / v200）**
   - **授權與條件**：**CC BY 4.0**（允許商用、修改與隨遊戲再散布，**無 Share-Alike 傳染性**）。須標註來源、授權連結、已修改說明及底層 Copernicus Sentinel 聲明，且不得對素材本身施加阻礙 CC BY 權利之額外法律限制（CC BY 4.0 §2(a)(5)(B)）。具備 10m 水體（Class 80）、森林、濕地、紅樹林、冰雪等 11 類柵格，**可直接取代 OSM 土地利用與高解析海岸／水域遮罩**。
   - **強制署名**：`"Modified from © ESA WorldCover project 2021 / Contains modified Copernicus Sentinel data (2021) processed by ESA WorldCover consortium (CC BY 4.0, https://creativecommons.org/licenses/by/4.0/)."`
   - **來源**：<https://esa-worldcover.org/en/data-access> · <https://doi.org/10.5281/zenodo.7254221>（查詢日期：2026-11-07）
3. **HydroSHEDS / HydroRIVERS（⚠️ 注意商用與再散布限制）**
   - **授權分裂與陷阱**：
     - **HydroSHEDS v1 / HydroRIVERS v1.0（全球版）**：採用附於各產品技術文件（Appendix A）之**自訂授權合約**（WWF / Lehner）。雖官網現標示允許商用，但自訂條款**嚴格禁止作為獨立產品再散布，並禁止未經書面許可向第三方提供原始或轉換後資料存取**（且早期 v1.1/v1.2 鏡像更載有非商用限制與賠償條款）。隨商業遊戲封包散佈水系模板存在違約解釋風險。
     - **HydroSHEDS v2**：已改採 **CC BY 4.0**，但自 2026-07-09 首波釋出起採分區推進（初期僅覆蓋美洲），截至 2026-11 尚未完成全球完整產品線替換。
   - **來源**：<https://www.hydrosheds.org/terms-of-use> · <https://www.hydrosheds.org/products/hydrorivers> · <https://www.hydrosheds.org/hydrosheds-v2>（查詢日期：2026-11-07）

---

## 4. 結論與執行建議

### 4.1 推薦採用清單
- **方案 A（首選推薦 · 零 Share-Alike 風險、幾何自洽）**：完全不依賴 OSM，以純開放／署名資料完成 `P3-02`：
  1. **陸域高程**：**Copernicus DEM GLO-30**（覆蓋冰島／挪威高緯峽灣與全球火山島）＋ `60°N–56°S` 可輔以 **NASADEM**。
  2. **海底與大陸棚過渡**：**ETOPO 2022**（或 **GEBCO**）。
  3. **海岸線、水體與土地覆蓋遮罩**：**ESA WorldCover v200 (10m, CC BY 4.0)**。
  4. **主河系遮罩**：於 `P3-02` 直接對 30m DEM 執行離線水文流向演算（D8 填窪＋匯流累積）結合 WorldCover 水體（河道必落於 DEM 谷底，解決向量河道與高程錯位問題，且**零額外授權負擔**）。
  5. **宏觀大洲輪廓備援**：**Natural Earth**。
- **方案 B（備選 · 需局部 OSM 河網／海岸向量時）**：在方案 A 外加入 **OSM（離線 PBF）**，但 `P3-02` 須將 OSM 遮罩存為獨立檔案／通道（不與 DEM 混編），並於官網／GitHub 平行公開該 12+ 張純 OSM 降採樣遮罩（ODbL 1.0），徹底滿足 ODbL §4.4/§4.6/§4.7(b)。

### 4.2 避免清單（及原因）
1. **HydroSHEDS v1 / HydroRIVERS v1.0**：自訂條款限制再散布與第三方存取，且 v2 全球覆蓋未齊；由 DEM 直接算 D8 水系更安全精準。
2. **ASTER GDEM（舊版 v2 及現行 v3）**：v2 禁再散布；v3 水面／陡坡立體匹配噪點嚴重，品質遜於 Copernicus GLO-30 與 NASADEM。
3. **OSM 官方圖磚（`tile.openstreetmap.org`）與現代道路層（`highway=*`）**：圖磚禁批次抓取；奇幻星球路徑由 `P3-05` 區域連通圖生成即可。
4. **Copernicus DEM 10m（EEA-10 / WorldDEM 商業層級）**：非免費公開商用授權。

### 4.3 遊戲內 Credits 與 EULA 署名範本（主選單「製作人員名單／法務聲明」可直達）
**English Template (Credits & Legal Notices / EULA):**
```text
Geospatial Terrain Acknowledgements:
- Elevation templates produced using Copernicus WorldDEM-30 © DLR e.V. 2010-2014 and © Airbus Defence and Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA; all rights reserved. The organisations in charge of the Copernicus programme by law or by delegation do not incur any liability for any use of the Copernicus WorldDEM-30.
- Additional elevation & bathymetry derived from NASADEM (NASA JPL / LP DAAC), ETOPO 2022 (NOAA NCEI, CC0 1.0), and the GEBCO Grid (GEBCO Compilation Group, https://www.gebco.net). Not for navigation.
- Land cover & water masks modified from © ESA WorldCover project 2021 / Contains modified Copernicus Sentinel data (2021) processed by ESA WorldCover consortium (licensed under CC BY 4.0, https://creativecommons.org/licenses/by/4.0/).
- [若採方案 B 始保留] Coastline/hydrography masks derived from OpenStreetMap data, © OpenStreetMap contributors, available under the Open Database License (ODbL 1.0, https://www.openstreetmap.org/copyright).
```
**繁體中文對照範本（建議中英並列以確保條文精確度）：**
```text
地理地形資料致謝與聲明：
- 高程模板係使用 Copernicus WorldDEM-30 製作（© DLR e.V. 2010-2014 及 © Airbus Defence and Space GmbH 2014-2018，由歐盟與歐洲太空總署於 COPERNICUS 計畫下提供；保留所有權利）。依法或受託負責 Copernicus 計畫之機構對 Copernicus WorldDEM-30 之任何使用概不承擔任何責任。
- 補充高程與水深衍生自 NASADEM（NASA JPL / LP DAAC）、ETOPO 2022（NOAA NCEI, CC0 1.0）及 GEBCO 網格（GEBCO Compilation Group, https://www.gebco.net）。本資料不得用於真實航行導航。
- 土地覆蓋與水域遮罩修改自 © ESA WorldCover 計畫 2021／包含由 ESA WorldCover 聯盟處理之修改後 Copernicus Sentinel 資料（依 CC BY 4.0 授權：https://creativecommons.org/licenses/by/4.0/）。
- [若採方案 B 始保留] 海岸線與水系遮罩衍生自 OpenStreetMap 資料，© OpenStreetMap 貢獻者，依開放資料庫授權條款（ODbL 1.0）提供（https://www.openstreetmap.org/copyright）。
```

### 4.4 需律師最終確認之殘留風險點
1. **⚠️ ODbL 定性與平行散布（僅方案 B 適用）**：確認隨遊戲封包之離線降採樣柵格遮罩屬 Produced Work，或透過官網平行提供 ODbL 遮罩包（§4.7(b)）即可完全免除 Steam DRM 與 §4.6 爭議。
2. **⚠️ Steam 封包／DRM 與 CC BY 4.0 §2(a)(5)(B)**：確認將修改後之 WorldCover 遮罩編入 Unity 資產封包與 Steam 發行標準保護，不構成對原始 CC BY 素材本身施加禁止之有效技術保護措施（必要時同樣可將未加密模板遮罩隨目錄或外部連結平行提供）。
3. **⚠️ Copernicus DEM Art. 6(e) 下游義務**：確認在遊戲 EULA 禁止玩家拆包再散布之前提下，於遊戲內 Credits 與 Steam EULA 同時刊載 Art. 6(b)(c)(d) 原文即滿足全部授權條件。
4. **⚠️ 目標發行區測繪法規**：本作模板經縮放變形、球面拼貼且無真實地名與國界，確認於重點發行市場（如中國大陸《測繪法》審圖規範）不構成實質地圖產品。
