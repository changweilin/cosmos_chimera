# P0 基礎建設

**出口條件**：Unity 專案可開啟；EditMode 測試在 CLI 可跑；資料 schema 與驗證工具可用；所有規範文件就位。

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P0-01 | 文件骨架（CLAUDE.md、AGENTS.md、rules、design、roadmap） | OPUS | 人工 | 本檔存在且使用者確認 | 🟨 |
| P0-02 | 建立 Unity 6 LTS + URP 專案（Windows x64 建置目標）、Input System（手把優先）、`git init`、`.gitignore`/`.gitattributes`（LFS）、鎖定版本；URP 品質分 `PC` 與 `SteamDeck` 兩組設定 | SONNET | OPUS | 專案可開；CLAUDE.md 指令已填入真實路徑；兩組品質設定存在 | 🟨 |
| P0-03 | `.editorconfig` + Roslyn analyzers、asmdef 骨架（依 overview.md） | SONNET | OPUS | 編譯 0 警告；跨 feature 引用 `Internal/` 會編譯失敗 | ⬜ |
| P0-04 | `Core/Contracts`（`Contract.Requires/Ensures`，Release 剝除）、`Core/Random/Pcg32` | OPUS | SOL | 單元測試覆蓋；Pcg32 對照官方測試向量 | ⬜ |
| P0-05 | `Data/schemas/*.schema.json`（creature, move, trait, relation, planet-type）+ `tools/validate-data` | SONNET | OPUS | 範例資料通過，錯誤資料給出可讀錯誤訊息 | ⬜ |
| P0-06 | JSON → ScriptableObject importer（Editor） | SONNET | OPUS | 改 JSON 後重新匯入，registry 可依 id 查詢 | ⬜ |
| P0-07 | CI（GitHub Actions + GameCI）跑 EditMode 測試與資料驗證 | SONNET | — | PR 上自動跑；失敗會擋 | ⬜ |

## 交接紀錄
- 2026-11-06 · Claude Opus · done · P0-01 文件骨架完成 · 下一步：使用者審閱 + 回答 open-questions
- 2026-11-06 · Claude Opus · done · Q-C1～C8、Q-T4 定案並寫入設計文件；新增 Q-C9～C11 · 下一步：SONNET 開工 P0-02（已不受阻）
- 2026-11-06 · Claude Opus · done · P0-02：Unity 6000.3.28f1 + URP 17.3 + Input System 1.20（僅新版輸入）+ Cinemachine 3.1.7；IL2CPP / Win64；品質等級 SteamDeck（陰影 60 m、2 cascades、無 MSAA）與 PC（150 m、4 cascades、4x MSAA），Standalone 預設 PC；範本內容移到 `Assets/_Project/`；設定可用 `ProjectBaseline.Apply` 重新套用；git + LFS + UnityYAMLMerge（本機 git config）。**未做**：執行期自動偵測 Steam Deck 並切換品質等級（P1 處理）；`companyName` 仍是 DefaultCompany（待使用者提供）；尚未 commit · 下一步：使用者審閱 → P0-03
