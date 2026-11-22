using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using IO_PJT.Data;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>F100: вид импорта Excel — профиль целевой таблицы (окно импорта общее для всех).</summary>
    public enum ImportTargetKind
    {
        /// <summary>IO_LIST-структура (F2): колонки Schema.DataColumns, обязательное поле Tag.</summary>
        IoList,
        /// <summary>Карта адресации Modbus → MODBUS_MAP (F9/F100): обязательное поле PlcTag.</summary>
        ModbusMap
    }

    /// <summary>Профиль целевой таблицы импорта (F100): колонки для привязки, алиасы заголовков
    /// Excel, обязательное поле, строка шапки, маска листов, режимы записи, встроенный шаблон.
    /// Новая таблица добавляется как новый профиль — без изменения окна импорта.</summary>
    public sealed class ImportTargetProfile
    {
        public ImportTargetKind Kind { get; init; }

        /// <summary>Имя в списке «Тип импорта» (и пресет пунктов меню F100).</summary>
        public string DisplayName { get; init; } = "";

        /// <summary>Целевая таблица Access.</summary>
        public string TableName { get; init; } = "";

        /// <summary>Обязательное поле: без его привязки Apply блокируется (F2.2/F100).</summary>
        public string RequiredField { get; init; } = "Tag";

        /// <summary>Строка шапки Excel по умолчанию.</summary>
        public int DefaultHeaderRow { get; init; } = 1;

        /// <summary>Маска листов по умолчанию ("" — не задана, импортируются все листы).</summary>
        public string DefaultSheetMask { get; init; } = "";

        /// <summary>Галочка «Все листы по маске» по умолчанию включена.</summary>
        public bool AllSheetsByMaskDefault { get; init; }

        /// <summary>Режимы записи (элементы ComboBox «Режим»).</summary>
        public IReadOnlyList<string> Modes { get; init; } = Array.Empty<string>();

        /// <summary>Разрешено создание таблицы с нуля (F1.3) — только IO_LIST-структуры.</summary>
        public bool SupportsCreateTable { get; init; }

        /// <summary>После импорта выполнять связывание строк карты (F9.3, FillEmpty).</summary>
        public bool SupportsLinkAfterImport { get; init; }

        /// <summary>Колонки, доступные для привязки (без служебных и автозаполняемых).</summary>
        public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();

        /// <summary>Алиасы: заголовок Excel → колонка таблицы (регистр не важен).</summary>
        public IReadOnlyDictionary<string, string> Aliases { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>F100: сопоставлять заголовок по его строкам (двуязычные шапки карты
        /// «кириллица\nEnglish» — алиас по английской строке). IO_LIST: false (F2.13 без изменений).</summary>
        public bool MatchHeaderLines { get; init; }

        /// <summary>Встроенный шаблон маппинга (null — только сохранённые вручную шаблоны).</summary>
        public MappingTemplate? BuiltInTemplate { get; init; }
    }

    /// <summary>Реестр профилей импорта (F100): пункты меню «Импорт IO LIST из Excel...» и
    /// «Импорт Modbus_MAP из Excel...» открывают одно окно с предвыбранным профилем.</summary>
    public static class ImportTargets
    {
        /// <summary>Алиасы карты Modbus: английские строки двуязычных заголовков Register Table
        /// (шапка строка 2) и Interface parameters (строка 1) эталонного файла F9.
        /// Кириллическая половина заголовка отбрасывается разбиением на строки (MatchHeaderLines);
        /// «LL/LA/HA/HH/scale/note» распознаются как совпадение имён колонок без алиаса.
        /// Объявляется ДО профиля ModbusMap — статические поля инициализируются сверху вниз.</summary>
        private static readonly Dictionary<string, string> ModbusAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Project Functional Designation"] = "PrfDesignation",
            ["PLC Tag/Variable Name"] = "PlcTag",
            ["Description of the variable"] = "Description",
            ["Units of measurement"] = "Units",
            ["Scaling factors"] = "Scaling",
            ["Signal type (Impulse/Permanent)"] = "SigType",
            ["Register type"] = "RegType",
            ["Address/Bit"] = "AddrBit",
            ["Access type"] = "AccessType",
            ["Data type"] = "DataType",
            ["Function code"] = "FuncCode",
            ["DCS channel"] = "DcsChannel",
            ["DCS Tag/Name variable"] = "DcsTag",
            ["DCS functions"] = "DcsFunctions",
            // Interface parameters — параметры станции, общие для всех строк файла
            ["Slave Station (Data Source)"] = "SlaveStation",
            ["Slave Station"] = "SlaveStation",
            ["Slave ID main"] = "SlaveIdMain",
            ["Slave ID back-up"] = "SlaveIdBackup",
            ["Slave ID backup"] = "SlaveIdBackup",
            ["speed"] = "Speed",
            ["timeout"] = "Timeout"
        };

        /// <summary>IO_LIST: как в F2 — шапка строка 1, обязательное поле Tag, колонки DataColumns.</summary>
        public static readonly ImportTargetProfile IoList = new()
        {
            Kind = ImportTargetKind.IoList,
            DisplayName = "IO_LIST",
            TableName = Schema.DefaultTable,
            RequiredField = "Tag",
            DefaultHeaderRow = 1,
            Modes = new[] { "Добавить (upsert по Tag)", "Заменить (очистить)" },
            SupportsCreateTable = true,
            Columns = MappingService.AvailableColumns,
            Aliases = MappingService.IoListAliases
        };

        /// <summary>MODBUS_MAP: шапка строка 2, маска листов «Register Table», обязательное поле PlcTag;
        /// режим по умолчанию — сравнение строк (F100), после импорта — связывание FillEmpty (F9.3).</summary>
        public static readonly ImportTargetProfile ModbusMap = new()
        {
            Kind = ImportTargetKind.ModbusMap,
            DisplayName = "MODBUS_MAP",
            TableName = Schema.ModbusMapTable,
            RequiredField = "PlcTag",
            DefaultHeaderRow = 2,
            DefaultSheetMask = "Register Table",
            AllSheetsByMaskDefault = true,
            Modes = new[] { "Сравнение (добавить/обновить/удалить)", "Заменить строки файла" },
            SupportsLinkAfterImport = true,
            MatchHeaderLines = true,
            Columns = new[]
            {
                "PrfDesignation", "PlcTag", "Description", "Units", "Scale", "LL", "LA", "HA", "HH", "Scaling",
                "SigType", "RegType", "AddrBit", "AccessType", "DataType", "FuncCode", "DcsChannel", "DcsTag",
                "DcsFunctions", "Note", "SlaveStation", "SlaveIdMain", "SlaveIdBackup", "Speed", "Timeout"
            },
            Aliases = ModbusAliases,
            BuiltInTemplate = BuiltInModbusTemplate()
        };

        /// <summary>Все профили (порядок = порядок в списке «Тип импорта»).</summary>
        public static IReadOnlyList<ImportTargetProfile> All { get; } = new[] { IoList, ModbusMap };

        /// <summary>Профиль по DisplayName (регистр не важен); не найден → IO_LIST.</summary>
        public static ImportTargetProfile Find(string? displayName) =>
            All.FirstOrDefault(p => string.Equals(p.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
            ?? IoList;

        /// <summary>F100: подходит ли лист под маску: пустая маска — все листы; маска с * / ? —
        /// полный wildcard-совпадение; иначе — вхождение подстроки (как «Register Table» в F9).</summary>
        public static bool MaskMatches(string? mask, string sheetName)
        {
            string m = (mask ?? "").Trim();
            if (m.Length == 0) return true;
            if (m.IndexOfAny(new[] { '*', '?' }) >= 0)
            {
                string pattern = "^" + Regex.Escape(m).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                return Regex.IsMatch(sheetName, pattern, RegexOptions.IgnoreCase);
            }
            return sheetName.Contains(m, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>F100: встроенный шаблон «Register Table (стандарт)» — колонки B..U эталонного
        /// файла (шапка строка 2). ExcelColumnIndex — позиция колонки (резолв по позиции в окне
        /// импорта: имя хранится как английская строка шапки и в листе-источнике может быть
        /// двуязычным), AccessColumn — колонка MODBUS_MAP.</summary>
        private static MappingTemplate BuiltInModbusTemplate()
        {
            var m = new List<ColumnMap>();
            void Add(int idx, string excel, string col) =>
                m.Add(new ColumnMap { ExcelColumn = excel, ExcelColumnIndex = idx, AccessColumn = col });

            Add(1, "Project Functional Designation", "PrfDesignation");
            Add(2, "PLC Tag/Variable Name", "PlcTag");
            Add(3, "Description of the variable", "Description");
            Add(4, "Units of measurement", "Units");
            Add(5, "scale", "Scale");
            Add(6, "LL", "LL");
            Add(7, "LA", "LA");
            Add(8, "HA", "HA");
            Add(9, "HH", "HH");
            Add(10, "Scaling factors", "Scaling");
            Add(11, "Signal type (Impulse/Permanent)", "SigType");
            Add(12, "Register type", "RegType");
            Add(13, "Address/Bit", "AddrBit");
            Add(14, "Access type", "AccessType");
            Add(15, "Data type", "DataType");
            Add(16, "Function code", "FuncCode");
            Add(17, "DCS channel", "DcsChannel");
            Add(18, "DCS Tag/Name variable", "DcsTag");
            Add(19, "DCS functions", "DcsFunctions");
            Add(20, "note", "Note");

            return new MappingTemplate
            {
                Name = "Register Table (стандарт)",
                TableName = Schema.ModbusMapTable,
                SheetHint = "Register Table",
                HeaderRow = 2,
                Mappings = m,
                Author = "F100"
            };
        }
    }
}