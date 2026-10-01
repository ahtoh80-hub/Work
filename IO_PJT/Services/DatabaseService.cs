using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Text;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    public class DatabaseService
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseService(string dbPath)
        {
            _dbPath = dbPath;
            string provider = dbPath.EndsWith(".accdb", StringComparison.OrdinalIgnoreCase)
                ? "Microsoft.ACE.OLEDB.12.0"
                : "Microsoft.Jet.OLEDB.4.0";
            _connectionString = $"Provider={provider};Data Source={_dbPath};";
        }

        public bool DatabaseExists() => File.Exists(_dbPath);

        public void CreateEmptyDatabase()
        {
            if (DatabaseExists()) return;

            try
            {
                // Создаем пустую базу данных через ADOX (если доступен)
                CreateDatabaseViaAdox();
            }
            catch
            {
                // Если ADOX не доступен, создаем через OleDb
                CreateDatabaseViaOleDb();
            }
        }

        /// <summary>
        /// Создание базы данных через ADOX (предпочтительный способ)
        /// </summary>
        private void CreateDatabaseViaAdox()
        {
            try
            {
                // Используем динамическое создание ADOX для совместимости
                Type catalogType = Type.GetTypeFromProgID("ADOX.Catalog");
                if (catalogType != null)
                {
                    dynamic catalog = Activator.CreateInstance(catalogType);
                    catalog.Create(_connectionString);
                    catalog = null;
                    return;
                }
                throw new Exception("ADOX не доступен");
            }
            catch
            {
                throw new Exception("Не удалось создать базу данных через ADOX. Попробуйте установить Microsoft Access Database Engine.");
            }
        }

        /// <summary>
        /// Создание базы данных через OleDb (альтернативный способ)
        /// </summary>
        private void CreateDatabaseViaOleDb()
        {
            // Для создания пустой базы через OleDb нужно создать хотя бы одну таблицу
            using (var connection = new OleDbConnection(_connectionString))
            {
                connection.Open();
                
                // Создаем временную таблицу
                using (var cmd = new OleDbCommand("CREATE TABLE _temp (ID COUNTER PRIMARY KEY)", connection))
                {
                    cmd.ExecuteNonQuery();
                }
                
                // Удаляем временную таблицу
                using (var cmd = new OleDbCommand("DROP TABLE _temp", connection))
                {
                    cmd.ExecuteNonQuery();
                }
                
                connection.Close();
            }
        }

        public bool TableExists(string tableName)
        {
            using (var connection = new OleDbConnection(_connectionString))
            {
                connection.Open();
                string[] restrictions = new string[4] { null, null, tableName, null };
                var tables = connection.GetSchema("Tables", restrictions);
                return tables.Rows.Count > 0;
            }
        }

        public void DropTable(string tableName)
        {
            using (var connection = new OleDbConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new OleDbCommand($"DROP TABLE [{tableName}]", connection))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void CreateTable(string tableName)
        {
            var fields = TableStructure.GetFields();
            var sb = new StringBuilder();
            sb.Append($"CREATE TABLE [{tableName}] (");

            for (int i = 0; i < fields.Count; i++)
            {
                sb.Append($"[{fields[i].Name}] {fields[i].Type}");
                if (i < fields.Count - 1)
                    sb.Append(", ");
            }
            sb.Append(")");

            string createSql = sb.ToString();

            using (var connection = new OleDbConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new OleDbCommand(createSql, connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // Создаем индекс для быстрого поиска по Tag
                try
                {
                    string createIndexSql = $"CREATE INDEX idx_Tag ON [{tableName}] (Tag)";
                    using (var cmd = new OleDbCommand(createIndexSql, connection))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
                catch { /* Индекс может не создаться, если поле Tag отсутствует */ }

                connection.Close();
            }
        }

        public void CreateTableIfNotExists(string tableName)
        {
            if (TableExists(tableName))
                DropTable(tableName);
            
            CreateTable(tableName);
        }

        public List<string> GetTableNames()
        {
            var tables = new List<string>();
            using (var connection = new OleDbConnection(_connectionString))
            {
                connection.Open();
                var schema = connection.GetSchema("Tables", new[] { null, null, null, "TABLE" });
                foreach (System.Data.DataRow row in schema.Rows)
                {
                    string tableName = row["TABLE_NAME"].ToString();
                    // Пропускаем системные таблицы
                    if (!tableName.StartsWith("~") && !tableName.StartsWith("MSys"))
                    {
                        tables.Add(tableName);
                    }
                }
                connection.Close();
            }
            return tables;
        }

        public void InsertSampleData(string tableName)
        {
            string insertSql = $@"
                INSERT INTO [{tableName}] (
                    Code, Area, Tag, TagPc, Service, ServiceEng, 
                    InstrumentType, InstrumentTypeEng, IoType, Location,
                    Controller, Pid
                ) VALUES (
                    1, 1401, '2701-XZY-10101A', '2701-XZY-10101A', 
                    'Распределительный клапан для впуска стирола', 
                    'Styrene Inlet Switch Valve of 2701-TK-1205A',
                    'Электромагнитный клапан', 'Solenoid valve',
                    'DOR-P', 'Field',
                    '2000-S-SC-B01', '2490156-1401-PR007-0101A'
                )";

            using (var connection = new OleDbConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new OleDbCommand(insertSql, connection))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Проверяет, доступен ли провайдер ACE для работы с .accdb
        /// </summary>
        public static bool IsAceProviderAvailable()
        {
            try
            {
                using (var connection = new OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=:memory:"))
                {
                    connection.Open();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Проверяет, доступен ли провайдер Jet для работы с .mdb
        /// </summary>
        public static bool IsJetProviderAvailable()
        {
            try
            {
                using (var connection = new OleDbConnection("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=:memory:"))
                {
                    connection.Open();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}