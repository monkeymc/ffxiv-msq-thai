#!/usr/bin/env python3
"""
migrate_to_community.py — One-time migration script.

Reads content/ + content_ai/ and produces content_community/ with a flat,
merged JSON structure. Every title and dialogue gets a "status" key set to
"AI" (or "none" if there is no Thai translation yet), ready for the community
to flip to "Community" as they review each line.

Usage:
    python scripts/migrate_to_community.py
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def migrate() -> None:
    content_dir = ROOT / "content"
    ai_dir = ROOT / "content_ai"
    community_dir = ROOT / "content_community"

    total_files = 0

    for exp_dir in sorted(content_dir.iterdir()):
        if not exp_dir.is_dir():
            continue
        exp_id = exp_dir.name

        for patch_dir in sorted(exp_dir.iterdir()):
            if not patch_dir.is_dir():
                continue
            patch = patch_dir.name

            out_dir = community_dir / exp_id / patch
            out_dir.mkdir(parents=True, exist_ok=True)

            json_files = sorted(patch_dir.glob("*.json"))
            print(f"[{exp_id}/{patch}] {len(json_files)} files -> content_community/{exp_id}/{patch}/")

            for bp_file in json_files:
                bp: dict = json.loads(bp_file.read_text(encoding="utf-8"))

                ai_file = ai_dir / exp_id / patch / bp_file.name
                ai: dict | None = (
                    json.loads(ai_file.read_text(encoding="utf-8"))
                    if ai_file.exists()
                    else None
                )

                title_th: str = (ai or {}).get("title_th") or ""
                title_status: str = "AI" if title_th else "none"

                ai_dialogues: list[dict] = (ai or {}).get("dialogues") or []
                dialogues: list[dict] = []
                for i, d in enumerate(bp.get("dialogues") or []):
                    text_th: str = (
                        ai_dialogues[i].get("text_th") or ""
                        if i < len(ai_dialogues)
                        else ""
                    )
                    dialogues.append({
                        "character": d["character"],
                        "text_en": d["text_en"],
                        "text_th": text_th,
                        "status": "AI" if text_th else "none",
                    })

                community: dict = {
                    "id": bp["id"],
                    "wiki_path": bp.get("wiki_path", ""),
                    "wiki_image_url": bp.get("wiki_image_url", ""),
                    "patch": bp.get("patch", patch),
                    "title_en": bp["title_en"],
                    "title_th": title_th,
                    "title_status": title_status,
                    "level": bp.get("level", ""),
                    "location": bp.get("location", ""),
                    "npc_start": bp.get("npc_start", ""),
                    "dialogues": dialogues,
                }

                out_file = out_dir / bp_file.name
                out_file.write_text(
                    json.dumps(community, ensure_ascii=False, indent=2),
                    encoding="utf-8",
                )
                total_files += 1

    print(f"\nDone — migrated {total_files} files to content_community/")


if __name__ == "__main__":
    migrate()
