import { readFileSync, readdirSync, existsSync } from 'fs';
import { join } from 'path';

export interface Dialogue {
  character: string;
  text_en: string;
  text_th: string;
  status: 'AI' | 'Community' | 'none';
}

export interface Quest {
  id: string;
  wiki_path: string;
  wiki_image_url: string;
  patch: string;
  title_en: string;
  title_th: string;
  title_status: 'AI' | 'Community' | 'none';
  level: string;
  location: string;
  npc_start: string;
  dialogues: Dialogue[];
  source: 'contributor' | 'ai' | 'none';
  expansion: string;
  communityPct: number;
}

/* ────────────────────────────────────────────────────────────
   Expansions — central registry. Add a row here when starting
   to translate a new expansion.
───────────────────────────────────────────────────────────── */
export interface Expansion {
  id: string;              // url-safe slug, also content-folder name
  patch: string;           // '2.0', '3.0', …
  title_en: string;        // 'A Realm Reborn'
  title_th: string;        // 'การเริ่มต้นใหม่ของ Eorzea'
  available: boolean;      // false → shown but disabled
}

export const EXPANSIONS: Expansion[] = [
  { id: 'arr', patch: '2.0', title_en: 'A Realm Reborn',  title_th: 'การเริ่มต้นใหม่ของ Eorzea', available: true  },
  { id: 'hw',  patch: '3.0', title_en: 'Heavensward',      title_th: 'สู่สวรรค์อันไกลโพ้น',         available: false },
  { id: 'sb',  patch: '4.0', title_en: 'Stormblood',       title_th: 'มหาสงครามแห่งอิสรภาพ',        available: false },
  { id: 'shb', patch: '5.0', title_en: 'Shadowbringers',   title_th: 'ผู้นำมาซึ่งเงามืด',              available: false },
  { id: 'ew',  patch: '6.0', title_en: 'Endwalker',        title_th: 'บทสุดท้ายของ Hydaelyn',        available: false },
  { id: 'dt',  patch: '7.0', title_en: 'Dawntrail',        title_th: 'การเดินทางสู่รุ่งอรุณ',          available: false },
];

/* ────────────────────────────────────────────────────────────
   File system
   GitLocalize Source : content_community/en/{exp}/{patch}/
   GitLocalize Target : content_community/th/{exp}/{patch}/
───────────────────────────────────────────────────────────── */
const ROOT = join(process.cwd(), '..');

function communityEnDir(exp: Expansion) {
  return join(ROOT, 'content_community', 'en', exp.id, exp.patch);
}
function communityThDir(exp: Expansion) {
  return join(ROOT, 'content_community', 'th', exp.id, exp.patch);
}

function readJson<T>(path: string): T | null {
  if (!existsSync(path)) return null;
  return JSON.parse(readFileSync(path, 'utf-8')) as T;
}

// en/ schema — blueprint + English text
interface EnQuest {
  id: string;
  wiki_path: string;
  wiki_image_url: string;
  patch: string;
  title: string;
  level: string;
  location: string;
  npc_start: string;
  dialogues: { character: string; text: string }[];
}

// th/ schema — Thai translations + community review status
interface ThQuest {
  title: string;
  title_status: 'AI' | 'Community' | 'none';
  dialogues: { text: string; status: 'AI' | 'Community' | 'none' }[];
}

