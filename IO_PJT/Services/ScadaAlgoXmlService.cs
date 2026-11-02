using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// F65: генератор XML логических схем из шаблона (BufScadaPOUS с одним OnePOU).
    /// Из шаблона читаются все переменные (Block/@Info); каждое имя привязано к строке
    /// TYPNO (SCADA_ALGO_BINDINGS) и заменяется на марку контура (точечная замена каждого
    /// имени по своей строке привязки, длинные имена первыми — префиксы не портятся).
    /// Экземпляр = один контур Loop (NAME POU = Loop); ID/GroupID/POUNum OnePOU и
    /// T11ID/cardId блоков/карточек сдвигаются на k*IdStride (уникальность внутри файла).
    /// Пагинация: InstancesPerFile экземпляров на файл, превышение — следующий файл
    /// (FilePattern: {Template}, {Seq}).</summary>
    public sealed class ScadaAlgoXmlService
    {
        /// <summary>F65: результат разбора шаблона (валидность + сводка для статуса окна).</summary>
        public sealed class TemplateInfo
        {
            /// <summary>true — шаблон пригоден для генерации.</summary>
            public bool Valid;

            /// <summary>Текст ошибки валидации (пуст, если Valid).</summary>
            public string Error = "";

            /// <summary>Число блоков в шаблоне.</summary>
            public int BlockCount;

            /// <summary>Число уникальных имён (Block/@Info) — сигналов для привязок.</summary>
            public int SignalCount;

            /// <summary>Имя POU шаблона (NAME OnePOU).</summary>
            public string PouName = "";

            /// <summary>Итоговый текст для статуса окна.</summary>
            public string Summary => Valid
                ? $"POU {PouName}; блоков: {BlockCount}; сигналов: {SignalCount}"
                : Error;
        }

        /// <summary>F65: параметры генерации одного файла/пачки файлов.</summary>
        public sealed class GenerateOptions
        {
            /// <summary>Экземпляров (OnePOU) на файл (минимум 1).</summary>
            public int InstancesPerFile = 10;

            /// <summary>Шаблон имени файла ({Template}, {Seq}); пусто — «{Template}_{Seq}».</summary>
            public string FilePattern = "{Template}_{Seq}";

            /// <summary>Шаг сдвига T11ID/cardId между экземплярами (минимум 100).</summary>
            public int IdStride = 10000;

            /// <summary>Имя шаблона — токен {Template} в имени файла.</summary>
            public string TemplateName = "";
        }

        /// <summary>F65: разобрать шаблон: корень BufScadaPOUS, POUS/OnePOU, блоки;
        /// сигналы — уникальные непустые Block/@Info.</summary>
        public TemplateInfo Parse(string? templateXml)
        {
            var r = new TemplateInfo();
            if (string.IsNullOrWhiteSpace(templateXml)) { r.Error = "шаблон пуст"; return r; }
            XDocument doc;
            try { doc = XDocument.Parse(templateXml!); }
            catch (Exception ex) { r.Error = $"XML не разбирается: {ex.Message}"; return r; }
            if (doc.Root == null || doc.Root.Name != "BufScadaPOUS")
            { r.Error = "корень документа должен быть <BufScadaPOUS>"; return r; }

            var pou = doc.Root.Element("POUS")?.Element("OnePOU");
            if (pou == null) { r.Error = "в шаблоне нет POUS/OnePOU"; return r; }
            r.PouName = (string?)pou.Attribute("NAME") ?? "";

            var blocks = pou.Element("ISAGraf")?.Element("Blocks")?.Elements("Block").ToList()
                         ?? new List<XElement>();
            if (blocks.Count == 0) { r.Error = "в шаблоне нет блоков"; return r; }
            r.BlockCount = blocks.Count;
            r.SignalCount = blocks
                .Select(b => ((string?)b.Attribute("Info") ?? "").Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            r.Valid = true;
            return r;
        }

        /// <summary>F65: список сигналов шаблона (уникальные Block/@Info в порядке документа).</summary>
        public static List<string> ParseSignals(string? templateXml)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(templateXml)) return result;
            XDocument doc;
            try { doc = XDocument.Parse(templateXml!); }
            catch (Exception) { return result; }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in doc.Root?.Element("POUS")?.Element("OnePOU")?
                         .Element("ISAGraf")?.Element("Blocks")?.Elements("Block")
                     ?? Enumerable.Empty<XElement>())
            {
                string info = ((string?)b.Attribute("Info") ?? "").Trim();
                if (info.Length > 0 && seen.Add(info)) result.Add(info);
            }
            return result;
        }

        /// <summary>F65: применить привязки к телу шаблона — точечная замена каждого имени
        /// на марку (вхождения по всему файлу, как в legacy-генераторе); длинные имена
        /// первыми, чтобы `_..._FCOM` не был испорчен заменой префикса `_...`.</summary>
        public static string ApplyReplacements(string body,
            IReadOnlyList<(string Signal, string Mark)> map, List<string>? warnings = null)
        {
            string result = body ?? "";
            foreach (var (signal, mark) in map
                         .Where(x => x.Signal.Length > 0 && x.Mark.Length > 0)
                         .OrderByDescending(x => x.Signal.Length))
            {
                if (string.Equals(signal, mark, StringComparison.Ordinal)) continue;
                if (result.IndexOf(signal, StringComparison.Ordinal) < 0)
                {
                    warnings?.Add($"F65: имя «{signal}» в шаблоне не найдено — замена не применялась");
                    continue;
                }
                result = result.Replace(signal, mark, StringComparison.Ordinal);
            }
            return result;
        }

        /// <summary>F65: имя файла по шаблону ({Template}, {Seq}); пусто/битый — «{Template}_{Seq}».
        /// Недопустимые для файла символы заменяются на «_».</summary>
        public static string BuildFileName(string? pattern, string? templateName, int seq)
        {
            string p = string.IsNullOrWhiteSpace(pattern) ? "{Template}_{Seq}" : pattern!.Trim();
            string name = p.Replace("{Template}", templateName ?? "", StringComparison.OrdinalIgnoreCase)
                           .Replace("{Seq}", seq.ToString(), StringComparison.OrdinalIgnoreCase);
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Trim();
            if (name.Length == 0) name = $"algogen_{seq}";
            if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) name += ".xml";
            return name;
        }

        // ================== Генерация ==================

        /// <summary>F65: сгенерировать экземпляры: на каждый выбранный контур — OnePOU с
        /// заменёнными марками и сдвинутыми ID; упаковка по opt.InstancesPerFile на файл.
        /// markFor(loop) → карта «сигнал → марка» (пустая карта — контур пропускается).</summary>
        public AlgoGenResult Generate(string templateXml,
            IReadOnlyList<AlgoLoopInfo> loops,
            Func<AlgoLoopInfo, IReadOnlyList<(string Signal, string Mark)>> markFor,
            GenerateOptions opt)
        {
            if (templateXml == null) throw new ArgumentNullException(nameof(templateXml));
            if (loops == null) throw new ArgumentNullException(nameof(loops));
            if (markFor == null) throw new ArgumentNullException(nameof(markFor));
            opt ??= new GenerateOptions();

            var result = new AlgoGenResult();
            var info = Parse(templateXml);
            if (!info.Valid) throw new InvalidOperationException("F65 шаблон: " + info.Error);

            int perFile = Math.Max(1, opt.InstancesPerFile);
            int stride = Math.Max(100, opt.IdStride);
            var tplRoot = XDocument.Parse(templateXml).Root!;
            var tplCommon = tplRoot.Element("Common");
            var tplObjs = tplRoot.Element("ISAOBJSINFO");
            var tplCards = tplRoot.Element("ISACARDSINFO");

            var selected = loops.Where(l => l.Generate).ToList();
            result.LoopsTotal = selected.Count;
            result.Skipped = loops.Count - selected.Count;

            var fileChunks = new List<List<(AlgoLoopInfo Loop, XElement Pou, List<XElement> Cards)>>();
            var current = new List<(AlgoLoopInfo, XElement, List<XElement>)>();

            for (int k = 0; k < selected.Count; k++)
            {
                var loop = selected[k];
                var map = markFor(loop) ?? Array.Empty<(string, string)>();
                string replaced = ApplyReplacements(templateXml, map, result.Warnings);
                XDocument instDoc;
                try { instDoc = XDocument.Parse(replaced); }
                catch (Exception ex)
                {
                    result.Warnings.Add($"F65: контур {loop.Loop}: экземпляр не разбирается — {ex.Message}");
                    result.Skipped++;
                    continue;
                }
                var instRoot = instDoc.Root!;
                var pou = new XElement(instRoot.Element("POUS")!.Element("OnePOU")!);

                // сдвиг ID внутри файла: k — индекс экземпляра в файле (current.Count)
                int shift = current.Count * stride;
                ShiftInstanceIds(pou, shift);

                // NAME POU = имя контура (уникальность POU в файле SCADA требует)
                string loopName = loop.Loop.Trim();
                pou.SetAttributeValue("NAME", loopName);
                pou.SetAttributeValue("Disc", ScadaAlgoService.EnsureLeadingUnderscore(loopName));

                // карточки экземпляра: rec ISACARDSINFO с ID, используемыми блоками
                var cards = CollectCards(pou, tplCards, shift, loopName);

                current.Add((loop, pou, cards));
                result.Instances++;

                if (current.Count >= perFile)
                {
                    fileChunks.Add(current);
                    current = new List<(AlgoLoopInfo, XElement, List<XElement>)>();
                }
            }
            if (current.Count > 0) fileChunks.Add(current);

            // сборка файлов
            for (int fi = 0; fi < fileChunks.Count; fi++)
            {
                string fileName = BuildFileName(opt.FilePattern, opt.TemplateName, fi + 1);
                string xml = BuildFile(tplCommon, tplObjs, fileChunks[fi], fileName);
                result.Files.Add((fileName, xml));
                foreach (var (loop, _, _) in fileChunks[fi]) loop.FileName = fileName;
            }
            return result;
        }

        /// <summary>F65: сдвинуть ID экземпляра: Block T11ID/cardId, OnePrim SourceT11ID,
        /// FirstPoint/LastPoint (FP/LP — первый сегмент до «|»), OnePOU ID/GroupID/POUNum.</summary>
        private static void ShiftInstanceIds(XElement pou, int shift)
        {
            if (shift == 0) return;
            foreach (var b in pou.Descendants("Block"))
            {
                if (int.TryParse((string?)b.Attribute("T11ID"), out int t))
                    b.SetAttributeValue("T11ID", t + shift);
                var pr = b.Element("Params");
                if (pr != null && int.TryParse((string?)pr.Attribute("cardId"), out int c) && c > 0)
                    pr.SetAttributeValue("cardId", c + shift);
            }
            foreach (var p in pou.Descendants("OnePrim"))
                if (int.TryParse((string?)p.Attribute("SourceT11ID"), out int st) && st > 0)
                    p.SetAttributeValue("SourceT11ID", st + shift);
            foreach (var l in pou.Descendants("Link"))
                foreach (var (el, attr) in new (string, string)[] { ("FirstPoint", "FP"), ("LastPoint", "LP") })
                {
                    var pt = l.Element(el);
                    string? v = (string?)pt?.Attribute(attr);
                    if (v == null) continue;
                    int bar = v.IndexOf('|');
                    string head = bar >= 0 ? v.Substring(0, bar) : v;
                    string rest = bar >= 0 ? v.Substring(bar) : "";
                    if (int.TryParse(head, out int id)) pt!.SetAttributeValue(attr, (id + shift) + rest);
                }
            if (int.TryParse((string?)pou.Attribute("ID"), out int pid))
                pou.SetAttributeValue("ID", pid + shift);
            if (int.TryParse((string?)pou.Attribute("GroupID"), out int gid))
                pou.SetAttributeValue("GroupID", gid + shift);
            if (int.TryParse((string?)pou.Attribute("POUNum"), out int num))
                pou.SetAttributeValue("POUNum", num + shift / 1000); // № — логический, дробный сдвиг
        }

        /// <summary>F65: карточки экземпляра — rec ISACARDSINFO, чей ID (cardId) используется
        /// блоками OnePOU; ID сдвинут как у блоков, Info = имя блока (после замен — марка).</summary>
        private static List<XElement> CollectCards(XElement instRoot, XElement? tplCards, int shift, string loopName)
        {
            var result = new List<XElement>();
            var used = new HashSet<int>();
            foreach (var b in instRoot.Descendants("Block"))
            {
                var pr = b.Element("Params");
                if (pr != null && int.TryParse((string?)pr.Attribute("cardId"), out int c) && c > 0)
                    used.Add(c);
            }
            if (used.Count == 0) return result;

            // rec шаблона по исходному ID (сдвинутый − shift)
            var recById = new Dictionary<int, XElement>();
            foreach (var rec in tplCards?.Elements("rec") ?? Enumerable.Empty<XElement>())
                if (int.TryParse((string?)rec.Attribute("ID"), out int rid))
                    recById[rid] = rec;

            foreach (int shifted in used.OrderBy(x => x))
            {
                int orig = shifted - shift;
                var rec = recById.TryGetValue(orig, out var r) ? new XElement(r) : new XElement("rec");
                rec.SetAttributeValue("ID", shifted);
                // Info = имя блока с этим cardId (уже с марками)
                string blockInfo = instRoot.Descendants("Block")
                    .FirstOrDefault(b => b.Element("Params") != null &&
                                         (string?)b.Element("Params")?.Attribute("cardId") == shifted.ToString())
                    ?.Attribute("Info")?.Value ?? loopName;
                rec.SetAttributeValue("Info", blockInfo);
                result.Add(rec);
            }
            return result;
        }

        /// <summary>F65: собрать файл BufScadaPOUS: Common/ISAOBJSINFO из шаблона,
        /// POUS = OnePOU всех экземпляров чанка, ISACARDSINFO = их карточки.</summary>
        private static string BuildFile(XElement? common, XElement? objs,
            List<(AlgoLoopInfo Loop, XElement Pou, List<XElement> Cards)> chunk, string fileName)
        {
            var outPous = new XElement("POUS");
            var outCards = new XElement("ISACARDSINFO");
            var seenCards = new HashSet<int>();
            foreach (var (_, pou, cards) in chunk)
            {
                outPous.Add(pou);
                foreach (var c in cards)
                    if (int.TryParse((string?)c.Attribute("ID"), out int id) && seenCards.Add(id))
                        outCards.Add(c);
            }
            var outRoot = new XElement("BufScadaPOUS",
                common != null ? new XElement(common) : new XElement("Common"),
                outPous,
                objs != null ? new XElement(objs) : new XElement("ISAOBJSINFO"),
                outCards);
            var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), outRoot);
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + doc.ToString(SaveOptions.DisableFormatting);
        }
    }
}
