using System;
using System.Globalization;

namespace IO_PJT.Services
{
    /// <summary>
    /// Конвертация значений шкал и уставок в число (ТЗ F2.3).
    /// Если в Excel значение не в формате Decimal — пробуем привести:
    /// удаляем пробелы, заменяем запятую на точку, отбрасываем нечисловые примеси.
    /// Не конвертируется — значение остаётся текстом, возвращается false (предупреждение W001).
    /// </summary>
    public static class ValueConverter
    {
        /// <summary>Попытка преобразовать строку в decimal (правила F2.3).</summary>
        public static bool TryParseDecimal(string? text, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(text)) return false;

            // 1. Удаляем пробелы (в т.ч. неразрывные) и типовые примеси единиц после числа
            string s = text.Trim()
                .Replace("\u00A0", "")
                .Replace(" ", "");

            // 2. Запятая → точка (десятичный разделитель)
            s = s.Replace(',', '.');

            // 3. Прямой разбор
            if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;

            // 4. Извлекаем первое число из строки вида «20±1», «<50», «100%», «42.5 mm»
            int start = -1;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsDigit(s[i]) || (s[i] == '.' && start >= 0) || (s[i] == '-' && i == 0))
                {
                    if (start < 0) start = i;
                }
                else if (start >= 0)
                {
                    break;
                }
            }
            if (start < 0) return false;

            string candidate = s[start..];
            int end = 0;
            while (end < candidate.Length && (char.IsDigit(candidate[end]) || candidate[end] == '.'))
                end++;
            candidate = candidate[..end];

            return decimal.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Проверка «значение в формате Decimal» без преобразования (для подсветки в предпросмотре).</summary>
        public static bool IsDecimal(string? text) => TryParseDecimal(text, out _);

        /// <summary>Нормализованное числовое представление (для сравнений) или исходный текст.</summary>
        public static string Normalize(string? text) =>
            TryParseDecimal(text, out var v) ? v.ToString(CultureInfo.InvariantCulture) : text ?? string.Empty;
    }
}
