# D0 Windows demo 操作手冊

## 啟動

解壓整個 demo 壓縮檔後執行 `Cosmimera.exe`；保留旁邊的資料夾與 DLL。無須安裝 Unity Editor。選單與探索均可使用手把或鍵盤滑鼠，建議 1280×720 以上視窗。

首次啟動選擇御三家之一（LV10），再按出發，從三顆星球中選擇降落。隊伍已有存檔時直接顯示大廳；可調整順序、移到待命區或加入隊伍。首位是預設主力，隊伍至少保留一隻、最多六隻。

大廳的語言按鈕依序切換繁中、英文、日文，立即套用各畫面；不寫入存檔，重開回到繁中。日文缺字串時逐項回退英文，包括尚無日文的宙獸及招式名稱。

## 選單操作

大廳、選星與結算使用方向鍵／WASD 或手把十字鍵／左搖桿移動焦點，Enter／Space 或手把 A（下方）確認，Esc 或 B（右方）從選星／結算返回大廳。滑鼠仍可直接點擊；焦點以顏色及 `>` 標示。

## 操作

| 按鍵 | 功能 |
|---|---|
| WASD／方向鍵 | 移動 |
| 滑鼠移動 | 轉動視角 |
| 左 Shift | 衝刺 |
| Space | 跳躍 |
| 1／2 | 選擇目前在場主力 |
| Tab | 將所選主力換成輪替池內可用宙獸；有共用冷卻 |
| F | 集火最近交戰敵人 |
| R | 自由戰鬥 |
| T | 跟隨 |
| G | 撤退 |
| V | 切換第一／第三人稱視角 |
| B | 輪換收服道具 |
| C | 嘗試收服 8 m 內、擊倒後 20 秒內的宙獸，每隻僅一次機會；靠近會顯示剩餘秒數 |
| Esc，再於 3 秒內按 Esc | 放棄本次探索，進入結算 |

宙獸會自動施招；第一次只有一隻御三家，收服後返回大廳編隊，下一場才可展示雙在場與輪替。玩家身上的攻擊與翻滾尚未接線。

## 探索中使用手把

偵測到手把時，HUD 顯示該手把的 binding 名稱。以下以 XInput 常見標示說明。這次驗證使用合成 Gamepad 事件，尚未做實體手把／Steam Deck 驗收。

| 操作 | 功能 |
|---|---|
| 左搖桿／右搖桿 | 移動／轉動視角 |
| 按左搖桿／按右搖桿 | 衝刺／切換第一、第三人稱 |
| A（下方） | 跳躍 |
| 十字鍵左／右 | 選擇第一／第二隻在場主力 |
| RB | 輪替所選主力 |
| RT | 集火最近交戰敵人 |
| 十字鍵下／上 | 自由戰鬥／跟隨 |
| LT | 撤退 |
| B（右方） | 切換收服道具 |
| Y（上方） | 嘗試收服 |
| 3 秒內按兩次 Start | 放棄本次探索並結算 |

探索與選單分別使用 `DemoRun`、`DemoMenu` map 的獨立 clone；探索時停用選單 map，結束探索時重新啟用，各 clone 隨其擁有者銷毀而釋放。尚未提供遊戲內重綁介面。

## 展示流程

1. 大廳選御三家，展示 LV 與隊伍順序，再出發。
2. 選星畫面展示星球類型、屬性、尺寸與 seed；目前只能重抽，沒有輸入 seed 的介面。
3. 降落後展示球面移動、地貌、植被／岩石／水晶，以及任務與隊伍 HUD。
4. 接近野生宙獸讓主力交戰，展示自動施招、HP 與 CD；擊倒後靠近並按 C 收服。
5. 依左上任務提示探索、擊倒指定目標及 BOSS，再查看結算與經驗。短展示可用兩次 Esc 提前結算；這不能代替 BOSS 完整通關驗收。
6. 回大廳確認收服與 LV，調整隊伍後退出、重開，確認存檔保留。

任務 HUD 顯示相對目前視角的左／右轉角度與球面距離；接近區域時導向區域中心，討伐時導向仍存活的對應目標，收服時優先導向倒下且尚可收服的宙獸。方向引導是最短大圓方向，未包含避障或區域地圖；遇到山壁、水域需自行繞行。右上角附基本操作提示。倒下的宙獸本場不能復活，戰外不自動回血；難度與輸入手感仍需人工試玩。

首次短展示可先選星蹄駒，尋找落單的幼年野生並避開土系；隔離模擬中它對六種非土系單怪各為 32/32 勝，但對雙怪為 0/224。這不是實玩保證；主力追擊時玩家應跟上，避免距離拉開後脫戰。三隻御三家的比較與限制見 `docs/reviews/D0-playability-sol.md`。

## 存檔與問題回報

大廳存檔：`%USERPROFILE%\AppData\LocalLow\DefaultCompany\COSMIMERA\lobby.json`。探索中途不存進度，結算時才把收服與經驗寫回。重選御三家前先關閉遊戲並備份此檔，再將原檔改名保留。

回報時附上 build-info.json、選星 seed、重現步驟，以及同目錄的 `Player.log`。公開分享前先檢查 log 內的個人路徑。

## 工程重建

專案必須未在 Editor 中開啟。依 `CLAUDE.md` 執行 EditMode／PlayMode；Python 工具驗證使用 `python -m pytest tools/tests tools/balance` 與 `python tools/validate_data.py --root Data/_demo`。若沙箱暫存目錄不可寫，將該次程序的 TEMP／TMP 與 pytest `--basetemp` 設為 `Logs/` 下的專用目錄。

重用現有 `Cosmimera.EditorTools.ReproBuild.BuildAll` 入口建置 Demo 場景，產出 `Builds/Repro/mono` 與 `Builds/Repro/il2cpp`。以 IL2CPP 輸出打包，排除 `*_DoNotShip` 偵錯資料夾，附上本手冊與版本資訊（地貌為程序化生成，不含第三方地理資料，無需署名檔，ADR-0008）。此流程尚未接入發行 CI。

同一 checkout 建置及測試完成後，可用正式工具打包；receipt 路徑改成該次實際輸出，版本名不可與既有包重複：

```text
python -B tools/demo/package.py --name COSMIMERA-D0-<version>-win64 --build-log Logs/<build>.log --editmode Logs/<editmode>.xml --playmode Logs/<playmode>.xml --preview Logs/demo-shot.png
```

工具驗證完整 IL2CPP 必要檔案、成功的建置與測試紀錄、ZIP CRC 及逐檔 SHA-256；產出操作手冊、`build-info.json`、`SHA256SUMS.txt` 與 ZIP 旁的 `.sha256`。它記錄打包當下 checkout，不能代替建置來源證明或實玩驗收。初始難度基線可用 `Cosmimera.EditorTools.DemoBalanceProbe.RunBatch` 在 batchmode 重跑，輸出 `Logs/d0-balance.json`。
