using System.Collections.Generic;

namespace IO_PJT.Models
{
    /// <summary>F12: строка перечня технологических объектов SCADA (20 колонок A..T
    /// по образцу «ТехОбъекты_простой.xls»; спецификация — «Генерация технологических объектов Modbus.md»).</summary>
    public sealed class ScadaTechObjectRow
    {
        /// <summary>№ — не заполняется.</summary>
        public string No { get; set; } = "";
        /// <summary>Статус — не заполняется.</summary>
        public string Status { get; set; } = "";
        /// <summary>Mode (Раздел) — константа, по умолчанию TEHOBJ.</summary>
        public string Mode { get; set; } = "TEHOBJ";
        /// <summary>MARKA (Марка) — из выбранного поля, всегда начинается с «_», «-» → «_».</summary>
        public string Marka { get; set; } = "";
        /// <summary>OBJTYPE (Тип объекта) — M_DataType через таблицу соответствия SCADA-типам.</summary>
        public string ObjType { get; set; } = "";
        /// <summary>NAME (Наименование) — по умолчанию Service.</summary>
        public string Name { get; set; } = "";
        /// <summary>DISC (Описание) — по умолчанию Loop.</summary>
        public string Disc { get; set; } = "";
        /// <summary>OBJSIGN (Подпись) — не заполняется.</summary>
        public string ObjSign { get; set; } = "";
        /// <summary>OBJNUMBER (Номер) — не заполняется.</summary>
        public string ObjNumber { get; set; } = "";
        /// <summary>PLC переменная — не заполняется.</summary>
        public string PlcVarName { get; set; } = "";
        /// <summary>ARH_PER (Период архив) — константа 0.</summary>
        public string ArhPer { get; set; } = "0";
        /// <summary>KKS — по умолчанию Tag.</summary>
        public string Kks { get; set; } = "";
        /// <summary>OBJDPARAM (Доп.параметр) — краткий адрес Modbus по шаблону, ≤50 символов.</summary>
        public string ObjDParam { get; set; } = "";
        /// <summary>SREZCONTROL (Маска упр. в срезах) — константа 1 (по образцу).</summary>
        public string SrezControl { get; set; } = "1";
        /// <summary>USERGROUP (Классификатор) — заполняется вручную.</summary>
        public string UserGroup { get; set; } = "";
        /// <summary>EVGROUP (Группа событий) — заполняется вручную.</summary>
        public string EvGroup { get; set; } = "";
        /// <summary>PLCNAME (КОНТРОЛЛЕР) — по умолчанию Controller (FCS).</summary>
        public string PlcName { get; set; } = "";
        /// <summary>PLC_ADRESS (Адрес) — константа 0.</summary>
        public string PlcAdress { get; set; } = "0";
        /// <summary>PLC_GR (№ ресурса или группа) — по умолчанию 1, можно менять вручную.</summary>
        public string PlcGr { get; set; } = "1";
        /// <summary>TEMPLATE (Шаблон) — всегда «простой».</summary>
        public string Template { get; set; } = "простой";
    }

    /// <summary>F12: соответствие типа данных IO_LIST (колонка M_DataType карты Modbus)
    /// и типа данных SCADA (BOOL/INT/WORD/UDINT/REAL). Хранится в таблице SCADA_TYPE_MAP.</summary>
    public sealed class ScadaTypeMapping
    {
        public long Id { get; set; }

        /// <summary>Тип в IO List (например «32-Bit Floating»; сравнение IgnoreCase).</summary>
        public string Source { get; set; } = "";

        /// <summary>Тип в SCADA (BOOL/INT/WORD/UINT/UDINT/REAL — см. 2.5.1 спецификации).</summary>
        public string Target { get; set; } = "";
    }

    /// <summary>F12: настройки генерации перечня (поля-источники + константы).
    /// Три формы значения — единое правило для всех полей: (1) имя колонки IO_LIST (Tag/Loop/Service/…)
    /// → её значение точки; (2) структура с подстановками {ИмяКолонки} (как у Доп.параметра,
    /// напр. «{Service} ({Loop})») → составное значение из полей; (3) любой другой текст → константа.
    /// Персистятся в SCADA_TEHOBJ_SETTINGS (Name/Value).</summary>
    public sealed class ScadaGenSettings
    {
        // —— поля-источники (или константы) ——
        /// <summary>Источник MARKA: Tag (по умолчанию) / Loop / любое поле / константа.</summary>
        public string MarkaField { get; set; } = "Tag";

        /// <summary>Источник NAME (Наименование): Service.</summary>
        public string NameField { get; set; } = "Service";

        /// <summary>Источник DISC (Описание): Loop.</summary>
        public string DiscField { get; set; } = "Loop";

        /// <summary>Источник KKS: Tag.</summary>
        public string KksField { get; set; } = "Tag";

        /// <summary>Источник PLCNAME (КОНТРОЛЛЕР): Controller (FCS).</summary>
        public string ControllerField { get; set; } = "Controller";

        /// <summary>Шаблон OBJDPARAM (Доп.параметр) — подстановки {ИмяКолонки} из IO_LIST/M_*,
        /// результат обрезается до 50 символов (спецификация: «не более 50 символов»);
        /// обрезка — специфика только этого поля, остальные поля со структурами не усекаются.</summary>
        public string DParamTemplate { get; set; } = "FC{M_FuncCode} {M_RegType}{M_Address} {M_SlaveStation}";

        // —— константы ——
        /// <summary>Mode (Раздел): TEHOBJ.</summary>
        public string Section { get; set; } = "TEHOBJ";

        /// <summary>ARH_PER (Период архив): 0.</summary>
        public string ArhPer { get; set; } = "0";

        /// <summary>SREZCONTROL (Маска упр. в срезах): 1 (по образцу).</summary>
        public string SrezControl { get; set; } = "1";

        /// <summary>USERGROUP (Классификатор): по умолчанию «[Все]\[Все технологические]» (спецификация).</summary>
        public string UserGroup { get; set; } = "[Все]\\[Все технологические]";

        /// <summary>EVGROUP (Группа событий): заполняется вручную.</summary>
        public string EvGroup { get; set; } = "";

        /// <summary>PLC_ADRESS (Адрес): 0.</summary>
        public string PlcAdress { get; set; } = "0";

        /// <summary>PLC_GR (№ ресурса или группа): 1.</summary>
        public string PlcGr { get; set; } = "1";

        /// <summary>TEMPLATE (Шаблон): простой.</summary>
        public string Template { get; set; } = "простой";
    }

    /// <summary>F12: результат построения перечня (строки + статистика/предупреждения).</summary>
    public sealed class ScadaBuildResult
    {
        public List<ScadaTechObjectRow> Rows { get; } = new();

        /// <summary>Точек с заполненной адресацией Modbus (M_PlcTag) — кандидаты в перечень.</summary>
        public int WithAddressing { get; set; }

        /// <summary>Точек пропущено (нет адресации Modbus).</summary>
        public int SkippedNoAddressing { get; set; }

        /// <summary>Типов M_DataType без соответствия в таблице SCADA_TYPE_MAP (оставлен исходный тип).</summary>
        public int UnmappedTypes { get; set; }

        /// <summary>Строк с обрезанным Доп.параметром (было &gt;50 символов).</summary>
        public int TruncatedDParam { get; set; }

        /// <summary>Предупреждения (пустой Tag/Марка, неизвестный плейсхолдер шаблона, …).</summary>
        public List<string> Warnings { get; } = new();
    }
}
