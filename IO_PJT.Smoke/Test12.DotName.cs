using System;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.Smoke
{
    public static partial class Program
    {
        // ============ 12. F106: точка «.» — нормальный символ, не заменяется на «_» ============

        private static void Test12_DotName()
        {
            Log("=== Тест 12: F106 — точка в именах/марках не заменяется на «_» ===");

            // ---- 1. SanitizeNameFull: точка сохраняется, renamed не срабатывает ----
            string dot1 = ScadaTemplateXmlService.SanitizeNameFull("My.Tag.1", out bool ren1);
            Log($"SanitizeNameFull(\"My.Tag.1\") = \"{dot1}\" (renamed={ren1})");
            Check(dot1 == "My.Tag.1", "SanitizeNameFull: точка внутри имени сохраняется");
            Check(!ren1, "SanitizeNameFull: точка не считается заменой (renamed=false)");

            // ---- 2. Прочие спецсимволы по-прежнему заменяются на «_» ----
            string hy1 = ScadaTemplateXmlService.SanitizeNameFull("My-Tag 1", out bool ren2);
            Log($"SanitizeNameFull(\"My-Tag 1\") = \"{hy1}\" (renamed={ren2})");
            Check(hy1 == "My_Tag_1", "SanitizeNameFull: тире/пробел → «_» (без изменений)");
            Check(ren2, "SanitizeNameFull: факт замены тире/пробела фиксируется (renamed=true)");

            // ---- 3. Регресс F69: серии «__» → «_» ----
            string ser = ScadaTemplateXmlService.SanitizeNameFull("a__b");
            Log($"SanitizeNameFull(\"a__b\") = \"{ser}\"");
            Check(ser == "a_b", "SanitizeNameFull: серия «__» схлопывается в «_» (регресс F69)");

            // ---- 4. Точка + замена рядом: точка не «съедается» соседней заменой ----
            string mix = ScadaTemplateXmlService.SanitizeNameFull("A - B.C", out bool ren3);
            Log($"SanitizeNameFull(\"A - B.C\") = \"{mix}\" (renamed={ren3})");
            Check(mix == "A_B.C", "SanitizeNameFull: серия « - » → «_», точка между буквами сохранена");
            Check(ren3, "SanitizeNameFull: замена тире зафиксирована, точка — нет");

            // ---- 5. ValidateName: точка допустима ----
            Check(ScadaTemplateXmlService.ValidateName("My.Tag"),
                "ValidateName: имя с точкой — валидно");
            Check(ScadaTemplateXmlService.ValidateName("My_Tag"),
                "ValidateName: имя с «_» — валидно (регресс)");
            Check(!ScadaTemplateXmlService.ValidateName("My Tag"),
                "ValidateName: имя с пробелом — невалидно (регресс)");
            Check(!ScadaTemplateXmlService.ValidateName("My__Tag"),
                "ValidateName: «__» подряд — невалидно (регресс F69)");

            // ---- 6. TagNameFor (F75/F13): точка в марке сохраняется, ведущий «_» добавляется ----
            string tag1 = ScadaDataOutXmlService.TagNameFor("TAG.ONE", out bool renT);
            Log($"TagNameFor(\"TAG.ONE\") = \"{tag1}\" (renamed={renT})");
            Check(tag1 == "_TAG.ONE", "TagNameFor: «TAG.ONE» → «_TAG.ONE» (точка сохранена)");
            Check(!renT, "TagNameFor: точка не считается заменой");
            Check(ScadaDataOutXmlService.TagNameFor("TAG_ONE") == "_TAG_ONE",
                "TagNameFor: марка с «_» не дублирует ведущий «_» (регресс F75)");
            Check(ScadaDataOutXmlService.TagNameFor("TAG ONE") == "_TAG_ONE",
                "TagNameFor: пробел → «_» (регресс F69)");

            // ---- 7. MakeMarka (F12/F27): точка сохраняется, тире → «_», ведущий «_» ----
            string mk1 = ScadaTechObjectService.MakeMarka("1115-MHS-00101A.2", out bool renM);
            Log($"MakeMarka(\"1115-MHS-00101A.2\") = \"{mk1}\" (renamed={renM})");
            Check(mk1 == "_1115_MHS_00101A.2",
                "MakeMarka: «1115-MHS-00101A.2» → «_1115_MHS_00101A.2» (точка сохранена)");
            Check(renM, "MakeMarka: замена тире зафиксирована, точка — нет");
            Check(ScadaTechObjectService.MakeMarka("1115-MHS-00101A") == "_1115_MHS_00101A",
                "MakeMarka: марка без точки — как раньше (регресс F12)");

            // ---- 8. Регресс F69: SanitizeObjectInfo — член через первую точку не трогается ----
            string obj1 = ScadaTemplateXmlService.SanitizeObjectInfo("X.BUSY.Busy", out bool renO);
            Log($"SanitizeObjectInfo(\"X.BUSY.Busy\") = \"{obj1}\" (renamed={renO})");
            Check(obj1 == "X.BUSY.Busy", "SanitizeObjectInfo: член «.BUSY.Busy» сохранён (регресс F69)");
            Check(!renO, "SanitizeObjectInfo: точка не считается заменой");

            // ---- 9. ApplyBlockNamePattern: точка в паттерне не порождает предупреждение ----
            var pkt = new MbPacketPlan
            {
                No = 3, MasterName = "_MB_X_MAIN",
                Ip1 = 192, Ip2 = 168, Ip3 = 4, Ip4 = 10, Port = 502,
                SlaveId = 7, FuncCode = 3, Address = 10, Count = 5, TimeoutMs = 150
            };
            var w1 = new System.Collections.Generic.List<string>();
            string bn1 = ScadaTemplateXmlService.ApplyBlockNamePattern(
                "MB.1_{Seq}", pkt, "_MB_X_MAIN", w1, "имя блока");
            Log($"ApplyBlockNamePattern(\"MB.1_{{Seq}}\") = \"{bn1}\", warnings={w1.Count}");
            Check(bn1 == "MB.1_3", "ApplyBlockNamePattern: точка в имени блока сохранена");
            Check(w1.Count == 0, "ApplyBlockNamePattern: точка не даёт предупреждений «ИМЯ:»");

            // ---- 10. Краевая точка паттерна срезается как раньше (F62-поведение) ----
            var w2 = new System.Collections.Generic.List<string>();
            string bn2 = ScadaTemplateXmlService.ApplyBlockNamePattern(
                ".{Seq}", pkt, "_MB_X_MAIN", w2, "имя блока");
            Log($"ApplyBlockNamePattern(\".{{Seq}}\") = \"{bn2}\"");
            Check(bn2 == "3", "ApplyBlockNamePattern: краевая точка «.{Seq}» → «3» (как в F62)");

            // ---- 11. Регресс F69: замена спецсимволов в имени блока по паттерну ----
            var w3 = new System.Collections.Generic.List<string>();
            string bn3 = ScadaTemplateXmlService.ApplyBlockNamePattern(
                "MB-X {Seq}", pkt, "_MB_X_MAIN", w3, "имя блока");
            Log($"ApplyBlockNamePattern(\"MB-X {{Seq}}\") = \"{bn3}\", warnings={w3.Count}");
            Check(bn3 == "MB_X_3", "ApplyBlockNamePattern: тире → «_» (регресс F69)");
            Check(w3.Count == 1, "ApplyBlockNamePattern: замена тире даёт одно предупреждение «ИМЯ:»");
        }
    }
}
