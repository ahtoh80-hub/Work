namespace IO_PJT.Models
{
    /// <summary>Результат операции Upsert по тегу (правило идентичности F2.8).</summary>
    public enum UpsertOutcome
    {
        /// <summary>Тега не было — вставка.</summary>
        Inserted,
        /// <summary>Есть изменения — частичное обновление + аудит изменённых полей.</summary>
        Updated,
        /// <summary>Данные не изменились — ни UPDATE, ни записи в аудит.</summary>
        Unchanged,
        /// <summary>Конфликт System/Plant — строка игнорирована, критическая ошибка в аудит.</summary>
        Conflict
    }

    /// <summary>Результат Upsert: исход + данные для отчёта/аудита.</summary>
    public sealed class UpsertResult
    {
        public UpsertOutcome Outcome { get; init; }

        /// <summary>Поле конфликта: System или Plant.</summary>
        public string? ConflictField { get; init; }

        /// <summary>Идентичность в БД (System=…; Plant=…).</summary>
        public string? OldIdentity { get; init; }

        /// <summary>Идентичность в импортируемых данных.</summary>
        public string? NewIdentity { get; init; }

        /// <summary>
        /// true, если входные System/Plant пусты, а в БД заданы —
        /// проверка идентичности пропущена (предупреждение W003).
        /// </summary>
        public bool IdentityNotChecked { get; init; }

        /// <summary>F9.1: документ существующей записи в БД (REV_FileName) — для определения
        /// задвоения тега между документами. null — документ неизвестен (старые записи).</summary>
        public string? ExistingFileName { get; init; }

        /// <summary>F9.1: вкладка существующей записи в БД (REV_Sheet).</summary>
        public string? ExistingSheet { get; init; }
    }
}
