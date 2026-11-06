# 架構總覽

## 目錄
```
Assets/_Project/
├── Core/                    # 無遊戲語意的基礎：Contracts, Random(Pcg32), Math, Events
├── GameData/                # 匯入的定義型別（MoveDef/CreatureDef…）+ GameDatabase registry + Editor importer（ADR-0004）
├── Features/
│   ├── Planet/              # cube-sphere、區塊、重力、PlanetFrame
│   ├── WorldGen/            # 骨架/生物群系/區域圖/POI/地城（純 C# + Jobs）
│   ├── Player/              # 角色控制、攝影機
│   ├── Creatures/           # 宙獸模型、進化、隊伍、副手規則
│   ├── Combat/              # 傷害、CD、命中、招式執行、AI 行為
│   ├── Capture/             # 收服、蛋、孵化
│   ├── Lobby/               # 地球大廳、星海選星
│   ├── Codex/               # 圖鑑/招式表/特性表/關係表 UI
│   ├── Net/                 # 連線、同步、房間
│   └── Save/                # 存檔與版本遷移
├── Data/                    # importer 產出的 ScriptableObject（勿手改）
└── Bootstrap/               # 組合根場景與 DI 接線
Data/                        # (repo 根) JSON 真實來源 + schemas
tools/                       # 資料驗證、匯入、AI prompt 模板
```

## 每個 Feature 內部
```
<Feature>/
├── Api/          # 對外公開：介面、DTO、事件（DbC 契約必填）
├── Internal/     # 實作（其他 feature 不可引用）
├── Runtime/Model # 純 C# 規則層
├── Tests/EditMode, Tests/PlayMode
└── <Feature>.asmdef
```

## 依賴方向（不可反向）
```
Bootstrap → Lobby/Codex → Combat/Capture/Creatures → WorldGen → Planet → Core
                 Net ──▶ (Combat, Creatures, Planet 的 Api)
                 Save ─▶ (Creatures, Capture 的 Api)
```
`Core` 不引用任何 feature；`Planet` 不知道宙獸存在。
`GameData` 只引用 Core；除了 Planet、Player 以外的 feature 都可以引用它（ADR-0004）。

## 資料流
```
Data/*.json ──tools/validate-data──▶ importer(Editor) ──▶ Assets/_Project/Data/*.asset ──▶ Runtime registries (by id)
```
Importer：選單 `Cosmimera/Data/Import All`，或 batchmode `-executeMethod Cosmimera.GameData.Editor.DataImporter.ImportAllBatch [-dataRoot DIR]`。registry：`GameDatabase.TryGet<T>(id)`、`GetRelations(creatureId)`。
