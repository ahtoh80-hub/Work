using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Linq;
using System.Text.Json;
using IO_PJT.Data;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// Сопоставление «столбцы Excel ↔ колонки таблицы Access» (ТЗ F2.2):
    /// авто-определение по именам (с словарём алиасов), ручная привязка в окне,
    /// шаблоны хранятся в таблице MappingTemplates самой базы.
    /// Обязательное поле — Tag: без его привязки Apply/импорт блокируется.
    /// </summary>
    public sealed class MappingService
    {
        private readonly string _dbPath;

        public MappingService(string dbPath) => _dbPath = dbPath;

        private string ConnStr => DbProbe.ConnStrWithTimeout(_dbPath);

        /// <summary>Колонки, доступные для привязки к Excel (автозаполняемые PTag_*/REV_* исключены).</summary>
        public static IReadOnlyList<string> AvailableColumns =>
            Schema.DataColumns.Where(c => !Schema.AutoColumns.Contains(c)).ToList();

        /// <summary>Словарь алиасов: заголовок Excel → колонка таблицы (эталонный файл).</summary>
        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["код"] = "Code", ["code"] = "Code",
            ["tagno"] = "Tag", ["tagname"] = "Tag", ["тег"] = "Tag",
            ["loopno"] = "Loop", ["mainloop"] = "Loop",
            ["instrumenttype"] = "InstrumentType",
            ["fcs"] = "Controller",
            ["pidno"] = "PID",
            ["logic no"] = "Logic", ["logicno"] = "Logic", ["logic"] = "Logic",
            ["min"] = "Range_MIN", ["max"] = "Range_MAX", ["unit"] = "Eng_Unit",
            ["min1"] = "Range_MIN2", ["max1"] = "Range_MAX2", ["unit1"] = "Eng_Unit2",
            ["rev"] = "REV_Number", ["doc"] = "REV_Document", ["rev_ak"] = "REV_AkerRevision",
            ["auxiliary instrument"] = "Auxiliary", ["sub system"] = "Subsystem",
            ["status_comment"] = "column1", ["typno"] = "column2", ["remarks"] = "column3"
        };

        /// <summary>Нормализация заголовка для сравнения (без пробелов/регистра/разделителей).</summary>
        private static string Normalize(string s) =>
            new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        /// <summary>Алиасы в нормализованном виде: «Tag No» → tagno → Tag.</summary>
        private static readonly Dictionary<string, string> NormalizedAliases = BuildNormalizedAliases();

        private static Dictionary<string, string> BuildNormalizedAliases()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in Aliases)
            {
                string key = Normalize(kv.Key);
                if (key.Length > 0 && !dict.ContainsKey(key)) dict[key] = kv.Value;
            }
            return dict;
        }

        /// <summary>Авто-определение маппинга: точное совпадение имени или алиас (F2.2).
        /// F2.14: каждая связка несёт индекс колонки Excel (0-based) — дубликаты заголовков
        /// (MIN/MIN) различаются по позиции; повторное имя не привязывается ко второй колонке
        /// автоматически (подсказка о дубликате — в Note).</summary>
        public List<ColumnMap> AutoDetect(IReadOnlyList<string> headers)
        {
            var byNormalized = new Dictionary<string, string>();
            foreach (var c in AvailableColumns) byNormalized[Normalize(c)] = c;

            var maps = new List<ColumnMap>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // первое вхождение имени

            // F2.14: сколько раз каждое имя встречается в шапке — подсказка о дубликатах (MIN/MIN)
            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers)
                if (!string.IsNullOrWhiteSpace(h))
                    nameCounts[h] = nameCounts.TryGetValue(h, out int c) ? c + 1 : 1;

            for (int i = 0; i < headers.Count; i++)
            {
                string header = headers[i];
                string norm = Normalize(header);
                if (string.IsNullOrEmpty(norm)) continue; // пустая колонка — строки маппинга нет

                bool dupName = nameCounts.TryGetValue(header, out int cnt) && cnt > 1;
                bool firstOcc = seen.Add(header); // true только у первого вхождения имени
                string? target = byNormalized.TryGetValue(norm, out var exact) ? exact : null;
                if (target == null && NormalizedAliases.TryGetValue(norm, out var aliased))
                    target = aliased;
                // F2.14: первое вхождение дубликата получает авто-привязку, повторные — нет
                // (E008-страховка): вторая колонка с тем же именем привязывается вручную по позиции
                bool autoOk = target != null && used.Add(target) && firstOcc;

                maps.Add(new ColumnMap
                {
                    ExcelColumn = header,
                    ExcelColumnIndex = i,
                    AccessColumn = autoOk ? target! : "",
                    Note = dupName ? $"заголовок встречается {cnt} раз — колонка определяется позицией (E008)" : ""
                });
            }
            return maps;
        }

        // ================== Шаблоны (таблица MappingTemplates самой базы) ==================

        /// <summary>Сохранить/обновить шаблон по имени.</summary>
        public void SaveTemplate(MappingTemplate t)
        {
            ServiceSchema.EnsureServiceTables(_dbPath);
            string json = JsonSerializer.Serialize(t.Mappings);
            t.UpdatedAt = DateTime.Now;
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();

            long? existing = FindIdByName(cn, t.Name);
            if (existing.HasValue)
            {
                const string upd = @"UPDATE MappingTemplates SET TableName=?, SheetHint=?, HeaderRow=?,
                    Mappings=?, Author=?, UpdatedAt=? WHERE ID=?";
                using var cmd = new OleDbCommand(upd, cn);
                OleDbArgs.Add(cmd, t.TableName);
                OleDbArgs.Add(cmd, t.SheetHint);
                OleDbArgs.Add(cmd, t.HeaderRow);
                OleDbArgs.Add(cmd, json);
                OleDbArgs.Add(cmd, t.Author);
                OleDbArgs.Add(cmd, t.UpdatedAt);
                OleDbArgs.Add(cmd, existing.Value);
                cmd.ExecuteNonQuery();
                t.Id = existing.Value;
            }
            else
            {
                t.CreatedAt = DateTime.Now;
                const string ins = @"INSERT INTO MappingTemplates
                    (Name, TableName, SheetHint, HeaderRow, Mappings, Author, CreatedAt, UpdatedAt)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?)";
                using var cmd = new OleDbCommand(ins, cn);
                OleDbArgs.Add(cmd, t.Name);
                OleDbArgs.Add(cmd, t.TableName);
                OleDbArgs.Add(cmd, t.SheetHint);
                OleDbArgs.Add(cmd, t.HeaderRow);
                OleDbArgs.Add(cmd, json);
                OleDbArgs.Add(cmd, t.Author);
                OleDbArgs.Add(cmd, t.CreatedAt);
                OleDbArgs.Add(cmd, t.UpdatedAt);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Загрузить шаблон по имени (или null).</summary>
        public MappingTemplate? LoadTemplate(string name)
        {
            ServiceSchema.EnsureServiceTables(_dbPath);
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            const string sql = @"SELECT ID, Name, TableName, SheetHint, HeaderRow, Mappings,
                Author, CreatedAt, UpdatedAt FROM MappingTemplates WHERE Name=?";
            using var cmd = new OleDbCommand(sql, cn);
            cmd.Parameters.AddWithValue("?", name);
            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;

            var t = new MappingTemplate
            {
                Id = Convert.ToInt64(rd["ID"]),
                Name = rd["Name"]?.ToString() ?? "",
                TableName = rd["TableName"]?.ToString() ?? "",
                SheetHint = rd["SheetHint"]?.ToString() ?? "",
                HeaderRow = rd["HeaderRow"] is int hr ? hr : 1,
                Author = rd["Author"]?.ToString() ?? "",
                CreatedAt = rd["CreatedAt"] is DateTime c ? c : DateTime.Now,
                UpdatedAt = rd["UpdatedAt"] is DateTime u ? u : DateTime.Now
            };
            try
            {
                var list = JsonSerializer.Deserialize<List<ColumnMap>>(rd["Mappings"]?.ToString() ?? "[]");
                if (list != null) t.Mappings = list;
            }
            catch { /* повреждённый JSON шаблона — оставляем пустой список */ }
            return t;
        }

        /// <summary>Имена всех сохранённых шаблонов.</summary>
        public IReadOnlyList<string> GetTemplateNames()
        {
            ServiceSchema.EnsureServiceTables(_dbPath);
            var result = new List<string>();
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var cmd = new OleDbCommand("SELECT Name FROM MappingTemplates ORDER BY Name", cn);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) result.Add(rd["Name"]?.ToString() ?? "");
            return result;
        }

        /// <summary>Удалить шаблон по имени.</summary>
        public void DeleteTemplate(string name)
        {
            ServiceSchema.EnsureServiceTables(_dbPath);
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var cmd = new OleDbCommand("DELETE FROM MappingTemplates WHERE Name=?", cn);
            cmd.Parameters.AddWithValue("?", name);
            cmd.ExecuteNonQuery();
        }

        private long? FindIdByName(OleDbConnection cn, string name)
        {
            using var cmd = new OleDbCommand("SELECT ID FROM MappingTemplates WHERE Name=?", cn);
            cmd.Parameters.AddWithValue("?", name);
            var r = cmd.ExecuteScalar();
            return r == null || r is DBNull ? null : Convert.ToInt64(r);
        }
    }
}
