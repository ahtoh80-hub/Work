using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using IO_PJT.Data;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// F12: генерация перечня «Тех.объекты» для импорта в SCADA (меню «SCADA → Генерация
    /// перечня Modbus (Тех.объекты)»). Источник — строки текущей таблицы с заполненной
    /// адресацией Modbus (M_PlcTag); формат выходного файла — образец «ТехОбъекты_простой.xls»
    /// (шапка 4 строки: группы «Cлужебные»/«ОБЩЕЕ СВ-ВО» → русские заголовки → имена колонок
    /// MARKA/OBJTYPE/…, данные с 5-й строки, 20 колонок A..T), спецификация —
    /// «Генерация технологических объектов Modbus.md». Маппинг типов — SCADA_TYPE_MAP (сид
    /// по 2.5.4), настройки — SCADA_TEHOBJ_SETTINGS.</summary>
    public sealed class ScadaTechObjectService
    {
        /// <summary>Максимальная длина Доп.параметра (спецификация: «не более 50 символов»).</summary>
        public const int DParamMaxLength = 50;

        private readonly string _dbPath;

        public ScadaTechObjectService(string dbPath) => _dbPath = dbPath;

        private string ConnStr => DbProbe.ConnStrWithTimeout(_dbPath);

        /// <summary>Гарантия таблиц SCADA_TYPE_MAP / SCADA_TEHOBJ_SETTINGS (идемпотентно).</summary>
        public void EnsureTables()
        {
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return;
            new TableCreator(_dbPath).EnsureServiceTables();
        }

        // ================== Построение строк ==================

        /// <summary>Преобразовать значение поля в Марку (F12): тире → «_», ведущий «_» обязателен.
        /// «1115-MHS-00101A» → «_1115_MHS_00101A»; уже начинающееся с «_» — не трогаем.</summary>
        public static string MakeMarka(string? value)
        {
            string s = (value ?? "").Trim().Replace('-', '_');
            if (s.Length == 0) return "";
            return s[0] == '_' ? s : "_" + s;
        }

        /// <summary>Резолв поля-источника: имя колонки из FieldRegistry → значение точки;
        /// иначе (нет в реестре) — константа как есть («поле или менять их вручную»).</summary>
        public static string ResolveSource(IoPoint p, string? fieldOrConstant)
        {
            string f = (fieldOrConstant ?? "").Trim();
            if (f.Length == 0) return "";
            if (!FieldRegistry.HasColumn(f)) return f; // константа
            object? v = FieldRegistry.GetValue(p, f);
            return v switch
            {
                null => "",
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm"),
                IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => v.ToString() ?? ""
            };
        }

        /// <summary>Соответствие типа данных: M_DataType → SCADA-тип (SCADA_TYPE_MAP, IgnoreCase);
        /// без соответствия — исходный тип (result.UnmappedTypes++).</summary>
        public static string MapDataType(string? source, IReadOnlyList<ScadaTypeMapping> map, ScadaBuildResult? result)
        {
            string s = (source ?? "").Trim();
            if (s.Length == 0) return "";
            foreach (var m in map)
                if (string.Equals(m.Source?.Trim(), s, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(m.Target))
                    return m.Target.Trim();
            if (result != null) result.UnmappedTypes++;
            return s;
        }

        /// <summary>Составное значение или простая ссылка: если в шаблоне есть «{» — подстановки
        /// {ИмяКолонки} из реестра полей точки (как у Доп.параметра), иначе — прежнее поведение
        /// (поле IO List, если имя есть в FieldRegistry, иначе текст-константа). Литералы между
        /// плейсхолдерами сохраняются. Неизвестная колонка → предупреждение и литерал как есть.
        /// Обрезка по длиме — только у Доп.параметра (RenderDParam).</summary>
        public static string RenderValue(IoPoint p, string? template, ScadaBuildResult? result, string label)
        {
            string t = template ?? "";
            if (t.IndexOf('{') < 0) return ResolveSource(p, t);
            return Regex.Replace(t, @"\{([^{}]+)\}", m =>
            {
                string col = m.Groups[1].Value.Trim();
                if (!FieldRegistry.HasColumn(col))
                {
                    result?.Warnings.Add($"«{label}»: неизвестная колонка «{col}» в структуре");
                    return m.Value;
                }
                return ResolveSource(p, col);
            });
        }

        /// <summary>Доп.параметр: структура {ИмяКолонки} (см. RenderValue), затем обрезка до 50 символов.</summary>
        public static string RenderDParam(string? template, IoPoint p, ScadaBuildResult? result)
        {
            string rendered = RenderValue(p, template, result, "Доп.параметр");
            if (rendered.Length > DParamMaxLength)
            {
                if (result != null) result.TruncatedDParam++;
                rendered = rendered[..DParamMaxLength];
            }
            return rendered;
        }

        /// <summary>Построить строки перечня. Отбор: только точки с заполненной адресацией
        /// Modbus (M_PlcTag); один M_PlcTag через запятую → берётся первый (предупреждение).</summary>
        public ScadaBuildResult BuildRows(IEnumerable<IoPoint> points, ScadaGenSettings settings,
            IReadOnlyList<ScadaTypeMapping> typeMap)
        {
            var result = new ScadaBuildResult();
            foreach (var p in points)
            {
                string plcTag = (p.Modbus?.PlcTag ?? "").Trim();
                if (plcTag.Length == 0)
                {
                    result.SkippedNoAddressing++;
                    continue;
                }
                result.WithAddressing++;

                if (plcTag.Contains(','))
                    result.Warnings.Add($"«{p.Tag}»: в M_PlcTag несколько регистров — взят первый ({plcTag.Split(',')[0].Trim()})");

                string marka = MakeMarka(RenderValue(p, settings.MarkaField, result, "Марка"));
                if (marka.Length <= 1)
                    result.Warnings.Add($"«{p.Tag}»: Марка пуста (поле-источник «{settings.MarkaField}»)");

                string objDParam = RenderDParam(settings.DParamTemplate, p, result);

                result.Rows.Add(new ScadaTechObjectRow
                {
                    SourceTag = p.Tag ?? "", // F27: ключ IO_LIST для записи M_Marka
                    Mode = RenderValue(p, settings.Section, result, "Раздел"),
                    Marka = marka,
                    ObjType = MapDataType(p.Modbus?.DataType, typeMap, result),
                    Name = RenderValue(p, settings.NameField, result, "Наименование"),
                    Disc = RenderValue(p, settings.DiscField, result, "Описание"),
                    ArhPer = RenderValue(p, settings.ArhPer, result, "Период архив"),
                    Kks = RenderValue(p, settings.KksField, result, "KKS"),
                    ObjDParam = objDParam,
                    SrezControl = RenderValue(p, settings.SrezControl, result, "Маска срезов"),
                    UserGroup = RenderValue(p, settings.UserGroup, result, "Классификатор"),
                    EvGroup = RenderValue(p, settings.EvGroup, result, "Группа событий"),
                    PlcName = RenderValue(p, settings.ControllerField, result, "КОНТРОЛЛЕР"),
                    PlcAdress = RenderValue(p, settings.PlcAdress, result, "Адрес"),
                    PlcGr = RenderValue(p, settings.PlcGr, result, "Ресурс/группа"),
                    Template = RenderValue(p, settings.Template, result, "Шаблон")
                });
            }
            return result;
        }

        // ================== Экспорт ==================

        /// <summary>Шапка шаблона: русские заголовки (строка 3) и имена колонок (строка 4);
        /// №/Статус в шаблоне подписаны «-» (не заполняются).</summary>
        public static readonly string[] RussianHeaders =
        {
            "№", "Статус", "Раздел", "Марка", "Тип объекта", "Наименование", "Описание",
            "Подпись", "Номер", "PLC переменная", "Период архив", "KKS", "Доп.параметр",
            "Маска упр. в срезах", "Классификатор", "Группа событий", "КОНТРОЛЛЕР",
            "Адрес", "№ ресурса или группа", "Шаблон"
        };

        /// <summary>Имена колонок SCADA (строка 4 шаблона).</summary>
        public static readonly string[] FieldNames =
        {
            "-", "-", "Mode", "MARKA", "OBJTYPE", "NAME", "DISC", "OBJSIGN", "OBJNUMBER",
            "PLC_VARNAME", "ARH_PER", "KKS", "OBJDPARAM", "SREZCONTROL", "USERGROUP",
            "EVGROUP", "PLCNAME", "PLC_ADRESS", "PLC_GR", "TEMPLATE"
        };

        private static string[] RowValues(ScadaTechObjectRow r) => new[]
        {
            r.No, r.Status, r.Mode, r.Marka, r.ObjType, r.Name, r.Disc, r.ObjSign,
            r.ObjNumber, r.PlcVarName, r.ArhPer, r.Kks, r.ObjDParam, r.SrezControl,
            r.UserGroup, r.EvGroup, r.PlcName, r.PlcAdress, r.PlcGr, r.Template
        };

        /// <summary>Записать перечень в Excel (.xlsx, ClosedXML) в формате образца:
        /// строка 1 — объединённые группы (A1:B1 «Cлужебные», C1:T1 «ОБЩЕЕ СВ-ВО»),
        /// строка 2 — группы по каждой колонке (как в образце), строка 3 — русские
        /// заголовки, строка 4 — имена колонок, данные с 5-й строки.</summary>
        public void Export(IEnumerable<ScadaTechObjectRow> rows, string filePath)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("TEHOBJ");

            // строка 1: группы (объединены, как в образце)
            ws.Cell(1, 1).Value = "Cлужебные";
            ws.Range(1, 1, 1, 2).Merge();
            ws.Cell(1, 3).Value = "ОБЩЕЕ СВ-ВО";
            ws.Range(1, 3, 1, 20).Merge();
            // строка 2: значение группы в каждой колонке (образец)
            for (int c = 0; c < 20; c++)
                ws.Cell(2, c + 1).Value = c < 2 ? "Cлужебные" : "ОБЩЕЕ СВ-ВО";
            // строки 3–4: русские заголовки + имена колонок
            for (int c = 0; c < 20; c++)
            {
                ws.Cell(3, c + 1).Value = RussianHeaders[c];
                ws.Cell(4, c + 1).Value = FieldNames[c];
            }
            ws.Range(1, 1, 2, 20).Style.Font.Bold = true;
            ws.Range(3, 1, 4, 20).Style.Font.Bold = true;
            ws.Range(1, 1, 4, 20).Style.Fill.BackgroundColor = XLColor.FromHtml("#EFF3F8");

            int r = 5;
            foreach (var row in rows)
            {
                var values = RowValues(row);
                for (int c = 0; c < values.Length; c++)
                    ws.Cell(r, c + 1).Value = values[c];
                r++;
            }
            ws.Range(1, 1, 1, 20).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Columns().AdjustToContents();
            wb.SaveAs(filePath);
        }

        // ================== SCADA_TEHOBJ_SETTINGS ==================

        /// <summary>Ключи настроек (соответствуют свойствам ScadaGenSettings).</summary>
        public static readonly string[] SettingKeys =
        {
            "MarkaField", "NameField", "DiscField", "KksField", "ControllerField",
            "DParamTemplate", "Section", "ArhPer", "SrezControl", "UserGroup",
            "EvGroup", "PlcAdress", "PlcGr", "Template"
        };

        /// <summary>Прочитать настройки из SCADA_TEHOBJ_SETTINGS (нет ключа → значение по умолчанию
        /// из ScadaGenSettings; пустая таблица/ошибка чтения → defaults без падения).</summary>
        public ScadaGenSettings LoadSettings()
        {
            var s = new ScadaGenSettings();
            try
            {
                EnsureTables();
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        using var cn = new OleDbConnection(ConnStr);
                        cn.Open();
                        using var cmd = new OleDbCommand(
                            $"SELECT [Name], [Value] FROM [{Schema.ScadaSettingsTable}]", cn);
                        cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                        using var rd = cmd.ExecuteReader();
                        while (rd.Read())
                        {
                            if (rd.IsDBNull(0)) continue;
                            values[rd.GetString(0)] = rd.IsDBNull(1) ? "" : rd.GetString(1);
                        }
                        break;
                    }
                    catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                    {
                        System.Threading.Thread.Sleep(300 * attempt);
                    }
                }
                foreach (var key in SettingKeys)
                    if (values.TryGetValue(key, out string? v))
                        ApplySetting(s, key, v);
            }
            catch
            {
                // чтение настроек не должно ломать открытие окна — остаются значения по умолчанию
            }
            return s;
        }

        private static void ApplySetting(ScadaGenSettings s, string key, string value)
        {
            switch (key)
            {
                case "MarkaField": s.MarkaField = value; break;
                case "NameField": s.NameField = value; break;
                case "DiscField": s.DiscField = value; break;
                case "KksField": s.KksField = value; break;
                case "ControllerField": s.ControllerField = value; break;
                case "DParamTemplate": s.DParamTemplate = value; break;
                case "Section": s.Section = value; break;
                case "ArhPer": s.ArhPer = value; break;
                case "SrezControl": s.SrezControl = value; break;
                case "UserGroup": s.UserGroup = value; break;
                case "EvGroup": s.EvGroup = value; break;
                case "PlcAdress": s.PlcAdress = value; break;
                case "PlcGr": s.PlcGr = value; break;
                case "Template": s.Template = value; break;
            }
        }

        private static string ReadSetting(ScadaGenSettings s, string key) => key switch
        {
            "MarkaField" => s.MarkaField,
            "NameField" => s.NameField,
            "DiscField" => s.DiscField,
            "KksField" => s.KksField,
            "ControllerField" => s.ControllerField,
            "DParamTemplate" => s.DParamTemplate,
            "Section" => s.Section,
            "ArhPer" => s.ArhPer,
            "SrezControl" => s.SrezControl,
            "UserGroup" => s.UserGroup,
            "EvGroup" => s.EvGroup,
            "PlcAdress" => s.PlcAdress,
            "PlcGr" => s.PlcGr,
            "Template" => s.Template,
            _ => ""
        };

        /// <summary>Сохранить настройки целиком (UPDATE каждого ключа, недостающие — INSERT;
        /// одной транзакцией, повтор при блокировке).</summary>
        public void SaveSettings(ScadaGenSettings settings)
        {
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            EnsureTables();
            for (int attempt = 1; ; attempt++)
            {
                try { SaveSettingsOnce(settings); return; }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private void SaveSettingsOnce(ScadaGenSettings settings)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                foreach (var key in SettingKeys)
                {
                    string value = ReadSetting(settings, key);
                    using var upd = new OleDbCommand(
                        $"UPDATE [{Schema.ScadaSettingsTable}] SET [Value] = ? WHERE [Name] = ?", cn, tr);
                    upd.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(upd, value);
                    OleDbArgs.Add(upd, key);
                    if (upd.ExecuteNonQuery() == 0)
                    {
                        using var ins = new OleDbCommand(
                            $"INSERT INTO [{Schema.ScadaSettingsTable}] ([Name], [Value]) VALUES (?, ?)", cn, tr);
                        ins.CommandTimeout = DbProbe.TimeoutSeconds;
                        OleDbArgs.Add(ins, key);
                        OleDbArgs.Add(ins, value);
                        ins.ExecuteNonQuery();
                    }
                }
                tr.Commit();
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }


        // ================== SCADA_TYPE_MAP ==================

        /// <summary>Прочитать маппинг типов; при пустой таблице — сид по умолчанию (2.5.4).</summary>
        public List<ScadaTypeMapping> LoadTypeMappings()
        {
            EnsureTables();
            var list = new List<ScadaTypeMapping>();
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand(
                        $"SELECT ID, [Source], [Target] FROM [{Schema.ScadaTypeMapTable}] ORDER BY ID", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                        list.Add(new ScadaTypeMapping
                        {
                            Id = Convert.ToInt64(rd.GetValue(0)),
                            Source = rd.IsDBNull(1) ? "" : rd.GetString(1),
                            Target = rd.IsDBNull(2) ? "" : rd.GetString(2)
                        });
                    break;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
            if (list.Count == 0)
                foreach (var (s, t) in Schema.DefaultScadaTypeMap)
                    list.Add(new ScadaTypeMapping { Source = s, Target = t });
            return list;
        }

        /// <summary>Сохранить маппинг типов целиком (DELETE + INSERT одной транзакцией; повтор
        /// при блокировке). Пустые Source-строки отбрасываются.</summary>
        public int SaveTypeMappings(IEnumerable<ScadaTypeMapping> mappings)
        {
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            var list = mappings
                .Where(m => !string.IsNullOrWhiteSpace(m.Source))
                .Select(m => new ScadaTypeMapping { Source = m.Source.Trim(), Target = (m.Target ?? "").Trim() })
                .ToList();
            EnsureTables();
            for (int attempt = 1; ; attempt++)
            {
                try { return SaveTypeMappingsOnce(list); }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private int SaveTypeMappingsOnce(List<ScadaTypeMapping> list)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                using (var del = new OleDbCommand(
                    $"DELETE FROM [{Schema.ScadaTypeMapTable}]", cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    del.ExecuteNonQuery();
                }
                using var ins = new OleDbCommand(
                    $"INSERT INTO [{Schema.ScadaTypeMapTable}] ([Source], [Target]) VALUES (?, ?)", cn, tr);
                ins.CommandTimeout = DbProbe.TimeoutSeconds;
                foreach (var m in list)
                {
                    ins.Parameters.Clear();
                    OleDbArgs.Add(ins, m.Source);
                    OleDbArgs.Add(ins, m.Target);
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


        // ================== F27: запись Марки в IO_LIST ==================

        /// <summary>F27: «Записать Марку в IO_LIST» — заполнить колонку M_Marka у точек по
        /// SourceTag строк превья (MakeMarka уже применён при построении). Аудит — Update по
        /// каждому изменённому полю (одна сессия репозитория); возвращает (обновлено, не найдено).</summary>
        public (int Updated, int NotFound) WriteMarka(IReadOnlyList<ScadaTechObjectRow> rows, IIOListRepository repo)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (repo == null) throw new ArgumentNullException(nameof(repo));

            // индекс IO_LIST по Tag (дубли тегов — первая запись, как в WriteBindings F13)
            var byTag = new Dictionary<string, IoPoint>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in repo.GetAll())
                if (!string.IsNullOrWhiteSpace(p.Tag))
                    byTag.TryAdd(p.Tag!.Trim(), p);

            int updated = 0, notFound = 0;
            repo.BeginSession();
            try
            {
                foreach (var r in rows)
                {
                    string tag = (r.SourceTag ?? "").Trim();
                    if (tag.Length == 0) continue;
                    if (!byTag.TryGetValue(tag, out var p)) { notFound++; continue; }
                    p.Modbus ??= new Modbus();
                    if (string.Equals(p.Modbus.Marka ?? "", r.Marka ?? "", StringComparison.Ordinal)) continue;
                    p.Modbus.Marka = r.Marka;
                    if (repo.Update(p) > 0) updated++;
                }
                repo.EndSession();
            }
            catch (Exception)
            {
                repo.AbortSession();
                throw;
            }
            return (updated, notFound);
        }
    }
}
