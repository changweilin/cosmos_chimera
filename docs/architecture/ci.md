# CI：GitHub Actions + GameCI（P0-07）

設定： [ci.yml](../../.github/workflows/ci.yml)。所有目標分支的 `pull_request`、推送至 `main` 都觸發；同一 PR／ref 的舊 run 會取消。不使用 workflow 層 `paths-ignore`（避免 required check 根本不觸發而卡住合併），改由 `ci-scope` fail-closed 判斷是否需要跑 Unity。

## Job 與失敗語意

| Job／check 名稱 | 工作與結果 |
|---|---|
| `validate-data` | Ubuntu、Python 3.12；`pip install -r tools/requirements.txt`，執行 `tools/validate-data` 與 `python -m pytest tools/tests -v`；任一非零退出即失敗。 |
| `ci-scope` | 不 checkout PR 程式碼，透過 GitHub API 檢查變更清單；僅當非空、`changed_files < 300` 且全部 `filename`／`previous_filename` 符合 `docs/.+` 或根目錄 `*.md` 時輸出 `needs_unity=false`，任何異常或非預期事件皆 fail-closed 設為 `true`。 |
| `unity-license` | 不 checkout PR 程式碼，只輸出三個 secrets 是否齊全的布林值；fork PR／Dependabot 一律不放行。缺少時在 summary 說明 Unity 未執行。 |
| `editmode-tests` | 僅 `needs_unity == 'true'` 且憑證齊全、來源受信任才執行，否則顯示 skipped。從 `ProjectSettings/ProjectVersion.txt` 讀版本（目前 `6000.3.28f1`），`UMA_CACHE_DIR` 指向 `runner.temp` 避免把 ~950 MB `.unitypackage` 重複塞進 `Library` cache（並清掉舊快取殘留的 `Library/UMA-cache`）；`tools/install-uma --unpack-only` 排除 `Assets/UMA` 內四個測試目錄（含 `[InitializeOnLoad]` 逐筆 `fsync` 磁碟的 `TestRunnerIntegration`）；GameCI 以 `-base-3` 映像執行 `-testFilter Cosmimera` EditMode 測試；測試／啟用／容器失敗皆保留失敗狀態。 |
| `ci-required` | `always()` 匯總以上四者：`validate-data`、`ci-scope`、`unity-license` 皆須為 `success`；當 `needs_unity == 'false'` 時允許 `editmode-tests` 為 `skipped`（或 `success`），否則必須為 `success`。設為 required check，避免未測試或條件異常時放行合併。 |

`tools/validate-data` 是 POSIX shell wrapper，轉交 `tools/validate_data.py`。預設 root 為 `Data`；只掃支援的資料種類目錄，不把 `_incoming` 的負例當成正式資料。支援 `--root`、`--schemas`、`--retired-ids`；退出碼 0＝無 error（允許 warning），1＝資料錯誤，2＝輸入／環境錯誤。

現有 `tools/tests/test_validate_data.py` 已驗證 `Data/_incoming/p0-05-samples/valid` 無 findings，將全部 8 個 invalid fixture 各自疊加至 valid 副本並斷言預期 error code／JSON pointer，另檢查 fixture 清單無漏列，所以 workflow 不再重複跑 fixture。此負例測試走 Python API；CLI 測試涵蓋 valid 退出碼 0 與不存在 root 的退出碼 2，尚未直接覆蓋 invalid 的 CLI 退出碼 1。

