using System;
using System.Collections.Generic;
using IO_PJT.Services;

namespace IO_PJT.Smoke
{
    public static partial class Program
    {
        private static void Test10_MasterObjectsBugSimple()
        {
            Log("=== Тест 10.1: Простой тест методов SubstituteMasterTokens и ContainsMasterToken ===");
            
            var svc = new ScadaTemplateXmlService();
            
            string mainToken = "NAME_PACK_TCP_MAINE";
            string reserveToken = "NAME_PACK_TCP_RESERVE";
            string mainName = "_MB_IMCS_EBSM102_D016_HOLDING_RES_MAIN";
            string reserveName = "_MB_IMCS_EBSM102_D016_HOLDING_RES_RESERVE";
            
            // Тест 1: Проверка ContainsMasterToken
            var testCases = new[]
            {
                "NAME_PACK_TCP_MAINE.Busy",
                "NAME_PACK_TCP_MAINE.Error",
                "NAME_PACK_TCP_RESERVE.Busy",
                "some.other.name",
                "NAME_PACK_TCP_MAINE_LINK",
                "TESTNAME_PACK_TCP_MAINE", // должно не совпадать
            };
            
            foreach (var test in testCases)
            {
                bool result = ScadaTemplateXmlService.ContainsMasterToken(test, mainToken, reserveToken);
                Log($"ContainsMasterToken('{test}'): {result}");
            }
            
            // Тест 2: Проверка SubstituteMasterTokens
            foreach (var test in testCases.Where(t => ScadaTemplateXmlService.ContainsMasterToken(t, mainToken, reserveToken)))
            {
                string result = ScadaTemplateXmlService.SubstituteMasterTokens(test, mainToken, reserveToken, mainName, reserveName);
                Log($"SubstituteMasterTokens('{test}'): {result}");
                
                // Проверка корректности
                if (test.Contains("NAME_PACK_TCP_MAINE") && !test.Contains("NAME_PACK_TCP_RESERVE"))
                {
                    Check(!result.Contains("NAME_PACK_TCP_MAINE"), $"'{result}' не содержит исходный токен");
                    Check(result.Contains(mainName), $"'{result}' содержит правильное имя мастера '{mainName}'");
                }
            }
            
            // Тест 3: Проверка нового метода ApplyMasterObjectSuffix (F102)
            var masterObjVars = new Dictionary<string, (string NameSuffix, string IoColumn, string Literal)>
            {
                ["NAME_PACK_TCP_MAINE.Busy"] = (".MyBusy", "", ""),
                ["NAME_PACK_TCP_MAINE.Error"] = (".MyError", "", ""),
                ["NAME_PACK_TCP_RESERVE.Busy"] = (".ResBusy", "", "")
            };
            
            string testName = "NAME_PACK_TCP_MAINE.Busy";
            string substituted = ScadaTemplateXmlService.SubstituteMasterTokens(testName, mainToken, reserveToken, mainName, reserveName);
            string withSuffix = ScadaTemplateXmlService.ApplyMasterObjectSuffix(substituted, mainToken, reserveToken, masterObjVars);
            
            Log($"Исходное: '{testName}'");
            Log($"После SubstituteMasterTokens: '{substituted}'");
            Log($"После ApplyMasterObjectSuffix: '{withSuffix}'");
            
            // Проверка что результат должен быть таким же (в masterObjVars нет маппинга для substituted)
            // Но если добавить маппинг для substituted, он должен сработать
            
            // Добавляем маппинг для уже замененного имени
            masterObjVars[substituted] = (".CustomBusy", "", "");
            string withSuffix2 = ScadaTemplateXmlService.ApplyMasterObjectSuffix(substituted, mainToken, reserveToken, masterObjVars);
            Log($"После добавления маппинга для '{substituted}': '{withSuffix2}'");
        }
    }
}