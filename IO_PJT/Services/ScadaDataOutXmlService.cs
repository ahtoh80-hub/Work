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
    /// F13.1/F18: генератор XML привязок «out → tagname» из отдельного шаблона (не путать с шаблоном
    /// блоков MB_TCP_MASTER). Шаблон — BufScadaPOUS, где каждый OnePOU = эталонная цепочка
    /// одного типа сигнала; тип эталона — в Disc OnePOU (BOOL/INT/DINT/REAL, регистронезависимо;
    /// пустой Disc — NAME). F18: в наборах «шаблон на один тип сигнала» (Disc пуст, напр.
    /// Data_Out_шаблоны_32-Bit Floating_V2) единственный эталон — fallback для любых типов.
    /// Внутри эталона: блок(и)-источник(и) (GROBJTYPE 31, T11Text «.DataOut[k]») и блок-приёмник
    /// (31, Info без «.DataOut[», конечный узел — тег, марка или член «.Xin»). На каждую пару
    /// (пакет, привязка) клонирует цепочку эталона: F18 — источники подставляются БАЙТОВЫМИ
    /// индексами «{Block}.DataOut[{byteBase+j}]» (DataOut мастера — массив байт MB_USINT_0_249;
    /// byteBase = Offset*2 для FC03/04, Offset/8 для FC01/02; число байт задаёт сам эталон —
    /// float V2 = 4 источника), cardId/IV от мастера пакета; приёмник — тег из IO_LIST
    /// (тире/пробелы → «_», ведущий «_» = марка) с новой карточкой ISACARDSINFO.</summary>
    public sealed class ScadaDataOutXmlService
    {
        /// <summary>Имя файла шаблона по умолчанию (поиск: каталог exe → вверх в Work\).</summary>
        public const string DefaultTemplateName = "Data_Out_пример.xml";

        /// <summary>F13.1: результат разбора шаблона привязок (валидность + эталоны + статус).</summary>
        public sealed class DataOutTemplateInfo
        {
            /// <summary>true — шаблон пригоден (хотя бы один эталон).</summary>
            public bool Valid;

            /// <summary>Текст ошибки валидации (пуст, если Valid).</summary>
            public string Error = "";

            /// <summary>Эталоны по типам (BOOL/INT/DINT/REAL, ключ — Disc/NAME OnePOU).</summary>
            public Dictionary<string, Exemplar> Exemplars { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Число эталонов в шаблоне.</summary>
            public int ExemplarCount => Exemplars.Count;

            /// <summary>Итоговый текст для статуса окна.</summary>
            public string Summary =>
                Valid
                    ? $"эталоны: {Exemplars.Count} ({string.Join(", ", Exemplars.Keys.OrderBy(k => k))})"
                    : Error;
        }

        /// <summary>F13.1: эталонная цепочка одного типа сигнала.</summary>
        public sealed class Exemplar
        {
            /// <summary>Ключ типа (Disc/NAME OnePOU).</summary>
            public string TypeKey = "";

            /// <summary>Блоки цепочки (XML как в шаблоне).</summary>
            public List<XElement> Blocks { get; } = new();

            /// <summary>Связи цепочки.</summary>
            public List<XElement> Links { get; } = new();

            /// <summary>Блок-источник (Info с «.DataOut[k]»).</summary>
            public XElement SourceTpl = null!;

            /// <summary>Блок-приёмник (тег/член тега).</summary>
            public XElement TargetTpl = null!;

            /// <summary>true — XML-элемент является блоком-источником «.DataOut[…]».</summary>
            public static bool IsSourceBlock(XElement b) =>
                b.Attribute("GROBJTYPE")?.Value == "31" &&
                (b.Attribute("Info")?.Value ?? "").Contains(".DataOut[", StringComparison.OrdinalIgnoreCase);

            /// <summary>F30: true — блок-приёмник записи «.DataIn[…]» (datain-эталон:
            /// тег → Word_to_MB → Block.DataIn[j]).</summary>
            public static bool IsDataInBlock(XElement b) =>
                b.Attribute("GROBJTYPE")?.Value == "31" &&
                (b.Attribute("Info")?.Value ?? "").Contains(".DataIn[", StringComparison.OrdinalIgnoreCase);

            /// <summary>F30: true — write-эталон (цепочка записи: есть блоки «.DataIn[…]»;
            /// направление инвертировано — тег-источник подставляется вместо DataOut-выхода).</summary>
            public bool IsWrite => Blocks.Any(IsDataInBlock);

            /// <summary>Число блоков-источников цепочки = ширина сигнала в байтах
            /// (F18: float V2 = 4 источника; BOOL — 1 байт).
            /// F30: для write-эталона — число блоков «.DataIn[…]» (= ширина в байтах).</summary>
            public int SourceCount => Blocks.Count(IsWrite ? (Func<XElement, bool>)IsDataInBlock : IsSourceBlock);
        }

        // ================== Разбор/валидация шаблона ==================

        /// <summary>F13.1: разобрать шаблон привязок: корень BufScadaPOUS, ≥1 OnePOU-эталон
        /// (Disc/NAME = тип), в каждом эталоне — блок-источник и блок-приёмник.</summary>
        public DataOutTemplateInfo Parse(string? templateXml)
        {
            var r = new DataOutTemplateInfo();
            if (string.IsNullOrWhiteSpace(templateXml)) { r.Error = "шаблон пуст"; return r; }
            XDocument doc;
            try { doc = XDocument.Parse(templateXml!); }
            catch (Exception ex) { r.Error = $"XML не разбирается: {ex.Message}"; return r; }
            if (doc.Root == null || doc.Root.Name != "BufScadaPOUS")
            { r.Error = "корень — не BufScadaPOUS"; return r; }

            var pous = doc.Root.Element("POUS")?.Elements("OnePOU").ToList() ?? new List<XElement>();
            if (pous.Count == 0) { r.Error = "в шаблоне нет POUS/OnePOU-эталонов"; return r; }

            foreach (var pou in pous)
            {
                // F18: Disc может присутствовать пустым (V2: Disc="") — тогда ключ = NAME
                string key = ((string?)pou.Attribute("Disc") ?? "").Trim();
                if (key.Length == 0) key = ((string?)pou.Attribute("NAME") ?? "").Trim();
                if (key.Length == 0) { r.Error = $"у эталона ID={pou.Attribute("ID")} нет Disc/NAME типа"; return r; }
                key = key.ToUpperInvariant();
                if (r.Exemplars.ContainsKey(key))
                { r.Error = $"два эталона одного типа «{key}» (Disc/NAME OnePOU)"; return r; }

                var graf = pou.Element("ISAGraf");
                var blocks = graf?.Element("Blocks")?.Elements("Block").ToList() ?? new List<XElement>();
                if (blocks.Count == 0) { r.Error = $"эталон «{key}»: нет блоков"; return r; }

                var sources = blocks.Where(Exemplar.IsSourceBlock).ToList();
                var dataIns = blocks.Where(Exemplar.IsDataInBlock).ToList();
                // F30: два вида эталонов — чтение (блок-источник «.DataOut[…]» → тег) и
                // запись (тег → блок-приёмник «.DataIn[…]»; в datain-шаблонах нет .DataOut[)
                bool writeEx = sources.Count == 0 && dataIns.Count > 0;
                if (sources.Count == 0 && !writeEx)
                { r.Error = $"эталон «{key}»: нет блока-источника «.DataOut[…]»"; return r; }

                // связи эталона
                var links = graf?.Element("Links")?.Elements("Link").ToList() ?? new List<XElement>();
                var outgoing = new HashSet<int>(links.Select(l => T11Of(l.Element("FirstPoint")?.Attribute("FP")?.Value)));
                var incoming = new HashSet<int>(links.Select(l => T11Of(l.Element("LastPoint")?.Attribute("LP")?.Value)));

                XElement root;
                XElement target;
                if (writeEx)
                {
                    // F30: корень цепочки — тег-блок (31, без .DataIn[/.DataOut[, без входящих связей)
                    var roots = blocks.Where(b =>
                        b.Attribute("GROBJTYPE")?.Value == "31" &&
                        !Exemplar.IsDataInBlock(b) && !Exemplar.IsSourceBlock(b) &&
                        !incoming.Contains(T11Of(b.Attribute("T11ID")?.Value))).ToList();
                    if (roots.Count == 0)
                    { r.Error = $"эталон записи «{key}»: нет тега-источника (корня цепочки)"; return r; }
                    root = roots[0];
                    target = dataIns[0]; // приёмник «.DataIn[» — подставится байтовым индексом
                }
                else
                {
                    // приёмник — конечный узел цепочки (31, Info без .DataOut[, без исходящих связей)
                    var targets = blocks.Where(b =>
                        b.Attribute("GROBJTYPE")?.Value == "31" &&
                        !Exemplar.IsSourceBlock(b) &&
                        !outgoing.Contains(T11Of(b.Attribute("T11ID")?.Value))).ToList();
                    if (targets.Count == 0)
                    { r.Error = $"эталон «{key}»: нет блока-приёмника (тег, конечный узел)"; return r; }
                    root = sources[0];
                    target = targets[0];
                }

                // цепочка эталона — компонента связности корня (все блоки/связи компоненты)
                var comp = ComponentOf(blocks, links, root);
                var ex = new Exemplar { TypeKey = key, SourceTpl = root, TargetTpl = target };
                ex.Blocks.AddRange(comp.Blocks);
                ex.Links.AddRange(comp.Links);
                if (!ex.Blocks.Contains(ex.TargetTpl))
                { r.Error = $"эталон «{key}»: приёмник не в цепочке источника"; return r; }
                r.Exemplars[key] = ex;
            }
            r.Valid = r.Exemplars.Count > 0;
            if (!r.Valid) r.Error = "в шаблоне нет ни одного эталона";
            return r;
        }

        /// <summary>F33 (этап 6): авто-размер переменной data-шаблона по числу источников
        /// первого эталона: 4 → 32bit (4 байта), 2 → 16bit, 8 → 64bit, 1 → bit (bool-like
        /// ключ Disc/NAME: BOOL/COIL/DI/DO/DISCRETE/BIT) либо 8bit; иначе "" — не определить.
        /// Sources — число источников эталона (слотов DataOut/DataIn).</summary>
        public (int Sources, string Size) DetectTemplateSize(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return (0, "");
            DataOutTemplateInfo inf;
            try { inf = Parse(body); }
            catch (Exception) { return (0, ""); }
            if (!inf.Valid || inf.Exemplars.Count == 0) return (0, "");
            var ex = inf.Exemplars.Values.First();
            int n = ex.SourceCount;
            string size = n switch
            {
                4 => "32bit", 2 => "16bit", 8 => "64bit",
                1 => IsBoolLikeKey(ex.TypeKey) ? "bit" : "8bit",
                _ => ""
            };
            return (n, size);
        }

        /// <summary>F33: bool-like ключ эталона (дискретные шаблоны: каждый DataOut — бит).</summary>
        private static bool IsBoolLikeKey(string key) =>
            key.IndexOf("BOOL", StringComparison.OrdinalIgnoreCase) >= 0 ||
            key.IndexOf("COIL", StringComparison.OrdinalIgnoreCase) >= 0 ||
            key.IndexOf("DISCRETE", StringComparison.OrdinalIgnoreCase) >= 0 ||
            key.IndexOf("BIT", StringComparison.OrdinalIgnoreCase) >= 0 ||
            key.StartsWith("DI", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("DO", StringComparison.OrdinalIgnoreCase);

        // ===== F33 (этап 7): объекты data-шаблонов (переменные) =====

        /// <summary>F33 (этап 7): объект data-шаблона — блок-переменная эталона: константа
        /// (GROBJTYPE 34) или тег (31) вне цепочки DataOut/DataIn и без имени приёмника.</summary>
        public sealed class TemplateObject
        {
            /// <summary>Ключ эталона (Disc/NAME OnePOU).</summary>
            public string ExemplarKey { get; set; } = "";

            /// <summary>Имя переменной (Info блока).</summary>
            public string Name { get; set; } = "";

            /// <summary>GROBJTYPE блока: 34 — константа, 31 — тег.</summary>
            public int GrobjType { get; set; }

            /// <summary>Значение по умолчанию (для константы 34 — Info/T11Text шаблона).</summary>
            public string DefaultValue { get; set; } = "";
        }

        /// <summary>F33 (этап 7): перечислить объекты-переменные data-шаблона: блоки
        /// GROBJTYPE 31/34, НЕ источники (.DataOut[/.DataIn[), НЕ тег-корень цепочки;
        /// функции-блоки (36 и др.) не включаются. F34: блок-приёмник (TargetTpl, напр.
        /// DCSTAG) тоже включается — его Info можно переопределить маппингом объектов;
        /// при пустом теге привязки сохраняется Info шаблона (не пустая строка).</summary>
        public List<TemplateObject> ParseTemplateObjects(string? body)
        {
            var list = new List<TemplateObject>();
            if (string.IsNullOrWhiteSpace(body)) return list;
            DataOutTemplateInfo inf;
            try { inf = Parse(body); }
            catch (Exception) { return list; }
            foreach (var kv in inf.Exemplars)
            {
                var ex = kv.Value;
                foreach (var blk in ex.Blocks)
                {
                    string gt = blk.Attribute("GROBJTYPE")?.Value ?? "";
                    if (gt != "31" && gt != "34") continue;
                    if (Exemplar.IsSourceBlock(blk) || Exemplar.IsDataInBlock(blk)) continue;
                    if (ReferenceEquals(blk, ex.SourceTpl)) continue;
                    string name = (blk.Attribute("Info")?.Value ?? "").Trim();
                    if (name.Length == 0) continue;
                    list.Add(new TemplateObject
                    {
                        ExemplarKey = kv.Key,
                        Name = name,
                        GrobjType = int.Parse(gt),
                        DefaultValue = gt == "34" ? name : ""
                    });
                }
            }
            return list;
        }

        /// <summary>F38: перечислить текстовые объекты основного шаблона — примитивы
        /// &lt;OnePrim&gt; с параметром [TEXT]=, идентификатор — ПЕРВОЕ СЛОВО значения
        /// (до пробела/запятой): «[TEXT]=LOOPComment, AdressComment…» → «LOOPComment(Text)».
        /// Всё значение [TEXT] при экспорте заменяется результатом резолва строки маппинга
        /// (см. ScadaTemplateXmlService.ApplyPrimTextSubstitution); дубли имён между
        /// примитивами отбрасываются (IgnoreCase), порядок — появления в документе.</summary>
        public List<string> ParseTextObjects(string? templateXml)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(templateXml)) return list;
            XDocument doc;
            try { doc = XDocument.Parse(templateXml!); }
            catch (Exception) { return list; }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prim in doc.Root?.Element("POUS")?.Element("OnePOU")?
                         .Element("GrObj")?.Elements("OnePrim") ?? Enumerable.Empty<XElement>())
            {
                string txt = prim.Element("PARAMS")?.Value ?? "";
                var m = Regex.Match(txt, @"(?m)^\[TEXT\]=([^\r\n]*)");
                if (!m.Success) continue;
                string ident = (m.Groups[1].Value.Trim()
                    .Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? "").Trim();
                if (ident.Length == 0) continue;
                if (seen.Add(ident + "(Text)"))
                    list.Add(ident + "(Text)");
            }
            return list;
        }

        // ===== Генерация =====

        /// <summary>F13.1: собрать BufScadaPOUS привязок: на каждый пакет — OnePOU (NAME = имя
        /// блока мастера), внутри — цепочка эталона на каждую привязку с подстановкой блока/
        /// индекса/тега. masterMeta — (cardId, IV) блоков мастера из основной генерации.
        /// F14/F16: typnoMap — «Typno → (шаблон, эталон)» (SCADA_TYPNO_MAP): эталон выбирается
        /// по правилу Typno; указанное имя шаблона (Template) ищется в namedTemplates —
        /// цепочка клонируется из него, не найден/невалиден — пропуск (F33).
        /// F33 (этап 5): typnoMap != null → маппинг Typno обязателен: точка без правила,
        /// правило с ненайденным шаблоном или отсутствие эталона типа — привязка
        /// ПРОПУСКАЕТСЯ с предупреждением (исторические fallback «по M_DataType»/«INT»/
        /// «основной шаблон» отключены); typnoMap == null — историческое поведение F13.
        /// F30: dataInTemplates — именованные DataIn-шаблоны (SCADA_DATAIN_TEMPLATES) для
        /// привязок записи (пакеты FC05/06/15/16): эталон ищется по правилу Typno среди
        /// datain, затем по typeKey среди datain (см. ChooseWriteTemplate); без write-эталона —
        /// предупреждение и пропуск цепочки записи.</summary>
        public string Write(IReadOnlyList<MbPacketPlan> packets, string templateXml,
            IReadOnlyDictionary<string, (int CardId, string Iv)> masterMeta, ScadaMbGenSettings s,
            List<string>? warnings = null,
            IReadOnlyDictionary<string, (string Template, string Exemplar, string Size)>? typnoMap = null,
            IReadOnlyDictionary<string, string>? namedTemplates = null,
            IReadOnlyDictionary<string, string>? dataInTemplates = null,
            // F33 (этап 7): маппинг объектов data-шаблонов «имя шаблона → объект → (колонка, литерал)»
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>? dataVars = null,
            string dataVarsTemplateKey = "")
        {
            if (packets == null) throw new ArgumentNullException(nameof(packets));
            if (s == null) throw new ArgumentNullException(nameof(s));
            warnings ??= new List<string>();
            var info = Parse(templateXml);
            if (!info.Valid) throw new InvalidOperationException("Шаблон привязок: " + info.Error);

            // F16: кэш разбора именованных шаблонов (имя → эталоны); невалидный XML
            // пропускается — при обращении выдаётся предупреждение «не найден/невалиден»
            var namedInfo = new Dictionary<string, DataOutTemplateInfo>(StringComparer.OrdinalIgnoreCase);
            if (namedTemplates != null)
                foreach (var kv in namedTemplates)
                    if (!string.IsNullOrWhiteSpace(kv.Value))
                    {
                        try { namedInfo[kv.Key] = Parse(kv.Value); }
                        catch (Exception) { /* невалиден — отмечется предупреждением ниже */ }
                    }

            // F30: кэш DataIn-шаблонов (write-эталоны: тег → Block.DataIn[…])
            var writeInfo = new Dictionary<string, DataOutTemplateInfo>(StringComparer.OrdinalIgnoreCase);
            if (dataInTemplates != null)
                foreach (var kv in dataInTemplates)
                    if (!string.IsNullOrWhiteSpace(kv.Value))
                    {
                        try { writeInfo[kv.Key] = Parse(kv.Value); }
                        catch (Exception) { /* невалиден — предупреждение при обращении */ }
                    }

            var tplRoot = XDocument.Parse(templateXml!).Root!;

            // счётчики уникальности клонов (продолжение после шаблонных ID)
            int maxTplT11 = info.Exemplars.Values.SelectMany(e => e.Blocks)
                .Select(b => T11Of(b.Attribute("T11ID")?.Value)).DefaultIfEmpty(0).Max();
            int nextT11 = Math.Max(s.BaseT11Id, maxTplT11 + 1);
            int maxTplCard = info.Exemplars.Values.SelectMany(e => e.Blocks)
                .Select(CardOf).DefaultIfEmpty(0).Max();
            int nextCard = Math.Max(s.BaseCardId, maxTplCard + 10);

            var outPous = new XElement("POUS");
            var usedPouNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // F17: уникальность NAME POU
            // F35: предупреждение «маппинг шаблона не найден» — один раз на ключ
            var noMappingWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // F35: предупреждение «приёмник без строки маппинга» — один раз на (пакет, объект)
            var noMapWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outCards = new List<XElement>();   // карточки тегов + мастеров
            var seenCards = new HashSet<int>();
            int pouId = Math.Max(20000, T11Of(tplRoot.Element("POUS")?.Element("OnePOU")?.Attribute("ID")?.Value));

            // F38: пакеты data группируются по N на один POU (файл привязок): пакет АТОМАРЕН
            // (все его цепочки целиком на одном POU, между POU не рвётся); чанки по N пакетов
            // подряд, имя POU — по первому пакету чанка; N=1 — историческое «один POU на
            // пакет» (F13). Зазор +100 между пакетами чанка — цепочки не налагаются
            int packetsPerPou = Math.Max(1, s.DataPacketsPerPage);
            for (int pi = 0; pi < packets.Count; pi += packetsPerPou)
            {
                var chunk = packets.Skip(pi).Take(packetsPerPou).ToList();
                var pouBlocks = new XElement("Blocks");
                var pouLinks = new XElement("Links");
                int yBase = 200;
                foreach (var p in chunk)
                {
                // F37.4: один data-объект на адрес — несколько привязок одного пакета с одним
                // DataOutExpr (DO-биты одного регистра, 1115-MHS-…A/B/C/F) клонируют цепочку
                // ОДИН раз по первой привязке (данные — из первого попавшегося по адресу тега);
                // M_BusBlock/M_BusDataOut в IO_LIST пишутся для всех тегов адреса (WriteBindings)
                var seenExpr = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var b in p.Bindings)
                {
                    if (b.DataOutExpr != null && b.DataOutExpr.Length > 0 &&
                        !seenExpr.Add(b.DataOutExpr)) continue;

                    // F14/F16: приоритет — сопоставление «Typno → (шаблон, эталон)» (SCADA_TYPNO_MAP)
                    string? mappedEx = null;
                    string mappedTplName = "";
                    (string Template, string Exemplar, string Size) rule = default;
                    bool haveRule = typnoMap != null &&
                                    !string.IsNullOrWhiteSpace(b.Typno) &&
                                    typnoMap.TryGetValue(b.Typno.Trim(), out rule);

                    // F33 (этап 5): при активных правилах (typnoMap != null) маппинг Typno
                    // ОБЯЗАТЕЛЕН — точка без правила не генерится (исторический выбор эталона
                    // по M_DataType отключён); typnoMap == null — историческое F13/F14
                    if (typnoMap != null && !haveRule)
                    {
                        warnings.Add($"F33: {b.Tag}: Typno «{b.Typno}» не сопоставлен в " +
                                     "SCADA_TYPNO_MAP — привязка пропущена (добавьте правило " +
                                     "«Typno → шаблон привязок»)");
                        continue;
                    }

                    if (haveRule)
                    {
                        mappedEx = rule.Exemplar;
                        mappedTplName = (rule.Template ?? "").Trim();
                    }

                    // F33: тип эталона — из правила Typno; правило без эталона → тип точки
                    // (подсказка для единственного эталона шаблона)
                    string typeKey = !string.IsNullOrWhiteSpace(mappedEx)
                        ? mappedEx!.Trim()
                        : MapTypeKey(b.DataType);

                    // F16/F30: активный шаблон выбирается по типу/шаблону правила
                    var active = info;
                    if (b.IsWrite)
                    {
                        var wact = ChooseWriteTemplate(writeInfo, namedInfo, mappedTplName, typeKey);
                        if (wact == null)
                        {
                            warnings.Add($"F30: {b.Tag}: datain-эталон «{typeKey}» не найден среди " +
                                         "DataIn-шаблонов (SCADA_DATAIN_TEMPLATES) — привязка записи пропущена");
                            continue;
                        }
                        active = wact;
                    }
                    else if (mappedTplName.Length > 0)
                    {
                        if (namedInfo.TryGetValue(mappedTplName, out var ni) && ni.Valid)
                            active = ni;
                        else
                        {
                            // F33: правило указало шаблон — без него привязка НЕ генерится
                            // (раньше — fallback на основной шаблон)
                            warnings.Add($"F33: {b.Tag}: шаблон привязок «{mappedTplName}» из " +
                                         "правила Typno не найден среди сохранённых или невалиден " +
                                         "— привязка пропущена");
                            continue;
                        }
                    }

                    Exemplar ex;
                    if (b.IsWrite)
                    {
                        // F30: datain-шаблон обычно один — ключ эталона = имя POU (не тип);
                        // единственный write-эталон берётся без предупреждений
                        if (active.Exemplars.TryGetValue(typeKey, out var wex) && wex.IsWrite)
                            ex = wex;
                        else
                        {
                            var wx = active.Exemplars.Values.Where(e => e.IsWrite).ToList();
                            if (wx.Count == 1) { ex = wx[0]; }
                            else
                            {
                                warnings.Add($"F30: {b.Tag}: в DataIn-шаблоне нет write-эталона «{typeKey}» " +
                                             "— привязка записи пропущена");
                                continue;
                            }
                        }
                    }
                    else if (active.Exemplars.TryGetValue(typeKey, out var rex))
                    {
                        ex = rex;
                    }
                    else if (active.Exemplars.Count == 1)
                    {
                        // F18: в шаблоне ровно один эталон (набор «шаблон на один тип
                        // сигнала» — напр. Data_Out_шаблоны_32-Bit Floating_V2) —
                        // берём его для всех типов точек
                        ex = active.Exemplars.Values.First();
                        warnings.Add($"F18: {b.Tag}: эталон «{typeKey}» не найден — взят " +
                                     $"единственный эталон шаблона «{ex.TypeKey}» " +
                                     $"(источников/байт: {ex.SourceCount})");
                    }
                    else
                    {
                        // F33: без эталона типа привязка не генерится (исторические
                        // fallback «основной шаблон»/«взят INT» отключены)
                        warnings.Add($"F33: {p.MasterName}.DataOut[{b.Offset}] ({b.Tag}): " +
                                     $"в шаблоне нет эталона «{typeKey}» — привязка пропущена");
                        continue;
                    }

                    // F30: направление эталона должно совпадать с привязкой — read-эталон
                    // в записи (и наоборот) даёт неверную цепочку
                    if (b.IsWrite != ex.IsWrite)
                    {
                        warnings.Add(b.IsWrite
                            ? $"F30: {b.Tag}: для записи нужен datain-эталон (блок .DataIn[), взят " +
                              $"эталон чтения «{ex.TypeKey}» — пропущено"
                            : $"F30: {b.Tag}: для чтения взят write-эталон «{ex.TypeKey}» — пропущено");
                        continue;
                    }

                    int nSrc = ex.SourceCount;

                    // F33 (этап 6): валидация Size правила Typno — размер должен соответствовать
                    // эталону шаблона (число источников = байт/слотов) и типу функции пакета;
                    // расхождение — предупреждение (клонирование идёт по шаблону — эталон истины)
                    if (haveRule)
                    {
                        string ruleSize = MbTypnoMapRule.NormalizeSize(rule.Size);
                        if (ruleSize == "bit" && !p.IsBit)
                            warnings.Add($"F33: {b.Tag}: Size=bit (каждый DataOut — бит), а пакет " +
                                         $"регистровый (FC{p.FuncCode}) — проверьте правило Typno");
                        else if (ruleSize != "" && ruleSize != "bit" && p.IsBit)
                            warnings.Add($"F33: {b.Tag}: Size={ruleSize}, а пакет битовый " +
                                         $"(FC{p.FuncCode}) — проверьте правило Typno");
                        else if (ruleSize != "" && ruleSize != "bit" &&
                                 MbTypnoMapRule.SizeBytes(ruleSize) != nSrc)
                            warnings.Add($"F33: {b.Tag}: Size={ruleSize} " +
                                         $"({MbTypnoMapRule.SizeBytes(ruleSize)} байт/слотов), а эталон " +
                                         $"«{ex.TypeKey}» в шаблоне — {nSrc} источников(а) — " +
                                         "проверьте правило Typno/шаблон");
                    }
                    // F18: граница привязки — DataOut мастера массив байт (MB_USINT_0_249);
                    // F33: для битовых функций каждый элемент dataOut — bool по АДРЕСУ пакета
                    // (индекс = адрес), граница — в адресах (Count); для регистров — байты
                    int byteBase = ModbusBlockPlannerService.DataOutByteIndex(b.Offset, p.IsBit);
                    int bytesAvail = p.IsBit ? p.Count : p.Count * 2;
                    if (byteBase + nSrc - 1 >= bytesAvail)
                    {
                        warnings.Add(p.IsBit
                            ? $"F18: {p.MasterName}: привязка {b.Tag} (адрес {byteBase}, эталон " +
                              $"{ex.TypeKey} на {nSrc} элемент(ов)) выходит за данные пакета " +
                              $"({bytesAvail} адресов, Count={p.Count}) — пропущено"
                            : $"F18: {p.MasterName}: привязка {b.Tag} (байт {byteBase}, эталон " +
                              $"{ex.TypeKey} на {nSrc} байт(ов)) выходит за данные пакета " +
                              $"({bytesAvail} байт, Count={p.Count}) — пропущено");
                        continue;
                    }

                    // клон цепочки эталона (T11ID перенумеровываются, Y смещается на строку)
                    // F33 (этап 7): маппинг объектов активного шаблона — переменные-объекты
                    // (блоки 31/34) получают значение колонки/шаблона/литерала
                    // F35: строго по ключу — правило Typno (Template), иначе настройка
                    // шаблона; ключ не найден — подстановок НЕТ (fallback «единственного
                    // шаблона» F34 удалён — недопустимы скрытые подстановки)
                    string activeTplName = mappedTplName.Length > 0 ? mappedTplName : dataVarsTemplateKey;
                    IReadOnlyDictionary<string, (string IoColumn, string Literal)>? dv = null;
                    if (dataVars != null && activeTplName.Length > 0 &&
                        dataVars.TryGetValue(activeTplName, out var dvFound))
                        dv = dvFound;
                    if (dv == null && dataVars != null && dataVars.Count > 0 &&
                        noMappingWarned.Add(activeTplName))
                        warnings.Add($"F35: {activeTplName}: маппинг объектов data-шаблона " +
                            "не найден — подстановки не выполняются");
                    var t11Map = new Dictionary<int, int>();
                    foreach (var blk in ex.Blocks)
                    {
                        int oldT11 = T11Of(blk.Attribute("T11ID")?.Value);
                        int newT11 = nextT11++;
                        t11Map[oldT11] = newT11;
                        var clone = new XElement(blk);
                        clone.SetAttributeValue("T11ID", newT11);
                        clone.Element("Graphics")?.SetAttributeValue("Y",
                            (int.TryParse((string?)blk.Element("Graphics")?.Attribute("Y"), out int by) ? by : 0) + yBase);
                        ApplyBindingSubstitutions(clone, blk, p, b, ex, masterMeta,
                            ref nextCard, outCards, seenCards, dv, warnings, noMapWarned);
                        pouBlocks.Add(clone);
                    }
                    foreach (var l in ex.Links)
                    {
                        var clone = new XElement(l);
                        RemapEndPoint(clone, "FirstPoint", "FP", t11Map);
                        RemapEndPoint(clone, "LastPoint", "LP", t11Map);
                        ShiftPointListY(clone, yBase);
                        pouLinks.Add(clone);
                    }

                    yBase += ChainStep(ex);
                }

                    // F38: следующий пакет чанка размещается ниже цепочек предыдущего (+ зазор,
                    // чтобы цепочки пакетов не наложились друг на друга)
                    yBase += 100;
                }

                if (pouBlocks.Elements("Block").Any())
                {
                    // F17/F38: имя POU чанка — по DataOutPouNamePattern (контекст — первый
                    // пакет чанка); пусто/не разрешилось — историческое «имя блока мастера»;
                    // дубликаты внутри файла → суффиксы _2, _3 (SCADA требует уникальности POU)
                    var first = chunk[0];
                    string pouName = ScadaTemplateXmlService.BuildPouName(
                        s.DataOutPouNamePattern, first.MasterName, first);
                    if (pouName.Length == 0) pouName = first.MasterName;
                    pouName = MakeUniquePouName(pouName, usedPouNames);
                    outPous.Add(BuildPacketPou(pouId++, tplRoot, pouBlocks, pouLinks, pouName));
                }
            }

            // карточки мастеров (Info = имя блока)
            foreach (var kv in masterMeta)
                if (seenCards.Add(kv.Value.CardId))
                    outCards.Add(NewCard(kv.Value.CardId, kv.Key));

            var outRoot = new XElement("BufScadaPOUS",
                new XElement(tplRoot.Element("Common") ?? new XElement("Common")),
                outPous,
                new XElement(tplRoot.Element("ISAOBJSINFO") ?? new XElement("ISAOBJSINFO")),
                new XElement("ISACARDSINFO"));
            var cardsEl = outRoot.Element("ISACARDSINFO")!;
            foreach (var rec in outCards) cardsEl.Add(rec);

            var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), outRoot);
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + doc.ToString(SaveOptions.DisableFormatting);
        }

        // ================== Утилиты генерации ==================

        /// <summary>F13.1: ключ эталона по M_DataType: BOOL → BOOL; 8/16-Bit → INT;
        /// 32-Bit целые → DINT; 32/64-Bit Floating → REAL; прочее → INT.</summary>
        public static string MapTypeKey(string? dataType)
        {
            string t = (dataType ?? "").Trim();
            if (t.Length == 0) return "INT";
            if (t.Equals("BOOL", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Bit", StringComparison.OrdinalIgnoreCase)) return "BOOL";
            if (t.IndexOf("Floating", StringComparison.OrdinalIgnoreCase) >= 0) return "REAL";
            if (t.StartsWith("32-Bit", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("64-Bit", StringComparison.OrdinalIgnoreCase)) return "DINT";
            return "INT";
        }

        /// <summary>F30: выбор DataIn-шаблона для привязки записи: (1) имя правила Typno среди
        /// datain; (2) имя правила среди data-шаблонов, но только с write-эталоном typeKey;
        /// (3) любой datain-шаблон с write-эталоном typeKey (по имени — детерминированно);
        /// (4) datain-шаблон с единственным эталоном (single-fallback F18). null — нет
        /// write-эталона (привязка записи пропускается с предупреждением).</summary>
        private static DataOutTemplateInfo? ChooseWriteTemplate(
            Dictionary<string, DataOutTemplateInfo> writeInfo,
            Dictionary<string, DataOutTemplateInfo> namedInfo,
            string mappedTplName, string typeKey)
        {
            bool Has(DataOutTemplateInfo ti) =>
                ti.Valid && (ti.Exemplars.ContainsKey(typeKey) ||
                             (ti.Exemplars.Count == 1 && ti.Exemplars.Values.First().IsWrite));
            if (mappedTplName.Length > 0 &&
                writeInfo.TryGetValue(mappedTplName, out var byRule) && Has(byRule))
                return byRule;
            if (mappedTplName.Length > 0 &&
                namedInfo.TryGetValue(mappedTplName, out var byRule2) && Has(byRule2))
                return byRule2;
            foreach (var kv in writeInfo.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                if (Has(kv.Value)) return kv.Value;
            foreach (var kv in namedInfo.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                if (kv.Value.Valid && kv.Value.Exemplars.Values.Any(e => e.IsWrite) && Has(kv.Value))
                    return kv.Value;
            return null;
        }

        /// <summary>F13.1: имя тега-приёмника из IO_LIST: тире/пробелы → «_», ведущий «_»
        /// (правило F12 MakeMarka).</summary>
        public static string TagNameFor(string? tag)
        {
            string v = (tag ?? "").Trim();
            v = Regex.Replace(v, @"[\-\s]+", "_");
            return v.Length > 0 && !v.StartsWith("_") ? "_" + v : v;
        }

        /// <summary>F35: колонки IO_LIST точки привязки — источник разрешения маппинга
        /// «Объекты data-шаблонов» для ЭТОЙ привязки (пер-точечные значения: M_Marka
        /// адреса 579 ≠ M_Marka адреса 580); пусто — колонки первой точки пакета
        /// (историческое поведение F23/F33).</summary>
        private static IReadOnlyDictionary<string, string> BindColumns(MbBinding b, MbPacketPlan p) =>
            b.ColumnValues != null && b.ColumnValues.Count > 0 ? b.ColumnValues : p.ColumnValues;

        /// <summary>F13.1/F18: подстановки в клон цепочки: источник(и) — «{Block}.DataOut[{byteBase+j}]»
        /// (байтовый индекс: byteBase по смещению привязки + позиция источника в эталоне),
        /// cardId/IV от мастера пакета; приёмник — тег (+член эталона «.Xin») и новая карточка.
        /// F33 (этап 7): dataVars — маппинг объектов шаблона — переменные-объекты (блоки 31/34
        /// вне цепочки источников/приёмника). F36: канал подстановки определяется типом
        /// источника маппинга — колонка/строковый шаблон «{}» → ИМЯ (Info = TagNameFor(значение),
        /// IV шаблона не трогается; константа 34 — Info/T11Text её значения, как и литерал);
        /// литерал → ЗНАЧЕНИЕ (&lt;IV&gt; тега, Info шаблонный). Тот же разделитель — у приёмника.</summary>
        private void ApplyBindingSubstitutions(XElement clone, XElement blk, MbPacketPlan p,
            MbBinding b, Exemplar ex,
            IReadOnlyDictionary<string, (int CardId, string Iv)> masterMeta,
            ref int nextCard, List<XElement> outCards, HashSet<int> seenCards,
            IReadOnlyDictionary<string, (string IoColumn, string Literal)>? dataVars = null,
            List<string>? warnings = null,
            // F35: дедупликация предупреждений «приёмник без строки маппинга»
            HashSet<string>? noMapWarned = null)
        {
            // F33 (этап 7): переменная data-шаблона — блок-константа (34) или тег (31),
            // не источник/приёмник цепочки; подстановка значения из маппинга объектов
            // F34: приёмник (TargetTpl) тоже участвует — его строка маппинга имеет приоритет
            // над тегом привязки (см. ветку «блок-приёмник» ниже)
            if (dataVars != null)
            {
                string gtVar = clone.Attribute("GROBJTYPE")?.Value ?? "";
                bool isVarBlock = (gtVar == "31" || gtVar == "34") &&
                    !Exemplar.IsSourceBlock(blk) && !Exemplar.IsDataInBlock(blk) &&
                    !ReferenceEquals(blk, ex.SourceTpl) && !ReferenceEquals(blk, ex.TargetTpl);
                if (isVarBlock)
                {
                    string objName = (blk.Attribute("Info")?.Value ?? "").Trim();
                    if (objName.Length > 0 && dataVars.TryGetValue(objName, out var drow))
                    {
                        string dval = ResolveDataVarValue(drow.IoColumn, drow.Literal,
                            BindColumns(b, p), out string? dUnresolved, out bool dFromName);
                        if (dUnresolved != null)
                            warnings?.Add($"F33: {b.Tag}: объект шаблона «{objName}» — " +
                                $"колонка/шаблон «{dUnresolved}» не разрешился — значение не подставлено");
                        if (dval.Length > 0)
                        {
                            if (gtVar == "34")
                            {
                                // F36: константа — её значение и есть Info/T11Text: замена
                                // и для колонки (имя константы), и для литерала — без изменений
                                clone.SetAttributeValue("Info", dval);
                                clone.Element("Params")?.SetAttributeValue("T11Text", dval);
                            }
                            else if (dFromName)
                            {
                                // F36: колонка/строковый шаблон «{}» — канал ИМЕНИ: Info тега
                                // переименовывается по правилу F12 (TagNameFor, как у приёмника),
                                // <IV> шаблона НЕ трогается; карточка — с новым именем
                                string newNameV = TagNameFor(dval);
                                clone.SetAttributeValue("Info", newNameV);
                                int newCardV = nextCard;
                                nextCard += 10;
                                clone.Element("Params")?.SetAttributeValue("cardId", newCardV);
                                if (seenCards.Add(newCardV))
                                    outCards.Add(NewCard(newCardV, newNameV));
                            }
                            else
                            {
                                // F36: литерал — канал ЗНАЧЕНИЯ: подстановка в <IV> тега,
                                // Info шаблонный без изменений
                                var prVar = clone.Element("Params");
                                var ivVar = prVar?.Element("IV");
                                if (ivVar != null) ivVar.Value = dval;
                                else prVar?.Add(new XElement("IV", dval));
                            }
                        }
                    }
                    else if (warnings != null && objName.Length > 0 &&
                             (noMapWarned == null || noMapWarned.Add($"var|{objName}")))
                    {
                        // F37.1: var-объект без строки маппинга — подстановка НЕ выполняется;
                        // раньше молчало (в отличие от приёмника F35) — баг «литерал не пишется
                        // в <IV>» оставался невидимым: проверьте имя объекта/ключ шаблона
                        warnings.Add($"F37: объект шаблона «{objName}» — в маппинге «Объекты " +
                            $"data-шаблонов» нет строки объекта — подстановка не выполнена " +
                            "(имя строки маппинга должно совпадать с Info блока шаблона точно, " +
                            "например SLAVE_BYTES_ORGER — как в шаблоне)");
                    }
                }
            }
            // F30: write-эталон — направление инвертировано: блок-приёмник «.DataIn[…]»
            // подставляет путь записи мастера, тег-корень — имя тега IO_LIST
            if (ex.IsWrite)
            {
                if (Exemplar.IsDataInBlock(blk))
                {
                    // позиция приёмника в эталоне (DataIn[0] → 0, DataIn[1] → 1, …) —
                    // байтовый индекс = база смещения привязки + позиция (как у DataOut)
                    int j = ex.Blocks.Where(Exemplar.IsDataInBlock)
                        .OrderBy(DataInIndexOf).ToList().IndexOf(blk);
                    int byteBase = ModbusBlockPlannerService.DataOutByteIndex(b.Offset, p.IsBit);
                    int newOffset = byteBase + j;
                    clone.SetAttributeValue("Info", $"{b.BlockName}.DataIn[{newOffset}]");
                    clone.Element("Params")?.SetAttributeValue("T11Text", $".DataIn[{newOffset}]");
                    if (masterMeta.TryGetValue(b.BlockName, out var metaW))
                    {
                        var prW = clone.Element("Params");
                        prW?.SetAttributeValue("cardId", metaW.CardId);
                        var ivW = prW?.Element("IV");
                        if (ivW != null) ivW.Value = metaW.Iv;
                        else prW?.Add(new XElement("IV", metaW.Iv));
                    }
                    return;
                }
                if (ReferenceEquals(blk, ex.SourceTpl))
                {
                    // тег-корень цепочки — имя тега IO_LIST (как приёмник в цепочках чтения)
                    string tagW = TagNameFor(b.Tag);
                    string suffixW = MemberSuffixOf(ex.SourceTpl);
                    clone.SetAttributeValue("Info", tagW + suffixW);
                    int newCardW = nextCard;
                    nextCard += 10;
                    clone.Element("Params")?.SetAttributeValue("cardId", newCardW);
                    if (seenCards.Add(newCardW))
                        outCards.Add(NewCard(newCardW, tagW));
                }
                return; // промежуточные блоки (Word_to_MB/swap) — без изменений
            }

            // блок-источник «.DataOut[…]»
            if (Exemplar.IsSourceBlock(blk))
            {
                var srcIdx = ex.Blocks.Where(Exemplar.IsSourceBlock)
                    .OrderBy(DataOutIndexOf).ToList().IndexOf(blk);
                // F18: байтовая адресация DataOut (массив байт MB_USINT_0_249)
                int byteBase = ModbusBlockPlannerService.DataOutByteIndex(b.Offset, p.IsBit);
                int newOffset = byteBase + srcIdx;
                clone.SetAttributeValue("Info", $"{b.BlockName}.DataOut[{newOffset}]");
                clone.Element("Params")?.SetAttributeValue("T11Text", $".DataOut[{newOffset}]");
                if (masterMeta.TryGetValue(b.BlockName, out var meta))
                {
                    var pr = clone.Element("Params");
                    pr?.SetAttributeValue("cardId", meta.CardId);
                    var iv = pr?.Element("IV");
                    if (iv != null) iv.Value = meta.Iv;
                    else pr?.Add(new XElement("IV", meta.Iv));
                }
                return;
            }

            // блок-приёмник (тег/член тега)
            if (ReferenceEquals(blk, ex.TargetTpl))
            {
                string suffix = MemberSuffixOf(ex.TargetTpl); // напр. «.Xin» из T11Text
                // F35: приёмник — подстановка ТОЛЬКО по строке маппинга «Объекты
                // data-шаблонов» (колонка/строковый шаблон/литерал, значение — по колонкам
                // СВОЕЙ точки привязки). Тег привязки НЕ подставляется — скрытых подстановок
                // нет; без маппинга Info шаблона не изменяется.
                string objName = (blk.Attribute("Info")?.Value ?? "").Trim();
                string? mappedName = null;
                bool mappedFromName = false;
                if (dataVars != null && objName.Length > 0 &&
                    dataVars.TryGetValue(objName, out var drowT))
                {
                    mappedName = ResolveDataVarValue(drowT.IoColumn, drowT.Literal,
                        BindColumns(b, p), out string? dUnT, out mappedFromName);
                    if (dUnT != null)
                        warnings?.Add($"F35: {b.Tag}: приёмник «{objName}» — колонка/шаблон " +
                            $"«{dUnT}» не разрешился — значение не подставлено");
                    if (string.IsNullOrEmpty(mappedName)) mappedName = null;
                }
                if (mappedName != null && mappedFromName)
                {
                    // F36: колонка/строковый шаблон «{}» — канал ИМЕНИ: значение маппинга —
                    // имя тега по правилу F12 («1130-MXA-…» → «_1130_MXA_…», уже подчёркнутое —
                    // без изменений) + член эталона (.Xin); <IV> приёмника НЕ трогается
                    clone.SetAttributeValue("Info", TagNameFor(mappedName) + suffix);
                }
                else if (mappedName != null)
                {
                    // F36: литерал — канал ЗНАЧЕНИЯ: подстановка в <IV> приёмника,
                    // Info шаблонный («DCSTAG» + член эталона) без изменений
                    var prLit = clone.Element("Params");
                    var ivLit = prLit?.Element("IV");
                    if (ivLit != null) ivLit.Value = mappedName;
                    else prLit?.Add(new XElement("IV", mappedName));
                }
                else
                {
                    // F35: маппинг не задан/не разрешился — Info клона остаётся шаблонным
                    // БЕЗ изменений (в т.ч. шаблонный Info может уже содержать член, как
                    // «…_FAIL.Xin»); предупреждение — только когда таблица маппинга передана,
                    // но без строки объекта
                    if (dataVars != null &&
                        (noMapWarned == null || noMapWarned.Add($"{p.MasterName}|{objName}")))
                        warnings?.Add($"F35: {p.MasterName}: блок-приёмник «{objName}» — в маппинге " +
                            "«Объекты data-шаблонов» нет строки объекта — Info шаблона оставлен без изменений");
                }
                int newCard = nextCard;
                nextCard += 10;
                clone.Element("Params")?.SetAttributeValue("cardId", newCard);
                if (seenCards.Add(newCard))
                    // F36: карточка называется по ИМЕНИ приёмника: колонка/шаблон — новое имя
                    // (TagNameFor), литерал/отсутствие маппинга — шаблонное имя объекта
                    outCards.Add(NewCard(newCard,
                        mappedName != null && mappedFromName ? TagNameFor(mappedName) : objName));
            }
        }

        /// <summary>F33 (этап 7)/F36: значение маппинга объекта data-шаблона — строка с «{}» →
        /// рендер по ColumnValues пакета (F32, неизвестный токен → unresolved); имя колонки →
        /// значение колонки СВОЕЙ точки привязки; иначе литерал. Пусто — значение не подставить.
        /// F36: fromName=true — значение из колонки/строкового шаблона «{}» (канал ИМЕНИ —
        /// переименование Info по правилу F12, IV шаблона не трогается); fromName=false —
        /// литерал (канал ЗНАЧЕНИЯ — подстановка в &lt;IV&gt;, Info шаблонный).
        /// F38: public — резолв переиспользуется ScadaTemplateXmlService для текстов
        /// примитивов ([TEXT] — только канал значения, без TagNameFor).</summary>
        public static string ResolveDataVarValue(string? ioColumn, string? literal,
            IReadOnlyDictionary<string, string>? columnValues, out string? unresolved,
            out bool fromName)
        {
            unresolved = null;
            fromName = false;
            string col = (ioColumn ?? "").Trim();
            if (col.Length > 0 && columnValues != null)
            {
                if (col.IndexOf('{') >= 0)
                {
                    string rendered = ModbusBlockPlannerService.RenderVarTemplate(
                        col, columnValues, out var unres);
                    if (unres.Count > 0) unresolved = string.Join(",", unres);
                    if (unresolved == null || rendered != col)
                    {
                        fromName = true;
                        return rendered;
                    }
                }
                else if (columnValues.TryGetValue(col, out var cv) &&
                         !string.IsNullOrWhiteSpace(cv))
                {
                    fromName = true;
                    return cv.Trim();
                }
                else
                    unresolved = col; // колонка не найдена/пуста у точки пакета
            }
            return (literal ?? "").Trim();
        }

        /// <summary>F13.1: индекс DataOut из T11Text/Info блока-источника эталона («.DataOut[k]»).</summary>
        private static int DataOutIndexOf(XElement blk)
        {
            string s = (string?)blk.Element("Params")?.Attribute("T11Text") ??
                       (string?)blk.Attribute("Info") ?? "";
            var m = Regex.Match(s, @"\.DataOut\[(\d+)\]");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        /// <summary>F30: индекс DataIn из T11Text/Info блока-приёмника записи («.DataIn[k]»).</summary>
        private static int DataInIndexOf(XElement blk)
        {
            string s = (string?)blk.Element("Params")?.Attribute("T11Text") ??
                       (string?)blk.Attribute("Info") ?? "";
            var m = Regex.Match(s, @"\.DataIn\[(\d+)\]");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        /// <summary>F13.1: член тега приёмника эталона — суффикс T11Text, начинающийся с «.»
        /// («.Xin»); пусто — тег без члена.</summary>
        private static string MemberSuffixOf(XElement target)
        {
            string t = (string?)target.Element("Params")?.Attribute("T11Text") ?? "";
            return t.StartsWith(".") ? t : "";
        }

        /// <summary>F13.1: вертикальный шаг клонов цепочки — по высоте bbox эталона + отступ.</summary>
        private static int ChainStep(Exemplar ex)
        {
            int YOf(XElement b) =>
                int.TryParse((string?)b.Element("Graphics")?.Attribute("Y"), out int y) ? y : 0;
            int maxY = ex.Blocks.Select(YOf).DefaultIfEmpty(0).Max();
            int minY = ex.Blocks.Select(YOf).DefaultIfEmpty(0).Min();
            return Math.Max(120, maxY - minY + 60);
        }

        /// <summary>F13.1/F17: OnePOU пакета: атрибуты — от первого эталона шаблона,
        /// NAME — имя POU пакета (BuildPouName по DataOutPouNamePattern, пусто — имя
        /// блока мастера; MakeUniquePouName — уже уникально в файле), ISAGraf — блоки/
        /// связи цепочек пакета.</summary>
        private static XElement BuildPacketPou(int pouId, XElement tplRoot,
            XElement blocks, XElement links, string pouName)
        {
            var tplPou = tplRoot.Element("POUS")?.Element("OnePOU");
            var pou = new XElement("OnePOU",
                new XAttribute("ID", pouId),
                new XAttribute("NAME", pouName),
                new XAttribute("isFBD", (string?)tplPou?.Attribute("isFBD") ?? "1"),
                new XAttribute("GroupID", (string?)tplPou?.Attribute("GroupID") ?? ""),
                new XAttribute("Enabled", "1"),
                new XAttribute("POUNum", pouId - 20000),
                new XAttribute("Disc", "Обработка"));
            var tplParams = tplPou?.Element("PARAMS");
            if (tplParams != null)
                pou.Add(new XElement(tplParams));
            pou.Add(new XElement("ISAGraf", blocks, new XElement("Gotos"), links));
            return pou;
        }

        /// <summary>F13.1: rec ISACARDSINFO для новой карточки (Info = имя тега/блока).</summary>
        private static XElement NewCard(int id, string info) => new("rec",
            new XAttribute("ID", id),
            new XAttribute("Info", info),
            new XAttribute("IsRetain", "-1"),
            new XAttribute("Name", ""),
            new XAttribute("SSize", "0"),
            new XAttribute("KLPath", ""));

        /// <summary>F13.1: T11ID/ID из значения атрибута (не число → 0).</summary>
        private static int T11Of(string? s) => int.TryParse(s, out int v) ? v : 0;

        /// <summary>F17: уникализация имени POU в пределах файла: повторное имя →
        /// суффикс _2, _3… (HashSet — регистронезависимый, как имена в SCADA).</summary>
        private static string MakeUniquePouName(string name, HashSet<string> used)
        {
            string candidate = name;
            int n = 1;
            while (!used.Add(candidate))
            {
                n++;
                candidate = $"{name}_{n}";
            }
            return candidate;
        }

        /// <summary>F13.1: cardId блока из Params (нет → 0).</summary>
        private static int CardOf(XElement b) =>
            int.TryParse((string?)b.Element("Params")?.Attribute("cardId"), out int c) ? c : 0;

        /// <summary>F13.1: компонента связности, содержащая стартовый блок (union-find по T11ID).</summary>
        private static (List<XElement> Blocks, List<XElement> Links) ComponentOf(
            List<XElement> blocks, List<XElement> links, XElement start)
        {
            int IdOf(XElement b) => T11Of(b.Attribute("T11ID")?.Value);
            var parent = new Dictionary<int, int>();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { parent[Find(a)] = Find(b); }
            foreach (var b in blocks) parent[IdOf(b)] = IdOf(b);
            foreach (var l in links)
            {
                int f = T11Of(l.Element("FirstPoint")?.Attribute("FP")?.Value?.Split('|')[0]);
                int t = T11Of(l.Element("LastPoint")?.Attribute("LP")?.Value?.Split('|')[0]);
                if (parent.ContainsKey(f) && parent.ContainsKey(t)) Union(f, t);
            }
            int root = Find(IdOf(start));
            var compBlocks = blocks.Where(b => Find(IdOf(b)) == root).ToList();
            var compT11 = new HashSet<int>(compBlocks.Select(IdOf));
            var compLinks = links.Where(l =>
                compT11.Contains(T11Of(l.Element("FirstPoint")?.Attribute("FP")?.Value?.Split('|')[0])) &&
                compT11.Contains(T11Of(l.Element("LastPoint")?.Attribute("LP")?.Value?.Split('|')[0]))).ToList();
            return (compBlocks, compLinks);
        }

        /// <summary>F13.1: перенумеровать T11ID в FirstPoint/LastPoint связи по карте клонов.</summary>
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

        /// <summary>F13.1: сдвинуть точки соединения связи PL по Y.</summary>
        private static void ShiftPointListY(XElement linkClone, int dy)
        {
            var a = linkClone.Element("PointList")?.Attribute("PL");
            if (a == null) return;
            a.Value = Regex.Replace(a.Value, @"\((\d+),(\d+)\)",
                m => $"({m.Groups[1].Value},{int.Parse(m.Groups[2].Value) + dy})");
        }

        // ================== Поиск шаблона ==================

        /// <summary>F13.1: найти файл шаблона привязок по умолчанию: каталог exe → вверх (Work\);
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