#!/usr/bin/env python3
"""
fix_th_structure.py — Rebuilds th/ files as exact structural clones of en/.

GitLocalize matches source (en/) and target (th/) by key structure.
The old th/ schema differed (missing id/character, had extra title_status/status)
which caused GitLocalize to report 0% translated.

New th/ files are identical to en/ files except:
  - "title"             → Thai translation
  - "dialogues[i].text" → Thai translation

All other keys (id, wiki_path, patch, character, level, etc.) are copied
unchanged from en/ so the structure is byte-for-byte compatible.

Usage:
    python scripts/fix_th_structure.py
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def fix() -> None:
    en_root = ROOT / "content_community" / "en"
    th_root = ROOT / "content_community" / "th"
    total = 0

    for en_file in sorted(en_root.rglob("*.json")):
        rel = en_file.relative_to(en_root)
        th_file = th_root / rel

        en: dict = json.loads(en_file.read_text(encoding="utf-8"))

        # Salvage existing Thai text from the old th/ file
        th_old: dict = (
            json.loads(th_file.read_text(encoding="utf-8"))
            if th_file.exists() else {}
        )
        old_dialogues: list[dict] = th_old.get("dialogues") or []

        # Clone en/ completely, then overwrite only the text values with Thai
        th_new: dict = json.loads(json.dumps(en))          # deep copy
        th_new["title"] = th_old.get("title") or ""

        for i, d in enumerate(th_new.get("dialogues") or []):
            d["text"] = (
                old_dialogues[i].get("text") or ""
                if i < len(old_dialogues) else ""
            )

        th_file.parent.mkdir(parents=True, exist_ok=True)
        th_file.write_text(
            json.dumps(th_new, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        total += 1

    print(f"Done -- rebuilt {total} th/ files to mirror en/ structure.")


if __name__ == "__main__":
    fix()
