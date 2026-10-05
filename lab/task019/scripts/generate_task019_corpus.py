#!/usr/bin/env python3
"""TASK-019 Deterministic 128MiB Synthetic Corpus Generator.
Generates exactly 1024 physical RFC822 messages across 5 folders totaling approx 128 MiB.
Outputs:
- runtime/task019/stage-128m/source/eml-tree/
- runtime/task019/stage-128m/source/corpus.mbox
- runtime/task019/stage-128m/source/manifest.json
"""
import datetime
import email.message
import email.policy
import hashlib
import json
import os
import pathlib
import random
import re
import sys

SEED = 20260914

FOLDERS = [
    ("Gelen Kutusu", 400),
    ("Gönderilenler", 200),
    ("Projeler/Anadolu", 200),
    ("Arşiv/2026/Finans", 150),
    ("Müşteri İlişkileri/Talepler", 74),
]
TOTAL_ITEMS = sum(count for _, count in FOLDERS)  # Exactly 1024

TURKISH_I_TERMS = [
    "Isparta", "İzmir", "ışık", "işlem", "Diyarbakır", "İstanbul",
    "Iğdır", "ılık", "ilik", "İnşaat", "SIRALAMA", "sıkıştırma"
]

UNICODE_PHRASES = [
    ("ru", "Отчёт о реализации проекта и системные параметры"),
    ("el", "Έκθεση προόδου του έργου και τεχνικές προδιαγραφές"),
    ("de", "München Übergabe, Größe und Überprüfung der Bauteile"),
    ("ar", "تقرير المشروع السنوي وجداول الميزانية المعتمدة"),
    ("emoji", "Proje Durumu 🚀 Dosyalar 📁 Faturalar 🧾 Raporlar 📊"),
]

MBOXRD_ESCAPES = [
    "From Istanbul with love",
    ">From previous conversation notes",
    ">>From ancient archives",
    ">>>From deeply nested replies",
]

LARGE_PAYLOAD_MAP = {
    # 16 items distributed across folders:
    # Gelen Kutusu (~48 MiB target):
    50: 10 * 1024 * 1024,
    150: 6 * 1024 * 1024,
    250: 3 * 1024 * 1024,
    350: 3 * 1024 * 1024,
    55: 1500 * 1024,
    155: 1500 * 1024,
    # Gönderilenler (~24 MiB target):
    450: 6 * 1024 * 1024,
    550: 3 * 1024 * 1024,
    455: 1500 * 1024,
    # Projeler/Anadolu (~28 MiB target):
    650: 6 * 1024 * 1024,
    750: 5 * 1024 * 1024,
    670: 3 * 1024 * 1024,
    770: 2 * 1024 * 1024,
    # Arşiv/2026/Finans (~20 MiB target):
    850: 6 * 1024 * 1024,
    900: 3 * 1024 * 1024,
    # Müşteri İlişkileri/Talepler (~8 MiB target):
    960: 2 * 1024 * 1024,
}

LONG_BODY_ORDINALS = {
    # 8 text > 512 KiB, 8 html > 512 KiB
    60: 'text', 160: 'text', 260: 'text', 460: 'text',
    660: 'text', 760: 'text', 860: 'text', 965: 'text',
    65: 'html', 165: 'html', 265: 'html', 465: 'html',
    665: 'html', 765: 'html', 865: 'html', 970: 'html',
}

EXACT_DUPLICATE_PAIRS = [
    (40, 41), (110, 111), (480, 481),
    (630, 631), (820, 821), (980, 981)
]

CONTENT_DUPLICATE_PAIRS = [
    (70, 71), (500, 501), (640, 641), (840, 841)
]

SHARED_MID_PAIRS = [
    (120, 121), (710, 711)
]

MISSING_DATE_ORDINALS = set()


def deterministic_bytes(seed_int, size):
    """Generate deterministic byte buffer using simple PRNG."""
    prng = random.Random(seed_int)
    chunk = bytearray(prng.getrandbits(8) for _ in range(min(size, 65536)))
    if size <= 65536:
        return bytes(chunk[:size])
    repeats = size // len(chunk)
    rem = size % len(chunk)
    return bytes(chunk * repeats + chunk[:rem])


