using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.Smoke
{
    public static partial class Program
    {
        // ============ 9. Реальный мастер-шаблон: перечень объектов и окончание ============
        private static void TestRealTemplate()
        {
            string path = @"c:\Users\RAN-Notebook\Desktop\Work\Work\_Шаблоны\ModbusTCP_reserv_v0.xml";
            if (!System.IO.File.Exists(path))
            {
                Check(false, "эталонный мастер-шаблон ModbusTCP_reserv_v0.xml найден");
                return;
            }
            string tpl = System.IO.File.ReadAllText(path);
            var svc = new ScadaTemplateXmlService();
            var info = svc.Parse(tpl);
            Check(info.Valid, $"шаблон валиден (мастеров: {info.MasterCount})");

            var objs = svc.ParseMasterTemplateObjects(tpl);
            foreach (var o in objs)
                Console.WriteLine($"    объект: {o.Name} (gt{o.GrobjType}) окончание «{o.DefaultEnding}»");
            Check(objs.Count > 0, $"перечень объектов не пуст (факт {objs.Count})");
            Check(objs.Any(o => o.Name.Contains(".Busy") || o.Name.Contains(".BUSY")),
                "в перечне есть объекты с окончанием Busy");
            Check(objs.All(o => !o.Name.StartsWith("NAME_PACK_TCP_MAINE", StringComparison.Ordinal) ||
                                o.Name.IndexOf('.') >= 0 || o.Name.Contains("_LINK")),
                "блоки мастера в перечень не попадают");

            var reserve = objs.FirstOrDefault(o => o.Name.Contains("RESERVE", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"    резервный объект: {reserve?.Name ?? "нет"}");

            var packets = MasterPackets();
            // без маппинга: имя = токен, заменённый на имя пакета + окончание шаблона
            string plain = svc.Write(packets, tpl, new ScadaMbGenSettings()).PousXml;
            Check(!plain.Contains("NAME_PACK_TCP_RESERVE") && !plain.Contains("NAME_PACK_TCP_MAINE"),
                "токены имени мастера подменены во всём результате");
            Check(!plain.Contains("NAME_PACK_TCP_RESERVE.BUSY") && !plain.Contains("NAME_PACK_TCP_MAINE.BUSY"),
                "после подмены не осталось литералов токена с BUSY");

            // маппинг: окончание задано пользователем
            if (reserve != null)
            {
                var map = new Dictionary<string, (string NameSuffix, string IoColumn, string Literal)>
                {
                    [reserve.Name] = (".Busy", "", "")
                };
                string mapped = svc.Write(packets, tpl, new ScadaMbGenSettings(), masterObjVars: map).PousXml;
                Check(mapped.Contains("Info=\"_MB_X_RESERVE.Busy\""),
                    "окончание из маппинга: резервный объект → _MB_X_RESERVE.Busy");
            }
        }
    }
}
