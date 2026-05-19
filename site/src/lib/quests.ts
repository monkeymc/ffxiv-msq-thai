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
}

// process.cwd() = site/ during both dev and build
const ROOT = join(process.cwd(), '..');
const BLUEPRINT_DIR = join(ROOT, 'content/arr/2.0');
const AI_DIR = join(ROOT, 'content_ai/arr/2.0');
const CONTRIBUTOR_DIR = join(ROOT, 'content_contributor/arr/2.0');

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

function mergeQuest(filename: string): Quest {
  const bp = readJson<Blueprint>(join(BLUEPRINT_DIR, filename))!;
  const contributor = readJson<TranslationLayer>(join(CONTRIBUTOR_DIR, filename));
  const ai = readJson<TranslationLayer>(join(AI_DIR, filename));

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
  };
}

export function getAllQuests(): Quest[] {
  const files = readdirSync(BLUEPRINT_DIR).filter(f => f.endsWith('.json'));

  const quests = files.map(f => mergeQuest(f));

  quests.sort((a, b) => {
    const la = parseInt(a.level) || 0;
    const lb = parseInt(b.level) || 0;
    if (la !== lb) return la - lb;
    return a.title_en.localeCompare(b.title_en);
  });

  return quests;
}

export function getAdjacentQuests(slug: string): { prev: Quest | null; next: Quest | null } {
  const quests = getAllQuests();
  const idx = quests.findIndex(q => q.id === slug);
  return {
    prev: idx > 0 ? quests[idx - 1] : null,
    next: idx < quests.length - 1 ? quests[idx + 1] : null,
  };
}
