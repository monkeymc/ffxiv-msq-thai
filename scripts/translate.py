"""
translate.py — แปล quest dialogue เป็นภาษาไทยด้วย Claude API
Usage: python scripts/translate.py --input content/arr/2.0/ --characters characters/
"""

import argparse
import json
from pathlib import Path

WIKI_BASE = "https://ffxiv.consolegameswiki.com"


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
- แปลบทสนทนาให้ออกมาเป็น **ภาษาพูดที่เป็นธรรมชาติ (Natural Spoken/Conversational Thai)** เหมือนบทพากย์ละคร ภาพยนตร์ หรือเกมแนวสวมบทบาท ไม่ใช่ภาษาเขียน ภาษาหนังสือ หรือภาษาที่แปลตรงตัวจากอังกฤษ
- หลีกเลี่ยงโครงสร้างประโยคภาษาอังกฤษทื่อๆ เช่น ประโยคถูกกระทำ (Passive Voice) "ฉันถูกบอกว่า..." ให้แปลงเป็นภาษาพูดธรรมชาติ เช่น "มีคนบอกฉันว่า..." หรือ "เขาบอกฉันว่า..."
- หลีกเลี่ยงคำเชื่อมหรือคำขึ้นต้นที่เป็นทางการเกินไปในบทสนทนาทั่วไป เช่น "ทำการ...", "มีความจำเป็นต้อง...", "ด้วยเหตุนี้...", "ส่งผลให้...", "ณ ที่แห่งนั้น" (ยกเว้นเมื่อตัวละครพูดเป็นทางการมากๆ เช่น รายงานทหาร หรือเป็นทางการตามบุคลิก)
- เลือกสรรคำสรรพนามและคำลงท้าย (ครับ, ค่ะ, นะ, สิ, หรอก, หรือเปล่า, หรือยัง, น่ะ) ให้ตรงกับบุคลิก ความสัมพันธ์ และบริบทของตัวละคร เพื่อให้บทสนทนาลื่นไหลและมีมิติ
- รักษาอารมณ์และน้ำเสียงของตัวละครแต่ละคนตาม character voice ที่กำหนด
- ใช้ glossary เป็นมาตรฐานคำศัพท์ ห้ามเบี่ยงเบน
- ห้ามเพิ่มหรือลดความหมายจากต้นฉบับ

