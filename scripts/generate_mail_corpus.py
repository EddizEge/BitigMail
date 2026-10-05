#!/usr/bin/env python3
"""
scripts/generate_mail_corpus.py

Deterministic synthetic email corpus generator and validator for Mail Manager (TASK-007).
Uses Python standard library only (no third-party dependencies).

Features:
- Exactly 12 physical EML files across 3 logical folders:
    * Gelen Kutusu (5 messages)
    * Gönderilenler (3 messages)
    * Projeler/İstanbul (4 messages)
- 11 unique raw contents: msg-01 and msg-02 are a byte-identical duplicate pair.
- msg-03 and msg-04 share the same Message-ID (<ortak-kimlik-2024-revizyon@projeler.example>)
  with different content, dates, and subjects to prove Message-ID alone is insufficient.
- Turkish Unicode in subjects, bodies, and folder names.
- All email addresses strictly under '.example'.
- Dates span 2022 to 2025 with 3 distinct timezone offsets (+03:00, +00:00, +02:00).
- Static safe HTML (no scripts, no external images, no tracking pixels).
- 4 attachment occurrences across 4 messages:
    1. Text attachment with UTF-8 Turkish filename (şartname_özeti_2024.txt)
    2. Deterministic binary payload (sistem_parametreleri.bin)
    3. Valid in-memory PNG attachment (rapor_grafik.png)
    4. Multipart/related inline PNG with CID reference (logo.png, Content-ID: <proje_logo_cid>)
- msg-08 contains lines beginning with 'From ' and '>From ' to demonstrate MBOX mboxrd
  escaping and unescaping semantics.
- Generates corpus.mbox using explicit mboxrd escaping across mixed CRLF/LF line representations
  and manifest.json containing full metadata, attachment hashes, and hard-coded filter oracles.
- Validates the generated corpus automatically via 13 sequential checks (EML MIME defects,
  hashes, duplicates, shared IDs, folder distribution, attachments, msg-11 UTC boundary,
  filter oracles, raw MBOX byte-level escaping, 12-message MBOX parsing, single unescape
  accounting preserving exact originals, MBOX attachment semantic hashes, and payload size < 1MB)
  and exits with code 0 on success or 1 on failure.
"""

import argparse
import base64
from datetime import datetime, timezone
import email
from email.header import Header
import email.policy
import hashlib
import json
import mailbox
import os
from pathlib import Path
import re
import struct
import sys
import urllib.parse
import zlib


def create_png_bytes(width: int = 4, height: int = 4, r: int = 30, g: int = 144, b: int = 255) -> bytes:
    """Generate a minimal, valid 8-bit RGB PNG file deterministically in memory."""
    signature = b'\x89PNG\r\n\x1a\n'
    
    # IHDR Chunk
    ihdr_data = struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)
    ihdr_crc = struct.pack('>I', zlib.crc32(b'IHDR' + ihdr_data) & 0xffffffff)
    ihdr = struct.pack('>I', len(ihdr_data)) + b'IHDR' + ihdr_data + ihdr_crc
    
    # IDAT Chunk (filter byte 0 + width * 3 RGB bytes per row)
    row = b'\x00' + bytes([r, g, b] * width)
    raw_image = row * height
    compressed = zlib.compress(raw_image, 9)
    idat_crc = struct.pack('>I', zlib.crc32(b'IDAT' + compressed) & 0xffffffff)
    idat = struct.pack('>I', len(compressed)) + b'IDAT' + compressed + idat_crc
    
    # IEND Chunk
    iend_crc = struct.pack('>I', zlib.crc32(b'IEND') & 0xffffffff)
    iend = struct.pack('>I', 0) + b'IEND' + iend_crc
    
    return signature + ihdr + idat + iend


def format_rfc2047(text: str) -> str:
    """Format string with RFC 2047 encoded-word syntax if non-ASCII."""
    try:
        text.encode('ascii')
        return text
    except UnicodeEncodeError:
        return Header(text, 'utf-8').encode()


def get_attachment_definitions():
    """Return deterministic attachment payloads and metadata."""
    txt_content = (
        "T.C. Proje İhale Şartnamesi Özeti\n"
        "1. Amaç ve Kapsam: İletişim altyapısının modernizasyonu.\n"
        "2. Teknik Koşullar: Kesintisiz veri iletimi ve arşivleme.\n"
        "3. Güvenlik: Uçtan uca şifreleme ve bütünlük doğrulaması.\n"
    ).encode('utf-8')

    bin_content = bytes(range(256))
    png_report = create_png_bytes(width=4, height=4, r=30, g=144, b=255)
    png_logo = create_png_bytes(width=4, height=4, r=220, g=20, b=60)

    return {
        'txt_sartname': {
            'filename': 'şartname_özeti_2024.txt',
            'content_type': 'text/plain; charset=utf-8',
            'data': txt_content,
            'is_inline': False,
            'content_id': None,
        },
        'bin_params': {
            'filename': 'sistem_parametreleri.bin',
            'content_type': 'application/octet-stream',
            'data': bin_content,
            'is_inline': False,
            'content_id': None,
        },
        'png_grafik': {
            'filename': 'rapor_grafik.png',
            'content_type': 'image/png',
            'data': png_report,
            'is_inline': False,
            'content_id': None,
        },
        'png_logo': {
            'filename': 'logo.png',
            'content_type': 'image/png',
            'data': png_logo,
            'is_inline': True,
            'content_id': '<proje_logo_cid>',
        }
    }


