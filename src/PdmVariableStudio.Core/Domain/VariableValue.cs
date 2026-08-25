using System;
using System.Globalization;

namespace PdmVariableStudio.Core.Domain;

/// <summary>Bir <see cref="VariableValue"/> nesnesinin taşıdığı normalize içeriğin türü.</summary>
public enum VariableValueKind
{
    /// <summary>null, boş dize ya da yalnızca boşluk. Üç kaynak da BURAYA düşer.</summary>
    Empty = 0,
    Text = 1,
    Int = 2,
    Float = 3,
    Bool = 4,
    Date = 5,

    /// <summary>
    /// Beyan edilen tipe çevrilemedi. Yalnızca kendisine eşittir ve ASLA PDM'ye yazılmaz.
    /// </summary>
    Unparseable = 6,
}

/// <summary>
/// Tipli, normalize edilmiş bir kart değeri.
/// </summary>
/// <remarks>
/// <para>
/// Bu tipin varlık sebebi: <c>"10"</c> ile <c>10</c>, <c>"12,4"</c> ile <c>12.4</c>,
/// <c>null</c> ile <c>""</c> ile <c>"   "</c> karşılaştırmalarının doğru sonuç vermesi.
/// Düz string karşılaştırması bu üçünde de yanlış "değişti" kararı üretir ve kullanıcıyı
/// gereksiz yazmaya iter.
/// </para>
/// <para>
/// Eşitlik NORMALİZE EDİLMİŞ içerik üzerinden hesaplanır; <see cref="RawText"/> yalnızca
/// gösterim ve teşhis içindir.
/// </para>
/// </remarks>
public sealed class VariableValue : IEquatable<VariableValue>
{
    private static readonly string[] TrueTokens =
        { "true", "1", "-1", "yes", "evet", "dogru", "doğru", "y", "e" };

    private static readonly string[] FalseTokens =
        { "false", "0", "no", "hayir", "hayır", "yanlis", "yanlış", "n", "h" };

    private readonly long _int;
    private readonly decimal _float;
    private readonly bool _bool;
    private readonly DateTime _date;
    private readonly string _text;

    private VariableValue(
        VariableValueKind kind,
        string rawText,
        string text = "",
        long intValue = 0,
        decimal floatValue = 0m,
        bool boolValue = false,
        DateTime dateValue = default)
    {
        Kind = kind;
        RawText = rawText ?? string.Empty;
        _text = text;
        _int = intValue;
        _float = floatValue;
        _bool = boolValue;
        _date = dateValue;
    }

    public VariableValueKind Kind { get; }

    /// <summary>Değerin geldiği ham metin. Gösterim ve hata mesajları için.</summary>
    public string RawText { get; }

    public bool IsEmpty => Kind == VariableValueKind.Empty;

    public bool IsUnparseable => Kind == VariableValueKind.Unparseable;

    public static VariableValue Empty { get; } = new(VariableValueKind.Empty, string.Empty);

    public static VariableValue Unparseable(string raw) => new(VariableValueKind.Unparseable, raw);

    // ------------------------------------------------------------------ oluşturma

