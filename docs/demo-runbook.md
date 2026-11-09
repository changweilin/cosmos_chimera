# D0 Windows demo 操作手冊

## 啟動

解壓整個 demo 壓縮檔後執行 `Cosmimera.exe`；保留旁邊的資料夾與 DLL。無須安裝 Unity Editor。使用鍵盤滑鼠，建議 1280×720 以上視窗。

首次啟動選擇御三家之一（LV10），再按出發，從三顆星球中選擇降落。隊伍已有存檔時直接顯示大廳；可調整順序、移到待命區或加入隊伍。首位是預設主力，隊伍至少保留一隻、最多六隻。

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
| V | 輪換收服道具；目前也會切換第一／第三人稱視角 |
| C | 嘗試收服 8 m 內最近的已倒下宙獸，每隻僅一次機會 |
| Esc，再於 3 秒內按 Esc | 放棄本次探索，進入結算 |

宙獸會自動施招；第一次只有一隻御三家，收服後返回大廳編隊，下一場才可展示雙在場與輪替。玩家身上的攻擊、翻滾及手把戰鬥輸入尚未接線。

## 展示流程

1. 大廳選御三家，展示 LV 與隊伍順序，再出發。
2. 選星畫面展示星球類型、屬性、尺寸與 seed；目前只能重抽，沒有輸入 seed 的介面。
3. 降落後展示球面移動、地貌、植被／岩石／水晶，以及任務與隊伍 HUD。
4. 接近野生宙獸讓主力交戰，展示自動施招、HP 與 CD；擊倒後靠近並按 C 收服。
5. 依左上任務提示探索、擊倒指定目標及 BOSS，再查看結算與經驗。短展示可用兩次 Esc 提前結算；這不能代替 BOSS 完整通關驗收。
6. 回大廳確認收服與 LV，調整隊伍後退出、重開，確認存檔保留。

目前任務 HUD 只顯示目標距離，尚無方向箭頭或區域地圖。倒下的宙獸本場不能復活，戰外不自動回血；難度與輸入手感仍需人工試玩。

首次短展示可先選星蹄駒，尋找落單的幼年野生並避開土系；隔離模擬中它對六種非土系單怪各為 32/32 勝，但對雙怪為 0/224。這不是實玩保證；主力追擊時玩家應跟上，避免距離拉開後脫戰。三隻御三家的比較與限制見 `docs/reviews/D0-playability-sol.md`。

## 存檔與問題回報

大廳存檔：`%USERPROFILE%\AppData\LocalLow\DefaultCompany\COSMIMERA\lobby.json`。探索中途不存進度，結算時才把收服與經驗寫回。重選御三家前先關閉遊戲並備份此檔，再將原檔改名保留。

回報時附上 build-info.json、選星 seed、重現步驟，以及同目錄的 `Player.log`。公開分享前先檢查 log 內的個人路徑。

地理資料署名附於 demo 包的 `ATTRIBUTION.md`，來源為既有 GLO-90 烘焙模板。

## 工程重建

專案必須未在 Editor 中開啟。依 `CLAUDE.md` 執行 EditMode／PlayMode；Python 工具驗證使用 `python -m pytest tools/tests tools/balance` 與 `python tools/validate_data.py --root Data/_demo`。若沙箱暫存目錄不可寫，將該次程序的 TEMP／TMP 與 pytest `--basetemp` 設為 `Logs/` 下的專用目錄。

重用現有 `Cosmimera.EditorTools.ReproBuild.BuildAll` 入口建置 Demo 場景，產出 `Builds/Repro/mono` 與 `Builds/Repro/il2cpp`。以 IL2CPP 輸出打包，排除 `*_DoNotShip` 偵錯資料夾，附上本手冊、既有 `Data/geo-templates/ATTRIBUTION.md` 與版本資訊。此流程尚未接入發行 CI。

同一 checkout 建置及測試完成後，可用正式工具打包；receipt 路徑改成該次實際輸出，版本名不可與既有包重複：

```text
python -B tools/demo/package.py --name COSMIMERA-D0-<version>-win64 --build-log Logs/<build>.log --editmode Logs/<editmode>.xml --playmode Logs/<playmode>.xml --preview Logs/demo-shot.png
```

工具驗證完整 IL2CPP 必要檔案、成功的建置與測試紀錄、ZIP CRC 及逐檔 SHA-256；產出操作手冊、署名、`build-info.json`、`SHA256SUMS.txt` 與 ZIP 旁的 `.sha256`。它記錄打包當下 checkout，不能代替建置來源證明或實玩驗收。初始難度基線可用 `Cosmimera.EditorTools.DemoBalanceProbe.RunBatch` 在 batchmode 重跑，輸出 `Logs/d0-balance.json`。
