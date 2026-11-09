# P2-05 接線對抗式審查 · SOL

2026-11-09 · SOL（ChatGPT Sol）。對象：`308dc767da568c0709567fa9f0f318fb439306d6`。行號均依該 commit；涵蓋 MaterialDrops、RunSession、LobbySave、DemoGame 及新增測試。已讀 AGENTS.md、CLAUDE.md、相關契約／測試／文件規則、roadmap 與分工、creatures.md 的 Q-C32 方案 B 定案、素材提案與存檔 v3 建議、tech-debt.md、P2-12-sol-r5.md。沒有修改程式、資料、tech-debt 或 phase 檔。

**Verdict：pass（D0 gate；0 high／3 med／2 low）。** 核定資料及正常生命週期下，未找到阻礙遊玩的接線錯誤。以下 med／low 交 OPUS 登錄 tech-debt，demo 後處理；這不代表例外處理或測試覆蓋已完整。W-02 會卡住受影響的 run，但前提是手動灌滿或極端長期累積到 Int32 上限，並非目前正常 D0 流程可抵達的阻塞。

| ID | severity | file:line | finding | evidence / failure scenario | suggested fix |
|---|---|---|---|---|---|
| W-01 | med | `Assets/_Project/Bootstrap/Demo/DemoGame.cs:501`、`:504`、`:505`；`Assets/_Project/Features/Save/Api/LobbySave.cs:375` | **進化寫檔失敗後，記憶體與磁碟不同步，沒有回復或可見的失敗處理。** | 合法個體 LV23、light/t1×3、需求 LV23＋同素材×3。先寫入舊檔，再以 `FileStream(path, Open, Read, FileShare.Read)` 鎖住最終檔；目錄及 `.tmp` 仍可寫。依按鈕順序執行 TryEvolve → SetSpecies → Write，Replace 拋 IOException。實測兩種契約設定均為記憶體 `b/0`、磁碟 `a/3`；成功訊息尚未設定。立即退出會回到舊進度，後續其他 Write 卻可能把此次未提示成功的進化存下。磁碟原子替換本身仍保護了完整舊檔，沒有存出「只扣料、沒換型態」的半筆 JSON。 | 在候選存檔上進化並寫入成功後才替換 live save，或捕捉 I/O 失敗並完整復原型態與素材；提供錯誤提示／重試。新增鎖檔測試；正常條件不足的拒絕路徑不必改。 |
| W-02 | med | `Assets/_Project/Bootstrap/Demo/DemoGame.cs:704`、`:723`、`:724`、`:731`；`Assets/_Project/Bootstrap/Demo/RunSession.cs:1001` | **滿倉溢位會中斷整個結算，run 已結束卻無法返回結果頁。** | v3 合法載入 wood／water／wind 的 tier3 各 `2147483647`；翠綠星 BOSS 必掉其中一鍵。End 先設 `_ended=true`，OnRunEnded 先改 XP／新增收服，再由 AddMaterials 拋 InvalidOperationException；寫檔、銷毀 run 及切換 Results 均未執行。原樣抽出的兩個方法在 CLR 重現：`ended=True ui=Running/run=True memoryXp=10 owned=2 diskXp=0`；再次 End 直接返回，仍卡在 Running。AddMaterials 自身保持全有或全無，問題在外層交易。 | 結算前對完整候選存檔做容量預檢，失敗時保留可重試 outcome 並顯示原因；只有提交成功才完成不可重試的生命週期切換。溢位不可默默截斷；庫存上限／超量處置需明定。 |
| W-03 | med | `Assets/_Project/Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs:142`、`:161`、`:167`；`Assets/_Project/Features/Save/Tests/EditMode/LobbySaveMaterialTests.cs:57`；`Assets/_Project/Bootstrap/Demo/RunSession.cs:473` | **新增測試能抓數種指定突變，但不足以保護完整接線。** | 新 PlayMode 案例只有兩個普通＋BOSS，且都透過注入 Faint；精英未測、正式 Tick 分派未測、End 回呼與 DemoGame 入帳／進化寫檔未測。依真實資產順序重建的 seed 1 中兩個普通均不掉落，只有 Water/T3；因此普通成功入帳也沒有整合見證。拿掉正式 `OnFaint(ev.TargetId)`、End 時清空 Materials、拿掉 DemoGame 的 AddMaterials／SetSpecies／Write，新增斷言均沒有對應觀測；精英在接線處誤傳 Normal 也不在案例輸入中。這些是靜態覆蓋判讀，未冒稱執行完整 Unity 突變測試。 | 用可控合成掉率補普通成功、精英、失敗去重及真實 Tick 擊倒；經 End 回呼驗 outcome，再驗勝／敗／放棄入帳與重載。進化至少測成功落盤、各種拒絕不變及 W-01／W-02。另用原生 JsonUtility 測 qty/tier 的小數、數字字串、超出 Int32、null；目前測試不能證明原始 JSON 數值型別嚴格拒絕。 |
| W-04 | low | `Assets/_Project/Features/Save/Api/LobbySave.cs:60`、`:70`、`:198`、`:300` | **公開庫存可接受 tier4，ToJson 卻產出自己拒讀的 v3 檔。** | `new MaterialKey(Element.Light, 4)` 與 qty1 符合既有型別的 Pre（tier≥1）。AddMaterials 接受後 ToJson 正常返回，FromJson 卻拋 InvalidDataException，違反 ToJson 的 round-trip Post；透過 `Materials.Add` 也能繞過銀行入口。契約啟用／剝除版均重現。正常掉落設定限制 1..3，故目前 D0 呼叫端不會產生此值。另測 default stack：Editor 可能在前一鍵已入帳後才因契約失敗，Release 可存入 tier0／qty0；此項是非法型別值的防禦缺口，不當作正常輸入 bug。 | 明載並檢查存檔材料域，至少在寫檔前以不剝除的驗證拒絕非法庫存；若要保證每次公開修改後皆可重載，須限制 Materials 的新增入口或統一 key 的 tier 上限。補 tier4 與 default stack 邊界測試。 |
| W-05 | low | `Assets/_Project/Features/Creatures/Api/MaterialDrops.cs:96`、`:105`、`:116`；`Assets/_Project/Features/Creatures/Tests/EditMode/MaterialDropsTests.cs:142` | **TryRoll 的 bias 元素 Pre 沒有完整檢查，RNG 的 seeded Pre 也未明載。** | 目前只檢查 bias 非空；非法 enum 只有被抽中且成功時，才可能由 MaterialKey 建構子發現。Editor 實測正常配置、seed1／D、Normal、bias=`[(Element)99]` 直接回 false，沒有 ContractException；Boss 時 Editor 拋契約例外，但 Release 會返回含未知元素的 stack。現有測試沒有這條 Pre 的違反案例。正式 PlanetTypeDef 由 importer 保證元素有效，故不是正常資料漏洞；Release 移除呼叫者契約本身也不是缺陷。 | Editor／Development 在抽樣前驗證所有 bias 元素，TryRoll 註明 rng 必須由 Pcg32 建構子播種；補非法元素即使此次未掉落也失敗的測試。外部資料驗證若依賴此入口，則應使用不剝除的拒絕路徑。 |

