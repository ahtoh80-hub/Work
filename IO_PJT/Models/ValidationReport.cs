using System;
using System.Collections.Generic;

namespace IO_PJT.Models
{
    /// <summary>Система кодов ошибок валидации (Задание п.3).</summary>
    public enum ErrCode
    {
        /// <summary>Нет привязки обязательного поля Tag.</summary>
        E001_MissingTagMapping = 1,
        /// <summary>Пустое значение Tag в строке.</summary>
        E002_EmptyTag = 2,
        /// <summary>Нарушение иерархии уставок LL2≤LL≤L≤H≤HH≤HH2 — НЕ блокирует импорт: строка записывается, ошибка попадает в отчёт.</summary>
        E003_AlarmHierarchy = 3,
        /// <summary>Нарушение диапазона Range_MIN &lt; Range_MAX.</summary>
        E004_RangeOrder = 4,
        /// <summary>Значение шкалы/уставки не конвертируется в число (предупреждение).</summary>
        W001_NotNumeric = 101,
        /// <summary>Неизвестная колонка Excel / отсутствует в таблице (предупреждение).</summary>
        W002_UnmappedColumn = 102,
        /// <summary>Повтор тега внутри одного Excel-файла.</summary>
        E005_DuplicateTagInFile = 5,
        /// <summary>Ошибка чтения файла Excel.</summary>
        E006_ExcelReadError = 6,
        /// <summary>Ошибка доступа к базе Access.</summary>
        E007_AccessError = 7,
        /// <summary>Колонка таблицы привязана к двум колонкам Excel (нарушение 1:1).</summary>
        E008_DuplicateBinding = 8,
        /// <summary>Конфликт идентичности: Tag существует с другими System/Plant — строка игнорируется.</summary>
        E009_TagConflict = 9,
        /// <summary>System/Plant не задан в данных — проверка идентичности пропущена (предупреждение).</summary>
        W003_IdentityNotChecked = 103,
        /// <summary>F2.11: тег заменяемого документа отсутствует в новой версии файла — помечен к удалению
        /// (REV_Description); физическое удаление не выполняется (предупреждение).</summary>
        W004_MarkedForDeletion = 104,
        /// <summary>F2.12: строка зачёркнута в Excel — пропущена при импорте
        /// (ячейка Tag зачёркнута → вся строка не импортируется; предупреждение).</summary>
        W005_StruckThroughRow = 105,
        /// <summary>F2.15: из Tag удалены пробельные символы (включая NBSP) — значение нормализовано
        /// перед записью; в аудит пишется действие TagCleaned (предупреждение).</summary>
        W006_TagCleaned = 106
    }

    /// <summary>Одна ошибка/предупреждение валидации с указанием Tag и поля.</summary>
    public class ValidationError
    {
        /// <summary>Код ошибки.</summary>
        public ErrCode Code { get; set; }

        /// <summary>Тип: ошибка или предупреждение.</summary>
        public bool IsWarning => (int)Code >= 100;

        /// <summary>Тег записи (если известен).</summary>
        public string? Tag { get; set; }

        /// <summary>Имя поля/колонки.</summary>
        public string? Field { get; set; }

        /// <summary>Номер строки Excel (если известен).</summary>
        public int? RowNumber { get; set; }

        /// <summary>Сообщение для пользователя.</summary>
        public string Message { get; set; } = string.Empty;

        public override string ToString() =>
            $"{Code}: {Message}" + (Tag != null ? $" [Tag={Tag}]" : "") +
            (RowNumber != null ? $" (строка {RowNumber})" : "");
    }

    /// <summary>Результат валидации — список ошибок и предупреждений (F5).</summary>
    public class ValidationReport
    {
        public List<ValidationError> Items { get; } = new();

        /// <summary>Ошибки (без предупреждений).</summary>
        public IEnumerable<ValidationError> Errors
        {
            get { foreach (var i in Items) if (!i.IsWarning) yield return i; }
        }

        /// <summary>Предупреждения.</summary>
        public IEnumerable<ValidationError> Warnings
        {
            get { foreach (var i in Items) if (i.IsWarning) yield return i; }
        }

        public bool HasErrors => Items.Exists(i => !i.IsWarning);

        public void Add(ErrCode code, string message, string? tag = null, string? field = null, int? row = null) =>
            Items.Add(new ValidationError { Code = code, Message = message, Tag = tag, Field = field, RowNumber = row });
    }

    /// <summary>Отчёт по импорту (ТЗ F2.6).</summary>
    public class ImportReport
    {
        /// <summary>Считано строк данных (без разделителей и заголовка).</summary>
        public int RowsRead { get; set; }

        /// <summary>Вставлено новых записей.</summary>
        public int Inserted { get; set; }

        /// <summary>Обновлено существующих записей (upsert).</summary>
        public int Updated { get; set; }

        /// <summary>Пропущено строк-разделителей (полностью из «-»).</summary>
        public int SeparatorsSkipped { get; set; }

        /// <summary>Строк с ошибками (не импортированы).</summary>
        public int Errors { get; set; }

        /// <summary>Результаты валидации.</summary>
        public ValidationReport Validation { get; } = new();

        /// <summary>Строк без изменений (не было ни UPDATE, ни записи в аудит — F2.8).</summary>
        public int Unchanged { get; set; }

        /// <summary>Конфликтов идентичности System/Plant — строки проигнорированы (E009).</summary>
        public int Conflicts { get; set; }

        /// <summary>F2.11: помечено к удалению тегов заменяемого документа (отсутствуют в новой версии файла;
        /// физически не удаляются — требуется ручное удаление).</summary>
        public int MarkedForDeletion { get; set; }

        /// <summary>F2.12: пропущено строк с зачёркнутой ячейкой Tag (вся строка не импортируется).</summary>
        public int StruckThroughSkipped { get; set; }

        /// <summary>Итоговый текст отчёта.</summary>
        public string Summary =>
            $"Считано: {RowsRead}; добавлено: {Inserted}; обновлено: {Updated}; " +
            $"без изменений: {Unchanged}; конфликтов: {Conflicts}; " +
            (MarkedForDeletion > 0 ? $"помечено к удалению: {MarkedForDeletion}; " : "") +
            (StruckThroughSkipped > 0 ? $"зачёркнутых пропущено: {StruckThroughSkipped}; " : "") +
            $"разделителей пропущено: {SeparatorsSkipped}; ошибок: {Errors}";
    }
}
