using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IO_PJT.Data;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>F100: импорт карты адресации Modbus через общее окно импорта: чтение листов по
    /// маске и маппингу (Register Table + Interface parameters), запись в MODBUS_MAP режимом
    /// «Сравнение» (ModbusMapService.ImportMapped) либо «Заменить строки файла» (SaveToDb),
    /// аудит IO_LIST_Audit. Замена окна ModbusImportWindow (F9.2/F9.5) — окно удалено в F100.</summary>
    public sealed class ModbusImportService
    {
        /// <summary>Поля станции — читаются с листа Interface parameters (общие для строк файла);
        /// если тот же столбец привязан в маппинге Register Table — значение листа приоритетнее.</summary>
        private static readonly string[] StationFields =
            { "SlaveStation", "SlaveIdMain", "SlaveIdBackup", "Speed", "Timeout" };

        private readonly string _dbPath;
        private readonly MappingService _mapping;
        private readonly AuditService _audit;
        private readonly IEventLogger _log;

        public ModbusImportService(string dbPath, MappingService mapping, AuditService audit, IEventLogger log)
        {
            _dbPath = dbPath;
            _mapping = mapping;
            _audit = audit;
            _log = log;
        }

        /// <summary>Итог чтения одного файла карты.</summary>
        public sealed class ReadResult
        {
            /// <summary>Имя файла (MODBUS_MAP.MapFile).</summary>
            public string FileName { get; set; } = "";

            /// <summary>Slave-станция с листа Interface parameters (null — не распознана).</summary>
            public string? SlaveStation { get; set; }

            /// <summary>Прочитано строк с PlcTag.</summary>
            public int RowsRead { get; set; }

            /// <summary>Пропущено строк (пустой PlcTag / «-» / зачёркнутый PlcTag).</summary>
            public int RowsSkipped { get; set; }

            /// <summary>Листы, из которых прочитаны строки.</summary>
            public List<string> SheetsRead { get; } = new();

            /// <summary>Предупреждения (лист пропущен, Interface parameters не найден и т.п.).</summary>
            public List<string> Warnings { get; } = new();

            /// <summary>Прочитанные строки карты.</summary>
            public List<ModbusMapEntry> Entries { get; } = new();
        }

        /// <summary>Итог импорта одного файла в MODBUS_MAP.</summary>
        public sealed class ImportResult
        {
            public string FileName { get; set; } = "";
            public int RowsRead { get; set; }
            public int Inserted { get; set; }
            public int Updated { get; set; }
            public int Deleted { get; set; }

            /// <summary>true — режим «Заменить строки файла» (F9.2), false — сравнение (F100).</summary>
            public bool Replaced { get; set; }

            /// <summary>Записано строк аудита (0 — аудит выключен или изменений нет).</summary>
            public int AuditRows { get; set; }

            public List<string> SheetsRead { get; } = new();
            public List<string> Warnings { get; } = new();

            /// <summary>Человекочитаемый итог для отчёта окна импорта.</summary>
            public string Summary =>
                $"Файл «{FileName}» | листов: {SheetsRead.Count}" +
                (SheetsRead.Count > 0 ? $" ({string.Join(", ", SheetsRead)})" : "") +
                $" | строк: {RowsRead} | +{Inserted} добавлено · ~{Updated} обновлено · -{Deleted} удалено" +
                (Replaced ? " | режим: замена строк файла" : " | режим: сравнение") +
                (AuditRows > 0 ? $" | аудит: {AuditRows} записей" : " | аудит: выключен") +
                (Warnings.Count > 0 ? "\n" + string.Join("\n", Warnings.Select(w => "⚠ " + w)) : "");
        }

        /// <summary>F100: прочитать файл карты по маппингу. allSheets — все листы по маске
        /// (иначе только singleSheet); шапка — строка headerRow. Лист без колонки PlcTag в шапке
        /// пропускается с предупреждением; параметры станции берутся с листа Interface parameters
        /// (значение листа Register приоритетнее, если столбец станции привязан в маппинге).</summary>
        public ReadResult Read(string path, IReadOnlyList<ColumnMap> mapping, int headerRow,
            string? sheetMask, bool allSheets, string? singleSheet)
        {
            var fileName = Path.GetFileName(path);
            var result = new ReadResult { FileName = fileName };
            using var reader = new ExcelReader(path);
            var sheetNames = reader.GetSheetNames();

            List<string> selected;
            if (allSheets)
                selected = sheetNames.Where(s => ImportTargets.MaskMatches(sheetMask, s)).ToList();
            else if (!string.IsNullOrWhiteSpace(singleSheet) &&
                     sheetNames.Contains(singleSheet, StringComparer.OrdinalIgnoreCase))
                selected = new List<string> { singleSheet };
            else
                selected = new List<string>();

            if (selected.Count == 0)
                throw new InvalidOperationException(
                    $"Файл «{fileName}» не содержит листов по маске " +
                    $"«{(string.IsNullOrWhiteSpace(sheetMask) ? "все листы" : sheetMask)}» — это не карта адресации Modbus");

            // ===== Interface parameters: параметры станции, общие для строк файла (F9) =====
            var stationDefaults = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            string? ifaceSheet = sheetNames.FirstOrDefault(s =>
                s.Contains("Interface", StringComparison.OrdinalIgnoreCase));
            if (ifaceSheet == null)
            {
                result.Warnings.Add("лист Interface parameters не найден — параметры станции не заполнены");
            }
            else
            {
                try
                {
                    var ifaceMaps = _mapping.AutoDetect(reader.GetHeaders(ifaceSheet, 1), ImportTargets.ModbusMap);
                    var idxByField = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in ifaceMaps)
                        if (!string.IsNullOrEmpty(m.AccessColumn) && m.ExcelColumnIndex is int ix &&
                            StationFields.Contains(m.AccessColumn, StringComparer.OrdinalIgnoreCase) &&
                            !idxByField.ContainsKey(m.AccessColumn))
                            idxByField[m.AccessColumn] = ix;

                    foreach (var row in reader.ReadRows(ifaceSheet, 1))
                    {
                        // первая строка с непустой Slave-станцией (строки-пояснения ниже не берём — F9)
                        string? st = idxByField.TryGetValue("SlaveStation", out var stIx)
                            ? row.Cell(stIx) : null;
                        if (string.IsNullOrWhiteSpace(st)) continue;
                        foreach (var f in StationFields)
                            if (idxByField.TryGetValue(f, out var ix))
                                stationDefaults[f] = row.Cell(ix)?.Trim();
                        break;
                    }
                    if (stationDefaults.Count == 0)
                        result.Warnings.Add($"лист «{ifaceSheet}»: параметры станции не распознаны в шапке");
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"лист «{ifaceSheet}»: {ex.Message}");
                }
            }
            result.SlaveStation = stationDefaults.TryGetValue("SlaveStation", out var stDef) ? stDef : null;
            // ===== Листы Register Table: чтение по маппингу =====
            foreach (var sheet in selected)
            {
                IReadOnlyList<string> headers;
                IReadOnlyList<ExcelRowData> rows;
                try
                {
                    headers = reader.GetHeaders(sheet, headerRow);
                    rows = reader.ReadRows(sheet, headerRow);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"лист «{sheet}»: {ex.Message}");
                    continue;
                }

                var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Count; i++)
                    if (!string.IsNullOrWhiteSpace(headers[i]) && !headerIndex.ContainsKey(headers[i]))
                        headerIndex[headers[i]] = i;

                var resolved = new List<(string Col, int Idx)>();
                foreach (var m in mapping)
                {
                    if (string.IsNullOrWhiteSpace(m.AccessColumn) || string.IsNullOrWhiteSpace(m.ExcelColumn))
                        continue;
                    int? idx = ExcelColumns.Resolve(m, headers, headerIndex);
                    if (idx.HasValue) resolved.Add((m.AccessColumn!, idx.Value));
                }
                if (!resolved.Any(r => string.Equals(r.Col, "PlcTag", StringComparison.OrdinalIgnoreCase)))
                {
                    result.Warnings.Add(
                        $"лист «{sheet}» пропущен: колонка PlcTag не найдена в шапке (строка {headerRow})");
                    continue;
                }

                result.SheetsRead.Add(sheet);
                foreach (var row in rows)
                {
                    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    bool struckTag = false;
                    foreach (var (col, idx) in resolved)
                    {
                        // F2.12: зачёркнутая ячейка PlcTag → строка не импортируется; другая → NULL
                        if (row.IsStruck(idx) &&
                            string.Equals(col, "PlcTag", StringComparison.OrdinalIgnoreCase))
                        {
                            struckTag = true;
                            break;
                        }
                        values[col] = row.IsStruck(idx) ? null : row.Cell(idx)?.Trim();
                    }

                    values.TryGetValue("PlcTag", out var plcTag);
                    if (struckTag || string.IsNullOrWhiteSpace(plcTag) || plcTag!.Trim() == "-")
                    {
                        result.RowsSkipped++; // пустой/зачёркнутый PlcTag или строка-разделитель
                        continue;
                    }

                    var entry = new ModbusMapEntry
                    {
                        MapFile = fileName,
                        MapSheet = sheet,
                        MapRow = row.ExcelRow > 0 ? row.ExcelRow : headerRow + 1,
                        PlcTag = plcTag!.Trim()
                    };
                    foreach (var kv in values) SetField(entry, kv.Key, kv.Value);
                    // параметры станции: значение листа Register приоритетнее, иначе Interface (F9)
                    foreach (var f in StationFields)
                        if (string.IsNullOrWhiteSpace(GetField(entry, f)) &&
                            stationDefaults.TryGetValue(f, out var dv))
                            SetField(entry, f, dv);
                    entry.PlcTag = plcTag.Trim();
                    entry.ObjectTag = ModbusMapService.DeriveObjectTag(entry.PlcTag);

                    result.Entries.Add(entry);
                    result.RowsRead++;
                }
            }
            return result;
        }

        /// <summary>Значение поля строки карты по имени колонки MODBUS_MAP (F100).</summary>
        private static string? GetField(ModbusMapEntry e, string col) => col switch
        {
            "SlaveStation" => e.SlaveStation, "SlaveIdMain" => e.SlaveIdMain,
            "SlaveIdBackup" => e.SlaveIdBackup, "Speed" => e.Speed, "Timeout" => e.Timeout,
            "PrfDesignation" => e.PrfDesignation, "PlcTag" => e.PlcTag,
            "Description" => e.Description, "Units" => e.Units, "Scale" => e.Scale,
            "LL" => e.LL, "LA" => e.LA, "HA" => e.HA, "HH" => e.HH,
            "Scaling" => e.Scaling, "SigType" => e.SigType, "RegType" => e.RegType,
            "AddrBit" => e.Address, "AccessType" => e.AccessType, "DataType" => e.DataType,
            "FuncCode" => e.FuncCode, "DcsChannel" => e.DcsChannel, "DcsTag" => e.DcsTag,
            "DcsFunctions" => e.DcsFunctions, "Note" => e.Note,
            _ => null
        };

        /// <summary>Записать значение поля строки карты по имени колонки MODBUS_MAP (F100).</summary>
        private static void SetField(ModbusMapEntry e, string col, string? v)
        {
            switch (col)
            {
                case "SlaveStation": e.SlaveStation = v; break;
                case "SlaveIdMain": e.SlaveIdMain = v; break;
                case "SlaveIdBackup": e.SlaveIdBackup = v; break;
                case "Speed": e.Speed = v; break;
                case "Timeout": e.Timeout = v; break;
                case "PrfDesignation": e.PrfDesignation = v; break;
                case "PlcTag": e.PlcTag = v; break;
                case "Description": e.Description = v; break;
                case "Units": e.Units = v; break;
                case "Scale": e.Scale = v; break;
                case "LL": e.LL = v; break;
                case "LA": e.LA = v; break;
                case "HA": e.HA = v; break;
                case "HH": e.HH = v; break;
                case "Scaling": e.Scaling = v; break;
                case "SigType": e.SigType = v; break;
                case "RegType": e.RegType = v; break;
                case "AddrBit": e.Address = v; break;
                case "AccessType": e.AccessType = v; break;
                case "DataType": e.DataType = v; break;
                case "FuncCode": e.FuncCode = v; break;
                case "DcsChannel": e.DcsChannel = v; break;
                case "DcsTag": e.DcsTag = v; break;
                case "DcsFunctions": e.DcsFunctions = v; break;
                case "Note": e.Note = v; break;
            }
        }

        /// <summary>F100: импорт файла карты в MODBUS_MAP + аудит IO_LIST_Audit. Режим по
        /// умолчанию — сравнение (ImportMapped: INSERT/UPDATE/DELETE, ID и привязки сохраняются);
        /// replaceFileRows=true — прежнее поведение F9.2 (DELETE строк файла + INSERT).
        /// withAudit=false — записи аудита не формируются (F2.7-галочка).</summary>
        public ImportResult Import(string path, IReadOnlyList<ColumnMap> mapping, int headerRow,
            string? sheetMask, bool allSheets, string? singleSheet, bool replaceFileRows,
            bool withAudit, IProgress<ImportProgress>? progress = null)
        {
            progress?.Report(new ImportProgress { Phase = "Чтение Excel" });
            var read = Read(path, mapping, headerRow, sheetMask, allSheets, singleSheet);
            if (read.Entries.Count == 0)
                throw new InvalidOperationException(
                    $"В файле «{read.FileName}» нет строк с PlcTag" +
                    (read.Warnings.Count > 0 ? ":\n" + string.Join("\n", read.Warnings) : ""));

            ServiceSchema.EnsureServiceTables(_dbPath);
            var maps = new ModbusMapService(_dbPath);
            maps.EnsureTable();

            var result = new ImportResult
            {
                FileName = read.FileName,
                RowsRead = read.RowsRead,
                Replaced = replaceFileRows
            };
            result.SheetsRead.AddRange(read.SheetsRead);
            result.Warnings.AddRange(read.Warnings);

            progress?.Report(new ImportProgress
            {
                Phase = replaceFileRows ? "Замена строк файла" : "Сравнение с базой",
                Total = read.Entries.Count
            });

            // ===== запись данных =====
            List<ModbusMapAuditEntry> auditRows;
            if (replaceFileRows)
            {
                // прежнее поведение F9.2: DELETE всех строк файла + INSERT (ID/привязки теряются)
                var old = maps.LoadFromFile(read.FileName);
                maps.SaveToDb(read.Entries, read.FileName);
                result.Inserted = read.Entries.Count;
                auditRows = new List<ModbusMapAuditEntry>();
                foreach (var o in old)
                    auditRows.Add(new ModbusMapAuditEntry
                    {
                        Tag = o.PlcTag, Action = AuditAction.Delete, Field = "Строка карты",
                        OldValue = $"{o.MapSheet}, стр. {o.MapRow}", Sheet = o.MapSheet
                    });
                foreach (var e in read.Entries)
                    ModbusMapService.AddInsertAudit(e, auditRows);
            }
            else
            {
                var diff = maps.ImportMapped(read.Entries, read.FileName);
                result.Inserted = diff.Inserted;
                result.Updated = diff.Updated;
                result.Deleted = diff.Deleted;
                auditRows = diff.AuditRows;
            }

            // ===== аудит (F100): одной пачкой после коммита данных (F2.9-подход) =====
            if (withAudit && auditRows.Count > 0)
            {
                _audit.BeginBatch();
                try
                {
                    foreach (var a in auditRows)
                        _audit.Record(Schema.ModbusMapTable, a.Tag, a.Action, a.Field,
                            a.OldValue, a.NewValue, read.FileName, "Modbus", a.Sheet);
                    _audit.EndBatch();
                    result.AuditRows = auditRows.Count;
                }
                catch (Exception ex)
                {
                    // данные уже записаны — сбой аудита не должен выглядеть как сбой импорта
                    try { _audit.DiscardBatch(); } catch { /* буфер мог уже упасть */ }
                    _log.Warn($"F100: аудит не записан: {ex.Message}");
                }
            }

            string? lockWarn = ModbusMapService.TakeLockWarning();
            if (lockWarn != null) _log.Warn($"F100: {lockWarn}");

            _log.Info($"F100: карта «{read.FileName}» — листов {read.SheetsRead.Count}, строк {result.RowsRead}, " +
                      $"+{result.Inserted} ~{result.Updated} -{result.Deleted}" +
                      (replaceFileRows ? " (замена файла)" : " (сравнение)"));

            progress?.Report(new ImportProgress
            {
                Phase = "Готово",
                Processed = result.RowsRead,
                Total = result.RowsRead,
                Inserted = result.Inserted,
                Updated = result.Updated
            });
            return result;
        }
    }
}