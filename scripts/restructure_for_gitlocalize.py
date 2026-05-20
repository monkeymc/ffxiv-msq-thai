#!/usr/bin/env python3
"""
restructure_for_gitlocalize.py — One-time restructure script.

Splits the merged content_community/{exp}/{patch}/*.json files into:
  content_community/en/{exp}/{patch}/*.json  <-- GitLocalize Source Path
  content_community/th/{exp}/{patch}/*.json  <-- GitLocalize Target Path

Then removes the old merged files at content_community/{exp}/.

en/ schema  — English blueprint (readable by translators as context):
  { id, wiki_path, wiki_image_url, patch, title, level, location,
    npc_start, dialogues: [{character, text}, ...] }

th/ schema  — Thai translations + community review status:
  { title, title_status, dialogues: [{text, status}, ...] }

Usage:
    python scripts/restructure_for_gitlocalize.py
"""
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
COMMUNITY = ROOT / "content_community"


def restructure() -> None:
    total_en = total_th = 0

    # Find expansion dirs directly under content_community/
    # (skip 'en' and 'th' if the script is run twice)
    exp_dirs = [
        d for d in sorted(COMMUNITY.iterdir())
        if d.is_dir() and d.name not in ("en", "th")
    ]

    if not exp_dirs:
        print("No expansion dirs found to restructure (already done?).")
        return

    for exp_dir in exp_dirs:
        exp_id = exp_dir.name

        for patch_dir in sorted(exp_dir.iterdir()):
            if not patch_dir.is_dir():
                continue
            patch = patch_dir.name

            out_en = COMMUNITY / "en" / exp_id / patch
            out_th = COMMUNITY / "th" / exp_id / patch
            out_en.mkdir(parents=True, exist_ok=True)
            out_th.mkdir(parents=True, exist_ok=True)

            json_files = sorted(patch_dir.glob("*.json"))
            print(f"[{exp_id}/{patch}] Splitting {len(json_files)} files...")

            for f in json_files:
                merged: dict = json.loads(f.read_text(encoding="utf-8"))

                # ── en/ : blueprint + English text (GitLocalize source) ──
                en_dialogues = [
                    {"character": d["character"], "text": d["text_en"]}
                    for d in merged.get("dialogues") or []
                ]
                en_doc = {
                    "id":            merged["id"],
                    "wiki_path":     merged.get("wiki_path", ""),
                    "wiki_image_url": merged.get("wiki_image_url", ""),
                    "patch":         merged.get("patch", patch),
                    "title":         merged["title_en"],
                    "level":         merged.get("level", ""),
                    "location":      merged.get("location", ""),
                    "npc_start":     merged.get("npc_start", ""),
                    "dialogues":     en_dialogues,
                }
                (out_en / f.name).write_text(
                    json.dumps(en_doc, ensure_ascii=False, indent=2),
                    encoding="utf-8",
                )
                total_en += 1

                # ── th/ : Thai translations + status (GitLocalize target) ──
                th_dialogues = [
                    {"text": d["text_th"], "status": d["status"]}
                    for d in merged.get("dialogues") or []
                ]
                th_doc = {
                    "title":        merged.get("title_th", ""),
                    "title_status": merged.get("title_status", "none"),
                    "dialogues":    th_dialogues,
                }
                (out_th / f.name).write_text(
                    json.dumps(th_doc, ensure_ascii=False, indent=2),
                    encoding="utf-8",
                )
                total_th += 1

        # Remove old merged expansion directory
        shutil.rmtree(exp_dir)
        print(f"  Removed content_community/{exp_id}/")

    print(f"\nDone — {total_en} en/ files, {total_th} th/ files.")
    print("GitLocalize Source Path : content_community/en/")
    print("GitLocalize Target Path : content_community/th/")


if __name__ == "__main__":
    restructure()
