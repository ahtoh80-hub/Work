using System;
using IO_PJT.Models;
using IO_PJT.Services;
using IO_PJT.ViewModels;

namespace IO_PJT.Smoke
{
    public static partial class Program
    {
        // ============ 13. F107: dataout-шаблон из БД при недоступном пути файла ============

        /// <summary>Смоук кейса переноса БД между компьютерами: настройки DataOutTemplateMode=File
        /// хранят путь файла старого компьютера; файл не найден → шаблон привязок должен
        /// подхватываться из SCADA_DATAOUT_TEMPLATES по DataOutTemplateName (fallback F107,
        /// аналог F88 мастер-шаблона), иначе dataTplValid=false и экспорт уходит в «простые
        /// привязки» без data-цепочек по Typno.</summary>
        private static void Test13_DataOutDbFallback()
        {
            Log("=== Тест 13: F107 — недоступный путь dataout-шаблона → шаблон из БД ===");
            string db = NewDbPath();
            try
            {
                CreateDb(db); // служебные таблицы (SCADA_*) + IO_LIST + MODBUS_MAP
                var app = new AppServices(db);

                // 1) сохранить валидный dataout-шаблон в БД (тело — мини-шаблон Test11)
                string body = BitDataTemplateXml();
                app.ModbusBlocks.SaveDataOutTemplate("TPL_FB_TEST", body, "");
                Check(app.ModbusBlocks.LoadDataOutTemplates().Count > 0,
                    "F107: шаблон сохранён в SCADA_DATAOUT_TEMPLATES");

                // 2) VM окна F13 от этой БД (конструктор загружает списки, в т.ч. шаблоны)
                var vm = new ScadaModbusBlocksViewModel(app);
                Check(vm.DataOutTemplates.Count > 0,
                    "F107: VM подгрузила перечень dataout-шаблонов из БД");

                // 3) режим «Файл» + НЕСУЩЕСТВУЮЩИЙ путь (перенос БД на другой компьютер)
                vm.Settings.DataOutTemplateMode = MbTemplateMode.File;
                vm.Settings.DataOutTemplateFilePath = @"C:\_no_such_dir_f107\MB_DI_TO_LOOP.xml";
                vm.Settings.DataOutTemplateName = "TPL_FB_TEST";
                vm.CheckDataOutTemplateCommand.Execute(null);

                string st = vm.DataOutTemplateStatus ?? "";
                Log($"статус (нет файла): {st}");
                Check(st.Contains("БД «TPL_FB_TEST»"),
                    "F107: файл не найден — шаблон подхвачен из БД по имени");
                Check(!st.Contains("файл по пути не найден"),
                    "F107: недоступный путь больше не роняет шаблон");
                Check(!st.Contains("ОШИБКА"),
                    "F107: шаблон из БД провалидировался (без ошибки парсинга)");

                // 4) регресс: существующий файл читается из файла, fallback не срабатывает
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "f107_tpl_" + Guid.NewGuid().ToString("N") + ".xml");
                System.IO.File.WriteAllText(tmp, body);
                try
                {
                    vm.Settings.DataOutTemplateFilePath = tmp;
                    vm.CheckDataOutTemplateCommand.Execute(null);
                    string st2 = vm.DataOutTemplateStatus ?? "";
                    Log($"статус (файл есть): {st2}");
                    Check(st2.Contains(tmp),
                        "регресс F13.1: существующий файл шаблона читается из файла");
                    Check(!st2.Contains("БД «"),
                        "регресс F13.1: при наличии файла fallback в БД не применяется");
                }
                finally { try { System.IO.File.Delete(tmp); } catch { } }
            }
            finally { DeleteDb(db); }
        }
    }
}
