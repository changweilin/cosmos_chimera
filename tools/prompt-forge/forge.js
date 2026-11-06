// COSMIMERA prompt-forge · 介面與組裝 + 交付成果審查台
(function () {
  const TOOL_VERSION = "1.3.0";
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
    customOptions: {}, customValues: {}, activeTab: "forge"
  };

  function getCustom(key) { return state.customOptions[key] || []; }
  function addCustom(key, val) {
    if (!val) return;
    const list = state.customOptions[key] || (state.customOptions[key] = []);
    if (!list.includes(val)) list.push(val);
  }

  // 取得外觀基底子類別清單（由外觀基底父類別決定）
  function getCreatureSubBases(cat) {
    const parent = CR.bases_hierarchy.find(b => b.cat === cat) || CR.bases_hierarchy[0];
    return parent.groups.flatMap(g => g.subs.map(s => cat + "·" + g.group + "·" + s));
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
      // 常見大型鳥類到中型哺乳類（例如金鵰、天鵝、狼、豹、羊）
      lenLogM = Math.log(1.05); lenLogS = 0.22;
      wtLogM = Math.log(22.0); wtLogS = 0.55;
    } else if (sizeClass === "L") {
      // 大型鳥類走禽到大型哺乳類（例如鴕鳥、獅、虎、鹿、馬、熊）
      lenLogM = Math.log(2.05); lenLogS = 0.18;
      wtLogM = Math.log(180.0); wtLogS = 0.48;
    } else { // XL: 上限到暴龍 (12.5m, 9000kg)
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
        return [{ v: d.height, w: 1 }, { v: "160 cm", w: 1 }, { v: "175 cm", w: 1 }, { v: "未知", w: 1 }].concat(getCustom("character.height").map(v => ({ v, w: 1 })));
      }, count: [{ n: 1, w: 1 }], dep: "gender", customGen: (s, rng) => sampleHumanDimensions(rng, s.character.gender?.[0], s.character.age_band?.[0], s.character.age?.[0]).height },
      { key: "weight", label: "體重 (kg)", options: s => {
        const d = sampleHumanDimensions(Math.random, s.character.gender?.[0], s.character.age_band?.[0], s.character.age?.[0]);
        return [{ v: d.weight, w: 1 }, { v: "52.0 kg", w: 1 }, { v: "68.0 kg", w: 1 }, { v: "未知", w: 1 }].concat(getCustom("character.weight").map(v => ({ v, w: 1 })));
      }, count: [{ n: 1, w: 1 }], dep: "height", customGen: (s, rng) => sampleHumanDimensions(rng, s.character.gender?.[0], s.character.age_band?.[0], s.character.age?.[0]).weight },
      { key: "body_fat", label: "體脂率 (%)", options: s => {
        const d = sampleHumanDimensions(Math.random, s.character.gender?.[0], s.character.age_band?.[0], s.character.age?.[0]);
        return [{ v: d.bodyFat, w: 1 }, { v: "16.0%", w: 1 }, { v: "22.0%", w: 1 }, { v: "未知", w: 1 }].concat(getCustom("character.body_fat").map(v => ({ v, w: 1 })));
      }, count: [{ n: 1, w: 1 }], dep: "gender", customGen: (s, rng) => sampleHumanDimensions(rng, s.character.gender?.[0], s.character.age_band?.[0], s.character.age?.[0]).bodyFat },
      { key: "cup_size", label: "罩杯（女角色）", options: () => CH.cup_sizes.map(c => ({ v: c.v, w: c.w })).concat(getCustom("character.cup_size").map(v => ({ v, w: 1 }))), count: [{ n: 1, w: 1 }], when: s => s.character.gender?.[0] === "女" },
      { key: "outfit_palette", label: "服裝色系（1–3）", options: () => C.PALETTES.map(p => ({ v: p.v, w: p.w, zh: `${p.v} (${p.colors || 0}色)` })).concat(getCustom("character.outfit_palette").map(v => ({ v, w: 1, zh: v }))), count: [{ n: 1, w: 45 }, { n: 2, w: 40 }, { n: 3, w: 15 }], max: 3 },
      { key: "hair_palette", label: "頭髮色系（1–2）", options: () => CH.hair_palettes.map(p => ({ v: p.v, w: p.w })).concat(getCustom("character.hair_palette").map(v => ({ v, w: 1 }))), count: [{ n: 1, w: 60 }, { n: 2, w: 40 }], max: 2 },
      { key: "eye_palette", label: "瞳孔色系（1–2）", options: () => CH.eye_palettes.map(p => ({ v: p.v, w: p.w })).concat(getCustom("character.eye_palette").map(v => ({ v, w: 1 }))), count: [{ n: 1, w: 60 }, { n: 2, w: 40 }], max: 2 },
      { key: "personality", label: "性格", options: () => uniform(C.PERSONALITIES.concat(getCustom("character.personality"))), count: [{ n: 2, w: 1 }], max: 3 },
      { key: "style", label: "風格", options: () => uniform(CH.style.concat(getCustom("character.style"))), count: [{ n: 1, w: 1 }], max: 2 },
      { key: "interests", label: "興趣", options: () => uniform(CH.interests.concat(getCustom("character.interests"))), count: [{ n: 2, w: 1 }], max: 3 },
      { key: "likes", label: "喜好", options: () => uniform(CH.likes.concat(getCustom("character.likes"))), count: [{ n: 2, w: 1 }], max: 3 },
      { key: "dislikes", label: "厭惡", options: () => uniform(CH.dislikes.concat(getCustom("character.dislikes"))), count: [{ n: 2, w: 1 }], max: 3 },
      { key: "vessel", label: "慣用收服道具", options: () => C.VESSELS.map(v => ({ v: v.v, w: 1, zh: v.zh })).concat(getCustom("character.vessel").map(v => ({ v, w: 1, zh: v }))), count: [{ n: 1, w: 1 }] }
    ],
    creature: [
      { key: "elements", label: "屬性（1–3）", options: () => C.ELEMENTS.map(e => ({ v: e.v, w: 1, zh: e.zh + " " + e.v })).concat(getCustom("creature.elements").map(v => ({ v, w: 1, zh: v }))), count: C.ELEMENT_COUNT, max: 3 },
      { key: "base_parents", label: "外觀基底父類別（1–2）", options: () => CR.base_parents.map(b => ({ v: b.v, zh: b.zh || b.v, w: b.w })).concat(getCustom("creature.base_parents").map(v => ({ v, zh: v, w: 1 }))), count: CR.base_count, max: 2 },
      { key: "bases", label: "外觀基底子類別（1–3）", options: s => {
        const parents = (s.creature.base_parents || []).map(traceBaseParent);
        let subs = [];
        parents.forEach(p => {
          const list = CR.base_sub_map[p] || [];
          const prefix = p.split("·")[1] || p;
          subs = subs.concat(list.map(x => `${prefix}·${x}`));
        });
        if (!subs.length) {
          subs = (CR.base_sub_map["生物·哺乳類"] || []).concat(CR.base_sub_map["生物·鳥類"] || []);
        }
        return uniform(subs.concat(["未知"]).concat(getCustom("creature.bases")));
      }, count: [{ n: 1, w: 45 }, { n: 2, w: 40 }, { n: 3, w: 15 }], max: 3, dep: "base_parents" },
      { key: "appearance_palette", label: "外觀色系（1–3）", options: () => C.PALETTES.map(p => ({ v: p.v, w: p.w, zh: `${p.v} (${p.colors || 0}色)` })).concat(getCustom("creature.appearance_palette").map(v => ({ v, w: 1, zh: v }))), count: [{ n: 1, w: 45 }, { n: 2, w: 40 }, { n: 3, w: 15 }], max: 3 },
      { key: "timeline", label: "文化時間線", options: () => C.TIMELINES.map(t => ({ v: t.v, zh: t.zh, w: 1 })).concat(getCustom("creature.timeline").map(v => ({ v, zh: v, w: 1 }))), count: [{ n: 1, w: 1 }] },
      { key: "region", label: "文化地區", options: () => uniform(C.CULTURE_REGIONS.concat(getCustom("creature.region"))), count: [{ n: 1, w: 1 }] },
      { key: "archetype", label: "文化原型", options: s => {
        const reg = s.creature.region?.[0];
        const pool = archetypePool(s).filter(a => !reg || reg === "未知" || a.region === reg || a.region === "未知");
        return pool.map(a => ({ v: a.name, w: 1, zh: a.name + "（" + a.region + "·" + a.type + (a.sensitivity === "review" ? "・需審" : "") + "）" })).concat(getCustom("creature.archetype").map(v => ({ v, w: 1, zh: v })));
      }, count: [{ n: 1, w: 1 }], dep: "region" },
      { key: "role", label: "戰鬥定位", options: () => uniform(CR.role.concat(getCustom("creature.role"))), count: [{ n: 1, w: 1 }] },
      { key: "temperament", label: "性情/性格", options: () => uniform(C.PERSONALITIES.concat(getCustom("creature.temperament"))), count: [{ n: 1, w: 1 }] },
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
    // 男角色此項不加入 prompt
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
    // 批次模式的主體來自雲端硬碟上的各立繪包，不使用目前概念。
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
  }

  function fieldRow(kind, f) {
    if (f.when && !f.when(state)) return el("div", { style: "display:none" });
    const key = kind + "." + f.key, vals = state[kind][f.key] || [], opts = f.options(state);
    const label = v => (opts.find(o => o.v === v) || {}).zh || v;
    const lock = el("input", { type: "checkbox", title: "鎖定（全部隨機時不變）", checked: !!state.locks[key], onchange: e => { state.locks[key] = e.target.checked; save(); } });
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
    return el("div", { class: "field" }, el("label", {}, f.label), control, el("div", { class: "fctl" }, edit, dice, el("label", { class: "lock" }, lock, "🔒")));
  }

  function renderConcepts() {
    const t = task(), o = taskOpts(t), subj = resolveSubject(t, o);
    const box = $("#concept"); box.replaceChildren();
    if (!subj) { box.append(el("p", { class: "muted" }, "此任務不使用角色/宙獸概念。")); return; }
    const kind = subj;
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
          el("button", { type: "button", class: "mini", onclick: () => { o[d.key] = o[d.key].length === items.length ? [] : items.map(i => i.v); renderAll(); } }, o[d.key].length === items.length ? "全不選" : "全選"),
          items.map(i => el("label", { class: "check" }, el("input", { type: "checkbox", checked: o[d.key].includes(i.v), onchange: e => { o[d.key] = e.target.checked ? o[d.key].concat(i.v) : o[d.key].filter(x => x !== i.v); renderAll(); } }), i.zh)));
      } else if (d.type === "select") {
        control = el("div", { class: "row" },
          el("select", { onchange: e => { o[d.key] = e.target.value; if (d.key === "subject") delete o.actions; taskOpts(t); renderAll(); } }, items.map(i => el("option", { value: i.v, selected: i.v === o[d.key] }, i.zh))),
          el("button", { class: "icon", type: "button", title: "隨機", onclick: () => { o[d.key] = items[Math.floor(Math.random() * items.length)].v; if (d.key === "subject") delete o.actions; taskOpts(t); renderAll(); } }, "🎲"));
      } else {
        control = el("input", { value: o[d.key], onchange: e => { o[d.key] = e.target.value; renderAll(); } });
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
      // 成品在雲端硬碟 3D_out；批次會有多包，所以用萬用字元一次解壓。
      const zips = b.ctx.taskId + (b.ctx.opts.scope === "batch" ? "*" : "") + ".zip";
      const dcmd = 'Get-ChildItem "$HOME\\Downloads\\' + zips + '" | ForEach-Object { Expand-Archive -Path $_.FullName -DestinationPath "' + dest + '\\" -Force }';
      $("#deliver").replaceChildren(
        el("ol", {},
          el("li", {}, "先把角色/宙獸立繪包（", el("code", {}, "chr-*.zip / cr-*.zip"), "）放到雲端硬碟 ", el("code", {}, "project/cosmos_chimera/image_in/")),
          el("li", {}, "按「複製 prompt」→「開啟 Gemini」切到任務模式貼上送出"),
          el("li", {}, "完成後從雲端硬碟 ", el("code", {}, "project/cosmos_chimera/3D_out/"), " 下載 ", el("code", {}, zips), " 到「下載」資料夾"),
          el("li", {}, "解壓到 ", el("code", {}, dest + "\\"), "，再到「成果審查台」審查。")
        ),
        el("div", { class: "cmd" }, el("code", {}, dcmd), el("button", { type: "button", class: "mini", onclick: () => copyText(dcmd, "已複製解壓指令") }, "複製"))
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
      el("div", { class: "cmd" }, el("code", {}, cmd), el("button", { type: "button", class: "mini", onclick: () => copyText(cmd, "已複製解壓指令") }, "複製")),
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
    document.querySelectorAll("input[name=platform]").forEach(r => r.checked = r.value === state.platform);
    $("#rec").textContent = task().platform === "gemini" ? "建議：Gemini Argon" : "建議：ChatGPT";
    renderTasks(); renderConcepts(); renderOptions(); renderOutput(); renderHistory();
    save();
  }

  // ---- 成果審查台（Review Workbench，三欄排版參考 steel_vs_swarm） ----
  const reviewState = {
    packages: [], activePkgId: null, activeImgIdx: 0,
    filterCat: "all", filterQuery: "", zoomLevel: 1.0, panX: 0, panY: 0,
    reviews: {}
  };

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
    let manifestData = null, conceptData = null, prefix = "";
    for (const path in files) {
      if (path.endsWith("manifest.json")) {
        try { manifestData = JSON.parse(new TextDecoder().decode(files[path])); } catch (e) {}
        prefix = path.slice(0, path.lastIndexOf("manifest.json"));
      } else if (path.endsWith("concept.json")) {
        try { conceptData = JSON.parse(new TextDecoder().decode(files[path])); } catch (e) {}
      }
    }
    const taskId = manifestData?.task_id || file.name.replace(/\.zip$/i, "");
    const images = [];
    for (const path in files) {
      if (/\.(png|jpe?g|webp)$/i.test(path)) {
        const rel = path.startsWith(prefix) ? path.slice(prefix.length) : path;
        const blob = new Blob([files[path]], { type: "image/png" });
        images.push({ path: rel, url: URL.createObjectURL(blob) });
      }
    }
    const pkg = {
      id: taskId,
      type: manifestData?.task_type || (taskId.startsWith("chr") ? "character-concept" : "creature-concept"),
      subjectId: manifestData?.subject_id || "",
      manifest: manifestData,
      concept: conceptData,
      images,
      fileCount: Object.keys(files).length,
      rawZip: file,
      isZip: true,
      status: manifestData?.missing?.length ? "missing" : "pending"
    };
    addPackage(pkg);
  }

  function addPackage(pkg) {
    const idx = reviewState.packages.findIndex(p => p.id === pkg.id);
    if (idx >= 0) reviewState.packages[idx] = pkg;
    else reviewState.packages.unshift(pkg);
    reviewState.activePkgId = pkg.id;
    reviewState.activeImgIdx = 0;
    renderReviewView();
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
          task_id: "cr-20261106-7f3a-2105",
          task_type: "creature-concept",
          subject_id: "line_7f3a",
          produced_by: "ChatGPT",
          files: [
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
          base_parents: ["類人奇幻·精靈族", "器械·武器"],
          bases: ["精靈族·高等精靈/月光祭司", "武器·符文巨盾/塔盾護壁"],
          appearance_palette: ["熔岩赤紅 × 炭黑硬甲 × 烈焰耀金"],
          timeline: "青銅神話/古典英雄時代", region: "東亞",
          height_m: "2.10 m", weight_kg: "340 kg", body_fat_pct: "15.0%",
          archetype: "桃太郎", role: "前排肉盾/坦克", temperament: "好鬥凶悍",
          stages: [
            { stage: "juvenile", name_zh: "火角石", size_class: "S", height_m: 0.45 },
            { stage: "adolescent", name_zh: "熔脊獸", size_class: "M", height_m: 1.10 },
            { stage: "adult", name_zh: "焰甲重龍", size_class: "L", height_m: 2.10 },
            { stage: "overload", name_zh: "天炎煌角甲", size_class: "XL", height_m: 3.80 }
          ]
        },
        images: [
          { path: "images/cr_line_7f3a_adult_main.png", url: createSampleSvgDataUrl("焰甲重龍 · 成年立繪 (2.1m)", "#d35400") },
          { path: "images/cr_line_7f3a_adult_turnaround.png", url: createSampleSvgDataUrl("焰甲重龍 · T-Pose 三視圖", "#7f8c8d") },
          { path: "images/cr_line_7f3a_adult_front.png", url: createSampleSvgDataUrl("正面正交視圖 1:1", "#34495e") },
          { path: "images/cr_line_7f3a_lineup.png", url: createSampleSvgDataUrl("進化排列圖 21:9", "#2c3e50") }
        ],
        fileCount: 6,
        status: "pending"
      },
      {
        id: "chr-20261106-7f3a-2105",
        type: "character-concept",
        subjectId: "chr_7f3a",
        manifest: {
          spec: "cosmimera-art-task/1",
          task_id: "chr-20261106-7f3a-2105",
          task_type: "character-concept",
          subject_id: "chr_7f3a",
          produced_by: "ChatGPT",
          files: [
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
          gender: "女", age: 17, height_cm: "163 cm", weight_kg: "51.5 kg", body_fat_pct: "21.5%",
          cup_size: "C",
          outfit_palette: ["海軍藍 × 象牙白 × 霧金"],
          hair_palette: ["純黑 × 挑染天藍"],
          eye_palette: ["琥珀金"],
          personality: ["熱血", "好奇"], style: "街頭機能 × 學院制服改造",
          vessel: "talisman_card"
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
    toast("已載入內建測試範例");
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

  // 掃描 Art/_incoming 目錄（File System Access API）
  async function scanIncomingDirectory() {
    if (!window.showDirectoryPicker) {
      toast("您的瀏覽器不支援目錄讀取，請使用「載入 Zip」按鈕");
      $("#zipFileInput").click();
      return;
    }
    try {
      const dirHandle = await window.showDirectoryPicker({ mode: "readwrite" });
      toast("正在掃描目錄...");
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
      toast("掃描完成，已載入 " + foundCount + " 個交付包");
    } catch (e) {
      if (e.name !== "AbortError") toast("掃描失敗：" + e.message);
    }
  }

  async function loadFromDirHandle(taskId, dirHandle) {
    let manifestData = null, conceptData = null;
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
      type: manifestData?.task_type || (taskId.startsWith("chr") ? "character-concept" : "creature-concept"),
      subjectId: manifestData?.subject_id || "",
      manifest: manifestData,
      concept: conceptData,
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

  function renderReviewView() {
    const pkg = getActivePkg();
    // 渲染名冊清單
    const railList = $("#railList"); railList.replaceChildren();
    const query = reviewState.filterQuery.toLowerCase();
    const filtered = reviewState.packages.filter(p => {
      if (reviewState.filterCat === "pending" && p.status !== "pending") return false;
      if (reviewState.filterCat === "chr" && !p.id.startsWith("chr")) return false;
      if (reviewState.filterCat === "cr" && !p.id.startsWith("cr")) return false;
      if (reviewState.filterCat === "fx" && !p.id.startsWith("fx")) return false;
      if (query && !p.id.toLowerCase().includes(query) && !p.subjectId.toLowerCase().includes(query)) return false;
      return true;
    });

    filtered.forEach(p => {
      const isSel = pkg && pkg.id === p.id;
      const thumbUrl = p.images?.[0]?.url;
      const statZh = p.status === "ok" ? "✅ 合規" : p.status === "reject" ? "❌ 退回" : p.status === "missing" ? "⚠️ 缺檔" : "🟨 待審";
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
          el("span", { class: "rl-stat " + p.status }, statZh)
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
      $("#stageImg").style.display = "none";
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

    // 舞台輪播
    const carousel = $("#stageCarousel"); carousel.replaceChildren();
    pkg.images.forEach((im, idx) => {
      carousel.append(el("img", {
        class: "c-thumb" + (idx === reviewState.activeImgIdx ? " on" : ""),
        src: im.url,
        title: im.path,
        onclick: () => { reviewState.activeImgIdx = idx; renderReviewView(); }
      }));
    });

    // 視圖按鈕
    const viewSeg = $("#viewSeg"); viewSeg.replaceChildren();
    pkg.images.forEach((im, idx) => {
      const shortName = im.path.replace(/^images\//, "").replace(/\.[^.]+$/, "");
      viewSeg.append(el("button", {
        class: "segb" + (idx === reviewState.activeImgIdx ? " on" : ""),
        type: "button",
        onclick: () => { reviewState.activeImgIdx = idx; renderReviewView(); }
      }, shortName));
    });

    // 渲染右欄分頁資訊
    renderPanel(pkg);
  }

  function renderPanel(pkg) {
    const box = $("#panelBody"); box.replaceChildren();
    const activeTab = $("#panelTabs .segb.on")?.dataset?.tab || "spec";

    if (activeTab === "spec") {
      box.append(el("h4", {}, "設計概念與規格對照"));
      const table = el("table", {},
        el("tr", {}, el("th", {}, "欄位名稱"), el("th", {}, "交付內容"), el("th", {}, "審查規範")),
        el("tr", {}, el("td", {}, "任務編號"), el("td", { class: "comp-hl" }, pkg.id), el("td", {}, "一字不差")),
        el("tr", {}, el("td", {}, "主體 ID"), el("td", {}, pkg.subjectId || "—"), el("td", {}, "snake_case")),
        el("tr", {}, el("td", {}, "產出平台"), el("td", {}, pkg.manifest?.produced_by || "—"), el("td", {}, "ChatGPT / Gemini"))
      );

      // 若有 concept.json 展開詳細規格（身高/體重/體脂/罩杯/色系）
      if (pkg.concept) {
        const c = pkg.concept;
        if (c.height_cm || c.height_m) table.append(el("tr", {}, el("td", {}, "身高 / 體長"), el("td", { class: "comp-hl" }, String(c.height_cm || c.height_m)), el("td", {}, "符合常態分佈")));
        if (c.weight_kg) table.append(el("tr", {}, el("td", {}, "體重"), el("td", { class: "comp-hl" }, String(c.weight_kg)), el("td", {}, "符合常態分佈")));
        if (c.body_fat_pct) table.append(el("tr", {}, el("td", {}, "體脂率"), el("td", { class: "comp-hl" }, String(c.body_fat_pct)), el("td", {}, "合理健康區間")));
        if (c.cup_size) table.append(el("tr", {}, el("td", {}, "罩杯（女性）"), el("td", { class: "comp-hl" }, String(c.cup_size)), el("td", {}, "女角色常態")));
        if (c.outfit_palette) table.append(el("tr", {}, el("td", {}, "服裝色系"), el("td", {}, Array.isArray(c.outfit_palette) ? c.outfit_palette.join(" / ") : String(c.outfit_palette)), el("td", {}, "1~3種和諧組合")));
        if (c.appearance_palette) table.append(el("tr", {}, el("td", {}, "宙獸色系"), el("td", {}, Array.isArray(c.appearance_palette) ? c.appearance_palette.join(" / ") : String(c.appearance_palette)), el("td", {}, "1~3種和諧組合")));
        if (c.hair_palette) table.append(el("tr", {}, el("td", {}, "頭髮色系"), el("td", {}, Array.isArray(c.hair_palette) ? c.hair_palette.join(" / ") : String(c.hair_palette)), el("td", {}, "1~2種合理組合")));
        if (c.eye_palette) table.append(el("tr", {}, el("td", {}, "瞳孔色系"), el("td", {}, Array.isArray(c.eye_palette) ? c.eye_palette.join(" / ") : String(c.eye_palette)), el("td", {}, "1~2種合理組合")));
        if (c.timeline) table.append(el("tr", {}, el("td", {}, "文化時間線"), el("td", { class: "comp-hl" }, String(c.timeline)), el("td", {}, "歷史與未來想像")));
        if (c.region) table.append(el("tr", {}, el("td", {}, "文化地區"), el("td", { class: "comp-hl" }, String(c.region)), el("td", {}, "跨洲與星際")));
        if (c.ethnicity) table.append(el("tr", {}, el("td", {}, "人種表型"), el("td", { class: "comp-hl" }, String(c.ethnicity)), el("td", {}, "獨立生理特徵")));
        if (c.base_parents || c.bases) {
          const pStr = Array.isArray(c.base_parents) ? c.base_parents.join("、") : (c.base_parents || "");
          const bStr = Array.isArray(c.bases) ? c.bases.join(" + ") : (c.bases || "");
          const fullBase = pStr && bStr ? `[${pStr}] → ${bStr}` : (pStr || bStr);
          table.append(el("tr", {}, el("td", {}, "外觀基底階層"), el("td", {}, fullBase), el("td", {}, "類人奇幻/生物/載具/器械")));
        }
        if (c.role) table.append(el("tr", {}, el("td", {}, "定位 / 職責"), el("td", { class: "comp-hl" }, String(c.role)), el("td", {}, "戰鬥/角色定位")));
        if (c.personality) table.append(el("tr", {}, el("td", {}, "性格"), el("td", {}, Array.isArray(c.personality) ? c.personality.join("、") : String(c.personality)), el("td", {}, "特質展現")));
        if (c.temperament) table.append(el("tr", {}, el("td", {}, "性情 / 性格"), el("td", {}, Array.isArray(c.temperament) ? c.temperament.join("、") : String(c.temperament)), el("td", {}, "共用性情庫")));
        if (c.archetype) table.append(el("tr", {}, el("td", {}, "文化原型"), el("td", {}, typeof c.archetype === "object" ? c.archetype.name : String(c.archetype)), el("td", {}, "意象轉化")));
      }
      box.append(table);

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

    } else if (activeTab === "cli") {
      box.append(el("h4", {}, "解壓與管線部署"));
      const root = state.root || DEFAULT_ROOT;
      const psCmd = `Expand-Archive -Path "$HOME\\Downloads\\${pkg.id}.zip" -DestinationPath "${root}\\Art\\_incoming\\" -Force`;
      const valCmd = `python tools/validate_art.py Art/_incoming/${pkg.id} --apply`;
      box.append(
        el("p", { class: "muted" }, "一鍵解壓指令（PowerShell）："),
        el("div", { class: "cmd" }, el("code", {}, psCmd), el("button", { type: "button", class: "mini", onclick: () => copyText(psCmd, "已複製解壓指令") }, "複製")),
        el("p", { class: "muted", style: "margin-top:10px" }, "驗收搬入管線指令（validate-art）："),
        el("div", { class: "cmd" }, el("code", {}, valCmd), el("button", { type: "button", class: "mini", onclick: () => copyText(valCmd, "已複製驗收指令") }, "複製"))
      );
    }
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
  function setViewMode(mode) {
    state.activeTab = mode;
    if (mode === "review") {
      $("#forgeView").style.display = "none";
      $("#reviewView").classList.add("active");
      $("#viewBtnForge").classList.remove("on");
      $("#viewBtnReview").classList.add("on");
      $("#forgeHeaderControls").style.display = "none";
      if (!reviewState.packages.length) {
        loadSampleFixtures();
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
  function toast(msg) { const t = $("#toast"); t.textContent = msg; t.classList.add("show"); clearTimeout(toastTimer); toastTimer = setTimeout(() => t.classList.remove("show"), 1600); }

  // ---- 持久化 ----
  function save() {
    try {
      localStorage.setItem(LS_KEY, JSON.stringify({
        catalog: C.version, seed: state.seed, platform: state.platform, taskId: state.taskId, includeReview: state.includeReview,
        character: state.character, creature: state.creature, locks: state.locks, opts: state.opts, notes: state.notes,
        subjectIds: state.subjectIds, history: state.history, root: state.root, customOptions: state.customOptions
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
        customOptions: d.customOptions || {}
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

    $("#reroll").addEventListener("click", () => reseed(newSeed(), true));
    $("#seed").addEventListener("change", e => reseed(e.target.value, true));
    document.querySelectorAll("input[name=platform]").forEach(r => r.addEventListener("change", e => { state.platform = e.target.value; renderAll(); }));
    $("#notes").addEventListener("input", e => { state.notes = e.target.value; renderOutput(); save(); });
    $("#copy").addEventListener("click", () => { copyText($("#prompt").value, "已複製 prompt"); record(); });
    $("#download").addEventListener("click", () => { download(lastBuild.ctx.taskId + ".prompt.md", $("#prompt").value, "text/markdown"); record(); });
    $("#export").addEventListener("click", () => {
      const b = lastBuild; if (!b.ctx.concept) { toast("此任務沒有概念"); return; }
      download(b.ctx.subjectId + ".concept.json", JSON.stringify(b.ctx.concept, null, 2), "application/json");
    });
    $("#open").addEventListener("click", () => window.open(state.platform === "gemini" ? "https://gemini.google.com/app" : "https://chatgpt.com/", "_blank", "noopener"));
    $("#root").addEventListener("change", e => { state.root = e.target.value.trim().replace(/[\\/]+$/, ""); renderOutput(); save(); });

    // 審查台控制列事件
    $("#btnScanDir").addEventListener("click", scanIncomingDirectory);
    $("#btnLoadZip").addEventListener("click", () => $("#zipFileInput").click());
    $("#zipFileInput").addEventListener("change", async e => {
      const files = e.target.files;
      for (const f of files) {
        if (f.name.endsWith(".zip")) await loadZipFile(f);
      }
    });
    $("#btnLoadFixtures").addEventListener("click", loadSampleFixtures);

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
