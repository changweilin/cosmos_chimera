// COSMIMERA prompt-forge · 任務模板
// 每個模板自帶 version：改產出清單、檔名或 JSON 欄位 → major；只改措辭 → patch（README.md）。
// build(ctx) 只組字串，不碰 DOM，方便日後移到 Node 批次產生。
(function () {
  const C = window.COSMI_CATALOG;

  const STYLE =
    "柔和卡通著色（2–3 階明暗）、溫和邊緣光、乾淨深色描邊、色塊清楚、輪廓剪影一眼可辨；背景水彩感、不描邊。" +
    "必須是原創設計：不得模仿任何現有遊戲/動畫的角色、畫風標誌、名稱或 logo。";

  const CULTURE_RULES =
    "- 文化原型只取**意象**（象徵物、配色、姿態、傳說情節），不使用神祇/人物真名作為命名。\n" +
    "- 不做嘲諷、貶低或刻板符號堆疊的族群呈現；神聖紋樣與儀式服飾避免直接照搬。\n" +
    "- 若你判斷某元素可能冒犯在世信仰或族群，改用替代意象，並寫入 manifest.notes。";

  function json(o) { return "```json\n" + JSON.stringify(o, null, 2) + "\n```"; }
  function list(items) { return items.map(s => "- " + s).join("\n"); }

  function deliverySection(ctx, tree) {
    return [
      "## 交付格式（必須遵守）",
      "把所有檔案打包成 **`" + ctx.taskId + ".zip`**，zip 內頂層是同名資料夾：",
      "```",
      ctx.taskId + "/",
      "├── manifest.json",
      tree,
      "```",
      "manifest.json：",
      json({
        spec: "cosmimera-art-task/1",
        task_header: "<原樣抄回本 prompt 第一行>",
        task_id: ctx.taskId,
        task_type: ctx.task.id,
        subject_id: ctx.subjectId,
        produced_by: ctx.platformName,
        files: [{ path: "images/<檔名>.png", kind: "<main|turnaround|view|sheet|spec|script|model|anim|flipbook|storyboard|report>", level: "L1|L2" }],
        missing: [{ item: "<路徑或項目>", level: "L2", reason: "<為何做不到>" }],
        notes: "<設計取捨、文化替代、需要人工確認的點>"
      }),
      "- **L1 = 必交**；**L2 = 平台能力允許才交**，做不到就列入 `missing`，**不要用佔位或假檔充數**。",
      "- 所有檔名小寫 snake_case，完全照〈產出清單〉。"
    ].join("\n");
  }

  function platformSection(ctx) {
    if (ctx.platform === "gemini") {
      return [
        "## 執行方式（Gemini 任務模式）",
        "- 自行規劃並逐步執行全部項目：先列出步驟清單，然後直接執行，不需要等我確認。",
        "- 圖像用你的圖像生成能力逐張產出，依檔名命名。",
        "- 全部完成後打包 zip 提供下載。若無法提供檔案下載：每個文字檔用一個 code block 輸出（block 上方標註完整路徑），圖片逐張附上並標註檔名，我會手動存放。",
        "- 每完成一個項目更新進度表（✅/⬜）。"
      ].join("\n");
    }
    return [
      "## 執行方式（ChatGPT 對話版）",
      "- 圖像用內建圖像生成，**一張圖一次生成**，生成後標註它的檔名。",
      "- JSON / Python / Markdown 用程式碼執行（Python）寫成檔案。",
      "- 全部完成後用 Python 依〈交付格式〉打包 zip 並給我下載連結。若生成的圖片無法進入 Python 環境：zip 只放文字檔，manifest.files 照樣列出每張圖的檔名，我會手動用該檔名下載到 images/。",
      "- 一次回覆做不完就先輸出進度表（✅/⬜）並繼續下一批，不要停下來問「是否繼續」。"
    ].join("\n");
  }

  function common(ctx, body) {
    return [
      ctx.header,
      "",
      "# 任務：" + ctx.task.zh + "（遊戲《宙獸紀 COSMIMERA》）",
      "你是這款遊戲的資深概念美術兼技術美術。遊戲：開放世界宙獸收集動作 RPG，玩家在每場隨機生成的小型球形星球上探索，召喚「宙獸」戰鬥（玩家本身不直接攻擊）。目標平台 PC 與 Steam Deck，引擎 Unity 6 URP。",
      "",
      "## 畫風",
      STYLE,
      "",
      "## 文化與原創準則",
      CULTURE_RULES,
      "",
      body,
      "",
      ctx.notes ? "## 使用者補充 / 修改（優先於上方預設）\n" + ctx.notes + "\n" : null,
      platformSection(ctx),
      "",
      "## 何時停下來問我",
      "只有這些情況才停：輸入互相矛盾、需要使用真實人物/神祇名、或某項規格無法理解。其他細節由你做設計決定並寫進 manifest.notes。",
      "",
      "## 完成前自我檢查",
      list([
        "manifest.task_header 與本 prompt 第一行一字不差",
        "每個 L1 項目都在 files 中；L2 做不到的都在 missing",
        "同一主體在所有圖中的配色、比例、特徵一致",
        "沒有出現文字浮水印、簽名、UI 框或現有作品的元素"
      ])
    ].filter(s => s !== null).join("\n");
  }

  function creatureStageLines(cc, stages) {
    const sizes = cc.size_profile.split("→");
    const order = ["juvenile", "adolescent", "adult", "overload"];
    return stages.map(s => {
      const i = order.indexOf(s);
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
      id: "character-concept", version: "1.2.0", zh: "角色設計概念與立繪", prefix: "chr", platform: "chatgpt",
      needs: "character", drop: "Art/_incoming",
      options: [],
      build(ctx) {
        const S = ctx.subjectId, c = ctx.concept;
        const conceptJsonSchema = {
          id: S, name_zh: "", name_en: "", role: c.role, ethnicity: c.ethnicity, region: c.region, timeline: c.timeline,
          gender: c.gender, age: c.age, height_cm: c.height, weight_kg: c.weight, body_fat_pct: c.body_fat
        };
        if (c.gender === "女" && c.cup_size !== undefined) {
          conceptJsonSchema.cup_size = c.cup_size;
        }
        Object.assign(conceptJsonSchema, {
          outfit_palette: c.outfit_palette, hair_palette: c.hair_palette, eye_palette: c.eye_palette,
          personality: c.personality, style: c.style, interests: c.interests, likes: c.likes, dislikes: c.dislikes,
          vessel: c.vessel, palette_hex: ["#", "#", "#", "#", "#"], silhouette_keywords: [""],
          outfit_breakdown: [{ part: "", material: "", color_hex: "" }],
          backstory_zh: "≤120 字", voice_lines_zh: ["", "", ""], sensitivity: "ok|review", sensitivity_note: ""
        });

        const body = [
          "## 輸入概念（已由企劃決定，可在不違背的前提下補完細節）",
          json(c),
          "",
          "## 產出清單",
          "| 檔案 | 內容 | 層級 |",
          "|---|---|---|",
          "| `concept.json` | 補完的角色設定（欄位見下） | L1 |",
          "| `images/" + S + "_main.png` | 全身立繪，3:4，展現性格的姿勢，簡單背景 | L1 |",
          "| `images/" + S + "_turnaround.png` | 三視圖合圖 16:9：正面/側面/背面/3/4 同比例同基線，**T-pose**，中性灰 #808080 底，正交、無透視、無投影 | L1 |",
          "| `images/" + S + "_front.png`、`_side.png`、`_back.png` | 單張正交視圖 1:1（給 image-to-3D 使用），T-pose、全身置中、灰底、平光 | L1 |",
          "| `images/" + S + "_expressions.png` | 表情集 2×3：平常、開心、生氣、驚訝、難過、專注 | L1 |",
          "| `images/" + S + "_props.png` | 道具板：背包、慣用收服道具（" + c.vessel_zh + "）、配件，各附尺寸感 | L1 |",
          "",
          "### concept.json 欄位",
          json(conceptJsonSchema),
          "",
          "## 規格",
          list([
            "身高、體重、體脂率與服裝/髮/瞳配色請嚴格遵循輸入概念之比例及配色組合設定（三視圖附身高刻度）。",
            (c.gender === "女" && c.cup_size && c.cup_size !== "未知") ? ("女性體態特徵：罩杯設定為 " + c.cup_size + "，立繪與三視圖請保持健康勻稱之卡通風格比例。") : null,
            "服裝要能做成 15–25k 三角面的遊戲模型：避免過多飄帶、半透明薄紗與細碎飾品；長髮/披風需可用少量骨骼驅動。",
            "雙手在 T-pose 時不可被服裝遮擋，腋下與跨下留出空間（方便自動綁骨）。",
            "背包與收服道具在模型上有固定掛點（背後、腰側、右手）。"
          ].filter(Boolean))
        ].join("\n");
        return common(ctx, body) + "\n\n" + deliverySection(ctx, "├── concept.json\n└── images/");
      }
    },

    {
      id: "creature-concept", version: "1.2.0", zh: "宙獸設計概念與四型態立繪", prefix: "cr", platform: "chatgpt",
      needs: "creature", drop: "Art/_incoming",
      options: [
        { key: "stages", type: "checks", label: "型態", items: () => C.STAGES.map(s => ({ v: s.v, zh: s.zh })), def: ["juvenile", "adolescent", "adult", "overload"] }
      ],
      build(ctx) {
        const S = ctx.subjectId, c = ctx.concept;
        const stages = creatureStageLines(c, ctx.opts.stages);
        const rows = [];
        stages.forEach(s => {
          rows.push("| `images/cr_" + S + "_" + s.v + "_main.png` | " + s.zh + "立繪 1:1，帶屬性氛圍 | L1 |");
          rows.push("| `images/cr_" + S + "_" + s.v + "_turnaround.png` | " + s.zh + "三視圖合圖 16:9（正/側/背/3/4，自然站姿，灰底正交） | L1 |");
          rows.push("| `images/cr_" + S + "_" + s.v + "_front.png` | " + s.zh + "單張正面正交 1:1（image-to-3D 用） | L1 |");
        });
        const body = [
          "## 輸入概念",
          json(c),
          "",
          "## 型態與體型",
          "| 型態 | 體型級距 | 面數預算 |",
          "|---|---|---|",
          stages.map(s => "| " + s.zh + " | " + s.sizeZh + " | " + s.tris + " tris |").join("\n"),
          "",
          "- 進化要**一眼看得出是同一條線**：保留 2–3 個貫穿特徵（花紋、角、核心發光點等）。",
          "- 幼年可愛圓潤；少年開始出現原型意象；成年完整呈現原型；**超載**＝成年在戰鬥中的短暫爆發型態（能量外溢、裝甲展開、光環），輪廓更具威脅但仍可辨識。",
          "- 文化時空與原型結合：時空設定為「" + (c.timeline || "架空") + "」，文化原型為「" + (c.archetype || "自由發揮") + "（" + (c.region || "未知") + "）」。",
          "- 外觀基底階層（" + (c.base_parents ? c.base_parents.join(" + ") + " → " : "") + (c.bases ? c.bases.join(" + ") : "") + "）要融合成一個生物，不是拼貼。若包含類人奇幻種族，請融入該種族特徵與奇幻神韻。",
          "- 外觀配色組合（" + (c.appearance_palette ? c.appearance_palette.join(" / ") : "屬性色系") + "）貫穿各型態（1~3 種和諧色系）。",
          "- 成年體型基準：長/高約 " + c.height_length + "、重約 " + c.weight + "、體脂率約 " + c.body_fat + "（符合生物常態分佈，多數集中在常見大型鳥類至大型哺乳類體型，上限至暴龍級，排除小於老鼠之體型）。",
          "",
          "## 產出清單",
          "| 檔案 | 內容 | 層級 |",
          "|---|---|---|",
          "| `concept.json` | 補完的宙獸設定（欄位見下） | L1 |",
          rows.join("\n"),
          "| `images/cr_" + S + "_lineup.png` | 進化排列圖 21:9，所有型態由小到大 | L1 |",
          "| `images/cr_" + S + "_scale.png` | 體型對照：各型態旁放 160 cm 人類剪影 | L1 |",
          "",
          "### concept.json 欄位（不要填數值能力，數值另由平衡流程決定）",
          json({
            line_id: S, elements: c.elements, base_parents: c.base_parents, bases: c.bases,
            appearance_palette: c.appearance_palette,
            timeline: c.timeline, region: c.region,
            height_m: c.height_length, weight_kg: c.weight, body_fat_pct: c.body_fat,
            archetype: { name: c.archetype, region: c.region, timeline: c.timeline, motifs_used: [""], how_adapted: "" },
            sensitivity: c.sensitivity, role: c.role, temperament: c.temperament,
            stages: stages.map(s => ({
              stage: s.v, name_zh: "", name_en: "", size_class: s.size, height_m: 0,
              silhouette: "", key_features: [""], palette_hex: ["#", "#", "#"], petted_reaction: ""
            })),
            evolution_story_zh: "≤150 字",
            signature_move_idea: { name_zh: "", element: "", range_class: "melee|mid|long", shape: "front|sweep|burst|line|cone|arc|self|ally|field", visual: "" },
            trait_idea_zh: "觸發條件 → 效果（文字）",
            overload_visual: ""
          })
        ].join("\n");
        return common(ctx, body) + "\n\n" + deliverySection(ctx, "├── concept.json\n└── images/");
      }
    },

    {
      id: "model-rig", version: "1.0.0", zh: "Image-to-3D 建模與骨架", prefix: "mdl", platform: "gemini",
      needs: "either", drop: "Art/_incoming",
      options: [
        { key: "subject", type: "select", label: "對象", items: () => [{ v: "character", zh: "目前角色概念" }, { v: "creature", zh: "目前宙獸概念" }], def: "creature" },
        { key: "stages", type: "checks", label: "宙獸型態", items: () => C.STAGES.map(s => ({ v: s.v, zh: s.zh })), def: ["juvenile", "adolescent", "adult", "overload"], when: o => o.subject === "creature" }
      ],
      build(ctx) {
        const S = ctx.subjectId, isCr = subjectOf(ctx) === "creature", c = ctx.concept;
        const units = isCr
          ? creatureStageLines(c, ctx.opts.stages).map(s => ({ id: "cr_" + S + "_" + s.v, zh: s.zh, tris: s.tris, tex: s.tex }))
          : [{ id: S, zh: "角色", tris: "15–25k", tex: 2048 }];
        const rigRule = isCr
          ? "Generic 骨架：`Root`（地面，pivot）→ `Hips` → `Spine_01..n` → `Neck` → `Head`；四肢 `L_`/`R_` 前綴（例 `L_Front_Upper`）；尾巴 `Tail_01..n`、翅膀 `Wing_L_01..n`；插槽骨 `Socket_Mouth`、`Socket_Head`、`Socket_Core_FX`、`Socket_Front`、`Socket_PetTouch`、`Socket_Ground_FX`。"
          : "Unity Humanoid 相容命名：Hips, Spine, Chest, UpperChest, Neck, Head, Left/RightShoulder, Left/RightUpperArm, Left/RightLowerArm, Left/RightHand, 手指三節（Thumb/Index/Middle/Ring/Little）, Left/RightUpperLeg, Left/RightLowerLeg, Left/RightFoot, Left/RightToes；額外骨（頭髮/披風）用 `Extra_` 前綴；插槽骨 `Socket_HandR_Prop`、`Socket_HandL_Prop`、`Socket_Back_Pack`、`Socket_Throw`、`Socket_Head_FX`。";
        const rows = units.map(u =>
          "| `images/" + u.id + "_model_front.png`、`_model_side.png`、`_model_back.png` | " + u.zh + "建模參考：平光、無描邊、無陰影、灰底 1:1、全身置中 2048px | L1 |\n" +
          "| `models/" + u.id + ".glb`、`models/" + u.id + ".fbx` | " + u.zh + "模型（" + u.tris + " tris，貼圖 " + u.tex + "²），已綁骨 | L2 |");
        const body = [
          "## 輸入概念（含既有概念圖時以概念圖為準）",
          json(c),
          "如果我在對話中附上了概念圖/三視圖，以附圖為造型依據。",
          "",
          "## 目標",
          "產出可交給 image-to-3D 工具與 Blender 的完整建模與綁骨包。你若有 3D 生成能力就直接產出模型（L2）；**無論如何都必須交付 L1**。",
          "",
          "## 產出清單",
          "| 檔案 | 內容 | 層級 |",
          "|---|---|---|",
          rows.join("\n"),
          "| `rig/rig_spec.json` | 每個模型的骨骼清單：name、parent、head/tail 位置（公尺，相對腳底 pivot）、humanoid 對應、插槽骨 | L1 |",
          "| `rig/build_rig.py` | Blender 4.2+ 腳本：讀 rig_spec.json，為已匯入的同名 mesh 建立 armature、自動權重綁定、建立插槽骨、以 Unity 設定匯出 FBX | L1 |",
          "| `rig/README.md` | 執行步驟（Blender 版本、指令、預期結果、已知限制） | L1 |",
          "",
          "## 規格",
          list([
            "Unity 座標：Y 向上、正面朝 +Z、單位公尺、腳底中心為原點；FBX 匯出 scale 1.0、Apply Transform。",
            rigRule,
            "骨骼數：角色 ≤ 75（含手指）；宙獸 ≤ 60。每頂點最多 4 骨權重。",
            "貼圖：base color（卡通著色用，光影不要烘焙進貼圖）+ 選用 emissive mask（屬性發光部位）。",
            "build_rig.py 必須可在 Blender `--background --python` 下執行，路徑用參數傳入，不寫死絕對路徑。"
          ])
        ].join("\n");
        return common(ctx, body) + "\n\n" + deliverySection(ctx, "├── images/\n├── rig/\n└── models/      # L2");
      }
    },

    {
      id: "animation-set", version: "1.0.0", zh: "骨架動作組", prefix: "anim", platform: "gemini",
      needs: "either", drop: "Art/_incoming",
      options: [
        { key: "subject", type: "select", label: "對象", items: () => [{ v: "character", zh: "目前角色概念" }, { v: "creature", zh: "目前宙獸概念" }], def: "character" },
        { key: "stages", type: "checks", label: "宙獸型態", items: () => C.STAGES.map(s => ({ v: s.v, zh: s.zh })), def: ["adult"], when: o => o.subject === "creature" },
        { key: "actions", type: "checks", label: "動作", items: o => C.ACTIONS[o.subject === "creature" ? "creature" : "character"].map(a => ({ v: a.id, zh: a.zh })), def: "all" }
      ],
      build(ctx) {
        const S = ctx.subjectId, isCr = subjectOf(ctx) === "creature", c = ctx.concept;
        const defs = C.ACTIONS[isCr ? "creature" : "character"].filter(a => ctx.opts.actions.includes(a.id));
        const prefixes = isCr ? ctx.opts.stages.map(s => "cr_" + S + "_" + s) : [S];
        const clipTable = defs.map(a =>
          "| `" + (isCr ? "cr_<stage>_" : "chr_") + a.id + "` | " + a.zh + " | " + a.f + " | " + (a.loop ? "✓" : "") + " | " + (a.rm ? "✓" : "") + " | " + (a.ev || []).join(", ") + " |"
        ).join("\n");
        const sheets = Math.ceil(defs.length / 6);
        const body = [
          "## 輸入概念",
          json(c),
          "骨架以同一主體的 `rig/rig_spec.json` 為準；若我在對話中附上了它就照用，沒有就依下列命名規則自行定義並一併交付。",
          isCr ? "宙獸骨架：Generic，`Root` 在地面；插槽骨 `Socket_PetTouch` 為被撫摸對位點。" : "角色骨架：Unity Humanoid 相容命名；撫摸動作依宙獸體型分 S/M/L/XL。",
          "",
          "## 動作清單（30 fps；loop＝循環；RM＝root motion，其餘 in-place）",
          "| clip | 說明 | 幀數 | loop | RM | 事件@幀 |",
          "|---|---|---|---|---|---|",
          clipTable,
          isCr ? "\n套用型態：" + ctx.opts.stages.join("、") + "（每個型態各一套；體型越大動作越沉重，幀數可 ±20%，事件幀等比例調整）" : "",
          "",
          "## 產出清單",
          "| 檔案 | 內容 | 層級 |",
          "|---|---|---|",
          "| `anims/anim_spec.json` | 每個 clip：name、frames、loop、root_motion、events[{name,frame}]、blend_in/out 幀、key_poses[{frame, pose_zh, 主要骨骼旋轉(度)}] | L1 |",
          "| `images/" + prefixes[0] + "_anim_sheet_01.png` … `_" + String(sheets).padStart(2, "0") + ".png` | 關鍵姿勢分鏡，每張 6 個動作、每動作 3–5 格，標註 clip 名與幀號 | L1 |",
          "| `anims/keyframes.py` | Blender 4.2+ 腳本：依 anim_spec 的 key_poses 在 armature 上建立 Actions 並以 Unity 設定匯出 FBX | L1 |",
          "| `anims/<clip>.fbx` | 完成動畫（每 clip 一檔） | L2 |",
          "",
          "## 規格",
          list([
            "事件幀必須與動作一致：`ev_hit` 落在攻擊接觸幀、`ev_release` 在道具離手、`ev_spawn_fx` 在特效產生點。",
            "攻擊動作的方向感要符合形狀：正前＝向前突刺/咬；橫掃＝水平弧；擴散＝原地爆發；直線/扇形/拋物線射擊從 `Socket_Front` 發出。",
            "撫摸對位：角色手掌接觸點對齊宙獸 `Socket_PetTouch`；宙獸的 `petted` 要有享受的反應（瞇眼、蹭手、尾巴搖）。",
            "投擲三類收服道具的動作要能一眼區分（卡片甩出 / 珠子下手拋 / 方塊轉動後上手投）。",
            "閃避類與突進類用 root motion 並記錄位移距離（公尺）。"
          ])
        ].join("\n");
        return common(ctx, body) + "\n\n" + deliverySection(ctx, "├── images/\n└── anims/");
      }
    },

    {
      id: "move-vfx", version: "1.0.0", zh: "招式效果動畫（特效）", prefix: "fx", platform: "chatgpt",
      needs: null, drop: "Art/_incoming",
      options: [
        { key: "use_creature", type: "select", label: "綁定宙獸", items: () => [{ v: "yes", zh: "使用目前宙獸概念" }, { v: "no", zh: "不綁定（通用招式）" }], def: "yes" },
        // 綁定宙獸時只允許它自己的屬性，否則招式配色會跟施放者對不上
        { key: "element", type: "select", label: "屬性", items: (o, env) => C.ELEMENTS.filter(e => o.use_creature !== "yes" || (env.creature.elements || []).includes(e.v)).map(e => ({ v: e.v, zh: e.zh + " " + e.v })), def: "rand" },
        { key: "shape", type: "select", label: "形狀", items: () => C.VFX.shapes, def: "rand" },
        { key: "tier", type: "select", label: "強度", items: () => C.VFX.tiers, def: "rand" },
        { key: "move_name", type: "text", label: "招式名稱（可留空讓 AI 命名）", def: "" },
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
            caster: o.use_creature === "yes" ? ctx.concept : "通用，不綁定外觀"
          }),
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
            id: base, phases: [{ name: "anticipation|cast|travel|impact|linger|fade", start_ms: 0, end_ms: 0, description_zh: "" }],
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
            "特效從宙獸插槽骨發出（Socket_Front / Socket_Core_FX / Socket_Ground_FX）。"
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