    /// <summary>
    /// PDM'den (ya da Excel'den) gelen ham bir değeri, değişkenin beyan edilen tipine göre
    /// normalize eder.
    /// </summary>
    /// <param name="raw">
    /// <c>null</c>, <see cref="string"/>, sayısal tip, <see cref="bool"/> ya da
    /// <see cref="DateTime"/> olabilir. COM'dan gelen <c>object</c> doğrudan verilebilir.
    /// </param>
    /// <param name="type">Değişkenin vault'ta beyan edilen tipi.</param>
    /// <param name="culture">
    /// Metin ayrıştırmasında invariant'tan SONRA denenecek kültür. <c>null</c> ise yalnızca
    /// invariant denenir. Excel'den gelen METİN hücreleri için kullanıcının kültürü verilir.
    /// </param>
    public static VariableValue From(object? raw, PdmVariableType type, CultureInfo? culture = null)
    {
        if (raw is null || raw is DBNull)
        {
            return Empty;
        }

        // Zaten tipli gelen değerler ayrıştırmaya hiç girmez. Excel'in gerçek sayı / tarih /
        // boolean hücreleri ve PDM'in tipli VARIANT'ları bu yoldan geçer; locale sorunu
        // burada doğmadan çözülür.
        switch (raw)
        {
            case bool b when type is PdmVariableType.Bool or PdmVariableType.None:
                return FromBool(b);

            case bool b:
                return FromRawText(b ? "true" : "false", type, culture);

            case DateTime dt when type is PdmVariableType.Date or PdmVariableType.None:
                return FromDate(dt);

            case DateTime dt:
                return FromRawText(dt.ToString("o", CultureInfo.InvariantCulture), type, culture);
        }

        if (IsNumericBox(raw))
        {
            var asDecimal = Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
            return type switch
            {
                PdmVariableType.Int => FromInt(decimal.ToInt64(decimal.Truncate(asDecimal))),
                PdmVariableType.Float or PdmVariableType.None => FromFloat(asDecimal),
                PdmVariableType.Bool => FromBool(asDecimal != 0m),
                PdmVariableType.Date => FromOaDate((double)asDecimal),
                _ => FromRawText(asDecimal.ToString(CultureInfo.InvariantCulture), type, culture),
            };
        }

        var text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
        return FromRawText(text, type, culture);
    }

    /// <summary>Metin bir değeri beyan edilen tipe çevirir.</summary>
    public static VariableValue FromRawText(string? raw, PdmVariableType type, CultureInfo? culture = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Empty;
        }

        var trimmed = raw!.Trim();

