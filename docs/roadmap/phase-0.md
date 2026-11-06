# P0 基礎建設

**出口條件**：Unity 專案可開啟；EditMode 測試在 CLI 可跑；資料 schema 與驗證工具可用；所有規範文件就位。

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P0-01 | 文件骨架（CLAUDE.md、AGENTS.md、rules、design、roadmap） | OPUS | 人工 | 本檔存在且使用者確認 | 🟨 |
| P0-02 | 建立 Unity 6 LTS + URP 專案（Windows x64 建置目標）、Input System（手把優先）、`git init`、`.gitignore`/`.gitattributes`（LFS）、鎖定版本；URP 品質分 `PC` 與 `SteamDeck` 兩組設定 | SONNET | OPUS | 專案可開；CLAUDE.md 指令已填入真實路徑；兩組品質設定存在 | 🟨 |
| P0-03 | `.editorconfig` + Roslyn analyzers、asmdef 骨架（依 overview.md） | SONNET | OPUS | 編譯 0 警告；跨 feature 引用 `Internal/` 會編譯失敗 | 🟨 |
| P0-04 | `Core/Contracts`（`Contract.Requires/Ensures`，Release 剝除）、`Core/Random/Pcg32` | OPUS | SOL | 單元測試覆蓋；Pcg32 對照官方測試向量 | 🟨 |
| P0-05 | `Data/schemas/*.schema.json`（creature, move, trait, relation, planet-type）+ `tools/validate-data` | SONNET | OPUS | 範例資料通過，錯誤資料給出可讀錯誤訊息 | 🟨 |
| P0-06 | JSON → ScriptableObject importer（Editor） | SONNET | OPUS | 改 JSON 後重新匯入，registry 可依 id 查詢 | 🟨 |
| P0-07 | CI（GitHub Actions + GameCI）跑 EditMode 測試與資料驗證 | SONNET | — | PR 上自動跑；失敗會擋 | ⬜ |