def get_corpus_spec():
    """Return specifications for the 12 corpus messages."""
    atts = get_attachment_definitions()

    spec = [
        {
            'fixture_id': 'msg-01',
            'eml_rel_path': 'eml/msg-01.eml',
            'folder': 'Gelen Kutusu',
            'message_id': '<msg-2023-01-hosgeldiniz@posta.example>',
            'date_iso': '2023-04-10T09:00:00+03:00',
            'date_rfc': 'Mon, 10 Apr 2023 09:00:00 +0300',
            'envelope_sender': 'ahmet.yilmaz@posta.example',
            'envelope_date': 'Mon Apr 10 09:00:00 2023',
            'from_name': 'Ahmet Yılmaz',
            'from_addr': 'ahmet.yilmaz@posta.example',
            'to_name': 'Mehmet Kaya',
            'to_addr': 'mehmet.kaya@destek.example',
            'subject': 'Sisteme Hoş Geldiniz - Bilgilendirme',
            'body_text': (
                "Merhaba Mehmet Bey,\n\n"
                "Sisteme hoş geldiniz. Kaydınız başarıyla tamamlanmıştır.\n"
                "Herhangi bir sorunuz olursa lütfen bu adrese yanıt veriniz.\n\n"
                "İyi çalışmalar,\n"
                "Ahmet Yılmaz"
            ),
            'body_html': (
                "<p>Merhaba Mehmet Bey,</p>"
                "<p>Sisteme hoş geldiniz. Kaydınız başarıyla tamamlanmıştır.</p>"
                "<p>Herhangi bir sorunuz olursa lütfen bu adrese yanıt veriniz.</p>"
                "<p>İyi çalışmalar,<br>Ahmet Yılmaz</p>"
            ),
            'attachments': [],
            'is_duplicate': False,
            'duplicate_of': None,
            'same_msg_id_diff_content': False,
            'shared_msg_id_with': None,
        },
        {
            'fixture_id': 'msg-02',
            'eml_rel_path': 'eml/msg-02.eml',
            'folder': 'Gelen Kutusu',
            'message_id': '<msg-2023-01-hosgeldiniz@posta.example>',
            'date_iso': '2023-04-10T09:00:00+03:00',
            'date_rfc': 'Mon, 10 Apr 2023 09:00:00 +0300',
            'envelope_sender': 'ahmet.yilmaz@posta.example',
            'envelope_date': 'Mon Apr 10 09:00:00 2023',
            'from_name': 'Ahmet Yılmaz',
            'from_addr': 'ahmet.yilmaz@posta.example',
            'to_name': 'Mehmet Kaya',
            'to_addr': 'mehmet.kaya@destek.example',
            'subject': 'Sisteme Hoş Geldiniz - Bilgilendirme',
            'body_text': (
                "Merhaba Mehmet Bey,\n\n"
                "Sisteme hoş geldiniz. Kaydınız başarıyla tamamlanmıştır.\n"
                "Herhangi bir sorunuz olursa lütfen bu adrese yanıt veriniz.\n\n"
                "İyi çalışmalar,\n"
                "Ahmet Yılmaz"
            ),
            'body_html': (
                "<p>Merhaba Mehmet Bey,</p>"
                "<p>Sisteme hoş geldiniz. Kaydınız başarıyla tamamlanmıştır.</p>"
                "<p>Herhangi bir sorunuz olursa lütfen bu adrese yanıt veriniz.</p>"
                "<p>İyi çalışmalar,<br>Ahmet Yılmaz</p>"
            ),
            'attachments': [],
            'is_duplicate': True,
            'duplicate_of': 'msg-01',
            'same_msg_id_diff_content': False,
            'shared_msg_id_with': None,
        },
        {
            'fixture_id': 'msg-03',
            'eml_rel_path': 'eml/msg-03.eml',
            'folder': 'Projeler/İstanbul',
            'message_id': '<ortak-kimlik-2024-revizyon@projeler.example>',
            'date_iso': '2024-02-15T14:30:00+03:00',
            'date_rfc': 'Thu, 15 Feb 2024 14:30:00 +0300',
            'envelope_sender': 'canan.ozkan@muhendislik.example',
            'envelope_date': 'Thu Feb 15 14:30:00 2024',
            'from_name': 'Canan Özkan',
            'from_addr': 'canan.ozkan@muhendislik.example',
            'to_name': 'Proje Grubu',
            'to_addr': 'proje.ekibi@sirket.example',
            'subject': 'İstanbul Saha Raporu - İlk Taslak',
            'body_text': (
                "Merhabalar,\n\n"
                "İstanbul saha çalışmasına ait ilk taslak rapor hazırlandı. İncelenmesini rica ederim.\n\n"
                "Saygılarımla,\n"
                "Canan Özkan"
            ),
            'body_html': (
                "<p>Merhabalar,</p>"
                "<p>İstanbul saha çalışmasına ait ilk taslak rapor hazırlandı. İncelenmesini rica ederim.</p>"
                "<p>Saygılarımla,<br>Canan Özkan</p>"
            ),
            'attachments': [],
            'is_duplicate': False,
            'duplicate_of': None,
            'same_msg_id_diff_content': True,
            'shared_msg_id_with': 'msg-04',
        },
        {
            'fixture_id': 'msg-04',
            'eml_rel_path': 'eml/msg-04.eml',
            'folder': 'Projeler/İstanbul',
            'message_id': '<ortak-kimlik-2024-revizyon@projeler.example>',
            'date_iso': '2024-02-16T10:15:00+03:00',
            'date_rfc': 'Fri, 16 Feb 2024 10:15:00 +0300',
            'envelope_sender': 'burak.aktas@lojistik.example',
            'envelope_date': 'Fri Feb 16 10:15:00 2024',
            'from_name': 'Burak Aktaş',
            'from_addr': 'burak.aktas@lojistik.example',
            'to_name': 'Proje Grubu',
            'to_addr': 'proje.ekibi@sirket.example',
            'subject': 'İstanbul Saha Raporu - Güncellenmiş İkinci Taslak',
            'body_text': (
                "Merhaba,\n\n"
                "Dünkü toplantıda konuşulan revizyonlar doğrultusunda metin güncellendi. "
                "Ek bütçe kalemleri eklendi.\n\n"
                "İyi günler,\n"
                "Burak Aktaş"
            ),
            'body_html': (
                "<p>Merhaba,</p>"
                "<p>Dünkü toplantıda konuşulan revizyonlar doğrultusunda metin güncellendi. "
                "Ek bütçe kalemleri eklendi.</p>"
                "<p>İyi günler,<br>Burak Aktaş</p>"
            ),
            'attachments': [],
            'is_duplicate': False,
            'duplicate_of': None,
            'same_msg_id_diff_content': True,
            'shared_msg_id_with': 'msg-03',
        },
        {
            'fixture_id': 'msg-05',
            'eml_rel_path': 'eml/msg-05.eml',
            'folder': 'Gelen Kutusu',
            'message_id': '<msg-2024-05-sartname@destek.example>',
            'date_iso': '2024-05-20T11:00:00+03:00',
            'date_rfc': 'Mon, 20 May 2024 11:00:00 +0300',
            'envelope_sender': 'ayse.demir@sirket.example',
            'envelope_date': 'Mon May 20 11:00:00 2024',
            'from_name': 'Ayşe Demir',
            'from_addr': 'ayse.demir@sirket.example',
            'to_name': 'Ahmet Yılmaz',
            'to_addr': 'ahmet.yilmaz@posta.example',
            'subject': 'İhale Şartnamesi Özeti ve Ek Dosya',
            'body_text': (
                "Ahmet Bey,\n\n"
                "İhale şartname özeti ekte yer almaktadır. Türkçe karakter içeren metin dosyasıdır.\n\n"
                "Selamlar,\n"
                "Ayşe Demir"
            ),
            'body_html': (
                "<p>Ahmet Bey,</p>"
                "<p>İhale şartname özeti ekte yer almaktadır. Türkçe karakter içeren metin dosyasıdır.</p>"
                "<p>Selamlar,<br>Ayşe Demir</p>"
            ),
            'attachments': [atts['txt_sartname']],
            'is_duplicate': False,
            'duplicate_of': None,
            'same_msg_id_diff_content': False,
            'shared_msg_id_with': None,
        },
        {
            'fixture_id': 'msg-06',
            'eml_rel_path': 'eml/msg-06.eml',
            'folder': 'Gelen Kutusu',
            'message_id': '<msg-2024-06-ikili-ek@muhendislik.example>',
            'date_iso': '2024-06-12T16:45:00+00:00',
            'date_rfc': 'Wed, 12 Jun 2024 16:45:00 +0000',
            'envelope_sender': 'mehmet.kaya@destek.example',
            'envelope_date': 'Wed Jun 12 16:45:00 2024',
            'from_name': 'Mehmet Kaya',
            'from_addr': 'mehmet.kaya@destek.example',
            'to_name': 'Canan Özkan',
            'to_addr': 'canan.ozkan@muhendislik.example',
            'subject': 'Deterministik İkili Veri Dosyası (Sistem Parametreleri)',
            'body_text': (
                "Canan Hanım,\n\n"
                "Test için üretilen 256 baytlık deterministik ikili test dosyasını iletiyorum.\n\n"
                "İyi çalışmalar,\n"
                "Mehmet Kaya"
            ),
            'body_html': None,
            'attachments': [atts['bin_params']],
            'is_duplicate': False,
            'duplicate_of': None,
            'same_msg_id_diff_content': False,
            'shared_msg_id_with': None,
        },
        {
            'fixture_id': 'msg-07',
            'eml_rel_path': 'eml/msg-07.eml',
            'folder': 'Gelen Kutusu',
            'message_id': '<msg-2024-07-png-ek@lojistik.example>',
            'date_iso': '2024-07-08T08:30:00+02:00',
            'date_rfc': 'Mon, 08 Jul 2024 08:30:00 +0200',
            'envelope_sender': 'burak.aktas@lojistik.example',
            'envelope_date': 'Mon Jul 08 08:30:00 2024',
            'from_name': 'Burak Aktaş',
            'from_addr': 'burak.aktas@lojistik.example',
            'to_name': 'Ayşe Demir',
            'to_addr': 'ayse.demir@sirket.example',
            'subject': 'Aylık Dağıtım Raporu ve Grafik',
            'body_text': (
                "Ayşe Hanım,\n\n"
                "Aylık dağıtım raporuna ait özet grafik PNG biçiminde eklenmiştir.\n\n"
                "Saygılarımla,\n"
                "Burak Aktaş"
            ),
            'body_html': None,
            'attachments': [atts['png_grafik']],
            'is_duplicate': False,
            'duplicate_of': None,
            'same_msg_id_diff_content': False,
            'shared_msg_id_with': None,
        },
        {
            'fixture_id': 'msg-08',
            'eml_rel_path': 'eml/msg-08.eml',
            'folder': 'Gönderilenler',
            'message_id': '<msg-2024-08-mbox-kacis@posta.example>',
            'date_iso': '2024-08-25T13:20:00+03:00',
            'date_rfc': 'Sun, 25 Aug 2024 13:20:00 +0300',
            'envelope_sender': 'ahmet.yilmaz@posta.example',
            'envelope_date': 'Sun Aug 25 13:20:00 2024',
            'from_name': 'Ahmet Yılmaz',
            'from_addr': 'ahmet.yilmaz@posta.example',
            'to_name': 'Mühendislik Ekibi',
            'to_addr': 'ekip@muhendislik.example',
            'subject': 'MBOX Kaçış Kuralları ve Alıntı Denemesi',
            'body_text': (
                "Ekip merhaba,\n\n"
                "Aşağıda alıntılanan e-posta satırları MBOX mboxrd kaçış semantiğini doğrulamak için hazırlanmıştır:\n\n"
                "From Ahmet Yılmaz <ahmet.yilmaz@posta.example>\n"
                "Tarih: 2024-08-24 10:00\n\n"
                ">From Canan Özkan <canan.ozkan@muhendislik.example>\n"
                ">Tarih: 2024-08-23 15:30\n\n"
                "Bu satırların başındaki 'From ' ve '>From ' ifadeleri mboxrd standartlarında dönüştürülmelidir.\n"
                "Selamlar,\n"
                "Ahmet"
            ),
            'body_html': (
                "<p>Ekip merhaba,</p>"
                "<p>Aşağıda alıntılanan e-posta satırları MBOX mboxrd kaçış semantiğini doğrulamak için hazırlanmıştır:</p>"
                "<pre>From Ahmet Yılmaz &lt;ahmet.yilmaz@posta.example&gt;&#10;Tarih: 2024-08-24 10:00&#10;&#10;"
                "&gt;From Canan Özkan &lt;canan.ozkan@muhendislik.example&gt;&#10;&gt;Tarih: 2024-08-23 15:30</pre>"
                "<p>Bu satırların başındaki 'From ' ve '&gt;From ' ifadeleri mboxrd standartlarında dönüştürülmelidir.<br>"
                "Selamlar,<br>Ahmet</p>"
            ),
            'attachments': [],
            'is_duplicate': False,
            'duplicate_of': None,
            'same_msg_id_diff_content': False,
            'shared_msg_id_with': None,
        },
        {
            'fixture_id': 'msg-09',
            'eml_rel_path': 'eml/msg-09.eml',
            'folder': 'Gönderilenler',
            'message_id': '<msg-2022-09-arsiv@posta.example>',
            'date_iso': '2022-11-05T15:00:00+03:00',
            'date_rfc': 'Sat, 05 Nov 2022 15:00:00 +0300',
            'envelope_sender': 'ahmet.yilmaz@posta.example',
            'envelope_date': 'Sat Nov 05 15:00:00 2022',
            'from_name': 'Ahmet Yılmaz',
            'from_addr': 'ahmet.yilmaz@posta.example',
            'to_name': 'Mehmet Kaya',
            'to_addr': 'mehmet.kaya@destek.example',
            'subject': 'Eski Arşiv Kaydı - 2022 Yılı Bilgilendirmesi',
            'body_text': (
                "Mehmet Bey,\n\n"
                "2022 yılına ait eski arşiv kaydı kontrol amacıyla saklanmaktadır.\n\n"
                "İyi çalışmalar,\n"
                "Ahmet Yılmaz"
            ),
            'body_html': None,
            'attachments': [],
            'is_duplicate': False,
            'duplicate_of': None,
            'same_msg_id_diff_content': False,
            'shared_msg_id_with': None,
        },
        {
            'fixture_id': 'msg-10',
            'eml_rel_path': 'eml/msg-10.eml',
            'folder': 'Gönderilenler',
            'message_id': '<msg-2025-10-planlama@posta.example>',
            'date_iso': '2025-01-15T11:30:00+03:00',
            'date_rfc': 'Wed, 15 Jan 2025 11:30:00 +0300',
            'envelope_sender': 'ahmet.yilmaz@posta.example',
            'envelope_date': 'Wed Jan 15 11:30:00 2025',
            'from_name': 'Ahmet Yılmaz',
            'from_addr': 'ahmet.yilmaz@posta.example',
            'to_name': 'Canan Özkan',
            'to_addr': 'canan.ozkan@muhendislik.example',
            'subject': '2025 Yılı İlk Çeyrek Hedefleri',
            'body_text': (
                "Canan Hanım,\n\n"
                "2025 yılı hedeflerimiz için toplantı gündemi belirlenmiştir.\n\n"
                "Başarılar,\n"
                "Ahmet Yılmaz"
            ),
            'body_html': None,
            'attachments': [],
            'is_duplicate': False,
            'duplicate_of': None,
            'same_msg_id_diff_content': False,
            'shared_msg_id_with': None,
        },
        {
            'fixture_id': 'msg-11',
            'eml_rel_path': 'eml/msg-11.eml',
            'folder': 'Projeler/İstanbul',
            'message_id': '<msg-2024-11-inline-cid@projeler.example>',
            'date_iso': '2024-01-01T00:30:00+03:00',
            'date_rfc': 'Mon, 01 Jan 2024 00:30:00 +0300',
            'envelope_sender': 'canan.ozkan@muhendislik.example',
            'envelope_date': 'Mon Jan 01 00:30:00 2024',
            'from_name': 'Canan Özkan',
            'from_addr': 'canan.ozkan@muhendislik.example',
            'to_name': 'Ahmet Yılmaz',
            'to_addr': 'ahmet.yilmaz@posta.example',
            'subject': 'İstanbul Projesi Kurumsal Kimlik ve Amblem',
            'body_text': (
                "Ahmet Bey,\n\n"
                "Proje kurumsal kimliği için onaylanan logo aşağıda sunulmuştur.\n"
                "[Gömülü Görsel: logo.png]\n\n"
                "İyi çalışmalar,\n"
                "Canan Özkan"
            ),
            'body_html': (
                "<p>Ahmet Bey,</p>"
                "<p>Proje kurumsal kimliği için onaylanan logo aşağıda sunulmuştur:</p>"
                '<p><img src="cid:proje_logo_cid" alt="İstanbul Proje Logosu"></p>'
                "<p>İyi çalışmalar,<br>Canan Özkan</p>"
            ),
            'attachments': [atts['png_logo']],
            'is_duplicate': False,
            'duplicate_of': None,
            'same_msg_id_diff_content': False,
            'shared_msg_id_with': None,
        },
        {
            'fixture_id': 'msg-12',
            'eml_rel_path': 'eml/msg-12.eml',
            'folder': 'Projeler/İstanbul',
            'message_id': '<msg-2024-12-saha-kapanis@projeler.example>',
            'date_iso': '2024-11-30T10:00:00+00:00',
            'date_rfc': 'Sat, 30 Nov 2024 10:00:00 +0000',
            'envelope_sender': 'burak.aktas@lojistik.example',
            'envelope_date': 'Sat Nov 30 10:00:00 2024',
            'from_name': 'Burak Aktaş',
            'from_addr': 'burak.aktas@lojistik.example',
            'to_name': 'Proje Grubu',
            'to_addr': 'proje.ekibi@sirket.example',
            'subject': 'İstanbul Saha Çalışmaları Tamamlanma Tutanağı',
            'body_text': (
                "Değerli Çalışma Arkadaşlarımız,\n\n"
                "İstanbul saha çalışmalarımız belirlenen takvime uygun biçimde tamamlanmıştır.\n"
                "Tüm paydaşlarımıza teşekkür ederiz.\n\n"
                "Lojistik Koordinatörlüğü"
            ),
            'body_html': (
                "<p>Değerli Çalışma Arkadaşlarımız,</p>"
                "<p>İstanbul saha çalışmalarımız belirlenen takvime uygun biçimde tamamlanmıştır.<br>"
                "Tüm paydaşlarımıza teşekkür ederiz.</p>"
                "<p><strong>Lojistik Koordinatörlüğü</strong></p>"
            ),
            'attachments': [],
            'is_duplicate': False,
            'duplicate_of': None,
            'same_msg_id_diff_content': False,
            'shared_msg_id_with': None,
        },
    ]

    return spec