def format_mboxrd_record(raw_mime_bytes, envelope_date):
    """Format one raw MIME message as an MBOXRD record conforming to BridgeMimeBytePolicy."""
    layout_crlf = b"\r\n" in raw_mime_bytes
    newline = b"\r\n" if layout_crlf else b"\n"
    added = not raw_mime_bytes.endswith(b"\n")

    asctime_str = envelope_date.strftime("%a %b %d %H:%M:%S %Y")
    envelope = f"From bitigmail@archive.invalid {asctime_str}".encode("ascii")

    # Find body start
    hdr_sep_crlf = raw_mime_bytes.find(b"\r\n\r\n")
    hdr_sep_lf = raw_mime_bytes.find(b"\n\n")

    if hdr_sep_crlf >= 0 and (hdr_sep_lf < 0 or hdr_sep_crlf < hdr_sep_lf):
        body_start = hdr_sep_crlf + 4
    elif hdr_sep_lf >= 0:
        body_start = hdr_sep_lf + 2
    else:
        body_start = len(raw_mime_bytes)

    out = bytearray()
    out.extend(envelope)
    out.extend(newline)
    out.extend(raw_mime_bytes[:body_start])

    start = body_start
    while start < len(raw_mime_bytes):
        lf = raw_mime_bytes.find(b"\n", start)
        end = len(raw_mime_bytes) if lf < 0 else lf + 1
        marker = start
        while marker < end and raw_mime_bytes[marker] == ord(b">"):
            marker += 1
        if raw_mime_bytes[marker:end].startswith(b"From "):
            out.append(ord(b">"))
        out.extend(raw_mime_bytes[start:end])
        start = end

    if added:
        out.extend(newline)
    return bytes(out)


