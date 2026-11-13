---
paths:
  - "Assets/**/Tests/**"
  - "Assets/**/*.cs"
---
# 測試規則

- 每個 feature：`Tests/EditMode/`（純規則，必須）與 `Tests/PlayMode/`（需要場景/物理時）。
- 規則層每個 public 方法至少測：一個正常案例、每條 `Pre:` 的違反案例（期望 `ContractException`）、每條 `Post:` 的邊界。
- 程序生成：**快照測試** — 固定 seed 產出的摘要雜湊寫入 `Tests/Snapshots/`；`GeneratorVersion` 變更時才可更新快照，且需在 commit message 說明。
- 數值平衡不寫成斷言精確值的測試；改測不變式（例：傷害 >= 0、CD 不低於下限）。
- 不 mock Unity 物理；需要物理的測試放 PlayMode。