def build_eml_bytes(item: dict) -> bytes:
    """Build deterministic RFC 5322 MIME EML bytes for a corpus item."""
    from_hdr = f"{format_rfc2047(item['from_name'])} <{item['from_addr']}>"
    to_hdr = f"{format_rfc2047(item['to_name'])} <{item['to_addr']}>"
    subject_hdr = format_rfc2047(item['subject'])
    date_hdr = item['date_rfc']
    msg_id_hdr = item['message_id']

    # Case 1: msg-11 (multipart/related with inline CID attachment)
    if item['fixture_id'] == 'msg-11':
        rel_bound = "===============_bound_msg11_rel_=="
        alt_bound = "===============_bound_msg11_alt_=="
        att = item['attachments'][0]
        att_b64 = base64.b64encode(att['data']).decode('ascii')
        att_b64_wrapped = "\r\n".join(att_b64[i:i+76] for i in range(0, len(att_b64), 76))

        lines = [
            f"From: {from_hdr}",
            f"To: {to_hdr}",
            f"Subject: {subject_hdr}",
            f"Date: {date_hdr}",
            f"Message-ID: {msg_id_hdr}",
            "MIME-Version: 1.0",
            f'Content-Type: multipart/related; boundary="{rel_bound}"; type="text/html"',
            "",
            f"--{rel_bound}",
            f'Content-Type: multipart/alternative; boundary="{alt_bound}"',
            "",
            f"--{alt_bound}",
            'Content-Type: text/plain; charset="utf-8"',
            "Content-Transfer-Encoding: 8bit",
            "",
            item['body_text'],
            f"--{alt_bound}",
            'Content-Type: text/html; charset="utf-8"',
            "Content-Transfer-Encoding: 8bit",
            "",
            item['body_html'],
            f"--{alt_bound}--",
            "",
            f"--{rel_bound}",
            f"Content-Type: {att['content_type']}",
            "Content-Transfer-Encoding: base64",
            f"Content-ID: {att['content_id']}",
            f'Content-Disposition: inline; filename="{att["filename"]}"',
            "",
            att_b64_wrapped,
            f"--{rel_bound}--",
            ""
        ]
        return "\r\n".join(lines).encode('utf-8')

    # Case 2: Messages with regular attachments (msg-05, msg-06, msg-07)
    if item['attachments']:
        mix_bound = f"===============_bound_{item['fixture_id'].replace('-', '')}_mix_=="
        att = item['attachments'][0]
        att_b64 = base64.b64encode(att['data']).decode('ascii')
        att_b64_wrapped = "\r\n".join(att_b64[i:i+76] for i in range(0, len(att_b64), 76))

        if item.get('body_html'):
            alt_bound = f"===============_bound_{item['fixture_id'].replace('-', '')}_alt_=="
            body_block = [
                f'Content-Type: multipart/alternative; boundary="{alt_bound}"',
                "",
                f"--{alt_bound}",
                'Content-Type: text/plain; charset="utf-8"',
                "Content-Transfer-Encoding: 8bit",
                "",
                item['body_text'],
                f"--{alt_bound}",
                'Content-Type: text/html; charset="utf-8"',
                "Content-Transfer-Encoding: 8bit",
                "",
                item['body_html'],
                f"--{alt_bound}--",
            ]
        else:
            body_block = [
                'Content-Type: text/plain; charset="utf-8"',
                "Content-Transfer-Encoding: 8bit",
                "",
                item['body_text']
            ]

        # Handle Turkish characters in attachment filename via RFC 2231
        fn = att['filename']
        if any(ord(c) > 127 for c in fn):
            fn_encoded = urllib.parse.quote(fn, encoding='utf-8')
            disposition = f"attachment; filename*=utf-8''{fn_encoded}"
        else:
            disposition = f'attachment; filename="{fn}"'

        lines = [
            f"From: {from_hdr}",
            f"To: {to_hdr}",
            f"Subject: {subject_hdr}",
            f"Date: {date_hdr}",
            f"Message-ID: {msg_id_hdr}",
            "MIME-Version: 1.0",
            f'Content-Type: multipart/mixed; boundary="{mix_bound}"',
            "",
            f"--{mix_bound}",
        ] + body_block + [
            f"--{mix_bound}",
            f"Content-Type: {att['content_type']}",
            "Content-Transfer-Encoding: base64",
            f"Content-Disposition: {disposition}",
            "",
            att_b64_wrapped,
            f"--{mix_bound}--",
            ""
        ]
        return "\r\n".join(lines).encode('utf-8')

    # Case 3: Multipart alternative (text + HTML) without attachments (msg-01, msg-03, msg-04, msg-08, msg-12)
    if item.get('body_html'):
        alt_bound = f"===============_bound_{item['fixture_id'].replace('-', '')}_alt_=="
        lines = [
            f"From: {from_hdr}",
            f"To: {to_hdr}",
            f"Subject: {subject_hdr}",
            f"Date: {date_hdr}",
            f"Message-ID: {msg_id_hdr}",
            "MIME-Version: 1.0",
            f'Content-Type: multipart/alternative; boundary="{alt_bound}"',
            "",
            f"--{alt_bound}",
            'Content-Type: text/plain; charset="utf-8"',
            "Content-Transfer-Encoding: 8bit",
            "",
            item['body_text'],
            f"--{alt_bound}",
            'Content-Type: text/html; charset="utf-8"',
            "Content-Transfer-Encoding: 8bit",
            "",
            item['body_html'],
            f"--{alt_bound}--",
            ""
        ]
        return "\r\n".join(lines).encode('utf-8')

    # Case 4: Plain text only without attachments (msg-09, msg-10)
    lines = [
        f"From: {from_hdr}",
        f"To: {to_hdr}",
        f"Subject: {subject_hdr}",
        f"Date: {date_hdr}",
        f"Message-ID: {msg_id_hdr}",
        "MIME-Version: 1.0",
        'Content-Type: text/plain; charset="utf-8"',
        "Content-Transfer-Encoding: 8bit",
        "",
        item['body_text'],
        ""
    ]
    return "\r\n".join(lines).encode('utf-8')


