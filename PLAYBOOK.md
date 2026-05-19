# Playbook — ขยาย MSQ ไปยัง Patch ใหม่

ทำตามลำดับนี้ทุกครั้งที่เพิ่ม patch ใหม่ (3.0, 4.0, ฯลฯ)

---

## ขั้นที่ 1 — หา URL ของ wiki page

เปิด `https://ffxiv.consolegameswiki.com` แล้วค้นหาชื่อ patch เช่น:

| Patch | Wiki Page |
|-------|-----------|
| 2.0 ARR | `/wiki/Seventh_Umbral_Era_Main_Scenario_Quests` |
| 3.0 HW | `/wiki/Heavensward_Main_Scenario_Quests` |
| 4.0 SB | `/wiki/Stormblood_Main_Scenario_Quests` |
| 5.0 ShB | `/wiki/Shadowbringers_Main_Scenario_Quests` |
| 6.0 EW | `/wiki/Endwalker_Main_Scenario_Quests` |
| 7.0 DT | `/wiki/Dawntrail_Main_Scenario_Quests` |

สำหรับ sub-patch (3.1, 3.2 ฯลฯ) ดูได้จาก `/wiki/Patch_X.X_Main_Scenario_Quests`

---

## ขั้นที่ 2 — Scrape blueprint

```bash
# เพิ่ม patch ใหม่ใน ARR_INDEX_PAGES ใน scripts/scrape.py ก่อน (ถ้า sub-patch)
# จากนั้นรัน:

python scripts/scrape.py \
  --url /wiki/<WIKI_PAGE_NAME> \
  --patch <VERSION> \
  --delay 1.5

# ตัวอย่าง 3.0:
python scripts/scrape.py \
  --url /wiki/Heavensward_Main_Scenario_Quests \
  --patch 3.0 \
  --delay 1.5
```

Output จะออกที่ `content/arr/<patch>/` โดยอัตโนมัติ

---

## ขั้นที่ 3 — วิเคราะห์ตัวละครและคำศัพท์ใหม่

รัน script วิเคราะห์ (copy-paste ลง terminal):

```bash
python3 - <<'EOF'
import json, pathlib, re
from collections import Counter, defaultdict

PATCH = "3.0"  # <-- เปลี่ยนตรงนี้
quest_dir = pathlib.Path(f"content/arr/{PATCH}")
quests = [json.loads(f.read_text()) for f in sorted(quest_dir.glob("*.json"))]

char_lines = defaultdict(list)
for q in quests:
    for d in q["dialogues"]:
        char_lines[d["character"]].append(d["text_en"])

print("=== TOP CHARACTERS ===")
for name, lines in sorted(char_lines.items(), key=lambda x: -len(x[1]))[:20]:
    print(f"  {len(lines):4d}  {name}")

print("\n=== LOCATIONS ===")
locs = Counter(q["location"].split("(")[0].strip() for q in quests if q["location"])
for loc, n in locs.most_common(20):
    print(f"  {n:3d}  {loc}")

print("\n=== NEW PROPER NOUNS ===")
all_text = " ".join(d["text_en"] for q in quests for d in q["dialogues"])
phrases = re.findall(r'[Tt]he ([A-Z][a-zA-Z\']+(?:\s+[A-Z][a-zA-Z\']+)*)', all_text)
phrases += re.findall(r'(?:of|from|in|at|to|by)\s+([A-Z][a-zA-Z\']+(?:\s+[A-Z][a-zA-Z\']+){1,3})', all_text)
proper = Counter(p.strip() for p in phrases if len(p) > 3)
for term, n in proper.most_common(50):
    if n >= 5:
        print(f"  {n:4d}  {term}")

print("\n=== SAMPLE DIALOGUE (top 8 chars) ===")
for name, lines in sorted(char_lines.items(), key=lambda x: -len(x[1]))[:8]:
    samples = [l for l in lines if 30 < len(l) < 150][:3]
    print(f"\n--- {name} ({len(lines)} lines) ---")
    for s in samples: print(f"  {s}")
EOF
```