function loadQuest(exp: Expansion, filename: string): Quest {
  const en = readJson<EnQuest>(join(communityEnDir(exp), filename))!;
  const th = readJson<ThQuest>(join(communityThDir(exp), filename));

  const dialogues: Dialogue[] = en.dialogues.map((d, i) => ({
    character: d.character,
    text_en: d.text,
    text_th: th?.dialogues[i]?.text ?? '',
    status: th?.dialogues[i]?.status ?? 'none',
  }));

  const title_th = th?.title ?? '';
  const title_status = th?.title_status ?? 'none';

  const total = dialogues.length;
  const communityCount = dialogues.filter(d => d.status === 'Community').length;
  const communityPct = total > 0 ? Math.round((communityCount / total) * 100) : 0;

  const hasAnyTranslation = !!title_th || dialogues.some(d => !!d.text_th);
  const isFullyCommunity =
    title_status === 'Community' && (total === 0 || communityPct === 100);
  const source: Quest['source'] = isFullyCommunity
    ? 'contributor'
    : hasAnyTranslation
    ? 'ai'
    : 'none';

  return {
    id: en.id,
    wiki_path: en.wiki_path,
    wiki_image_url: en.wiki_image_url,
    patch: en.patch,
    title_en: en.title,
    title_th,
    title_status,
    level: en.level,
    location: en.location,
    npc_start: en.npc_start,
    dialogues,
    source,
    expansion: exp.id,
    communityPct,
  };
}

export function getAllQuests(expansionId: string = 'arr'): Quest[] {
  const exp = EXPANSIONS.find(e => e.id === expansionId);
  if (!exp || !exp.available) return [];

  const dir = communityEnDir(exp);
  if (!existsSync(dir)) return [];

  const files = readdirSync(dir).filter(f => f.endsWith('.json'));
  let quests = files.map(f => loadQuest(exp, f));

  if (expansionId === 'arr') {
    // Only keep quests that are part of the defined chronological order
    quests = quests.filter(q => ARR_QUEST_ORDER.includes(q.id));
    // Sort them according to ARR_QUEST_ORDER
    quests.sort((a, b) => ARR_QUEST_ORDER.indexOf(a.id) - ARR_QUEST_ORDER.indexOf(b.id));
  } else {
    quests.sort((a, b) => {
      const la = parseInt(a.level) || 0;
      const lb = parseInt(b.level) || 0;
      if (la !== lb) return la - lb;
      return a.title_en.localeCompare(b.title_en);
    });
  }

  return quests;
}

export function getAdjacentQuests(slug: string, expansionId: string = 'arr'): { prev: Quest | null; next: Quest | null } {
  const quests = getAllQuests(expansionId);
  const idx = quests.findIndex(q => q.id === slug);
  if (idx === -1) return { prev: null, next: null };

  let prev = idx > 0 ? quests[idx - 1] : null;
  let next = idx < quests.length - 1 ? quests[idx + 1] : null;

  if (expansionId === 'arr') {
    // Custom overrides for starting city-state branching paths:
    if (slug === 'call-of-the-sea-gridania' || slug === 'call-of-the-sea-limsa-lominsa') {
      const pirates = quests.find(q => q.id === 'it-s-probably-pirates');
      next = pirates ?? null;
    }

    if (slug === 'coming-to-limsa-lominsa' || slug === 'coming-to-ul-dah' || slug === 'it-s-probably-pirates') {
      prev = null;
    }
  }

  return { prev, next };
}

/* ────────────────────────────────────────────────────────────
   Stats + grouping helpers
───────────────────────────────────────────────────────────── */
export function getStats(quests: Quest[]) {
  const total = quests.length;
  const contrib = quests.filter(q => q.source === 'contributor').length;
  const ai      = quests.filter(q => q.source === 'ai').length;
  const pending = quests.filter(q => q.source === 'none').length;
  const pct = (n: number) => total ? Math.round((n / total) * 100) : 0;
  return {
    total, contrib, ai, pending,
    pctContrib: pct(contrib),
    pctAi:      pct(ai),
    pctPending: pct(pending),
  };
}

export interface QuestSubsection {
  title: string;
  quests: Quest[];
}

export interface QuestGroup {
  num: string;
  range: string;
  title: string;
  desc: string;
  quests: Quest[];
  subsections?: QuestSubsection[];
}

