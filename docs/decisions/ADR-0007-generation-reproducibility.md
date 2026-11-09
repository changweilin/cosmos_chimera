# ADR-0007：生成可重現性 — 同後端逐位元、跨後端幾何容差（Q-T6 (b)）

## 狀態
已採納（2026-11-09，使用者定案 Q-T6 選 (b)）。

## 背景
- 連線時各客戶端自行生成星球（CLAUDE.md 硬性規則），必須對「同 seed + 同 `GeneratorVersion`」得到相同內容。
- `PlanetHeight` 使用 float、sqrt 與 simplex 噪聲。不同後端（Mono／IL2CPP／Burst、x64／ARM64）的末位可能不同；Sol 的 CLR 探針與 Unity 快照雜湊已出現差異（P1 審查 r2 B1 起）。
- P3-10 探針實測（2026-11-09，同一台 i7-14700，6 星球類型 × 5 seed）：
  - Editor 與 Mono 玩家逐位元相同。
  - IL2CPP 對 Mono 的高度有 95% 樣本位元不同，最大差 2.2 mm；61,440 個樣本中有 116 個超過 1 mm。
  - 區域、生成、任務等離散內容 30/30 相同。
  - `RunPlanner.Hash` 有 3/30 不同，原因只有方向在 1e-4 量化下翻轉。
- 第二台機器的實測尚未完成。

## 決定
1. **同後端、同建置：逐位元一致。** 快照測試（`Tests/Snapshots`、`RunPlannerTests` 等）以 Unity Editor（Mono）為準，維持逐位元比對。在其他執行環境（例：CLR runner）得到不同的高度雜湊屬於預期，不算回歸。
2. **跨後端／跨機器：幾何容差 ≤ 3 mm。** 地形高度（base 與模板合成）在 Windows x64 的 Mono 與 IL2CPP 之間，差距不得超過 3 mm。實測最大差為 2.2 mm，因此原本提議的 1 mm 不夠。
3. **內容決策不得讀浮點高度門檻。** 區域、POI、生成、BOSS、任務等內容，一律由整數 seed 與離散表推導（`RunPlanner` 的離散內容）。新增依高度做決策的生成步驟，需先改用離散化的權威資料，或在本 ADR 補充例外。
4. **連線握手比對離散內容雜湊**（探針的 `planDiscrete`：鏈、降落區、BOSS、區域主題／詞綴／層級／鄰接、生成的區域／群組／物種／角色、任務步驟），不比對高度，也不比對含量化方向的 `RunPlanner.Hash`。
5. **P3-10 驗收條件**：兩台 Windows x64 機器以 `python tools/repro/compare.py a.txt b.txt --policy b` 判定 PASS。PASS 的條件是：`seaCount` 與 `planDiscrete` 完全相同、兩份 `.samples` 都存在，而且 base／surface 高度的最大差都 ≤ 3 mm。

## 後果
- 不需重寫噪聲或改用定點數；Burst 網格維持 `FloatMode.Default`（ADR-0006）。
- 角色、物理與表現層在不同客戶端可能相差幾毫米。伺服器權威模擬（P6）要以伺服器的高度為準，不能假設客戶端的地面高度完全相同。
- **已知風險**：生成點選陸地（`TemplateView.IsSea(direction)`）與 `Scatter` 會讀浮點方向。方向落在格界上時，不同後端可能翻轉結果。實測 30/30 沒有發生，但沒有證明不會發生。P6 握手比對 `planDiscrete` 時，會偵測到這種不一致，到時改成以主機的計畫為準下發。追蹤在 tech-debt 的 Q-T6-R1。
- 探針沒有摘要 `LandingDirection`（tech-debt S6）。降落點屬於幾何，只要在容差內即可，不列入握手。

## 替代方案
- **(a) 逐位元一致**（定點數或自訂決定性數學）：所有依高度的決策都會變安全，但要重寫噪聲與高度管線，成本高。實測顯示離散內容已經一致，因此不採用。
- **只靠 1 mm 容差**：實測 IL2CPP 的最大差 2.2 mm 會誤判失敗，因此不採用。
