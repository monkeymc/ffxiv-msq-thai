"""
scrape.py — ดึงข้อมูล MSQ quest จาก consolegameswiki
Usage: python scripts/scrape.py [--patch 2.0] [--out content/arr/]
"""

import argparse
import json
import re
import time
from pathlib import Path

import requests
from bs4 import BeautifulSoup

WIKI_BASE = "https://ffxiv.consolegameswiki.com"

HEADERS = {
    "User-Agent": "ffxiv-msq-thai/1.0 (educational project; mc.daikazoku@gmail.com)"
}

ARR_INDEX_PAGES = {
    "2.0": "/wiki/A_Realm_Reborn_Main_Scenario_Quests",
    "2.1": "/wiki/Patch_2.1_Main_Scenario_Quests",
    "2.2": "/wiki/Patch_2.2_Main_Scenario_Quests",
    "2.3": "/wiki/Patch_2.3_Main_Scenario_Quests",
    "2.4": "/wiki/Patch_2.4_Main_Scenario_Quests",
    "2.5": "/wiki/Patch_2.5_Main_Scenario_Quests",
}


def slug(title: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", title.lower()).strip("-")


def wiki_image_url(src: str) -> str:
    """แปลง thumbnail src เป็น full-size image URL"""
    if not src:
        return ""
    # /mediawiki/images/thumb/a/ab/Name.png/500px-Name.png → /mediawiki/images/a/ab/Name.png
    m = re.match(r"(/mediawiki/images)/thumb/(.+)\.\w+/\d+px-.+", src)
    if m:
        return WIKI_BASE + m.group(1) + "/" + m.group(2) + src[src.rfind("."):]
    if src.startswith("/"):
        return WIKI_BASE + src
    return src


def get_quest_list(patch: str) -> list[dict]:
    path = ARR_INDEX_PAGES[patch]
    resp = requests.get(WIKI_BASE + path, headers=HEADERS, timeout=15)
    resp.raise_for_status()
    soup = BeautifulSoup(resp.text, "html.parser")

    quests = []
    seen = set()
    for table in soup.find_all("table", class_="quest"):
        for row in table.find_all("tr")[1:]:  # skip header
            link = row.find("a")
            if not link:
                continue
            href = link.get("href", "")
            title = link.get_text(strip=True)
            if href and title and href not in seen:
                seen.add(href)
                cells = row.find_all(["td", "th"])
                level = cells[2].get_text(strip=True) if len(cells) > 2 else ""
                quests.append({
                    "title_en": title,
                    "wiki_path": href,
                    "level": level,
                })

    return quests


def get_quest_detail(wiki_path: str) -> dict:
    resp = requests.get(WIKI_BASE + wiki_path, headers=HEADERS, timeout=15)
    resp.raise_for_status()
    soup = BeautifulSoup(resp.text, "html.parser")
    content = soup.find("div", id="mw-content-text")

    # --- infobox ---
    infobox = content.find(class_="infobox-n")
    location = npc_start = patch = level = ""
    if infobox:
        for dt in infobox.find_all("dt"):
            dd = dt.find_next_sibling("dd")
            key = dt.get_text(strip=True).lower()
            val = dd.get_text(strip=True) if dd else ""
            if "location" in key:
                location = val
            elif "quest giver" in key:
                npc_start = val
            elif "patch" in key:
                patch = val
            elif "level" in key:
                level = val

    # --- quest image (banner ของ quest) ---
    # ใช้ dialogue-quest-banner ถ้ามี, fallback = generic Quest_Accepted
    wiki_image = ""
    banner = content.find(class_="dialogue-quest-banner")
    if banner:
        img = banner.find("img")
        if img:
            wiki_image = wiki_image_url(img.get("src", ""))

    # --- dialogue ---
    SKIP_SPEAKERS = {"optional dialogue", "unknown"}
    SKIP_LINE_CLASSES = {"dialogue-line--cutscene", "dialogue-line--system"}

    dialogues = []
    for box in content.find_all(class_="dialogue-box"):
        header = box.find(class_="dialogue-box-header")
        speaker = header.get_text(strip=True) if header else "Unknown"
        speaker = re.sub(r"\[.*?\]", "", speaker).strip()

        # ข้าม optional/system blocks — เนื้อหาซ้ำกับ attributed blocks ด้านล่าง
        if speaker.lower() in SKIP_SPEAKERS:
            continue

        for line in box.find_all(class_="dialogue-line"):
            classes = set(line.get("class", []))
            if classes & SKIP_LINE_CLASSES:
                continue
            text = line.get_text(" ", strip=True)
            if text:
                dialogues.append({
                    "character": speaker,
                    "text_en": text,
                    "text_th": "",
                })

    return {
        "wiki_image_url": wiki_image,
        "location": location,
        "npc_start": npc_start,
        "patch": patch,
        "level": level,
        "dialogues": dialogues,
    }


def scrape_patch(patch: str, out_dir: Path, delay: float = 1.5):
    out_dir.mkdir(parents=True, exist_ok=True)
    quests = get_quest_list(patch)
    print(f"[patch {patch}] found {len(quests)} quests")

    for i, q in enumerate(quests, 1):
        out_file = out_dir / f"{slug(q['title_en'])}.json"
        if out_file.exists():
            print(f"  [{i:03}/{len(quests)}] skip  {q['title_en']}")
            continue

        print(f"  [{i:03}/{len(quests)}] scrape {q['title_en']}")
        try:
            detail = get_quest_detail(q["wiki_path"])
            data = {
                "id": slug(q["title_en"]),
                "wiki_path": q["wiki_path"],
                "wiki_image_url": detail["wiki_image_url"],
                "patch": detail["patch"] or patch,
                "title_en": q["title_en"],
                "level": detail["level"] or q["level"],
                "location": detail["location"],
                "npc_start": detail["npc_start"],
                "dialogues": [
                    {"character": d["character"], "text_en": d["text_en"]}
                    for d in detail["dialogues"]
                ],
            }
            out_file.write_text(json.dumps(data, ensure_ascii=False, indent=2))
        except Exception as e:
            print(f"    ERROR: {e}")

        time.sleep(delay)

    print(f"[patch {patch}] done → {out_dir}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--patch", default="2.0", choices=ARR_INDEX_PAGES.keys())
    parser.add_argument("--url", default="", help="Override index page path/URL (e.g. /wiki/Seventh_Umbral_Era_Main_Scenario_Quests)")
    parser.add_argument("--out", default="content/arr")
    parser.add_argument("--delay", type=float, default=1.5)
    parser.add_argument("--limit", type=int, default=0, help="Scrape only N quests (0=all)")
    args = parser.parse_args()

    out_dir = Path(args.out) / args.patch

    if args.url:
        index_path = args.url if args.url.startswith("/") else "/" + args.url.split(WIKI_BASE, 1)[-1]
        resp = requests.get(WIKI_BASE + index_path, headers=HEADERS, timeout=15)
        resp.raise_for_status()
        soup = BeautifulSoup(resp.text, "html.parser")
        quests = []
        seen = set()
        for table in soup.find_all("table", class_="quest"):
            for row in table.find_all("tr")[1:]:
                link = row.find("a")
                if not link:
                    continue
                href = link.get("href", "")
                title = link.get_text(strip=True)
                if href and title and href not in seen:
                    seen.add(href)
                    cells = row.find_all(["td", "th"])
                    level = cells[2].get_text(strip=True) if len(cells) > 2 else ""
                    quests.append({"title_en": title, "wiki_path": href, "level": level})
    else:
        quests = get_quest_list(args.patch)
    if args.limit:
        quests = quests[: args.limit]

    print(f"[patch {args.patch}] found {len(quests)} quests")
    out_dir.mkdir(parents=True, exist_ok=True)

    for i, q in enumerate(quests, 1):
        out_file = out_dir / f"{slug(q['title_en'])}.json"
        if out_file.exists():
            print(f"  [{i:03}/{len(quests)}] skip  {q['title_en']}")
            continue

        print(f"  [{i:03}/{len(quests)}] scrape {q['title_en']}")
        try:
            detail = get_quest_detail(q["wiki_path"])
            data = {
                "id": slug(q["title_en"]),
                "wiki_path": q["wiki_path"],
                "wiki_image_url": detail["wiki_image_url"],
                "patch": detail["patch"] or args.patch,
                "title_en": q["title_en"],
                "level": detail["level"] or q["level"],
                "location": detail["location"],
                "npc_start": detail["npc_start"],
                "dialogues": [
                    {"character": d["character"], "text_en": d["text_en"]}
                    for d in detail["dialogues"]
                ],
            }
            out_file.write_text(json.dumps(data, ensure_ascii=False, indent=2))
        except Exception as e:
            print(f"    ERROR: {e}")

        time.sleep(args.delay)

    print("done.")


if __name__ == "__main__":
    main()