def build_message(ordinal, folder, rng):
    """Builds a deterministic email.message.EmailMessage and metadata."""
    msg = email.message.EmailMessage()
    prng = random.Random(SEED * 10000 + ordinal)

    sender_user = f"kullanici{ordinal % 50}"
    sender_domain = "posta.example" if ordinal % 2 == 0 else "sirket.example"
    sender_addr = f"{sender_user}@{sender_domain}"
    sender_name = f"Kullanıcı {ordinal % 50}"
    msg['From'] = f"{sender_name} <{sender_addr}>"

    recipient_user = f"alici{ordinal % 30}"
    recipient_domain = "destek.example" if ordinal % 3 == 0 else "muhendislik.example"
    msg['To'] = f"Alıcı {ordinal % 30} <{recipient_user}@{recipient_domain}>"

    # Date header
    if ordinal not in MISSING_DATE_ORDINALS:
        base_date = datetime.datetime(2023, 1, 1, 9, 0, 0, tzinfo=datetime.timezone.utc)
        delta_days = (ordinal * 1.3) % 1300
        msg_date = base_date + datetime.timedelta(days=delta_days, hours=(ordinal % 12), minutes=(ordinal % 60))
        if ordinal == 200:
            msg_date = datetime.datetime(2024, 2, 29, 23, 59, 59, tzinfo=datetime.timezone.utc)
        tz_offset = datetime.timezone(datetime.timedelta(hours=(ordinal % 5 - 1)))
        msg_date = msg_date.astimezone(tz_offset)
        msg['Date'] = msg_date.strftime("%a, %d %b %Y %H:%M:%S %z")
        date_iso = msg_date.astimezone(datetime.timezone.utc).isoformat()
    else:
        msg_date = datetime.datetime(2026, 9, 14, 0, 0, 0, tzinfo=datetime.timezone.utc)
        date_iso = None

    # Message-ID
    if ordinal in (120, 121):
        mid = "<shared-mid-rev1@posta.example>"
    elif ordinal in (710, 711):
        mid = "<shared-mid-rev2@projeler.example>"
    else:
        mid = f"<20260914-task019-{ordinal:04d}@posta.example>"
    msg['Message-ID'] = mid

    # Subject & Body inclusions
    search_matches = ["task019"]
    subject_parts = [f"İleti {ordinal:04d} - {folder}"]

    # Turkish I terms
    if ordinal < 120:
        term = TURKISH_I_TERMS[ordinal % len(TURKISH_I_TERMS)]
        subject_parts.append(f"Konu: {term}")
        search_matches.append(term.lower())

    # Unicode phrases
    if 120 <= ordinal < 200:
        lang, phrase = UNICODE_PHRASES[ordinal % len(UNICODE_PHRASES)]
        subject_parts.append(f"[{lang.upper()}] {phrase[:30]}")
        search_matches.append(lang)

    # Escapes
    if 200 <= ordinal < 224:
        esc = MBOXRD_ESCAPES[ordinal % len(MBOXRD_ESCAPES)]
        subject_parts.append("MBOXRD Deneme")

    subject = " | ".join(subject_parts)
    msg['Subject'] = subject

    # Attachments & payload
    attachments_meta = []
    is_truncated = False

    if ordinal in LARGE_PAYLOAD_MAP:
        payload_size = LARGE_PAYLOAD_MAP[ordinal]
        body_text = f"Büyük ikili veri eki içerir: {payload_size} bayt.\r\nKlasör: {folder}\r\nSıra: {ordinal}\r\n"
        msg.set_content(body_text)
        att_data = deterministic_bytes(ordinal + 777, payload_size)
        att_name = f"buyuk_veri_{ordinal}_{payload_size // (1024*1024)}mb.bin"
        msg.add_attachment(att_data, maintype="application", subtype="octet-stream", filename=att_name)
        att_hash = hashlib.sha256(att_data).hexdigest()
        attachments_meta.append({
            "fileName": att_name,
            "sizeBytes": len(att_data),
            "sha256": att_hash,
            "isInline": False,
            "contentId": None
        })
    elif ordinal in LONG_BODY_ORDINALS:
        kind = LONG_BODY_ORDINALS[ordinal]
        is_truncated = True
        # Target > 512 KiB (e.g. ~750 KiB body)
        repeat_count = 6000
        line_template = f"Sıra {ordinal:04d} uzun gövde satırı deneme metni Türkçe karakterler: çığıöşü İŞLEM SIRALAMA Isparta İzmir.\r\n"
        full_text = line_template * repeat_count
        if kind == 'text':
            msg.set_content(full_text)
        else:
            html_content = f"<html><body><h2>Tablo {ordinal}</h2><table border='1'>" + \
                           "".join(f"<tr><td>Hücre {i}</td><td>{line_template.strip()}</td></tr>" for i in range(5000)) + \
                           "</table></body></html>"
            msg.set_content(f"HTML tablosu içerir: {ordinal}")
            msg.add_alternative(html_content, subtype='html')
    elif 220 <= ordinal < 520:
        # Standard attachment spectrum
        body_text = f"İşlem bildirimi e-postası.\r\nKlasör: {folder}\r\nÖğe sıra: {ordinal}\r\n"
        if ordinal % 4 == 0:
            # Text attachment
            att_text = (f"Şartname ve teknik gereksinim özeti - Sıra {ordinal}\r\n" + ("Madde 1: Deterministik test verisi.\r\n" * 50)).encode('utf-8')
            att_name = f"sartname_ozeti_{ordinal}.txt"
            msg.set_content(body_text)
            msg.add_attachment(att_text, maintype="text", subtype="plain", filename=att_name)
            attachments_meta.append({
                "fileName": att_name,
                "sizeBytes": len(att_text),
                "sha256": hashlib.sha256(att_text).hexdigest(),
                "isInline": False,
                "contentId": None
            })
            search_matches.append("sartname")
        elif ordinal % 4 == 1:
            # PNG image
            png_header = b"\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x01\x00\x00\x00\x01\x00\x08\x06\x00\x00\x00"
            att_data = png_header + deterministic_bytes(ordinal, 150 * 1024)
            att_name = f"grafik_{ordinal}.png"
            msg.set_content(body_text)
            msg.add_attachment(att_data, maintype="image", subtype="png", filename=att_name)
            attachments_meta.append({
                "fileName": att_name,
                "sizeBytes": len(att_data),
                "sha256": hashlib.sha256(att_data).hexdigest(),
                "isInline": False,
                "contentId": None
            })
        elif ordinal % 4 == 2:
            # Inline CID image
            cid = f"<proje_logo_{ordinal}@posta.example>"
            html_body = f"<html><body><p>{body_text}</p><img src='cid:proje_logo_{ordinal}@posta.example'/></body></html>"
            png_header = b"\x89PNG\r\n\x1a\n"
            att_data = png_header + deterministic_bytes(ordinal, 40 * 1024)
            msg.set_content(body_text)
            msg.add_alternative(html_body, subtype='html')
            # Inline attachment
            sub = msg.get_payload(1) if msg.is_multipart() else msg
            msg.add_attachment(att_data, maintype="image", subtype="png", filename=f"logo_{ordinal}.png", cid=cid)
            attachments_meta.append({
                "fileName": f"logo_{ordinal}.png",
                "sizeBytes": len(att_data),
                "sha256": hashlib.sha256(att_data).hexdigest(),
                "isInline": True,
                "contentId": cid
            })
        else:
            # PDF file
            pdf_header = b"%PDF-1.5\r\n%\xe2\xe3\xcf\xd3\r\n"
            att_data = pdf_header + deterministic_bytes(ordinal, 80 * 1024)
            att_name = f"dokuman_{ordinal}.pdf"
            msg.set_content(body_text)
            msg.add_attachment(att_data, maintype="application", subtype="pdf", filename=att_name)
            attachments_meta.append({
                "fileName": att_name,
                "sizeBytes": len(att_data),
                "sha256": hashlib.sha256(att_data).hexdigest(),
                "isInline": False,
                "contentId": None
            })
    else:
        # Standard operational correspondence
        body_lines = [
            f"Sayın Yetkili,",
            f"Bu e-posta {folder} klasörü altındaki standart iş akışını temsil eder (Sıra {ordinal}).",
            f"Sistem kayıt tarihi: 2026-09-14.",
            f"Detaylı bilgi için teknik şartnameye ve proje dosyalarına başvurunuz.",
        ]
        if 200 <= ordinal < 224:
            body_lines.append(MBOXRD_ESCAPES[ordinal % len(MBOXRD_ESCAPES)])
            body_lines.append("From another paragraph starting with From")
        body_text = "\r\n".join(body_lines) + "\r\n"
        msg.set_content(body_text)

    # Export bytes with pure CRLF policy
    raw_bytes = msg.as_bytes(policy=email.policy.SMTP)

    meta = {
        "ordinal": ordinal,
        "folder": folder,
        "senderAddress": sender_addr,
        "senderDisplay": sender_name,
        "subject": subject,
        "dateUtc": date_iso,
        "messageIdHeader": mid,
        "attachmentCount": len(attachments_meta),
        "attachments": attachments_meta,
        "isBodyTruncated": is_truncated,
        "expectedSearchMatches": sorted(list(set(search_matches)))
    }
    return raw_bytes, meta, msg_date


