using System.Linq;
using System.Text.RegularExpressions;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// Парсер промышленных тегов (дерево IOclass, InstrumentTag).
    /// Заполняет PTag_* (TEXT(50)) из значения Tag:
    /// TagArea — код площадки (1-6 цифр), DeviceClass — класс прибора (1-5 букв),
    /// LoopNumber — цифровая часть Loop/тега, TagNumber — 1-3 цифры, Suffix — 0-5 символов.
    /// </summary>
    public static class TagParser
    {
        // Пример: 2701-XZY-10101A → Area=2701, Device=XZY, Loop=10101, Number=01, Suffix=A
        private static readonly Regex TagPattern =
            new(@"^(?<area>\d{1,6})-(?<dev>[A-Za-z]{1,5})-((?<loop>\d{1,5})(?<num>\d{1,3}))?(?<suf>[A-Za-z0-9]{0,5})$",
                RegexOptions.Compiled);

        /// <summary>F2.15: удалить все пробельные символы из Tag (включая NBSP \u00A0 и управляющие
        /// пробелы) — нормализация значения перед разбором/записью. null → null.</summary>
        public static string? CleanWhitespace(string? tag)
        {
            if (string.IsNullOrEmpty(tag)) return tag;
            if (!tag.Any(char.IsWhiteSpace)) return tag; // быстрый путь — чистое значение
            var sb = new System.Text.StringBuilder(tag.Length);
            foreach (var ch in tag)
                if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            return sb.ToString();
        }

        /// <summary>Заполнить ParsedTag по значению Tag (всегда программно, маппингом не переопределяется).</summary>
        public static InstrumentTag Parse(string? tag)
        {
            var result = new InstrumentTag { FullTag = tag };
            if (string.IsNullOrWhiteSpace(tag)) return result;

            string t = tag.Trim();
            result.Separator = t.Contains('-') ? "-" : (t.Contains('/') ? "/" : null);

            var m = TagPattern.Match(t);
            if (!m.Success)
            {
                // Запасной вариант: находим первую цифровую группу (TagArea) и всё после неё
                var parts = t.Split(new[] { '-', '/' }, 2, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0].All(char.IsDigit))
                {
                    result.TagArea = parts[0];
                    var rest = SplitRest(parts[1], result);
                    result.DeviceClass = rest;
                }
                return result;
            }

            result.TagArea = m.Groups["area"].Value;
            result.DeviceClass = m.Groups["dev"].Value.ToUpperInvariant();
            result.LoopNumber = m.Groups["loop"].Success ? m.Groups["loop"].Value
                             : (m.Groups["num"].Success ? m.Groups["num"].Value : null);
            result.TagNumber = m.Groups["num"].Success ? m.Groups["num"].Value : null;
            result.Suffix = m.Groups["suf"].Success && m.Groups["suf"].Value.Length > 0
                ? m.Groups["suf"].Value : null;
            return result;
        }

        /// <summary>Разбор остатка тега: отделяем класс прибора (буквы) от цифровой части Loop.</summary>
        private static string? SplitRest(string rest, InstrumentTag target)
        {
            int i = 0;
            while (i < rest.Length && char.IsLetter(rest[i])) i++;
            string dev = rest[..i];
            string num = rest[i..];

            // LoopNumber — цифровая часть (все цифры), TagNumber — последние 1-3 цифры
            var digits = new string(num.Where(char.IsDigit).ToArray());
            if (digits.Length > 0)
            {
                target.LoopNumber = digits.Length > 3 ? digits[..^2] : digits;
                target.TagNumber = digits.Length > 3 ? digits[^2..] : digits;
            }
            int s = 0;
            while (s < num.Length && char.IsDigit(num[s])) s++;
            if (s < num.Length) target.Suffix = num[s..];
            return string.IsNullOrEmpty(dev) ? null : dev.ToUpperInvariant();
        }

        /// <summary>Обновить ParsedTag у точки из её Tag (вызывается при импорте).</summary>
        public static void Fill(IoPoint point)
        {
            point.Identification.ParsedTag = Parse(point.Identification.Tag);
        }
    }
}
