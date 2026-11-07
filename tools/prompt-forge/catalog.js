// COSMIMERA prompt-forge · 選項池與規格資料（唯一來源）
// 改動規則見 README.md：新增選項 → minor；改既有 ID / 刪除 / 改動作事件 → major。
// 權重 w 為相對值，不必加總為 100。
window.COSMI_CATALOG = {
  version: "1.3.0",

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
    { v: "shadow",  zh: "影", palette: "深紫、墨黑、幽藍邊光" },
    { v: "未知",    zh: "未知", palette: "留待生成決定" }
  ],
  ELEMENT_COUNT: [ { n: 1, w: 40 }, { n: 2, w: 45 }, { n: 3, w: 15 } ],

  VESSELS: [
    { v: "talisman_card", zh: "符籙卡片", sub: "招式", look: "可甩出的紙/金屬符卡，書寫發光符文，命中後展開成封印陣" },
    { v: "glass_bead",    zh: "琉璃珠",   sub: "特性", look: "掌心大小的多色琉璃珠，內有流動星雲，拋擲後化為光泡包覆" },
    { v: "magic_cube",    zh: "魔術方塊", sub: "超載", look: "可旋轉的多面方塊，各面刻屬性紋章，投擲後轉動解鎖成牢籠" },
    { v: "未知",          zh: "未知",     sub: "未知", look: "留待外包/模型階段自由發揮" }
  ],

  // 跨角色/宙獸共用之文化地區
  CULTURE_REGIONS: [
    "東亞", "南亞", "東南亞", "西亞/中東", "非洲", "歐洲", "美洲", "大洋洲",
    "中亞/北亞", "極地", "多元交融/世界都市", "星際宙域/跨星系殖民地", "未知"
  ],
  get RESEARCH_REGIONS() { return this.CULTURE_REGIONS; },

  // 跨角色/宙獸共用之文化時間線（包含歷史、近現代與不同幻想的未來時間線發展）
  TIMELINES: [
    { v: "遠古神話/原始神代", zh: "遠古神話/原始神代", desc: "神祇行走於世、原始混沌、創世圖騰與古老神明" },
    { v: "遠古石器/部落圖騰紀元", zh: "遠古石器/部落圖騰", desc: "岩壁彩繪、薩滿信仰、巨獸與原始聚落共生" },
    { v: "青銅神話/古典英雄時代", zh: "青銅神話/古典英雄", desc: "山海經與荷馬史詩、神兵青銅、英雄史詩遠征" },
    { v: "封建王朝/古典帝國盛世", zh: "古典帝國/封建盛世", desc: "絲路跨洲貿易、哲學百家、宏偉古帝國城堡與宮廷" },
    { v: "中世紀高奇幻/騎士魔導王國", zh: "中世紀高奇幻/魔導王國", desc: "圓桌騎士、魔導高塔、領地城堡、巨龍與法印" },
    { v: "大航海大發現/符文星盤開拓", zh: "大航海/符文星盤開拓", desc: "洋流探險、新大陸測繪、神秘海圖與巨艦遠航" },
    { v: "以太蒸汽/魔導第一次工業革命", zh: "以太蒸汽/魔導工革", desc: "齒輪工場、飛行飛艇、以太蒸氣重機關與發明潮" },
    { v: "柴油重工/符文鋼鐵要塞", zh: "柴油重工/符文裝甲", desc: "重裝履帶、鋼鐵要塞、柴油魔導內燃裝甲與要塞戰" },
    { v: "電氣霓虹/裝飾藝術時代", zh: "電氣霓虹/裝飾藝術", desc: "早期無線電、摩天摩登、爵士狂潮、早期電氣火花" },
    { v: "晶石賽博/霓虹魔導義體紀元", zh: "晶石賽博/魔導義體", desc: "霓虹雨夜、魔導晶片、賽博駭客、合成人神崇拜" },
    { v: "太陽龐克/生態科技綠色未來", zh: "太陽龐克/生態共生", desc: "生物建築、太陽光帆、微氣候循環、萬物和諧共榮" },
    { v: "近未來軌道殖民/近地宙域", zh: "近未來軌道/太空電梯", desc: "太空電梯、月面質量投射器、低軌殖民站與空間站" },
    { v: "太空歌劇/星際魔導帝國", zh: "太空歌劇/星際魔導", desc: "曲率躍遷、星門艦隊、跨星系議會、行星巨構與宙航" },
    { v: "後啟示末日/廢土殘響重生", zh: "後啟示末日/廢土殘響", desc: "古文明崩解、荒原拾荒、機械殘骸、神明再臨與廢墟重組" },
    { v: "虛空超弦/維度升華神代", zh: "虛空超弦/維度升華", desc: "高維躍遷、超光速純能量體、超維宇宙巨構與因果改寫" },
    { v: "未知", zh: "未知", desc: "自由發揮未來或架空時空" }
  ],

  // 基礎單色系選項池（供角色服裝、頭髮與宙獸外觀多選組合，每項為單色，可多選組合成 1~3 色）
  COLOR_TONES: [
    // 中性通用色（全元素適用，或作基底/高光）
    { v: "曜石純黑", zh: "曜石黑", elements: ["all"], w: 9 },
    { v: "月華純白", zh: "月華白", elements: ["all"], w: 9 },
    { v: "象牙米白", zh: "象牙白", elements: ["all"], w: 8 },
    { v: "炭煙燻灰", zh: "炭煙灰", elements: ["all"], w: 8 },
    { v: "暮雲灰藍", zh: "暮雲灰藍", elements: ["water", "wind", "ice", "metal", "all"], w: 7 },
    { v: "鍍銀鋼白", zh: "鍍銀鋼白", elements: ["metal", "ice", "light", "all"], w: 8 },
    { v: "玄鐵青黑", zh: "玄鐵青黑", elements: ["metal", "earth", "shadow", "all"], w: 7 },
    { v: "古銅生鏽綠", zh: "古銅綠", elements: ["metal", "earth", "wood"], w: 6 },
    { v: "黃銅金", zh: "黃銅金", elements: ["metal", "thunder", "light"], w: 7 },

    // 火系相關
    { v: "熔岩赤紅", zh: "熔岩赤紅", elements: ["fire", "earth"], w: 8 },
    { v: "烈焰紅", zh: "烈焰紅", elements: ["fire"], w: 8 },
    { v: "珊瑚鮮橘", zh: "珊瑚鮮橘", elements: ["fire", "light", "water"], w: 7 },
    { v: "深酒紅", zh: "深酒紅", elements: ["fire", "shadow"], w: 7 },
    { v: "陶土磚紅", zh: "陶土磚紅", elements: ["earth", "fire"], w: 7 },

    // 木系相關
    { v: "蒼翡翠", zh: "蒼翡翠", elements: ["wood"], w: 8 },
    { v: "翠綠", zh: "翠綠", elements: ["wood"], w: 8 },
    { v: "嫩芽黃綠", zh: "嫩芽黃綠", elements: ["wood", "light"], w: 7 },
    { v: "橄欖墨綠", zh: "橄欖墨綠", elements: ["wood", "earth"], w: 7 },
    { v: "苔綠", zh: "苔蘚綠", elements: ["wood", "earth"], w: 7 },

    // 水系相關
    { v: "海軍深藍", zh: "海軍深藍", elements: ["water", "shadow"], w: 8 },
    { v: "深海湛藍", zh: "深海湛藍", elements: ["water"], w: 8 },
    { v: "克萊因藍", zh: "克萊因藍", elements: ["water", "thunder"], w: 7 },
    { v: "清透天藍", zh: "清透天藍", elements: ["water", "wind", "ice"], w: 7 },
    { v: "湖水綠", zh: "湖水綠", elements: ["water", "wood"], w: 7 },

    // 冰系相關
    { v: "冰晶天藍", zh: "冰晶天藍", elements: ["ice", "water"], w: 8 },
    { v: "霜雪透白", zh: "霜雪透白", elements: ["ice", "light"], w: 8 },
    { v: "純淨冰藍", zh: "純淨冰藍", elements: ["ice"], w: 8 },
    { v: "稜鏡淡紫", zh: "稜鏡淡紫", elements: ["ice", "light", "wind"], w: 7 },

    // 風系相關
    { v: "薄荷淺青", zh: "薄荷淺青", elements: ["wind", "water", "ice"], w: 8 },
    { v: "青瓷灰綠", zh: "青瓷灰綠", elements: ["wind", "wood", "water"], w: 7 },
    { v: "流線淡青", zh: "流線淡青", elements: ["wind", "light"], w: 7 },

    // 土系相關
    { v: "大地赭石", zh: "大地赭石", elements: ["earth"], w: 8 },
    { v: "卡其棕", zh: "卡其棕", elements: ["earth", "wood", "all"], w: 7 },
    { v: "焦糖茶褐", zh: "焦糖茶褐", elements: ["earth", "wood"], w: 7 },
    { v: "砂岩黃", zh: "砂岩黃", elements: ["earth", "light"], w: 7 },

    // 雷系相關
    { v: "雷電曜紫", zh: "雷電曜紫", elements: ["thunder", "shadow"], w: 8 },
    { v: "閃電熾黃", zh: "閃電熾黃", elements: ["thunder", "light"], w: 8 },
    { v: "電弧青藍", zh: "電弧青藍", elements: ["thunder", "water"], w: 7 },

    // 光系相關
    { v: "耀日光金", zh: "耀日光金", elements: ["light", "thunder", "metal", "fire"], w: 8 },
    { v: "晨曦金", zh: "晨曦金", elements: ["light", "fire"], w: 8 },
    { v: "琥珀金", zh: "琥珀金", elements: ["light", "earth", "thunder"], w: 7 },
    { v: "櫻花粉", zh: "櫻花粉", elements: ["light", "wood"], w: 7 },

    // 影系相關
    { v: "虛空深紫", zh: "虛空深紫", elements: ["shadow"], w: 8 },
    { v: "暗影幽黑", zh: "暗影幽黑", elements: ["shadow"], w: 8 },
    { v: "丁香淡紫", zh: "丁香淡紫", elements: ["shadow", "wind", "ice", "light"], w: 7 },
    { v: "緋紅血瞳色", zh: "緋紅血瞳色", elements: ["shadow", "fire"], w: 6 }
  ],

  // 取得受元素限定範圍的色系池（排除與元素不合的色系）
  getFilteredPalettes(creatureElements) {
    if (!creatureElements || !creatureElements.length) return this.COLOR_TONES;
    const cleanElems = creatureElements.map(e => e.replace(/（.*）/, "").replace(/\(.*?\)/, "").trim());
    if (cleanElems.includes("未知") || !cleanElems.length) return this.COLOR_TONES;
    return this.COLOR_TONES.filter(c => {
      if (c.elements.includes("all")) return true;
      return cleanElems.some(el => c.elements.includes(el));
    });
  },

  get PALETTES() { return this.COLOR_TONES; },

  // 角色性格與宙獸性情合併且共用
  PERSONALITIES: [
    "熱血", "冷靜", "害羞", "毒舌", "樂天", "好奇", "傲嬌", "沉穩", "衝動", "溫柔",
    "腹黑", "迷糊", "正義感強", "憤世嫉俗", "慵懶", "完美主義", "話很多", "寡言", "膽小但堅強", "自信過剩",
    "愛照顧人", "愛惡作劇", "堅毅不拔", "重情重義", "散漫隨性", "不拘小節", "好勝心切", "心軟善良", "謹慎過度", "狂妄不羈",
    "古靈精怪", "熱心腸", "死板認真", "隨遇而安", "護短", "深思熟慮", "機敏狡黠", "率真耿直", "愛操心", "豁達大度",
    "執著專注", "善解人意", "幽默風趣", "孤傲清冷", "自來熟",
    "溫順黏人", "好鬥凶悍", "膽怯機警", "高傲尊貴", "頑皮搗蛋", "沉默孤僻", "暴躁易怒", "警惕敏銳", "忠勇護主", "霸氣威嚴",
    "神秘莫測", "貪吃嗜睡", "狡詐靈動", "嗜好閃耀之物", "暴烈瘋狂", "護巢護崽", "好奇心過剩", "未知"
  ],

  CHARACTER: {
    // 角色人種：純粹外貌生理表型特徵，與文化地區互相獨立
    ethnicity: [
      { v: "東亞體系", w: 10, zh: "東亞體系（黑髮直髮、深褐瞳孔、溫潤黃調膚質、細緻面龐）" },
      { v: "東南亞/南島語族體系", w: 9, zh: "東南亞/南島體系（微卷深褐髮、深色雙瞳、健康小麥古銅膚色）" },
      { v: "南亞/印度河體系", w: 8, zh: "南亞體系（濃密黑鬈髮、大而深邃雙眸、暖棕至古銅膚色）" },
      { v: "中亞/突厥-歐亞體系", w: 7, zh: "中亞/歐亞體系（歐亞融合輪廓、高顴骨、琥珀/深褐眼、淺褐/黑髮）" },
      { v: "西亞/中東-閃米特體系", w: 7, zh: "西亞/中東體系（高挺立體鼻樑、波浪濃髮、橄欖膚色、深色長睫）" },
      { v: "北歐/日耳曼體系", w: 8, zh: "北歐體系（白皙膚色、淺金/白金/銀白髮、淺藍/灰藍眼、立體輪廓）" },
      { v: "凱爾特/不列顛體系", w: 7, zh: "凱爾特體系（火紅/金褐捲髮、明亮綠瞳/灰瞳、白皙膚質帶淺雀斑）" },
      { v: "地中海/南歐體系", w: 8, zh: "地中海體系（溫暖橄欖小麥膚、深褐波浪濃髮、深褐/琥珀眼）" },
      { v: "東歐/斯拉夫體系", w: 7, zh: "東歐/斯拉夫體系（明亮灰藍/淺綠眼、淺棕/金褐髮、清秀柔和輪廓）" },
      { v: "西非/班圖體系", w: 8, zh: "西非/班圖體系（緊緻捲曲黑髮、深邃黑曜瞳孔、飽滿深棕至黑曜石膚色、健美體格）" },
      { v: "東非/庫希特-尼羅特體系", w: 7, zh: "東非/尼羅特體系（修長四肢比例、高挑優雅輪廓、深棕膚色、杏形雙瞳）" },
      { v: "北非/柏柏爾-馬格里布體系", w: 6, zh: "北非/柏柏爾體系（淺褐至小麥膚色、深鬈髮、琥珀或淺褐色雙瞳）" },
      { v: "美洲原住民體系", w: 7, zh: "美洲原住民體系（烏黑濃密直髮、高顴骨、赤銅/暖褐膚色、銳利深褐眼）" },
      { v: "極地/因紐特-薩米體系", w: 6, zh: "極地體系（耐寒結實體型、黑直髮、紅潤小麥面頰、深色靈動雙眼）" },
      { v: "大洋洲/美拉尼西亞-玻里尼西亞體系", w: 6, zh: "大洋洲體系（高大健碩骨架、金色或深色捲髮、暖古銅膚色）" },
      { v: "多元混血/跨洲交融體系", w: 8, zh: "多元混血體系（融合多大洲面貌特徵、獨特氣質與異質面龐）" },
      { v: "奇幻星民特徵", w: 6, zh: "奇幻星民特徵（星痕印記、微光異色雙眸、輕微精靈尖耳、星塵髮色等架空特徵）" },
      { v: "未知", w: 2, zh: "未知（由生成自由決定人種外觀特質）" }
    ],
    gender: [ { v: "男", w: 50 }, { v: "女", w: 50 }, { v: "未知", w: 2 } ],
    age: [
      { v: "兒童", w: 10, min: 9, max: 12 },
      { v: "青少年", w: 50, min: 13, max: 19 },
      { v: "青年", w: 25, min: 20, max: 35 },
      { v: "中年", w: 10, min: 36, max: 55 },
      { v: "長者", w: 5, min: 56, max: 80 },
      { v: "未知", w: 2, min: 16, max: 25 }
    ],
    cup_sizes: [
      { v: "A", w: 12 }, { v: "B", w: 32 }, { v: "C", w: 30 }, { v: "D", w: 16 }, { v: "E", w: 6 }, { v: "F+", w: 3 }, { v: "未知", w: 2 }
    ],
    get outfit_palettes() { return window.COSMI_CATALOG.PALETTES; },
    hair_palettes: [
      { v: "自然黑", w: 16 },
      { v: "深栗褐", w: 14 },
      { v: "亞麻淺金", w: 12 },
      { v: "銀白", w: 12 },
      { v: "白金", w: 8 },
      { v: "石墨灰", w: 8 },
      { v: "薄荷青", w: 6 },
      { v: "海軍深藍", w: 6 },
      { v: "烈焰赤紅", w: 6 },
      { v: "櫻花粉", w: 6 },
      { v: "紫羅蘭", w: 6 },
      { v: "焦糖奶茶", w: 8 },
      { v: "酒紅", w: 6 },
      { v: "青木灰", w: 6 },
      { v: "香檳金", w: 7 },
      { v: "孔雀藍", w: 5 },
      { v: "珊瑚橘", w: 5 },
      { v: "純雪白", w: 8 },
      { v: "天藍", w: 6 },
      { v: "冰晶淡藍", w: 6 },
      { v: "丁香淡紫", w: 6 },
      { v: "挑染天藍", w: 7 },
      { v: "挑染緋紅", w: 7 },
      { v: "挑染白金", w: 6 },
      { v: "挑染薄荷綠", w: 6 },
      { v: "挑染亮紫", w: 5 },
      { v: "漸層粉櫻", w: 6 },
      { v: "漸層琥珀", w: 5 },
      { v: "未知", w: 2 }
    ],
    eye_palettes: [
      { v: "琥珀金", w: 16 },
      { v: "深海藍", w: 15 },
      { v: "黑曜石", w: 15 },
      { v: "翡翠綠", w: 14 },
      { v: "榛果褐", w: 12 },
      { v: "紫羅蘭", w: 8 },
      { v: "赤紅", w: 7 },
      { v: "冰藍", w: 8 },
      { v: "星空灰", w: 7 },
      { v: "石榴紅", w: 6 },
      { v: "青金石藍", w: 6 },
      { v: "蜂蜜茶褐", w: 7 },
      { v: "淡紫丁香", w: 6 },
      { v: "瑪瑙紅", w: 6 },
      { v: "金綠寶石", w: 6 },
      { v: "深墨黑", w: 8 },
      { v: "金 / 藍（異色瞳）", w: 8 },
      { v: "赤紅 / 黑（異色瞳）", w: 7 },
      { v: "翡翠綠 / 琥珀金（異色瞳）", w: 7 },
      { v: "紫羅蘭 / 冰藍（異色瞳）", w: 6 },
      { v: "碧綠 / 赤紅（異色瞳）", w: 6 },
      { v: "深海藍 × 外圈金環（雙色虹膜）", w: 6 },
      { v: "黑曜石 × 金色星芒（星瞳）", w: 6 },
      { v: "深紫 × 外圈銀環", w: 5 },
      { v: "琥珀金 × 外圈深黑", w: 5 },
      { v: "冰藍 × 外圈幽紫", w: 5 },
      { v: "未知", w: 2 }
    ],
    get personality() { return window.COSMI_CATALOG.PERSONALITIES; },
    style: [
      "星際探險裝", "街頭機能", "傳統民族服融合現代剪裁", "學院制服改造", "蒸汽龐克", "太陽龐克", "戶外登山", "復古運動",
      "魔法學徒袍", "機甲駕駛服", "遊牧旅人", "海洋水手", "賽博市集", "田園農作", "考古學者", "祭典華服改良",
      "賽博龐克駭客", "和風羽織機能", "哥德風格改裝", "波西米亞流蘇", "極地抗寒防風裝", "沙漠旅者防沙斗篷", "星宮神官祭袍",
      "復古工裝吊帶", "精靈森林編織", "宮廷華麗輕騎裝", "現代跑酷機能", "暗影夜行衣", "廢土拾荒改裝", "古典英倫偵探",
      "浪漫騎士風衣", "錬金術士大衣", "宇宙採礦防護服", "幻獸學者研究袍", "未知"
    ],
    interests: [
      "天文觀星", "料理", "攝影", "古代遺跡", "機械改裝", "音樂", "繪畫", "釣魚", "園藝", "武術",
      "收集化石", "寫日記", "滑板", "編織", "地圖測繪", "民俗傳說", "跑酷", "電子遊戲", "泡茶/咖啡", "觀察宙獸生態",
      "採集礦石標本", "魔導編程", "烹飪野炊", "修繕古機械", "野外露營", "素描寫生", "打水漂", "拓印碑文", "研究古星圖",
      "滑翔飛行", "收集舊郵票/徽章", "製作植物壓花", "研發奇特道具", "收集羽毛", "攀岩", "製作木雕", "調配香薰草藥",
      "吹奏笛樂", "占卜星象", "尋訪古道", "未知"
    ],
    likes: [
      "甜食", "下雨天", "毛茸茸的生物", "星空", "舊書", "熱湯", "高處的風", "閃亮的礦石", "熱鬧祭典", "安靜的清晨",
      "雷雨", "海浪聲", "手作物", "辣的食物", "老地圖", "乾爽的晴天", "篝火劈啪聲", "剛出爐的麵包香", "晨曦微風", "古代鐘錶的滴答聲",
      "毛茸茸的幼獸", "踩落葉的沙沙聲", "舊羊皮紙氣味", "高空俯瞰地景", "閃閃發光的貝殼", "溫暖的日光浴", "森林的泥土香", "冰涼的泉水", "脆脆的堅果",
      "觀星時的寂靜", "熟透的野果", "機械運轉的齒輪聲", "洗淨曬乾的棉布", "貓咪的呼嚕聲", "爐火旁的熱茶", "未知"
    ],
    dislikes: [
      "蟲子", "吵雜", "謊言", "高處", "黑暗", "苦味", "等待", "被小看", "冰冷的金屬", "濕衣服",
      "早起", "輸", "擁擠人群", "打雷", "濕黏的天氣", "指甲刮黑板的刺耳聲", "背叛與暗算", "毫無意義的爭吵", "停滯不前", "潮濕發霉的地下室",
      "過甜的油膩食物", "粗製濫造的機械", "被打斷思路", "突如其來的巨響", "繁瑣的繁文縟節", "浪費食物", "束縛感", "悶熱的封閉空間", "枯燥的長篇大論",
      "迷路", "苦澀的藥草", "沾到泥巴", "說教", "虛偽的奉承", "嚴寒凍僵的感覺", "未知"
    ],
    role: ["玩家角色預設外觀", "隊友 NPC", "大廳 NPC：研究員", "大廳 NPC：孵蛋師", "大廳 NPC：商人", "星球原住民 NPC", "對手訓練家", "未知"]
  },

  CREATURE: {
    // 宙獸外觀基底父類別（10 生物 + 12 類人奇幻種族 + 6 器械 + 8 載具，最多選 2 個）
    // 舊版本「人形·*」類別一律追溯至「類人奇幻·精靈族」
    base_parents: [
      // 生物類
      { v: "生物·哺乳類", zh: "哺乳類", w: 10 },
      { v: "生物·鳥類", zh: "鳥類", w: 10 },
      { v: "生物·爬蟲類", zh: "爬蟲類", w: 10 },
      { v: "生物·兩棲類", zh: "兩棲類", w: 8 },
      { v: "生物·魚類", zh: "魚類", w: 8 },
      { v: "生物·昆蟲", zh: "昆蟲", w: 8 },
      { v: "生物·甲殼類", zh: "甲殼類", w: 7 },
      { v: "生物·軟體動物", zh: "軟體動物", w: 7 },
      { v: "生物·植物/菌類", zh: "植物/菌類", w: 7 },
      { v: "生物·古生物", zh: "古生物", w: 9 },

      // 類人奇幻種族（舊版人形一律追溯為精靈族）
      { v: "類人奇幻·精靈族", zh: "精靈族（森/月/高等精靈）", w: 8 },
      { v: "類人奇幻·矮人族", zh: "矮人族（符文鐵匠/山脈氏族）", w: 7 },
      { v: "類人奇幻·獸人族", zh: "獸人族（荒原戰團/薩滿部落）", w: 7 },
      { v: "類人奇幻·龍人/蜥蜴人", zh: "龍人/蜥蜴人（龍脈裔/沼澤鱗戰士）", w: 7 },
      { v: "類人奇幻·妖精/半身人", zh: "妖精/半身人（花精/侏儒/幸運斥候）", w: 6 },
      { v: "類人奇幻·天使/天人", zh: "天使/天人（光翼神使/晨星座天人）", w: 6 },
      { v: "類人奇幻·惡魔/魔人", zh: "惡魔/魔人（深淵角魔/幽火魅魔）", w: 6 },
      { v: "類人奇幻·水棲人/塞壬", zh: "水棲人/塞壬（潮汐鮫人/深淵海歌）", w: 6 },
      { v: "類人奇幻·翼人/天狗", zh: "翼人/天狗（蒼穹鷹羽/風息天狗）", w: 6 },
      { v: "類人奇幻·不死/巫妖", zh: "不死/巫妖（冥魂巫妖/復生骸骨）", w: 5 },
      { v: "類人奇幻·傀儡/魔偶", zh: "傀儡/魔偶（發條機巧/古代符文石偶）", w: 6 },
      { v: "類人奇幻·巨人/泰坦", zh: "巨人/泰坦（山脈風暴/熔岩巨神後裔）", w: 5 },

      // 器械類
      { v: "器械·工具", zh: "工具", w: 7 },
      { v: "器械·武器", zh: "武器", w: 8 },
      { v: "器械·樂器", zh: "樂器", w: 6 },
      { v: "器械·天文/測量儀器", zh: "天文/測量儀器", w: 7 },
      { v: "器械·機關人偶", zh: "機關人偶", w: 7 },
      { v: "器械·燈具/鐘錶", zh: "燈具/鐘錶", w: 6 },

      // 載具類
      { v: "載具·戰車", zh: "戰車", w: 7 },
      { v: "載具·帆船", zh: "帆船", w: 7 },
      { v: "載具·飛行器", zh: "飛行器", w: 8 },
      { v: "載具·列車", zh: "列車", w: 6 },
      { v: "載具·機車", zh: "機車", w: 6 },
      { v: "載具·熱氣球", zh: "熱氣球", w: 5 },
      { v: "載具·雪橇", zh: "雪橇", w: 5 },
      { v: "載具·潛水艇", zh: "潛水艇", w: 6 },
      { v: "未知", zh: "未知", w: 2 }
    ],
    base_count: [ { n: 1, w: 55 }, { n: 2, w: 45 } ],

    // 各父類別所屬之豐富子類別庫（含舊版人形對照追溯至精靈族）
    base_sub_map: {
      "生物·哺乳類": ["貓科/獵豹/雪豹", "犬科/狼/狐狸", "蹄類/鹿/羚羊/野牛", "熊類/巨熊/北極熊", "鯨豚/海獸/海獺", "齧齒翼手/兔/蝙蝠/飛鼠", "靈長類/猿猴", "貧齒穿山甲/食蟻獸/犰狳"],
      "生物·鳥類": ["猛禽/巨鵰/夜鴞/遊隼", "水禽/雁鴨/天鵝/企鵝", "鳴禽/翠鳥/雀鳥/蜂鳥", "走禽/鴕鳥/古代恐鳥", "極樂鳥/孔雀/長尾雉", "涉禽/鶴/白鷺/琵鷺"],
      "生物·爬蟲類": ["蜥蜴/石龍子/科莫多巨蜥", "蛇蟒/毒蛇/響尾蛇/羽蛇", "陸龜/海龜/象龜", "鱷類/帝鱷/短吻鱷", "變色龍/角蜥", "角龍/甲龍/劍龍/植食恐龍", "獸腳類/迅猛龍/暴龍"],
      "生物·兩棲類": ["蛙蟾/箭毒蛙/紅眼樹蛙", "蠑螈/六角恐龍/大鯢", "古代迷齒兩棲/笠頭螈"],
      "生物·魚類": ["硬骨魚/金龍魚/深海帶魚/錦鯉", "鯊鰩/巨齒鯊/魔鬼魚/蝠魟", "深海發光魚/鮟鱇/吞噬鰻", "古代肉鰭/腔棘魚", "海馬/海龍/飛魚/旗魚", "電鰻/電鰩"],
      "生物·昆蟲": ["甲蟲/獨角仙/長戟大兜/鍬形蟲", "鱗翅/幻蝶/透翅蝶/巨蛾", "膜翅/虎頭蜂/掠食切葉蟻", "螳螂/蘭花螳螂/直翅蝗蟲", "古巨蜻蜓/蜉蝣/金龜子"],
      "生物·甲殼類": ["巨蟹/椰子蟹/帝王蟹/招潮蟹", "龍蝦/雀尾螳螂蝦/皮皮蝦", "藤壺/大王具足蟲/寄居蟹", "三葉蟲/寒武紀節肢/奇蝦"],
      "生物·軟體動物": ["頭足/大王烏賊/擬態章魚/藍環章魚", "腹足/海蛞蝓/海天使/巨蝸牛", "雙殼/大硨磲/黑珍珠貝/扇貝", "鸚鵡螺/菊石/箭石"],
      "生物·植物/菌類": ["捕蟲草/霸王花/食人花", "神木古樹/世界樹精/柳樹精", "寄生藤蔓/古代巨蕨", "發光夜光蕈/孢子真菌", "沙漠刺葵/金琥仙人掌", "深海巨藻/水晶珊瑚草"],
      "生物·古生物": ["三葉蟲/奇蝦/寒武紀怪蟲", "菊石/直角石軟體", "鄧氏魚/盾皮巨魚", "翼龍/風神翼龍/無齒翼龍", "長毛象/劍齒虎/恐狼", "板足鱟/巨型海蠍", "巨脈蜻蜓/廣翅鱟"],

      // 類人奇幻種族
      "類人奇幻·精靈族": ["高等精靈/月光祭司", "森林精靈/巡林遊俠", "暗影精靈/潛行夜刃", "冰霜精靈/詠雪者", "太陽精靈/晨曦騎士", "血精靈/魔導學者"],
      "類人奇幻·矮人族": ["符文鍛造鐵匠", "深地採礦戰士", "矮人重裝盾衛", "麥酒烈火狂暴者", "精密機械工程師"],
      "類人奇幻·獸人族": ["狂暴戰吼督軍", "荒原薩滿長老", "狼騎獵手", "血斧蠻鬥士", "獸骨圖騰衛士"],
      "類人奇幻·龍人/蜥蜴人": ["龍裔吐息戰士", "金龍龍脈術士", "深沼毒牙蜥蜴人", "黑曜龍鱗重甲兵", "飛翼龍血斥候"],
      "類人奇幻·妖精/半身人": ["花仙妖精/皮克西", "森林小仙靈", "半身人飛賊/幸運斥候", "地精/侏儒發明狂", "蒲公英遊俠"],
      "類人奇幻·天使/天人": ["六翼光熾熾天使", "白銀審判戰天人", "黎明天使琴師", "神聖守護座天使", "晨星引路者"],
      "類人奇幻·惡魔/魔人": ["深淵赤角戰魔", "幽火魔人術士", "暗影魅魔/誘惑遊俠", "黑曜石化惡魔", "裂隙虛空魔裔"],
      "類人奇幻·水棲人/塞壬": ["潮汐鮫人海歌者", "深海深淵塞壬", "珊瑚鱗片戰士", "水月人魚祭司", "巨浪馭水客"],
      "類人奇幻·翼人/天狗": ["蒼穹翔空鷹身翼人", "高山黑羽天狗", "白鳥風息羽人", "雷鳴疾風投矛翼手"],
      "類人奇幻·不死/巫妖": ["冥魂白骨死靈", "骸骨黑騎", "幽靈怨女/報喪女妖", "長生巫妖法王", "詛咒復生行屍"],
      "類人奇幻·傀儡/魔偶": ["發條齒輪機巧人", "古代符文巨石像", "黃銅蒸氣魔像", "琉璃晶核人偶", "自律水銀人形"],
      "類人奇幻·巨人/泰坦": ["風暴泰坦/雷神後裔", "山脈岩石巨人", "極地霜雪巨人", "熔岩火山巨怪"],

      // 舊版本人形項目追溯對照（一律追溯至精靈族）
      "人形·戰士": ["精靈族·高等精靈/月光祭司", "精靈族·森林精靈/巡林遊俠", "精靈族·太陽精靈/晨曦騎士"],
      "人形·術士": ["精靈族·高等精靈/月光祭司", "精靈族·血精靈/魔導學者", "精靈族·冰霜精靈/詠雪者"],
      "人形·工匠": ["精靈族·森林精靈/巡林遊俠", "精靈族·血精靈/魔導學者"],
      "人形·舞者": ["精靈族·森林精靈/巡林遊俠", "精靈族·月光祭司"],
      "人形·僧侶/修行者": ["精靈族·月光祭司", "精靈族·太陽精靈/晨曦騎士"],
      "人形·孩童精靈": ["精靈族·森林小仙靈", "精靈族·花仙妖精/皮克西"],

      // 器械類
      "器械·工具": ["鍛錘/鐵砧/重鉗", "巨剪/裁割刃/短鋸", "起重滑輪/牽引絞盤", "鑿岩鑽頭/工程挖掘鏟", "開鎖工具/精密鑷子尺規"],
      "器械·武器": ["複合巨弓/重型床弩", "符文巨盾/塔盾護壁", "加農重砲/手持臼砲", "浮空靈巧飛刃/輪刃", "長槍/長戟/戰鉞", "鏈枷/流星錘/連枷"],
      "器械·樂器": ["絲弦古琴/琵琶/豎琴", "巨型管風琴/手風琴", "編鐘/排鐘/神聖大鐘", "戰號/青銅號角/風笛", "戰鼓/太鼓/手鼓", "長笛/陶笛/橫笛"],
      "器械·天文/測量儀器": ["渾天儀/六分儀/象限儀", "星盤/日晷/黃道儀", "指南地動儀/候風地動儀", "星象望遠鏡/多重透鏡組", "水鐘/滴漏/精密擺錘"],
      "器械·機關人偶": ["發條自鳴人偶/鐘琴偶", "木牛流馬/四足機關獸", "蒸氣壓力偶/齒輪連動偶", "磁力懸浮偶/水晶共鳴偶", "機關千手傀儡"],
      "器械·燈具/鐘錶": ["走馬燈/琉璃彩繪提燈", "天文重錘鐘/天文擺鐘", "星火燃燈台/聚光三棱鏡座", "發光鍊金油燈/防爆礦燈", "古青銅神火燭台"],

      // 載具類
      "載具·戰車": ["古代重裝雙輪戰車", "履帶巡航車/蒸汽裝甲車", "發條單輪巨型衝擊車", "沙海風帆滑行車", "採礦鑽地重裝車"],
      "載具·帆船": ["多桅古帆船/戰列艦", "樓船/龜甲防禦船", "雙體風帆獨木舟/水翼帆船", "極地破冰外輪明輪船", "貢多拉/狹長靈動飛舟"],
      "載具·飛行器": ["撲翼機/滑翔翼鳥機", "硬式雙翼飛艇/齊柏林艇", "空天浮島舟/反重力浮舟", "螺旋升力飛翼機/早期飛行傘"],
      "載具·列車": ["蒸氣機關火車/鐵甲列車", "山地齒軌登山車/採礦礦車", "懸浮單軌車/導軌滑車", "雪原機關雪地列車"],
      "載具·機車": ["復古蒸氣重型機車", "發條單輪陀螺機車", "履帶式雪地突擊機車", "反重力懸浮機車"],
      "載具·熱氣球": ["蒸氣加熱探測熱氣球", "多氣囊探險吊籃", "發光水母形懸浮浮囊", "太陽能浮空吊艙"],
      "載具·雪橇": ["極地獸拉木雪橇", "風帆冰原滑行橇", "發條推力雪原橇", "蒸氣履帶雪橇車"],
      "載具·潛水艇": ["早期球形深海觀測艇", "蝠鱝形深海探測潛艇", "發條機械動力潛艦", "透明琉璃耐壓深潛器"],
      "未知": ["自由發揮未知子類"]
    },

    get appearance_palettes() { return window.COSMI_CATALOG.PALETTES; },
    // 擴充戰鬥定位
    role: [
      "前排肉盾/坦克", "近戰爆發刺客", "遠程火力射手", "群體持續傷害/DOT", "戰術控場/牽制封鎖",
      "輔助治療/生命共鳴", "團隊增益/光環守護", "場地佈署/陣地構築", "空中游擊/截擊", "高速突襲/破陣先鋒",
      "干擾削弱/詛咒削弱", "自爆/致命反傷護衛", "指揮/僕從群體召喚", "未知"
    ],
    get temperament() { return window.COSMI_CATALOG.PERSONALITIES; },
    // 多數集中在大型鳥類到大型哺乳類之間的體型（成體在 M 到 L）
    size_profile: [
      { v: "S→M→M→L", w: 40 },
      { v: "S→M→L→L", w: 35 },
      { v: "S→M→L→XL", w: 20 },
      { v: "M→L→L→XL", w: 5 },
      { v: "未知", w: 2 }
    ],
    // 擴充文化原型庫（跨洲各文明神話、歷史、英雄、傳說與科幻/未來想像，共 110+ 項）
    archetypes: [
      // 東亞
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
      { region: "東亞", name: "莊周夢蝶", type: "哲學傳奇", motifs: "彩蝶、虛實幻境、夢醒哲思", sensitivity: "ok" },
      { region: "東亞", name: "嫦娥奔月", type: "傳說", motifs: "廣寒月宮、白玉兔、桂花清香", sensitivity: "ok" },
      { region: "東亞", name: "魯班", type: "歷史名人", motifs: "木鳶飛天、魯班鎖、鋸子墨斗", sensitivity: "ok" },
      { region: "東亞", name: "白蛇傳說", type: "文學", motifs: "油紙傘、雷峰雄黃、斷橋水漫", sensitivity: "ok" },
      { region: "東亞", name: "哪吒", type: "神祇", motifs: "風火輪、混天綾、乾坤圈", sensitivity: "review" },
      { region: "東亞", name: "須佐之男", type: "神祇", motifs: "八岐大蛇、草薙劍、暴風", sensitivity: "review" },
      { region: "東亞", name: "刑天", type: "神祇", motifs: "斷首不屈、雙乳為目、干戚巨斧", sensitivity: "review" },
      { region: "東亞", name: "台灣原住民射日傳說", type: "傳說", motifs: "雙日、遠征的父子、柚子樹", sensitivity: "review" },

      // 南亞
      { region: "南亞", name: "阿育王", type: "歷史名人", motifs: "石柱、獅首柱頭、法輪", sensitivity: "ok" },
      { region: "南亞", name: "阿耶波多", type: "歷史名人", motifs: "零、星盤、月食軌跡", sensitivity: "ok" },
      { region: "南亞", name: "迦梨陀娑", type: "文學", motifs: "雲使、梵文詩卷、雨季蓮花", sensitivity: "ok" },
      { region: "南亞", name: "迦樓羅大鵬金翅鳥", type: "神話生物", motifs: "金色羽翼、烈日巨翅、降伏龍蛇", sensitivity: "ok" },
      { region: "南亞", name: "葛內舍象神智慧", type: "傳說", motifs: "象首、斷牙、智慧典籍、吉祥甜點", sensitivity: "ok" },
      { region: "南亞", name: "羅摩", type: "神祇", motifs: "神弓、猴軍、跨海石橋", sensitivity: "review" },
      { region: "南亞", name: "那羅延千手化身", type: "神祇", motifs: "法螺、金輪、睡蓮、神杖", sensitivity: "review" },

      // 東南亞
      { region: "東南亞", name: "巴庫納瓦", type: "傳說", motifs: "吞月海龍、銅鑼聲、七個月亮", sensitivity: "ok" },
      { region: "東南亞", name: "加多卡加", type: "英雄", motifs: "皮影、鋼筋鐵骨、飛翔斗篷", sensitivity: "ok" },
      { region: "東南亞", name: "徵氏姊妹", type: "歷史名人", motifs: "雙騎戰象、銅鼓、起義旗", sensitivity: "ok" },
      { region: "東南亞", name: "扶董天王", type: "英雄", motifs: "鐵馬噴火、竹叢、一夜長大", sensitivity: "ok" },
      { region: "東南亞", name: "瑪麗亞·瑪基林", type: "傳說", motifs: "迷霧山林、金色野果、山嵐守護", sensitivity: "ok" },
      { region: "東南亞", name: "金翅金鹿傳奇", type: "傳奇", motifs: "南洋皮影金光、寶石鹿角、熱帶密林", sensitivity: "ok" },

      // 西亞/中東
      { region: "西亞/中東", name: "吉爾伽美什", type: "英雄", motifs: "雪松林、天牛、不死草", sensitivity: "ok" },
      { region: "西亞/中東", name: "伊南娜", type: "神祇", motifs: "八芒星、冥界七門、獅子", sensitivity: "ok" },
      { region: "西亞/中東", name: "辛巴達", type: "文學", motifs: "大鵬鳥、島嶼巨鯨、羅盤", sensitivity: "ok" },
      { region: "西亞/中東", name: "伊本·西那", type: "歷史名人", motifs: "醫典、草藥、星盤", sensitivity: "ok" },
      { region: "西亞/中東", name: "花拉子米", type: "歷史名人", motifs: "代數符號、星盤、幾何圖紋", sensitivity: "ok" },
      { region: "西亞/中東", name: "雪赫拉莎德天方夜譚", type: "文學", motifs: "飛毯、神燈藍煙、千夜說書卷軸", sensitivity: "ok" },
      { region: "西亞/中東", name: "漢摩拉比", type: "歷史名人", motifs: "黑玄武岩石柱、正義天平、楔形法典", sensitivity: "ok" },
      { region: "西亞/中東", name: "所羅門魔戒封印", type: "傳說", motifs: "封魔指環、六芒星、黃銅封印魔壺", sensitivity: "review" },

      // 非洲
      { region: "非洲", name: "阿努比斯", type: "神祇", motifs: "胡狼頭、天秤、羽毛", sensitivity: "ok" },
      { region: "非洲", name: "荷魯斯", type: "神祇", motifs: "隼、荷魯斯之眼、太陽圓盤", sensitivity: "ok" },
      { region: "非洲", name: "阿南西", type: "傳說", motifs: "蜘蛛、故事寶盒、絲線", sensitivity: "ok" },
      { region: "非洲", name: "松迪亞塔·凱塔", type: "歷史名人", motifs: "鐵杖、獅子、說書人的科拉琴", sensitivity: "ok" },
      { region: "非洲", name: "曼薩·穆薩", type: "歷史名人", motifs: "金砂、駱駝商隊、泥磚清真寺尖塔", sensitivity: "ok" },
      { region: "非洲", name: "恩津加女王", type: "歷史名人", motifs: "談判石座、弓、外交印信", sensitivity: "ok" },
      { region: "非洲", name: "獅身人面斯芬克斯", type: "傳說", motifs: "黃金獅身、鷹羽雙翼、永恆謎題", sensitivity: "ok" },
      { region: "非洲", name: "芭絲特貓神", type: "神祇", motifs: "黑貓守護、歡慶搖鈴、金項圈", sensitivity: "ok" },
      { region: "非洲", name: "尚戈", type: "神祇", motifs: "雙刃斧、雷石、紅白配色", sensitivity: "review" },
      { region: "非洲", name: "奧貢鐵匠守護神", type: "神祇", motifs: "鐵砧、重錘、開路長刀", sensitivity: "review" },

      // 歐洲
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
      { region: "歐洲", name: "普羅米修斯", type: "傳說", motifs: "天火茴香枝、高加索岩壁、不熄光芒", sensitivity: "ok" },
      { region: "歐洲", name: "貝奧武夫", type: "英雄", motifs: "沼澤巨妖、龍骨寶劍、金鎖子甲", sensitivity: "ok" },
      { region: "歐洲", name: "羅賓漢", type: "傳說", motifs: "雪伍德綠林、紫衫長弓、百步穿楊", sensitivity: "ok" },
      { region: "歐洲", name: "牛頓", type: "歷史名人", motifs: "三棱鏡彩虹色散、萬有引力、光學透鏡", sensitivity: "ok" },
      { region: "歐洲", name: "聖女貞德", type: "歷史名人", motifs: "白旗、鎧甲、火焰", sensitivity: "review" },

      // 美洲
      { region: "美洲", name: "羽蛇神", type: "神祇", motifs: "羽毛蛇、金星、玉石", sensitivity: "ok" },
      { region: "美洲", name: "瑪雅雙子英雄", type: "英雄", motifs: "球賽、吹箭、冥界試煉", sensitivity: "ok" },
      { region: "美洲", name: "庫魯皮拉", type: "傳說", motifs: "反轉的腳、火紅頭髮、森林守護", sensitivity: "ok" },
      { region: "美洲", name: "哈莉特·塔布曼", type: "歷史名人", motifs: "北極星、提燈、地下鐵路", sensitivity: "ok" },
      { region: "美洲", name: "西蒙·玻利瓦", type: "歷史名人", motifs: "軍刀、安地斯山脈、白馬", sensitivity: "ok" },
      { region: "美洲", name: "萊特兄弟", type: "歷史名人", motifs: "雙翼機、自行車工坊、沙丘", sensitivity: "ok" },
      { region: "美洲", name: "特拉洛克雨神", type: "傳說", motifs: "碧玉水罐、暴風閃電眼、綠玉面具", sensitivity: "ok" },
      { region: "美洲", name: "風之靈納瓦霍", type: "傳說", motifs: "天然彩色沙畫、四方之風、鷹羽吹管", sensitivity: "ok" },
      { region: "美洲", name: "雷鳥", type: "傳說", motifs: "巨鳥、雷鳴振翅、閃電眼", sensitivity: "review" },
      { region: "美洲", name: "因蒂", type: "神祇", motifs: "太陽金面、安地斯梯田、秘魯金器", sensitivity: "review" },

      // 大洋洲
      { region: "大洋洲", name: "卡美哈梅哈一世", type: "歷史名人", motifs: "羽毛斗篷、浪花、雙體獨木舟", sensitivity: "ok" },
      { region: "大洋洲", name: "拉帕努伊鳥人", type: "傳說", motifs: "烏燕鷗蛋、懸崖、摩艾石像", sensitivity: "ok" },
      { region: "大洋洲", name: "塔尼瓦巨蜥守護者", type: "傳說", motifs: "深海潮汐湧浪、綠玉雕紋、巨型鰭尾", sensitivity: "ok" },
      { region: "大洋洲", name: "毛伊", type: "神祇", motifs: "魚鉤、套索捕日、釣起島嶼", sensitivity: "review" },
      { region: "大洋洲", name: "彩虹蛇", type: "傳說", motifs: "彩虹、水坑、大地紋路", sensitivity: "review" },

      // 中亞/北亞
      { region: "中亞/北亞", name: "瑪納斯", type: "英雄", motifs: "白鷹、長矛、天山", sensitivity: "ok" },
      { region: "中亞/北亞", name: "兀魯伯", type: "歷史名人", motifs: "巨型六分儀、星表、撒馬爾罕藍磚", sensitivity: "ok" },
      { region: "中亞/北亞", name: "阿史那灰狼傳奇", type: "傳說", motifs: "蒼穹雪山、灰狼金眸、穹廬氈帳", sensitivity: "ok" },
      { region: "中亞/北亞", name: "成吉思汗", type: "歷史名人", motifs: "蒼狼、白鹿、草原騎兵", sensitivity: "review" },
      { region: "中亞/北亞", name: "格薩爾王", type: "英雄", motifs: "神駒、雪山、戰盔", sensitivity: "review" },

      // 極地
      { region: "極地", name: "羅爾德·阿蒙森", type: "歷史名人", motifs: "雪橇犬、極地帳篷、羅盤", sensitivity: "ok" },
      { region: "極地", name: "納努克白熊之主", type: "傳說", motifs: "冰原巨熊、暴風雪長嘯、極光庇佑", sensitivity: "ok" },
      { region: "極地", name: "賽德娜", type: "神祇", motifs: "海底、海豹與鯨、長髮", sensitivity: "review" },

      // 多元交融/世界都市
      { region: "多元交融/世界都市", name: "萬國星聯天文使", type: "未來架空", motifs: "跨文明星儀、全息外交印、各族織錦", sensitivity: "ok" },
      { region: "多元交融/世界都市", name: "水晶宮博覽發明家", type: "架空歷史", motifs: "琉璃溫室展廳、發條自鳴琴、齒輪差分機", sensitivity: "ok" },

      // 星際宙域/跨星系殖民地
      { region: "星際宙域/跨星系殖民地", name: "曲率星圖領航員", type: "未來科幻", motifs: "重力羅盤、超光速航線光譜、引力透鏡", sensitivity: "ok" },
      { region: "星際宙域/跨星系殖民地", name: "星門戴森球工程師", type: "未來科幻", motifs: "戴森環結構、光壓推動器、磁約束電漿", sensitivity: "ok" },
      { region: "星際宙域/跨星系殖民地", name: "虛空荒原流浪者", type: "未來科幻", motifs: "微隕石補丁防護服、離子氣體冷光、星屑收藏盒", sensitivity: "ok" },

      // 現代科學
      { region: "現代科學", name: "居禮夫人", type: "歷史名人", motifs: "發光試管、元素符號、實驗袍", sensitivity: "ok" },
      { region: "現代科學", name: "南丁格爾", type: "歷史名人", motifs: "提燈、繃帶、圓餅統計圖", sensitivity: "ok" },

      // 未知
      { region: "未知", name: "自由發揮原型", type: "未知", motifs: "由 AI 自行發揮意象", sensitivity: "ok" }
    ]
  },

  // 體型級距設定：上限至暴龍 (12.5m, 9000kg)，多數落在 M 和 L（大型鳥類至大型哺乳類）
  SIZE_CLASSES: {
    S:  { zh: "S（< 0.6 m）",  tris: "5–8k",   tex: 1024, len_min: 0.15, len_max: 0.60, wt_min: 0.04, wt_max: 5.0,  fat_min: 8,  fat_max: 25 },
    M:  { zh: "M（0.6–1.5 m）", tris: "8–15k",  tex: 1024, len_min: 0.60, len_max: 1.50, wt_min: 5.0,  wt_max: 65.0, fat_min: 6,  fat_max: 24 },
    L:  { zh: "L（1.5–2.8 m）", tris: "15–25k", tex: 2048, len_min: 1.50, len_max: 2.80, wt_min: 65.0, wt_max: 550,  fat_min: 8,  fat_max: 28 },
    XL: { zh: "XL（2.8–12.5 m·暴龍級）", tris: "25–35k", tex: 2048, len_min: 2.80, len_max: 12.5, wt_min: 550, wt_max: 9000, fat_min: 10, fat_max: 32 }
  },
  STAGES: [
    { v: "juvenile", zh: "幼年" }, { v: "adolescent", zh: "少年" },
    { v: "adult", zh: "成年" }, { v: "overload", zh: "超載（戰鬥中短暫進化）" }
  ],

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
  }
};
