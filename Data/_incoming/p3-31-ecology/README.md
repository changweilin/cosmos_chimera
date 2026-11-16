# P3-31 生態棲位提案

`proposal.json` 符合 `Data/schemas/ecological-niche.schema.json`，包含 14 種棲位。全為設計提案，未匯入遊戲；目錄、性格權重與數值待 Q-C50 確認。生成順序與兩層標準差已定案，規則唯一來源為 `docs/design/creature-ecology.md`。

- `habitat_kinds`：對應 `LandformSynth.Kinds` 的出生地貌初篩；物種另配候選棲位權重與必要微環境限制，並非每種物種都能抽遍全部棲位。
- `temperament_weights`：鍵即允許集合，值是正整數相對權重。缺少的性格機率為零，多個棲位可共享集合。
- `wander.mode`：來回／環狀／區域隨機／無限制隨機。前兩種的 `radius_mm` 是路線離出生點的最大球面距離，區域隨機則是活動半徑；無限制必為 `null`。
- `speed_permille`：戰鬥基礎移速千分比；`move_ms`／`pause_ms`：移動時間預算／停留時間（毫秒）。各有 `mean`、`individual_sd`、`action_sd`、`min`、`max`；採樣與夾限意義見設計文件。
- 食礦／環境能量採食是幻想延伸；生態名稱不額外啟用飛行、游泳、傳粉或食物鏈模擬。

驗證：`python Data/_incoming/p3-31-ecology/check.py`，再跑 `python tools/validate_data.py`。主驗證器目前未註冊棲位種類，**只跑主驗證器不會驗到本提案**；批次檢查重用其嚴格 JSON 載入與 schema registry，再檢查數值順序、地貌、ID、權重總和及四模式覆蓋。

下一步：確認 Q-C50／Q-C51 → 配現有物種候選 → 一起遷移 schema／importer／生成／Combat／觀察／收服存檔，通過相關 Unity 測試與審查後才移入正式資料。
