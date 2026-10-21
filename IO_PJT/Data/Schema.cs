namespace IO_PJT.Data
{
    /// <summary>
    /// Схема базы данных — производный раздел Б8 файла IOclass.md (источник истины).
    /// Содержит DDL: таблица данных IO_LIST (126 колонок + ID = 127), служебные IO_LIST_Audit,
    /// MappingTemplates и MODBUS_MAP (F9: карта адресации Modbus).
    /// </summary>
    public static class Schema
    {
        /// <summary>Имя таблицы по умолчанию (ТЗ F1.2).</summary>
        public const string DefaultTable = "IO_LIST";

        /// <summary>F2.17: таблица копий повторяющихся тегов. Структура = IO_LIST (Schema.CreateTable),
        /// но БЕЗ уникального индекса UX_..._Tag — хранит все копии тега, включая первую.</summary>
        public const string DuplicateTable = "IO_LIST_DublicateTag";

        /// <summary>F9: таблица карты адресации Modbus (Register Table Function 02/03/06 +
        /// Interface parameters из файлов IMCS TO DCS Register Table Modbus).</summary>
        public const string ModbusMapTable = "MODBUS_MAP";

        /// <summary>F9.11: таблица ручных правил связывания IO_LIST ↔ MODBUS_MAP —
        /// пользовательские зависимости «поле IO (Tag/Service/…) по шаблону → поле карты
        /// (Description/Scale/PlcTag/…) по шаблону», применяются в Link() ПЕРЕД встроенными
        /// ролями DetectRole. Создаётся TableCreator.EnsureServiceTables, редактируется
        /// окном «Правила связывания Modbus...».</summary>
        public const string ModbusLinkRulesTable = "MODBUS_LINK_RULES";

        /// <summary>DDL таблицы ручных правил связывания (F9.11). Шаблоны (TagPattern/ValuePattern):
        /// содержит «*»/«?» → wildcard (анкорный), иначе — регулярное выражение без якорей;
        /// FuncCode пуст — фильтр не применяется; Priority меньше — правило раньше.</summary>
        public static string CreateModbusLinkRulesTable => $@"
CREATE TABLE [{ModbusLinkRulesTable}] (
  ID COUNTER PRIMARY KEY,
  [Name] TEXT(255),
  [IOField] TEXT(50),
  [TagPattern] TEXT(255),
  [MapField] TEXT(50),
  [ValuePattern] TEXT(255),
  [FuncCode] TEXT(50),
  [Priority] LONG,
  [Enabled] YESNO
)";

        /// <summary>F12: таблица соответствия типов данных IO_LIST (M_DataType) ↔ SCADA
        /// (BOOL/INT/WORD/UINT/UDINT/REAL) для колонки «Тип объекта» перечня Тех.объекты.</summary>
        public const string ScadaTypeMapTable = "SCADA_TYPE_MAP";

        /// <summary>F12: настройки окна «Генерация перечня Modbus (Тех.объекты)» —
        /// поля-источники и константы (Name/Value), переживают перезапуск.</summary>
        public const string ScadaSettingsTable = "SCADA_TEHOBJ_SETTINGS";

        /// <summary>DDL F12: таблица маппинга типов IO List → SCADA (семантика Source → Target).</summary>
        public static string CreateScadaTypeMapTable => $@"
CREATE TABLE [{ScadaTypeMapTable}] (
  ID COUNTER PRIMARY KEY,
  [Source] TEXT(255),
  [Target] TEXT(255)
)";

        /// <summary>DDL F12: настройки генерации перечня (значения параметров).</summary>
        public static string CreateScadaSettingsTable => $@"
CREATE TABLE [{ScadaSettingsTable}] (
  ID COUNTER PRIMARY KEY,
  [Name] TEXT(255),
  [Value] TEXT(255)
)";

        /// <summary>F13: настройки окна «Генерация блоков Modbus (XML)» — параметры пакетирования
        /// и XML-шаблона (Name/Value), переживают перезапуск.</summary>
        public const string ScadaMbSettingsTable = "SCADA_MB_SETTINGS";

        /// <summary>F13/F21: правила «станция Modbus → SlaveId (1-255)» — числового SlaveId нет
        /// в IO_LIST (в M_SlaveIdMain — только IP/маска/порт), задаётся таблицей.
        /// F21: станция хранит параметры подключения: IP main/reserve (авто из IO_LIST),
        /// Protocol TCP/RTU, Speed (авто), Parity/StopBit/DataBit/Reverse (UART), Timeout
        /// (авто), MaxLength — см. MbSlaveIdRule.</summary>
        public const string ScadaMbSlaveRulesTable = "SCADA_MB_SLAVE_RULES";

        /// <summary>F13: вставленный в окно XML-шаблон генерации (режим «Код», Body — MEMO,
        /// строка «current»).</summary>
        public const string ScadaMbTemplateTable = "SCADA_MB_TEMPLATE";

        /// <summary>F14: ручное сопоставление «Typno (IO_LIST) → (шаблон Data_Out, эталон)»
        /// (F16: Template — имя из SCADA_DATAOUT_TEMPLATES, пусто — основной шаблон;
        /// Exemplar — ключ Disc/NAME OnePOU). Сигналы без Typno не участвуют в генерации F14;
        /// Typno без строки маппинга — эталон по M_DataType (с предупреждением).</summary>
        public const string ScadaTypnoMapTable = "SCADA_TYPNO_MAP";

        /// <summary>F16: именованные шаблоны привязок Data_Out (каждый — свой XML-текст;
        /// Typno → имя шаблона в SCADA_TYPNO_MAP). Body — MEMO, путь файла — справочно
        /// (кнопка «Обновить из файла» перечитывает тело по FilePath).</summary>
        public const string ScadaDataOutTemplatesTable = "SCADA_DATAOUT_TEMPLATES";

        /// <summary>F22: именованные мастер-шаблоны блоков Modbus (Name + Protocol TCP/RTU →
        /// XML-тело) — «залитые» шаблоны в окне генерации; экспорт использует TCP/RTU-шаблон
        /// по протоколу станции пакета (SCADA_MB_SETTINGS MasterTcp/RtuTemplateName).</summary>
        public const string ScadaMasterTemplatesTable = "SCADA_MASTER_TEMPLATES";

        /// <summary>F22: именованные шаблоны привязок DataIn (вход DataIn = массив записи
        /// в Slave) — хранение/список/маппинг F23; генерации привязок DataIn нет.</summary>
        public const string ScadaDataInTemplatesTable = "SCADA_DATAIN_TEMPLATES";

        /// <summary>F23: маппинг «переменная шаблона → колонка IO_LIST/литерал»
        /// (Scope: master/dataout/datain; применяется при генерации).</summary>
        public const string ScadaTemplateVarsTable = "SCADA_TEMPLATE_VARS";

        /// <summary>DDL F13: настройки генерации блоков (значения параметров).</summary>
        public static string CreateScadaMbSettingsTable => $@"
CREATE TABLE [{ScadaMbSettingsTable}] (
  ID COUNTER PRIMARY KEY,
  [Name] TEXT(255),
  [Value] TEXT(255)
)";

        /// <summary>DDL F13/F21: правила «станция → SlaveId» + параметры станции (F21);
        /// существующие старые базы дополняются колонками в TableCreator.EnsureServiceTables.</summary>
        public static string CreateScadaMbSlaveRulesTable => $@"
CREATE TABLE [{ScadaMbSlaveRulesTable}] (
  ID COUNTER PRIMARY KEY,
  [Station] TEXT(255),
  [SlaveId] LONG,
  [IpMain] TEXT(255),
  [IpReserve] TEXT(255),
  [Protocol] TEXT(10),
  [Speed] TEXT(50),
  [Parity] YESNO,
  [StopBit] LONG,
  [DataBit] LONG,
  [Reverse] YESNO,
  [Timeout] LONG,
  [MaxLength] LONG,
  [MasterTemplate] TEXT(255)
)";

        /// <summary>DDL F13: вставленный шаблон генерации (код окна; MEMO — до 64 КБ).</summary>
        public static string CreateScadaMbTemplateTable => $@"
CREATE TABLE [{ScadaMbTemplateTable}] (
  ID COUNTER PRIMARY KEY,
  [Name] TEXT(50),
  [Body] MEMO
)";

        /// <summary>DDL F14/F16: сопоставление «Typno → (шаблон Data_Out, эталон)»
        /// (F16: Template — имя из SCADA_DATAOUT_TEMPLATES, пусто — основной шаблон;
        /// Exemplar — ключ Disc/NAME OnePOU шаблона, напр. BOOL/INT/DINT/REAL).</summary>
        public static string CreateScadaTypnoMapTable => $@"
CREATE TABLE [{ScadaTypnoMapTable}] (
  ID COUNTER PRIMARY KEY,
  [Typno] TEXT(255),
  [Template] TEXT(255),
  [Exemplar] TEXT(255)
)";

        /// <summary>DDL F16: именованные шаблоны привязок Data_Out (имя → XML-тело;
        /// FilePath — путь файла-источника, справочно).</summary>
        public static string CreateScadaDataOutTemplatesTable => $@"
CREATE TABLE [{ScadaDataOutTemplatesTable}] (
  ID COUNTER PRIMARY KEY,
  [Name] TEXT(255),
  [Body] MEMO,
  [FilePath] TEXT(255)
)";

        /// <summary>DDL F22: именованные мастер-шаблоны блоков (TCP/RTU).</summary>
        public static string CreateScadaMasterTemplatesTable => $@"
CREATE TABLE [{ScadaMasterTemplatesTable}] (
  ID COUNTER PRIMARY KEY,
  [Name] TEXT(255),
  [Protocol] TEXT(10),
  [Body] MEMO,
  [FilePath] TEXT(255)
)";

        /// <summary>DDL F22: именованные шаблоны привязок DataIn (без генерации).</summary>
        public static string CreateScadaDataInTemplatesTable => $@"
CREATE TABLE [{ScadaDataInTemplatesTable}] (
  ID COUNTER PRIMARY KEY,
  [Name] TEXT(255),
  [Body] MEMO,
  [FilePath] TEXT(255)
)";

        /// <summary>DDL F23: маппинг «переменная шаблона → колонка IO_LIST/литерал».</summary>
        public static string CreateScadaTemplateVarsTable => $@"
CREATE TABLE [{ScadaTemplateVarsTable}] (
  ID COUNTER PRIMARY KEY,
  [Scope] TEXT(10),
  [TemplateName] TEXT(255),
  [Var] TEXT(255),
  [IoColumn] TEXT(255),
  [Literal] TEXT(255)
)";


        /// <summary>F12: маппинг типов по умолчанию (раздел 2.5.4 спецификации;
        /// сидируется в SCADA_TYPE_MAP при создании пустой таблицы).</summary>
        public static readonly (string Source, string Target)[] DefaultScadaTypeMap =
        {
            ("BOOL", "BOOL"),
            ("8-Bit Signed", "INT"),
            ("8-Bit Unsigned", "WORD"),
            ("16-Bit Signed", "INT"),
            ("16-Bit Unsigned", "WORD"),
            ("32-Bit Signed", "INT"),
            ("32-Bit Unsigned", "UDINT"),
            ("32-Bit Floating", "REAL"),
            ("64-Bit Floating", "REAL")
        };


        /// <summary>DDL таблицы данных IO_LIST: 130 колонок данных (+ ID = 131), TEXT(255) везде,
        /// кроме PTag_* — TEXT(50). Блок M_* (F9 27 + F13 2 + F27 1 = 30 колонок) — данные карты
        /// Modbus и привязки блоков, перед CBL_*. F14: Typno в Identification.</summary>
        public static string CreateTable(string tableName) => $@"
CREATE TABLE [{tableName}] (
  ID COUNTER PRIMARY KEY,
  Code LONG, Area TEXT(255), Plant TEXT(255), Title TEXT(255), Loop TEXT(255), SubLoop TEXT(255),
  ProcessLoop TEXT(255), Logic TEXT(255), Typno TEXT(255), Tag TEXT(255), ProcessTag TEXT(255), System TEXT(255),
  IoType TEXT(255), Controller TEXT(255), Location TEXT(255),
  PTag_TagArea TEXT(50), PTag_DeviceClass TEXT(50), PTag_LoopNumber TEXT(50),
  PTag_TagNumber TEXT(50), PTag_Suffix TEXT(50), PTag_FullTag TEXT(50), PTag_Separator TEXT(50),
  Service TEXT(255), ServiceEnglish TEXT(255), InstrumentType TEXT(255),
  InstrumentTypeEnglish TEXT(255), PID TEXT(255), SignalType TEXT(255),
  ExType TEXT(255), Subsystem TEXT(255), Auxiliary TEXT(255),
  VoltageLevel TEXT(255), PowerConsumption TEXT(255),
  Range_MIN TEXT(255), Range_MAX TEXT(255), Eng_Unit TEXT(255),
  Range_MIN2 TEXT(255), Range_MAX2 TEXT(255), Eng_Unit2 TEXT(255),
  LL TEXT(255), LL2 TEXT(255), L TEXT(255),
  H TEXT(255), HH TEXT(255), HH2 TEXT(255),
  M_MapFile TEXT(255), M_SlaveStation TEXT(255), M_SlaveIdMain TEXT(255), M_SlaveIdBackup TEXT(255),
  M_Speed TEXT(255), M_Timeout TEXT(255),
  M_PrfDesignation TEXT(255), M_PlcTag TEXT(255), M_ObjectTag TEXT(255), M_Description TEXT(255),
  M_Units TEXT(255), M_Scale TEXT(255), M_LL TEXT(255), M_LA TEXT(255), M_HA TEXT(255), M_HH TEXT(255),
  M_Scaling TEXT(255), M_SigType TEXT(255), M_RegType TEXT(255), M_Address TEXT(255),
  M_AccessType TEXT(255), M_DataType TEXT(255), M_FuncCode TEXT(255),
  M_DcsChannel TEXT(255), M_DcsTag TEXT(255), M_DcsFunctions TEXT(255), M_Note TEXT(255),
  M_BusBlock TEXT(255), M_BusDataOut TEXT(255), M_Marka TEXT(255),
  CBL_Id TEXT(255), CBL_Description TEXT(255), CBL_Type TEXT(255), CBL_Designation TEXT(255),
  CBL_From TEXT(255), CBL_To TEXT(255), CBL_Length LONG, CBL_Color TEXT(255), CBL_Pair LONG,
  CBL_Note TEXT(255), CBL_VendorDesignation TEXT(255), CBL_Drum TEXT(255), CBL_Voltage TEXT(255),
  EQP_CableTecon TEXT(255), EQP_Mcc TEXT(255), EQP_ControlCabinet TEXT(255),
  EQP_MarshallingCabinet TEXT(255), EQP_CPU TEXT(255),
  EQP_Chassis_Main TEXT(255), EQP_Chassis_Redundant TEXT(255),
  EQP_SlotMain TEXT(255), EQP_SlotRedundant TEXT(255), EQP_SlotMain2 TEXT(255), EQP_SlotRedundant2 TEXT(255),
  EQP_ModuleMain TEXT(255), EQP_ModuleRedundant TEXT(255), EQP_ModuleMain2 TEXT(255),
  EQP_Module2Redundant TEXT(255), EQP_Channel LONG, EQP_Type TEXT(255),
  REV_Number LONG, REV_Description TEXT(255), REV_Package TEXT(255),
  REV_VendorField1 TEXT(255), REV_VendorField2 TEXT(255), REV_Document TEXT(255),
  REV_AkerRevision TEXT(255), REV_FileName TEXT(255), REV_Sheet TEXT(255), REV_Author TEXT(255),
  REV_DateEntered DATETIME, REV_RowNumber LONG, REV_Type TEXT(255),
  REV_Count TEXT(255), REV_DateModified DATETIME,
  column1 TEXT(255), column2 TEXT(255), column3 TEXT(255), column4 TEXT(255), column5 TEXT(255),
  column6 TEXT(255), column7 TEXT(255), column8 TEXT(255), column9 TEXT(255), column10 TEXT(255)
)";

        /// <summary>Уникальный индекс по Tag — ключ upsert (ТЗ F4.3).</summary>
        public static string CreateTagIndex(string tableName) =>
            $"CREATE UNIQUE INDEX UX_{tableName}_Tag ON [{tableName}] (Tag)";

        /// <summary>DDL служебной таблицы истории изменений (ТЗ F7.1).
        /// Имя колонки ChangeAction (не Action — зарезервированное слово Access).
        /// REV_FileName/System (F7.6): контекст изменения — из какого файла и по какой системе.</summary>
        public const string CreateAuditTable = @"
CREATE TABLE IO_LIST_Audit (
  ID COUNTER PRIMARY KEY,
  TableName TEXT(255), Tag TEXT(255), ChangeAction TEXT(50),
  FieldName TEXT(255), OldValue TEXT(255), NewValue TEXT(255),
  REV_FileName TEXT(255), REV_Sheet TEXT(255), System TEXT(255),
  ChangedBy TEXT(255), ChangedAt DATETIME
)";

        /// <summary>Контекстные колонки аудита (F7.6/F7.7): источник изменения — документ (файл+вкладка) и система.
        /// Добавляются в существующие таблицы аудита через ALTER TABLE (идемпотентно, Б8).</summary>
        public static readonly string[] AuditContextColumns = { "REV_FileName", "REV_Sheet", "System" };

        /// <summary>DDL таблицы шаблонов маппинга (шаблоны хранятся в самой базе).</summary>
        public const string CreateMappingTemplatesTable = @"
CREATE TABLE MappingTemplates (
  ID COUNTER PRIMARY KEY,
  Name TEXT(255), TableName TEXT(255), SheetHint TEXT(255), HeaderRow LONG,
  Mappings MEMO, Author TEXT(255), CreatedAt DATETIME, UpdatedAt DATETIME
)";

        /// <summary>DDL таблицы карты адресации Modbus (F9): полная копия Register Table Function 02/03/06
        /// + параметры интерфейса станции (Interface parameters) + связь с IO_LIST (LinkTag — тег РСУ,
        /// сопоставленный ModbusLinkService). Строки без привязки хранятся тоже (LinkTag пуст).
        /// Все имена в квадратных скобках: Address — зарезервированное слово JET/Access.</summary>
        public const string CreateModbusMapTable = @"
CREATE TABLE MODBUS_MAP (
  [ID] COUNTER PRIMARY KEY,
  [MapFile] TEXT(255), [MapSheet] TEXT(255), [MapRow] LONG,
  [SlaveStation] TEXT(255), [SlaveIdMain] TEXT(255), [SlaveIdBackup] TEXT(255),
  [Speed] TEXT(255), [Timeout] TEXT(255),
  [PrfDesignation] TEXT(255), [PlcTag] TEXT(255), [ObjectTag] TEXT(255), [Description] TEXT(255),
  [Units] TEXT(255), [Scale] TEXT(255), [LL] TEXT(255), [LA] TEXT(255), [HA] TEXT(255), [HH] TEXT(255),
  [Scaling] TEXT(255), [SigType] TEXT(255), [RegType] TEXT(255), [AddrBit] TEXT(255),
  [AccessType] TEXT(255), [DataType] TEXT(255), [FuncCode] TEXT(255),
  [DcsChannel] TEXT(255), [DcsTag] TEXT(255), [DcsFunctions] TEXT(255), [Note] TEXT(255),
  [LinkTag] TEXT(255), [LinkState] TEXT(255)
)";

        /// <summary>
        /// Имена 130 колонок таблицы данных (в порядке DDL; вместе с ID — 131).
        /// Используются для маппинга Excel↔Access, создания таблицы и подсветки.
        /// REV_Sheet (F2.16) — вкладка-источник, входит в идентификацию документа (файл+вкладка).
        /// M_* (F9) — данные карты Modbus перед CBL_*.
        /// </summary>
        public static readonly string[] DataColumns =
        {
            "Code", "Area", "Plant", "Title", "Loop", "SubLoop", "ProcessLoop", "Logic", "Typno", "Tag", "ProcessTag",
            "System", "IoType", "Controller", "Location",
            "PTag_TagArea", "PTag_DeviceClass", "PTag_LoopNumber", "PTag_TagNumber", "PTag_Suffix",
            "PTag_FullTag", "PTag_Separator",
            "Service", "ServiceEnglish", "InstrumentType", "InstrumentTypeEnglish", "PID",
            "SignalType", "ExType", "Subsystem", "Auxiliary",
            "VoltageLevel", "PowerConsumption",
            "Range_MIN", "Range_MAX", "Eng_Unit", "Range_MIN2", "Range_MAX2", "Eng_Unit2",
            "LL", "LL2", "L", "H", "HH", "HH2",
            "M_MapFile", "M_SlaveStation", "M_SlaveIdMain", "M_SlaveIdBackup", "M_Speed", "M_Timeout",
            "M_PrfDesignation", "M_PlcTag", "M_ObjectTag", "M_Description",
            "M_Units", "M_Scale", "M_LL", "M_LA", "M_HA", "M_HH",
            "M_Scaling", "M_SigType", "M_RegType", "M_Address", "M_AccessType", "M_DataType", "M_FuncCode",
            "M_DcsChannel", "M_DcsTag", "M_DcsFunctions", "M_Note",
            "M_BusBlock", "M_BusDataOut", "M_Marka",
            "CBL_Id", "CBL_Description", "CBL_Type", "CBL_Designation", "CBL_From", "CBL_To",
            "CBL_Length", "CBL_Color", "CBL_Pair", "CBL_Note", "CBL_VendorDesignation",
            "CBL_Drum", "CBL_Voltage",
            "EQP_CableTecon", "EQP_Mcc", "EQP_ControlCabinet", "EQP_MarshallingCabinet", "EQP_CPU",
            "EQP_Chassis_Main", "EQP_Chassis_Redundant",
            "EQP_SlotMain", "EQP_SlotRedundant", "EQP_SlotMain2", "EQP_SlotRedundant2",
            "EQP_ModuleMain", "EQP_ModuleRedundant", "EQP_ModuleMain2", "EQP_Module2Redundant",
            "EQP_Channel", "EQP_Type",
            "REV_Number", "REV_Description", "REV_Package", "REV_VendorField1", "REV_VendorField2",
            "REV_Document", "REV_AkerRevision", "REV_FileName", "REV_Sheet", "REV_Author", "REV_DateEntered",
            "REV_RowNumber", "REV_Type", "REV_Count", "REV_DateModified",
            "column1", "column2", "column3", "column4", "column5",
            "column6", "column7", "column8", "column9", "column10"
        };

        /// <summary>Колонки, заполняемые программой (не из Excel) — автозаполнение Б8, п.9.</summary>
        public static readonly string[] AutoColumns =
        {
            "PTag_TagArea", "PTag_DeviceClass", "PTag_LoopNumber", "PTag_TagNumber",
            "PTag_Suffix", "PTag_FullTag", "PTag_Separator",
            "REV_FileName", "REV_Sheet", "REV_RowNumber", "REV_Author", "REV_DateEntered", "REV_DateModified"
        };

        /// <summary>Колонки-шкалы и уставки: TEXT, конвертация в число при валидации (Б8, п.10).</summary>
        public static readonly string[] ScaleAlarmColumns =
        {
            "Range_MIN", "Range_MAX", "Range_MIN2", "Range_MAX2",
            "LL", "LL2", "L", "H", "HH", "HH2"
        };

        /// <summary>SQL-тип колонки для ALTER TABLE ADD (автодополнение существующих таблиц, Б8).</summary>
        public static string SqlTypeFor(string column)
        {
            if (column.StartsWith("PTag_")) return "TEXT(50)";
            if (column is "Code" or "CBL_Length" or "CBL_Pair" or "EQP_Channel"
                or "REV_Number" or "REV_RowNumber")
                return "LONG";
            if (column is "REV_DateEntered" or "REV_DateModified")
                return "DATETIME";
            return "TEXT(255)";
        }
    }
}
