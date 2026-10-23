using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// F13: генератор XML блоков Modbus из внешнего шаблона (замена ScadaPouXmlWriter).
    /// Шаблон (BufScadaPOUS с POU и блоком мастера MB_TCP_MASTER) задаёт
    /// структуру и логику одного запроса целиком; F15: блок мастера опознаётся по имени
    /// объекта (MB_TCP_MASTER/MB_RTU_MASTER в ISAOBJSINFO или Info блока), а не по
    /// значению IsaObjId — ID rec при импорте в SCADA переподставляется.
    /// Генератор размножает юнит мастера по числу
    /// пакетов (MbPacketPlan): имена (MB_TCP_MASTER_4_1 → _4_2…), T11ID/cardId клонов +
    /// ISACARDSINFO, подстановки значений входов по связям и IP/порт в IV мастера, смещение
    /// координат. Дополнительно строится файл привязок (корень &lt;BufScada&gt;): блок 31
    /// «{Master}.DataOut[{i}]» на каждую уникальную пару (блок, индекс) — cardId/IV берутся
    /// от блока мастера пакета.
    /// </summary>
    public sealed class ScadaTemplateXmlService
    {
        /// <summary>Имя файла шаблона по умолчанию (поиск: каталог exe → вверх в Work\).</summary>
        public const string DefaultTemplateName = "MB_TCP_MASTER_пример_new.xml";

        /// <summary>F15: исторический IsaObjId мастера эталонного шаблона (ISAOBJSINFO:
        /// LibName TenixRtLib). Критерием «блок мастера» НЕ является: мастер опознаётся
        /// по имени объекта (MB_TCP_MASTER/MB_RTU_MASTER), а ID rec SCADA переподставляет
        /// при импорте в свою базу. Используется только как fallback для вырожденных
        /// шаблонов (нет Params.IsaObjId у мастера / нет ISAOBJSINFO).</summary>
        public const int IsaMaster = 15003;

        /// <summary>F13: результат разбора шаблона (валидность + сводка для статуса окна).</summary>
        public sealed class TemplateInfo
        {
            /// <summary>true — шаблон пригоден для генерации.</summary>
            public bool Valid;

            /// <summary>Текст ошибки валидации (пуст, если Valid).</summary>
            public string Error = "";

            /// <summary>Имя блока мастера шаблона (MB_TCP_MASTER_4_1) — база имен пакетов.</summary>
            public string MasterNameBase = "";

            /// <summary>Число блоков мастера в шаблоне.</summary>
            public int MasterCount;

            /// <summary>F15: IsaObjId блока мастера из шаблона (0 — у мастера нет
            /// Params.IsaObjId). Значение произвольно (15003, 17573, …) и переподставляется
            /// SCADA при импорте; используется только как связка «блок ↔ rec ISAOBJSINFO».</summary>
            public int MasterIsaId;

            /// <summary>Число блоков в шаблоне.</summary>
            public int BlockCount;

            /// <summary>Число связей в шаблоне.</summary>
            public int LinkCount;

            /// <summary>Итоговый текст для статуса окна.</summary>
            public string Summary =>
                Valid
                    ? $"мастер {MasterNameBase}; блоков: {BlockCount}; связей: {LinkCount}"
                    : Error;
        }

        /// <summary>F13: результат генерации — основной BufScadaPOUS + файл привязок.</summary>
        public sealed class GeneratedXml
        {
            public string PousXml = "";
            public string BindingsXml = "";

            /// <summary>F19: предупреждения генерации (исключённые юниты-мастера шаблона,
            /// скрытые карточки) — выводятся в журнал/статус при экспорте.</summary>
            public List<string> Warnings { get; } = new();

            /// <summary>F13.1: метаданные блоков-мастеров пакетов (имя → cardId, IV мастера) —
            /// используются генератором привязок Data_Out (блоки-источники и их карточки).</summary>
            public Dictionary<string, (int CardId, string Iv)> MasterMeta { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        // ================== Разбор/валидация шаблона ==================

        /// <summary>F13/F15: разобрать и провалидировать шаблон: корень BufScadaPOUS, есть POU
        /// с блоками, есть блок мастера (F15: по имени объекта MB_TCP_MASTER/MB_RTU_MASTER,
        /// не по значению IsaObjId) с непустым Info.</summary>
        public TemplateInfo Parse(string? templateXml)
        {
            var r = new TemplateInfo();
            if (string.IsNullOrWhiteSpace(templateXml)) { r.Error = "шаблон пуст"; return r; }
            XDocument doc;
            try { doc = XDocument.Parse(templateXml!); }
            catch (Exception ex) { r.Error = $"XML не разбирается: {ex.Message}"; return r; }
            if (doc.Root == null || doc.Root.Name != "BufScadaPOUS")
            { r.Error = "корень документа должен быть <BufScadaPOUS>"; return r; }

            var graf = doc.Root.Element("POUS")?.Element("OnePOU")?.Element("ISAGraf");
            var blocksEl = graf?.Element("Blocks");
            if (blocksEl == null) { r.Error = "в шаблоне нет POUS/OnePOU/ISAGraf/Blocks"; return r; }

            var blocks = blocksEl.Elements("Block").ToList();
            if (blocks.Count == 0) { r.Error = "в шаблоне нет блоков"; return r; }
            r.BlockCount = blocks.Count;
            r.LinkCount = graf?.Element("Links")?.Elements("Link").Count() ?? 0;

            var masters = FindMasterElements(blocks,
                doc.Root.Element("ISAOBJSINFO")?.Elements("rec") ?? Enumerable.Empty<XElement>());
            if (masters.Count == 0)
            { r.Error = "в шаблоне нет блока мастера (rec ISAOBJSINFO «MB_TCP_MASTER»/«MB_RTU_MASTER» " +
                        "или блок GROBJTYPE=37 с именем MB_TCP_MASTER*)"; return r; }
            r.MasterCount = masters.Count;
            r.MasterIsaId = int.TryParse((string?)masters[0].Element("Params")?.Attribute("IsaObjId"), out int mio)
                ? mio : 0;
            r.MasterNameBase = ((string?)masters[0].Attribute("Info") ?? "").Trim();
            if (r.MasterNameBase.Length == 0) { r.Error = "у блока мастера пустой Info"; return r; }

            r.Valid = true;
            return r;
        }

        /// <summary>true — имя объекта/блока мастера: MB_TCP_MASTER / MB_RTU_MASTER
        /// (IgnoreCase, по началу имени — покрывает «MB_TCP_MASTER_4_1» и rec Info
        /// объекта библиотеки «MB_TCP_MASTER»).</summary>
        public static bool IsMasterObjectName(string? name) =>
            !string.IsNullOrWhiteSpace(name) &&
            (name!.StartsWith("MB_TCP_MASTER", StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("MB_RTU_MASTER", StringComparison.OrdinalIgnoreCase));

        /// <summary>F15: найти блоки мастера в шаблоне. Критерий — имя, а не значение
        /// IsaObjId (ID rec SCADA переподставляет при импорте в свою базу):
        /// ① блок, чей Params.IsaObjId равен ID rec «MB_TCP_MASTER»/«MB_RTU_MASTER»
        ///    в ISAOBJSINFO — связка «блок ↔ объект библиотеки» внутри одного шаблона
        ///    (само значение ID произвольно: 15003, 17573, …);
        /// ② fallback (нет/битый ISAOBJSINFO): блок GROBJTYPE=37 с именем MB_TCP_MASTER*.</summary>
        public static List<XElement> FindMasterElements(XDocument doc)
        {
            var root = doc.Root;
            var blocks = root?.Element("POUS")?.Element("OnePOU")?.Element("ISAGraf")
                             ?.Element("Blocks")?.Elements("Block") ?? Enumerable.Empty<XElement>();
            var recs = root?.Element("ISAOBJSINFO")?.Elements("rec") ?? Enumerable.Empty<XElement>();
            return FindMasterElements(blocks, recs);
        }

        /// <summary>F15: то же для готовых списков блоков и rec ISAOBJSINFO (см. FindMasterElements).</summary>
        public static List<XElement> FindMasterElements(IEnumerable<XElement> blocks, IEnumerable<XElement> objRecs)
        {
            var masterObjIds = new HashSet<int>();
            foreach (var rec in objRecs)
                if (IsMasterObjectName((string?)rec.Attribute("Info")) &&
                    int.TryParse((string?)rec.Attribute("ID"), out int id))
                    masterObjIds.Add(id);
            var list = blocks.ToList();
            // ① связка «блок ↔ rec ISAOBJSINFO» (внутри одного шаблона)
            if (masterObjIds.Count > 0)
            {
                var byRec = list.Where(b =>
                    int.TryParse((string?)b.Element("Params")?.Attribute("IsaObjId"), out int io) &&
                    masterObjIds.Contains(io)).ToList();
                if (byRec.Count > 0) return byRec;
            }
            // ② имя блока (GROBJTYPE=37)
            return list.Where(b =>
                b.Attribute("GROBJTYPE")?.Value == "37" &&
                IsMasterObjectName((string?)b.Attribute("Info"))).ToList();
        }

        /// <summary>F13: имя блока мастера для пакета No: ведущие цифры имени шаблона заменяются
        /// номером пакета (MB_TCP_MASTER_4_1 + 2 → MB_TCP_MASTER_4_2; нет цифр → «_No»).</summary>
        public static string MasterNameFor(string? masterNameBase, int packetNo)
        {
            string name = (masterNameBase ?? "").Trim();
            if (name.Length == 0) name = "MB_TCP_MASTER";
            var m = Regex.Match(name, @"^(.*?)(\d+)$");
            return m.Success ? m.Groups[1].Value + packetNo : name + "_" + packetNo;
        }

        /// <summary>F14: формат входа Address блока — «регистр + адрес». Префикс — по коду
        /// функции (FC1 → 0, FC2 → 1, FC3 → 4, FC4 → 3), адрес дополняется ведущими нулями
        /// минимум до 3 цифр (итог ≥ 4 знака: FC3, 1 → 4001; 602 → 4602); адрес ≥ 10000
        /// (уже 5-значный, напр. 40506) — как есть. on=false — сырой адрес.</summary>
        public static string FormatBlockAddress(int rawAddr, int funcCode, bool on)
        {
            if (!on || rawAddr >= 10000) return rawAddr.ToString();
            string prefix = funcCode switch
            {
                1 => "0",
                2 => "1",
                4 => "3",
                _ => "4"
            };
            return prefix + rawAddr.ToString().PadLeft(3, '0');
        }

        /// <summary>F14: выбор паттерна имени блока по протоколу шаблона: мастер с «RTU» в имени
        /// (IgnoreCase) → BlockNamePatternRtu (пусто → BlockNamePattern); иначе BlockNamePattern.</summary>
        public static string SelectBlockNamePattern(ScadaMbGenSettings s, string? masterNameBase)
        {
            bool rtu = (masterNameBase ?? "").IndexOf("RTU", StringComparison.OrdinalIgnoreCase) >= 0;
            if (rtu && !string.IsNullOrWhiteSpace(s.BlockNamePatternRtu))
                return s.BlockNamePatternRtu;
            return s.BlockNamePattern;
        }

        /// <summary>F13.1: имя блока мастера по настраиваемому правилу. Пустой паттерн —
        /// историческое MasterNameFor(masterNameBase, No). Иначе — маска с токенами:
        /// {Seq} — номер пакета, {Slave} — SlaveId, {Station} — станция («_» вместо пробелов),
        /// {Ip} — IP через «_», {Func} — код функции, {Port} — порт; F24: {ИмяКолонки} —
        /// значение колонки IO_LIST первой точки пакета; прочие символы — литерал.</summary>
        public static string ApplyBlockNamePattern(string? pattern, MbPacketPlan packet, string? masterNameBase)
        {
            string pat = (pattern ?? "").Trim();
            if (pat.Length == 0)
                return MasterNameFor(masterNameBase, packet.No);
            string name = Regex.Replace(pat, @"\{([^{}]+)\}", m =>
            {
                string key = m.Groups[1].Value.Trim();
                switch (key) // служебные токены F13.1/F21
                {
                    case "Seq": return packet.No.ToString();
                    case "Slave": return packet.SlaveId.ToString();
                    case "Station": return SanitizeNamePart(packet.Station);
                    case "Controller": return SanitizeNamePart(packet.Controller); // F37
                    case "Ip": return $"{packet.Ip1}_{packet.Ip2}_{packet.Ip3}_{packet.Ip4}";
                    case "Func": return packet.FuncCode.ToString();
                    case "Port": return packet.Port.ToString();
                }
                // F24: {ИмяКолонки} — значение колонки IO_LIST первой точки пакета
                if (packet.ColumnValues.TryGetValue(key, out var v) && v.Length > 0)
                    return SanitizeNamePart(v);
                return m.Value; // неизвестный токен — литерал как есть
            });
            name = SanitizeNamePart(name);
            return name.Length > 0 ? name : MasterNameFor(masterNameBase, packet.No);
        }

        /// <summary>F17: имя POU по паттерну (PouNamePattern — основной файл,
        /// DataOutPouNamePattern — файл привязок). Пустой паттерн или пустой результат
        /// санитизации — «» (вызывающий оставляет историческое имя). ctx — контекст пакета
        /// (токены {Seq}/{Slave}/{Station}/{Ip}/{Port}/{Func}); null — только
        /// {Master}/{Date}/{Time}. {Date} = yyyyMMdd, {Time} = HHmmss на момент вызова.</summary>
        public static string BuildPouName(string? pattern, string? masterNameBase, MbPacketPlan? ctx)
        {
            string pat = (pattern ?? "").Trim();
            if (pat.Length == 0) return "";
            string name = Regex.Replace(pat, @"\{([^{}]+)\}", m =>
            {
                string key = m.Groups[1].Value.Trim();
                switch (key) // токены без контекста пакета
                {
                    case "Master": return SanitizeNamePart(masterNameBase ?? "");
                    case "Date": return DateTime.Now.ToString("yyyyMMdd");
                    case "Time": return DateTime.Now.ToString("HHmmss");
                }
                if (ctx != null)
                {
                    switch (key) // токены контекста первого пакета (F17)
                    {
                        case "Seq": return ctx.No.ToString();
                        case "Slave": return ctx.SlaveId.ToString();
                        case "Station": return SanitizeNamePart(ctx.Station);
                        case "Controller": return SanitizeNamePart(ctx.Controller); // F37
                        case "Ip": return $"{ctx.Ip1}_{ctx.Ip2}_{ctx.Ip3}_{ctx.Ip4}";
                        case "Port": return ctx.Port.ToString();
                        case "Func": return ctx.FuncCode.ToString();
                    }
                    // F24: {ИмяКолонки} — значение колонки IO_LIST первой точки пакета
                    if (ctx.ColumnValues.TryGetValue(key, out var v) && v.Length > 0)
                        return SanitizeNamePart(v);
                }
                return m.Value; // неизвестный токен — литерал как есть
            });
            name = SanitizeNamePart(name);
            return name;
        }

        /// <summary>F13.1: очистка имени блока — пробелы/точки/кавычки → «_», пустые края срезаются.</summary>
        private static string SanitizeNamePart(string s)
        {
            string v = (s ?? "").Trim();
            v = Regex.Replace(v, @"[\s.""'`]+", "_");
            return v.Trim('_', ' ');
        }

        /// <summary>F34: стандартные входы мастера, заполняемые из плана при генерации;
        /// для RTU-мастера IpOctet_1..4/Port не синтезируются (у MB_RTU_MASTER их нет).</summary>
        public static readonly string[] MasterInputPorts =
        {
            "SlaveId", "Mode", "Address", "Count", "TimeOut",
            "IpOctet_1", "IpOctet_2", "IpOctet_3", "IpOctet_4", "Port"
        };

        /// <summary>F34/F35: значение входа мастера при генерации — строго из плана пакета
        /// (SlaveId/Mode/Address/Count/TimeOut/IpOctet_1..4/Port); скрытых источников нет
        /// (VarValues F23 удалён — недопустимы подстановки вне плана/таблиц маппинга).
        /// Порт вне списка — null.</summary>
        public static string? MasterInputValue(MbPacketPlan p, string port, ScadaMbGenSettings s)
        {
            return port switch
            {
                // F33: IP/порт/SlaveId/Timeout задаются входами блока по связям шаблона
                "SlaveId" => p.SlaveId.ToString(),
                "Mode" => p.Mode.ToString(),
                "Address" => FormatBlockAddress(p.Address, p.FuncCode, s.FormatAddress),
                "Count" => p.Count.ToString(),
                "TimeOut" => p.TimeoutMs.ToString(),
                "IpOctet_1" => p.Ip1.ToString(),
                "IpOctet_2" => p.Ip2.ToString(),
                "IpOctet_3" => p.Ip3.ToString(),
                "IpOctet_4" => p.Ip4.ToString(),
                "Port" => p.Port.ToString(),
                _ => null
            };
        }

        /// <summary>F35: позиционная строка IV блока мастера со значениями плана пакета —
        /// вместо синтеза блоков-констант F34. Карта позиций (с 1): 2=SlaveId, 3=TimeOut,
        /// 4=Mode, 5=Address, 7=Count (поз. 6/8 — буферы «(246(0))» скелета),
        /// 17-20=IpOctet_1..4, 21=Port; значения пишутся
        /// обычными числами (как в эталонных примерах). Пустой/усечённый IV шаблона
        /// дополняется скелетом эталонов (FALSE,0,0,0,0,(246(0)),0,(246(0)),FALSE,TRUE,
        /// 3(0),FALSE,0,65533,0,6(0),0,0,0,0,0). RTU-мастер: позиции 17-21 не пишутся
        /// (у MB_RTU_MASTER нет TCP-адреса; COM-порт задаёт шаблон RTU).</summary>
        public static string BuildMasterIv(string templateIv, MbPacketPlan p,
            ScadaMbGenSettings s, bool masterRtu)
        {
            const string skeleton =
                "FALSE,0,0,0,0,(246(0)),0,(246(0)),FALSE,TRUE,3(0),FALSE,0,65533,0,6(0),0,0,0,0,0";
            string[] parts = string.IsNullOrWhiteSpace(templateIv)
                ? skeleton.Split(',')
                : templateIv!.Split(',');
            if (parts.Length < 21) // усечённый IV шаблона дополняется скелетом справа
            {
                var ext = skeleton.Split(',');
                Array.Copy(parts, ext, parts.Length);
                parts = ext;
            }
            void Set(int pos, string? val)
            {
                if (!string.IsNullOrEmpty(val) && pos >= 1 && pos <= parts.Length)
                    parts[pos - 1] = val!;
            }
            Set(2, MasterInputValue(p, "SlaveId", s));
            Set(3, MasterInputValue(p, "TimeOut", s));
            Set(4, MasterInputValue(p, "Mode", s));
            Set(5, MasterInputValue(p, "Address", s));
            Set(7, MasterInputValue(p, "Count", s));
            if (!masterRtu)
            {
                Set(17, MasterInputValue(p, "IpOctet_1", s));
                Set(18, MasterInputValue(p, "IpOctet_2", s));
                Set(19, MasterInputValue(p, "IpOctet_3", s));
                Set(20, MasterInputValue(p, "IpOctet_4", s));
                Set(21, MasterInputValue(p, "Port", s));
            }
            return string.Join(",", parts);
        }

        // ================== Генерация ==================

        /// <summary>Внутренняя модель блока шаблона.</summary>
        private sealed class TBlock
        {
            public XElement El = null!;
            public int T11;
            public int Type;          // GROBJTYPE
            public string Info = "";
            public int IsaObjId;
            public int CardId;

            /// <summary>F15: блок мастера — опознан по имени объекта (не по IsaObjId).</summary>
            public bool IsMaster;
        }

        /// <summary>Внутренняя модель связи шаблона.</summary>
        private sealed class TLink
        {
            public XElement El = null!;
            public int FromT11;
            public int ToT11;
        }

        /// <summary>F13: собрать BufScadaPOUS (пакеты) и BufScada (привязки DataOut) из плана
        /// пакетов по шаблону. Юнит = компонента связности ровно с одним мастером; блоки
        /// связных с несколькими мастерами (или без мастера) — общие, пишутся один раз.
        /// F38: dataVars/dataVarsTemplateKey — таблица объектов data-шаблонов (маппинг
        /// «колонка/шаблон/литерал») для подстановки ТЕКСТОВ примитивов [TEXT]: всё значение
        /// [TEXT] клона примитива заменяется резолвом строки текстового объекта
        /// (LOOPComment(Text) и др., идентификатор — первое слово [TEXT]); null/пусто —
        /// тексты копируются как в шаблоне (строгий режим F35).</summary>
        public GeneratedXml Write(IReadOnlyList<MbPacketPlan> packets, string templateXml, ScadaMbGenSettings s,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>? dataVars = null,
            string dataVarsTemplateKey = "")
        {
            if (packets == null) throw new ArgumentNullException(nameof(packets));
            if (s == null) throw new ArgumentNullException(nameof(s));
            var info = Parse(templateXml);
            if (!info.Valid) throw new InvalidOperationException("Шаблон: " + info.Error);

            var root = XDocument.Parse(templateXml!).Root!;
            var onePou = root.Element("POUS")!.Element("OnePOU")!;
            var grafTpl = onePou.Element("ISAGraf")!;
            var blocksEl = grafTpl.Element("Blocks")!;
            var linksEl = grafTpl.Element("Links");

            // —— модель шаблона ——
            var blocks = new List<TBlock>();
            foreach (var b in blocksEl.Elements("Block"))
            {
                var p = b.Element("Params");
                blocks.Add(new TBlock
                {
                    El = b,
                    T11 = int.TryParse((string?)b.Attribute("T11ID"), out int t) ? t : 0,
                    Type = int.TryParse((string?)b.Attribute("GROBJTYPE"), out int ty) ? ty : 0,
                    Info = (string?)b.Attribute("Info") ?? "",
                    IsaObjId = int.TryParse((string?)p?.Attribute("IsaObjId"), out int io) ? io : 0,
                    CardId = int.TryParse((string?)p?.Attribute("cardId"), out int c) ? c : 0
                });
            }

            // F15: блоки мастера — по имени объекта (IsaObjId не критерий: SCADA
            // переподставляет ID при импорте); ① связка блок ↔ rec ISAOBJSINFO,
            // ② fallback GROBJTYPE=37 + имя MB_TCP_MASTER*
            var masterSet = new HashSet<XElement>(FindMasterElements(blocksEl.Elements("Block"),
                root.Element("ISAOBJSINFO")?.Elements("rec") ?? Enumerable.Empty<XElement>()));
            foreach (var b in blocks) b.IsMaster = masterSet.Contains(b.El);

            var links = new List<TLink>();
            foreach (var l in linksEl?.Elements("Link") ?? Enumerable.Empty<XElement>())
            {
                var fp = ((string?)l.Element("FirstPoint")?.Attribute("FP") ?? "").Split('|');
                var lp = ((string?)l.Element("LastPoint")?.Attribute("LP") ?? "").Split('|');
                links.Add(new TLink
                {
                    El = l,
                    FromT11 = fp.Length > 0 && int.TryParse(fp[0], out int f) ? f : 0,
                    ToT11 = lp.Length > 0 && int.TryParse(lp[0], out int t) ? t : 0
                });
            }

            // —— компоненты связности (union-find); юнит — компонента ровно с одним мастером ——
            var parent = new Dictionary<int, int>();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { parent[Find(a)] = Find(b); }
            foreach (var b in blocks) parent[b.T11] = b.T11;
            foreach (var l in links)
                if (parent.ContainsKey(l.FromT11) && parent.ContainsKey(l.ToT11)) Union(l.FromT11, l.ToT11);
            var compMasters = new Dictionary<int, List<TBlock>>();
            foreach (var b in blocks.Where(x => x.IsMaster))
            {
                int r = Find(b.T11);
                if (!compMasters.TryGetValue(r, out var lst)) compMasters[r] = lst = new List<TBlock>();
                lst.Add(b);
            }

            // юниты: (мастер, блоки юнита) в порядке следования мастеров в документе
            var singleComps = new HashSet<int>(compMasters.Where(kv => kv.Value.Count == 1).Select(kv => kv.Key));
            var units = new List<(TBlock Master, List<TBlock> Unit)>();
            var unitT11Set = new HashSet<int>();
            foreach (var b in blocks.Where(x => x.IsMaster && singleComps.Contains(Find(x.T11))))
            {
                int r = Find(b.T11);
                var unit = blocks.Where(x => Find(x.T11) == r).ToList();
                units.Add((b, unit));
                foreach (var ub in unit) unitT11Set.Add(ub.T11);
            }
            if (units.Count == 0)
                throw new InvalidOperationException(
                    "в шаблоне нет блока мастера, обвязанного только своими объектами (все мастеры связаны между собой)");

            // F19: активен только основной юнит — «мастер, указанный в XML-шаблоне»
            // (units[0]: порядок документа совпадает с MasterNameBase из Parse). Лишние
            // юниты-мастера не размножаются: их имена в превью отсутствуют, а размножение
            // давало дубли (ModbusTCP_2 ×2) и лишние блоки (ModbusTCP_3). Блоки/связи
            // исключённых юнитов в результат не попадают; предупреждение — в журнал/статус.
            var warnings = new List<string>();
            if (units.Count > 1)
            {
                var extra = string.Join(", ", units.Skip(1).Select(u => u.Master.Info));
                warnings.Add($"в шаблоне {units.Count} блока мастера — сгенерирован юнит мастера " +
                    $"«{units[0].Master.Info}»; блоки мастера [{extra}] исключены из результата " +
                    "(в превью отображается один блок на пакет)");
            }
            var activeT11 = new HashSet<int>(units[0].Unit.Select(x => x.T11));
            var droppedT11 = new HashSet<int>(unitT11Set.Where(t => !activeT11.Contains(t)));

            // —— счётчики клонов: продолжение после шаблонных ID ——
            int maxTplT11 = blocks.Count > 0 ? blocks.Max(b => b.T11) : 0;
            int nextT11 = Math.Max(s.BaseT11Id, maxTplT11 + 1);
            int maxTplCard = blocks.Count > 0 ? blocks.Max(b => b.CardId) : 0;
            int nextCard = Math.Max(s.BaseCardId, maxTplCard + 1);

            var commonBlocks = blocks.Where(b => !unitT11Set.Contains(b.T11)).ToList();
            // F19: связи исключённых юнитов не включаются ни в «общие», ни в клоны —
            // иначе в результат утекут ссылки на блоки, которых в нём больше нет
            var commonLinks = links
                .Where(l => !droppedT11.Contains(l.FromT11) && !droppedT11.Contains(l.ToT11) &&
                            (!unitT11Set.Contains(l.FromT11) || !unitT11Set.Contains(l.ToT11))).ToList();
            var outBlocks = new List<XElement>();
            var outLinks = new List<XElement>();
            var outPrims = new List<XElement>();
            var outCards = new List<XElement>();   // новые rec ISACARDSINFO (клоны)
            var masterMeta = new Dictionary<string, (int CardId, string Iv)>(StringComparer.OrdinalIgnoreCase);
            var tplPrims = onePou.Element("GrObj")?.Elements("OnePrim").ToList() ?? new List<XElement>();

            // общие блоки/связи — один раз, как в шаблоне
            foreach (var b in commonBlocks) outBlocks.Add(new XElement(b.El));
            foreach (var l in commonLinks) outLinks.Add(new XElement(l.El));
            foreach (var pr in tplPrims)
                if (!int.TryParse((string?)pr.Attribute("SourceT11ID"), out int st) || !unitT11Set.Contains(st))
                    outPrims.Add(new XElement(pr));

            int cols = Math.Max(1, s.ColumnsPerRow);
            bool rtuIpWarned = false;   // F35: RTU без IP/порта в IV — один раз на экспорт
            var textWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // F38: (пакет|объект)

            foreach (var p in packets)
            {
                int k = p.No;
                int dx = ((k - 1) % cols) * Math.Max(1, s.ColumnStep);
                int dy = ((k - 1) / cols) * Math.Max(1, s.RowStep);

                // F19: размножается только основной юнит — «мастер, указанный в XML-шаблоне»
                // (units[0]); имя мастера строго из плана (превью), без сдвига за счёт
                // дополнительных юнитов шаблона (units[1..] исключены, см. warnings)
                for (int ui = 0; ui < 1; ui++)
                {
                    var (master, unit) = units[ui];
                    var t11Map = new Dictionary<int, int>();
                    var cloneByT11 = new Dictionary<int, XElement>();
                    string masterName = p.MasterName;

                    foreach (var b in unit)
                    {
                        int newT11 = k == 1 ? b.T11 : nextT11++;
                        t11Map[b.T11] = newT11;
                        string name = b.T11 == master.T11 ? masterName : ShiftInstanceName(b.Info, k);
                        int newCard = b.CardId > 0
                            ? (k == 1 ? b.CardId : NextCardId(ref nextCard))
                            : 0;

                        var clone = new XElement(b.El);
                        clone.SetAttributeValue("T11ID", newT11);
                        clone.SetAttributeValue("Info", name);
                        var g = clone.Element("Graphics");
                        if (g != null)
                        {
                            g.SetAttributeValue("X",
                                (int.TryParse((string?)g.Attribute("X"), out int x) ? x : 0) + dx);
                            g.SetAttributeValue("Y",
                                (int.TryParse((string?)g.Attribute("Y"), out int y) ? y : 0) + dy);
                        }
                        var pr = clone.Element("Params");
                        if (pr != null && b.CardId > 0)
                        {
                            pr.SetAttributeValue("cardId", newCard);
                            if (newCard != b.CardId) // карточка клона — новая, Info = новое имя
                            {
                                var tplRec = root.Element("ISACARDSINFO")?.Elements("rec")
                                    .FirstOrDefault(r => ((string?)r.Attribute("ID")) == b.CardId.ToString());
                                var rec = tplRec != null ? new XElement(tplRec) : new XElement("rec");
                                rec.SetAttributeValue("ID", newCard);
                                rec.SetAttributeValue("Info", name);
                                if (rec.Attribute("IsRetain") == null) rec.SetAttributeValue("IsRetain", "1");
                                if (rec.Attribute("Name") == null) rec.SetAttributeValue("Name", "");
                                if (rec.Attribute("SSize") == null) rec.SetAttributeValue("SSize", "0");
                                if (rec.Attribute("KLPath") == null) rec.SetAttributeValue("KLPath", "");
                                outCards.Add(rec);
                            }
                        }
                        cloneByT11[newT11] = clone;
                        outBlocks.Add(clone);
                    }

                    // подстановки значений входов мастера по связям шаблона (константе —
                    // Info/T11Text, тегу — IV); F35: остальные входы пишутся в <IV> мастера
                    foreach (var l in links.Where(l => l.ToT11 == master.T11))
                    {
                        string port = PortOf(l.El.Element("LastPoint")?.Attribute("LP")?.Value);
                        string? val = port.Length > 0 ? MasterInputValue(p, port, s) : null;
                        if (val != null && t11Map.TryGetValue(l.FromT11, out int srcNew))
                            ApplyInputValue(cloneByT11[srcNew], val);
                    }

                    var masterClone = cloneByT11[t11Map[master.T11]];

                    // F35: синтез блоков-констант F34 отменён — недостающие входы мастера
                    // (Mode/Address/Count/IpOctet_1..4/Port и т.д.) пишутся в позиционную
                    // строку <IV> блока мастера (BuildMasterIv ниже)
                    // RTU-опознание — имя блока/пакета либо имя объекта библиотеки
                    // (rec ISAOBJSINFO по IsaObjId мастера: MB_RTU_MASTER)
                    string masterObjName =
                        root.Element("ISAOBJSINFO")?.Elements("rec")
                            .FirstOrDefault(r => (string?)r.Attribute("ID") ==
                                                 master.IsaObjId.ToString())?
                            .Attribute("Info")?.Value ?? "";
                    bool masterRtu =
                        masterName.IndexOf("RTU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        master.Info.IndexOf("RTU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        masterObjName.IndexOf("RTU", StringComparison.OrdinalIgnoreCase) >= 0;


                    // F35: позиционная строка IV мастера — значения плана по карте позиций
                    // (2=SlaveId, 3=TimeOut, 4=Mode, 5=Address, 7=Count, 17-20=IpOctet_1..4,
                    // 21=Port); пустой IV шаблона дополняется скелетом эталонных примеров;
                    // RTU-мастеру IP/порт (17-21) не пишутся (+ предупреждение один раз)
                    var mp = masterClone.Element("Params");
                    string srcIv = mp?.Element("IV")?.Value ?? "";
                    string newIv = BuildMasterIv(srcIv, p, s, masterRtu);
                    if (mp != null)
                    {
                        var ivEl = mp.Element("IV");
                        if (ivEl == null) { ivEl = new XElement("IV"); mp.Add(ivEl); }
                        ivEl.Value = newIv;
                    }
                    if (masterRtu && !rtuIpWarned)
                    {
                        rtuIpWarned = true;
                        warnings.Add("F35: RTU-мастер — IP/порт (позиции 17-21 IV) не " +
                            "записываются (у MB_RTU_MASTER их нет; COM-порт задаёт шаблон RTU)");
                    }
                    // cardId мастера пакета — из Params клона (учтён в cardMap)
                    int masterCard = mp != null && int.TryParse((string?)mp.Attribute("cardId"), out int mc)
                        ? mc : 0;
                    masterMeta[masterName] = (masterCard, newIv);

                    // связи юнита: PL сдвигается на (dx,dy), T11ID перенумеровываются
                    // F19: только блоки активного юнита — чужие юниты не включаются
                    // (иначе связи исключённых юнитов копировались с несмапленными T11)
                    foreach (var l in links.Where(l =>
                                 activeT11.Contains(l.FromT11) && activeT11.Contains(l.ToT11)))
                    {
                        var clone = new XElement(l.El);
                        ShiftPointList(clone, dx, dy);
                        RemapEndPoint(clone, "FirstPoint", "FP", t11Map);
                        RemapEndPoint(clone, "LastPoint", "LP", t11Map);
                        outLinks.Add(clone);
                    }

                    // примитивы шаблона (заголовки), относящиеся к юниту — клонируются с пакетом
                    foreach (var pr in tplPrims)
                    {
                        if (!int.TryParse((string?)pr.Attribute("SourceT11ID"), out int st)) continue;
                        if (!t11Map.TryGetValue(st, out int stNew)) continue;
                        var prc = new XElement(pr);
                        prc.SetAttributeValue("SourceT11ID", stNew);
                        prc.SetAttributeValue("X",
                            (int.TryParse((string?)prc.Attribute("X"), out int px) ? px : 0) + dx);
                        prc.SetAttributeValue("Y",
                            (int.TryParse((string?)prc.Attribute("Y"), out int py) ? py : 0) + dy);
                        // F38: подстановка текста [TEXT] примитива по таблице объектов
                        // (LOOPComment(Text) и др.) — значение пишется пер-пакетно
                        ApplyPrimTextSubstitution(prc, p, dataVars, dataVarsTemplateKey,
                            warnings, textWarned);
                        outPrims.Add(prc);
                    }
                }
            }

            // —— BufScadaPOUS ——
            var outRoot = new XElement("BufScadaPOUS");
            outRoot.Add(new XElement(root.Element("Common")!));
            var pou = new XElement(onePou);
            // F17: имя POU основного файла — по PouNamePattern (контекст — первый пакет);
            // пусто/не разрешилось — историческое NAME из шаблона (MB_Master_4)
            string pouName = BuildPouName(s.PouNamePattern, info.MasterNameBase,
                packets.Count > 0 ? packets[0] : null);
            if (pouName.Length > 0) pou.SetAttributeValue("NAME", pouName);
            pou.Element("ISAGraf")?.Remove();
            pou.Element("GrObj")?.Remove();
            var pp = pou.Element("PARAMS");
            if (pp != null && packets.Count > 0)
            {
                int maxRow = (packets.Count - 1) / cols;
                pp.SetAttributeValue("WIDTH",
                    Math.Max(int.TryParse((string?)pp.Attribute("WIDTH"), out int w) ? w : 0,
                        300 + cols * Math.Max(1, s.ColumnStep)));
                pp.SetAttributeValue("HEIGHT",
                    Math.Max(int.TryParse((string?)pp.Attribute("HEIGHT"), out int h) ? h : 0,
                        300 + (maxRow + 1) * Math.Max(1, s.RowStep)));
            }
            var iSAGraf = new XElement("ISAGraf");
            var blocksOutEl = new XElement("Blocks");
            foreach (var b in outBlocks) blocksOutEl.Add(b);
            iSAGraf.Add(blocksOutEl, new XElement("Gotos"));
            var linksOutEl = new XElement("Links");
            foreach (var l in outLinks) linksOutEl.Add(l);
            iSAGraf.Add(linksOutEl);
            pou.Add(iSAGraf);
            var grObj = new XElement("GrObj");
            foreach (var pr in outPrims) grObj.Add(pr);
            pou.Add(grObj);
            outRoot.Add(new XElement("POUS", pou));
            outRoot.Add(new XElement(root.Element("ISAOBJSINFO")!));
            // F19: карточки — только по блокам результата, Info rec = имя блока этого
            // результата (устраняет старые имена вида «MB_TCP_MASTER_4_1»: при клонировании
            // k=1 cardId не меняется и rec шаблона раньше копировался без переименования,
            // а также rec исключённых юнитов-мастеров — как сироты)
            var cardNames = new Dictionary<int, string>();
            foreach (var b in outBlocks)
            {
                var bp = b.Element("Params");
                if (bp != null && int.TryParse((string?)bp.Attribute("cardId"), out int cid) && cid > 0)
                    cardNames[cid] = (string?)b.Attribute("Info") ?? "";
            }
            var cardsEl = new XElement("ISACARDSINFO");
            foreach (var rec in root.Element("ISACARDSINFO")?.Elements("rec") ?? Enumerable.Empty<XElement>())
            {
                if (!int.TryParse((string?)rec.Attribute("ID"), out int rid))
                { cardsEl.Add(new XElement(rec)); continue; }   // rec без ID — как в шаблоне
                if (!cardNames.TryGetValue(rid, out string? name))
                    continue;                                    // rec не используется блоками результата
                var rc = new XElement(rec);
                if (!string.IsNullOrEmpty(name)) rc.SetAttributeValue("Info", name);
                cardsEl.Add(rc);
            }
            foreach (var rec in outCards) cardsEl.Add(rec);
            outRoot.Add(cardsEl);

            var outDoc = new XDocument(new XDeclaration("1.0", "UTF-8", null), outRoot);
            string pousXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                             outDoc.ToString(SaveOptions.DisableFormatting);

            // —— файл привязок (корень <BufScada>): блок 31 «{Master}.DataOut[i]» ——
            // F15: IsaObjId блоков/rec берётся из шаблона (мастер опознан по имени;
            // при импорте SCADA переподставит свой ID); fallback — исторический 15003.
            int masterIsaId = info.MasterIsaId > 0 ? info.MasterIsaId : IsaMaster;
            int bindT11 = nextT11;
            int yBind = 260;
            var bindPairs = new HashSet<(string Block, int Offset)>();
            var bindBlocks = new XElement("Blocks");
            foreach (var p in packets)
                foreach (var b in p.Bindings)
                {
                    // F18: DataOut блока мастера — массив байт: индекс = байт
                    // (регистр → *2, бит → /8); дедупликация — по байтовому индексу
                    int bIdx = ModbusBlockPlannerService.DataOutByteIndex(b.Offset, p.IsBit);
                    if (!bindPairs.Add((b.BlockName, bIdx))) continue;
                    if (!masterMeta.TryGetValue(b.BlockName, out var meta)) continue;
                    bindBlocks.Add(new XElement("Block",
                        new XAttribute("GROBJTYPE", "31"),
                        new XAttribute("Info", $"{b.BlockName}.DataOut[{bIdx}]"),
                        new XAttribute("T11ID", bindT11++),
                        new XElement("Graphics",
                            new XAttribute("X", "700"), new XAttribute("Y", yBind),
                            new XAttribute("WIDTH", "220"), new XAttribute("HEIGHT", "20")),
                        new XElement("Params",
                            new XAttribute("T11Text", $".DataOut[{bIdx}]"),
                            new XAttribute("CI", "1"), new XAttribute("CO", "1"),
                            new XAttribute("VMODE", "0"), new XAttribute("Commented", "false"),
                            new XAttribute("IsaObjId", masterIsaId),
                            new XAttribute("cardId", meta.CardId),
                            new XElement("IV", meta.Iv))));
                    yBind += 40;
                }

            var bindCommon = new XElement(root.Element("Common")!);
            bindCommon.SetAttributeValue("PageMode", "4");
            bindCommon.SetAttributeValue("PageID", (string?)onePou.Attribute("ID") ?? "");
            var bindCards = new XElement("ISACARDSINFO");
            foreach (var kv in masterMeta)
            {
                var tplRec = root.Element("ISACARDSINFO")?.Elements("rec")
                    .FirstOrDefault(r => ((string?)r.Attribute("ID")) == kv.Value.CardId.ToString());
                var rec = tplRec != null ? new XElement(tplRec) : new XElement("rec");
                rec.SetAttributeValue("ID", kv.Value.CardId);
                rec.SetAttributeValue("Info", kv.Key);
                if (rec.Attribute("IsRetain") == null) rec.SetAttributeValue("IsRetain", "1");
                if (rec.Attribute("Name") == null) rec.SetAttributeValue("Name", "");
                if (rec.Attribute("SSize") == null) rec.SetAttributeValue("SSize", "0");
                if (rec.Attribute("KLPath") == null) rec.SetAttributeValue("KLPath", "");
                bindCards.Add(rec);
            }
            var bindObjsRec = root.Element("ISAOBJSINFO")?.Elements("rec")
                .FirstOrDefault(r => ((string?)r.Attribute("ID")) == masterIsaId.ToString());
            var bindObjs = new XElement("ISAOBJSINFO",
                bindObjsRec != null
                    ? new XElement(bindObjsRec)
                    : new XElement("rec",
                        new XAttribute("ID", masterIsaId),
                        new XAttribute("Info", "MB_TCP_MASTER"),
                        new XAttribute("LibName", "TenixRtLib")));
            var bindRoot = new XElement("BufScada", bindCommon,
                new XElement("ISAGraf", bindBlocks, new XElement("Gotos"), new XElement("Links")),
                new XElement("GrObj"), bindObjs, bindCards);
            var bindDoc = new XDocument(new XDeclaration("1.0", "UTF-8", null), bindRoot);
            string bindingsXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                                 bindDoc.ToString(SaveOptions.DisableFormatting);

            var gen = new GeneratedXml { PousXml = pousXml, BindingsXml = bindingsXml };
            foreach (var kv in masterMeta) gen.MasterMeta[kv.Key] = kv.Value;
            foreach (var w in warnings) gen.Warnings.Add(w);
            return gen;
        }

        // ================== F22: генерация по протоколу станции (TCP/RTU) ==================

        /// <summary>F22: собрать BufScadaPOUS + привязки, разделив пакеты по протоколу станции:
        /// TCP-пакеты — по шаблону tcpTemplateXml (текущий шаблон редактора), RTU — по
        /// rtuTemplateXml (SCADA_MASTER_TEMPLATES, имя настройки MasterRtuTemplateName).
        /// RTU-шаблон не задан — RTU-пакеты пропускаются с предупреждением; при наличии обеих
        /// групп результаты склеиваются в один документ (T11/cardId/имена без конфликтов).</summary>
        public GeneratedXml WriteProtocolSplit(IReadOnlyList<MbPacketPlan> packets,
            string tcpTemplateXml, string? rtuTemplateXml, ScadaMbGenSettings s,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>? dataVars = null,
            string dataVarsTemplateKey = "") =>
            WriteProtocolSplit(packets, null, tcpTemplateXml, rtuTemplateXml, s, dataVars, dataVarsTemplateKey);

        /// <summary>F29: генерация с выбором шаблона пакетов по станции: templateByStation —
        /// «станция → тело шаблона» (SCADA_MB_SLAVE_RULES.MasterTemplate → SCADA_MASTER_TEMPLATES).
        /// Станция без правила/пустое имя — fallback по протоколу (tcpTemplateXml/rtuTemplateXml).
        /// Пакеты группируются по ТЕЛУ шаблона (одинаковые тела → один проход Write + склейка
        /// MergeSplitResults), поэтому несколько станций с одним шаблоном не дублируют генерацию.
        /// F38: dataVars/dataVarsTemplateKey — таблица объектов маппинга для подстановки текстов
        /// примитивов [TEXT] (см. Write).</summary>
        public GeneratedXml WriteProtocolSplit(IReadOnlyList<MbPacketPlan> packets,
            IReadOnlyDictionary<string, string>? templateByStation,
            string tcpTemplateXml, string? rtuTemplateXml, ScadaMbGenSettings s,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>? dataVars = null,
            string dataVarsTemplateKey = "")
        {
            if (packets == null) throw new ArgumentNullException(nameof(packets));
            if (s == null) throw new ArgumentNullException(nameof(s));

            // —— группировка по телу шаблона (правило станции → иначе fallback по протоколу) ——
            var groups = new Dictionary<string, List<MbPacketPlan>>(StringComparer.Ordinal);
            var fallbackWarnings = new List<string>();
            foreach (var p in packets)
            {
                string st = (p.Station ?? "").Trim();
                string? body = null;
                if (templateByStation != null && st.Length > 0)
                    templateByStation.TryGetValue(st, out body);
                if (string.IsNullOrWhiteSpace(body))
                    body = p.IsRtu ? rtuTemplateXml : tcpTemplateXml;
                if (string.IsNullOrWhiteSpace(body))
                {
                    fallbackWarnings.Add($"пакеты Modbus RTU ({st}) пропущены: " +
                        "RTU-шаблон мастер-блока не задан (SCADA_MASTER_TEMPLATES → «RTU-шаблон»)");
                    continue;
                }
                if (!groups.TryGetValue(body!, out var list))
                    groups[body!] = list = new List<MbPacketPlan>();
                list.Add(p);
            }

            if (groups.Count == 0)
            {
                // все пакеты пропущены (или их нет) — валидный пустой документ + предупреждения
                var none = Write(new List<MbPacketPlan>(), tcpTemplateXml ?? "", s);
                foreach (var w in fallbackWarnings) none.Warnings.Add(w);
                return none;
            }

            // —— проходы по группам + склейка (вторая и далее — со сдвигом T11/cardId) ——
            GeneratedXml? acc = null;
            var settings = s;
            foreach (var g in groups)
            {
                var gen = Write(g.Value, g.Key, settings, dataVars, dataVarsTemplateKey);
                acc = acc == null ? gen : MergeSplitResults(acc, gen);
                settings = ShiftSettingsForSecondPass(s, acc);
            }
            foreach (var w in fallbackWarnings) acc!.Warnings.Add(w);
            return acc!;
        }

        /// <summary>F22: настройки второго прохода — BaseT11Id/BaseCardId выше максимумов
        /// первого результата (блоки и привязки), чтобы клоны RTU не пересекались с TCP.</summary>
        private static ScadaMbGenSettings ShiftSettingsForSecondPass(ScadaMbGenSettings s, GeneratedXml first)
        {
            var s2 = s.Clone();
            s2.BaseT11Id = Math.Max(s.BaseT11Id, MaxT11(first.PousXml, first.BindingsXml) + 1);
            s2.BaseCardId = Math.Max(s.BaseCardId, MaxIntAttr(first.PousXml, "cardId") + 1);
            return s2;
        }

        /// <summary>F22/F37: максимум T11ID среди Block во всех кусках XML (0 — нет блоков).</summary>
        public static int MaxT11(params string[] xmls)
        {
            int max = 0;
            foreach (var xml in xmls)
            {
                if (string.IsNullOrWhiteSpace(xml)) continue;
                try
                {
                    var root = XDocument.Parse(xml).Root;
                    if (root == null) continue;
                    foreach (var b in root.Descendants("Block"))
                        if (int.TryParse((string?)b.Attribute("T11ID"), out int t) && t > max) max = t;
                }
                catch (System.Xml.XmlException) { /* непарсенный кусок — игнорируем */ }
            }
            return max;
        }

        /// <summary>F22: максимум атрибута (числа) среди Block в куске XML (0 — нет).</summary>
        private static int MaxIntAttr(string xml, string attr)
        {
            int max = 0;
            if (string.IsNullOrWhiteSpace(xml)) return max;
            try
            {
                var root = XDocument.Parse(xml).Root;
                foreach (var b in root?.Descendants("Block") ?? Enumerable.Empty<XElement>())
                    if (int.TryParse((string?)b.Attribute(attr), out int t) && t > max) max = t;
            }
            catch (System.Xml.XmlException) { }
            return max;
        }

        /// <summary>F37: максимум cardId во всех кусках XML — Block/@cardId, Block/Params/@cardId
        /// и rec ISACARDSINFO/@ID (0 — нет); используется для непрерывности счётчика карточек
        /// между файлами split-экспорта.</summary>
        public static int MaxCardId(params string[] xmls)
        {
            int max = 0;
            foreach (var xml in xmls)
            {
                if (string.IsNullOrWhiteSpace(xml)) continue;
                try
                {
                    var root = XDocument.Parse(xml).Root;
                    if (root == null) continue;
                    foreach (var b in root.Descendants("Block"))
                    {
                        if (int.TryParse((string?)b.Attribute("cardId"), out int c1) && c1 > max) max = c1;
                        if (int.TryParse((string?)b.Element("Params")?.Attribute("cardId"), out int c2) && c2 > max)
                            max = c2;
                    }
                    foreach (var r in root.Descendants("rec"))
                        if (int.TryParse((string?)r.Attribute("ID"), out int c3) && c3 > max) max = c3;
                }
                catch (System.Xml.XmlException) { /* непарсенный кусок — игнорируем */ }
            }
            return max;
        }

        /// <summary>F37: настройки следующего файла split-экспорта — BaseT11Id/BaseCardId выше
        /// максимумов уже сгенерированных файлов (T11ID/cardId не пересекаются между файлами
        /// при импорте в один проект SCADA).</summary>
        public static ScadaMbGenSettings ShiftSettingsForNextFile(ScadaMbGenSettings s, params string[] xmls)
        {
            var s2 = s.Clone();
            s2.BaseT11Id = Math.Max(s.BaseT11Id, MaxT11(xmls) + 1);
            s2.BaseCardId = Math.Max(s.BaseCardId, MaxCardId(xmls) + 10);
            return s2;
        }

        /// <summary>F38: подстановка текста примитива [OnePrim/&lt;PARAMS&gt;] — идентификатор
        /// (первое слово значения [TEXT], до пробела/запятой) сопоставляется с текстовым
        /// объектом таблицы маппинга (Var «ident(Text)», fallback — «ident»): строка маппинга
        /// ищется сначала по ключу активного data-шаблона (dataVarsTemplateKey), затем
        /// сквозным поиском по всем ключам (текст один на основной шаблон, а Typno-правило
        /// пакета может указывать любой data-шаблон). Найден — ВСЯ строка [TEXT] заменяется
        /// резолвом (ResolveDataVarValue: колонка/«{}»-шаблон по ColumnValues первой точки
        /// пакета, иначе литерал; [TEXT] — только канал значения, без TagNameFor — текст
        /// пишется как есть). Нет строки — текст без изменений + предупреждение один раз на
        /// (пакет, объект); неразрешённая колонка — фолбэк на литерал + предупреждение
        /// (семантика F36).</summary>
        private static void ApplyPrimTextSubstitution(XElement prim, MbPacketPlan p,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>? dataVars,
            string dataVarsTemplateKey,
            List<string> warnings, HashSet<string> textWarned)
        {
            if (dataVars == null || dataVars.Count == 0) return;
            var pars = prim.Element("PARAMS");
            if (pars == null) return;
            string txt = pars.Value ?? "";
            var m = Regex.Match(txt, @"(?m)^\[TEXT\]=([^\r\n]*)");
            if (!m.Success) return;
            string value = m.Groups[1].Value.Trim();
            if (value.Length == 0) return;
            string ident = (value.Split(new[] { ' ', '\t', ',' },
                StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "").Trim();
            if (ident.Length == 0) return;

            // строка маппинга: активный ключ data-шаблона, затем сквозной поиск по всем
            (string IoColumn, string Literal) row = default;
            bool found = dataVarsTemplateKey.Length > 0 &&
                         dataVars.TryGetValue(dataVarsTemplateKey, out var byKey) &&
                         FindTextRow(byKey, ident, out row);
            if (!found)
                foreach (var kv in dataVars)
                    if (FindTextRow(kv.Value, ident, out row)) { found = true; break; }
            string warnKey = $"{p.No}|{ident}";
            if (!found)
            {
                if (textWarned.Add(warnKey))
                    warnings.Add($"F38: примитив пакета {p.MasterName}: текст «{ident}» — " +
                        "нет строки объекта в маппинге («Объекты data-шаблонов», объект " +
                        $"«{ident}(Text)») — текст не подставлен");
                return;
            }

            var colVals = p.Bindings != null && p.Bindings.Count > 0
                ? p.Bindings[0].ColumnValues : null;
            string text = ScadaDataOutXmlService.ResolveDataVarValue(
                row.IoColumn, row.Literal, colVals, out string? unres, out _);
            if (unres != null && textWarned.Add(warnKey + "|unres"))
                warnings.Add($"F38: примитив пакета {p.MasterName}: текст «{ident}» — " +
                    $"колонка/шаблон «{row.IoColumn}» не разрешился ({unres}) — фолбэк на литерал");
            if (string.IsNullOrWhiteSpace(text))
            {
                if (textWarned.Add(warnKey + "|empty"))
                    warnings.Add($"F38: примитив пакета {p.MasterName}: текст «{ident}» — " +
                        "колонка/шаблон/литерал пусты — текст не подставлен");
                return;
            }

            // вся строка [TEXT]=… → разрешённый текст (пер-пакетно: значения ColumnValues
            // своей точки пакета); параметры [FONTID]/[HINT] и прочие не трогаются
            pars.Value = txt.Substring(0, m.Groups[1].Index) + text +
                         txt.Substring(m.Groups[1].Index + m.Groups[1].Length);
        }

        /// <summary>F38: строка текстового объекта в словаре маппинга шаблона — Var
        /// «ident(Text)» (маркер объекта из грида), fallback «ident» (без суффикса).</summary>
        private static bool FindTextRow(
            IReadOnlyDictionary<string, (string IoColumn, string Literal)> vars,
            string ident, out (string IoColumn, string Literal) row)
        {
            if (vars.TryGetValue(ident + "(Text)", out row)) return true;
            if (vars.TryGetValue(ident, out row)) return true;
            row = default;
            return false;
        }

        /// <summary>F22: склеить результат второго прохода (RTU) в документ первого (TCP):
        /// сдвиг T11 RTU-части вверх, конфликтующие cardId → новые (rec ISACARDSINFO
        /// копируются), NAME POU и Info блоков дедуплицируются, ISAOBJSINFO объединяется
        /// без дублей, MasterMeta/Warnings сливаются.</summary>
        private GeneratedXml MergeSplitResults(GeneratedXml g1, GeneratedXml g2)
        {
            var root1 = XDocument.Parse(g1.PousXml).Root!;
            var root2 = XDocument.Parse(g2.PousXml).Root!;

            // —— занятые T11/cardId первого результата ——
            var usedT11 = new HashSet<int>();
            var usedCard = new HashSet<int>();
            foreach (var b in root1.Descendants("Block"))
            {
                if (int.TryParse((string?)b.Attribute("T11ID"), out int t)) usedT11.Add(t);
                if (int.TryParse((string?)b.Element("Params")?.Attribute("cardId"), out int c) && c > 0)
                    usedCard.Add(c);
            }
            int maxT11 = usedT11.Count > 0 ? usedT11.Max() : 0;
            int nextCard = Math.Max(usedCard.Count > 0 ? usedCard.Max() : 0,
                MaxIntAttr(g2.PousXml, "cardId")) + 10;

            // —— сдвиг T11 части второго документа вверх (общие блоки шаблона — шаблонные T11) ——
            int minT11 = int.MaxValue;
            foreach (var b in root2.Descendants("Block"))
                if (int.TryParse((string?)b.Attribute("T11ID"), out int t)) minT11 = Math.Min(minT11, t);
            int offset = minT11 != int.MaxValue && minT11 <= maxT11 ? maxT11 + 1 - minT11 : 0;
            if (offset > 0)
            {
                foreach (var b in root2.Descendants("Block"))
                    if (int.TryParse((string?)b.Attribute("T11ID"), out int t))
                        b.SetAttributeValue("T11ID", t + offset);
                foreach (var l in root2.Descendants("Link"))
                {
                    foreach (var (el, attr) in new (string, string)[] { ("FirstPoint", "FP"), ("LastPoint", "LP") })
                    {
                        var a = l.Element(el)?.Attribute(attr);
                        if (a == null) continue;
                        var parts = a.Value.Split('|');
                        if (parts.Length > 0 && int.TryParse(parts[0], out int t))
                        {
                            parts[0] = (t + offset).ToString();
                            a.Value = string.Join("|", parts);
                        }
                    }
                }
                foreach (var pr in root2.Descendants("OnePrim"))
                    if (int.TryParse((string?)pr.Attribute("SourceT11ID"), out int t))
                        pr.SetAttributeValue("SourceT11ID", t + offset);
            }

            // —— дедуп Info блоков (общие блоки шаблонов могут совпадать по имени) ——
            var usedInfo = new HashSet<string>(
                root1.Descendants("Block").Select(b => (string?)b.Attribute("Info") ?? ""),
                StringComparer.OrdinalIgnoreCase);
            foreach (var b in root2.Descendants("Block"))
            {
                string info = (string?)b.Attribute("Info") ?? "";
                if (info.Length == 0 || usedInfo.Add(info)) continue;
                string uniq = info;
                int k = 2;
                while (!usedInfo.Add(uniq)) uniq = $"{info}_{k++}";
                b.SetAttributeValue("Info", uniq);
            }

            // —— cardId: конфликт → новый (rec из шаблона второго копируется с новым ID) ——
            var cards1 = root1.Element("ISACARDSINFO");
            var cards2 = root2.Element("ISACARDSINFO");
            foreach (var b in root2.Descendants("Block"))
            {
                var bp = b.Element("Params");
                if (bp == null || !int.TryParse((string?)bp.Attribute("cardId"), out int cid) || cid <= 0)
                    continue;
                int id = cid;
                if (usedCard.Contains(cid))
                {
                    id = nextCard;
                    nextCard += 10;
                    bp.SetAttributeValue("cardId", id);
                }
                usedCard.Add(id);
                if (cards1 == null || cards2 == null) continue;
                if (cards1.Elements("rec").Any(r => ((string?)r.Attribute("ID")) == id.ToString())) continue;
                var rec = cards2.Elements("rec")
                    .FirstOrDefault(r => ((string?)r.Attribute("ID")) == cid.ToString());
                if (rec == null) continue;
                var rc = new XElement(rec);
                rc.SetAttributeValue("ID", id);
                cards1.Add(rc);
            }

            MergeSplitTail(root1, root2, g1, g2, out string pousXml, out string bindingsXml);

            var merged = new GeneratedXml { PousXml = pousXml, BindingsXml = bindingsXml };
            foreach (var kv in g1.MasterMeta) merged.MasterMeta[kv.Key] = kv.Value;
            foreach (var kv in g2.MasterMeta) merged.MasterMeta[kv.Key] = kv.Value;
            foreach (var w in g1.Warnings) merged.Warnings.Add(w);
            foreach (var w in g2.Warnings) merged.Warnings.Add(w);
            return merged;
        }

        /// <summary>F22: вторая половина склейки — NAME POU, перенос блоков/связей/примитивов,
        /// ISAOBJSINFO без дублей, сборка итоговых PousXml/BindingsXml.</summary>
        private static void MergeSplitTail(XElement root1, XElement root2,
            GeneratedXml g1, GeneratedXml g2, out string pousXml, out string bindingsXml)
        {
            // —— NAME POU второго: дубль имени первого → суффикс _2/_3 ——
            var pou1 = root1.Element("POUS")?.Element("OnePOU");
            var pou2 = root2.Element("POUS")?.Element("OnePOU");
            if (pou1 != null && pou2 != null)
            {
                string n1 = (string?)pou1.Attribute("NAME") ?? "";
                string n2 = (string?)pou2.Attribute("NAME") ?? "";
                if (n2.Length > 0 && string.Equals(n1, n2, StringComparison.OrdinalIgnoreCase))
                {
                    var usedPou = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { n1 };
                    string uniq = n2;
                    int k = 2;
                    while (!usedPou.Add(uniq)) uniq = $"{n2}_{k++}";
                    pou2.SetAttributeValue("NAME", uniq);
                }

                // —— перенос блоков/связей/примитивов второго в первый ——
                var ig1 = pou1.Element("ISAGraf");
                var ig2 = pou2.Element("ISAGraf");
                if (ig1 != null && ig2 != null)
                {
                    ig1.Element("Blocks")?.Add(ig2.Element("Blocks")?.Elements()
                        ?? Enumerable.Empty<XElement>());
                    ig1.Element("Gotos")?.Add(ig2.Element("Gotos")?.Elements()
                        ?? Enumerable.Empty<XElement>());
                    ig1.Element("Links")?.Add(ig2.Element("Links")?.Elements()
                        ?? Enumerable.Empty<XElement>());
                }
                pou1.Element("GrObj")?.Add(pou2.Element("GrObj")?.Elements()
                    ?? Enumerable.Empty<XElement>());
            }

            // —— ISAOBJSINFO: добавить rec, которых нет в первом (сравнение по тексту rec) ——
            var objs1 = root1.Element("ISAOBJSINFO");
            var objs2 = root2.Element("ISAOBJSINFO");
            if (objs1 != null && objs2 != null)
            {
                var have = objs1.Elements("rec").Select(r => r.ToString())
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var rec in objs2.Elements("rec"))
                    if (have.Add(rec.ToString()))
                        objs1.Add(new XElement(rec));
            }

            pousXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                new XDocument(new XDeclaration("1.0", "UTF-8", null), root1)
                    .ToString(SaveOptions.DisableFormatting);

            // —— файл привязок: блоки/карточки второго — в первый ——
            bindingsXml = g1.BindingsXml;
            var bRoot1 = XDocument.Parse(g1.BindingsXml).Root;
            var bRoot2 = XDocument.Parse(g2.BindingsXml).Root;
            if (bRoot1 == null || bRoot2 == null) return;
            bRoot1.Element("ISAGraf")?.Element("Blocks")?.Add(
                bRoot2.Element("ISAGraf")?.Element("Blocks")?.Elements()
                    ?? Enumerable.Empty<XElement>());
            var bc1 = bRoot1.Element("ISACARDSINFO");
            var bc2 = bRoot2.Element("ISACARDSINFO");
            if (bc1 != null && bc2 != null)
                foreach (var rec in bc2.Elements("rec"))
                {
                    string id = (string?)rec.Attribute("ID") ?? "";
                    if (!bc1.Elements("rec").Any(r => ((string?)r.Attribute("ID")) == id))
                        bc1.Add(new XElement(rec));
                }
            bindingsXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                new XDocument(new XDeclaration("1.0", "UTF-8", null), bRoot1)
                    .ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>F23: имена входов мастера из связей шаблона (LastPoint LP «…|True|Имя|rect»)
        /// в порядке появления — список «переменных» для маппинга SCADA_TEMPLATE_VARS.</summary>
        public List<string> ParseInputVariables(string? templateXml)
        {
            var list = new List<string>();
            try
            {
                var root = XDocument.Parse(templateXml ?? "").Root;
                var links = root?.Element("POUS")?.Element("OnePOU")?
                    .Element("ISAGraf")?.Element("Links");
                foreach (var l in links?.Elements("Link") ?? Enumerable.Empty<XElement>())
                {
                    string port = PortOf(l.Element("LastPoint")?.Attribute("LP")?.Value);
                    if (port.Length > 0 && !list.Contains(port, StringComparer.OrdinalIgnoreCase))
                        list.Add(port);
                }
            }
            catch (System.Xml.XmlException) { /* невалидный шаблон — пустой список */ }
            return list;
        }

        // ================== Утилиты генерации ==================

        /// <summary>F13: имя входа из LastPoint LP («…|True|SlaveId|rect»).</summary>
        private static string PortOf(string? lp)
        {
            var parts = (lp ?? "").Split('|');
            return parts.Length > 2 ? parts[2] : "";
        }

        /// <summary>F13: подставить значение входа в клон источника: константе (34) — Info+T11Text,
        /// тегу (31) — содержимое IV.</summary>
        private static void ApplyInputValue(XElement blockClone, string value)
        {
            if (blockClone.Attribute("GROBJTYPE")?.Value == "34")
            {
                blockClone.SetAttributeValue("Info", value);
                blockClone.Element("Params")?.SetAttributeValue("T11Text", value);
            }
            else
            {
                var iv = blockClone.Element("Params")?.Element("IV");
                if (iv != null) iv.Value = value;
                else blockClone.Element("Params")?.Add(new XElement("IV", value));
            }
        }

        /// <summary>F13: сдвинуть точки соединения связи PL на (dx,dy).</summary>
        private static void ShiftPointList(XElement linkClone, int dx, int dy)
        {
            var a = linkClone.Element("PointList")?.Attribute("PL");
            if (a == null) return;
            a.Value = Regex.Replace(a.Value, @"\((\d+),(\d+)\)",
                m => $"({int.Parse(m.Groups[1].Value) + dx},{int.Parse(m.Groups[2].Value) + dy})");
        }

        /// <summary>F13: перенумеровать T11ID в FirstPoint/LastPoint связи по карте клонов.</summary>
        private static void RemapEndPoint(XElement linkClone, string elName, string attrName,
            Dictionary<int, int> map)
        {
            var a = linkClone.Element(elName)?.Attribute(attrName);
            if (a == null) return;
            var parts = a.Value.Split('|');
            if (parts.Length > 0 && int.TryParse(parts[0], out int t) && map.TryGetValue(t, out int nt))
            {
                parts[0] = nt.ToString();
                a.Value = string.Join("|", parts);
            }
        }

        /// <summary>F13: следующий уникальный cardId клона (шаг 10).</summary>
        private static int NextCardId(ref int next)
        {
            int id = next;
            next += 10;
            return id;
        }

        /// <summary>F13: имя инстанса клона (TON_8 + k=2 → TON_9; нет цифр — «_k»).</summary>
        private static string ShiftInstanceName(string info, int k)
        {
            if (k == 1) return info;
            var m = Regex.Match(info, @"^(.*?)(\d+)$");
            if (m.Success && long.TryParse(m.Groups[2].Value, out long d))
                return m.Groups[1].Value + (d + k - 1);
            return info + "_" + k;
        }

        /// <summary>F13: найти файл шаблона по умолчанию: каталог exe → вверх по каталогам (Work\).
        /// null — файл не найден (F18: встроенных копий примеров в приложении больше нет —
        /// шаблон задаётся явно: файл или вставленный код).</summary>
        public static string? ResolveDefaultTemplatePath()
        {
            string direct = Path.Combine(AppContext.BaseDirectory, DefaultTemplateName);
            if (File.Exists(direct)) return direct;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                string p = Path.Combine(dir.FullName, "Work", DefaultTemplateName);
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }
}

