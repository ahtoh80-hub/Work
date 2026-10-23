using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

        /// <summary>F35: колонки IO_LIST СВОЕЙ точки привязки (ColumnValuesOf(IoPoint),
        /// заполняются в BuildPlan) — источник разрешения маппинга «Объекты data-шаблонов»
        /// для этой привязки (пер-точечные значения: адрес 579 → _1130_MXA_01301A из
        /// M_Marka этой точки, а не первой точки пакета). Пусто — колонки пакета.</summary>
        public Dictionary<string, string> ColumnValues { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>F13: план одного запроса (пакета) — ровно один блок мастера из XML-шаблона,
    /// размноженный по пакетам (имя — от имени мастера шаблона + номер пакета).
    /// F34: INPC — параметры пакета редактируются в превью (SlaveId/FuncCode/Address/Count/
    /// TimeoutMs/IP:порт), правки применяются при экспорте.</summary>
    public sealed class MbPacketPlan : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnChanged(name);
            return true;
        }

        private int _no;
        /// <summary>Порядковый номер пакета (с 1).</summary>
        public int No { get => _no; set => Set(ref _no, value); }

        private string _station = "";
        /// <summary>Станция Modbus (M_SlaveStation) — для отображения/группировки.</summary>
        public string Station { get => _station; set => Set(ref _station, value ?? ""); }

        private string _controller = "";
        /// <summary>F37: контроллер IO_LIST (Identification.Controller) — ключ группировки
        /// пакетов (Controller, Station, Typno, IP, порт, функция): одна станция в разных
        /// контроллерах даёт раздельные пакеты и своё адресное пространство.</summary>
        public string Controller { get => _controller; set => Set(ref _controller, value ?? ""); }

        // —— параметры запроса (входы MB_TCP_MASTER) ——
        private int _ip1;
        /// <summary>IP-адрес устройства (первый октет из M_SlaveIdMain).</summary>
        public int Ip1 { get => _ip1; set { if (Set(ref _ip1, value)) OnChanged(nameof(Endpoint)); } }
        private int _ip2;
        public int Ip2 { get => _ip2; set { if (Set(ref _ip2, value)) OnChanged(nameof(Endpoint)); } }
        private int _ip3;
        public int Ip3 { get => _ip3; set { if (Set(ref _ip3, value)) OnChanged(nameof(Endpoint)); } }
        private int _ip4;
        public int Ip4 { get => _ip4; set { if (Set(ref _ip4, value)) OnChanged(nameof(Endpoint)); } }

        private int _port = 502;
        /// <summary>Порт Modbus TCP (из M_SlaveIdMain, иначе настройка DefaultPort).</summary>
        public int Port { get => _port; set { if (Set(ref _port, value)) OnChanged(nameof(Endpoint)); } }

        private int _slaveId = 1;
        /// <summary>SlaveId (1-255) — по правилу «станция → SlaveId», иначе DefaultSlaveId.</summary>
        public int SlaveId { get => _slaveId; set => Set(ref _slaveId, value); }

        private string _protocol = "TCP";
        /// <summary>F21: протокол станции из SCADA_MB_SLAVE_RULES («TCP»/«RTU»; без правила —
        /// «TCP»). Определяет, какой мастер-шаблон использовать при генерации (F22).</summary>
        public string Protocol { get => _protocol; set => Set(ref _protocol, value ?? "TCP"); }

        /// <summary>F21: true — станция Modbus RTU (COM-порт/шина вместо TCP/IP).</summary>
        public bool IsRtu => string.Equals(Protocol, "RTU", StringComparison.OrdinalIgnoreCase);

        /// <summary>F23/F24: значения колонок IO_LIST первой точки пакета (колонка → текст;
        /// только непустые) — источник токенов {ИмяКолонки} в именах блоков/POU и маппинга
        /// переменных шаблона. Заполняется в BuildPlan.</summary>
        public Dictionary<string, string> ColumnValues { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        // F35: VarValues (маппинг входов мастера F23, приоритет в MasterInputValue) удалён —
        // недопустимы скрытые подстановки; входы мастера строго из плана (превью)

        private int _funcCode = 3;
        /// <summary>Код функции чтения (1/2/3/4) — Mode блока MB_TCP_MASTER.</summary>
        public int FuncCode
        {
            get => _funcCode;
            set
            {
                if (!Set(ref _funcCode, value)) return;
                // F34: вычисляемые от кода функции — обновляются в превью при правке
                OnChanged(nameof(Mode));
                OnChanged(nameof(IsBit));
                OnChanged(nameof(IsWrite));
                OnChanged(nameof(Direction));
            }
        }

        /// <summary>Mode входа блока (= код функции чтения).</summary>
        public int Mode => FuncCode;

        private int _address;
        /// <summary>Начальный адрес пакета (минимальный адрес точек пакета).</summary>
        public int Address { get => _address; set => Set(ref _address, value); }

        private string _addressInput = "";
        /// <summary>F14: значение входа Address блока в формате «регистр + адрес»
        /// (FormatBlockAddress при включённой настройке FormatAddress); заполняется
        /// в BuildPlan для отображения в превью.</summary>
        public string AddressInput { get => _addressInput; set => Set(ref _addressInput, value ?? ""); }

        private int _count;
        /// <summary>Число адресов в пакете (Count блока; регистры или биты).</summary>
        public int Count { get => _count; set => Set(ref _count, value); }

        private int _timeoutMs = 200;
        /// <summary>Таймаут ответа, мс (общий тег MB_*_TIMEOUT; min по точкам группы).</summary>
        public int TimeoutMs { get => _timeoutMs; set => Set(ref _timeoutMs, value); }

        /// <summary>true — битовая функция (01/02), Count в битах; false — регистровая (03/04).
        /// F30: 05/15 (запись битов) тоже битовые.</summary>
        public bool IsBit => FuncCode == 1 || FuncCode == 2 || FuncCode == 5 || FuncCode == 15;

        /// <summary>F30: true — пакет записи (FC05/06/15/16); привязки точек пакета —
        /// DataIn[] вместо DataOut[], направление data-цепочки инвертировано (тег → DataIn).</summary>
        public bool IsWrite => FuncCode == 5 || FuncCode == 6 || FuncCode == 15 || FuncCode == 16;

        /// <summary>F30: направление для превью («чтение»/«запись»).</summary>
        public string Direction => IsWrite ? "запись" : "чтение";

        // —— имена/привязки ——
        private string _masterName = "";
        /// <summary>Имя блока мастера пакета (от имени мастера шаблона: MB_TCP_MASTER_4_1 → _4_2, …).</summary>
        public string MasterName { get => _masterName; set => Set(ref _masterName, value ?? ""); }

        private string _firstTag = "";
        /// <summary>Объект-подпись пакета (PLC Tag первого объекта).</summary>
        public string FirstTag { get => _firstTag; set => Set(ref _firstTag, value ?? ""); }

        /// <summary>PLC-теги, попавшие в пакет (M_PlcTag, по порядку адресов).</summary>
        public List<string> Tags { get; } = new();

        /// <summary>Привязки точек пакета к выходам DataOut блока мастера.</summary>
        public List<MbBinding> Bindings { get; } = new();

        /// <summary>Число точек IO_LIST в пакете.</summary>
        public int PointCount => Tags.Count;

        /// <summary>Строка «a.b.c.d:port» для отображения/редактирования (F34: сеттер
        /// парсит введённый адрес — Ip1..Ip4/Port; невалидная строка игнорируется).</summary>
        public string Endpoint
        {
            get => $"{Ip1}.{Ip2}.{Ip3}.{Ip4}:{Port}";
            set
            {
                var parts = (value ?? "").Trim().Split(':', 2);
                var oct = parts[0].Split('.');
                if (parts.Length != 2 || oct.Length != 4) return;
                if (!int.TryParse(parts[1], out int port) || port < 0 || port > 65535) return;
                var ip = new int[4];
                for (int i = 0; i < 4; i++)
                    if (!int.TryParse(oct[i], out ip[i]) || ip[i] < 0 || ip[i] > 255) return;
                Ip1 = ip[0]; Ip2 = ip[1]; Ip3 = ip[2]; Ip4 = ip[3]; Port = port;
            }
        }
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

        /// <summary>F37: контроллер IO_LIST (Identification.Controller); пусто — правило
        /// универсальное (действует для всех контроллеров станции). Сравнение IgnoreCase.</summary>
        public string Controller { get; set; } = "";

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

    /// <summary>F14/F16/F33: правило ручного сопоставления «Typno → (шаблон привязок, эталон,
    /// размер)» — таблица SCADA_TYPNO_MAP. Typno выбирает, из какого именованного шаблона Data_Out
    /// (F16, пусто — основной/текущий шаблон редактора) и какой эталон (Disc OnePOU)
    /// клонировать для сигналов этого алгоритма; все Tag с тем же Typno получают цепочки
    /// этого эталона (по одной цепочке на тег). F33: Size — размер переменной шаблона
    /// (32bit = 4 байта = DataOut[0..3], 16bit = 2, 8bit, bit = каждый DataOut[i] — бит
    /// 0/1, 64bit = 8); заполняется автоматически по выбранному шаблону, пусто — авто по
    /// M_DataType.</summary>
    public sealed class MbTypnoMapRule : INotifyPropertyChanged
    {
        // F33: INPC — колонка «Размер» обновляется в гриде при автозаполнении из шаблона
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnChanged(name);
            return true;
        }

        private long _id;
        /// <summary>ID строки в SCADA_TYPNO_MAP (COUNTER); 0 — ещё не сохранено.</summary>
        public long Id { get => _id; set => Set(ref _id, value); }

        private string _typno = "";
        /// <summary>Типовой алгоритм (IO_LIST Typno, напр. «PID1200-01R»); сравнение IgnoreCase.</summary>
        public string Typno { get => _typno; set => Set(ref _typno, value ?? ""); }

        private string _template = "";
        /// <summary>F16: имя шаблона привязок из SCADA_DATAOUT_TEMPLATES (IgnoreCase);
        /// пусто — основной шаблон (файл/вставленный код редактора).</summary>
        public string Template { get => _template; set => Set(ref _template, value ?? ""); }

        private string _exemplar = "";
        /// <summary>Ключ эталона в шаблоне привязок (Disc/NAME OnePOU: BOOL/INT/DINT/REAL).</summary>
        public string Exemplar { get => _exemplar; set => Set(ref _exemplar, value ?? ""); }

        private string _size = "";
        /// <summary>F33: размер переменной (колонка Size): 32bit/16bit/8bit/bit/64bit;
        /// пусто — авто по M_DataType. Автозаполняется при выборе шаблона привязок.</summary>
        public string Size { get => _size; set => Set(ref _size, value ?? ""); }

        /// <summary>F33: допустимые значения размера (первая — пустая = авто по M_DataType).</summary>
        public static readonly string[] ValidSizes = { "", "bit", "8bit", "16bit", "32bit", "64bit" };

        /// <summary>F33: нормализация размера (trim + lower; недопустимое значение → "").</summary>
        public static string NormalizeSize(string? size)
        {
            string v = (size ?? "").Trim().ToLowerInvariant();
            return Array.IndexOf(ValidSizes, v) >= 0 ? v : "";
        }

        /// <summary>F33: размер → число байтов данных (слотов DataOut/DataIn); bit → 1 элемент.</summary>
        public static int SizeBytes(string? size) => NormalizeSize(size) switch
        {
            "8bit" => 1, "16bit" => 2, "32bit" => 4, "64bit" => 8,
            "bit" => 1, _ => 0
        };

        /// <summary>F33: размер → число регистров в запросе (FC03/04/06/16);
        /// bit/пусто → 0 (для битовых функций ширина всегда 1 адрес).</summary>
        public static int SizeRegisters(string? size) => NormalizeSize(size) switch
        {
            "8bit" => 1, "16bit" => 1, "32bit" => 2, "64bit" => 4, _ => 0
        };
    }

    /// <summary>F33 (этап 7): строка маппинга объекта data-шаблона — «переменная шаблона
    /// (блок-константа 34 / тег 31, кроме источников .DataOut[/.DataIn[ и приёмника-тега)
    /// → колонка IO_LIST / строковый шаблон «{Кол}…» / литерал». Хранится в
    /// SCADA_TEMPLATE_VARS (Scope=dataout для чтения, datain для записи;
    /// TemplateName — имя data-шаблона, Var — имя объекта).</summary>
    public sealed class MbDataTemplateVarRow
    {
        /// <summary>Имя data-шаблона (SCADA_DATAOUT_/SCADA_DATAIN_TEMPLATES).</summary>
        public string TemplateName { get; set; } = "";

        /// <summary>Направление шаблона для UI: «чтение»/«запись» (scope dataout/datain).</summary>
        public string Direction { get; set; } = "";

        /// <summary>Имя переменной-объекта в шаблоне (Info блока 31/34).</summary>
        public string Var { get; set; } = "";

        /// <summary>Колонка IO_LIST или строковый шаблон «{M_DcsTag} …» (F32).</summary>
        public string IoColumn { get; set; } = "";

        /// <summary>Фиксированный текст (используется, если колонка/шаблон не заданы).</summary>
        public string Literal { get; set; } = "";
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

        /// <summary>F37: контроллер IO_LIST — ключ фильтра (Controller|Station): одна станция
        /// у разных контроллеров — независимые пункты фильтра.</summary>
        public string Controller { get; set; } = "";

        /// <summary>F37: ключ пункта фильтра «Controller|Station» (IgnoreCase при сравнении).</summary>
        public string Key => (Controller ?? "").Trim() + "|" + (Station ?? "").Trim();

        /// <summary>F37: подпись в UI — «Контроллер / Станция» (без контроллера — только станция).</summary>
        public string Display
        {
            get
            {
                string ct = (Controller ?? "").Trim();
                string st = Station ?? "";
                return ct.Length > 0 ? $"{ct} / {st}" : st;
            }
        }

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
    public sealed class ScadaMbGenSettings : INotifyPropertyChanged
    {
        // F33: уведомление UI об изменении свойства — TwoWay-привязки окна F13 («Обзор»,
        // выбор шаблонов) сразу отражаются в полях формы
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnChanged(name);
            return true;
        }

        // —— пакетирование ——
        private MbPacketPolicy _policy = MbPacketPolicy.Contiguous;
        /// <summary>Политика пакетирования (по умолчанию — непрерывные диапазоны).</summary>
        public MbPacketPolicy Policy { get => _policy; set => Set(ref _policy, value); }

        private int _maxCountRegisters = 100;
        /// <summary>F20: максимум регистров в пакете (FC03/04); фактический лимит =
        /// min(MaxCountRegisters, 123 — лимит блока). 0 — без ограничения пользователя
        /// (= константа блока). Валидация: 0..123 (подрезка min с MaxRegisters).</summary>
        public int MaxCountRegisters { get => _maxCountRegisters; set => Set(ref _maxCountRegisters, value); }

        private int _maxCountBits = 100;
        /// <summary>F20: максимум битов в пакете (FC01/02); фактический лимит =
        /// min(MaxCountBits, 246 — лимит блока). 0 — без ограничения пользователя
        /// (= константа блока). Валидация: 0..246 (подрезка min с MaxBits).</summary>
        public int MaxCountBits { get => _maxCountBits; set => Set(ref _maxCountBits, value); }

        private int _maxGap = 0;
        /// <summary>F13.1: допустимый промежуток адресов внутри пакета (политика Gap):
        /// разрыв ≤ MaxGap — сигналы в одном блоке (прореха читается вхолостую),
        /// > MaxGap — новый блок. 0 — без промежутков (= Contiguous).</summary>
        public int MaxGap { get => _maxGap; set => Set(ref _maxGap, value); }

        private bool _splitByControllerStation = true;
        /// <summary>F37: экспорт отдельными файлами «Контроллер_Станция_№» (блоки Modbus) +
        /// «Контроллер_Станция_Typno_№» (data-объекты по Typno) в выбранную папку;
        /// false — исторический сводный экспорт (один PousXml + «_bindings.xml» рядом
        /// с файлом из диалога сохранения).</summary>
        public bool SplitByControllerStation
        {
            get => _splitByControllerStation;
            set => Set(ref _splitByControllerStation, value);
        }

        private int _dataPacketsPerPage = 1;
        /// <summary>F38: сколько пакетов data (файл привязок) помещается на одном POU
        /// (странице). Пакет атомарен — целиком размещается на одном POU; пакеты
        /// группируются по N подряд в один OnePOU (имя POU — по первому пакету чанка,
        /// зазор между пакетами не даёт цепочкам наложиться). 1 — историческое
        /// «один POU на пакет» (F13). Минимум функционально — 1 (все данные одного
        /// Modbus-пакета на одном POU выполняются всегда).</summary>
        public int DataPacketsPerPage
        {
            get => _dataPacketsPerPage;
            set => Set(ref _dataPacketsPerPage, value);
        }

        private int _defaultPort = 502;
        /// <summary>Порт по умолчанию, если в M_SlaveIdMain не указан (Modbus TCP — 502).</summary>
        public int DefaultPort { get => _defaultPort; set => Set(ref _defaultPort, value); }

        private int _defaultSlaveId = 1;
        /// <summary>SlaveId по умолчанию, если станция не описана правилом SCADA_MB_SLAVE_RULES.</summary>
        public int DefaultSlaveId { get => _defaultSlaveId; set => Set(ref _defaultSlaveId, value); }

        private int _defaultTimeoutMs = 200;
        /// <summary>Таймаут по умолчанию, мс, если M_Timeout пуст (ивалон — 200).</summary>
        public int DefaultTimeoutMs { get => _defaultTimeoutMs; set => Set(ref _defaultTimeoutMs, value); }

        /// <summary>F22: копия настроек — для второго прохода Write по RTU-шаблону
        /// (с BaseT11Id/BaseCardId, сдвинутыми за пределы первого результата).</summary>
        public ScadaMbGenSettings Clone() => new ScadaMbGenSettings
        {
            Policy = Policy, MaxCountRegisters = MaxCountRegisters, MaxCountBits = MaxCountBits,
            MaxGap = MaxGap, DefaultPort = DefaultPort, DefaultSlaveId = DefaultSlaveId,
            SplitByControllerStation = SplitByControllerStation, // F37
            DefaultTimeoutMs = DefaultTimeoutMs, TemplateMode = TemplateMode,
            TemplateFilePath = TemplateFilePath, DataOutTemplateMode = DataOutTemplateMode,
            DataOutTemplateFilePath = DataOutTemplateFilePath, DataOutTemplateName = DataOutTemplateName,
            MasterTcpTemplateName = MasterTcpTemplateName, MasterRtuTemplateName = MasterRtuTemplateName,
            BlockNamePattern = BlockNamePattern, BlockNamePatternRtu = BlockNamePatternRtu,
            PouNamePattern = PouNamePattern, DataOutPouNamePattern = DataOutPouNamePattern,
            FormatAddress = FormatAddress, BaseT11Id = BaseT11Id, BaseCardId = BaseCardId,
            ColumnsPerRow = ColumnsPerRow, RowStep = RowStep, ColumnStep = ColumnStep,
            DataPacketsPerPage = DataPacketsPerPage // F38
        };

        // —— XML-шаблон ——
        private MbTemplateMode _templateMode = MbTemplateMode.File;
        /// <summary>Источник шаблона: файл (File) или вставленный код (Inline, SCADA_MB_TEMPLATE).</summary>
        public MbTemplateMode TemplateMode { get => _templateMode; set => Set(ref _templateMode, value); }

        private string _templateFilePath = "";
        /// <summary>Путь к файлу шаблона; пуст — авто-поиск «MB_TCP_MASTER_пример_new.xml»
        /// (каталог exe, потом Work\, затем встроенная копия).</summary>
        public string TemplateFilePath { get => _templateFilePath; set => Set(ref _templateFilePath, value ?? ""); }

        private string _masterTcpTemplateName = "";
        /// <summary>F22: имя мастер-шаблона Modbus TCP из SCADA_MASTER_TEMPLATES, которым
        /// генерируются пакеты TCP; пуст — текущий шаблон редактора (историческое).</summary>
        public string MasterTcpTemplateName { get => _masterTcpTemplateName; set => Set(ref _masterTcpTemplateName, value ?? ""); }

        private string _masterRtuTemplateName = "";
        /// <summary>F22: имя мастер-шаблона Modbus RTU из SCADA_MASTER_TEMPLATES для пакетов
        /// RTU-станций; пуст — RTU-пакеты пропускаются с предупреждением.</summary>
        public string MasterRtuTemplateName { get => _masterRtuTemplateName; set => Set(ref _masterRtuTemplateName, value ?? ""); }

        // —— F13.1: шаблон привязок (out → tagname) и имя блока ——
        private MbTemplateMode _dataOutTemplateMode = MbTemplateMode.File;
        /// <summary>Источник шаблона привязок: файл или вставленный код (SCADA_MB_TEMPLATE,
        /// строка Name='dataout'). Пуст/нет шаблона — простые привязки «блок 31 на пару
        /// (блок, индекс)» (историческое поведение).</summary>
        public MbTemplateMode DataOutTemplateMode { get => _dataOutTemplateMode; set => Set(ref _dataOutTemplateMode, value); }

        private string _dataOutTemplateFilePath = "";
        /// <summary>Путь к файлу шаблона привязок; пуст — авто-поиск
        /// «Data_Out_пример.xml» (каталог exe → Work\ → встроенная копия).</summary>
        public string DataOutTemplateFilePath { get => _dataOutTemplateFilePath; set => Set(ref _dataOutTemplateFilePath, value ?? ""); }

        private string _dataOutTemplateName = "";
        /// <summary>F16: имя текущего шаблона привязок в редакторе (SCADA_DATAOUT_TEMPLATES).
        /// Идентифицирует шаблон при «Сохранить под именем…»; пусто — имя ещё не присвоено
        /// (для файла подсказывается имя файла без расширения).</summary>
        public string DataOutTemplateName { get => _dataOutTemplateName; set => Set(ref _dataOutTemplateName, value ?? ""); }

        private string _blockNamePattern = "";
        /// <summary>F13.1: шаблон имени блока мастера. Пуст — историческое правило
        /// (цифры имени шаблона + номер пакета). Иначе — маска с токенами:
        /// {Seq} — номер пакета, {Slave} — SlaveId, {Station} — станция M_SlaveStation,
        /// {Ip} — IP через «_», {Func} — код функции. Пример: «MB_TCP_Master_{Slave}_{Seq}».</summary>
        public string BlockNamePattern { get => _blockNamePattern; set => Set(ref _blockNamePattern, value ?? ""); }

        private string _blockNamePatternRtu = "";
        /// <summary>F14: шаблон имени блока для Modbus RTU (мастер шаблона с «RTU» в имени).
        /// Пуст — fallback на BlockNamePattern. Токены те же.</summary>
        public string BlockNamePatternRtu { get => _blockNamePatternRtu; set => Set(ref _blockNamePatternRtu, value ?? ""); }

        private string _pouNamePattern = "";
        /// <summary>F17: шаблон имени POU в основном файле BufScadaPOUS (мастер-блоки).
        /// Пуст — историческое поведение: NAME из шаблона (MB_Master_4). Токены:
        /// {Master} — имя мастера шаблона, {Station}/{Slave}/{Ip}/{Port}/{Func}/{Seq} —
        /// контекст ПЕРВОГО пакета (POU один на все пакеты), {Date}/{Time} — момент экспорта.</summary>
        public string PouNamePattern { get => _pouNamePattern; set => Set(ref _pouNamePattern, value ?? ""); }

        private string _dataOutPouNamePattern = "";
        /// <summary>F17: шаблон имени POU в файле привязок (dataout) — один POU на пакет.
        /// Пуст — историческое поведение: NAME = имя блока мастера пакета
        /// (MB_TCP_MASTER_4_1). Токены: {Master} — имя блока мастера пакета, {Seq} — №
        /// пакета, {Slave}, {Station}, {Ip}, {Port}, {Func}, {Date}/{Time}; дубли имени
        /// внутри файла получают суффиксы _2, _3 (SCADA требует уникальности POU).</summary>
        public string DataOutPouNamePattern { get => _dataOutPouNamePattern; set => Set(ref _dataOutPouNamePattern, value ?? ""); }

        private bool _formatAddress = true;
        /// <summary>F14: формат входа Address блока: true (default) — «регистр + адрес»
        /// (FC3: 1 → 4001, 602 → 4602, 1506 → 41506; адрес ≥ 10000 — как есть);
        /// false — сырой адрес M_Address.</summary>
        public bool FormatAddress { get => _formatAddress; set => Set(ref _formatAddress, value); }

        // —— графика/раскладка ——
        private int _baseT11Id = 1000000;
        /// <summary>Стартовый T11ID для клонов шаблона (уникальность — счётчиком от максимума шаблона).</summary>
        public int BaseT11Id { get => _baseT11Id; set => Set(ref _baseT11Id, value); }

        private int _baseCardId = 500000;
        /// <summary>Стартовый cardId для клонов (уникальность — счётчиком, шаг 10).</summary>
        public int BaseCardId { get => _baseCardId; set => Set(ref _baseCardId, value); }

        private int _columnsPerRow = 2;
        /// <summary>Число запросов в строке раскладки POU (ивалон — 2).</summary>
        public int ColumnsPerRow { get => _columnsPerRow; set => Set(ref _columnsPerRow, value); }

        private int _rowStep = 560;
        /// <summary>Шаг по вертикали между запросами, px.</summary>
        public int RowStep { get => _rowStep; set => Set(ref _rowStep, value); }

        private int _columnStep = 1150;
        /// <summary>Шаг по горизонтали между запросами, px.</summary>
        public int ColumnStep { get => _columnStep; set => Set(ref _columnStep, value); }
    }
}