ดูผลลัพธ์และตัดสินใจว่าตัวละครไหนต้องสร้างไฟล์ใหม่ และคำไหนต้องเพิ่มใน glossary

---

## ขั้นที่ 4 — สร้าง/อัปเดต character files

**ตัวละครที่มีอยู่แล้วใน `characters/`:** ไม่ต้องสร้างใหม่ (เช่น Alphinaud, Y'shtola ปรากฏใน HW ด้วย)

**ตัวละครใหม่ที่มี > 50 บรรทัด:** ให้สร้างไฟล์ใหม่ตาม TEMPLATE:

```bash
cp characters/TEMPLATE.md characters/<ชื่อตัวละคร-lowercase>.md
```

กรอก:
- `name:` ชื่อเต็ม
- `wiki_path:` path บน consolegameswiki
- ข้อมูลตัวละคร, สำนวนการพูด, คำที่ต้องระวัง
- ตัวอย่างบทพูด EN → TH อย่างน้อย 2 คู่

---

## ขั้นที่ 5 — อัปเดต `characters/glossary.md`

เพิ่มคำใหม่จากผลวิเคราะห์ขั้นที่ 3 เข้าในหมวดที่เหมาะสม:
- สถานที่ใหม่
- องค์กรใหม่
- ศัตรู/ชนชาติใหม่
- แนวคิด/พลังใหม่
- ตัวละครสำคัญใหม่

**กฎ:** ถ้าไม่แน่ใจจะแปลหรือทับศัพท์ — ทับศัพท์ก่อน แล้วค่อยให้ชุมชน vote

---

## ขั้นที่ 6 — อัปเดต `characters/context.md`

เพิ่ม section ใหม่ด้านล่างของไฟล์:

```markdown
---

## Patch X.0 — [ชื่อ Expansion]

### ฉากหลัง
[อธิบายฉากหลังใหม่ที่เปลี่ยนไปจาก patch ก่อน]

### ตัวละครหลักใหม่
- **ชื่อ** — บทบาทและบุคลิกย่อ

### ลำดับเหตุการณ์หลัก
1. ...

### โทนของเรื่อง
[เปลี่ยนไปอย่างไรจาก patch ก่อน]
```

---

## ขั้นที่ 7 — แปล (AI layer)

เลือกใช้ตัวเลือกการแปลตามความสะดวก:

### ทางเลือกที่ A: ใช้ Local LLM ผ่าน Ollama (แนะนำหากไม่สะดวกใช้ API Key)
*ได้ผลลัพธ์คุณภาพสูง และสามารถอ่านเพื่อรักษาข้อมูล Character Voices และ Glossary ร่วมในการแปลได้*
1. ดาวน์โหลดและติดตั้ง [Ollama](https://ollama.com/) ในเครื่องของคุณ
2. ดาวน์โหลด Model ที่จะใช้ผ่าน Command Line (เช่น Qwen 2.5 หรือ Gemma 2):
   ```bash
   ollama pull qwen2.5:7b
   ```
3. รันสคริปต์ระบุ `--engine ollama` และ `--model` ที่ติดตั้ง:
   ```bash
   # ทดสอบ 5 quest เพื่อดูคุณภาพ
   python scripts/translate.py \
     --engine ollama \
     --model qwen2.5:7b \
     --input content/arr/<patch> \
     --output content_ai/arr/<patch> \
     --limit 5

   # แปลเต็มรูปแบบ (ลบ --limit)
   python scripts/translate.py \
     --engine ollama \
     --model qwen2.5:7b \
     --input content/arr/<patch> \
     --output content_ai/arr/<patch>
   ```

### ทางเลือกที่ B: ใช้ Google Translate (แปลฟรี ไม่มีขั้นตอนเพิ่มเติม)
*สะดวก รวดเร็ว และไม่ต้องใช้ API Key แต่ไม่รองรับการนำ Character Voices หรือ Glossary มาปรับแต่งการแปล*
   ```bash
   # ทดสอบ 5 quest
   python scripts/translate.py \
     --engine google-free \
     --input content/arr/<patch> \
     --output content_ai/arr/<patch> \
     --limit 5

   # แปลเต็มรูปแบบ (ลบ --limit)
   python scripts/translate.py \
     --engine google-free \
     --input content/arr/<patch> \
     --output content_ai/arr/<patch>
   ```

### ทางเลือกที่ C: ใช้ Claude API (ต้องใช้ API Key)
*แปลคุณภาพสูงที่สุด ผ่าน API ทางการ*
1. ตั้งค่า API Key ใน Terminal:
   - PowerShell: `$env:ANTHROPIC_API_KEY="your-api-key"`
   - Command Prompt: `set ANTHROPIC_API_KEY="your-api-key"`
   - Linux/macOS: `export ANTHROPIC_API_KEY="your-api-key"`
2. รันสคริปต์:
   ```bash
   # ทดสอบ 5 quest
   python scripts/translate.py \
     --engine claude \
     --input content/arr/<patch> \
     --output content_ai/arr/<patch> \
     --limit 5

   # แปลเต็มรูปแบบ (ลบ --limit)
   python scripts/translate.py \
     --engine claude \
     --input content/arr/<patch> \
     --output content_ai/arr/<patch>
   ```

---

## ขั้นที่ 8 — อัปเดต site

เปิด `site/src/lib/quests.ts` แก้ไข path ถ้าต้องการรองรับหลาย patch:

```typescript
// ปัจจุบัน hardcode 2.0 — ถ้าเพิ่ม 3.0 ต้องปรับ getAllQuests() ให้ scan หลาย patch
const BLUEPRINT_DIR = join(ROOT, 'content/arr/2.0');
```

จากนั้น build และ verify:

```bash
cd site && npm run build
npm run preview  # เปิด http://localhost:4321 ตรวจสอบ
```

---

## Checklist

```
[ ] 1. หา wiki URL ของ patch ใหม่
[ ] 2. รัน scraper → content/arr/<patch>/
[ ] 3. รัน analysis script → ดู characters และ proper nouns
[ ] 4. สร้าง character files สำหรับตัวละครใหม่ (> 50 lines)
[ ] 5. อัปเดต characters/glossary.md
[ ] 6. อัปเดต characters/context.md
[ ] 7. รัน translate.py --limit 5 เพื่อทดสอบ (เลือก --engine ollama หรือ google-free ได้ถ้าไม่มี API key)
[ ] 8. รัน translate.py เต็มรูปแบบตามตัวเลือกที่กำหนด
[ ] 9. npm run build ใน site/
[ ] 10. ตรวจสอบ site ด้วยตา ก่อน push
```


---

## โครงสร้างไฟล์อ้างอิง

```
ffxiv-msq-thai/
├── content/arr/<patch>/          ← blueprint (scraper output)
├── content_ai/arr/<patch>/       ← AI translations
├── content_contributor/arr/<patch>/  ← community translations
├── characters/
│   ├── TEMPLATE.md               ← copy เวลาสร้างตัวละครใหม่
│   ├── glossary.md               ← คำศัพท์มาตรฐาน (อัปเดตทุก patch)
│   ├── context.md                ← บริบทเรื่องราว (เพิ่ม section ทุก patch)
│   └── <character>.md            ← voice ของแต่ละตัวละคร
├── scripts/
│   ├── scrape.py                 ← ขั้นที่ 2
│   └── translate.py              ← ขั้นที่ 7
└── site/                         ← Astro static site
```

---

> ไฟล์นี้อยู่ที่ root ไม่ใช่ใน `characters/` เพราะ `characters/` ถูก inject เข้า AI system prompt
> ทุกครั้งที่แปล — playbook ไม่ใช่ context สำหรับ AI จึงไม่ควรอยู่ตรงนั้น
