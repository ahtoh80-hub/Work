using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IO_PJT.Data;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.Smoke
{
    /// <summary>Смоук-тесты IO_PJT — 8 сквозных проверок основного функционала (см. ТЗ.md):
    /// 1 схема БД, 2 импорт/upsert IO_LIST + аудит, 3 импорт карты MODBUS_MAP (сравнение),
    /// 4 маски связывания, 5 связывание + адресация, 6 план блоков, 7 генерация XML,
    /// 8 объекты мастер-шаблона (F101). Внешние файлы не нужны: данные создаются в коде,
    /// БД — временная .accdb в %TEMP%. Код выхода: 0 — всё OK, 1 — есть FAIL.</summary>
    public static partial class Program
    {
        private static int _passed;
        private static int _failed;

        private static void Check(bool cond, string name)
        {
            if (cond) { _passed++; Console.WriteLine($"  OK   {name}"); }
            else { _failed++; Console.WriteLine($"  FAIL {name}"); }
        }

        private static void Log(string message)
        {
            Console.WriteLine($"  LOG  {message}");
        }

        /// <summary>Каждый тест изолирован: исключение = FAIL теста, остальные продолжают работу.</summary>
        private static void RunTest(string title, Action body)
        {
            Console.WriteLine($"== {title} ==");
            try { body(); }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine($"  FAIL исключение: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            RunTest("1. Схема БД и реестр колонок", TestSchema);
            RunTest("2. IO_LIST: upsert и аудит", TestIoListUpsert);
            RunTest("3. Импорт карты MODBUS_MAP (сравнение)", TestMapImport);
            RunTest("4. Маски связывания", TestMasks);
            RunTest("5. Связывание и адресация Modbus", TestLinkAndAddressing);
            RunTest("6. План блоков (пакеты)", TestBlockPlan);
            RunTest("7. Генерация XML мастер-блоков", TestGenerateXml);
            RunTest("8. Объекты мастер-шаблона (F101)", TestMasterObjects);
            RunTest("9. Реальный мастер-шаблон", TestRealTemplate);
            RunTest("10. БАГ: объекты мастер-шаблона с .Busy/.Error", Test10_MasterObjectsBug);
            RunTest("10.1. Простая проверка методов маппинга", Test10_MasterObjectsBugSimple);
            RunTest("11. F105: удаление непривязанных битовых объектов", Test11_UnboundBits);
            RunTest("12. F106: точка в именах не заменяется на _", Test12_DotName);
            RunTest("13. F107: dataout-шаблон из БД при недоступном пути", Test13_DataOutDbFallback);
            Console.WriteLine($"ИТОГО: OK {_passed}, FAIL {_failed}");
            return _failed == 0 ? 0 : 1;
        }

        // ===== общие помощники =====

        private static string NewDbPath() =>
            Path.Combine(Path.GetTempPath(), $"iosmoke_{Guid.NewGuid():N}.accdb");

        private static void DeleteDb(string path)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); // освободить handle ACE
            try { File.Delete(path); } catch { /* мог остаться lock */ }
        }

        /// <summary>Новая БД: служебные таблицы + IO_LIST + MODBUS_MAP.</summary>
        private static TableCreator CreateDb(string path)
        {
            var creator = new TableCreator(path);
            creator.CreateDatabase();
            creator.EnsureServiceTables();
            creator.CreateDataTable(Schema.DefaultTable);
            new ModbusMapService(path).EnsureTable();
            return creator;
        }

        private static IoPoint MakePoint(string tag, string ioType, string service) => new()
        {
            Identification = new Identification { Tag = tag, IoType = ioType, System = "РСУ" },
            Signal = new Signal { Service = service },
            Revision = new Revision { FileName = "smoke.xlsx", Sheet = "ALL" }
        };

        private const string Ip10 = "192.168.4.10(Default)\n255.255.255.0\nport 502";
        private const string Ip12 = "192.168.4.12(Default)\n255.255.255.0\nport 502";
    }
}
