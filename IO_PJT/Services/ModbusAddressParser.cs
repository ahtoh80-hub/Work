using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace IO_PJT.Services
{
    /// <summary>F113: разбор совмещённого Modbus-адреса «регистр+бит».
    /// Пример: «32.1» → регистр «32», бит 1. Разделитель — ровно один спецсимвол
    /// (точка, запятая, подчёркивание, дефис, слэш и т.п.); буквы/цифры как разделитель
    /// не допускаются. Пробелы вокруг разделителя разрешены.</summary>
    public static class ModbusAddressParser
    {
        // [^\p{L}\p{Nd}\s] — не буква, не цифра, не пробел: кириллица тоже исключена.
        private static readonly Regex Combined = new(
            @"^\s*([0-9]+)\s*([^\p{L}\p{Nd}\s])\s*([0-9]+)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex Separator = new(
            @"[^\p{L}\p{Nd}\s]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Есть ли в строке хотя бы один потенциальный разделитель.
        /// Нужен, чтобы обычный адрес «32» не считался ошибкой разбора.</summary>
        public static bool HasSeparator(string? value) =>
            !string.IsNullOrWhiteSpace(value) && Separator.IsMatch(value);

        /// <summary>Разобрать «регистр+бит» с учётом режима смещения бита.</summary>
        public static bool TryParse(string? value, bool bitOffset,
            out string address, out string bit, out string? error)
        {
            address = "";
            bit = "";
            error = null;

            string s = (value ?? "").Trim();
            if (s.Length == 0)
            {
                error = "пустой M_Address";
                return false;
            }

            var m = Combined.Match(s);
            if (!m.Success)
            {
                error = "ожидается «регистр+бит» через один спецсимвол (например 32.1)";
                return false;
            }

            address = m.Groups[1].Value;
            if (!int.TryParse(m.Groups[3].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int n))
            {
                error = "номер бита не является целым числом";
                return false;
            }

            int min = bitOffset ? 1 : 0;
            int max = bitOffset ? 16 : 15;
            if (n < min || n > max)
            {
                error = $"бит {n} вне диапазона {min}..{max}" +
                        (bitOffset ? " (M_Bit_Offset=1)" : "");
                return false;
            }

            bit = n.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>Строковый режим M_Bit_Offset: «1» — смещение включено.</summary>
        public static bool TryParse(string? value, string? bitOffset,
            out string address, out string bit, out string? error) =>
            TryParse(value, string.Equals((bitOffset ?? "").Trim(), "1",
                StringComparison.OrdinalIgnoreCase), out address, out bit, out error);
    }
}
