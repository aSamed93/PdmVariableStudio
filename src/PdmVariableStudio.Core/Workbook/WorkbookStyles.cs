using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;

namespace PdmVariableStudio.Core.Workbook;

/// <summary>
/// Çalışma kitabının biçim tablosu ve stil indisleri.
/// </summary>
/// <remarks>
/// <para>
/// Hücre kilidi biçimden gelir: SpreadsheetML'de varsayılan <c>Locked = true</c>'dur ve sayfa
/// koruması açıldığında yalnızca <c>Locked = false</c> olan hücreler düzenlenebilir. Bu yüzden
/// "düzenlenebilir" ve "kilitli" varyantları ayrı stil kayıtlarıdır.
/// </para>
/// <para>
/// Tarihler seri numarası + tarih biçimi olarak yazılır, <c>CellValues.Date</c> ile DEĞİL:
/// ikincisi SpreadsheetML'de geçerli olsa da Excel sürümleri arasında tutarsız davranıyor ve
/// bazı yollarda hücreyi metne çeviriyor. Seri numarası her sürümde gerçek tarihtir.
/// </para>
/// </remarks>
internal static class WorkbookStyles
{
    /// <summary>Kilitli, genel biçim. Teknik sayfalar ve varsayılan.</summary>
    public const uint Default = 0;

    /// <summary>Kalın, dolgulu, kilitli. Başlık satırı.</summary>
    public const uint Header = 1;

    /// <summary>Kilitli, açık gri dolgu. Kimlik sütunları (#, dosya adı, klasör, konfigürasyon).</summary>
    public const uint Identity = 2;

    /// <summary>Kilitsiz, genel biçim. Düzenlenebilir metin / sayı / boolean.</summary>
    public const uint Editable = 3;

    /// <summary>Kilitsiz, tarih biçimi.</summary>
    public const uint EditableDate = 4;

    /// <summary>Kilitli, gri dolgu. Salt okunur değişken sütunu.</summary>
    public const uint ReadOnlyVariable = 5;

    /// <summary>Kilitli, gri dolgu, tarih biçimi.</summary>
    public const uint ReadOnlyDate = 6;

    /// <summary>Özel tarih biçiminin kimliği. 164'ten küçük değerler yerleşiktir.</summary>
    private const uint DateFormatId = 164;

    public static Stylesheet Create()
    {
        var numberingFormats = new NumberingFormats(
            new NumberingFormat
            {
                NumberFormatId = DateFormatId,
                FormatCode = "yyyy\\-mm\\-dd",
            })
        {
            Count = 1,
        };

        var fonts = new Fonts(
            new Font(new FontSize { Val = 11D }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11D }, new FontName { Val = "Calibri" }))
        {
            Count = 2,
        };

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),

            // 2 - başlık dolgusu
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFDCE6F1" })
            {
                PatternType = PatternValues.Solid,
            }),

            // 3 - kimlik/salt okunur dolgusu
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFF2F2F2" })
            {
                PatternType = PatternValues.Solid,
            }))
        {
            Count = 4,
        };

        var borders = new Borders(
            new Border(
                new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder()),
            new Border(
                new LeftBorder(),
                new RightBorder(),
                new TopBorder(),
                new BottomBorder { Style = BorderStyleValues.Thin },
                new DiagonalBorder()))
        {
            Count = 2,
        };

        var cellStyleFormats = new CellStyleFormats(
            new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 })
        {
            Count = 1,
        };

        var cellFormats = new CellFormats(
            // 0 - Default
            Locked(numberFormatId: 0, fontId: 0, fillId: 0, borderId: 0),

            // 1 - Header
            Locked(numberFormatId: 0, fontId: 1, fillId: 2, borderId: 1),

            // 2 - Identity
            Locked(numberFormatId: 0, fontId: 0, fillId: 3, borderId: 0),

            // 3 - Editable
            Unlocked(numberFormatId: 0, fontId: 0, fillId: 0, borderId: 0),

            // 4 - EditableDate
            Unlocked(numberFormatId: DateFormatId, fontId: 0, fillId: 0, borderId: 0),

            // 5 - ReadOnlyVariable
            Locked(numberFormatId: 0, fontId: 0, fillId: 3, borderId: 0),

            // 6 - ReadOnlyDate
            Locked(numberFormatId: DateFormatId, fontId: 0, fillId: 3, borderId: 0))
        {
            Count = 7,
        };

        return new Stylesheet(numberingFormats, fonts, fills, borders, cellStyleFormats, cellFormats);
    }

    /// <summary>Değişken sütunu için stil indisi seçer.</summary>
    public static uint ForVariable(Domain.PdmVariableType type, bool isReadOnly)
    {
        var isDate = type == Domain.PdmVariableType.Date;

        if (isReadOnly)
        {
            return isDate ? ReadOnlyDate : ReadOnlyVariable;
        }

        return isDate ? EditableDate : Editable;
    }

    private static CellFormat Locked(uint numberFormatId, uint fontId, uint fillId, uint borderId) =>
        new()
        {
            NumberFormatId = numberFormatId,
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            FormatId = 0,
            ApplyNumberFormat = numberFormatId != 0,
            ApplyFill = fillId > 1,
            ApplyFont = fontId != 0,
            ApplyBorder = borderId != 0,
            ApplyProtection = true,
            Protection = new Protection { Locked = true },
        };

    private static CellFormat Unlocked(uint numberFormatId, uint fontId, uint fillId, uint borderId) =>
        new()
        {
            NumberFormatId = numberFormatId,
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            FormatId = 0,
            ApplyNumberFormat = numberFormatId != 0,
            ApplyFill = fillId > 1,
            ApplyFont = fontId != 0,
            ApplyBorder = borderId != 0,
            ApplyProtection = true,
            Protection = new Protection { Locked = false },
        };
}
