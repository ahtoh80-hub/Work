using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IO_PJT.Data;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// F65: генерация экземпляров типовых алгоритмов (меню «SCADA → Генерация экземпляров
    /// типовых алгоритмов…»). Источник — строки текущей таблицы, сгруппированные по Loop
    /// (контуру); экземпляр = один контур. Привязки «сигнал шаблона → строка TYPNO»
    /// (SCADA_ALGO_BINDINGS) задают марку: формула(Mask, Suffix) + Constructor. Шаблоны —
    /// SCADA_ALGO_TEMPLATES (BufScadaPOUS с одним OnePOU), настройки — SCADA_ALGO_SETTINGS.
    /// TYPNO — таблица описаний Typno (в рабочих базах уже заполнена).</summary>
    public sealed class ScadaAlgoService
    {
        private readonly string _dbPath;

        /// <summary>F45-паттерн: схема гарантирована для инстанса — повторные Ensure пропускаются.</summary>
        private bool _ensured;

        public ScadaAlgoService(string dbPath) => _dbPath = dbPath;

        private string ConnStr => DbProbe.ConnStrWithTimeout(_dbPath);

        /// <summary>Гарантия таблиц SCADA_ALGO_* / TYPNO (идемпотентно).</summary>
        public void EnsureTables()
        {
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return;
            if (_ensured) return;
            new TableCreator(_dbPath).EnsureServiceTables();
            _ensured = true;
        }

        // ================== Настройки ==================

        /// <summary>F65: загрузить настройки из SCADA_ALGO_SETTINGS (нет таблицы/значения — default).</summary>
        public AlgoGenSettings LoadSettings()
        {
            var s = new AlgoGenSettings();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(_dbPath) && File.Exists(_dbPath))
            {
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        using var cn = new OleDbConnection(ConnStr);
                        cn.Open();
                        using var cmd = new OleDbCommand(
                            $"SELECT [Name], [Value] FROM [{Schema.ScadaAlgoSettingsTable}]", cn);
                        cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                        using var rd = cmd.ExecuteReader();
                        while (rd.Read())
                        {
                            string key = rd.IsDBNull(0) ? "" : rd.GetString(0);
                            if (key.Length > 0) values[key] = rd.IsDBNull(1) ? "" : rd.GetString(1);
                        }
                        break;
                    }
                    catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                    {
                        System.Threading.Thread.Sleep(300 * attempt);
                    }
                }
            }
            if (values.TryGetValue("InstancesPerFile", out var ipf) && int.TryParse(ipf, out int ipfv))
                s.InstancesPerFile = ipfv;
            if (values.TryGetValue("FilePattern", out var fp)) s.FilePattern = fp;
            if (values.TryGetValue("IdStride", out var istr) && int.TryParse(istr, out int istrv))
                s.IdStride = istrv;
            if (values.TryGetValue("OutDir", out var od)) s.OutDir = od;
            if (values.TryGetValue("TemplateName", out var tn)) s.TemplateName = tn;
            return s;
        }

        /// <summary>F65: сохранить настройки в SCADA_ALGO_SETTINGS (полная замена ключей).</summary>
        public void SaveSettings(AlgoGenSettings s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            EnsureTables();
            var pairs = new List<(string Key, string Value)>
            {
                ("InstancesPerFile", Math.Max(1, s.InstancesPerFile).ToString()),
                ("FilePattern", string.IsNullOrWhiteSpace(s.FilePattern) ? "{Template}_{Seq}" : s.FilePattern),
                ("IdStride", Math.Max(100, s.IdStride).ToString()),
                ("OutDir", s.OutDir ?? ""),
                ("TemplateName", s.TemplateName ?? "")
            };
            for (int attempt = 1; ; attempt++)
            {
                try { SaveSettingsOnce(pairs); return; }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private void SaveSettingsOnce(List<(string Key, string Value)> pairs)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                using (var del = new OleDbCommand(
                    $"DELETE FROM [{Schema.ScadaAlgoSettingsTable}]", cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    del.ExecuteNonQuery();
                }
                using var ins = new OleDbCommand(
                    $"INSERT INTO [{Schema.ScadaAlgoSettingsTable}] ([Name], [Value]) VALUES (?, ?)", cn, tr);
                ins.CommandTimeout = DbProbe.TimeoutSeconds;
                foreach (var (key, value) in pairs)
                {
                    ins.Parameters.Clear();
                    OleDbArgs.Add(ins, key);
                    OleDbArgs.Add(ins, value);
                    ins.ExecuteNonQuery();
                }
                tr.Commit();
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }

        // ================== Формула марки ==================

        /// <summary>F65: марка контура по строке TYPNO (формула подтверждена буквально,
        /// Loop No = колонка Loop, ведущий «_» обязателен):
        /// маска «*» → «_» + Left(Loop,4) + «_» + сегмент между первым и последним «-» +
        /// «_» + сегмент после последнего «-» + Суффикс;
        /// иначе → «_» + Left(Loop,4) + «_» + 6-й символ Loop + Маска + «_» + сегмент после
        /// последнего «-» + Суффикс. Пример (1115-PM-101B): «*» → _1115_PM_101B,
        /// ZSC → _1115_PZSC_101B.</summary>
        public static string MarkFor(string? loop, string? mask, string? suffix, string? constructor = null,
            IoPoint? firstRow = null)
        {
            string l = (loop ?? "").Trim();
            if (l.Length == 0) return "";
            string m = (mask ?? "").Trim();
            string suf = (suffix ?? "").Trim();

            int firstDash = l.IndexOf('-');
            int lastDash = l.LastIndexOf('-');
            string area = l.Length >= 4 ? l.Substring(0, 4) : l; // Left(Loop, 4)
            string tail = lastDash >= 0 && lastDash < l.Length - 1 ? l.Substring(lastDash + 1) : "";

            string mark;
            if (m == "*")
            {
                // Mid(Loop; InStr+1; InStrRev-InStr-1) — сегмент между первым и последним «-»
                string mid = firstDash >= 0 && lastDash > firstDash
                    ? l.Substring(firstDash + 1, lastDash - firstDash - 1)
                    : "";
                mark = "_" + area + (mid.Length > 0 ? "_" + mid : "") +
                       (tail.Length > 0 ? "_" + tail : "") + suf;
            }
            else
            {
                // Mid(Loop; 6; 1) — 6-й символ (1-based), т.е. первый символ второй части
                string ch = l.Length >= 6 ? l.Substring(5, 1) : "";
                mark = "_" + area + "_" + ch + m + (tail.Length > 0 ? "_" + tail : "") + suf;
            }

            // конструктор марки (как Доп.параметр F12): константа + {ИмяКолонки} первой строки
            if (!string.IsNullOrEmpty(constructor))
                mark += firstRow != null ? RenderConstructor(constructor!, firstRow)
                                         : constructor!.Trim();

            return EnsureLeadingUnderscore(mark);
        }

        /// <summary>F65: гарантия ведущего «_» у марки (принцип M_marka).</summary>
        public static string EnsureLeadingUnderscore(string? mark)
        {
            string s = (mark ?? "").Trim();
            if (s.Length == 0) return "";
            return s[0] == '_' ? s : "_" + s;
        }

        /// <summary>F65: рендер конструктора марки — структуры {ИмяКолонки} → значение точки
        /// (FieldRegistry), иначе — константа как есть (как ResolveSource/DParam F12).</summary>
        public static string RenderConstructor(string? pattern, IoPoint p)
        {
            string t = (pattern ?? "").Trim();
            if (t.Length == 0 || p == null) return "";
            return Regex.Replace(t, @"\{([^{}]+)\}", m =>
            {
                string col = m.Groups[1].Value.Trim();
                if (!FieldRegistry.HasColumn(col)) return m.Value; // не колонка — как есть
                object? v = FieldRegistry.GetValue(p, col);
                return v switch
                {
                    null => "",
                    DateTime dt => dt.ToString("yyyy-MM-dd HH:mm"),
                    IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                    _ => v.ToString() ?? ""
                };
            });
        }

        // ================== Привязки ==================

        /// <summary>F65: загрузить привязки шаблона из SCADA_ALGO_BINDINGS (IgnoreCase по Template).</summary>
        public List<AlgoBinding> LoadBindings(string? templateName)
        {
            var list = new List<AlgoBinding>();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return list;
            string tpl = (templateName ?? "").Trim();
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand(
                        $"SELECT [Template], [Signal], [Mask], [Suffix], [ObjType], [Constructor] " +
                        $"FROM [{Schema.ScadaAlgoBindingsTable}]", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                    {
                        string rowTpl = rd.IsDBNull(0) ? "" : rd.GetString(0);
                        if (!string.Equals(rowTpl.Trim(), tpl, StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(new AlgoBinding
                        {
                            Signal = rd.IsDBNull(1) ? "" : rd.GetString(1),
                            Mask = rd.IsDBNull(2) ? "" : rd.GetString(2),
                            Suffix = rd.IsDBNull(3) ? "" : rd.GetString(3),
                            ObjType = rd.IsDBNull(4) ? "" : rd.GetString(4),
                            Constructor = rd.IsDBNull(5) ? "" : rd.GetString(5)
                        });
                    }
                    break;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
            return list;
        }

        /// <summary>F65: сохранить привязки шаблона (полная замена строк этого шаблона).</summary>
        public void SaveBindings(string? templateName, IEnumerable<AlgoBinding> bindings)
        {
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return;
            EnsureTables();
            string tpl = (templateName ?? "").Trim();
            var rows = (bindings ?? Enumerable.Empty<AlgoBinding>())
                .Where(b => !string.IsNullOrWhiteSpace(b.Signal))
                .Select(b => (Signal: b.Signal.Trim(), b.Mask, b.Suffix, b.ObjType, b.Constructor))
                .ToList();
            for (int attempt = 1; ; attempt++)
            {
                try { SaveBindingsOnce(tpl, rows); return; }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private void SaveBindingsOnce(string tpl,
            List<(string Signal, string Mask, string Suffix, string ObjType, string Constructor)> rows)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                using (var del = new OleDbCommand(
                    $"DELETE FROM [{Schema.ScadaAlgoBindingsTable}] WHERE [Template] = ?", cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(del, tpl);
                    del.ExecuteNonQuery();
                }
                using var ins = new OleDbCommand(
                    $"INSERT INTO [{Schema.ScadaAlgoBindingsTable}] " +
                    "([Template], [Signal], [Mask], [Suffix], [ObjType], [Constructor]) " +
                    "VALUES (?, ?, ?, ?, ?, ?)", cn, tr);
                ins.CommandTimeout = DbProbe.TimeoutSeconds;
                foreach (var r in rows)
                {
                    ins.Parameters.Clear();
                    OleDbArgs.Add(ins, tpl);
                    OleDbArgs.Add(ins, r.Signal);
                    OleDbArgs.Add(ins, r.Mask);
                    OleDbArgs.Add(ins, r.Suffix);
                    OleDbArgs.Add(ins, r.ObjType);
                    OleDbArgs.Add(ins, r.Constructor);
                    ins.ExecuteNonQuery();
                }
                tr.Commit();
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }

        // ================== Шаблоны ==================

        /// <summary>F65: все именованные шаблоны логических схем (SCADA_ALGO_TEMPLATES, по Name).</summary>
        public List<AlgoTemplate> LoadTemplates()
        {
            var list = new List<AlgoTemplate>();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return list;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand(
                        $"SELECT [Name], [Body], [FilePath] FROM [{Schema.ScadaAlgoTemplatesTable}] ORDER BY [Name]", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                        list.Add(new AlgoTemplate
                        {
                            Name = rd.IsDBNull(0) ? "" : rd.GetString(0),
                            Body = rd.IsDBNull(1) ? "" : rd.GetString(1),
                            FilePath = rd.IsDBNull(2) ? "" : rd.GetString(2)
                        });
                    break;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
            return list;
        }

        /// <summary>F65: шаблон по имени (нет — null).</summary>
        public AlgoTemplate? LoadTemplate(string? name)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0) return null;
            return LoadTemplates().FirstOrDefault(t =>
                string.Equals(t.Name.Trim(), n, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>F65: сохранить шаблон (upsert по имени IgnoreCase, полная замена тела).</summary>
        public void SaveTemplate(AlgoTemplate t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            string n = (t.Name ?? "").Trim();
            if (n.Length == 0) throw new InvalidOperationException("F65: у шаблона пустое имя");
            EnsureTables();
            for (int attempt = 1; ; attempt++)
            {
                try { SaveTemplateOnce(n, t.Body ?? "", t.FilePath ?? ""); return; }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private void SaveTemplateOnce(string name, string body, string filePath)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                using (var del = new OleDbCommand(
                    $"DELETE FROM [{Schema.ScadaAlgoTemplatesTable}] WHERE [Name] = ?", cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(del, name);
                    del.ExecuteNonQuery();
                }
                using (var ins = new OleDbCommand(
                    $"INSERT INTO [{Schema.ScadaAlgoTemplatesTable}] ([Name], [Body], [FilePath]) VALUES (?, ?, ?)", cn, tr))
                {
                    ins.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(ins, name);
                    OleDbArgs.Add(ins, body);
                    OleDbArgs.Add(ins, filePath);
                    ins.ExecuteNonQuery();
                }
                tr.Commit();
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }

        /// <summary>F65: удалить шаблон по имени (IgnoreCase).</summary>
        public void DeleteTemplate(string? name)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0 || string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand(
                        $"DELETE FROM [{Schema.ScadaAlgoTemplatesTable}] WHERE [Name] = ?", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(cmd, n);
                    cmd.ExecuteNonQuery();
                    return;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        // ================== TYPNO и контуры ==================

        /// <summary>F65: прочитать таблицу описаний TYPNO (в рабочих базах уже заполнена;
        /// нет таблицы — пусто). Колонки кириллические, как в таблице пользователя.</summary>
        public List<AlgoTypnoRow> LoadTypno()
        {
            var list = new List<AlgoTypnoRow>();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return list;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand(
                        "SELECT [№], [TYPNO], [Маска], [Суффикс], [Тип объекта], [TYPNO описание], " +
                        $"[Шаблон], [Шаблон описание], [Подпись] FROM [{Schema.TypnoTable}]", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                        list.Add(new AlgoTypnoRow
                        {
                            No = rd.IsDBNull(0) ? "" : Convert.ToString(rd.GetValue(0)) ?? "",
                            Typno = rd.IsDBNull(1) ? "" : rd.GetString(1),
                            Mask = rd.IsDBNull(2) ? "" : Convert.ToString(rd.GetValue(2)) ?? "",
                            Suffix = rd.IsDBNull(3) ? "" : Convert.ToString(rd.GetValue(3)) ?? "",
                            ObjType = rd.IsDBNull(4) ? "" : Convert.ToString(rd.GetValue(4)) ?? "",
                            TypnoDesc = rd.IsDBNull(5) ? "" : Convert.ToString(rd.GetValue(5)) ?? "",
                            Template = rd.IsDBNull(6) ? "" : Convert.ToString(rd.GetValue(6)) ?? "",
                            TemplateDesc = rd.IsDBNull(7) ? "" : Convert.ToString(rd.GetValue(7)) ?? "",
                            Sign = rd.IsDBNull(8) ? "" : Convert.ToString(rd.GetValue(8)) ?? ""
                        });
                    break;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
            return list;
        }

        /// <summary>F65: сгруппировать строки таблицы по Loop — контур = экземпляр.
        /// Loop пуст — строка не группа; Typno — первая непустая строка группы
        /// (смешанный Typno — предупреждение в result.Warnings); FirstRow — первая
        /// строка группы (источник колонок для конструктора марки).</summary>
        public static (List<AlgoLoopInfo> Loops, List<string> Warnings) LoadLoops(IReadOnlyList<IoPoint> points)
        {
            var loops = new List<AlgoLoopInfo>();
            var warnings = new List<string>();
            if (points == null) return (loops, warnings);

            foreach (var g in points
                         .Where(p => !string.IsNullOrWhiteSpace(p?.Identification?.Loop))
                         .GroupBy(p => (p!.Identification?.Loop ?? "").Trim(), StringComparer.OrdinalIgnoreCase))
            {
                string typno = "";
                bool mixed = false;
                foreach (var p in g)
                {
                    string t = (p.Identification.Typno ?? "").Trim();
                    if (t.Length == 0) continue;
                    if (typno.Length == 0) typno = t;
                    else if (!string.Equals(typno, t, StringComparison.OrdinalIgnoreCase))
                    {
                        mixed = true;
                        break;
                    }
                }
                if (mixed)
                    warnings.Add($"F65: контур {g.Key}: в группе несколько Typno — взят первый («{typno}»)");
                loops.Add(new AlgoLoopInfo
                {
                    Loop = g.Key,
                    Typno = typno,
                    TagCount = g.Count(),
                    FirstRow = g.First(),
                    Generate = typno.Length > 0 // без Typno — контур не генерируется
                });
            }
            return (loops, warnings);
        }
    }
}
