using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace IO_PJT.Services
{
    /// <summary>п.10: маска сопоставления IO_LIST ↔ MODBUS_MAP — переиспользуемый парсер/оценщик.
    /// Синтаксис: <c>{ИмяКолонки}</c> — значение колонки; <c>&amp;</c> — разделитель «-»
    /// (только между непустыми компонентами); <c>*</c> — wildcard; <c>(-N)</c> — убрать N
    /// символов справа; <c>(+N)</c> — взять N символов слева. Пустая маска = полное совпадение.</summary>
    public sealed class MaskPattern
    {
        private readonly List<object> _tokens = new();
        private static readonly Regex TokenRe = new(
            @"\{([^}]+)\}(\([+-]\d+\))?|&|\*",
            RegexOptions.Compiled);

        public string Source { get; }
        private MaskPattern(string source) => Source = source;

        /// <summary>Разобрать маску; null при пустом источнике (полное совпадение).</summary>
        public static MaskPattern? Parse(string? mask)
        {
            string s = (mask ?? "").Trim();
            if (s.Length == 0) return null;
            var p = new MaskPattern(s);
            int pos = 0;
            while (pos < s.Length)
            {
                var m = TokenRe.Match(s, pos);
                if (!m.Success || m.Index > pos)
                {
                    string lit = m.Success ? s.Substring(pos, m.Index - pos) : s.Substring(pos);
                    if (lit.Length > 0) p._tokens.Add(lit);
                    if (!m.Success) break;
                }
                if (m.Value == "&") p._tokens.Add(SeparatorToken.Instance);
                else if (m.Value == "*") p._tokens.Add(WildcardToken.Instance);
                else
                {
                    string field = m.Groups[1].Value.Trim();
                    int? mod = null;
                    if (m.Groups[2].Success)
                    {
                        string mg = m.Groups[2].Value;
                        mod = int.Parse(mg.Substring(2, mg.Length - 3));
                        if (mg[1] == '-') mod = -mod.Value;
                    }
                    p._tokens.Add(new FieldToken(field, mod));
                }
                pos = m.Index + m.Length;
            }
            return p;
        }

        /// <summary>Оценить маску по строке (колонка → значение) → паттерн для поиска в MODBUS_MAP.</summary>
        public string Evaluate(IReadOnlyDictionary<string, string> row)
        {
            var sb = new StringBuilder();
            bool needSep = false;
            foreach (var tok in _tokens)
            {
                if (tok is SeparatorToken) { needSep = true; }
                else if (tok is WildcardToken) { needSep = false; sb.Append('*'); }
                else if (tok is FieldToken ft)
                {
                    string val = "";
                    foreach (var kv in row)
                        if (string.Equals(kv.Key, ft.FieldName, StringComparison.OrdinalIgnoreCase))
                        { val = kv.Value ?? ""; break; }
                    if (ft.Mod is int md)
                    {
                        if (md < 0) { int cut = Math.Min(-md, val.Length); val = val.Substring(0, val.Length - cut); }
                        else { val = val.Substring(0, Math.Min(md, val.Length)); }
                    }
                    if (val.Length == 0) continue;
                    if (needSep && sb.Length > 0) sb.Append('-');
                    needSep = false;
                    sb.Append(val);
                }
                else if (tok is string lit)
                {
                    if (lit.Length == 0) continue;
                    if (needSep && sb.Length > 0) sb.Append('-');
                    needSep = false;
                    sb.Append(lit);
                }
            }
            return sb.ToString();
        }

        /// <summary>Совпадение значения колонки карты с паттерном (wildcard «*» = любые символы).</summary>
        public static bool IsMatch(string? pattern, string? value)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            if (pattern.IndexOf('*') < 0)
                return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
            return WildcardRegex(pattern).IsMatch(value ?? "");
        }

        // F99: кэш скомпилированных wildcard-паттернов (п.10 перебирает сотни строк карты
        // на каждую точку — повторная компиляция одного и того же паттерна лишняя)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex>
            _rxCache = new(StringComparer.Ordinal);

        private static Regex WildcardRegex(string pattern) =>
            _rxCache.GetOrAdd(pattern, p => new Regex(
                "^" + Regex.Escape(p).Replace(@"\*", ".*") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));

        /// <summary>F99: пустая маска = полное совпадение IOField ↔ MapField (валидна всегда).</summary>
        public const string DefaultTagMask =
            "{PTAG_TagArea}&{PTag_DeviceClass}&{Ptag_LoopNumber}&{Ptag_TagNumber}&{PTag_Suffix}";

        /// <summary>F99: проверить маску (для UI/валидации правил): null — ок (в т.ч. пустая),
        /// иначе текст ошибки — неразборчивый синтаксис либо неизвестная колонка FieldRegistry.</summary>
        public static string? Validate(string? mask)
        {
            string s = (mask ?? "").Trim();
            if (s.Length == 0) return null; // пустая маска — полное совпадение
            MaskPattern? p;
            try { p = Parse(s); }
            catch (Exception ex) { return $"неверная маска «{s}»: {ex.Message}"; }
            if (p == null) return null;
            foreach (var f in p.Fields())
                if (!FieldRegistry.HasColumn(f))
                    return $"неизвестная колонка «{f}» в маске (нет в IO_LIST)";
            return null;
        }

        /// <summary>F99: колонки FieldRegistry, упомянутые в маске (без дублей, в порядке встречи).</summary>
        public string[] Fields()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var tok in _tokens)
                if (tok is FieldToken ft && seen.Add(ft.FieldName)) list.Add(ft.FieldName);
            return list.ToArray();
        }

        /// <summary>F99: оценка совпадения паттерна со значением карты: null — не совпадает;
        /// 0 — полное совпадение (без wildcard либо «*» ничего не поглотил); N — сколько
        /// символов поглотили wildcard (меньше — точнее).</summary>
        public static int? Score(string? pattern, string? value)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            if (pattern.IndexOf('*') < 0)
                return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase)
                    ? 0 : (int?)null;
            // каждый «*» → захватывающая группа; сумма их длин = поглощённые символы
            string rx = "^" + Regex.Escape(pattern).Replace(@"\*", "(.*)") + "$";
            var m = Regex.Match(value ?? "", rx,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success) return null;
            int swallowed = 0;
            for (int g = 1; g < m.Groups.Count; g++) swallowed += m.Groups[g].Value.Length;
            return swallowed;
        }

        /// <summary>F99: среди строк карты, совпавших с паттерном, выбрать совпадение с
        /// минимальным отклонением (наименьший Score); null — совпадений нет.
        /// values — (строка карты, значение MapField); первый минимум выигрывает.</summary>
        public static T? FindBest<T>(string? pattern, IEnumerable<(T Row, string? Value)> values)
            where T : class
        {
            T? best = null;
            int bestScore = int.MaxValue;
            foreach (var (row, value) in values)
            {
                int? s = Score(pattern, value);
                if (s is int sc && sc < bestScore) { bestScore = sc; best = row; }
            }
            return best;
        }

        private sealed class SeparatorToken { public static readonly SeparatorToken Instance = new(); }
        private sealed class WildcardToken { public static readonly WildcardToken Instance = new(); }
        private sealed class FieldToken
        {
            public string FieldName { get; }
            public int? Mod { get; }
            public FieldToken(string name, int? mod) { FieldName = name; Mod = mod; }
        }
    }
}
