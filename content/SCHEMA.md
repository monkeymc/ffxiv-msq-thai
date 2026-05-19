# Quest Content Schema

โครงสร้างแบ่งเป็น 3 layers — site merge ลำดับ: **contributor > ai > blueprint**

---

## Blueprint (`content/arr/<patch>/<quest-slug>.json`)

ข้อมูลดิบจาก scraper ภาษาอังกฤษล้วน — **ห้ามแก้ไขด้วยมือ**

```json
{
  "id": "quest-slug",
  "wiki_path": "/wiki/Quest_Name",
  "wiki_image_url": "https://ffxiv.consolegameswiki.com/mediawiki/images/...",
  "patch": "2.0",
  "title_en": "Quest Name",
  "level": "1",
  "location": "Zone Name",
  "npc_start": "NPC Name",
  "dialogues": [
    { "character": "Character Name", "text_en": "Original English text." }
  ]
}
```

---

## AI Layer (`content_ai/arr/<patch>/<quest-slug>.json`)

output จาก `translate.py` — **ห้ามแก้ด้วยมือ ให้ contributor override แทน**

```json
{
  "title_th": "ชื่อ Quest ภาษาไทย",
  "dialogues": [
    { "text_th": "คำแปลภาษาไทย" }
  ]
}
```

---

## Contributor Layer (`content_contributor/arr/<patch>/<quest-slug>.json`)

แปลโดยชุมชนผ่าน PR — **แสดงผลก่อน AI เสมอ**

```json
{
  "contributor": "github_username",
  "title_th": "ชื่อ Quest ภาษาไทย",
  "dialogues": [
    { "text_th": "คำแปลภาษาไทย" }
  ]
}
```

index ของ `dialogues` ต้องตรงกับ blueprint เสมอ

---

## หมายเหตุ

- `wiki_image_url`: hotlink จาก consolegameswiki โดยตรง ไม่ download
- `wiki_path`: path สัมพัทธ์จาก `https://ffxiv.consolegameswiki.com`
- Contributor ที่ต้องการ override เฉพาะบางบรรทัด ให้ใส่ `text_th: ""` สำหรับบรรทัดที่ไม่แปล (site จะ fallback ไป AI หรือ EN อัตโนมัติ)
