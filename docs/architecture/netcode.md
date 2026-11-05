# 網路技術

框架選擇待 ADR-0002 定案（P6-01）。以下原則與框架無關。

## 拓撲
- Listen server（LAN 主機兼玩家）與 headless dedicated server 共用同一套程式；以啟動參數切換。
- 發現：LAN UDP 廣播；自建伺服器：直連 IP:port。

## 權威劃分
| 由主機計算 | 由客戶端本地計算 |
|---|---|
| 傷害、HP、CD、收服判定、掉落 | 地形生成與 LOD、植被、特效 |
| 宙獸 AI 決策 | 自己角色的移動預測（主機校正） |
| 任務/BOSS 狀態 | UI |

## 握手
`{protocol_version, generator_version, data_hash, planet_seed, planet_type}` — 任一版本/雜湊不符即拒絕，避免兩端星球或數值不同步。

## 規則層要求
戰鬥規則是純 C#（見 overview.md），主機與單機走同一套程式碼；單機 = 本地主機，不另寫一份邏輯。