export function groupQuestsByLevel(quests: Quest[]): QuestGroup[] {
  const questMap = new Map(quests.map(q => [q.id, q]));
  const groups: QuestGroup[] = [];

  for (const sec of ARR_QUEST_SECTIONS) {
    const secQuests: Quest[] = [];
    let subsections: QuestSubsection[] | undefined;

    if (sec.subsections) {
      subsections = [];
      for (const sub of sec.subsections) {
        const subQuests = sub.questIds
          .map(id => questMap.get(id))
          .filter((q): q is Quest => !!q);
        subsections.push({
          title: sub.title,
          quests: subQuests,
        });
        secQuests.push(...subQuests);
      }
    } else if (sec.questIds) {
      const qList = sec.questIds
        .map(id => questMap.get(id))
        .filter((q): q is Quest => !!q);
      secQuests.push(...qList);
    }

    if (secQuests.length > 0) {
      const match = sec.title.match(/^(\d+)\s+Levels\s+(.+)$/i);
      const num = match ? match[1] : "";
      const range = match ? `Lv ${match[2]}` : sec.title;

      let title = sec.title;
      let desc = "";
      if (num === "1") {
        title = "การเริ่มต้นผจญภัย (Levels 1-15)";
        desc = "เลือกเมืองเริ่มต้นตาม Class · ค้นพบพิธีสาบานตนและเรียนรู้โลกแห่ง Eorzea";
      } else if (num === "2") {
        title = "การเดินทางร่วมกับ Scions (Levels 15-20)";
        desc = "รวมพลที่ Vesper Bay เผชิญหน้ากับ Ifrit และเข้าร่วมกับ Grand Company";
      } else if (num === "3") {
        title = "ความระส่ำระสายในผืนป่าและขุนเขา (Levels 21-30)";
        desc = "ช่วยเหลือกองกำลังต่อต้านแห่ง Ala Mhigo และประสานรอยร้าวในเผ่านกและชาว Sylph";
      } else if (num === "4") {
        title = "ปฐมบทแห่งวีรบุรุษผู้พิชิต (Levels 31-40)";
        desc = "ค้นหาความลับของ Titan ในทุ่งหญ้าแห่งความหลัง และทะยานสู่ฟากฟ้าใน Coerthas";
      } else if (num === "5") {
        title = "บทสรุปแห่ง Seventh Umbral Era (Levels 41-50)";
        desc = "บุกทะลวงป้อมปราการจักรวรรดิ Garlean และเผชิญหน้าพิชิตอาวุธร้ายทำลายล้างโลก Ultima Weapon";
      }

      groups.push({
        num,
        range,
        title,
        desc,
        quests: secQuests,
        subsections,
      });
    }
  }

  return groups;
}


export interface QuestSection {
  title: string;
  subsections?: {
    title: string;
    questIds: string[];
  }[];
  questIds?: string[];
}