定案與正常流程判讀：

- 正式 `Data/balance.json` 與 staged Copy 的素材配置一致：普通 tier1／10%、精英 tier2／60%、BOSS tier3／100%，成功各1。角色只選一組；星球 bias 抽一種，不看被擊倒者專精；沒有收服、任務或區域拾取的第二筆素材。
- `WildInfo` 是參考型別，Dropped 在第一次判定前設 true，成功與失敗都只判一次；BOSS 重送不會重抽素材。既有注入接縫重送會重發 XP 的 R5-01 仍存在且已在 tech-debt，本輪沒有把它誤列為新素材 bug。
- 素材為獨立 `Pcg32(seed, 'D')`，以 ref 推進；戰鬥仍用 Encounter 的 C，收服仍用 K，沒有把新抽樣插入既有兩條串流。同 seed、相同擊倒角色／順序及 bias 輸入可重現；不同操作造成擊倒順序改變，本來就可改變掉落。失敗一個 bounded draw、成功兩個；bounded draw 內部可能有拒絕取樣，並非固定一／兩次底層 PCG step。
- End 的同步單次防護保留同一份 RunOutcome.Materials，OnRunEnded 不按勝敗／放棄篩除素材，符合已知刻意政策。`last_material_settlement_id` 在目前單次同步回呼下不要求補做；若將來加入重試／續存，再與庫存同筆保存去重。W-02 的建議若引入重試，也須同步處理交易及去重。
- v1／v2 忽略 materials；v1 維持階段底 XP 遷移，v2 保留累計 XP；舊檔備份沿用 `.vN.bak` 且不覆寫。v3 庫存由穩定小寫元素字串及唯一鍵組成，Snapshot 依元素／tier 排序。未知元素、數字元素 ID、tier0／4、非正 qty、重複鍵透過不剝除的 Check 拒絕；重複鍵沒有合併。更高格式版本仍拒絕。原始超大數／小數等解析行為見驗證限制，沒有假定 Unity 會嚴格處理。
- AddMaterials 對正常建構且在存檔域內的 stack 先用 long 彙總，再檢查所有鍵後入帳；列表 Count 及每筆 Qty 都受 Int32 上限約束，總和也不會溢出 Int64。精確達 Int32 上限可入帳，再加1拒絕且全部庫存不變。
- 正常進化同步呼叫 TryEvolve → SetSpecies → Write；目前單執行緒及有效資料下，SetSpecies 的 owned／species Pre 由按鈕來源保證，沒有中途競態。成功保留永久 ID、器皿、經驗與隊伍順序；等級或素材不足時列出各缺口，不扣素材、不換型態、不 Write。例外路徑另列 W-01。
- 新增公開 API 基本 XML DbC 齊備；設定範圍、載入檢查、入帳溢位使用不剝除的拒絕路徑。內部 Bootstrap 成員不因宣告 public 就成為對外 API，但仍已附相應註解。契約邊界缺口見 W-04／W-05。

