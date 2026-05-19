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
}

export function groupQuestsByLevel(quests: Quest[]): QuestGroup[] {
  const groups: Omit<QuestGroup, 'quests'>[] = [
    { num: 'I',   range: 'Lv 1—15',  title: 'การเริ่มต้นการผจญภัย', desc: 'เลือกเมืองเริ่มต้น · พบกับ Eorzea',     match: (l) => l <= 15 },
    { num: 'II',  range: 'Lv 16—30', title: 'การพบกับ Scions',     desc: 'รวมพลและการเริ่มต้นภารกิจที่ยิ่งใหญ่',     match: (l) => l >= 16 && l <= 30 },
    { num: 'III', range: 'Lv 31—50', title: 'ปฐมบทแห่งวีรบุรุษ',     desc: 'สู่บทอวสานของ Seventh Umbral Era', match: (l) => l >= 31 && l <= 50 },
    { num: 'IV',  range: 'Lv 50+',   title: 'หลังบทอวสาน',           desc: 'Patch 2.x — สะพานสู่ Heavensward',  match: (l) => l > 50 },
  ];
  return groups
    .map(g => ({ ...g, quests: quests.filter(q => g.match(parseInt(q.level) || 0)) }))
    .filter(g => g.quests.length > 0);
}
