using System;
using System.Collections.Generic;

namespace IO_PJT.Models
{
    /// <summary>F13: политика пакетирования адресов в запрос Modbus (переключатель в окне
    /// «Генерация блоков Modbus (XML)»). Contiguous — разрыв адресов → новый блок
    /// (читаем только нужное); Cover — одно поле min..max, пока Count ≤ лимита (прорехи
    /// читаются вхолостую); FixedSize — фиксированные пакеты по лимиту Count адресов подряд
    /// от начального адреса группы; Gap — как Contiguous, но промежуток ≤ MaxGap
    /// допускается в одном пакете (читается вхолостую), > MaxGap → новый блок.</summary>
    public enum MbPacketPolicy
    {
        Contiguous = 0,
        Cover = 1,
        FixedSize = 2,
        Gap = 3
    }

    /// <summary>F13: источник XML-шаблона генерации блоков Modbus. File — файл шаблона
    /// (по умолчанию «MB_TCP_MASTER_пример_new.xml»); Inline — код, вставленный в окно
    /// и хранящийся в SCADA_MB_TEMPLATE (MEMO).</summary>
    public enum MbTemplateMode
    {
        File = 0,
        Inline = 1
    }

    /// <summary>F13: привязка одной точки IO_LIST к выходу DataOut блока мастера
    /// (MB_TCP_MASTER_4_1.DataOut[0] — индекс = адрес точки − Address пакета).</summary>
    public sealed class MbBinding
    {
        /// <summary>Тег точки IO_LIST (Tag).</summary>
        public string Tag { get; set; } = "";

        /// <summary>Адрес точки в устройстве (M_Address).</summary>
        public int Address { get; set; }

        /// <summary>Смещение адреса внутри пакета (Address − Address пакета) — индекс DataOut.</summary>
        public int Offset { get; set; }

        /// <summary>Имя блока мастера пакета (MB_TCP_MASTER_4_1).</summary>
        public string BlockName { get; set; } = "";

        /// <summary>Путь привязки данных: «{BlockName}.DataOut[{Offset}]» для чтения;
        /// F30: для записи (FC05/06/15/16) — «{BlockName}.DataIn[{байт}]» (та же колонка
        /// M_BusDataOut в IO_LIST хранит путь привязки).</summary>
        public string DataOutExpr { get; set; } = "";

        /// <summary>F30: true — привязка записи (пакет FC05/06/15/16, путь DataIn[]).</summary>
        public bool IsWrite { get; set; }

        /// <summary>F13.1: ширина точки в регистрах (1/2) — для выбора пары источников
        /// эталона привязки (REAL 32-бит = 2 регистра).</summary>
        public int Width { get; set; } = 1;

        /// <summary>F13.1: тип значения точки (M_DataType, напр. «32-Bit Floating») —
        /// определяет эталон цепочки привязки (BOOL/INT/DINT/REAL).</summary>
        public string DataType { get; set; } = "";

        /// <summary>F14: типовой алгоритм точки (IO_LIST Typno) — ключ ручного сопоставления
        /// SCADA_TYPNO_MAP (Typno → эталон шаблона привязок). Пуст — точка не участвует
        /// в генерации F14 (отфильтрована в BuildPlan).</summary>
        public string Typno { get; set; } = "";
    }

    /// <summary>F13: план одного запроса (пакета) — ровно один блок мастера из XML-шаблона,
    /// размноженный по пакетам (имя — от имени мастера шаблона + номер пакета).</summary>
    public sealed class MbPacketPlan
    {
        /// <summary>Порядковый номер пакета (с 1).</summary>
        public int No { get; set; }

        /// <summary>Станция Modbus (M_SlaveStation) — для отображения/группировки.</summary>
        public string Station { get; set; } = "";

        // —— параметры запроса (входы MB_TCP_MASTER) ——
        /// <summary>IP-адрес устройства (первый октет из M_SlaveIdMain).</summary>
        public int Ip1 { get; set; }
        public int Ip2 { get; set; }
        public int Ip3 { get; set; }
        public int Ip4 { get; set; }

        /// <summary>Порт Modbus TCP (из M_SlaveIdMain, иначе настройка DefaultPort).</summary>
        public int Port { get; set; } = 502;