        switch (type)
        {
            case PdmVariableType.Int:
                return TryParseInt(trimmed, culture, out var intValue)
                    ? new VariableValue(VariableValueKind.Int, raw, intValue: intValue)
                    : Unparseable(raw);

            case PdmVariableType.Float:
                return TryParseDecimal(trimmed, culture, out var floatValue)
                    ? new VariableValue(VariableValueKind.Float, raw, floatValue: floatValue)
                    : Unparseable(raw);

            case PdmVariableType.Bool:
                return TryParseBool(trimmed, out var boolValue)
                    ? new VariableValue(VariableValueKind.Bool, raw, boolValue: boolValue)
                    : Unparseable(raw);

            case PdmVariableType.Date:
                return TryParseDate(trimmed, culture, out var dateValue)
                    ? new VariableValue(VariableValueKind.Date, raw, dateValue: dateValue)
                    : Unparseable(raw);

            default:
                // Metin: baştaki/sondaki boşluk atılır. Kullanıcı Excel'de yanlışlıkla boşluk
                // bırakıyor ve bu, gereksiz "değişti" kararlarının en yaygın sebebi.
                return new VariableValue(VariableValueKind.Text, raw, text: trimmed);
        }
    }

    public static VariableValue FromInt(long value) =>
        new(VariableValueKind.Int, value.ToString(CultureInfo.InvariantCulture), intValue: value);

    public static VariableValue FromFloat(decimal value) =>
        new(VariableValueKind.Float, value.ToString(CultureInfo.InvariantCulture), floatValue: value);

    public static VariableValue FromBool(bool value) =>
        new(VariableValueKind.Bool, value ? "TRUE" : "FALSE", boolValue: value);

    /// <summary>
    /// Tarih değeri. Saat bileşeni ATILIR: PDM kart tarihleri gün hassasiyetinde tutuluyor ve
    /// saat farkı yanlış "değişti" kararı üretiyordu.
    /// </summary>
    public static VariableValue FromDate(DateTime value)
    {
        var day = value.Date;
        var raw = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new VariableValue(VariableValueKind.Date, raw, dateValue: day);
    }

    public static VariableValue FromText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Empty
            : new VariableValue(VariableValueKind.Text, value!, text: value!.Trim());

    // ---------------------------------------------------------------- dışarı verme

    /// <summary>
    /// Workbook'un <c>_Rows</c> sayfasında ve journal'da saklanan, kültür bağımsız gösterim.
    /// <see cref="FromStorage"/> ile tam dönüş yapar.
    /// </summary>
    public string ToStorageString() => Kind switch
    {
        VariableValueKind.Empty => string.Empty,
        VariableValueKind.Text => _text,
        VariableValueKind.Int => _int.ToString(CultureInfo.InvariantCulture),
        VariableValueKind.Float => _float.ToString(CultureInfo.InvariantCulture),
        VariableValueKind.Bool => _bool ? "true" : "false",
        VariableValueKind.Date => _date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => RawText,
    };

    /// <summary><see cref="ToStorageString"/> çıktısını geri okur.</summary>
    public static VariableValue FromStorage(string? stored, PdmVariableType type) =>
        FromRawText(stored, type, culture: null);

    /// <summary>
    /// PDM'ye <c>SetVar</c> ile gönderilecek değer. Tipli gönderilir ki PDM kendi dönüşümünü
    /// kullanıcının kültürüne göre yapmak zorunda kalmasın.
    /// </summary>
    public object ToPdmObject() => Kind switch
    {
        VariableValueKind.Empty => string.Empty,
        VariableValueKind.Text => _text,
        VariableValueKind.Int => _int,
        VariableValueKind.Float => _float,
        VariableValueKind.Bool => _bool,
        VariableValueKind.Date => _date,
        _ => throw new InvalidOperationException(
            "Ayrıştırılamayan değer PDM'ye yazılamaz. Çağıran taraf IsUnparseable kontrolünü atlamış."),
    };

    /// <summary>
    /// Excel hücresine yazılacak değer. Tipli hücre üretilebilmesi için kutulanmış gerçek tip
    /// döner; metin ise dize döner. <c>null</c> boş hücre demektir.
    /// </summary>
    public object? ToExcelObject() => Kind switch
    {
        VariableValueKind.Empty => null,
        VariableValueKind.Text => _text,
        VariableValueKind.Int => _int,
        VariableValueKind.Float => _float,
        VariableValueKind.Bool => _bool,
        VariableValueKind.Date => _date,
        _ => RawText,
    };

    /// <summary>Kullanıcıya gösterilecek metin; kullanıcının kendi kültüründe biçimlenir.</summary>
    public string ToDisplayString(CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentCulture;
        return Kind switch
        {
            VariableValueKind.Empty => string.Empty,
            VariableValueKind.Text => _text,
            VariableValueKind.Int => _int.ToString(c),
            VariableValueKind.Float => _float.ToString(c),
            VariableValueKind.Bool => _bool ? "TRUE" : "FALSE",
            VariableValueKind.Date => _date.ToString("d", c),
            _ => RawText,
        };
    }

    // -------------------------------------------------------------------- eşitlik

    public bool Equals(VariableValue? other)
    {
        if (other is null)
        {
            return false;
        }

        // Tür farklıysa eşit değiller. Empty yalnızca Empty'ye, Unparseable yalnızca aynı
        // ham metne eşittir.
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            VariableValueKind.Empty => true,
            VariableValueKind.Text => string.Equals(_text, other._text, StringComparison.Ordinal),
            VariableValueKind.Int => _int == other._int,
            VariableValueKind.Float => _float == other._float,
            VariableValueKind.Bool => _bool == other._bool,
            VariableValueKind.Date => _date == other._date,

            // İki bozuk değerin "aynı" sayılıp sessizce geçmesini engeller.
            _ => string.Equals(RawText, other.RawText, StringComparison.Ordinal),
        };
    }

    public override bool Equals(object? obj) => Equals(obj as VariableValue);

    public override int GetHashCode() => Kind switch
    {
        VariableValueKind.Empty => 0,
        VariableValueKind.Text => StringComparer.Ordinal.GetHashCode(_text),
        VariableValueKind.Int => _int.GetHashCode(),
        VariableValueKind.Float => _float.GetHashCode(),
        VariableValueKind.Bool => _bool.GetHashCode(),
        VariableValueKind.Date => _date.GetHashCode(),
        _ => StringComparer.Ordinal.GetHashCode(RawText),
    };

    public override string ToString() => $"{Kind}:{ToStorageString()}";

    // ----------------------------------------------------------------- ayrıştırma

    private static bool IsNumericBox(object raw) =>
        raw is byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal;

    private static VariableValue FromOaDate(double serial)
    {
        // Excel'in 1900 tarih sistemindeki geçerli aralık. Dışındaki değerler tarih değil,
        // kullanıcının yazdığı ham sayıdır.
        if (serial < 1 || serial > 2958465)
        {
            return Unparseable(serial.ToString(CultureInfo.InvariantCulture));
        }

        return FromDate(DateTime.FromOADate(serial));
    }

    private static bool TryParseInt(string text, CultureInfo? culture, out long value)
    {
        // Binlik ayracına izin veren denemeler EN SONA: gerekçe TryParseDecimal içinde.
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (culture is not null && long.TryParse(text, NumberStyles.Integer, culture, out value))
        {
            return true;
        }

        // "12,0" gibi tamsayıya denk gelen ondalıklı metinler kabul edilir; kullanıcı bir
        // tamsayı alanına 12,0 yazdığında bunu hata saymak gereksiz katılık olurdu.
        if (TryParseDecimal(text, culture, out var asDecimal) && decimal.Truncate(asDecimal) == asDecimal)
        {
            value = decimal.ToInt64(asDecimal);
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Ondalık ayrıştırma. Deneme sırası kritiktir.
    /// </summary>
    /// <remarks>
    /// Binlik ayracına izin veren denemeler EN SONA bırakılır. Aksi hâlde invariant kültürde
    /// <c>"12,4"</c> metni virgülü BİNLİK ayracı sayıp <c>124</c> olarak ayrıştırılır ve
    /// Türkçe yazan bir kullanıcının 12,4 değeri PDM'ye 124 olarak yazılırdı — sessiz ve
    /// on kat büyük bir veri bozulması. Önce ayraçsız (kesin) yorumlar denenir; ancak
    /// hiçbiri tutmazsa gruplu yoruma düşülür.
    /// </remarks>
    private static bool TryParseDecimal(string text, CultureInfo? culture, out decimal value)
    {
        const NumberStyles Plain = NumberStyles.Float;
        const NumberStyles Grouped = NumberStyles.Float | NumberStyles.AllowThousands;

        if (decimal.TryParse(text, Plain, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (culture is not null && decimal.TryParse(text, Plain, culture, out value))
        {
            return true;
        }

        if (decimal.TryParse(text, Grouped, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return culture is not null && decimal.TryParse(text, Grouped, culture, out value);
    }

    private static bool TryParseBool(string text, out bool value)
    {
        // Ordinal-ignorecase bilinçli: tr-TR'de "I" harfinin küçültülmesi kültüre duyarlı
        // karşılaştırmada TRUE / true eşitliğini bozabiliyor.
        foreach (var token in TrueTokens)
        {
            if (string.Equals(text, token, StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }
        }

        foreach (var token in FalseTokens)
        {
            if (string.Equals(text, token, StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }
        }

        value = false;
        return false;
    }

    private static bool TryParseDate(string text, CultureInfo? culture, out DateTime value)
    {
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            value = value.Date;
            return true;
        }

        if (culture is not null && DateTime.TryParse(text, culture, DateTimeStyles.None, out value))
        {
            value = value.Date;
            return true;
        }

        // Excel bazı durumlarda tarihi seri numarası olarak METİN hücresine yazıyor. Son çare:
        // sayı olarak okunup OADate kabul edilir.
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            && serial >= 1
            && serial <= 2958465)
        {
            value = DateTime.FromOADate(serial).Date;
            return true;
        }

        value = default;
        return false;
    }
}
