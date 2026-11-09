# Art/_incoming — 已審查通過的 AI 美術交付暫存區

所有美術 Prompt 鍛造、交付壓縮包（.zip）、成果審查台（2D/3D 對比檢視）與驗收腳本均已集中於 `cosmos_chimera_forge` 專案。

本目錄僅保留作為「審查通過後交付」的暫存區：
1. 成果由 `cosmos_chimera_forge` 審查通過後，透過「🚀 交付至 cosmos_chimera」解壓部署至此目錄（每個任務一個 `<task_id>/` 資料夾）。
2. 在 `cosmos_chimera_forge` 執行驗收腳本：
   `python tools/validate_art.py Art/_incoming/<task_id> --apply`
   即可驗收並搬入遊戲資產庫 `Assets/_Project/Art/`。
3. 此目錄除本檔外一律被 git 忽略，避免未經 Unity 規範整理的檔案直接進版控。
