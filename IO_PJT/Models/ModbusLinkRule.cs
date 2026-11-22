namespace IO_PJT.Models
{
    /// <summary>F9.11/F99: ручное правило связывания IO_LIST ↔ MODBUS_MAP (таблица
    /// MODBUS_LINK_RULES). Правило сопоставляет поле записи IO_LIST (IOField) со строкой
    /// карты: маска (Mask, см. MaskPattern) оценивается по колонкам точки → паттерн
    /// ищется в значении MapField; без маски — точное совпадение IOField ↔ MapField.
    /// F99: поля-шаблоны TagPattern/ValuePattern удалены (дублировали маску).</summary>
    public class ModbusLinkRule
    {
        /// <summary>ID правила в MODBUS_LINK_RULES (COUNTER); 0 — ещё не сохранено.</summary>
        public long Id { get; set; }

        /// <summary>Название правила (для журнала/окна; обязательно при сохранении).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Поле IO_LIST-стороны без маски: любая колонка FieldRegistry
        /// (Tag, Service, IoType, Loop, …).</summary>
        public string IOField { get; set; } = "Tag";

        /// <summary>Поле строки карты MODBUS_MAP: любое строковое свойство ModbusMapEntry
        /// (Description, Scale, PlcTag, FuncCode, DcsFunctions, …).</summary>
        public string MapField { get; set; } = "Description";

        /// <summary>п.10: маска сопоставления (см. MaskPattern). Пусто — полное совпадение
        /// IOField ↔ MapField. Непустая — маска оценивается по строке IO_LIST → паттерн
        /// ищется в значении MapField.</summary>
        public string Mask { get; set; } = string.Empty;

        /// <summary>Фильтр Function Code строки карты («02»/«03»/«06»); пусто — без фильтра.</summary>
        public string? FuncCode { get; set; }

        /// <summary>Приоритет: правило с меньшим значением применяется раньше (см. LoadEnabled).</summary>
        public int Priority { get; set; } = 100;

        /// <summary>Правило включено (выключенное хранится в БД, но в Link() не применяется).</summary>
        public bool Enabled { get; set; } = true;
    }
}