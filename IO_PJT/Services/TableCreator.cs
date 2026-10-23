using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.IO;
using IO_PJT.Data;

namespace IO_PJT.Services
{
    /// <summary>
    /// Создание базы Access и таблиц по DDL из раздела Б8 IOclass.md (ТЗ F1).
    /// Имя таблицы данных может быть любым, по умолчанию — IO_LIST.
    /// </summary>
    public sealed class TableCreator : ITableCreator
    {
        private readonly string _dbPath;

        public TableCreator(string dbPath) => _dbPath = dbPath;

        private string ConnStr => DbProbe.ConnStrWithTimeout(_dbPath);

        /// <summary>Строка соединения без таймаутов — для ADOX (Catalog.Create не принимает
        /// дополнительные ключи OLE DB вроде Connect Timeout).</summary>
        private string ConnStrRaw => $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={_dbPath};";

        /// <summary>Файл базы существует.</summary>
        public bool DatabaseExists() => File.Exists(_dbPath);

        /// <summary>Создать базу с нуля + служебные таблицы (аудит и шаблоны маппинга).</summary>
        public void CreateDatabase()
        {
            if (DatabaseExists()) return;

            // Способ создания .accdb — ADOX (Microsoft Access Database Engine)
            Type? catalogType = Type.GetTypeFromProgID("ADOX.Catalog")
                ?? throw new InvalidOperationException(
                    "Провайдер ADOX недоступен. Установите Microsoft Access Database Engine (ACE OLEDB).");

            object? catalog = Activator.CreateInstance(catalogType);
            try
            {
                dynamic cat = catalog!;
                cat.Create(ConnStrRaw);
            }
            finally
            {
                if (catalog != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(catalog);
            }

            // Служебные таблицы: история изменений и шаблоны маппинга
            Execute(Schema.CreateAuditTable);
            Execute(Schema.CreateMappingTemplatesTable);
        }

        /// <summary>Список пользовательских таблиц базы (без системных MSys*).</summary>
        public IReadOnlyList<string> GetTableNames()
        {
            var result = new List<string>();
            using var cn = Open();
            var dt = cn.GetSchema("Tables", new[] { null, null, null, "TABLE" });
            foreach (DataRow row in dt.Rows)
            {
                string name = row["TABLE_NAME"]?.ToString() ?? "";
                if (!name.StartsWith("~") && !name.StartsWith("MSys"))
                    result.Add(name);
            }
            return result;
        }

        /// <summary>Проверка существования таблицы.</summary>
        public bool TableExists(string tableName)
        {
            using var cn = Open();
            var dt = cn.GetSchema("Tables", new[] { null, null, tableName, "TABLE" });
            return dt.Rows.Count > 0;
        }

        /// <summary>Создать таблицу данных с нуля (F1.3): структура + уникальный индекс Tag.</summary>
        public void CreateDataTable(string tableName)
        {
            if (TableExists(tableName))
                throw new InvalidOperationException($"Таблица [{tableName}] уже существует — укажите другое имя.");

            Execute(Schema.CreateTable(tableName));
            Execute(Schema.CreateTagIndex(tableName)); // ключевое поле Tag (F4.3)
        }

        /// <summary>F2.17: гарантия таблицы копий IO_LIST_DublicateTag — структура = IO_LIST,
        /// но без уникального индекса Tag (все копии повторяющихся тегов). Идемпотентно;
        /// вызывается перед импортом, чтобы копии писались без сбоев.</summary>
        public void EnsureDuplicateTagTable()
        {
            if (!DatabaseExists()) return;
            if (!TableExists(Schema.DuplicateTable))
                Execute(Schema.CreateTable(Schema.DuplicateTable)); // без CreateTagIndex — копии без ограничения
            else
                EnsureColumns(Schema.DuplicateTable); // миграция: недостающие колонки (как у IO_LIST)
        }

        /// <summary>Колонки таблицы в порядке следования.</summary>
        public IReadOnlyList<string> GetColumns(string tableName)
        {
            var result = new List<string>();
            using var cn = Open();
            var dt = cn.GetSchema("Columns", new[] { null, null, tableName, null });
            var rows = dt.Select(null, "ORDINAL_POSITION");
            foreach (DataRow row in rows)
                result.Add(row["COLUMN_NAME"]?.ToString() ?? "");
            return result;
        }

        /// <summary>
        /// Гарантия служебных таблиц: создаёт IO_LIST_Audit и MappingTemplates,
        /// если их нет (идемпотентно). Вызывается ServiceSchema перед записью.
        /// Существующие таблицы аудита дополняются контекстными колонками (F7.6, ALTER TABLE ADD).
        /// </summary>
        public void EnsureServiceTables()
        {
            if (!DatabaseExists()) return;
            if (!TableExists("IO_LIST_Audit"))
            {
                Execute(Schema.CreateAuditTable);
            }
            else
            {
                // F7.6: миграция старых таблиц аудита — добавить REV_FileName/System при отсутствии
                var auditCols = new HashSet<string>(GetColumns("IO_LIST_Audit"), StringComparer.OrdinalIgnoreCase);
                foreach (var col in Schema.AuditContextColumns)
                    if (!auditCols.Contains(col))
                        Execute($"ALTER TABLE [IO_LIST_Audit] ADD COLUMN [{col}] TEXT(255)");
            }
            if (!TableExists("MappingTemplates"))
                Execute(Schema.CreateMappingTemplatesTable);
            if (!TableExists(Schema.ModbusMapTable))
            {
                Execute(Schema.CreateModbusMapTable);
            }
            else
            {
                // F9.3: миграция старых MODBUS_MAP — добавить LinkTag/LinkState при отсутствии
                var mapCols = new HashSet<string>(GetColumns(Schema.ModbusMapTable), StringComparer.OrdinalIgnoreCase);
                if (!mapCols.Contains("LinkTag"))
                    Execute($"ALTER TABLE [{Schema.ModbusMapTable}] ADD COLUMN [LinkTag] TEXT(255)");
                if (!mapCols.Contains("LinkState"))
                    Execute($"ALTER TABLE [{Schema.ModbusMapTable}] ADD COLUMN [LinkState] TEXT(255)");
            }
            // F9.11: ручные правила связывания IO_LIST ↔ MODBUS_MAP (окно «Правила связывания Modbus…»)
            if (!TableExists(Schema.ModbusLinkRulesTable))
                Execute(Schema.CreateModbusLinkRulesTable);
            // F12: таблицы генерации перечня «Тех.объекты» (SCADA → меню SCADA)
            if (!TableExists(Schema.ScadaTypeMapTable))
            {
                Execute(Schema.CreateScadaTypeMapTable);
                SeedScadaTypeMap();
            }
            if (!TableExists(Schema.ScadaSettingsTable))
                Execute(Schema.CreateScadaSettingsTable);
            // F13: таблицы генерации блоков Modbus (XML) (окно «Генерация блоков Modbus (XML)…»)
            if (!TableExists(Schema.ScadaMbSettingsTable))
                Execute(Schema.CreateScadaMbSettingsTable);
            if (!TableExists(Schema.ScadaMbSlaveRulesTable))
                Execute(Schema.CreateScadaMbSlaveRulesTable);
            else
            {
                // F21: миграция — параметры станции (IP main/reserve, протокол, скорость,
                // UART, таймаут, макс. длина) в старых базах (Station/SlaveId)
                var srCols = new HashSet<string>(GetColumns(Schema.ScadaMbSlaveRulesTable),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var (col, type) in new (string, string)[]
                {
                    ("Controller", "TEXT(255)"), // F37: контроллер IO_LIST (Identification.Controller)
                    ("IpMain", "TEXT(255)"), ("IpReserve", "TEXT(255)"), ("Protocol", "TEXT(10)"),
                    ("Speed", "TEXT(50)"), ("Parity", "YESNO"), ("StopBit", "LONG"),
                    ("DataBit", "LONG"), ("Reverse", "YESNO"), ("Timeout", "LONG"),
                    ("MaxLength", "LONG"),
                    ("MasterTemplate", "TEXT(255)") // F29: шаблон пакетов станции
                })
                    if (!srCols.Contains(col))
                        Execute($"ALTER TABLE [{Schema.ScadaMbSlaveRulesTable}] ADD COLUMN [{col}] {type}");
            }
            if (!TableExists(Schema.ScadaMbTemplateTable))
                Execute(Schema.CreateScadaMbTemplateTable);
            // F14: ручное сопоставление «Typno → эталон шаблона привязок Data_Out»
            if (!TableExists(Schema.ScadaTypnoMapTable))
                Execute(Schema.CreateScadaTypnoMapTable);
            else
            {
                // F16: миграция — колонка Template (имя именованного шаблона Data_Out)
                var tnCols = new HashSet<string>(GetColumns(Schema.ScadaTypnoMapTable), StringComparer.OrdinalIgnoreCase);
                if (!tnCols.Contains("Template"))
                    Execute($"ALTER TABLE [{Schema.ScadaTypnoMapTable}] ADD COLUMN [Template] TEXT(255)");
                // F33: миграция — колонка Size (размер переменной: 32bit/16bit/8bit/bit/64bit)
                if (!tnCols.Contains("Size"))
                    Execute($"ALTER TABLE [{Schema.ScadaTypnoMapTable}] ADD COLUMN [Size] TEXT(10)");
            }
            // F16: именованные шаблоны привязок Data_Out (имя → XML-текст, Typno → имя)
            if (!TableExists(Schema.ScadaDataOutTemplatesTable))
                Execute(Schema.CreateScadaDataOutTemplatesTable);
            // F22: мастер-шаблоны блоков (TCP/RTU) и шаблоны привязок DataIn
            if (!TableExists(Schema.ScadaMasterTemplatesTable))
                Execute(Schema.CreateScadaMasterTemplatesTable);
            if (!TableExists(Schema.ScadaDataInTemplatesTable))
                Execute(Schema.CreateScadaDataInTemplatesTable);
            // F23: маппинг «переменная шаблона → колонка IO_LIST/литерал»
            if (!TableExists(Schema.ScadaTemplateVarsTable))
                Execute(Schema.CreateScadaTemplateVarsTable);
        }

        /// <summary>F12: сидирование маппинга типов IO List → SCADA по умолчанию
        /// (раздел 2.5.4 спецификации; выполняется только при создании таблицы).</summary>
        private void SeedScadaTypeMap()
        {
            using var cn = Open();
            using var ins = new OleDbCommand(
                $"INSERT INTO [{Schema.ScadaTypeMapTable}] ([Source], [Target]) VALUES (?, ?)", cn);
            foreach (var (source, target) in Schema.DefaultScadaTypeMap)
            {
                ins.Parameters.Clear();
                OleDbArgs.Add(ins, source);
                OleDbArgs.Add(ins, target);
                ins.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Автодополнение структуры существующей таблицы: недостающие колонки
        /// добавляются через ALTER TABLE ADD по типам из Б8 (Schema.SqlTypeFor).
        /// </summary>
        public void EnsureColumns(string tableName)
        {
            if (!TableExists(tableName)) return;
            var existing = new HashSet<string>(GetColumns(tableName), StringComparer.OrdinalIgnoreCase);
            foreach (var col in Schema.DataColumns)
            {
                if (!existing.Contains(col))
                    Execute($"ALTER TABLE [{tableName}] ADD COLUMN [{col}] {Schema.SqlTypeFor(col)}");
            }
        }

        private OleDbConnection Open()
        {
            var cn = new OleDbConnection(ConnStr);
            cn.Open();
            return cn;
        }

        private void Execute(string sql)
        {
            using var cn = Open();
            using var cmd = new OleDbCommand(sql, cn);
            cmd.ExecuteNonQuery();
        }
    }
}
