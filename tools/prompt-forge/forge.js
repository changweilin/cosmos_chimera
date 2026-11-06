// COSMIMERA prompt-forge · 介面與組裝
(function () {
  const TOOL_VERSION = "1.0.0";
  const SPEC = "1";
  const C = window.COSMI_CATALOG, TASKS = window.COSMI_TASKS;
  const LS_KEY = "cosmimera.prompt-forge.v1";
  const DEFAULT_ROOT = "C:\\Users\\user\\Documents\\app\\cosmos_chimera";

  // ---- seeded RNG：同 seed 的「全部隨機」結果可重現，方便日後追溯任務 ----
  function hashStr(s) { let h = 2166136261; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); } return h >>> 0; }
  function mulberry32(a) { return function () { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
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

  // ---- 概念欄位定義：options(state) 回傳 [{v,w}]；count 決定抽幾個 ----
  const CH = C.CHARACTER, CR = C.CREATURE;
  const state = {
    seed: "", platform: "chatgpt", taskId: TASKS[0].id, includeReview: false,
    character: {}, creature: {}, locks: {}, opts: {}, notes: "", subjectIds: {}
  };

  const FIELDS = {
    character: [
      { key: "role", label: "定位", options: () => uniform(CH.role), count: [{ n: 1, w: 1 }] },
      { key: "ethnicity", label: "人種", options: () => CH.ethnicity.map(e => ({ v: e.v, w: e.w })), count: [{ n: 1, w: 1 }] },
      { key: "region", label: "地區", options: s => uniform((CH.ethnicity.find(e => e.v === s.character.ethnicity?.[0]) || CH.ethnicity[0]).subs), count: [{ n: 1, w: 1 }], dep: "ethnicity" },
      { key: "gender", label: "性別", options: () => CH.gender.map(g => ({ v: g.v, w: g.w })), count: [{ n: 1, w: 1 }] },
      { key: "age_band", label: "年齡層", options: () => CH.age.map(a => ({ v: a.v, w: a.w })), count: [{ n: 1, w: 1 }] },
      { key: "age", label: "年齡", options: s => { const b = CH.age.find(a => a.v === s.character.age_band?.[0]) || CH.age[1]; const r = []; for (let i = b.min; i <= b.max; i++) r.push({ v: String(i), w: 1 }); return r; }, count: [{ n: 1, w: 1 }], dep: "age_band" },
      { key: "personality", label: "性格", options: () => uniform(CH.personality), count: [{ n: 2, w: 1 }], max: 3 },
      { key: "style", label: "風格", options: () => uniform(CH.style), count: [{ n: 1, w: 1 }], max: 2 },
      { key: "interests", label: "興趣", options: () => uniform(CH.interests), count: [{ n: 2, w: 1 }], max: 3 },
      { key: "likes", label: "喜好", options: () => uniform(CH.likes), count: [{ n: 2, w: 1 }], max: 3 },
      { key: "dislikes", label: "厭惡", options: () => uniform(CH.dislikes), count: [{ n: 2, w: 1 }], max: 3 },
      { key: "vessel", label: "慣用收服道具", options: () => C.VESSELS.map(v => ({ v: v.v, w: 1, zh: v.zh })), count: [{ n: 1, w: 1 }] }
    ],
    creature: [
      { key: "elements", label: "屬性（1–3）", options: () => C.ELEMENTS.map(e => ({ v: e.v, w: 1, zh: e.zh + " " + e.v })), count: C.ELEMENT_COUNT, max: 3 },
      { key: "bases", label: "外觀基底（1–2）", options: () => CR.bases.flatMap(b => b.subs.map(s => ({ v: b.cat + "·" + s, w: b.w / b.subs.length }))), count: CR.base_count, max: 2 },
      { key: "region", label: "文化地區", options: s => uniform([...new Set(archetypePool(s).map(a => a.region))]), count: [{ n: 1, w: 1 }] },
      { key: "archetype", label: "文化原型", options: s => archetypePool(s).filter(a => a.region === s.creature.region?.[0]).map(a => ({ v: a.name, w: 1, zh: a.name + "（" + a.type + (a.sensitivity === "review" ? "・需審" : "") + "）" })), count: [{ n: 1, w: 1 }], dep: "region" },
      { key: "role", label: "戰鬥定位", options: () => uniform(CR.role), count: [{ n: 1, w: 1 }] },
      { key: "temperament", label: "性情", options: () => uniform(CR.temperament), count: [{ n: 1, w: 1 }] },
      { key: "size_profile", label: "體型（幼→少→成→超載）", options: () => CR.size_profile.map(p => ({ v: p.v, w: p.w })), count: [{ n: 1, w: 1 }] }
    ]
  };
  function archetypePool(s) { return CR.archetypes.filter(a => s.includeReview || a.sensitivity === "ok"); }

  function rollField(kind, f, rng) {
    const opts = f.options(state);
    const n = pickWeighted(rng, f.count).n;
    state[kind][f.key] = pickN(rng, opts, n);
  }
  // 依欄位順序抽，讓相依欄位（地區←人種）看到最新的上游值
  function rollAll(kind, rng, force) {
    FIELDS[kind].forEach(f => {
      const locked = state.locks[kind + "." + f.key];
      const valid = (state[kind][f.key] || []).length > 0 && (state[kind][f.key] || []).every(v => f.options(state).some(o => o.v === v));
      if (force ? !locked || !valid : !valid) rollField(kind, f, rng);
    });
  }
  function revalidate(kind) { rollAll(kind, Math.random, false); }

  // ---- 概念輸出（prompt 內的 JSON） ----
  function zhOf(list, v) { const o = list.find(x => x.v === v); return o ? o.zh : v; }
  function characterConcept() {
    const c = state.character;
    return {
      id: subjectId("character"), role: c.role[0], ethnicity: c.ethnicity[0], region: c.region[0], gender: c.gender[0],
      age: Number(c.age[0]), age_band: c.age_band[0], personality: c.personality, style: c.style.join(" × "),
      interests: c.interests, likes: c.likes, dislikes: c.dislikes, vessel: c.vessel[0], vessel_zh: zhOf(C.VESSELS, c.vessel[0])
    };
  }
  function creatureConcept() {
    const c = state.creature, a = CR.archetypes.find(x => x.name === c.archetype[0]) || {};
    return {
      line_id: subjectId("creature"),
      elements: c.elements.map(e => e + "（" + zhOf(C.ELEMENTS, e).split(" ")[0] + "）"),
      element_palettes: c.elements.map(e => C.ELEMENTS.find(x => x.v === e).palette),
      bases: c.bases, region: c.region[0], archetype: c.archetype[0], archetype_type: a.type, motifs: a.motifs,
      sensitivity: a.sensitivity, role: c.role[0], temperament: c.temperament[0], size_profile: c.size_profile[0],
      formula: c.elements.join("+") + " × " + c.bases.join("+") + " × " + c.archetype[0] + "的意象"
    };
  }
  function subjectId(kind) {
    const custom = state.subjectIds[kind];
    if (custom) return custom;
    return (kind === "character" ? "chr_" : "line_") + state.seed;
  }

  // ---- 任務選項 ----
  function task() { return TASKS.find(t => t.id === state.taskId); }
  const env = () => ({ character: state.character, creature: state.creature });
  function taskOpts(t) {
    const o = state.opts[t.id] || (state.opts[t.id] = {});
    t.options.forEach(def => {
      const items = def.items ? def.items(o, env()) : [];
      // select 的選項可能隨概念改變（例：宙獸換了屬性），失效的值要重抽
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
      .filter(d => d.type === "checks" && !d.optional && (!d.when || d.when(o)) && o[d.key].length === 0)
      .forEach(d => errors.push("至少勾選一個「" + d.label + "」"));
    const s = stamp();
    const taskId = t.prefix + "-" + s.day + "-" + state.seed + "-" + s.hm;
    const header = "[COSMIMERA-TASK spec=" + SPEC + " tool=" + TOOL_VERSION + " catalog=" + C.version + " template=" + t.id + "@" + t.version + " task_id=" + taskId + " seed=" + state.seed + "]";
    const concept = subj === "character" ? characterConcept() : subj === "creature" ? creatureConcept() : null;
    const ctx = {
      header, taskId, task: t, opts: o, concept, notes: state.notes.trim(),
      platform: state.platform, platformName: state.platform === "gemini" ? "Gemini Argon（任務模式）" : "ChatGPT（對話版）",
      subjectId: subj ? subjectId(subj) : t.id === "capture-vessel" ? "vessels" : t.id === "culture-research" ? "region_" + o.region : "fx_" + o.element + "_" + o.shape
    };
    lastBuild = { ctx, errors, text: errors.length ? "" : t.build(ctx) };
    return lastBuild;
  }

  // ---- DOM ----
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
    const key = kind + "." + f.key, vals = state[kind][f.key] || [], opts = f.options(state);
    const label = v => (opts.find(o => o.v === v) || {}).zh || v;
    const lock = el("input", { type: "checkbox", title: "鎖定（全部隨機時不變）", checked: !!state.locks[key], onchange: e => { state.locks[key] = e.target.checked; save(); } });
    const dice = el("button", { class: "icon", type: "button", title: "重抽此欄", onclick: () => { rollField(kind, f, Math.random); revalidate(kind); renderAll(); } }, "🎲");
    let control;
    const multi = (f.max || 1) > 1;
    if (!multi) {
      control = el("select", { onchange: e => { state[kind][f.key] = [e.target.value]; revalidate(kind); renderAll(); } },
        opts.map(o => el("option", { value: o.v, selected: o.v === vals[0] }, (o.zh || o.v) + (o.w === 0 ? "（權重 0）" : ""))));
    } else {
      const chips = vals.map(v => el("span", { class: "chip" }, label(v), el("button", { type: "button", title: "移除", onclick: () => { state[kind][f.key] = vals.filter(x => x !== v); renderAll(); } }, "×")));
      const add = el("select", { onchange: e => { if (!e.target.value) return; state[kind][f.key] = vals.concat(e.target.value); renderAll(); }, disabled: vals.length >= f.max },
        el("option", { value: "" }, vals.length >= f.max ? "已達上限" : "＋ 加入"), opts.filter(o => !vals.includes(o.v)).map(o => el("option", { value: o.v }, o.zh || o.v)));
      control = el("div", { class: "chips" }, chips, add);
    }
    return el("div", { class: "field" }, el("label", {}, f.label), control, el("div", { class: "fctl" }, dice, el("label", { class: "lock" }, lock, "🔒")));
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
    $("#deliver").replaceChildren(
      el("ol", {},
        el("li", {}, "按「複製 prompt」→「開啟 " + (state.platform === "gemini" ? "Gemini" : "ChatGPT") + "」貼上送出", state.platform === "gemini" ? "（先切到任務/Agent 模式）" : "", "。後續任務（3D/動作）可把前一包的概念圖、concept.json、rig_spec.json 一起附上。"),
        el("li", {}, "下載 ", el("code", {}, b.ctx.taskId + ".zip"), " 到「下載」資料夾"),
        el("li", {}, "解壓到 ", el("code", {}, dest + "\\"), "，結果應為 ", el("code", {}, t.drop + "/" + b.ctx.taskId + "/manifest.json")),
        el("li", {}, "在 Claude Code 說：", el("code", {}, "驗收 " + t.drop + "/" + b.ctx.taskId)),
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
    $("#rec").textContent = task().platform === "gemini" ? "建議：Gemini Argon 任務模式" : "建議：ChatGPT 對話版";
    renderTasks(); renderConcepts(); renderOptions(); renderOutput(); renderHistory();
    save();
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

  // ---- 持久化（僅本機便利用途；壞掉就重置） ----
  function save() {
    try {
      localStorage.setItem(LS_KEY, JSON.stringify({
        catalog: C.version, seed: state.seed, platform: state.platform, taskId: state.taskId, includeReview: state.includeReview,
        character: state.character, creature: state.creature, locks: state.locks, opts: state.opts, notes: state.notes,
        subjectIds: state.subjectIds, history: state.history, root: state.root
      }));
    } catch (e) { /* 私密視窗或封鎖儲存：照常運作，只是不記憶 */ }
  }
  function load() {
    try {
      const d = JSON.parse(localStorage.getItem(LS_KEY) || "null");
      if (!d) return false;
      // catalog major 變動時舊的選項值可能不存在；revalidate 會補抽無效欄位，所以只保留歷史以外的資料於同 major
      const sameMajor = (d.catalog || "").split(".")[0] === C.version.split(".")[0];
      Object.assign(state, { platform: d.platform || "chatgpt", history: d.history || [], root: d.root || "", notes: d.notes || "" });
      if (TASKS.some(t => t.id === d.taskId)) state.taskId = d.taskId;
      if (sameMajor) Object.assign(state, { seed: d.seed, includeReview: !!d.includeReview, character: d.character || {}, creature: d.creature || {}, locks: d.locks || {}, opts: d.opts || {}, subjectIds: d.subjectIds || {} });
      return !!state.seed;
    } catch (e) { return false; }
  }

  function init() {
    $("#ver").textContent = "tool " + TOOL_VERSION + " · catalog " + C.version + " · spec " + SPEC;
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
    const had = load();
    $("#notes").value = state.notes;
    $("#root").value = state.root || DEFAULT_ROOT;
    if (had) renderAll(); else reseed(newSeed(), true);
  }
  document.addEventListener("DOMContentLoaded", init);
})();
