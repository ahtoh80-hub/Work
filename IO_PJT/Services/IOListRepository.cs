using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Linq;
using IO_PJT.Data;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// Репозиторий таблицы данных (OleDb/ACE): CRUD, upsert по Tag, поиск (ТЗ F3/F4).
    /// Каждое изменение поля фиксируется в IO_LIST_Audit (ТЗ F7).
    /// </summary>
    public sealed class IOListRepository : IIOListRepository
    {
        private readonly string _dbPath;
        private readonly IAuditService _audit;
        private readonly IEventLogger _log;

        public string TableName { get; set; }

        public IOListRepository(string dbPath, string tableName, IAuditService audit, IEventLogger log)
        {
            _dbPath = dbPath;
            TableName = tableName;
            _audit = audit;
            _log = log;
        }

        private string ConnStr => DbProbe.ConnStrWithTimeout(_dbPath);

        /// <summary>Список колонок в квадратных скобках (защита от зарезервированных имён: Loop, System, L...).</summary>
        private static string ColList =>
            "ID, " + string.Join(", ", FieldRegistry.Columns.Select(c => $"[{c}]"));

        private static string ParamList => string.Join(", ", FieldRegistry.Columns.Select(_ => "?"));

        private OleDbConnection Open()
        {
            var cn = new OleDbConnection(ConnStr);
            cn.Open();
            return cn;
        }

        // ===== Сессия массовой записи (F2.9) =====

        private OleDbConnection? _sessionCn;
        private OleDbTransaction? _sessionTr;

        /// <summary>Область соединения: в сессии возвращает сессионное соединение (не закрывать),
        /// вне сессии — новое (закрывать). Замена «using var cn = Open()».</summary>
        private readonly struct CnScope : IDisposable
        {
            private readonly OleDbConnection? _owned;
            public OleDbConnection Cn { get; }
            public CnScope(OleDbConnection cn, bool owned)
            {
                Cn = cn;
                _owned = owned ? cn : null;
            }
            public void Dispose() => _owned?.Dispose();
        }

        private CnScope Scope() => new CnScope(_sessionCn ?? Open(), _sessionCn == null);

        /// <summary>Команда на соединении: в сессии (F2.9) привязывается к сессионной транзакции.
        /// CommandTimeout (часть C): занятая база даёт ошибку быстро, а не через 30 с ожидания.</summary>
        private OleDbCommand Cmd(string sql, OleDbConnection cn)
        {
            var cmd = _sessionTr == null ? new OleDbCommand(sql, cn) : new OleDbCommand(sql, cn, _sessionTr);
            cmd.CommandTimeout = DbProbe.TimeoutSeconds;
            return cmd;
        }

        /// <summary>Начать сессию импорта: одно соединение + одна транзакция + пакет аудита. Идемпотентно.</summary>
        public void BeginSession()
        {
            if (_sessionCn != null) return;
            _sessionCn = new OleDbConnection(ConnStr);
            _sessionCn.Open();
            _sessionTr = _sessionCn.BeginTransaction();
            _audit.BeginBatch();
        }

        /// <summary>Завершить сессию: COMMIT данных + запись батча аудита одной транзакцией. Идемпотентно.</summary>
        public void EndSession()
        {
            if (_sessionCn == null) return;
            try
            {
                _sessionTr?.Commit();
            }
            finally
            {
                _sessionTr?.Dispose();
                _sessionTr = null;
                _sessionCn.Dispose();
                _sessionCn = null;
            }
            _audit.EndBatch(); // после COMMIT данных — батч аудита пишется своей транзакцией
        }

        /// <summary>Отменить сессию: ROLLBACK данных + отброс батча аудита. Идемпотентно.</summary>
        public void AbortSession()
        {
            if (_sessionCn == null) return;
            try
            {
                try { _sessionTr?.Rollback(); }
                catch { /* соединение могло уже разорваться из-за ошибки */ }
            }
            finally
            {
                _sessionTr?.Dispose();
                _sessionTr = null;
                _sessionCn.Dispose();
                _sessionCn = null;
            }
            _audit.DiscardBatch();
        }

        /// <summary>Построчный Info-лог: в сессии (F2.9) подавляется — иначе 1300+ сообщений на импорт.</summary>
        private void LogRowInfo(string msg)
        {
            if (_sessionCn == null) _log.Info(msg);
        }

        /// <summary>Прочитать запись из DataRecord в модель (приведение типов — в FieldRegistry).</summary>
        private static IoPoint ReadPoint(IDataRecord rd)
        {
            var p = new IoPoint { Id = Convert.ToInt64(rd["ID"]) };
            foreach (var f in FieldRegistry.Fields)
            {
                int ord = rd.GetOrdinal(f.Column);
                object? v = rd.IsDBNull(ord) ? null : rd.GetValue(ord);
                f.Set(p, v);
            }
            return p;
        }

        /// <summary>Экранирование спецсимволов LIKE для Access (%, _, [).</summary>
        private static string Esc(string s) =>
            s.Replace("[", "[[]").Replace("%", "[%]").Replace("*", "[*]");

        // ================== Чтение (F3) ==================

        /// <summary>Все записи, упорядоченные по Area, Loop, Tag (F3.1).</summary>
        public List<IoPoint> GetAll()
        {
            var list = new List<IoPoint>();
            using var scope = Scope(); var cn = scope.Cn;
            string sql = $"SELECT {ColList} FROM [{TableName}] ORDER BY [Area], [Loop], [Tag]";
            using var cmd = Cmd(sql, cn);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) list.Add(ReadPoint(rd));
            return list;
        }

        /// <summary>Точный поиск по уникальному тегу (F3.2).</summary>
        public IoPoint? GetByTag(string tag)
        {
            using var scope = Scope(); var cn = scope.Cn;
            string sql = $"SELECT {ColList} FROM [{TableName}] WHERE [Tag] = ?";
            using var cmd = Cmd(sql, cn);
            OleDbArgs.Add(cmd, tag);
            using var rd = cmd.ExecuteReader();
            return rd.Read() ? ReadPoint(rd) : null;
        }

        /// <summary>Подстрочный поиск по Tag, Service, Loop, InstrumentType (F3.3).</summary>
        public List<IoPoint> Search(string term)
        {
            var list = new List<IoPoint>();
            using var scope = Scope(); var cn = scope.Cn;
            string pattern = "%" + Esc(term) + "%";
            string sql = $"SELECT {ColList} FROM [{TableName}] WHERE [Tag] LIKE ? OR [Service] LIKE ? " +
                         "OR [Loop] LIKE ? OR [InstrumentType] LIKE ? ORDER BY [Area], [Loop], [Tag]";
            using var cmd = Cmd(sql, cn);
            for (int i = 0; i < 4; i++) cmd.Parameters.AddWithValue("?", pattern);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) list.Add(ReadPoint(rd));
            return list;
        }

        /// <summary>Фильтр по системе (F3.4; значение System произвольное — DCS, РСУ, ПСБ и т.п.; точное совпадение).</summary>
        public List<IoPoint> GetBySystem(string system)
        {
            var list = new List<IoPoint>();
            using var scope = Scope(); var cn = scope.Cn;
            string sql = $"SELECT {ColList} FROM [{TableName}] WHERE [System] = ? ORDER BY [Area], [Loop], [Tag]";
            using var cmd = Cmd(sql, cn);
            cmd.Parameters.AddWithValue("?", system);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) list.Add(ReadPoint(rd));
            return list;
        }

        /// <summary>Уникальные непустые значения System (F3.4): список фильтра главного окна; System произвольный (DCS, РСУ, ПСБ и т.п.).</summary>
        public List<string> GetSystems()
        {
            var list = new List<string>();
            using var scope = Scope(); var cn = scope.Cn;
            string sql = $"SELECT DISTINCT [System] FROM [{TableName}] " +
                         "WHERE [System] IS NOT NULL AND TRIM([System]) <> '' ORDER BY [System]";
            using var cmd = Cmd(sql, cn);
            using var rd = cmd.ExecuteReader();
            while (rd.Read() && !rd.IsDBNull(0))
            {
                string s = rd.GetString(0).Trim();
                if (s.Length > 0) list.Add(s);
            }
            return list;
        }

        /// <summary>Количество записей (F3.4).</summary>
        public int Count()
        {
            using var scope = Scope(); var cn = scope.Cn;
            using var cmd = Cmd($"SELECT COUNT(*) FROM [{TableName}]", cn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // ================== Запись (F4) и аудит (F7) ==================

        /// <summary>
        /// Писать изменения в IO_LIST_Audit (галочка «Импорт с аудитом», F2.7).
        /// false — Insert/Update/Clear не создают записей истории.
        /// </summary>
        public bool WriteAudit { get; set; } = true;

        /// <summary>Значение поля в строковом виде для журнала аудита.</summary>
        private static string? ToAuditString(object? v) =>
            v == null || v is DBNull ? null : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Служебные поля: даты внесения/изменения проставляются программой (Б8, п.4/9).</summary>
        private static void Prepare(IoPoint p, bool isInsert)
        {
            p.Revision ??= new Revision();
            if (isInsert && p.Revision.DateEntered == null)
                p.Revision.DateEntered = DateTime.Now;
            p.Revision.DateModified = DateTime.Now;
        }

        /// <summary>Чтение записи по ID (для сравнения «старое → новое»).</summary>
        private IoPoint? GetById(OleDbConnection cn, long id)
        {
            string sql = $"SELECT {ColList} FROM [{TableName}] WHERE [ID] = ?";
            using var cmd = Cmd(sql, cn);
            OleDbArgs.Add(cmd, id);
            using var rd = cmd.ExecuteReader();
            return rd.Read() ? ReadPoint(rd) : null;
        }

        /// <summary>Добавить параметры всех полей модели в команду (порядок = FieldRegistry).</summary>
        private static void AddFieldParameters(OleDbCommand cmd, IoPoint p)
        {
            foreach (var f in FieldRegistry.Fields)
                OleDbArgs.Add(cmd, f.Get(p));
        }

        /// <summary>Изменённые поля данных (без авто-колонок, F2.8): старое/новое значение.</summary>
        private static List<(FieldDef Field, string? Old, string? New)> ComputeDataChanges(
            IoPoint oldP, IoPoint newP, IReadOnlySet<string>? mappedColumns = null)
        {
            var changes = new List<(FieldDef Field, string? Old, string? New)>();
            foreach (var f in FieldRegistry.Fields)
            {
                // служебные колонки не инициируют запись и не попадают в аудит
                if (Schema.AutoColumns.Contains(f.Column)) continue;
                if (mappedColumns != null && !mappedColumns.Contains(f.Column)) continue; // F2.10: немаппленные колонки Excel не трогаем
                string? o = ToAuditString(f.Get(oldP));
                string? n = ToAuditString(f.Get(newP));
                if (!string.Equals(o, n, StringComparison.Ordinal))
                    changes.Add((f, o, n));
            }
            return changes;
        }

        /// <summary>
        /// Частичный UPDATE: только изменившиеся колонки + служебные (PTag_*/REV_*,
        /// кроме REV_DateEntered — дата внесения сохраняется, прежнее затирание исправлено).
        /// </summary>
        private void UpdatePartial(IoPoint oldP, IoPoint p,
            List<(FieldDef Field, string? Old, string? New)> changes)
        {
            p.Revision ??= new Revision();
            p.Revision.DateEntered ??= oldP.Revision?.DateEntered;
            p.Revision.DateModified = DateTime.Now;

            var columns = changes.Select(c => c.Field.Column)
                .Union(Schema.AutoColumns.Where(c => c != "REV_DateEntered"))
                .ToList();

            using var scope = Scope(); var cn = scope.Cn;
            bool ownTr = _sessionTr == null;
            using var trOwn = ownTr ? cn.BeginTransaction() : null;
            var tr = trOwn ?? _sessionTr!;
            try
            {
                string sets = string.Join(", ", columns.Select(c => $"[{c}] = ?"));
                string sql = $"UPDATE [{TableName}] SET {sets} WHERE [ID] = ?";
                using (var cmd = new OleDbCommand(sql, cn, tr))
                {
                    foreach (var col in columns)
                        OleDbArgs.Add(cmd, FieldRegistry.GetValue(p, col));
                    OleDbArgs.Add(cmd, p.Id);
                    cmd.ExecuteNonQuery();
                }
                if (ownTr) tr.Commit();
            }
            catch (Exception ex)
            {
                if (ownTr) tr.Rollback();
                _log.Error($"Ошибка частичного обновления ID={p.Id}: {ex.Message}");
                throw;
            }
        }

        /// <summary>Вставка записи; возвращает ID. Аудит: INSERT по всем заполненным полям.</summary>
        public long Insert(IoPoint p)
        {
            Prepare(p, isInsert: true);
            using var scope = Scope(); var cn = scope.Cn;
            bool ownTr = _sessionTr == null;
            using var trOwn = ownTr ? cn.BeginTransaction() : null;
            var tr = trOwn ?? _sessionTr!;
            long id;
            try
            {
                string cols = string.Join(", ", FieldRegistry.Columns.Select(c => $"[{c}]"));
                string sql = $"INSERT INTO [{TableName}] ({cols}) VALUES ({ParamList})";
                using (var cmd = new OleDbCommand(sql, cn, tr))
                {
                    AddFieldParameters(cmd, p);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new OleDbCommand("SELECT @@identity", cn, tr))
                    id = Convert.ToInt64(cmd.ExecuteScalar());
                if (ownTr) tr.Commit();
            }
            catch (Exception ex)
            {
                if (ownTr) tr.Rollback();
                _log.Error($"Ошибка вставки Tag={p.Tag}: {ex.Message}");
                throw;
            }

            p.Id = id;
            if (WriteAudit)
            {
                foreach (var f in FieldRegistry.Fields)
                {
                    var v = f.Get(p);
                    if (v != null)
                        _audit.Record(TableName, p.Tag, AuditAction.Insert, f.Column, null, ToAuditString(v),
                            p.Revision?.FileName, p.Identification.System, p.Revision?.Sheet); // F7.6/F7.7: документ (файл+вкладка) + система
                }
            }
            LogRowInfo($"Добавлена запись Tag={p.Tag} (ID={id})");
            return id;
        }

        /// <summary>Обновление по ID: только изменённые поля (F2.8); без изменений — без записи и аудита.
        /// Возвращает число изменённых полей (0 — без изменений).</summary>
        public int Update(IoPoint p)
        {
            IoPoint? old;
            using (var sc = Scope()) old = GetById(sc.Cn, p.Id);
            if (old == null) throw new InvalidOperationException($"Запись ID={p.Id} не найдена");

            var changes = ComputeDataChanges(old, p);
            if (changes.Count == 0)
            {
                LogRowInfo($"Без изменений (редактирование): ID={p.Id}");
                return 0;
            }

            UpdatePartial(old, p, changes);

            if (WriteAudit)
                foreach (var (f, o, n) in changes)
                    _audit.Record(TableName, p.Tag ?? old.Tag, AuditAction.Update, f.Column, o, n,
                        p.Revision?.FileName ?? old.Revision?.FileName,
                        p.Identification.System ?? old.Identification.System,
                        p.Revision?.Sheet ?? old.Revision?.Sheet); // F7.6/F7.7: документ (файл+вкладка) + система
            LogRowInfo($"Изменена запись Tag={p.Tag} (ID={p.Id}), полей: {changes.Count}");
            return changes.Count;
        }

        /// <summary>Удаление по ID. Аудит: DELETE (старые значения, новые — NULL).</summary>
        public void Delete(long id)
        {
            using var scope = Scope(); var cn = scope.Cn;
            var old = GetById(cn, id);
            if (old == null) return;

            bool ownTr = _sessionTr == null;
            using var trOwn = ownTr ? cn.BeginTransaction() : null;
            var tr = trOwn ?? _sessionTr!;
            try
            {
                using var cmd = new OleDbCommand($"DELETE FROM [{TableName}] WHERE [ID] = ?", cn, tr);
                OleDbArgs.Add(cmd, id);
                cmd.ExecuteNonQuery();
                if (ownTr) tr.Commit();
            }
            catch (Exception ex)
            {
                if (ownTr) tr.Rollback();
                _log.Error($"Ошибка удаления ID={id}: {ex.Message}");
                throw;
            }

            if (WriteAudit)
            {
                foreach (var f in FieldRegistry.Fields)
                {
                    var v = f.Get(old);
                    if (v != null)
                        _audit.Record(TableName, old.Tag, AuditAction.Delete, f.Column, ToAuditString(v), null,
                            old.Revision?.FileName, old.Identification.System, old.Revision?.Sheet); // F7.6/F7.7: документ + система
                }
            }
            LogRowInfo($"Удалена запись Tag={old.Tag} (ID={id})");
        }

        /// <summary>
        /// Upsert по Tag (F2.7/F2.8): вставка / идентичность System+Plant /
        /// без изменений (ни UPDATE, ни аудита) / частичное обновление + аудит только по ним.
        /// </summary>
        public UpsertResult Upsert(IoPoint p, IReadOnlySet<string>? mappedColumns = null)
        {
            var existing = GetByTag(p.Tag ?? "");
            if (existing == null)
            {
                Insert(p);
                return new UpsertResult { Outcome = UpsertOutcome.Inserted };
            }

            // ===== Правило идентичности: System + Plant (F2.8) =====
            string sysNew = (p.Identification.System ?? "").Trim();
            string sysOld = (existing.Identification.System ?? "").Trim();
            string plNew = (p.Identification.Plant ?? "").Trim();
            string plOld = (existing.Identification.Plant ?? "").Trim();

            // пустое входное значение = проверка пропускается (W003), конфликт только при явном различии
            bool notChecked = (sysNew.Length == 0 && sysOld.Length > 0) ||
                              (plNew.Length == 0 && plOld.Length > 0);

            string? conflict = null;
            if (sysNew.Length > 0 && sysOld.Length > 0 &&
                !string.Equals(sysNew, sysOld, StringComparison.OrdinalIgnoreCase))
                conflict = "System";
            else if (plNew.Length > 0 && plOld.Length > 0 &&
                     !string.Equals(plNew, plOld, StringComparison.OrdinalIgnoreCase))
                conflict = "Plant";

            if (conflict != null)
            {
                string oldId = $"System={sysOld}; Plant={plOld}";
                string newId = $"System={sysNew}; Plant={plNew}";
                if (WriteAudit)
                    _audit.Record(TableName, p.Tag, AuditAction.CriticalError,
                        "System/Plant", oldId, newId,
                        p.Revision?.FileName, p.Identification.System, p.Revision?.Sheet); // F7.6/F7.7: документ + система
                _log.Error($"КРИТИЧЕСКАЯ ОШИБКА: Tag={p.Tag} — в БД [{oldId}] ≠ импорт [{newId}]; строка игнорирована");
                return new UpsertResult
                {
                    Outcome = UpsertOutcome.Conflict,
                    ConflictField = conflict,
                    OldIdentity = oldId,
                    NewIdentity = newId,
                    IdentityNotChecked = notChecked,
                    ExistingFileName = existing.Revision?.FileName, // F9.1: документ записи в БД
                    ExistingSheet = existing.Revision?.Sheet
                };
            }

            // ===== Сравнение данных =====
            p.Id = existing.Id;
            var changes = ComputeDataChanges(existing, p, mappedColumns);
            if (changes.Count == 0)
            {
                // F2.8: данных не изменилось — ни UPDATE, ни записи в аудит
                LogRowInfo($"Без изменений: Tag={p.Tag}");
                return new UpsertResult
                {
                    Outcome = UpsertOutcome.Unchanged,
                    IdentityNotChecked = notChecked,
                    ExistingFileName = existing.Revision?.FileName, // F9.1
                    ExistingSheet = existing.Revision?.Sheet
                };
            }

            UpdatePartial(existing, p, changes);
            if (WriteAudit)
                foreach (var (f, o, n) in changes)
                    _audit.Record(TableName, p.Tag, AuditAction.Update, f.Column, o, n,
                        p.Revision?.FileName ?? existing.Revision?.FileName,
                        p.Identification.System ?? existing.Identification.System,
                        p.Revision?.Sheet ?? existing.Revision?.Sheet); // F7.6/F7.7: документ + система
            LogRowInfo($"Обновлено полей: {changes.Count} — Tag={p.Tag}");
            return new UpsertResult
            {
                Outcome = UpsertOutcome.Updated,
                IdentityNotChecked = notChecked,
                ExistingFileName = existing.Revision?.FileName, // F9.1
                ExistingSheet = existing.Revision?.Sheet
            };
        }

        /// <summary>Очистка таблицы (режим «Заменить», F2.7). Аудит: одна запись о массовом удалении.</summary>
        public void Clear()
        {
            int count = Count();
            using var scope = Scope(); var cn = scope.Cn;
            using var cmd = Cmd($"DELETE FROM [{TableName}]", cn);
            cmd.ExecuteNonQuery();
            if (WriteAudit)
                _audit.Record(TableName, null, AuditAction.Delete, "(все записи)", count.ToString(), null);
            _log.Warn($"Таблица [{TableName}] очищена: удалено записей — {count}");
        }

        // ===== F2.11/F2.16: замена документа (документ = файл + вкладка) =====

        /// <summary>Список документов таблицы (DISTINCT пары непустой REV_FileName + REV_Sheet)
        /// — для диалога «взамен какого». Старые записи без вкладки — Sheet = null.</summary>
        public List<(string FileName, string? Sheet)> GetDocuments()
        {
            var list = new List<(string, string?)>();
            using var scope = Scope(); var cn = scope.Cn;
            string sql = $@"SELECT DISTINCT [REV_FileName], [REV_Sheet] FROM [{TableName}]
                WHERE [REV_FileName] IS NOT NULL AND [REV_FileName] <> ''
                ORDER BY [REV_FileName], [REV_Sheet]";
            using var cmd = Cmd(sql, cn);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var v = rd[0]?.ToString();
                if (string.IsNullOrWhiteSpace(v)) continue;
                string? sheet = rd.IsDBNull(1) ? null : rd[1]?.ToString();
                if (string.IsNullOrWhiteSpace(sheet)) sheet = null; // старые записи — «без вкладки»
                list.Add((v!, sheet));
            }
            return list;
        }

        /// <summary>F2.11/F2.16/F2.17: замена документов (мультивыбор — ревизии/вкладки): теги каждого
        /// документа из replacedDocs, отсутствующие в tagsInFile (новая вкладка), помечаются в
        /// REV_Description (MarkedDelete, красным в аудите), физически НЕ удаляются; выжившие
        /// переносятся на новый документ (file + sheet) с OldValue = реальный прежний документ записи.
        /// Записи, уже принадлежащие новому документу, не переносятся (идемпотентность).
        /// См. IIOListRepository.</summary>
        public List<string> MarkMissingTagsForDeletion(IReadOnlySet<string> tagsInFile,
            IReadOnlyList<(string FileName, string? Sheet)> replacedDocs, string newFileName, string? newSheet)
        {
            var marked = new List<string>();
            if (replacedDocs.Count == 0) return marked;

            string marker = $"Удаляются, отсутствуют в последней версии файла {newFileName}";
            string? destSheet = string.IsNullOrWhiteSpace(newSheet) ? null : newSheet!.Trim();

            static string? NormSheet(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

            // Нормализация списка документов-заменителей (F2.17): дедупликация пар «файл + вкладка»
            var docs = replacedDocs
                .Select(d => (FileName: d.FileName.Trim(), Sheet: NormSheet(d.Sheet)))
                .Where(d => d.FileName.Length > 0)
                .Distinct()
                .ToList();
            if (docs.Count == 0) return marked;

            // WHERE по всем выбранным парам: OR-условия с параметрами OleDb (F2.17);
            // документ без вкладки (старые записи) матчится с NULL/пустым REV_Sheet
            string where = string.Join(" OR ", docs.Select(d => d.Sheet == null
                ? "([REV_FileName] = ? AND ([REV_Sheet] IS NULL OR [REV_Sheet] = ''))"
                : "([REV_FileName] = ? AND [REV_Sheet] = ?)"));

            using var scope = Scope(); var cn = scope.Cn;
            bool ownTr = _sessionTr == null;
            using var trOwn = ownTr ? cn.BeginTransaction() : null;
            var tr = trOwn ?? _sessionTr!;

            try
            {
                // Строки документов-заменителей: SELECT по OR-парам (F2.17); в выборку входит
                // и прежний документ каждой записи (REV_FileName/REV_Sheet) — для аудита OldValue
                var missing = new List<(long Id, string Tag, string? OldDesc, string? System, string? OwnFile, string? OwnSheet)>();
                var survivors = new List<(long Id, string Tag, string? System, string? OwnFile, string? OwnSheet)>();
                using (var cmd = new OleDbCommand(
                    $@"SELECT [ID], [Tag], [REV_Description], [System], [REV_FileName], [REV_Sheet] FROM [{TableName}]
                        WHERE {where}", cn, tr))
                {
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    foreach (var d in docs)
                    {
                        OleDbArgs.Add(cmd, d.FileName);
                        if (d.Sheet != null) OleDbArgs.Add(cmd, d.Sheet);
                    }
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                    {
                        long id = Convert.ToInt64(rd[0]);
                        string tag = rd[1]?.ToString() ?? "";
                        string? oldDesc = rd.IsDBNull(2) ? null : rd[2]?.ToString();
                        string? sys = rd.IsDBNull(3) ? null : rd[3]?.ToString(); // F7.6: система записи
                        string? ownFile = rd.IsDBNull(4) ? null : rd[4]?.ToString(); // прежний документ записи (F2.17)
                        string? ownSheet = rd.IsDBNull(5) ? null : rd[5]?.ToString();
                        if (tagsInFile.Contains(tag)) survivors.Add((id, tag, sys, ownFile, ownSheet));
                        else missing.Add((id, tag, oldDesc, sys, ownFile, ownSheet));
                    }
                }

                var now = DateTime.Now;

                // Отсутствуют в новой версии — пометка к удалению (без физического DELETE);
                // уже помеченный ранее тег не трогаем (идемпотентность повторной замены)
                foreach (var (id, tag, oldDesc, sys, _, _) in missing)
                {
                    if (string.Equals(oldDesc?.Trim(), marker, StringComparison.Ordinal)) continue;
                    using (var cmd = new OleDbCommand(
                        $"UPDATE [{TableName}] SET [REV_Description] = ?, [REV_DateModified] = ? WHERE [ID] = ?", cn, tr))
                    {
                        cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                        OleDbArgs.Add(cmd, marker);
                        OleDbArgs.Add(cmd, now);
                        OleDbArgs.Add(cmd, id);
                        cmd.ExecuteNonQuery();
                    }
                    if (WriteAudit)
                        _audit.Record(TableName, tag, AuditAction.MarkedDelete, "REV_Description", oldDesc, marker,
                            newFileName, sys, destSheet); // F7.6/F7.7: инициирующий документ — новый файл+вкладка
                    marked.Add(tag);
                }

                // Выжившие теги документов переносятся на новый документ (корректная линовка версий);
                // записи уже нового документа не трогаются — повторный импорт идемпотентен (F2.16)
                foreach (var (id, tag, sys, ownFile, ownSheet) in survivors)
                {
                    bool alreadyNewDoc = string.Equals(ownFile ?? "", newFileName, StringComparison.OrdinalIgnoreCase) &&
                                         string.Equals(ownSheet ?? "", destSheet ?? "", StringComparison.OrdinalIgnoreCase);
                    if (alreadyNewDoc) continue;

                    using (var cmd = new OleDbCommand(
                        $"UPDATE [{TableName}] SET [REV_FileName] = ?, [REV_Sheet] = ?, [REV_DateModified] = ? WHERE [ID] = ?", cn, tr))
                    {
                        cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                        OleDbArgs.Add(cmd, newFileName);
                        OleDbArgs.Add(cmd, destSheet);
                        OleDbArgs.Add(cmd, now);
                        OleDbArgs.Add(cmd, id);
                        cmd.ExecuteNonQuery();
                    }
                    if (WriteAudit)
                    {
                        // F2.17: OldValue — реальный прежний документ записи (в мульти-замене записи
                        // приходят из разных документов, а не из одного «replacedFileName»)
                        _audit.Record(TableName, tag, AuditAction.Update, "REV_FileName", ownFile, newFileName,
                            newFileName, sys, destSheet); // F7.6/F7.7: инициирующий документ — новый файл+вкладка
                        // F2.16: смена вкладки документа у выжившей записи — отдельная строка аудита
                        if (!string.Equals(ownSheet ?? "", destSheet ?? "", StringComparison.OrdinalIgnoreCase))
                            _audit.Record(TableName, tag, AuditAction.Update, "REV_Sheet", ownSheet, destSheet,
                                newFileName, sys, destSheet);
                    }
                }

                if (ownTr) tr.Commit();
            }
            catch (Exception ex)
            {
                if (ownTr) tr.Rollback();
                _log.Error($"Ошибка пометки на удаление (замена документов {DescribeDocs(docs)}): {ex.Message}");
                throw;
            }

            if (marked.Count > 0)
                _log.Warn($"Замена документов ({docs.Count}): {DescribeDocs(docs)} — помечено к удалению: {marked.Count} (аудит MarkedDelete, красным)");
            return marked;

            static string DescribeDocs(List<(string FileName, string? Sheet)> docs) =>
                string.Join(", ", docs.Select(d => d.Sheet == null ? $"«{d.FileName}» (без вкладки)" : $"«{d.FileName}» :: «{d.Sheet}»"));
        }

        /// <summary>F2.17: копия строки в IO_LIST_DublicateTag (все копии повторяющихся тегов — включая первую;
        /// без аудита, без уникального индекса). Внутри сессии импорта пишется в её транзакции.</summary>
        public void InsertDuplicate(IoPoint p)
        {
            using var scope = Scope(); var cn = scope.Cn;
            bool ownTr = _sessionTr == null;
            using var trOwn = ownTr ? cn.BeginTransaction() : null;
            var tr = trOwn ?? _sessionTr!;
            try
            {
                // даты для копии: если upsert был Unchanged — проставляем явно (копия = фиксация события)
                p.Revision ??= new Revision();
                p.Revision.DateEntered ??= DateTime.Now;
                p.Revision.DateModified ??= DateTime.Now;

                string cols = string.Join(", ", FieldRegistry.Columns.Select(c => $"[{c}]"));
                string sql = $"INSERT INTO [{Schema.DuplicateTable}] ({cols}) VALUES ({ParamList})";
                using var cmd = new OleDbCommand(sql, cn, tr);
                cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                AddFieldParameters(cmd, p);
                cmd.ExecuteNonQuery();
                if (ownTr) tr.Commit();
            }
            catch (Exception ex)
            {
                if (ownTr) tr.Rollback();
                _log.Error($"Ошибка копии в [{Schema.DuplicateTable}] (Tag={p.Tag}): {ex.Message}");
                throw;
            }
        }

        /// <summary>F2.15: запись аудита TagCleaned — нормализация Tag (удаление пробелов) при импорте.</summary>
        public void RecordTagCleanedAudit(string? oldTag, string? newTag, string? fileName, string? sheet, string? system)
        {
            if (!WriteAudit) return;
            _audit.Record(TableName, newTag, AuditAction.TagCleaned, "Tag", oldTag, newTag, fileName, system, sheet);
        }

        /// <summary>F9.1: запись аудита Duplicate — задвоение тега между документами: тег уже существует
        /// в БД из другого документа (файл + вкладка), а импортируется из текущего. Равноправная
        /// запись истории (как Insert/Update), отображается жёлтым. Старый/новый документ — в
        /// OldValue/NewValue; FileName/Sheet аудита — инициирующий документ.</summary>
        public void RecordDuplicateTagAudit(string? tag, string? oldFileName, string? oldSheet,
            string? newFileName, string? newSheet, string? system)
        {
            if (!WriteAudit) return;
            string oldDoc = FormatDocument(oldFileName, oldSheet);
            string newDoc = FormatDocument(newFileName, newSheet);
            _audit.Record(TableName, tag, AuditAction.Duplicate, "Tag", oldDoc, newDoc,
                newFileName, system, newSheet);
        }

        /// <summary>Формат документа «файл :: вкладка» (как в диалоге замены F2.16).</summary>
        private static string FormatDocument(string? fileName, string? sheet)
        {
            string file = string.IsNullOrWhiteSpace(fileName) ? "(без файла)" : fileName!;
            return string.IsNullOrWhiteSpace(sheet) ? $"{file} (без вкладки)" : $"{file} :: {sheet}";
        }
    }
}
