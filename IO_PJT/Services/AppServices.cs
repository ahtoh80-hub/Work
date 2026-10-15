using IO_PJT.Data;

namespace IO_PJT.Services
{
    /// <summary>
    /// Агрегат служб приложения (создаётся один раз на окно).
    /// Позволяет открыть другой файл базы и переключать целевую таблицу.
    /// </summary>
    public sealed class AppServices
    {
        public string DbPath { get; private set; }
        public EventLogger Log { get; } = new();
        public TableCreator Creator { get; private set; }
        public AuditService Audit { get; private set; }
        public Validator Validator { get; } = new();
        public MappingService Mapping { get; private set; }
        public ExcelExportService Exporter { get; } = new();
        public BackupService Backup { get; private set; }
        public IIOListRepository Repo { get; private set; }
        public ImportService Import { get; private set; }

        /// <summary>F9: разбор карты адресации Modbus + хранилище MODBUS_MAP.</summary>
        public ModbusMapService ModbusMaps { get; private set; }

        /// <summary>F9: связывание IO_LIST с картой Modbus (M_-поля).</summary>
        public ModbusLinkService ModbusLink { get; private set; }

        /// <summary>F9.11: ручные правила связывания IO_LIST ↔ MODBUS_MAP (MODBUS_LINK_RULES).</summary>
        public ModbusLinkRuleService ModbusLinkRules { get; private set; }

        /// <summary>Текущая целевая таблица (по умолчанию IO_LIST).</summary>
        public string CurrentTable { get; private set; }

        public AppServices(string dbPath)
        {
            DbPath = dbPath;
            Creator = new TableCreator(dbPath);
            Audit = new AuditService(dbPath, Log);
            Mapping = new MappingService(dbPath);
            Backup = new BackupService(dbPath);
            CurrentTable = Schema.DefaultTable;
            Repo = new IOListRepository(dbPath, CurrentTable, Audit, Log);
            Import = new ImportService(Repo, Validator, Log);
            ModbusMaps = new ModbusMapService(dbPath);
            ModbusLinkRules = new ModbusLinkRuleService(dbPath);
            ModbusLink = new ModbusLinkService(Repo, ModbusMaps, Log, Audit, ModbusLinkRules);
            Log.Info($"База данных: {dbPath}");
            // Гарантия служебных таблиц (IO_LIST_Audit, MappingTemplates) при открытии базы
            ServiceSchema.EnsureServiceTables(dbPath);
        }

        /// <summary>Переключиться на другую таблицу той же базы (ТЗ F1.2) + автодополнение колонок.</summary>
        public void UseTable(string tableName)
        {
            CurrentTable = tableName;
            Repo.TableName = tableName;
            if (Creator.TableExists(tableName))
                Creator.EnsureColumns(tableName); // ALTER TABLE ADD недостающих колонок
            Log.Info($"Выбрана таблица: {tableName}");
        }

        /// <summary>Открыть другой файл базы Access (ТЗ F1.1) и создать служебные таблицы при необходимости.</summary>
        public void UseDatabase(string dbPath)
        {
            DbPath = dbPath;
            if (!Creator.DatabaseExists())
            {
                Creator = new TableCreator(dbPath);
                Creator.CreateDatabase();
            }
            Creator = new TableCreator(dbPath);
            Audit = new AuditService(dbPath, Log);
            Mapping = new MappingService(dbPath);
            Backup = new BackupService(dbPath);
            CurrentTable = Schema.DefaultTable;
            Repo = new IOListRepository(dbPath, CurrentTable, Audit, Log);
            Import = new ImportService(Repo, Validator, Log);
            ModbusMaps = new ModbusMapService(dbPath);
            ModbusLinkRules = new ModbusLinkRuleService(dbPath);
            ModbusLink = new ModbusLinkService(Repo, ModbusMaps, Log, Audit, ModbusLinkRules);
            Log.Info($"Открыта база данных: {dbPath}");
            ServiceSchema.EnsureServiceTables(dbPath);
        }
    }
}
