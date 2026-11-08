# D0 r2 — SOL 唯讀對抗式審查

判定：**fail**。**high 1 / med 3 / low 1 / doc 0**。審查範圍固定為 `git diff 14ebb9e ba6ea89`，行號均指 `ba6ea89`，不混入後續 HEAD 或工作樹變更。2026-11-09 · ChatGPT Sol（SOL）。

依 `docs/roadmap/demo.md`，D0 先處理 high 與阻礙遊玩；S2–S5 交由維護者記入 `docs/tech-debt.md`。本次只新增本報告，未改 roadmap、tech-debt、程式或資料，未執行 Unity。

| ID | 嚴重度 | 檔案:行 | 問題 | 見證 | 建議修法 |
|---|---|---|---|---|---|
| S1 | high | `tools/demo-data/merge.py:33`；`Assets/_Project/Bootstrap/Demo/GeoTemplateLoader.cs:33` | **既有缺陷，非本次 diff 引入**：正常 merge 會整目錄刪除 GeoTemplates，連同 36 個場景引用的資產 `.meta` 與任意非產生檔一起刪除，僅還原資料檔。Unity 重建 GUID 後原場景引用失效；loader 直接解參考 TextAsset，失效時會阻止大廳初始化。單純成功退出不代表可遊玩。 | W1 實際執行完整目標版 merge：exit=0、36 個舊 GUID 均被 Demo.unity 引用、執行後 0 個 .meta，handmade.txt 亦消失。未以 Unity 重現例外；失效後的解參考後果由 loader 原碼推導。 | 保留既有資料檔及其 .meta，按產生檔 manifest 就地更新；僅清理 manifest 內已退役項目及其配對 .meta。不要用整目錄 rmtree；驗證重跑前後場景 GUID 不變。 |
| S2 | med | `Assets/_Project/Data/Copy/strings.json:243`；`Data/_incoming/d0-copy-terra/demo-ui-supplement.json:83` | 七個隊伍編成繁中字串已被實際寫成 ASCII 問號；預設 zh-TW 的加入、移除、上移及錯誤提示無法辨識。非終端機編碼顯示問題，來源與產物均損壞。 | W2 輸出七個鍵及實際值；例如 add_to_party 與 remove_from_party 都是 `????`。 | 修復來源 UTF-8 文案後安全重產；補入資料驗證，拒絕整句只有問號的翻譯。 |
| S3 | med | `tools/demo-data/merge.py:48` | 衝突偵測發生在複製輸出之後。遇到跨檔重複鍵雖然失敗，原已審核 strings.json 已被基礎檔覆蓋，supplement 與原輸出鍵消失；主程式還會先刪除 Data/_demo。 | W2 觸發 same 衝突後，輸出從 old_reviewed_key 變成僅有 same/base，並非原輸出。 | 先完整解析及驗證所有輸入，再在暫存輸出樹組裝並發布；失敗保留既有產物。 |
| S4 | med | `tools/demo-data/merge.py:52` | json.loads 未拒絕**同檔內**重複鍵；set 交集只能檢查解析後的跨檔鍵名。同一 supplement 中重複 new 可靜默覆寫已審核文案。未發現目標版目前資料已有此種重複；這是實際執行的對抗輸入。 | W2：兩筆 new 被接受，最後產物為 second，沒有衝突錯誤。 | 以 object_pairs_hook 檢查每個 JSON object 的重複鍵；可沿用 `tools/balance/derived_stats.py` 的檢查模式，再檢查跨檔交集。 |
| S5 | low | `Assets/_Project/Bootstrap/Demo/ScatterField.cs:311` | 半格距離的圓盤取樣不保證覆蓋所有相交格；在 cube-face 接縫附近，90m 內可有通過放置機率的格未加入 wanted。屬裝飾邊界漏生／跳現，非遊玩阻擋。 | W3：平地、無海，R=640、seed=2026、focus=normalize(1,1,1)，key=21301 的候選位置距離約 85.833869m，卻 sampled=False。見證是 float32 數學鏡像，非 Unity 執行結果。 | 保守枚舉圓盤覆蓋的 face/cell 範圍，跨面處理後以候選位置或 cell bounds 做距離篩選；加上角點與接縫測例。 |