## Reference Materials
{combined}
"""


def call_llm(engine: str, model: str, system_prompt: str, user_prompt: str, ollama_url: str) -> str:
    if engine == "claude":
        import anthropic
        client = anthropic.Anthropic()
        response = client.messages.create(
            model=model,
            max_tokens=4096 if "dialogue" in user_prompt.lower() or "dialogues" in user_prompt.lower() else 200,
            system=system_prompt,
            messages=[{
                "role": "user",
                "content": user_prompt
            }]
        )
        return response.content[0].text.strip()
    elif engine == "ollama":
        import requests
        url = f"{ollama_url.rstrip('/')}/api/chat"
        payload = {
            "model": model,
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt}
            ],
            "stream": False,
            "options": {
                "temperature": 0.3
            }
        }
        try:
            res = requests.post(url, json=payload)
            res.raise_for_status()
            return res.json()["message"]["content"].strip()
        except requests.exceptions.HTTPError as e:
            if e.response is not None and e.response.status_code == 404:
                try:
                    err_msg = e.response.json().get("error", "")
                    if "not found" in err_msg or "pull" in err_msg:
                        print(f"\n[ERROR] Model '{model}' not found in Ollama.")
                        print(f"Please run in terminal: ollama pull {model}\n")
                except:
                    pass
            raise e
    elif engine == "gemini":
        import os
        try:
            import google.generativeai as genai
        except ImportError:
            print("\n[ERROR] The 'google-generativeai' package is not installed.")
            print("Please run: pip install google-generativeai\n")
            raise

        api_key = os.environ.get("GEMINI_API_KEY")
        if not api_key:
            raise ValueError("[ERROR] GEMINI_API_KEY environment variable is not set. Please set it before running.")

        genai.configure(api_key=api_key)
        model_name = model if model else "gemini-1.5-flash"
        
        gemini_model = genai.GenerativeModel(
            model_name=model_name,
            system_instruction=system_prompt
        )
        response = gemini_model.generate_content(user_prompt)
        return response.text.strip()
    else:
        raise ValueError(f"Unknown engine: {engine}")


def translate_quest(blueprint: dict, system_prompt: str, engine: str, model: str, ollama_url: str) -> dict:
    """แปล blueprint และ return AI layer (ไม่แก้ blueprint)"""
    dialogues = blueprint.get("dialogues", [])

    title_th = ""
    title_en = blueprint.get("title_en", "")

    if title_en:
        if engine == "google-free":
            from deep_translator import GoogleTranslator
            try:
                title_th = GoogleTranslator(source='auto', target='th').translate(title_en)
            except Exception as e:
                print(f"    Google Translate Title Error: {e}")
                title_th = ""
        else:
            title_prompt = f"แปลชื่อ quest นี้เป็นภาษาไทย (ตอบแค่ชื่อที่แปลแล้วเท่านั้น):\n{title_en}"
            try:
                title_th = call_llm(engine, model, system_prompt, title_prompt, ollama_url)
            except Exception as e:
                print(f"    LLM Title Translation Error: {e}")
                title_th = ""

    translated_dialogues = [{"text_th": ""} for _ in dialogues]

    if dialogues:
        if engine == "google-free":
            from deep_translator import GoogleTranslator
            translator = GoogleTranslator(source='auto', target='th')
            print(f"    Translating {len(dialogues)} dialogues line-by-line via Google Translate...")
            for idx, d in enumerate(dialogues):
                text_en = d.get("text_en", "")
                if text_en:
                    try:
                        translated_dialogues[idx]["text_th"] = translator.translate(text_en)
                    except Exception as e:
                        print(f"      Google translate error at line {idx}: {e}")
        else:
            dialogue_lines = "\n".join(
                f"[{i}] {d['character']}: {d['text_en']}"
                for i, d in enumerate(dialogues)
            )
            user_prompt = (
                f"แปล dialogue ต่อไปนี้จาก quest \"{title_en}\" เป็นภาษาไทย\n"
                f"ตอบกลับในรูปแบบ JSON array: [{{\"index\": 0, \"text_th\": \"...\"}}]\n\n"
                f"{dialogue_lines}"
            )
            try:
                raw = call_llm(engine, model, system_prompt, user_prompt, ollama_url)
                start = raw.find("[")
                end = raw.rfind("]") + 1
                if start != -1 and end > start:
                    for item in json.loads(raw[start:end]):
                        idx = item.get("index")
                        if idx is not None and idx < len(translated_dialogues):
                            translated_dialogues[idx]["text_th"] = item.get("text_th", "")
            except Exception as e:
                print(f"    LLM Dialogue Translation Error: {e}")

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
    parser.add_argument("--engine", default="claude", choices=["claude", "ollama", "gemini", "google-free"], help="Translation engine to use")
    parser.add_argument("--model", default=None, help="LLM Model name (default: claude-opus-4-7 for claude, qwen2.5:7b for ollama, gemini-1.5-flash for gemini)")
    parser.add_argument("--ollama-url", default="http://localhost:11434", help="Ollama API URL")
    args = parser.parse_args()

    engine = args.engine
    model = args.model
    if model is None:
        if engine == "claude":
            model = "claude-opus-4-7"
        elif engine == "ollama":
            model = "qwen2.5:7b"
        elif engine == "gemini":
            model = "gemini-1.5-flash"

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

    print(f"Translating {len(quest_files)} quests via '{engine}' (model: {model}) -> {output_dir}")
    for i, quest_file in enumerate(quest_files, 1):
        out_file = output_dir / quest_file.name
        if out_file.exists():
            print(f"  [{i}/{len(quest_files)}] skip: {quest_file.stem}")
            continue

        blueprint = json.loads(quest_file.read_text(encoding="utf-8"))
        print(f"  [{i}/{len(quest_files)}] translating: {blueprint['title_en']}")
        try:
            ai_layer = translate_quest(blueprint, system_prompt, engine, model, args.ollama_url)
            out_file.write_text(json.dumps(ai_layer, ensure_ascii=False, indent=2), encoding="utf-8")
        except Exception as e:
            print(f"    ERROR: {e}")

    print("Done.")


if __name__ == "__main__":
    main()