def generate_corpus(repo_root):
    """Generates the full stage-128m corpus deterministically."""
    source_dir = pathlib.Path(repo_root) / "runtime" / "task019" / "stage-128m" / "source"
    eml_tree_dir = source_dir / "eml-tree"
    mbox_file = source_dir / "corpus.mbox"
    manifest_file = source_dir / "manifest.json"

    source_dir.mkdir(parents=True, exist_ok=True)
    eml_tree_dir.mkdir(parents=True, exist_ok=True)

    rng = random.Random(SEED)
    print(f"Generating 1,024 synthetic RFC822 messages (Seed: {SEED})...")

    # Map ordinals to folders
    folder_assignments = []
    curr_ord = 0
    for folder_name, count in FOLDERS:
        for _ in range(count):
            folder_assignments.append((curr_ord, folder_name))
            curr_ord += 1

    messages_meta = []
    folder_stats = {name: {"itemCount": 0, "rawSizeBytes": 0} for name, _ in FOLDERS}
    mbox_records = []
    generated_raw = {}
    envelope_dates = {}

    for ordinal, folder in folder_assignments:
        raw_bytes, meta, envelope_date = build_message(ordinal, folder, rng)
        generated_raw[ordinal] = raw_bytes
        envelope_dates[ordinal] = envelope_date
        messages_meta.append(meta)

    # Apply exact duplicates
    for src_ord, dst_ord in EXACT_DUPLICATE_PAIRS:
        generated_raw[dst_ord] = generated_raw[src_ord]
        messages_meta[dst_ord]["messageIdHeader"] = messages_meta[src_ord]["messageIdHeader"]
        messages_meta[dst_ord]["subject"] = messages_meta[src_ord]["subject"]
        messages_meta[dst_ord]["attachmentCount"] = messages_meta[src_ord]["attachmentCount"]
        messages_meta[dst_ord]["attachments"] = messages_meta[src_ord]["attachments"]
        messages_meta[dst_ord]["isBodyTruncated"] = messages_meta[src_ord]["isBodyTruncated"]
        messages_meta[dst_ord]["expectedSearchMatches"] = messages_meta[src_ord]["expectedSearchMatches"]
        envelope_dates[dst_ord] = envelope_dates[src_ord]

    # Write EML files & collect mbox records
    total_raw_bytes = 0
    item_hashes = []

    for ordinal, folder in folder_assignments:
        folder_disk_path = eml_tree_dir / pathlib.Path(folder)
        folder_disk_path.mkdir(parents=True, exist_ok=True)

        eml_filename = f"msg_{ordinal:04d}.eml"
        eml_file_path = folder_disk_path / eml_filename
        raw_data = generated_raw[ordinal]

        eml_file_path.write_bytes(raw_data)
        file_sha256 = hashlib.sha256(raw_data).hexdigest()
        item_hashes.append(file_sha256)

        total_raw_bytes += len(raw_data)
        folder_stats[folder]["itemCount"] += 1
        folder_stats[folder]["rawSizeBytes"] += len(raw_data)

        meta = messages_meta[ordinal]
        meta["relativePath"] = f"{folder}/{eml_filename}"
        meta["rawSizeBytes"] = len(raw_data)
        meta["rawSha256"] = file_sha256

        # Format MBOX record
        mbox_rec = format_mboxrd_record(raw_data, envelope_dates[ordinal])
        mbox_records.append(mbox_rec)

    # Write unified corpus.mbox
    print("Writing unified corpus.mbox...")
    with mbox_file.open("wb") as f_mbox:
        for rec in mbox_records:
            f_mbox.write(rec)

    aggregate_fingerprint = hashlib.sha256("".join(item_hashes).encode("ascii")).hexdigest()

    # Search query oracles
    turkish_queries = [
        {"query": "isparta", "expectedMatchingOrdinals": [m["ordinal"] for m in messages_meta if "isparta" in m["expectedSearchMatches"]]},
        {"query": "izmir", "expectedMatchingOrdinals": [m["ordinal"] for m in messages_meta if "izmir" in m["expectedSearchMatches"]]},
        {"query": "işlem", "expectedMatchingOrdinals": [m["ordinal"] for m in messages_meta if "işlem" in m["expectedSearchMatches"]]},
        {"query": "diyarbakır", "expectedMatchingOrdinals": [m["ordinal"] for m in messages_meta if "diyarbakır" in m["expectedSearchMatches"]]},
        {"query": "sıkıştırma", "expectedMatchingOrdinals": [m["ordinal"] for m in messages_meta if "sıkıştırma" in m["expectedSearchMatches"]]},
    ]

    manifest = {
        "generatorSeed": SEED,
        "generatedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "totalItems": TOTAL_ITEMS,
        "totalRawSizeBytes": total_raw_bytes,
        "aggregateFingerprint": aggregate_fingerprint,
        "folders": folder_stats,
        "messages": messages_meta,
        "searchOracles": {
            "turkishIQueries": turkish_queries,
            "exactDuplicatePairs": EXACT_DUPLICATE_PAIRS,
            "contentDuplicatePairs": CONTENT_DUPLICATE_PAIRS,
            "sharedMidPairs": SHARED_MID_PAIRS
        }
    }

    manifest_file.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Generation complete! Total items: {TOTAL_ITEMS}, Total raw bytes: {total_raw_bytes} ({total_raw_bytes / (1024*1024):.2f} MiB)")
    return manifest


if __name__ == "__main__":
    root = pathlib.Path(__file__).resolve().parents[3]
    generate_corpus(root)
