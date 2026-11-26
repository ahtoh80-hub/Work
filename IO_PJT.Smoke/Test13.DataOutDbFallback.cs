using System;
using IO_PJT.Services;
using IO_PJT.ViewModels;

namespace IO_PJT.Smoke
{
    public static partial class Program
    {
        // ============ 13. F108: шаблоны только из БД/редактора (внешних путей нет) ============

        /// <summary>F108: генерация шаблонов работает только от данных БД: тело текущего
        /// шаблона = код редактора (SCADA_MB_TEMPLATE) либо именованный шаблон из
        /// SCADA_MASTER_TEMPLATES/SCADA_DATAOUT_TEMPLATES; внешние файлы и пути
        /// (TemplateFilePath/DataOutTemplateFilePath) больше не читаются и не сохраняются.</summary>
        private static void Test13_TemplatesFromDb()
        {
            Log("=== Тест 13: F108 — шаблоны только из БД/редактора (без внешних путей) ===");
            string db = NewDbPath();
            try
            {
                CreateDb(db);
                var app = new AppServices(db);

                // 1) шаблоны в БД: dataout + мастер (тела — встроенные тестовые)
                string body = BitDataTemplateXml();
                app.ModbusBlocks.SaveDataOutTemplate("TPL_FB_TEST", body, "");
                app.ModbusBlocks.SaveMasterTemplate("TPL_MST", MasterTemplateXml(), "TCP", "");
                Check(app.ModbusBlocks.LoadDataOutTemplates().Count > 0,
                    "F108: dataout-шаблон сохранён в SCADA_DATAOUT_TEMPLATES");
                Check(app.ModbusBlocks.LoadMasterTemplates().Count > 0,
                    "F108: мастер-шаблон сохранён в SCADA_MASTER_TEMPLATES");

                // 2) VM от этой БД (конструктор грузит коды из SCADA_MB_TEMPLATE — в свежей БД пусты)
                var vm = new ScadaModbusBlocksViewModel(app);
                Check(vm.DataOutTemplates.Count > 0 && vm.MasterTemplates.Count > 0,
                    "F108: перечни dataout/мастер-шаблонов подгружены из БД");

                // 3) dataout: редактор пуст → шаблон из БД по имени
                vm.Settings.DataOutTemplateName = "TPL_FB_TEST";
                vm.CheckDataOutTemplateCommand.Execute(null);
                string st = vm.DataOutTemplateStatus ?? "";
                Log($"dataout (по имени): {st}");
                Check(st.Contains("БД «TPL_FB_TEST»"),
                    "F108: пустой редактор → шаблон привязок взят из БД по имени");
                Check(!st.Contains("ОШИБКА"),
                    "F108: шаблон из БД провалидировался (эталоны разобраны)");

                // 4) приоритет кода редактора над БД
                vm.InlineDataOutText = body;
                vm.CheckDataOutTemplateCommand.Execute(null);
                string st2 = vm.DataOutTemplateStatus ?? "";
                Log($"dataout (редактор): {st2}");
                Check(st2.Contains("код редактора") && !st2.Contains("ОШИБКА"),
                    "F108: непустой код редактора имеет приоритет над шаблоном БД");

                // 5) регресс: ни имени, ни кода → понятное сообщение (внешние пути не читаются)
                vm.InlineDataOutText = "";
                vm.Settings.DataOutTemplateName = "";
                vm.CheckDataOutTemplateCommand.Execute(null);
                string st3 = vm.DataOutTemplateStatus ?? "";
                Check(st3.Contains("не выбран"),
                    "F108: без шаблона — понятное сообщение вместо чтения внешних файлов");

                // 6) мастер: пустой редактор + имя → тело из SCADA_MASTER_TEMPLATES
                vm.InlineTemplateText = "";
                vm.Settings.MasterTcpTemplateName = "TPL_MST";
                vm.CheckTemplateCommand.Execute(null);
                string stM = vm.TemplateStatus ?? "";
                Log($"мастер (по имени): {stM}");
                Check(stM.Contains("БД «TPL_MST»") && !stM.Contains("ОШИБКА"),
                    "F108: мастер-шаблон взят из БД по имени (внешние пути не используются)");

                // 7) ключи путей убраны из настроек (в ObsoleteSettingKeys → удалятся из БД)
                Check(Array.IndexOf(ModbusBlockPlannerService.SettingKeys, "TemplateFilePath") < 0 &&
                      Array.IndexOf(ModbusBlockPlannerService.SettingKeys, "DataOutTemplateFilePath") < 0 &&
                      Array.IndexOf(ModbusBlockPlannerService.SettingKeys, "TemplateMode") < 0 &&
                      Array.IndexOf(ModbusBlockPlannerService.SettingKeys, "DataOutTemplateMode") < 0,
                    "F108: ключи путей/режимов исключены из SettingKeys");
                Check(Array.IndexOf(ModbusBlockPlannerService.ObsoleteSettingKeys, "TemplateFilePath") >= 0 &&
                      Array.IndexOf(ModbusBlockPlannerService.ObsoleteSettingKeys, "DataOutTemplateFilePath") >= 0,
                    "F108: ключи путей в ObsoleteSettingKeys (вычищаются при сохранении настроек)");
            }
            finally { DeleteDb(db); }
        }
    }
}
