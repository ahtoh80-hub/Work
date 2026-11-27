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
        // ============ 11. F105: удаление непривязанных битовых объектов ============

        /// <summary>Миниатюрный data-шаблон: эталон BOOL (источник → приёмник TAG_IN +
        /// битовые var-объекты BIT1/BIT2) и эталон INT (источник → приёмник-бит BIT0).</summary>
        private static string BitDataTemplateXml() => (
            "<?xml version='1.0' encoding='UTF-8'?><BufScadaPOUS><Common/>" +
            "<POUS>" +
            // BOOL: источник → TAG_IN (приёмник), BIT1/BIT2 — var-объекты
            "<OnePOU ID='9001' NAME='BOOL' Disc='BOOL'><PARAMS WIDTH='600' HEIGHT='400'/>" +
            "<ISAGraf><Blocks>" +
            "<Block T11ID='91001' GROBJTYPE='31' Info='MB.DataOut[0]'>" +
            "<Graphics X='40' Y='40' WIDTH='160' HEIGHT='20'/>" +
            "<Params T11Text='.DataOut[0]' CI='1' CO='1' cardId='91101'><IV/></Params></Block>" +
            "<Block T11ID='91002' GROBJTYPE='31' Info='TAG_IN'>" +
            "<Graphics X='400' Y='40' WIDTH='160' HEIGHT='20'/>" +
            "<Params T11Text='' CI='1' CO='1' cardId='91102'><IV/></Params></Block>" +
            "<Block T11ID='91003' GROBJTYPE='31' Info='BIT1'>" +
            "<Graphics X='400' Y='100' WIDTH='160' HEIGHT='20'/>" +
            "<Params T11Text='' CI='1' CO='1' cardId='91103'><IV/></Params></Block>" +
            "<Block T11ID='91004' GROBJTYPE='31' Info='BIT2'>" +
            "<Graphics X='400' Y='160' WIDTH='160' HEIGHT='20'/>" +
            "<Params T11Text='' CI='1' CO='1' cardId='91104'><IV/></Params></Block>" +
            "</Blocks><Links>" +
            "<Link Negative='false' asPointer='false' UserEdit='false' Color='0' GetBitNum='-1' ConvertTo='0'>" +
            "<PointList PL='(200,50);(400,50);'/><FirstPoint FP='91001|False|0|0,0,100,20'/>" +
            "<LastPoint LP='91002|True||0,0,100,20'/></Link>" +
            "<Link Negative='false' asPointer='false' UserEdit='false' Color='0' GetBitNum='-1' ConvertTo='0'>" +
            "<PointList PL='(200,50);(400,110);'/><FirstPoint FP='91001|False|0|0,0,100,20'/>" +
            "<LastPoint LP='91003|True||0,0,100,20'/></Link>" +
            "<Link Negative='false' asPointer='false' UserEdit='false' Color='0' GetBitNum='-1' ConvertTo='0'>" +
            "<PointList PL='(200,50);(400,170);'/><FirstPoint FP='91001|False|0|0,0,100,20'/>" +
            "<LastPoint LP='91004|True||0,0,100,20'/></Link>" +
            "</Links></ISAGraf></OnePOU>" +
            // INT: источник → приёмник-бит BIT0 (ветка удаления приёмника)
            "<OnePOU ID='9002' NAME='INT' Disc='INT'><PARAMS WIDTH='600' HEIGHT='300'/>" +
            "<ISAGraf><Blocks>" +
            "<Block T11ID='92001' GROBJTYPE='31' Info='MB.DataOut[0]'>" +
            "<Graphics X='40' Y='40' WIDTH='160' HEIGHT='20'/>" +
            "<Params T11Text='.DataOut[0]' CI='1' CO='1' cardId='92101'><IV/></Params></Block>" +
            "<Block T11ID='92002' GROBJTYPE='31' Info='BIT0'>" +
            "<Graphics X='400' Y='40' WIDTH='160' HEIGHT='20'/>" +
            "<Params T11Text='' CI='1' CO='1' cardId='92102'><IV/></Params></Block>" +
            "</Blocks><Links>" +
            "<Link Negative='false' asPointer='false' UserEdit='false' Color='0' GetBitNum='-1' ConvertTo='0'>" +
            "<PointList PL='(200,50);(400,50);'/><FirstPoint FP='92001|False|0|0,0,100,20'/>" +
            "<LastPoint LP='92002|True||0,0,100,20'/></Link>" +
            "</Links></ISAGraf></OnePOU>" +
            "</POUS><ISAOBJSINFO/><ISACARDSINFO/></BufScadaPOUS>").Replace('\'', '"');


        /// <summary>Миниатюрный write-data-шаблон: битовый корень BIT0 → .DataIn[0]
        /// (корень write-эталона должен участвовать в F105).</summary>
        private static string BitWriteDataTemplateXml() => (
            "<?xml version='1.0' encoding='UTF-8'?><BufScadaPOUS><Common/>" +
            "<POUS>" +
            "<OnePOU ID='9101' NAME='BOOL' Disc='BOOL'><PARAMS WIDTH='400' HEIGHT='200'/>" +
            "<ISAGraf><Blocks>" +
            "<Block T11ID='91101' GROBJTYPE='31' Info='BIT0'>" +
            "<Graphics X='40' Y='40' WIDTH='160' HEIGHT='20'/>" +
            "<Params T11Text='' CI='1' CO='1' cardId='91111'><IV/></Params></Block>" +
            "<Block T11ID='91102' GROBJTYPE='31' Info='MB.DataIn[0]'>" +
            "<Graphics X='300' Y='40' WIDTH='160' HEIGHT='20'/>" +
            "<Params T11Text='.DataIn[0]' CI='1' CO='1' cardId='91112'><IV/></Params></Block>" +
            "</Blocks><Links>" +
            "<Link Negative='false' asPointer='false' UserEdit='false' Color='0' GetBitNum='-1' ConvertTo='0'>" +
            "<PointList PL='(200,50);(300,50);'/><FirstPoint FP='91101|False|0|0,0,100,20'/>" +
            "<LastPoint LP='91102|True||0,0,100,20'/></Link>" +
            "</Links></ISAGraf></OnePOU>" +
            "</POUS><ISAOBJSINFO/><ISACARDSINFO/></BufScadaPOUS>").Replace('\'', '"');

        /// <summary>Пакет FC16 с одним write-привязкой BIT0; bound=true — точка
        /// M_Bit=0 с непустой M_Marka, иначе — точка с пустой M_Marka.</summary>
        private static List<MbPacketPlan> BitWritePackets(bool bound)
        {
            var p = new MbPacketPlan
            {
                No = 1, MasterName = "_MB_BIT_W",
                Ip1 = 192, Ip2 = 168, Ip3 = 4, Ip4 = 20, Port = 502,
                SlaveId = 5, FuncCode = 16, Address = 0, Count = 1, TimeoutMs = 200
            };
            p.Bindings.Add(new MbBinding
            {
                Tag = "1130-MXA-W00", Address = 0, Offset = 0, BlockName = "_MB_BIT_W",
                DataType = "BOOL", IsWrite = true, DataOutExpr = "W0",
                ColumnValues = new Dictionary<string, string>
                {
                    ["M_Bit"] = "0", ["M_Marka"] = bound ? "TAG_ROOT" : ""
                }
            });
            return new List<MbPacketPlan> { p };
        }

        /// <summary>Пакет FC03 с одной привязкой: точка бита 2 найдена, но M_Marka пуста —
        /// марка не привязалась, объект F105 должен удаляться.</summary>
        private static List<MbPacketPlan> BitPacketsEmptyMarka()
        {
            var p = new MbPacketPlan
            {
                No = 1, MasterName = "_MB_BIT_T",
                Ip1 = 192, Ip2 = 168, Ip3 = 4, Ip4 = 20, Port = 502,
                SlaveId = 5, FuncCode = 3, Address = 0, Count = 5, TimeoutMs = 200
            };
            p.Bindings.Add(new MbBinding
            {
                Tag = "1130-MXA-B02", Address = 0, Offset = 0, BlockName = "_MB_BIT_T",
                DataType = "BOOL", Typno = "P101-T",
                ColumnValues = new Dictionary<string, string>
                {
                    ["M_Bit"] = "2", ["M_Marka"] = ""
                }
            });
            return new List<MbPacketPlan> { p };
        }

        /// <summary>Пакет FC03 с двумя привязками: BOOL (бит 1 привязан — M_Bit=1/M_Marka,
        /// бит 2 не привязан) и INT (без M_Bit — приёмник BIT0 непривязан).</summary>
        private static List<MbPacketPlan> BitPackets()
        {
            var p = new MbPacketPlan
            {
                No = 1, MasterName = "_MB_BIT_T",
                Ip1 = 192, Ip2 = 168, Ip3 = 4, Ip4 = 20, Port = 502,
                SlaveId = 5, FuncCode = 3, Address = 0, Count = 5, TimeoutMs = 200
            };
            p.Bindings.Add(new MbBinding
            {
                Tag = "1130-MXA-B01", Address = 0, Offset = 0, BlockName = "_MB_BIT_T",
                DataType = "BOOL", Typno = "P101-T",
                ColumnValues = new Dictionary<string, string>
                {
                    ["M_Bit"] = "1", ["M_Marka"] = "TAG_ONE"
                }
            });
            p.Bindings.Add(new MbBinding
            {
                Tag = "1130-MXA-W01", Address = 0, Offset = 0, BlockName = "_MB_BIT_T",
                DataType = "16-Bit Signed", Typno = "P101-T",
                ColumnValues = new Dictionary<string, string>
                {
                    ["M_Marka"] = "TAG_INT"
                }
            });
            return new List<MbPacketPlan> { p };
        }

        /// <summary>Собрать из результата пару (T11ID блоков, концы связей FP/LP):
        /// проверка отсутствия висячих ссылок после удаления объектов.</summary>
        private static (HashSet<int> Blocks, HashSet<int> Ends) CollectT11(string xml)
        {
            var doc = XDocument.Parse(xml);
            var blocks = new HashSet<int>();
            var ends = new HashSet<int>();
            foreach (var b in doc.Descendants("Block"))
                if (int.TryParse((string?)b.Attribute("T11ID"), out int t)) blocks.Add(t);
            foreach (var el in doc.Descendants().Where(e => e.Name == "FirstPoint" || e.Name == "LastPoint"))
            {
                string? v = (string?)el.Attribute(el.Name == "FirstPoint" ? "FP" : "LP");
                string head = (v ?? "").Split('|')[0];
                if (int.TryParse(head, out int t)) ends.Add(t);
            }
            return (blocks, ends);
        }

        /// <summary>F105: карточка удалённого битового объекта не должна попадать в XML.</summary>
        private static bool HasCard(string xml, string info) =>
            XDocument.Parse(xml).Descendants("rec").Any(e =>
                string.Equals((string?)e.Attribute("Info"), info, StringComparison.Ordinal));

        private static void Test11_UnboundBits()
        {
            string tpl = BitDataTemplateXml();
            var svc = new ScadaDataOutXmlService();
            var info = svc.Parse(tpl);
            Check(info.Valid, "битовый data-шаблон валиден (эталоны BOOL/INT)");
            Check(info.Exemplars.ContainsKey("BOOL") && info.Exemplars.ContainsKey("INT"),
                "эталоны BOOL и INT разобраны");

            var packets = BitPackets();
            var masterMeta = new Dictionary<string, (int CardId, string Iv)>();

            // ---- 1. Настройка выкл (default): непривязанные объекты остаются ----
            var sOff = new ScadaMbGenSettings();
            Check(!sOff.RemoveUnboundBitObjects, "default: RemoveUnboundBitObjects = false");
            var wOff = new List<string>();
            string xOff = svc.WriteMulti(packets, tpl, masterMeta, sOff, wOff);
            Check(xOff.Contains("Info=\"BIT2\""), "выкл: var-объект BIT2 остаётся в XML");
            Check(xOff.Contains("Info=\"BIT0\""), "выкл: приёмник-бит BIT0 остаётся в XML");
            Check(xOff.Contains("Info=\"TAG_IN\""), "выкл: приёмник TAG_IN остаётся");
            Check(!wOff.Any(w => w.StartsWith("F105")), "выкл: предупреждений F105 нет");

            // ---- 2. Настройка вкл, таблицы маппинга нет: непривязанные удалены ----
            var sOn = new ScadaMbGenSettings { RemoveUnboundBitObjects = true };
            var wOn = new List<string>();
            string xOn = svc.WriteMulti(packets, tpl, masterMeta, sOn, wOn);
            Check(!xOn.Contains("Info=\"BIT2\""), "вкл: непривязанный var-объект BIT2 удалён");
            Check(!xOn.Contains("Info=\"BIT0\""), "вкл: непривязанный приёмник-бит BIT0 удалён");
            Check(!HasCard(xOn, "BIT2"), "вкл: карточка удалённого var-объекта BIT2 не создана");
            Check(!HasCard(xOn, "BIT0"), "вкл: карточка удалённого приёмника BIT0 не создана");
            Check(xOn.Contains("Info=\"BIT1\""), "вкл: привязанный бит 1 (M_Bit=1/M_Marka) сохранён");
            Check(xOn.Contains("Info=\"TAG_IN\""), "вкл: небитовый приёмник TAG_IN сохранён");
            Check(wOn.Any(w => w.StartsWith("F105")), "вкл: есть предупреждение F105");
            var (blk, ends) = CollectT11(xOn);
            Check(ends.IsSubsetOf(blk), "вкл: висячих ссылок FP/LP нет (концы — существующие блоки)");

            // ---- 3. Настройка вкл + реальная строка {M_Marka}: непривязанные удалены ----
            var dvMarka = new Dictionary<string, IReadOnlyDictionary<string,
                (string IoColumn, string Literal, string BitMode, string BitNo)>>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["T"] = new Dictionary<string, (string, string, string, string)>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["BIT2"] = ("{M_Marka}", "", "Адрес/бит", "2"),
                    ["BIT0"] = ("{M_Marka}", "", "Адрес/бит", "0")
                }
            };
            var wMarka = new List<string>();
            string xMarka = svc.WriteMulti(packets, tpl, masterMeta, sOn, wMarka,
                dataVars: dvMarka, dataVarsTemplateKey: "T");
            Check(!xMarka.Contains("Info=\"BIT2\""),
                "вкл + {M_Marka}: var-объект BIT2 без точки M_Bit=2 удалён");
            Check(!xMarka.Contains("Info=\"BIT0\""),
                "вкл + {M_Marka}: приёмник BIT0 без точки M_Bit=0 удалён");
            Check(!HasCard(xMarka, "BIT2"), "вкл + {M_Marka}: карточка BIT2 не создана");
            Check(!HasCard(xMarka, "BIT0"), "вкл + {M_Marka}: карточка BIT0 не создана");
            // F12: TagNameFor добавляет ведущий «_» к марке с подчёркиванием → _TAG_ONE
            Check(xMarka.Contains("Info=\"_TAG_ONE\""),
                "вкл + {M_Marka}: привязанный BIT1 переименован по M_Marka (F75)");
            Check(wMarka.Any(w => w.StartsWith("F105")), "вкл + {M_Marka}: есть предупреждение F105");
            var (blkMarka, endsMarka) = CollectT11(xMarka);
            Check(endsMarka.IsSubsetOf(blkMarka),
                "вкл + {M_Marka}: висячих ссылок FP/LP нет (концы — существующие блоки)");

            // ---- 4. Точка с нужным битом есть, но M_Marka пуста: объект удалён ----
            var packetsEmptyMarka = BitPacketsEmptyMarka();
            var wEmptyMarka = new List<string>();
            string xEmptyMarka = svc.WriteMulti(packetsEmptyMarka, tpl, masterMeta, sOn,
                wEmptyMarka, dataVars: dvMarka, dataVarsTemplateKey: "T");
            Check(!xEmptyMarka.Contains("Info=\"BIT2\""),
                "вкл + пустая M_Marka у точки с M_Bit=2: объект BIT2 удалён");
            Check(!HasCard(xEmptyMarka, "BIT2"),
                "вкл + пустая M_Marka у точки с M_Bit=2: карточка BIT2 не создана");
            Check(wEmptyMarka.Any(w => w.StartsWith("F105")),
                "вкл + пустая M_Marka: есть предупреждение F105");

            // ---- 5. Настройка вкл + литерал при непривязанном бите: объекты удалены ----
            var dvLit = new Dictionary<string, IReadOnlyDictionary<string,
                (string IoColumn, string Literal, string BitMode, string BitNo)>>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["T"] = new Dictionary<string, (string, string, string, string)>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["BIT2"] = ("", "7", "", ""),   // литерал — марка бита всё равно не привязана
                    ["BIT0"] = ("", "9", "", "")    // приёмник с литералом
                }
            };
            var wLit = new List<string>();
            string xLit = svc.WriteMulti(packets, tpl, masterMeta, sOn, wLit,
                dataVars: dvLit, dataVarsTemplateKey: "T");
            Check(!xLit.Contains("Info=\"BIT2\""),
                "вкл + литерал: var-объект BIT2 без точки M_Bit=2 удалён");
            Check(!xLit.Contains("Info=\"BIT0\""),
                "вкл + литерал: приёмник BIT0 без точки M_Bit=0 удалён");
            Check(xLit.Contains("Info=\"_TAG_ONE\""),
                "вкл + литерал: привязанный BIT1 переименован по M_Marka (F75)");

            // ---- 6. Настройка вкл + режим «Адрес»: объект не удаляется ----
            var dvAddr = new Dictionary<string, IReadOnlyDictionary<string,
                (string IoColumn, string Literal, string BitMode, string BitNo)>>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["T"] = new Dictionary<string, (string, string, string, string)>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["BIT2"] = ("", "", "Адрес", "")
                }
            };
            var wAddr = new List<string>();
            string xAddr = svc.WriteMulti(packets, tpl, masterMeta, sOn, wAddr,
                dataVars: dvAddr, dataVarsTemplateKey: "T");
            Check(xAddr.Contains("Info=\"BIT2\""), "вкл + режим «Адрес»: объект BIT2 сохранён");
            Check(!xAddr.Contains("Info=\"BIT0\""),
                "вкл + режим «Адрес»: приёмник BIT0 без строки маппинга удалён");

            // ---- 7. Write-эталон: битовый КОРЕНЬ BIT0 участвует в F105 ----
            string writeTpl = BitWriteDataTemplateXml();
            var writeIn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["T"] = writeTpl
            };
            var dvWrite = new Dictionary<string, IReadOnlyDictionary<string,
                (string IoColumn, string Literal, string BitMode, string BitNo)>>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["T"] = new Dictionary<string, (string, string, string, string)>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["BIT0"] = ("{M_Marka}", "", "Адрес/бит", "0")
                }
            };
            var wWriteOff = new List<string>();
            string xWriteOff = svc.WriteMulti(BitWritePackets(false), tpl, masterMeta, sOff,
                wWriteOff, dataInTemplates: writeIn, dataVars: dvWrite,
                dataVarsTemplateKey: "T");
            Check(xWriteOff.Contains("Info=\"BIT0\""),
                "write выкл: битовый корень BIT0 остаётся в XML");

            var wWriteOn = new List<string>();
            string xWriteOn = svc.WriteMulti(BitWritePackets(false), tpl, masterMeta, sOn,
                wWriteOn, dataInTemplates: writeIn, dataVars: dvWrite,
                dataVarsTemplateKey: "T");
            Check(!xWriteOn.Contains("Info=\"BIT0\""),
                "write вкл: непривязанный корень BIT0 удалён");
            Check(!HasCard(xWriteOn, "BIT0"),
                "write вкл: карточка непривязанного корня BIT0 не создана");

            var wWriteBound = new List<string>();
            string xWriteBound = svc.WriteMulti(BitWritePackets(true), tpl, masterMeta, sOn,
                wWriteBound, dataInTemplates: writeIn, dataVars: dvWrite,
                dataVarsTemplateKey: "T");
            Check(xWriteBound.Contains("Info=\"_TAG_ROOT\""),
                "write вкл: привязанный корень BIT0 переименован по M_Marka (F75)");

            // ---- 8. Персист настроек: SettingKeys + roundtrip Save/Load ----
            Check(ModbusBlockPlannerService.SettingKeys.Contains("RemoveUnboundBitObjects"),
                "SettingKeys содержит RemoveUnboundBitObjects");
            var sClone = new ScadaMbGenSettings { RemoveUnboundBitObjects = true }.Clone();
            Check(sClone.RemoveUnboundBitObjects, "Clone() копирует RemoveUnboundBitObjects");
            string db = NewDbPath();
            try
            {
                CreateDb(db);
                var planner = new ModbusBlockPlannerService(db);
                planner.SaveSettings(sOn);
                Check(planner.LoadSettings().RemoveUnboundBitObjects,
                    "roundtrip: Save/Load сохраняет true");
                planner.SaveSettings(sOff);
                Check(!planner.LoadSettings().RemoveUnboundBitObjects,
                    "roundtrip: Save/Load сохраняет false");
            }
            finally { DeleteDb(db); }
        }
    }
}
