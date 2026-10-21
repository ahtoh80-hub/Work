using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IO_PJT.Data;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// F13: планировщик блоков Modbus для генерации XML SCADA (меню «SCADA → Генерация
    /// блоков Modbus (XML)»). Источник — строки текущей таблицы с адресацией Modbus
    /// (M_PlcTag); из точек формируются пакеты-запросы «IP станции × код функции»
    /// с разбивкой по политике (непрерывные диапазоны / покрытие / фиксированные),
    /// по одному блоку MB_TCP_MASTER на пакет (образец «MB_TCP_MASTER_пример.xml»,
    /// структура — SCADA_XML_STRUCTURE.md). Настройки — SCADA_MB_SETTINGS,
    /// правила «станция → SlaveId» — SCADA_MB_SLAVE_RULES (числовой SlaveId в IO_LIST
    /// отсутствует). XML эмитит ScadaPouXmlWriter.
    /// </summary>
    public sealed class ModbusBlockPlannerService
    {
        /// <summary>Лимит Count блока MB_TCP_MASTER для регистровых режимов (документация блока).</summary>
        public const int MaxRegisters = 123;

        /// <summary>Лимит Count блока MB_TCP_MASTER для битовых режимов (документация блока).</summary>
        public const int MaxBits = 246;

        private readonly string _dbPath;

        public ModbusBlockPlannerService(string dbPath) => _dbPath = dbPath;

        private string ConnStr => DbProbe.ConnStrWithTimeout(_dbPath);

        /// <summary>Гарантия таблиц SCADA_MB_SETTINGS / SCADA_MB_SLAVE_RULES (идемпотентно).</summary>
        public void EnsureTables()
        {
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return;
            new TableCreator(_dbPath).EnsureServiceTables();
        }

        // ================== Парсы M_-полей ==================

        /// <summary>F13: разбор M_SlaveIdMain (колонка E «Interface parameters») — многострочное
        /// значение вида «192.168.4.10(Default)↵255.255.255.0↵port 502»: первый IPv4-адрес →
        /// октеты, «port N» → порт (иначе defaultPort). Нечисловой фрагмент — null.</summary>
        public static (int Ip1, int Ip2, int Ip3, int Ip4, int Port)? ParseSlaveIdMain(string? text, int defaultPort)
        {
            string s = (text ?? "").Trim();
            if (s.Length == 0) return null;
            var m = Regex.Match(s, @"(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})");
            if (!m.Success) return null;
            int o1 = int.Parse(m.Groups[1].Value);
            int o2 = int.Parse(m.Groups[2].Value);
            int o3 = int.Parse(m.Groups[3].Value);
            int o4 = int.Parse(m.Groups[4].Value);
            if (o1 > 255 || o2 > 255 || o3 > 255 || o4 > 255) return null;
            int port = defaultPort;
            var pm = Regex.Match(s, @"port\s*(\d{1,5})", RegexOptions.IgnoreCase);
            if (pm.Success)
            {
                int p = int.Parse(pm.Groups[1].Value);
                if (p >= 1 && p <= 65535) port = p;
            }
            return (o1, o2, o3, o4, port);
        }

        /// <summary>F13: разбор M_Timeout («100ms»/«100»/«1000ms») → мс; нечисловой/пустой — null.</summary>
        public static int? ParseTimeoutMs(string? text)
        {
            var m = Regex.Match((text ?? "").Trim(), @"(\d{1,7})");
            if (!m.Success) return null;
            int v = int.Parse(m.Groups[1].Value);
            return v > 0 ? v : null;
        }

        /// <summary>F13: ширина значения в регистрах по M_DataType (BOOL/биты → 1 бит;
        /// 8/16-Bit → 1 регистр; 32-Bit → 2; 64-Bit → 4; неизвестный → 1).</summary>
        public static int GetRegisterWidth(string? dataType)
        {
            string s = (dataType ?? "").Trim();
            if (s.Length == 0) return 1;
            if (s.Contains("64")) return 4;
            if (s.Contains("32")) return 2;
            return 1;
        }

        /// <summary>F13: true — тип данных хранится по 1 биту на сигнал (BOOL) для FC01/02.</summary>
        public static bool IsBitDataType(string? dataType) =>
            (dataType ?? "").Trim().Equals("BOOL", StringComparison.OrdinalIgnoreCase);

        /// <summary>F13: нормализация M_FuncCode («02»/«2»/« 3 ») → int; не 01-16 → null.</summary>
        /// <summary>F13/F30: код функции из M_FuncCode («02»/« 03 »/«16» → 2/3/16); мусор/0 → null.
        /// F30: диапазон расширен до 23 — 22/23 распознаются и явно отклоняются предупреждением
        /// в BuildPlan (Mask/ReadWrite Register не поддерживаются планировщиком).</summary>
        public static int? NormalizeFuncCode(string? funcCode)
        {
            string s = (funcCode ?? "").Trim().TrimStart('0');
            if (s.Length == 0) s = "0";
            return int.TryParse(s, out int v) && v >= 1 && v <= 23 ? v : null;
        }

        /// <summary>F13: разбор начального адреса из M_Address («602», «0500», «12.3» → 12).</summary>
        public static int? ParseAddress(string? address)
        {
            string s = (address ?? "").Trim();
            if (s.Length == 0) return null;
            int dot = s.IndexOf('.');
            if (dot >= 0) s = s.Substring(0, dot); // «адрес.бит» → адрес
            return int.TryParse(s, out int v) && v >= 0 && v <= 65535 ? v : null;
        }

        // ================== Построение плана ==================

        /// <summary>F13/F30: построить пакеты-запросы из точек таблицы. Отбираются точки с заполненной
        /// адресацией Modbus (M_PlcTag — как в F12); группировка: (IP из M_SlaveIdMain, порт,
        /// код функции — F30 в пакете всегда один M_FuncCode, чтение и запись не смешиваются);
        /// внутри группы — сортировка по M_Address и разбивка по политике
        /// settings.Policy с лимитом min(MaxCountRegisters/MaxCountBits — F20, лимит блока).
        /// F30: FC05/06/15/16 (запись) включаются в план — привязка DataIn[bайт];
        /// FC05/06 — пакет ровно на одну операцию (одиночная запись Modbus).
        /// SlaveId — по правилу «станция → SlaveId».
        /// Имя блока мастера и привязки DataOut/DataIn считаются от имени мастера шаблона
        /// (ScadaTemplateXmlService.MasterNameFor).</summary>
        public MbPlanResult BuildPlan(IReadOnlyList<IoPoint> points,
            ScadaMbGenSettings settings, IReadOnlyList<MbSlaveIdRule> slaveRules,
            string? masterNameBase)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            slaveRules ??= Array.Empty<MbSlaveIdRule>();
            var result = new MbPlanResult();

            // правила «станция → SlaveId» + параметры станции (F21: протокол) (IgnoreCase)
            var ruleByStation = new Dictionary<string, MbSlaveIdRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in slaveRules)
                if (!string.IsNullOrWhiteSpace(r.Station) && !ruleByStation.ContainsKey(r.Station.Trim()))
                    ruleByStation[r.Station.Trim()] = r;

            // черновые точки запроса (Tag — тег IO_LIST, нужен для привязок M_BusBlock/M_BusDataOut)
            var raw = new List<(int Ip1, int Ip2, int Ip3, int Ip4, int Port, int FuncCode,
                int Address, int Width, string PlcTag, string Station, int Timeout, string Tag,
                string DataType, string Typno, IoPoint Point)>();

            foreach (var p in points)
            {
                var mb = p.Modbus;
                if (mb == null || string.IsNullOrWhiteSpace(mb.PlcTag))
                {
                    result.SkippedNoAddressing++;
                    continue;
                }
                result.WithAddressing++;

                // F14: без Typno сигнал не участвует в генерации xml (пакеты + привязки)
                string typno = (p.Identification.Typno ?? "").Trim();
                if (typno.Length == 0)
                {
                    result.SkippedNoTypno++;
                    continue;
                }

                int? fc = NormalizeFuncCode(mb.FuncCode);
                if (fc == null) { result.SkippedBadFunc++; continue; }
                // F30: чтение 01-04, запись 05/06/15/16 — оба входят в план (запись → DataIn[]);
                // 22/23 и прочее за пределами 1-16 — неподдерживаемая функция
                bool isWriteFc = fc == 5 || fc == 6 || fc == 15 || fc == 16;
                if (!isWriteFc && (fc < 1 || fc > 4)) { result.SkippedBadFunc++; continue; }
                if (fc == 22 || fc == 23)
                {
                    result.SkippedBadFunc++;
                    result.Warnings.Add($"F30: {p.Tag}: функция {fc} (Mask/ReadWrite Register) " +
                        "не поддерживается планировщиком — точка пропущена");
                    continue;
                }

                int? addr = ParseAddress(mb.Address);
                if (addr == null) { result.SkippedBadAddress++; continue; }

                var ep = ParseSlaveIdMain(mb.SlaveIdMain, settings.DefaultPort);
                if (ep == null) { result.SkippedBadEndpoint++; continue; }

                bool isBitFunc = fc == 1 || fc == 2 || fc == 5 || fc == 15;
                int width = isBitFunc && IsBitDataType(mb.DataType) ? 1 : GetRegisterWidth(mb.DataType);

                string plcTag = mb.PlcTag.Trim();
                int comma = plcTag.IndexOf(',');
                if (comma >= 0) // несколько регистров через запятую — первый (как в F12)
                {
                    plcTag = plcTag.Substring(0, comma).Trim();
                    result.Warnings.Add($"F13: {p.Tag}: в M_PlcTag несколько значений через запятую — взят первый («{plcTag}»)");
                }

                int timeout = ParseTimeoutMs(mb.Timeout) ?? settings.DefaultTimeoutMs;
                string station = (mb.SlaveStation ?? "").Trim();

                raw.Add((ep.Value.Ip1, ep.Value.Ip2, ep.Value.Ip3, ep.Value.Ip4, ep.Value.Port,
                    fc.Value, addr.Value, width, plcTag, station, timeout,
                    (p.Tag ?? "").Trim(), (mb.DataType ?? "").Trim(), typno, p));
            }

            // группировка: (IP, порт, код функции)
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int no = 0;
            foreach (var g in raw.GroupBy(r => (r.Ip1, r.Ip2, r.Ip3, r.Ip4, r.Port, r.FuncCode))
                                 .OrderBy(g => g.Key.Ip1).ThenBy(g => g.Key.Ip2)
                                 .ThenBy(g => g.Key.Ip3).ThenBy(g => g.Key.Ip4)
                                 .ThenBy(g => g.Key.Port).ThenBy(g => g.Key.FuncCode))
            {
                var items = g.OrderBy(r => r.Address).ThenBy(r => r.PlcTag).ToList();
                int gfc = g.Key.FuncCode;
                bool isBitFunc = gfc == 1 || gfc == 2 || gfc == 5 || gfc == 15;
                // F20: раздельные лимиты — FC01/02/05/15 (бит) по MaxCountBits, FC03/04/06/16
                // (регистры) по MaxCountRegisters; каждый дополнительно ограничен константой блока
                // (246 бит / 123 регистра); < 1 — без ограничения пользователя (= константа).
                int blockLimit = isBitFunc ? MaxBits : MaxRegisters;
                int userLimit = isBitFunc ? settings.MaxCountBits : settings.MaxCountRegisters;
                int limit = Math.Min(userLimit, blockLimit);
                if (limit < 1) limit = blockLimit;
                // F30: FC05/FC06 — одиночная запись Modbus (ровно 1 операция на запрос)
                if (gfc == 5 || gfc == 6) limit = 1;
                int groupTimeout = items.Min(r => r.Timeout);
                if (items.Any(r => r.Timeout != groupTimeout))
                    result.Warnings.Add(
                        $"F13: {items[0].Ip1}.{items[0].Ip2}.{items[0].Ip3}.{items[0].Ip4}: " +
                        $"разные таймауты в группе — взят минимальный ({groupTimeout} мс)");

                string station = items.First(r => r.Station.Length > 0).Station;
                // F21: SlaveId и протокол станции — из правила SCADA_MB_SLAVE_RULES
                bool hasRule = ruleByStation.TryGetValue(station, out var srule);
                int slaveId = hasRule ? Math.Clamp(srule!.SlaveId, 1, 255) : settings.DefaultSlaveId;
                string protocol = hasRule && !string.IsNullOrWhiteSpace(srule!.Protocol)
                    ? NormalizeProtocol(srule.Protocol) : "TCP";

                foreach (var (start, count, members) in PackItems(items, settings.Policy, limit, isBitFunc, result,
                    Math.Max(0, settings.MaxGap)))
                {
                    no++;
                    // F23/F24: IoPoint первой точки пакета — колонки IO_LIST (токены/маппинг)
                    var pointByTag = new Dictionary<string, IoPoint>(StringComparer.OrdinalIgnoreCase);
                    foreach (var i in items)
                        if (!pointByTag.ContainsKey(i.Tag)) pointByTag[i.Tag] = i.Point;
                    IoPoint firstPoint = pointByTag.TryGetValue(members[0].Tag, out var fp) ? fp : items[0].Point;
                    var packet = new MbPacketPlan
                    {
                        No = no,
                        Station = station,
                        Ip1 = g.Key.Ip1, Ip2 = g.Key.Ip2, Ip3 = g.Key.Ip3, Ip4 = g.Key.Ip4,
                        Port = g.Key.Port,
                        SlaveId = slaveId,
                        Protocol = protocol,
                        FuncCode = g.Key.FuncCode,
                        Address = start,
                        AddressInput = ScadaTemplateXmlService.FormatBlockAddress(
                            start, g.Key.FuncCode, settings.FormatAddress),
                        Count = count,
                        TimeoutMs = groupTimeout,
                        FirstTag = members[0].PlcTag,
                        // F23/F24: колонки IO_LIST первой точки пакета — источник токенов
                        // {ИмяКолонки} в именах и маппинга переменных шаблона
                        ColumnValues = ColumnValuesOf(firstPoint)
                    };
                    packet.MasterName = MakeUniqueName(
                        ScadaTemplateXmlService.ApplyBlockNamePattern(
                            ScadaTemplateXmlService.SelectBlockNamePattern(
                                settings, masterNameBase),
                            packet, masterNameBase), usedNames);

                    // F13.1: тип точки по тегу (M_DataType) — выбор эталона цепочки привязки
                    var dtByTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var i in items)
                        if (!dtByTag.ContainsKey(i.Tag)) dtByTag[i.Tag] = i.DataType;
                    // F14: Typno точки по тегу — ключ сопоставления SCADA_TYPNO_MAP
                    var typnoByTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var i in items)
                        if (!typnoByTag.ContainsKey(i.Tag)) typnoByTag[i.Tag] = i.Typno;

                    foreach (var m in members)
                    {
                        packet.Tags.Add(m.PlcTag);
                        // смещение в адресации пакета: FC03/04/06/16 — регистры, FC01/02/05/15 — биты
                        int offset = m.Address - start;
                        // F30: чтение → DataOut[байт], запись → DataIn[байт] (одинаковый байтовый
                        // индекс); M_BusDataOut в IO_LIST хранит путь независимо от направления
                        string exprField = packet.IsWrite ? "DataIn" : "DataOut";
                        packet.Bindings.Add(new MbBinding
                        {
                            Tag = m.Tag,
                            Address = m.Address,
                            Offset = offset,
                            BlockName = packet.MasterName,
                            // F18: DataOut/DataIn блока мастера — массив байт (MB_USINT_0_249):
                            // индекс = байт (регистр → 2 байта, бит → байт в упаковке)
                            DataOutExpr = $"{packet.MasterName}.{exprField}[{DataOutByteIndex(offset, packet.IsBit)}]",
                            IsWrite = packet.IsWrite, // F30
                            Width = Math.Max(1, m.Width),
                            DataType = dtByTag.TryGetValue(m.Tag, out var dt) ? dt : "",
                            Typno = typnoByTag.TryGetValue(m.Tag, out var tn) ? tn : ""
                        });
                    }
                    result.Packets.Add(packet);
                }
            }
            // F14: обобщённое предупреждение по точкам без Typno (не в пакетах и не в привязках)
            if (result.SkippedNoTypno > 0)
                result.Warnings.Add(
                    $"F14: точек с адресацией без Typno — {result.SkippedNoTypno}: " +
                    "сигнал не участвует в генерации xml (заполните колонку Typno)");
            return result;
        }

        /// <summary>F13: разбивка отсортированного по адресу списка на пакеты по политике.
        /// Возвращает (начальный адрес, покрывающий Count, участники).</summary>
        private static List<(int Start, int Count, List<(int Address, int Width, string PlcTag, string Tag)> Members)>
            PackItems(List<(int Ip1, int Ip2, int Ip3, int Ip4, int Port, int FuncCode,
                int Address, int Width, string PlcTag, string Station, int Timeout, string Tag,
                string DataType, string Typno, IoPoint Point)> items,
            MbPacketPolicy policy, int limit, bool isBitFunc, MbPlanResult result, int maxGap)
        {
            var packets = new List<(int Start, int Count, List<(int Address, int Width, string PlcTag, string Tag)> Members)>();

            switch (policy)
            {
                case MbPacketPolicy.FixedSize:
                {
                    // фиксированные пакеты по limit адресов подряд от начального адреса группы
                    int start0 = items[0].Address;
                    int end0 = items[items.Count - 1].Address;
                    for (int s = start0; s <= end0; s += limit)
                    {
                        int e = Math.Min(s + limit - 1, end0);
                        var inPacket = items.Where(i => i.Address >= s && i.Address <= e).ToList();
                        if (inPacket.Count == 0) continue;
                        var members = inPacket.Select(i => (i.Address, i.Width, i.PlcTag, i.Tag)).ToList();
                        packets.Add((s, e - s + 1, members));
                    }
                    break;
                }
                case MbPacketPolicy.Cover:
                {
                    // одно поле min..max, пока покрытие ≤ лимита (прорехи читаются вхолостую)
                    int start = items[0].Address;
                    int maxEnd = items[0].Address + items[0].Width - 1;
                    var members = new List<(int Address, int Width, string PlcTag, string Tag)> { (items[0].Address, items[0].Width, items[0].PlcTag, items[0].Tag) };
                    for (int i = 1; i < items.Count; i++)
                    {
                        int newEnd = Math.Max(maxEnd, items[i].Address + items[i].Width - 1);
                        if (newEnd - start + 1 > limit)
                        {
                            packets.Add((start, maxEnd - start + 1, members));
                            start = items[i].Address;
                            newEnd = items[i].Address + items[i].Width - 1;
                            members = new List<(int Address, int Width, string PlcTag, string Tag)>();
                        }
                        maxEnd = newEnd;
                        members.Add((items[i].Address, items[i].Width, items[i].PlcTag, items[i].Tag));
                    }
                    if (members.Count > 0)
                        packets.Add((start, maxEnd - start + 1, members));
                    break;
                }
                default: // Contiguous — разрыв адресов → новый блок
                {
                    int start = items[0].Address;
                    int maxEnd = items[0].Address + items[0].Width - 1;
                    var members = new List<(int Address, int Width, string PlcTag, string Tag)> { (items[0].Address, items[0].Width, items[0].PlcTag, items[0].Tag) };
                    for (int i = 1; i < items.Count; i++)
                    {
                        bool contiguous = items[i].Address <= maxEnd + 1;
                        int newEnd = Math.Max(maxEnd, items[i].Address + items[i].Width - 1);
                        if (!contiguous || newEnd - start + 1 > limit)
                        {
                            packets.Add((start, maxEnd - start + 1, members));
                            start = items[i].Address;
                            newEnd = items[i].Address + items[i].Width - 1;
                            members = new List<(int Address, int Width, string PlcTag, string Tag)>();
                        }
                        maxEnd = newEnd;
                        members.Add((items[i].Address, items[i].Width, items[i].PlcTag, items[i].Tag));
                    }
                    if (members.Count > 0)
                        packets.Add((start, maxEnd - start + 1, members));
                    break;
                }
                case MbPacketPolicy.Gap:
                {
                    // F13.1: как Contiguous, но прореха ≤ maxGap остаётся в пакете
                    // (читается вхолостую); больше maxGap → новый блок
                    int start = items[0].Address;
                    int maxEnd = items[0].Address + items[0].Width - 1;
                    var members = new List<(int Address, int Width, string PlcTag, string Tag)> { (items[0].Address, items[0].Width, items[0].PlcTag, items[0].Tag) };
                    for (int i = 1; i < items.Count; i++)
                    {
                        bool close = items[i].Address <= maxEnd + 1 + maxGap;
                        int newEnd = Math.Max(maxEnd, items[i].Address + items[i].Width - 1);
                        if (!close || newEnd - start + 1 > limit)
                        {
                            packets.Add((start, maxEnd - start + 1, members));
                            start = items[i].Address;
                            newEnd = items[i].Address + items[i].Width - 1;
                            members = new List<(int Address, int Width, string PlcTag, string Tag)>();
                        }
                        maxEnd = newEnd;
                        members.Add((items[i].Address, items[i].Width, items[i].PlcTag, items[i].Tag));
                    }
                    if (members.Count > 0)
                        packets.Add((start, maxEnd - start + 1, members));
                    break;
                }
            }

            // контроль лимита протокола (не должно случиться при корректном limit)
            foreach (var p in packets)
                if (p.Item2 > (isBitFunc ? MaxBits : MaxRegisters))
                    result.Warnings.Add($"F13: пакет Address={p.Item1} Count={p.Item2} превышает лимит блока");

            return packets;
        }

        /// <summary>F13: уникальное имя в пределах POU (коллизия → суффикс _2, _3, …).</summary>
        public static string MakeUniqueName(string name, HashSet<string> used)
        {
            string baseName = string.IsNullOrEmpty(name) ? "MB_OBJ" : name;
            string candidate = baseName;
            int n = 1;
            while (!used.Add(candidate))
            {
                n++;
                candidate = $"{baseName}_{n}";
            }
            return candidate;
        }

        // ================== Персист настроек и правил ==================

        /// <summary>F13: ключи настроек в SCADA_MB_SETTINGS (Name/Value). Структура/имена схемы —
        /// из XML-шаблона; устаревшие ключи (PouName/MasterPrefix/…) при сохранении удаляются.</summary>
        public static readonly string[] SettingKeys =
        {
            "Policy", "MaxCountRegisters", "MaxCountBits", "MaxGap", "DefaultPort", "DefaultSlaveId", "DefaultTimeoutMs",
            "TemplateMode", "TemplateFilePath", "MasterTcpTemplateName", "MasterRtuTemplateName",
            "DataOutTemplateMode", "DataOutTemplateFilePath", "BlockNamePattern",
            "BlockNamePatternRtu", "FormatAddress", "DataOutTemplateName",
            "PouNamePattern", "DataOutPouNamePattern",
            "BaseT11Id", "BaseCardId", "ColumnsPerRow", "RowStep", "ColumnStep"
        };

        /// <summary>F13: устаревшие ключи SCADA_MB_SETTINGS (были в F13 v1 до шаблонной генерации);
        /// удаляются при сохранении настроек.</summary>
        public static readonly string[] ObsoleteSettingKeys =
        {
            "PulsePeriodMs", "TimeoutTagName", "PouName", "MasterPrefix", "ArrayPrefix",
            "NoLinkPrefix", "PouId", "PouNum", "ControllerTypeName", "ControllerId",
            "ResourceId", "ShowTitles",
            "MaxCount" // F20: единый лимит заменён на MaxCountRegisters/MaxCountBits — удаляется при сохранении
        };

        /// <summary>F13: загрузить настройки генерации из SCADA_MB_SETTINGS
        /// (нет таблицы/нет значения — defaults).</summary>
        public ScadaMbGenSettings LoadSettings()
        {
            var s = new ScadaMbGenSettings();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(_dbPath) && File.Exists(_dbPath))
            {
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        using var cn = new OleDbConnection(ConnStr);
                        cn.Open();
                        using var cmd = new OleDbCommand(
                            $"SELECT [Name], [Value] FROM [{Schema.ScadaMbSettingsTable}]", cn);
                        cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                        using var rd = cmd.ExecuteReader();
                        while (rd.Read())
                        {
                            string key = rd.IsDBNull(0) ? "" : rd.GetString(0);
                            if (key.Length > 0) values[key] = rd.IsDBNull(1) ? "" : rd.GetString(1);
                        }
                        break;
                    }
                    catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                    {
                        System.Threading.Thread.Sleep(300 * attempt);
                    }
                }
            }
            if (values.TryGetValue("Policy", out var pol) && Enum.TryParse(pol, true, out MbPacketPolicy pp))
                s.Policy = pp;
            // F20: раздельные лимиты; миграция старых баз — единый ключ MaxCount применяется
            // к обоим лимитам, только когда новые ключи ещё не записаны.
            int mcrv = 0, mcbv = 0;
            bool hasMcR = values.TryGetValue("MaxCountRegisters", out var mcr) && int.TryParse(mcr, out mcrv);
            bool hasMcB = values.TryGetValue("MaxCountBits", out var mcb) && int.TryParse(mcb, out mcbv);
            if (hasMcR) s.MaxCountRegisters = mcrv;
            if (hasMcB) s.MaxCountBits = mcbv;
            if (!hasMcR && !hasMcB && values.TryGetValue("MaxCount", out var mc) && int.TryParse(mc, out int mcv))
            { s.MaxCountRegisters = mcv; s.MaxCountBits = mcv; }
            if (values.TryGetValue("DefaultPort", out var dp) && int.TryParse(dp, out int dpv)) s.DefaultPort = dpv;
            if (values.TryGetValue("DefaultSlaveId", out var ds) && int.TryParse(ds, out int dsv)) s.DefaultSlaveId = dsv;
            if (values.TryGetValue("DefaultTimeoutMs", out var dt) && int.TryParse(dt, out int dtv)) s.DefaultTimeoutMs = dtv;
            if (values.TryGetValue("TemplateMode", out var tm) && Enum.TryParse(tm, true, out MbTemplateMode tmm))
                s.TemplateMode = tmm;
            if (values.TryGetValue("TemplateFilePath", out var tf)) s.TemplateFilePath = tf;
            if (values.TryGetValue("MasterTcpTemplateName", out var mtc)) s.MasterTcpTemplateName = mtc;
            if (values.TryGetValue("MasterRtuTemplateName", out var mtr)) s.MasterRtuTemplateName = mtr;
            if (values.TryGetValue("MaxGap", out var mg) && int.TryParse(mg, out int mgv)) s.MaxGap = mgv;
            if (values.TryGetValue("DataOutTemplateMode", out var dom) && Enum.TryParse(dom, true, out MbTemplateMode domm))
                s.DataOutTemplateMode = domm;
            if (values.TryGetValue("DataOutTemplateFilePath", out var dopf)) s.DataOutTemplateFilePath = dopf;
            if (values.TryGetValue("DataOutTemplateName", out var dont)) s.DataOutTemplateName = dont;
            if (values.TryGetValue("BlockNamePattern", out var bnp)) s.BlockNamePattern = bnp;
            if (values.TryGetValue("BlockNamePatternRtu", out var bnr)) s.BlockNamePatternRtu = bnr;
            if (values.TryGetValue("PouNamePattern", out var pnp)) s.PouNamePattern = pnp;
            if (values.TryGetValue("DataOutPouNamePattern", out var dopp)) s.DataOutPouNamePattern = dopp;
            if (values.TryGetValue("FormatAddress", out var fa) &&
                bool.TryParse(fa, out bool fav)) s.FormatAddress = fav;
            if (values.TryGetValue("BaseT11Id", out var bt) && int.TryParse(bt, out int btv)) s.BaseT11Id = btv;
            if (values.TryGetValue("BaseCardId", out var bc) && int.TryParse(bc, out int bcv)) s.BaseCardId = bcv;
            if (values.TryGetValue("ColumnsPerRow", out var cpr) && int.TryParse(cpr, out int cprv)) s.ColumnsPerRow = cprv;
            if (values.TryGetValue("RowStep", out var rs) && int.TryParse(rs, out int rsv)) s.RowStep = rsv;
            if (values.TryGetValue("ColumnStep", out var cs) && int.TryParse(cs, out int csv)) s.ColumnStep = csv;
            return s;
        }

        /// <summary>F13: сохранить настройки в SCADA_MB_SETTINGS (полная замена ключей SettingKeys).</summary>
        public void SaveSettings(ScadaMbGenSettings s)
        {
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            EnsureTables();
            var pairs = new List<(string Key, string Value)>
            {
                ("Policy", s.Policy.ToString()),
                ("MaxCountRegisters", s.MaxCountRegisters.ToString()),
                ("MaxCountBits", s.MaxCountBits.ToString()),
                ("DefaultPort", s.DefaultPort.ToString()),
                ("DefaultSlaveId", s.DefaultSlaveId.ToString()),
                ("DefaultTimeoutMs", s.DefaultTimeoutMs.ToString()),
                ("TemplateMode", s.TemplateMode.ToString()),
                ("TemplateFilePath", s.TemplateFilePath ?? ""),
                ("MasterTcpTemplateName", s.MasterTcpTemplateName ?? ""),
                ("MasterRtuTemplateName", s.MasterRtuTemplateName ?? ""),
                ("MaxGap", s.MaxGap.ToString()),
                ("DataOutTemplateMode", s.DataOutTemplateMode.ToString()),
                ("DataOutTemplateFilePath", s.DataOutTemplateFilePath ?? ""),
                ("DataOutTemplateName", s.DataOutTemplateName ?? ""),
                ("BlockNamePattern", s.BlockNamePattern ?? ""),
                ("BlockNamePatternRtu", s.BlockNamePatternRtu ?? ""),
                ("PouNamePattern", s.PouNamePattern ?? ""),
                ("DataOutPouNamePattern", s.DataOutPouNamePattern ?? ""),
                ("FormatAddress", s.FormatAddress ? "True" : "False"),
                ("BaseT11Id", s.BaseT11Id.ToString()),
                ("BaseCardId", s.BaseCardId.ToString()),
                ("ColumnsPerRow", s.ColumnsPerRow.ToString()),
                ("RowStep", s.RowStep.ToString()),
                ("ColumnStep", s.ColumnStep.ToString())
            };
            for (int attempt = 1; ; attempt++)
            {
                try { SaveSettingsOnce(pairs); return; }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private void SaveSettingsOnce(List<(string Key, string Value)> pairs)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                using (var del = new OleDbCommand(
                    $"DELETE FROM [{Schema.ScadaMbSettingsTable}]", cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    del.ExecuteNonQuery();
                }
                using var ins = new OleDbCommand(
                    $"INSERT INTO [{Schema.ScadaMbSettingsTable}] ([Name], [Value]) VALUES (?, ?)", cn, tr);
                ins.CommandTimeout = DbProbe.TimeoutSeconds;
                foreach (var (key, value) in pairs)
                {
                    ins.Parameters.Clear();
                    OleDbArgs.Add(ins, key);
                    OleDbArgs.Add(ins, value);
                    ins.ExecuteNonQuery();
                }
                tr.Commit();
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }

        /// <summary>F13: загрузить правила «станция → SlaveId» из SCADA_MB_SLAVE_RULES (по Station).</summary>
        public List<MbSlaveIdRule> LoadSlaveRules()
        {
            var list = new List<MbSlaveIdRule>();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return list;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    EnsureTables(); // F21: таблица + миграция колонок параметров станции
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand(
                        $@"SELECT ID, [Station], [SlaveId], [IpMain], [IpReserve], [Protocol],
                                   [Speed], [Parity], [StopBit], [DataBit], [Reverse], [Timeout], [MaxLength],
                                   [MasterTemplate]
                            FROM [{Schema.ScadaMbSlaveRulesTable}] ORDER BY ID", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                        list.Add(new MbSlaveIdRule
                        {
                            Id = Convert.ToInt64(rd.GetValue(0)),
                            Station = rd.IsDBNull(1) ? "" : rd.GetString(1),
                            SlaveId = rd.IsDBNull(2) ? 1 : Convert.ToInt32(rd.GetValue(2)),
                            IpMain = rd.IsDBNull(3) ? "" : rd.GetString(3),
                            IpReserve = rd.IsDBNull(4) ? "" : rd.GetString(4),
                            Protocol = rd.IsDBNull(5) ? "" : rd.GetString(5),
                            Speed = rd.IsDBNull(6) ? "" : rd.GetString(6),
                            Parity = !rd.IsDBNull(7) && Convert.ToBoolean(rd.GetValue(7)),
                            StopBit = rd.IsDBNull(8) ? 1 : Convert.ToInt32(rd.GetValue(8)),
                            DataBit = rd.IsDBNull(9) ? 8 : Convert.ToInt32(rd.GetValue(9)),
                            Reverse = !rd.IsDBNull(10) && Convert.ToBoolean(rd.GetValue(10)),
                            Timeout = rd.IsDBNull(11) ? 0 : Convert.ToInt32(rd.GetValue(11)),
                            MaxLength = rd.IsDBNull(12) ? 0 : Convert.ToInt32(rd.GetValue(12)),
                            MasterTemplate = rd.IsDBNull(13) ? "" : rd.GetString(13) // F29
                        });
                    break;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
            return list;
        }

        /// <summary>F13: сохранить правила «станция → SlaveId» (полная замена; пустые станции
        /// и SlaveId вне 1-255 отбрасываются).</summary>
        public int SaveSlaveRules(IEnumerable<MbSlaveIdRule> rules)
        {
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            var list = rules
                .Where(r => !string.IsNullOrWhiteSpace(r.Station) && r.SlaveId >= 1 && r.SlaveId <= 255)
                .Select(r => new MbSlaveIdRule
                {
                    Station = r.Station.Trim(),
                    SlaveId = r.SlaveId,
                    // F21: параметры станции (IP/протокол/скорость/UART/таймаут/длина)
                    IpMain = r.IpMain ?? "",
                    IpReserve = r.IpReserve ?? "",
                    Protocol = (r.Protocol ?? "").Trim(),
                    Speed = r.Speed ?? "",
                    Parity = r.Parity,
                    StopBit = r.StopBit,
                    DataBit = r.DataBit,
                    Reverse = r.Reverse,
                    Timeout = r.Timeout,
                    MaxLength = r.MaxLength,
                    MasterTemplate = (r.MasterTemplate ?? "").Trim() // F29
                })
                .ToList();
            EnsureTables();
            for (int attempt = 1; ; attempt++)
            {
                try { return SaveSlaveRulesOnce(list); }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private int SaveSlaveRulesOnce(List<MbSlaveIdRule> list)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                using (var del = new OleDbCommand(
                    $"DELETE FROM [{Schema.ScadaMbSlaveRulesTable}]", cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    del.ExecuteNonQuery();
                }
                using var ins = new OleDbCommand(
                    $@"INSERT INTO [{Schema.ScadaMbSlaveRulesTable}]
                       ([Station], [SlaveId], [IpMain], [IpReserve], [Protocol], [Speed],
                        [Parity], [StopBit], [DataBit], [Reverse], [Timeout], [MaxLength], [MasterTemplate])
                       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", cn, tr);
                ins.CommandTimeout = DbProbe.TimeoutSeconds;
                foreach (var r in list)
                {
                    ins.Parameters.Clear();
                    OleDbArgs.Add(ins, r.Station);
                    OleDbArgs.Add(ins, r.SlaveId);
                    OleDbArgs.Add(ins, r.IpMain);
                    OleDbArgs.Add(ins, r.IpReserve);
                    OleDbArgs.Add(ins, r.Protocol);
                    OleDbArgs.Add(ins, r.Speed);
                    OleDbArgs.Add(ins, r.Parity);
                    OleDbArgs.Add(ins, r.StopBit);
                    OleDbArgs.Add(ins, r.DataBit);
                    OleDbArgs.Add(ins, r.Reverse);
                    OleDbArgs.Add(ins, r.Timeout);
                    OleDbArgs.Add(ins, r.MaxLength);
                    OleDbArgs.Add(ins, r.MasterTemplate); // F29
                    ins.ExecuteNonQuery();
                }
                tr.Commit();
                return list.Count;
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }

        // ================== F21: параметры станций (protocol/автосбор из IO_LIST) ==================

        /// <summary>F21: нормализация Protocol станции — «RTU» (IgnoreCase) → «RTU»,
        /// всё прочее → «TCP» (историческое поведение планировщика).</summary>
        public static string NormalizeProtocol(string? protocol) =>
            string.Equals((protocol ?? "").Trim(), "RTU", StringComparison.OrdinalIgnoreCase)
                ? "RTU" : "TCP";

        /// <summary>F21: эвристика протокола по M_SlaveIdMain — есть IPv4 → «TCP» (Modbus
        /// TCP/IP); непустое значение без IPv4 (COM-порт/шина) → «RTU»; пусто → «TCP».</summary>
        public static string DetectProtocol(string? slaveIdMain)
        {
            string s = (slaveIdMain ?? "").Trim();
            if (s.Length == 0) return "TCP"; // адрес не задан — историческое поведение TCP
            return System.Text.RegularExpressions.Regex.IsMatch(s,
                @"\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}") ? "TCP" : "RTU";
        }

        /// <summary>F21: автосбор станций slave из точек IO_LIST (M_SlaveStation, M_SlaveIdMain,
        /// M_SlaveIdBackup, M_Speed, M_Timeout) с merge по Station: авто-поля (IP main/reserve,
        /// Speed, Timeout) обновляются из данных, ручные (SlaveId, Parity, StopBit, DataBit,
        /// Reverse, MaxLength) и Protocol сохраняются; Protocol заполняется эвристикой только
        /// для новых/пустых строк. Станции из правил без точек в IO_LIST не теряются.</summary>
        public static List<MbSlaveIdRule> MergeSlaveRulesFromPoints(
            IEnumerable<MbSlaveIdRule>? current, IEnumerable<IoPoint>? points)
        {
            var byStation = new Dictionary<string, MbSlaveIdRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in current ?? Array.Empty<MbSlaveIdRule>())
                if (!string.IsNullOrWhiteSpace(r.Station) && !byStation.ContainsKey(r.Station.Trim()))
                    byStation[r.Station.Trim()] = r;

            // авто-поля из IO_LIST: первая точка станции задаёт значения, пустые дозаполняются
            var auto = new Dictionary<string, (string IpMain, string IpReserve, string Speed, int TimeoutMs)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var p in points ?? Array.Empty<IoPoint>())
            {
                string st = (p.Modbus?.SlaveStation ?? "").Trim();
                if (st.Length == 0) continue;
                string ipMain = (p.Modbus?.SlaveIdMain ?? "").Trim();
                string ipReserve = (p.Modbus?.SlaveIdBackup ?? "").Trim();
                string speed = (p.Modbus?.Speed ?? "").Trim();
                int timeout = ParseTimeoutMs(p.Modbus?.Timeout) ?? 0;
                if (!auto.TryGetValue(st, out var a))
                    auto[st] = (ipMain, ipReserve, speed, timeout);
                else
                    auto[st] = (a.IpMain.Length > 0 ? a.IpMain : ipMain,
                                a.IpReserve.Length > 0 ? a.IpReserve : ipReserve,
                                a.Speed.Length > 0 ? a.Speed : speed,
                                a.TimeoutMs > 0 ? a.TimeoutMs : timeout);
            }

            var result = new List<MbSlaveIdRule>();
            foreach (var st in auto.Keys)
            {
                if (!byStation.TryGetValue(st, out var rule))
                    byStation[st] = rule = new MbSlaveIdRule { Station = st };
                rule.IpMain = auto[st].IpMain;
                rule.IpReserve = auto[st].IpReserve;
                rule.Speed = auto[st].Speed;
                rule.Timeout = auto[st].TimeoutMs;
                if (string.IsNullOrWhiteSpace(rule.Protocol))
                    rule.Protocol = DetectProtocol(rule.IpMain);
                result.Add(rule);
            }
            foreach (var r in byStation.Values) // ручные станции без точек в IO_LIST
                if (!result.Contains(r))
                    result.Add(r);
            return result.OrderBy(r => r.Station, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ================== F23: переменные шаблона (SCADA_TEMPLATE_VARS) ==================

        /// <summary>F23/F24: значения колонок FieldRegistry точки (колонка → текст; только
        /// непустые) — источник токенов {ИмяКолонки} и маппинга переменных шаблона.</summary>
        public static Dictionary<string, string> ColumnValuesOf(IoPoint p)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (p == null) return d;
            foreach (var col in FieldRegistry.Columns)
            {
                string v = (Convert.ToString(FieldRegistry.GetValue(p, col)) ?? "").Trim();
                if (v.Length > 0) d[col] = v;
            }
            return d;
        }

        /// <summary>F32: резолв строкового шаблона привязки «{M_DcsTag} Adress: {M_Address}» —
        /// токены {ИмяКолонки} заменяются значениями колонок точки, литералы между нами
        /// сохраняются; неизвестная/пустая колонка → её токен остаётся литералом и попадает
        /// в unresolved (для предупреждений). Пустой/нешаблонный вход возвращает как есть
        /// (вызывающий сам решает: колонка или литерал).</summary>
        public static string RenderVarTemplate(string? template,
            IReadOnlyDictionary<string, string> columnValues, out List<string> unresolved)
        {
            var found = new List<string>();
            unresolved = found;
            string t = template ?? "";
            if (t.IndexOf('{') < 0) return t;
            return System.Text.RegularExpressions.Regex.Replace(t, @"\{([^{}]+)\}", m =>
            {
                string col = m.Groups[1].Value.Trim();
                if (columnValues != null &&
                    columnValues.TryGetValue(col, out var cv) && cv.Length > 0)
                    return cv;
                found.Add(col);
                return m.Value; // неизвестная/пустая колонка → токен-литерал
            });
        }

        /// <summary>F23: прочитать все строки маппинга скоупа ('master'/'dataout'/'datain')
        /// без фильтра по шаблону, ORDER BY Var.</summary>
        public List<MbTemplateVar> LoadTemplateVars(string scope) =>
            LoadTemplateVarsCore(scope, null);

        /// <summary>F25: прочитать маппинг скоупа для конкретного шаблона (TemplateName =
        /// имя файла без расширения, «current» — вставленный код): строки своего ключа +
        /// легаси-строки с пустым/NULL именем (общий fallback); при совпадении Var
        /// приоритет у строк своего ключа. ORDER BY Var.</summary>
        public List<MbTemplateVar> LoadTemplateVars(string scope, string? templateName) =>
            LoadTemplateVarsCore(scope, (templateName ?? "").Trim());

        /// <summary>Общий читатель SCADA_TEMPLATE_VARS: key == null — все строки скоупа
        /// (F23); иначе — строки ключа + легаси (пустое имя) с приоритетом ключа (F25).</summary>
        private List<MbTemplateVar> LoadTemplateVarsCore(string scope, string? key)
        {
            var list = new List<MbTemplateVar>();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return list;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    EnsureTables();
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    string sql = key == null
                        ? $@"SELECT ID, [Scope], [TemplateName], [Var], [IoColumn], [Literal]
                            FROM [{Schema.ScadaTemplateVarsTable}] WHERE [Scope] = ?
                            ORDER BY [Var]"
                        : $@"SELECT ID, [Scope], [TemplateName], [Var], [IoColumn], [Literal]
                            FROM [{Schema.ScadaTemplateVarsTable}]
                            WHERE [Scope] = ? AND ([TemplateName] = ? OR [TemplateName] = ''
                                  OR [TemplateName] IS NULL)
                            ORDER BY [Var]";
                    using var cmd = new OleDbCommand(sql, cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(cmd, scope);
                    if (key != null) OleDbArgs.Add(cmd, key);
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                        list.Add(new MbTemplateVar
                        {
                            Id = Convert.ToInt64(rd.GetValue(0)),
                            Scope = rd.IsDBNull(1) ? "" : rd.GetString(1),
                            TemplateName = rd.IsDBNull(2) ? "" : rd.GetString(2),
                            Var = rd.IsDBNull(3) ? "" : rd.GetString(3),
                            IoColumn = rd.IsDBNull(4) ? "" : rd.GetString(4),
                            Literal = rd.IsDBNull(5) ? "" : rd.GetString(5)
                        });
                    break;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
            if (key == null) return list;

            // F25: легаси (пустое TemplateName) как fallback, строки ключа — приоритет
            var dict = new Dictionary<string, MbTemplateVar>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in list)
                if (string.IsNullOrWhiteSpace(v.TemplateName) && !string.IsNullOrWhiteSpace(v.Var))
                    dict[v.Var] = v;
            foreach (var v in list)
                if (string.Equals(v.TemplateName?.Trim(), key, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(v.Var))
                    dict[v.Var] = v;
            return dict.Values
                .OrderBy(v => v.Var, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>F23: сохранить маппинг переменных в легаси-строки скоупа (пустое имя
        /// шаблона — общий fallback; строки без имени переменной отбрасываются).</summary>
        public int SaveTemplateVars(string scope, IEnumerable<MbTemplateVar> vars) =>
            SaveTemplateVars(scope, "", vars);

        /// <summary>F25: сохранить маппинг переменных конкретного шаблона: заменяются только
        /// строки [Scope]+[TemplateName] (чужие шаблоны не затираются); строки без имени
        /// переменной отбрасываются.</summary>
        public int SaveTemplateVars(string scope, string? templateName, IEnumerable<MbTemplateVar> vars)
        {
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            string key = (templateName ?? "").Trim();
            var list = (vars ?? Array.Empty<MbTemplateVar>())
                .Where(v => !string.IsNullOrWhiteSpace(v.Var))
                .Select(v => new MbTemplateVar
                {
                    Scope = (scope ?? "").Trim(),
                    TemplateName = key,
                    Var = v.Var.Trim(),
                    IoColumn = (v.IoColumn ?? "").Trim(),
                    Literal = v.Literal ?? ""
                })
                .ToList();
            EnsureTables();
            for (int attempt = 1; ; attempt++)
            {
                try { SaveTemplateVarsOnce(scope?.Trim() ?? "", key, list); return list.Count; }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private void SaveTemplateVarsOnce(string scope, string key, List<MbTemplateVar> list)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                // F25: DELETE только по своему ключу; пустой ключ — и NULL-строки (легаси)
                string delSql = key.Length == 0
                    ? $@"DELETE FROM [{Schema.ScadaTemplateVarsTable}]
                         WHERE [Scope] = ? AND ([TemplateName] = '' OR [TemplateName] IS NULL)"
                    : $@"DELETE FROM [{Schema.ScadaTemplateVarsTable}]
                         WHERE [Scope] = ? AND [TemplateName] = ?";
                using (var del = new OleDbCommand(delSql, cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(del, scope);
                    if (key.Length > 0) OleDbArgs.Add(del, key);
                    del.ExecuteNonQuery();
                }
                using (var ins = new OleDbCommand(
                    $@"INSERT INTO [{Schema.ScadaTemplateVarsTable}]
                       ([Scope], [TemplateName], [Var], [IoColumn], [Literal])
                       VALUES (?, ?, ?, ?, ?)", cn, tr))
                {
                    ins.CommandTimeout = DbProbe.TimeoutSeconds;
                    foreach (var v in list)
                    {
                        ins.Parameters.Clear();
                        OleDbArgs.Add(ins, v.Scope);
                        OleDbArgs.Add(ins, v.TemplateName);
                        OleDbArgs.Add(ins, v.Var);
                        OleDbArgs.Add(ins, v.IoColumn);
                        OleDbArgs.Add(ins, v.Literal);
                        ins.ExecuteNonQuery();
                    }
                }
                tr.Commit();
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }

        // ================== Сопоставление Typno (SCADA_TYPNO_MAP, F14) ==================

        /// <summary>F14/F16: загрузить правила «Typno → (шаблон, эталон)» из SCADA_TYPNO_MAP.</summary>
        public List<MbTypnoMapRule> LoadTypnoMap()
        {
            var list = new List<MbTypnoMapRule>();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return list;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand(
                        $"SELECT ID, [Typno], [Template], [Exemplar] FROM [{Schema.ScadaTypnoMapTable}] ORDER BY ID", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                        list.Add(new MbTypnoMapRule
                        {
                            Id = Convert.ToInt64(rd.GetValue(0)),
                            Typno = rd.IsDBNull(1) ? "" : rd.GetString(1),
                            Template = rd.IsDBNull(2) ? "" : rd.GetString(2),
                            Exemplar = rd.IsDBNull(3) ? "" : rd.GetString(3)
                        });
                    break;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
            return list;
        }

        /// <summary>F14/F16: сохранить сопоставление «Typno → (шаблон, эталон)» (полная замена;
        /// строки без Typno или без эталона отбрасываются, значения обрезаются; дубли Typno —
        /// последний выигрывает. F16: пустой Template — «основной шаблон редактора».</summary>
        public int SaveTypnoMap(IEnumerable<MbTypnoMapRule> rules)
        {
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            var byTypno = new Dictionary<string, MbTypnoMapRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rules)
            {
                string typno = (r.Typno ?? "").Trim();
                string exemplar = (r.Exemplar ?? "").Trim();
                string template = (r.Template ?? "").Trim();
                if (typno.Length == 0 || exemplar.Length == 0) continue;
                // дубль IgnoreCase — выигрывает последний, но каноническое написание Typno
                // остаётся от первого вхождения (привязка к строке IO_LIST регистронезависима)
                string canon = byTypno.TryGetValue(typno, out var prev) ? prev.Typno : typno;
                byTypno[typno] = new MbTypnoMapRule { Typno = canon, Exemplar = exemplar, Template = template };
            }
            var list = byTypno.Values.ToList();
            EnsureTables();
            for (int attempt = 1; ; attempt++)
            {
                try { return SaveTypnoMapOnce(list); }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private int SaveTypnoMapOnce(List<MbTypnoMapRule> list)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                using (var del = new OleDbCommand(
                    $"DELETE FROM [{Schema.ScadaTypnoMapTable}]", cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    del.ExecuteNonQuery();
                }
                using var ins = new OleDbCommand(
                    $"INSERT INTO [{Schema.ScadaTypnoMapTable}] ([Typno], [Template], [Exemplar]) VALUES (?, ?, ?)", cn, tr);
                ins.CommandTimeout = DbProbe.TimeoutSeconds;
                foreach (var r in list)
                {
                    ins.Parameters.Clear();
                    OleDbArgs.Add(ins, r.Typno);
                    OleDbArgs.Add(ins, r.Template);
                    OleDbArgs.Add(ins, r.Exemplar);
                    ins.ExecuteNonQuery();
                }
                tr.Commit();
                return list.Count;
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }

        // ================== Шаблон (SCADA_MB_TEMPLATE) и привязки ==================

        /// <summary>F13: имя строки хранения вставленного кода шаблона (MEMO).</summary>
        public const string InlineTemplateName = "current";

        /// <summary>F13.1: имя строки хранения вставленного шаблона привязок Data_Out (MEMO).</summary>
        public const string InlineDataOutTemplateName = "dataout";

        /// <summary>F18: байтовый индекс DataOut блока мастера из смещения привязки.
        /// DataOut мастера — массив байт (MB_USINT_0_249, «Modbus RTU_TCP block.md»):
        /// FC03/04 — смещение в регистрах, регистр = 2 байта (offset*2);
        /// FC01/02 — смещение в битах, биты упакованы в байты (offset/8).</summary>
        public static int DataOutByteIndex(int offset, bool isBitFunc) =>
            isBitFunc ? offset / 8 : offset * 2;

        /// <summary>F13: прочитать вставленный шаблон (SCADA_MB_TEMPLATE, строка «current»);
        /// null — шаблона нет.</summary>
        public string? LoadInlineTemplate() => LoadInlineTemplate(InlineTemplateName);

        /// <summary>F13.1: прочитать вставленный шаблон по имени строки («current»/«dataout»).</summary>
        public string? LoadInlineTemplate(string rowName)
        {
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand(
                        $"SELECT [Body] FROM [{Schema.ScadaMbTemplateTable}] WHERE [Name] = ?", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(cmd, rowName);
                    object? v = cmd.ExecuteScalar();
                    return v == null || v is DBNull ? null : Convert.ToString(v);
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
            return null;
        }

        /// <summary>F13: сохранить вставленный шаблон в SCADA_MB_TEMPLATE (строка «current», замена).</summary>
        public void SaveInlineTemplate(string xml) => SaveInlineTemplate(InlineTemplateName, xml);

        /// <summary>F13.1: сохранить вставленный шаблон по имени строки («current»/«dataout», замена).</summary>
        public void SaveInlineTemplate(string rowName, string xml)
        {
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            EnsureTables();
            for (int attempt = 1; ; attempt++)
            {
                try { SaveInlineTemplateOnce(rowName, xml ?? ""); return; }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private void SaveInlineTemplateOnce(string rowName, string xml)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                using (var del = new OleDbCommand(
                    $"DELETE FROM [{Schema.ScadaMbTemplateTable}] WHERE [Name] = ?", cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(del, rowName);
                    del.ExecuteNonQuery();
                }
                using (var ins = new OleDbCommand(
                    $"INSERT INTO [{Schema.ScadaMbTemplateTable}] ([Name], [Body]) VALUES (?, ?)", cn, tr))
                {
                    ins.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(ins, rowName);
                    OleDbArgs.Add(ins, xml);
                    ins.ExecuteNonQuery();
                }
                tr.Commit();
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }

        // ================== Именованные шаблоны привязок (SCADA_DATAOUT_TEMPLATES, F16) ==================

        /// <summary>F16: прочитать все именованные шаблоны привязок Data_Out
        /// (имя → XML-тело + путь файла-источника), ORDER BY Name.</summary>
        public List<MbDataOutTemplate> LoadDataOutTemplates()
        {
            var list = new List<MbDataOutTemplate>();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return list;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand(
                        $"SELECT ID, [Name], [Body], [FilePath] FROM [{Schema.ScadaDataOutTemplatesTable}] ORDER BY [Name]", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                        list.Add(new MbDataOutTemplate
                        {
                            Id = Convert.ToInt64(rd.GetValue(0)),
                            Name = rd.IsDBNull(1) ? "" : rd.GetString(1),
                            Body = rd.IsDBNull(2) ? "" : rd.GetString(2),
                            FilePath = rd.IsDBNull(3) ? "" : rd.GetString(3)
                        });
                    break;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
            return list;
        }

        /// <summary>F16: сохранить именованный шаблон привязок (замена по имени IgnoreCase).
        /// Имя обязательно (пустое — InvalidOperationException).</summary>
        public void SaveDataOutTemplate(string name, string body, string? filePath = null)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0) throw new InvalidOperationException("Имя шаблона привязок не задано");
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            EnsureTables();
            for (int attempt = 1; ; attempt++)
            {
                try { SaveDataOutTemplateOnce(n, body ?? "", filePath ?? ""); return; }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private void SaveDataOutTemplateOnce(string name, string body, string filePath)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                // ACE SQL сравнивает текст без учёта регистра → замена дубля IgnoreCase
                using (var del = new OleDbCommand(
                    $"DELETE FROM [{Schema.ScadaDataOutTemplatesTable}] WHERE [Name] = ?", cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(del, name);
                    del.ExecuteNonQuery();
                }
                using (var ins = new OleDbCommand(
                    $"INSERT INTO [{Schema.ScadaDataOutTemplatesTable}] ([Name], [Body], [FilePath]) VALUES (?, ?, ?)", cn, tr))
                {
                    ins.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(ins, name);
                    OleDbArgs.Add(ins, body);
                    OleDbArgs.Add(ins, filePath);
                    ins.ExecuteNonQuery();
                }
                tr.Commit();
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }

        /// <summary>F16: удалить именованный шаблон по имени (IgnoreCase); true — строка удалена.</summary>
        public bool DeleteDataOutTemplate(string name)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0 || string.IsNullOrEmpty(_dbPath)) return false;
            EnsureTables();
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand(
                        $"DELETE FROM [{Schema.ScadaDataOutTemplatesTable}] WHERE [Name] = ?", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(cmd, n);
                    return cmd.ExecuteNonQuery() > 0;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        // ================== F22: мастер-шаблоны (SCADA_MASTER_TEMPLATES) ==================

        /// <summary>F22: прочитать все именованные мастер-шаблоны блоков (Name + Protocol
        /// TCP/RTU → XML-тело + путь файла), ORDER BY Name.</summary>
        public List<MbMasterTemplate> LoadMasterTemplates()
        {
            var list = new List<MbMasterTemplate>();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return list;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    EnsureTables();
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand(
                        $@"SELECT ID, [Name], [Protocol], [Body], [FilePath]
                            FROM [{Schema.ScadaMasterTemplatesTable}] ORDER BY [Name]", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                        list.Add(new MbMasterTemplate
                        {
                            Id = Convert.ToInt64(rd.GetValue(0)),
                            Name = rd.IsDBNull(1) ? "" : rd.GetString(1),
                            Protocol = rd.IsDBNull(2) ? "" : rd.GetString(2),
                            Body = rd.IsDBNull(3) ? "" : rd.GetString(3),
                            FilePath = rd.IsDBNull(4) ? "" : rd.GetString(4)
                        });
                    break;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
            return list;
        }

        /// <summary>F22: сохранить мастер-шаблон (замена по имени IgnoreCase). Protocol пуст —
        /// эвристика: «RTU» в имени шаблона → RTU, иначе TCP; явный — нормализуется.</summary>
        public void SaveMasterTemplate(string name, string body, string protocol, string? filePath = null)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0) throw new InvalidOperationException("Имя мастер-шаблона не задано");
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            string proto = string.IsNullOrWhiteSpace(protocol)
                ? (n.Contains("RTU", StringComparison.OrdinalIgnoreCase) ? "RTU" : "TCP")
                : NormalizeProtocol(protocol);
            EnsureTables();
            for (int attempt = 1; ; attempt++)
            {
                try { SaveMasterTemplateOnce(n, proto, body ?? "", filePath ?? ""); return; }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private void SaveMasterTemplateOnce(string name, string protocol, string body, string filePath)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                using (var del = new OleDbCommand(
                    $@"DELETE FROM [{Schema.ScadaMasterTemplatesTable}] WHERE [Name] = ?", cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(del, name);
                    del.ExecuteNonQuery();
                }
                using (var ins = new OleDbCommand(
                    $@"INSERT INTO [{Schema.ScadaMasterTemplatesTable}]
                       ([Name], [Protocol], [Body], [FilePath]) VALUES (?, ?, ?, ?)", cn, tr))
                {
                    ins.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(ins, name);
                    OleDbArgs.Add(ins, protocol);
                    OleDbArgs.Add(ins, body);
                    OleDbArgs.Add(ins, filePath);
                    ins.ExecuteNonQuery();
                }
                tr.Commit();
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }

        /// <summary>F22: удалить мастер-шаблон по имени (IgnoreCase); true — строка удалена.</summary>
        public bool DeleteMasterTemplate(string name) =>
            DeleteTemplateRow(Schema.ScadaMasterTemplatesTable, name);

        // ================== F22: шаблоны привязок DataIn (SCADA_DATAIN_TEMPLATES) ==================

        /// <summary>F22: прочитать все именованные шаблоны DataIn (хранение/список/маппинг F23;
        /// генерации привязок DataIn нет).</summary>
        public List<MbDataInTemplate> LoadDataInTemplates()
        {
            var list = new List<MbDataInTemplate>();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return list;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    EnsureTables();
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand(
                        $@"SELECT ID, [Name], [Body], [FilePath]
                            FROM [{Schema.ScadaDataInTemplatesTable}] ORDER BY [Name]", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                        list.Add(new MbDataInTemplate
                        {
                            Id = Convert.ToInt64(rd.GetValue(0)),
                            Name = rd.IsDBNull(1) ? "" : rd.GetString(1),
                            Body = rd.IsDBNull(2) ? "" : rd.GetString(2),
                            FilePath = rd.IsDBNull(3) ? "" : rd.GetString(3)
                        });
                    break;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
            return list;
        }

        /// <summary>F22: сохранить шаблон DataIn (замена по имени IgnoreCase).</summary>
        public void SaveDataInTemplate(string name, string body, string? filePath = null)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0) throw new InvalidOperationException("Имя шаблона DataIn не задано");
            if (string.IsNullOrEmpty(_dbPath)) throw new InvalidOperationException("База данных не задана");
            EnsureTables();
            for (int attempt = 1; ; attempt++)
            {
                try { SaveDataInTemplateOnce(n, body ?? "", filePath ?? ""); return; }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        private void SaveDataInTemplateOnce(string name, string body, string filePath)
        {
            using var cn = new OleDbConnection(ConnStr);
            cn.Open();
            using var tr = cn.BeginTransaction();
            try
            {
                using (var del = new OleDbCommand(
                    $@"DELETE FROM [{Schema.ScadaDataInTemplatesTable}] WHERE [Name] = ?", cn, tr))
                {
                    del.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(del, name);
                    del.ExecuteNonQuery();
                }
                using (var ins = new OleDbCommand(
                    $@"INSERT INTO [{Schema.ScadaDataInTemplatesTable}]
                       ([Name], [Body], [FilePath]) VALUES (?, ?, ?)", cn, tr))
                {
                    ins.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(ins, name);
                    OleDbArgs.Add(ins, body);
                    OleDbArgs.Add(ins, filePath);
                    ins.ExecuteNonQuery();
                }
                tr.Commit();
            }
            catch
            {
                try { tr.Rollback(); } catch { /* соединение могло упасть — не мешаем исходной ошибке */ }
                throw;
            }
        }

        /// <summary>F22: удалить шаблон DataIn по имени (IgnoreCase); true — строка удалена.</summary>
        public bool DeleteDataInTemplate(string name) =>
            DeleteTemplateRow(Schema.ScadaDataInTemplatesTable, name);

        /// <summary>F22: общий DELETE по таблице шаблонов (имя — параметр запроса).</summary>
        private bool DeleteTemplateRow(string table, string name)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0 || string.IsNullOrEmpty(_dbPath)) return false;
            EnsureTables();
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using var cn = new OleDbConnection(ConnStr);
                    cn.Open();
                    using var cmd = new OleDbCommand($"DELETE FROM [{table}] WHERE [Name] = ?", cn);
                    cmd.CommandTimeout = DbProbe.TimeoutSeconds;
                    OleDbArgs.Add(cmd, n);
                    return cmd.ExecuteNonQuery() > 0;
                }
                catch (OleDbException ex) when (attempt < 3 && DbProbe.IsLockError(ex.Message))
                {
                    System.Threading.Thread.Sleep(300 * attempt);
                }
            }
        }

        /// <summary>F13: «Записать привязки в IO_LIST» — заполнить M_BusBlock (имя блока мастера)
        /// и M_BusDataOut (путь «Block.DataOut[i]») у точек, попавших в пакеты плана; точки
        /// с адресацией, не попавшие ни в один пакет, очищаются. Ограничение F9.4 сохраняется:
        /// привязка пишется только для Modbus-сигнала (IO Type «M…»/«COM»). Аудит — Update
        /// по каждому изменённому полю (одна сессия репозитория).</summary>
        public MbBindingsWriteResult WriteBindings(MbPlanResult plan, IIOListRepository repo)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            var result = new MbBindingsWriteResult();

            // индекс IO_LIST по Tag (дубли тегов — первая запись)
            var byTag = new Dictionary<string, IoPoint>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in repo.GetAll())
                if (!string.IsNullOrWhiteSpace(p.Tag))
                    byTag.TryAdd(p.Tag!.Trim(), p);

            // ожидаемые привязки: Tag → (блок, путь DataOut)
            var wanted = new Dictionary<string, (string Block, string DataOut)>(StringComparer.OrdinalIgnoreCase);
            foreach (var packet in plan.Packets)
                foreach (var b in packet.Bindings)
                    if (b.Tag.Length > 0)
                        wanted[b.Tag] = (b.BlockName, b.DataOutExpr);

            repo.BeginSession();
            try
            {
                foreach (var kv in wanted)
                {
                    if (!byTag.TryGetValue(kv.Key, out var p))
                    {
                        result.NotFound++;
                        continue;
                    }
                    if (!ModbusLinkService.IsModbusSignal(p))
                    {
                        result.Errors.Add(
                            $"привязка не записана: «{kv.Key}» — IO Type «{p.Identification.IoType ?? "?"}» " +
                            "не Modbus-сигнала (нет признака M*/COM)");
                        continue;
                    }
                    p.Modbus ??= new Modbus();
                    p.Modbus.BusBlock = kv.Value.Block;
                    p.Modbus.BusDataOut = kv.Value.DataOut;
                    if (repo.Update(p) > 0) result.Updated++; // аудит Update по изменённым полям (F7)
                }

                // очистка: адресация есть, а в пакеты точка не попала
                foreach (var p in byTag.Values)
                {
                    if (string.IsNullOrWhiteSpace(p.Modbus?.PlcTag)) continue;
                    if (wanted.ContainsKey(p.Tag!.Trim())) continue;
                    if (string.IsNullOrEmpty(p.Modbus!.BusBlock) && string.IsNullOrEmpty(p.Modbus.BusDataOut))
                        continue;
                    if (!ModbusLinkService.IsModbusSignal(p)) continue;
                    p.Modbus.BusBlock = null;
                    p.Modbus.BusDataOut = null;
                    if (repo.Update(p) > 0) result.Cleared++;
                }
                repo.EndSession();
            }
            catch (Exception)
            {
                repo.AbortSession();
                throw;
            }
            return result;
        }
    }
}



