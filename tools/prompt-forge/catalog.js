// COSMIMERA prompt-forge · 選項池與規格資料（唯一來源）
// 改動規則見 README.md：新增選項 → minor；改既有 ID / 刪除 / 改動作事件 → major。
// 權重 w 為相對值，不必加總為 100。
window.COSMI_CATALOG = {
  version: "1.0.0",

  ELEMENTS: [
    { v: "metal",   zh: "金", palette: "銀白、淡金、鋼藍高光" },
    { v: "wood",    zh: "木", palette: "翠綠、嫩芽黃綠、木褐" },
    { v: "water",   zh: "水", palette: "青藍、湖綠、白色浪花" },
    { v: "fire",    zh: "火", palette: "橘紅、熔金、炭黑邊緣" },
    { v: "earth",   zh: "土", palette: "赭黃、陶土紅、苔綠點綴" },
    { v: "ice",     zh: "冰", palette: "冰藍、霜白、淡紫折射" },
    { v: "wind",    zh: "風", palette: "薄荷綠、雲白、淡青流線" },
    { v: "thunder", zh: "雷", palette: "電紫、亮黃、白色閃光" },
    { v: "light",   zh: "光", palette: "金白、暖黃光暈、淡粉" },
    { v: "shadow",  zh: "影", palette: "深紫、墨黑、幽藍邊光" }
  ],
  ELEMENT_COUNT: [ { n: 1, w: 40 }, { n: 2, w: 45 }, { n: 3, w: 15 } ],

  VESSELS: [
    { v: "talisman_card", zh: "符籙卡片", sub: "招式", look: "可甩出的紙/金屬符卡，書寫發光符文，命中後展開成封印陣" },
    { v: "glass_bead",    zh: "琉璃珠",   sub: "特性", look: "掌心大小的多色琉璃珠，內有流動星雲，拋擲後化為光泡包覆" },
    { v: "magic_cube",    zh: "魔術方塊", sub: "超載", look: "可旋轉的多面方塊，各面刻屬性紋章，投擲後轉動解鎖成牢籠" }
  ],

  CHARACTER: {
    ethnicity: [
      { v: "東亞", w: 25, subs: ["台灣漢人", "中國華北", "中國華南", "日本", "韓國", "蒙古"] },
      { v: "白人", w: 25, subs: ["北歐", "西歐", "南歐地中海", "東歐斯拉夫", "北美", "澳紐"] },
      { v: "各地原住民", w: 25, subs: ["台灣原住民族", "阿伊努", "毛利", "夏威夷原住民", "北美平原原住民", "北美西北海岸原住民", "安地斯原住民（克丘亞/艾馬拉）", "亞馬遜原住民", "薩米", "因紐特", "澳洲原住民", "西伯利亞原住民"] },
      { v: "其他", w: 25, subs: ["南亞", "東南亞", "西非", "東非", "南部非洲", "北非", "中東", "中亞", "拉丁美洲混血", "加勒比", "太平洋島民"] }
    ],
    gender: [ { v: "男", w: 50 }, { v: "女", w: 50 }, { v: "中性外觀", w: 0 } ],
    age: [
      { v: "兒童", w: 10, min: 9, max: 12 },
      { v: "青少年", w: 50, min: 13, max: 19 },
      { v: "青年", w: 25, min: 20, max: 35 },
      { v: "中年", w: 10, min: 36, max: 55 },
      { v: "長者", w: 5, min: 56, max: 80 }
    ],
    personality: ["熱血", "冷靜", "害羞", "毒舌", "樂天", "好奇", "傲嬌", "沉穩", "衝動", "溫柔", "腹黑", "迷糊", "正義感強", "憤世嫉俗", "慵懶", "完美主義", "話很多", "寡言", "膽小但堅強", "自信過剩", "愛照顧人", "愛惡作劇"],
    style: ["星際探險裝", "街頭機能", "傳統民族服融合現代剪裁", "學院制服改造", "蒸汽龐克", "太陽龐克", "戶外登山", "復古運動", "魔法學徒袍", "機甲駕駛服", "遊牧旅人", "海洋水手", "賽博市集", "田園農作", "考古學者", "祭典華服改良"],
    interests: ["天文觀星", "料理", "攝影", "古代遺跡", "機械改裝", "音樂", "繪畫", "釣魚", "園藝", "武術", "收集化石", "寫日記", "滑板", "編織", "地圖測繪", "民俗傳說", "跑酷", "電子遊戲", "泡茶/咖啡", "觀察宙獸生態"],
    likes: ["甜食", "下雨天", "毛茸茸的生物", "星空", "舊書", "熱湯", "高處的風", "閃亮的礦石", "熱鬧祭典", "安靜的清晨", "雷雨", "海浪聲", "手作物", "辣的食物", "老地圖"],
    dislikes: ["蟲子", "吵雜", "謊言", "高處", "黑暗", "苦味", "等待", "被小看", "冰冷的金屬", "濕衣服", "早起", "輸", "擁擠人群", "打雷"],
    role: ["玩家角色預設外觀", "隊友 NPC", "大廳 NPC：研究員", "大廳 NPC：孵蛋師", "大廳 NPC：商人", "星球原住民 NPC", "對手訓練家"]
  },

  CREATURE: {
    bases: [
      { cat: "生物", w: 40, subs: ["哺乳類", "鳥類", "爬蟲類", "兩棲類", "魚類", "昆蟲", "甲殼類", "軟體動物", "植物/菌類", "古生物"] },
      { cat: "人形", w: 20, subs: ["戰士", "術士", "工匠", "舞者", "僧侶/修行者", "孩童精靈"] },
      { cat: "器械", w: 20, subs: ["工具", "武器", "樂器", "天文/測量儀器", "機關人偶", "燈具/鐘錶"] },
      { cat: "載具", w: 20, subs: ["戰車", "帆船", "飛行器", "列車", "機車", "熱氣球", "雪橇", "潛水艇"] }
    ],
    base_count: [ { n: 1, w: 55 }, { n: 2, w: 45 } ],
    role: ["遠程輸出", "近戰刺客", "前排坦克", "輔助治療", "控場", "突擊爆發", "場地佈署"],
    temperament: ["溫順", "好鬥", "膽怯", "高傲", "頑皮", "沉默", "黏人", "神秘", "暴躁", "慵懶"],
    size_profile: [
      { v: "S→M→L→XL", w: 50 },
      { v: "S→M→M→L", w: 25 },
      { v: "S→L→XL→XL", w: 25 }
    ],
    // type：神祇 | 英雄 | 歷史名人 | 傳說 | 文學；sensitivity: review 預設排除（creature-concept-pipeline.md#文化原型準則）
    archetypes: [
      { region: "東亞", name: "后羿射日", type: "英雄", motifs: "長弓、九日、墜落的金烏", sensitivity: "ok" },
      { region: "東亞", name: "精衛填海", type: "傳說", motifs: "小鳥、銜石、怒海", sensitivity: "ok" },
      { region: "東亞", name: "夸父逐日", type: "傳說", motifs: "巨人、長杖、乾涸河流、桃林", sensitivity: "ok" },
      { region: "東亞", name: "孫悟空", type: "文學", motifs: "金箍棒、筋斗雲、七十二變", sensitivity: "ok" },
      { region: "東亞", name: "鄭和下西洋", type: "歷史名人", motifs: "寶船、羅盤、長頸鹿貢品", sensitivity: "ok" },
      { region: "東亞", name: "張衡", type: "歷史名人", motifs: "地動儀、銅龍銜珠、渾天儀", sensitivity: "ok" },
      { region: "東亞", name: "桃太郎", type: "傳說", motifs: "桃子、犬猿雉、黍糰", sensitivity: "ok" },
      { region: "東亞", name: "浦島太郎", type: "傳說", motifs: "海龜、龍宮、玉手箱的煙", sensitivity: "ok" },
      { region: "東亞", name: "李舜臣", type: "歷史名人", motifs: "龜甲船、鶴翼陣、戰鼓", sensitivity: "ok" },
      { region: "東亞", name: "世宗大王", type: "歷史名人", motifs: "文字刻版、測雨器、日晷", sensitivity: "ok" },
      { region: "東亞", name: "哪吒", type: "神祇", motifs: "風火輪、混天綾、乾坤圈", sensitivity: "review" },
      { region: "東亞", name: "須佐之男", type: "神祇", motifs: "八岐大蛇、草薙劍、暴風", sensitivity: "review" },
      { region: "東亞", name: "台灣原住民射日傳說", type: "傳說", motifs: "雙日、遠征的父子、柚子樹", sensitivity: "review" },
      { region: "南亞", name: "阿育王", type: "歷史名人", motifs: "石柱、獅首柱頭、法輪", sensitivity: "ok" },
      { region: "南亞", name: "阿耶波多", type: "歷史名人", motifs: "零、星盤、月食軌跡", sensitivity: "ok" },
      { region: "南亞", name: "羅摩", type: "神祇", motifs: "神弓、猴軍、跨海石橋", sensitivity: "review" },
      { region: "東南亞", name: "巴庫納瓦", type: "傳說", motifs: "吞月海龍、銅鑼聲、七個月亮", sensitivity: "ok" },
      { region: "東南亞", name: "加多卡加", type: "英雄", motifs: "皮影、鋼筋鐵骨、飛翔斗篷", sensitivity: "ok" },
      { region: "東南亞", name: "徵氏姊妹", type: "歷史名人", motifs: "雙騎戰象、銅鼓、起義旗", sensitivity: "ok" },
      { region: "東南亞", name: "扶董天王", type: "英雄", motifs: "鐵馬噴火、竹叢、一夜長大", sensitivity: "ok" },
      { region: "西亞/中東", name: "吉爾伽美什", type: "英雄", motifs: "雪松林、天牛、不死草", sensitivity: "ok" },
      { region: "西亞/中東", name: "伊南娜", type: "神祇", motifs: "八芒星、冥界七門、獅子", sensitivity: "ok" },
      { region: "西亞/中東", name: "辛巴達", type: "文學", motifs: "大鵬鳥、島嶼巨鯨、羅盤", sensitivity: "ok" },
      { region: "西亞/中東", name: "伊本·西那", type: "歷史名人", motifs: "醫典、草藥、星盤", sensitivity: "ok" },
      { region: "西亞/中東", name: "花拉子米", type: "歷史名人", motifs: "代數符號、星盤、幾何圖紋", sensitivity: "ok" },
      { region: "非洲", name: "阿努比斯", type: "神祇", motifs: "胡狼頭、天秤、羽毛", sensitivity: "ok" },
      { region: "非洲", name: "荷魯斯", type: "神祇", motifs: "隼、荷魯斯之眼、太陽圓盤", sensitivity: "ok" },
      { region: "非洲", name: "阿南西", type: "傳說", motifs: "蜘蛛、故事寶盒、絲線", sensitivity: "ok" },
      { region: "非洲", name: "松迪亞塔·凱塔", type: "歷史名人", motifs: "鐵杖、獅子、說書人的科拉琴", sensitivity: "ok" },
      { region: "非洲", name: "曼薩·穆薩", type: "歷史名人", motifs: "金砂、駱駝商隊、泥磚清真寺尖塔", sensitivity: "ok" },
      { region: "非洲", name: "恩津加女王", type: "歷史名人", motifs: "談判石座、弓、外交印信", sensitivity: "ok" },
      { region: "非洲", name: "尚戈", type: "神祇", motifs: "雙刃斧、雷石、紅白配色", sensitivity: "review" },
      { region: "歐洲", name: "索爾", type: "神祇", motifs: "戰錘、山羊車、雷霆", sensitivity: "ok" },
      { region: "歐洲", name: "奧丁", type: "神祇", motifs: "雙鴉、八足馬、獨眼、長矛", sensitivity: "ok" },
      { region: "歐洲", name: "赫拉克勒斯", type: "英雄", motifs: "獅皮、棍棒、十二試煉", sensitivity: "ok" },
      { region: "歐洲", name: "雅典娜", type: "神祇", motifs: "貓頭鷹、橄欖、蛇髮盾", sensitivity: "ok" },
      { region: "歐洲", name: "亞瑟王", type: "傳說", motifs: "石中劍、圓桌、湖中仙女", sensitivity: "ok" },
      { region: "歐洲", name: "庫丘林", type: "英雄", motifs: "獵犬、魔槍、戰狂變身", sensitivity: "ok" },
      { region: "歐洲", name: "芭芭雅嘎", type: "傳說", motifs: "雞腳小屋、臼與杵、骷髏燈", sensitivity: "ok" },
      { region: "歐洲", name: "維那莫寧", type: "英雄", motifs: "康特勒琴、三寶磨坊、白鬍子", sensitivity: "ok" },
      { region: "歐洲", name: "達文西", type: "歷史名人", motifs: "撲翼機、手稿鏡像字、齒輪", sensitivity: "ok" },
      { region: "歐洲", name: "伽利略", type: "歷史名人", motifs: "望遠鏡、木星衛星、斜塔落球", sensitivity: "ok" },
      { region: "歐洲", name: "聖女貞德", type: "歷史名人", motifs: "白旗、鎧甲、火焰", sensitivity: "review" },
      { region: "美洲", name: "羽蛇神", type: "神祇", motifs: "羽毛蛇、金星、玉石", sensitivity: "ok" },
      { region: "美洲", name: "瑪雅雙子英雄", type: "英雄", motifs: "球賽、吹箭、冥界試煉", sensitivity: "ok" },
      { region: "美洲", name: "庫魯皮拉", type: "傳說", motifs: "反轉的腳、火紅頭髮、森林守護", sensitivity: "ok" },
      { region: "美洲", name: "哈莉特·塔布曼", type: "歷史名人", motifs: "北極星、提燈、地下鐵路", sensitivity: "ok" },
      { region: "美洲", name: "西蒙·玻利瓦", type: "歷史名人", motifs: "軍刀、安地斯山脈、白馬", sensitivity: "ok" },
      { region: "美洲", name: "萊特兄弟", type: "歷史名人", motifs: "雙翼機、自行車工坊、沙丘", sensitivity: "ok" },
      { region: "美洲", name: "雷鳥", type: "傳說", motifs: "巨鳥、雷鳴振翅、閃電眼", sensitivity: "review" },
      { region: "美洲", name: "因蒂", type: "神祇", motifs: "太陽金面、安地斯梯田、秘魯金器", sensitivity: "review" },
      { region: "大洋洲", name: "卡美哈梅哈一世", type: "歷史名人", motifs: "羽毛斗篷、浪花、雙體獨木舟", sensitivity: "ok" },
      { region: "大洋洲", name: "拉帕努伊鳥人", type: "傳說", motifs: "烏燕鷗蛋、懸崖、摩艾石像", sensitivity: "ok" },
      { region: "大洋洲", name: "毛伊", type: "神祇", motifs: "魚鉤、套索捕日、釣起島嶼", sensitivity: "review" },
      { region: "大洋洲", name: "彩虹蛇", type: "傳說", motifs: "彩虹、水坑、大地紋路", sensitivity: "review" },
      { region: "中亞/北亞", name: "瑪納斯", type: "英雄", motifs: "白鷹、長矛、天山", sensitivity: "ok" },
      { region: "中亞/北亞", name: "兀魯伯", type: "歷史名人", motifs: "巨型六分儀、星表、撒馬爾罕藍磚", sensitivity: "ok" },
      { region: "中亞/北亞", name: "成吉思汗", type: "歷史名人", motifs: "蒼狼、白鹿、草原騎兵", sensitivity: "review" },
      { region: "中亞/北亞", name: "格薩爾王", type: "英雄", motifs: "神駒、雪山、戰盔", sensitivity: "review" },
      { region: "極地", name: "賽德娜", type: "神祇", motifs: "海底、海豹與鯨、長髮", sensitivity: "review" },
      { region: "極地", name: "羅爾德·阿蒙森", type: "歷史名人", motifs: "雪橇犬、極地帳篷、羅盤", sensitivity: "ok" },
      { region: "現代科學", name: "居禮夫人", type: "歷史名人", motifs: "發光試管、元素符號、實驗袍", sensitivity: "ok" },
      { region: "現代科學", name: "南丁格爾", type: "歷史名人", motifs: "提燈、繃帶、圓餅統計圖", sensitivity: "ok" }
    ]
  },

  SIZE_CLASSES: {
    S:  { zh: "S（< 0.6 m）",  tris: "5–8k",   tex: 1024 },
    M:  { zh: "M（0.6–1.4 m）", tris: "8–15k",  tex: 1024 },
    L:  { zh: "L（1.4–2.5 m）", tris: "15–25k", tex: 2048 },
    XL: { zh: "XL（> 2.5 m）", tris: "25–35k", tex: 2048 }
  },
  STAGES: [
    { v: "juvenile", zh: "幼年" }, { v: "adolescent", zh: "少年" },
    { v: "adult", zh: "成年" }, { v: "overload", zh: "超載（戰鬥中短暫進化）" }
  ],

  // 動作 clip 唯一來源（art-asset-pipeline.md#動作）。f = 30 fps 幀數；loop；rm = root motion；ev = 事件幀
  ACTIONS: {
    character: [
      { id: "idle", zh: "待機", f: 60, loop: true },
      { id: "walk", zh: "走路", f: 32, loop: true, ev: ["ev_footstep@8", "ev_footstep@24"] },
      { id: "run", zh: "奔跑", f: 22, loop: true, ev: ["ev_footstep@5", "ev_footstep@16"] },
      { id: "sneak_idle", zh: "潛行待機", f: 60, loop: true },
      { id: "sneak_move", zh: "潛行移動", f: 40, loop: true },
      { id: "roll_dodge", zh: "翻滾閃避（無敵幀）", f: 20, rm: true, ev: ["ev_end_lock@16"] },
      { id: "jump", zh: "跳躍（起跳/滯空/落地）", f: 30 },
      { id: "block", zh: "格擋（舉起→維持→放下）", f: 30 },
      { id: "hit_react", zh: "受擊", f: 15 },
      { id: "knockdown", zh: "倒地", f: 30 },
      { id: "getup", zh: "起身", f: 30 },
      { id: "command_point", zh: "命令：指向目標", f: 24, ev: ["ev_release@10"] },
      { id: "command_attack", zh: "命令：全力進攻", f: 24, ev: ["ev_release@10"] },
      { id: "command_recall", zh: "命令：後退集合", f: 24, ev: ["ev_release@10"] },
      { id: "summon", zh: "召喚宙獸", f: 36, ev: ["ev_spawn_fx@18"] },
      { id: "recall", zh: "收回宙獸", f: 30, ev: ["ev_spawn_fx@14"] },
      { id: "item_use_self", zh: "使用道具（自身）", f: 40, ev: ["ev_spawn_fx@24"] },
      { id: "item_use_other", zh: "使用道具（他者/宙獸）", f: 40, ev: ["ev_release@20"] },
      { id: "field_deploy", zh: "佈署場地", f: 48, ev: ["ev_spawn_fx@30"] },
      { id: "backpack_open", zh: "使用背包（取下→翻找→收回）", f: 60 },
      { id: "talk_idle", zh: "對話待機", f: 90, loop: true },
      { id: "talk_gesture", zh: "對話手勢", f: 45 },
      { id: "pet_s", zh: "撫摸 S 體型（蹲下摸頭）", f: 60, ev: ["ev_spawn_fx@30"] },
      { id: "pet_m", zh: "撫摸 M 體型（彎腰）", f: 60, ev: ["ev_spawn_fx@30"] },
      { id: "pet_l", zh: "撫摸 L 體型（抬手摸頸）", f: 60, ev: ["ev_spawn_fx@30"] },
      { id: "pet_xl", zh: "撫摸 XL 體型（宙獸低頭，角色雙手撫摸）", f: 72, ev: ["ev_spawn_fx@36"] },
      { id: "throw_card", zh: "投擲：符籙卡片（雙指甩出）", f: 24, ev: ["ev_release@12"] },
      { id: "throw_bead", zh: "投擲：琉璃珠（下手拋擲）", f: 28, ev: ["ev_release@15"] },
      { id: "throw_cube", zh: "投擲：魔術方塊（轉動後上手投）", f: 32, ev: ["ev_release@18"] },
      { id: "victory", zh: "勝利", f: 60 },
      { id: "defeated", zh: "戰敗", f: 45 }
    ],
    creature: [
      { id: "idle", zh: "待機", f: 60, loop: true },
      { id: "walk", zh: "移動", f: 32, loop: true, ev: ["ev_footstep@8"] },
      { id: "run", zh: "奔跑", f: 20, loop: true, ev: ["ev_footstep@5"] },
      { id: "sneak", zh: "潛行", f: 40, loop: true },
      { id: "block", zh: "格擋", f: 30 },
      { id: "dodge_miss", zh: "閃避（MISS 殘影）", f: 14, rm: true },
      { id: "hit_react", zh: "受擊", f: 15 },
      { id: "faint", zh: "倒下", f: 40 },
      { id: "atk_melee_front", zh: "近戰：正前", f: 26, ev: ["ev_hit@12", "ev_end_lock@20"] },
      { id: "atk_melee_sweep", zh: "近戰：橫掃", f: 30, ev: ["ev_hit@14", "ev_end_lock@24"] },
      { id: "atk_melee_burst", zh: "近戰：擴散", f: 34, ev: ["ev_spawn_fx@16", "ev_hit@18"] },
      { id: "atk_ranged_line", zh: "射擊：直線", f: 30, ev: ["ev_spawn_fx@14"] },
      { id: "atk_ranged_cone", zh: "射擊：扇形", f: 30, ev: ["ev_spawn_fx@14"] },
      { id: "atk_ranged_arc", zh: "射擊：拋物線", f: 34, ev: ["ev_spawn_fx@18"] },
      { id: "sup_self", zh: "輔助：自身", f: 36, ev: ["ev_spawn_fx@18"] },
      { id: "sup_ally", zh: "輔助：他者", f: 36, ev: ["ev_spawn_fx@18"] },
      { id: "field_deploy", zh: "佈署場地", f: 48, ev: ["ev_spawn_fx@28"] },
      { id: "petted", zh: "接受撫摸（依體型對位 Socket_PetTouch）", f: 60 },
      { id: "summon_appear", zh: "被召喚登場", f: 36 },
      { id: "recall_vanish", zh: "被收回", f: 24 },
      { id: "capture_struggle", zh: "被收服時掙扎", f: 45 },
      { id: "overload_enter", zh: "進入超載", f: 60, ev: ["ev_spawn_fx@20"] },
      { id: "overload_exit", zh: "超載結束", f: 36 },
      { id: "hatch", zh: "破蛋而出（僅幼年）", f: 72 },
      { id: "victory", zh: "勝利", f: 60 }
    ]
  },

  VFX: {
    shapes: [
      { v: "melee_front", zh: "近戰：正前" }, { v: "melee_sweep", zh: "近戰：橫掃" }, { v: "melee_burst", zh: "近戰：擴散" },
      { v: "ranged_line", zh: "射擊：直線" }, { v: "ranged_cone", zh: "射擊：扇形" }, { v: "ranged_arc", zh: "射擊：拋物線" },
      { v: "support_self", zh: "輔助：自身" }, { v: "support_ally", zh: "輔助：他者" }, { v: "field", zh: "佈署場地" }
    ],
    tiers: [ { v: "light", zh: "輕" }, { v: "medium", zh: "中" }, { v: "heavy", zh: "重" }, { v: "overload", zh: "超載必殺" } ],
    common: [
      { v: "summon", zh: "召喚光陣" }, { v: "recall", zh: "收回光流" }, { v: "overload_aura", zh: "超載氣場" },
      { v: "miss_afterimage", zh: "MISS 殘影" }, { v: "hit_spark", zh: "屬性命中火花" }, { v: "pet_hearts", zh: "撫摸回饋" },
      { v: "capture_talisman_card", zh: "符籙卡片收服" }, { v: "capture_glass_bead", zh: "琉璃珠收服" }, { v: "capture_magic_cube", zh: "魔術方塊收服" }
    ]
  },

  RESEARCH_REGIONS: ["東亞", "南亞", "東南亞", "西亞/中東", "非洲", "歐洲", "美洲", "大洋洲", "中亞/北亞", "極地"]
};