實際執行的驗證與結果：

| 驗證 | 結果／可宣稱範圍 |
|---|---|
| `git show 308dc76` 及範圍內差異／呼叫端搜尋 | 已逐一審閱；受審 HEAD 為該完整 commit。審查用 C# 原始碼由 `git show 308dc76:<path>` 複製到 repo 外暫存目錄，避免修改工作檔。 |
| .NET SDK 9.0.304，新增兩個測試 fixture 的原始方法＋本機 nunit.framework DLL | `UNITY_EDITOR`：28/28 通過；契約剝除版排除4個含 ContractException 斷言的方法後24/24通過。這是 CLR 執行原始 NUnit 斷言，不是 Unity Test Runner。 |
| 1,000 seeds、三種角色、雙偏好星球的 chance／element／tier／qty／後續 RNG 狀態核對，加上遷移、備份、庫存上限及進化持久化／拒絕 | 契約啟用及剝除版各 **9,394 項自訂斷言通過**。成功寫檔重載為新型態／扣料後庫存；拒絕時狀態完全不變。 |
| 四個獨立暫存突變，逐一還原後再測 | normal／boss 配置對調：24 pass／4 fail；v2讀素材：27／1；重複鍵合併：27／1；逐鍵檢查並立即入帳：27／1，最後一案明確失敗於 `no partial deposit`。全數由對應斷言殺死，非編譯失敗。還原基準再次28/28。 |
| 用 GameDatabase 資產 GUID 順序、實際 staged 物種及文案 vocabulary 重建新 PlayMode 案例的 RunPlanner 輸入；使用正式 RunPlanner／Pcg32／MaterialDrops | seed **1**，spawn **23、24、47**；物種為 wood_fern_chick、wood_fern_chick、wind_monsoon_galeon。D 得 **Water/T3**；改 K 或 C 均得 **Wind/T3**。因此此案例能分辨指定串流錯接；若屬性改取被擊倒者，該 BOSS 主屬性 Wind 也與預期不同。這是 CLR 重播 oracle 的具體見證，未執行 Unity coroutine。 |
| 鎖定最終存檔後，照 DemoGame 交易順序執行正式庫存／進化／存檔程式 | 兩設定均重現 W-01 的 `IOException memory=b/0 disk=a/3`。此探針重播交易順序，沒有執行 IMGUI 按鈕。 |
| 原樣抽出 RunSession.End 與 DemoGame.OnRunEnded，包在 UI 無副作用替身中；正式 LobbySave／Progression／材料類別 | 兩設定均重現 W-02 的半途結算與無法再次 End；沒有用 Unity 場景驗證畫面。 |
| tier4、default stack、非法 bias 元素探針 | 重現 W-04／W-05；未將違反既有型別 Pre 的輸入提升為 high。 |