GameCI 釘選 [`v4.3.2`](https://github.com/game-ci/unity-test-runner/releases/tag/v4.3.2)，使用既有 action 架構；該版本發行說明指出 `v4.4.0` 改為 CLI wrapper。本次未採用浮動 `@v4`／`@main`。

兩個 checkout 都啟用 LFS 並關閉 credential 持久化。Library cache key 包含 OS、模式、`Packages/manifest.json`、`Packages/packages-lock.json`、`ProjectSettings/ProjectVersion.txt` 雜湊與 commit SHA；restore 只接受同一套套件／Editor 版本，新的 SHA 可更新快取。測試失敗後仍嘗試上傳 `artifacts/editmode/` 與 `Logs/`，artifact 名為 `editmode-results`，保留 14 天；若啟動前已失敗，可能沒有檔案。

權限僅 `contents: read`（`ci-scope` 另含 `pull-requests: read` 且不 checkout 程式碼）；無 checkout 與無 API 查詢的 `unity-license`、`ci-required` 為 `permissions: {}`。不傳 GameCI 的 `githubToken`，不用額外建立 check 的寫入權限；測試結果由 job 狀態與 artifact 提供。[GameCI Test runner 文件](https://game.ci/docs/github/test-runner/)

## Unity 6 Personal 授權與 secrets

查核日期：2026-11-06。Unity 6.3 官方明訂 **Personal 只能透過 Unity Hub 啟用／歸還**，不支援 Personal 的離線手動啟用。不要把舊版 `.alf` → 手動啟用網站流程套用至 Unity 6 Personal。[Unity 6.3 授權方式](https://docs.unity3d.com/6000.3/Documentation/Manual/LicenseActivationMethods.html)

GameCI 現行 Personal 指引仍採「本機 Hub 啟用、取得 `.ulf`、連同帳密存入 GitHub secrets」。以下是該指引的設定路徑；**本專案的 Unity 6000.3.28f1 + v4.3.2 啟用實測未驗證**，不能保證目前每個 Hub／帳號都會產生可供容器使用的 `.ulf`。[GameCI Activation 文件](https://game.ci/docs/github/activation/)

1. 以供 CI 使用的 Unity 帳號登入 Unity Hub，進入 Preferences → Licenses → Add → Get a free personal license，依 Hub 流程完成啟用。
2. 依 GameCI 指引尋找授權檔：Windows `C:\ProgramData\Unity\Unity_lic.ulf`；macOS `/Library/Application Support/Unity/Unity_lic.ulf`；Linux `~/.local/share/unity3d/Unity/Unity_lic.ulf`。若未產生 `.ulf`，停止此設定流程，查核 GameCI 最新指引與 Unity 支援，不假設只有帳密就能啟用。
3. GitHub repository → Settings → Secrets and variables → Actions → New repository secret，建立下表三項。組織 secrets 須授權此 repository 存取。

| Secret | 內容／來源 |
|---|---|
| `UNITY_LICENSE` | 上述 `.ulf` 的完整文字內容，不是檔案路徑或 base64。 |
| `UNITY_EMAIL` | 啟用 Personal 的同一個 Unity 帳號 email。 |
| `UNITY_PASSWORD` | 該帳號密碼。 |

授權檔、密碼不得加入 git／artifact。首次具 secrets 的 PR 必須確認容器成功啟用、真的產出 EditMode XML、失敗測試使 job 失敗；本機 `.ulf` 在此 runner 的可用性、MFA／SSO、授權席位回收行為均**未驗證**。憑證存在只決定是否啟動 job，不會把啟用失敗改成 skipped／成功。Professional／license server 需要另行設計設定，本 workflow 目前只實作指定的三個 Personal secrets。

## Branch protection：由 repository 管理者設定

workflow 不會自行修改 GitHub 保護規則。GitHub 會接受 skipped 的 required job，因此**只要求 `editmode-tests` 不足以擋 merge**，必須要求 `ci-required`。[GitHub skipped check 語意](https://docs.github.com/en/pull-requests/reference/status-checks)

1. 先讓可信任的同 repository PR 完整成功一次，確保 `ci-required` 出現在 check 清單中。
2. Repository → Settings → Branches → Add classic branch protection rule，Branch name pattern 填 `main`；其他需要保護的分支另建規則。
3. 勾選 Require a pull request before merging、Require status checks to pass before merging，選取 **`ci-required`**（來源 GitHub Actions）。可再要求 `validate-data` 與 `editmode-tests`，但不能取代匯總閘門。
4. 勾選 Require branches to be up to date before merging，以及 Do not allow bypassing the above settings，保留禁止 force push／delete，按 Create 儲存。[GitHub 設定步驟](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/managing-a-branch-protection-rule)
5. 在測試 PR 驗收：有效修改可合併；資料錯誤／失敗 EditMode 測試不可合併；移除任一 secret 或 fork PR 時 Unity 為 skipped、summary 說明原因、`ci-required` 失敗且不可合併。以上遠端驗收尚未執行。

fork PR／Dependabot 仍跑資料驗證，但缺 Unity 測試就不能直接通過閘門。維護者審閱整份變更後，可將已審查的 commit 納入同 repository 的受信任分支並開 PR，讓有 secrets 的 CI 重跑；更新內容需重新審查。不使用 `pull_request_target` 執行外部 PR 程式碼，也不將 secrets 提供給 fork。重新執行原 fork run 不會改變其信任條件。

## 已知限制與本機驗證

- 本機 EditMode 與 `validate-data`／`pytest` 皆可驗證，但調整後的 `ci-scope` 純文件略過邏輯、`UMA_CACHE_DIR` 遷移與 `unityci/editor:ubuntu-6000.3.28f1-base-3` 映像切換仍需在第一次 GitHub Actions run 上實測確認。CI 的 EditMode 不替代 Windows／IL2CPP／Burst／PlayMode 測試。
- Unity batchmode 已知可能將 `PC_RPAsset.asset` 的 `k_AssetPreviousVersion` 由 12 改成 13；runner 工作目錄為暫時性，CI 不應 commit／push 資產遷移回 repository。本 workflow 沒有 commit 或 push 步驟。
- 尚未支援 merge queue 的 `merge_group` 事件；啟用 merge queue 前需補該觸發器，否則 required check 不會出現。[GitHub merge queue 限制](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks)

可用環境的本機驗證指令（repository 根目錄）：

```sh
pip install -r tools/requirements.txt
tools/validate-data
python -m pytest tools/tests -v
actionlint .github/workflows/ci.yml
# 無 actionlint 時：pip install pyyaml，再執行
python -c "import yaml; yaml.load(open('.github/workflows/ci.yml', encoding='utf-8'), Loader=yaml.BaseLoader)"
```

## 建議修改

目前不需要修改 `tools/` 才能在 CI 執行。後續若要直接守住 CLI invalid 退出碼 1，可由工具主責於 `test_cli_exit_codes` 加入 valid 副本疊加 invalid fixture 後呼叫 `main()` 的斷言；現有 fixture API 測試與 CLI error 計數分支已可供本 workflow 使用。本次未修改 `tools/`。
