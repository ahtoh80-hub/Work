namespace IO_PJT.Data
{
    /// <summary>
    /// Схема базы данных — производный раздел Б8 файла IOclass.md (источник истины).
    /// Содержит DDL: таблица данных IO_LIST (100 колонок), служебные IO_LIST_Audit
    /// и MappingTemplates.
    /// </summary>
    public static class Schema
    {
        /// <summary>Имя таблицы по умолчанию (ТЗ F1.2).</summary>
        public const string DefaultTable = "IO_LIST";

        /// <summary>DDL таблицы данных IO_LIST: 100 колонок (99 + ID), TEXT(255) везде, кроме PTag_* — TEXT(50).</summary>
        public static string CreateTable(string tableName) => $@"
CREATE TABLE [{tableName}] (
  ID COUNTER PRIMARY KEY,
  Code LONG, Area TEXT(255), Plant TEXT(255), Title TEXT(255), Loop TEXT(255), SubLoop TEXT(255),
  ProcessLoop TEXT(255), Logic TEXT(255), Tag TEXT(255), ProcessTag TEXT(255), System TEXT(255),
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

        /// <summary>
        /// Имена 99 колонок таблицы данных (в порядке DDL; вместе с ID — 100).
        /// Используются для маппинга Excel↔Access, создания таблицы и подсветки.
        /// REV_Sheet (F2.16) — вкладка-источник, входит в идентификацию документа (файл+вкладка).
        /// </summary>
        public static readonly string[] DataColumns =
        {
            "Code", "Area", "Plant", "Title", "Loop", "SubLoop", "ProcessLoop", "Logic", "Tag", "ProcessTag",
            "System", "IoType", "Controller", "Location",
            "PTag_TagArea", "PTag_DeviceClass", "PTag_LoopNumber", "PTag_TagNumber", "PTag_Suffix",
            "PTag_FullTag", "PTag_Separator",
            "Service", "ServiceEnglish", "InstrumentType", "InstrumentTypeEnglish", "PID",
            "SignalType", "ExType", "Subsystem", "Auxiliary",
            "VoltageLevel", "PowerConsumption",
            "Range_MIN", "Range_MAX", "Eng_Unit", "Range_MIN2", "Range_MAX2", "Eng_Unit2",
            "LL", "LL2", "L", "H", "HH", "HH2",
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
