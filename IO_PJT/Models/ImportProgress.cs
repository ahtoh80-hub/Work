namespace IO_PJT.Models
{
    /// <summary>
    /// Состояние прогресса импорта в Access (данные + генерация аудита IO_LIST_Audit).
    /// Report вызывается ImportService; обновляет ProgressBar в окне импорта.
    /// </summary>
    public sealed class ImportProgress
    {
        /// <summary>Фаза: «Чтение Excel» / «Очистка таблицы» / «Запись (данные + аудит)» / «Готово».</summary>
        public string Phase { get; init; } = "";

        /// <summary>Обработано строк (0..Total).</summary>
        public int Processed { get; init; }

        /// <summary>Всего строк данных (0 — фаза без известного объёма).</summary>
        public int Total { get; init; }

        /// <summary>Вставлено новых записей.</summary>
        public int Inserted { get; init; }

        /// <summary>Обновлено существующих записей (upsert).</summary>
        public int Updated { get; init; }

        /// <summary>Тег текущей обрабатываемой строки (если есть).</summary>
        public string? CurrentTag { get; init; }
    }
}
