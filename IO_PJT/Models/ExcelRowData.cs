using System;
using System.Collections.Generic;

namespace IO_PJT.Models
{
    /// <summary>
    /// Строка данных Excel: значения ячеек + признаки зачёркивания (F2.12).
    /// Ячейка Tag зачёркнута → строка исключена из проекта (не импортируется);
    /// другая ячейка зачёркнута → значение считается пустым (NULL).
    /// </summary>
    public sealed class ExcelRowData
    {
        /// <summary>Значения ячеек строки (по колонкам листа; null — пусто).</summary>
        public IReadOnlyList<string?> Cells { get; init; } = Array.Empty<string?>();

        /// <summary>Признак зачёркивания ячейки (true — зачёркнута); параллленно Cells.</summary>
        public IReadOnlyList<bool> Struck { get; init; } = Array.Empty<bool>();

        /// <summary>Ячейка зачёркнута (безопасно по индексу).</summary>
        public bool IsStruck(int index) => index >= 0 && index < Struck.Count && Struck[index];

        /// <summary>Значение ячейки (безопасно по индексу).</summary>
        public string? Cell(int index) => index >= 0 && index < Cells.Count ? Cells[index] : null;
    }
}