export const ARR_QUEST_SECTIONS: QuestSection[] = [
  {
    title: "1 Levels 1-15",
    subsections: [
      {
        title: "1.1 Gridania Quest Chain",
        questIds: [
          "coming-to-gridania",
          "close-to-home-gridania",
          "to-the-bannock",
          "passing-muster",
          "chasing-shadows",
          "eggs-over-queasy",
          "surveying-the-damage",
          "a-soldier-s-breakfast",
          "spirithold-broken",
          "on-to-bentbranch",
          "you-shall-not-trespass",
          "don-t-look-down",
          "in-the-grim-darkness-of-the-forest",
          "threat-level-elevated",
          "migrant-marauders",
          "a-hearer-is-often-late",
          "salvaging-the-scene",
          "leia-s-legacy",
          "dread-is-in-the-air",
          "to-guard-a-guardian",
          "festive-endeavors",
          "renewing-the-covenant",
          "the-gridanian-envoy",
          "call-of-the-sea-gridania",
        ]
      },
      {
        title: "1.2 Limsa Lominsa Quest Chain",
        questIds: [
          "coming-to-limsa-lominsa",
          "close-to-home-limsa-lominsa",
          "on-to-summerford",
          "dressed-to-call",
          "lurkers-in-the-grotto",
          "washed-up",
          "double-dealing",
          "loam-maintenance",
          "plowshares-to-swords",
          "just-deserts",
          "sky-high",
          "thanks-a-million",
          "relighting-the-torch",
          "on-to-the-drydocks",
          "without-a-doubt",
          "righting-the-shipwright",
          "do-angry-pirates-dream",
          "victory-in-peril",
          "men-of-the-blue-tattoos",
          "feint-and-strike",
          "high-society",
          "a-mizzenmast-repast",
          "the-lominsan-envoy",
          "call-of-the-sea-limsa-lominsa",
        ]
      },
      {
        title: "1.3 Ul'dah Quest Chain",
        questIds: [
          "coming-to-ul-dah",
          "close-to-home-ul-dah",
          "we-must-rebuild",
          "nothing-to-see-here",
          "underneath-the-sultantree",
          "step-nine",
          "prudence-at-this-junction",
          "out-of-house-and-home",
          "way-down-in-the-hole",
          "takin-what-they-re-givin",
          "supply-and-demands",
          "give-it-to-me-raw",
          "the-perfect-swarm",
          "last-letter-to-lost-hope",
          "heir-today-gone-tomorrow",
          "passing-the-blade",
          "following-footfalls",
          "storms-on-the-horizon",
          "oh-captain-my-captain",
          "secrets-and-lies",
          "duty-honor-country",
          "a-matter-of-tradition",
          "a-royal-reception",
          "the-ul-dahn-envoy",
          "call-of-the-sea-ul-dah",
        ]
      },
    ]
  },
  {
    title: "2 Levels 15-20",
    questIds: [
      "it-s-probably-pirates",
      "call-of-the-forest",
      "fire-in-the-gloom",
      "call-of-the-desert",
      "into-a-copper-hell",
      "the-scions-of-the-seventh-dawn",
      "a-wild-rose-by-any-other-name",
      "unsolved-mystery",
      "what-poor-people-think",
      "a-proper-burial",
      "for-the-children",
      "amalj-aa-wrong-places",
      "dressed-to-deceive",
      "life-materia-and-everything",
      "lord-of-the-inferno",
      "a-hero-in-the-making",
      "the-company-you-keep-immortal-flames",
      "for-coin-and-country",
      "the-company-you-keep-maelstrom",
      "till-sea-swallows-all",
      "the-company-you-keep-twin-adder",
      "wood-s-will-be-done",
      "sylph-management",
      "we-come-in-peace",
      "sylphic-studies",
      "first-impressions",
    ]
  },
  {
    title: "3 Levels 21-30",
    questIds: [
      "first-contact",
      "dance-dance-diplomacy",
      "forest-friend",
      "presence-of-the-enemy",
      "brotherly-love",
      "spirited-away",
      "druthers-house-rules",
      "never-forget",
      "microbrewing",
      "like-fine-wine",
      "sylphish-concerns",
      "nouveau-riche",
      "into-the-beast-s-maw",
      "a-simple-gift",
      "believe-in-your-sylph",
      "back-from-the-wood",
      "shadow-of-darkness",
      "highbridge-times",
      "where-there-is-smoke",
      "on-to-little-ala-mhigo",
      "tea-for-three",
      "foot-in-the-door",
      "meeting-with-the-resistance",
      "killing-him-softly",
      "helping-horn",
      "he-ain-t-heavy",
      "come-highly-recommended",
    ]
  },
  {
    title: "4 Levels 31-40",
    questIds: [
      "the-perfect-prey",
      "when-the-worm-turns",
      "there-and-back-again",
      "the-things-we-do-for-cheese",
      "what-do-you-mean-you-forgot-the-wine",
      "an-offer-you-can-refuse",
      "it-won-t-work",
      "give-a-man-a-drink",
      "that-weight",
      "battle-scars",
      "it-was-a-very-good-year",
      "in-the-company-of-heroes",
      "as-you-wish",
      "lord-of-crags",
      "all-good-things",
      "you-can-t-take-it-with-you",
      "bringing-out-the-dead",
      "bury-me-not-on-the-lone-prairie",
      "eyes-on-me",
      "he-who-waited-behind",
      "cold-reception",
      "the-unending-war",
      "men-of-honor",
      "three-for-three",
      "the-rose-and-the-unicorn",
      "the-talk-of-coerthas",
      "road-to-redemption",
      "following-the-evidence",
      "in-the-eyes-of-gods-and-men",
      "the-final-flight-of-the-enterprise",
      "ye-of-little-faith",
      "factual-folklore",
      "the-best-inventions",
      "influencing-inquisitors",
      "by-the-lights-of-ishgard",
      "blood-for-blood",
      "the-heretic-among-us",
    ]
  },
  {
    title: "5 Levels 41-50",
    questIds: [
      "in-pursuit-of-the-past",
      "into-the-eye-of-the-storm",
      "sealed-with-science",
      "with-the-utmost-care",
      "a-promising-prospect",
      "it-s-probably-not-pirates",
      "representing-the-representative",
      "the-reluctant-researcher",
      "sweet-somethings",
      "history-repeating",
      "the-curious-case-of-giggity",
      "better-late-than-never",
      "lady-of-the-vortex",
      "reclamation",
      "casing-the-castrum",
      "eyes-on-the-empire",
      "footprints-in-the-snow",
      "monumental-hopes",
      "notorious-biggs",
      "come-into-my-castrum",
      "getting-even-with-garlemald",
      "drowning-out-the-voices",
      "acting-the-part",
      "dressed-for-conquest",
      "fool-me-twice",
      "every-little-thing-she-does-is-magitek",
      "escape-from-castrum-centri",
      "the-black-wolf-s-ultimatum",
      "operation-archon",
      "a-hero-in-need",
      "hearts-on-fire",
      "rock-the-castrum",
      "the-ultimate-weapon",
    ]
  },
];

