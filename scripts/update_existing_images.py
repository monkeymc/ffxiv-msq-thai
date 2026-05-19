"""
update_existing_images.py — อัปเดต wiki_image_url สำหรับเควสต์ที่มีรูปภาพแบบ generic (Quest_Accepted.png/Quest_Complete.png)
โดยไปดึงรูปภาพของเควสต์จริงๆ จาก infobox บน consolegameswiki
"""

import json
import pathlib
import re
import time
import requests
from bs4 import BeautifulSoup

WIKI_BASE = "https://ffxiv.consolegameswiki.com"
HEADERS = {
    "User-Agent": "ffxiv-msq-thai/1.0 (educational project; mc.daikazoku@gmail.com)"
}

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

def main():
    content_dir = pathlib.Path("content/arr")
    files = list(content_dir.rglob("*.json"))
    
    # กรองเฉพาะไฟล์ที่ยังมีรูป generic
    target_files = []
    for f in files:
        try:
            data = json.loads(f.read_text(encoding="utf-8"))
            img_url = data.get("wiki_image_url", "")
            if "Quest_Accepted.png" in img_url or "Quest_Complete.png" in img_url or not img_url:
                target_files.append((f, data))
        except Exception as e:
            print(f"Error reading {f}: {e}")

    print(f"Found {len(target_files)} files with generic images out of {len(files)} total files.")
    if not target_files:
        print("All files already have custom images!")
        return

    print("Starting update process...")
    updated_count = 0
    
    for i, (file_path, data) in enumerate(target_files, 1):
        wiki_path = data.get("wiki_path")
        if not wiki_path:
            print(f"[{i}/{len(target_files)}] Skip {file_path.name} (no wiki_path)")
            continue

        print(f"[{i}/{len(target_files)}] Fetching image for: {data['title_en']} ({file_path.name})")
        try:
            resp = requests.get(WIKI_BASE + wiki_path, headers=HEADERS, timeout=15)
            resp.raise_for_status()
            soup = BeautifulSoup(resp.text, "html.parser")
            
            content = soup.find("div", id="mw-content-text")
            if not content:
                print(f"  ERROR: No mw-content-text found")
                continue
                
            infobox = content.find(class_="infobox-n")
            wiki_image = ""
            
            # ดึงรูปจาก infobox ก่อน
            if infobox:
                for img in infobox.find_all("img"):
                    try:
                        width = int(img.get("width", 0))
                    except ValueError:
                        width = 0
                    if width >= 100:
                        wiki_image = wiki_image_url(img.get("src", ""))
                        break
            
            if wiki_image and wiki_image != data.get("wiki_image_url"):
                data["wiki_image_url"] = wiki_image
                file_path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
                print(f"  SUCCESS -> {wiki_image}")
                updated_count += 1
            else:
                print(f"  No new image found or image is the same (keeps: {data.get('wiki_image_url')})")
                
        except Exception as e:
            print(f"  ERROR: {e}")
            
        # delay เพื่อไม่ให้โดนบล็อก
        time.sleep(1.5)

    print(f"\nDone! Updated {updated_count} files.")

if __name__ == "__main__":
    main()
