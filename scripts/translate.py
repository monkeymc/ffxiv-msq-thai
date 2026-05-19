"""
translate.py — แปล quest dialogue เป็นภาษาไทยด้วย Claude API
Usage: python scripts/translate.py --input content/arr/2.0/ --characters characters/
"""

import argparse
import json
from pathlib import Path

import anthropic

WIKI_BASE = "https://ffxiv.consolegameswiki.com"
CLIENT = anthropic.Anthropic()



def build_system_prompt(char_dir: Path) -> str:
    sections: list[str] = []
    order = ["context", "glossary"]  # โหลดก่อน
    files = {f.stem: f for f in char_dir.glob("*.md") if f.stem != "TEMPLATE"}

    for key in order:
        if key in files:
            sections.append(files[key].read_text(encoding="utf-8"))
            del files[key]

    for f in files.values():
        sections.append(f.read_text(encoding="utf-8"))

    combined = "\n\n---\n\n".join(sections)
    return f"""คุณคือนักแปลผู้เชี่ยวชาญ Final Fantasy XIV เป็นภาษาไทย

## หลักการแปล
- แปลให้สละสลวยเป็นภาษาไทยที่อ่านง่าย ไม่แปลตรงตัวจนเกินไป
- รักษาอารมณ์และน้ำเสียงของตัวละครแต่ละคนตาม character voice ที่กำหนด
- ใช้ glossary เป็นมาตรฐานคำศัพท์ ห้ามเบี่ยงเบน
- ห้ามเพิ่มหรือลดความหมายจากต้นฉบับ

## Reference Materials
{combined}
"""


def translate_quest(blueprint: dict, system_prompt: str) -> dict:
    """แปล blueprint และ return AI layer (ไม่แก้ blueprint)"""
    dialogues = blueprint.get("dialogues", [])

    title_th = ""
    if blueprint.get("title_en"):
        title_response = CLIENT.messages.create(
            model="claude-opus-4-7",
            max_tokens=200,
            system=system_prompt,
            messages=[{
                "role": "user",
                "content": f"แปลชื่อ quest นี้เป็นภาษาไทย (ตอบแค่ชื่อที่แปลแล้วเท่านั้น):\n{blueprint['title_en']}"
            }]
        )
        title_th = title_response.content[0].text.strip()

    translated_dialogues = [{"text_th": ""} for _ in dialogues]

    if dialogues:
        dialogue_lines = "\n".join(
            f"[{i}] {d['character']}: {d['text_en']}"
            for i, d in enumerate(dialogues)
        )
        response = CLIENT.messages.create(
            model="claude-opus-4-7",
            max_tokens=4096,
            system=system_prompt,
            messages=[{
                "role": "user",
                "content": (
                    f"แปล dialogue ต่อไปนี้จาก quest \"{blueprint['title_en']}\" เป็นภาษาไทย\n"
                    f"ตอบกลับในรูปแบบ JSON array: [{{\"index\": 0, \"text_th\": \"...\"}}]\n\n"
                    f"{dialogue_lines}"
                )
            }]
        )
        raw = response.content[0].text.strip()
        start = raw.find("[")
        end = raw.rfind("]") + 1
        if start != -1 and end > start:
            for item in json.loads(raw[start:end]):
                idx = item.get("index")
                if idx is not None and idx < len(translated_dialogues):
                    translated_dialogues[idx]["text_th"] = item.get("text_th", "")

    return {
        "title_th": title_th,
        "dialogues": translated_dialogues,
    }


def main():
    parser = argparse.ArgumentParser(description="Translate FFXIV MSQ quests to Thai")
    parser.add_argument("--input", default="content/arr/2.0", help="Blueprint directory")
    parser.add_argument("--output", default="content_ai/arr/2.0", help="AI layer output directory")
    parser.add_argument("--characters", default="characters", help="Directory containing character .md files")
    parser.add_argument("--limit", type=int, default=0, help="Max quests to translate (0 = all)")
    args = parser.parse_args()

    input_dir = Path(args.input)
    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)
    char_dir = Path(args.characters)

    md_files = [f.stem for f in char_dir.glob("*.md") if f.stem != "TEMPLATE"]
    print(f"Loaded {len(md_files)} reference files: {', '.join(md_files)}")

    system_prompt = build_system_prompt(char_dir)

    quest_files = sorted(input_dir.glob("*.json"))
    if args.limit:
        quest_files = quest_files[: args.limit]

    print(f"Translating {len(quest_files)} quests → {output_dir}")
    for i, quest_file in enumerate(quest_files, 1):
        out_file = output_dir / quest_file.name
        if out_file.exists():
            print(f"  [{i}/{len(quest_files)}] skip: {quest_file.stem}")
            continue

        blueprint = json.loads(quest_file.read_text(encoding="utf-8"))
        print(f"  [{i}/{len(quest_files)}] translating: {blueprint['title_en']}")
        try:
            ai_layer = translate_quest(blueprint, system_prompt)
            out_file.write_text(json.dumps(ai_layer, ensure_ascii=False, indent=2))
        except Exception as e:
            print(f"    ERROR: {e}")

    print("Done.")


if __name__ == "__main__":
    main()
