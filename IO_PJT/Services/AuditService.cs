using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// История изменений полей (ТЗ F7): таблица IO_LIST_Audit в той же базе.
    /// Фиксирует «что на что менялось»: старое → новое значение каждого поля.
    /// </summary>
    public sealed class AuditService : IAuditService
    {
        private readonly string _dbPath;
        private readonly object _sync = new();
        private readonly List<(string TableName, string? Tag, AuditAction Action, string Field, string? Old, string? New, string? FileName, string? Sheet, string? System)> _buffer = new();
        private bool _batch;

        public AuditService(string dbPath) => _dbPath = dbPath;

        private string ConnStr => $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={_dbPath};";

        /// <summary>Windows-логин текущего пользователя (домен\логин).</summary>
        public static string CurrentUser =>
            $"{Environment.UserDomainName}\\{Environment.UserName}";

        /// <summary>Записать одно изменение поля. В пакетном режиме (F2.9) — только буфер, без обращений к БД.
        /// fileName/sheet (F7.6/F7.7) — контекст документа (файл + вкладка), system — система.</summary>
        public void Record(string tableName, string? tag, AuditAction action,
                           string field, string? oldValue, string? newValue,
                           string? fileName = null, string? system = null, string? sheet = null)
        {
            lock (_sync)
            {
                if (_batch)
                {
                    _buffer.Add((tableName, tag, action, field, oldValue, newValue, fileName, sheet, system));
                    return;
                }
            }
            // Гарантия: служебные таблицы создаются автоматически при записи (решение п.3)
            ServiceSchema.EnsureServiceTables(_dbPath);
            try
            {
                InsertRecord(tableName, tag, action, field, oldValue, newValue, fileName, system, sheet);
            }
            catch (System.Data.OleDb.OleDbException ex) when (IsTableMissing(ex))
            {
                // Таблицу удалили во время работы — пересоздаём и повторяем запись
                ServiceSchema.Reset(_dbPath);
                ServiceSchema.EnsureServiceTables(_dbPath);
                InsertRecord(tableName, tag, action, field, oldValue, newValue, fileName, system, sheet);
            }
        }

        private const string InsertSql = @"INSERT INTO IO_LIST_Audit
                (TableName, Tag, ChangeAction, FieldName, OldValue, NewValue, REV_FileName, REV_Sheet, [System], ChangedBy, ChangedAt)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

        private void InsertRecord(string tableName, string? tag, AuditAction action,
                                  string field, string? oldValue, string? newValue,
                                  string? fileName, string? system, string? sheet)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var cmd = new OleDbCommand(InsertSql, cn);
            FillParams(cmd, tableName, tag, action, field, oldValue, newValue, fileName, system, sheet);
            cmd.ExecuteNonQuery();
        }

        private static void FillParams(OleDbCommand cmd, string tableName, string? tag, AuditAction action,
                                       string field, string? oldValue, string? newValue,
                                       string? fileName, string? system, string? sheet)
        {
            OleDbArgs.Add(cmd, tableName);
            OleDbArgs.Add(cmd, tag);
            OleDbArgs.Add(cmd, action.ToString());
            OleDbArgs.Add(cmd, field);
            OleDbArgs.Add(cmd, oldValue);
            OleDbArgs.Add(cmd, newValue);
            OleDbArgs.Add(cmd, fileName); // F7.6: REV_FileName — документ-источник изменения
            OleDbArgs.Add(cmd, sheet);    // F7.7: REV_Sheet — вкладка-источник (F2.16)
            OleDbArgs.Add(cmd, system);  // F7.6: System — система изменения
            OleDbArgs.Add(cmd, CurrentUser);
            OleDbArgs.Add(cmd, DateTime.Now);
        }

        // ===== Пакетный режим (F2.9): импорт пишет аудит одной транзакцией =====

        /// <summary>Начать пакет: Record() только буферизуется (без открытия соединений).</summary>
        public void BeginBatch()
        {
            lock (_sync)
            {
                _buffer.Clear();
                _batch = true;
            }
        }

        /// <summary>Записать буфер одним соединением и одной транзакцией. Идемпотентно.</summary>
        public void EndBatch()
        {
            List<(string TableName, string? Tag, AuditAction Action, string Field, string? Old, string? New, string? FileName, string? Sheet, string? System)> pending;
            lock (_sync)
            {
                _batch = false;
                if (_buffer.Count == 0) return;
                pending = new List<(string TableName, string? Tag, AuditAction Action, string Field, string? Old, string? New, string? FileName, string? Sheet, string? System)>(_buffer);
                _buffer.Clear();
            }

            ServiceSchema.EnsureServiceTables(_dbPath);
            try
            {
                WriteMany(pending);
            }
            catch (OleDbException ex) when (IsTableMissing(ex))
            {
                // Таблицу удалили во время работы — пересоздаём и повторяем запись пакета
                ServiceSchema.Reset(_dbPath);
                ServiceSchema.EnsureServiceTables(_dbPath);
                WriteMany(pending);
            }
        }

        /// <summary>Отменить пакет: очистить буфер (данные аудита ещё не писались).</summary>
        public void DiscardBatch()
        {
            lock (_sync)
            {
                _batch = false;
                _buffer.Clear();
            }
        }

        /// <summary>Пакетная вставка: одно соединение + одна транзакция на весь буфер.</summary>
        private void WriteMany(List<(string TableName, string? Tag, AuditAction Action, string Field, string? Old, string? New, string? FileName, string? Sheet, string? System)> items)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            using var cmd = new OleDbCommand(InsertSql, cn, tr);
            foreach (var it in items)
            {
                cmd.Parameters.Clear();
                FillParams(cmd, it.TableName, it.Tag, it.Action, it.Field, it.Old, it.New, it.FileName, it.System, it.Sheet);
                cmd.ExecuteNonQuery();
            }
            tr.Commit();
        }

        /// <summary>Сообщение «таблица не найдена» (её удалили вне приложения).</summary>
        private static bool IsTableMissing(OleDbException ex) =>
            ex.Message.Contains("find", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("не может найти") || ex.Message.Contains("не найден");

        /// <summary>Выборка истории с фильтрами (Tag, поле, документ REV_FileName, вкладка REV_Sheet,
        /// система, период — F7.6/F7.7).</summary>
        public List<AuditRecord> GetHistory(string? tag = null, string? field = null,
                                            string? fileName = null, string? system = null,
                                            DateTime? from = null, DateTime? to = null,
                                            string? sheet = null)
        {
            ServiceSchema.EnsureServiceTables(_dbPath);
            var result = new List<AuditRecord>();
            string sql = "SELECT ID, TableName, Tag, ChangeAction, FieldName, OldValue, NewValue, REV_FileName, REV_Sheet, [System], ChangedBy, ChangedAt FROM IO_LIST_Audit WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(tag)) sql += " AND Tag LIKE ?";
            if (!string.IsNullOrWhiteSpace(field)) sql += " AND FieldName = ?";
            if (!string.IsNullOrWhiteSpace(fileName)) sql += " AND REV_FileName LIKE ?";
            if (!string.IsNullOrWhiteSpace(sheet)) sql += " AND REV_Sheet LIKE ?"; // F7.7
            if (!string.IsNullOrWhiteSpace(system)) sql += " AND [System] LIKE ?";
            if (from.HasValue) sql += " AND ChangedAt >= ?";
            if (to.HasValue) sql += " AND ChangedAt <= ?";
            sql += " ORDER BY ChangedAt DESC, ID DESC";

            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var cmd = new OleDbCommand(sql, cn);
            if (!string.IsNullOrWhiteSpace(tag)) cmd.Parameters.AddWithValue("?", "%" + tag + "%");
            if (!string.IsNullOrWhiteSpace(field)) cmd.Parameters.AddWithValue("?", field);
            if (!string.IsNullOrWhiteSpace(fileName)) cmd.Parameters.AddWithValue("?", "%" + fileName + "%");
            if (!string.IsNullOrWhiteSpace(sheet)) cmd.Parameters.AddWithValue("?", "%" + sheet + "%");
            if (!string.IsNullOrWhiteSpace(system)) cmd.Parameters.AddWithValue("?", "%" + system + "%");
            if (from.HasValue) OleDbArgs.Add(cmd, from.Value);
            if (to.HasValue) OleDbArgs.Add(cmd, to.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new AuditRecord
                {
                    Id = Convert.ToInt64(reader["ID"]),
                    TableName = reader["TableName"]?.ToString() ?? "",
                    Tag = reader["Tag"] as string,
                    Action = Enum.TryParse(reader["ChangeAction"]?.ToString() ?? "", true, out AuditAction act)
                        ? act : AuditAction.Update,
                    FieldName = reader["FieldName"]?.ToString() ?? "",
                    OldValue = reader["OldValue"] as string,
                    NewValue = reader["NewValue"] as string,
                    FileName = reader["REV_FileName"] as string, // F7.6: документ-источник
                    Sheet = reader["REV_Sheet"] as string,        // F7.7: вкладка-источник (F2.16)
                    System = reader["System"] as string,         // F7.6: система
                    ChangedBy = reader["ChangedBy"]?.ToString() ?? "",
                    ChangedAt = reader["ChangedAt"] is DateTime dt ? dt : DateTime.MinValue
                });
            }
            return result;
        }

        /// <summary>F7.8: агрегат истории для отчёта: COUNT по комбинации «документ → поле → действие»
        /// (SQL GROUP BY — считает вся таблица, а не только отображаемые 500 записей истории).
        /// Фильтры — те же, что у GetHistory.</summary>
        public List<AuditSummary> GetSummary(string? tag = null, string? field = null,
                                             string? fileName = null, string? system = null,
                                             DateTime? from = null, DateTime? to = null,
                                             string? sheet = null)
        {
            ServiceSchema.EnsureServiceTables(_dbPath);
            var result = new List<AuditSummary>();
            string sql = "SELECT REV_FileName, REV_Sheet, FieldName, ChangeAction, COUNT(*) AS Cnt " +
                         "FROM IO_LIST_Audit WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(tag)) sql += " AND Tag LIKE ?";
            if (!string.IsNullOrWhiteSpace(field)) sql += " AND FieldName = ?";
            if (!string.IsNullOrWhiteSpace(fileName)) sql += " AND REV_FileName LIKE ?";
            if (!string.IsNullOrWhiteSpace(sheet)) sql += " AND REV_Sheet LIKE ?"; // F7.7
            if (!string.IsNullOrWhiteSpace(system)) sql += " AND [System] LIKE ?";
            if (from.HasValue) sql += " AND ChangedAt >= ?";
            if (to.HasValue) sql += " AND ChangedAt <= ?";
            sql += " GROUP BY REV_FileName, REV_Sheet, FieldName, ChangeAction" +
                   " ORDER BY REV_FileName, REV_Sheet, FieldName";

            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var cmd = new OleDbCommand(sql, cn);
            if (!string.IsNullOrWhiteSpace(tag)) cmd.Parameters.AddWithValue("?", "%" + tag + "%");
            if (!string.IsNullOrWhiteSpace(field)) cmd.Parameters.AddWithValue("?", field);
            if (!string.IsNullOrWhiteSpace(fileName)) cmd.Parameters.AddWithValue("?", "%" + fileName + "%");
            if (!string.IsNullOrWhiteSpace(sheet)) cmd.Parameters.AddWithValue("?", "%" + sheet + "%");
            if (!string.IsNullOrWhiteSpace(system)) cmd.Parameters.AddWithValue("?", "%" + system + "%");
            if (from.HasValue) OleDbArgs.Add(cmd, from.Value);
            if (to.HasValue) OleDbArgs.Add(cmd, to.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new AuditSummary
                {
                    FileName = reader["REV_FileName"] as string,
                    Sheet = reader["REV_Sheet"] as string,
                    Field = reader["FieldName"]?.ToString() ?? "",
                    Action = Enum.TryParse(reader["ChangeAction"]?.ToString() ?? "", true, out AuditAction act)
                        ? act : AuditAction.Update,
                    Count = Convert.ToInt32(reader["Cnt"])
                });
            }
            return result;
        }
    }
}
