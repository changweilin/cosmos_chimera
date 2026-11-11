# D0-E4 數值修正提案 · SOL

2026-11-10 · SOL（ChatGPT Sol，Codex，唯讀審查）。交 OPUS 於 `Features/Combat` 實作；**本輪未套用修正**。基準 `8e182da`，`Encounter.cs` 與 d3efca6 相同。

問題位置：`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:727`、`:736`、`:740`；消去徑向分量後，幾微米的 float 殘差被放大成單位 forward。消去已經存在；再次做同一次減法，或只把門檻換成 `1e-6*|to-from|`，都沒有修復保證。消費端 `Combat/Runtime/Model/HitVolume.cs:477` 要求 `|dot(forward,up)| <= 1e-3`。

原 r5 R5-07 輸入在本輪重新執行：

```text
PlanetConfig(640, (0,0,0))
from = (413.63202f, 96.05307f, -479.63602f)
to   = (414.2777f, 96.20301f, -480.38474f)
fallback = PlanetFrame.FromUp(planet.ToDirection(from)).North
old forward = (0.7524326f, -0.05130222f, 0.6566684f)
old |dot(forward,trueUp)| = 0.013517568310782047
UNITY_EDITOR: Overlaps -> ContractException
RELEASE_PROBE: Overlaps -> True（非法 forward 的前置條件已剝除）
proposed |dot(forward,trueUp)| = 9.633358499794298e-9
proposed Overlaps -> True（兩版皆正常通過前置條件）
```

兩個 True 不表示舊方向正確；見證要檢查向量不變式，不能只檢查是否命中。

## 數學與行為

以 float32 儲存輸入／輸出，用 double 中間值避免投影消去誤差放大。既有 MoveGeometry 亦用 double 計算球面幾何（HitVolume.cs:501），沒有新增跨 feature 依賴。

令 `r = double(from)-double(center)`、`n = r/|r|`、`d = double(to)-double(from)`，用

`q = (n × d) × n = d - n(n·d)`。

**先轉 double 再減**，不能寫成 `(double3)(to-from)`，也不能先以 float 算 `from-center`。對已儲存的 float 座標，這避免先丟掉低位再放大；不承諾重建 float 座標產生前已失去的資訊。

取 float32 unit roundoff `u=2^-24`，世界尺度 `S=max(1, |r|, max|from|, max|to|, max|center|)`，設定距離門檻

`tau = 16*u*S*(1+|d|/|r|)`（m）。

`|q| <= tau` 時保留 fallback 的當地切向方向；無有效 fallback 時沿用 PlanetFrame 的 North。此範圍只保證有限、單位、切向，不保證追逐低於座標不確定度的目標方位。超過門檻才採目標投影。最後無論哪一分支都通過既有 `PlanetFrame.ProjectOnTangent`，再次正交化及正規化；不能讓 fallback 分支繼續呼叫舊的單次消去實作。

這個 tau 是保守的方向選擇尺度，並非碰撞容差或移動距離；只影響朝向，不改 hit volume、射程、步進、RNG 或座標。它刻意可能放棄部分仍可表示的微小方位，避免在量化噪音內抖動；沒有新加 hysteresis 或跨 tick 狀態。

## 精確 code sketch

替換 Encounter.cs:727 的方法；保留其他 Tangent／steering 呼叫的既有行為。無新公開 API，輸入延續內部契約：from／to 在合法 combat shell、fallback 有限，from 不在球心。

```csharp
private float3 TangentToward(float3 from, float3 to, float3 fallback)
{
    // Promote before subtraction: float world positions have already lost sub-ULP bearings.
    double3 offset = (double3)from - (double3)_planet.Center;
    double radius = math.length(offset);
    double3 up = offset / radius;
    double3 delta = (double3)to - (double3)from;
    double3 tangent = math.cross(math.cross(up, delta), up);
    double length = math.length(tangent);

    const double FloatUnitRoundoff = 5.9604644775390625e-8; // 2^-24
    double scale = math.max(1.0, math.max(radius,
        math.max(math.cmax(math.abs((double3)from)),
        math.max(math.cmax(math.abs((double3)to)),
                 math.cmax(math.abs((double3)_planet.Center))))));
    double uncertainty = 16.0 * FloatUnitRoundoff * scale
        * (1.0 + math.length(delta) / radius);

    float3 candidate = length > uncertainty
        ? (float3)(tangent / length)
        : fallback;

    // Also repairs the carried heading at this position; zero/radial fallback uses North.
    return PlanetFrame.FromUp((float3)up).ProjectOnTangent(candidate);
}
```

`PlanetFrame.FromUp`（Planet/Api/PlanetFrame.cs:81）會重新正規化 up；`ProjectOnTangent`（:130）已有零向量／近徑向 fallback 與第二次 Gram–Schmidt。不要複製一份新的正交化 helper。其內部 1e-3 rad 退化測試用於已選定的單位 candidate：目標分支此時已切向，不會再以「與目標的高度差」錯誤淘汰它。

## R=640..1280 m 的精度理由

