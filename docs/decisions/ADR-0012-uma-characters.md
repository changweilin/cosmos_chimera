# ADR-0012：主角與捏臉改用 UMA 3

## 狀態
已採納（2026-11-22 定案 Q-T6、Q-T7）。規格見 `docs/design/characters.md#捏臉範圍`。

## 背景
- 三位主角（`chr_roen`、`chr_amemori_sumiha`、`chr_yeh_chengxi`）原為外包 GLB/FBX（67 骨 Unity Humanoid，`Model/rig_spec.json`）。
- 捏臉規格要求在預設外觀上以滑桿調整並存成數值、不存網格；外包單一網格難以支援換裝與捏臉。
- UMA（umasteeringgroup/UMA）提供 DNA 滑桿、wardrobe recipe、共享色與 `UMAPreset`（ScriptableObject，`preset.ApplyTo(avatar)`）。

## 決定
1. 引入 **UMA 3.1f5（套件版本 3.1.5，GitHub release `V3.1.5`，要求 Unity 6.3+）**，以 URP 管線安裝；只裝核心套件，不裝 UMA2Compatibility、OverlayPainter 等外掛。
2. 基底種族用內建 **HumanMale／HumanFemale**（寫實）。
3. 三位主角改為 UMA avatar：從內建 preset 起手，調 DNA、換髮型與服裝 recipe、設共享色，存成專案自有 `UMAPreset`；外包模型被取代。
4. 捏臉滑桿對應 UMA DNA；存檔仍只存數值（DNA 與色值），不存網格。
5. UMA 本體不進 git：`Assets/UMA/` 列入 `.gitignore`，由 `tools/install-uma` 下載固定版本並核對 SHA-256 後匯入；unitypackage 保留 GUID，專案自有 recipe／preset（放 `Assets/_Project/Art/Characters/`）的引用跨機器一致。

## 後果
- 立繪中的專屬服裝與配件（外套、背包、耳機等）UMA 內建素材沒有，需另做 wardrobe recipe 與網格；未做前以最接近的內建服裝代替。
- 寫實基底與卡通渲染的立繪畫風有差距，需靠 DNA 比例與材質縮小。
- 既有動作（`Anim/*.fbx`）需重新定向到 UMA 骨架（Humanoid avatar）。
- 新機器或 CI 需先跑 `tools/install-uma`（下載約 0.95 GB）才能開啟角色資產；升級 UMA 時改腳本中的版本與雜湊。

## 替代方案
- 只用 UMA 做捏臉／NPC、主角沿用外包 GLB：使用者未採用。
- 不引入 UMA、在 GLB 上做 blendshape 捏臉：使用者未採用。
- 另找動漫風 UMA 種族：使用者選內建寫實種族，未採用。
- UMA 本體以 Git LFS 或直接提交進 git：repo 增加 1–2 GB，未採用。
