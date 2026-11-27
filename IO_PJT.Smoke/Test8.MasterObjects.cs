using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using IO_PJT.Data;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.Smoke
{
    public static partial class Program
    {
        // ============ 8. Объекты мастер-шаблона (F101) ============
        private static void TestMasterObjects()
        {
            var svc = new ScadaTemplateXmlService();
            string tpl = MasterTemplateXml();

            var objs = svc.ParseMasterTemplateObjects(tpl);
            Check(objs.Count == 4 && objs.Any(o => o.Name == "SLAVEID") &&
                  objs.Any(o => o.Name == "NAME_PACK_TCP_MAINE.Busy") &&
                  objs.Any(o => o.Name == "NAME_PACK_TCP_MAINE.BUSY.Busy") &&
                  objs.Any(o => o.Name == "NAME_PACK_TCP_RESERVE.Busy"),
                $"список объектов шаблона: 4 объекта, блоки мастера не входят (факт {objs.Count})");
            Check(objs.First(o => o.Name == "NAME_PACK_TCP_MAINE.Busy").DefaultEnding == ".Busy" &&
                  objs.First(o => o.Name == "NAME_PACK_TCP_MAINE.BUSY.Busy").DefaultEnding == ".BUSY.Busy" &&
                  objs.First(o => o.Name == "SLAVEID").DefaultEnding == "",
                "DefaultEnding = окончание после токена мастера");

            // без маппинга: окончание берётся из шаблона (поведение до F101)
            string x0 = svc.Write(MasterPackets(), tpl, new ScadaMbGenSettings()).PousXml;
            Check(x0.Contains("Info=\"_MB_X_MAIN.Busy\"") && x0.Contains("Info=\"_MB_X_MAIN.BUSY.Busy\"") &&
                  x0.Contains("Info=\"_MB_X_RESERVE.Busy\""),
                "без маппинга: имя = имя мастера + окончание из шаблона");
            // F109: rec карточки = база имени, члены делят карточку мастера
            CheckCardMembers(x0, "F109 синтетика, без маппинга");
            CheckSharedCards(x0, "_MB_X_MAIN", "F109 синтетика, без маппинга");
            CheckSharedCards(x0, "_MB_X_RESERVE", "F109 синтетика, без маппинга");

            var map = new Dictionary<string, (string NameSuffix, string IoColumn, string Literal)>
            {
                ["NAME_PACK_TCP_MAINE.BUSY.Busy"] = (".Busy", "", ""),
                ["NAME_PACK_TCP_MAINE.Busy"] = (".Error", "", "TRUE"),
                ["NAME_PACK_TCP_RESERVE.Busy"] = ("", "", "")
            };
            string x1 = svc.Write(MasterPackets(), tpl, new ScadaMbGenSettings(), masterObjVars: map).PousXml;
            Check(x1.Contains("Info=\"_MB_X_MAIN.Busy\"") && !x1.Contains("BUSY.Busy"),
                "регресс бага: окончание «.Busy» даёт _MB_X_MAIN.Busy, а не ..BUSY.Busy");
            Check(x1.Contains("Info=\"_MB_X_MAIN.Error\""),
                "окончание задано пользователем: имя мастера + «.Error»");
            Check(x1.Contains("Info=\"_MB_X_RESERVE.Busy\""),
                "пустое окончание → окончание из шаблона (reserve → имя reserve-блока)");
            // F109: и с маппингом rec = база, члены делят карточку мастера
            CheckCardMembers(x1, "F109 синтетика, с маппингом");
            CheckSharedCards(x1, "_MB_X_MAIN", "F109 синтетика, с маппингом");

            var err = XDocument.Parse(x1).Root!.Descendants("Block")
                .FirstOrDefault(b => (string?)b.Attribute("Info") == "_MB_X_MAIN.Error");
            Check(err?.Element("Params")?.Element("IV")?.Value == "TRUE",
                "начальное значение (литерал) записано в <IV> тега");

            // маппинг data-шаблонов (F52) не переименовывает объекты с токенами мастера
            var dv = new Dictionary<string, IReadOnlyDictionary<string,
                (string IoColumn, string Literal, string BitMode, string BitNo)>>
            {
                ["cur"] = new Dictionary<string, (string IoColumn, string Literal, string BitMode, string BitNo)>
                    { ["NAME_PACK_TCP_MAINE.Busy"] = ("M_Marka", "", "", "") }
            };
            string x2 = svc.Write(MasterPackets(), tpl, new ScadaMbGenSettings(), dv, "cur").PousXml;
            Check(x2.Contains("Info=\"_MB_X_MAIN.Busy\"") && !x2.Contains("_1130_MXA_01501A"),
                "канал имён data-маппинга не переименовывает объекты с токенами мастера");

            // хранение: колонка NameSuffix сохраняется и читается обратно
            string db = NewDbPath();
            try
            {
                CreateDb(db);
                var planner = new ModbusBlockPlannerService(db);
                planner.SaveTemplateVars("masterobj", "MB_TCP_MASTER_1", new[]
                {
                    new MbTemplateVar { Var = "NAME_PACK_TCP_MAINE.Busy", NameSuffix = ".Error",
                                        Literal = "TRUE" }
                });
                var back = planner.LoadTemplateVars("masterobj", "MB_TCP_MASTER_1");
                Check(back.Count == 1 && back[0].NameSuffix == ".Error" && back[0].Literal == "TRUE",
                    $"NameSuffix и литерал сохранены в SCADA_TEMPLATE_VARS (строк {back.Count})");
            }
            finally { DeleteDb(db); }
        }

        // ============ F109: инвариант карточек ============

        /// <summary>F109: инвариант SCADA — тег = UPPER(card.Info) + T11Text.
        /// Для блока-члена, у которого Info заканчивается на T11Text, rec карточки
        /// обязан удовлетворять card.Info + T11Text == Info блока (без задвоения
        /// окончания _X.BUSY.Busy). Блоки с переименованием при конфликте имён
        /// (суффикс _2, предупреждение «ИМЯ:») Info-инвариант не выполняют — они
        /// исключаются; для них важно лишь, что rec не содержит окончания члена.</summary>
        private static void CheckCardMembers(string xml, string label)
        {
            var doc = XDocument.Parse(xml); // исключение = FAIL теста
            var cards = doc.Root!.Descendants("ISACARDSINFO").Elements("rec")
                .Where(r => int.TryParse((string?)r.Attribute("ID"), out _))
                .ToDictionary(r => (string)r.Attribute("ID")!, r => (string?)r.Attribute("Info") ?? "");
            int members = 0;
            foreach (var b in doc.Root.Descendants("Block"))
            {
                var t11 = (string?)b.Element("Params")?.Attribute("T11Text") ?? "";
                if (!t11.StartsWith(".", StringComparison.Ordinal)) continue;
                var cid = (string?)b.Element("Params")?.Attribute("cardId") ?? "";
                var info = (string?)b.Attribute("Info") ?? "";
                if (!cards.TryGetValue(cid, out var cinfo))
                {
                    Check(false, $"{label}: карточка {cid} блока «{info}» найдена");
                    return;
                }
                if (!info.EndsWith(t11, StringComparison.OrdinalIgnoreCase)) continue; // конфликт имён (_2) — вне инварианта
                members++;
                if (!string.Equals(cinfo + t11, info, StringComparison.OrdinalIgnoreCase))
                {
                    Check(false, $"{label}: card «{cinfo}» + T11 «{t11}» ≠ Info «{info}» — задвоение окончания");
                    return;
                }
            }
            Check(true, $"{label}: члены .Busy/.Error ({members}) — card.Info + T11Text = Info блока");
        }

        /// <summary>F109: прямые члены мастера (Info == имя мастера + T11Text)
        /// ссылаются на одну и ту же карточку — как в шаблоне SCADA.</summary>
        private static void CheckSharedCards(string xml, string masterName, string label)
        {
            var doc = XDocument.Parse(xml);
            string? masterCard = doc.Root!.Descendants("Block")
                .FirstOrDefault(b => (string?)b.Attribute("Info") == masterName)?
                .Element("Params")?.Attribute("cardId")?.Value;
            var memberCards = doc.Root.Descendants("Block")
                .Select(b => (
                    Info: (string?)b.Attribute("Info") ?? "",
                    T11: (string?)b.Element("Params")?.Attribute("T11Text") ?? "",
                    Card: (string?)b.Element("Params")?.Attribute("cardId")?.Value))
                .Where(x => x.T11.StartsWith(".", StringComparison.Ordinal) &&
                            string.Equals(x.Info, masterName + x.T11, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Card)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            Check(masterCard != null && memberCards.Count == 1 && memberCards[0] == masterCard,
                $"{label}: мастер «{masterName}» и члены делят одну карточку (мастер={masterCard}, члены=[{string.Join(", ", memberCards)}])");
        }
    }
}
