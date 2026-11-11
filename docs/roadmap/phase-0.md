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
| P0-07 | CI（GitHub Actions + GameCI）跑 EditMode 測試與資料驗證 | SONNET | — | PR 上自動跑；失敗會擋 | 🟨 |

## 交接紀錄
見 [log/phase-0.md](log/phase-0.md)（只追加）。
