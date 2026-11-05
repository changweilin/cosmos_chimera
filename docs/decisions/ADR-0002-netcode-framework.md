# ADR-0002 網路框架

**狀態**：提議中（於 P6-01 定案；負責：Claude Opus）

## 背景
需要 LAN listen server + 玩家自建 dedicated server，1–4 人，不依賴官方雲端。主機權威，角色移動需客戶端預測。

## 候選
| 選項 | 優點 | 風險 |
|---|---|---|
| Netcode for GameObjects + Unity Transport | 官方、文件多、與 Unity 6 同步更新 | 客戶端預測需自寫 |
| FishNet | 內建預測/回滾、效能佳 | 第三方、單一維護者 |
| Mirror | 社群大、成熟 | 架構較舊 |

## 傾向
NGO：人數少（≤4）、PvE 為主，預測需求有限；先以 P6 原型驗證對抗模式的延遲手感再定案。

## 決定
（待定）
