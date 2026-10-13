using System;
using System.Collections.Generic;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>Уровень события журнала (Задание п.8: белый — норма, жёлтый — предупреждение, красный — ошибка).</summary>
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>Запись журнала событий.</summary>
    public class LogEntry
    {
        public DateTime Time { get; init; } = DateTime.Now;
        public LogLevel Level { get; init; }
        public string Message { get; init; } = string.Empty;

        public override string ToString() => $"[{Time:HH:mm:ss}] {Message}";
    }

    /// <summary>Журнал событий приложения.</summary>
    public interface IEventLogger
    {
        /// <summary>Все записи журнала.</summary>
        IReadOnlyList<LogEntry> Entries { get; }

        /// <summary>Событие добавления записи (для подписки UI).</summary>
        event Action<LogEntry>? EntryAdded;

        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Clear();
    }

    /// <summary>Чтение Excel (ClosedXML): листы, строки, заголовки.</summary>
    public interface IExcelReader : IDisposable
    {
        /// <summary>Список вкладок (листов) файла.</summary>
        IReadOnlyList<string> GetSheetNames();

        /// <summary>Заголовки выбранного листа (строка HeaderRow, первая строка — названия колонок).</summary>
        IReadOnlyList<string> GetHeaders(string sheetName, int headerRow);

        /// <summary>Строки данных листа начиная со следующей после заголовка (F2.12: со значениями + признаками зачёркивания).</summary>
        IReadOnlyList<ExcelRowData> ReadRows(string sheetName, int headerRow);

        /// <summary>Реальный номер строки Excel для индекса строки данных (0-based) с учётом HeaderRow.</summary>
        int GetExcelRowNumber(string sheetName, int headerRow, int dataIndex);
    }

    /// <summary>Создание/инспекция таблиц Access (ТЗ F1).</summary>
    public interface ITableCreator
    {
        bool DatabaseExists();
        void CreateDatabase();
        IReadOnlyList<string> GetTableNames();
        bool TableExists(string tableName);
        void CreateDataTable(string tableName);
        IReadOnlyList<string> GetColumns(string tableName);
    }

    /// <summary>Репозиторий таблицы IO_LIST (OleDb/ACE): CRUD и поиск (ТЗ F3/F4).</summary>
    public interface IIOListRepository
    {
        string TableName { get; set; }

        /// <summary>Прочитать все записи.</summary>
        List<IoPoint> GetAll();

        /// <summary>Точная выборка по тегу.</summary>
        IoPoint? GetByTag(string tag);

        /// <summary>Подстрочный поиск (F3.3).</summary>
        List<IoPoint> Search(string term);

        /// <summary>Фильтр по системе DCS/SIS/GDS (F3.4).</summary>
        List<IoPoint> GetBySystem(string system);

        /// <summary>Количество записей.</summary>
        int Count();

        /// <summary>Вставка новой записи (возвращает ID). Аудит: INSERT.</summary>
        long Insert(IoPoint p);

        /// <summary>Обновление по ID. Аудит: UPDATE «старое → новое».</summary>
        void Update(IoPoint p);

        /// <summary>Удаление по ID. Аудит: DELETE.</summary>
        void Delete(long id);

        /// <summary>Upsert по Tag (F2.7/F2.8): вставка / частичное обновление / без изменений / конфликт System+Plant.
        /// mappedColumns — колонки, замаппленные из Excel (F2.10): сравнение идёт только по ним,
        /// чтобы немаппленные колонки (null) не затирали поля БД. null — сравнивать все (ручные операции).</summary>
        UpsertResult Upsert(IoPoint p, IReadOnlySet<string>? mappedColumns = null);

        /// <summary>Писать ли изменения в аудит (галочка «Импорт с аудитом» F2.7; ручные операции — всегда true).</summary>
        bool WriteAudit { get; set; }

        /// <summary>Очистка таблицы (режим «Заменить»).</summary>
        void Clear();

        /// <summary>F2.11/F2.16: список документов таблицы — пары (файл, вкладка), DISTINCT непустых
        /// REV_FileName (+ REV_Sheet) — для диалога «взамен какого». Документ = файл + вкладка.</summary>
        List<(string FileName, string? Sheet)> GetDocuments();

        /// <summary>F2.11/F2.16: замена документа = пара (файл + вкладка). Теги документа
        /// (replacedFileName + replacedSheet), отсутствующие в tagsInFile (новая вкладка файла),
        /// помечаются к удалению: REV_Description = «Удаляются, отсутствуют в последней версии файла …»
        /// + аудит MarkedDelete; физически строки НЕ удаляются. Выжившие теги документа (есть в новом файле)
        /// переносятся на новый документ (REV_FileName = newFileName, REV_Sheet = newSheet) + аудит Update.
        /// replacedSheet/newSheet: null/пусто — документ без вкладки (старые записи, REV_Sheet IS NULL).
        /// Возвращает список помеченных к удалению тегов. Работает внутри сессии импорта (F2.9).</summary>
        List<string> MarkMissingTagsForDeletion(IReadOnlySet<string> tagsInFile,
            string replacedFileName, string? replacedSheet, string newFileName, string? newSheet);

        /// <summary>F2.15: запись аудита TagCleaned (из значения Tag удалены пробельные символы) —
        /// действие отображается красным в окне истории. Контекст: документ (файл + вкладка) и система.</summary>
        void RecordTagCleanedAudit(string? oldTag, string? newTag, string? fileName, string? sheet, string? system);

        /// <summary>Начать сессию массовой записи: одно соединение + одна транзакция + батч аудита (F2.9). Для импорта.</summary>
        void BeginSession();

        /// <summary>Завершить сессию: COMMIT данных и запись батча аудита. Идемпотентно.</summary>
        void EndSession();

        /// <summary>Отменить сессию: ROLLBACK данных и отброс батча аудита. Идемпотентно.</summary>
        void AbortSession();
    }

    /// <summary>История изменений (ТЗ F7).</summary>
    public interface IAuditService
    {
        /// <summary>Записать одно изменение. fileName/sheet (REV_FileName/REV_Sheet) и system (System) —
        /// F7.6/F7.7: контекст изменения (из какого документа — файл+вкладка, по какой системе);
        /// null — контекст неизвестен.</summary>
        void Record(string tableName, string? tag, AuditAction action, string field, string? oldValue, string? newValue,
                    string? fileName = null, string? system = null, string? sheet = null);

        /// <summary>Выборка истории с фильтрами: Tag (подстрока), поле, документ (REV_FileName, подстрока),
        /// система (System, подстрока), период (F7.6); sheet — фильтр по вкладке-источнику (REV_Sheet, F7.7).</summary>
        List<AuditRecord> GetHistory(string? tag = null, string? field = null,
                                     string? fileName = null, string? system = null,
                                     DateTime? from = null, DateTime? to = null,
                                     string? sheet = null);

        /// <summary>Агрегат истории для отчёта «Формирование отчёта» (F7.8): количество изменений по комбинации
        /// «документ (REV_FileName + REV_Sheet) → поле (FieldName) → действие (ChangeAction)» — SQL GROUP BY
        /// на стороне Access, без ограничения на количество записей. Фильтры — те же, что у GetHistory.</summary>
        List<AuditSummary> GetSummary(string? tag = null, string? field = null,
                                      string? fileName = null, string? system = null,
                                      DateTime? from = null, DateTime? to = null,
                                      string? sheet = null);

        /// <summary>Начать пакет: Record() буферизуется в памяти без обращений к БД (F2.9).</summary>
        void BeginBatch();

        /// <summary>Записать буфер одним соединением и одной транзакцией. Идемпотентно.</summary>
        void EndBatch();

        /// <summary>Отменить пакет: очистить буфер (данные ещё не писались).</summary>
        void DiscardBatch();
    }

    /// <summary>Валидация данных импорта (ТЗ F5).</summary>
    public interface IValidator
    {
        ValidationReport Validate(IoPoint p, int? rowNumber = null);
        ValidationReport ValidateBatch(IEnumerable<IoPoint> items);
    }

    /// <summary>Выгрузка данных/истории в Excel (ТЗ F7.3).</summary>
    public interface IExcelExportService
    {
        void ExportAudit(IEnumerable<AuditRecord> rows, string filePath);

        /// <summary>F7.8: экспорт отчёта «Формирование отчёта» (сводка по документам и ячейкам) с условием отбора.</summary>
        void ExportAuditReport(IEnumerable<AuditReportRow> rows, string filterText, string filePath);

        void ExportPoints(IEnumerable<IoPoint> rows, string filePath);
    }

    /// <summary>Резервное копирование таблицы с датой в имени (Задание п.5).</summary>
    public interface IBackupService
    {
        /// <summary>Копирует данные таблицы в новую таблицу с суффиксом даты. Возвращает имя таблицы-копии.</summary>
        string BackupTable(string tableName);
    }
}
