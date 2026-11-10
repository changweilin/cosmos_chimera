# Q-C35 各物種性格提案

**未定案；不套用資料。** `proposal.json` 的 current 是現行草稿，proposed 是本提案。距離是野生主動開戰半徑，不代表招式射程；timid 仍會在受擊或被集火時開戰。

## 分佈

| 性格 | 半徑 | 數量 |
|---|---:|---:|
| timid | 0 m | 8 |
| cautious | 4 m | 7 |
| normal | 8 m | 4 |
| territorial | 14 m | 8 |
| fierce | 20 m | 2 |
| apex | 28 m | 1 |

## 進化線梯度

依幼 → 少 → 成排列；同線均不降階。理由逐隻見 JSON。

| 線 | 幼 | 少 | 成 |
|---|---|---|---|
| 日 | timid | cautious | territorial |
| 月 | cautious | normal | fierce |
| 星 | timid | cautious | territorial |
| 金 | timid | cautious | territorial |
| 木 | timid | cautious | territorial |
| 水 | timid | normal | territorial |
| 火 | cautious | territorial | apex |
| 土 | timid | normal | territorial |
| 冰 | timid | territorial | fierce |
| 風 | timid | cautious | normal |

## 御三家與 BOSS 候選

日雛鳥追日、星馬駒跌撞，採 timid 讓玩家有時間認識目標與主動集火；月幼狼已有繞背揮爪的獵手形象，採 cautious，保留近身風險。成年日鷺的鏡陣與星天馬的雷鳴衝鋒採 territorial；成年月巨狼則以 fierce 突顯孤行掠食。屬性及遠程攻擊能力不是直接決定侵略性的證據。

火成年三面霸獅的咆哮引動砂岩爆震，選為唯一 apex 與 BOSS 候選；冰成年裝甲獵獸及月成年巨狼可作次級 BOSS 候選，但保持 fierce。這是物種性格，並非新增 BOSS 專屬覆寫：同物種普通個體也會使用相同半徑，實際生成位置與密度須一併審查。

## D0 難度取捨

較多 0–4 m 物種提供繞行與自主開戰空間，但 timid 不會靠接近觸發，需向玩家教會集火或攻擊；冰、火少年及成年掠食者仍提供早期路線的危險訊號。14–28 m 易與鄰近個體領域重疊，且脫戰距離固定 40 m，可能連鎖開戰；玩家倒下即失敗，須以真實生成密度、視野及操作驗證，而非只看單隻半徑。現有 P5-07 模擬中月、星對雙普通怪勝率均為 0/224，並非本提案的實測結果；應優先試玩御三家、混合群與 BOSS 出生點，必要時調整配置或提案，不能據此宣稱已平衡。
