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
            string rx = "^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$";
            return Regex.IsMatch(value ?? "", rx, RegexOptions.IgnoreCase);
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
