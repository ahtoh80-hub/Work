using System.Collections.Generic;

namespace IO_PJT.Models
{
    /// <summary>F113: итог кнопки «Заполнить M_bit из M_Address» — разбор совмещённого
    /// адреса «регистр+бит» и запись M_Bit/M_Address в IO_LIST.</summary>
    public sealed class ModbusBitFillResult
    {
        /// <summary>Проверено записей (включая выбранные/все строки).</summary>
        public int Examined { get; set; }

        /// <summary>Записей, у которых M_Address/M_Bit изменились.</summary>
        public int Updated { get; set; }

        /// <summary>Записей с уже нормализованными M_Address/M_Bit.</summary>
        public int Unchanged { get; set; }

        /// <summary>Строк без совмещённого адреса (обычный M_Address) — не трогались.</summary>
        public int Skipped { get; set; }

        /// <summary>Строк с некорректным совмещённым адресом (несколько разделителей,
        /// нецифровые части, бит вне диапазона) — не трогались.</summary>
        public int Invalid { get; set; }

        /// <summary>Сколько полей реально изменено (M_Address + M_Bit по всем строкам).</summary>
        public int ChangedFields { get; set; }

        /// <summary>Ошибки разбора (до 20; полный перечень — в Errors).</summary>
        public List<string> Errors { get; } = new();

        /// <summary>Примеры ошибок для журнала (до 10).</summary>
        public List<string> Samples { get; } = new();

        public string Summary =>
            $"F113: проверено {Examined}; обновлено {Updated} (полей: {ChangedFields}); " +
            $"без изменений {Unchanged}; пропущено {Skipped}; ошибок {Invalid}";
    }
}
