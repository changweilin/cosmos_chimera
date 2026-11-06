# ADR-0003 · Roslyn analyzer 與 feature 封裝的強制方式

## 狀態
已採納（2026-11-06，使用者同意下載 analyzer）

## 背景
P0-03 要求「編譯 0 警告；跨 feature 引用 `Internal/` 會編譯失敗」，且 `unity-csharp.md` 把格式與命名交給 `.editorconfig` + analyzer。`overview.md` 規定每個 feature 一個 asmdef。

## 決定
1. **Analyzer**：`Microsoft.Unity.Analyzers` 1.28.0（Microsoft，MIT）。只取 `analyzers/dotnet/cs/Microsoft.Unity.Analyzers.dll`，放在 `Assets/_Project/Analyzers/`，設定 `RoslynAnalyzer` label，並排除所有平台（只在編譯期載入，不打包進 Player）。它是用 Roslyn 3.11 建置的，可在 Unity 6.3 內建的 Roslyn 4.x 上載入。
   nupkg SHA-256：`8b5fcfc94090a5d925a84c78b0091962a8accd682ef978c4753cffbc01cefb21`。升級時更新本 ADR 的版本與雜湊。
2. **嚴重度**：寫在 `Assets/Default.ruleset`。Unity 會把它以 `-ruleset` 傳給每個組件，但不會傳 `.editorconfig`（2026-11-06 已在 `Library/Bee/*.rsp` 確認）。只有真正的 bug 類規則（Unity 物件存活判斷被繞過、Update 中的配置等）調到 warning。repo 根目錄的 `.editorconfig` 只管格式與命名，在 IDE 中生效，編譯器不會強制。
3. **封裝**：維持一個 feature 一個 asmdef。`Internal/` 的型別一律宣告為 `internal`，命名空間用 `Cosmimera.<Feature>.Internal`；`AssemblyInfo.cs` 只對該 feature 的測試組件開 `InternalsVisibleTo`。這樣跨 feature 引用時會得到 CS0122 編譯錯誤。
4. **守門測試**：`Assets/_Project/Tests/EditMode/ArchitectureTests` 檢查兩件事：(a) 任何 `*.Internal` 命名空間都沒有 public 型別；(b) 各 asmdef 的 references 都在 `overview.md` 依賴圖的白名單內。

## 後果
- 有人把 Internal 型別誤設成 public，編譯不會擋，但 EditMode 測試會擋，CI（P0-07）也會擋。
- 新增 feature 或依賴邊時，需要同步修改 `overview.md` 和守門測試的白名單。
- 0 警告目前靠人工檢查 batchmode 編譯 log。等 CI 建好後，再考慮 `-warnaserror`（需要 `csc.rsp`）。

## 替代方案
- **每個 feature 拆成 Api 與 Impl 兩個 asmdef**：不靠 `internal` 也能擋住引用，但 asmdef 數量會加倍，也違反 `overview.md` 的「一個 feature 一個 asmdef」。
- **自寫 Roslyn analyzer 檢查資料夾**：最精確，但要另外建置和維護 .NET 專案，成本太高，P0 階段不值得。