export const ARR_QUEST_ORDER: string[] = [
  "coming-to-gridania",
  "close-to-home-gridania",
  "to-the-bannock",
  "passing-muster",
  "chasing-shadows",
  "eggs-over-queasy",
  "surveying-the-damage",
  "a-soldier-s-breakfast",
  "spirithold-broken",
  "on-to-bentbranch",
  "you-shall-not-trespass",
  "don-t-look-down",
  "in-the-grim-darkness-of-the-forest",
  "threat-level-elevated",
  "migrant-marauders",
  "a-hearer-is-often-late",
  "salvaging-the-scene",
  "leia-s-legacy",
  "dread-is-in-the-air",
  "to-guard-a-guardian",
  "festive-endeavors",
  "renewing-the-covenant",
  "the-gridanian-envoy",
  "call-of-the-sea-gridania",
  "coming-to-limsa-lominsa",
  "close-to-home-limsa-lominsa",
  "on-to-summerford",
  "dressed-to-call",
  "lurkers-in-the-grotto",
  "washed-up",
  "double-dealing",
  "loam-maintenance",
  "plowshares-to-swords",
  "just-deserts",
  "sky-high",
  "thanks-a-million",
  "relighting-the-torch",
  "on-to-the-drydocks",
  "without-a-doubt",
  "righting-the-shipwright",
  "do-angry-pirates-dream",
  "victory-in-peril",
  "men-of-the-blue-tattoos",
  "feint-and-strike",
  "high-society",
  "a-mizzenmast-repast",
  "the-lominsan-envoy",
  "call-of-the-sea-limsa-lominsa",
  "coming-to-ul-dah",
  "close-to-home-ul-dah",
  "we-must-rebuild",
  "nothing-to-see-here",
  "underneath-the-sultantree",
  "step-nine",
  "prudence-at-this-junction",
  "out-of-house-and-home",
  "way-down-in-the-hole",
  "takin-what-they-re-givin",
  "supply-and-demands",
  "give-it-to-me-raw",
  "the-perfect-swarm",
  "last-letter-to-lost-hope",
  "heir-today-gone-tomorrow",
  "passing-the-blade",
  "following-footfalls",
  "storms-on-the-horizon",
  "oh-captain-my-captain",
  "secrets-and-lies",
  "duty-honor-country",
  "a-matter-of-tradition",
  "a-royal-reception",
  "the-ul-dahn-envoy",
  "call-of-the-sea-ul-dah",
  "it-s-probably-pirates",
  "call-of-the-forest",
  "fire-in-the-gloom",
  "call-of-the-desert",
  "into-a-copper-hell",
  "the-scions-of-the-seventh-dawn",
  "a-wild-rose-by-any-other-name",
  "unsolved-mystery",
  "what-poor-people-think",
  "a-proper-burial",
  "for-the-children",
  "amalj-aa-wrong-places",
  "dressed-to-deceive",
  "life-materia-and-everything",
  "lord-of-the-inferno",
  "a-hero-in-the-making",
  "the-company-you-keep-immortal-flames",
  "for-coin-and-country",
  "the-company-you-keep-maelstrom",
  "till-sea-swallows-all",
  "the-company-you-keep-twin-adder",
  "wood-s-will-be-done",
  "sylph-management",
  "we-come-in-peace",
  "sylphic-studies",
  "first-impressions",
  "first-contact",
  "dance-dance-diplomacy",
  "forest-friend",
  "presence-of-the-enemy",
  "brotherly-love",
  "spirited-away",
  "druthers-house-rules",
  "never-forget",
  "microbrewing",
  "like-fine-wine",
  "sylphish-concerns",
  "nouveau-riche",
  "into-the-beast-s-maw",
  "a-simple-gift",
  "believe-in-your-sylph",
  "back-from-the-wood",
  "shadow-of-darkness",
  "highbridge-times",
  "where-there-is-smoke",
  "on-to-little-ala-mhigo",
  "tea-for-three",
  "foot-in-the-door",
  "meeting-with-the-resistance",
  "killing-him-softly",
  "helping-horn",
  "he-ain-t-heavy",
  "come-highly-recommended",
  "the-perfect-prey",
  "when-the-worm-turns",
  "there-and-back-again",
  "the-things-we-do-for-cheese",
  "what-do-you-mean-you-forgot-the-wine",
  "an-offer-you-can-refuse",
  "it-won-t-work",
  "give-a-man-a-drink",
  "that-weight",
  "battle-scars",
  "it-was-a-very-good-year",
  "in-the-company-of-heroes",
  "as-you-wish",
  "lord-of-crags",
  "all-good-things",
  "you-can-t-take-it-with-you",
  "bringing-out-the-dead",
  "bury-me-not-on-the-lone-prairie",
  "eyes-on-me",
  "he-who-waited-behind",
  "cold-reception",
  "the-unending-war",
  "men-of-honor",
  "three-for-three",
  "the-rose-and-the-unicorn",
  "the-talk-of-coerthas",
  "road-to-redemption",
  "following-the-evidence",
  "in-the-eyes-of-gods-and-men",
  "the-final-flight-of-the-enterprise",
  "ye-of-little-faith",
  "factual-folklore",
  "the-best-inventions",
  "influencing-inquisitors",
  "by-the-lights-of-ishgard",
  "blood-for-blood",
  "the-heretic-among-us",
  "in-pursuit-of-the-past",
  "into-the-eye-of-the-storm",
  "sealed-with-science",
  "with-the-utmost-care",
  "a-promising-prospect",
  "it-s-probably-not-pirates",
  "representing-the-representative",
  "the-reluctant-researcher",
  "sweet-somethings",
  "history-repeating",
  "the-curious-case-of-giggity",
  "better-late-than-never",
  "lady-of-the-vortex",
  "reclamation",
  "casing-the-castrum",
  "eyes-on-the-empire",
  "footprints-in-the-snow",
  "monumental-hopes",
  "notorious-biggs",
  "come-into-my-castrum",
  "getting-even-with-garlemald",
  "drowning-out-the-voices",
  "acting-the-part",
  "dressed-for-conquest",
  "fool-me-twice",
  "every-little-thing-she-does-is-magitek",
  "escape-from-castrum-centri",
  "the-black-wolf-s-ultimatum",
  "operation-archon",
  "a-hero-in-need",
  "hearts-on-fire",
  "rock-the-castrum",
  "the-ultimate-weapon",
];
