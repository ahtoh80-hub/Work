using System;

namespace IO_PJT.Models
{
    /// <summary>Действие над записью в истории изменений (F7 ТЗ).</summary>
    public enum AuditAction
    {
        /// <summary>Добавление записи.</summary>
        Insert,
        /// <summary>Изменение поля существующей записи.</summary>
        Update,
        /// <summary>Удаление записи.</summary>
        Delete,
        /// <summary>Критическая ошибка (конфликт идентичности System/Plant — строка игнорирована).</summary>
        CriticalError,
        /// <summary>F2.11: тег помечен к удалению при замене документа (отсутствует в новой версии файла;
        /// физическое удаление не выполняется — удаляется вручную). Отображается красным.</summary>
        MarkedDelete,
        /// <summary>F2.15: из значения Tag удалены пробельные символы (нормализация при импорте/сохранении
        /// карточки). Отображается красным.</summary>
        TagCleaned,
        /// <summary>F9.1: задвоение тега между документами — тег уже существует в БД из другого
        /// документа (файл + вкладка), а импортируется из текущего. Равноправная запись аудита
        /// (как Insert/Update): OldValue/NewValue — прежний/текущий документ. Отображается жёлтым.</summary>
        Duplicate,
        /// <summary>F9.8: предупреждение связывания с картой Modbus — «Не связано: …» либо тег из
        /// DcsTag не найден в IO_LIST. NewValue — текст сообщения (как в журнале событий);
        /// System = «Modbus», FieldName = «Modbus». Отображается жёлтым.</summary>
        ModbusWarning,
        /// <summary>F9.8: ошибка связывания/обновления адресации Modbus — связь отклонена
        /// (не Modbus-сигнал), DCSTAG не Modbus сигнала. NewValue — текст сообщения
        /// (как в журнале событий). Отображается красным.</summary>
        ModbusError
    }

    /// <summary>
    /// Строка истории изменений таблицы (таблица IO_LIST_Audit, ТЗ F7).
    /// Хранит «что на что менялось»: старое → новое значение поля.
    /// </summary>
    public class AuditRecord
    {
        /// <summary>Автономер записи истории.</summary>
        public long Id { get; set; }

        /// <summary>Имя затронутой таблицы (например, IO_LIST).</summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>Тег записи.</summary>
        public string? Tag { get; set; }

        /// <summary>Действие: INSERT / UPDATE / DELETE.</summary>
        public AuditAction Action { get; set; }

        /// <summary>Имя изменившегося поля (колонки).</summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>Старое значение (NULL при INSERT).</summary>
        public string? OldValue { get; set; }

        /// <summary>Новое значение (NULL при DELETE).</summary>
        public string? NewValue { get; set; }

        /// <summary>Автор изменения — Windows-логин (домен\логин).</summary>
        public string ChangedBy { get; set; } = string.Empty;

        /// <summary>F7.6: имя Excel-документа (REV_FileName), из которого произошло изменение (NULL для старых записей/ручных операций без файла).</summary>
        public string? FileName { get; set; }

        /// <summary>F7.7: имя вкладки (листа) документа-источника (REV_Sheet); документ = файл + вкладка (F2.16).
        /// NULL для старых записей/ручных операций/вкладки, неизвестной на момент изменения.</summary>
        public string? Sheet { get; set; }

        /// <summary>F7.6: система (System — произвольное значение: DCS, РСУ, ПСБ и т.п.), к которой относится изменение.</summary>
        public string? System { get; set; }

        /// <summary>Момент изменения (системные часы).</summary>
        public DateTime ChangedAt { get; set; }

        /// <summary>Человекочитаемое описание «что → что».</summary>
        public string ChangeDescription => Action switch
        {
            AuditAction.Insert => $"добавлено: {FieldName} = {NewValue ?? "NULL"}",
            AuditAction.Delete => $"удалено: {FieldName} = {OldValue ?? "NULL"}",
            AuditAction.MarkedDelete => $"помечен к удалению: {NewValue ?? "NULL"} (необходимо удалить из БД)",
            AuditAction.TagCleaned => $"очищен от пробелов: «{OldValue ?? "NULL"}» → «{NewValue ?? "NULL"}» (F2.15)",
            AuditAction.Duplicate => $"задвоение тега: документ «{OldValue ?? "NULL"}» → «{NewValue ?? "NULL"}» (F9.1)",
            AuditAction.ModbusWarning => $"предупреждение связывания Modbus: {NewValue ?? "NULL"} (F9.8)",
            AuditAction.ModbusError => $"ошибка связывания Modbus: {NewValue ?? "NULL"} (F9.8)",
            _ => $"{OldValue ?? "NULL"} → {NewValue ?? "NULL"}"
        };
    }

    /// <summary>F7.8: строка SQL-агрегации истории для отчёта (GROUP BY документ → поле → действие).</summary>
    public class AuditSummary
    {
        /// <summary>Документ-источник (REV_FileName); NULL — контекст неизвестен.</summary>
        public string? FileName { get; set; }

        /// <summary>Вкладка-источник (REV_Sheet); NULL — старые записи/без вкладки.</summary>
        public string? Sheet { get; set; }

        /// <summary>Ячейка (поле, FieldName).</summary>
        public string Field { get; set; } = string.Empty;

        /// <summary>Тип изменения (ChangeAction).</summary>
        public AuditAction Action { get; set; }

        /// <summary>Количество записей аудита с такой комбинацией.</summary>
        public int Count { get; set; }
    }

    /// <summary>F7.8: строка отчёта «Формирование отчёта» — по документу (файл + вкладка) и ячейке (полю)
    /// количество изменений по типам + итоги (строки-итоги документа и общий итог).</summary>
    public class AuditReportRow
    {
        /// <summary>Документ-источник (REV_FileName).</summary>
        public string? FileName { get; set; }

        /// <summary>Вкладка-источник (REV_Sheet).</summary>
        public string? Sheet { get; set; }

        /// <summary>Документ для отображения: «файл :: вкладка»; NULL — «(без файла)», «(без вкладки)».</summary>
        public string Document { get; set; } = string.Empty;

        /// <summary>Ячейка (поле); для строк-итогов — «ИТОГО»/«ВСЕГО».</summary>
        public string Field { get; set; } = string.Empty;

        public int InsertCount { get; set; }
        public int UpdateCount { get; set; }
        public int DeleteCount { get; set; }
        public int CriticalErrorCount { get; set; }
        public int MarkedDeleteCount { get; set; }
        public int TagCleanedCount { get; set; }
        /// <summary>F9.1: задвоения тега между документами (Duplicate).</summary>
        public int DuplicateCount { get; set; }
        /// <summary>F9.8: предупреждения связывания с картой Modbus (ModbusWarning).</summary>
        public int ModbusWarningCount { get; set; }
        /// <summary>F9.8: ошибки связывания/обновления адресации Modbus (ModbusError).</summary>
        public int ModbusErrorCount { get; set; }

        /// <summary>Суммарное количество изменений по строке.</summary>
        public int Total { get; set; }

        /// <summary>Строка-итог по документу.</summary>
        public bool IsSubtotal { get; set; }

        /// <summary>Общая строка-итог по всем документам.</summary>
        public bool IsTotal { get; set; }
    }
}
