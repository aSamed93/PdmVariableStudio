using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PdmVariableStudio.Core.Journal;

/// <summary>
/// Tek satırlık, düz (iç içe olmayan) JSON nesneleri için asgari yazıcı/okuyucu.
/// </summary>
/// <remarks>
/// <para>
/// Neden hazır bir kütüphane değil: PDM eklentisi Administration aracına DÜZ bir DLL listesi
/// olarak yükleniyor ve pakete giren her ek dosya, unutulduğunda eklentinin hiç yüklenmemesi
/// demek. Journal formatı bilinçli olarak düz tutuldu (dizi ve iç içe nesne yok; her kayıt
/// kendi satırında), bu yüzden ihtiyaç duyulan ayrıştırıcı da küçük ve tümüyle test edilebilir.
/// </para>
/// <para>
/// Desteklenen değer türleri: dize, sayı, boolean, null. Bilinçli olarak dizi ve iç içe nesne
/// YOK — journal satırlarının düz kalması, yarım yazılmış bir satırın atılabilmesini de
/// garanti ediyor.
/// </para>
/// </remarks>
internal static class FlatJson
{
    public sealed class Writer
    {
        private readonly StringBuilder _builder = new("{");
        private bool _hasFields;

        public Writer Text(string name, string? value)
        {
            if (value is null)
            {
                return this;
            }

            Separator();
            AppendKey(name);
            AppendString(value);
            return this;
        }

        public Writer Number(string name, long value)
        {
            Separator();
            AppendKey(name);
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public Writer Bool(string name, bool value)
        {
            Separator();
            AppendKey(name);
            _builder.Append(value ? "true" : "false");
            return this;
        }

        public string Build() => _builder.Append('}').ToString();

        private void Separator()
        {
            if (_hasFields)
            {
                _builder.Append(',');
            }

            _hasFields = true;
        }

        private void AppendKey(string name)
        {
            AppendString(name);
            _builder.Append(':');
        }

        private void AppendString(string value)
        {
            _builder.Append('"');

            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        _builder.Append("\\\"");
                        break;
                    case '\\':
                        _builder.Append("\\\\");
                        break;
                    case '\n':
                        _builder.Append("\\n");
                        break;
                    case '\r':
                        _builder.Append("\\r");
                        break;
                    case '\t':
                        _builder.Append("\\t");
                        break;
                    case '\b':
                        _builder.Append("\\b");
                        break;
                    case '\f':
                        _builder.Append("\\f");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            _builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            _builder.Append(c);
                        }

                        break;
                }
            }

            _builder.Append('"');
        }
    }

    /// <summary>
    /// Ayrıştırılmış düz bir JSON nesnesi. Eksik alan istendiğinde varsayılan döner —
    /// journal şeması zamanla büyüyeceği için eski satırların yeni alanları olmayacak.
    /// </summary>
    public sealed class Record
    {
        private readonly Dictionary<string, string?> _fields;

        internal Record(Dictionary<string, string?> fields) => _fields = fields;

        public bool Has(string name) => _fields.ContainsKey(name);

        public string Text(string name, string fallback = "") =>
            _fields.TryGetValue(name, out var value) && value is not null ? value : fallback;

        public long Number(string name, long fallback = 0) =>
            _fields.TryGetValue(name, out var value)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;

        public int Int(string name, int fallback = 0) => (int)Number(name, fallback);

        public bool Bool(string name, bool fallback = false) =>
            _fields.TryGetValue(name, out var value)
                ? string.Equals(value, "true", StringComparison.Ordinal)
                : fallback;

        public Guid Guid(string name) =>
            System.Guid.TryParse(Text(name), out var value) ? value : System.Guid.Empty;

        public DateTime DateUtc(string name) =>
            DateTime.TryParse(Text(name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
                ? value.ToUniversalTime()
                : default;
    }

    /// <summary>
    /// Tek bir satırı ayrıştırır. Bozuk satır <c>null</c> döner — çağıran taraf onu atlar.
    /// </summary>
    /// <remarks>
    /// Çökme sırasında yarım kalan son satır tam olarak bu yoldan elenir. Append-only format
    /// sayesinde önceki satırlar sağlam kalır ve işlem geçmişi kaybolmaz.
    /// </remarks>
    public static Record? TryParse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var text = line!.Trim();
        if (text.Length < 2 || text[0] != '{' || text[text.Length - 1] != '}')
        {
            return null;
        }

        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        var position = 1;
        var end = text.Length - 1;

        while (position < end)
        {
            SkipWhitespace(text, ref position);
            if (position >= end)
            {
                break;
            }

            if (text[position] == ',')
            {
                position++;
                continue;
            }

            if (text[position] != '"' || !TryReadString(text, ref position, out var key))
            {
                return null;
            }

            SkipWhitespace(text, ref position);
            if (position >= end || text[position] != ':')
            {
                return null;
            }

            position++;
            SkipWhitespace(text, ref position);

            if (!TryReadValue(text, ref position, end, out var value))
            {
                return null;
            }

            fields[key] = value;
        }

        return new Record(fields);
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }

    private static bool TryReadValue(string text, ref int position, int end, out string? value)
    {
        value = null;

        if (position >= end)
        {
            return false;
        }

        if (text[position] == '"')
        {
            if (!TryReadString(text, ref position, out var stringValue))
            {
                return false;
            }

            value = stringValue;
            return true;
        }

        var start = position;
        while (position < end && text[position] != ',')
        {
            position++;
        }

        var literal = text.Substring(start, position - start).Trim();

        if (string.Equals(literal, "null", StringComparison.Ordinal))
        {
            value = null;
            return true;
        }

        value = literal;
        return literal.Length > 0;
    }

    private static bool TryReadString(string text, ref int position, out string value)
    {
        value = string.Empty;

        // Açılış tırnağı.
        position++;

        var builder = new StringBuilder();

        while (position < text.Length)
        {
            var c = text[position];

            if (c == '"')
            {
                position++;
                value = builder.ToString();
                return true;
            }

            if (c != '\\')
            {
                builder.Append(c);
                position++;
                continue;
            }

            position++;
            if (position >= text.Length)
            {
                return false;
            }

            switch (text[position])
            {
                case '"': builder.Append('"'); break;
                case '\\': builder.Append('\\'); break;
                case '/': builder.Append('/'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'u':
                    if (position + 4 >= text.Length)
                    {
                        return false;
                    }

                    var hex = text.Substring(position + 1, 4);
                    if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                    {
                        return false;
                    }

                    builder.Append((char)code);
                    position += 4;
                    break;

                default:
                    return false;
            }

            position++;
        }

        return false;
    }
}