W1：在 repo 根目錄，以 Python 3.12+ 執行下列程式。所有刪寫皆在 TemporaryDirectory；直接執行的是 `ba6ea89` 原始 merge.py。

```python
import io, pathlib, re, subprocess, sys, tarfile, tempfile
rev="ba6ea89"
paths=["tools/demo-data/merge.py","Data","Assets/_Project/Data/GeoTemplates","Assets/_Project/Bootstrap/Demo.unity"]
with tempfile.TemporaryDirectory() as td:
    root=pathlib.Path(td)
    archive=subprocess.check_output(["git","archive",rev,*paths])
    with tarfile.open(fileobj=io.BytesIO(archive)) as ar: ar.extractall(root,filter="data")
    geo=root/"Assets/_Project/Data/GeoTemplates"
    (geo/"handmade.txt").write_text("KEEP")
    metas=list(geo.glob("*.meta")); scene=(root/"Assets/_Project/Bootstrap/Demo.unity").read_text()
    print("before_meta=%d scene_referenced=%d"%(len(metas),sum(re.search(r"guid: (\w+)",p.read_text()).group(1) in scene for p in metas)),flush=True)
    result=subprocess.run([sys.executable,"tools/demo-data/merge.py"],cwd=root,capture_output=True,text=True)
    print("merge_exit="+str(result.returncode)); print(result.stdout.strip())
    print("after_meta=%d handmade_exists=%s"%(len(list(geo.glob("*.meta"))),(geo/"handmade.txt").exists()))
    assert result.returncode==0 and len(metas)==36 and not list(geo.glob("*.meta"))
```

實際輸出（Git 權限提示省略）：

```text
before_meta=36 scene_referenced=36
merge_exit=0
staged 12 geo templates into Assets\_Project\Data\GeoTemplates
staged 17 files into Data\_demo; copy into Assets\_Project\Data\Copy
after_meta=0 handmade_exists=False
```

另實際比較兩端 `stage_geo` 函式原文，相等，輸出 `high_source_predates_diff=True`。S1 保留為本次明確要求評估的既有 D0 阻擋，並非將舊碼誤認成新增回歸。

W2：直接載入目標版 stage_copy，所有對抗資料及輸出在暫存目錄。NAME_SOURCES 清空只排除無關的名稱產製；衝突判斷與寫入原碼未更動。

```python
import json, pathlib, subprocess, tempfile, types
def blob(p): return subprocess.check_output(["git","show","ba6ea89:"+p])
m=types.ModuleType("merge")
exec(compile(blob("tools/demo-data/merge.py"),"merge.py","exec"),m.__dict__)
with tempfile.TemporaryDirectory() as td:
    root=pathlib.Path(td); m.COPY_OUT=root/"Copy"; m.NAME_SOURCES=[]
    inc=root/"Data/_incoming/d0-copy-terra"; inc.mkdir(parents=True)
    for name in m.D0_COPY: (inc/(name+".json")).write_text("{}")
    (root/"Data/balance.json").write_text("{}")
    m.COPY_OUT.mkdir(); (m.COPY_OUT/"strings.json").write_text('{"old_reviewed_key":{}}')
    (inc/"strings.json").write_text('{"same":{"en":"base"}}')
    (inc/"demo-ui-supplement.json").write_text('{"same":{"en":"override"}}')
    try: m.stage_copy(root/"Data")
    except SystemExit as e: print("overlap rejected:",e)
    print("S3: strings_after_rejected_merge="+(m.COPY_OUT/"strings.json").read_text())
    (inc/"demo-ui-supplement.json").write_text('{"new":{"en":"first"},"new":{"en":"second"}}')
    m.stage_copy(root/"Data")
    print("S4: duplicate_within_supplement_accepted="+json.loads((m.COPY_OUT/"strings.json").read_text())["new"]["en"])
strings=json.loads(blob("Assets/_Project/Data/Copy/strings.json"))
bad={k:v["zh-TW"] for k,v in strings.items() if "?" in v["zh-TW"] and all(c in "? 6" for c in v["zh-TW"])}
print("S2: corrupted_zh_TW="+json.dumps(bad,ensure_ascii=True,sort_keys=True))
```

