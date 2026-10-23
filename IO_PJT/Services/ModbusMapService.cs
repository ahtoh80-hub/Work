using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using IO_PJT.Data;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// F9: разбор файлов карты адресации Modbus «IMCS TO DCS Register Table Modbus»:
    /// лист «Interface parameters» (станция, IP main/backup, скорость, таймаут) +
    /// листы «Register Table Function 02/03/06» (шапка — строка 2, данные со строки 3,
    /// колонки A..U). Результат — список ModbusMapEntry для записи в MODBUS_MAP.
    /// </summary>
    public sealed class ModbusMapService
    {
        /// <summary>F9.3: строка карты привязана (LinkState).</summary>
        public const string LinkStateLinked = "привязано";

        /// <summary>F9.3: строка карты без привязки (LinkState).</summary>
        public const string LinkStateUnlinked = "не привязано";

        /// <summary>Прочитать файл карты. При отсутствии листов Register Table — InvalidOperationException.</summary>
        public ModbusMapImportResult Read(string path, List<ModbusMapEntry> into)
        {
            var fileName = Path.GetFileName(path);
            var result = new ModbusMapImportResult { FileName = fileName };

            using var wb = new XLWorkbook(path);

            // ===== Interface parameters (станция; строки данных после шапки) =====
            string? station = null, idMain = null, idBackup = null, speed = null, timeout = null;
            var ipSheet = wb.Worksheets.FirstOrDefault(ws =>
                ws.Name.Contains("Interface", StringComparison.OrdinalIgnoreCase));
            if (ipSheet != null)
            {
                // Шапка: B=Interface type, C=Protocol type, D=Slave Station, E=Slave ID main,
                // F=Slave ID backup, G=speed, L=timeout (строка 1; данные со строки 2)
                int lastRow = ipSheet.LastRowUsed()?.RowNumber() ?? 1;
                for (int r = 2; r <= lastRow; r++)
                {
                    string? d = CellText(ipSheet.Cell(r, 4)); // Slave Station
                    if (string.IsNullOrWhiteSpace(d)) continue; // строки-пояснения ниже не берём
                    station = d;
                    idMain = CellText(ipSheet.Cell(r, 5));
                    idBackup = CellText(ipSheet.Cell(r, 6));
                    speed = CellText(ipSheet.Cell(r, 7));
                    timeout = CellText(ipSheet.Cell(r, 12));
                    break; // первая строка со станцией — основная (в файле их две: main/backup IP)
                }
                result.SlaveStation = station;
            }

            // ===== Register Table Function 02/03/06 =====
            var regSheets = wb.Worksheets
                .Where(ws => ws.Name.Contains("Register Table", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (regSheets.Count == 0)
                throw new InvalidOperationException(
                    $"Файл «{fileName}» не содержит листов Register Table — это не карта адресации Modbus");

            foreach (var ws in regSheets)
            {
                int lastRow = ws.LastRowUsed()?.RowNumber() ?? 2;
                for (int r = 3; r <= lastRow; r++) // шапка — строка 2, данные со строки 3
                {
                    string? plcTag = CellText(ws.Cell(r, 3)); // C: PLC Tag/Variable Name
                    if (string.IsNullOrWhiteSpace(plcTag)) continue;

                    string? PlcTagTrimmed = plcTag.Trim();
                    var entry = new ModbusMapEntry
                    {
                        MapFile = fileName,
                        MapSheet = ws.Name,
                        MapRow = r,
                        SlaveStation = station,
                        SlaveIdMain = idMain,
                        SlaveIdBackup = idBackup,
                        Speed = speed,
                        Timeout = timeout,
                        PrfDesignation = CellText(ws.Cell(r, 2)),          // B
                        PlcTag = PlcTagTrimmed,
                        ObjectTag = DeriveObjectTag(PlcTagTrimmed),        // F37: базовый тег объекта
                        Description = CellText(ws.Cell(r, 4)),              // D
                        Units = CellText(ws.Cell(r, 5)),                    // E
                        Scale = CellText(ws.Cell(r, 6)),                    // F
                        LL = CellText(ws.Cell(r, 7)),                       // G
                        LA = CellText(ws.Cell(r, 8)),                       // H
                        HA = CellText(ws.Cell(r, 9)),                       // I
                        HH = CellText(ws.Cell(r, 10)),                      // J
                        Scaling = CellText(ws.Cell(r, 11)),                 // K
                        SigType = CellText(ws.Cell(r, 12)),                 // L
                        RegType = CellText(ws.Cell(r, 13)),                 // M
                        Address = CellText(ws.Cell(r, 14)),                 // N
                        AccessType = CellText(ws.Cell(r, 15)),              // O
                        DataType = CellText(ws.Cell(r, 16)),                // P
                        FuncCode = CellText(ws.Cell(r, 17)),                // Q
                        DcsChannel = CellText(ws.Cell(r, 18)),              // R
                        DcsTag = CellText(ws.Cell(r, 19)),                  // S
                        DcsFunctions = CellText(ws.Cell(r, 20)),            // T
                        Note = CellText(ws.Cell(r, 21))                     // U
                    };
                    into.Add(entry);
                    result.RowsRead++;
                }
            }

            return result;
        }

        /// <summary>Текст ячейки (ClosedXML: значение + формула-текст; пусто → null).</summary>
        private static string? CellText(IXLCell cell)
        {
            string? t = cell.GetString()?.Trim();
            return string.IsNullOrEmpty(t) ? null : t;
        }

        /// <summary>F37: вычисление ObjectTag из PlcTag — иерархия «до пробела → префикс
        /// контроллера → до «_»»: «MCU.1120-P-203A.HE-JB - Oil heater junction box_MCCB…» →
        /// «MCU.1120-P-203A.HE-JB» (до первого пробела) → «1120-P-203A.HE-JB» (ведущий
        /// префикс «MCU.» срезается только когда после точки — тег «[0-9]{3,4}-») → без
        /// суффикса после «_» (историческое поведение F9). Уровень «до точки» выполняется
        /// при поиске строки карты (ModbusLinkService.FindMapRow → BaseDevice): сначала
        /// пытается связаться полное имя объекта, и только если его нет в IO_LIST — базовое
        /// устройство до точки («1120-P-203A.HE-JB» → «1120-P-203A»).</summary>
        public static string DeriveObjectTag(string? plcTag)
        {
            string s = (plcTag ?? "").Trim();
            int sp = s.IndexOfAny(new[] { ' ', '\t' });
            if (sp > 0) s = s.Substring(0, sp).Trim();
            var m = Regex.Match(s, @"^[A-Za-z][A-Za-z0-9_\-]*\.(?=[0-9]{3,4}-)");
            if (m.Success) s = s.Substring(m.Length);
            int us = s.IndexOf('_');
            if (us >= 0) s = s.Substring(0, us);
            return s.Trim();
        }

        // ================== Хранилище MODBUS_MAP (F9) ==================

        private readonly string? _dbPath;

        /// <summary>Сервис разбора карты + хранилище MODBUS_MAP (dbPath — для операций с БД).</summary>
        public ModbusMapService(string? dbPath = null) => _dbPath = dbPath;

        private string ConnStr => DbProbe.ConnStrWithTimeout(_dbPath!);

        /// <summary>Гарантия таблицы MODBUS_MAP (идемпотентно).</summary>
        public void EnsureTable()
        {
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return;
            new TableCreator(_dbPath).EnsureServiceTables();
        }

        /// <summary>Записать строки карты в MODBUS_MAP: старые строки этого файла удаляются
        /// (идемпотентный повторный импорт), новые вставляются одной транзакцией.
        /// F9-фикс: при кратковременной блокировке БД («Could not update; currently locked»)
        /// операция повторяется до 3 раз.</summary>
        public int SaveToDb(IEnumerable<ModbusMapEntry> entries, string fileName)
        {
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            var list = entries.ToList();
            return WithLockRetry(() => SaveToDbOnce(list, fileName), $"запись карты «{fileName}»");
        }

        /// <summary>Тело SaveToDb: DELETE строк файла + батч INSERT — одна транзакция.</summary>
        private int SaveToDbOnce(List<ModbusMapEntry> list, string fileName)
        {
            using var cn = new System.Data.OleDb.OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                using (var cmd = new System.Data.OleDb.OleDbCommand(
                    $"DELETE FROM [{Schema.ModbusMapTable}] WHERE [MapFile] = ?", cn, tr))
                {
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(cmd, fileName);
                    cmd.ExecuteNonQuery();
                }
                // Все имена в квадратных скобках: Note — зарезервированное слово JET
                const string cols = "[MapFile], [MapSheet], [MapRow], [SlaveStation], [SlaveIdMain], [SlaveIdBackup], " +
                    "[Speed], [Timeout], [PrfDesignation], [PlcTag], [ObjectTag], [Description], [Units], [Scale], " +
                    "[LL], [LA], [HA], [HH], [Scaling], [SigType], [RegType], [AddrBit], [AccessType], [DataType], " +
                    "[FuncCode], [DcsChannel], [DcsTag], [DcsFunctions], [Note], [LinkTag]";
                string placeholders = string.Join(", ", cols.Split(", ").Select(_ => "?"));
                using var ins = new System.Data.OleDb.OleDbCommand(
                    $"INSERT INTO [{Schema.ModbusMapTable}] ({cols}) VALUES ({placeholders})", cn, tr);
                ins.CommandTimeout = DbProbe.TimeoutSeconds;
                foreach (var e in list)
                {
                    ins.Parameters.Clear();
                    OleDbArgs.Add(ins, e.MapFile); OleDbArgs.Add(ins, e.MapSheet); OleDbArgs.Add(ins, e.MapRow);
                    OleDbArgs.Add(ins, e.SlaveStation); OleDbArgs.Add(ins, e.SlaveIdMain); OleDbArgs.Add(ins, e.SlaveIdBackup);
                    OleDbArgs.Add(ins, e.Speed); OleDbArgs.Add(ins, e.Timeout);
                    OleDbArgs.Add(ins, e.PrfDesignation); OleDbArgs.Add(ins, e.PlcTag); OleDbArgs.Add(ins, e.ObjectTag);
                    OleDbArgs.Add(ins, e.Description); OleDbArgs.Add(ins, e.Units); OleDbArgs.Add(ins, e.Scale);
                    OleDbArgs.Add(ins, e.LL); OleDbArgs.Add(ins, e.LA); OleDbArgs.Add(ins, e.HA); OleDbArgs.Add(ins, e.HH);
                    OleDbArgs.Add(ins, e.Scaling); OleDbArgs.Add(ins, e.SigType); OleDbArgs.Add(ins, e.RegType);
                    OleDbArgs.Add(ins, e.Address); OleDbArgs.Add(ins, e.AccessType); OleDbArgs.Add(ins, e.DataType);
                    OleDbArgs.Add(ins, e.FuncCode); OleDbArgs.Add(ins, e.DcsChannel); OleDbArgs.Add(ins, e.DcsTag);
                    OleDbArgs.Add(ins, e.DcsFunctions); OleDbArgs.Add(ins, e.Note); OleDbArgs.Add(ins, e.LinkTag);
                    ins.ExecuteNonQuery();
                }
                tr.Commit();
                return list.Count;
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло уже упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }

        /// <summary>F9-фикс: повтор операции при кратковременной блокировке БД («locked»/«in use»).
        /// Повторяется вся транзакция — неудачную попытку JET откатывает сам.</summary>
        private static int WithLockRetry(Func<int> op, string what, int attempts = 3)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return op();
                }
                catch (System.Data.OleDb.OleDbException ex) when (
                    attempt < attempts && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt); // 300/600 мс — ждём снятия блокировки
                    LastLockWarning = $"блокировка БД пережита повтором {attempt} ({what}): {ex.Message}";
                }
            }
        }

        /// <summary>Текст последней пережитой блокировки (null — не было); сбрасывается чтением.</summary>
        public static string? TakeLockWarning()
        {
            string? w = LastLockWarning;
            LastLockWarning = null;
            return w;
        }

        private static string? LastLockWarning;

        /// <summary>Прочитать все строки MODBUS_MAP (для связывания).</summary>
        public List<ModbusMapEntry> LoadFromDb()
        {
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            EnsureTable();
            var result = new List<ModbusMapEntry>();
            using var cn = new System.Data.OleDb.OleDbConnection(ConnStr);
            cn.Open();
            using var cmd = new System.Data.OleDb.OleDbCommand(
                "SELECT [ID], [MapFile], [MapSheet], [MapRow], [SlaveStation], [SlaveIdMain], [SlaveIdBackup], " +
                "[Speed], [Timeout], [PrfDesignation], [PlcTag], [ObjectTag], [Description], [Units], [Scale], " +
                "[LL], [LA], [HA], [HH], [Scaling], [SigType], [RegType], [AddrBit], [AccessType], [DataType], " +
                "[FuncCode], [DcsChannel], [DcsTag], [DcsFunctions], [Note], [LinkTag], [LinkState] " +
                $"FROM [{Schema.ModbusMapTable}]", cn);
            cmd.CommandTimeout = DbProbe.TimeoutSeconds;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                string? S(int i) => rd.IsDBNull(i) ? null : rd.GetString(i);
                result.Add(new ModbusMapEntry
                {
                    Id = Convert.ToInt64(rd[0]),
                    MapFile = S(1) ?? "", MapSheet = S(2) ?? "", MapRow = rd.IsDBNull(3) ? 0 : Convert.ToInt32(rd.GetValue(3)),
                    SlaveStation = S(4), SlaveIdMain = S(5), SlaveIdBackup = S(6), Speed = S(7), Timeout = S(8),
                    PrfDesignation = S(9), PlcTag = S(10), ObjectTag = S(11), Description = S(12),
                    Units = S(13), Scale = S(14), LL = S(15), LA = S(16), HA = S(17), HH = S(18),
                    Scaling = S(19), SigType = S(20), RegType = S(21), Address = S(22), AccessType = S(23),
                    DataType = S(24), FuncCode = S(25), DcsChannel = S(26), DcsTag = S(27),
                    DcsFunctions = S(28), Note = S(29), LinkTag = S(30), LinkState = S(31)
                });
            }
            // F37: ObjectTag пересчитывается из PlcTag при каждом чтении (память) —
            // старые БД с ObjectTag «до первого _» (включавшим пробелы/описание) работают
            // по новому правилу без переимпорта карты
            foreach (var e in result)
                if (!string.IsNullOrWhiteSpace(e.PlcTag))
                    e.ObjectTag = DeriveObjectTag(e.PlcTag);
            return result;
        }

        /// <summary>F9.3: сброс результатов привязки перед новым связыванием —
        /// LinkTag и LinkState обнуляются у всех строк (устаревшие связи не переживают прогон).</summary>
        public void ResetLinkResults()
        {
            if (string.IsNullOrEmpty(_dbPath)) return;
            WithLockRetry(() =>
            {
                using var cn = new System.Data.OleDb.OleDbConnection(ConnStr);
                cn.Open();
                using var cmd = new System.Data.OleDb.OleDbCommand(
                    $"UPDATE [{Schema.ModbusMapTable}] SET [LinkTag] = NULL, [LinkState] = NULL", cn);
                cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                return cmd.ExecuteNonQuery();
            }, "сброс привязок MODBUS_MAP");
        }

        /// <summary>F9-фикс: записать все результаты связывания одной транзакцией — пакет привязок
        /// (LinkTag + LinkState=«привязано» + DcsTag) и разметка остальных строк «не привязано».
        /// F9.4: DcsTag — ключ связи MODBUS_MAP ↔ IO_LIST (им пользуется кнопка
        /// «Обновить адресацию Modbus»); в памяти значение накоплено как «прежнее содержимое
        /// колонки (ручной ввод в Access) + новые теги через запятую без дублей».
        /// Вызывается ПОСЛЕ завершения сессии репозитория: в цикле связывания второе соединение
        /// не открывается, что устраняет ошибку «Could not update; currently locked» (JET 3260)
        /// при параллельной открытой транзакции по IO_LIST.</summary>
        public int ApplyLinkResults(IReadOnlyList<(long Id, string? LinkTag, string? DcsTag)> linked)
        {
            if (string.IsNullOrEmpty(_dbPath)) return 0;
            return WithLockRetry(() =>
            {
                int n = 0;
                // один Id мог попасть несколько раз (теги копятся) — пишем последнее значение,
                // оно уже содержит все накопленные LinkTag и DcsTag
                var byId = linked.GroupBy(x => x.Id).Select(g => g.Last()).ToList();
                using var cn = new System.Data.OleDb.OleDbConnection(ConnStr);
                cn.Open();
                using var tr = cn.BeginTransaction();
                try
                {
                    if (byId.Count > 0)
                    {
                        using var upd = new System.Data.OleDb.OleDbCommand(
                            $"UPDATE [{Schema.ModbusMapTable}] SET [LinkTag] = ?, [LinkState] = ?, [DcsTag] = ? WHERE [ID] = ?", cn, tr);
                        upd.CommandTimeout = DbProbe.TimeoutSeconds;
                        foreach (var (id, linkTag, dcsTag) in byId)
                        {
                            upd.Parameters.Clear();
                            OleDbArgs.Add(upd, linkTag);
                            OleDbArgs.Add(upd, LinkStateLinked);
                            OleDbArgs.Add(upd, dcsTag);
                            OleDbArgs.Add(upd, id);
                            n += upd.ExecuteNonQuery();
                        }
                    }
                    // Строки без привязки — «не привязано» (бывший MarkUnlinked — в той же транзакции)
                    using (var rest = new System.Data.OleDb.OleDbCommand(
                        $"UPDATE [{Schema.ModbusMapTable}] SET [LinkState] = ? WHERE [LinkState] IS NULL", cn, tr))
                    {
                        rest.CommandTimeout = DbProbe.TimeoutSeconds;
                        OleDbArgs.Add(rest, LinkStateUnlinked);
                        rest.ExecuteNonQuery();
                    }
                    tr.Commit();
                    return n;
                }
                catch
                {
                    try { tr.Rollback(); } catch { /* см. SaveToDbOnce */ }
                    throw;
                }
            }, "запись привязок MODBUS_MAP");
        }

        /// <summary>F9.3: PlcTag строк карты без привязки (до n) — для превью окна импорта.</summary>
        public List<string> GetUnlinkedPlcTags(int n)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(_dbPath) || n <= 0) return result;
            using var cn = new System.Data.OleDb.OleDbConnection(ConnStr);
            cn.Open();
            using var cmd = new System.Data.OleDb.OleDbCommand(
                $"SELECT TOP {n} [PlcTag] FROM [{Schema.ModbusMapTable}] " +
                $"WHERE [LinkState] = ? ORDER BY [ID]", cn);
            cmd.CommandTimeout = DbProbe.TimeoutSeconds;
            OleDbArgs.Add(cmd, LinkStateUnlinked);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                if (!rd.IsDBNull(0))
                    result.Add(rd.GetString(0));
            return result;
        }
    }
}