- 球心在原點、距地面約 1 m 時，float 的表面座標 spacing 約為 0.061–0.122 mm（跨 1024 binade）；tau 約 0.61–1.22 mm。原見證約 4.36 μm 的殘差及 0.1 mm 鄰近偏移皆落在保留 heading 的區域；6 m 正常目標不受此退化判定影響。
- 若 center 分量達合法上限 16000 m，座標 spacing 約 1–2 mm，tau 隨世界座標放大至約 16–17 mm（近鄰目標），而不是錯用只有 R 的門檻。位移減法的每軸座標捨入誤差為 O(uS)；徑向方向不確定度約 O(uS/|r|)，投影時再乘 |d|，因此 tau 含 `S*(1+|d|/|r|)`。係數 16 為逐軸誤差、世界點建構與尺度估計留保守餘裕，不是聲稱所有門檻以下方向都不可表示。
- double 的單位捨入為 2^-53；被採用的非零 q 至少為上述 float 尺度門檻。在本 shell 尺度，double 投影相對誤差約 O(2^-53*|d|/tau)，遠小於 float 輸出捨入，避免原本 O(u*|d|/|q|) 的放大。
- 轉回 float 的單位 candidate，以及 frame 的 float up，只有 O(u) 的方向誤差；既有 ProjectOnTangent 再正交化。它對 frame.Up 的公開保證為 dot<=1e-6；frame.Up 與 double 真 up 的差亦為幾個 u，合計仍是數個 1e-6 的尺度，遠低於幾何的 1e-3。此保證來自最後的向量修復，並不依賴 tau 恰好辨識每個退化點。
- 浮點結果仍可能因後端指令不同而不逐位元相同；此提案承諾幾何容差內的方向，不新增跨 Mono／IL2CPP bitwise 宣稱，也不以 epsilon 改寫命中邊界。

## 已執行的 repo 外驗證

Scratch 與執行環境見 [core-contracts-sol.md](core-contracts-sol.md)。Geometry.cs 實作與上述 sketch 相同（將 `_planet` 換成參數），原 TangentToward 由正式 Encounter 私有方法反射呼叫；不是用重寫的舊算法當比較基準。

固定 System.Random(559)：R=640／960／1280，center=(0,0,0)／(16000,-16000,8000)，側向距離=0／0.01 mm／0.1 mm／1 mm／1 cm／0.1 m／1 m／6 m，高度差=-1／0／1／10 m，每組500方向；起點海拔0.6 m。每個幾何樣本另測四個 fallback：原 north、零、徑向、任意有限單位向量。

每種編譯共 **384,000 組**新 forward，逐組檢查 finite、unit、true-up dot 與正式 Overlaps；另24個精確極軸／重合／徑向案例。Editor 與剝除版各 exit 0：

```text
oldInvalid = 14161 / 96000 個幾何樣本（非遊玩發生率）
proposedMaxDot = 1.1844164010677005e-7
maxLengthError = 2.7908205191273794e-7
bearingMismatch = 0（側向 >=0.1 m，與獨立 double 減法投影 oracle 內積 >=0.99999）
```

這些是 CLR 9.0.8 的數值見證；未在 Unity 執行提案、未修改正式幾何、未驗 Mono／IL2CPP 或效能。Part A 的 Unity 948/948 是現行 branch 的 sanity，不能當作提案已通過 Unity 測試。

## OPUS 實作後的驗收

1. 將原 R5-07 固定座標及本輪另一見證 `from=(392.2076,506.4875,3.457169)`、`to=(391.59537,505.69684,3.4517725)` 加入獨立 EditMode 幾何測試。驗 true-up dot／unit／finite；原方法須確實失敗。不要把「沒有 ContractException」當作 Release 的 oracle。
2. 加入可精確控制投影距離的 tau 下方／等號／上方案例，驗 `<=` 保留 fallback，`>` 指向可辨識目標；涵蓋 coincident、正上／下、極區、零／徑向 fallback、接近正負軸的方向與非零球心。低於 tau 不斷言精確目標 bearing；高於 tau 用獨立 oracle 驗投影方向。6 m 與一般戰鬥距離應保持合法 heading，R=640／1280 都測。
3. 在真實 Encounter.Tick 放置相同方向、不同身體半徑／高度的合法雙方單位，涵蓋 attack 與 fallback 朝向，跑首 tick／後續 tick。驗 Forward 在當前 Position 切向及正式 MoveGeometry 消費成功；避免以退開6 m隔離掉要驗的缺陷。
4. 契約啟用及剝除的 Player／CLR 都驗幾何不變式；Unity EditMode 跑完整現有案例（本輪基準948），PlayMode 跑既有 RunSession 回歸，再做 Mono／IL2CPP 相同輸入的容差比較。不要要求與舊的非法 forward 逐位元一致。
5. 測有敵人時的熱路徑成本及暖機後配置；這個改法無 managed 配置，但每次轉向會多 double cross／sqrt 與 frame 投影，不能以數值正確代替效能驗收。

提案只處理 D0-E4 的朝向；共用 Tangent 的其他呼叫點、AI 極小距離保證或碰撞語義若需調整，另立範圍，避免這項數值修正擴成無關重構。
