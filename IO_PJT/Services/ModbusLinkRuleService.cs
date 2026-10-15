using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using IO_PJT.Data;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// F9.11: ручные правила связывания IO_LIST ↔ MODBUS_MAP (таблица MODBUS_LINK_RULES).
    /// Правило сопоставляет поле записи IO_LIST (IOField: Tag/Service/…) по шаблону TagPattern
    /// с полем строки карты (MapField: Description/Scale/…) по шаблону ValuePattern (+ фильтр
    /// FuncCode); применяются в ModbusLinkService.Link() ПЕРЕД встроенными ролями DetectRole.
    /// Шаблоны (TagPattern/ValuePattern): начинается с «*»/«?» либо содержит только wildcard-метки
    /// → wildcard (анкорный: «*» = любые символы, «?» = один, IgnoreCase); содержит метасимволы
    /// regex (. + ^ $ ( ) [ ] { } | \) → регулярное выражение. Примеры: «*Fault*» (wildcard),
    /// «MXA.*A»/«MXA+A» (regex).</summary>
    public sealed class ModbusLinkRuleService
    {
        private readonly string _dbPath;

        public ModbusLinkRuleService(string dbPath) => _dbPath = dbPath;

        private string ConnStr => DbProbe.ConnStrWithTimeout(_dbPath);

        /// <summary>Гарантия таблицы MODBUS_LINK_RULES (идемпотентно).</summary>
        public void EnsureTable()
        {
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return;
            new TableCreator(_dbPath).EnsureServiceTables();
        }

        // ================== Шаблоны ==================

        /// <summary>Скомпилировать шаблон. Семантика (F9.11): (1) начинается с «*» или «?» →
        /// wildcard (анкорный: «*» = любые символы, «?» = один); (2) содержит метасимволы
        /// регулярного выражения (. + ^ $ ( ) [ ] { } | \) → regex как есть; (3) содержит
        /// «*»/«?» в теле → wildcard; иначе → regex. IgnoreCase. Невалидный regex → ArgumentException.
        /// Примеры: «*Fault*»/«*MXA*A»/«1115-MXA-*» — wildcard; «MXA.*A»/«MXA+A»/«MXA-\d+A» — regex.</summary>
        public static Regex Compile(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                throw new ArgumentException("шаблон пуст");

            // метасимволы regex, кроме «*»/«?» — их наличие означает regex-нотацию
            static bool HasRegexMeta(string p) =>
                p.IndexOfAny(new[] { '.', '+', '^', '$', '(', ')', '[', ']', '{', '}', '|', '\\' }) >= 0;

            bool wildcard;
            if (pattern[0] is '*' or '?')
                wildcard = true;                       // glob-нотация: начинается с метки wildcard
            else if (HasRegexMeta(pattern))
                wildcard = false;                      // regex-нотация («MXA.*A», «MXA+A», «\d+»)
            else
                wildcard = pattern.IndexOfAny(new[] { '*', '?' }) >= 0;

            string rx = wildcard
                ? "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$"
                : pattern;
            return new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>Проверить шаблон (для UI); null — ок, иначе текст ошибки.</summary>
        public static string? ValidatePattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return "шаблон пуст";
            try { Compile(pattern); return null; }
            catch (ArgumentException ex) { return $"неверный шаблон «{pattern}»: {ex.Message}"; }
        }

        /// <summary>Совпадение значения с шаблоном (wildcard/regex, IgnoreCase); пустой шаблон — false.</summary>
        public static bool PatternMatch(string? value, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            try { return Compile(pattern).IsMatch(value ?? ""); }
            catch (ArgumentException) { return false; } // невалидный шаблон не ломает связывание
        }

        // ================== Значения полей ==================

        /// <summary>Поля IO_LIST, доступные правилу (IOField).</summary>
        public static readonly string[] IoFields = { "Tag", "Service", "ServiceEnglish", "IoType", "Loop", "SignalType" };

        /// <summary>Значение поля IO-стороны (IOField) записи; неизвестное поле → null.</summary>
        public static string? GetIoValue(IoPoint p, string ioField)
        {
            switch ((ioField ?? "").Trim().ToUpperInvariant())
            {
                case "TAG": return p.Tag;
                case "SERVICE": return p.Signal?.Service;
                case "SERVICEENGLISH": return p.Signal?.ServiceEnglish;
                case "IOTYPE": return p.Identification.IoType;
                case "LOOP": return p.Identification.Loop;
                case "SIGNALTYPE": return p.Signal?.SignalType;
                default: return null;
            }
        }

        private static readonly Dictionary<string, PropertyInfo> MapProps =
            typeof(ModbusMapEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(pr => pr.PropertyType == typeof(string))
                .ToDictionary(pr => pr.Name, StringComparer.OrdinalIgnoreCase);

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

        /// <summary>Проверка одного правила (для UI/сохранения); null — ок.</summary>
        public static string? Validate(ModbusLinkRule r)
        {
            if (string.IsNullOrWhiteSpace(r.Name)) return "название правила обязательно";
            if (!IoFields.Contains((r.IOField ?? "").Trim(), StringComparer.OrdinalIgnoreCase))
                return $"неизвестное поле IO «{r.IOField}» (допустимы: {string.Join(", ", IoFields)})";
            string? e1 = ValidatePattern(r.TagPattern);
            if (e1 != null) return $"TagPattern: {e1}";
            string? e2 = ValidatePattern(r.ValuePattern);
            if (e2 != null) return $"ValuePattern: {e2}";
            return ValidateMapField(r.MapField);
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
                $"SELECT [ID], [Name], [IOField], [TagPattern], [MapField], [ValuePattern], " +
                $"[FuncCode], [Priority], [Enabled] FROM [{Schema.ModbusLinkRulesTable}] " +
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
                $"SELECT [ID], [Name], [IOField], [TagPattern], [MapField], [ValuePattern], " +
                $"[FuncCode], [Priority], [Enabled] FROM [{Schema.ModbusLinkRulesTable}] " +
                "WHERE [Enabled] <> 0 ORDER BY [Priority], [ID]", cn);
            cmd.CommandTimeout = DbProbe.TimeoutSeconds;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                result.Add(ReadRule(rd));
            return result;
        }

        private static ModbusLinkRule ReadRule(OleDbDataReader rd) => new()
        {
            Id = Convert.ToInt64(rd.GetValue(0)),
            Name = rd.IsDBNull(1) ? "" : rd.GetString(1),
            IOField = rd.IsDBNull(2) ? "Tag" : rd.GetString(2),
            TagPattern = rd.IsDBNull(3) ? "" : rd.GetString(3),
            MapField = rd.IsDBNull(4) ? "Description" : rd.GetString(4),
            ValuePattern = rd.IsDBNull(5) ? "" : rd.GetString(5),
            FuncCode = rd.IsDBNull(6) ? null : rd.GetString(6),
            Priority = rd.IsDBNull(7) ? 100 : rd.GetInt32(7),
            Enabled = !rd.IsDBNull(8) && Convert.ToBoolean(rd.GetValue(8))
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
                using var ins = new OleDbCommand(
                    $"INSERT INTO [{Schema.ModbusLinkRulesTable}] " +
                    "([Name], [IOField], [TagPattern], [MapField], [ValuePattern], [FuncCode], [Priority], [Enabled]) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?)", cn, tr);
                ins.CommandTimeout = DbProbe.TimeoutSeconds;
                foreach (var r in list)
                {
                    ins.Parameters.Clear();
                    OleDbArgs.Add(ins, r.Name);
                    OleDbArgs.Add(ins, r.IOField);
                    OleDbArgs.Add(ins, r.TagPattern);
                    OleDbArgs.Add(ins, r.MapField);
                    OleDbArgs.Add(ins, r.ValuePattern);
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