def build_raw_corpus():
    """Build raw byte representations for all 12 corpus items."""
    spec = get_corpus_spec()
    raw_map = {}

    # Build msg-01 first
    msg01_bytes = build_eml_bytes(spec[0])
    raw_map['msg-01'] = msg01_bytes

    # msg-02 is the exact byte-identical duplicate of msg-01
    raw_map['msg-02'] = msg01_bytes

    # Build msg-03 through msg-12
    for item in spec[2:]:
        raw_map[item['fixture_id']] = build_eml_bytes(item)

    return spec, raw_map


def generate_corpus(output_dir: Path):
    """Generate all EML files, manifest.json, and corpus.mbox in output_dir."""
    output_dir = output_dir.resolve()
    eml_dir = output_dir / 'eml'
    eml_dir.mkdir(parents=True, exist_ok=True)

    spec, raw_map = build_raw_corpus()

    # 1. Write the 12 physical EML files
    manifest_records = []
    folder_counts = {'Gelen Kutusu': 0, 'Gönderilenler': 0, 'Projeler/İstanbul': 0}

    for item in spec:
        fid = item['fixture_id']
        raw_bytes = raw_map[fid]
        eml_file = output_dir / item['eml_rel_path']
        eml_file.parent.mkdir(parents=True, exist_ok=True)
        eml_file.write_bytes(raw_bytes)

        folder_counts[item['folder']] += 1

        # Process attachments metadata
        att_meta_list = []
        for att in item['attachments']:
            att_bytes = att['data']
            att_meta_list.append({
                'filename': att['filename'],
                'contentType': att['content_type'],
                'sha256': hashlib.sha256(att_bytes).hexdigest(),
                'size': len(att_bytes),
                'isInline': att.get('is_inline', False),
                'contentId': att.get('content_id')
            })

        manifest_records.append({
            'fixtureId': fid,
            'emlPath': item['eml_rel_path'],
            'folder': item['folder'],
            'messageId': item['message_id'],
            'date': item['date_iso'],
            'dateRfc': item['date_rfc'],
            'from': f"{item['from_name']} <{item['from_addr']}>",
            'to': f"{item['to_name']} <{item['to_addr']}>",
            'subject': item['subject'],
            'bodyText': item['body_text'],
            'rawSha256': hashlib.sha256(raw_bytes).hexdigest(),
            'rawSize': len(raw_bytes),
            'attachments': att_meta_list,
            'isDuplicate': item['is_duplicate'],
            'duplicateOf': item['duplicate_of'],
            'sameMessageIdDiffContent': item['same_msg_id_diff_content'],
            'sharedMessageIdWith': item['shared_msg_id_with']
        })

    # 2. Hard-coded filter oracles (fixed in design, not derived dynamically)
    filter_oracles = {
        'dateRange2024Utc': {
            'range': '[2024-01-01T00:00:00Z, 2025-01-01T00:00:00Z)',
            'description': 'Messages whose timestamp converted to UTC falls within the year 2024 (half-open interval). Excludes boundary msg-11 whose local time is 2024-01-01 00:30:00+03:00 but UTC instant is 2023-12-31 21:30:00Z.',
            'expectedFixtureIds': [
                'msg-03', 'msg-04', 'msg-05', 'msg-06', 'msg-07', 'msg-08', 'msg-12'
            ]
        },
        'attachmentBearingMessages': {
            'description': 'Messages containing one or more attachments (including inline CID images)',
            'expectedFixtureIds': [
                'msg-05', 'msg-06', 'msg-07', 'msg-11'
            ]
        }
    }

    # 3. Write manifest.json
    manifest_data = {
        'corpusVersion': 'v1',
        'generatedAt': '2026-09-12T12:00:00Z',
        'totalMessages': 12,
        'uniqueRawContents': 11,
        'folderDistribution': folder_counts,
        'filterOracles': filter_oracles,
        'messages': manifest_records
    }

    manifest_file = output_dir / 'manifest.json'
    manifest_file.write_text(json.dumps(manifest_data, indent=2, ensure_ascii=False), encoding='utf-8')

    # 4. Write corpus.mbox using explicit mboxrd escaping
    # Python stdlib mailbox.mbox with default mangle_from_=True only escapes '^From ' (mboxo),
    # which fails to distinguish an original '>From ' from an escaped 'From '.
    # We explicitly implement mboxrd escaping: any line in body matching ^>*From gets an extra '>'.
    # We reliably identify the header/body boundary and use splitlines(keepends=True)
    # so every physical body line is handled independently of CRLF/LF mixed representation.
    mbox_file = output_dir / 'corpus.mbox'
    if mbox_file.exists():
        mbox_file.unlink()

    with open(mbox_file, 'wb') as f:
        for item in spec:
            fid = item['fixture_id']
            raw_bytes = raw_map[fid]
            from_line = f"From {item['envelope_sender']} {item['envelope_date']}\n".encode('ascii')
            f.write(from_line)

            # Reliably identify header and body boundary
            pos_crlf = raw_bytes.find(b'\r\n\r\n')
            pos_lf = raw_bytes.find(b'\n\n')

            if pos_crlf != -1 and (pos_lf == -1 or pos_crlf <= pos_lf):
                header_bytes = raw_bytes[:pos_crlf]
                body_bytes = raw_bytes[pos_crlf + 4:]
                hdr_sep = b'\r\n\r\n'
            elif pos_lf != -1:
                header_bytes = raw_bytes[:pos_lf]
                body_bytes = raw_bytes[pos_lf + 2:]
                hdr_sep = b'\n\n'
            else:
                header_bytes = raw_bytes
                body_bytes = b''
                hdr_sep = b'\n\n'

            # Explicit mboxrd transform on every physical body line matching ^>*From 
            # using splitlines(keepends=True) independent of CRLF/LF
            escaped_body_lines = []
            for line in body_bytes.splitlines(keepends=True):
                if re.match(rb'^(>*)From ', line):
                    escaped_body_lines.append(b'>' + line)
                else:
                    escaped_body_lines.append(line)

            escaped_eml = header_bytes + hdr_sep + b''.join(escaped_body_lines)
            f.write(escaped_eml)
            if not escaped_eml.endswith(b'\n'):
                f.write(b'\n')
            f.write(b'\n')

    return output_dir, manifest_data