## 交接紀錄
- 2026-11-06 · Claude Opus · done · P0-01 文件骨架完成 · 下一步：使用者審閱 + 回答 open-questions
- 2026-11-06 · Claude Opus · done · Q-C1～C8、Q-T4 定案並寫入設計文件；新增 Q-C9～C11 · 下一步：SONNET 開工 P0-02（已不受阻）
- 2026-11-06 · Claude Opus · done · P0-02：Unity 6000.3.28f1 + URP 17.3 + Input System 1.20（僅新版輸入）+ Cinemachine 3.1.7；IL2CPP / Win64；品質等級 SteamDeck（陰影 60 m、2 cascades、無 MSAA）與 PC（150 m、4 cascades、4x MSAA），Standalone 預設 PC；範本內容移到 `Assets/_Project/`；設定可用 `ProjectBaseline.Apply` 重新套用；git + LFS + UnityYAMLMerge（本機 git config）。**未做**：執行期自動偵測 Steam Deck 並切換品質等級（P1 處理）；`companyName` 仍是 DefaultCompany（待使用者提供）；尚未 commit · 下一步：使用者審閱 → P0-03
- 2026-11-06 · Claude Opus · done · P0-04：`Cosmimera.Core` asmdef（noEngineReferences）+ `Contract.Requires/Ensures`（`[Conditional(UNITY_EDITOR, DEVELOPMENT_BUILD)]`，Release 連同引數求值一起剝除）+ `Pcg32`（PCG-XSH-RR 64/32 struct；NextUInt/有界/NextInt/NextFloat 24-bit）；EditMode 20/20 通過，含官方 pcg32-demo(42,54) 輸出與擲硬幣向量。先建了 Core 的 asmdef，P0-03 其餘 asmdef 骨架照舊。Pcg32 是可變 struct（為 Burst），複製會分岔序列，已寫在契約與測試 · 下一步：SOL 審查 P0-04
- 2026-11-06 · Gemini Flash（agy，Claude Opus 驗收）· partial · P0-05 範例 fixture 初稿：`Data/_incoming/p0-05-samples/`（valid：6 招式 + 1 條三段進化線 + 2 條關係邊；invalid：8 個單一缺陷檔，已用腳本確認每檔只有預期缺陷）；README 內有 2 個 schema 細節問題（頂層陣列 vs 物件、素材屬性限制）留給 schema 作者 · 下一步：SONNET 寫 schema + validate-data，以這組 fixture 作驗收
- 2026-11-06 · Claude Opus · done · P0-05：`Data/schemas/` 5 個 schema + `common.schema.json`（Draft 2020-12）；`tools/validate-data`（Python + jsonschema，見 `tools/requirements.txt`）做 schema + 跨檔檢查（全域 id 重複、retired id、懸空引用、進化鏈同線且階段遞進、邊 a<b/自環/重複、浮點字面值、重複 key）；素材屬性≠專精只 warning（Q-C9）。使用者定案：trait/effect 只定 `{type, params}` 殼（P2 收斂 enum）、edge kind 封閉 enum、planet-type 不含數值。fixture 改為與 `Data/` 同結構並補 traits/planet-types 範例；`tools/tests` 13/13 通過。發現：`evolves_to_on_adult.json` 實際有 3 個缺陷（另含重複 id 與懸空 evolves_to）；combat.md 用 `move.base_range`，schema 依 creatures.md 用 `range_class`，P2 需統一 · 下一步：OPUS 以外的審查者（SONNET）審 P0-05 → P0-06 importer
- 2026-11-06 · ChatGPT Sol（Codex CLI，唯讀）· done · P0-04 審查**不通過**：PCG 公式與官方向量正確，但 (1) 建構子 seed 加法與 `ulong→uint` 截斷沒有明示 `unchecked`，在 `/checked+` 編譯下會拋例外；(2) `default(Pcg32)` 呼叫 `NextUInt(3)` 會無限迴圈；(3) 把拒絕取樣改成 `% bound`，現有測試仍全過；另有可變 struct 防呆、Burst 相容性、DbC 文件缺口等項。全文：`docs/reviews/P0-04-sol.md` · 下一步：OPUS 修正 1–3 並補測試，4–6 評估後交使用者決定
- 2026-11-06 · Claude Opus · done · P0-03：10 個 feature asmdef + Bootstrap（依 overview.md 依賴圖，只用名稱引用）；每個 feature 的 `AssemblyInfo.cs` 只對自家測試開 `InternalsVisibleTo`；`Microsoft.Unity.Analyzers` 1.28.0 + `Assets/Default.ruleset`（ADR-0003）；`.editorconfig`；`Tests/EditMode/ArchitectureTests`（依賴方向白名單、Internal 命名空間不可有 public 型別）。驗證：EditMode 23/23、0 個警告。反向探針（驗證後已刪除）：跨 feature 引用 internal 型別 → CS0122；空的 `Update` → UNT0001 warning；public 的 Internal 型別 → 守門測試失敗。**注意**：Unity 在 batchmode 會把 `PC_RPAsset.asset` 的 `k_AssetPreviousVersion` 12 改成 13（URP 資產遷移），這次已還原，未納入本項 · 下一步：SONNET 審 P0-03 → P0-06
- 2026-11-06 · Claude Opus · done · P0-04 修正 Sol 審查的 #1–#3：seed 加法與 32 位截斷改為明示 `unchecked`；`NextUInt()` 加上 `Contract.Requires(increment 為奇數)`，所以 default 實例在 Editor/Dev 會丟 `ContractException`，不會無限迴圈（Release 仍是未定義行為，已寫進契約）；補拒絕取樣向量、極端 seed 向量、未初始化與逆序區間測試。驗證：EditMode 28/28；`/checked+` 編譯探針：修正前拋 `OverflowException`、修正後輸出正確；變異測試（有界取樣改成 `% bound`）會被新測試擋下。**未做**：#4 struct 防呆、#5 Burst 編譯驗證、#6 DbC 文件缺口；也沒有把 checked/unchecked 編譯矩陣做成常駐測試 · 下一步：SOL 複審 #1–#3；使用者決定 #4–#6
- 2026-11-06 · Claude Opus · done · P0-06：新增 `Cosmimera.GameData` 組件（`MoveDef`/`TraitDef`/`CreatureDef`/`PlanetTypeDef`/`RuleDef`/關係邊 + `GameDatabase.TryGet<T>(id)`、`GetRelations`，setter 皆為 internal）與 Editor importer `DataImporter`（選單 + batchmode `ImportAllBatch [-dataRoot]`）。兩階段：先解析與檢查所有引用，有錯就在寫入前中止並列出全部錯誤；重新匯入時原地覆寫，GUID 不變；id 消失則刪除資產。決定寫在 ADR-0004（**提議中，待使用者確認**）：GameData 只引用 Core，Planet/Player 不可引用；Newtonsoft.Json 3.2.2 從遞移依賴改為明確釘選（無新下載）。驗證：EditMode 41/41（新增 13 項 importer 測試），0 個警告；對真實 `Data/` 執行 batchmode 匯入 exit 0（目前沒有正式資料，所以 `GameDatabase.asset` 是空的）。URP `PC_RPAsset` 遷移的變動照例已還原 · 下一步：使用者確認 ADR-0004；SONNET 審 P0-06
