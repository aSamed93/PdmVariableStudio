# -*- coding: utf-8 -*-
r"""PDM Variable Studio — Kurulum ve Kullanım Kılavuzu (PDF) üreticisi, Türkçe ve İngilizce.

Kılavuzun KAYNAĞI bu dosyadır: metin, tablolar ve bölüm yapısı aşağıda yazılı. PDF'i elle
düzenlemeyin; burayı değiştirip yeniden üretin. Arayüzde bir düğme, durum ya da ayar
değiştiğinde bu dosya da aynı değişiklikle güncellenmeli.

İki dil TEK kaynakta: her metin T("Türkçe", "English") ile yan yana yazılır — uygulamadaki
Loc.T ile aynı yaklaşım. Böylece bir bölüm eklenip çıkarıldığında iki kılavuz birbirinden
kopamaz; biri değişince öbürünün de değişmesi gerektiği aynı satırda görünür. İngilizce
metindeki düğme ve durum adları uygulamanın İngilizce arayüzündekilerle birebir aynı olmalı.

Kullanım (depo kökünden):
    pip install -r docs/guide/requirements.txt
    python docs/guide/build_guide.py                  # iki dil
    python docs/guide/build_guide.py --lang en        # yalnızca İngilizce
    python docs/guide/build_guide.py --lang tr --output C:\Temp\kilavuz.pdf

Varsayılan çıktılar:
    docs/PdmVariableStudio-Kurulum-ve-Kullanim.pdf        (tr)
    docs/PdmVariableStudio-Installation-and-User-Guide.pdf (en)

Sürüm numarası elle yazılmaz; package-release.ps1 gibi ProductInfo.Version'dan okunur.

Yazı tipleri: Windows'ta Arial + Consolas + Segoe UI Symbol, Linux'ta Liberation Sans
(Arial ile ölçü uyumlu) + DejaVu. Türkçe karakterler ve durum simgeleri (✓ ⚠ ✗) için
gömülü TrueType yazı tipi şart; ReportLab'in yerleşik yazı tipleri bunları çizemez.
"""
import argparse
import os
import re
import sys
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.enums import TA_CENTER
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.pdfmetrics import registerFontFamily
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (BaseDocTemplate, PageTemplate, Frame, Paragraph, Spacer,
                                Table, TableStyle, PageBreak, KeepTogether, NextPageTemplate,
                                Flowable, CondPageBreak)
from reportlab.platypus.tableofcontents import TableOfContents

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEFAULT_OUTPUTS = {
    "tr": os.path.join(REPO_ROOT, "docs", "PdmVariableStudio-Kurulum-ve-Kullanim.pdf"),
    "en": os.path.join(REPO_ROOT, "docs", "PdmVariableStudio-Installation-and-User-Guide.pdf"),
}
VERSION_SOURCE = os.path.join(REPO_ROOT, "src", "PdmVariableStudio.Core", "Workbook",
                              "WorkbookWriter.cs")


def read_product_version():
    with open(VERSION_SOURCE, encoding="utf-8-sig") as f:
        match = re.search(r'public const string Version = "([^"]+)"', f.read())
    if not match:
        sys.exit(f"ProductInfo.Version bulunamadı: {VERSION_SOURCE}")
    return match.group(1)


_WIN_FONTS = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")
_DEJAVU = "/usr/share/fonts/truetype/dejavu"
_LIBERATION = "/usr/share/fonts/truetype/liberation"

# Her rol için adaylar; var olan ilk dosya kullanılır.
FONT_CANDIDATES = {
    "Sans": [os.path.join(_WIN_FONTS, "arial.ttf"),
             os.path.join(_LIBERATION, "LiberationSans-Regular.ttf")],
    "Sans-Bold": [os.path.join(_WIN_FONTS, "arialbd.ttf"),
                  os.path.join(_LIBERATION, "LiberationSans-Bold.ttf")],
    "Sans-Oblique": [os.path.join(_WIN_FONTS, "ariali.ttf"),
                     os.path.join(_LIBERATION, "LiberationSans-Italic.ttf")],
    "Sans-BoldOblique": [os.path.join(_WIN_FONTS, "arialbi.ttf"),
                         os.path.join(_LIBERATION, "LiberationSans-BoldItalic.ttf")],
    "Mono": [os.path.join(_WIN_FONTS, "consola.ttf"),
             os.path.join(_DEJAVU, "DejaVuSansMono.ttf")],
    "Mono-Bold": [os.path.join(_WIN_FONTS, "consolab.ttf"),
                  os.path.join(_DEJAVU, "DejaVuSansMono-Bold.ttf")],
    # Durum simgeleri (✓ ⚠ ✗ →) Arial/Liberation'da yok.
    "Sym": [os.path.join(_WIN_FONTS, "seguisym.ttf"),
            os.path.join(_DEJAVU, "DejaVuSans-Bold.ttf")],
}


def register_fonts():
    for name, candidates in FONT_CANDIDATES.items():
        path = next((p for p in candidates if os.path.isfile(p)), None)
        if path is None:
            sys.exit(f"'{name}' için yazı tipi bulunamadı. Denenenler:\n  " +
                     "\n  ".join(candidates))
        pdfmetrics.registerFont(TTFont(name, path))
    registerFontFamily("Sans", normal="Sans", bold="Sans-Bold", italic="Sans-Oblique",
                       boldItalic="Sans-BoldOblique")
    registerFontFamily("Mono", normal="Mono", bold="Mono-Bold", italic="Mono",
                       boldItalic="Mono-Bold")


parser = argparse.ArgumentParser(description="Kurulum ve kullanım kılavuzunu (PDF) üretir.")
parser.add_argument("--lang", choices=["tr", "en", "all"], default="all",
                    help="Üretilecek dil (varsayılan: ikisi)")
parser.add_argument("--output", default=None,
                    help="PDF'in yazılacağı yol (yalnızca tek dil üretilirken)")
args = parser.parse_args()
if args.output and args.lang == "all":
    parser.error("--output yalnızca --lang tr ya da --lang en ile verilebilir.")

register_fonts()
VERSION = read_product_version()
ZIP_NAME = f"PdmVariableStudio-{VERSION}"
REPO = "https://github.com/aSamed93/PdmVariableStudio"
RELEASES = REPO + "/releases/latest"
ISSUES = REPO + "/issues"

# Etkin dil; build() her dil için ayarlar.
LANG = "tr"


def T(tr, en):
    """Etkin dile göre iki metinden birini seçer (uygulamadaki Loc.T karşılığı)."""
    return en if LANG == "en" else tr


# --- renkler ---
INK = colors.HexColor("#1F2933")
MUTED = colors.HexColor("#5B6770")
ACCENT = colors.HexColor("#1D4E89")
ACCENT_LIGHT = colors.HexColor("#E8EFF8")
RULE = colors.HexColor("#D5DCE3")
CODE_BG = colors.HexColor("#F3F5F7")
OK = colors.HexColor("#1E7B34")
OK_BG = colors.HexColor("#E6F4EA")
WARN = colors.HexColor("#A15C00")
WARN_BG = colors.HexColor("#FFF4E0")
ERR = colors.HexColor("#B3261E")
ERR_BG = colors.HexColor("#FCE8E6")
GREY_BG = colors.HexColor("#F1F3F4")
LINK = "#1D4E89"

PAGE_W, PAGE_H = A4
MARGIN_L = MARGIN_R = 20 * mm
MARGIN_T = 22 * mm
MARGIN_B = 20 * mm
CONTENT_W = PAGE_W - MARGIN_L - MARGIN_R

# --- stiller ---
base = ParagraphStyle("base", fontName="Sans", fontSize=9.6, leading=14.2, textColor=INK,
                      spaceAfter=6)
body = base
small = ParagraphStyle("small", parent=base, fontSize=8.4, leading=11.8, textColor=MUTED)
cell = ParagraphStyle("cell", parent=base, fontSize=8.7, leading=12, spaceAfter=0)
cell_b = ParagraphStyle("cell_b", parent=cell, fontName="Sans-Bold")
cell_h = ParagraphStyle("cell_h", parent=cell, fontName="Sans-Bold", textColor=colors.white)
bullet = ParagraphStyle("bullet", parent=base, leftIndent=14, bulletIndent=3, spaceAfter=3,
                        bulletFontName="Sym")
step = ParagraphStyle("step", parent=base, leftIndent=18, bulletIndent=0, spaceAfter=4,
                      bulletFontName="Sans-Bold", bulletColor=ACCENT)
h1 = ParagraphStyle("h1", fontName="Sans-Bold", fontSize=19, leading=24, textColor=ACCENT,
                    spaceBefore=16, spaceAfter=10, keepWithNext=1)
h2 = ParagraphStyle("h2", fontName="Sans-Bold", fontSize=12.6, leading=17, textColor=INK,
                    spaceBefore=12, spaceAfter=6, keepWithNext=1)
h3 = ParagraphStyle("h3", fontName="Sans-Bold", fontSize=10.4, leading=14, textColor=ACCENT,
                    spaceBefore=8, spaceAfter=4, keepWithNext=1)
code_style = ParagraphStyle("code", fontName="Mono", fontSize=8.2, leading=11.2, textColor=INK)
toc_title = ParagraphStyle("toc_title", parent=h1)


def link(url, text=None):
    return f'<a href="{url}" color="{LINK}"><u>{text or url}</u></a>'


def ref(key, text):
    """Belge içi bağlantı."""
    return f'<a href="#{key}" color="{LINK}"><u>{text}</u></a>'


def c(t):
    """Satır içi kod."""
    return f'<font name="Mono" size="8.4" color="#2E3A45">{t}</font>'


# --- akış öğeleri ---
class Heading(Paragraph):
    def __init__(self, text, style, level, key):
        super().__init__(text, style)
        self.toc_level = level
        self.key = key
        self.plain = text


def H1(text, key):
    return Heading(text, h1, 0, key)


def H2(text, key):
    return Heading(text, h2, 1, key)


def H3(text, key):
    return Heading(text, h3, 2, key)


def P(t, st=body):
    return Paragraph(t, st)


def bullets(items):
    return [Paragraph(i, bullet, bulletText="•") for i in items]


def steps(items):
    out = []
    for n, i in enumerate(items, 1):
        out.append(Paragraph(i, step, bulletText=f"{n}."))
    return out


def code(lines):
    if isinstance(lines, str):
        lines = [lines]
    paras = [Paragraph(l.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
                       .replace(" ", "&nbsp;") or "&nbsp;", code_style) for l in lines]
    t = Table([[paras]], colWidths=[CONTENT_W])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), CODE_BG),
        ("BOX", (0, 0), (-1, -1), 0.5, RULE),
        ("LEFTPADDING", (0, 0), (-1, -1), 8), ("RIGHTPADDING", (0, 0), (-1, -1), 8),
        ("TOPPADDING", (0, 0), (-1, -1), 6), ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
    ]))
    return KeepTogether([t, Spacer(1, 6)])


def callout(kind, title, text):
    palette = {"note": (ACCENT, ACCENT_LIGHT, "i"), "warn": (WARN, WARN_BG, "!"),
               "ok": (OK, OK_BG, "✓"), "err": (ERR, ERR_BG, "✗")}
    fg, bg, sym = palette[kind]
    sym_p = Paragraph(f'<font name="Sym" color="{fg.hexval()}" size="12">{sym}</font>',
                      ParagraphStyle("s", parent=cell, alignment=TA_CENTER, leading=15))
    content = [Paragraph(f'<font color="{fg.hexval()}"><b>{title}</b></font>', cell)]
    if isinstance(text, str):
        text = [text]
    for t in text:
        content.append(Spacer(1, 2))
        content.append(Paragraph(t, cell))
    t = Table([[sym_p, content]], colWidths=[9 * mm, CONTENT_W - 9 * mm])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), bg),
        ("LINEBEFORE", (0, 0), (0, -1), 3, fg),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 6), ("RIGHTPADDING", (0, 0), (-1, -1), 8),
        ("TOPPADDING", (0, 0), (-1, -1), 6), ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
    ]))
    return KeepTogether([t, Spacer(1, 8)])