        /// <summary>SlaveId (1-255) — по правилу «станция → SlaveId», иначе DefaultSlaveId.</summary>
        public int SlaveId { get; set; } = 1;

        /// <summary>F21: протокол станции из SCADA_MB_SLAVE_RULES («TCP»/«RTU»; без правила —
        /// «TCP»). Определяет, какой мастер-шаблон использовать при генерации (F22).</summary>
        public string Protocol { get; set; } = "TCP";

        /// <summary>F21: true — станция Modbus RTU (COM-порт/шина вместо TCP/IP).</summary>
        public bool IsRtu => string.Equals(Protocol, "RTU", StringComparison.OrdinalIgnoreCase);

        /// <summary>F23/F24: значения колонок IO_LIST первой точки пакета (колонка → текст;
        /// только непустые) — источник токенов {ИмяКолонки} в именах блоков/POU и маппинга
        /// переменных шаблона. Заполняется в BuildPlan.</summary>
        public Dictionary<string, string> ColumnValues { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>F23: подстановки входов мастера (имя входа → значение из колонки IO_LIST
        /// или литерал, SCADA_TEMPLATE_VARS) — заполняются в VM при экспорте; пусто —
        /// источник значения из плана (SlaveId/Mode/Address/Count/TimeOut).</summary>
        public Dictionary<string, string> VarValues { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Код функции чтения (1/2/3/4) — Mode блока MB_TCP_MASTER.</summary>
        public int FuncCode { get; set; } = 3;

        /// <summary>Mode входа блока (= код функции чтения).</summary>
        public int Mode => FuncCode;

        /// <summary>Начальный адрес пакета (минимальный адрес точек пакета).</summary>
        public int Address { get; set; }

        /// <summary>F14: значение входа Address блока в формате «регистр + адрес»
        /// (FormatBlockAddress при включённой настройке FormatAddress); заполняется
        /// в BuildPlan для отображения в превью.</summary>
        public string AddressInput { get; set; } = "";

        /// <summary>Число адресов в пакете (Count блока; регистры или биты).</summary>
        public int Count { get; set; }

        /// <summary>Таймаут ответа, мс (общий тег MB_*_TIMEOUT; min по точкам группы).</summary>
        public int TimeoutMs { get; set; } = 200;

        /// <summary>true — битовая функция (01/02), Count в битах; false — регистровая (03/04).
        /// F30: 05/15 (запись битов) тоже битовые.</summary>
        public bool IsBit => FuncCode == 1 || FuncCode == 2 || FuncCode == 5 || FuncCode == 15;

        /// <summary>F30: true — пакет записи (FC05/06/15/16); привязки точек пакета —
        /// DataIn[] вместо DataOut[], направление data-цепочки инвертировано (тег → DataIn).</summary>
        public bool IsWrite => FuncCode == 5 || FuncCode == 6 || FuncCode == 15 || FuncCode == 16;

        /// <summary>F30: направление для превью («чтение»/«запись»).</summary>
        public string Direction => IsWrite ? "запись" : "чтение";

        // —— имена/привязки ——
        /// <summary>Имя блока мастера пакета (от имени мастера шаблона: MB_TCP_MASTER_4_1 → _4_2, …).</summary>
        public string MasterName { get; set; } = "";

        /// <summary>Объект-подпись пакета (PLC Tag первого объекта).</summary>
        public string FirstTag { get; set; } = "";

        /// <summary>PLC-теги, попавшие в пакет (M_PlcTag, по порядку адресов).</summary>
        public List<string> Tags { get; } = new();

        /// <summary>Привязки точек пакета к выходам DataOut блока мастера.</summary>
        public List<MbBinding> Bindings { get; } = new();

        /// <summary>Число точек IO_LIST в пакете.</summary>
        public int PointCount => Tags.Count;

        /// <summary>Строка «a.b.c.d:port» для отображения.</summary>
        public string Endpoint => $"{Ip1}.{Ip2}.{Ip3}.{Ip4}:{Port}";
    }

    /// <summary>F13: результат планирования блоков (пакеты + статистика/предупреждения).</summary>
    public sealed class MbPlanResult
    {
        /// <summary>Планы пакетов (по одному блоку MB_TCP_MASTER на пакет).</summary>
        public List<MbPacketPlan> Packets { get; } = new();

        /// <summary>Точек с адресацией Modbus (M_PlcTag) — кандидаты.</summary>
        public int WithAddressing { get; set; }

        /// <summary>Точек пропущено (нет M_PlcTag / адресации Modbus).</summary>
        public int SkippedNoAddressing { get; set; }

        /// <summary>Точек с записью (FuncCode 06/15/16 — запись не генерируется).</summary>
        public int SkippedWrite { get; set; }

        /// <summary>Точек с неразборчивым адресом (M_Address не число).</summary>
        public int SkippedBadAddress { get; set; }

        /// <summary>Точек с неразборчивым IP (M_SlaveIdMain не содержит адреса).</summary>
        public int SkippedBadEndpoint { get; set; }

        /// <summary>Точек с неподдерживаемым кодом функции (не 01-04/06).</summary>
        public int SkippedBadFunc { get; set; }

        /// <summary>F14: точек без Typno — вне генерации (не в пакетах, не в привязках).</summary>
        public int SkippedNoTypno { get; set; }

        /// <summary>Предупреждения (разные таймауты в группе, усечённые имена, …).</summary>
        public List<string> Warnings { get; } = new();

        /// <summary>Всего точек во всех пакетах.</summary>
        public int TotalPoints { get { int n = 0; foreach (var p in Packets) n += p.PointCount; return n; } }
    }

    /// <summary>F13/F21: правило «станция Modbus (M_SlaveStation) → SlaveId и параметры
    /// станции» — таблица SCADA_MB_SLAVE_RULES. Числовой SlaveId в IO_LIST отсутствует,
    /// поэтому задаётся правилом или значением по умолчанию. F21: станция хранит параметры
    /// подключения (IP main/reserve, протокол, скорость, UART, таймаут, макс. длина) —
    /// авто-поля заполняются из IO_LIST («Заполнить из IO_LIST»), редактируются вручную.</summary>
    public sealed class MbSlaveIdRule
    {
        /// <summary>ID строки в SCADA_MB_SLAVE_RULES (COUNTER); 0 — ещё не сохранено.</summary>
        public long Id { get; set; }

        /// <summary>Станция Modbus (M_SlaveStation, напр. «IMCS EBSM102»); сравнение IgnoreCase.</summary>
        public string Station { get; set; } = "";

        /// <summary>SlaveId устройства (1-255) — вход SlaveId блока MB_TCP_MASTER.</summary>
        public int SlaveId { get; set; } = 1;

        // —— F21: параметры станции (авто из IO_LIST либо вручную) ——

        /// <summary>Основной адрес (M_SlaveIdMain: IP/маска/port либо COM-интерфейс) — авто.</summary>
        public string IpMain { get; set; } = "";

        /// <summary>Резервный адрес (M_SlaveIdBackup) — авто.</summary>
        public string IpReserve { get; set; } = "";

        /// <summary>Протокол: «TCP»/«RTU» (эвристика: в IpMain есть IPv4 → TCP, непустой
        /// не-IP → RTU; заполняется для новых/пустых, редактируется вручную).</summary>
        public string Protocol { get; set; } = "";

        /// <summary>Скорость обмена (M_Speed, напр. «100Mbps»/«9600») — авто.</summary>
        public string Speed { get; set; } = "";

        /// <summary>Чётность (вход Parity блока MB_RTU_MASTER; ручное в БД отсутствует).</summary>
        public bool Parity { get; set; }

        /// <summary>Стоп-биты (1/2; вход StopBit) — ручное.</summary>
        public int StopBit { get; set; } = 1;

        /// <summary>Биты данных (7/8; вход DataBit) — ручное.</summary>
        public int DataBit { get; set; } = 8;

        /// <summary>Реверс обмена (reverse) — ручное.</summary>
        public bool Reverse { get; set; }

        /// <summary>Таймаут станции, мс (M_Timeout, «100ms» → 100; 0 — пусто) — авто.</summary>
        public int Timeout { get; set; }

        /// <summary>Максимальная длина пакета (maximum length) — ручное.</summary>
        public int MaxLength { get; set; }

        /// <summary>F29: имя шаблона пакетов SCADA_MASTER_TEMPLATES для станции (выбор при
        /// экспорте); пусто — fallback на MasterTcpTemplateName/MasterRtuTemplateName по Protocol
        /// (обратная совместимость F22).</summary>
        public string MasterTemplate { get; set; } = "";
    }

    /// <summary>F14/F16: правило ручного сопоставления «Typno → (шаблон привязок, эталон)» —
    /// таблица SCADA_TYPNO_MAP. Typno выбирает, из какого именованного шаблона Data_Out
    /// (F16, пусто — основной/текущий шаблон редактора) и какой эталон (Disc OnePOU)
    /// клонировать для сигналов этого алгоритма; все Tag с тем же Typno получают цепочки
    /// этого эталона (по одной цепочке на тег).</summary>
    public sealed class MbTypnoMapRule
    {
        /// <summary>ID строки в SCADA_TYPNO_MAP (COUNTER); 0 — ещё не сохранено.</summary>
        public long Id { get; set; }

        /// <summary>Типовой алгоритм (IO_LIST Typno, напр. «PID1200-01R»); сравнение IgnoreCase.</summary>
        public string Typno { get; set; } = "";

        /// <summary>F16: имя шаблона привязок из SCADA_DATAOUT_TEMPLATES (IgnoreCase);
        /// пусто — основной шаблон (файл/вставленный код редактора).</summary>
        public string Template { get; set; } = "";

        /// <summary>Ключ эталона в шаблоне привязок (Disc/NAME OnePOU: BOOL/INT/DINT/REAL).</summary>
        public string Exemplar { get; set; } = "";
    }

    /// <summary>F16: именованный шаблон привязок Data_Out — таблица SCADA_DATAOUT_TEMPLATES.
    /// Каждый сохранённый шаблон (файл или вставленный код, присвоено имя) может
    /// использоваться в генерации: Typno → имя шаблона в SCADA_TYPNO_MAP.</summary>
    public sealed class MbDataOutTemplate
    {
        /// <summary>ID строки в SCADA_DATAOUT_TEMPLATES (COUNTER); 0 — ещё не сохранено.</summary>
        public long Id { get; set; }

        /// <summary>Имя шаблона (идентификатор для SCADA_TYPNO_MAP); IgnoreCase, уникально.</summary>
        public string Name { get; set; } = "";

        /// <summary>XML-тело шаблона (BufScadaPOUS с эталонами BOOL/INT/DINT/REAL).</summary>
        public string Body { get; set; } = "";

        /// <summary>Путь к файлу-источнику (справочно; «Обновить из файла» перечитывает Body).</summary>
        public string FilePath { get; set; } = "";
    }

    /// <summary>F22: именованный мастер-шаблон блоков Modbus — таблица SCADA_MASTER_TEMPLATES.
    /// Protocol (TCP/RTU) выбирает шаблон для пакетов соответствующей станции при экспорте;
    /// эвристика при сохранении: в имени есть «RTU» → RTU, иначе TCP.</summary>
    public sealed class MbMasterTemplate
    {
        /// <summary>ID строки в SCADA_MASTER_TEMPLATES (COUNTER); 0 — ещё не сохранено.</summary>
        public long Id { get; set; }

        /// <summary>Имя шаблона (идентификатор для SCADA_MB_SETTINGS MasterTcp/RtuTemplateName).</summary>
        public string Name { get; set; } = "";

        /// <summary>Протокол шаблона: «TCP»/«RTU» (нормализуется при сохранении).</summary>
        public string Protocol { get; set; } = "TCP";

        /// <summary>XML-тело шаблона (BufScadaPOUS с блоком мастера).</summary>
        public string Body { get; set; } = "";

        /// <summary>Путь к файлу-источнику (справочно).</summary>
        public string FilePath { get; set; } = "";
    }

    /// <summary>F22: именованный шаблон привязок DataIn — таблица SCADA_DATAIN_TEMPLATES
    /// (вход DataIn = массив записи в Slave). Хранение/список/маппинг F23; генерации
    /// привязок DataIn нет — шаблоны добавляются по мере появления.</summary>
    public sealed class MbDataInTemplate
    {
        /// <summary>ID строки в SCADA_DATAIN_TEMPLATES (COUNTER); 0 — ещё не сохранено.</summary>
        public long Id { get; set; }

        /// <summary>Имя шаблона (из имени файла при загрузке); IgnoreCase, уникально.</summary>
        public string Name { get; set; } = "";

        /// <summary>XML-тело шаблона.</summary>
        public string Body { get; set; } = "";

        /// <summary>Путь к файлу-источнику (справочно).</summary>
        public string FilePath { get; set; } = "";
    }

    /// <summary>F23: строка маппинга «переменная (вход) мастер-шаблона → колонка IO_LIST
    /// или литерал» — таблица SCADA_TEMPLATE_VARS (Scope='master'; dataout/datain — скрытые
    /// скоупы для будущего применения). Приоритет: IoColumn (значение колонки первой точки
    /// пакета) → Literal → источник из плана (SlaveId/Mode/Address/Count/TimeOut).</summary>
    public sealed class MbTemplateVar
    {
        /// <summary>ID строки в SCADA_TEMPLATE_VARS (COUNTER); 0 — ещё не сохранено.</summary>
        public long Id { get; set; }

        /// <summary>Скоуп: «master»/«dataout»/«datain».</summary>
        public string Scope { get; set; } = "master";

        /// <summary>Имя шаблона-владельца (пусто — общий маппинг скоупа).</summary>
        public string TemplateName { get; set; } = "";

        /// <summary>Переменная — имя входа мастера (SlaveId/TimeOut/Mode/Address/Count/…).</summary>
        public string Var { get; set; } = "";

        /// <summary>Колонка IO_LIST (FieldRegistry); пусто — используется Literal.</summary>
        public string IoColumn { get; set; } = "";

        /// <summary>Фиксированное значение, когда колонка не задана.</summary>
        public string Literal { get; set; } = "";
    }

    /// <summary>F31: строка объединённого перечня data-шаблонов окна генерации — шаблон
    /// привязок чтения (SCADA_DATAOUT_TEMPLATES, «DataOut[] → тег») или записи
    /// (SCADA_DATAIN_TEMPLATES, «тег → DataIn[]»). Тело (Body) живёт в исходных списках —
    /// строка только отображает принадлежность и путь.</summary>
    public sealed class MbDataTemplateRow
    {
        /// <summary>Направление: «чтение» (DataOut) / «запись» (DataIn).</summary>
        public string Kind { get; set; } = "чтение";

        /// <summary>true — шаблон записи (SCADA_DATAIN_TEMPLATES).</summary>
        public bool IsWrite => Kind == "запись";

        /// <summary>Имя шаблона (ключ SCADA_TYPNO_MAP / идентификатор).</summary>
        public string Name { get; set; } = "";

        /// <summary>Путь к файлу-источнику (справочно).</summary>
        public string FilePath { get; set; } = "";
    }

    /// <summary>F28: строка фильтра станций в окне генерации блоков — галочка «генерить/не генерить»
    /// при экспорте XML и записи привязок (пункт 4 ТЗ). Счётчик пакетов — из плана превья.</summary>
    public sealed class MbStationFilterItem
    {
        /// <summary>Станция (M_SlaveStation, ключ группировки пакетов).</summary>
        public string Station { get; set; } = "";

        /// <summary>true — станция генерится (по умолчанию все выбранные).</summary>
        public bool IsSelected { get; set; } = true;

        /// <summary>Число пакетов станции в построенном плане (для подсказки в UI).</summary>
        public int PacketCount { get; set; }
    }

    /// <summary>F13: итог записи привязок в IO_LIST (M_BusBlock / M_BusDataOut) —
    /// кнопка «Записать привязки в IO_LIST».</summary>
    public sealed class MbBindingsWriteResult
    {
        /// <summary>Обновлено точек IO_LIST (изменены M_BusBlock/M_BusDataOut).</summary>
        public int Updated { get; set; }

        /// <summary>Очищено точек (есть адресация M_PlcTag, но в пакеты не попали — привязки сняты).</summary>
        public int Cleared { get; set; }

        /// <summary>Тегов привязок не найден в IO_LIST.</summary>
        public int NotFound { get; set; }

        /// <summary>Ошибки (не Modbus-сигнал по IO Type и т.п. — привязка не записана).</summary>
        public List<string> Errors { get; } = new();

        /// <summary>Итоговый текст для MessageBox.</summary>
        public string Summary =>
            $"Привязки записаны в IO_LIST: обновлено точек: {Updated}; очищено (не в пакетах): {Cleared}; " +
            $"тегов не найдено: {NotFound}; ошибок: {Errors.Count}";
    }

    /// <summary>F13: настройки генерации блоков Modbus (XML) — персистятся в SCADA_MB_SETTINGS
    /// (Name/Value), переживают перезапуск. Значения по умолчанию сняты с образца
    /// «MB_TCP_MASTER_пример.xml» и документации блока (IO_PJT/Modbus RTU_TCP block.md).</summary>
    public sealed class ScadaMbGenSettings
    {
        // —— пакетирование ——
        /// <summary>Политика пакетирования (по умолчанию — непрерывные диапазоны).</summary>
        public MbPacketPolicy Policy { get; set; } = MbPacketPolicy.Contiguous;

        /// <summary>F20: максимум регистров в пакете (FC03/04); фактический лимит =
        /// min(MaxCountRegisters, 123 — лимит блока). 0 — без ограничения пользователя
        /// (= константа блока). Валидация: 0..123 (подрезка min с MaxRegisters).</summary>
        public int MaxCountRegisters { get; set; } = 100;

        /// <summary>F20: максимум битов в пакете (FC01/02); фактический лимит =
        /// min(MaxCountBits, 246 — лимит блока). 0 — без ограничения пользователя
        /// (= константа блока). Валидация: 0..246 (подрезка min с MaxBits).</summary>
        public int MaxCountBits { get; set; } = 100;

        /// <summary>F13.1: допустимый промежуток адресов внутри пакета (политика Gap):
        /// разрыв ≤ MaxGap — сигналы в одном блоке (прореха читается вхолостую),
        /// > MaxGap — новый блок. 0 — без промежутков (= Contiguous).</summary>
        public int MaxGap { get; set; } = 0;

        /// <summary>Порт по умолчанию, если в M_SlaveIdMain не указан (Modbus TCP — 502).</summary>
        public int DefaultPort { get; set; } = 502;

        /// <summary>SlaveId по умолчанию, если станция не описана правилом SCADA_MB_SLAVE_RULES.</summary>
        public int DefaultSlaveId { get; set; } = 1;

        /// <summary>Таймаут по умолчанию, мс, если M_Timeout пуст (ивалон — 200).</summary>
        public int DefaultTimeoutMs { get; set; } = 200;

        /// <summary>F22: копия настроек — для второго прохода Write по RTU-шаблону
        /// (с BaseT11Id/BaseCardId, сдвинутыми за пределы первого результата).</summary>
        public ScadaMbGenSettings Clone() => new ScadaMbGenSettings
        {
            Policy = Policy, MaxCountRegisters = MaxCountRegisters, MaxCountBits = MaxCountBits,
            MaxGap = MaxGap, DefaultPort = DefaultPort, DefaultSlaveId = DefaultSlaveId,
            DefaultTimeoutMs = DefaultTimeoutMs, TemplateMode = TemplateMode,
            TemplateFilePath = TemplateFilePath, DataOutTemplateMode = DataOutTemplateMode,
            DataOutTemplateFilePath = DataOutTemplateFilePath, DataOutTemplateName = DataOutTemplateName,
            MasterTcpTemplateName = MasterTcpTemplateName, MasterRtuTemplateName = MasterRtuTemplateName,
            BlockNamePattern = BlockNamePattern, BlockNamePatternRtu = BlockNamePatternRtu,
            PouNamePattern = PouNamePattern, DataOutPouNamePattern = DataOutPouNamePattern,
            FormatAddress = FormatAddress, BaseT11Id = BaseT11Id, BaseCardId = BaseCardId,
            ColumnsPerRow = ColumnsPerRow, RowStep = RowStep, ColumnStep = ColumnStep
        };

        // —— XML-шаблон ——
        /// <summary>Источник шаблона: файл (File) или вставленный код (Inline, SCADA_MB_TEMPLATE).</summary>
        public MbTemplateMode TemplateMode { get; set; } = MbTemplateMode.File;

        /// <summary>Путь к файлу шаблона; пуст — авто-поиск «MB_TCP_MASTER_пример_new.xml»
        /// (каталог exe, потом Work\, затем встроенная копия).</summary>
        public string TemplateFilePath { get; set; } = "";

        /// <summary>F22: имя мастер-шаблона Modbus TCP из SCADA_MASTER_TEMPLATES, которым
        /// генерируются пакеты TCP; пуст — текущий шаблон редактора (историческое).</summary>
        public string MasterTcpTemplateName { get; set; } = "";

        /// <summary>F22: имя мастер-шаблона Modbus RTU из SCADA_MASTER_TEMPLATES для пакетов
        /// RTU-станций; пуст — RTU-пакеты пропускаются с предупреждением.</summary>
        public string MasterRtuTemplateName { get; set; } = "";

        // —— F13.1: шаблон привязок (out → tagname) и имя блока ——
        /// <summary>Источник шаблона привязок: файл или вставленный код (SCADA_MB_TEMPLATE,
        /// строка Name='dataout'). Пуст/нет шаблона — простые привязки «блок 31 на пару
        /// (блок, индекс)» (историческое поведение).</summary>
        public MbTemplateMode DataOutTemplateMode { get; set; } = MbTemplateMode.File;

        /// <summary>Путь к файлу шаблона привязок; пуст — авто-поиск
        /// «Data_Out_пример.xml» (каталог exe → Work\ → встроенная копия).</summary>
        public string DataOutTemplateFilePath { get; set; } = "";

        /// <summary>F16: имя текущего шаблона привязок в редакторе (SCADA_DATAOUT_TEMPLATES).
        /// Идентифицирует шаблон при «Сохранить под именем…»; пусто — имя ещё не присвоено
        /// (для файла подсказывается имя файла без расширения).</summary>
        public string DataOutTemplateName { get; set; } = "";

        /// <summary>F13.1: шаблон имени блока мастера. Пуст — историческое правило
        /// (цифры имени шаблона + номер пакета). Иначе — маска с токенами:
        /// {Seq} — номер пакета, {Slave} — SlaveId, {Station} — станция M_SlaveStation,
        /// {Ip} — IP через «_», {Func} — код функции. Пример: «MB_TCP_Master_{Slave}_{Seq}».</summary>
        public string BlockNamePattern { get; set; } = "";

        /// <summary>F14: шаблон имени блока для Modbus RTU (мастер шаблона с «RTU» в имени).
        /// Пуст — fallback на BlockNamePattern. Токены те же.</summary>
        public string BlockNamePatternRtu { get; set; } = "";

        /// <summary>F17: шаблон имени POU в основном файле BufScadaPOUS (мастер-блоки).
        /// Пуст — историческое поведение: NAME из шаблона (MB_Master_4). Токены:
        /// {Master} — имя мастера шаблона, {Station}/{Slave}/{Ip}/{Port}/{Func}/{Seq} —
        /// контекст ПЕРВОГО пакета (POU один на все пакеты), {Date}/{Time} — момент экспорта.</summary>
        public string PouNamePattern { get; set; } = "";

        /// <summary>F17: шаблон имени POU в файле привязок (dataout) — один POU на пакет.
        /// Пуст — историческое поведение: NAME = имя блока мастера пакета
        /// (MB_TCP_MASTER_4_1). Токены: {Master} — имя блока мастера пакета, {Seq} — №
        /// пакета, {Slave}, {Station}, {Ip}, {Port}, {Func}, {Date}/{Time}; дубли имени
        /// внутри файла получают суффиксы _2, _3 (SCADA требует уникальности POU).</summary>
        public string DataOutPouNamePattern { get; set; } = "";

        /// <summary>F14: формат входа Address блока: true (default) — «регистр + адрес»
        /// (FC3: 1 → 4001, 602 → 4602, 1506 → 41506; адрес ≥ 10000 — как есть);
        /// false — сырой адрес M_Address.</summary>
        public bool FormatAddress { get; set; } = true;

        // —— графика/раскладка ——
        /// <summary>Стартовый T11ID для клонов шаблона (уникальность — счётчиком от максимума шаблона).</summary>
        public int BaseT11Id { get; set; } = 1000000;

        /// <summary>Стартовый cardId для клонов (уникальность — счётчиком, шаг 10).</summary>
        public int BaseCardId { get; set; } = 500000;

        /// <summary>Число запросов в строке раскладки POU (ивалон — 2).</summary>
        public int ColumnsPerRow { get; set; } = 2;

        /// <summary>Шаг по вертикали между запросами, px.</summary>
        public int RowStep { get; set; } = 560;

        /// <summary>Шаг по горизонтали между запросами, px.</summary>
        public int ColumnStep { get; set; } = 1150;
    }
}


