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
    }
}
