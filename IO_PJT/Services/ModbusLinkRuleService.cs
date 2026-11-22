using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Reflection;
using IO_PJT.Data;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>F9.11/F99: ручные правила связывания IO_LIST ↔ MODBUS_MAP (таблица
    /// MODBUS_LINK_RULES). Правило сопоставляет поле записи IO_LIST (IOField: любая колонка
    /// FieldRegistry) с полем строки карты (MapField) по маске Mask (см. MaskPattern)
    /// либо, при пустой маске, точным равенством значений (+ фильтр FuncCode);
    /// применяются в ModbusLinkService.Link(). F99: шаблоны TagPattern/ValuePattern
    /// и ServiceField (F69) удалены — только маска.</summary>
    public sealed class ModbusLinkRuleService
    {
        private readonly string _dbPath;

        public ModbusLinkRuleService(string dbPath) => _dbPath = dbPath;

        private string ConnStr => DbProbe.ConnStrWithTimeout(_dbPath);

        /// <summary>Гарантия таблицы MODBUS_LINK_RULES (идемпотентно; F99 — также мигрирует
        /// старые базы: ADD COLUMN Mask, DROP COLUMN TagPattern/ValuePattern).</summary>
        public void EnsureTable()
        {
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return;
            new TableCreator(_dbPath).EnsureServiceTables();
        }

        // ================== Значения полей ==================

        private static readonly Dictionary<string, PropertyInfo> MapProps =
            typeof(ModbusMapEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(pr => pr.PropertyType == typeof(string))
                .ToDictionary(pr => pr.Name, StringComparer.OrdinalIgnoreCase);

        /// <summary>F99: имена строковых полей карты (для ComboBox в окне правил).</summary>
        public static string[] MapFields =>
            MapProps.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

        /// <summary>Значение поля карты (MapField) строки; неизвестное поле → null.
        /// Доступны все строковые свойства ModbusMapEntry (Description, Scale, PlcTag, …).</summary>
        public static string? GetMapValue(ModbusMapEntry e, string mapField) =>
            mapField != null && MapProps.TryGetValue(mapField.Trim(), out var pr)
                ? (string?)pr.GetValue(e)
                : null;

        /// <summary>Проверка поля карты (для UI); null — ок, иначе текст ошибки.</summary>
        public static string? ValidateMapField(string mapField) =>
            !string.IsNullOrWhiteSpace(mapField) && MapProps.ContainsKey(mapField.Trim())
                ? null
                : $"неизвестное поле карты «{mapField}»";

        /// <summary>Значение поля IO-стороны (IOField) записи — любая колонка FieldRegistry;
        /// неизвестное/пустое поле → null.</summary>
        public static string? GetIoValue(IoPoint p, string ioField)
        {
            string col = (ioField ?? "").Trim();
            if (col.Length == 0) return null;
            return FieldRegistry.GetValue(p, col) is { } v ? Convert.ToString(v) : null;
        }

        /// <summary>Проверка одного правила (для UI/сохранения); null — ок. F99: IOField —
        /// колонка FieldRegistry, маска — MaskPattern.Validate.</summary>
        public static string? Validate(ModbusLinkRule r)
        {
            if (string.IsNullOrWhiteSpace(r.Name)) return "название правила обязательно";
            if (!FieldRegistry.HasColumn((r.IOField ?? "").Trim()))
                return $"неизвестное поле IO «{r.IOField}» (колонки IO_LIST: см. справку)";
            string? e1 = ValidateMapField(r.MapField);
            if (e1 != null) return e1;
            return MaskPattern.Validate(r.Mask);
        }

        // ================== CRUD ==================

        /// <summary>Все правила (ORDER BY Priority, ID) — для окна редактирования.</summary>
        public List<ModbusLinkRule> Load()
        {
            var result = new List<ModbusLinkRule>();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return result;
            EnsureTable();
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var cmd = new OleDbCommand(
                $"SELECT [ID], [Name], [IOField], [MapField], [Mask], [FuncCode], " +
                $"[Priority], [Enabled] FROM [{Schema.ModbusLinkRulesTable}] " +
                "ORDER BY [Priority], [ID]", cn);
            cmd.CommandTimeout = DbProbe.TimeoutSeconds;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                result.Add(ReadRule(rd));
            return result;
        }

        /// <summary>Включённые правила для Link() (Enabled &lt;&gt; 0; ORDER BY Priority, ID —
        /// правило с меньшим приоритетом применяется раньше).</summary>
        public List<ModbusLinkRule> LoadEnabled()
        {
            var result = new List<ModbusLinkRule>();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return result;
            EnsureTable();
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var cmd = new OleDbCommand(
                $"SELECT [ID], [Name], [IOField], [MapField], [Mask], [FuncCode], " +
                $"[Priority], [Enabled] FROM [{Schema.ModbusLinkRulesTable}] " +
                "WHERE [Enabled] <> 0 ORDER BY [Priority], [ID]", cn);
            cmd.CommandTimeout = DbProbe.TimeoutSeconds;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                result.Add(ReadRule(rd));
            return result;
        }

        // F99: SELECT 8 колонок — ID, Name, IOField, MapField, Mask, FuncCode, Priority,
        // Enabled (индексы 0..7; Enabled читается из СВОЕГО индекса 7 — раньше была ошибка
        // с чтением из индекса 8 = Priority)
        private static ModbusLinkRule ReadRule(OleDbDataReader rd) => new()
        {
            Id = Convert.ToInt64(rd.GetValue(0)),
            Name = rd.IsDBNull(1) ? "" : rd.GetString(1),
            IOField = rd.IsDBNull(2) ? "Tag" : rd.GetString(2),
            MapField = rd.IsDBNull(3) ? "Description" : rd.GetString(3),
            Mask = rd.IsDBNull(4) ? "" : rd.GetString(4),
            FuncCode = rd.IsDBNull(5) ? null : rd.GetString(5),
            Priority = rd.IsDBNull(6) ? 100 : rd.GetInt32(6),
            Enabled = !rd.IsDBNull(7) && Convert.ToBoolean(rd.GetValue(7))
        };

        /// <summary>Сохранить набор правил целиком (DELETE + INSERT одной транзакцией —
        /// полная замена, как в окне редактирования). Валидация до записи.
        /// F9-фикс: при блокировке БД повтор до 3 раз.</summary>
        public int SaveAll(IEnumerable<ModbusLinkRule> rules)
        {
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            var list = rules.ToList();
            foreach (var r in list)
            {
                string? err = Validate(r);
                if (err != null) throw new InvalidOperationException(err);
            }
            EnsureTable();
            for (int attempt = 1; ; attempt++)
            {
                try { return SaveAllOnce(list); }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private int SaveAllOnce(List<ModbusLinkRule> list)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                using (var del = new OleDbCommand(
                    $"DELETE FROM [{Schema.ModbusLinkRulesTable}]", cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    del.ExecuteNonQuery();
                }
                // F99: 7 столбцов и 7 «?» (раньше в INSERT было 9 колонок и 8 параметров —
                // запись правил падала с ошибкой OleDb)
                using var ins = new OleDbCommand(
                    $"INSERT INTO [{Schema.ModbusLinkRulesTable}] " +
                    "([Name], [IOField], [MapField], [Mask], [FuncCode], [Priority], [Enabled]) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?)", cn, tr);
                ins.CommandTimeout = DbProbe.TimeoutSeconds;
                foreach (var r in list)
                {
                    ins.Parameters.Clear();
                    OleDbArgs.Add(ins, r.Name);
                    OleDbArgs.Add(ins, r.IOField);
                    OleDbArgs.Add(ins, r.MapField);
                    OleDbArgs.Add(ins, r.Mask);
                    OleDbArgs.Add(ins, r.FuncCode);
                    OleDbArgs.Add(ins, r.Priority);
                    OleDbArgs.Add(ins, r.Enabled);
                    ins.ExecuteNonQuery();
                }
                tr.Commit();
                return list.Count;
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }
    }
}
