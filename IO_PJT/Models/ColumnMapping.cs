using System;
using System.Collections.Generic;

namespace IO_PJT.Models
{
    /// <summary>Одна связка «колонка Excel ↔ колонка таблицы Access».</summary>
    public class ColumnMap
    {
        /// <summary>Имя колонки (заголовка) в Excel. Пусто — колонка не привязана.</summary>
        public string ExcelColumn { get; set; } = string.Empty;

        /// <summary>F2.14: 0-based индекс колонки Excel (для различия дубликатов заголовков MIN/MIN);
        /// null — старый шаблон без индекса (резолв по имени).</summary>
        public int? ExcelColumnIndex { get; set; }

        /// <summary>Имя колонки в таблице Access (колонки дерева IOclass).</summary>
        public string AccessColumn { get; set; } = string.Empty;

        /// <summary>Примечание для пользователя (тип, обязательность).</summary>
        public string Note { get; set; } = string.Empty;

        public override string ToString() =>
            string.IsNullOrEmpty(ExcelColumn) ? $"{AccessColumn} ← (не задано)" : $"{AccessColumn} ← {ExcelColumn}";
    }

    /// <summary>
    /// Шаблон маппинга. Хранится в таблице MappingTemplates самой Access-базы
    /// (шаблоны едут вместе с БД — решение пользователя).
    /// </summary>
    public class MappingTemplate
    {
        /// <summary>Идентификатор шаблона.</summary>
        public long Id { get; set; }

        /// <summary>Имя шаблона (например, «OSBL_IO_List»).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Целевая таблица Access.</summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>Подсказка по листу Excel (имя или маска).</summary>
        public string SheetHint { get; set; } = string.Empty;

        /// <summary>Строка заголовка в Excel (по умолчанию 1).</summary>
        public int HeaderRow { get; set; } = 1;

        /// <summary>Связки колонок.</summary>
        public List<ColumnMap> Mappings { get; set; } = new();

        /// <summary>Автор шаблона.</summary>
        public string Author { get; set; } = string.Empty;

        /// <summary>Дата создания.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Дата изменения.</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>Есть ли привязка обязательного поля Tag (валидация F2.2).</summary>
        public bool HasTagMapping =>
            Mappings.Exists(m => string.Equals(m.AccessColumn, "Tag", StringComparison.OrdinalIgnoreCase)
                              && !string.IsNullOrWhiteSpace(m.ExcelColumn));
    }

    /// <summary>Режим импорта (ТЗ F2.7).</summary>
    public enum ImportMode
    {
        /// <summary>Добавить: upsert по Tag (тег есть — обновить, нет — вставить).</summary>
        Upsert,
        /// <summary>Заменить: очистить таблицу перед импортом (с предупреждением).</summary>
        Replace
    }

    /// <summary>Параметры импорта, выбираемые пользователем в едином окне.</summary>
    public class ImportOptions
    {
        /// <summary>Путь к файлу Excel.</summary>
        public string ExcelPath { get; set; } = string.Empty;

        /// <summary>Выбранная вкладка (лист) Excel.</summary>
        public string SheetName { get; set; } = string.Empty;

        /// <summary>Строка начала чтения: первая строка с названиями колонок (по умолчанию 1).</summary>
        public int HeaderRow { get; set; } = 1;

        /// <summary>Целевая таблица Access (по умолчанию IO_LIST).</summary>
        public string TableName { get; set; } = "IO_LIST";

        /// <summary>Режим импорта.</summary>
        public ImportMode Mode { get; set; } = ImportMode.Upsert;

        /// <summary>F2.11/F2.17: документы-заменители, взамен которых импортируется файл (мультивыбор —
        /// документы делятся по ревизиям/вкладкам). Пустой список — импорт нового документа.
        /// Теги этих документов, отсутствующие в новом файле, помечаются к удалению (без физического DELETE).</summary>
        public List<(string FileName, string? Sheet)> ReplacedDocuments { get; set; } = new();

        /// <summary>Писать историю в IO_LIST_Audit при импорте (галочка F2.7; по умолчанию включено).</summary>
        public bool WithAudit { get; set; } = true;
    }

    /// <summary>F2.14: хелперы позиционного резолва колонок Excel (дубликаты заголовков MIN/MIN).</summary>
    public static class ExcelColumns
    {
        /// <summary>Буква колонки Excel по 0-based индексу: 0 → A, 25 → Z, 26 → AA.</summary>
        public static string Letter(int zeroBasedIndex)
        {
            if (zeroBasedIndex < 0) return "?";
            string s = "";
            int i = zeroBasedIndex;
            do
            {
                s = (char)('A' + i % 26) + s;
                i = i / 26 - 1;
            } while (i >= 0);
            return s;
        }

        /// <summary>Резолв индекса колонки для привязки: приоритет — ExcelColumnIndex (F2.14),
        /// если в этой позиции действительно лежит ожидаемое имя; иначе — поиск по имени (шаблоны без индекса).
        /// null — колонка в листе отсутствует.</summary>
        public static int? Resolve(ColumnMap m, IReadOnlyList<string> headers,
            IReadOnlyDictionary<string, int> byName)
        {
            if (m.ExcelColumnIndex is int idx && idx >= 0 && idx < headers.Count &&
                string.Equals(headers[idx]?.Trim(), m.ExcelColumn.Trim(), StringComparison.OrdinalIgnoreCase))
                return idx;
            return byName.TryGetValue(m.ExcelColumn, out int byN) ? byN : null;
        }
    }
}