def table(rows, widths, header=True, first_bold=False, row_bgs=None):
    data = []
    for r_i, row in enumerate(rows):
        out = []
        for c_i, v in enumerate(row):
            if isinstance(v, Flowable) or isinstance(v, list):
                out.append(v)
                continue
            if header and r_i == 0:
                st = cell_h
            elif first_bold and c_i == 0:
                st = cell_b
            else:
                st = cell
            out.append(Paragraph(v, st))
        data.append(out)
    widths = [w * CONTENT_W for w in widths]
    t = Table(data, colWidths=widths, repeatRows=1 if header else 0)
    style = [
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LINEBELOW", (0, 0), (-1, -1), 0.5, RULE),
        ("LEFTPADDING", (0, 0), (-1, -1), 6), ("RIGHTPADDING", (0, 0), (-1, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 5), ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
    ]
    if header:
        style += [("BACKGROUND", (0, 0), (-1, 0), ACCENT)]
    if row_bgs:
        for r_i, bg in row_bgs.items():
            style.append(("BACKGROUND", (0, r_i), (0, r_i), bg))
    t.setStyle(TableStyle(style))
    return KeepTogether([t, Spacer(1, 8)]) if len(rows) <= 9 else [t, Spacer(1, 8)]


def status(sym, text, fg):
    return Paragraph(f'<font name="Sym" color="{fg.hexval()}">{sym}</font>  '
                     f'<font color="{fg.hexval()}"><b>{text}</b></font>', cell)


def flow_diagram():
    steps_ = [T("Dosyaları\nseç", "Select\nfiles"), T("Excel'e\naktar", "Export\nto Excel"),
              T("Excel'de\ndüzenle", "Edit\nin Excel"), T("Geri yükle\n& önizle", "Import\n& preview"),
              T("Onayla &\nuygula", "Confirm\n& apply"), T("Gerekirse\ngeri al", "Undo if\nneeded")]
    cells = []
    widths = []
    box_w = (CONTENT_W - 5 * 6 * mm) / 6
    st = ParagraphStyle("fd", parent=cell, alignment=TA_CENTER, fontName="Sans-Bold",
                        fontSize=8.4, leading=11, textColor=ACCENT)
    arrow = ParagraphStyle("ar", parent=cell, alignment=TA_CENTER, fontSize=12,
                           textColor=MUTED, leading=14)
    for i, s in enumerate(steps_):
        cells.append(Paragraph(s.replace("\n", "<br/>"), st))
        widths.append(box_w)
        if i < len(steps_) - 1:
            cells.append(Paragraph("→", arrow))
            widths.append(6 * mm)
    t = Table([cells], colWidths=widths, rowHeights=[15 * mm])
    style = [("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
             ("LEFTPADDING", (0, 0), (-1, -1), 2), ("RIGHTPADDING", (0, 0), (-1, -1), 2)]
    for i in range(0, len(cells), 2):
        style += [("BACKGROUND", (i, 0), (i, 0), ACCENT_LIGHT),
                  ("BOX", (i, 0), (i, 0), 0.8, ACCENT)]
    t.setStyle(TableStyle(style))
    return KeepTogether([t, Spacer(1, 10)])


# --- belge şablonu ---
class GuideDoc(BaseDocTemplate):
    def __init__(self, filename, **kw):
        super().__init__(filename, pagesize=A4, leftMargin=MARGIN_L, rightMargin=MARGIN_R,
                         topMargin=MARGIN_T, bottomMargin=MARGIN_B,
                         title=T("PDM Variable Studio — Kurulum ve Kullanım Kılavuzu",
                                 "PDM Variable Studio — Installation and User Guide"),
                         author="Abdussamed Tarlak",
                         subject=T("SOLIDWORKS PDM kart değişkenleri için Excel ile toplu "
                                   "düzenleme aracı",
                                   "Bulk editing of SOLIDWORKS PDM data card variables in Excel"),
                         creator="PDM Variable Studio",
                         lang=T("tr-TR", "en-US"), invariant=True, **kw)
        frame = Frame(MARGIN_L, MARGIN_B, CONTENT_W, PAGE_H - MARGIN_T - MARGIN_B, id="f",
                      leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
        self.addPageTemplates([
            PageTemplate(id="cover", frames=[frame], onPage=self.draw_cover),
            PageTemplate(id="normal", frames=[frame], onPage=self.draw_page),
        ])
        self.seen = set()

    def afterFlowable(self, fl):
        if isinstance(fl, Heading):
            key = fl.key
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(fl.getPlainText(), key, level=fl.toc_level,
                                      closed=fl.toc_level > 0)
            if fl.toc_level <= 1:
                self.notify("TOCEntry", (fl.toc_level, fl.getPlainText(), self.page, key))

    def draw_cover(self, canv, doc):
        canv.saveState()
        canv.setFillColor(ACCENT)
        canv.rect(0, PAGE_H - 118 * mm, PAGE_W, 118 * mm, stroke=0, fill=1)
        canv.setFillColor(colors.HexColor("#FFFFFF"))
        canv.setFont("Sans", 10)
        canv.drawString(MARGIN_L, PAGE_H - 30 * mm,
                        T("SOLIDWORKS PDM PROFESSIONAL İÇİN", "FOR SOLIDWORKS PDM PROFESSIONAL"))
        canv.setFont("Sans-Bold", 32)
        canv.drawString(MARGIN_L, PAGE_H - 50 * mm, "PDM Variable Studio")
        canv.setFont("Sans", 15)
        canv.drawString(MARGIN_L, PAGE_H - 62 * mm,
                        T("Kurulum ve Kullanım Kılavuzu", "Installation and User Guide"))
        canv.setStrokeColor(colors.HexColor("#8FB3DE"))
        canv.setLineWidth(1)
        canv.line(MARGIN_L, PAGE_H - 72 * mm, MARGIN_L + 60 * mm, PAGE_H - 72 * mm)
        canv.setFont("Sans", 10.5)
        canv.setFillColor(colors.HexColor("#DCE7F5"))
        lines = T(["Kart değişkenlerini Excel ile toplu düzenleyin —",
                   "önizlemeli, çakışma korumalı ve geri alınabilir."],
                  ["Bulk-edit data card variables in Excel —",
                   "with preview, conflict protection and undo."])
        for i, l in enumerate(lines):
            canv.drawString(MARGIN_L, PAGE_H - (82 + i * 6) * mm, l)
        canv.setFont("Sans", 9)
        canv.drawString(MARGIN_L, PAGE_H - 106 * mm,
                        T(f"Sürüm {VERSION}   ·   Ücretsiz   ·   MIT lisansı",
                          f"Version {VERSION}   ·   Free   ·   MIT license"))
        canv.restoreState()

    def draw_page(self, canv, doc):
        canv.saveState()
        canv.setStrokeColor(RULE)
        canv.setLineWidth(0.5)
        canv.line(MARGIN_L, PAGE_H - 13 * mm, PAGE_W - MARGIN_R, PAGE_H - 13 * mm)
        canv.setFont("Sans", 7.8)
        canv.setFillColor(MUTED)
        canv.drawString(MARGIN_L, PAGE_H - 11 * mm,
                        T("PDM Variable Studio — Kurulum ve Kullanım Kılavuzu",
                          "PDM Variable Studio — Installation and User Guide"))
        canv.drawRightString(PAGE_W - MARGIN_R, PAGE_H - 11 * mm,
                             T(f"Sürüm {VERSION}", f"Version {VERSION}"))
        canv.line(MARGIN_L, 13 * mm, PAGE_W - MARGIN_R, 13 * mm)
        canv.drawString(MARGIN_L, 9 * mm, "github.com/aSamed93/PdmVariableStudio")
        canv.linkURL(REPO, (MARGIN_L, 8 * mm, MARGIN_L + 60 * mm, 12 * mm), relative=0)
        canv.drawRightString(PAGE_W - MARGIN_R, 9 * mm, f"{doc.page}")
        # sayfa numarası → İçindekiler'e dönüş
        canv.linkRect("", "toc", (PAGE_W - MARGIN_R - 10 * mm, 8 * mm, PAGE_W - MARGIN_R,
                                  12 * mm), relative=0)
        canv.restoreState()


class Anchor(Flowable):
    """Görünmez yer imi (İçindekiler'e dönüş hedefi)."""
    def __init__(self, key):
        super().__init__()
        self.key = key
        self.width = self.height = 0

    def draw(self):
        self.canv.bookmarkPage(self.key)


# ================= İÇERİK =================
def build_story():
    story = []

    # --- kapak ---
    story.append(Spacer(1, 108 * mm))
    cover_intro = ParagraphStyle("ci", parent=base, fontSize=10.2, leading=15.5)
    story.append(P(T(
        "<b>Bu belge kimin için?</b> PDM Variable Studio'yu kendi SOLIDWORKS PDM "
        "ortamında kurmak ve kullanmak isteyen herkes için. Kurulumu yapacak "
        "<b>PDM yöneticisi</b> de, aracı günlük işte kullanacak <b>mühendis / "
        "dokümantasyon ekibi</b> de aradığı her şeyi burada bulur. Başka bir yardıma "
        "ihtiyaç duymadan baştan sona ilerleyebilmeniz amaçlandı.",
        "<b>Who is this document for?</b> Anyone who wants to install and use PDM Variable "
        "Studio in their own SOLIDWORKS PDM environment. Both the <b>PDM administrator</b> who "
        "installs it and the <b>engineering / documentation team</b> who use it every day will "
        "find everything they need here. It is meant to take you from start to finish without "
        "any other help."), cover_intro))
    story.append(Spacer(1, 4))
    story.append(table([
        ["", ""],
        [T("Hedef", "Target"), T("SOLIDWORKS PDM <b>Professional</b> 2022 (30.0) ve üstü",
                                 "SOLIDWORKS PDM <b>Professional</b> 2022 (30.0) or later")],
        ["Platform", "Windows 10 / 11, .NET Framework 4.8.1"],
        [T("Dil", "Language"), T("Türkçe ve İngilizce — tek kurulum", "Turkish and English — one setup")],
        [T("İndir", "Download"), link(RELEASES, T("GitHub Releases — son sürüm",
                                                  "GitHub Releases — latest version"))],
        [T("Kaynak kod", "Source code"), link(REPO, "github.com/aSamed93/PdmVariableStudio")],
        [T("Sorun bildir", "Report an issue"), link(ISSUES, "GitHub Issues")],
        [T("Gizlilik", "Privacy"), T("Hiçbir yere veri göndermez; tüm kayıtlar kendi bilgisayarınızda kalır",
                                     "Sends no data anywhere; all records stay on your own computer")],
    ], [0.22, 0.78], header=False, first_bold=True))
    story.append(Spacer(1, 6))
    story.append(callout("warn", T("Başlamadan önce", "Before you start"), [T(
        "Araç PDM'ye <b>yazar</b>. İlk denemeyi bir <b>test vault'unda</b> ya da küçük bir test "
        "klasöründe yapın ve vault yedeğinizin güncel olduğundan emin olun.",
        "The tool <b>writes</b> to PDM. Make your first attempts in a <b>test vault</b> or a small "
        "test folder, and make sure your vault backup is up to date.")]))
    story.append(P(T(
        "SOLIDWORKS ve SOLIDWORKS PDM, Dassault Systèmes SolidWorks Corp. şirketinin "
        "tescilli markalarıdır. Bu proje bağımsızdır; Dassault Systèmes ile bağlantılı "
        "değildir, onun tarafından onaylanmamış ya da desteklenmemiştir.",
        "SOLIDWORKS and SOLIDWORKS PDM are registered trademarks of Dassault Systèmes "
        "SolidWorks Corp. This project is independent; it is not affiliated with, endorsed or "
        "supported by Dassault Systèmes."), small))
    story.append(NextPageTemplate("normal"))
    story.append(PageBreak())

    # --- içindekiler ---
    story.append(Anchor("toc"))
    story.append(P(T("İçindekiler", "Contents"), toc_title))
    story.append(P(T(
        "Başlıklar tıklanabilir. Her sayfanın altındaki sayfa numarası sizi buraya "
        "geri getirir; aynı başlıklar PDF okuyucunuzun yer imleri panelinde de var.",
        "Headings are clickable. The page number at the bottom of every page brings you back "
        "here; the same headings are also in your PDF reader's bookmarks panel."), small))
    toc = TableOfContents()
    toc.levelStyles = [
        ParagraphStyle("t0", fontName="Sans-Bold", fontSize=10.3, leading=13, leftIndent=0,
                       firstLineIndent=0, spaceBefore=2, textColor=ACCENT),
        ParagraphStyle("t1", fontName="Sans", fontSize=9.1, leading=10.8, leftIndent=14,
                       firstLineIndent=0, textColor=INK),
    ]
    toc.dotsMinLevel = 0
    story.append(toc)
    story.append(PageBreak())

    # ================= 1. GENEL BAKIŞ =================
    story.append(H1(T("1. Genel bakış", "1. Overview"), "s1"))
    story.append(P(T(
        "PDM Variable Studio, SOLIDWORKS PDM Professional'daki dosyaların <b>kart "
        "değişkenlerini</b> (data card değerlerini) Excel ile toplu olarak düzenlemenizi "
        "sağlar. Yüzlerce dosyanın açıklamasını, malzemesini, proje kodunu ya da revizyon "
        "notunu tek tek kart açarak değiştirmek yerine hepsini bir tabloda düzenler, "
        "tek seferde geri yüklersiniz.",
        "PDM Variable Studio lets you bulk-edit the <b>data card variables</b> of files in "
        "SOLIDWORKS PDM Professional in Excel. Instead of opening cards one by one to change "
        "the description, material, project code or revision note of hundreds of files, you "
        "edit them all in one table and load them back in one go.")))
    story.append(flow_diagram())

    story.append(H2(T("1.1 Neden basit bir Excel aktarımından farklı?",
                      "1.1 Why is this different from a simple Excel export?"), "s1-1"))
    story.append(P(T(
        "Toplu güncellemenin asıl riski Excel değil, <b>eşzamanlılıktır</b>. Siz dosyayı "
        "dışa aktarıp Excel'de çalışırken bir iş arkadaşınız aynı kartı PDM'de "
        "değiştirmiş olabilir. Bunu görmezden gelen bir araç onun işini sessizce siler. "
        "PDM Variable Studio bu yüzden her hücre için <b>üç değeri</b> birden "
        "karşılaştırır:",
        "The real risk of a bulk update is not Excel but <b>concurrency</b>. While you export "
        "the file and work in Excel, a colleague may change the same card in PDM. A tool that "
        "ignores this silently wipes out their work. That is why PDM Variable Studio compares "
        "<b>three values</b> for every cell:")))
    story.append(table([
        [T("Değer", "Value"), T("Anlamı", "Meaning")],
        [T("Dışa aktarımdaki", "At export"), T("Excel dosyasını oluşturduğunuz andaki PDM değeri",
                                                "The PDM value when you created the Excel file")],
        [T("Excel'deki yeni", "New in Excel"), T("Sizin Excel'de bıraktığınız değer",
                                                  "The value you left in Excel")],
        [T("PDM'deki güncel", "Current in PDM"), T("Dosyayı geri yüklediğiniz andaki taze PDM değeri",
                                                    "The fresh PDM value when you load the file back")],
    ], [0.28, 0.72], first_bold=True))
    story.append(P(T(
        "Yalnızca <b>sizin değiştirdiğiniz ve PDM tarafında o arada kimsenin dokunmadığı</b> "
        "hücreler yazılır. Başkası da değiştirdiyse hücre <b>çakışma</b> olarak "
        "işaretlenir ve otomatik olarak yazılmaz. Ayrıntı: "
        + ref("s4-5", "4.5 İçe aktarma ve önizleme") + ".",
        "Only cells <b>that you changed and that nobody touched in PDM in the meantime</b> are "
        "written. If someone else changed it too, the cell is marked as a <b>conflict</b> and "
        "is not written automatically. Details: "
        + ref("s4-5", "4.5 Import and preview") + ".")))

    story.append(H2(T("1.2 Öne çıkan özellikler", "1.2 Key features"), "s1-2"))
    story += bullets(T([
        "<b>Önizleme olmadan yazma yok.</b> Her hücrenin neden yazılacağı ya da yazılmayacağı "
        "uygulamadan önce gösterilir.",
        "<b>Sessiz check-out yok.</b> Siz açıkça onaylamadan hiçbir dosya check-out edilmez. "
        "Check-in yalnızca aracın kendi çektiği dosyalara yapılır; sizin üzerinde çalıştığınız "
        "dosyalara dokunulmaz.",
        "<b>Güvenli geri alma.</b> Her işlem kaydedilir. Geri alırken PDM'deki güncel değer "
        "kontrol edilir; arada başkası değiştirdiyse onun işi korunur.",
        "<b>Konfigürasyonlar ayrı.</b> Konfigürasyonlu SOLIDWORKS dosyalarında her konfigürasyon "
        "ayrı satırdır.",
        "<b>Veritabanı ile dosya ayrışmaz.</b> Değerler hem PDM veritabanına hem de fiziksel "
        "dosyanın özel özelliklerine (custom properties) yazılır.",
        "<b>Bir dosyanın hatası diğerlerini durdurmaz.</b> Sorunlu dosya atlanır, gerekçesi "
        "gösterilir.",
        "<b>Explorer'ı yormaz.</b> Uygulama PDM Explorer'ın içinde değil, ayrı bir pencerede "
        "(ayrı süreçte) çalışır; bir hata Explorer'ı düşüremez.",
        "<b>Türkçe ve İngilizce.</b> Tek kurulum; her bilgisayar kendi dilini seçer ("
        + ref("s4-9", "4.9") + ").",
    ], [
        "<b>No writes without a preview.</b> Before applying, you see why each cell will or "
        "will not be written.",
        "<b>No silent check-out.</b> No file is checked out unless you explicitly agree. Only "
        "the files the tool itself checked out are checked in; files you are working on are "
        "left alone.",
        "<b>Safe undo.</b> Every operation is recorded. When undoing, the current value in PDM "
        "is checked; if someone else changed it in the meantime, their work is preserved.",
        "<b>Configurations are separate.</b> In SOLIDWORKS files with configurations, each "
        "configuration is a separate row.",
        "<b>Database and file stay in sync.</b> Values are written both to the PDM database and "
        "to the custom properties of the physical file.",
        "<b>An error in one file does not stop the others.</b> The problem file is skipped and "
        "the reason is shown.",
        "<b>Easy on Explorer.</b> The application runs in its own window (a separate process), "
        "not inside PDM Explorer; an error cannot bring Explorer down.",
        "<b>Turkish and English.</b> One setup; each computer picks its own language ("
        + ref("s4-9", "4.9") + ").",
    ]))

    story.append(H2(T("1.3 İki parçalı yapı", "1.3 Two-part structure"), "s1-3"))
    story.append(P(T(
        "Kurulumu anlamak için bilmeniz gereken tek teknik ayrıntı şu: araç iki "
        "parçadan oluşur ve <b>ikisi de gereklidir</b>.",
        "The only technical detail you need to understand the installation: the tool consists "
        "of two parts, and <b>both are required</b>.")))
    story.append(table([
        [T("Parça", "Part"), T("Nereye kurulur", "Installed where"), T("Kim kurar", "Installed by"),
         T("Ne işe yarar", "Purpose")],
        [T("<b>Uygulama</b>", "<b>Application</b>") + "<br/>PdmVariableStudio.exe",
         T("Her kullanıcının bilgisayarına (ya da bir ağ paylaşımına)",
           "On every user's computer (or on a network share)"),
         T("Bilgisayarın yöneticisi / BT", "Computer administrator / IT"),
         T("Asıl pencere: dışa aktarma, önizleme, uygulama, geri alma",
           "The main window: export, preview, apply, undo")],
        [T("<b>Eklenti</b>", "<b>Add-in</b>") + "<br/>PdmVariableStudio.AddIn.dll",
         T("Vault'a, <b>bir kez</b>", "In the vault, <b>once</b>"),
         T("PDM yöneticisi", "PDM administrator"),
         T("PDM Explorer'a menü komutunu ekler ve uygulamayı başlatır",
           "Adds the menu command to PDM Explorer and starts the application")],
    ], [0.28, 0.25, 0.18, 0.29]))
    story.append(P(T(
        "Eklenti vault'a bir kez yüklendiğinde PDM onu vault'a bağlanan tüm istemcilere "
        "kendiliğinden dağıtır. Uygulama ise vault'ta değil, diskte durur; bu yüzden "
        "uygulama güncellemeleri için vault'a dokunmak gerekmez.",
        "Once the add-in is added to the vault, PDM distributes it automatically to every "
        "client that connects to the vault. The application lives on disk, not in the vault, "
        "so application updates never require touching the vault.")))
    story.append(CondPageBreak(150 * mm))

    # ================= 2. BAŞLAMADAN ÖNCE =================
    story.append(H1(T("2. Başlamadan önce", "2. Before you start"), "s2"))
    story.append(H2(T("2.1 Gereksinimler", "2.1 Requirements"), "s2-1"))
    story.append(table([
        ["", T("Gereksinim", "Requirement")],
        [T("PDM istemcisi", "PDM client"),
         T("SOLIDWORKS PDM <b>Professional</b> 2022 (30.0) veya üstü. PDM "
           "<b>Standard</b> desteklenmez.",
           "SOLIDWORKS PDM <b>Professional</b> 2022 (30.0) or later. PDM "
           "<b>Standard</b> is not supported.")],
        ["Windows", T("Windows 10 veya 11", "Windows 10 or 11")],
        [".NET Framework", T("4.8.1 — Windows 11'de hazır gelir; Windows 10'da Windows Update ile gelir",
                             "4.8.1 — included in Windows 11; delivered by Windows Update on Windows 10")],
        ["Vault view", T("Aracı kullanacak her bilgisayarda o vault için yerel bir <i>vault view</i> "
                         "bulunmalı",
                         "Every computer that uses the tool needs a local <i>vault view</i> for "
                         "that vault")],
        [T("Tablo programı", "Spreadsheet"), T("Microsoft Excel (LibreOffice Calc da çalışır)",
                                               "Microsoft Excel (LibreOffice Calc also works)")],
    ], [0.22, 0.78], first_bold=True))

    story.append(H2(T("2.2 Gerekli yetkiler", "2.2 Required permissions"), "s2-2"))
    story.append(table([
        [T("İş", "Task"), T("Gereken yetki", "Permission needed")],
        [T("Uygulamayı <b>Program Files</b> altına kurmak", "Installing the application under "
           "<b>Program Files</b>"),
         T("Windows yönetici hakkı (bir kez). Yönetici hakkınız yoksa kullanıcı klasörüne "
           "kurabilirsiniz — " + ref("s3-2", "bkz. 3.2") + ".",
           "Windows administrator rights (once). Without administrator rights you can install "
           "into your user folder — " + ref("s3-2", "see 3.2") + ".")],
        [T("Eklentiyi vault'a yüklemek", "Adding the add-in to the vault"),
         T("PDM Administration'da eklenti ekleme yetkisi olan bir hesap (genellikle <i>Admin</i>)",
           "An account with permission to add add-ins in PDM Administration (usually <i>Admin</i>)")],
        [T("Kart değerlerini değiştirmek", "Changing card values"),
         T("Normal PDM kullanıcı yetkileriniz geçerlidir. Araç, PDM'de sizin yapamayacağınız "
           "hiçbir şeyi yapamaz; tüm işlemler PDM API'si üzerinden, PDM'in kurallarına uyarak "
           "yapılır.",
           "Your normal PDM user permissions apply. The tool cannot do anything in PDM that you "
           "cannot do yourself; everything goes through the PDM API and follows PDM's rules.")],
    ], [0.34, 0.66]))

    story.append(H2(T("2.3 Kontrol listesi", "2.3 Checklist"), "s2-3"))
    story.append(P(T("Kuruluma geçmeden önce:", "Before you install:")))
    story += bullets(T([
        "PDM Professional istemcisi kurulu ve vault'ta oturum açabiliyorsunuz.",
        c("C:\\Program Files\\SOLIDWORKS PDM\\EPDM.Interop.epdm.dll") + " dosyası mevcut "
        "(PDM istemcisiyle gelir; kurulumda gerekecek).",
        "Eklentiyi vault'a yükleyecek bir PDM yönetici hesabına erişiminiz var ya da PDM "
        "yöneticinizle konuştunuz.",
        "İlk denemeler için bir test vault'u veya küçük bir test klasörü belirlediniz.",
    ], [
        "The PDM Professional client is installed and you can log in to the vault.",
        "The file " + c("C:\\Program Files\\SOLIDWORKS PDM\\EPDM.Interop.epdm.dll") + " exists "
        "(it comes with the PDM client; the installation needs it).",
        "You have access to a PDM administrator account to add the add-in to the vault, or you "
        "have talked to your PDM administrator.",
        "You have picked a test vault or a small test folder for your first attempts.",
    ]))
    story.append(callout("note", T("Kısa yol", "Shortcut"), [T(
        "Eklentiyi yüklemeden önce yalnızca uygulamayı kurup doğrudan çalıştırarak aracı "
        "deneyebilirsiniz (" + ref("s3-2", "3.2") + " ve " + ref("s4-1", "4.1") + "). Bu, vault "
        "ayarlarına hiç dokunmadan aracı tanımanın en hızlı yoludur.",
        "You can try the tool before adding the add-in by installing just the application and "
        "starting it directly (" + ref("s3-2", "3.2") + " and " + ref("s4-1", "4.1") + "). This "
        "is the quickest way to get to know the tool without touching any vault settings.")]))
    story.append(CondPageBreak(110 * mm))

    # ================= 3. KURULUM =================
    story.append(H1(T("3. Kurulum", "3. Installation"), "s3"))
    story.append(P(T(
        "Kurulum dört adımdır: <b>indir → uygulamayı kur → eklentiyi vault'a yükle → "
        "doğrula.</b> Toplam süre genellikle 10–15 dakikadır.",
        "Installation has four steps: <b>download → install the application → add the add-in "
        "to the vault → verify.</b> It usually takes 10–15 minutes in total.")))

    story.append(H2(T("3.1 Paketi indirme ve doğrulama", "3.1 Downloading and verifying the package"),
                    "s3-1"))
    story += steps(T([
        f"{link(RELEASES, 'GitHub Releases')} sayfasını açın ve en son sürümün altındaki "
        + c("PdmVariableStudio-x.y.z.zip") + " dosyasını indirin (örneğin "
        + c(ZIP_NAME + ".zip") + ").",
        "<b>İsteğe bağlı ama önerilir — dosyanın bozulmadığını doğrulayın.</b> İndirdiğiniz "
        "klasörde PowerShell açıp aşağıdaki komutu çalıştırın ve çıkan değeri yayım sayfasında "
        "yazan SHA-256 değeriyle karşılaştırın:",
    ], [
        f"Open the {link(RELEASES, 'GitHub Releases')} page and download "
        + c("PdmVariableStudio-x.y.z.zip") + " under the latest version (for example "
        + c(ZIP_NAME + ".zip") + ").",
        "<b>Optional but recommended — verify that the file is intact.</b> Open PowerShell in "
        "the download folder, run the command below and compare the result with the SHA-256 "
        "value on the release page:",
    ]))
    story.append(code([f"Get-FileHash .\\{ZIP_NAME}.zip -Algorithm SHA256"]))
    # Numaralandırmayı sürdürmek için elle:
    for n, t in [(3, T("<b>Engellemeyi kaldırın.</b> İnternetten indirilen zip dosyalarını Windows "
                       "işaretler. Zip'e sağ tıklayın → <b>Özellikler</b> → alttaki <b>Engellemeyi "
                       "kaldır</b> (Unblock) kutusunu işaretleyin → <b>Tamam</b>. Kutu yoksa bu adımı "
                       "atlayın.",
                       "<b>Unblock it.</b> Windows marks zip files downloaded from the internet. "
                       "Right-click the zip → <b>Properties</b> → tick <b>Unblock</b> at the "
                       "bottom → <b>OK</b>. If there is no such box, skip this step.")),
                 (4, T("Zip'i bir klasöre çıkarın (sağ tık → <b>Tümünü ayıkla…</b>).",
                       "Extract the zip to a folder (right-click → <b>Extract All…</b>)."))]:
        story.append(Paragraph(t, step, bulletText=f"{n}."))
    story.append(Spacer(1, 4))
    story.append(P(T("Çıkardığınız klasörün içeriği:", "Contents of the extracted folder:")))
    story.append(table([
        [T("Dosya / klasör", "File / folder"), T("Ne işe yarar", "Purpose")],
        [c("App\\"), T("Uygulama dosyaları (exe + 3 DLL)", "Application files (exe + 3 DLLs)")],
        [c("AddIn\\PdmVariableStudio.AddIn.dll"), T("Vault'a yüklenecek eklenti",
                                                    "The add-in to add to the vault")],
        [c("install-app.ps1"), T("Uygulamayı kuran betik", "Script that installs the application")],
        [c("BENIOKU.txt") + ", " + c("README.txt"), T("Üç adımlık kısa özet (Türkçe, İngilizce)",
                                                      "Three-step summary (Turkish, English)")],
        [c("KULLANIM.md") + ", " + c("USAGE.md"), T("Kısa kullanım kılavuzu (bu belgenin özeti; "
                                                    "Türkçe, İngilizce)",
                                                    "Short user guide (a summary of this document; "
                                                    "Turkish, English)")],
        [c("CHANGELOG.md") + ", " + c("LICENSE"), T("Sürüm notları ve lisans",
                                                    "Release notes and license")],
    ], [0.42, 0.58]))
    story.append(callout("note", T("EPDM.Interop.epdm.dll neden pakette yok?",
                                   "Why is EPDM.Interop.epdm.dll not in the package?"), [T(
        "Bu dosya Dassault Systèmes'e aittir ve yeniden dağıtılamaz. Her PDM istemcisinde zaten "
        "vardır: " + c("C:\\Program Files\\SOLIDWORKS PDM\\") + ". Kurulum betiği onu oradan kendisi "
        "kopyalar; eklenti için de aynı yerden seçeceksiniz.",
        "This file belongs to Dassault Systèmes and cannot be redistributed. Every PDM client "
        "already has it: " + c("C:\\Program Files\\SOLIDWORKS PDM\\") + ". The installation "
        "script copies it from there; for the add-in you will select it from the same place.")]))

    story.append(H2(T("3.2 Uygulamayı kurma (her bilgisayara)",
                      "3.2 Installing the application (on every computer)"), "s3-2"))
    story.append(H3(T("Kurulum dosyasıyla (en kolay)", "With the setup file (easiest)"), "s3-2s"))
    story += steps(T([
        f"{link(RELEASES, 'GitHub Releases')} sayfasından "
        + c(f"PdmVariableStudio-Setup-{VERSION}.exe") + " dosyasını indirip çalıştırın. "
        "Uygulama imzasız olduğu için Windows SmartScreen uyarı verebilir: <b>Daha fazla bilgi → "
        "Yine de çalıştır</b>.",
        "Sihirbazı izleyin. İlk sayfada seçtiğiniz dil (<b>Türkçe</b> / <b>English</b>) uygulamanın "
        "bu bilgisayardaki dili olur (bkz. " + ref("s4-9", "4.9") + "). Kurulum önce PDM "
        "istemcisinin kurulu olduğunu denetler; .NET Framework 4.8.1 eksikse indirip kurar.",
        "Bitişte eklentinin iki dosyasının hazırlandığı " + c("C:\\Program Files\\PDM Variable Studio\\AddIn\\")
        + " klasörü açılır. Bu iki dosyayı " + ref("s3-3", "3.3") + "'teki gibi vault'a yükleyin.",
    ], [
        f"Download {c(f'PdmVariableStudio-Setup-{VERSION}.exe')} from the "
        f"{link(RELEASES, 'GitHub Releases')} page and run it. Because the application is not "
        "code-signed, Windows SmartScreen may warn you: <b>More info → Run anyway</b>.",
        "Follow the wizard. The language you choose on the first page (<b>Türkçe</b> / "
        "<b>English</b>) becomes the application's language on this computer (see "
        + ref("s4-9", "4.9") + "). Setup first checks that the PDM client is installed; if "
        ".NET Framework 4.8.1 is missing it downloads and installs it.",
        "At the end the folder " + c("C:\\Program Files\\PDM Variable Studio\\AddIn\\")
        + " opens, with the add-in's two files ready. Add these two files to the vault as "
        "described in " + ref("s3-3", "3.3") + ".",
    ]))
    story.append(P(T("Kaldırmak için <b>Ayarlar → Uygulamalar</b>. Kaldırma, işlem geçmişinize ve "
                     "ayarlarınıza dokunmaz.",
                     "To uninstall, use <b>Settings → Apps</b>. Uninstalling does not touch your "
                     "operation history or settings.")))
    story.append(H3(T("Zip paketiyle kurulum", "Installing from the zip package"), "s3-2a"))
    story += steps(T([
        "Başlat menüsünde <b>PowerShell</b> yazın, sağ tıklayın → <b>Yönetici olarak çalıştır</b>.",
        "Zip'i çıkardığınız klasöre gidin (yolu kendinize göre düzeltin):",
    ], [
        "Type <b>PowerShell</b> in the Start menu, right-click → <b>Run as administrator</b>.",
        "Go to the folder where you extracted the zip (adjust the path to yours):",
    ]))
    story.append(code([f'cd "$env:USERPROFILE\\Downloads\\{ZIP_NAME}"']))
    story.append(Paragraph(T("Kurulum betiğini çalıştırın:", "Run the installation script:"),
                           step, bulletText="3."))
    story.append(code(["powershell -ExecutionPolicy Bypass -File install-app.ps1"]))
    story.append(P(T(
        "Betik uygulamayı <b>C:\\Program Files\\PDM Variable Studio\\</b> klasörüne "
        "kopyalar, PDM istemcinizden interop dosyasını alır ve eklentinin uygulamayı "
        "bulabilmesi için yolu kayıt defterine yazar. Sonunda yeşil renkte "
        "<b>Kurulum tamam.</b> yazısını görmelisiniz.",
        "The script copies the application to <b>C:\\Program Files\\PDM Variable Studio\\</b>, "
        "takes the interop file from your PDM client and writes the path to the registry so "
        "the add-in can find the application. At the end you should see "
        "<b>Kurulum tamam.</b> (installation complete) in green. The interface language then "
        "follows Windows; you can change it in the application (" + ref("s4-9", "4.9") + ").")))
    story.append(callout("note", T("“-ExecutionPolicy Bypass” güvenli mi?",
                                   "Is “-ExecutionPolicy Bypass” safe?"), [T(
        "Evet. Bu seçenek yalnızca o tek komut için geçerlidir; bilgisayarın PowerShell "
        "ayarlarını kalıcı olarak değiştirmez. Betiğin ne yaptığını görmek isterseniz "
        f"{c('install-app.ps1')} dosyasını Not Defteri ile açabilirsiniz; tamamı Türkçe "
        "açıklamalıdır.",
        "Yes. The option applies to that single command only; it does not permanently change "
        "the computer's PowerShell settings. To see what the script does, open "
        f"{c('install-app.ps1')} in Notepad (its comments and messages are in Turkish).")]))

    story.append(H3(T("Yönetici hakkı olmadan kurulum", "Installing without administrator rights"),
                    "s3-2b"))
    story.append(P(T(
        "Yönetici hakkınız yoksa uygulamayı kendi kullanıcı klasörünüze kurabilirsiniz. "
        "Normal (yönetici olmayan) bir PowerShell penceresinde:",
        "Without administrator rights you can install the application into your own user "
        "folder. In a normal (non-administrator) PowerShell window:")))
    story.append(code(['powershell -ExecutionPolicy Bypass -File install-app.ps1 -CurrentUser `',
                       '    -Destination "$env:LOCALAPPDATA\\PDM Variable Studio"']))

    story.append(H3(T("Ağ paylaşımından kurulum (çok bilgisayarlı ortam)",
                      "Installing from a network share (multiple computers)"), "s3-2c"))
    story.append(P(T(
        "Uygulamayı bir kez ağ paylaşımına kopyalayıp her bilgisayarda yalnızca yolu "
        "kaydedebilirsiniz. Böylece güncelleme için tek bir klasörü değiştirmek yeter.",
        "You can copy the application to a network share once and only register the path on "
        "each computer. Updating then means replacing a single folder.")))
    share = T("\\\\sunucu\\pdm\\VariableStudio", "\\\\server\\pdm\\VariableStudio")
    story.append(code([T("# 1) Bir kez: paylaşıma kopyala", "# 1) Once: copy to the share"),
                       "powershell -ExecutionPolicy Bypass -File install-app.ps1 `",
                       f'    -Destination "{share}"',
                       "",
                       T("# 2) Her bilgisayarda (yönetici): yalnızca kaydet",
                         "# 2) On every computer (administrator): register only"),
                       "powershell -ExecutionPolicy Bypass -File install-app.ps1 -RegisterOnly `",
                       f'    -Destination "{share}"']))
    story.append(callout("warn", T("Exe dosyasını tek başına kopyalamayın",
                                   "Do not copy the exe on its own"), [T(
        "Uygulama yanındaki DLL dosyalarına ihtiyaç duyar. Yalnızca "
        f"{c('PdmVariableStudio.exe')} kopyalanırsa uygulama açılmadan sessizce kapanır. "
        "Her zaman kurulum betiğini kullanın.",
        "The application needs the DLL files next to it. If only "
        f"{c('PdmVariableStudio.exe')} is copied, it closes silently without opening. "
        "Always use the installation script.")]))

    story.append(H2(T("3.3 Eklentiyi vault'a yükleme (bir kez)",
                      "3.3 Adding the add-in to the vault (once)"), "s3-3"))
    story.append(P(T(
        "Bu adımı <b>PDM yöneticisi</b> yapar ve vault başına <b>yalnızca bir kez</b> "
        "gerekir. PDM, eklentiyi vault'a bağlanan tüm istemcilere kendisi dağıtır.",
        "This step is done by the <b>PDM administrator</b> and is needed <b>only once</b> per "
        "vault. PDM distributes the add-in to every client that connects to the vault. There is "
        "one add-in for both languages; each client shows it in its own language.")))
    story += steps(T([
        "<b>SOLIDWORKS PDM Administration</b> aracını açın ve vault'a yönetici hesabıyla oturum açın.",
        "Sol ağaçta vault'u genişletin, <b>Add-ins</b> düğümüne sağ tıklayın → <b>New Add-in…</b>",
        "Açılan pencerede <b>şu iki dosyayı birlikte</b> seçin (Ctrl tuşuyla):",
    ], [
        "Open <b>SOLIDWORKS PDM Administration</b> and log in to the vault with an administrator "
        "account.",
        "Expand the vault in the left tree, right-click the <b>Add-ins</b> node → <b>New Add-in…</b>",
        "In the dialog, select <b>these two files together</b> (with the Ctrl key):",
    ]))
    story.append(table([
        [T("Dosya", "File"), T("Nerede", "Where")],
        [c("PdmVariableStudio.AddIn.dll"),
         T("Kurulum dosyasıyla kurduysanız " + c("C:\\Program Files\\PDM Variable Studio\\AddIn\\")
           + "; zip'le kurduysanız çıkardığınız klasördeki " + c("AddIn\\") + " klasöründe",
           "If installed with the setup file, " + c("C:\\Program Files\\PDM Variable Studio\\AddIn\\")
           + "; if installed from the zip, the " + c("AddIn\\") + " folder in the extracted folder")],
        [c("EPDM.Interop.epdm.dll"),
         T("Kurulum dosyasıyla kurduysanız aynı " + c("AddIn\\") + " klasöründe hazır; zip'le "
           "kurduysanız " + c("C:\\Program Files\\SOLIDWORKS PDM\\")
           + " — pakette yoktur, PDM istemcinizden alın",
           "If installed with the setup file, ready in the same " + c("AddIn\\") + " folder; if "
           "installed from the zip, " + c("C:\\Program Files\\SOLIDWORKS PDM\\")
           + " — it is not in the package, take it from your PDM client")],
    ], [0.38, 0.62]))
    for n, t in [(4, T("<b>Aç</b>'a tıklayın, eklenti bilgilerini gösteren pencerede <b>Tamam</b> "
                       "diyerek yüklemeyi tamamlayın. Add-ins altında <b>PDM Variable Studio</b> "
                       "görünmelidir.",
                       "Click <b>Open</b>, then <b>OK</b> in the window showing the add-in "
                       "information. <b>PDM Variable Studio</b> should appear under Add-ins.")),
                 (5, T("<b>Tüm</b> PDM Explorer ve Administration pencerelerini kapatın, sonra "
                       "yeniden açın. PDM eklentileri çalışan pencerelere yüklenmez.",
                       "Close <b>all</b> PDM Explorer and Administration windows, then reopen "
                       "them. PDM add-ins are not loaded into running windows."))]:
        story.append(Paragraph(t, step, bulletText=f"{n}."))
    story.append(Spacer(1, 4))
    story.append(callout("err", T("En sık yapılan hata: ikinci dosyayı atlamak",
                                  "The most common mistake: skipping the second file"), [T(
        f"{c('EPDM.Interop.epdm.dll')} eklentiyle <b>birlikte</b> seçilmezse PDM "
        "<i>“…is not a multi-threaded COM-server”</i> gibi yanıltıcı bir hata verir; mesaj eksik "
        "dosyadan hiç söz etmez. Bu hatayı görürseniz eklentiyi silip iki dosyayla yeniden ekleyin.",
        f"If {c('EPDM.Interop.epdm.dll')} is not selected <b>together</b> with the add-in, PDM "
        "shows a misleading error such as <i>“…is not a multi-threaded COM-server”</i>; the "
        "message never mentions the missing file. If you see it, remove the add-in and add it "
        "again with both files.")]))

    story.append(H2(T("3.4 Kurulumu doğrulama", "3.4 Verifying the installation"), "s3-4"))
    story.append(P(T("Aşağıdakilerin hepsi doğruysa kurulum tamamdır:",
                     "The installation is complete if all of the following are true:")))
    story += bullets(T([
        "PDM Explorer'da vault içindeki bir klasörde <b>Araçlar</b> menüsünde (ya da sağ tık "
        "menüsünde) <b>PDM Variable Studio</b> komutu görünüyor.",
        "Komuta tıklayınca ayrı bir pencere açılıyor; üstte doğru <b>vault adı</b> ve <b>klasör</b> "
        "yazıyor.",
        "<b>Dışa Aktar</b> sekmesinin altındaki değişken listesi vault'unuzun değişken adlarıyla doluyor.",
        "Pencerenin <b>Hakkında</b> düğmesi doğru sürüm numarasını gösteriyor.",
    ], [
        "In PDM Explorer, in a folder of the vault, the <b>PDM Variable Studio</b> command appears "
        "in the <b>Tools</b> menu (or the right-click menu).",
        "Clicking the command opens a separate window showing the correct <b>vault name</b> and "
        "<b>folder</b> at the top.",
        "The variable list at the bottom of the <b>Export</b> tab fills with your vault's variable "
        "names.",
        "The window's <b>About</b> button shows the correct version number.",
    ]))
    story.append(P(T("Bir şey yolunda gitmediyse " + ref("s6", "6. Sorun giderme") + " bölümüne bakın.",
                     "If something went wrong, see " + ref("s6", "6. Troubleshooting") + ".")))

    story.append(H2(T("3.5 Ekip kurulumu için ek ayarlar", "3.5 Additional settings for teams"),
                    "s3-5"))
    story.append(P(T(
        "<b>Ortak işlem geçmişi.</b> Varsayılan olarak işlem geçmişi her kullanıcının "
        "kendi bilgisayarında tutulur; bir işlem yalnızca yapıldığı bilgisayarda, aynı "
        "Windows hesabıyla geri alınabilir. Ekip olarak ortak bir geçmiş isteniyorsa "
        "geçmiş bir ağ paylaşımına yönlendirilebilir:",
        "<b>Shared operation history.</b> By default the operation history is kept on each "
        "user's own computer; an operation can only be undone on the computer where it was "
        "made, with the same Windows account. If the team wants a shared history, it can be "
        "redirected to a network share:")))
    journal_share = T("\\\\sunucu\\pdm\\gunluk", "\\\\server\\pdm\\journal")
    journal_json = T('"journalRoot":"\\\\\\\\sunucu\\\\pdm\\\\gunluk"',
                     '"journalRoot":"\\\\\\\\server\\\\pdm\\\\journal"')
    story.append(table([
        [T("Kapsam", "Scope"), T("Ayar", "Setting")],
        [T("Tüm bilgisayar (yönetici)", "Whole computer (administrator)"),
         T("Kayıt defterinde " + c("HKLM\\SOFTWARE\\PdmVariableStudio") + " altında "
           + c("JournalRoot") + " adlı dize (String) değeri, örneğin " + c(journal_share)
           + ". Kullanıcı ayarını geçersiz kılar.",
           "A string value named " + c("JournalRoot") + " under "
           + c("HKLM\\SOFTWARE\\PdmVariableStudio") + " in the registry, for example "
           + c(journal_share) + ". Overrides the user setting.")],
        [T("Tek kullanıcı", "Single user"),
         T(c("%LOCALAPPDATA%\\PdmVariableStudio\\settings.json") + " dosyasında " + c(journal_json),
           c(journal_json) + " in " + c("%LOCALAPPDATA%\\PdmVariableStudio\\settings.json"))],
    ], [0.27, 0.73], first_bold=True))
    story.append(P(T("Yönetici için PowerShell ile örnek:", "Example for administrators, in PowerShell:")))
    story.append(code(['New-Item -Path "HKLM:\\SOFTWARE\\PdmVariableStudio" -Force | Out-Null',
                       'Set-ItemProperty -Path "HKLM:\\SOFTWARE\\PdmVariableStudio" -Name JournalRoot `',
                       f'    -Value "{journal_share}" -Type String']))
    story.append(callout("note", T("Paylaşıma ulaşılamazsa ne olur?",
                                   "What if the share is unreachable?"), [T(
        "Uygulama yerel klasöre <b>geçmez</b>; işlemi hiç başlatmaz ve nedenini söyler. Geri "
        "alınamayacak bir değişiklik yapmaktansa hiç yapmamak tercih edilir.",
        "The application does <b>not</b> fall back to a local folder; it does not start the "
        "operation and tells you why. Making no change is preferred to making a change that "
        "cannot be undone.")]))
    story.append(P(T(
        "<b>Dil.</b> Kurulumda seçilen dil " + c("HKLM\\SOFTWARE\\PdmVariableStudio\\Language")
        + " değerine yazılır (" + c("tr") + " / " + c("en") + "); BT ekibi aynı değeri dağıtımla "
        "da verebilir. Ayrıntı: " + ref("s4-9", "4.9") + ".",
        "<b>Language.</b> The language chosen in setup is written to "
        + c("HKLM\\SOFTWARE\\PdmVariableStudio\\Language") + " (" + c("tr") + " / " + c("en")
        + "); IT can deploy the same value too. Details: " + ref("s4-9", "4.9") + ".")))
    story.append(CondPageBreak(110 * mm))

    # ================= 4. KULLANIM =================
    story.append(H1(T("4. Kullanım", "4. Usage"), "s4"))

    story.append(H2(T("4.1 Uygulamayı açma", "4.1 Opening the application"), "s4-1"))
    story.append(table([
        [T("Yol", "Way"), T("Nasıl", "How"), T("Sonuç", "Result")],
        [T("<b>PDM Explorer'dan</b><br/>(önerilen)", "<b>From PDM Explorer</b><br/>(recommended)"),
         T("Vault içinde bir klasördeyken <b>Araçlar</b> menüsünden ya da sağ tık menüsünden "
           "<b>PDM Variable Studio</b>",
           "While in a folder of the vault, <b>PDM Variable Studio</b> from the <b>Tools</b> menu "
           "or the right-click menu"),
         T("O klasörün dosyaları listeye hazır gelir", "The files of that folder are loaded into the list")],
        [T("<b>Doğrudan</b>", "<b>Directly</b>"),
         T("Kurulum klasöründeki " + c("PdmVariableStudio.exe") + " (varsayılan: "
           + c("C:\\Program Files\\PDM Variable Studio\\") + ")",
           c("PdmVariableStudio.exe") + " in the installation folder (default: "
           + c("C:\\Program Files\\PDM Variable Studio\\") + ")"),
         T("Birden fazla vault'unuz varsa hangisine bağlanacağı sorulur; liste boş açılır",
           "If you have more than one vault you are asked which one to connect to; the list "
           "starts empty")],
    ], [0.2, 0.47, 0.33]))
    story.append(P(T("İpucu: doğrudan kullanacaksanız exe'ye sağ tıklayıp <b>Başlat'a sabitle</b> "
                     "ya da <b>Görev çubuğuna sabitle</b> diyebilirsiniz.",
                     "Tip: if you start it directly, right-click the exe and choose <b>Pin to "
                     "Start</b> or <b>Pin to taskbar</b>.")))

    story.append(H2(T("4.2 Pencere düzeni", "4.2 Window layout"), "s4-2"))
    story.append(P(T(
        "Tek pencere, üç sekme. Üst şeritte bağlı olduğunuz <b>Vault</b> ve <b>Klasör</b>, "
        "sağ üstte <b>dil kutusu</b> (" + ref("s4-9", "4.9") + ") ve <b>Hakkında</b> düğmesi bulunur.",
        "One window, three tabs. The top strip shows the <b>Vault</b> and <b>Folder</b> you are "
        "connected to; at the top right are the <b>language box</b> (" + ref("s4-9", "4.9")
        + ") and the <b>About</b> button.")))
    story.append(table([
        [T("Sekme", "Tab"), T("Ne yaparsınız", "What you do")],
        [T("<b>Dışa Aktar</b>", "<b>Export</b>"),
         T("Dosyaları toplar, değişkenleri seçer, Excel dosyasını üretirsiniz",
           "Collect files, choose variables and create the Excel file")],
        [T("<b>İçe Aktar</b>", "<b>Import</b>"),
         T("Düzenlediğiniz Excel dosyasını yükler, önizlemeyi inceler ve uygularsınız",
           "Load the edited Excel file, review the preview and apply")],
        [T("<b>İşlem Geçmişi</b>", "<b>Operation History</b>"),
         T("Yapılan işlemleri görür, gerekirse güvenle geri alırsınız",
           "See past operations and undo them safely if needed")],
    ], [0.25, 0.75]))

    story.append(H2(T("4.3 Dışa aktarma", "4.3 Exporting"), "s4-3"))
    story.append(H3(T("1. Dosyaları toplayın", "1. Collect the files"), "s4-3a"))
    story.append(P(T(
        "Dört kaynak düğmesi aynı listeye ekler; bunları karışık kullanabilirsiniz. "
        "Aynı dosya iki kez eklenmez; <b>Kaynak</b> sütunu her satırın nereden geldiğini gösterir.",
        "The four source buttons add to the same list; you can mix them. The same file is never "
        "added twice; the <b>Source</b> column shows where each row came from.")))
    story.append(table([
        [T("Düğme", "Button"), T("Ne yapar", "What it does")],
        [T("<b>Klasör Ekle…</b>", "<b>Add Folder…</b>"),
         T("PDM'in klasör seçme penceresini açar. <b>Alt klasörler</b> kutusu işaretliyse alt "
           "klasörlerdeki dosyalar da eklenir (varsayılan: kapalı).",
           "Opens PDM's folder picker. With the <b>Subfolders</b> box ticked, files in subfolders "
           "are added too (default: off).")],
        [T("<b>Dosya Ekle…</b>", "<b>Add Files…</b>"),
         T("PDM'in dosya seçme penceresi; birden fazla dosya seçebilirsiniz.",
           "PDM's file picker; you can select multiple files.")],
        [T("<b>Ara ve Ekle…</b>", "<b>Search and Add…</b>"),
         T("Dosya adı desenine (ör. " + c("*.sldprt") + ", " + c("MIL-*")
           + ") ve/veya bir kart değişkeninin değerine göre PDM'de arar, sonuçları ekler.",
           "Searches PDM by file name pattern (e.g. " + c("*.sldprt") + ", " + c("MIL-*")
           + ") and/or the value of a data card variable, and adds the results.")],
        [T("<b>Montajdan Ekle…</b>", "<b>Add from Assembly…</b>"),
         T("Bir montaj seçip konfigürasyonunu belirlersiniz; montajın bütün bileşenleri (istenirse "
           "alt montajların içindekiler de) farklı klasörlerde olsalar bile eklenir. Her parça için "
           "yalnızca <b>montajın kullandığı konfigürasyon</b> ve " + c("@") + " satırı gelir. "
           "Excel'de <b>Üst montaj</b>, <b>Seviye</b> ve <b>Adet</b> sütunları görünür; bunlar "
           "yalnızca bilgi amaçlıdır, PDM'ye yazılmaz.",
           "You pick an assembly and its configuration; all components of the assembly "
           "(optionally also the contents of subassemblies) are added, even if they are in "
           "different folders. Each part gets rows only for <b>the configuration the assembly "
           "uses</b> and " + c("@") + ". Excel shows <b>Parent assembly</b>, <b>Level</b> and "
           "<b>Quantity</b> columns; they are for information only and are never written to PDM.")],
        [T("<b>Seçilenleri Sil</b> / <b>Listeyi Temizle</b>", "<b>Remove Selected</b> / <b>Clear List</b>"),
         T("İşaretlediğiniz satırları ya da listenin tamamını çıkarır.",
           "Removes the rows you ticked, or the whole list.")],
    ], [0.3, 0.7]))
    story.append(H3(T("2. Değişkenleri seçin", "2. Choose the variables"), "s4-3b"))
    story.append(P(T(
        "Alttaki <b>Dışa aktarılacak değişkenler</b> listesinde yalnızca düzenlemek "
        "istediklerinizi işaretli bırakın (<b>Tümünü seç</b> / <b>Tümünü temizle</b>). "
        "Daha az sütun, Excel'de daha kolay gezinme ve daha az hata demektir.",
        "In the <b>Variables to export</b> list at the bottom, leave only the ones you want to "
        "edit ticked (<b>Select all</b> / <b>Clear all</b>). Fewer columns mean easier "
        "navigation in Excel and fewer mistakes.")))
    story.append(H3(T("3. Dışa aktarın", "3. Export"), "s4-3c"))
    story.append(P(T(
        "<b>Dışa Aktar…</b> düğmesiyle " + c(".xlsx") + " dosyasını kaydedin. Ardından "
        "<b>Excel'de Aç</b> ile dosyayı hemen açabilir, <b>Klasörü Göster</b> ile "
        "Windows Gezgini'nde bulabilirsiniz.",
        "Save the " + c(".xlsx") + " file with the <b>Export…</b> button. Then open it right away "
        "with <b>Open in Excel</b>, or find it in Windows Explorer with <b>Show in Folder</b>.")))
    story.append(callout("note", T("Konfigürasyonlu SOLIDWORKS dosyaları",
                                   "SOLIDWORKS files with configurations"), [T(
        "Her konfigürasyon <b>ayrı bir satırdır</b>. " + c("@") + " satırı konfigürasyondan "
        "bağımsız, dosya düzeyindeki değerdir (SOLIDWORKS'ün <b>Custom</b> sekmesi) ve "
        "adlandırılmış konfigürasyonlardan ayrı tutulur. Konfigürasyonu olmayan dosyalar "
        "(ör. " + c(".docx") + ", " + c(".pdf") + ") tek satırdır.",
        "Each configuration is <b>a separate row</b>. The " + c("@") + " row is the "
        "configuration-independent, file-level value (SOLIDWORKS's <b>Custom</b> tab) and is kept "
        "separate from the named configurations. Files without configurations (e.g. "
        + c(".docx") + ", " + c(".pdf") + ") have a single row.")]))

    story.append(H2(T("4.4 Excel'de düzenleme", "4.4 Editing in Excel"), "s4-4"))
    story.append(table([
        [T("Yapabilirsiniz", "You can"), T("Yapmayın", "Do not")],
        [[Paragraph(t, bullet, bulletText="✓") for t in T([
            "Değer hücrelerini değiştirmek",
            "Satırları sıralamak, süzmek, silmek",
            "Sütun gizlemek, genişletmek, biçimlendirmek",
            "Sayıları kendi bölgesel biçiminizle yazmak (" + c("12,4") + ")",
            "Dosyayı kaydedip günler sonra geri yüklemek"], [
            "Change value cells",
            "Sort, filter and delete rows",
            "Hide, widen and format columns",
            "Type numbers in your own regional format (" + c("12.4") + " or " + c("12,4") + ")",
            "Save the file and load it back days later"])],
         [Paragraph(t, bullet, bulletText="✗") for t in T([
            "Gizli " + c("_Rows") + " ve " + c("_Metadata") + " sayfalarını silmek ya da "
            "değiştirmek — eşleme bilgisi oradadır",
            "Dolu bir hücreyi yanlışlıkla boşaltmak — boşaltmak <b>“değeri sil”</b> demektir",
            "Yeni satır ekleyerek dosya oluşturmayı beklemek — yeni satırlar atlanır",
            "Dosya adı, klasör, konfigürasyon sütunlarını değiştirmeye çalışmak (kilitlidir)"], [
            "Delete or change the hidden " + c("_Rows") + " and " + c("_Metadata") + " sheets — "
            "the matching information lives there",
            "Clear a filled cell by accident — clearing means <b>“delete the value”</b>",
            "Expect new rows to create files — new rows are skipped",
            "Try to change the file name, folder or configuration columns (they are locked)"])]],
    ], [0.5, 0.5]))
    story.append(P(T(
        "Uygulama satırları sıraya ya da başlığa göre değil, dosyanın içine gizlenmiş "
        "kimliklere göre eşler. Bu yüzden sıralamak veya satır silmek güvenlidir. "
        "Şüpheli görünen bir satır ise hiç uygulanmaz — yanlış dosyaya yazmaktansa "
        "atlamak her zaman tercih edilir.",
        "The application matches rows by IDs hidden inside the file, not by order or header. "
        "That is why sorting or deleting rows is safe. A row that looks suspicious is not "
        "applied at all — skipping is always preferred to writing to the wrong file.")))
    story.append(P(T(
        "Excel'deki başlıklar dışa aktarım anındaki arayüz dilindedir. İngilizce arayüzde dışa "
        "aktarılan bir dosya Türkçe arayüzde (ve tersi) sorunsuz içe aktarılır.",
        "The headers in Excel are in the interface language at the time of the export. A file "
        "exported with the English interface imports without problems in the Turkish interface "
        "(and vice versa).")))

    story.append(H2(T("4.5 İçe aktarma ve önizleme", "4.5 Import and preview"), "s4-5"))
    story += steps(T([
        "<b>İçe Aktar</b> sekmesinde <b>Çalışma kitabı seç…</b> ile düzenlediğiniz dosyayı açın.",
        "Uygulama PDM'den taze değerleri okur ve her hücre için bir önizleme satırı üretir. "
        "Tabloda <b>Dışa aktarımdaki</b>, <b>Excel'deki yeni</b> ve <b>PDM'deki güncel</b> değerler "
        "yan yana görünür.",
    ], [
        "On the <b>Import</b> tab, open the file you edited with <b>Select workbook…</b>.",
        "The application reads fresh values from PDM and creates a preview row for every cell. "
        "The table shows the <b>At export</b>, <b>New in Excel</b> and <b>Current in PDM</b> "
        "values side by side.",
    ]))
    story.append(table([
        [T("Durum", "Status"), T("Anlamı", "Meaning"), T("Ne olur", "What happens")],
        [status("✓", T("Güvenli", "Safe"), OK),
         T("Siz değiştirdiniz; PDM'de dışa aktarımdan beri kimse dokunmadı",
           "You changed it; nobody touched it in PDM since the export"),
         T("<b>Uygulanır</b> (varsayılan olarak seçili)", "<b>Applied</b> (selected by default)")],
        [status("·", T("Değişmemiş", "Unchanged"), MUTED),
         T("Excel'de dokunulmamış", "Not edited in Excel"), T("Atlanır", "Skipped")],
        [status("·", T("Zaten uygulanmış", "Already applied"), MUTED),
         T("PDM'de zaten aynı değer var (başkası yazmış)",
           "PDM already has the same value (someone else wrote it)"),
         T("Atlanır; gereksiz yazma yapılmaz", "Skipped; no unnecessary write")],
        [status("⚠", T("Çakışma", "Conflict"), WARN),
         T("Siz değiştirdiniz <b>ve</b> PDM'de de başkası farklı bir değer yazmış",
           "You changed it <b>and</b> someone else wrote a different value in PDM"),
         T("<b>Uygulanmaz</b>, seçilemez. Aşağıdaki nota bakın.",
           "<b>Not applied</b>, cannot be selected. See the note below.")],
        [status("✗", T("Hata", "Error"), ERR),
         T("Tür uyuşmazlığı (ör. sayı alanına metin), bulunamayan dosya, bozuk satır vb.",
           "Type mismatch (e.g. text in a number field), file not found, damaged row, etc."),
         T("<b>Uygulanmaz</b>; nedeni <b>Neden</b> sütununda ve ipucunda",
           "<b>Not applied</b>; the reason is in the <b>Reason</b> column and the tooltip")],
    ], [0.2, 0.47, 0.33], row_bgs={1: OK_BG, 4: WARN_BG, 5: ERR_BG}))
    story.append(P(T(
        "Her durum <b>simge + metin + renk</b> ile gösterilir; renkleri ayırt etmek "
        "zor olsa da bilgi kaybolmaz. Bir satırın üzerine geldiğinizde ipucu, nedenini "
        "ve ne yapmanız gerektiğini söyler.",
        "Every status is shown with <b>icon + text + color</b>, so no information is lost even "
        "if colors are hard to tell apart. Hovering over a row shows a tooltip with the reason "
        "and what to do.")))
    story.append(P(T(
        "<b>Süzgeçler:</b> <b>Yalnızca değişenler</b>, <b>Çakışmalar</b>, <b>Hatalar</b> "
        "ve arama kutusu (dosya adı, değişken ya da değere göre). <b>Güvenli olanları "
        "seç</b> / <b>Seçimi temizle</b> ile seçimi toplu değiştirebilirsiniz.",
        "<b>Filters:</b> <b>Changes only</b>, <b>Conflicts</b>, <b>Errors</b> and the search box "
        "(by file name, variable or value). Use <b>Select safe changes</b> / <b>Clear "
        "selection</b> to change the selection in bulk.")))
    story.append(callout("warn", T("Çakışma görürseniz", "If you see a conflict"), [T(
        "Bu, iş arkadaşınızın değişikliğini koruyan bir güvenlik önlemidir. <b>PDM'deki güncel</b> "
        "değere bakın; hâlâ kendi değerinizin doğru olduğunu düşünüyorsanız aracı yeniden "
        "<b>dışa aktarın</b>, değişikliğinizi yeni dosyada tekrar yapın ve içe aktarın. Böylece "
        "karar bilerek verilmiş olur.",
        "This is a safety measure that protects your colleague's change. Look at the <b>Current "
        "in PDM</b> value; if you still think your value is right, <b>export</b> again, repeat "
        "your change in the new file and import it. That way the decision is made knowingly.")]))

    story.append(H2(T("4.6 Değişiklikleri uygulama", "4.6 Applying the changes"), "s4-6"))
    story += steps(T([
        "<b>Check-out onayı.</b> PDM'de bir kart değerini değiştirmek için dosyanın check-out "
        "edilmesi gerekir. Gerekirse sarı bir uyarı şeridi çıkar; <b>“Anladım, dosyalar check-out "
        "edilsin”</b> kutusunu işaretlemeden <b>Uygula</b> etkinleşmez.",
        "<b>Check-in seçeneği.</b> <b>“İşlem sonunda bizim çektiğimiz dosyalar check-in edilsin”</b> "
        "açıksa araç, kendi çektiği dosyaları işlem sonunda iade eder (her biri yeni bir sürüm "
        "üretir). Yanındaki kutuya bir check-in yorumu yazabilirsiniz. Kapalıysa dosyalar sizde "
        "çekili kalır.",
        "<b>Uygula</b>'ya tıklayın. İlerleme alttaki durum çubuğunda görünür; gerekirse "
        "<b>Durdur</b> ile kesebilirsiniz.",
    ], [
        "<b>Check-out consent.</b> Changing a card value in PDM requires the file to be checked "
        "out. When needed, a yellow warning strip appears; <b>Apply</b> stays disabled until you "
        "tick <b>“I understand, check out the files”</b>.",
        "<b>Check-in option.</b> With <b>“Check in the files we checked out when finished "
        "(creates a new version)”</b> on, the tool checks in the files it checked out at the end "
        "(each creates a new version). You can type a check-in comment in the box next to it. "
        "When it is off, the files stay checked out to you.",
        "Click <b>Apply</b>. Progress is shown in the status bar at the bottom; you can interrupt "
        "with <b>Stop</b> if needed.",
    ]))
    story.append(P(T("Bilmeniz iyi olanlar:", "Good to know:")))
    story += bullets(T([
        "<b>Sizin zaten çekili tuttuğunuz</b> dosyalar işlem sonunda check-in <b>edilmez</b>; "
        "üzerindeki işinizi bozmamak için.",
        "<b>Başkasının çekili tuttuğu</b> dosyalar atlanır, gerekçesi gösterilir.",
        "Bir dosyada yazma başarısız olursa aracın yaptığı check-out geri alınır; dosyada iz kalmaz. "
        "Diğer dosyalar işlemeye devam eder.",
        "Dosya SOLIDWORKS'te açıksa yazılamaz; kapatıp o dosya için yeniden uygulayın.",
    ], [
        "Files <b>you already had checked out</b> are <b>not</b> checked in at the end, so your "
        "work on them is not disturbed.",
        "Files <b>checked out by someone else</b> are skipped and the reason is shown.",
        "If writing fails for a file, the tool's check-out is undone; the file is left untouched. "
        "The other files keep being processed.",
        "A file open in SOLIDWORKS cannot be written; close it and apply again for that file.",
    ]))

    story.append(H2(T("4.7 İşlem geçmişi ve geri alma", "4.7 Operation history and undo"), "s4-7"))
    story.append(P(T(
        "Her uygulama <b>İşlem Geçmişi</b> sekmesinde listelenir (tarih, kullanıcı, "
        "kapsam, sonuç). Geri almak için:",
        "Every apply is listed on the <b>Operation History</b> tab (date, user, scope, result). "
        "To undo:")))
    story += steps(T([
        "Listeden işlemi seçin.",
        "<b>Geri alma önizlemesi</b>'ne tıklayın. Araç, her hücre için PDM'deki güncel değeri "
        "kontrol eder.",
        "Önizlemeyi inceleyip <b>Geri Al</b>'a tıklayın.",
    ], [
        "Select the operation in the list.",
        "Click <b>Preview undo</b>. The tool checks the current PDM value of every cell.",
        "Review the preview and click <b>Undo</b>.",
    ]))
    story.append(table([
        [T("Önizlemedeki durum", "Status in the preview"), T("Anlamı", "Meaning"),
         T("Ne olur", "What happens")],
        [status("✓", T("Güvenli", "Safe"), OK),
         T("Değer hâlâ sizin yazdığınız değer", "The value is still the one you wrote"),
         T("Eski değere döndürülür", "Reverted to the old value")],
        [status("·", T("Zaten geri alınmış", "Already reverted"), MUTED),
         T("Değer zaten eskisine dönmüş", "The value is already back to the old one"),
         T("Atlanır", "Skipped")],
        [status("⚠", T("Çakışma", "Conflict"), WARN),
         T("Sizden sonra başkası o değeri değiştirmiş", "Someone changed the value after you"),
         T("<b>Dokunulmaz</b> — onun işi korunur", "<b>Left untouched</b> — their work is preserved")],
        [status("✗", T("Kullanılamaz", "Unavailable"), ERR),
         T("Dosya bulunamıyor, erişilemiyor vb.", "The file cannot be found, cannot be accessed, etc."),
         T("Atlanır; nedeni gösterilir", "Skipped; the reason is shown")],
    ], [0.25, 0.42, 0.33], row_bgs={1: OK_BG, 3: WARN_BG, 4: ERR_BG}))
    story.append(P(T(
        "Geri alma da bir işlemdir ve geçmişe kaydedilir. İşlem geçmişi varsayılan "
        "olarak kullanıcıya ve bilgisayara özeldir — " + ref("s3-5", "3.5") + ".",
        "An undo is also an operation and is recorded in the history. By default the operation "
        "history is per user and per computer — " + ref("s3-5", "3.5") + ".")))

    story.append(H2(T("4.8 Örnek senaryo", "4.8 Example scenario"), "s4-8"))
    story.append(P(T(
        "<i>“Proje-A klasöründeki 120 parçanın Malzeme ve Açıklama değerlerini "
        "güncellemem gerekiyor.”</i>",
        "<i>“I need to update the Material and Description values of the 120 parts in the "
        "Project-A folder.”</i>")))
    story += steps(T([
        "PDM Explorer'da <b>Proje-A</b> klasörüne sağ tıklayın → <b>PDM Variable Studio</b>. "
        "Dosyalar listeye gelir. Alt klasörler de gerekiyorsa <b>Alt klasörler</b>'i işaretleyip "
        "<b>Klasör Ekle…</b> ile klasörü yeniden ekleyin.",
        "Değişken listesinde <b>Tümünü temizle</b> deyip yalnızca <i>Malzeme</i> ve "
        "<i>Açıklama</i>'yı işaretleyin.",
        "<b>Dışa Aktar…</b> → " + c("ProjeA_malzeme.xlsx") + " → <b>Excel'de Aç</b>.",
        "Excel'de Malzeme sütununu süzüp değerleri güncelleyin, kaydedip kapatın.",
        "<b>İçe Aktar</b> → <b>Çalışma kitabı seç…</b> → aynı dosya. Önizlemede örneğin "
        "<i>118 güvenli, 2 çakışma</i> görürsünüz.",
        "<b>Çakışmalar</b> süzgeciyle iki satıra bakın: bir iş arkadaşınız bu arada o iki dosyayı "
        "güncellemiş. Onun değerleri doğruysa bırakın.",
        "Check-out onayını ve check-in seçeneğini işaretleyip (yorum: <i>Malzeme güncellemesi</i>) "
        "<b>Uygula</b>.",
        "Bir hata fark ederseniz <b>İşlem Geçmişi</b> → işlemi seçin → <b>Geri alma önizlemesi</b> "
        "→ <b>Geri Al</b>.",
    ], [
        "In PDM Explorer, right-click the <b>Project-A</b> folder → <b>PDM Variable Studio</b>. "
        "The files are loaded into the list. If you need subfolders too, tick <b>Subfolders</b> "
        "and add the folder again with <b>Add Folder…</b>.",
        "In the variable list click <b>Clear all</b> and tick only <i>Material</i> and "
        "<i>Description</i>.",
        "<b>Export…</b> → " + c("ProjectA_material.xlsx") + " → <b>Open in Excel</b>.",
        "In Excel, filter the Material column and update the values; save and close.",
        "<b>Import</b> → <b>Select workbook…</b> → the same file. The preview shows, for example, "
        "<i>118 safe changes, 2 conflicts</i>.",
        "Look at the two rows with the <b>Conflicts</b> filter: a colleague updated those two "
        "files in the meantime. If their values are right, leave them.",
        "Tick the check-out consent and the check-in option (comment: <i>Material update</i>) "
        "and click <b>Apply</b>.",
        "If you notice a mistake: <b>Operation History</b> → select the operation → <b>Preview "
        "undo</b> → <b>Undo</b>.",
    ]))

    story.append(H2(T("4.9 Arayüz dili", "4.9 Interface language"), "s4-9"))
    story.append(P(T(
        "Uygulama ve eklenti <b>Türkçe</b> ve <b>İngilizce</b> çalışır. Kurulum ve eklenti "
        "tektir: aynı vault'u kullanan her bilgisayar kendi dilini seçer. Dil şu sırayla "
        "belirlenir:",
        "The application and the add-in work in <b>Turkish</b> and <b>English</b>. There is one "
        "setup and one add-in: every computer that uses the same vault picks its own language. "
        "The language is chosen in this order:")))
    story += steps(T([
        "Sağ üstteki <b>dil kutusunda</b> sizin seçtiğiniz dil "
        "(" + c("HKCU\\SOFTWARE\\PdmVariableStudio\\Language") + ").",
        "Kurulumda seçilen dil (" + c("HKLM\\SOFTWARE\\PdmVariableStudio\\Language") + "); "
        "BT ekibi bu değeri dağıtımla da verebilir.",
        "Windows'un dili: Türkçe Windows'ta Türkçe, diğerlerinde İngilizce.",
    ], [
        "The language you pick in the <b>language box</b> at the top right "
        "(" + c("HKCU\\SOFTWARE\\PdmVariableStudio\\Language") + ").",
        "The language chosen in setup (" + c("HKLM\\SOFTWARE\\PdmVariableStudio\\Language")
        + "); IT can also deploy this value.",
        "The Windows display language: Turkish on Turkish Windows, English otherwise.",
    ]))
    story.append(P(T(
        "Dil kutusundan değiştirdiğinizde uygulama yeniden başlatmayı önerir; dosya "
        "listesi ve önizleme sıfırlanır, işlem geçmişi etkilenmez. PDM Explorer "
        "menüsündeki ipucu metni Explorer bir sonraki açılışında değişir.",
        "When you change it in the language box, the application offers to restart; the file "
        "list and preview are reset, the operation history is not affected. The tooltip in the "
        "PDM Explorer menu changes the next time Explorer starts.")))
    story.append(callout("note", T("Dil veriyi değiştirmez", "The language does not change the data"), [T(
        "Sayılar ve tarihler her zaman Windows'un bölge ayarıyla okunur. İngilizce arayüzde dışa "
        "aktarılan bir çalışma kitabı Türkçe arayüzde (ve tersi) sorunsuz içe aktarılır; yalnızca "
        "Excel'deki başlıklar dışa aktarımın dilindedir. Günlük dosyası (" + c("studio.log")
        + ") her zaman Türkçedir.",
        "Numbers and dates are always read with the Windows regional settings. A workbook "
        "exported with the English interface imports without problems in the Turkish interface "
        "(and vice versa); only the headers in Excel are in the language of the export. The log "
        "file (" + c("studio.log") + ") is always in Turkish.")]))
    story.append(CondPageBreak(110 * mm))

    # ================= 5. DOSYALAR =================
    story.append(H1(T("5. Dosyalar ve ayarlar", "5. Files and settings"), "s5"))
    story.append(P(T(
        "Uygulamanın yazdığı her şey bu bilgisayarda, "
        + c("%LOCALAPPDATA%\\PdmVariableStudio\\") + " altındadır. Klasörü açmanın en "
        "kolay yolu: uygulamada <b>Hakkında</b> → ilgili satırdaki düğme. Ya da "
        "Windows Gezgini'nin adres çubuğuna " + c("%LOCALAPPDATA%\\PdmVariableStudio")
        + " yazın.",
        "Everything the application writes is on this computer, under "
        + c("%LOCALAPPDATA%\\PdmVariableStudio\\") + ". The easiest way to open the folder: "
        "<b>About</b> in the application → the button on the relevant row. Or type "
        + c("%LOCALAPPDATA%\\PdmVariableStudio") + " in the Windows Explorer address bar.")))
    story.append(table([
        [T("Dosya", "File"), T("İçerik", "Contents"), T("Silinirse", "If deleted")],
        [c("studio.log"), T("Uygulama ve eklenti günlüğü. Sorun bildirirken ekleyin.",
                            "Application and add-in log (in Turkish). Attach it when reporting a "
                            "problem."),
         T("Sorun yok; yeniden oluşur", "No problem; it is recreated")],
        [c("journal\\&lt;vault&gt;\\"), T("İşlem geçmişi — geri alma buradan çalışır.",
                                          "Operation history — undo works from here."),
         T("<font color='#B3261E'><b>Geri alınamaz!</b> Silmeyin.</font>",
           "<font color='#B3261E'><b>Cannot be undone!</b> Do not delete.</font>")],
        [c("settings.json"), T("Tercihler: alt klasörler, check-in seçeneği ve yorumu, son klasör, "
                               "işlem geçmişi konumu.",
                               "Preferences: subfolders, check-in option and comment, last folder, "
                               "operation history location."),
         T("Varsayılanlar gelir", "Defaults are used")],
    ], [0.25, 0.47, 0.28]))
    story.append(P(T(
        "Tüm dosyalar düz metindir ve Not Defteri ile okunabilir. Uygulama internete "
        "bağlanmaz, hiçbir yere veri göndermez. Arayüz dili kayıt defterinde durur ("
        + ref("s4-9", "4.9") + ").",
        "All files are plain text and can be read with Notepad. The application does not "
        "connect to the internet and sends no data anywhere. The interface language is stored "
        "in the registry (" + ref("s4-9", "4.9") + ").")))

    story.append(H2(T("5.1 Hakkında penceresi", "5.1 The About window"), "s5-1"))
    story.append(P(T(
        "Sağ üstteki <b>Hakkında</b> düğmesi sürümü, arayüz dilini ve nereden geldiğini, "
        "günlük / işlem geçmişi / ayar dosyalarının konumlarını (her biri tek tıkla açılır) "
        "ve proje bağlantılarını gösterir. <b>Bilgileri Kopyala</b> tüm bu bilgileri panoya "
        "alır; destek isterken yapıştırmanız yeterlidir.",
        "The <b>About</b> button at the top right shows the version, the interface language and "
        "where it comes from, the locations of the log / operation history / settings files "
        "(each one click away) and the project links. <b>Copy Info</b> copies all of this to the "
        "clipboard; just paste it when asking for support.")))

    # ================= 6. SORUN GİDERME =================
    story.append(H1(T("6. Sorun giderme", "6. Troubleshooting"), "s6"))
    story.append(P(T(
        "İlk bakılacak yer her zaman " + c("studio.log") + " dosyasıdır ("
        + ref("s5", "5. Dosyalar ve ayarlar") + "). Eklenti satırları " + c("[eklenti]")
        + " etiketiyle başlar ve akışı adım adım gösterir:",
        "The first place to look is always " + c("studio.log") + " ("
        + ref("s5", "5. Files and settings") + "). Log lines are in Turkish; add-in lines carry "
        "the " + c("[eklenti]") + " (add-in) tag and show the flow step by step:")))
    story.append(code([
        "[BILGI] [eklenti] Eklenti yüklendi, komut kaydedildi.   ← "
        + T("eklenti sağlam", "add-in loaded"),
        "[BILGI] [eklenti] Komut tetiklendi (veri sayısı: 1).    ← "
        + T("komuta tıklandı", "command clicked"),
        "[BILGI] [eklenti] Seçili klasör: Parts (#42).",
        "[BILGI] [eklenti] Uygulama başlatılıyor: ..." + T("", "            ← starting the application")]))

    faq = T([
        ("Menüde komut görünmüyor.",
         ["Tüm PDM Explorer pencerelerini kapatıp yeniden açtınız mı? Eklenti yalnızca yeni açılan "
          "pencerelere yüklenir.",
          "PDM Administration'da vault → <b>Add-ins</b> altında <b>PDM Variable Studio</b> var mı?",
          "Günlükte <i>“Eklenti yüklendi”</i> satırı var mı? Yoksa eklenti yüklenmemiştir."]),
        ("Eklenti yüklenirken “…is not a multi-threaded COM-server” hatası.",
         [c("EPDM.Interop.epdm.dll") + " eklentiyle birlikte seçilmemiş. Eklentiyi silip iki "
          "dosyayla yeniden ekleyin (" + ref("s3-3", "3.3") + ")."]),
        ("Eklentiyi güncellerken “Sunucu Meşgul” / “Diğer program meşgul” iletisi.",
         ["Veri kaybı riski yoktur, <b>Yeniden Dene</b> güvenlidir. Kalıcıysa: tüm PDM pencerelerini "
          "ve " + c("PdmVariableStudio.exe") + "'yi kapatın, Administration'da eski eklentiyi "
          "silin, tüm pencereleri <b>yeniden kapatın</b>, sonra Administration'ı açıp iki dosyayı "
          "ekleyin."]),
        ("Komuta tıklıyorum, hiçbir şey olmuyor ya da “uygulama bulunamadı” iletisi çıkıyor.",
         ["Uygulama bu bilgisayara kurulmamış ya da başka bir klasörde. İleti, uygulamanın nerede "
          "arandığını söyler. " + c("install-app.ps1") + "'i yeniden çalıştırın; kayıt defteri "
          "değerini düzeltir.",
          "İleti eksik DLL'lerden söz ediyorsa kurulum yarım kalmıştır; betiği yönetici olarak "
          "yeniden çalıştırın."]),
        ("“Başlatma başarısız” / “vault'a bağlanılamadı”.",
         ["Bu bilgisayarda o vault için bir <i>vault view</i> olmalı ve PDM'de oturum açmış "
          "olmalısınız. Ayrıntı " + c("studio.log") + " içinde."]),
        ("Kurulum betiği “yönetici hakkı gerekiyor” diyor.",
         ["PowerShell'i <b>Yönetici olarak çalıştır</b> ile açın ya da " + ref("s3-2b",
          "yönetici hakkı olmadan kurulum") + " yöntemini kullanın."]),
        ("Kurulum betiği “EPDM.Interop.epdm.dll bulunamadı” diyor.",
         ["Bu bilgisayarda PDM istemcisi kurulu değil ya da farklı bir klasörde. Uygulama PDM "
          "istemcisi olmadan çalışamaz."]),
        ("“PowerShell betikleri bu sistemde devre dışı” hatası.",
         ["Komutu tam olarak " + c("powershell -ExecutionPolicy Bypass -File install-app.ps1")
          + " biçiminde çalıştırdığınızdan emin olun. Kurumsal grup ilkesi yine de engelliyorsa BT "
          "ekibinizden yardım isteyin."]),
        ("Windows SmartScreen “tanınmayan uygulama” uyarısı veriyor.",
         ["Uygulama kod imzalı değildir (imza sertifikası ücretlidir). Zip'in SHA-256 değerini "
          "yayım sayfasıyla karşılaştırdıktan sonra (" + ref("s3-1", "3.1") + ") <b>Daha fazla "
          "bilgi → Yine de çalıştır</b> diyebilirsiniz."]),
        ("Çalışma kitabı reddedildi.",
         [c("_Rows") + " / " + c("_Metadata") + " sayfası silinmiş ya da değiştirilmiş, dosya "
          "başka bir vault'tan veya uyumsuz bir sürümden geliyor. Yeniden dışa aktarıp "
          "değişikliklerinizi yeni dosyaya taşıyın. Reddedilen dosyadan PDM'ye <b>hiçbir şey "
          "yazılmaz</b>."]),
        ("Önizlemede satırlar “Dosya bulunamadı” ile engellendi.",
         ["Dosya dışa aktarımdan sonra silinmiş, başka klasöre taşınmış ya da silinip aynı adla "
          "<b>yeniden eklenmiş</b>. Yeniden eklenen dosya PDM'de yeni bir kimlik alır; eski "
          "çalışma kitabı onu tanımaz. Yanlış dosyaya yazmamak için bu satırlar bilerek "
          "atlanır. Klasörü yeniden dışa aktarıp değişikliklerinizi yeni dosyaya taşıyın."]),
        ("Bir dosya için “özel olarak açık” / kilitli hatası.",
         ["Dosya SOLIDWORKS'te ya da başka bir programda açık. Kapatıp o dosya için yeniden "
          "uygulayın; diğer dosyalar etkilenmez."]),
        ("Sayılar yanlış görünüyor (ör. 12,4 → 124).",
         ["Araç Türkçe ve İngilizce ondalık biçimlerini doğru ayırt eder. Yine de beklenmedik bir "
          "değer görürseniz <b>uygulamayın</b>, önizleme ekran görüntüsüyle birlikte bildirin."]),
        ("Başka bir bilgisayardan yaptığım işlemi geri alamıyorum.",
         ["İşlem geçmişi varsayılan olarak bilgisayara ve kullanıcıya özeldir. Ekip için ortak "
          "geçmiş: " + ref("s3-5", "3.5") + "."]),
        ("Uygulama yanlış dilde açılıyor.",
         ["Sağ üstteki dil kutusundan dili seçin; tercih bu Windows kullanıcısı için saklanır. "
          "Sıralama: " + ref("s4-9", "4.9") + "."]),
    ], [
        ("The command does not appear in the menu.",
         ["Did you close and reopen all PDM Explorer windows? The add-in is only loaded into newly "
          "opened windows.",
          "In PDM Administration, is <b>PDM Variable Studio</b> listed under vault → <b>Add-ins</b>?",
          "Is there an <i>“Eklenti yüklendi”</i> (add-in loaded) line in the log? If not, the "
          "add-in was not loaded."]),
        ("“…is not a multi-threaded COM-server” error while loading the add-in.",
         [c("EPDM.Interop.epdm.dll") + " was not selected together with the add-in. Remove the "
          "add-in and add it again with both files (" + ref("s3-3", "3.3") + ")."]),
        ("“Server Busy” / “Other program is busy” message while updating the add-in.",
         ["There is no risk of data loss; <b>Retry</b> is safe. If it persists: close all PDM "
          "windows and " + c("PdmVariableStudio.exe") + ", remove the old add-in in "
          "Administration, <b>close all windows again</b>, then open Administration and add the "
          "two files."]),
        ("I click the command and nothing happens, or “application not found” appears.",
         ["The application is not installed on this computer or is in another folder. The "
          "message says where the application was looked for. Run " + c("install-app.ps1")
          + " again; it fixes the registry value.",
          "If the message mentions missing DLLs, the installation is incomplete; run the script "
          "again as administrator."]),
        ("“Startup failed” / “Could not connect to vault”.",
         ["This computer needs a <i>vault view</i> for that vault and you must be logged in to "
          "PDM. Details are in " + c("studio.log") + "."]),
        ("The installation script says “administrator rights required”.",
         ["Open PowerShell with <b>Run as administrator</b>, or use the " + ref("s3-2b",
          "installation without administrator rights") + " method."]),
        ("The installation script says “EPDM.Interop.epdm.dll not found”.",
         ["The PDM client is not installed on this computer or is in a different folder. The "
          "application cannot run without the PDM client."]),
        ("“Running scripts is disabled on this system” error.",
         ["Make sure you run the command exactly as "
          + c("powershell -ExecutionPolicy Bypass -File install-app.ps1")
          + ". If a corporate group policy still blocks it, ask your IT team for help."]),
        ("Windows SmartScreen shows an “unrecognized app” warning.",
         ["The application is not code-signed (signing certificates are not free). After "
          "comparing the zip's SHA-256 value with the release page (" + ref("s3-1", "3.1")
          + "), you can choose <b>More info → Run anyway</b>."]),
        ("The workbook was rejected.",
         ["The " + c("_Rows") + " / " + c("_Metadata") + " sheet was deleted or changed, or the "
          "file comes from another vault or an incompatible version. Export again and move your "
          "changes to the new file. <b>Nothing</b> from a rejected file is written to PDM."]),
        ("Rows in the preview are blocked with “File not found”.",
         ["The file was deleted after the export, moved to another folder, or deleted and "
          "<b>re-added</b> with the same name. A re-added file gets a new ID in PDM; the old "
          "workbook does not recognize it. These rows are skipped on purpose to avoid writing "
          "to the wrong file. Export the folder again and move your changes to the new file."]),
        ("A file reports “open in another application” / locked.",
         ["The file is open in SOLIDWORKS or another program. Close it and apply again for that "
          "file; the other files are not affected."]),
        ("Numbers look wrong (e.g. 12,4 → 124).",
         ["The tool tells Turkish and English decimal formats apart correctly. If you still see "
          "an unexpected value, <b>do not apply</b>; report it with a screenshot of the preview."]),
        ("I cannot undo an operation made on another computer.",
         ["By default the operation history is per computer and per user. Shared history for "
          "teams: " + ref("s3-5", "3.5") + "."]),
        ("The application opens in the wrong language.",
         ["Pick the language in the language box at the top right; the choice is stored for "
          "this Windows user. Order of precedence: " + ref("s4-9", "4.9") + "."]),
    ])
    for q, answers in faq:
        block = [Paragraph(f"<b>{q}</b>", ParagraphStyle("q", parent=base, textColor=ACCENT,
                                                         spaceBefore=6, spaceAfter=3))]
        block += [Paragraph(a, bullet, bulletText="→") for a in answers]
        story.append(KeepTogether(block))
    story.append(CondPageBreak(90 * mm))

    # ================= 7. GÜNCELLEME / KALDIRMA =================
    story.append(H1(T("7. Güncelleme ve kaldırma", "7. Updating and uninstalling"), "s7"))
    story.append(H2(T("7.1 Yeni sürüme geçme", "7.1 Upgrading to a new version"), "s7-1"))
    story.append(table([
        [T("Parça", "Part"), T("Ne zaman", "When"), T("Nasıl", "How")],
        [T("<b>Uygulama</b>", "<b>Application</b>"), T("Her yeni sürümde", "With every new version"),
         T("Yeni kurulum dosyasını çalıştırın (ya da yeni zip'i indirip " + c("install-app.ps1")
           + "'i yeniden çalıştırın). Önce uygulama penceresini kapatın. PDM Explorer'ı kapatmak "
           "<b>gerekmez</b>. İşlem geçmişiniz ve ayarlarınız korunur.",
           "Run the new setup file (or download the new zip and run " + c("install-app.ps1")
           + " again). Close the application window first. Closing PDM Explorer is <b>not</b> "
           "needed. Your operation history and settings are kept.")],
        [T("<b>Eklenti</b>", "<b>Add-in</b>"),
         T("Yalnızca sürüm notlarında <b>“eklenti güncellendi”</b> yazıyorsa",
           "Only if the release notes say <b>“add-in updated”</b> (“eklenti güncellendi”)"),
         T("Administration → Add-ins → mevcut eklentiyi yeni " + c("PdmVariableStudio.AddIn.dll")
           + " ve " + c("EPDM.Interop.epdm.dll") + " ile güncelleyin, ardından tüm PDM "
           "pencerelerini kapatıp açın.",
           "Administration → Add-ins → update the existing add-in with the new "
           + c("PdmVariableStudio.AddIn.dll") + " and " + c("EPDM.Interop.epdm.dll")
           + ", then close and reopen all PDM windows.")],
    ], [0.16, 0.26, 0.58]))
    story.append(P(T("Sürüm notları: " + link(REPO + "/releases", "GitHub Releases") + " ve paketteki "
                     + c("CHANGELOG.md") + ".",
                     "Release notes: " + link(REPO + "/releases", "GitHub Releases") + " and "
                     + c("CHANGELOG.md") + " in the package (in Turkish).")))

    story.append(H2(T("7.2 Kaldırma", "7.2 Uninstalling"), "s7-2"))
    story += steps(T([
        "<b>Eklenti (PDM yöneticisi):</b> Administration → vault → <b>Add-ins</b> → "
        "<b>PDM Variable Studio</b>'ya sağ tık → <b>Remove</b> (Kaldır). Ardından tüm PDM "
        "pencerelerini kapatıp açın.",
        "<b>Uygulama:</b> uygulama penceresini kapatın, kurulum klasörünü silin (varsayılan: "
        + c("C:\\Program Files\\PDM Variable Studio\\") + ").",
        "<b>Kayıt defteri:</b> yönetici PowerShell'de:",
    ], [
        "<b>Add-in (PDM administrator):</b> Administration → vault → <b>Add-ins</b> → right-click "
        "<b>PDM Variable Studio</b> → <b>Remove</b>. Then close and reopen all PDM windows.",
        "<b>Application:</b> close the application window and delete the installation folder "
        "(default: " + c("C:\\Program Files\\PDM Variable Studio\\") + "). If you installed with "
        "the setup file, use <b>Settings → Apps</b> instead.",
        "<b>Registry:</b> in an administrator PowerShell:",
    ]))
    story.append(code(['Remove-Item "HKLM:\\SOFTWARE\\PdmVariableStudio" -Recurse   # '
                       + T("standart kurulum", "standard installation"),
                       'Remove-Item "HKCU:\\SOFTWARE\\PdmVariableStudio" -Recurse   # '
                       + T("-CurrentUser kurulumu, dil seçimi", "-CurrentUser installation, language choice")]))
    story.append(Paragraph(T("<b>Kullanıcı verileri (isteğe bağlı):</b> "
                             + c("%LOCALAPPDATA%\\PdmVariableStudio\\") + " klasörü.",
                             "<b>User data (optional):</b> the "
                             + c("%LOCALAPPDATA%\\PdmVariableStudio\\") + " folder."), step,
                           bulletText="4."))
    story.append(Spacer(1, 4))
    story.append(callout("warn", T("İşlem geçmişini silmeden önce düşünün",
                                   "Think twice before deleting the operation history"), [T(
        "Klasördeki " + c("journal") + " alt klasörü silinirse o güne kadar yapılan değişiklikler "
        "artık araçla geri alınamaz. Emin değilseniz yalnızca yedekleyip saklayın.",
        "If the " + c("journal") + " subfolder is deleted, the changes made until then can no "
        "longer be undone with the tool. If in doubt, back it up and keep it.")]))

    # ================= 8. DESTEK =================
    story.append(H1(T("8. Destek, lisans ve gizlilik", "8. Support, license and privacy"), "s8"))
    story.append(H2(T("8.1 Sorun bildirme", "8.1 Reporting a problem"), "s8-1"))
    story.append(P(T("Sorunları ve önerileri " + link(ISSUES, "GitHub Issues") + " sayfasından "
                     "bildirebilirsiniz. Hızlı çözüm için şunları ekleyin:",
                     "Report problems and suggestions on the " + link(ISSUES, "GitHub Issues")
                     + " page. For a quick fix, include:")))
    story += bullets(T([
        "<b>Hakkında</b> → <b>Bilgileri Kopyala</b> çıktısı (sürüm ve dosya konumları)",
        c("studio.log") + " dosyası",
        "Ne yaptığınız, ne beklediğiniz ve ne olduğu; mümkünse ekran görüntüsü",
    ], [
        "The output of <b>About</b> → <b>Copy Info</b> (version and file locations)",
        "The " + c("studio.log") + " file",
        "What you did, what you expected and what happened; a screenshot if possible",
    ]))
    story.append(callout("warn", T("Paylaşmadan önce", "Before sharing"), [T(
        "Çalışma kitapları ve günlük dosyaları vault verisi (dosya adları, kart değerleri, "
        "kullanıcı adları) içerir. Herkese açık bir sayfaya eklemeden önce gizli bilgileri "
        "temizleyin.",
        "Workbooks and log files contain vault data (file names, card values, user names). "
        "Remove confidential information before attaching them to a public page.")]))
    story.append(H2(T("8.2 Lisans", "8.2 License"), "s8-2"))
    story.append(P(T(
        "PDM Variable Studio <b>MIT lisansı</b> ile dağıtılır: ücretsizdir, lisans anahtarı "
        "yoktur; ticari kullanım, değiştirme ve dağıtım serbesttir. Yazılım “olduğu "
        "gibi” sunulur, herhangi bir garanti verilmez. Tam metin: "
        + link(REPO + "/blob/main/LICENSE", "LICENSE") + ".",
        "PDM Variable Studio is distributed under the <b>MIT license</b>: it is free, there is no "
        "license key, and commercial use, modification and distribution are allowed. The "
        "software is provided “as is”, without any warranty. Full text: "
        + link(REPO + "/blob/main/LICENSE", "LICENSE") + ".")))
    story.append(H2(T("8.3 Gizlilik ve güvenlik", "8.3 Privacy and security"), "s8-3"))
    story += bullets(T([
        "Uygulama internete bağlanmaz, telemetri toplamaz, hiçbir yere veri göndermez.",
        "PDM veritabanına doğrudan SQL ile yazmaz; her işlem PDM API'si üzerinden, sizin PDM "
        "yetkileriniz ve iş akışı kurallarınız içinde yapılır.",
        "Kaynak kodun tamamı " + link(REPO, "GitHub'da") + " açıktır ve incelenebilir.",
    ], [
        "The application does not connect to the internet, collects no telemetry and sends no "
        "data anywhere.",
        "It never writes to the PDM database with SQL directly; every operation goes through the "
        "PDM API, within your PDM permissions and workflow rules.",
        "The complete source code is open " + link(REPO, "on GitHub") + " and can be reviewed.",
    ]))
    story.append(Spacer(1, 10))
    story.append(P(T(
        "SOLIDWORKS ve SOLIDWORKS PDM, Dassault Systèmes SolidWorks Corp. şirketinin "
        "tescilli markalarıdır. Bu proje bağımsız bir çalışmadır; Dassault Systèmes ile "
        "bağlantılı değildir, onun tarafından onaylanmamış ya da desteklenmemiştir.",
        "SOLIDWORKS and SOLIDWORKS PDM are registered trademarks of Dassault Systèmes "
        "SolidWorks Corp. This project is an independent work; it is not affiliated with, "
        "endorsed or supported by Dassault Systèmes."), small))
    return story


def build(lang, output):
    global LANG
    LANG = lang
    doc = GuideDoc(output)
    doc.multiBuild(build_story())
    print(T(f"Kılavuz üretildi: {output} ({doc.page} sayfa, sürüm {VERSION})",
            f"Guide generated: {output} ({doc.page} pages, version {VERSION})"))


for language in (["tr", "en"] if args.lang == "all" else [args.lang]):
    build(language, args.output or DEFAULT_OUTPUTS[language])