def validate_corpus(output_dir: Path, manifest_data: dict) -> bool:
    """Validate the generated corpus against all specifications and hard-coded oracles."""
    evidence = []
    errors = []

    output_dir = output_dir.resolve()
    manifest_file = output_dir / 'manifest.json'
    mbox_file = output_dir / 'corpus.mbox'

    if not manifest_file.exists():
        errors.append(f"Missing manifest file: {manifest_file}")
    if not mbox_file.exists():
        errors.append(f"Missing mbox file: {mbox_file}")

    # Check 1: Exactly 12 physical EML files, parseable without defects
    parsed_messages = {}
    raw_hashes = {}

    for rec in manifest_data['messages']:
        fid = rec['fixtureId']
        eml_path = output_dir / rec['emlPath']
        if not eml_path.exists():
            errors.append(f"Missing physical EML file: {eml_path}")
            continue

        eml_bytes = eml_path.read_bytes()
        actual_hash = hashlib.sha256(eml_bytes).hexdigest()
        actual_size = len(eml_bytes)

        if actual_hash != rec['rawSha256']:
            errors.append(f"{fid} hash mismatch: computed {actual_hash} != manifest {rec['rawSha256']}")
        if actual_size != rec['rawSize']:
            errors.append(f"{fid} size mismatch: computed {actual_size} != manifest {rec['rawSize']}")

        raw_hashes[fid] = actual_hash

        # Parse with email.policy.default to check defects
        parsed = email.message_from_bytes(eml_bytes, policy=email.policy.default)
        if parsed.defects:
            errors.append(f"{fid} MIME parse defects found: {parsed.defects}")
        parsed_messages[fid] = parsed

    if len(parsed_messages) == 12:
        evidence.append("Check 1 PASS: Exactly 12 physical EML files parsed with zero MIME defects.")

    # Check 2: 11 unique raw contents with byte-identical duplicate pair msg-01 == msg-02
    unique_hashes = set(raw_hashes.values())
    if len(unique_hashes) != 11:
        errors.append(f"Expected 11 unique raw hashes, found {len(unique_hashes)}")
    else:
        evidence.append(f"Check 2 PASS: Exactly 11 unique raw hashes found across 12 EML files.")

    if raw_hashes.get('msg-01') != raw_hashes.get('msg-02'):
        errors.append("msg-01 and msg-02 do not have identical raw hashes")
    else:
        evidence.append(f"Check 3 PASS: Byte-identical duplicate pair verified: msg-01 == msg-02 ({raw_hashes['msg-01'][:12]}...)")

    # Check 3: Same Message-ID / different content pair: msg-03 and msg-04
    p3 = parsed_messages.get('msg-03')
    p4 = parsed_messages.get('msg-04')
    if p3 and p4:
        id3 = p3['Message-ID']
        id4 = p4['Message-ID']
        if id3 != id4:
            errors.append(f"msg-03 Message-ID ({id3}) != msg-04 Message-ID ({id4})")
        if raw_hashes.get('msg-03') == raw_hashes.get('msg-04'):
            errors.append("msg-03 and msg-04 have identical raw content, expected different content")
        if p3['Subject'] == p4['Subject']:
            errors.append("msg-03 and msg-04 have identical subjects")
        evidence.append(f"Check 4 PASS: Shared Message-ID / distinct content verified for msg-03 & msg-04 ({id3}).")

    # Check 4: Folder distribution
    folder_counts = {'Gelen Kutusu': 0, 'Gönderilenler': 0, 'Projeler/İstanbul': 0}
    for rec in manifest_data['messages']:
        f = rec['folder']
        if f in folder_counts:
            folder_counts[f] += 1
        else:
            errors.append(f"Unexpected folder in record: {f}")

    if folder_counts == {'Gelen Kutusu': 5, 'Gönderilenler': 3, 'Projeler/İstanbul': 4}:
        evidence.append("Check 5 PASS: Folder distribution verified: Gelen Kutusu (5), Gönderilenler (3), Projeler/İstanbul (4).")
    else:
        errors.append(f"Folder count mismatch: {folder_counts}")

    # Check 5: Attachments verification
    att_checked = 0
    for rec in manifest_data['messages']:
        fid = rec['fixtureId']
        expected_atts = rec['attachments']
        parsed = parsed_messages.get(fid)
        if not parsed:
            continue

        # Extract attachments from parsed message
        actual_atts = []
        for part in parsed.walk():
            # Check disposition or inline with Content-ID
            disposition = part.get_content_disposition()
            cid = part.get('Content-ID')
            filename = part.get_filename()

            if disposition == 'attachment' or (disposition == 'inline' and cid) or (filename and disposition is None):
                payload = part.get_payload(decode=True)
                if payload is not None:
                    actual_atts.append({
                        'filename': filename,
                        'contentType': part.get_content_type(),
                        'sha256': hashlib.sha256(payload).hexdigest(),
                        'size': len(payload)
                    })

        if len(actual_atts) != len(expected_atts):
            errors.append(f"{fid} attachment count mismatch: actual {len(actual_atts)} != expected {len(expected_atts)}")
        else:
            for exp, act in zip(expected_atts, actual_atts):
                att_checked += 1
                if exp['filename'] != act['filename']:
                    errors.append(f"{fid} attachment filename mismatch: {act['filename']} != {exp['filename']}")
                if exp['sha256'] != act['sha256']:
                    errors.append(f"{fid} attachment hash mismatch: {act['sha256']} != {exp['sha256']}")
                if exp['size'] != act['size']:
                    errors.append(f"{fid} attachment size mismatch: {act['size']} != {exp['size']}")

    if att_checked == 4 and not errors:
        evidence.append("Check 6 PASS: All 4 attachments verified (UTF-8 Turkish text, binary 256B, report PNG, inline CID PNG).")

    # Check 6: Filter Oracles (verified against hard-coded design lists)
    # Oracle 1: Date in [2024-01-01T00:00:00Z, 2025-01-01T00:00:00Z)
    range_start = datetime(2024, 1, 1, 0, 0, 0, tzinfo=timezone.utc)
    range_end = datetime(2025, 1, 1, 0, 0, 0, tzinfo=timezone.utc)

    # Explicit boundary check on msg-11: local year == 2024, but UTC instant is in 2023 (< range_start)
    p11 = parsed_messages.get('msg-11')
    if p11:
        dt11_raw = p11['Date']
        dt11_local = email.utils.parsedate_to_datetime(dt11_raw)
        dt11_utc = dt11_local.astimezone(timezone.utc)

        if dt11_local.year != 2024:
            errors.append(f"msg-11 expected local year 2024, got {dt11_local.year}")
        if dt11_utc >= range_start:
            errors.append(f"msg-11 expected UTC instant before {range_start.isoformat()}, got {dt11_utc.isoformat()}")

        if dt11_local.year == 2024 and dt11_utc < range_start:
            evidence.append(
                f"Check 7 PASS: msg-11 date boundary verified: local Date is {dt11_local.isoformat()} (year 2024), "
                f"but UTC instant is {dt11_utc.isoformat()} (year 2023) -> correctly excluded from UTC 2024 oracle."
            )
    else:
        errors.append("msg-11 not found in parsed messages for boundary check")

    actual_2024_ids = []
    for fid, parsed in parsed_messages.items():
        dt_hdr = parsed['Date']
        parsed_dt = email.utils.parsedate_to_datetime(dt_hdr)
        utc_dt = parsed_dt.astimezone(timezone.utc)
        if range_start <= utc_dt < range_end:
            actual_2024_ids.append(fid)

    actual_2024_ids.sort()
    expected_2024_ids = manifest_data['filterOracles']['dateRange2024Utc']['expectedFixtureIds']
    if actual_2024_ids != expected_2024_ids:
        errors.append(f"Oracle 1 mismatch: actual {actual_2024_ids} != expected {expected_2024_ids}")
    else:
        evidence.append(f"Check 8 PASS: Filter Oracle 1 (2024 UTC range) matches hard-coded expected list ({len(actual_2024_ids)} messages: {actual_2024_ids}).")

    # Oracle 2: Attachment-bearing messages
    actual_att_ids = [rec['fixtureId'] for rec in manifest_data['messages'] if len(rec['attachments']) > 0]
    actual_att_ids.sort()
    expected_att_ids = manifest_data['filterOracles']['attachmentBearingMessages']['expectedFixtureIds']
    if actual_att_ids != expected_att_ids:
        errors.append(f"Oracle 2 mismatch: actual {actual_att_ids} != expected {expected_att_ids}")
    else:
        evidence.append(f"Check 9 PASS: Filter Oracle 2 (attachment-bearing) matches hard-coded expected list ({len(actual_att_ids)} messages).")

    # Check 10: Raw MBOX byte-level escaping verification before mailbox.mbox parsing
    raw_mbox_bytes = mbox_file.read_bytes()
    env_msg08 = b"From ahmet.yilmaz@posta.example Sun Aug 25 13:20:00 2024"
    idx08 = raw_mbox_bytes.find(env_msg08)
    if idx08 == -1:
        errors.append("msg-08 envelope line not found in raw corpus.mbox bytes")
    else:
        next_env = raw_mbox_bytes.find(b"\nFrom ", idx08 + len(env_msg08))
        if next_env == -1:
            msg08_segment = raw_mbox_bytes[idx08:]
        else:
            msg08_segment = raw_mbox_bytes[idx08:next_env]

        has_raw_gt_from = b">From Ahmet Y" in msg08_segment
        has_raw_gt_gt_from = b">>From Canan" in msg08_segment

        body_in_segment = msg08_segment[len(env_msg08):]
        has_unescaped_from_in_body = (b"\nFrom Ahmet Y" in body_in_segment or b"\r\nFrom Ahmet Y" in body_in_segment)

        if not has_raw_gt_from:
            errors.append("Raw corpus.mbox msg-08 segment missing expected escaped '>From Ahmet Yılmaz' line")
        if not has_raw_gt_gt_from:
            errors.append("Raw corpus.mbox msg-08 segment missing expected escaped '>>From Canan Özkan' line")
        if has_unescaped_from_in_body:
            errors.append("Raw corpus.mbox msg-08 segment body contains unescaped 'From Ahmet Yılmaz' line")

        if has_raw_gt_from and has_raw_gt_gt_from and not has_unescaped_from_in_body:
            evidence.append(
                "Check 10 PASS: Raw MBOX byte-level escaping verified: msg-08 segment physically contains '>From ' and '>>From '; "
                "no unescaped 'From ' line exists in body."
            )

    # Check 11: corpus.mbox parsing and semantic msg-08 body validation
    mbox = mailbox.mbox(str(mbox_file))
    if len(mbox) != 12:
        errors.append(f"corpus.mbox message count mismatch: {len(mbox)} != 12")
    else:
        evidence.append(f"Check 11a PASS: corpus.mbox contains exactly {len(mbox)} messages.")

    msg08_found = False
    for mbox_msg in mbox:
        if mbox_msg.get('Message-ID') == '<msg-2024-08-mbox-kacis@posta.example>':
            msg08_found = True
            body_payload = ""
            for part in mbox_msg.walk():
                if part.get_content_type() == 'text/plain':
                    raw_payload = part.get_payload(decode=True)
                    if raw_payload:
                        body_payload = raw_payload.decode('utf-8')
                        break

            # Account for mailbox.mbox automatic From_ unescape exactly once; do not double-unescape.
            lines = body_payload.splitlines()
            has_raw_escaped_ahmet = any(l.startswith('>From Ahmet Yılmaz') for l in lines)
            has_already_unescaped_ahmet = any(l.startswith('From Ahmet Yılmaz') for l in lines)

            if has_raw_escaped_ahmet:
                # mailbox.mbox preserved raw escaped lines; unescape mboxrd once
                unescaped_lines = []
                for line in lines:
                    unescaped_lines.append(re.sub(r'^>(>*From )', r'\1', line))
                semantic_body = "\n".join(unescaped_lines)
            elif has_already_unescaped_ahmet:
                # mailbox.mbox already unescaped From_ once; do not double-unescape
                semantic_body = "\n".join(lines)
            else:
                errors.append("MBOX msg-08 decoded text missing both '>From Ahmet' and 'From Ahmet'")
                semantic_body = "\n".join(lines)

            # Assert that final semantic body contains both distinct exact original lines
            semantic_lines = semantic_body.splitlines()
            orig_from_preserved = any(l.startswith('From Ahmet Yılmaz <ahmet.yilmaz@posta.example>') for l in semantic_lines)
            orig_gt_from_preserved = any(l.startswith('>From Canan Özkan <canan.ozkan@muhendislik.example>') for l in semantic_lines)

            if not orig_from_preserved:
                errors.append("MBOX msg-08 semantic body missing exact original line 'From Ahmet Yılmaz <ahmet.yilmaz@posta.example>'")
            if not orig_gt_from_preserved:
                errors.append("MBOX msg-08 semantic body missing exact original line '>From Canan Özkan <canan.ozkan@muhendislik.example>'")

            expected_body = manifest_data['messages'][7]['bodyText']
            # Normalize newlines for comparison
            if semantic_body.replace('\r\n', '\n').strip() != expected_body.replace('\r\n', '\n').strip():
                errors.append("MBOX msg-08 semantic body does not match expected body in manifest")
            elif orig_from_preserved and orig_gt_from_preserved:
                evidence.append(
                    "Check 11b PASS: MBOX msg-08 semantic body verified: unescaped exactly once; contains distinct exact original lines "
                    "'From Ahmet Yılmaz...' and '>From Canan Özkan...'; matches expected manifest bodyText."
                )
            break

    if not msg08_found:
        errors.append("msg-08 not found in corpus.mbox by Message-ID")

    # Check 12: MBOX attachments verification against manifest semantic hashes
    manifest_attachments_by_msgid = {}
    for rec in manifest_data['messages']:
        if rec['attachments']:
            manifest_attachments_by_msgid[rec['messageId']] = rec['attachments']

    mbox_attachments_checked = 0
    for mbox_msg in mbox:
        mid = mbox_msg.get('Message-ID')
        if mid in manifest_attachments_by_msgid:
            expected_atts = manifest_attachments_by_msgid[mid]
            actual_atts = []
            for part in mbox_msg.walk():
                disposition = part.get_content_disposition()
                cid = part.get('Content-ID')
                filename = part.get_filename()
                if disposition == 'attachment' or (disposition == 'inline' and cid) or (filename and disposition is None):
                    payload = part.get_payload(decode=True)
                    if payload is not None:
                        actual_atts.append({
                            'filename': filename,
                            'contentType': part.get_content_type(),
                            'sha256': hashlib.sha256(payload).hexdigest(),
                            'size': len(payload)
                        })
            if len(actual_atts) != len(expected_atts):
                errors.append(f"MBOX message {mid} attachment count mismatch: actual {len(actual_atts)} != expected {len(expected_atts)}")
            else:
                for exp, act in zip(expected_atts, actual_atts):
                    mbox_attachments_checked += 1
                    if exp['filename'] != act['filename']:
                        errors.append(f"MBOX message {mid} attachment filename mismatch: {act['filename']} != {exp['filename']}")
                    if exp['sha256'] != act['sha256']:
                        errors.append(f"MBOX message {mid} attachment sha256 mismatch: {act['sha256']} != {exp['sha256']}")
                    if exp['size'] != act['size']:
                        errors.append(f"MBOX message {mid} attachment size mismatch: {act['size']} != {exp['size']}")

    if mbox_attachments_checked == 4 and not errors:
        evidence.append(
            "Check 12 PASS: MBOX attachments verification: all 4 attachments parsed from corpus.mbox match manifest semantic SHA-256 hashes and sizes."
        )

    # Check 13: Total delivered corpus size under 1MB (actual real bytes from stat)
    eml_file_sizes = [(output_dir / rec['emlPath']).stat().st_size for rec in manifest_data['messages']]
    eml_payload_bytes = sum(eml_file_sizes)
    mbox_bytes = mbox_file.stat().st_size
    manifest_bytes = manifest_file.stat().st_size
    total_delivered_bytes = eml_payload_bytes + mbox_bytes + manifest_bytes

    if total_delivered_bytes >= 1_000_000:
        errors.append(f"Total delivered corpus size ({total_delivered_bytes} bytes) exceeds 1MB limit (1,000,000 bytes)")
    else:
        evidence.append(
            f"Check 13 PASS: Total delivered corpus size is bounded at {total_delivered_bytes:,} bytes "
            f"({total_delivered_bytes / 1024:.2f} KB < 1,000 KB) [12 EML files: {eml_payload_bytes:,} B, "
            f"corpus.mbox: {mbox_bytes:,} B, manifest.json: {manifest_bytes:,} B]."
        )

    # Final reporting
    print("=" * 70)
    print("CORPUS GENERATION AND VALIDATION EVIDENCE REPORT")
    print("=" * 70)
    for line in evidence:
        print(f" [+] {line}")

    if errors:
        print("\n" + "!" * 70)
        print("VALIDATION ERRORS ENCOUNTERED:")
        print("!" * 70)
        for err in errors:
            print(f" [-] {err}")
        return False

    print("=" * 70)
    print("ALL 13 VALIDATION CHECKS COMPLETED SUCCESSFULLY.")
    print("=" * 70)
    return True


def main():
    parser = argparse.ArgumentParser(
        description="Generate and validate deterministic synthetic mail corpus (TASK-007)."
    )
    parser.add_argument(
        'output_dir',
        nargs='?',
        default=None,
        help="Target output directory. Defaults to 'fixtures/mail-corpus-v1' relative to repo root."
    )
    parser.add_argument(
        '--output-dir',
        dest='opt_output_dir',
        default=None,
        help="Alternative flag to specify target directory."
    )

    args = parser.parse_args()
    target_str = args.opt_output_dir or args.output_dir

    if target_str:
        target_path = Path(target_str)
    else:
        # Default to repo_root/fixtures/mail-corpus-v1
        repo_root = Path(__file__).resolve().parent.parent
        target_path = repo_root / 'fixtures' / 'mail-corpus-v1'

    print(f"[*] Target corpus directory: {target_path.resolve()}")
    out_dir, manifest = generate_corpus(target_path)
    print(f"[*] Generated 12 EML files, manifest.json, and corpus.mbox.")
    print(f"[*] Running self-validation via email, mailbox, json, and hashlib...")

    success = validate_corpus(out_dir, manifest)
    if not success:
        sys.exit(1)
    sys.exit(0)


if __name__ == '__main__':
    main()
