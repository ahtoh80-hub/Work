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
        private static void Test10_MasterObjectsBug()
        {
            Log("=== Тест 10: Проверка бага с master объекта (NAME_PACK_TCP_MAINE.Busy) ===");
            
            // Используем относительный путь
            string path = @"Work\_Шаблоны\ModbusTCP_reserv_v0.xml";
            if (!System.IO.File.Exists(path))
            {
                Log($"Файл не найден: {System.IO.Path.GetFullPath(path)}");
                Check(false, "эталонный мастер-шаблон найден");
                return;
            }
            
            string tpl = System.IO.File.ReadAllText(path);
            var svc = new ScadaTemplateXmlService();
            
            // Проверяем ContainsMasterToken
            string mainToken = "NAME_PACK_TCP_MAINE";
            string reserveToken = "NAME_PACK_TCP_RESERVE";
            
            // Тестируем разные варианты
            var testCases = new[]
            {
                "NAME_PACK_TCP_MAINE.Busy",
                "NAME_PACK_TCP_MAINE.BUSY",
                "NAME_PACK_TCP_MAINE.Error",
                "NAME_PACK_TCP_MAINE_LINK",
                "NAME_PACK_TCP_RESERVE.Busy",
                "NAME_PACK_TCP_RESERVE_LINK"
            };
            
            foreach (var test in testCases)
            {
                bool contains = ScadaTemplateXmlService.ContainsMasterToken(test, mainToken, reserveToken);
                Log($"ContainsMasterToken('{test}'): {contains}");
                
                // Проверяем SubstituteMasterTokens
                string mainName = "_MB_IMCS_EBSM102_D016_HOLDING_RES_MAIN";
                string reserveName = "_MB_IMCS_EBSM102_D016_HOLDING_RES_RESERVE";
                string result = ScadaTemplateXmlService.SubstituteMasterTokens(test, mainToken, reserveToken, mainName, reserveName);
                Log($"SubstituteMasterTokens('{test}'): {result}");
                
                // Проверяем, что нет двойных окончаний
                if (test.Contains(".Busy") && !test.Contains(".BUSY"))
                {
                    Check(!result.Contains(".BUSY."), $"Результат '{result}' не содержит '.BUSY.' (двойное окончание)");
                }
            }
            
            // Проверяем ParseMasterTemplateObjects
            var objs = svc.ParseMasterTemplateObjects(tpl);
            Log($"ParseMasterTemplateObjects найдено объектов: {objs.Count}");
            
            foreach (var obj in objs.Where(o => o.Name.Contains("NAME_PACK_TCP_MAINE")))
            {
                Log($"Объект: {obj.Name}, окончание: '{obj.DefaultEnding}'");
            }
            
            // Проверяем генерацию с masterObjVars
            var packets = MasterPackets();
            var settings = new ScadaMbGenSettings();
            
            // Тест 1: Без маппинга
            var result1 = svc.Write(packets, tpl, settings);
            Check(!result1.PousXml.Contains("NAME_PACK_TCP_MAINE"), "Без маппинга: токены заменены");
            Check(!result1.PousXml.Contains("NAME_PACK_TCP_RESERVE"), "Без маппинга: токены RESERVE заменены");
            
            // Проверяем, что есть корректные имена
            bool hasCorrectBusy = result1.PousXml.Contains("_MB_IMCS_EBSM102_D016_HOLDING_RES_MAIN.Busy") ||
                                 result1.PousXml.Contains("_MB_IMCS_EBSM102_D016_HOLDING_RES_MAIN.BUSY");
            bool hasIncorrectBusy = result1.PousXml.Contains(".BUSY.Busy") ||
                                   result1.PousXml.Contains("BUSY.Busy");
            
            Log($"Результат содержит корректное .Busy: {hasCorrectBusy}");
            Log($"Результат содержит некорректное .BUSY.Busy: {hasIncorrectBusy}");
            
            if (hasIncorrectBusy)
            {
                Log("ОШИБКА: Обнаружено некорректное имя с .BUSY.Busy!");
            }
            
            // Тест 2: С маппингом (окончание из маппинга)
            var masterObjVars = new Dictionary<string, (string NameSuffix, string IoColumn, string Literal)>
            {
                ["NAME_PACK_TCP_MAINE.Busy"] = (".MyBusy", "", ""),
                ["NAME_PACK_TCP_RESERVE.Busy"] = (".ResBusy", "", "")
            };
            
            var result2 = svc.Write(packets, tpl, settings, masterObjVars: masterObjVars);
            
            // Проверяем что окончание из маппинга применено
            bool hasCustomBusy = result2.PousXml.Contains(".MyBusy");
            bool hasCustomResBusy = result2.PousXml.Contains(".ResBusy");
            
            Log($"С маппингом: есть .MyBusy: {hasCustomBusy}");
            Log($"С маппингом: есть .ResBusy: {hasCustomResBusy}");
            
            Check(hasCustomBusy, "Окончание из маппинга применено (основной мастер)");
        }
    }
}