實際輸出：

```text
overlap rejected: error: demo-ui-supplement.json overrides strings.json keys: ['same']
S3: strings_after_rejected_merge={"same":{"en":"base"}}
S4: duplicate_within_supplement_accepted=second
S2: corrupted_zh_TW={"ui.lobby.add_to_party": "????", "ui.lobby.move_up": "??", "ui.lobby.party_full": "??????? 6 ???", "ui.lobby.party_hint": "??????????????", "ui.lobby.party_min": "???????????", "ui.lobby.remove_from_party": "????", "ui.lobby.reserve": "???"}
```

W3：需 NumPy。重現 ScatterField 的圓盤格點、CubeSphere 的 equi-angular 座標與 Pcg32 前三次取樣；平地且無海，故不會被坡度／海面判定排除。tangent_m 是回投取樣切平面的半徑，與程式的 90m 圓盤一致。

```python
import math, numpy as np
F=np.float32; R=F(640); n=101; key=21301
N=np.array([[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]],dtype=F)
U=np.array([[0,0,-1],[0,0,1],[1,0,0],[1,0,0],[1,0,0],[-1,0,0]],dtype=F)
V=np.array([[0,1,0],[0,1,0],[0,0,-1],[0,0,1],[0,1,0],[0,1,0]],dtype=F)
def norm(p): return p/np.sqrt(np.sum(p*p,axis=-1,keepdims=True))
up=norm(np.array([1,1,1],dtype=F))
east=norm(np.cross(np.array([0,1,0],dtype=F),up)); north=np.cross(up,east)
ab=np.array([(a,b) for a in range(-18,19) for b in range(-18,19) if a*a+b*b<=324],dtype=F)*F(5)
p=up*R+ab[:,0,None]*east+ab[:,1,None]*north
axes=np.argmax(np.abs(p),axis=1)
face=axes*2+(np.take_along_axis(p,axes[:,None],1)[:,0]<0)
uv=np.arctan(np.stack([np.sum(p*U[face],1),np.sum(p*V[face],1)],1)/np.sum(p*N[face],1)[:,None])*F(4/math.pi)
ij=np.clip(((uv+F(1))*F(.5)*n).astype(int),0,n-1)
wanted=set((face*n+ij[:,1])*n+ij[:,0])
state=0;inc=key*2+1;mask=(1<<64)-1
def uint():
    global state
    old=state;state=(old*6364136223846793005+inc)&mask
    x=(((old>>18)^old)>>27)&0xffffffff;r=old>>59
    return ((x>>r)|(x<<((-r)&31)))&0xffffffff
uint();state=(state+(2026^0x5ca77e5))&mask;uint()
def rand(): return F((uint()>>8)/16777216)
chance=rand();face,rem=divmod(key,n*n);j,i=divmod(rem,n)
u=F(-1)+F(2)*F(F(i)+rand())/F(n)
v=F(-1)+F(2)*F(F(j)+rand())/F(n)
pos=norm(N[face]+U[face]*F(math.tan(float(u)*float(F(math.pi)*F(.25))))+V[face]*F(math.tan(float(v)*float(F(math.pi)*F(.25)))))
dot=float(np.sum(pos*up));distance=640*math.sqrt(1-dot*dot)/dot
assert chance<=F(.55) and distance<90 and key not in wanted
print("S5: samples=%d wanted_cells=%d key=%d sampled=%s tangent_m=%.6f"%(len(ab),len(wanted),key,key in wanted,distance))
```

實際輸出：

```text
S5: samples=1009 wanted_cells=339 key=21301 sampled=False tangent_m=85.833869
```

已核對、未列為缺陷：

