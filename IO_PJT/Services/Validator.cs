using System;
using System.Collections.Generic;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// Валидация данных импорта (ТЗ F5):
    /// F5.1 обязательный Tag; F5.2 иерархия уставок LL2≤LL≤L≤H≤HH≤HH2 (после конвертации);
    /// F5.3 диапазоны Range_MIN &lt; Range_MAX; W001 — значение не конвертируется в число.
    /// </summary>
    public sealed class Validator : IValidator
    {
        /// <summary>Валидация одной точки.</summary>
        public ValidationReport Validate(IoPoint p, int? rowNumber = null)
        {
            var report = new ValidationReport();
            string? tag = p.Tag;

            // F5.1 — обязательное поле Tag (в т.ч. минимальная длина)
            if (string.IsNullOrWhiteSpace(tag))
                report.Add(ErrCode.E002_EmptyTag, "Пустое значение обязательного поля Tag", tag, "Tag", rowNumber);
            else if (tag.Trim().Length < 3)
                report.Add(ErrCode.E002_EmptyTag, $"Слишком короткий Tag: «{tag}»", tag, "Tag", rowNumber);

            // F5.2 — иерархия уставок ПОСЛЕ конвертации (F2.3)
            var alarms = p.Ranges?.Alarms;
            if (alarms != null)
            {
                var ordered = new (string Name, string? Value)[]
                {
                    ("LL2", alarms.LL2), ("LL", alarms.LL), ("L", alarms.L),
                    ("H", alarms.H), ("HH", alarms.HH), ("HH2", alarms.HH2)
                };

                var numbers = new List<(string Name, decimal Value)>();
                foreach (var (name, value) in ordered)
                {
                    if (string.IsNullOrWhiteSpace(value) || value.Trim() == "-") continue; // «-» = нет значения
                    if (ValueConverter.TryParseDecimal(value, out var d))
                        numbers.Add((name, d));
                    else
                        report.Add(ErrCode.W001_NotNumeric,
                            $"Уставка {name} = «{value}» не является числом — значение сохранено как текст",
                            tag, name, rowNumber);
                }

                for (int i = 1; i < numbers.Count; i++)
                {
                    if (numbers[i].Value < numbers[i - 1].Value)
                        report.Add(ErrCode.E003_AlarmHierarchy,
                            $"Нарушена иерархия уставок: {numbers[i - 1].Name}={numbers[i - 1].Value} > {numbers[i].Name}={numbers[i].Value} (ожидается LL2≤LL≤L≤H≤HH≤HH2) — строка будет записана",
                            tag, numbers[i].Name, rowNumber);
                }
            }

            // F5.3 — диапазоны шкал (после конвертации)
            CheckRange(report, tag, "Range_MIN", p.Ranges?.Primary.Min,
                                 "Range_MAX", p.Ranges?.Primary.Max, rowNumber);
            CheckRange(report, tag, "Range_MIN2", p.Ranges?.Secondary?.Min,
                                 "Range_MAX2", p.Ranges?.Secondary?.Max, rowNumber);

            return report;
        }

        /// <summary>Проверка пары Min &lt; Max одного диапазона.</summary>
        private static void CheckRange(ValidationReport report, string? tag,
            string minName, string? minValue, string maxName, string? maxValue, int? row)
        {
            decimal min = 0, max = 0;
            bool minOk = !string.IsNullOrWhiteSpace(minValue) && minValue.Trim() != "-" &&
                         ValueConverter.TryParseDecimal(minValue, out min);
            bool maxOk = !string.IsNullOrWhiteSpace(maxValue) && maxValue.Trim() != "-" &&
                         ValueConverter.TryParseDecimal(maxValue, out max);

            if (!string.IsNullOrWhiteSpace(minValue) && minValue.Trim() != "-" && !minOk)
                report.Add(ErrCode.W001_NotNumeric, $"{minName} = «{minValue}» не является числом — сохранено как текст", tag, minName, row);
            if (!string.IsNullOrWhiteSpace(maxValue) && maxValue.Trim() != "-" && !maxOk)
                report.Add(ErrCode.W001_NotNumeric, $"{maxName} = «{maxValue}» не является числом — сохранено как текст", tag, maxName, row);

            if (minOk && maxOk && min >= max)
                report.Add(ErrCode.E004_RangeOrder,
                    $"Нарушён диапазон: {minName}={min} ≥ {maxName}={max}", tag, maxName, row);
        }

        /// <summary>Валидация пачки (импорт файла целиком).</summary>
        public ValidationReport ValidateBatch(IEnumerable<IoPoint> items)
        {
            var total = new ValidationReport();
            foreach (var p in items)
            {
                var r = Validate(p, p.Revision?.RowNumber);
                total.Items.AddRange(r.Items);
            }
            return total;
        }
    }
}
