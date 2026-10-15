namespace IO_PJT.Models
{
    /// <summary>F9.11: ручное правило связывания IO_LIST ↔ MODBUS_MAP (таблица MODBUS_LINK_RULES).
    /// Сопоставляет поле записи IO_LIST (по умолчанию Tag) по шаблону TagPattern с полем строки
    /// карты MapField по шаблону ValuePattern — применяется в Link() ПЕРЕД встроенными ролями
    /// DetectRole (ручные правила приоритетнее эвристик). Шаблоны: начинается с «*»/«?» либо
    /// содержит только wildcard-метки → wildcard (анкорный, «*»=любые символы, «?»=один, IgnoreCase);
    /// содержит метасимволы regex (. + ^ $ ( ) [ ] { } | \) → регулярное выражение
    /// (примеры: TagPattern «MXA+A» (regex) → MapField Description «*Fault*» (wildcard)).</summary>
    public class ModbusLinkRule
    {
        /// <summary>ID правила в MODBUS_LINK_RULES (COUNTER); 0 — ещё не сохранено.</summary>
        public long Id { get; set; }

        /// <summary>Название правила (для журнала/окна; обязательно при сохранении).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Поле IO_LIST-стороны: Tag / Service / ServiceEnglish / IoType / Loop / SignalType.</summary>
        public string IOField { get; set; } = "Tag";

        /// <summary>Шаблон по значению IOField (например «*MXA*A» либо regex «MXA+A»).</summary>
        public string TagPattern { get; set; } = string.Empty;

        /// <summary>Поле строки карты MODBUS_MAP: любое строковое свойство ModbusMapEntry
        /// (Description, Scale, PlcTag, FuncCode, DcsFunctions, …).</summary>
        public string MapField { get; set; } = "Description";

        /// <summary>Шаблон по значению MapField (например «*Fault*»).</summary>
        public string ValuePattern { get; set; } = string.Empty;

        /// <summary>Фильтр Function Code строки карты («02»/«03»/«06»); пусто — без фильтра.</summary>
        public string? FuncCode { get; set; }

        /// <summary>Приоритет: правило с меньшим значением применяется раньше (см. LoadEnabled).</summary>
        public int Priority { get; set; } = 100;

        /// <summary>Правило включено (выключенное хранится в БД, но в Link() не применяется).</summary>
        public bool Enabled { get; set; } = true;
    }
}