- 散佈每格以 `Pcg32(seed ^ Salt, key)` 獨立取樣；種類從固定陣列選取。Sets 僅按鍵查詢，_wanted 的列舉順序不回饋任何其他格的 RNG。另以相同 PCG 鏡像執行正序、反序、奇偶交錯三種順序，輸出 `same_cell_rng: forward/reverse/interleaved descriptors identical for 339 keys`。未發現 UnityEngine.Random、跨格浮點累加、Dictionary 列舉或地形 chunk 載入順序影響格內候選的路徑。
- 相同 seed／GeneratorVersion 的結論限於相同輸入與同一數學實作；未執行 Mono／IL2CPP／Burst 跨後端比較，不宣稱浮點三角函式逐位元一致。ScatterField 目前也無獨立生成快照；改動其規則時應納入 GeneratorVersion 管理。
- 世界上方取自 PlanetFrame；`LookRotation(frame.North, frame.Up) * AngleAxis(yaw, Vector3.up)` 的右側 Vector3.up 是物件局部 yaw 軸，非世界上方。未使用 Physics.gravity。cell key 的 face/i/j 唯一且 _live 去重；未找到一般跨面重複生出的見證。
- 每次 Refresh 有 1369 次迴圈、1009 次有效投影，靜止仍每 0.5 秒重算；平常未到刷新時只計算 focus／位移。首次或移動進新格會配置每格 List、GameObject／primitive 並銷毀退出物件，沒有池化；集合與材質快取會重用。未跑 Unity profiler，無 GC 位元組數或幀時間實測，故不將成本推測升格為效能 finding。
- DemoGame 從合法存檔出發：移除保留至少一隻、加入僅遍歷 Owned 且排除已在隊伍者並限制六隻、交換只改順序；提交前複製清單，不觸發舊 SetParty 自我別名問題。未找到 UI 可構造空隊、重複、超額或未擁有 ID 的路徑。LobbySave.SetParty／Write 均同步；Write 失敗可能使記憶體先於磁碟，但原子替換仍保留完整舊檔，未見新增半份 JSON 的路徑。這是原碼審查，未實跑 Unity UI 或注入檔案系統故障；既有 FromJson 對手改非法隊伍的完整驗證不由本 diff 修補。
- level_progression.py 使用精確整數冪次及向上取整，與文件每級獨立算成本一致；half-up 縮放、LV1／100、T(10)=1106、T(100)=248528、死亡排除分母、事件隔離及精確分數分配均由既有獨立 oracle 測試通過。未把尚未定案的副手及整數結算當成已接線功能。

驗證與唯讀限制：

- 透過 `git archive ba6ea89` 匯出 tools／Data 等目標資料到系統暫存，避免工作樹後續修改污染測試；對該副本執行 `python -m unittest discover -s tools/balance -p 'test_*.py' -v`：**Ran 27 tests in 0.961s / OK**。PYTHONDONTWRITEBYTECODE=1，未在 repo 寫入快取。
- 完整 pytest 嘗試未能通過收集：初始 Python 無 pytest；唯讀借用既有 pytest 後，因缺 `jsonschema`（另缺 tifffile）中止，輸出 `ModuleNotFoundError: No module named 'jsonschema'`。暫存目錄安裝依賴亦被網路權限拒絕。未宣稱 `tools/tests tools/balance` 全部通過；未執行 Unity。
- 工作樹並非本審查獨占：開始檢查時 HEAD 為 7fe2f8f，審查期間其他程序持續修改 DemoGame／RunSession／場景、字串與日文資料，並新增 StarField／shader／meta。這些變更均未覆寫、還原或納入目標版結論。一般 git status 最初受 LFS 暫存權限阻擋；用系統暫存中的等價 LFS SHA-256 filter 核對後，已排除圖片假陽性。
- 已重讀本檔、確認唯一 high 對應實際執行過的 W1，並執行 `git status --porcelain`。輸出包含本報告、9 個外部修改檔及 6 個其他新檔；對先前建立的 1574 個追蹤檔 SHA-256 基線比較，另觀察到 7 個檔案在審查期間改變。「全工作樹只出現本報告」受外部並行變更影響，無法達成；未還原他人工作以湊出乾淨狀態。SOL 的 repo 寫入操作只有新增本檔；所有執行見證均僅在系統暫存刪寫。
