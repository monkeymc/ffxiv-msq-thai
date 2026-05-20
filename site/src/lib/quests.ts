import { readFileSync, readdirSync, existsSync } from 'fs';
import { join } from 'path';

export interface Dialogue {
  character: string;
  text_en: string;
  text_th: string;
}

export interface Quest {
  id: string;
  wiki_path: string;
  wiki_image_url: string;
  patch: string;
  title_en: string;
  title_th: string;
  level: string;
  location: string;
  npc_start: string;
  dialogues: Dialogue[];
  source: 'contributor' | 'ai' | 'none';
  contributor?: string;
  expansion: string;       // e.g. 'arr', 'hw' — determined from folder
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
   File system — content lives at /content/arr/2.0/ etc.
───────────────────────────────────────────────────────────── */
const ROOT = join(process.cwd(), '..');

function blueprintDir(exp: Expansion) {
  return join(ROOT, 'content',             exp.id, exp.patch);
}
function aiDir(exp: Expansion) {
  return join(ROOT, 'content_ai',          exp.id, exp.patch);
}
function contribDir(exp: Expansion) {
  return join(ROOT, 'content_contributor', exp.id, exp.patch);
}

function readJson<T>(path: string): T | null {
  if (!existsSync(path)) return null;
  return JSON.parse(readFileSync(path, 'utf-8')) as T;
}

interface BlueprintDialogue { character: string; text_en: string; }
interface Blueprint {
  id: string; wiki_path: string; wiki_image_url: string; patch: string;
  title_en: string; level: string; location: string; npc_start: string;
  dialogues: BlueprintDialogue[];
}
interface TranslationLayer {
  title_th?: string;
  contributor?: string;
  dialogues: { text_th: string }[];
}

function mergeQuest(exp: Expansion, filename: string): Quest {
  const bp = readJson<Blueprint>(join(blueprintDir(exp), filename))!;
  const contributor = readJson<TranslationLayer>(join(contribDir(exp), filename));
  const ai = readJson<TranslationLayer>(join(aiDir(exp), filename));

  const layer = contributor ?? ai ?? null;
  const source: Quest['source'] = contributor ? 'contributor' : ai ? 'ai' : 'none';

  const dialogues: Dialogue[] = bp.dialogues.map((d, i) => ({
    character: d.character,
    text_en: d.text_en,
    text_th: layer?.dialogues[i]?.text_th ?? '',
  }));

  return {
    ...bp,
    title_th: layer?.title_th ?? '',
    dialogues,
    source,
    contributor: contributor?.contributor,
    expansion: exp.id,
  };
}

const SPECIAL_SORT_ORDER: Record<string, number> = {
  'coming-to-gridania': 1,
  'close-to-home-gridania': 2,
  'coming-to-limsa-lominsa': 1,
  'close-to-home-limsa-lominsa': 2,
  'coming-to-ul-dah': 1,
  'close-to-home-ul-dah': 2,
};

export function getAllQuests(expansionId: string = 'arr'): Quest[] {
  const exp = EXPANSIONS.find(e => e.id === expansionId);
  if (!exp || !exp.available) return [];

  const dir = blueprintDir(exp);
  if (!existsSync(dir)) return [];

  const files = readdirSync(dir).filter(f => f.endsWith('.json'));
  const quests = files.map(f => mergeQuest(exp, f));

  quests.sort((a, b) => {
    const la = parseInt(a.level) || 0;
    const lb = parseInt(b.level) || 0;
    if (la !== lb) return la - lb;

    const orderA = SPECIAL_SORT_ORDER[a.id];
    const orderB = SPECIAL_SORT_ORDER[b.id];
    if (orderA !== undefined && orderB !== undefined) {
      return orderA - orderB;
    }
    if (orderA !== undefined) return -1;
    if (orderB !== undefined) return 1;

    return a.title_en.localeCompare(b.title_en);
  });

  return quests;
}

export function getAdjacentQuests(slug: string, expansionId: string = 'arr'): { prev: Quest | null; next: Quest | null } {
  const quests = getAllQuests(expansionId);
  const idx = quests.findIndex(q => q.id === slug);
  return {
    prev: idx > 0 ? quests[idx - 1] : null,
    next: idx >= 0 && idx < quests.length - 1 ? quests[idx + 1] : null,
  };
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

export interface QuestGroup {
  num: string;
  range: string;
  title: string;
  desc: string;
  match: (lv: number) => boolean;
  quests: Quest[];
  majorSection?: string;
}

const GRIDANIA_STARTER_IDS = new Set([
  'coming-to-gridania',
  'close-to-home-gridania',
  'to-the-bannock',
  'passing-muster',
  'chasing-shadows',
  'eggs-over-queasy',
  'surveying-the-damage',
  'a-soldier-s-breakfast',
  'spirithold-broken',
  'on-to-bentbranch',
  'you-shall-not-trespass',
  'don-t-look-down',
  'in-the-grim-darkness-of-the-forest',
  'threat-level-elevated',
  'migrant-marauders',
  'a-hearer-is-often-late',
  'salvaging-the-scene',
  'leia-s-legacy',
  'dread-is-in-the-air',
  'to-guard-a-guardian',
  'festive-endeavors',
  'renewing-the-covenant',
  'the-gridanian-envoy',
  'gone-from-gridania',
  'call-of-the-sea-gridania',
  'an-eft-for-effort',
  'butcher-of-greentear',
  'feeding-time',
  'ruffled-feathers',
  'lights-out',
  'skeletons-in-my-deepcroft'
]);

const LIMSA_STARTER_IDS = new Set([
  'coming-to-limsa-lominsa',
  'close-to-home-limsa-lominsa',
  'on-to-summerford',
  'dressed-to-call',
  'lurkers-in-the-grotto',
  'washed-up',
  'double-dealing',
  'loam-maintenance',
  'plowshares-to-swords',
  'just-deserts',
  'sky-high',
  'thanks-a-million',
  'relighting-the-torch',
  'on-to-the-drydocks',
  'without-a-doubt',
  'righting-the-shipwright',
  'do-angry-pirates-dream',
  'victory-in-peril',
  'men-of-the-blue-tattoos',
  'feint-and-strike',
  'high-society',
  'a-mizzenmast-repast',
  'the-lominsan-envoy',
  'leaving-limsa-lominsa',
  'call-of-the-sea-limsa-lominsa',
  'further-afield',
  'courier-for-a-day',
  'farmer-of-fortune'
]);

const ULDAH_STARTER_IDS = new Set([
  'coming-to-ul-dah',
  'close-to-home-ul-dah',
  'we-must-rebuild',
  'nothing-to-see-here',
  'underneath-the-sultantree',
  'step-nine',
  'prudence-at-this-junction',
  'out-of-house-and-home',
  'way-down-in-the-hole',
  'takin-what-they-re-givin',
  'supply-and-demands',
  'give-it-to-me-raw',
  'the-perfect-swarm',
  'last-letter-to-lost-hope',
  'heir-today-gone-tomorrow',
  'passing-the-blade',
  'following-footfalls',
  'storms-on-the-horizon',
  'oh-captain-my-captain',
  'secrets-and-lies',
  'duty-honor-country',
  'a-matter-of-tradition',
  'a-royal-reception',
  'the-ul-dahn-envoy',
  'out-of-ul-dah',
  'call-of-the-sea-ul-dah',
  'disorderly-conduct',
  'until-a-quieter-time',
  'spriggan-cleaning',
  'compulsory-catering',
  'the-warden-works-in-mysterious-ways'
]);

export function groupQuestsByLevel(quests: Quest[]): QuestGroup[] {
  const isArr = quests.some(q => q.expansion === 'arr');
  if (isArr) {
    const gridania: Quest[] = [];
    const limsa: Quest[] = [];
    const uldah: Quest[] = [];
    const unified: Quest[] = [];
    const astral: Quest[] = [];

    quests.forEach(q => {
      if (q.patch !== '2.0') {
        astral.push(q);
      } else {
        if (GRIDANIA_STARTER_IDS.has(q.id)) {
          gridania.push(q);
        } else if (LIMSA_STARTER_IDS.has(q.id)) {
          limsa.push(q);
        } else if (ULDAH_STARTER_IDS.has(q.id)) {
          uldah.push(q);
        } else {
          unified.push(q);
        }
      }
    });

    const groups: QuestGroup[] = [
      {
        num: '1',
        range: 'Lv 1—15',
        title: 'Gridania Quest Chain',
        desc: 'เส้นทางเริ่มต้นของป่าใหญ่ Gridania',
        match: () => false,
        quests: gridania,
        majorSection: 'Seventh Umbral Era Main Scenario Quests'
      },
      {
        num: '2',
        range: 'Lv 1—15',
        title: 'Limsa Lominsa Quest Chain',
        desc: 'เส้นทางเริ่มต้นของเมืองท่า Limsa Lominsa',
        match: () => false,
        quests: limsa,
        majorSection: 'Seventh Umbral Era Main Scenario Quests'
      },
      {
        num: '3',
        range: 'Lv 1—15',
        title: 'Ul\'dah Quest Chain',
        desc: 'เส้นทางเริ่มต้นของนครทะเลทราย Ul\'dah',
        match: () => false,
        quests: uldah,
        majorSection: 'Seventh Umbral Era Main Scenario Quests'
      },
      {
        num: '4',
        range: 'Lv 15—50',
        title: 'Unified Quest Chain',
        desc: 'เส้นทางรวมพลตั้งแต่วิหาร Scions ถึงการต่อสู้ปกป้อง Eorzea',
        match: () => false,
        quests: unified,
        majorSection: 'Seventh Umbral Era Main Scenario Quests'
      },
      {
        num: '5',
        range: 'Lv 50+',
        title: 'Seventh Astral Era Main Scenario Quests',
        desc: 'เนื้อเรื่องช่วงรอยต่อ Patch 2.1 — 2.55 ก่อนเข้าสู่ Heavensward',
        match: () => false,
        quests: astral,
        majorSection: 'Seventh Astral Era Main Scenario Quests'
      }
    ];

    return groups.filter(g => g.quests.length > 0);
  }

  // Fallback for other expansions
  const groups: Omit<QuestGroup, 'quests'>[] = [
    { num: 'I',   range: 'Lv 1—15',  title: 'การเริ่มต้นการผจญภัย', desc: 'เลือกเมืองเริ่มต้น · พบกับ Eorzea',     match: (l) => l <= 15 },
    { num: 'II',  range: 'Lv 16—30', title: 'การพบกับ Scions',     desc: 'รวมพลและการเริ่มต้นภารกิจที่ยิ่งใหญ่',     match: (l) => l >= 16 && l <= 30 },
    { num: 'III', range: 'Lv 31—50', title: 'ปฐมบทแห่งวีรบุรุษ',     desc: 'สู่บทอวสานของ Seventh Umbral Era', match: (l) => l >= 31 && l <= 50 },
    { num: 'IV',  range: 'Lv 50+',   title: 'หลังบทอวสาน',           desc: 'สะพานสู่ภาคถัดไป',  match: (l) => l > 50 },
  ];
  return groups
    .map(g => ({ ...g, quests: quests.filter(q => g.match(parseInt(q.level) || 0)) }))
    .filter(g => g.quests.length > 0);
}

