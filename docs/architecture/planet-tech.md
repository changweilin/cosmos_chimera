# 球形星球技術

## 從參考作學到的（sakura-crossing，Three.js）
| 參考作做法 | 對本作的意義 |
|---|---|
| R = 160 m，所有尺寸由單一 `R` 推導 | 沿用：`PlanetConfig.Radius` 單一來源 |
| 內容在平面 XZ 上製作，再以等距圓柱投影包到球上（x→經度、z→緯度） | 適合「一條赤道帶」的城鎮；極區壓縮，所以參考作把玩家緯度夾在 ±0.24 圓周。本作要全球可走 → **不沿用**（ADR-0001） |
| `basisAt(x,z)` 給 east/up/north 正交框架，攝影機 up 跟隨地表法線 | 沿用概念：`PlanetFrame.At(worldPos)` |
| 長邊三角形細分後再彎曲，避免弦切穿星球 | 沿用：大型手作物件放上球面前先細分 |
| 遠景用 icosphere 避免極點擠壓 | 改為 cube-sphere，同樣無極點問題且易於四叉樹 LOD |
| 卡通描邊：螢幕空間深度二階差分 + 主角物件反向外殼 | 參考：本作角色/宙獸用反向外殼描邊，地形不描邊 |

## 幾何：Cube-sphere 四叉樹
- 6 個立方體面，各一棵四叉樹；葉區塊固定網格解析度（暫定 33×33 頂點）。
- 立方體→球用等面積修正映射（減少面中心/角落的密度差）。
- LOD 依攝影機距離分裂/合併；相鄰 LOD 以裙邊或縫合索引消除裂縫。
- 高度：`height(dir)` 只依單位方向向量取樣（3D 噪聲 + 模板），所以面與面之間天生無縫。
- 網格生成用 Jobs + Burst；碰撞只為攝影機附近（約 128 m）的葉區塊烘焙 MeshCollider。

## 真實地圖模板投影
模板是 2D 柵格。以模板中心方向為切點，用 **gnomonic（日晷）局部投影** 貼到球面；單一模板角半徑 ≤ 25° 時形變可接受。多塊模板之間用噪聲山脈接縫。

## 重力與角色
- `up = normalize(pos − center)`；重力 = `−up × g`。
- ⚠️ Unity `CharacterController` 的膠囊永遠沿世界 Y 軸，**不能**用於球面。改用 Rigidbody（`useGravity=false`）+ 自訂地面偵測，或自寫 kinematic controller。
- Cinemachine 3：每幀設定 World Up Override 為角色的當地 up。
- 宙獸/敵人導航：內建 NavMesh 假設平面世界 → 待評估（Q-T1）：區域局部 NavMesh（每個戰鬥區域一塊有向 NavMeshSurface）vs. 高度場上的自訂 steering。

## 浮點精度
R = 1280 m 時最大座標約 2.6 km，float 精度約 0.25 mm，**不需要** floating origin。若日後 R > 8 km 才需重新評估。

## 地平線
`horizon(h) = sqrt(2Rh + h²)`；眼高 1.7 m：R=320 → 33 m，R=640 → 47 m，R=1280 → 66 m。地平線近是特色，高地標（巨樹、遺跡塔、BOSS 巢穴）需高到能從地平線後探頭作為導航。