探針位於 `%TEMP%/cosmimera-p2-05-wiring-sol/`；`model/` 為 commit 原始碼快照、暫存 driver 及替身。JsonUtility 替身以 System.Text.Json 映射 DTO 欄位，ScriptableObject 為最小替身；數學使用現有 Unity.Mathematics managed DLL。**沒有使用這個較嚴格的替身證明 Unity 原生 JSON parser 對溢位、小數、數字字串的行為。** 這些應在可用 Unity 時補驗；也未跑 Mono／IL2CPP、IMGUI、真實場景或完整 EditMode／PlayMode。commit 訊息所述929/929與68/68是作者驗收資料，不列為本輪實跑結果。暫存替身有 CS0649 未賦值欄位警告，非受審程式錯誤。

實跑命令摘要（Python 使用本機可用的 Blender 內附直譯器）：

```powershell
git show 308dc76
dotnet --version
$py = 'C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe'
$reviewTemp = Join-Path $env:TEMP 'cosmimera-p2-05-wiring-sol'
& $py -X utf8 -B (Join-Path $reviewTemp 'prepare.py')
& $py -X utf8 -B (Join-Path $reviewTemp 'plan_fixture.py')
dotnet restore (Join-Path $reviewTemp 'model/Probe.csproj') --ignore-failed-sources --verbosity quiet
dotnet run --project (Join-Path $reviewTemp 'model/Probe.csproj') --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR -- tests
dotnet run --project (Join-Path $reviewTemp 'model/Probe.csproj') --no-restore -c Release -p:ProbeSymbols=RELEASE_PROBE -- release-tests
dotnet run --project (Join-Path $reviewTemp 'model/Probe.csproj') --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR
dotnet run --project (Join-Path $reviewTemp 'model/Probe.csproj') --no-restore -c Release -p:ProbeSymbols=RELEASE_PROBE
dotnet run --project (Join-Path $reviewTemp 'model/Probe.csproj') --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR -- plan
& $py -u -X utf8 -B (Join-Path $reviewTemp 'mutations.py')
dotnet run --project (Join-Path $reviewTemp 'model/Probe.csproj') --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR -- end
dotnet run --project (Join-Path $reviewTemp 'model/Probe.csproj') --no-restore -c Release -p:ProbeSymbols=RELEASE_PROBE -- end
dotnet run --project (Join-Path $reviewTemp 'model/Probe.csproj') --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR -- dbc
Set-Alias -Name python -Value $py
python -c "open('docs/reviews/P2-05-wiring-sol.md',encoding='utf-8').read()"
git diff --check -- docs/reviews/P2-05-wiring-sol.md
```

交付驗收：沒有 high，故沒有欠缺重現步驟的 high。報告以 UTF-8 無 BOM 寫入，指定 Python 讀取成功、U+FFFD 為0；既有 tracked 檔案 SHA-256 比對無變更，本輪唯一新增的 repo 檔案為本報告。med／low 待 OPUS 登錄 tech-debt，本輪遵守限制未代寫。
