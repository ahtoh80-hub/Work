using System;
using System.Linq;
using System.Xml.Linq;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.Smoke
{
    public static partial class Program
    {
        // ============ 7. Генерация XML мастер-блоков ============
        private static void TestGenerateXml()
        {
            var svc = new ScadaTemplateXmlService();
            string tpl = MasterTemplateXml();

            var info = svc.Parse(tpl);
            Check(info.Valid, $"шаблон распознан (ошибка: {info.Error})");
            Check(!svc.Parse("<не xml").Valid, "битый XML → шаблон невалиден");

            var packets = MasterPackets();
            var gen = svc.Write(packets, tpl, new ScadaMbGenSettings());
            string xml = gen.PousXml;
            var doc = XDocument.Parse(xml); // исключение = FAIL теста
            Check(doc.Root != null, "результат — корректный XML");

            var blocks = doc.Root!.Descendants("Block").ToList();
            var master = blocks.Where(b => (string?)b.Attribute("GROBJTYPE") == "37").ToList();
            Check(master.Count == 2,
                $"в результате 2 блока мастера — по числу пакетов (факт {master.Count})");
            var names = master.Select(b => (string?)b.Attribute("Info")).ToList();
            Check(names.Contains("_MB_X_MAIN") && names.Contains("_MB_X_RESERVE"),
                $"блоки названы по MasterName пакетов (факт {string.Join(", ", names)})");

            Check(!xml.Contains("NAME_PACK_TCP_MAINE") && !xml.Contains("NAME_PACK_TCP_RESERVE"),
                "токены имён мастера шаблона заменены везде (блоки, связи, карточки)");
            Check(blocks.Select(b => (string?)b.Attribute("T11ID")).Distinct().Count() == blocks.Count,
                "T11ID блоков уникальны");

            var ids = doc.Root.Descendants("rec").Select(r => (string?)r.Attribute("ID")).ToList();
            Check(ids.Distinct().Count() == ids.Count, "ID карточек уникальны");
            Check(gen.Warnings.All(w => !w.Contains("ИМЯ:")), "нет красных предупреждений «ИМЯ:»");

            bool threw = false;
            try { svc.Write(packets, "<не xml", new ScadaMbGenSettings()); }
            catch (InvalidOperationException) { threw = true; }
            Check(threw, "Write с невалидным шаблоном → InvalidOperationException");
        }
    }
}
