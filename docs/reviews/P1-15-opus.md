# P1-15 遠景 LOD／impostor — OPUS 審查

2026-11-16 · Claude Opus 5.5（OPUS）· 基準 `95a52ef`，受審變更 `18a005d` 中的遠景部分：`FarScenery`、`FarSceneryMeshes`、`ScatterField.TryGetFarItem`／`ItemGone`、`ToonScenery` `_FarFade`、`RunSession` 接線與相關測試。同一 commit 內的地質變體（P3-24 a）不在本次範圍。未修改程式。

**結論：pass with debt；high 0 / med 0 / low 3。** 截圖之外的實景目視與 Steam Deck 幀率仍待人工驗收（README「現在」第 1 條）。

## 核對結果
- 規則層不受影響：遠景只讀 `ScatterPlanner` 已放置的物件，不擋路、不可被擊中，也不屬於重現性契約。
- 被破壞（`_isDestroyed`）或被喚醒（`_detached`）的物件不會出現在遠景；`ItemGone` 的 key → tile id 換算和 `BuildTile` 的格子範圍一致（已逐步核對），林下層的 key 會被排除。
- 串流：每移動 10 m 才重新篩選 tile（6 × tiles² 個中心點的弦長比較，R=1280 時約 3,750 個，可忽略）；每幀最多建 2 塊，近的先建；清單與 buffer 都重複使用，`Update` 不配置記憶體（新建 tile 的 GameObject 除外）。
- 淡入／淡出：近景範圍內縮回地面，84–96 m 淡入，外緣 40 m 淡出；`radiusM = min(260, 晴天 fog end × 0.85)`，在天氣上限套用之前設定，符合 `Configure` 的 Pre。近景的散佈物件沒有設定 `_FarFade`（y = 0），不會被縮放。

## 發現

| ID | 嚴重度 | 位置 | 問題 | 建議 |
|---|---|---|---|---|
| P1-15-O1 | low | `Art/Shaders/ToonScenery.shader` `_FarFade` 分支 | 淡入用錨點到焦點的 **3D 距離**，但近景範圍是依**地面**格子（≥ 90 m）。地勢落差大時（例：比玩家高 50 m 的山坡，地面距離約 80 m，3D 距離約 95 m），遠景輪廓已接近原尺寸，近景的真物件也還在，兩者會疊在一起。 | 人工試玩時在山地／峽谷觀察；如果看得出來，改用錨點與焦點在球面上的弧長（兩者都已知方向與 R）。 |
| P1-15-O2 | low | `RunSession` 遠景接線 | 遠景半徑依晴天 fog 決定；天氣把視野縮到 40–120 m 時，90 m 外的 tile 全在霧裡，還是照常繪製。 | 只影響效能；量 Steam Deck 幀率時一併看，必要時依目前視野暫停繪製外圈 tile。 |
| P1-15-O3 | low | commit `18a005d` | 同一個 commit 混了 P1-15 與 P3-24 a，訊息沒有測試數字和未完成事項，不符合 `AGENTS.md` §3「commit message 就是交接」。 | 不追溯修改；之後的 commit 照 §3 寫。本次完整測試數字見下方。 |

## 驗證
與 P3-12／P3-04a／P3-04b 同批的完整測試：`Logs/opus-review-editmode.xml`、`Logs/opus-review-playmode.xml`。
- Unity 6000.3.28f1 batchmode，基準 `95a52ef`：EditMode **3555/3555 通過**；PlayMode **147 通過、0 失敗、8 略過**（略過的都是需要 `COSMIMERA_CAPTURE=1` 的截圖測試，未當作目視驗收）；無 C# 編譯警告或錯誤。
