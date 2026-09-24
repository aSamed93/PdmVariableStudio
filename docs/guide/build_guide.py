# -*- coding: utf-8 -*-
r"""PDM Variable Studio — Kurulum ve Kullanım Kılavuzu (PDF) üreticisi.

Kılavuzun KAYNAĞI bu dosyadır: metin, tablolar ve bölüm yapısı aşağıda yazılı. PDF'i elle
düzenlemeyin; burayı değiştirip yeniden üretin. Arayüzde bir düğme, durum ya da ayar
değiştiğinde bu dosya da aynı değişiklikle güncellenmeli.

Kullanım (depo kökünden):
    pip install -r docs/guide/requirements.txt
    python docs/guide/build_guide.py
    python docs/guide/build_guide.py --output C:\Temp\kilavuz.pdf

Varsayılan çıktı: docs/PdmVariableStudio-Kurulum-ve-Kullanim.pdf

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
DEFAULT_OUTPUT = os.path.join(REPO_ROOT, "docs", "PdmVariableStudio-Kurulum-ve-Kullanim.pdf")
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
parser.add_argument("--output", default=DEFAULT_OUTPUT, help="PDF'in yazılacağı yol")
args = parser.parse_args()

register_fonts()
VERSION = read_product_version()
ZIP_NAME = f"PdmVariableStudio-{VERSION}"
REPO = "https://github.com/aSamed93/PdmVariableStudio"
RELEASES = REPO + "/releases/latest"
ISSUES = REPO + "/issues"

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
    steps_ = ["Dosyaları\nseç", "Excel'e\naktar", "Excel'de\ndüzenle", "Geri yükle\n& önizle",
              "Onayla &\nuygula", "Gerekirse\ngeri al"]
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
                         title="PDM Variable Studio — Kurulum ve Kullanım Kılavuzu",
                         author="Samed Tarlak", subject="SOLIDWORKS PDM kart değişkenleri için "
                         "Excel ile toplu düzenleme aracı", creator="PDM Variable Studio",
                         lang="tr-TR", invariant=True, **kw)
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
        canv.drawString(MARGIN_L, PAGE_H - 30 * mm, "SOLIDWORKS PDM PROFESSIONAL İÇİN")
        canv.setFont("Sans-Bold", 32)
        canv.drawString(MARGIN_L, PAGE_H - 50 * mm, "PDM Variable Studio")
        canv.setFont("Sans", 15)
        canv.drawString(MARGIN_L, PAGE_H - 62 * mm, "Kurulum ve Kullanım Kılavuzu")
        canv.setStrokeColor(colors.HexColor("#8FB3DE"))
        canv.setLineWidth(1)
        canv.line(MARGIN_L, PAGE_H - 72 * mm, MARGIN_L + 60 * mm, PAGE_H - 72 * mm)
        canv.setFont("Sans", 10.5)
        canv.setFillColor(colors.HexColor("#DCE7F5"))
        lines = ["Kart değişkenlerini Excel ile toplu düzenleyin —",
                 "önizlemeli, çakışma korumalı ve geri alınabilir."]
        for i, l in enumerate(lines):
            canv.drawString(MARGIN_L, PAGE_H - (82 + i * 6) * mm, l)
        canv.setFont("Sans", 9)
        canv.drawString(MARGIN_L, PAGE_H - 106 * mm, f"Sürüm {VERSION}   ·   Ücretsiz   ·   MIT lisansı")
        canv.restoreState()

    def draw_page(self, canv, doc):
        canv.saveState()
        canv.setStrokeColor(RULE)
        canv.setLineWidth(0.5)
        canv.line(MARGIN_L, PAGE_H - 13 * mm, PAGE_W - MARGIN_R, PAGE_H - 13 * mm)
        canv.setFont("Sans", 7.8)
        canv.setFillColor(MUTED)
        canv.drawString(MARGIN_L, PAGE_H - 11 * mm, "PDM Variable Studio — Kurulum ve Kullanım Kılavuzu")
        canv.drawRightString(PAGE_W - MARGIN_R, PAGE_H - 11 * mm, f"Sürüm {VERSION}")
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
story = []

# --- kapak ---
story.append(Spacer(1, 108 * mm))
cover_intro = ParagraphStyle("ci", parent=base, fontSize=10.2, leading=15.5)
story.append(P("<b>Bu belge kimin için?</b> PDM Variable Studio'yu kendi SOLIDWORKS PDM "
               "ortamında kurmak ve kullanmak isteyen herkes için. Kurulumu yapacak "
               "<b>PDM yöneticisi</b> de, aracı günlük işte kullanacak <b>mühendis / "
               "dokümantasyon ekibi</b> de aradığı her şeyi burada bulur. Başka bir yardıma "
               "ihtiyaç duymadan baştan sona ilerleyebilmeniz amaçlandı.", cover_intro))
story.append(Spacer(1, 4))
story.append(table([
    ["", ""],
    ["Hedef", "SOLIDWORKS PDM <b>Professional</b> 2022 (30.0) ve üstü"],
    ["Platform", "Windows 10 / 11, .NET Framework 4.8.1"],
    ["İndir", link(RELEASES, "GitHub Releases — son sürüm")],
    ["Kaynak kod", link(REPO, "github.com/aSamed93/PdmVariableStudio")],
    ["Sorun bildir", link(ISSUES, "GitHub Issues")],
    ["Gizlilik", "Hiçbir yere veri göndermez; tüm kayıtlar kendi bilgisayarınızda kalır"],
], [0.22, 0.78], header=False, first_bold=True))
story.append(Spacer(1, 6))
story.append(callout("warn", "Başlamadan önce", [
    "Araç PDM'ye <b>yazar</b>. İlk denemeyi bir <b>test vault'unda</b> ya da küçük bir test "
    "klasöründe yapın ve vault yedeğinizin güncel olduğundan emin olun."]))
story.append(P("SOLIDWORKS ve SOLIDWORKS PDM, Dassault Systèmes SolidWorks Corp. şirketinin "
               "tescilli markalarıdır. Bu proje bağımsızdır; Dassault Systèmes ile bağlantılı "
               "değildir, onun tarafından onaylanmamış ya da desteklenmemiştir.", small))
story.append(NextPageTemplate("normal"))
story.append(PageBreak())

# --- içindekiler ---
story.append(Anchor("toc"))
story.append(P("İçindekiler", toc_title))
story.append(P("Başlıklar tıklanabilir. Her sayfanın altındaki sayfa numarası sizi buraya "
               "geri getirir; aynı başlıklar PDF okuyucunuzun yer imleri panelinde de var.", small))
toc = TableOfContents()
toc.levelStyles = [
    ParagraphStyle("t0", fontName="Sans-Bold", fontSize=10.3, leading=13, leftIndent=0,
                   firstLineIndent=0, spaceBefore=3, textColor=ACCENT),
    ParagraphStyle("t1", fontName="Sans", fontSize=9.1, leading=11.2, leftIndent=14,
                   firstLineIndent=0, textColor=INK),
]
toc.dotsMinLevel = 0
story.append(toc)
story.append(PageBreak())

# ================= 1. GENEL BAKIŞ =================
story.append(H1("1. Genel bakış", "s1"))
story.append(P("PDM Variable Studio, SOLIDWORKS PDM Professional'daki dosyaların <b>kart "
               "değişkenlerini</b> (data card değerlerini) Excel ile toplu olarak düzenlemenizi "
               "sağlar. Yüzlerce dosyanın açıklamasını, malzemesini, proje kodunu ya da revizyon "
               "notunu tek tek kart açarak değiştirmek yerine hepsini bir tabloda düzenler, "
               "tek seferde geri yüklersiniz."))
story.append(flow_diagram())

story.append(H2("1.1 Neden basit bir Excel aktarımından farklı?", "s1-1"))
story.append(P("Toplu güncellemenin asıl riski Excel değil, <b>eşzamanlılıktır</b>. Siz dosyayı "
               "dışa aktarıp Excel'de çalışırken bir iş arkadaşınız aynı kartı PDM'de "
               "değiştirmiş olabilir. Bunu görmezden gelen bir araç onun işini sessizce siler. "
               "PDM Variable Studio bu yüzden her hücre için <b>üç değeri</b> birden "
               "karşılaştırır:"))
story.append(table([
    ["Değer", "Anlamı"],
    ["Dışa aktarımdaki", "Excel dosyasını oluşturduğunuz andaki PDM değeri"],
    ["Excel'deki yeni", "Sizin Excel'de bıraktığınız değer"],
    ["PDM'deki güncel", "Dosyayı geri yüklediğiniz andaki taze PDM değeri"],
], [0.28, 0.72], first_bold=True))
story.append(P("Yalnızca <b>sizin değiştirdiğiniz ve PDM tarafında o arada kimsenin dokunmadığı</b> "
               "hücreler yazılır. Başkası da değiştirdiyse hücre <b>çakışma</b> olarak "
               "işaretlenir ve otomatik olarak yazılmaz. Ayrıntı: "
               + ref("s4-5", "4.5 İçe aktarma ve önizleme") + "."))

story.append(H2("1.2 Öne çıkan özellikler", "s1-2"))
story += bullets([
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
])

story.append(H2("1.3 İki parçalı yapı", "s1-3"))
story.append(P("Kurulumu anlamak için bilmeniz gereken tek teknik ayrıntı şu: araç iki "
               "parçadan oluşur ve <b>ikisi de gereklidir</b>."))
story.append(table([
    ["Parça", "Nereye kurulur", "Kim kurar", "Ne işe yarar"],
    ["<b>Uygulama</b><br/>PdmVariableStudio.exe", "Her kullanıcının bilgisayarına (ya da bir "
     "ağ paylaşımına)", "Bilgisayarın yöneticisi / BT", "Asıl pencere: dışa aktarma, önizleme, "
     "uygulama, geri alma"],
    ["<b>Eklenti</b><br/>PdmVariableStudio.AddIn.dll", "Vault'a, <b>bir kez</b>", "PDM yöneticisi",
     "PDM Explorer'a menü komutunu ekler ve uygulamayı başlatır"],
], [0.28, 0.25, 0.18, 0.29]))
story.append(P("Eklenti vault'a bir kez yüklendiğinde PDM onu vault'a bağlanan tüm istemcilere "
               "kendiliğinden dağıtır. Uygulama ise vault'ta değil, diskte durur; bu yüzden "
               "uygulama güncellemeleri için vault'a dokunmak gerekmez."))
story.append(CondPageBreak(150 * mm))

# ================= 2. BAŞLAMADAN ÖNCE =================
story.append(H1("2. Başlamadan önce", "s2"))
story.append(H2("2.1 Gereksinimler", "s2-1"))
story.append(table([
    ["", "Gereksinim"],
    ["PDM istemcisi", "SOLIDWORKS PDM <b>Professional</b> 2022 (30.0) veya üstü. PDM "
     "<b>Standard</b> desteklenmez."],
    ["Windows", "Windows 10 veya 11"],
    [".NET Framework", "4.8.1 — Windows 11'de hazır gelir; Windows 10'da Windows Update ile gelir"],
    ["Vault view", "Aracı kullanacak her bilgisayarda o vault için yerel bir <i>vault view</i> "
     "bulunmalı"],
    ["Tablo programı", "Microsoft Excel (LibreOffice Calc da çalışır)"],
], [0.22, 0.78], first_bold=True))

story.append(H2("2.2 Gerekli yetkiler", "s2-2"))
story.append(table([
    ["İş", "Gereken yetki"],
    ["Uygulamayı <b>Program Files</b> altına kurmak", "Windows yönetici hakkı (bir kez). "
     "Yönetici hakkınız yoksa kullanıcı klasörüne kurabilirsiniz — "
     + ref("s3-2", "bkz. 3.2") + "."],
    ["Eklentiyi vault'a yüklemek", "PDM Administration'da eklenti ekleme yetkisi olan bir hesap "
     "(genellikle <i>Admin</i>)"],
    ["Kart değerlerini değiştirmek", "Normal PDM kullanıcı yetkileriniz geçerlidir. Araç, "
     "PDM'de sizin yapamayacağınız hiçbir şeyi yapamaz; tüm işlemler PDM API'si üzerinden, "
     "PDM'in kurallarına uyarak yapılır."],
], [0.34, 0.66]))

story.append(H2("2.3 Kontrol listesi", "s2-3"))
story.append(P("Kuruluma geçmeden önce:"))
story += bullets([
    "PDM Professional istemcisi kurulu ve vault'ta oturum açabiliyorsunuz.",
    c("C:\\Program Files\\SOLIDWORKS PDM\\EPDM.Interop.epdm.dll") + " dosyası mevcut "
    "(PDM istemcisiyle gelir; kurulumda gerekecek).",
    "Eklentiyi vault'a yükleyecek bir PDM yönetici hesabına erişiminiz var ya da PDM "
    "yöneticinizle konuştunuz.",
    "İlk denemeler için bir test vault'u veya küçük bir test klasörü belirlediniz.",
])
story.append(callout("note", "Kısa yol", [
    "Eklentiyi yüklemeden önce yalnızca uygulamayı kurup doğrudan çalıştırarak aracı "
    "deneyebilirsiniz (" + ref("s3-2", "3.2") + " ve " + ref("s4-1", "4.1") + "). Bu, vault "
    "ayarlarına hiç dokunmadan aracı tanımanın en hızlı yoludur."]))
story.append(PageBreak())

# ================= 3. KURULUM =================
story.append(H1("3. Kurulum", "s3"))
story.append(P("Kurulum dört adımdır: <b>indir → uygulamayı kur → eklentiyi vault'a yükle → "
               "doğrula.</b> Toplam süre genellikle 10–15 dakikadır."))

story.append(H2("3.1 Paketi indirme ve doğrulama", "s3-1"))
story += steps([
    f"{link(RELEASES, 'GitHub Releases')} sayfasını açın ve en son sürümün altındaki "
    + c("PdmVariableStudio-x.y.z.zip") + " dosyasını indirin (örneğin "
    + c(ZIP_NAME + ".zip") + ").",
    "<b>İsteğe bağlı ama önerilir — dosyanın bozulmadığını doğrulayın.</b> İndirdiğiniz "
    "klasörde PowerShell açıp aşağıdaki komutu çalıştırın ve çıkan değeri yayım sayfasında "
    "yazan SHA-256 değeriyle karşılaştırın:",
])
story.append(code([f"Get-FileHash .\\{ZIP_NAME}.zip -Algorithm SHA256"]))
# Numaralandırmayı sürdürmek için elle:
for n, t in [(3, "<b>Engellemeyi kaldırın.</b> İnternetten indirilen zip dosyalarını Windows "
                 "işaretler. Zip'e sağ tıklayın → <b>Özellikler</b> → alttaki <b>Engellemeyi "
                 "kaldır</b> (Unblock) kutusunu işaretleyin → <b>Tamam</b>. Kutu yoksa bu adımı "
                 "atlayın."),
             (4, "Zip'i bir klasöre çıkarın (sağ tık → <b>Tümünü ayıkla…</b>).")]:
    story.append(Paragraph(t, step, bulletText=f"{n}."))
story.append(Spacer(1, 4))
story.append(P("Çıkardığınız klasörün içeriği:"))
story.append(table([
    ["Dosya / klasör", "Ne işe yarar"],
    [c("App\\"), "Uygulama dosyaları (exe + 3 DLL)"],
    [c("AddIn\\PdmVariableStudio.AddIn.dll"), "Vault'a yüklenecek eklenti"],
    [c("install-app.ps1"), "Uygulamayı kuran betik"],
    [c("BENIOKU.txt"), "Üç adımlık kısa özet"],
    [c("KULLANIM.md"), "Kısa kullanım kılavuzu (bu belgenin özeti)"],
    [c("CHANGELOG.md") + ", " + c("LICENSE"), "Sürüm notları ve lisans"],
], [0.42, 0.58]))
story.append(callout("note", "EPDM.Interop.epdm.dll neden pakette yok?", [
    "Bu dosya Dassault Systèmes'e aittir ve yeniden dağıtılamaz. Her PDM istemcisinde zaten "
    "vardır: " + c("C:\\Program Files\\SOLIDWORKS PDM\\") + ". Kurulum betiği onu oradan kendisi "
    "kopyalar; eklenti için de aynı yerden seçeceksiniz."]))

story.append(H2("3.2 Uygulamayı kurma (her bilgisayara)", "s3-2"))
story.append(H3("Standart kurulum (önerilen)", "s3-2a"))
story += steps([
    "Başlat menüsünde <b>PowerShell</b> yazın, sağ tıklayın → <b>Yönetici olarak çalıştır</b>.",
    "Zip'i çıkardığınız klasöre gidin (yolu kendinize göre düzeltin):",
])
story.append(code([f'cd "$env:USERPROFILE\\Downloads\\{ZIP_NAME}"']))
story.append(Paragraph("Kurulum betiğini çalıştırın:", step, bulletText="3."))
story.append(code(["powershell -ExecutionPolicy Bypass -File install-app.ps1"]))
story.append(P("Betik uygulamayı <b>C:\\Program Files\\PDM Variable Studio\\</b> klasörüne "
               "kopyalar, PDM istemcinizden interop dosyasını alır ve eklentinin uygulamayı "
               "bulabilmesi için yolu kayıt defterine yazar. Sonunda yeşil renkte "
               "<b>Kurulum tamam.</b> yazısını görmelisiniz."))
story.append(callout("note", "“-ExecutionPolicy Bypass” güvenli mi?", [
    "Evet. Bu seçenek yalnızca o tek komut için geçerlidir; bilgisayarın PowerShell "
    "ayarlarını kalıcı olarak değiştirmez. Betiğin ne yaptığını görmek isterseniz "
    f"{c('install-app.ps1')} dosyasını Not Defteri ile açabilirsiniz; tamamı Türkçe "
    "açıklamalıdır."]))

story.append(H3("Yönetici hakkı olmadan kurulum", "s3-2b"))
story.append(P("Yönetici hakkınız yoksa uygulamayı kendi kullanıcı klasörünüze kurabilirsiniz. "
               "Normal (yönetici olmayan) bir PowerShell penceresinde:"))
story.append(code(['powershell -ExecutionPolicy Bypass -File install-app.ps1 -CurrentUser `',
                   '    -Destination "$env:LOCALAPPDATA\\PDM Variable Studio"']))

story.append(H3("Ağ paylaşımından kurulum (çok bilgisayarlı ortam)", "s3-2c"))
story.append(P("Uygulamayı bir kez ağ paylaşımına kopyalayıp her bilgisayarda yalnızca yolu "
               "kaydedebilirsiniz. Böylece güncelleme için tek bir klasörü değiştirmek yeter."))
story.append(code(["# 1) Bir kez: paylaşıma kopyala",
                   "powershell -ExecutionPolicy Bypass -File install-app.ps1 `",
                   '    -Destination "\\\\sunucu\\pdm\\VariableStudio"',
                   "",
                   "# 2) Her bilgisayarda (yönetici): yalnızca kaydet",
                   "powershell -ExecutionPolicy Bypass -File install-app.ps1 -RegisterOnly `",
                   '    -Destination "\\\\sunucu\\pdm\\VariableStudio"']))
story.append(callout("warn", "Exe dosyasını tek başına kopyalamayın", [
    "Uygulama yanındaki DLL dosyalarına ihtiyaç duyar. Yalnızca "
    f"{c('PdmVariableStudio.exe')} kopyalanırsa uygulama açılmadan sessizce kapanır. "
    "Her zaman kurulum betiğini kullanın."]))

story.append(H2("3.3 Eklentiyi vault'a yükleme (bir kez)", "s3-3"))
story.append(P("Bu adımı <b>PDM yöneticisi</b> yapar ve vault başına <b>yalnızca bir kez</b> "
               "gerekir. PDM, eklentiyi vault'a bağlanan tüm istemcilere kendisi dağıtır."))
story += steps([
    "<b>SOLIDWORKS PDM Administration</b> aracını açın ve vault'a yönetici hesabıyla oturum açın.",
    "Sol ağaçta vault'u genişletin, <b>Add-ins</b> düğümüne sağ tıklayın → <b>New Add-in…</b>",
    "Açılan pencerede <b>şu iki dosyayı birlikte</b> seçin (Ctrl tuşuyla):",
])
story.append(table([
    ["Dosya", "Nerede"],
    [c("PdmVariableStudio.AddIn.dll"), "Zip'ten çıkardığınız klasördeki " + c("AddIn\\")
     + " klasöründe"],
    [c("EPDM.Interop.epdm.dll"), c("C:\\Program Files\\SOLIDWORKS PDM\\")
     + " — pakette yoktur, PDM istemcinizden alın"],
], [0.38, 0.62]))
for n, t in [(4, "<b>Aç</b>'a tıklayın, eklenti bilgilerini gösteren pencerede <b>Tamam</b> "
                 "diyerek yüklemeyi tamamlayın. Add-ins altında <b>PDM Variable Studio</b> "
                 "görünmelidir."),
             (5, "<b>Tüm</b> PDM Explorer ve Administration pencerelerini kapatın, sonra "
                 "yeniden açın. PDM eklentileri çalışan pencerelere yüklenmez.")]:
    story.append(Paragraph(t, step, bulletText=f"{n}."))
story.append(Spacer(1, 4))
story.append(callout("err", "En sık yapılan hata: ikinci dosyayı atlamak", [
    f"{c('EPDM.Interop.epdm.dll')} eklentiyle <b>birlikte</b> seçilmezse PDM "
    "<i>“…is not a multi-threaded COM-server”</i> gibi yanıltıcı bir hata verir; mesaj eksik "
    "dosyadan hiç söz etmez. Bu hatayı görürseniz eklentiyi silip iki dosyayla yeniden ekleyin."]))

story.append(H2("3.4 Kurulumu doğrulama", "s3-4"))
story.append(P("Aşağıdakilerin hepsi doğruysa kurulum tamamdır:"))
story += bullets([
    "PDM Explorer'da vault içindeki bir klasörde <b>Araçlar</b> menüsünde (ya da sağ tık "
    "menüsünde) <b>PDM Variable Studio</b> komutu görünüyor.",
    "Komuta tıklayınca ayrı bir pencere açılıyor; üstte doğru <b>vault adı</b> ve <b>klasör</b> "
    "yazıyor.",
    "<b>Dışa Aktar</b> sekmesinin altındaki değişken listesi vault'unuzun değişken adlarıyla doluyor.",
    "Pencerenin <b>Hakkında</b> düğmesi doğru sürüm numarasını gösteriyor.",
])
story.append(P("Bir şey yolunda gitmediyse " + ref("s6", "6. Sorun giderme") + " bölümüne bakın."))

story.append(H2("3.5 Ekip kurulumu için ek ayarlar", "s3-5"))
story.append(P("<b>Ortak işlem geçmişi.</b> Varsayılan olarak işlem geçmişi her kullanıcının "
               "kendi bilgisayarında tutulur; bir işlem yalnızca yapıldığı bilgisayarda, aynı "
               "Windows hesabıyla geri alınabilir. Ekip olarak ortak bir geçmiş isteniyorsa "
               "geçmiş bir ağ paylaşımına yönlendirilebilir:"))
story.append(table([
    ["Kapsam", "Ayar"],
    ["Tüm bilgisayar (yönetici)", "Kayıt defterinde " + c("HKLM\\SOFTWARE\\PdmVariableStudio")
     + " altında " + c("JournalRoot") + " adlı dize (String) değeri, örneğin "
     + c("\\\\sunucu\\pdm\\gunluk") + ". Kullanıcı ayarını geçersiz kılar."],
    ["Tek kullanıcı", c("%LOCALAPPDATA%\\PdmVariableStudio\\settings.json") + " dosyasında "
     + c('"journalRoot":"\\\\\\\\sunucu\\\\pdm\\\\gunluk"')],
], [0.27, 0.73], first_bold=True))
story.append(P("Yönetici için PowerShell ile örnek:"))
story.append(code(['New-Item -Path "HKLM:\\SOFTWARE\\PdmVariableStudio" -Force | Out-Null',
                   'Set-ItemProperty -Path "HKLM:\\SOFTWARE\\PdmVariableStudio" -Name JournalRoot `',
                   '    -Value "\\\\sunucu\\pdm\\gunluk" -Type String']))
story.append(callout("note", "Paylaşıma ulaşılamazsa ne olur?", [
    "Uygulama yerel klasöre <b>geçmez</b>; işlemi hiç başlatmaz ve nedenini söyler. Geri "
    "alınamayacak bir değişiklik yapmaktansa hiç yapmamak tercih edilir."]))
story.append(PageBreak())

# ================= 4. KULLANIM =================
story.append(H1("4. Kullanım", "s4"))

story.append(H2("4.1 Uygulamayı açma", "s4-1"))
story.append(table([
    ["Yol", "Nasıl", "Sonuç"],
    ["<b>PDM Explorer'dan</b><br/>(önerilen)", "Vault içinde bir klasördeyken <b>Araçlar</b> "
     "menüsünden ya da sağ tık menüsünden <b>PDM Variable Studio</b>",
     "O klasörün dosyaları listeye hazır gelir"],
    ["<b>Doğrudan</b>", "Kurulum klasöründeki " + c("PdmVariableStudio.exe")
     + " (varsayılan: " + c("C:\\Program Files\\PDM Variable Studio\\") + ")",
     "Birden fazla vault'unuz varsa hangisine bağlanacağı sorulur; liste boş açılır"],
], [0.2, 0.47, 0.33]))
story.append(P("İpucu: doğrudan kullanacaksanız exe'ye sağ tıklayıp <b>Başlat'a sabitle</b> "
               "ya da <b>Görev çubuğuna sabitle</b> diyebilirsiniz."))

story.append(H2("4.2 Pencere düzeni", "s4-2"))
story.append(P("Tek pencere, üç sekme. Üst şeritte bağlı olduğunuz <b>Vault</b> ve <b>Klasör</b>, "
               "sağ üstte <b>Hakkında</b> düğmesi bulunur."))
story.append(table([
    ["Sekme", "Ne yaparsınız"],
    ["<b>Dışa Aktar</b>", "Dosyaları toplar, değişkenleri seçer, Excel dosyasını üretirsiniz"],
    ["<b>İçe Aktar</b>", "Düzenlediğiniz Excel dosyasını yükler, önizlemeyi inceler ve uygularsınız"],
    ["<b>İşlem Geçmişi</b>", "Yapılan işlemleri görür, gerekirse güvenle geri alırsınız"],
], [0.25, 0.75]))

story.append(H2("4.3 Dışa aktarma", "s4-3"))
story.append(H3("1. Dosyaları toplayın", "s4-3a"))
story.append(P("Üç kaynak düğmesi aynı listeye ekler; bunları karışık kullanabilirsiniz. "
               "Aynı dosya iki kez eklenmez; <b>Kaynak</b> sütunu her satırın nereden geldiğini gösterir."))
story.append(table([
    ["Düğme", "Ne yapar"],
    ["<b>Klasör Ekle…</b>", "PDM'in klasör seçme penceresini açar. <b>Alt klasörler</b> "
     "kutusu işaretliyse alt klasörlerdeki dosyalar da eklenir (varsayılan: kapalı)."],
    ["<b>Dosya Ekle…</b>", "PDM'in dosya seçme penceresi; birden fazla dosya seçebilirsiniz."],
    ["<b>Ara ve Ekle…</b>", "Dosya adı desenine (ör. " + c("*.sldprt") + ", " + c("MIL-*")
     + ") ve/veya bir kart değişkeninin değerine göre PDM'de arar, sonuçları ekler."],
    ["<b>Seçilenleri Sil</b> / <b>Listeyi Temizle</b>", "İşaretlediğiniz satırları ya da "
     "listenin tamamını çıkarır."],
], [0.3, 0.7]))
story.append(H3("2. Değişkenleri seçin", "s4-3b"))
story.append(P("Alttaki <b>Dışa aktarılacak değişkenler</b> listesinde yalnızca düzenlemek "
               "istediklerinizi işaretli bırakın (<b>Tümünü seç</b> / <b>Tümünü temizle</b>). "
               "Daha az sütun, Excel'de daha kolay gezinme ve daha az hata demektir."))
story.append(H3("3. Dışa aktarın", "s4-3c"))
story.append(P("<b>Dışa Aktar…</b> düğmesiyle " + c(".xlsx") + " dosyasını kaydedin. Ardından "
               "<b>Excel'de Aç</b> ile dosyayı hemen açabilir, <b>Klasörü Göster</b> ile "
               "Windows Gezgini'nde bulabilirsiniz."))
story.append(callout("note", "Konfigürasyonlu SOLIDWORKS dosyaları", [
    "Her konfigürasyon <b>ayrı bir satırdır</b>. " + c("@") + " satırı konfigürasyondan "
    "bağımsız, dosya düzeyindeki değerdir (SOLIDWORKS'ün <b>Custom</b> sekmesi) ve "
    "adlandırılmış konfigürasyonlardan ayrı tutulur. Konfigürasyonu olmayan dosyalar "
    "(ör. " + c(".docx") + ", " + c(".pdf") + ") tek satırdır."]))

story.append(H2("4.4 Excel'de düzenleme", "s4-4"))
story.append(table([
    ["Yapabilirsiniz", "Yapmayın"],
    [[Paragraph(t, bullet, bulletText="✓") for t in [
        "Değer hücrelerini değiştirmek",
        "Satırları sıralamak, süzmek, silmek",
        "Sütun gizlemek, genişletmek, biçimlendirmek",
        "Sayıları kendi bölgesel biçiminizle yazmak (" + c("12,4") + ")",
        "Dosyayı kaydedip günler sonra geri yüklemek"]],
     [Paragraph(t, bullet, bulletText="✗") for t in [
        "Gizli " + c("_Rows") + " ve " + c("_Metadata") + " sayfalarını silmek ya da "
        "değiştirmek — eşleme bilgisi oradadır",
        "Dolu bir hücreyi yanlışlıkla boşaltmak — boşaltmak <b>“değeri sil”</b> demektir",
        "Yeni satır ekleyerek dosya oluşturmayı beklemek — yeni satırlar atlanır",
        "Dosya adı, klasör, konfigürasyon sütunlarını değiştirmeye çalışmak (kilitlidir)"]]],
], [0.5, 0.5]))
story.append(P("Uygulama satırları sıraya ya da başlığa göre değil, dosyanın içine gizlenmiş "
               "kimliklere göre eşler. Bu yüzden sıralamak veya satır silmek güvenlidir. "
               "Şüpheli görünen bir satır ise hiç uygulanmaz — yanlış dosyaya yazmaktansa "
               "atlamak her zaman tercih edilir."))

story.append(H2("4.5 İçe aktarma ve önizleme", "s4-5"))
story += steps([
    "<b>İçe Aktar</b> sekmesinde <b>Çalışma kitabı seç…</b> ile düzenlediğiniz dosyayı açın.",
    "Uygulama PDM'den taze değerleri okur ve her hücre için bir önizleme satırı üretir. "
    "Tabloda <b>Dışa aktarımdaki</b>, <b>Excel'deki yeni</b> ve <b>PDM'deki güncel</b> değerler "
    "yan yana görünür.",
])
story.append(table([
    ["Durum", "Anlamı", "Ne olur"],
    [status("✓", "Güvenli", OK), "Siz değiştirdiniz; PDM'de dışa aktarımdan beri kimse dokunmadı",
     "<b>Uygulanır</b> (varsayılan olarak seçili)"],
    [status("·", "Değişmemiş", MUTED), "Excel'de dokunulmamış", "Atlanır"],
    [status("·", "Zaten uygulanmış", MUTED), "PDM'de zaten aynı değer var (başkası yazmış)",
     "Atlanır; gereksiz yazma yapılmaz"],
    [status("⚠", "Çakışma", WARN), "Siz değiştirdiniz <b>ve</b> PDM'de de başkası farklı bir "
     "değer yazmış", "<b>Uygulanmaz</b>, seçilemez. Aşağıdaki nota bakın."],
    [status("✗", "Hata", ERR), "Tür uyuşmazlığı (ör. sayı alanına metin), bulunamayan dosya, "
     "bozuk satır vb.", "<b>Uygulanmaz</b>; nedeni <b>Neden</b> sütununda ve ipucunda"],
], [0.2, 0.47, 0.33], row_bgs={1: OK_BG, 4: WARN_BG, 5: ERR_BG}))
story.append(P("Her durum <b>simge + metin + renk</b> ile gösterilir; renkleri ayırt etmek "
               "zor olsa da bilgi kaybolmaz. Bir satırın üzerine geldiğinizde ipucu, nedenini "
               "ve ne yapmanız gerektiğini söyler."))
story.append(P("<b>Süzgeçler:</b> <b>Yalnızca değişenler</b>, <b>Çakışmalar</b>, <b>Hatalar</b> "
               "ve arama kutusu (dosya adı, değişken ya da değere göre). <b>Güvenli olanları "
               "seç</b> / <b>Seçimi temizle</b> ile seçimi toplu değiştirebilirsiniz."))
story.append(callout("warn", "Çakışma görürseniz", [
    "Bu, iş arkadaşınızın değişikliğini koruyan bir güvenlik önlemidir. <b>PDM'deki güncel</b> "
    "değere bakın; hâlâ kendi değerinizin doğru olduğunu düşünüyorsanız aracı yeniden "
    "<b>dışa aktarın</b>, değişikliğinizi yeni dosyada tekrar yapın ve içe aktarın. Böylece "
    "karar bilerek verilmiş olur."]))

story.append(H2("4.6 Değişiklikleri uygulama", "s4-6"))
story += steps([
    "<b>Check-out onayı.</b> PDM'de bir kart değerini değiştirmek için dosyanın check-out "
    "edilmesi gerekir. Gerekirse sarı bir uyarı şeridi çıkar; <b>“Anladım, dosyalar check-out "
    "edilsin”</b> kutusunu işaretlemeden <b>Uygula</b> etkinleşmez.",
    "<b>Check-in seçeneği.</b> <b>“İşlem sonunda bizim çektiğimiz dosyalar check-in edilsin”</b> "
    "açıksa araç, kendi çektiği dosyaları işlem sonunda iade eder (her biri yeni bir sürüm "
    "üretir). Yanındaki kutuya bir check-in yorumu yazabilirsiniz. Kapalıysa dosyalar sizde "
    "çekili kalır.",
    "<b>Uygula</b>'ya tıklayın. İlerleme alttaki durum çubuğunda görünür; gerekirse "
    "<b>Durdur</b> ile kesebilirsiniz.",
])
story.append(P("Bilmeniz iyi olanlar:"))
story += bullets([
    "<b>Sizin zaten çekili tuttuğunuz</b> dosyalar işlem sonunda check-in <b>edilmez</b>; "
    "üzerindeki işinizi bozmamak için.",
    "<b>Başkasının çekili tuttuğu</b> dosyalar atlanır, gerekçesi gösterilir.",
    "Bir dosyada yazma başarısız olursa aracın yaptığı check-out geri alınır; dosyada iz kalmaz. "
    "Diğer dosyalar işlemeye devam eder.",
    "Dosya SOLIDWORKS'te açıksa yazılamaz; kapatıp o dosya için yeniden uygulayın.",
])

story.append(H2("4.7 İşlem geçmişi ve geri alma", "s4-7"))
story.append(P("Her uygulama <b>İşlem Geçmişi</b> sekmesinde listelenir (tarih, kullanıcı, "
               "kapsam, sonuç). Geri almak için:"))
story += steps([
    "Listeden işlemi seçin.",
    "<b>Geri alma önizlemesi</b>'ne tıklayın. Araç, her hücre için PDM'deki güncel değeri "
    "kontrol eder.",
    "Önizlemeyi inceleyip <b>Geri Al</b>'a tıklayın.",
])
story.append(table([
    ["Önizlemedeki durum", "Anlamı", "Ne olur"],
    [status("✓", "Güvenli", OK), "Değer hâlâ sizin yazdığınız değer", "Eski değere döndürülür"],
    [status("·", "Zaten geri alınmış", MUTED), "Değer zaten eskisine dönmüş", "Atlanır"],
    [status("⚠", "Çakışma", WARN), "Sizden sonra başkası o değeri değiştirmiş",
     "<b>Dokunulmaz</b> — onun işi korunur"],
    [status("✗", "Kullanılamaz", ERR), "Dosya bulunamıyor, erişilemiyor vb.", "Atlanır; nedeni gösterilir"],
], [0.25, 0.42, 0.33], row_bgs={1: OK_BG, 3: WARN_BG, 4: ERR_BG}))
story.append(P("Geri alma da bir işlemdir ve geçmişe kaydedilir. İşlem geçmişi varsayılan "
               "olarak kullanıcıya ve bilgisayara özeldir — " + ref("s3-5", "3.5") + "."))

story.append(H2("4.8 Örnek senaryo", "s4-8"))
story.append(P("<i>“Proje-A klasöründeki 120 parçanın Malzeme ve Açıklama değerlerini "
               "güncellemem gerekiyor.”</i>"))
story += steps([
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
])
story.append(PageBreak())

# ================= 5. DOSYALAR =================
story.append(H1("5. Dosyalar ve ayarlar", "s5"))
story.append(P("Uygulamanın yazdığı her şey bu bilgisayarda, "
               + c("%LOCALAPPDATA%\\PdmVariableStudio\\") + " altındadır. Klasörü açmanın en "
               "kolay yolu: uygulamada <b>Hakkında</b> → ilgili satırdaki düğme. Ya da "
               "Windows Gezgini'nin adres çubuğuna " + c("%LOCALAPPDATA%\\PdmVariableStudio")
               + " yazın."))
story.append(table([
    ["Dosya", "İçerik", "Silinirse"],
    [c("studio.log"), "Uygulama ve eklenti günlüğü. Sorun bildirirken ekleyin.",
     "Sorun yok; yeniden oluşur"],
    [c("journal\\&lt;vault&gt;\\"), "İşlem geçmişi — geri alma buradan çalışır.",
     "<font color='#B3261E'><b>Geri alınamaz!</b> Silmeyin.</font>"],
    [c("settings.json"), "Tercihler: alt klasörler, check-in seçeneği ve yorumu, son klasör, "
     "işlem geçmişi konumu.", "Varsayılanlar gelir"],
], [0.25, 0.47, 0.28]))
story.append(P("Tüm dosyalar düz metindir ve Not Defteri ile okunabilir. Uygulama internete "
               "bağlanmaz, hiçbir yere veri göndermez."))

story.append(H2("5.1 Hakkında penceresi", "s5-1"))
story.append(P("Sağ üstteki <b>Hakkında</b> düğmesi sürümü, günlük / işlem geçmişi / ayar "
               "dosyalarının konumlarını (her biri tek tıkla açılır) ve proje bağlantılarını "
               "gösterir. <b>Bilgileri Kopyala</b> tüm bu bilgileri panoya alır; destek "
               "isterken yapıştırmanız yeterlidir."))

# ================= 6. SORUN GİDERME =================
story.append(H1("6. Sorun giderme", "s6"))
story.append(P("İlk bakılacak yer her zaman " + c("studio.log") + " dosyasıdır ("
               + ref("s5", "5. Dosyalar ve ayarlar") + "). Eklenti satırları " + c("[eklenti]")
               + " etiketiyle başlar ve akışı adım adım gösterir:"))
story.append(code(["[BILGI] [eklenti] Eklenti yüklendi, komut kaydedildi.   ← eklenti sağlam",
                   "[BILGI] [eklenti] Komut tetiklendi (veri sayısı: 1).    ← komuta tıklandı",
                   "[BILGI] [eklenti] Seçili klasör: Parts (#42).",
                   "[BILGI] [eklenti] Uygulama başlatılıyor: ..."]))

faq = [
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
    ("Bir dosya için “özel olarak açık” / kilitli hatası.",
     ["Dosya SOLIDWORKS'te ya da başka bir programda açık. Kapatıp o dosya için yeniden "
      "uygulayın; diğer dosyalar etkilenmez."]),
    ("Sayılar yanlış görünüyor (ör. 12,4 → 124).",
     ["Araç Türkçe ve İngilizce ondalık biçimlerini doğru ayırt eder. Yine de beklenmedik bir "
      "değer görürseniz <b>uygulamayın</b>, önizleme ekran görüntüsüyle birlikte bildirin."]),
    ("Başka bir bilgisayardan yaptığım işlemi geri alamıyorum.",
     ["İşlem geçmişi varsayılan olarak bilgisayara ve kullanıcıya özeldir. Ekip için ortak "
      "geçmiş: " + ref("s3-5", "3.5") + "."]),
]
for q, answers in faq:
    block = [Paragraph(f"<b>{q}</b>", ParagraphStyle("q", parent=base, textColor=ACCENT,
                                                     spaceBefore=6, spaceAfter=3))]
    block += [Paragraph(a, bullet, bulletText="→") for a in answers]
    story.append(KeepTogether(block))
story.append(CondPageBreak(90 * mm))

# ================= 7. GÜNCELLEME / KALDIRMA =================
story.append(H1("7. Güncelleme ve kaldırma", "s7"))
story.append(H2("7.1 Yeni sürüme geçme", "s7-1"))
story.append(table([
    ["Parça", "Ne zaman", "Nasıl"],
    ["<b>Uygulama</b>", "Her yeni sürümde", "Yeni zip'i indirip " + c("install-app.ps1")
     + "'i yeniden çalıştırın. Önce uygulama penceresini kapatın. PDM Explorer'ı kapatmak "
     "<b>gerekmez</b>. İşlem geçmişiniz ve ayarlarınız korunur."],
    ["<b>Eklenti</b>", "Yalnızca sürüm notlarında <b>“eklenti güncellendi”</b> yazıyorsa",
     "Administration → Add-ins → mevcut eklentiyi yeni " + c("PdmVariableStudio.AddIn.dll")
     + " ve " + c("EPDM.Interop.epdm.dll") + " ile güncelleyin, ardından tüm PDM "
     "pencerelerini kapatıp açın."],
], [0.16, 0.26, 0.58]))
story.append(P("Sürüm notları: " + link(REPO + "/releases", "GitHub Releases") + " ve paketteki "
               + c("CHANGELOG.md") + "."))

story.append(H2("7.2 Kaldırma", "s7-2"))
story += steps([
    "<b>Eklenti (PDM yöneticisi):</b> Administration → vault → <b>Add-ins</b> → "
    "<b>PDM Variable Studio</b>'ya sağ tık → <b>Remove</b> (Kaldır). Ardından tüm PDM "
    "pencerelerini kapatıp açın.",
    "<b>Uygulama:</b> uygulama penceresini kapatın, kurulum klasörünü silin (varsayılan: "
    + c("C:\\Program Files\\PDM Variable Studio\\") + ").",
    "<b>Kayıt defteri:</b> yönetici PowerShell'de:",
])
story.append(code(['Remove-Item "HKLM:\\SOFTWARE\\PdmVariableStudio" -Recurse   # standart kurulum',
                   'Remove-Item "HKCU:\\SOFTWARE\\PdmVariableStudio" -Recurse   # -CurrentUser kurulumu']))
story.append(Paragraph("<b>Kullanıcı verileri (isteğe bağlı):</b> "
                       + c("%LOCALAPPDATA%\\PdmVariableStudio\\") + " klasörü.", step,
                       bulletText="4."))
story.append(Spacer(1, 4))
story.append(callout("warn", "İşlem geçmişini silmeden önce düşünün", [
    "Klasördeki " + c("journal") + " alt klasörü silinirse o güne kadar yapılan değişiklikler "
    "artık araçla geri alınamaz. Emin değilseniz yalnızca yedekleyip saklayın."]))

# ================= 8. DESTEK =================
story.append(H1("8. Destek, lisans ve gizlilik", "s8"))
story.append(H2("8.1 Sorun bildirme", "s8-1"))
story.append(P("Sorunları ve önerileri " + link(ISSUES, "GitHub Issues") + " sayfasından "
               "bildirebilirsiniz. Hızlı çözüm için şunları ekleyin:"))
story += bullets([
    "<b>Hakkında</b> → <b>Bilgileri Kopyala</b> çıktısı (sürüm ve dosya konumları)",
    c("studio.log") + " dosyası",
    "Ne yaptığınız, ne beklediğiniz ve ne olduğu; mümkünse ekran görüntüsü",
])
story.append(callout("warn", "Paylaşmadan önce", [
    "Çalışma kitapları ve günlük dosyaları vault verisi (dosya adları, kart değerleri, "
    "kullanıcı adları) içerir. Herkese açık bir sayfaya eklemeden önce gizli bilgileri "
    "temizleyin."]))
story.append(H2("8.2 Lisans", "s8-2"))
story.append(P("PDM Variable Studio <b>MIT lisansı</b> ile dağıtılır: ücretsizdir, lisans anahtarı "
               "yoktur; ticari kullanım, değiştirme ve dağıtım serbesttir. Yazılım “olduğu "
               "gibi” sunulur, herhangi bir garanti verilmez. Tam metin: "
               + link(REPO + "/blob/main/LICENSE", "LICENSE") + "."))
story.append(H2("8.3 Gizlilik ve güvenlik", "s8-3"))
story += bullets([
    "Uygulama internete bağlanmaz, telemetri toplamaz, hiçbir yere veri göndermez.",
    "PDM veritabanına doğrudan SQL ile yazmaz; her işlem PDM API'si üzerinden, sizin PDM "
    "yetkileriniz ve iş akışı kurallarınız içinde yapılır.",
    "Kaynak kodun tamamı " + link(REPO, "GitHub'da") + " açıktır ve incelenebilir.",
])
story.append(Spacer(1, 10))
story.append(P("SOLIDWORKS ve SOLIDWORKS PDM, Dassault Systèmes SolidWorks Corp. şirketinin "
               "tescilli markalarıdır. Bu proje bağımsız bir çalışmadır; Dassault Systèmes ile "
               "bağlantılı değildir, onun tarafından onaylanmamış ya da desteklenmemiştir.",
               small))

doc = GuideDoc(args.output)
doc.multiBuild(story)
print(f"Kılavuz üretildi: {args.output} ({doc.page} sayfa, sürüm {VERSION})")
