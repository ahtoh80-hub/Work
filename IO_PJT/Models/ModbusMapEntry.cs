namespace IO_PJT.Models
{
    /// <summary>F9: строка карты адресации Modbus — модель файла «IMCS TO DCS Register Table Modbus»
    /// (лист Interface parameters + лист Register Table Function 02/03/06).</summary>
    public class ModbusMapEntry
    {
        /// <summary>ID строки в MODBUS_MAP (COUNTER); 0 — ещё не записана.</summary>
        public long Id { get; set; }

        /// <summary>Имя файла карты (REV_FileName-аналог для MODBUS_MAP).</summary>
        public string MapFile { get; set; } = string.Empty;

        /// <summary>Лист-источник: «Register Table Function 02/03/06».</summary>
        public string MapSheet { get; set; } = string.Empty;

        /// <summary>Номер строки Excel в листе карты.</summary>
        public int MapRow { get; set; }

        // —— Interface parameters (станция; значения общи всем строкам листа) ——
        public string? SlaveStation { get; set; }
        public string? SlaveIdMain { get; set; }
        public string? SlaveIdBackup { get; set; }
        public string? Speed { get; set; }
        public string? Timeout { get; set; }

        // —— Register Table (колонки A..U, шапка строка 2) ——
        /// <summary>B: Project Functional Designation.</summary>
        public string? PrfDesignation { get; set; }
        /// <summary>C: PLC Tag/Variable Name (полное имя, напр. 1115-PM-101A_STA.MCU).</summary>
        public string? PlcTag { get; set; }
        /// <summary>Базовый тег объекта: PlcTag до первого «_» (вычисляется).</summary>
        public string? ObjectTag { get; set; }
        /// <summary>D: Description of the variable.</summary>
        public string? Description { get; set; }
        /// <summary>E: Units of measurement.</summary>
        public string? Units { get; set; }
        /// <summary>F: Scale.</summary>
        public string? Scale { get; set; }
        /// <summary>G: Low Low alarm value.</summary>
        public string? LL { get; set; }
        /// <summary>H: Low alarm value.</summary>
        public string? LA { get; set; }
        /// <summary>I: High alarm value.</summary>
        public string? HA { get; set; }
        /// <summary>J: High High alarm value.</summary>
        public string? HH { get; set; }
        /// <summary>K: Scaling factors.</summary>
        public string? Scaling { get; set; }
        /// <summary>L: Signal type (Impulse/Permanent).</summary>
        public string? SigType { get; set; }
        /// <summary>M: Register type (1/4/6).</summary>
        public string? RegType { get; set; }
        /// <summary>N: Address/Bit.</summary>
        public string? Address { get; set; }
        /// <summary>O: Access type (Read/Write).</summary>
        public string? AccessType { get; set; }
        /// <summary>P: Data type (BOOL / 16-Bit Unsigned / 32-Bit Float…).</summary>
        public string? DataType { get; set; }
        /// <summary>Q: Function code (02/03/06).</summary>
        public string? FuncCode { get; set; }
        /// <summary>R: DCS channel.</summary>
        public string? DcsChannel { get; set; }
        /// <summary>S: DCS Tag/Name variable — тег РСУ (в исходниках пуст, заполняется связыванием).</summary>
        public string? DcsTag { get; set; }
        /// <summary>T: DCS functions.</summary>
        public string? DcsFunctions { get; set; }
        /// <summary>U: note.</summary>
        public string? Note { get; set; }

        /// <summary>Связанный тег РСУ (IO_LIST.Tag), заполняется ModbusLinkService; пуст — строка без привязки.
        /// Несколько команд, делящих один Control word, перечисляются через запятую.</summary>
        public string? LinkTag { get; set; }

        /// <summary>F9.3: состояние привязки строки карты после связывания:
        /// «привязано» / «не привязано» / null — связывание ещё не выполнялось.</summary>
        public string? LinkState { get; set; }
    }

    /// <summary>F9: итог импорта карты Modbus одного файла.</summary>
    public class ModbusMapImportResult
    {
        /// <summary>Имя файла карты.</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>Имя Slave-станции (Interface parameters).</summary>
        public string? SlaveStation { get; set; }

        /// <summary>Прочитано строк Register Table (все листы Function 02/03/06).</summary>
        public int RowsRead { get; set; }

        /// <summary>Записано строк в MODBUS_MAP.</summary>
        public int RowsInserted { get; set; }
    }

    /// <summary>F9: итог связывания IO_LIST с картой Modbus.
    /// F9.4: при импорте M_-поля в IO_LIST НЕ записываются — только связь (LinkTag/LinkState/DcsTag);
    /// запись адресации выполняется кнопкой «Обновить адресацию Modbus» (UpdateAddressing).</summary>
    public class ModbusLinkResult
    {
        /// <summary>Проверено записей IO_LIST.</summary>
        public int Examined { get; set; }

        /// <summary>Связано со строкой карты (заполнены LinkTag/DcsTag строки карты; M_-поля — по кнопке).</summary>
        public int Linked { get; set; }

        /// <summary>Найдено строк карты (суммарно по связанным тегам).</summary>
        public int MapRowsMatched { get; set; }

        /// <summary>Не связанных записей (без кандидата-объекта или без строки карты).</summary>
        public int Unlinked { get; set; }

        /// <summary>F9.3: строк карты с LinkState=«привязано» после связывания.</summary>
        public int MapRowsLinked { get; set; }

        /// <summary>F9.3: строк карты с LinkState=«не привязано».</summary>
        public int MapRowsUnlinked { get; set; }

        /// <summary>F9.11: точек связано ручными правилами MODBUS_LINK_RULES (правила приоритетнее DetectRole).</summary>
        public int LinkedByRule { get; set; }

        /// <summary>F9.11: срабатывания правил: имя правила → количество связанных точек.</summary>
        public Dictionary<string, int> RuleHits { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>F9.4: ошибки связывания — связь с точкой, у которой IO Type не признак Modbus
        /// (нет «M…»/«COM»): такая связь не записывается в карту.</summary>
        public List<string> Errors { get; } = new();

        /// <summary>Примеры несопоставленных тегов (до 20) — для журнала/превью.</summary>
        public List<string> UnlinkedSamples { get; } = new();

        /// <summary>F9.3: строки карты без привязки (до 10 PlcTag) — для превью окна.</summary>
        public List<string> MapUnlinkedSamples { get; } = new();

        /// <summary>Итоговый текст (F9.4: без «обновлено в БД» — M_-поля пишет кнопка обновления адресации).</summary>
        public string Summary =>
            $"Проверено: {Examined}; связано: {Linked}; " +
            $"строк карты найдено: {MapRowsMatched}; не связано: {Unlinked}; " +
            $"карта: привязано {MapRowsLinked}, без привязки {MapRowsUnlinked}" +
            (Errors.Count > 0 ? $"; ошибок: {Errors.Count}" : "") +
            (LinkedByRule > 0 ? $"; правилом F9.11: {LinkedByRule}" : "") +
            "; M_-поля не записаны (кнопка «Обновить адресацию Modbus»)";
    }

    /// <summary>F9.4: итог кнопки «Обновить адресацию Modbus» — запись M_-полей IO_LIST
    /// по парам MODBUS_MAP.DcsTag ↔ IO_LIST.Tag.</summary>
    public class ModbusAddressingResult
    {
        /// <summary>Обработано связей DcsTag (уникальных тегов в колонке DcsTag).</summary>
        public int Examined { get; set; }

        /// <summary>Обновлено записей IO_LIST (были изменения M_-полей).</summary>
        public int Updated { get; set; }

        /// <summary>Тег из DcsTag не найден в IO_LIST (примеры — в NotFoundSamples).</summary>
        public int NotFound { get; set; }

        /// <summary>Ошибки: в карте указан DcsTag не-Modbus-сигнала (IO Type без признака M*/COM) —
        /// M_-поля не заполняются.</summary>
        public List<string> Errors { get; } = new();

        /// <summary>Примеры тегов DcsTag, отсутствующих в IO_LIST (до 10).</summary>
        public List<string> NotFoundSamples { get; } = new();

        /// <summary>Итоговый текст.</summary>
        public string Summary =>
            $"Обновление адресации Modbus: связей DcsTag: {Examined}; обновлено записей IO_LIST: {Updated}; " +
            $"тегов не найдено: {NotFound}; ошибок: {Errors.Count}";
    }
}