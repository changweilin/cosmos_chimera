// COSMIMERA prompt-forge · 任務模板
// 每個模板自帶 version：改產出清單、檔名或 JSON 欄位 → major；只改措辭 → patch（README.md）。
// build(ctx) 只組字串，不碰 DOM，方便日後移到 Node 批次產生。
(function () {
  const C = window.COSMI_CATALOG;

  const STYLE =
    "柔和卡通著色（2–3 階明暗）、乾淨深色描邊、色塊分明、輪廓剪影鮮明、微弱邊緣光；純色或水彩感微紋理背景、主體不與背景融合。必須是原創設計，嚴禁模仿現有商業 IP。";

  // REQ: 使用者 2026-11-07 — 3D 管線的輸入/輸出固定在這兩個雲端硬碟資料夾。
  const DRIVE_IN = "我的雲端硬碟/project/cosmos_chimera/image_in";
  const DRIVE_OUT = "我的雲端硬碟/project/cosmos_chimera/3D_out";

  const CULTURE_RULES =
    "- 文化原型只擷取象徵符號與意象，不直接照搬宗教儀式紋樣，不用神祇/人物本名為角色命名。\n" +
    "- 避免刻板印象與文化嘲弄；若元素有爭議，改用替代意象並於 notes 說明。";

  function json(o) { return "```json\n" + JSON.stringify(o, null, 2) + "\n```"; }
  function list(items) { return items.map(s => "- " + s).join("\n"); }

  function deliverySection(ctx, tree) {
    const isModelRig = ctx.task.id === "model-rig";
    const manifestObj = {
      spec: "cosmimera-art-task/1",
      task_header: "<原樣抄回本 prompt 第一行>",
      task_id: ctx.taskId,
      task_type: ctx.task.id,
      subject_id: ctx.subjectId,
      produced_by: ctx.platformName,
    };
    if (isModelRig) {
      manifestObj.source_pack = ctx.opts?.source_pack || "<來源立繪包 task_id，例 cr-20261106-7f3a-2105 或 chr-20261106-7f3a-2105>";
    }
    Object.assign(manifestObj, {
      files: [{ path: "images/<檔名>.png", kind: "<main|turnaround|view|sheet|spec|script|model|anim|flipbook|storyboard|report>", level: "L1|L2" }],
      missing: [{ item: "<路徑或項目>", level: "L2", reason: "<為何做不到>" }],
      notes: "<設計取捨、文化替代、需要人工確認的點>"
    });

    return [
      "## 交付格式（必須遵守）",
      "把所有檔案打包成 **`" + ctx.taskId + ".zip`**，zip 內頂層是同名資料夾：",
      "```",
      ctx.taskId + "/",
      "├── manifest.json",
      tree,
      "```",
      "manifest.json：",
      json(manifestObj),
      "- **L1 = 必交**；**L2 = 平台能力允許才交**，做不到就列入 `missing`，**不要用佔位或假檔充數**。",
      isModelRig ? "- **source_pack 必須如實填寫來源立繪包 task_id**，供技術美術與審查台 100% 雙向追溯。" : null,
      "- 所有檔名小寫 snake_case，完全照〈產出清單〉。"
    ].filter(Boolean).join("\n");
  }

  function platformSection(ctx) {
    if (ctx.platform === "gemini") {
      return [
        "## 執行方式（Gemini 任務模式）",
        "- 圖像用內建能力逐張產出並依檔名命名。",
        ctx.task.drive
          ? "- 從 Google 雲端硬碟讀取輸入、成品 zip 上傳回雲端硬碟。若無法寫入則提供 zip 下載連結。"
          : "- 全部完成後打包 zip 提供下載（若無法下載則以 code block 與標註檔名提供）。"
      ].join("\n");
    }
    return [
      "## 執行方式（ChatGPT 對話版）",
      "- 圖像用內建生成逐張產出並標註檔名。",
      "- JSON / 文字檔用程式碼執行寫成檔案。",
      "- 完成後依〈交付格式〉打包 zip 下載（若圖片無法進 Python 環境，zip 放文字檔，manifest 照列圖檔名供手動存入）。"
    ].join("\n");
  }

  function common(ctx, body) {
    return [
      ctx.header,
      "",
      "# 任務：" + ctx.task.zh,
      "你是《宙獸紀 COSMIMERA》概念美術設計師。",
      "",
      "## 畫風要求",
      STYLE,
      "",
      "## 原創與文化準則",
      CULTURE_RULES,
      "",
      body,
      "",
      ctx.notes ? "## 使用者補充（優先於預設）\n" + ctx.notes + "\n" : null,
      platformSection(ctx),
      "",
      "## 自我檢查",
      list([
        "manifest.task_header 與本 prompt 第一行一字不差",
        "所有 L1 項目均在 files 中，做不到的列入 missing",
        "無文字浮水印、簽名或現有商業 IP 元素"
      ])
    ].filter(s => s !== null).join("\n");
  }

  function creatureStageLines(cc, stages) {
    const sizes = cc.size_profile.split("→");
    const baseOrder = ["juvenile", "adolescent", "adult", "overload"];
    const descOrder = ["overload", "adult", "adolescent", "juvenile"];
    // 依體型由大到小排序：overload → adult → adolescent → juvenile
    const sortedStages = [...stages].sort((a, b) => {
      const ia = descOrder.indexOf(a);
      const ib = descOrder.indexOf(b);
      return (ia === -1 ? 99 : ia) - (ib === -1 ? 99 : ib);
    });
    return sortedStages.map(s => {
      const i = baseOrder.indexOf(s);
      const st = C.STAGES[i];
      const sz = C.SIZE_CLASSES[sizes[i]];
      return { v: s, zh: st.zh, size: sizes[i], sizeZh: sz.zh, tris: sz.tris, tex: sz.tex };
    });
  }

  function subjectOf(ctx) {
    return ctx.opts.subject === "creature" ? "creature" : "character";
  }

  const TASKS = [
    {
      id: "character-concept", version: "2.2.0", zh: "角色設計概念與立繪", prefix: "chr", platform: "chatgpt",
      needs: "character", drop: "Art/_incoming",
      options: [],
      build(ctx) {
        const S = ctx.subjectId, c = ctx.concept;
        const conceptJsonSchema = {
          id: S,
          name_zh: "角色繁中名（請依命名格式創作）",
          name_en: "CharacterEnglishName (PascalCase)",
          role: c.role,
          gender: c.gender, age: c.age, height_cm: c.height, weight_kg: c.weight, body_fat_pct: c.body_fat,
          ...(c.gender === "女" && c.cup_size && c.cup_size !== "未知" ? { cup_size: c.cup_size } : {}),
          ethnicity: c.ethnicity, region: c.region, timeline: c.timeline,
          outfit_palette: c.outfit_palette, hair_palette: c.hair_palette, eye_palette: c.eye_palette,
          personality: c.personality, style: c.style, vessel: c.vessel?.zh || c.vessel
        };

        const body = [
          "## 角色命名任務與格式規範",
          "請為此角色創作原創姓名（中英文各一，填入 concept.json）：",
          "- 繁中姓名格式：2–4 字，符合該文化地區與人種背景，體現角色個性與時空氛圍（例：凌霄、薇爾菈、墨巡、艾希莉亞、拓跋烈、琉璃）。",
          "- 英文姓名格式：符合羅馬拼音或西洋/科幻命名美感（PascalCase，例：Linxiao, Valera, MoXun, Ashley, Tuobalie）。",
          "",
          "## 輸入概念",
          json(c),
          "",
          "## 產出清單（3 種圖像 + 1 設定檔）",
          "| 檔案 | 內容 | 層級 |",
          "|---|---|---|",
          "| `concept.json` | 角色設定檔（欄位見下） | L1 |",
          "| `images/" + S + "_main.png` | 全身主立繪，3:4，展現性格姿勢與服裝 | L1 |",
          "| `images/" + S + "_front.png` | 單張正面正交灰底 1:1，T-pose 全身置中、平光（供 Image-to-3D 建模使用） | L1 |",
          "| `images/" + S + "_turnaround.png` | 三視圖合圖 16:9（正/側/背/3/4 同比例，T-pose，中性灰底正交、無透視） | L1 |",
          "",
          "### concept.json 欄位",
          json(conceptJsonSchema),
          "",
          "## 繪製要求",
          list([
            "嚴格遵循輸入概念之身高體型比例與配色組合設定（三視圖附身高刻度）。",
            (c.gender === "女" && c.cup_size && c.cup_size !== "未知") ? ("女性體態特徵：罩杯設定為 " + c.cup_size + "，立繪與三視圖保持健康勻稱之卡通比例。") : null,
            "服裝結構適合 3D 建模：避免過多細碎飄帶；T-pose 雙臂張開不被服裝遮擋，腋下與跨下留空。",
            "收服道具（" + (c.vessel?.zh || "道具") + "）在角色身上有固定配戴位置（腰側、背後或右手）。"
          ].filter(Boolean))
        ].join("\n");
        return common(ctx, body) + "\n\n" + deliverySection(ctx, "├── concept.json\n└── images/");
      }
    },

    {
      id: "creature-concept", version: "4.1.0", zh: "宙獸設計概念與型態立繪", prefix: "cr", platform: "chatgpt",
      needs: "creature", drop: "Art/_incoming",
      options: [
        { key: "stage", type: "select", label: "宙獸型態（四型態分開產出）", items: () => C.STAGES.map(s => ({ v: s.v, zh: s.zh + "（" + s.v + "）" })), def: "adult" }
      ],
      build(ctx) {
        const S = ctx.subjectId, c = ctx.concept;
        const curStage = ctx.opts?.stage || "adult";
        const stageDef = C.STAGES.find(s => s.v === curStage) || C.STAGES[2];
        const sizes = (c.size_profile || "S→M→L→XL").split("→");
        const stageIdx = ["juvenile", "adolescent", "adult", "overload"].indexOf(curStage);
        const curSize = sizes[stageIdx >= 0 ? stageIdx : 2] || "L";
        const szInfo = C.SIZE_CLASSES[curSize] || C.SIZE_CLASSES["L"];

        // 鎖定資訊（若為已設計項目）
        const lockedStage = c.locked?.stages?.find(s => s.stage === curStage);
        const lockedTrait = c.locked?.trait;

        // 當前型態尺寸數據
        const curHeight = lockedStage?.height_length || (stageIdx === 2 ? c.height_length : null) || (curSize === "XS" ? "0.20 m" : curSize === "S" ? "0.45 m" : curSize === "M" ? "1.20 m" : curSize === "L" ? "2.20 m" : "5.0 m");
        const curWeight = lockedStage?.weight || (stageIdx === 2 ? c.weight : null) || (curSize === "XS" ? "0.15 kg" : curSize === "S" ? "3.5 kg" : curSize === "M" ? "35 kg" : curSize === "L" ? "220 kg" : "2500 kg");
        const curFat = lockedStage?.body_fat || (stageIdx === 2 ? c.body_fat : null) || "15%";

        const namingSection = lockedStage ? [
          "### 命名規範（已設計定案）",
          "- 繁中名：**" + lockedStage.name_zh + "** · 英文名：**" + lockedStage.name_en + "** · 宙獸 ID：`" + lockedStage.creature_id + "`",
          "- （已有名稱者原樣照抄，嚴禁任意更動）。"
        ] : [
          "### 宙獸命名任務與格式規範（請 AI 原創命名）",
          "請為此宙獸（本型態）創作原創圖鑑名稱（中英文各一，填入 concept.json）：",
          "- 繁中名稱格式：",
          "  - 長度：2–4 個繁體中文字。",
          "  - 格式公式：『特徵/屬性字 + 生物/外觀意象字』（例：幼年『鏡芽雀』、少年『鏡羽鷺』、成年『鏡環天鷺』、超載『鏡冕天鷺』；或『雷鬃駒』、『夜痕霜狼』、『熾晶狐』、『岩甲巨犀』）。",
          "  - 語感：具備奇幻生物圖鑑感與自然神話韻味，嚴禁使用人類姓名、真實神祇全名或商業 IP 名稱。",
          "- 英文名稱格式：",
          "  - 1–2 個詞根拼接之原創奇幻生物名（PascalCase，例：Mirrill, Mirravel, Voltrastorm, Nivervast, Pyraclaw）。"
        ];

        const body = [
          "## 本次目標型態：" + stageDef.zh + "（" + curStage + "）",
          "- 體型級距：" + curSize + "（" + szInfo.zh + "） · 面數預算約 " + szInfo.tris + " tris",
          "- 當前型態尺寸估算：身長/高約 " + curHeight + "、體重約 " + curWeight + "、體脂率約 " + curFat,
          "",
          "## 文化原型轉化指引（避免人形化）",
          "- 本任務目標為【宙獸（奇幻生物/魔獸/機獸）】，嚴禁生成為人形角色（除非外觀基底明確指定類人種族）。",
          "- 文化原型（如神祇、傳說英雄、歷史人物）僅提取其代表性的【象徵圖騰、法寶神兵、自然天象、聖獸化身、精神意義與意象元素】，必須將其完全轉化為動物、幻獸或構裝體的生理構造、羽翼、角鱗、光環或外甲特徵。",
          "",
          ...namingSection,
          "",
          ...(lockedStage ? [
            "### 已設計定案規格（嚴格照抄）",
            "- 宙獸 ID：`" + lockedStage.creature_id + "` · 繁中名：**" + lockedStage.name_zh + "** · 英文名：**" + lockedStage.name_en + "**",
            "- 外觀設定：" + lockedStage.look,
            "- 圖鑑描述：" + lockedStage.dex_zh,
            "- 專屬招式：`" + lockedStage.signature_move?.move_id + "`（" + lockedStage.signature_move?.name_zh + "）— " + lockedStage.signature_move?.visual,
            lockedTrait ? "- 專屬特性：`" + lockedTrait.trait_id + "`（" + lockedTrait.name_zh + "）— " + lockedTrait.idea_zh : null,
            ""
          ].filter(Boolean) : []),
          "### 概念設定（完整規格）",
          "- 屬性：" + (c.elements || []).join("、"),
          "- 配色組合：" + (c.appearance_palette || []).join("、"),
          "- 外觀基底：" + (c.base_parents ? c.base_parents.join(" + ") + " → " : "") + (c.bases ? c.bases.join(" + ") : ""),
          "- 文化原型與時空：" + (c.archetype || "自由原型") + "（" + (c.region || "未知") + "）· " + (c.timeline || "架空"),
          c.motifs ? "- 核心文化象徵與意象：" + c.motifs + (c.meaning ? " · 精神象徵：" + c.meaning : "") : null,
          "- 戰鬥定位與性情：" + (c.role || "戰鬥") + " · " + (c.temperament || "沉穩"),
          "",
          "## 型態特徵指引",
          curStage === "juvenile" ? "- 幼年型態：體型圓潤小巧（" + curSize + " 級），特徵幼態可愛，保留 1–2 個核心意象特徵，動作靈動。" :
          curStage === "adolescent" ? "- 少年型態：介於幼年與成年之間（" + curSize + " 級），初顯原型輪廓，活力與敏捷感並重。" :
          curStage === "adult" ? "- 成年型態：完全展現文化原型之威嚴與主體構造（" + curSize + " 級），比例成熟勻稱，特徵完備。" :
          "- 超載型態：戰鬥中能量極致爆發之態（" + curSize + " 級），裝甲展開、光環浮現、能量外溢或粒子流，輪廓更具威嚴與壓迫感，但保留核心種族識別。",
          "",
          "## 產出清單（3 種圖像 + 1 設定檔，單獨打包）",
          "| 檔案 | 內容 | 層級 |",
          "|---|---|---|",
          "| `concept.json` | 本型態設定檔（欄位見下） | L1 |",
          "| `images/cr_" + S + "_" + curStage + "_main.png` | " + stageDef.zh + "全身主立繪 1:1，展現生動姿態與屬性氛圍 | L1 |",
          "| `images/cr_" + S + "_" + curStage + "_front.png` | " + stageDef.zh + "正面正交 1:1，中性灰 #808080 底、平光、全身置中（供 Image-to-3D 建模使用） | L1 |",
          "| `images/cr_" + S + "_" + curStage + "_turnaround.png` | " + stageDef.zh + "三視圖合圖 16:9（正/側/背/3/4，自然站姿，中性灰底正交、無透視） | L1 |",
          "",
          "### concept.json 欄位",
          json({
            line_id: S,
            stage: curStage,
            creature_id: lockedStage ? lockedStage.creature_id : ("cr_" + S + "_" + curStage),
            name_zh: lockedStage ? lockedStage.name_zh : "宙獸繁中名（請依命名格式創作）",
            name_en: lockedStage ? lockedStage.name_en : "CreatureEnglishName (PascalCase)",
            size_class: curSize,
            height_length: curHeight,
            weight: curWeight,
            body_fat: curFat,
            elements: c.elements,
            appearance_palette: c.appearance_palette,
            base_parents: c.base_parents,
            bases: c.bases,
            region: c.region,
            timeline: c.timeline,
            archetype: c.archetype,
            role: c.role,
            temperament: c.temperament,
            signature_move: lockedStage?.signature_move || {
              move_id: "snake_case_move_id",
              name_zh: "招式繁中名（請命名）",
              name_en: "MoveEnglishName (PascalCase)",
              element: (c.elements || ["light"])[0],
              range_class: "melee|mid|long",
              shape: "front|line|sweep|burst|cone|arc",
              visual: "招式演出視覺效果描述"
            },
            ...(curStage === "overload" ? { overload_visual: lockedStage?.look || "超載能量外溢特徵" } : {})
          })
        ].filter(Boolean).join("\n");
        return common(ctx, body) + "\n\n" + deliverySection(ctx, "├── concept.json\n└── images/");
      }
    },

    {
      // REQ: 使用者 2026-11-07 — image-to-3D → Blender → 綁骨 → 動作在同一管線；輸入/輸出走 Google 雲端硬碟；可批次；宙獸四型態分開處理。
      // 取代 animation-set（已退役）。招式不在此管線：招式是獨立圖鑑條目，由 move-vfx 處理。
      id: "model-rig", version: "3.2.0", zh: "Image-to-3D 建模、綁骨與動作（雲端硬碟立繪包）", prefix: "mdl", platform: "gemini",
      needs: "either", drop: "Art/_incoming", drive: true,
      options: [
        { key: "scope", type: "select", label: "範圍", items: () => [{ v: "single", zh: "單一立繪包（目前概念）" }, { v: "batch", zh: "批次：image_in 內所有立繪包" }], def: "single" },
        { key: "source_pack", type: "text", label: "參照立繪包 ID (source_pack)", def: "", placeholder: "例：cr-20261106-7f3a-2105-adult 或 chr-20261106-7f3a-2105", when: o => o.scope !== "batch" },
        { key: "input_img", type: "text", label: "指定輸入 img", def: "", placeholder: "選填，留空預設使用正面正交 + turnaround 三視圖", when: o => o.scope !== "batch" },
        { key: "subject", type: "select", label: "對象", items: () => [{ v: "character", zh: "目前角色概念" }, { v: "creature", zh: "目前宙獸概念" }], def: "creature", when: o => o.scope !== "batch" },
        { key: "stage", type: "select", label: "宙獸型態（分開處理）", items: () => C.STAGES.map(s => ({ v: s.v, zh: s.zh + "（" + s.v + "）" })), def: "adult", when: o => o.scope !== "batch" && o.subject === "creature" },
        { key: "actions_character", type: "checks", label: "角色動作", items: () => C.ACTIONS.character.map(a => ({ v: a.id, zh: a.zh })), def: "all", when: o => o.scope === "batch" || o.subject !== "creature" },
        { key: "actions_creature", type: "checks", label: "宙獸動作", items: () => C.ACTIONS.creature.map(a => ({ v: a.id, zh: a.zh })), def: "all", when: o => o.scope === "batch" || o.subject === "creature" }
      ],
      build(ctx) {
        const o = ctx.opts, batch = o.scope === "batch";
        const kinds = batch ? ["character", "creature"] : [subjectOf(ctx)];
        const S = batch ? "<subject_id>" : ctx.subjectId;
        const curStage = o.stage || "adult";
        const stageDef = C.STAGES.find(s => s.v === curStage) || C.STAGES[2];
        const outId = batch ? ctx.taskId + "-<subject-id>" : ctx.taskId;

        const clipTable = kind => {
          const isCr = kind === "creature";
          const picked = isCr ? o.actions_creature : o.actions_character;
          return C.ACTIONS[kind].filter(a => picked.includes(a.id)).map(a =>
            "| `" + (isCr ? "cr_" + curStage + "_" : "chr_") + a.id + "` | " + a.zh + " | " + a.f + " | " + (a.loop ? "✓" : "") + " | " + (a.rm ? "✓" : "") + " | " + (a.ev || []).join(", ") + " |"
          ).join("\n");
        };
        const rigRule = {
          creature: "Generic 骨架：`Root`（地面，pivot）→ `Hips` → `Spine_01..n` → `Neck` → `Head`；四肢 `L_`/`R_` 前綴；尾巴 `Tail_01..n`、翅膀 `Wing_L_01..n`；插槽骨 `Socket_Mouth`、`Socket_Head`、`Socket_Core_FX`、`Socket_Front`、`Socket_PetTouch`、`Socket_Ground_FX`。骨骼 ≤ 60。",
          character: "Unity Humanoid 相容命名：Hips, Spine, Chest, UpperChest, Neck, Head, Left/RightShoulder, Left/RightUpperArm, Left/RightLowerArm, Left/RightHand, 手指三節（Thumb/Index/Middle/Ring/Little）, Left/RightUpperLeg, Left/RightLowerLeg, Left/RightFoot, Left/RightToes；額外骨（頭髮/披風）用 `Extra_` 前綴；插槽骨 `Socket_HandR_Prop`、`Socket_HandL_Prop`、`Socket_Back_Pack`、`Socket_Throw`、`Socket_Head_FX`。骨骼 ≤ 75（含手指）。"
        };
        const sizeTable = Object.keys(C.SIZE_CLASSES).map(k => "| " + k + " | " + C.SIZE_CLASSES[k].tris + " | " + C.SIZE_CLASSES[k].tex + "² |").join("\n");

        const kindBlocks = kinds.map(kind => {
          const isCr = kind === "creature";
          const unit = isCr ? "cr_" + S + "_" + curStage : S;
          let src = isCr
            ? "`images/cr_" + S + "_" + curStage + "_front.png`（單張正面正交灰底）+ `_turnaround.png`（側/背/3/4 從三視圖裁切）"
            : "`images/" + S + "_front.png`（單張正面正交灰底）+ `_turnaround.png`（側/背/3/4 從三視圖裁切）";
          if (o.input_img) {
            src = "`" + o.input_img + "`（審查台指定輸入立繪，其餘視圖由三視圖裁切補齊）";
          }
          return [
            "### " + (isCr ? "宙獸型態：" + stageDef.zh + "（`cr-*-" + curStage + "`）" : "角色（立繪包 task_type = character-concept，`chr-` 開頭）"),
            "- image-to-3D 輸入圖：" + src + "。",
            isCr
              ? "- 目標型態：" + stageDef.zh + "（" + curStage + "），獨立模型、一套骨架與動作。面數與貼圖依該型態 size_class 查上表。"
              : "- 面數 15–25k tris，貼圖 2048²。",
            "- 骨架：" + rigRule[kind],
            "- 動作（30 fps；loop＝循環；RM＝root motion）：",
            "",
            "| clip | 說明 | 幀數 | loop | RM | 事件@幀 |",
            "|---|---|---|---|---|---|",
            clipTable(kind),
            "",
            "產出（`<unit>` = `" + unit + "`）：",
            "",
            "| 檔案 | 內容 | 層級 |",
            "|---|---|---|",
            "| `images/<unit>_model_front.png`、`_model_side.png`、`_model_back.png` | 建模參考圖：平光、無描邊、無陰影、灰底 1:1、全身置中 2048px | L1 |",
            "| `images/<unit>_anim_sheet_01.png` | 關鍵姿勢分鏡，每動作 3–5 格 | L1 |",
            "| `models/<unit>.glb`、`models/<unit>.fbx` | image-to-3D 產生、減面 UV 烘焙並綁骨之模型 | L2 |",
            "| `anims/" + (isCr ? "cr_" + curStage + "_<clip>" : "chr_<clip>") + ".fbx` | 完成動畫，每 clip 一檔 | L2 |"
          ].join("\n");
        }).join("\n\n");

        const inputSection = batch
          ? [
            "## 輸入：Google 雲端硬碟立繪包（批次）",
            list([
              "資料夾：`" + DRIVE_IN + "/`。處理其中所有 `chr-*.zip` 與 `cr-*.zip`。",
              "跳過已完成的：`" + DRIVE_OUT + "/` 已有任何 `mdl-*.zip` 的 manifest.source_pack 等於該立繪包 task_id。",
              "逐包獨立處理：每包各打包獨立 zip。"
            ])
          ].join("\n")
          : [
            "## 輸入：Google 雲端硬碟立繪包",
            list([
              o.source_pack
                ? "資料夾：`" + DRIVE_IN + "/`。指定來源立繪包：`" + o.source_pack + ".zip`（manifest.task_id = `" + o.source_pack + "`）。"
                : "資料夾：`" + DRIVE_IN + "/`。找 manifest.subject_id = `" + S + "` 的立繪包。",
              o.input_img ? "指定 image-to-3D 輸入圖：`" + o.input_img + "`。" : null,
              "找不到就停下來告訴我，不要改用其他主體或自行生成立繪。"
            ].filter(Boolean))
          ].join("\n");

        const body = [
          inputSection,
          "",
          "## 規格與產出",
          kindBlocks,
          "",
          "### 共同規格腳本（L1）",
          "| 檔案 | 內容 | 層級 |",
          "|---|---|---|",
          "| `rig/rig_spec.json` | 骨骼清單：name、parent、head/tail 位置、插槽骨 | L1 |",
          "| `rig/build_rig.py` | Blender 腳本：匯入同名 mesh → 清理/減面 → 依 rig_spec 建 armature、權重、插槽骨 → 匯出 FBX/GLB | L1 |",
          "| `anims/anim_spec.json` | 動作 clip 規格與 key_poses | L1 |",
          "| `anims/keyframes.py` | Blender 腳本：依 anim_spec 建 Actions 並匯出 FBX | L1 |",
          "| `pipeline/run_all.py` | 一鍵串接腳本 | L1 |",
          "| `pipeline/README.md` | 執行步驟與參數說明 | L1 |",
          "",
          "## 交付格式",
          list([
            "單獨打包成 `" + outId + ".zip` 上傳至 `" + DRIVE_OUT + "/`。",
            "manifest.json 填入 `source_pack: \"" + (o.source_pack || "<來源立繪包 task_id>") + "\"`。"
          ])
        ].join("\n");
        const tree = "├── images/\n├── rig/\n├── anims/\n├── pipeline/\n└── models/      # L2";
        return common(ctx, body) + "\n\n" + deliverySection(Object.assign({}, ctx, { taskId: outId, subjectId: S }), tree);
      }
    },

    {
      id: "move-vfx", version: "1.1.0", zh: "招式效果動畫（特效）", prefix: "fx", platform: "chatgpt",
      needs: null, drop: "Art/_incoming",
      options: [
        { key: "use_creature", type: "select", label: "示範施放者", items: () => [{ v: "yes", zh: "以目前宙獸概念示範" }, { v: "no", zh: "不指定（通用示範）" }], def: "yes" },
        // 有示範施放者時只允許它自己的屬性，否則示範畫面的招式配色會跟施放者對不上
        { key: "element", type: "select", label: "屬性", items: (o, env) => C.ELEMENTS.filter(e => o.use_creature !== "yes" || (env.creature.elements || []).includes(e.v)).map(e => ({ v: e.v, zh: e.zh + " " + e.v })), def: "rand" },
        { key: "shape", type: "select", label: "形狀", items: () => C.VFX.shapes, def: "rand" },
        { key: "tier", type: "select", label: "強度", items: () => C.VFX.tiers, def: "rand" },
        { key: "move_name", type: "text", label: "招式名稱（可留空讓 AI 命名）", def: "" },
        { key: "move_id", type: "text", label: "招式圖鑑 id（snake_case，可留空讓 AI 命名）", def: "" },
        { key: "common", type: "checks", label: "附帶通用特效", items: () => C.VFX.common, def: [], optional: true }
      ],
      build(ctx) {
        const o = ctx.opts, el = C.ELEMENTS.find(e => e.v === o.element);
        const shape = C.VFX.shapes.find(s => s.v === o.shape), tier = C.VFX.tiers.find(t => t.v === o.tier);
        const base = "fx_" + o.element + "_" + o.shape + "_" + o.tier;
        const commons = C.VFX.common.filter(x => o.common.includes(x.v));
        const body = [
          "## 招式",
          json({
            move_name: o.move_name || "（請命名，中英各一）", element: o.element + "（" + el.zh + "）", palette: el.palette,
            shape: shape.v + "（" + shape.zh + "）", tier: tier.v + "（" + tier.zh + "）",
            move_id: o.move_id || "（請取 snake_case 通用名稱，不含任何宙獸名）",
            demo_caster: o.use_creature === "yes" ? ctx.concept : "不指定，用中性體型示範"
          }),
          // REQ: 使用者 2026-11-07 — 招式圖鑑獨立於宙獸圖鑑，多隻宙獸可共用同一招式。
          "招式是**獨立圖鑑條目**，任何宙獸都可能學會：demo_caster 只用於分鏡示範，特效不得依賴它的特定身體部位或外觀。",
          "",
          "## 產出清單",
          "| 檔案 | 內容 | 層級 |",
          "|---|---|---|",
          "| `vfx/" + base + "_spec.json` | 特效規格（欄位見下） | L1 |",
          "| `images/" + base + "_storyboard.png` | 分鏡 6 格：預備 → 施放 → 飛行/展開 → 命中 → 餘韻 → 消散，標註毫秒 | L1 |",
          "| `images/" + base + "_layers.png` | 圖層拆解：核心、外光、粒子、拖尾、地面印記、衝擊波各自獨立示意 | L1 |",
          "| `vfx/" + base + "_flipbook.png` | 序列圖 8×8 格、2048²（每格 256²），純黑底（additive 用），單一主要元素循環 | L2 |",
          commons.map(x => "| `vfx/fx_common_" + x.v + "_spec.json` + `images/fx_common_" + x.v + "_storyboard.png` | 通用特效：" + x.zh + " | L1 |").join("\n"),
          "",
          "### spec.json 欄位",
          json({
            id: base, move_id: o.move_id || "<snake_case>", phases: [{ name: "anticipation|cast|travel|impact|linger|fade", start_ms: 0, end_ms: 0, description_zh: "" }],
            colors_hex: { core: "#", glow: "#", accent: "#" },
            geometry: { range_class: "melee|mid|long", shape: shape.v, length_m: 0, width_m: 0, angle_deg: 0, radius_m: 0, arc_height_m: 0, note: "尺寸以 base_range 為 1.0 的比例另列 scale_by_range" },
            layers: [{ name: "", type: "mesh|particles|trail|decal|light|distortion", count: 0, lifetime_ms: 0, size_m: 0, speed_mps: 0, texture: "", blend: "additive|alpha" }],
            sockets: ["Socket_Front"], hit_stop_ms: 0, camera_shake: { amplitude: 0, duration_ms: 0 },
            sound_cues_zh: [""], deck_budget: { max_particles: 0, note: "Steam Deck 版粒子數約為 PC 的 50%" },
            unity_notes: "URP VFX Graph 實作建議；卡通風：硬邊漸層、2–3 色階、避免寫實煙霧"
          }),
          "",
          "## 規格",
          list([
            "形狀必須清楚表達判定範圍（玩家要看得出會不會被打到）；地面印記表示範圍邊界。",
            "強度越高：預備時間越長、畫面占比越大；超載必殺可加短暫的全螢幕色調變化（≤ 400 ms）。",
            "同屬性的特效共用配色語言：" + el.palette + "。",
            "特效從宙獸插槽骨發出（Socket_Front / Socket_Core_FX / Socket_Ground_FX），尺寸隨施放者體型（S/M/L/XL）縮放，在 spec 的 geometry.note 寫明縮放規則。"
          ])
        ].join("\n");
        return common(ctx, body) + "\n\n" + deliverySection(ctx, "├── images/\n└── vfx/");
      }
    },

    {
      id: "capture-vessel", version: "1.0.0", zh: "收服道具設計", prefix: "vsl", platform: "chatgpt",
      needs: null, drop: "Art/_incoming",
      options: [
        { key: "vessels", type: "checks", label: "道具類別", items: () => C.VESSELS.map(v => ({ v: v.v, zh: v.zh + "（副手提供" + v.sub + "）" })), def: ["talisman_card", "glass_bead", "magic_cube"] },
        { key: "variants", type: "select", label: "外觀變體數", items: () => [1, 3, 5].map(n => ({ v: String(n), zh: n + " 種" })), def: "3" }
      ],
      build(ctx) {
        const vs = C.VESSELS.filter(v => ctx.opts.vessels.includes(v.v));
        const rows = vs.map(v => [
          "| `images/vsl_" + v.v + "_concept.png` | " + v.zh + "：收合 / 展開 / 收服中 三態 | L1 |",
          "| `images/vsl_" + v.v + "_variants.png` | " + ctx.opts.variants + " 種外觀變體並排（同類別、可辨識） | L1 |",
          "| `images/vsl_" + v.v + "_icon.png` | 512² 圖示，簡潔剪影、深色底 | L1 |",
          "| `images/vsl_" + v.v + "_model_front.png`、`_model_side.png` | 建模參考，平光灰底 | L1 |",
          "| `images/vsl_" + v.v + "_capture_storyboard.png` | 收服分鏡 8 格：投擲 → 飛行 → 命中 → 展開 → 吸入 → 掙扎 1–3 次 → 成功 / 失敗 | L1 |",
          "| `vfx/vsl_" + v.v + "_capture_spec.json` | 收服特效規格（同招式 spec 欄位） | L1 |"
        ].join("\n"));
        const body = [
          "## 遊戲規則背景",
          "玩家投擲收服道具收服宙獸。三個類別作為主力時無差別；作為副手時，提供給主力的內容由類別決定：",
          list(C.VESSELS.map(v => v.zh + " → " + v.sub)),
          "因此三類道具的外觀語言要**一眼可分**，並暗示它的副手效果。",
          "",
          "## 設計方向",
          json(vs.map(v => ({ id: v.v, name: v.zh, gives_sub: v.sub, look: v.look }))),
          "",
          "## 產出清單",
          "| 檔案 | 內容 | 層級 |",
          "|---|---|---|",
          "| `concept.json` | 每類：名稱中英、材質、配色 hex、變體清單、副手效果的視覺暗示、投擲手感描述 | L1 |",
          rows.join("\n"),
          "| `models/vsl_<id>.glb` | 道具模型 ≤ 1.5k tris，開合以 2–4 根骨骼驅動 | L2 |",
          "",
          "## 規格",
          list([
            "握持尺寸：卡片約 6×9 cm、珠子直徑約 5 cm、方塊邊長約 6 cm；三者在角色手中都要清楚可見。",
            "收服特效需呼應道具本身（符文封印陣 / 光泡包覆 / 方塊牢籠展開）。",
            "對應角色投擲動作：throw_card（雙指甩出）、throw_bead（下手拋擲）、throw_cube（轉動後上手投）。"
          ])
        ].join("\n");
        return common(ctx, body) + "\n\n" + deliverySection(ctx, "├── concept.json\n├── images/\n├── vfx/\n└── models/      # L2");
      }
    },

    {
      id: "culture-research", version: "1.0.0", zh: "文化原型考據", prefix: "res", platform: "gemini",
      needs: null, drop: "Data/_incoming",
      options: [
        { key: "region", type: "select", label: "地區", items: () => C.RESEARCH_REGIONS.map(r => ({ v: r, zh: r })), def: "rand" },
        { key: "count", type: "select", label: "原型數量", items: () => [15, 20, 30].map(n => ({ v: String(n), zh: n + " 個" })), def: "20" }
      ],
      build(ctx) {
        const o = ctx.opts;
        const existing = C.CREATURE.archetypes.filter(a => a.region === o.region).map(a => a.name);
        const body = [
          "## 目標",
          "為宙獸設計建立「" + o.region + "」地區的文化原型考據庫，共 " + o.count + " 個（神祇、英雄、歷史名人、傳說生物、文學人物混合，歷史名人與傳說至少各 1/4）。",
          "已收錄、不要重複：" + (existing.join("、") || "（無）"),
          "",
          "## 產出清單",
          "| 檔案 | 內容 | 層級 |",
          "|---|---|---|",
          "| `archetypes.json` | 陣列，欄位見下 | L1 |",
          "| `report.md` | 地區概述、取材分布、敏感度判斷理由、建議排除清單 | L1 |",
          "",
          "### archetypes.json 每筆",
          json({
            id: "snake_case 英文", name_local: "", name_zh: "", region: o.region, culture: "",
            type: "神祇|英雄|歷史名人|傳說|文學", motifs: ["象徵物/配色/姿態"], colors_hex: ["#"],
            story_summary_zh: "≤80 字", suitable_elements: ["metal|wood|water|fire|earth|ice|wind|thunder|light|shadow"],
            suitable_bases: ["生物|人形|器械|載具"], sensitivity: "ok|review", sensitivity_reason: "",
            sources: [{ title: "", url: "" }]
          }),
          "",
          "## 規則",
          list([
            "仍在信仰中的宗教主神、族群神聖敘事、近代爭議人物 → `sensitivity: review` 並說明理由。",
            "每筆至少 2 個可查證來源（學術、博物館、百科），附 URL；查不到可靠來源就不要收錄。",
            "motifs 要具體到可畫出來（物件、顏色、動作），不要抽象形容詞。"
          ])
        ].join("\n");
        return common(ctx, body) + "\n\n" + deliverySection(ctx, "├── archetypes.json\n└── report.md");
      }
    }
  ];

  window.COSMI_TASKS = TASKS;
})();
