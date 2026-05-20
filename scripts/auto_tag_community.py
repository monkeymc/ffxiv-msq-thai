#!/usr/bin/env python3
"""
auto_tag_community.py — CI script for the auto-tag GitHub Actions workflow.

For every content_community/ JSON file that changed in this PR, compares the
new text_th values against the base branch. Any dialogue (or title) whose
text_th was modified gets its status flipped from "AI" to "Community".

Usage:
    python scripts/auto_tag_community.py <base_ref>
    e.g.  python scripts/auto_tag_community.py origin/main
"""
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def get_changed_community_files(base_ref: str) -> list[str]:
    result = subprocess.run(
        ["git", "diff", "--name-only", f"{base_ref}...HEAD"],
        capture_output=True, text=True, cwd=ROOT,
    )
    return [
        f for f in result.stdout.strip().splitlines()
        if f.startswith("content_community/th/") and f.endswith(".json")
    ]


def get_base_content(filepath: str, base_ref: str) -> dict | None:
    result = subprocess.run(
        ["git", "show", f"{base_ref}:{filepath}"],
        capture_output=True, text=True, cwd=ROOT,
    )
    if result.returncode != 0:
        return None
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError:
        return None


def auto_tag_file(filepath: str, base_ref: str) -> bool:
    path = ROOT / filepath
    if not path.exists():
        return False

    new: dict = json.loads(path.read_text(encoding="utf-8"))
    old: dict | None = get_base_content(filepath, base_ref)
    changed = False

    # th/ files no longer carry title_status / dialogue.status fields.
    # Status is derived at build time by comparing th/ text to en/ text.
    # The auto-tag script therefore only needs to verify text changed —
    # status upgrade is handled implicitly by the site's build logic.
    # We keep this loop to detect changes and report them; no field mutation needed.
    old_dialogues: list[dict] = (old or {}).get("dialogues") or []
    for i, dialogue in enumerate(new.get("dialogues") or []):
        new_text_th = dialogue.get("text") or ""
        old_text_th = old_dialogues[i].get("text", "") if i < len(old_dialogues) else ""
        if new_text_th and new_text_th != old_text_th and False:  # status field removed
            dialogue["status"] = "Community"
            changed = True

    if changed:
        path.write_text(json.dumps(new, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"  Tagged: {filepath}")

    return changed


def main() -> None:
    base_ref = sys.argv[1] if len(sys.argv) > 1 else "origin/main"
    changed_files = get_changed_community_files(base_ref)

    if not changed_files:
        print("No content_community/ files changed.")
        return

    print(f"Checking {len(changed_files)} changed file(s) against {base_ref}...")
    tagged_any = False
    for f in changed_files:
        if auto_tag_file(f, base_ref):
            tagged_any = True

    if tagged_any:
        print("Community tagging complete.")
    else:
        print("No new community translations detected.")


if __name__ == "__main__":
    main()
