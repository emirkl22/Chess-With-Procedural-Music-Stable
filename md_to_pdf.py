# md_to_pdf.py  — Markdown → PDF converter (Turkish-aware, ReportLab)
# Usage: python md_to_pdf.py

import re, os
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import cm
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.enums import TA_LEFT, TA_CENTER
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, HRFlowable,
    Table, TableStyle, KeepTogether
)
from reportlab.lib import colors
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

# ── Fonts ──────────────────────────────────────────────────────────────────
FONT_DIR = r"C:\Windows\Fonts"
pdfmetrics.registerFont(TTFont("Arial",     os.path.join(FONT_DIR, "arial.ttf")))
pdfmetrics.registerFont(TTFont("Arial-Bold",os.path.join(FONT_DIR, "arialbd.ttf")))
pdfmetrics.registerFont(TTFont("Arial-Italic",os.path.join(FONT_DIR, "ariali.ttf")))
pdfmetrics.registerFontFamily("Arial",
    normal="Arial", bold="Arial-Bold", italic="Arial-Italic")

MONO = "Courier"   # built-in — ASCII-safe for code blocks

# ── Styles ─────────────────────────────────────────────────────────────────
W, H = A4
MARGIN = 2.2 * cm

def make_styles():
    base = dict(fontName="Arial", leading=14, spaceAfter=4)
    S = {}
    S["normal"]  = ParagraphStyle("normal",  fontSize=10, **base)
    S["h1"]      = ParagraphStyle("h1",  fontName="Arial-Bold",
                                  fontSize=18, spaceBefore=14, spaceAfter=6,
                                  textColor=colors.HexColor("#1a1a5e"), leading=22)
    S["h2"]      = ParagraphStyle("h2",  fontName="Arial-Bold",
                                  fontSize=14, spaceBefore=12, spaceAfter=5,
                                  textColor=colors.HexColor("#1a3a6e"), leading=18)
    S["h3"]      = ParagraphStyle("h3",  fontName="Arial-Bold",
                                  fontSize=11, spaceBefore=8,  spaceAfter=4,
                                  textColor=colors.HexColor("#2a4a8e"), leading=15)
    S["h4"]      = ParagraphStyle("h4",  fontName="Arial-Bold",
                                  fontSize=10, spaceBefore=6,  spaceAfter=3,
                                  textColor=colors.HexColor("#444"), leading=13)
    S["code"]    = ParagraphStyle("code", fontName=MONO, fontSize=7.5,
                                  leading=11, spaceAfter=2,
                                  leftIndent=8, rightIndent=8,
                                  backColor=colors.HexColor("#f4f4f4"),
                                  textColor=colors.HexColor("#1a1a1a"))
    S["bullet"]  = ParagraphStyle("bullet", fontName="Arial", fontSize=10,
                                  leading=14, leftIndent=16, bulletIndent=4,
                                  spaceAfter=2)
    S["bullet2"] = ParagraphStyle("bullet2", fontName="Arial", fontSize=10,
                                  leading=14, leftIndent=30, bulletIndent=18,
                                  spaceAfter=2)
    S["toc"]     = ParagraphStyle("toc", fontName="Arial", fontSize=10,
                                  leading=14, leftIndent=10)
    S["meta"]    = ParagraphStyle("meta", fontName="Arial", fontSize=10,
                                  textColor=colors.HexColor("#555"), leading=14)
    S["hr_spacer"] = ParagraphStyle("hr_spacer", spaceBefore=6, spaceAfter=6)
    return S

STYLES = make_styles()

# ── Inline markdown helpers ─────────────────────────────────────────────────
def escape_xml(text):
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")

def inline(text):
    """Convert inline markdown bold/italic/code to ReportLab XML."""
    text = escape_xml(text)
    # bold + italic: ***...***
    text = re.sub(r'\*\*\*(.+?)\*\*\*', r'<b><i>\1</i></b>', text)
    # bold: **...** or __...__
    text = re.sub(r'\*\*(.+?)\*\*', r'<b>\1</b>', text)
    text = re.sub(r'__(.+?)__',     r'<b>\1</b>', text)
    # italic: *...* or _..._
    text = re.sub(r'\*(.+?)\*',     r'<i>\1</i>', text)
    text = re.sub(r'_(.+?)_',       r'<i>\1</i>', text)
    # inline code: `...`
    text = re.sub(r'`([^`]+)`', lambda m:
        f'<font name="{MONO}" size="8">{m.group(1)}</font>', text)
    return text

