# P2-01／P2-03 驗收 · SOL

2026-11-06 · Codex（SOL）。分支 p2/data-model-damage，基線 2539d74a99f6a8a5463cf58687af2519bc1d0aaa 加本次工作樹變更。

**P2-01／P2-03 驗收通過。** 本輪已實作修正，並依使用者確認的 Q-C14～Q-C16、七種觸發／七種效果與傷害契約完成資料遷移。使用者指定最後複審與驗收由本對話完成；本次未執行 AGY 或其他廠商模型的複審，不將同一模型的複審描述為跨廠商驗證。

## 缺口關閉

| 項目 | 修正與證據 |
|---|---|
| R1 · schema／Int32／傷害 API 表示域不同 | schema 的非負／正整數加上 Int32 上限；power 上限 100000、mastery 上限 1000，與 DamageModel 一致。跨層測試檢查 schema 引用／上限，匯入最大值再呼叫實際 Compute；Python 測上限接受與上限＋1 拒絕。 |
| H1 · 巨整數拋 OverflowException | importer 改為具型別檢查與安全 TryParse；公開入口測試確認錯誤彙整成 DataImportException 且不寫資產。 |
| H2 · always.params=null 被接受 | optional params 改為 Required.DisallowNull；省略／空物件仍可用。412 組比對全數一致，零未彙整例外。 |

傷害域上限是表示契約，不是平衡目標。P2-09 必須保證修正後有效專精仍符合 0..1000；本輪沒有自行實作特性疊加。半向上取整與非免疫命中最低 1 點已由使用者確認，公式與實作吻合。

## 已確認並實作的資料契約

- 九種 shape：front／sweep／burst／line／cone／arc／self／ally／field。保留既有 Line=0、Cone=1；退役的 2／3 序列化值不重用。
- burst 必填 origin=caster|target，其餘 shape 不得填。movement 可省略，預設 none；dash 只搭配 line，base_range_mm 必須 >0。
- 每招必填非負 Int32 的 base_range_mm。初稿逐招寫入 melee=2000、mid=12000、long=24000 mm，不在執行期查 range_class 表。burst 表示半徑，dash 表示路徑長度；目標選擇與施法距離判定留給 P2-04。
- size_class 必填 S／M／L／XL，樣本狐狸為 S／M／L。超載仍待 Q-C12／P2-05，未擴充 stage。
- 150 招：line→front 10、cone→sweep 13、中遠距 line/cone 保留 52、aoe→burst 37、dash→line＋movement=dash 38。逐筆與基線比對，ID 與原有非 shape 欄位均保留。

契約變更是破壞性變更：舊 JSON 必須遷移並重新匯入資產；本輪同步更新合法／非法 fixtures、150 招初稿、importer、型別、測試及設計文件。新增 shape 的實際戰鬥處理器屬 P2-04，沒有在本輪宣稱已完成。

## 本機證據

- Unity 6000.3.28f1／Windows x64：最終交付版本完整 EditMode **247/247**（無暫存探針）；此前含 3 個暫存探針的驗證為 250/250。新回歸涵蓋九種 shape、四種體型、非法組合、Origin 前置條件、最大值跨層消費，以及四批 150 招實際匯入後逐筆比對。
- schema／importer 語料 **412/412** 相符，包括 96 組合法資料；零接受差異、零未彙整例外。語料測的是實際 ReadKind／CheckRule；另以公開 Import 驗證巨整數與 null 的代表案例。
- 傷害獨立 decimal oracle **100455** 組通過：455 組端點笛卡兒積（含 int.MinValue／int.MaxValue 抗性）加 seed=20261106 的 100000 組隨機輸入。DamageModel 計算邏輯未改。
- Python 資料驗證測試 **15/15**、平衡不變式 **11/11**。正式 Data、合法樣本、四批招式與三批特性均 **0 errors／0 warnings**。
- 本輪沒有執行 PlayMode、IL2CPP 或其他平台。暫存探針與 Unity 自動遷移的渲染資產不納入交付。

本機證據在 Logs：p2-final-editmode.xml（最終 247/247）、p2-acceptance-editmode.xml、p2-review-probes.xml、p2-review/differential.txt、p2-review/public-import.txt、p2-review/typed-rules.txt；重跑語料與探針來源亦保留於 Logs/p2-review/，未納入版控。

## 最後複審

SOL 逐項複審 schema／DTO 的必填與 nullable 規則、九種 enum 映射及退役值、burst／dash 約束、Int32 與傷害表示域、TryParse 錯誤彙整、寫入前驗證順序、Set 呼叫端及序列化欄位，未發現本次範圍內的阻擋問題。資料遷移與新 public accessor 的正常／違約路徑由回歸測試覆蓋。

先前 Claude CLI OAuth 過期，AGY 呼叫也因未授權原始碼外傳被自動核准審查拒絕，均未產生外部模型的審查結果。使用者隨後指定「此對話完成」，本項依此指示在本對話收尾；專案的通用分工規則未修改。DamageModel 已過時的提案註解已更新，計算邏輯未改。
