# P6 多人

**出口條件**：LAN 與自建 dedicated server 皆可 1–4 人合作完成一顆星球；對抗模式可玩；版本不符時拒絕連線。
讀：`../design/multiplayer.md`、`../architecture/netcode.md`、`../decisions/ADR-0002-netcode-framework.md`
**開工前需定案**：Q-N1、Q-N2

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P6-01 | 網路框架原型比較並定案 ADR-0002 | OPUS | SOL | ADR 狀態改為已接受 | ⬜ |
| P6-02 | 連線層：listen server、headless server build、LAN 探索、直連 | SONNET | OPUS | 兩台 PC LAN 連線成功 | ⬜ |
| P6-03 | 握手：版本/資料雜湊/seed 同步，本地生成 | SONNET | OPUS | 不符時明確錯誤訊息 | ⬜ |
| P6-04 | 合作：角色/宙獸同步、主機權威戰鬥、獎勵分配（Q-N1） | OPUS | SONNET | 100 ms 模擬延遲下可玩 | ⬜ |
| P6-05 | 對抗模式（Q-N2） | SONNET | OPUS + SOL（平衡） | 4 人對戰完整一局 | ⬜ |
| P6-06 | 房間 UI 與大廳整合 | SONNET | 人工 | 從大廳建立/加入房間 | ⬜ |

## 未收尾紀錄
[log/phase-6.md](log/phase-6.md)（partial／blocked 才寫；舊紀錄為封存）。