# ── Table builder ──────────────────────────────────────────────────────────
def build_table(rows_raw):
    """rows_raw: list of raw line strings starting/ending with |"""
    table_data = []
    is_header_row = True
    col_count = 0
    for raw in rows_raw:
        cells = [c.strip() for c in raw.strip().strip("|").split("|")]
        if re.match(r'^[\s\-|:]+$', raw):   # separator row
            is_header_row = False
            continue
        col_count = max(col_count, len(cells))
        style = STYLES["h4"] if is_header_row else STYLES["normal"]
        para_cells = [Paragraph(inline(c), style) for c in cells]
        table_data.append(para_cells)
        if is_header_row:
            is_header_row = False  # only first data row is header

    if not table_data:
        return None

    avail_w = W - 2 * MARGIN
    col_w = avail_w / max(col_count, 1)
    col_widths = [col_w] * col_count

    t = Table(table_data, colWidths=col_widths, repeatRows=1)
    t.setStyle(TableStyle([
        ("BACKGROUND",  (0,0), (-1,0), colors.HexColor("#1a3a6e")),
        ("TEXTCOLOR",   (0,0), (-1,0), colors.white),
        ("FONTNAME",    (0,0), (-1,0), "Arial-Bold"),
        ("FONTSIZE",    (0,0), (-1,0), 9),
        ("BACKGROUND",  (0,1), (-1,-1), colors.HexColor("#f9f9f9")),
        ("ROWBACKGROUNDS", (0,1), (-1,-1),
            [colors.HexColor("#f9f9f9"), colors.HexColor("#eef2ff")]),
        ("GRID",        (0,0), (-1,-1), 0.3, colors.HexColor("#cccccc")),
        ("VALIGN",      (0,0), (-1,-1), "TOP"),
        ("TOPPADDING",  (0,0), (-1,-1), 4),
        ("BOTTOMPADDING",(0,0),(-1,-1), 4),
        ("LEFTPADDING", (0,0), (-1,-1), 6),
        ("RIGHTPADDING",(0,0), (-1,-1), 6),
    ]))
    return t

# ── Code block builder ─────────────────────────────────────────────────────
def build_code_block(lines):
    """Return a Table containing a shaded code block."""
    content = "\n".join(lines)
    # Escape XML; keep newlines as <br/>
    escaped = escape_xml(content)
    para_lines = []
    for ln in escaped.split("\n"):
        para_lines.append(Paragraph(ln if ln else " ", STYLES["code"]))
    avail_w = W - 2 * MARGIN
    t = Table([[p] for p in para_lines], colWidths=[avail_w])
    t.setStyle(TableStyle([
        ("BACKGROUND",   (0,0), (-1,-1), colors.HexColor("#f4f4f4")),
        ("BOX",          (0,0), (-1,-1), 0.5, colors.HexColor("#cccccc")),
        ("TOPPADDING",   (0,0), (-1,-1), 2),
        ("BOTTOMPADDING",(0,0), (-1,-1), 2),
        ("LEFTPADDING",  (0,0), (-1,-1), 8),
        ("RIGHTPADDING", (0,0), (-1,-1), 8),
    ]))
    return t

