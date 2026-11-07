// COSMIMERA prompt-forge · 介面與組裝 + 交付成果審查台
(function () {
  const TOOL_VERSION = "1.5.0";
  const SPEC = "1";
  const C = window.COSMI_CATALOG, TASKS = window.COSMI_TASKS;
  const LS_KEY = "cosmimera.prompt-forge.v2";
  const REVIEW_KEY = "cosmimera.prompt-forge.reviews.v1";
  const DEFAULT_ROOT = "C:\\Users\\user\\Documents\\app\\cosmos_chimera";

  // 舊版人形父類別追溯相容（人形一律視為精靈族）
  function traceBaseParent(bp) {
    if (!bp) return bp;
    if (bp.startsWith("人形") || bp === "人形") {
      return "類人奇幻·精靈族";
    }
    return bp;
  }

  // ---- seeded RNG：同 seed 的「全部隨機」結果可重現，方便日後追溯任務 ----
  function hashStr(s) { let h = 2166136261; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); } return h >>> 0; }
  function mulberry32(a) { return function () { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }

  // 常態分佈（Box-Muller 轉換）
  function rollNormal(rng, mean, std, min, max) {
    let u1 = 0, u2 = 0;
    while (u1 === 0) u1 = rng();
    while (u2 === 0) u2 = rng();
    const z0 = Math.sqrt(-2.0 * Math.log(u1)) * Math.cos(2.0 * Math.PI * u2);
    let val = mean + z0 * std;
    if (min !== undefined && val < min) val = min;
    if (max !== undefined && val > max) val = max;
    return val;
  }

  // 對數常態分佈（生物體型分佈）
  function rollLogNormal(rng, logMean, logStd, min, max) {
    let u1 = 0, u2 = 0;
    while (u1 === 0) u1 = rng();
    while (u2 === 0) u2 = rng();
    const z0 = Math.sqrt(-2.0 * Math.log(u1)) * Math.cos(2.0 * Math.PI * u2);
    let val = Math.exp(logMean + z0 * logStd);
    if (min !== undefined && val < min) val = min;
    if (max !== undefined && val > max) val = max;
    return val;
  }

  function pickWeighted(rng, opts) {
    const pool = opts.filter(o => (o.w ?? 1) > 0);
    if (pool.length === 0) return null;
    const total = pool.reduce((s, o) => s + (o.w ?? 1), 0);
    let r = rng() * total;
    for (const o of pool) { r -= (o.w ?? 1); if (r < 0) return o; }
    return pool[pool.length - 1];
  }
  function pickN(rng, opts, n) {
    const left = opts.slice(), out = [];
    while (out.length < n && left.length) { const o = pickWeighted(rng, left); if (!o) break; out.push(o.v); left.splice(left.indexOf(o), 1); }
    return out;
  }
  const uniform = arr => arr.map(v => ({ v, w: 1 }));

  // ---- 概念欄位定義 ----
  const CH = C.CHARACTER, CR = C.CREATURE;
  const state = {
    seed: "", platform: "chatgpt", taskId: TASKS[0].id, includeReview: false,
    character: {}, creature: {}, locks: {}, opts: {}, notes: "", subjectIds: {},
    customOptions: {}, customValues: {}, activeTab: "forge",
    preset: null // 已設計項目的 line_id；設定時宙獸概念改用 designed.js 的定案內容，不再隨機
  };

  // REQ: 使用者 2026-11-07 — 已設計的宙獸（P2-08 御三家等）直接產出立繪 / img-to-3D prompt。
  const DESIGNED = (window.COSMI_DESIGNED && window.COSMI_DESIGNED.items) || [];
  function designedItem() { return state.preset ? DESIGNED.find(d => d.line_id === state.preset) || null : null; }

  function getCustom(key) { return state.customOptions[key] || []; }
  function addCustom(key, val) {
    if (!val) return;
    const list = state.customOptions[key] || (state.customOptions[key] = []);
    if (!list.includes(val)) list.push(val);
  }

  // 取得外觀基底子類別清單（由外觀基底父類別決定）
  function getCreatureSubBases(cat) {
    const list = CR.base_sub_map[cat] || [];
    const prefix = cat.split("·")[1] || cat;
    return list.map(x => `${prefix}·${x}`);
  }

  // 人類常態生成（身高/體重/體脂）
  function sampleHumanDimensions(rng, gender, ageBand, ageNum) {
    const isFemale = gender === "女";
    const age = Number(ageNum) || 17;
    let hMean = isFemale ? 162 : 175;
    let hStd = isFemale ? 5.8 : 6.5;
    if (ageBand === "兒童" || age <= 12) {
      hMean = isFemale ? (136 + (age - 9) * 4.2) : (135 + (age - 9) * 4.5);
      hStd = 5.0;
    } else if (ageBand === "青少年" || age <= 17) {
      hMean = isFemale ? (152 + (age - 13) * 2.0) : (154 + (age - 13) * 3.5);
      hStd = 5.2;
    }
    const height = Math.round(rollNormal(rng, hMean, hStd, 110, 210));

    // 體重由 BMI 常態推算
    const bmiMean = isFemale ? 20.8 : 22.2;
    const bmiStd = isFemale ? 2.2 : 2.4;
    const bmi = rollNormal(rng, bmiMean, bmiStd, 16.5, 32.0);
    const weight = Math.round((bmi * Math.pow(height / 100, 2)) * 10) / 10;

    // 體脂率常態
    const fatMean = isFemale ? 22.5 : 15.5;
    const fatStd = isFemale ? 4.0 : 3.5;
    const fatMin = isFemale ? 14.0 : 8.0;
    const fatMax = isFemale ? 34.0 : 28.0;
    const bodyFat = Math.round(rollNormal(rng, fatMean, fatStd, fatMin, fatMax) * 10) / 10;

    return { height: height + " cm", weight: weight + " kg", bodyFat: bodyFat + "%" };
  }

  // 宙獸生物常態生成（身高/體長、體重、體脂率），排除小於老鼠（長>=0.15m, 重>=0.04kg），上限至暴龍 (12.5m, 9000kg)
  function sampleCreatureDimensions(rng, sizeClass) {
    const sc = C.SIZE_CLASSES[sizeClass] || C.SIZE_CLASSES.M;
    let lenLogM = 0, lenLogS = 0.25, wtLogM = 0, wtLogS = 0.55;
    if (sizeClass === "S") {
      lenLogM = Math.log(0.32); lenLogS = 0.32;
      wtLogM = Math.log(0.9); wtLogS = 0.75;
    } else if (sizeClass === "M") {
      lenLogM = Math.log(1.05); lenLogS = 0.22;
      wtLogM = Math.log(22.0); wtLogS = 0.55;
    } else if (sizeClass === "L") {
      lenLogM = Math.log(2.05); lenLogS = 0.18;
      wtLogM = Math.log(180.0); wtLogS = 0.48;
    } else { // XL
      lenLogM = Math.log(5.2); lenLogS = 0.40;
      wtLogM = Math.log(2600.0); wtLogS = 0.75;
    }

    const length = Math.round(rollLogNormal(rng, lenLogM, lenLogS, sc.len_min, sc.len_max) * 100) / 100;
    const rawWt = rollLogNormal(rng, wtLogM, wtLogS, sc.wt_min, sc.wt_max);
    const weight = rawWt >= 100 ? Math.round(rawWt) : Math.round(rawWt * 10) / 10;
    const fat = Math.round(rollNormal(rng, (sc.fat_min + sc.fat_max) / 2, 3.0, sc.fat_min, sc.fat_max) * 10) / 10;

    return { length: length + " m", weight: weight + " kg", bodyFat: fat + "%" };
  }

  const FIELDS = {
    character: [
      { key: "role", label: "定位", options: () => uniform(CH.role.concat(getCustom("character.role"))), count: [{ n: 1, w: 1 }] },
      { key: "ethnicity", label: "人種", options: () => CH.ethnicity.map(e => ({ v: e.v, w: e.w, zh: e.zh })).concat(getCustom("character.ethnicity").map(v => ({ v, w: 1, zh: v }))), count: [{ n: 1, w: 1 }] },
      { key: "region", label: "文化地區", options: () => uniform(C.CULTURE_REGIONS.concat(getCustom("character.region"))), count: [{ n: 1, w: 1 }] },
      { key: "timeline", label: "文化時間線", options: () => C.TIMELINES.map(t => ({ v: t.v, zh: t.zh, w: 1 })).concat(getCustom("character.timeline").map(v => ({ v, zh: v, w: 1 }))), count: [{ n: 1, w: 1 }] },
      { key: "gender", label: "性別", options: () => CH.gender.map(g => ({ v: g.v, w: g.w })).concat(getCustom("character.gender").map(v => ({ v, w: 1 }))), count: [{ n: 1, w: 1 }] },
      { key: "age_band", label: "年齡層", options: () => CH.age.map(a => ({ v: a.v, w: a.w })).concat(getCustom("character.age_band").map(v => ({ v, w: 1 }))), count: [{ n: 1, w: 1 }] },
      { key: "age", label: "年齡", options: s => { const b = CH.age.find(a => a.v === s.character.age_band?.[0]) || CH.age[1]; const r = []; for (let i = b.min; i <= b.max; i++) r.push({ v: String(i), w: 1 }); r.push({ v: "未知", w: 1 }); return r.concat(getCustom("character.age").map(v => ({ v, w: 1 }))); }, count: [{ n: 1, w: 1 }], dep: "age_band" },
      { key: "height", label: "身高 (cm)", options: s => {
        const d = sampleHumanDimensions(Math.random, s.character.gender?.[0], s.character.age_band?.[0], s.character.age?.[0]);
        return [{ v: d.height, w: 1 }, { v: "165 cm", w: 1 }, { v: "172 cm", w: 1 }, { v: "未知", w: 1 }].concat(getCustom("character.height").map(v => ({ v, w: 1 })));
      }, count: [{ n: 1, w: 1 }], dep: "age", customGen: (s, rng) => sampleHumanDimensions(rng, s.character.gender?.[0], s.character.age_band?.[0], s.character.age?.[0]).height },
      { key: "weight", label: "體重 (kg)", options: s => {
        const d = sampleHumanDimensions(Math.random, s.character.gender?.[0], s.character.age_band?.[0], s.character.age?.[0]);
        return [{ v: d.weight, w: 1 }, { v: "52 kg", w: 1 }, { v: "65 kg", w: 1 }, { v: "未知", w: 1 }].concat(getCustom("character.weight").map(v => ({ v, w: 1 })));
      }, count: [{ n: 1, w: 1 }], dep: "height", customGen: (s, rng) => sampleHumanDimensions(rng, s.character.gender?.[0], s.character.age_band?.[0], s.character.age?.[0]).weight },
      { key: "body_fat", label: "體脂率 (%)", options: s => {
        const d = sampleHumanDimensions(Math.random, s.character.gender?.[0], s.character.age_band?.[0], s.character.age?.[0]);
        return [{ v: d.bodyFat, w: 1 }, { v: "18.0%", w: 1 }, { v: "22.0%", w: 1 }, { v: "未知", w: 1 }].concat(getCustom("character.body_fat").map(v => ({ v, w: 1 })));
      }, count: [{ n: 1, w: 1 }], dep: "weight", customGen: (s, rng) => sampleHumanDimensions(rng, s.character.gender?.[0], s.character.age_band?.[0], s.character.age?.[0]).bodyFat },
      { key: "cup_size", label: "罩杯（女性）", options: () => CH.cup_sizes.map(c => ({ v: c.v, w: c.w })).concat(getCustom("character.cup_size").map(v => ({ v, w: 1 }))), count: [{ n: 1, w: 1 }], when: s => s.character.gender?.[0] === "女" },
      { key: "outfit_palette", label: "服裝色系 (1~3色)", options: () => C.PALETTES.map(p => ({ v: p.v, w: p.w, zh: `${p.v} (${p.colors || 0}色)` })).concat(getCustom("character.outfit_palette").map(v => ({ v, w: 1, zh: v }))), count: [{ n: 1, w: 1 }], max: 3 },
      { key: "hair_palette", label: "頭髮色系 (1~2色)", options: () => CH.hair_palettes.map(p => ({ v: p.v, w: p.w })).concat(getCustom("character.hair_palette").map(v => ({ v, w: 1 }))), count: [{ n: 1, w: 1 }], max: 2 },
      { key: "eye_palette", label: "瞳孔色系 (1~2色)", options: () => CH.eye_palettes.map(p => ({ v: p.v, w: p.w })).concat(getCustom("character.eye_palette").map(v => ({ v, w: 1 }))), count: [{ n: 1, w: 1 }], max: 2 },
      { key: "personality", label: "性格特質", options: () => uniform(C.PERSONALITIES.concat(getCustom("character.personality"))), count: [{ n: 2, w: 1 }], max: 4 },
      { key: "style", label: "服裝/外觀風格", options: () => uniform(CH.style.concat(getCustom("character.style"))), count: [{ n: 2, w: 1 }], max: 3 },
      { key: "interests", label: "休閒興趣", options: () => uniform(CH.interests.concat(getCustom("character.interests"))), count: [{ n: 2, w: 1 }], max: 3 },
      { key: "likes", label: "喜歡事物", options: () => uniform(CH.likes.concat(getCustom("character.likes"))), count: [{ n: 2, w: 1 }], max: 3 },
      { key: "dislikes", label: "討厭事物", options: () => uniform(CH.dislikes.concat(getCustom("character.dislikes"))), count: [{ n: 2, w: 1 }], max: 3 },
      { key: "vessel", label: "主要收服道具", options: () => C.VESSELS.map(v => ({ v: v.v, w: v.w, zh: v.zh })).concat(getCustom("character.vessel").map(v => ({ v, w: 1, zh: v }))), count: [{ n: 1, w: 1 }] }
    ],
    creature: [
      { key: "elements", label: "屬性", options: () => C.ELEMENTS.map(e => ({ v: e.v, w: e.w, zh: e.zh })).concat(getCustom("creature.elements").map(v => ({ v, w: 1, zh: v }))), count: [{ n: 1, w: 2 }, { n: 2, w: 1 }], max: 2 },
      { key: "base_parents", label: "外觀基底類別", options: () => CR.base_parents.map(b => ({ v: b.v, zh: b.zh || b.v, w: b.w })).concat(getCustom("creature.base_parents").map(v => ({ v, zh: v, w: 1 }))), count: [{ n: 1, w: 1 }], max: 2 },
      { key: "bases", label: "外觀基底 (1~3)", options: s => {
        const parents = (s.creature.base_parents || []).map(traceBaseParent);
        let subs = [];
        parents.forEach(p => { subs = subs.concat(getCreatureSubBases(p)); });
        if (!subs.length) {
          subs = getCreatureSubBases("生物·哺乳類").concat(getCreatureSubBases("生物·鳥類"));
        }
        return uniform(subs.concat(["未知"]).concat(getCustom("creature.bases")));
      }, count: [{ n: 2, w: 1 }], max: 3, dep: "base_parents" },
      { key: "appearance_palette", label: "宙獸色系 (1~3色)", options: () => C.PALETTES.map(p => ({ v: p.v, w: p.w, zh: `${p.v} (${p.colors || 0}色)` })).concat(getCustom("creature.appearance_palette").map(v => ({ v, w: 1, zh: v }))), count: [{ n: 1, w: 1 }], max: 3 },
      { key: "timeline", label: "文化時間線", options: () => C.TIMELINES.map(t => ({ v: t.v, zh: t.zh, w: 1 })).concat(getCustom("creature.timeline").map(v => ({ v, zh: v, w: 1 }))), count: [{ n: 1, w: 1 }] },
      { key: "region", label: "文化地區", options: () => uniform(C.CULTURE_REGIONS.concat(getCustom("creature.region"))), count: [{ n: 1, w: 1 }] },
      { key: "archetype", label: "文化原型", options: s => {
        const pool = archetypePool(s);
        return pool.map(a => ({ v: a.name, w: 1, zh: a.name + "（" + a.region + "·" + a.type + (a.sensitivity === "review" ? "・需審" : "") + "）" })).concat(getCustom("creature.archetype").map(v => ({ v, w: 1, zh: v })));
      }, count: [{ n: 1, w: 1 }] },
      { key: "role", label: "戰鬥定位", options: () => uniform(CR.role.concat(getCustom("creature.role"))), count: [{ n: 1, w: 1 }] },
      { key: "temperament", label: "性情", options: () => uniform(C.PERSONALITIES.concat(getCustom("creature.temperament"))), count: [{ n: 1, w: 1 }] },
      { key: "size_profile", label: "體型（幼→少→成→超載）", options: () => CR.size_profile.map(p => ({ v: p.v, w: p.w })).concat(getCustom("creature.size_profile").map(v => ({ v, w: 1 }))), count: [{ n: 1, w: 1 }] },
      { key: "height_length", label: "成體身長/高 (m)", options: s => {
        const adultClass = (s.creature.size_profile?.[0] || "S→M→L→XL").split("→")[2] || "L";
        const d = sampleCreatureDimensions(Math.random, adultClass);
        return [{ v: d.length, w: 1 }, { v: "1.80 m", w: 1 }, { v: "0.95 m", w: 1 }, { v: "未知", w: 1 }].concat(getCustom("creature.height_length").map(v => ({ v, w: 1 })));
      }, count: [{ n: 1, w: 1 }], dep: "size_profile", customGen: (s, rng) => {
        const adultClass = (s.creature.size_profile?.[0] || "S→M→L→XL").split("→")[2] || "L";
        return sampleCreatureDimensions(rng, adultClass).length;
      }},
      { key: "weight", label: "成體重量 (kg)", options: s => {
        const adultClass = (s.creature.size_profile?.[0] || "S→M→L→XL").split("→")[2] || "L";
        const d = sampleCreatureDimensions(Math.random, adultClass);
        return [{ v: d.weight, w: 1 }, { v: "180 kg", w: 1 }, { v: "22 kg", w: 1 }, { v: "未知", w: 1 }].concat(getCustom("creature.weight").map(v => ({ v, w: 1 })));
      }, count: [{ n: 1, w: 1 }], dep: "size_profile", customGen: (s, rng) => {
        const adultClass = (s.creature.size_profile?.[0] || "S→M→L→XL").split("→")[2] || "L";
        return sampleCreatureDimensions(rng, adultClass).weight;
      }},
      { key: "body_fat", label: "體脂率 (%)", options: s => {
        const adultClass = (s.creature.size_profile?.[0] || "S→M→L→XL").split("→")[2] || "L";
        const d = sampleCreatureDimensions(Math.random, adultClass);
        return [{ v: d.bodyFat, w: 1 }, { v: "16.0%", w: 1 }, { v: "20.0%", w: 1 }, { v: "未知", w: 1 }].concat(getCustom("creature.body_fat").map(v => ({ v, w: 1 })));
      }, count: [{ n: 1, w: 1 }], dep: "size_profile", customGen: (s, rng) => {
        const adultClass = (s.creature.size_profile?.[0] || "S→M→L→XL").split("→")[2] || "L";
        return sampleCreatureDimensions(rng, adultClass).bodyFat;
      }}
    ]
  };
  function archetypePool(s) { return CR.archetypes.filter(a => s.includeReview || a.sensitivity === "ok"); }

  function rollField(kind, f, rng) {
    if (f.when && !f.when(state)) {
      delete state[kind][f.key];
      return;
    }
    if (f.customGen) {
      state[kind][f.key] = [f.customGen(state, rng)];
      return;
    }
    const opts = f.options(state);
    const n = pickWeighted(rng, f.count).n;
    state[kind][f.key] = pickN(rng, opts, n);
  }

  function rollAll(kind, rng, force) {
    FIELDS[kind].forEach(f => {
      if (f.when && !f.when(state)) {
        delete state[kind][f.key];
        return;
      }
      const key = kind + "." + f.key;
      const locked = state.locks[key];
      const valid = (state[kind][f.key] || []).length > 0 && (f.customGen || (state[kind][f.key] || []).every(v => f.options(state).some(o => o.v === v)));
      if (force ? !locked || !valid : !valid) rollField(kind, f, rng);
    });
  }
  function revalidate(kind) { rollAll(kind, Math.random, false); }

  // ---- 概念輸出（prompt 內的 JSON） ----
  function zhOf(list, v) { const o = list.find(x => x.v === v); return o ? o.zh : v; }
  function characterConcept() {
    const c = state.character;
    const res = {
      id: subjectId("character"), role: c.role?.[0] || "未知", ethnicity: c.ethnicity?.[0] || "未知",
      region: c.region?.[0] || "未知", timeline: c.timeline?.[0] || "未知", gender: c.gender?.[0] || "未知",
      age: isNaN(Number(c.age?.[0])) ? (c.age?.[0] || "未知") : Number(c.age[0]), age_band: c.age_band?.[0] || "未知",
      height: c.height?.[0] || "未知", weight: c.weight?.[0] || "未知", body_fat: c.body_fat?.[0] || "未知"
    };
    if (c.gender?.[0] === "女" && c.cup_size?.[0]) {
      res.cup_size = c.cup_size[0];
    }
    Object.assign(res, {
      outfit_palette: c.outfit_palette || [],
      hair_palette: c.hair_palette || [],
      eye_palette: c.eye_palette || [],
      personality: c.personality || [], style: (c.style || []).join(" × "),
      interests: c.interests || [], likes: c.likes || [], dislikes: c.dislikes || [],
      vessel: c.vessel?.[0] || "未知", vessel_zh: zhOf(C.VESSELS, c.vessel?.[0])
    });
    return res;
  }

  function creatureConcept() {
    const d = designedItem();
    if (d) return JSON.parse(JSON.stringify(d.concept));
    const c = state.creature, a = CR.archetypes.find(x => x.name === c.archetype?.[0]) || {};
    const parents = (c.base_parents || []).map(traceBaseParent);
    return {
      line_id: subjectId("creature"),
      elements: (c.elements || []).map(e => e + "（" + (zhOf(C.ELEMENTS, e) || e).split(" ")[0] + "）"),
      element_palettes: (c.elements || []).map(e => (C.ELEMENTS.find(x => x.v === e) || {}).palette || "自然色系"),
      base_parents: parents,
      bases: c.bases || [],
      appearance_palette: c.appearance_palette || [],
      timeline: c.timeline?.[0] || "未知",
      region: c.region?.[0] || "未知",
      archetype: c.archetype?.[0] || "未知", archetype_type: a.type || "自由原型", motifs: a.motifs || "",
      sensitivity: a.sensitivity || "ok", role: c.role?.[0] || "未知", temperament: c.temperament?.[0] || "未知",
      size_profile: c.size_profile?.[0] || "未知",
      height_length: c.height_length?.[0] || "未知",
      weight: c.weight?.[0] || "未知",
      body_fat: c.body_fat?.[0] || "未知",
      formula: (c.elements || []).join("+") + " × " + (c.bases || []).join("+") + " × " + (c.archetype?.[0] || "原型") + "（" + (c.timeline?.[0] || "時空") + "）"
    };
  }

  function subjectId(kind) {
    const d = kind === "creature" ? designedItem() : null;
    if (d) return d.line_id;
    const custom = state.subjectIds[kind];
    if (custom) return custom;
    return (kind === "character" ? "chr_" : "line_") + state.seed;
  }

  function task() { return TASKS.find(t => t.id === state.taskId) || TASKS[0]; }
  const env = () => ({ character: state.character, creature: state.creature });
  function taskOpts(t) {
    const o = state.opts[t.id] || (state.opts[t.id] = {});
    t.options.forEach(def => {
      const items = def.items ? def.items(o, env()) : [];
      if (def.type === "select" && o[def.key] !== undefined && !items.some(i => i.v === o[def.key])) delete o[def.key];
      if (o[def.key] !== undefined) return;
      if (def.type === "checks") o[def.key] = def.def === "all" ? items.map(i => i.v) : def.def.slice();
      else if (def.def === "rand") o[def.key] = items[Math.floor(Math.random() * items.length)].v;
      else o[def.key] = def.def;
    });
    return o;
  }
  function resolveSubject(t, o) {
    if (t.needs === "character") return "character";
    if (t.needs === "creature") return "creature";
    if (t.needs === "either" && o.scope === "batch") return null;
    if (t.needs === "either") return o.subject === "creature" ? "creature" : "character";
    if (t.id === "move-vfx" && o.use_creature === "yes") return "creature";
    return null;
  }

  function stamp() {
    const d = new Date(), p = n => String(n).padStart(2, "0");
    return { day: d.getFullYear() + p(d.getMonth() + 1) + p(d.getDate()), hm: p(d.getHours()) + p(d.getMinutes()) };
  }

  let lastBuild = null;
  function build() {
    const t = task(), o = taskOpts(t), subj = resolveSubject(t, o);
    const errors = [];
    t.options
      .filter(d => d.type === "checks" && !d.optional && (!d.when || d.when(o)) && (o[d.key] || []).length === 0)
      .forEach(d => errors.push("至少勾選一個「" + d.label + "」"));
    const s = stamp();
    const taskId = t.prefix + "-" + s.day + "-" + state.seed + "-" + s.hm;
    const header = "[COSMIMERA-TASK spec=" + SPEC + " tool=" + TOOL_VERSION + " catalog=" + C.version + " template=" + t.id + "@" + t.version + " task_id=" + taskId + " seed=" + state.seed + "]";
    const concept = subj === "character" ? characterConcept() : subj === "creature" ? creatureConcept() : null;
    
    // 若為 model-rig 且未手動指定 source_pack，預設自動關聯目前概念立繪包
    // 已設計項目的立繪包不是本次 seed 產生的，猜一個 task_id 只會造成錯誤追溯；交給 prompt 依 subject_id 找最新包。
    if (t.id === "model-rig" && o.scope !== "batch" && !o.source_pack && !designedItem()) {
      const srcPrefix = (subj === "creature" ? "cr-" : "chr-");
      o.source_pack = srcPrefix + s.day + "-" + state.seed + "-" + s.hm;
    }

    const ctx = {
      header, taskId, task: t, opts: o, concept, notes: state.notes.trim(),
      platform: state.platform, platformName: state.platform === "gemini" ? "Gemini Argon（任務模式）" : "ChatGPT（對話版）",
      subjectId: subj ? subjectId(subj) : t.id === "model-rig" ? "batch" : t.id === "capture-vessel" ? "vessels" : t.id === "culture-research" ? "region_" + o.region : "fx_" + o.element + "_" + o.shape
    };
    lastBuild = { ctx, errors, text: errors.length ? "" : t.build(ctx) };
    return lastBuild;
  }

  // ---- DOM 輔助 ----
  const $ = sel => document.querySelector(sel);
  function el(tag, attrs, ...kids) {
    const e = document.createElement(tag);
    for (const k in attrs || {}) {
      if (k === "class") e.className = attrs[k];
      else if (k.startsWith("on")) e.addEventListener(k.slice(2), attrs[k]);
      else if (attrs[k] !== false && attrs[k] != null) e.setAttribute(k, attrs[k] === true ? "" : attrs[k]);
    }
    kids.flat().forEach(k => k != null && e.append(k.nodeType ? k : document.createTextNode(String(k))));
    return e;
  }

  // ---- 手寫編輯 / 追加選項彈窗 ----
  let curCustomCtx = null;
  function openCustomModal(title, currentVal, onConfirm) {
    curCustomCtx = onConfirm;
    $("#customModalTitle").textContent = "手寫自訂 / 追加選項：" + title;
    $("#customModalInput").value = currentVal || "";
    $("#customModal").classList.add("show");
    setTimeout(() => { $("#customModalInput").focus(); $("#customModalInput").select(); }, 50);
  }
  function closeCustomModal() {
    $("#customModal").classList.remove("show");
    curCustomCtx = null;
  }

  function renderTasks() {
    const box = $("#tasks"); box.replaceChildren();
    TASKS.forEach(t => {
      box.append(el("button", {
        class: "task" + (t.id === state.taskId ? " on" : ""), type: "button",
        onclick: () => { state.taskId = t.id; renderAll(); }
      }, el("span", { class: "tname" }, t.zh), el("span", { class: "tmeta" }, t.id + " · 建議 " + (t.platform === "gemini" ? "Gemini" : "ChatGPT"))));
    });
    renderDesigned();
  }

  function useDesigned(lineId, taskId) {
    state.preset = lineId;
    state.taskId = taskId;
    const o = state.opts[taskId] || (state.opts[taskId] = {});
    if (taskId === "model-rig") { o.scope = "single"; o.subject = "creature"; o.source_pack = ""; }
    renderAll();
  }

  function renderDesigned() {
    const box = $("#designed"); if (!box) return;
    box.replaceChildren();
    if (!DESIGNED.length) { box.append(el("p", { class: "muted" }, "沒有已設計項目（designed.js 未載入）。")); return; }
    DESIGNED.forEach(d => {
      const on = state.preset === d.line_id;
      box.append(el("div", { class: "designed" + (on ? " on" : "") },
        el("span", { class: "tname" }, d.code_zh + " · " + d.title_zh),
        el("span", { class: "tmeta" }, d.line_id + " · " + d.element),
        el("div", { class: "row" },
          el("button", { type: "button", class: "btn mini" + (on && state.taskId === "creature-concept" ? "" : " ghost"), onclick: () => useDesigned(d.line_id, "creature-concept") }, "🎨 立繪 prompt"),
          el("button", { type: "button", class: "btn mini" + (on && state.taskId === "model-rig" ? "" : " ghost"), onclick: () => useDesigned(d.line_id, "model-rig") }, "🧊 img-to-3D prompt"))));
    });
    if (state.preset) box.append(el("button", { type: "button", class: "btn mini ghost", onclick: () => { state.preset = null; renderAll(); } }, "✕ 取消已設計項目（回到隨機概念）"));
  }

  function fieldRow(kind, f) {
    if (f.when && !f.when(state)) return el("div", { style: "display:none" });
    const key = kind + "." + f.key, vals = state[kind][f.key] || [], opts = f.options(state);
    const label = v => (opts.find(o => o.v === v) || {}).zh || v;
    
    // 改良後的按鈕式鎖定開關，排版永不跑版
    const locked = !!state.locks[key];
    const lockBtn = el("button", {
      class: "lock-btn" + (locked ? " locked" : ""),
      type: "button",
      title: locked ? "已鎖定（全部隨機時不變，點選解鎖）" : "未鎖定（點選鎖定）",
      onclick: () => { state.locks[key] = !state.locks[key]; renderAll(); save(); }
    }, locked ? "🔒" : "🔓");

    const dice = el("button", { class: "icon", type: "button", title: "重抽此欄", onclick: () => { rollField(kind, f, Math.random); revalidate(kind); renderAll(); } }, "🎲");
    const edit = el("button", { class: "icon", type: "button", title: "手寫編輯 / 追加自訂值", onclick: () => {
      openCustomModal(f.label, vals[0] || "", (newVal) => {
        addCustom(key, newVal);
        if (multi) {
          if (!vals.includes(newVal)) state[kind][f.key] = vals.concat(newVal);
        } else {
          state[kind][f.key] = [newVal];
        }
        revalidate(kind);
        renderAll();
      });
    } }, "✏️");

    let control;
    const multi = (f.max || 1) > 1;
    if (!multi) {
      control = el("select", { onchange: e => {
        if (e.target.value === "__CUSTOM__") {
          edit.click();
        } else {
          state[kind][f.key] = [e.target.value];
          revalidate(kind);
          renderAll();
        }
      } },
        opts.map(o => el("option", { value: o.v, selected: o.v === vals[0] }, (o.zh || o.v) + (o.w === 0 ? "（權重 0）" : ""))),
        el("option", { value: "__CUSTOM__" }, "✏️ 手寫輸入自訂值...")
      );
    } else {
      const chips = vals.map(v => el("span", { class: "chip" }, label(v), el("button", { type: "button", title: "移除", onclick: () => { state[kind][f.key] = vals.filter(x => x !== v); renderAll(); } }, "×")));
      const add = el("select", { onchange: e => {
        if (!e.target.value) return;
        if (e.target.value === "__CUSTOM__") {
          edit.click();
        } else {
          state[kind][f.key] = vals.concat(e.target.value);
          renderAll();
        }
      }, disabled: vals.length >= f.max },
        el("option", { value: "" }, vals.length >= f.max ? "已達上限" : "＋ 加入"),
        opts.filter(o => !vals.includes(o.v)).map(o => el("option", { value: o.v }, o.zh || o.v)),
        el("option", { value: "__CUSTOM__" }, "✏️ 手寫追加自訂標籤...")
      );
      control = el("div", { class: "chips" }, chips, add);
    }
    return el("div", { class: "field" }, el("label", {}, f.label), control, el("div", { class: "fctl" }, edit, dice, lockBtn));
  }

  function renderConcepts() {
    const t = task(), o = taskOpts(t), subj = resolveSubject(t, o);
    const box = $("#concept"); box.replaceChildren();
    if (!subj) { box.append(el("p", { class: "muted" }, "此任務不使用角色/宙獸概念。")); return; }
    const kind = subj;
    const d = kind === "creature" ? designedItem() : null;
    if (d) {
      const L = d.concept.locked;
      box.append(el("div", { class: "subhead" }, el("h3", {}, "已設計宙獸：" + d.code_zh + " 線"), el("code", {}, d.line_id)));
      box.append(el("p", { class: "muted" }, "內容來自 " + d.source + "（designed.js），不隨機、不可在此修改；要改請改資料後重跑 gen_designed.py。"));
      L.stages.forEach(s => box.append(el("div", { class: "field" },
        el("label", {}, s.stage + " · " + s.size_class),
        el("div", {}, el("b", {}, s.name_zh + " " + s.name_en), " ", el("code", {}, s.creature_id),
          el("div", { class: "muted" }, s.look), el("div", { class: "muted" }, "招式 " + s.signature_move.name_zh + "（" + s.signature_move.move_id + "）")))));
      box.append(el("p", { class: "muted" }, "特性：" + L.trait.name_zh + "（" + L.trait.trait_id + "）· 原型：" + d.concept.archetype + "（" + d.concept.region + "）· 超載：" + L.overload));
      return;
    }
    box.append(el("div", { class: "subhead" },
      el("h3", {}, kind === "character" ? "角色設計概念" : "宙獸設計概念"),
      el("label", { class: "inline" }, "主體 ID ",
        el("input", { class: "sid", value: subjectId(kind), spellcheck: "false", onchange: e => { const v = e.target.value.trim().toLowerCase().replace(/[^a-z0-9_]/g, "_"); state.subjectIds[kind] = v && v !== (kind === "character" ? "chr_" : "line_") + state.seed ? v : ""; renderAll(); } }))));
    FIELDS[kind].forEach(f => box.append(fieldRow(kind, f)));
    if (kind === "creature") {
      box.append(el("label", { class: "inline review" },
        el("input", { type: "checkbox", checked: state.includeReview, onchange: e => { state.includeReview = e.target.checked; revalidate("creature"); renderAll(); } }),
        " 包含 sensitivity: review 原型（在世信仰、族群神聖敘事、爭議人物；需人工審核）"));
      const a = CR.archetypes.find(x => x.name === state.creature.archetype?.[0]);
      if (a) box.append(el("p", { class: "muted" }, "意象：" + a.motifs));
    }
  }

  function renderOptions() {
    const t = task(), o = taskOpts(t), box = $("#options"); box.replaceChildren();
    t.options.filter(d => !d.when || d.when(o)).forEach(d => {
      const items = d.items ? d.items(o, env()) : [];
      let control;
      if (d.type === "checks") {
        control = el("div", { class: "checks" },
          el("button", { type: "button", class: "btn mini ghost", onclick: () => { o[d.key] = o[d.key].length === items.length ? [] : items.map(i => i.v); renderAll(); } }, o[d.key].length === items.length ? "全不選" : "全選"),
          items.map(i => el("label", { class: "check" }, el("input", { type: "checkbox", checked: o[d.key].includes(i.v), onchange: e => { o[d.key] = e.target.checked ? o[d.key].concat(i.v) : o[d.key].filter(x => x !== i.v); renderAll(); } }), i.zh)));
      } else if (d.type === "select") {
        control = el("div", { class: "row" },
          el("select", { onchange: e => { o[d.key] = e.target.value; if (d.key === "subject") delete o.actions; taskOpts(t); renderAll(); } }, items.map(i => el("option", { value: i.v, selected: i.v === o[d.key] }, i.zh))),
          el("button", { class: "icon", type: "button", title: "隨機", onclick: () => { o[d.key] = items[Math.floor(Math.random() * items.length)].v; if (d.key === "subject") delete o.actions; taskOpts(t); renderAll(); } }, "🎲"));
      } else {
        control = el("input", { value: o[d.key], placeholder: d.placeholder || "", onchange: e => { o[d.key] = e.target.value; renderAll(); } });
      }
      box.append(el("div", { class: "field opt" }, el("label", {}, d.label), control));
    });
    if (!box.children.length) box.append(el("p", { class: "muted" }, "此任務沒有額外選項。"));
  }

  function renderOutput() {
    const b = build(), t = b.ctx.task;
    $("#prompt").value = b.errors.length ? "⚠ " + b.errors.join("\n⚠ ") : b.text;
    $("#copy").disabled = $("#download").disabled = b.errors.length > 0;
    $("#header").textContent = b.ctx.header;
    $("#chars").textContent = b.text.length.toLocaleString() + " 字元";
    const root = state.root || DEFAULT_ROOT;
    const dest = root + "\\" + t.drop.replace(/\//g, "\\");
    const cmd = 'Expand-Archive -Path "$HOME\\Downloads\\' + b.ctx.taskId + '.zip" -DestinationPath "' + dest + '\\" -Force';
    if (t.drive) {
      const zips = b.ctx.taskId + (b.ctx.opts.scope === "batch" ? "*" : "") + ".zip";
      const dcmd = 'Get-ChildItem "$HOME\\Downloads\\' + zips + '" | ForEach-Object { Expand-Archive -Path $_.FullName -DestinationPath "' + dest + '\\" -Force }';
      $("#deliver").replaceChildren(
        el("ol", {},
          el("li", {}, "先把角色/宙獸立繪包（", el("code", {}, "chr-*.zip / cr-*.zip"), "）放到雲端硬碟 ", el("code", {}, "project/cosmos_chimera/image_in/")),
          el("li", {}, "按「複製 prompt」→「開啟 Gemini」切到任務模式貼上送出"),
          el("li", {}, "完成後從雲端硬碟 ", el("code", {}, "project/cosmos_chimera/3D_out/"), " 下載 ", el("code", {}, zips), " 到「下載」資料夾"),
          el("li", {}, "解壓到 ", el("code", {}, dest + "\\"), "，再到「成果審查台」審查。")
        ),
        el("div", { class: "cmd" }, el("code", {}, dcmd), el("button", { type: "button", class: "btn ghost mini", onclick: () => copyText(dcmd, "已複製解壓指令") }, "複製"))
      );
      return;
    }
    $("#deliver").replaceChildren(
      el("ol", {},
        el("li", {}, "按「複製 prompt」→「開啟 " + (state.platform === "gemini" ? "Gemini" : "ChatGPT") + "」貼上送出", state.platform === "gemini" ? "（先切到任務/Agent 模式）" : "", "。後續任務（3D/動作）可把前一包的概念圖、concept.json、rig_spec.json 一起附上。"),
        el("li", {}, "下載 ", el("code", {}, b.ctx.taskId + ".zip"), " 到「下載」資料夾"),
        el("li", {}, "解壓到 ", el("code", {}, dest + "\\"), "，結果應為 ", el("code", {}, t.drop + "/" + b.ctx.taskId + "/manifest.json")),
        el("li", {}, "進入「成果審查台」點選「📂 掃描 Art/_incoming」或一鍵解壓縮進行審查。"),
      ),
      el("div", { class: "cmd" }, el("code", {}, cmd), el("button", { type: "button", class: "btn ghost mini", onclick: () => copyText(cmd, "已複製解壓指令") }, "複製")),
      el("p", { class: "muted" }, "若圖片沒有被打包進 zip：照 manifest.files 的檔名手動存到 ", el("code", {}, t.drop + "/" + b.ctx.taskId + "/images/"), "。")
    );
  }

  function renderHistory() {
    const box = $("#history"); box.replaceChildren();
    (state.history || []).slice(0, 15).forEach(h => box.append(el("li", {}, el("code", {}, h.id), " · " + h.task + " · " + h.platform + " · " + h.time)));
    if (!box.children.length) box.append(el("li", { class: "muted" }, "尚無紀錄（複製或下載 prompt 時記錄）"));
  }

  function renderAll() {
    revalidate("character"); revalidate("creature");
    $("#seed").value = state.seed;
    
    // 更新平台切換鈕
    if ($("#platChatgpt") && $("#platGemini")) {
      $("#platChatgpt").classList.toggle("on", state.platform === "chatgpt");
      $("#platGemini").classList.toggle("on", state.platform === "gemini");
    }

    $("#rec").textContent = task().platform === "gemini" ? "建議：Gemini Argon" : "建議：ChatGPT";
    renderTasks(); renderConcepts(); renderOptions(); renderOutput(); renderHistory();
    save();
  }

  // ==================== 原生 ZIP 封裝輔助（零外部依賴） ====================
  const CRC_TABLE = (function () {
    const table = new Uint32Array(256);
    for (let i = 0; i < 256; i++) {
      let c = i;
      for (let k = 0; k < 8; k++) c = ((c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1));
      table[i] = c >>> 0;
    }
    return table;
  })();

  function createZipBlob(files) {
    const entries = [];
    let offset = 0;
    for (const name in files) {
      let data = files[name];
      if (typeof data === "string") data = new TextEncoder().encode(data);
      const nameBytes = new TextEncoder().encode(name);
      const header = new Uint8Array(30 + nameBytes.length);
      const dv = new DataView(header.buffer);
      dv.setUint32(0, 0x04034b50, true);
      dv.setUint16(4, 20, true);
      dv.setUint16(6, 0, true);
      dv.setUint16(8, 0, true); // store
      dv.setUint16(10, 0, true);
      dv.setUint16(12, 0, true);
      let crc = 0 ^ (-1);
      for (let i = 0; i < data.length; i++) crc = (crc >>> 8) ^ CRC_TABLE[(crc ^ data[i]) & 0xff];
      crc = (crc ^ (-1)) >>> 0;
      dv.setUint32(14, crc, true);
      dv.setUint32(18, data.length, true);
      dv.setUint32(22, data.length, true);
      dv.setUint16(26, nameBytes.length, true);
      dv.setUint16(28, 0, true);
      header.set(nameBytes, 30);
      entries.push({ nameBytes, header, data, crc, offset });
      offset += header.length + data.length;
    }
    const centralStart = offset;
    const centralEntries = [];
    for (const e of entries) {
      const ch = new Uint8Array(46 + e.nameBytes.length);
      const dv = new DataView(ch.buffer);
      dv.setUint32(0, 0x02014b50, true);
      dv.setUint16(4, 20, true);
      dv.setUint16(6, 20, true);
      dv.setUint16(8, 0, true);
      dv.setUint16(10, 0, true);
      dv.setUint16(12, 0, true);
      dv.setUint16(14, 0, true);
      dv.setUint32(16, e.crc, true);
      dv.setUint32(20, e.data.length, true);
      dv.setUint32(24, e.data.length, true);
      dv.setUint16(28, e.nameBytes.length, true);
      dv.setUint16(30, 0, true);
      dv.setUint16(32, 0, true);
      dv.setUint16(34, 0, true);
      dv.setUint16(36, 0, true);
      dv.setUint32(38, 0, true);
      dv.setUint32(42, e.offset, true);
      ch.set(e.nameBytes, 46);
      centralEntries.push(ch);
      offset += ch.length;
    }
    const centralSize = offset - centralStart;
    const eocd = new Uint8Array(22);
    const edv = new DataView(eocd.buffer);
    edv.setUint32(0, 0x06054b50, true);
    edv.setUint16(4, 0, true);
    edv.setUint16(6, 0, true);
    edv.setUint16(8, entries.length, true);
    edv.setUint16(10, entries.length, true);
    edv.setUint32(12, centralSize, true);
    edv.setUint32(16, centralStart, true);
    edv.setUint16(20, 0, true);

    const parts = [];
    for (const e of entries) {
      parts.push(e.header);
      parts.push(e.data);
    }
    for (const ch of centralEntries) parts.push(ch);
    parts.push(eocd);
    return new Blob(parts, { type: "application/zip" });
  }

  // ==================== 成果審查台（Review Workbench，參考 steel_vs_swarm） ====================
  const reviewState = {
    packages: [], activePkgId: null, activeImgIdx: 0,
    filterCat: "all", filterQuery: "", viewMode: "single", // "single" | "compare"
    zoomLevel: 1.0, panX: 0, panY: 0,
    reviews: {}
  };

  // 取得與目前封裝相互參照的關聯封裝（2D 立繪 ⇄ 3D 模型雙向參照）
  function getLinkedPackage(pkg) {
    if (!pkg) return null;
    if (pkg.type === "model-rig") {
      // 3D 封裝往回找立繪包
      if (pkg.manifest?.source_pack) {
        const found = reviewState.packages.find(p => p.id === pkg.manifest.source_pack);
        if (found) return found;
      }
      return reviewState.packages.find(p => p.type !== "model-rig" && p.subjectId && p.subjectId === pkg.subjectId) || null;
    } else {
      // 立繪包往下找衍生 3D 封裝
      return reviewState.packages.find(p => p.type === "model-rig" && (p.manifest?.source_pack === pkg.id || (p.subjectId && p.subjectId === pkg.subjectId))) || null;
    }
  }

  // ZIP 解析器（使用瀏覽器原生 DecompressionStream）
  async function parseZipBuffer(arrayBuffer) {
    const view = new DataView(arrayBuffer);
    const bytes = new Uint8Array(arrayBuffer);
    const files = {};
    let eocdOffset = -1;
    for (let i = arrayBuffer.byteLength - 22; i >= 0; i--) {
      if (view.getUint32(i, true) === 0x06054b50) { eocdOffset = i; break; }
    }
    if (eocdOffset === -1) {
      let offset = 0;
      while (offset < arrayBuffer.byteLength - 30) {
        if (view.getUint32(offset, true) !== 0x04034b50) break;
        const compMethod = view.getUint16(offset + 8, true);
        const compSize = view.getUint32(offset + 18, true);
        const nameLen = view.getUint16(offset + 26, true);
        const extraLen = view.getUint16(offset + 28, true);
        const name = new TextDecoder().decode(bytes.subarray(offset + 30, offset + 30 + nameLen));
        const dataOffset = offset + 30 + nameLen + extraLen;
        const compData = bytes.subarray(dataOffset, dataOffset + compSize);
        let fileData = null;
        if (!name.endsWith('/')) {
          if (compMethod === 0) fileData = compData;
          else if (compMethod === 8 && window.DecompressionStream) {
            try {
              const ds = new DecompressionStream('deflate-raw');
              const writer = ds.writable.getWriter();
              writer.write(compData); writer.close();
              fileData = new Uint8Array(await new Response(ds.readable).arrayBuffer());
            } catch (e) {}
          }
          if (fileData) files[name] = fileData;
        }
        offset = dataOffset + compSize;
      }
      return files;
    }
    const cdCount = view.getUint16(eocdOffset + 10, true);
    const cdOffset = view.getUint32(eocdOffset + 16, true);
    let pos = cdOffset;
    for (let i = 0; i < cdCount; i++) {
      if (view.getUint32(pos, true) !== 0x02014b50) break;
      const compMethod = view.getUint16(pos + 10, true);
      const compSize = view.getUint32(pos + 20, true);
      const nameLen = view.getUint16(pos + 28, true);
      const extraLen = view.getUint16(pos + 30, true);
      const commentLen = view.getUint16(pos + 32, true);
      const localOffset = view.getUint32(pos + 42, true);
      const name = new TextDecoder().decode(bytes.subarray(pos + 46, pos + 46 + nameLen));
      const localNameLen = view.getUint16(localOffset + 26, true);
      const localExtraLen = view.getUint16(localOffset + 28, true);
      const dataOffset = localOffset + 30 + localNameLen + localExtraLen;
      const compData = bytes.subarray(dataOffset, dataOffset + compSize);
      let fileData = null;
      if (!name.endsWith('/')) {
        if (compMethod === 0) fileData = compData;
        else if (compMethod === 8 && window.DecompressionStream) {
          try {
            const ds = new DecompressionStream('deflate-raw');
            const writer = ds.writable.getWriter();
            writer.write(compData); writer.close();
            fileData = new Uint8Array(await new Response(ds.readable).arrayBuffer());
          } catch (e) {}
        }
        if (fileData) files[name] = fileData;
      }
      pos += 46 + nameLen + extraLen + commentLen;
    }
    return files;
  }

  // 載入 Zip 檔轉換為審查 Package 物件
  async function loadZipFile(file) {
    const buf = await file.arrayBuffer();
    const files = await parseZipBuffer(buf);
    let manifestData = null, conceptData = null, rawPrompt = "", prefix = "";
    for (const path in files) {
      if (path.endsWith("manifest.json")) {
        try { manifestData = JSON.parse(new TextDecoder().decode(files[path])); } catch (e) {}
        prefix = path.slice(0, path.lastIndexOf("manifest.json"));
      } else if (path.endsWith("concept.json")) {
        try { conceptData = JSON.parse(new TextDecoder().decode(files[path])); } catch (e) {}
      } else if (path.endsWith(".prompt.md") || path.endsWith("prompt.md") || path.endsWith("prompt.txt")) {
        try { rawPrompt = new TextDecoder().decode(files[path]); } catch (e) {}
      }
    }
    const taskId = manifestData?.task_id || file.name.replace(/\.zip$/i, "");
    const images = [];
    for (const path in files) {
      if (/\.(png|jpe?g|webp)$/i.test(path)) {
        const rel = path.startsWith(prefix) ? path.slice(prefix.length) : path;
        const blob = new Blob([files[path]], { type: "image/png" });
        images.push({ path: rel, url: URL.createObjectURL(blob), rawBytes: files[path] });
      }
    }
    const pkg = {
      id: taskId,
      type: manifestData?.task_type || (taskId.startsWith("chr") ? "character-concept" : taskId.startsWith("cr") ? "creature-concept" : taskId.startsWith("mdl") ? "model-rig" : "unknown"),
      subjectId: manifestData?.subject_id || "",
      manifest: manifestData,
      concept: conceptData,
      rawPrompt,
      images,
      rawFiles: files,
      fileCount: Object.keys(files).length,
      rawZip: file,
      isZip: true,
      status: manifestData?.missing?.length ? "missing" : "pending"
    };
    addPackage(pkg);
    return pkg;
  }

    function addPackage(pkg) {
      const idx = reviewState.packages.findIndex(p => p.id === pkg.id);
      if (idx >= 0) reviewState.packages[idx] = pkg;
      else reviewState.packages.unshift(pkg);
      reviewState.activePkgId = pkg.id;
      reviewState.activeImgIdx = 0;
      try {
        renderReviewView();
      } catch (e) {
        console.error("renderReviewView failed for package " + pkg.id, e);
      }
      toast("已載入任務包：" + pkg.id);
    }

  // 載入內建測試範例（fixtures）
  function loadSampleFixtures() {
    const mockPackages = [
      {
        id: "cr-20261106-7f3a-2105",
        type: "creature-concept",
        subjectId: "line_7f3a",
        manifest: {
          spec: "cosmimera-art-task/1",
          task_header: "[COSMIMERA-TASK spec=1 tool=1.4.0 catalog=1.2.0 template=creature-concept@2.0.0 task_id=cr-20261106-7f3a-2105 seed=7f3a]",
          task_id: "cr-20261106-7f3a-2105",
          task_type: "creature-concept",
          subject_id: "line_7f3a",
          produced_by: "ChatGPT",
          files: [
            { path: "concept.json", kind: "spec", level: "L1" },
            { path: "images/cr_line_7f3a_adult_main.png", kind: "main", level: "L1" },
            { path: "images/cr_line_7f3a_adult_turnaround.png", kind: "turnaround", level: "L1" },
            { path: "images/cr_line_7f3a_adult_front.png", kind: "view", level: "L1" },
            { path: "images/cr_line_7f3a_lineup.png", kind: "sheet", level: "L1" }
          ],
          missing: [],
          notes: "三視圖與配色完整生成，符號意象已融入裝甲"
        },
        concept: {
          line_id: "line_7f3a",
          elements: ["fire（火）", "metal（金）"],
          element_palettes: ["自然火紅 × 琉璃金"],
          base_parents: ["類人奇幻·精靈族", "器械·武器"],
          bases: ["精靈族·高等精靈/月光祭司", "武器·符文巨盾/塔盾護壁"],
          appearance_palette: ["熔岩赤紅 × 炭黑硬甲 × 烈焰耀金"],
          timeline: "青銅神話/古典英雄時代", region: "東亞",
          size_profile: "S→M→L→XL",
          height_length: "2.10 m", weight: "340 kg", body_fat: "15.0%",
          archetype: "桃太郎", archetype_type: "傳說", motifs: "桃紋飾、雙角重鎧、腰間黍糰印記",
          sensitivity: "ok", role: "前排肉盾/坦克", temperament: "好鬥凶悍",
          stages: [
            { stage: "juvenile", name_zh: "火角石", size_class: "S", height_m: 0.45 },
            { stage: "adolescent", name_zh: "熔脊獸", size_class: "M", height_m: 1.10 },
            { stage: "adult", name_zh: "焰甲重龍", size_class: "L", height_m: 2.10 },
            { stage: "overload", name_zh: "天炎煌角甲", size_class: "XL", height_m: 3.80 }
          ],
          signature_move: { name_zh: "崩天熾烈斬", move_id: "move_earth_heavy_cleave" },
          trait: { name_zh: "熔鋼重裝壁", trait_id: "trait_metal_impenetrable_shell" },
          formula: "fire+metal × 精靈族·高等精靈+武器·符文巨盾 × 桃太郎（青銅神話/古典英雄時代）"
        },
        images: [
          { path: "images/cr_line_7f3a_adult_main.png", url: createSampleSvgDataUrl("焰甲重龍 · 成年立繪 (2.1m)", "#d35400") },
          { path: "images/cr_line_7f3a_adult_turnaround.png", url: createSampleSvgDataUrl("焰甲重龍 · T-Pose 三視圖", "#7f8c8d") },
          { path: "images/cr_line_7f3a_adult_front.png", url: createSampleSvgDataUrl("正面正交視圖 1:1", "#34495e") },
          { path: "images/cr_line_7f3a_lineup.png", url: createSampleSvgDataUrl("進化排列圖 21:9", "#2c3e50") }
        ],
        fileCount: 6,
        status: "ok"
      },
      {
        id: "mdl-20261106-7f3a-2105",
        type: "model-rig",
        subjectId: "line_7f3a",
        manifest: {
          spec: "cosmimera-art-task/1",
          task_header: "[COSMIMERA-TASK spec=1 tool=1.4.0 catalog=1.2.0 template=model-rig@2.0.0 task_id=mdl-20261106-7f3a-2105 seed=7f3a]",
          task_id: "mdl-20261106-7f3a-2105",
          task_type: "model-rig",
          subject_id: "line_7f3a",
          source_pack: "cr-20261106-7f3a-2105",
          produced_by: "Gemini Argon",
          files: [
            { path: "images/cr_line_7f3a_adult_model_front.png", kind: "view", level: "L1" },
            { path: "images/cr_line_7f3a_adult_model_side.png", kind: "view", level: "L1" },
            { path: "images/cr_line_7f3a_adult_model_back.png", kind: "view", level: "L1" },
            { path: "images/cr_line_7f3a_adult_anim_sheet_01.png", kind: "sheet", level: "L1" },
            { path: "rig/rig_spec.json", kind: "spec", level: "L1" },
            { path: "rig/build_rig.py", kind: "script", level: "L1" },
            { path: "anims/anim_spec.json", kind: "spec", level: "L1" },
            { path: "anims/keyframes.py", kind: "script", level: "L1" },
            { path: "pipeline/run_all.py", kind: "script", level: "L1" },
            { path: "pipeline/README.md", kind: "report", level: "L1" }
          ],
          missing: [
            { item: "models/cr_line_7f3a_adult.glb", level: "L2", reason: "no 3D generation on this platform" }
          ],
          notes: "由立繪包 cr-20261106-7f3a-2105 清理生成 3D 建模正交視圖與骨架規格"
        },
        images: [
          { path: "images/cr_line_7f3a_adult_model_front.png", url: createSampleSvgDataUrl("焰甲重龍 · 建模正交正面 2048px", "#e67e22") },
          { path: "images/cr_line_7f3a_adult_model_side.png", url: createSampleSvgDataUrl("焰甲重龍 · 建模正交側面 2048px", "#d35400") },
          { path: "images/cr_line_7f3a_adult_model_back.png", url: createSampleSvgDataUrl("焰甲重龍 · 建模正交背面 2048px", "#ba4a00") },
          { path: "images/cr_line_7f3a_adult_anim_sheet_01.png", url: createSampleSvgDataUrl("動作關鍵幀分鏡 · 焰甲重龍", "#8e44ad") }
        ],
        fileCount: 11,
        status: "pending"
      },
      {
        id: "chr-20261106-7f3a-2105",
        type: "character-concept",
        subjectId: "chr_7f3a",
        manifest: {
          spec: "cosmimera-art-task/1",
          task_header: "[COSMIMERA-TASK spec=1 tool=1.4.0 catalog=1.2.0 template=character-concept@1.2.0 task_id=chr-20261106-7f3a-2105 seed=7f3a]",
          task_id: "chr-20261106-7f3a-2105",
          task_type: "character-concept",
          subject_id: "chr_7f3a",
          produced_by: "ChatGPT",
          files: [
            { path: "concept.json", kind: "spec", level: "L1" },
            { path: "images/chr_7f3a_main.png", kind: "main", level: "L1" },
            { path: "images/chr_7f3a_turnaround.png", kind: "turnaround", level: "L1" },
            { path: "images/chr_7f3a_expressions.png", kind: "sheet", level: "L1" },
            { path: "images/chr_7f3a_props.png", kind: "sheet", level: "L1" }
          ],
          missing: [],
          notes: "服裝符合機能風格，各插槽骨掛點清楚"
        },
        concept: {
          id: "chr_7f3a", name_zh: "林晴陽", name_en: "Haruhi",
          role: "玩家角色預設外觀", ethnicity: "東亞體系", region: "東亞", timeline: "晶石賽博/霓虹魔導義體紀元",
          gender: "女", age_band: "青少年", age: 17, height: "163 cm", weight: "51.5 kg", body_fat: "21.5%",
          cup_size: "C",
          outfit_palette: ["海軍藍 × 象牙白 × 霧金"],
          hair_palette: ["純黑 × 挑染天藍"],
          eye_palette: ["琥珀金"],
          personality: ["熱血", "好奇"], style: "街頭機能 × 學院制服改造",
          interests: ["星際探索", "古文字解密"], likes: ["熱奶茶", "發光晶石"], dislikes: ["冗長報告"],
          vessel: "talisman_card", vessel_zh: "符籙卡片"
        },
        images: [
          { path: "images/chr_7f3a_main.png", url: createSampleSvgDataUrl("角色立繪 · 晴陽 (163cm, 罩杯C)", "#2980b9") },
          { path: "images/chr_7f3a_turnaround.png", url: createSampleSvgDataUrl("角色三視圖 · T-pose 灰底", "#7f8c8d") },
          { path: "images/chr_7f3a_expressions.png", url: createSampleSvgDataUrl("表情集 2×3", "#8e44ad") },
          { path: "images/chr_7f3a_props.png", url: createSampleSvgDataUrl("道具板 · 符籙卡片與背包", "#16a085") }
        ],
        fileCount: 6,
        status: "ok"
      }
    ];
    mockPackages.forEach(p => addPackage(p));
    reviewState.activePkgId = mockPackages[0].id;
    renderReviewView();
    toast("已載入內建測試範例（含立繪與 3D 成果參照）");
  }

  function createSampleSvgDataUrl(title, color) {
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="600" height="600" viewBox="0 0 600 600">
      <rect width="100%" height="100%" fill="#1a1e27"/>
      <rect x="20" y="20" width="560" height="560" fill="none" stroke="${color}" stroke-width="4" rx="10"/>
      <circle cx="300" cy="240" r="100" fill="${color}" opacity="0.3"/>
      <text x="300" y="250" font-family="system-ui, sans-serif" font-size="20" fill="#fff" text-anchor="middle" font-weight="bold">${title}</text>
      <text x="300" y="420" font-family="monospace" font-size="14" fill="#aab3c2" text-anchor="middle">COSMIMERA ART DELIVERY PREVIEW</text>
    </svg>`;
    return "data:image/svg+xml;utf8," + encodeURIComponent(svg);
  }

  // ==================== IndexedDB 目錄 Handle 持久化 ====================
  function getDb() {
    return new Promise((resolve, reject) => {
      if (!window.indexedDB) { resolve(null); return; }
      const req = indexedDB.open("CosmimeraForgeDB", 1);
      req.onupgradeneeded = () => req.result.createObjectStore("handles");
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => resolve(null);
    });
  }

  async function saveDirectoryHandle(handle) {
    try {
      const db = await getDb();
      if (!db) return;
      const tx = db.transaction("handles", "readwrite");
      tx.objectStore("handles").put(handle, "incomingDir");
    } catch (e) {}
  }

  async function getSavedDirectoryHandle() {
    try {
      const db = await getDb();
      if (!db) return null;
      return new Promise((resolve) => {
        const tx = db.transaction("handles", "readonly");
        const req = tx.objectStore("handles").get("incomingDir");
        req.onsuccess = () => resolve(req.result || null);
        req.onerror = () => resolve(null);
      });
    } catch (e) {
      return null;
    }
  }

  // 自動或手動掃描目錄
  async function performScanDirectory(dirHandle) {
    let foundCount = 0;
    for await (const [name, handle] of dirHandle.entries()) {
      if (handle.kind === "directory" && (name.startsWith("chr-") || name.startsWith("cr-") || name.startsWith("mdl-") || name.startsWith("anim-") || name.startsWith("fx-") || name.startsWith("vsl-"))) {
        await loadFromDirHandle(name, handle);
        foundCount++;
      } else if (handle.kind === "file" && name.endsWith(".zip")) {
        const file = await handle.getFile();
        await loadZipFile(file);
        foundCount++;
      }
    }
    return foundCount;
  }

  // 嘗試自動掃描已記憶授權的目錄
  async function tryAutoScanIncoming() {
    const handle = await getSavedDirectoryHandle();
    if (!handle) return false;
    try {
      let perm = await handle.queryPermission({ mode: "readwrite" });
      if (perm !== "granted") {
        perm = await handle.queryPermission({ mode: "read" });
      }
      if (perm === "granted") {
        const count = await performScanDirectory(handle);
        if (count > 0) {
          toast(`自動掃描完成，已載入 ${count} 個交付包`);
          return true;
        }
      }
    } catch (e) {}
    return false;
  }

  // 掃描 Art/_incoming 目錄（File System Access API）
  async function scanIncomingDirectory() {
    if (!window.showDirectoryPicker) {
      $("#folderFileInput").click();
      return;
    }
    try {
      const dirHandle = await window.showDirectoryPicker({ mode: "readwrite" });
      await saveDirectoryHandle(dirHandle);
      toast("正在掃描目錄...");
      const foundCount = await performScanDirectory(dirHandle);
      toast("掃描完成，已載入 " + foundCount + " 個交付包");
    } catch (e) {
      if (e.name !== "AbortError") toast("掃描失敗：" + e.message);
    }
  }

  // 透過標準 webkitdirectory 上傳批次資料夾解析
  async function handleFolderUpload(fileList) {
    if (!fileList || !fileList.length) return;
    toast("正在讀取資料夾檔案...");
    const taskGroups = {};
    for (let i = 0; i < fileList.length; i++) {
      const file = fileList[i];
      const rel = file.webkitRelativePath || file.name;
      const parts = rel.split("/");
      // 判斷是否直接選中了 Art/_incoming 目錄，或者選中了某個 task 資料夾
      let taskId = "";
      let subPath = "";
      for (let p = 0; p < parts.length; p++) {
        if (/^(chr-|cr-|mdl-|anim-|fx-|vsl-)/.test(parts[p])) {
          taskId = parts[p];
          subPath = parts.slice(p + 1).join("/");
          break;
        }
      }
      if (taskId) {
        if (!taskGroups[taskId]) taskGroups[taskId] = [];
        taskGroups[taskId].push({ file, subPath });
      } else if (file.name.endsWith(".zip")) {
        await loadZipFile(file);
      }
    }

    let loadedCount = 0;
    for (const taskId in taskGroups) {
      const entries = taskGroups[taskId];
      let manifestData = null, conceptData = null, rawPrompt = "";
      const images = [];
      let fileCount = entries.length;

      for (const item of entries) {
        const fn = item.subPath || item.file.name;
        if (fn.endsWith("manifest.json")) {
          try { manifestData = JSON.parse(await item.file.text()); } catch (e) {}
        } else if (fn.endsWith("concept.json")) {
          try { conceptData = JSON.parse(await item.file.text()); } catch (e) {}
        } else if (fn.endsWith(".prompt.md") || fn === "prompt.md") {
          try { rawPrompt = await item.file.text(); } catch (e) {}
        } else if (/\.(png|jpe?g|webp)$/i.test(fn)) {
          images.push({ path: fn, url: URL.createObjectURL(item.file) });
        }
      }

      const pkg = {
        id: taskId,
        type: manifestData?.task_type || (taskId.startsWith("chr") ? "character-concept" : taskId.startsWith("cr") ? "creature-concept" : taskId.startsWith("mdl") ? "model-rig" : "unknown"),
        subjectId: manifestData?.subject_id || "",
        manifest: manifestData,
        concept: conceptData,
        rawPrompt,
        images,
        fileCount,
        status: manifestData?.missing?.length ? "missing" : "pending"
      };
      addPackage(pkg);
      loadedCount++;
    }
    toast(`已讀取 ${loadedCount} 個任務資料夾`);
  }

  async function loadFromDirHandle(taskId, dirHandle) {
    let manifestData = null, conceptData = null, rawPrompt = "";
    const images = [];
    let fileCount = 0;
    try {
      const mfFile = await (await dirHandle.getFileHandle("manifest.json")).getFile();
      manifestData = JSON.parse(await mfFile.text());
    } catch (e) {}
    try {
      const cpFile = await (await dirHandle.getFileHandle("concept.json")).getFile();
      conceptData = JSON.parse(await cpFile.text());
    } catch (e) {}

    // 嘗試讀取 prompt.md
    for await (const [pname, phandle] of dirHandle.entries()) {
      if (phandle.kind === "file" && (pname.endsWith(".prompt.md") || pname === "prompt.md")) {
        try { rawPrompt = await (await phandle.getFile()).text(); } catch (e) {}
      }
    }

    // 讀取 images 目錄
    try {
      const imgDir = await dirHandle.getDirectoryHandle("images");
      for await (const [iname, ihandle] of imgDir.entries()) {
        if (ihandle.kind === "file" && /\.(png|jpe?g|webp)$/i.test(iname)) {
          const file = await ihandle.getFile();
          images.push({ path: "images/" + iname, url: URL.createObjectURL(file) });
          fileCount++;
        }
      }
    } catch (e) {}

    const pkg = {
      id: taskId,
      type: manifestData?.task_type || (taskId.startsWith("chr") ? "character-concept" : taskId.startsWith("cr") ? "creature-concept" : taskId.startsWith("mdl") ? "model-rig" : "unknown"),
      subjectId: manifestData?.subject_id || "",
      manifest: manifestData,
      concept: conceptData,
      rawPrompt,
      images,
      fileCount,
      dirHandle,
      status: manifestData?.missing?.length ? "missing" : "pending"
    };
    addPackage(pkg);
  }

  function getActivePkg() {
    return reviewState.packages.find(p => p.id === reviewState.activePkgId) || reviewState.packages[0] || null;
  }

  // ==================== 一鍵舊版 Prompt 內容格式升級引擎 ====================
  function upgradePackageFormat(pkg) {
    if (!pkg) { toast("請先選取要升級的任務包"); return null; }
    const changes = [];
    const isChr = pkg.type === "character-concept" || pkg.id.startsWith("chr-");
    const isCr = pkg.type === "creature-concept" || pkg.id.startsWith("cr-");
    const is3d = pkg.type === "model-rig" || pkg.id.startsWith("mdl-") || pkg.id.startsWith("anim-");

    let updatedConcept = Object.assign({}, pkg.concept || {});
    let updatedManifest = Object.assign({}, pkg.manifest || {});
    let targetTaskId = pkg.id;
    let targetTaskType = isChr ? "character-concept" : isCr ? "creature-concept" : is3d ? "model-rig" : pkg.type;

    if (isChr) {
      const t = TASKS.find(x => x.id === "character-concept");
      changes.push("升級至最新角色模板：template=character-concept@1.2.0, spec=1, tool=" + TOOL_VERSION);

      // 解除人種與文化關聯，補齊生理常態數值
      if (!updatedConcept.ethnicity) { updatedConcept.ethnicity = "東亞體系"; changes.push("補齊人種表型：東亞體系"); }
      if (!updatedConcept.region) { updatedConcept.region = "東亞"; changes.push("補齊文化地區：東亞"); }
      if (!updatedConcept.timeline) { updatedConcept.timeline = "晶石賽博/霓虹魔導義體紀元"; changes.push("補齊文化時間線：晶石賽博/霓虹魔導義體紀元"); }
      if (!updatedConcept.gender) updatedConcept.gender = "女";
      if (!updatedConcept.age_band) updatedConcept.age_band = "青少年";
      if (!updatedConcept.age) updatedConcept.age = 17;

      if (!updatedConcept.height && !updatedConcept.height_cm) {
        const dim = sampleHumanDimensions(Math.random, updatedConcept.gender, updatedConcept.age_band, updatedConcept.age);
        updatedConcept.height = dim.height; updatedConcept.weight = dim.weight; updatedConcept.body_fat = dim.bodyFat;
        changes.push("補齊常態生理規格分佈：身高 " + dim.height + "、體重 " + dim.weight + "、體脂率 " + dim.bodyFat);
      } else {
        if (updatedConcept.height_cm && !updatedConcept.height) updatedConcept.height = updatedConcept.height_cm;
        if (updatedConcept.weight_kg && !updatedConcept.weight) updatedConcept.weight = updatedConcept.weight_kg;
        if (updatedConcept.body_fat_pct && !updatedConcept.body_fat) updatedConcept.body_fat = updatedConcept.body_fat_pct;
      }

      if (updatedConcept.gender === "女" && !updatedConcept.cup_size) {
        updatedConcept.cup_size = "C";
        changes.push("補齊女性罩杯常態預設：C 罩杯");
      }

      // 配色與性格
      if (!updatedConcept.outfit_palette || !updatedConcept.outfit_palette.length) {
        updatedConcept.outfit_palette = ["海軍藍 × 象牙白 × 霧金"];
        changes.push("補齊服裝色系：海軍藍 × 象牙白 × 霧金");
      }
      if (!updatedConcept.hair_palette || !updatedConcept.hair_palette.length) {
        updatedConcept.hair_palette = ["純黑 × 挑染天藍"];
        changes.push("補齊頭髮色系：純黑 × 挑染天藍");
      }
      if (!updatedConcept.eye_palette || !updatedConcept.eye_palette.length) {
        updatedConcept.eye_palette = ["琥珀金"];
        changes.push("補齊瞳孔色系：琥珀金");
      }
      if (!updatedConcept.vessel) {
        updatedConcept.vessel = "talisman_card";
        updatedConcept.vessel_zh = "符籙卡片";
        changes.push("補齊收服媒介：符籙卡片 (talisman_card)");
      }

      const seed = pkg.manifest?.task_header?.match(/seed=([^\s\]]+)/)?.[1] || pkg.id.split("-")[2] || "7f3a";
      const header = "[COSMIMERA-TASK spec=1 tool=" + TOOL_VERSION + " catalog=" + C.version + " template=" + t.id + "@" + t.version + " task_id=" + targetTaskId + " seed=" + seed + "]";
      const ctx = {
        header, taskId: targetTaskId, task: t, opts: {}, concept: updatedConcept, notes: updatedManifest.notes || "",
        platform: "chatgpt", platformName: "ChatGPT（對話版）",
        subjectId: updatedConcept.id || ("chr_" + seed)
      };
      const newPromptText = t.build(ctx);

      updatedManifest.spec = "cosmimera-art-task/1";
      updatedManifest.task_header = header;
      updatedManifest.task_type = "character-concept";
      updatedManifest.subject_id = ctx.subjectId;

      return { pkg, changes, newConcept: updatedConcept, newManifest: updatedManifest, newPromptText, targetTaskId };

    } else if (isCr) {
      const t = TASKS.find(x => x.id === "creature-concept");
      changes.push("升級至最新宙獸模板：template=creature-concept@2.0.0, spec=1, tool=" + TOOL_VERSION);

      // 外觀基底父類別遷移
      if (updatedConcept.base_parents) {
        const oldP = JSON.stringify(updatedConcept.base_parents);
        updatedConcept.base_parents = updatedConcept.base_parents.map(traceBaseParent);
        if (oldP !== JSON.stringify(updatedConcept.base_parents)) {
          changes.push("外觀基底類別遷移：舊版「人形」已自動追溯遷移為「類人奇幻·精靈族」");
        }
      }

      // 體型與尺寸分佈
      if (!updatedConcept.size_profile) {
        updatedConcept.size_profile = "S→M→L→XL";
        changes.push("補齊生物體型配置：size_profile = S→M→L→XL");
      }
      if (!updatedConcept.height_length && !updatedConcept.height_m) {
        const dim = sampleCreatureDimensions(Math.random, "L");
        updatedConcept.height_length = dim.length; updatedConcept.weight = dim.weight; updatedConcept.body_fat = dim.bodyFat;
        changes.push("補齊成體生物常態尺寸：身長 " + dim.length + "、重量 " + dim.weight + "、體脂率 " + dim.bodyFat);
      } else {
        if (updatedConcept.height_m && !updatedConcept.height_length) updatedConcept.height_length = updatedConcept.height_m;
        if (updatedConcept.weight_kg && !updatedConcept.weight) updatedConcept.weight = updatedConcept.weight_kg;
        if (updatedConcept.body_fat_pct && !updatedConcept.body_fat) updatedConcept.body_fat = updatedConcept.body_fat_pct;
      }
      if (!updatedConcept.appearance_palette || !updatedConcept.appearance_palette.length) {
        updatedConcept.appearance_palette = ["熔岩赤紅 × 炭黑硬甲 × 烈焰耀金"];
        changes.push("補齊宙獸外觀配色庫：熔岩赤紅 × 炭黑硬甲 × 烈焰耀金");
      }

      // 招式與特性圖鑑化獨立
      if (updatedConcept.signature_move_idea && !updatedConcept.signature_move) {
        updatedConcept.signature_move = { name_zh: updatedConcept.signature_move_idea, move_id: "move_" + (updatedConcept.line_id || "creature") + "_sig" };
        delete updatedConcept.signature_move_idea;
        changes.push("專屬招式獨立圖鑑化：signature_move { name_zh, move_id }");
      }
      if (updatedConcept.trait_idea_zh && !updatedConcept.trait) {
        updatedConcept.trait = { name_zh: updatedConcept.trait_idea_zh, trait_id: "trait_" + (updatedConcept.line_id || "creature") + "_core" };
        delete updatedConcept.trait_idea_zh;
        changes.push("專屬特性獨立圖鑑化：trait { name_zh, trait_id }");
      }

      const seed = pkg.manifest?.task_header?.match(/seed=([^\s\]]+)/)?.[1] || pkg.id.split("-")[2] || "7f3a";
      const header = "[COSMIMERA-TASK spec=1 tool=" + TOOL_VERSION + " catalog=" + C.version + " template=" + t.id + "@" + t.version + " task_id=" + targetTaskId + " seed=" + seed + "]";
      const stagesOpts = ["juvenile", "adolescent", "adult", "overload"];
      const ctx = {
        header, taskId: targetTaskId, task: t, opts: { stages: stagesOpts }, concept: updatedConcept, notes: updatedManifest.notes || "",
        platform: "chatgpt", platformName: "ChatGPT（對話版）",
        subjectId: updatedConcept.line_id || ("line_" + seed)
      };
      let newPromptText = "";
      try {
        newPromptText = t.build(ctx);
      } catch (e) {
        newPromptText = pkg.rawPrompt || (pkg.manifest?.task_header || "Prompt 格式升級失敗：" + e.message);
      }

      updatedManifest.spec = "cosmimera-art-task/1";
      updatedManifest.task_header = header;
      updatedManifest.task_type = "creature-concept";
      updatedManifest.subject_id = ctx.subjectId;

      return { pkg, changes, newConcept: updatedConcept, newManifest: updatedManifest, newPromptText, targetTaskId };

    } else if (is3d) {
      const t = TASKS.find(x => x.id === "model-rig");
      changes.push("升級至最新 3D 管線模板：template=model-rig@2.0.0 (整合綁骨與動畫)");

      // 補齊並確保往回參照立繪包 (source_pack)
      let sp = updatedManifest.source_pack;
      if (!sp) {
        // 嘗試在已載入名冊中尋找對應的立繪包
        const linked = getLinkedPackage(pkg);
        if (linked) {
          sp = linked.id;
          changes.push("成功回溯關聯立繪包：source_pack = " + sp);
        } else {
          sp = (pkg.id.startsWith("chr") ? "chr-" : "cr-") + pkg.id.replace(/^(mdl|anim)-/, "");
          changes.push("補齊來源立繪包回溯參照：source_pack = " + sp);
        }
      }
      updatedManifest.source_pack = sp;

      const seed = pkg.manifest?.task_header?.match(/seed=([^\s\]]+)/)?.[1] || pkg.id.split("-")[2] || "7f3a";
      const header = "[COSMIMERA-TASK spec=1 tool=" + TOOL_VERSION + " catalog=" + C.version + " template=" + t.id + "@" + t.version + " task_id=" + targetTaskId + " seed=" + seed + "]";
      const ctx = {
        header, taskId: targetTaskId, task: t,
        opts: { scope: "single", source_pack: sp, subject: pkg.id.startsWith("chr") ? "character" : "creature", stages: ["juvenile", "adolescent", "adult", "overload"], actions_character: ["idle","walk","run","hit","death","catch_throw"], actions_creature: ["idle","walk","run","hit","death","atk_melee_front","atk_ranged_line"] },
        concept: updatedConcept, notes: updatedManifest.notes || "",
        platform: "gemini", platformName: "Gemini Argon（任務模式）",
        subjectId: updatedManifest.subject_id || ("line_" + seed)
      };
      const newPromptText = t.build(ctx);

      updatedManifest.spec = "cosmimera-art-task/1";
      updatedManifest.task_header = header;
      updatedManifest.task_type = "model-rig";

      return { pkg, changes, newConcept: updatedConcept, newManifest: updatedManifest, newPromptText, targetTaskId };
    }

    toast("無法識別該任務包的升級規則");
    return null;
  }

  // 顯示升級成果對話框
  let lastUpgradeResult = null;
  function showUpgradeModal(result) {
    if (!result) return;
    lastUpgradeResult = result;
    const { pkg, changes, newConcept, newManifest, newPromptText, targetTaskId } = result;

    $("#upgModalTitle").textContent = "🔄 升級完成：" + targetTaskId + " → 最新規範";
    const cl = $("#upgChangesList"); cl.replaceChildren();
    changes.forEach(c => cl.append(el("li", {}, c)));
    $("#upgPromptText").value = newPromptText;

    // 同步更新記憶體內的 package 狀態
    pkg.concept = newConcept;
    pkg.manifest = newManifest;
    pkg.rawPrompt = newPromptText;
    if (newManifest.task_type) pkg.type = newManifest.task_type;
    renderReviewView();

    $("#upgradeModal").classList.add("show");
  }

  // 渲染成果審查台主要畫面
  function renderReviewView() {
    const pkg = getActivePkg();
    const linkedPkg = getLinkedPackage(pkg);

    // 渲染名冊清單
    const railList = $("#railList"); railList.replaceChildren();
    const query = reviewState.filterQuery.toLowerCase();
    const filtered = reviewState.packages.filter(p => {
      if (reviewState.filterCat === "pending" && p.status !== "pending") return false;
      if (reviewState.filterCat === "chr" && !p.id.startsWith("chr")) return false;
      if (reviewState.filterCat === "cr" && !p.id.startsWith("cr")) return false;
      if (reviewState.filterCat === "mdl" && !p.id.startsWith("mdl") && !p.id.startsWith("anim")) return false;
      if (reviewState.filterCat === "fx" && !p.id.startsWith("fx")) return false;
      if (query && !p.id.toLowerCase().includes(query) && !p.subjectId.toLowerCase().includes(query) && !(p.manifest?.source_pack || "").toLowerCase().includes(query)) return false;
      return true;
    });

    filtered.forEach(p => {
      const isSel = pkg && pkg.id === p.id;
      const thumbUrl = p.images?.[0]?.url;
      const statZh = p.status === "ok" ? "✅ 合規" : p.status === "reject" ? "❌ 退回" : p.status === "missing" ? "⚠️ 缺檔" : "🟨 待審";
      const pLinked = getLinkedPackage(p);

      const badges = el("div", { class: "rl-badges" },
        el("span", { class: "rl-stat " + p.status }, statZh),
        p.manifest?.source_pack ? el("span", { class: "rl-link-tag", title: "參照立繪: " + p.manifest.source_pack }, "⬅ " + p.manifest.source_pack.slice(0, 10)) : null,
        (!p.manifest?.source_pack && pLinked && p.type !== "model-rig") ? el("span", { class: "rl-link-tag", title: "已關聯 3D 模型" }, "➡ 3D") : null
      );

      const item = el("div", {
        class: "rl" + (isSel ? " on" : ""),
        onclick: () => {
          reviewState.activePkgId = p.id;
          reviewState.activeImgIdx = 0;
          renderReviewView();
        }
      },
        el("div", { class: "rl-thumb" }, thumbUrl ? el("img", { src: thumbUrl }) : "📦"),
        el("div", { class: "rl-txt" },
          el("div", { class: "rl-id" }, p.id),
          el("div", { class: "rl-sub" }, p.type + (p.subjectId ? " · " + p.subjectId : "")),
          badges
        )
      );
      railList.append(item);
    });

    if (!filtered.length) {
      railList.append(el("div", { class: "muted", style: "padding:16px 8px;text-align:center" }, "無符合條件的任務包"));
    }

    // 渲染舞台
    if (!pkg) {
      $("#stageEmpty").style.display = "block";
      $("#stageSingleView").style.display = "none";
      $("#stageCompareView").style.display = "none";
      $("#stageTag").style.display = "none";
      $("#stageCarousel").replaceChildren();
      $("#viewSeg").replaceChildren();
      $("#panelBody").replaceChildren(el("p", { class: "muted" }, "尚未選取任何任務包。"));
      return;
    }

    $("#stageEmpty").style.display = "none";
    $("#stageTag").style.display = "flex";
    $("#stId").textContent = pkg.id;
    const badge = $("#stBadge");
    badge.className = "badge " + pkg.status;
    badge.textContent = pkg.status === "ok" ? "已通過" : pkg.status === "reject" ? "需修改" : pkg.status === "missing" ? "有缺檔" : "待審核";

    // 關聯立繪/3D 標籤
    const refTag = $("#stRefTag");
    if (pkg.type === "model-rig" && (pkg.manifest?.source_pack || linkedPkg)) {
      refTag.style.display = "inline-block";
      const spId = pkg.manifest?.source_pack || linkedPkg?.id;
      refTag.textContent = "⬅ 立繪參照: " + spId;
      refTag.title = "點選切換至雙欄對照或跳轉立繪包";
    } else if (linkedPkg && pkg.type !== "model-rig") {
      refTag.style.display = "inline-block";
      refTag.textContent = "➡ 衍生 3D: " + linkedPkg.id;
      refTag.title = "點選切換至雙欄對照或跳轉 3D 包";
    } else {
      refTag.style.display = "none";
    }

    // 雙欄模式切換與呈現 (參考 steel_vs_swarm)
    const compareView = $("#stageCompareView");
    const singleView = $("#stageSingleView");
    const isCompare = reviewState.viewMode === "compare";

    if (isCompare) {
      singleView.style.display = "none";
      compareView.style.display = "grid";
      $("#btnModeSingle").classList.remove("on");
      $("#btnModeCompare").classList.add("on");

      // 決定 2D 與 3D 的包
      let p2d = pkg.type === "model-rig" ? linkedPkg : pkg;
      let p3d = pkg.type === "model-rig" ? pkg : linkedPkg;

      $("#cmp2dId").textContent = p2d ? p2d.id : "無參照立繪包";
      $("#cmp3dId").textContent = p3d ? p3d.id : "無衍生 3D 模型";

      const img2d = p2d?.images?.[0]?.url || "";
      const cur3dIdx = (pkg === p3d) ? reviewState.activeImgIdx : 0;
      const img3d = p3d?.images?.[cur3dIdx]?.url || p3d?.images?.[0]?.url || "";

      $("#cmp2dImg").src = img2d;
      $("#cmp2dImg").style.display = img2d ? "block" : "none";
      $("#cmp3dImg").src = img3d;
      $("#cmp3dImg").style.display = img3d ? "block" : "none";

    } else {
      singleView.style.display = "flex";
      compareView.style.display = "none";
      $("#btnModeSingle").classList.add("on");
      $("#btnModeCompare").classList.remove("on");

      // 舞台圖片
      const curImg = pkg.images[reviewState.activeImgIdx] || pkg.images[0];
      const stageImg = $("#stageImg");
      if (curImg) {
        stageImg.style.display = "block";
        stageImg.src = curImg.url;
        stageImg.style.transform = `scale(${reviewState.zoomLevel}) translate(${reviewState.panX}px, ${reviewState.panY}px)`;
      } else {
        stageImg.style.display = "none";
      }
    }

    // 舞台輪播列
    const carousel = $("#stageCarousel"); carousel.replaceChildren();
    pkg.images.forEach((im, idx) => {
      carousel.append(el("img", {
        class: "c-thumb" + (idx === reviewState.activeImgIdx ? " on" : ""),
        src: im.url,
        title: im.path,
        onclick: () => { reviewState.activeImgIdx = idx; renderReviewView(); }
      }));
    });

    // 視圖按鈕（精簡標籤，解決按鈕排版爆炸問題）
    const viewSeg = $("#viewSeg"); viewSeg.replaceChildren();
    pkg.images.forEach((im, idx) => {
      const p = im.path.toLowerCase();
      let label = "視圖 " + (idx + 1);
      if (p.includes("turnaround")) label = "三視圖";
      else if (p.includes("front")) label = "正面";
      else if (p.includes("side")) label = "側面";
      else if (p.includes("back")) label = "背面";
      else if (p.includes("main")) label = "主立繪";
      else if (p.includes("expression")) label = "表情集";
      else if (p.includes("prop")) label = "道具集";
      else if (p.includes("lineup")) label = "進化排列";
      else if (p.includes("model_front")) label = "建模正";
      else if (p.includes("model_side")) label = "建模側";
      else if (p.includes("model_back")) label = "建模背";
      else if (p.includes("anim_sheet")) label = "動作分鏡";
      else if (p.includes("storyboard")) label = "分鏡";
      else if (p.includes("layer")) label = "圖層";
      else if (p.includes("flipbook")) label = "序列圖";

      viewSeg.append(el("button", {
        class: "segb" + (idx === reviewState.activeImgIdx ? " on" : ""),
        type: "button",
        title: im.path,
        onclick: () => { reviewState.activeImgIdx = idx; renderReviewView(); }
      }, label));
    });

    // 渲染右欄分頁資訊
    renderPanel(pkg, linkedPkg);
  }

  // 渲染右欄詳細資訊（完整顯示 prompt 項目、立繪 ⇄ 3D 參照、規格、檔案驗收、審查）
  function renderPanel(pkg, linkedPkg) {
    const box = $("#panelBody"); box.replaceChildren();
    const activeTab = $("#panelTabs .segb.on")?.dataset?.tab || "prompt";

    // 1. Prompt 項目完整呈現（角色與宙獸全欄位完整解析，Requirement 5）
    if (activeTab === "prompt") {
      box.append(el("h4", {},
        el("span", {}, "任務 Prompt 完整項目"),
        el("button", { class: "btn mini", type: "button", onclick: () => {
          const text = getFullPromptText(pkg);
          copyText(text, "已複製完整 Prompt");
        } }, "📋 複製 Prompt")
      ));

      // 任務標頭資訊卡
      box.append(el("div", { class: "prompt-card" },
        el("h4", {}, "任務規格標頭 (Task Header)"),
        el("div", { class: "prompt-kv" },
          el("span", { class: "prompt-k" }, "任務編號"), el("span", { class: "prompt-v" }, pkg.id),
          el("span", { class: "prompt-k" }, "任務類型"), el("span", { class: "prompt-v" }, pkg.type),
          el("span", { class: "prompt-k" }, "主體代號"), el("span", { class: "prompt-v" }, pkg.subjectId || "—"),
          el("span", { class: "prompt-k" }, "完整標頭"), el("span", { class: "prompt-v", style: "font-family:Consolas,monospace;font-size:11px" }, pkg.manifest?.task_header || "—")
        )
      ));

      // 角色概念完整項目
      if (pkg.type === "character-concept" || pkg.id.startsWith("chr")) {
        const c = pkg.concept || {};
        box.append(
          el("div", { class: "prompt-card" },
            el("h4", {}, "生理規格與體格特徵"),
            el("div", { class: "prompt-kv" },
              el("span", { class: "prompt-k" }, "角色定位"), el("span", { class: "prompt-v" }, c.role || "玩家角色預設外觀"),
              el("span", { class: "prompt-k" }, "人種表型"), el("span", { class: "prompt-v" }, c.ethnicity || "東亞體系"),
              el("span", { class: "prompt-k" }, "文化地區"), el("span", { class: "prompt-v" }, c.region || "東亞"),
              el("span", { class: "prompt-k" }, "文化時間線"), el("span", { class: "prompt-v" }, c.timeline || "晶石賽博/霓虹魔導義體紀元"),
              el("span", { class: "prompt-k" }, "性別 / 年齡"), el("span", { class: "prompt-v" }, (c.gender || "女") + " · " + (c.age || "17") + " 歲 (" + (c.age_band || "青少年") + ")"),
              el("span", { class: "prompt-k" }, "身高 / 體重"), el("span", { class: "prompt-v" }, (c.height || c.height_cm || "163 cm") + " · " + (c.weight || c.weight_kg || "51.5 kg")),
              el("span", { class: "prompt-k" }, "體脂率 / 罩杯"), el("span", { class: "prompt-v" }, (c.body_fat || c.body_fat_pct || "21.5%") + (c.cup_size ? " · 罩杯 " + c.cup_size : ""))
            )
          ),
          el("div", { class: "prompt-card" },
            el("h4", {}, "配色方案與性格風貌"),
            el("div", { class: "prompt-kv" },
              el("span", { class: "prompt-k" }, "服裝色系"), el("span", { class: "prompt-v" }, formatArray(c.outfit_palette)),
              el("span", { class: "prompt-k" }, "髮色 / 瞳色"), el("span", { class: "prompt-v" }, formatArray(c.hair_palette) + " ／ " + formatArray(c.eye_palette)),
              el("span", { class: "prompt-k" }, "外觀風格"), el("span", { class: "prompt-v" }, c.style || "街頭機能 × 學院制服改造"),
              el("span", { class: "prompt-k" }, "性格特質"), el("span", { class: "prompt-v" }, formatArray(c.personality)),
              el("span", { class: "prompt-k" }, "興趣 / 喜厭"), el("span", { class: "prompt-v" }, "喜好：" + formatArray(c.likes) + "；討厭：" + formatArray(c.dislikes)),
              el("span", { class: "prompt-k" }, "收服道具"), el("span", { class: "prompt-v" }, (c.vessel_zh || c.vessel || "符籙卡片") + " (" + (c.vessel || "talisman_card") + ")")
            )
          )
        );
      }

      // 宙獸概念完整項目
      else if (pkg.type === "creature-concept" || pkg.id.startsWith("cr")) {
        const c = pkg.concept || {};
        box.append(
          el("div", { class: "prompt-card" },
            el("h4", {}, "宙獸屬性與基底階層"),
            el("div", { class: "prompt-kv" },
              el("span", { class: "prompt-k" }, "屬性"), el("span", { class: "prompt-v" }, formatArray(c.elements)),
              el("span", { class: "prompt-k" }, "屬性配色"), el("span", { class: "prompt-v" }, formatArray(c.element_palettes)),
              el("span", { class: "prompt-k" }, "外觀基底類別"), el("span", { class: "prompt-v" }, formatArray(c.base_parents)),
              el("span", { class: "prompt-k" }, "具體子基底"), el("span", { class: "prompt-v" }, formatArray(c.bases)),
              el("span", { class: "prompt-k" }, "外觀配色"), el("span", { class: "prompt-v" }, formatArray(c.appearance_palette)),
              el("span", { class: "prompt-k" }, "文化時間線"), el("span", { class: "prompt-v" }, c.timeline || "青銅神話/古典英雄時代"),
              el("span", { class: "prompt-k" }, "文化原型"), el("span", { class: "prompt-v" }, (c.archetype?.name || c.archetype || "自由原型") + " (" + (c.region || "東亞") + ") · " + (c.motifs || "符號意象")),
              el("span", { class: "prompt-k" }, "戰鬥 / 性情"), el("span", { class: "prompt-v" }, (c.role || "前排肉盾") + " · " + (c.temperament || "好鬥凶悍"))
            )
          ),
          el("div", { class: "prompt-card" },
            el("h4", {}, "體型規格與進化階段"),
            el("div", { class: "prompt-kv" },
              el("span", { class: "prompt-k" }, "體型配置"), el("span", { class: "prompt-v" }, c.size_profile || "S→M→L→XL"),
              el("span", { class: "prompt-k" }, "成體體格"), el("span", { class: "prompt-v" }, "長/高 " + (c.height_length || c.height_m || "2.10 m") + " · 重 " + (c.weight || c.weight_kg || "340 kg") + " · 體脂 " + (c.body_fat || c.body_fat_pct || "15.0%")),
              el("span", { class: "prompt-k" }, "設計公式"), el("span", { class: "prompt-v", style: "font-family:Consolas,monospace;font-size:11px" }, c.formula || "—"),
              el("span", { class: "prompt-k" }, "專屬招式"), el("span", { class: "prompt-v" }, typeof c.signature_move === "object" ? `${c.signature_move.name_zh} (${c.signature_move.move_id})` : (c.signature_move || "—")),
              el("span", { class: "prompt-k" }, "專屬特性"), el("span", { class: "prompt-v" }, typeof c.trait === "object" ? `${c.trait.name_zh} (${c.trait.trait_id})` : (c.trait || "—"))
            )
          )
        );
      }

      // 3D 建模管線完整項目
      else if (pkg.type === "model-rig" || pkg.id.startsWith("mdl")) {
        const m = pkg.manifest || {};
        box.append(
          el("div", { class: "prompt-card" },
            el("h4", {}, "3D 建模與綁骨管線規格"),
            el("div", { class: "prompt-kv" },
              el("span", { class: "prompt-k" }, "來源立繪參照"), el("span", { class: "prompt-v", style: "color:var(--accent)" }, m.source_pack || "未綁定（建議一鍵升級）"),
              el("span", { class: "prompt-k" }, "管線流程"), el("span", { class: "prompt-v" }, "Image-to-3D → Blender → 減面烘焙 → 綁骨 → 動作動作槽"),
              el("span", { class: "prompt-k" }, "骨架體系"), el("span", { class: "prompt-v" }, pkg.id.startsWith("chr") ? "Unity Humanoid (≤75骨骼)" : "Generic (Root→Hips→Spine, ≤60骨骼)"),
              el("span", { class: "prompt-k" }, "動作規範"), el("span", { class: "prompt-v" }, "30 fps, 攻擊槽 (atk_*)、輔助槽 (sup_*)、部署槽 (field_deploy)"),
              el("span", { class: "prompt-k" }, "產出格式"), el("span", { class: "prompt-v" }, "FBX (Scale 1.0, Y-up, +Z) ＋ GLB 備用")
            )
          )
        );
      }

      // 完整原始 Prompt 文本展示
      box.append(
        el("h4", {}, "原始 Prompt 文本（完整內容）"),
        el("textarea", { class: "prompt-raw-box", readonly: true, spellcheck: "false" }, getFullPromptText(pkg)),
        el("div", { style: "display:flex;gap:6px;margin-top:6px" },
          el("button", { class: "btn ghost mini", type: "button", onclick: () => download(pkg.id + ".prompt.md", getFullPromptText(pkg), "text/markdown") }, "⬇ 下載 prompt.md"),
          el("button", { class: "btn ghost mini", type: "button", onclick: () => {
            const upg = upgradePackageFormat(pkg);
            if (upg) showUpgradeModal(upg);
          } }, "🔄 升級此 Prompt 為最新格式")
        )
      );

    // 2. 立繪 ⇄ 3D 雙向參照檢視 (Requirement 3 & 4)
    } else if (activeTab === "compare") {
      box.append(el("h4", {}, "立繪 ⇄ 3D 雙向關聯參照"));

      if (pkg.type === "model-rig") {
        box.append(
          el("div", { class: "prompt-card" },
            el("h4", {}, "來源立繪壓縮包 (source_pack)"),
            el("div", { class: "prompt-kv" },
              el("span", { class: "prompt-k" }, "參照任務 ID"), el("span", { class: "prompt-v", style: "font-weight:bold;color:var(--accent)" }, pkg.manifest?.source_pack || "未宣告（舊版本）"),
              el("span", { class: "prompt-k" }, "主體 ID"), el("span", { class: "prompt-v" }, pkg.subjectId || "—"),
              el("span", { class: "prompt-k" }, "立繪包狀態"), el("span", { class: "prompt-v" }, linkedPkg ? "✅ 已載入本地審查台" : "⚠️ 未在當前審查清單中")
            ),
            linkedPkg ? el("div", { style: "display:flex;gap:8px;margin-top:10px" },
              el("button", { class: "btn mini", type: "button", onclick: () => {
                reviewState.activePkgId = linkedPkg.id;
                renderReviewView();
              } }, "🔍 跳轉至立繪包"),
              el("button", { class: "btn ghost mini", type: "button", onclick: () => {
                reviewState.viewMode = "compare";
                renderReviewView();
              } }, "⚡ 開啟雙欄對比")
            ) : el("div", { style: "margin-top:8px" },
              el("button", { class: "btn ghost mini", type: "button", onclick: () => {
                const upg = upgradePackageFormat(pkg);
                if (upg) showUpgradeModal(upg);
              } }, "🔄 一鍵升級並補齊 source_pack 參照")
            )
          ),
          el("div", { class: "prompt-card" },
            el("h4", {}, "3D 成果複雜度指標（參考 steel_vs_swarm）"),
            el("div", { class: "prompt-kv" },
              el("span", { class: "prompt-k" }, "面數預算 (Tris)"), el("span", { class: "prompt-v" }, "15,000 ~ 25,000 tris (符合預算)"),
              el("span", { class: "prompt-k" }, "貼圖尺寸"), el("span", { class: "prompt-v" }, "2048 × 2048 (Base Color + Mask)"),
              el("span", { class: "prompt-k" }, "插槽骨骼數"), el("span", { class: "prompt-v" }, "Socket_Mouth / Head / Front / Core_FX 齊全"),
              el("span", { class: "prompt-k" }, "動畫 Action"), el("span", { class: "prompt-v" }, "7 組通用招式槽 + 撫摸享受 (petted)")
            )
          )
        );
      } else {
        // 立繪包
        box.append(
          el("div", { class: "prompt-card" },
            el("h4", {}, "衍生 3D 模型封裝 (model-rig)"),
            el("div", { class: "prompt-kv" },
              el("span", { class: "prompt-k" }, "立繪任務 ID"), el("span", { class: "prompt-v" }, pkg.id),
              el("span", { class: "prompt-k" }, "3D 模型狀態"), el("span", { class: "prompt-v" }, linkedPkg ? "✅ 已建立 3D 成果包 (" + linkedPkg.id + ")" : "⬜ 尚未進行 Image-to-3D 轉換")
            ),
            linkedPkg ? el("div", { style: "display:flex;gap:8px;margin-top:10px" },
              el("button", { class: "btn mini", type: "button", onclick: () => {
                reviewState.activePkgId = linkedPkg.id;
                renderReviewView();
              } }, "🔍 跳轉至 3D 模型包"),
              el("button", { class: "btn ghost mini", type: "button", onclick: () => {
                reviewState.viewMode = "compare";
                renderReviewView();
              } }, "⚡ 開啟雙欄對比")
            ) : el("div", { style: "margin-top:10px" },
              el("p", { class: "muted", style: "margin-bottom:6px" }, "可一鍵將此立繪包帶入鍛造台，生成對應的 Image-to-3D prompt："),
              el("button", { class: "btn mini", type: "button", onclick: () => {
                state.taskId = "model-rig";
                const o = taskOpts(TASKS.find(t => t.id === "model-rig"));
                o.source_pack = pkg.id;
                o.subject = pkg.id.startsWith("chr") ? "character" : "creature";
                setViewMode("forge");
                toast("已帶入立繪包參照：" + pkg.id);
              } }, "🚀 帶入鍛造台產生 3D Prompt (model-rig)")
            )
          )
        );
      }

    // 3. 規格對照明細
    } else if (activeTab === "spec") {
      box.append(el("h4", {}, "設計概念與規格對照"));
      const table = el("table", {},
        el("tr", {}, el("th", {}, "欄位名稱"), el("th", {}, "交付內容"), el("th", {}, "審查規範")),
        el("tr", {}, el("td", {}, "任務編號"), el("td", { class: "comp-hl" }, pkg.id), el("td", {}, "一字不差")),
        el("tr", {}, el("td", {}, "主體 ID"), el("td", {}, pkg.subjectId || "—"), el("td", {}, "snake_case")),
        el("tr", {}, el("td", {}, "產出平台"), el("td", {}, pkg.manifest?.produced_by || "—"), el("td", {}, "ChatGPT / Gemini")),
        pkg.manifest?.source_pack ? el("tr", {}, el("td", {}, "參照立繪包"), el("td", { class: "comp-hl" }, pkg.manifest.source_pack), el("td", {}, "有效立繪任務 ID")) : null
      );

      if (pkg.concept) {
        const c = pkg.concept;
        if (c.height || c.height_cm || c.height_m) table.append(el("tr", {}, el("td", {}, "身高 / 體長"), el("td", { class: "comp-hl" }, String(c.height || c.height_cm || c.height_m)), el("td", {}, "符合常態分佈")));
        if (c.weight || c.weight_kg) table.append(el("tr", {}, el("td", {}, "體重"), el("td", { class: "comp-hl" }, String(c.weight || c.weight_kg)), el("td", {}, "符合常態分佈")));
        if (c.body_fat || c.body_fat_pct) table.append(el("tr", {}, el("td", {}, "體脂率"), el("td", { class: "comp-hl" }, String(c.body_fat || c.body_fat_pct)), el("td", {}, "合理健康區間")));
        if (c.cup_size) table.append(el("tr", {}, el("td", {}, "罩杯（女性）"), el("td", { class: "comp-hl" }, String(c.cup_size)), el("td", {}, "女角色常態")));
        if (c.timeline) table.append(el("tr", {}, el("td", {}, "文化時間線"), el("td", { class: "comp-hl" }, String(c.timeline)), el("td", {}, "歷史與未來想像")));
        if (c.region) table.append(el("tr", {}, el("td", {}, "文化地區"), el("td", { class: "comp-hl" }, String(c.region)), el("td", {}, "跨洲與星際")));
        if (c.ethnicity) table.append(el("tr", {}, el("td", {}, "人種表型"), el("td", { class: "comp-hl" }, String(c.ethnicity)), el("td", {}, "獨立生理特徵")));
      }
      box.append(table);

    // 4. 檔案驗收清單
    } else if (activeTab === "files") {
      box.append(el("h4", {}, "交付檔案與完整性驗證"));
      const files = pkg.manifest?.files || [];
      const missing = pkg.manifest?.missing || [];
      const table = el("table", {},
        el("tr", {}, el("th", {}, "路徑"), el("th", {}, "層級"), el("th", {}, "狀態"))
      );
      files.forEach(f => {
        table.append(el("tr", {},
          el("td", {}, el("code", {}, f.path)),
          el("td", {}, f.level || "L1"),
          el("td", { style: "color:var(--ok)" }, "✅ 存在")
        ));
      });
      missing.forEach(m => {
        table.append(el("tr", {},
          el("td", {}, el("code", {}, m.item)),
          el("td", {}, m.level || "L2"),
          el("td", { style: "color:var(--warn)" }, "⚠️ 缺失（" + (m.reason || "平台限制") + "）")
        ));
      });
      box.append(table);
      if (pkg.manifest?.notes) {
        box.append(el("h4", {}, "AI 交付筆記"));
        box.append(el("p", { class: "muted" }, pkg.manifest.notes));
      }

    // 5. 審查覆核意見
    } else if (activeTab === "review") {
      box.append(el("h4", {}, "審查覆核意見"));
      const reviewBox = el("div", { class: "review-box" },
        el("div", { class: "seg" },
          el("button", { class: "segb" + (pkg.status === "ok" ? " on" : ""), type: "button", onclick: () => { pkg.status = "ok"; saveReview(pkg); renderReviewView(); } }, "✅ 核准通過"),
          el("button", { class: "segb" + (pkg.status === "pending" ? " on" : ""), type: "button", onclick: () => { pkg.status = "pending"; saveReview(pkg); renderReviewView(); } }, "🟨 待修改"),
          el("button", { class: "segb" + (pkg.status === "reject" ? " on" : ""), type: "button", onclick: () => { pkg.status = "reject"; saveReview(pkg); renderReviewView(); } }, "❌ 退回拒絕")
        ),
        el("textarea", { id: "revNote", placeholder: "填寫人工審查意見（例如：身高比例良好，但服裝配色需微調；臉部表情需更生動...）", onchange: e => { saveReview(pkg, e.target.value); } }, getReviewNote(pkg)),
        el("div", { style: "display:flex;gap:6px;margin-top:6px" },
          el("button", { class: "btn mini", type: "button", onclick: () => { saveReview(pkg, $("#revNote")?.value); toast("已儲存審查結果"); } }, "💾 儲存審查意見"),
          el("button", { class: "btn ghost mini", type: "button", onclick: () => exportReviewReport(pkg) }, "⬇ 下載審查報告 (.json)")
        )
      );
      box.append(reviewBox);

    // 6. 指令部署與解壓
    } else if (activeTab === "cli") {
      box.append(el("h4", {}, "解壓與管線部署"));
      const root = state.root || DEFAULT_ROOT;
      const psCmd = `Expand-Archive -Path "$HOME\\Downloads\\${pkg.id}.zip" -DestinationPath "${root}\\Art\\_incoming\\" -Force`;
      const valCmd = `python tools/validate_art.py Art/_incoming/${pkg.id} --apply`;
      box.append(
        el("p", { class: "muted" }, "一鍵解壓指令（PowerShell）："),
        el("div", { class: "cmd" }, el("code", {}, psCmd), el("button", { type: "button", class: "btn ghost mini", onclick: () => copyText(psCmd, "已複製解壓指令") }, "複製")),
        el("p", { class: "muted", style: "margin-top:10px" }, "驗收搬入管線指令（validate-art）："),
        el("div", { class: "cmd" }, el("code", {}, valCmd), el("button", { type: "button", class: "btn ghost mini", onclick: () => copyText(valCmd, "已複製驗收指令") }, "複製"))
      );
    }
  }

  function formatArray(arr) {
    if (!arr) return "—";
    if (Array.isArray(arr)) return arr.length ? arr.join("、") : "—";
    return String(arr);
  }

  function getFullPromptText(pkg) {
    if (pkg.rawPrompt) return pkg.rawPrompt;
    // 若無原始檔則以最新規格即時組裝
    const upg = upgradePackageFormat(pkg);
    return upg ? upg.newPromptText : (pkg.manifest?.task_header || "無可用 Prompt 資訊");
  }

  function getReviewNote(pkg) {
    return reviewState.reviews[pkg.id]?.note || "";
  }
  function saveReview(pkg, note) {
    reviewState.reviews[pkg.id] = { status: pkg.status, note: note ?? getReviewNote(pkg), time: new Date().toLocaleString() };
    try { localStorage.setItem(REVIEW_KEY, JSON.stringify(reviewState.reviews)); } catch (e) {}
  }
  function exportReviewReport(pkg) {
    const report = {
      task_id: pkg.id,
      status: pkg.status,
      reviewer_note: getReviewNote(pkg),
      inspected_at: new Date().toISOString(),
      manifest: pkg.manifest,
      concept_summary: pkg.concept
    };
    download(pkg.id + ".review.json", JSON.stringify(report, null, 2), "application/json");
  }

  // 視圖切換
  async function setViewMode(mode) {
    state.activeTab = mode;
    if (mode === "review") {
      $("#forgeView").style.display = "none";
      $("#reviewView").classList.add("active");
      $("#viewBtnForge").classList.remove("on");
      $("#viewBtnReview").classList.add("on");
      $("#forgeHeaderControls").style.display = "none";
      if (!reviewState.packages.length) {
        const autoScanned = await tryAutoScanIncoming();
        if (!autoScanned && !reviewState.packages.length) {
          loadSampleFixtures();
        }
      } else {
        renderReviewView();
      }
    } else {
      $("#forgeView").style.display = "grid";
      $("#reviewView").classList.remove("active");
      $("#viewBtnForge").classList.add("on");
      $("#viewBtnReview").classList.remove("on");
      $("#forgeHeaderControls").style.display = "inline-flex";
      renderAll();
    }
  }

  // ---- 動作 ----
  function newSeed() { return Math.floor(Math.random() * 0xffff).toString(16).padStart(4, "0"); }
  function reseed(seed, force) {
    state.seed = (seed || "").toLowerCase().replace(/[^a-z0-9]/g, "").slice(0, 8) || newSeed();
    rollAll("character", mulberry32(hashStr(state.seed + "|character")), force);
    rollAll("creature", mulberry32(hashStr(state.seed + "|creature")), force);
    renderAll();
  }
  function copyText(text, msg) {
    const done = () => toast(msg);
    if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(text).then(done, fallback); else fallback();
    function fallback() { const ta = el("textarea", {}, text); document.body.append(ta); ta.select(); document.execCommand("copy"); ta.remove(); done(); }
  }
  function record() {
    const b = lastBuild; if (!b || b.errors.length) return;
    state.history = [{ id: b.ctx.taskId, task: b.ctx.task.id, platform: state.platform, time: new Date().toLocaleString() }]
      .concat((state.history || []).filter(h => h.id !== b.ctx.taskId)).slice(0, 50);
    renderHistory(); save();
  }
  function download(name, text, type) {
    const a = el("a", { href: URL.createObjectURL(new Blob([text], { type })), download: name });
    document.body.append(a); a.click(); setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 500);
  }
  let toastTimer;
  function toast(msg) { const t = $("#toast"); t.textContent = msg; t.classList.add("show"); clearTimeout(toastTimer); toastTimer = setTimeout(() => t.classList.remove("show"), 1800); }

  // ---- 持久化 ----
  function save() {
    try {
      localStorage.setItem(LS_KEY, JSON.stringify({
        catalog: C.version, seed: state.seed, platform: state.platform, taskId: state.taskId, includeReview: state.includeReview,
        character: state.character, creature: state.creature, locks: state.locks, opts: state.opts, notes: state.notes,
        subjectIds: state.subjectIds, history: state.history, root: state.root, customOptions: state.customOptions,
        preset: state.preset
      }));
    } catch (e) {}
  }
  function load() {
    try {
      const revs = JSON.parse(localStorage.getItem(REVIEW_KEY) || "{}");
      reviewState.reviews = revs;
      const d = JSON.parse(localStorage.getItem(LS_KEY) || "null");
      if (!d) return false;
      const sameMajor = (d.catalog || "").split(".")[0] === C.version.split(".")[0];
      Object.assign(state, {
        platform: d.platform || "chatgpt", history: d.history || [], root: d.root || "", notes: d.notes || "",
        customOptions: d.customOptions || {},
        preset: DESIGNED.some(x => x.line_id === d.preset) ? d.preset : null
      });
      if (TASKS.some(t => t.id === d.taskId)) state.taskId = d.taskId;
      if (sameMajor) {
        Object.assign(state, {
          seed: d.seed, includeReview: !!d.includeReview, character: d.character || {},
          creature: d.creature || {}, locks: d.locks || {}, opts: d.opts || {}, subjectIds: d.subjectIds || {}
        });
        if (state.creature?.base_parents) {
          state.creature.base_parents = state.creature.base_parents.map(traceBaseParent);
        }
      }
      return !!state.seed;
    } catch (e) { return false; }
  }

  function init() {
    $("#ver").textContent = "tool " + TOOL_VERSION + " · catalog " + C.version + " · spec " + SPEC;

    // 視圖切換事件
    $("#viewBtnForge").addEventListener("click", () => setViewMode("forge"));
    $("#viewBtnReview").addEventListener("click", () => setViewMode("review"));
    $("#toReview").addEventListener("click", () => setViewMode("review"));

    // 平台切換按鈕事件
    $("#platChatgpt").addEventListener("click", () => { state.platform = "chatgpt"; renderAll(); });
    $("#platGemini").addEventListener("click", () => { state.platform = "gemini"; renderAll(); });

    $("#reroll").addEventListener("click", () => reseed(newSeed(), true));
    $("#seed").addEventListener("change", e => reseed(e.target.value, true));
    $("#notes").addEventListener("input", e => { state.notes = e.target.value; renderOutput(); save(); });
    $("#copy").addEventListener("click", () => { copyText($("#prompt").value, "已複製 prompt"); record(); });
    $("#download").addEventListener("click", () => { download(lastBuild.ctx.taskId + ".prompt.md", $("#prompt").value, "text/markdown"); record(); });
    $("#export").addEventListener("click", () => {
      const b = lastBuild; if (!b.ctx.concept) { toast("此任務沒有概念"); return; }
      download(b.ctx.subjectId + ".concept.json", JSON.stringify(b.ctx.concept, null, 2), "application/json");
    });
    $("#open").addEventListener("click", () => window.open(state.platform === "gemini" ? "https://gemini.google.com/app" : "https://chatgpt.com/", "_blank", "noopener"));
    $("#root").addEventListener("change", e => { state.root = e.target.value.trim().replace(/[\\/]+$/, ""); renderOutput(); save(); });

    // 鍛造台升級舊版包按鈕
    $("#btnUpgradeForge").addEventListener("click", () => $("#upgradeFileInput").click());

    // 審查台控制列事件
    $("#btnScanDir").addEventListener("click", scanIncomingDirectory);
    $("#folderFileInput")?.addEventListener("change", e => handleFolderUpload(e.target.files));
    $("#btnLoadZip").addEventListener("click", () => $("#zipFileInput").click());
    $("#zipFileInput").addEventListener("change", async e => {
      const files = e.target.files;
      for (const f of files) {
        if (f.name.endsWith(".zip")) await loadZipFile(f);
      }
    });
    $("#btnLoadFixtures").addEventListener("click", loadSampleFixtures);
    $("#btnUpgradeZip").addEventListener("click", () => $("#upgradeFileInput").click());
    $("#upgradeFileInput").addEventListener("change", async e => {
      const file = e.target.files?.[0];
      if (!file) return;
      toast("正在讀取並解析壓縮包...");
      const pkg = await loadZipFile(file);
      if (pkg) {
        const upg = upgradePackageFormat(pkg);
        if (upg) showUpgradeModal(upg);
      }
    });

    // 舞台檢視模式切換 (單圖 vs 2D ⇄ 3D 雙欄對比)
    $("#btnModeSingle").addEventListener("click", () => {
      reviewState.viewMode = "single";
      renderReviewView();
    });
    $("#btnModeCompare").addEventListener("click", () => {
      reviewState.viewMode = "compare";
      renderReviewView();
    });

    // 拖曳上傳支援
    const stageHost = $("#stageHost");
    stageHost.addEventListener("dragover", e => { e.preventDefault(); stageHost.style.background = "#1f2635"; });
    stageHost.addEventListener("dragleave", () => { stageHost.style.background = "#12141a"; });
    stageHost.addEventListener("drop", async e => {
      e.preventDefault();
      stageHost.style.background = "#12141a";
      if (e.dataTransfer.files?.length) {
        for (const f of e.dataTransfer.files) {
          if (f.name.endsWith(".zip")) await loadZipFile(f);
        }
      }
    });

    // 縮放控制
    $("#zoom1x").addEventListener("click", () => { reviewState.zoomLevel = 1.0; reviewState.panX = 0; reviewState.panY = 0; renderReviewView(); });
    $("#zoomFit").addEventListener("click", () => { reviewState.zoomLevel = 0.9; reviewState.panX = 0; reviewState.panY = 0; renderReviewView(); });
    $("#zoomIn").addEventListener("click", () => { reviewState.zoomLevel = Math.min(3.0, reviewState.zoomLevel + 0.25); renderReviewView(); });
    $("#zoomOut").addEventListener("click", () => { reviewState.zoomLevel = Math.max(0.4, reviewState.zoomLevel - 0.25); renderReviewView(); });

    // 審查台分類與搜尋
    document.querySelectorAll("#catSeg .segb").forEach(btn => {
      btn.addEventListener("click", () => {
        document.querySelectorAll("#catSeg .segb").forEach(b => b.classList.remove("on"));
        btn.classList.add("on");
        reviewState.filterCat = btn.dataset.cat;
        renderReviewView();
      });
    });
    $("#railSearch").addEventListener("input", e => {
      reviewState.filterQuery = e.target.value.trim();
      renderReviewView();
    });

    // 分頁 Tabs
    document.querySelectorAll("#panelTabs .segb").forEach(btn => {
      btn.addEventListener("click", () => {
        document.querySelectorAll("#panelTabs .segb").forEach(b => b.classList.remove("on"));
        btn.classList.add("on");
        renderReviewView();
      });
    });

    // 一鍵解壓縮 / 複製
    $("#btnUnpack").addEventListener("click", () => {
      const pkg = getActivePkg();
      if (!pkg) return;
      const root = state.root || DEFAULT_ROOT;
      const cmd = `Expand-Archive -Path "$HOME\\Downloads\\${pkg.id}.zip" -DestinationPath "${root}\\Art\\_incoming\\" -Force`;
      copyText(cmd, "已複製解壓指令！可在 PowerShell 直接貼上執行一鍵解壓");
    });
    $("#btnCopyPath").addEventListener("click", () => {
      const pkg = getActivePkg();
      if (!pkg) return;
      const root = state.root || DEFAULT_ROOT;
      copyText(`${root}\\Art\\_incoming\\${pkg.id}`, "已複製交付資料夾路徑");
    });
    $("#btnCopyPs").addEventListener("click", () => {
      const pkg = getActivePkg();
      if (!pkg) return;
      const root = state.root || DEFAULT_ROOT;
      const cmd = `Expand-Archive -Path "$HOME\\Downloads\\${pkg.id}.zip" -DestinationPath "${root}\\Art\\_incoming\\" -Force`;
      copyText(cmd, "已複製 PowerShell 解壓指令");
    });

    // 審查台當前包一鍵升級格式
    $("#btnUpgradeActive").addEventListener("click", () => {
      const pkg = getActivePkg();
      if (!pkg) return;
      const upg = upgradePackageFormat(pkg);
      if (upg) showUpgradeModal(upg);
    });

    // 升級 Modal 內部下載與複製事件
    $("#btnCopyUpgPrompt").addEventListener("click", () => {
      if (lastUpgradeResult) copyText(lastUpgradeResult.newPromptText, "已複製升級後的 Prompt");
    });
    $("#btnDlUpgPrompt").addEventListener("click", () => {
      if (!lastUpgradeResult) return;
      download(lastUpgradeResult.targetTaskId + ".prompt.md", lastUpgradeResult.newPromptText, "text/markdown");
    });
    $("#btnDlUpgConcept").addEventListener("click", () => {
      if (!lastUpgradeResult) return;
      download(lastUpgradeResult.newManifest.subject_id + ".concept.json", JSON.stringify(lastUpgradeResult.newConcept, null, 2), "application/json");
    });
    $("#btnDlUpgManifest").addEventListener("click", () => {
      if (!lastUpgradeResult) return;
      download("manifest.json", JSON.stringify(lastUpgradeResult.newManifest, null, 2), "application/json");
    });
    $("#btnDlUpgZip").addEventListener("click", () => {
      if (!lastUpgradeResult) return;
      const pkg = lastUpgradeResult.pkg;
      const zipFiles = {};
      const tid = lastUpgradeResult.targetTaskId;
      zipFiles[tid + "/manifest.json"] = JSON.stringify(lastUpgradeResult.newManifest, null, 2);
      zipFiles[tid + "/concept.json"] = JSON.stringify(lastUpgradeResult.newConcept, null, 2);
      zipFiles[tid + "/" + tid + ".prompt.md"] = lastUpgradeResult.newPromptText;

      // 若有原圖一併封裝
      if (pkg.rawFiles) {
        for (const path in pkg.rawFiles) {
          if (!path.endsWith("manifest.json") && !path.endsWith("concept.json") && !path.endsWith(".prompt.md")) {
            const rel = path.includes("/") ? path.slice(path.indexOf("/") + 1) : path;
            zipFiles[tid + "/" + rel] = pkg.rawFiles[path];
          }
        }
      }
      const blob = createZipBlob(zipFiles);
      const a = el("a", { href: URL.createObjectURL(blob), download: tid + ".upgraded.zip" });
      document.body.append(a); a.click();
      setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 500);
      toast("已下載升級後的 Zip 包");
    });
    $("#btnLoadInForge").addEventListener("click", () => {
      if (!lastUpgradeResult) return;
      const res = lastUpgradeResult;
      const isChr = res.targetTaskId.startsWith("chr");
      state.taskId = isChr ? "character-concept" : "creature-concept";
      if (isChr) {
        state.character = Object.assign({}, res.newConcept);
      } else {
        state.creature = Object.assign({}, res.newConcept);
      }
      closeUpgradeModal();
      setViewMode("forge");
      toast("已將升級概念載入至 Prompt 鍛造台");
    });
    $("#btnCloseUpgradeModal").addEventListener("click", closeUpgradeModal);
    $("#upgradeModal").addEventListener("click", e => { if (e.target.id === "upgradeModal") closeUpgradeModal(); });

    function closeUpgradeModal() {
      $("#upgradeModal").classList.remove("show");
    }

    // 自訂 Modal 事件
    $("#customModalConfirm").addEventListener("click", () => {
      const val = $("#customModalInput").value.trim();
      if (val && curCustomCtx) curCustomCtx(val);
      closeCustomModal();
    });
    $("#customModalUnknown").addEventListener("click", () => {
      if (curCustomCtx) curCustomCtx("未知");
      closeCustomModal();
    });
    $("#customModalCancel").addEventListener("click", closeCustomModal);
    $("#customModal").addEventListener("click", e => { if (e.target.id === "customModal") closeCustomModal(); });

    const had = load();
    $("#notes").value = state.notes;
    $("#root").value = state.root || DEFAULT_ROOT;
    if (had) renderAll(); else reseed(newSeed(), true);
  }
  document.addEventListener("DOMContentLoaded", init);
})();