# ── Main parser ────────────────────────────────────────────────────────────
def parse_md(text):
    """Parse markdown text into a list of ReportLab flowables."""
    story = []
    lines = text.splitlines()
    i = 0

    while i < len(lines):
        line = lines[i]

        # ── Fenced code block ```
        if line.strip().startswith("```"):
            i += 1
            code_lines = []
            while i < len(lines) and not lines[i].strip().startswith("```"):
                code_lines.append(lines[i])
                i += 1
            i += 1  # skip closing ```
            story.append(Spacer(1, 4))
            story.append(build_code_block(code_lines))
            story.append(Spacer(1, 6))
            continue

        # ── Table rows
        if line.startswith("|"):
            table_rows = []
            while i < len(lines) and lines[i].startswith("|"):
                table_rows.append(lines[i])
                i += 1
            t = build_table(table_rows)
            if t:
                story.append(Spacer(1, 4))
                story.append(t)
                story.append(Spacer(1, 6))
            continue

        # ── Horizontal rule
        if re.match(r'^---+\s*$', line) or re.match(r'^===+\s*$', line):
            story.append(Spacer(1, 6))
            story.append(HRFlowable(width="100%", thickness=0.5,
                                    color=colors.HexColor("#aaaaaa")))
            story.append(Spacer(1, 6))
            i += 1
            continue

        # ── Headings
        m = re.match(r'^(#{1,6})\s+(.*)', line)
        if m:
            level = len(m.group(1))
            text  = m.group(2).strip()
            # strip inline links like [text](#anchor)
            text = re.sub(r'\[([^\]]+)\]\([^)]*\)', r'\1', text)
            skey = {1:"h1", 2:"h2", 3:"h3"}.get(level, "h4")
            story.append(Paragraph(inline(text), STYLES[skey]))
            i += 1
            continue

        # ── Bullet list (- or *)
        m = re.match(r'^(\s*)([-*])\s+(.*)', line)
        if m:
            indent = len(m.group(1))
            content = m.group(3)
            skey = "bullet2" if indent >= 2 else "bullet"
            story.append(Paragraph(
                f'<bullet>&bull;</bullet>{inline(content)}',
                STYLES[skey]
            ))
            i += 1
            continue

        # ── Numbered list
        m = re.match(r'^(\s*)\d+\.\s+(.*)', line)
        if m:
            indent = len(m.group(1))
            content = m.group(2)
            skey = "bullet2" if indent >= 2 else "bullet"
            story.append(Paragraph(
                f'<bullet>&#x25AA;</bullet>{inline(content)}',
                STYLES[skey]
            ))
            i += 1
            continue

        # ── Bold-only line (often used as sub-heading **...**)
        m = re.match(r'^\*\*([^*]+)\*\*\s*$', line.strip())
        if m and line.strip():
            story.append(Paragraph(f'<b>{escape_xml(m.group(1))}</b>', STYLES["h4"]))
            i += 1
            continue

        # ── Empty line
        if not line.strip():
            story.append(Spacer(1, 4))
            i += 1
            continue

        # ── Normal paragraph
        story.append(Paragraph(inline(line.strip()), STYLES["normal"]))
        i += 1

    return story

# ── Title page helper ──────────────────────────────────────────────────────
def make_title_page(title, meta_lines):
    S = []
    S.append(Spacer(1, 3*cm))
    S.append(Paragraph(title, ParagraphStyle("cover_title",
        fontName="Arial-Bold", fontSize=22, textColor=colors.HexColor("#1a1a5e"),
        leading=28, spaceAfter=12, alignment=TA_CENTER)))
    for ml in meta_lines:
        S.append(Paragraph(ml, ParagraphStyle("cover_meta",
            fontName="Arial", fontSize=11, textColor=colors.HexColor("#444"),
            leading=16, spaceAfter=4, alignment=TA_CENTER)))
    S.append(Spacer(1, 1*cm))
    S.append(HRFlowable(width="60%", thickness=1,
                         color=colors.HexColor("#1a3a6e"), hAlign="CENTER"))
    S.append(Spacer(1, 4*cm))
    return S

# ── Convert one file ────────────────────────────────────────────────────────
def convert(md_path, pdf_path, doc_title, meta_lines):
    with open(md_path, encoding="utf-8") as f:
        text = f.read()

    doc = SimpleDocTemplate(
        pdf_path,
        pagesize=A4,
        leftMargin=MARGIN, rightMargin=MARGIN,
        topMargin=MARGIN,  bottomMargin=MARGIN,
        title=doc_title,
        author="Muhammed Emir Kilic"
    )

    story = make_title_page(doc_title, meta_lines)
    story += parse_md(text)

    doc.build(story)
    print(f"  Created: {pdf_path}")

# ── Entry point ─────────────────────────────────────────────────────────────
if __name__ == "__main__":
    BASE = r"C:\Unity Projects\Chess With Procedural Music Stable"

    convert(
        md_path   = os.path.join(BASE, "MUSIC_SYSTEM.md"),
        pdf_path  = os.path.join(BASE, "MUSIC_SYSTEM.pdf"),
        doc_title = "Adaptif Prosedürel Ses Sistemi — Teknik Döküman",
        meta_lines= [
            "Muhammed Emir Kılıç — 2021555040",
            "Chess With Procedural Music",
            "Bahar 2026",
        ]
    )

    convert(
        md_path   = os.path.join(BASE, "SUPERCOLLIDER_INTEGRATION.md"),
        pdf_path  = os.path.join(BASE, "SUPERCOLLIDER_INTEGRATION.pdf"),
        doc_title = "SuperCollider Entegrasyon Planı",
        meta_lines= [
            "Adaptive Procedural Audio System Based on Chess AI Evaluation",
            "Muhammed Emir Kılıç — 2021555040",
            "Bahar 2026",
        ]
    )

    print("Done.")
