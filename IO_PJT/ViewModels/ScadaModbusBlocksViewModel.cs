using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.ViewModels
{
    /// <summary>F13: окно «SCADA → Генерация блоков Modbus (XML)» — XML-шаблон (файл или
    /// вставленный код), планирование пакетов из точек с адресацией Modbus (M_PlcTag),
    /// превью пакетов и привязок «MB_TCP_MASTER_4_1.DataOut[i]», запись привязок в IO_LIST
    /// (M_BusBlock/M_BusDataOut) и экспорт BufScadaPOUS + файла привязок. Настройки —
    /// SCADA_MB_SETTINGS, правила «станция → SlaveId» — SCADA_MB_SLAVE_RULES,
    /// вставленный код — SCADA_MB_TEMPLATE (MEMO).</summary>
    public sealed class ScadaModbusBlocksViewModel : ViewModelBase
    {
        private readonly AppServices _s;
        private readonly ScadaTemplateXmlService _xml = new();

        /// <summary>Редактируемые настройки; персистятся SaveSettingsCommand.</summary>
        public ScadaMbGenSettings Settings { get; }

        /// <summary>Правила «станция → SlaveId» (редактируются в гриде).</summary>
        public ObservableCollection<MbSlaveIdRule> SlaveRules { get; } = new();

        /// <summary>F14: сопоставление «Typno → эталон шаблона привязок» (SCADA_TYPNO_MAP).</summary>
        public ObservableCollection<MbTypnoMapRule> TypnoRules { get; } = new();

        /// <summary>F16: сохранённые именованные шаблоны привязок (SCADA_DATAOUT_TEMPLATES).</summary>
        public ObservableCollection<MbDataOutTemplate> DataOutTemplates { get; } = new();

        /// <summary>F16: имена сохранённых шаблонов — список для колонки
        /// «Шаблон привязок» в гриде Typno.</summary>
        public ObservableCollection<string> DataOutTemplateNames { get; } = new();

        /// <summary>F33 (этап 6): список значений размера для колонки «Размер» грида Typno
        /// (бит/8/16/32/64bit; пустое значение — авто по M_DataType, вводится вручную).</summary>
        public string[] TypnoSizes { get; } =
            MbTypnoMapRule.ValidSizes.Where(v => v.Length > 0).ToArray();

        private MbDataOutTemplate? _selectedDataOutTemplate;
        /// <summary>F16: выбранный шаблон в списке «Сохранённые шаблоны».</summary>
        public MbDataOutTemplate? SelectedDataOutTemplate
        {
            get => _selectedDataOutTemplate;
            set => Set(ref _selectedDataOutTemplate, value, nameof(SelectedDataOutTemplate));
        }

        // —— F22: залитые шаблоны: мастер-блоки (TCP/RTU) и привязки DataIn ——

        /// <summary>F22: все залитые мастер-шаблоны блоков (SCADA_MASTER_TEMPLATES).</summary>
        public ObservableCollection<MbMasterTemplate> MasterTemplates { get; } = new();

        private MbMasterTemplate? _selectedMasterTemplate;
        /// <summary>F22: выбранный мастер-шаблон в списке.</summary>
        public MbMasterTemplate? SelectedMasterTemplate
        {
            get => _selectedMasterTemplate;
            set => Set(ref _selectedMasterTemplate, value, nameof(SelectedMasterTemplate));
        }

        /// <summary>F22: все залитые шаблоны привязок DataIn (SCADA_DATAIN_TEMPLATES).</summary>
        public ObservableCollection<MbDataInTemplate> DataInTemplates { get; } = new();

        private MbDataInTemplate? _selectedDataInTemplate;
        /// <summary>F22: выбранный шаблон DataIn в списке.</summary>
        public MbDataInTemplate? SelectedDataInTemplate
        {
            get => _selectedDataInTemplate;
            set => Set(ref _selectedDataInTemplate, value, nameof(SelectedDataInTemplate));
        }

        /// <summary>F33 (этап 7): маппинг объектов data-шаблонов — «шаблон | направление |
        /// объект | колонка/шаблон | литерал» (SCADA_TEMPLATE_VARS scope dataout/datain).
        /// F34: маппинг входов мастер-шаблона (F23/F25, scope=master) из UI убран — входы
        /// заполняются автоматически из плана.</summary>
        public ObservableCollection<MbDataTemplateVarRow> DataTemplateVars { get; } = new();

        /// <summary>Превью пакетов (по одному блоку мастера на строку).</summary>
        public ObservableCollection<MbPacketPlan> Preview { get; } = new();

        /// <summary>Превью привязок точек к выходам DataOut блоков.</summary>
        public ObservableCollection<MbBinding> BindingsPreview { get; } = new();

        /// <summary>F28: фильтр станций (галочки) — какие станции генерить при экспорте XML
        /// и записи привязок; заполняется в Build() по станциям плана (все выбраны).</summary>
        public ObservableCollection<MbStationFilterItem> StationFilters { get; } = new();

        private string _stationsHint = "";
        /// <summary>F28: подсказка выбора станций («генерить станций: X из Y»).</summary>
        public string StationsHint
        {
            get => _stationsHint;
            set => Set(ref _stationsHint, value, nameof(StationsHint));
        }

        /// <summary>F28: выбрать все станции.</summary>
        public RelayCommand SelectAllStationsCommand { get; }

        /// <summary>F28: снять все станции.</summary>
        public RelayCommand ClearAllStationsCommand { get; }

        /// <summary>Последний разобранный шаблон (валидация + имя мастера).</summary>
        private ScadaTemplateXmlService.TemplateInfo? _template;

        /// <summary>Последний текст шаблона (для генерации при экспорте).</summary>
        private string? _templateText;

        /// <summary>F13.1: сервис и последний разобранный шаблон привязок Data_Out.</summary>
        private readonly ScadaDataOutXmlService _dataOut = new();
        private ScadaDataOutXmlService.DataOutTemplateInfo? _dataOutTemplate;

        /// <summary>F13.1: последний текст шаблона привязок (для генерации при экспорте).</summary>
        private string? _dataOutTemplateText;

        /// <summary>Подсказка по источникам данных.</summary>
        public string FieldsHint =>
            "Источник — точки текущей таблицы с адресацией Modbus (M_PlcTag). Шаблон задаёт структуру одного запроса " +
            "(мастер MB_TCP_MASTER + обвязка); генератор размножает его по пакетам, заменяя SlaveId/Mode/Address/Count/таймаут/IP. " +
            "Привязки (M_BusBlock/M_BusDataOut) пишутся кнопкой «Записать привязки в IO_LIST».";

        private string _statusText = "Загрузите шаблон, настройте пакетирование и нажмите «Построить превью»";
        public string StatusText { get => _statusText; set => Set(ref _statusText, value, nameof(StatusText)); }

        private string _statsText = "";
        public string StatsText { get => _statsText; set => Set(ref _statsText, value, nameof(StatsText)); }

        private string _templateStatus = "Шаблон не загружен";
        public string TemplateStatus { get => _templateStatus; set => Set(ref _templateStatus, value, nameof(TemplateStatus)); }

        private string _inlineTemplateText = "";
        /// <summary>Вставленный код шаблона (режим «Код», хранится в SCADA_MB_TEMPLATE).</summary>
        public string InlineTemplateText
        {
            get => _inlineTemplateText;
            set => Set(ref _inlineTemplateText, value, nameof(InlineTemplateText));
        }

        // —— режим источника шаблона (радиокнопки Файл/Код) ——
        private bool _modeFile = true;
        public bool ModeFile
        {
            get => _modeFile;
            set
            {
                if (Set(ref _modeFile, value, nameof(ModeFile)))
                {
                    Settings.TemplateMode = value ? MbTemplateMode.File : MbTemplateMode.Inline;
                    Raise(nameof(ModeInline));
                }
            }
        }

        public bool ModeInline
        {
            get => !_modeFile;
            set { if (value) ModeFile = false; }
        }

        // —— F13.1: шаблон привязок Data_Out (отдельная группа Файл/Код) ——
        private string _dataOutTemplateStatus = "Шаблон привязок не загружен";
        /// <summary>F13.1: статус шаблона привязок (эталоны BOOL/INT/… или текст ошибки).</summary>
        public string DataOutTemplateStatus
        {
            get => _dataOutTemplateStatus;
            set => Set(ref _dataOutTemplateStatus, value, nameof(DataOutTemplateStatus));
        }

        private string _inlineDataOutText = "";
        /// <summary>F13.1: вставленный код шаблона привязок (SCADA_MB_TEMPLATE, строка «dataout»).</summary>
        public string InlineDataOutText
        {
            get => _inlineDataOutText;
            set => Set(ref _inlineDataOutText, value, nameof(InlineDataOutText));
        }

        private bool _modeDataOutFile = true;
        /// <summary>F13.1: режим источника шаблона привязок (Файл/Код).</summary>
        public bool ModeDataOutFile
        {
            get => _modeDataOutFile;
            set
            {
                if (Set(ref _modeDataOutFile, value, nameof(ModeDataOutFile)))
                {
                    Settings.DataOutTemplateMode = value ? MbTemplateMode.File : MbTemplateMode.Inline;
                    Raise(nameof(ModeDataOutInline));
                }
            }
        }

        public bool ModeDataOutInline
        {
            get => !_modeDataOutFile;
            set { if (value) ModeDataOutFile = false; }
        }

        /// <summary>Последний построенный результат (для экспорта/записи привязок).</summary>
        private MbPlanResult? _built;

        /// <summary>Индекс политики пакетирования для ComboBox (Settings.Policy).</summary>
        private int _policyIndex;
        public int PolicyIndex
        {
            get => _policyIndex;
            set
            {
                if (Set(ref _policyIndex, value, nameof(PolicyIndex)))
                    Settings.Policy = (MbPacketPolicy)Math.Clamp(value, 0, 3);
            }
        }

        public RelayCommand BuildCommand { get; }
        public RelayCommand ExportCommand { get; }
        public RelayCommand WriteBindingsCommand { get; }
        public RelayCommand CheckTemplateCommand { get; }
        public RelayCommand BrowseTemplateCommand { get; }
        public RelayCommand CheckDataOutTemplateCommand { get; }
        public RelayCommand BrowseDataOutTemplateCommand { get; }
        public RelayCommand SaveSettingsCommand { get; }
        public RelayCommand SaveRulesCommand { get; }
        public RelayCommand AddRuleCommand { get; }
        public RelayCommand DeleteRuleCommand { get; }
        public RelayCommand AddTypnoRuleCommand { get; }
        public RelayCommand DeleteTypnoRuleCommand { get; }
        public RelayCommand SaveTypnoMapCommand { get; }
        public RelayCommand FillTypnoRulesCommand { get; }
        /// <summary>F21: автосбор станций slave из IO_LIST («Заполнить из IO_LIST»).</summary>
        public RelayCommand FillSlaveRulesCommand { get; }
        /// <summary>F22: CRUD мастер-шаблонов (SCADA_MASTER_TEMPLATES).</summary>
        public RelayCommand SaveMasterTemplateAsCommand { get; }
        public RelayCommand LoadMasterTemplateCommand { get; }
        public RelayCommand DeleteMasterTemplateCommand { get; }
        /// <summary>F22: загрузка/удаление шаблонов DataIn (SCADA_DATAIN_TEMPLATES).</summary>
        public RelayCommand AddDataInFromFileCommand { get; }
        public RelayCommand DeleteDataInTemplateCommand { get; }
        /// <summary>F31: объединённый перечень data-шаблонов (чтение+запись) — «второе место»
        /// окна по ТЗ; шаблоны пакетов (Master) живут отдельно (MasterTemplates).</summary>
        public ObservableCollection<MbDataTemplateRow> DataTemplates { get; } = new();
        private MbDataTemplateRow? _selectedDataTemplate;
        public MbDataTemplateRow? SelectedDataTemplate
        {
            get => _selectedDataTemplate;
            set => Set(ref _selectedDataTemplate, value, nameof(SelectedDataTemplate));
        }
        /// <summary>F31: «Открыть шаблон из файла…» — авто-тип (DataIn[ → запись), сохранение в БД.</summary>
        public RelayCommand OpenDataTemplateCommand { get; }
        /// <summary>F31: «В редактор» — тело data-шаблона чтения в редактор привязок.</summary>
        public RelayCommand LoadDataTemplateCommand { get; }
        /// <summary>F31: «Удалить» — по типу выбранной строки (SCADA_DATAOUT_/SCADA_DATAIN_).</summary>
        public RelayCommand DeleteDataTemplateCommand { get; }
        /// <summary>F31: «Из файла…» — мастер-шаблон пакетов из XML-файла в SCADA_MASTER_TEMPLATES.</summary>
        public RelayCommand OpenMasterTemplateCommand { get; }
        /// <summary>F33 (этап 7): перечень объектов-переменных data-шаблонов и их маппинг.</summary>
        public RelayCommand FillDataTemplateVarsCommand { get; }
        public RelayCommand SaveDataTemplateVarsCommand { get; }

        /// <summary>F16: сохранить текущий текст шаблона привязок под именем (имя спрашивается).</summary>
        public RelayCommand SaveDataOutTemplateAsCommand { get; }

        /// <summary>F16: загрузить выбранный именованный шаблон в редактор.</summary>
        public RelayCommand LoadDataOutTemplateCommand { get; }

        /// <summary>F16: перечитать Body выбранного шаблона из его файла-источника.</summary>
        public RelayCommand UpdateDataOutTemplateFileCommand { get; }

        /// <summary>F16: удалить выбранный именованный шаблон.</summary>
        public RelayCommand DeleteDataOutTemplateCommand { get; }

        private MbSlaveIdRule? _selectedRule;
        public MbSlaveIdRule? SelectedRule
        {
            get => _selectedRule;
            set => Set(ref _selectedRule, value, nameof(SelectedRule));
        }

        private MbTypnoMapRule? _selectedTypnoRule;
        /// <summary>F14: выбранное правило «Typno → эталон» в гриде.</summary>
        public MbTypnoMapRule? SelectedTypnoRule
        {
            get => _selectedTypnoRule;
            set => Set(ref _selectedTypnoRule, value, nameof(SelectedTypnoRule));
        }

        public ScadaModbusBlocksViewModel(AppServices services)
        {
            _s = services;
            Settings = _s.ModbusBlocks.LoadSettings();
            _policyIndex = (int)Settings.Policy;
            _modeFile = Settings.TemplateMode == MbTemplateMode.File;
            _modeDataOutFile = Settings.DataOutTemplateMode == MbTemplateMode.File;
            BuildCommand = new RelayCommand(Build);
            ExportCommand = new RelayCommand(Export, () => Preview.Count > 0);
            WriteBindingsCommand = new RelayCommand(WriteBindingsToDb, () => Preview.Count > 0);
            CheckTemplateCommand = new RelayCommand(CheckTemplate);
            BrowseTemplateCommand = new RelayCommand(BrowseTemplate);
            CheckDataOutTemplateCommand = new RelayCommand(CheckDataOutTemplate);
            BrowseDataOutTemplateCommand = new RelayCommand(BrowseDataOutTemplate);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            SaveRulesCommand = new RelayCommand(SaveRules);
            AddRuleCommand = new RelayCommand(AddRule);
            DeleteRuleCommand = new RelayCommand(DeleteRule, () => SelectedRule != null);
            AddTypnoRuleCommand = new RelayCommand(AddTypnoRule);
            DeleteTypnoRuleCommand = new RelayCommand(DeleteTypnoRule, () => SelectedTypnoRule != null);
            SaveTypnoMapCommand = new RelayCommand(SaveTypnoMap);
            FillTypnoRulesCommand = new RelayCommand(FillTypnoRules);
            FillSlaveRulesCommand = new RelayCommand(FillSlaveRules);
            SaveMasterTemplateAsCommand = new RelayCommand(SaveMasterTemplateAs);
            LoadMasterTemplateCommand = new RelayCommand(LoadMasterTemplateIntoEditor);
            DeleteMasterTemplateCommand = new RelayCommand(DeleteMasterTemplate,
                () => SelectedMasterTemplate != null);
            AddDataInFromFileCommand = new RelayCommand(AddDataInFromFile);
            DeleteDataInTemplateCommand = new RelayCommand(DeleteDataInTemplate,
                () => SelectedDataInTemplate != null);
            OpenDataTemplateCommand = new RelayCommand(OpenDataTemplate);            // F31
            LoadDataTemplateCommand = new RelayCommand(LoadDataTemplate,
                () => SelectedDataTemplate != null);                                 // F31
            DeleteDataTemplateCommand = new RelayCommand(DeleteDataTemplate,
                () => SelectedDataTemplate != null);                                 // F31
            OpenMasterTemplateCommand = new RelayCommand(OpenMasterTemplate);         // F31
            // F23/F25: команды маппинга мастер-шаблона убраны (F34) — входы заполняются из плана
            FillDataTemplateVarsCommand = new RelayCommand(FillDataTemplateVars);  // F33
            SaveDataTemplateVarsCommand = new RelayCommand(SaveDataTemplateVars);  // F33
            SelectAllStationsCommand = new RelayCommand(() => SetAllStations(true));   // F28
            ClearAllStationsCommand = new RelayCommand(() => SetAllStations(false));   // F28
            SaveDataOutTemplateAsCommand = new RelayCommand(SaveDataOutTemplateAs);
            LoadDataOutTemplateCommand = new RelayCommand(LoadDataOutTemplateIntoEditor);
            UpdateDataOutTemplateFileCommand = new RelayCommand(UpdateDataOutTemplateFromFile);
            DeleteDataOutTemplateCommand = new RelayCommand(DeleteDataOutTemplate);
            LoadRules();
            LoadTypnoRules();
            LoadDataOutTemplateList(); // F16: список именованных шаблонов привязок
            LoadMasterTemplateList();  // F22: список мастер-шаблонов (TCP/RTU)
            LoadDataInTemplateList();  // F22: список шаблонов привязок DataIn
            LoadDataTemplateVars();    // F33 (этап 7): маппинг объектов data-шаблонов
            if (Settings.TemplateMode == MbTemplateMode.Inline)
            {
                try { InlineTemplateText = _s.ModbusBlocks.LoadInlineTemplate() ?? ""; }
                catch (Exception) { InlineTemplateText = ""; }
            }
            if (Settings.DataOutTemplateMode == MbTemplateMode.Inline)
            {
                try { InlineDataOutText = _s.ModbusBlocks.LoadInlineTemplate(
                    ModbusBlockPlannerService.InlineDataOutTemplateName) ?? ""; }
                catch (Exception) { InlineDataOutText = ""; }
            }
            CheckTemplate(); // автозагрузка шаблона при открытии окна
            CheckDataOutTemplate(); // F13.1: автозагрузка шаблона привязок
            StatusText = $"Настройки загружены. Правил «станция → SlaveId»: {SlaveRules.Count}. " +
                         "Постройте превью для просмотра пакетов.";
        }

        /// <summary>Прочитать правила SlaveId из SCADA_MB_SLAVE_RULES.</summary>
        private void LoadRules()
        {
            try
            {
                SlaveRules.Clear();
                foreach (var r in _s.ModbusBlocks.LoadSlaveRules()) SlaveRules.Add(r);
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F13: ошибка чтения SCADA_MB_SLAVE_RULES: {msg}");
                StatusText = $"Ошибка чтения правил SlaveId: {msg}";
            }
        }
        // ================== Шаблон ==================

        /// <summary>F13/F18: прочитать текст шаблона по режиму: Inline — вставленный код,
        /// File — путь настройки (иначе авто-поиск MB_TCP_MASTER_пример_new.xml рядом с exe
        /// или в Work\). F18: встроенных копий примеров больше нет — без файла/кода шаблон
        /// не найден.</summary>
        private (string? Text, string Source) LoadTemplateText()
        {
            if (Settings.TemplateMode == MbTemplateMode.Inline)
            {
                string t = (InlineTemplateText ?? "").Trim();
                return t.Length > 0 ? (t, "вставленный код") : (null, "вставленный код (пусто)");
            }
            if (!string.IsNullOrWhiteSpace(Settings.TemplateFilePath))
            {
                if (File.Exists(Settings.TemplateFilePath))
                    return (File.ReadAllText(Settings.TemplateFilePath), Settings.TemplateFilePath);
                return (null, $"файл по пути не найден: {Settings.TemplateFilePath}");
            }
            string? path = ScadaTemplateXmlService.ResolveDefaultTemplatePath();
            return path != null
                ? (File.ReadAllText(path), path)
                : (null, "шаблон не найден — укажите файл или вставьте код");
        }

        /// <summary>F13: загрузить и провалидировать шаблон (кнопка «Проверить шаблон»).</summary>
        private void CheckTemplate()
        {
            try
            {
                var (text, source) = LoadTemplateText();
                _templateText = text;
                if (text == null)
                {
                    _template = null;
                    TemplateStatus = $"Шаблон: {source}";
                    StatusText = $"Шаблон не загружен: {source}.";
                    return;
                }
                _template = _xml.Parse(text);
                TemplateStatus = _template.Valid
                    ? $"Шаблон ({source}): {_template.Summary}"
                    : $"Шаблон ({source}): ОШИБКА — {_template.Error}";
                StatusText = _template.Valid
                    ? "Шаблон загружен. Нажмите «Построить превью»."
                    : $"Ошибка шаблона: {_template.Error}";
            }
            catch (Exception ex)
            {
                _template = null;
                TemplateStatus = $"Ошибка чтения шаблона: {ex.Message}";
                StatusText = TemplateStatus;
            }
        }

        /// <summary>F13: выбрать файл шаблона (диалог) и переключить режим на «Файл».</summary>
        private void BrowseTemplate()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "XML шаблон (*.xml)|*.xml|Все файлы (*.*)|*.*",
                FileName = Settings.TemplateFilePath
            };
            if (dlg.ShowDialog() != true) return;
            ModeFile = true;
            Settings.TemplateFilePath = dlg.FileName;
            CheckTemplate();
        }

        // ================== F13.1: шаблон привязок Data_Out ==================

        /// <summary>F13.1/F18: прочитать текст шаблона привязок по режиму: Inline — вставленный
        /// код, File — путь настройки (иначе авто-поиск Data_Out_пример.xml рядом с exe или
        /// в Work\). F18: встроенных копий примеров больше нет — без файла/кода шаблон
        /// не найден.</summary>
        private (string? Text, string Source) LoadDataOutTemplateText()
        {
            if (Settings.DataOutTemplateMode == MbTemplateMode.Inline)
            {
                string t = (InlineDataOutText ?? "").Trim();
                return t.Length > 0 ? (t, "вставленный код") : (null, "вставленный код (пусто)");
            }
            if (!string.IsNullOrWhiteSpace(Settings.DataOutTemplateFilePath))
            {
                if (File.Exists(Settings.DataOutTemplateFilePath))
                    return (File.ReadAllText(Settings.DataOutTemplateFilePath), Settings.DataOutTemplateFilePath);
                return (null, $"файл по пути не найден: {Settings.DataOutTemplateFilePath}");
            }
            string? path = ScadaDataOutXmlService.ResolveDefaultTemplatePath();
            return path != null
                ? (File.ReadAllText(path), path)
                : (null, "шаблон привязок не найден — укажите файл или вставьте код");
        }

        /// <summary>F13.1: загрузить и провалидировать шаблон привязок (кнопка «Проверить»).</summary>
        private void CheckDataOutTemplate()
        {
            try
            {
                var (text, source) = LoadDataOutTemplateText();
                _dataOutTemplateText = text;
                if (text == null)
                {
                    _dataOutTemplate = null;
                    DataOutTemplateStatus = $"Шаблон привязок: {source}";
                    return;
                }
                _dataOutTemplate = _dataOut.Parse(text);
                DataOutTemplateStatus = _dataOutTemplate.Valid
                    ? $"Шаблон привязок ({source}): {_dataOutTemplate.Summary}"
                    : $"Шаблон привязок ({source}): ОШИБКА — {_dataOutTemplate.Error}";
            }
            catch (Exception ex)
            {
                _dataOutTemplate = null;
                DataOutTemplateStatus = $"Ошибка чтения шаблона привязок: {ex.Message}";
            }
        }

        /// <summary>F13.1: выбрать файл шаблона привязок (диалог) и переключить режим на «Файл».</summary>
        private void BrowseDataOutTemplate()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "XML шаблон привязок (*.xml)|*.xml|Все файлы (*.*)|*.*",
                FileName = Settings.DataOutTemplateFilePath
            };
            if (dlg.ShowDialog() != true) return;
            ModeDataOutFile = true;
            Settings.DataOutTemplateFilePath = dlg.FileName;
            // F16: имя шаблона подсказывается именем файла (пустое поле не затирается)
            if (string.IsNullOrWhiteSpace(Settings.DataOutTemplateName))
                Settings.DataOutTemplateName = Path.GetFileNameWithoutExtension(dlg.FileName);
            CheckDataOutTemplate();
        }

        // ================== F16: именованные шаблоны привязок ==================

        /// <summary>F16: перечитать список сохранённых шаблонов (SCADA_DATAOUT_TEMPLATES)
        /// и список имён для колонки «Шаблон привязок» грида Typno.
        /// F26.1 (баг-fix): без Clear() — поштучная синхронизация. Reset-событие
        /// ObservableCollection у DataOutTemplateNames сбрасывает ItemsSource редактируемого
        /// ComboBox в гриде Typno; ComboBox затирал Text и TwoWay-привязкой обнулял
        /// MbTypnoMapRule.Template у всех правил — «Сохранить настройки» «слетало» привязки.</summary>
        private void LoadDataOutTemplateList()
        {
            try
            {
                var loaded = _s.ModbusBlocks.LoadDataOutTemplates();

                // 1) объектный список (ComboBox «сохранённые шаблоны»): удалить отсутствующие,
                //    обновить Body/путь у существующих (сопоставление по имени, IgnoreCase —
                //    как уникальность в БД), добавить новые. SelectedItem не сбрасывается.
                foreach (var gone in DataOutTemplates
                    .Where(old => !loaded.Any(t => string.Equals(t.Name, old.Name, StringComparison.OrdinalIgnoreCase)))
                    .ToList())
                    DataOutTemplates.Remove(gone);
                foreach (var t in loaded)
                {
                    var cur = DataOutTemplates.FirstOrDefault(
                        old => string.Equals(t.Name, old.Name, StringComparison.OrdinalIgnoreCase));
                    if (cur == null) DataOutTemplates.Add(t);
                    else { cur.Id = t.Id; cur.Body = t.Body; cur.FilePath = t.FilePath; }
                }

                // 2) список имён для грида Typno: те же поштучные Add/Remove (без Clear).
                foreach (var gone in DataOutTemplateNames
                    .Where(n => !loaded.Any(t => string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase)))
                    .ToList())
                    DataOutTemplateNames.Remove(gone);
                foreach (var t in loaded)
                    if (!DataOutTemplateNames.Any(n => string.Equals(n, t.Name, StringComparison.OrdinalIgnoreCase)))
                        DataOutTemplateNames.Add(t.Name);

                RefreshDataTemplates(); // F31: объединённый перечень data-шаблонов
                LoadDataTemplateVars(); // F33 (этап 7): перечитать маппинг объектов
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F16: ошибка чтения SCADA_DATAOUT_TEMPLATES: {msg}");
                StatusText = $"Ошибка чтения списка шаблонов привязок: {msg}";
            }
        }

        /// <summary>F16/F18: запросить имя шаблона (диалог ввода); null — отмена.
        /// F18: диалог показывается поверх активного окна (Owner) — не «теряется» за ним.</summary>
        private static string? PromptTemplateName(string prompt, string initial = "")
        {
            var owner = System.Windows.Application.Current?.Windows
                .OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive);
            return IO_PJT.Views.NamePromptWindow.Ask(prompt, initial, owner);
        }

        /// <summary>F16: «Сохранить шаблон под именем…» — текущий текст редактора
        /// (файла или кода) кладётся в SCADA_DATAOUT_TEMPLATES; имя пустое — спросить.</summary>
        private void SaveDataOutTemplateAs()
        {
            try
            {
                var (text, source) = LoadDataOutTemplateText();
                if (text == null)
                {
                    StatusText = $"Сохранение шаблона невозможно: {source}.";
                    return;
                }
                string name = (Settings.DataOutTemplateName ?? "").Trim();
                if (name.Length == 0)
                    name = PromptTemplateName(
                        "Шаблон не имеет имени — введите имя для сопоставления Typno «Шаблон привязок»:",
                        Path.GetFileNameWithoutExtension(Settings.DataOutTemplateFilePath ?? "")) ?? "";
                if (name.Trim().Length == 0)
                {
                    StatusText = "Имя шаблона не задано — шаблон не сохранён.";
                    return;
                }
                name = name.Trim();
                Settings.DataOutTemplateName = name;
                _s.ModbusBlocks.SaveDataOutTemplate(name, text, Settings.DataOutTemplateFilePath ?? "");
                LoadDataOutTemplateList();
                _s.Log.Info($"F16: шаблон привязок сохранён под именем «{name}» (SCADA_DATAOUT_TEMPLATES)");
                StatusText = $"Шаблон привязок сохранён: «{name}» ({source}).";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F16: ошибка сохранения шаблона привязок: {msg}");
                StatusText = $"Ошибка сохранения шаблона: {msg}";
            }
        }

        /// <summary>F16: «Загрузить» — тело выбранного шаблона из БД в редактор (режим «Код»).</summary>
        private void LoadDataOutTemplateIntoEditor()
        {
            var t = SelectedDataOutTemplate;
            if (t == null)
            {
                StatusText = "Выберите шаблон в списке «Сохранённые шаблоны».";
                return;
            }
            InlineDataOutText = t.Body ?? "";
            ModeDataOutInline = true;
            Settings.DataOutTemplateName = t.Name;
            if (!string.IsNullOrWhiteSpace(t.FilePath)) Settings.DataOutTemplateFilePath = t.FilePath;
            CheckDataOutTemplate();
            StatusText = $"Шаблон «{t.Name}» загружен в редактор: {DataOutTemplateStatus}";
        }

        /// <summary>F16: «Обновить из файла» — перечитать Body выбранного шаблона по FilePath
        /// (тело в БД — основное; изменения файла переносятся в БД кнопкой).</summary>
        private void UpdateDataOutTemplateFromFile()
        {
            var t = SelectedDataOutTemplate;
            if (t == null)
            {
                StatusText = "Выберите шаблон в списке «Сохранённые шаблоны».";
                return;
            }
            try
            {
                if (string.IsNullOrWhiteSpace(t.FilePath) || !File.Exists(t.FilePath))
                {
                    StatusText = $"Файл-источник шаблона «{t.Name}» не задан/не найден: {t.FilePath}";
                    return;
                }
                string body = File.ReadAllText(t.FilePath);
                _s.ModbusBlocks.SaveDataOutTemplate(t.Name, body, t.FilePath);
                LoadDataOutTemplateList();
                if (string.Equals(Settings.DataOutTemplateName, t.Name, StringComparison.OrdinalIgnoreCase))
                {
                    InlineDataOutText = body; // шаблон открыт в редакторе — обновить и его
                    CheckDataOutTemplate();
                }
                _s.Log.Info($"F16: шаблон «{t.Name}» обновлён из файла {t.FilePath}");
                StatusText = $"Шаблон «{t.Name}» обновлён из файла: {t.FilePath}";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F16: ошибка обновления шаблона из файла: {msg}");
                StatusText = $"Ошибка обновления из файла: {msg}";
            }
        }

        /// <summary>F16: «Удалить» — убрать выбранный шаблон из SCADA_DATAOUT_TEMPLATES
        /// (с подтверждением; правила Typno на него ссылаются → основной шаблон).</summary>
        private void DeleteDataOutTemplate()
        {
            var t = SelectedDataOutTemplate;
            if (t == null)
            {
                StatusText = "Выберите шаблон в списке «Сохранённые шаблоны».";
                return;
            }
            var ans = System.Windows.MessageBox.Show(
                $"Удалить шаблон привязок «{t.Name}»? Правила SCADA_TYPNO_MAP, ссылающиеся на него, " +
                "будут использовать основной шаблон.",
                "Удаление шаблона привязок",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (ans != System.Windows.MessageBoxResult.Yes) return;
            try
            {
                bool removed = _s.ModbusBlocks.DeleteDataOutTemplate(t.Name);
                LoadDataOutTemplateList();
                StatusText = removed
                    ? $"Шаблон «{t.Name}» удалён."
                    : $"Шаблон «{t.Name}» не найден (возможно, уже удалён).";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F16: ошибка удаления шаблона: {msg}");
                StatusText = $"Ошибка удаления шаблона: {msg}";
            }
        }



        // ================== Планирование/экспорт/привязки ==================

        /// <summary>F13: построить превью пакетов по текущему шаблону, настройкам и правилам.</summary>
        private void Build()
        {
            try
            {
                if (_template == null || !_template.Valid) CheckTemplate();
                if (_template == null || !_template.Valid)
                {
                    StatusText = $"Превью не построено: {_template?.Error ?? "шаблон не загружен"}.";
                    return;
                }
                var points = _s.Repo.GetAll();
                var rules = SlaveRules.Where(r => !string.IsNullOrWhiteSpace(r.Station)).ToList();
                // F33 (этап 6): размеры переменных правил Typno (Size) — приоритет над
                // M_DataType в ширине пакета (бит/8/16bit → 1 регистр, 32 → 2, 64 → 4)
                var sizeByTypno = new System.Collections.Generic.Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var r in TypnoRules)
                    if (r.Typno.Trim().Length > 0 && !sizeByTypno.ContainsKey(r.Typno.Trim()))
                        sizeByTypno[r.Typno.Trim()] = MbTypnoMapRule.NormalizeSize(r.Size);
                var result = _s.ModbusBlocks.BuildPlan(points, Settings, rules, _template.MasterNameBase, sizeByTypno);
                _built = result;
                Preview.Clear();
                foreach (var p in result.Packets) Preview.Add(p);
                BindingsPreview.Clear();
                foreach (var p in result.Packets)
                    foreach (var b in p.Bindings)
                        BindingsPreview.Add(b);
                RefreshStationFilters(result); // F28: галочки станций для экспорта
                StatsText = $"Пакетов (блоков мастера): {result.Packets.Count}; " +
                            $"привязок DataOut: {BindingsPreview.Count}; " +
                            $"точек в пакетах: {result.TotalPoints} из {result.WithAddressing} с адресацией; " +
                            $"пропущено: без адресации {result.SkippedNoAddressing}, " +
                            $"без Typno {result.SkippedNoTypno}, " +
                            $"запись {result.SkippedWrite}, адрес {result.SkippedBadAddress}, " +
                            $"IP {result.SkippedBadEndpoint}, функция {result.SkippedBadFunc}; " +
                            $"Typno без маппинга: {CountUnmappedTypno(result)} (F33: привязки пропускаются); " +
                            $"предупреждений: {result.Warnings.Count}";
                StatusText = result.Packets.Count > 0
                    ? "Превью построено. Кнопки: «Записать привязки в IO_LIST», «Сформировать XML...»."
                    : "Пакеты не сформированы — проверьте адресацию Modbus (M_PlcTag) в таблице.";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F13: ошибка планирования: {msg}");
                StatusText = $"Ошибка планирования: {msg}";
            }
        }

        /// <summary>F28: обновить галочки станций по станциям плана (новые добавляются выбранными,
        /// состояние уже известных станций сохраняется — перестроение превью не сбрасывает выбор).</summary>
        private void RefreshStationFilters(MbPlanResult result)
        {
            // F37: ключ пункта — «Controller|Station»: одна станция у разных контроллеров —
            // независимые галочки (пакеты планируются раздельно)
            var counts = new Dictionary<string, (string Ctrl, string St, int N)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var p in result.Packets)
            {
                string ctrl = (p.Controller ?? "").Trim();
                string st = (p.Station ?? "").Trim();
                string key = ctrl + "|" + st;
                counts.TryGetValue(key, out var cur0);
                counts[key] = (ctrl, st, cur0.N + 1);
            }
            foreach (var gone in StationFilters.Where(f => !counts.ContainsKey(f.Key)).ToList())
                StationFilters.Remove(gone);
            foreach (var kv in counts)
            {
                var cur = StationFilters.FirstOrDefault(f =>
                    string.Equals(f.Key, kv.Key, StringComparison.OrdinalIgnoreCase));
                if (cur == null) StationFilters.Add(new MbStationFilterItem
                {
                    Controller = kv.Value.Ctrl,
                    Station = kv.Value.St,
                    PacketCount = kv.Value.N
                });
                else cur.PacketCount = kv.Value.N;
            }
            UpdateStationsHint();
        }

        /// <summary>F28: «Выбрать все»/«Снять все».</summary>
        private void SetAllStations(bool selected)
        {
            foreach (var f in StationFilters) f.IsSelected = selected;
            UpdateStationsHint();
        }

        /// <summary>F28: подсказка «генерить станций: X из Y».</summary>
        private void UpdateStationsHint()
        {
            int sel = StationFilters.Count(f => f.IsSelected);
            StationsHint = StationFilters.Count > 0
                ? $"Генерить станций: {sel} из {StationFilters.Count}"
                : "Станции не определены — постройте превью.";
        }

        /// <summary>F28: обновить подсказку после клика по чекбоксу станции (из code-behind окна —
        /// у MbStationFilterItem нет INPC, привязка IsChecked TwoWay пишет IsSelected напрямую).</summary>
        public void NotifyStationFilterChanged() => UpdateStationsHint();

        /// <summary>F28: пакеты плана, отфильтрованные по выбранным станциям (null — ни одна не
        /// выбрано; пустой список фильтра = все станции, напр. превью без пакетов).</summary>
        private List<MbPacketPlan>? SelectedStationPackets()
        {
            if (_built == null) return null;
            if (StationFilters.Count == 0) return _built.Packets.ToList();
            var selected = new HashSet<string>(
                StationFilters.Where(f => f.IsSelected).Select(f => f.Key),
                StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0) return null; // ни одной станции не выбрано — ошибка экспорта
            return _built.Packets.Where(p => selected.Contains(
                (p.Controller ?? "").Trim() + "|" + (p.Station ?? "").Trim())).ToList();
        }

        /// <summary>F28: копия плана с пакетами только выбранных станций (для WriteBindings/экспорта);
        /// счётчики и предупреждения переносятся как есть.</summary>
        private MbPlanResult? FilteredPlan()
        {
            var packets = SelectedStationPackets();
            if (packets == null) return null;
            var plan = new MbPlanResult
            {
                WithAddressing = _built!.WithAddressing,
                SkippedNoAddressing = _built.SkippedNoAddressing,
                SkippedWrite = _built.SkippedWrite,
                SkippedBadAddress = _built.SkippedBadAddress,
                SkippedBadEndpoint = _built.SkippedBadEndpoint,
                SkippedBadFunc = _built.SkippedBadFunc,
                SkippedNoTypno = _built.SkippedNoTypno
            };
            foreach (var p in packets) plan.Packets.Add(p);
            plan.Warnings.AddRange(_built.Warnings);
            return plan;
        }

        /// <summary>F13: «Записать привязки в IO_LIST» — M_BusBlock/M_BusDataOut по плану
        /// (MessageBox со счётчиками, аудит Update).</summary>
        private void WriteBindingsToDb()
        {
            if (_built == null) return;
            try
            {
                // F28: привязки пишутся только для выбранных станций (иначе M_BusBlock
                // заполнится у станций, которые не генерились)
                var plan = FilteredPlan();
                if (plan == null)
                {
                    StatusText = "Запись привязок невозможна: не выбрана ни одна станция (галочки выше превью).";
                    return;
                }
                var r = _s.ModbusBlocks.WriteBindings(plan, _s.Repo);
                _s.Log.Info($"F13: {r.Summary}");
                foreach (var e in r.Errors) _s.Log.Warn(e);
                System.Windows.MessageBox.Show(
                    r.Summary + (r.Errors.Count > 0 ? "\n\n" + string.Join("\n", r.Errors.Take(10)) : ""),
                    "Привязки Modbus (IO_LIST)",
                    System.Windows.MessageBoxButton.OK,
                    r.Errors.Count > 0 ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Information);
                StatusText = r.Summary;
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F13: ошибка записи привязок: {msg}");
                StatusText = $"Ошибка записи привязок: {msg}";
            }
        }

        /// <summary>F13: экспорт BufScadaPOUS + файла привязок (рядом, «{имя}_bindings.xml»).</summary>
        private void Export()
        {
            if (Preview.Count == 0 || _built == null) return;
            try
            {
                if (_templateText == null || _template == null || !_template.Valid) CheckTemplate();
                if (_templateText == null || _template == null || !_template.Valid)
                {
                    StatusText = $"Экспорт невозможен: {_template?.Error ?? "шаблон не загружен"}.";
                    return;
                }
                // F37: split-режим (по умолчанию) — диалог ПАПКИ и файлы «Контроллер_Станция…»;
                // сводный режим — исторический SaveFileDialog (один PousXml + _bindings рядом)
                string? splitDir = null;
                string? outFile = null;
                if (Settings.SplitByControllerStation)
                {
                    var fdlg = new Microsoft.Win32.OpenFolderDialog
                    {
                        Title = "F37: папка для файлов экспорта (Контроллер_Станция[_Typno]_№)"
                    };
                    if (fdlg.ShowDialog() != true) return;
                    splitDir = fdlg.FolderName;
                }
                else
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "XML (*.xml)|*.xml|Все файлы (*.*)|*.*",
                        FileName = $"{_template.MasterNameBase}_{DateTime.Now:yyyyMMdd_HHmmss}.xml"
                    };
                    if (dlg.ShowDialog() != true) return;
                    outFile = dlg.FileName;
                }

                // F28: экспорт только выбранных станций (галочки выше превью)
                var packets = SelectedStationPackets();
                if (packets == null)
                {
                    StatusText = "Экспорт невозможен: не выбрана ни одна станция (галочки выше превью).";
                    return;
                }
                int stationsTotal = StationFilters.Count;

                // F22: пакеты разделяются по протоколу станции (SCADA_MB_SLAVE_RULES.Protocol):
                // RTU-пакеты генерируются по шаблону SCADA_MASTER_TEMPLATES (MasterRtuTemplateName),
                // без него — пропускаются с предупреждением (все warnings — в журнал ниже)
                string? rtuText = null;
                if (!string.IsNullOrWhiteSpace(Settings.MasterRtuTemplateName))
                    rtuText = MasterTemplates.FirstOrDefault(t =>
                        string.Equals(t.Name, Settings.MasterRtuTemplateName.Trim(),
                            StringComparison.OrdinalIgnoreCase))?.Body;
                // F29 + F37: «станция → тело пакетного шаблона» (SCADA_MB_SLAVE_RULES.MasterTemplate);
                // ключ правила (Controller|Station) приоритетнее универсального «станция»;
                // станция без правила/имя не найдено — fallback по протоколу (TCP/RTU-шаблоны ниже)
                var ruleByCsF37 = new Dictionary<string, MbSlaveIdRule>(StringComparer.OrdinalIgnoreCase);
                var ruleByStF37 = new Dictionary<string, MbSlaveIdRule>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in SlaveRules)
                {
                    string stR = (r.Station ?? "").Trim();
                    string ctR = (r.Controller ?? "").Trim();
                    if (stR.Length == 0) continue;
                    string keyR = ctR + "|" + stR;
                    if (!ruleByCsF37.ContainsKey(keyR)) ruleByCsF37[keyR] = r;
                    if (ctR.Length == 0 && !ruleByStF37.ContainsKey(stR)) ruleByStF37[stR] = r;
                }
                var templateByStation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var grp in packets.GroupBy(p => new
                {
                    Ctrl = (p.Controller ?? "").Trim(),
                    St = (p.Station ?? "").Trim()
                }))
                {
                    if (grp.Key.St.Length == 0) continue;
                    MbSlaveIdRule? rule;
                    if (!(grp.Key.Ctrl.Length > 0 &&
                          ruleByCsF37.TryGetValue(grp.Key.Ctrl + "|" + grp.Key.St, out rule)))
                        ruleByStF37.TryGetValue(grp.Key.St, out rule);
                    string mt = (rule?.MasterTemplate ?? "").Trim();
                    if (mt.Length == 0) continue;
                    string body = MasterTemplates.FirstOrDefault(t =>
                        string.Equals(t.Name, mt, StringComparison.OrdinalIgnoreCase))?.Body ?? "";
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        if (!templateByStation.ContainsKey(grp.Key.St))
                            templateByStation[grp.Key.St] = body;
                    }
                    else
                        _s.Log.Warn($"F29: станция «{grp.Key.St}» ссылается на шаблон «{mt}» — не найден в SCADA_MASTER_TEMPLATES (fallback по протоколу)");
                }

                // F37: раздельный экспорт — файлы по (Controller, Station) в выбранную папку
                if (splitDir != null)
                {
                    ExportSplit(packets, splitDir, templateByStation, rtuText);
                    return;
                }

                // F14/F16/F33/F37: карты Typno/шаблонов/dataVars — общий хелпер
                // (тот же набор использует split-экспорт ExportSplit);
                // F38: dataVars нужен и WriteProtocolSplit — тексты примитивов [TEXT]
                var (typnoMap, namedTemplates, dataInTemplates, dataVars) = BuildDataMaps();

                var gen = _s.ModbusBlocksXml.WriteProtocolSplit(
                    packets, templateByStation, _templateText!, rtuText, Settings,
                    dataVars, Settings.DataOutTemplateName);
                foreach (var w in gen.Warnings) _s.Log.Warn(w);
                File.WriteAllText(outFile!, gen.PousXml, new System.Text.UTF8Encoding(true));
                string dir = Path.GetDirectoryName(outFile!) ?? "";
                string baseName = Path.GetFileNameWithoutExtension(outFile!);
                string bindingsPath = Path.Combine(dir, baseName + "_bindings.xml");

                // F13.1: привязки из шаблона Data_Out (цепочки per тип сигнала); если шаблон
                // не загружен/невалиден — исторический простой файл привязок из Write
                string bindingsXml = gen.BindingsXml;
                bool fromTemplate = false;
                if (_dataOutTemplate == null || !_dataOutTemplate.Valid) CheckDataOutTemplate();
                if (_dataOutTemplateText != null && _dataOutTemplate != null && _dataOutTemplate.Valid)
                {
                    var warnings = new System.Collections.Generic.List<string>();
                    bindingsXml = _dataOut.Write(packets, _dataOutTemplateText,
                        gen.MasterMeta, Settings, warnings, typnoMap, namedTemplates, dataInTemplates,
                        dataVars, Settings.DataOutTemplateName);
                    fromTemplate = true;
                    foreach (var w in warnings) _s.Log.Warn(w);
                }
                File.WriteAllText(bindingsPath, bindingsXml, new System.Text.UTF8Encoding(true));
                int stSel = StationFilters.Count(f => f.IsSelected); // F28
                _s.Log.Info($"F13: XML сформирован: {outFile} + привязки {bindingsPath} " +
                            $"(станций: {stSel}/{stationsTotal}, пакетов: {packets.Count}, привязок: {BindingsPreview.Count}, " +
                            (fromTemplate ? "шаблон привязок Data_Out)" : "простые привязки)"));
                StatusText = $"Файлы сформированы: {outFile} и {bindingsPath} " +
                             $"(станций {stSel}/{stationsTotal}, пакетов {packets.Count}, " +
                             (fromTemplate ? "цепочки из шаблона привязок)." : "простые привязки).");
                if (gen.Warnings.Count > 0)
                    StatusText += $" Предупреждений: {gen.Warnings.Count} — подробности в журнале.";
            }
            catch (Exception ex)
            {
                _s.Log.Error($"F13: ошибка экспорта XML: {ex.Message}");
                StatusText = $"Ошибка экспорта: {ex.Message}";
            }
        }

        /// <summary>F37: раздельный экспорт — на каждую пару (Controller, Station) файл блоков
        /// «Контроллер_Станция_№.xml» (WriteProtocolSplit: протокол/шаблон станции F22/F29) и
        /// отдельные файлы data-объектов «Контроллер_Станция_Typno_№.xml» по каждому Typno
        /// (dataout-шаблон); без dataout-шаблона — «…№_bindings.xml» простыми привязками.
        /// № — сквозная нумерация по порядку внутри экспорта; счётчики T11ID/cardId
        /// продолжаются от файла к файлу (ShiftSettingsForNextFile) — уникальность ID при
        /// импорте нескольких файлов в один проект SCADA. Сводный режим — см. Export.</summary>
        private void ExportSplit(List<MbPacketPlan> packets, string dir,
            Dictionary<string, string> templateByStation, string? rtuText)
        {
            var (typnoMap, namedTemplates, dataInTemplates, dataVars) = BuildDataMaps();
            bool dataTplValid = _dataOutTemplateText != null && _dataOutTemplate != null && _dataOutTemplate.Valid;
            if (!dataTplValid) CheckDataOutTemplate();
            dataTplValid = _dataOutTemplateText != null && _dataOutTemplate != null && _dataOutTemplate.Valid;
            var utf8 = new System.Text.UTF8Encoding(true);
            var settings = Settings.Clone();
            int seq = 0;
            var written = new List<string>();

            foreach (var grp in packets.GroupBy(p => new
                     {
                         Ctrl = (p.Controller ?? "").Trim(),
                         St = (p.Station ?? "").Trim()
                     })
                     .OrderBy(g => g.Key.Ctrl, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.Key.St, StringComparer.OrdinalIgnoreCase))
            {
                var groupPackets = grp.OrderBy(p => p.No).ToList();
                string ctrlPart = SanitizeFilePart(grp.Key.Ctrl, "БезКонтроллера");
                string stPart = SanitizeFilePart(grp.Key.St, "БезСтанции");

                // —— файл блоков Modbus пары (Controller, Station) ——
                seq++;
                var gen = _s.ModbusBlocksXml.WriteProtocolSplit(
                    groupPackets, templateByStation, _templateText!, rtuText, settings,
                    dataVars, Settings.DataOutTemplateName); // F38: тексты примитивов [TEXT]
                foreach (var w in gen.Warnings) _s.Log.Warn(w);
                string blockPath = Path.Combine(dir, $"{ctrlPart}_{stPart}_{seq}.xml");
                File.WriteAllText(blockPath, gen.PousXml, utf8);
                written.Add(blockPath);
                settings = ScadaTemplateXmlService.ShiftSettingsForNextFile(
                    settings, gen.PousXml, gen.BindingsXml);

                // —— файлы data-объектов: по одному на Typno внутри пары ——
                if (dataTplValid)
                {
                    var warnings = new System.Collections.Generic.List<string>();
                    foreach (var tg in groupPackets.GroupBy(p =>
                                 p.Bindings.Count > 0 ? p.Bindings[0].Typno.Trim() : "")
                             .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (tg.Key.Length == 0) continue;
                        seq++;
                        string typnoPart = SanitizeFilePart(tg.Key, "NoTypno");
                        string dataXml = _dataOut.Write(tg.ToList(), _dataOutTemplateText!,
                            gen.MasterMeta, settings, warnings, typnoMap, namedTemplates,
                            dataInTemplates, dataVars, Settings.DataOutTemplateName);
                        string dataPath = Path.Combine(dir,
                            $"{ctrlPart}_{stPart}_{typnoPart}_{seq}.xml");
                        File.WriteAllText(dataPath, dataXml, utf8);
                        written.Add(dataPath);
                        settings = ScadaTemplateXmlService.ShiftSettingsForNextFile(settings, dataXml);
                    }
                    foreach (var w in warnings) _s.Log.Warn(w);
                }
                else if (!string.IsNullOrEmpty(gen.BindingsXml))
                {
                    // dataout-шаблон не загружен/невалиден — простые привязки пары (F13)
                    seq++;
                    string bindPath = Path.Combine(dir, $"{ctrlPart}_{stPart}_{seq}_bindings.xml");
                    File.WriteAllText(bindPath, gen.BindingsXml, utf8);
                    written.Add(bindPath);
                    settings = ScadaTemplateXmlService.ShiftSettingsForNextFile(settings, gen.BindingsXml);
                }
            }

            int stSel = StationFilters.Count(f => f.IsSelected); // F28
            int stationsTotal = StationFilters.Count;
            _s.Log.Info($"F37: split-экспорт в «{dir}»: файлов {written.Count} " +
                        $"(станций: {stSel}/{stationsTotal}, пакетов: {packets.Count}): " +
                        string.Join(", ", written.Select(Path.GetFileName)));
            StatusText = $"Файлов сформировано: {written.Count} в папке {dir} " +
                         $"(станций {stSel}/{stationsTotal}, пакетов {packets.Count}; " +
                         (dataTplValid ? "блоки Modbus + data-объекты по Typno)." : "простые привязки).");
        }

        /// <summary>F37: карты генерации data-привязок — «Typno → (шаблон, эталон, размер)»
        /// (SCADA_TYPNO_MAP), именованные dataout- (F16) и datain-шаблоны (F30) и маппинг
        /// объектов data-шаблонов (F33/F37 «шаблон → объект → (колонка, литерал)»).</summary>
        private (Dictionary<string, (string Template, string Exemplar, string Size)> Typno,
                 Dictionary<string, string> Named,
                 Dictionary<string, string> DataIn,
                 Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>> Vars)
            BuildDataMaps()
        {
            var typnoMap = new System.Collections.Generic.Dictionary<string,
                (string Template, string Exemplar, string Size)>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in TypnoRules)
                if (r.Typno.Trim().Length > 0 && r.Exemplar.Trim().Length > 0)
                    typnoMap[r.Typno.Trim()] = (r.Template.Trim(), r.Exemplar.Trim(),
                        MbTypnoMapRule.NormalizeSize(r.Size));
            var namedTemplates = new System.Collections.Generic.Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var t in DataOutTemplates)
                if (t.Name.Trim().Length > 0 && !string.IsNullOrWhiteSpace(t.Body))
                    namedTemplates[t.Name.Trim()] = t.Body;
            var dataInTemplates = new System.Collections.Generic.Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var t in DataInTemplates)
                if (t.Name.Trim().Length > 0 && !string.IsNullOrWhiteSpace(t.Body))
                    dataInTemplates[t.Name.Trim()] = t.Body;
            var dataVars = new System.Collections.Generic.Dictionary<string,
                System.Collections.Generic.IReadOnlyDictionary<string,
                    (string IoColumn, string Literal)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in DataTemplateVars)
            {
                string tn = row.TemplateName.Trim();
                string vn = row.Var.Trim();
                if (tn.Length == 0 || vn.Length == 0) continue;
                System.Collections.Generic.Dictionary<string,
                    (string IoColumn, string Literal)> m;
                if (dataVars.TryGetValue(tn, out var existing))
                    m = (System.Collections.Generic.Dictionary<string,
                        (string IoColumn, string Literal)>)existing;
                else
                {
                    m = new System.Collections.Generic.Dictionary<string,
                        (string IoColumn, string Literal)>(StringComparer.OrdinalIgnoreCase);
                    dataVars[tn] = m;
                }
                m[vn] = (row.IoColumn.Trim(), row.Literal.Trim());
            }
            return (typnoMap, namedTemplates, dataInTemplates, dataVars);
        }

        /// <summary>F37: часть имени файла из Controller/Station/Typno — недопустимые символы
        /// → «_», пробелы → «_»; пустое значение → fallback («БезКонтроллера»/«БезСтанции»).</summary>
        private static string SanitizeFilePart(string? s, string fallback)
        {
            string v = (s ?? "").Trim();
            if (v.Length == 0) return fallback;
            foreach (var c in Path.GetInvalidFileNameChars())
                v = v.Replace(c, '_');
            v = v.Replace(' ', '_');
            return v.Length > 0 ? v : fallback;
        }

        // ================== Персист ==================

        /// <summary>F13/F18: сохранить настройки в SCADA_MB_SETTINGS (переживают перезапуск);
        /// в режиме «Код» — и вставленный шаблон в SCADA_MB_TEMPLATE. F18: после сохранения
        /// шаблоны перечитываются/валидируются — статус окна не «пропадает», а показывает
        /// актуальный источник шаблона.</summary>
        private void SaveSettings()
        {
            try
            {
                // F16/F18: тело шаблона привязок сохраняется под именем SCADA_DATAOUT_TEMPLATES
                // (режим «Код»: имя спрашивается, если не задано — «спросить имя»; отмена
                // имени — тело остаётся в SCADA_MB_TEMPLATE «dataout», о чём явно сообщаем)
                var (dText, _) = LoadDataOutTemplateText();
                string? dNote = null;
                if (dText != null)
                {
                    string dName = (Settings.DataOutTemplateName ?? "").Trim();
                    if (dName.Length == 0 && Settings.DataOutTemplateMode == MbTemplateMode.Inline)
                        dName = PromptTemplateName(
                            "Код шаблона привязок вставлен — присвойте ему имя «Шаблон привязок» " +
                            "(Typno → этот шаблон в SCADA_TYPNO_MAP):",
                            Path.GetFileNameWithoutExtension(Settings.DataOutTemplateFilePath ?? "")) ?? "";
                    if (dName.Trim().Length > 0)
                    {
                        dName = dName.Trim();
                        Settings.DataOutTemplateName = dName;
                        _s.ModbusBlocks.SaveDataOutTemplate(dName, dText,
                            Settings.DataOutTemplateFilePath ?? "");
                        LoadDataOutTemplateList();
                    }
                    else
                        dNote = "имя шаблона привязок не задано — тело в SCADA_MB_TEMPLATE «dataout», " +
                                "в список шаблонов не добавлено";
                }
                _s.ModbusBlocks.SaveSettings(Settings);
                if (Settings.TemplateMode == MbTemplateMode.Inline)
                    _s.ModbusBlocks.SaveInlineTemplate(InlineTemplateText ?? "");
                if (Settings.DataOutTemplateMode == MbTemplateMode.Inline) // F13.1: строка «dataout»
                    _s.ModbusBlocks.SaveInlineTemplate(
                        ModbusBlockPlannerService.InlineDataOutTemplateName, InlineDataOutText ?? "");
                // F18: перечитать и провалидировать оба шаблона — статус отражает только что
                // сохранённые настройки (путь/код/автопоиск) и ничего не «пропадает»
                CheckTemplate();
                CheckDataOutTemplate();
                _s.Log.Info($"F13: настройки генерации сохранены ({ModbusBlockPlannerService.SettingKeys.Length} параметров" +
                            (Settings.TemplateMode == MbTemplateMode.Inline ? " + шаблон в SCADA_MB_TEMPLATE)" : ")"));
                StatusText = (Settings.TemplateMode == MbTemplateMode.Inline
                    ? "Настройки и шаблон сохранены (SCADA_MB_SETTINGS / SCADA_MB_TEMPLATE)"
                    : "Настройки сохранены в SCADA_MB_SETTINGS")
                    + (Settings.DataOutTemplateMode == MbTemplateMode.Inline
                        ? "; шаблон привязок — SCADA_MB_TEMPLATE «dataout»"
                        : "")
                    + (dNote != null ? "; " + dNote : "") + ".";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F13: ошибка сохранения настроек: {msg}");
                StatusText = $"Ошибка сохранения настроек: {msg}";
            }
        }

        /// <summary>F13: сохранить правила «станция → SlaveId» в SCADA_MB_SLAVE_RULES (полная замена).</summary>
        private void SaveRules()
        {
            try
            {
                int n = _s.ModbusBlocks.SaveSlaveRules(SlaveRules);
                _s.Log.Info($"F13: правил SlaveId сохранено: {n} (SCADA_MB_SLAVE_RULES)");
                StatusText = $"Сохранено правил SlaveId: {n}.";
                LoadRules(); // перечитать с ID
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F13: ошибка сохранения SCADA_MB_SLAVE_RULES: {msg}");
                StatusText = $"Ошибка сохранения правил: {msg}";
            }
        }

        private void AddRule()
        {
            var r = new MbSlaveIdRule { Station = "", SlaveId = Settings.DefaultSlaveId };
            SlaveRules.Add(r);
            SelectedRule = r;
            StatusText = $"Правило добавлено (не сохранено): {SlaveRules.Count}. Не забудьте «Сохранить правила SlaveId».";
        }

        private void DeleteRule()
        {
            if (SelectedRule == null) return;
            SlaveRules.Remove(SelectedRule);
            SelectedRule = null;
            StatusText = $"Правило удалено (не сохранено): {SlaveRules.Count}. Не забудьте «Сохранить правила SlaveId».";
        }

        // ================== F14: сопоставление Typno ==================

        /// <summary>F14: число привязок, чей Typno отсутствует в текущих правилах
        /// (эталон будет выбран по M_DataType с предупреждением).</summary>
        private int CountUnmappedTypno(MbPlanResult result)
        {
            var mapped = new HashSet<string>(
                TypnoRules.Where(r => !string.IsNullOrWhiteSpace(r.Exemplar))
                    .Select(r => r.Typno.Trim()), StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var p in result.Packets)
                foreach (var b in p.Bindings)
                    if (b.Typno.Length > 0 && !mapped.Contains(b.Typno)) n++;
            return n;
        }

        /// <summary>F33 (этап 6): размер переменной по телу data-шаблона — число источников
        /// первого эталона: 4→32bit, 2→16bit, 8→64bit, 1→bit (bool-like ключ)/8bit;
        /// "" — не определить (нет шаблона/источников).</summary>
        private string SizeFromDataTemplate(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string key = name.Trim();
            string? body =
                DataOutTemplates.FirstOrDefault(t => string.Equals(t.Name, key, StringComparison.OrdinalIgnoreCase))?.Body
                ?? DataInTemplates.FirstOrDefault(t => string.Equals(t.Name, key, StringComparison.OrdinalIgnoreCase))?.Body;
            if (string.IsNullOrWhiteSpace(body)) return "";
            return _dataOut.DetectTemplateSize(body).Size;
        }

        /// <summary>F33: подписка на правило Typno — автозаполнение Size при смене шаблона.</summary>
        private void HookTypnoRule(MbTypnoMapRule r)
        {
            r.PropertyChanged -= OnTypnoRuleChanged;
            r.PropertyChanged += OnTypnoRuleChanged;
        }

        /// <summary>F33: выбор шаблона привязок в правиле Typno → Size заполняется из шаблона
        /// (число источников эталона); ручная правка Size живёт до смены шаблона.</summary>
        private void OnTypnoRuleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not MbTypnoMapRule r || e.PropertyName != nameof(MbTypnoMapRule.Template)) return;
            string sz = SizeFromDataTemplate(r.Template);
            if (sz.Length > 0) r.Size = sz;
        }

        /// <summary>F14: прочитать правила «Typno → эталон» из SCADA_TYPNO_MAP.</summary>
        private void LoadTypnoRules()
        {
            try
            {
                TypnoRules.Clear();
                foreach (var r in _s.ModbusBlocks.LoadTypnoMap())
                {
                    HookTypnoRule(r); // F33: автозаполнение Size при смене шаблона
                    TypnoRules.Add(r);
                }
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F14: ошибка чтения SCADA_TYPNO_MAP: {msg}");
                StatusText = $"Ошибка чтения сопоставления Typno: {msg}";
            }
        }

        private void AddTypnoRule()
        {
            var r = new MbTypnoMapRule();
            HookTypnoRule(r); // F33: автозаполнение Size при смене шаблона
            TypnoRules.Add(r);
            SelectedTypnoRule = r;
            StatusText = $"Правило Typno добавлено (не сохранено): {TypnoRules.Count}. " +
                         "Не забудьте «Сохранить сопоставление Typno».";
        }

        private void DeleteTypnoRule()
        {
            if (SelectedTypnoRule == null) return;
            TypnoRules.Remove(SelectedTypnoRule);
            SelectedTypnoRule = null;
            StatusText = $"Правило Typno удалено (не сохранено): {TypnoRules.Count}.";
        }

        /// <summary>F14: сохранить сопоставление «Typno → эталон» в SCADA_TYPNO_MAP
        /// (полная замена; строки без Typno/эталона отбрасываются).</summary>
        private void SaveTypnoMap()
        {
            try
            {
                int n = _s.ModbusBlocks.SaveTypnoMap(TypnoRules);
                _s.Log.Info($"F14: сопоставлений Typno сохранено: {n} (SCADA_TYPNO_MAP)");
                StatusText = $"Сохранено сопоставлений Typno: {n}.";
                LoadTypnoRules(); // перечитать с ID
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F14: ошибка сохранения SCADA_TYPNO_MAP: {msg}");
                StatusText = $"Ошибка сохранения сопоставления Typno: {msg}";
            }
        }

        /// <summary>F14: заполнить грид уникальными Typno точек текущей таблицы с адресацией
        /// (M_PlcTag), ещё не добавленными; эталон задаётся вручную.</summary>
        private void FillTypnoRules()
        {
            try
            {
                var existing = new HashSet<string>(
                    TypnoRules.Select(r => r.Typno.Trim()), StringComparer.OrdinalIgnoreCase);
                int added = 0;
                foreach (var p in _s.Repo.GetAll())
                {
                    if (string.IsNullOrWhiteSpace(p.Modbus?.PlcTag)) continue;
                    string t = (p.Identification.Typno ?? "").Trim();
                    if (t.Length == 0 || !existing.Add(t)) continue;
                    var ruleNew = new MbTypnoMapRule { Typno = t };
                    HookTypnoRule(ruleNew); // F33: автозаполнение Size при выборе шаблона
                    TypnoRules.Add(ruleNew);
                    added++;
                }
                StatusText = added > 0
                    ? $"Добавлено Typno из данных: {added} (всего {TypnoRules.Count}). Задайте эталон и «Сохранить сопоставление Typno»."
                    : $"Новых Typno не найдено (всего {TypnoRules.Count}).";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F14: ошибка заполнения Typno из данных: {msg}");
                StatusText = $"Ошибка заполнения Typno: {msg}";
            }
        }

        /// <summary>F21: автосбор станций slave из IO_LIST (M_SlaveStation, M_SlaveIdMain,
        /// M_SlaveIdBackup, M_Speed, M_Timeout) с merge по Station: авто-поля (IP main/reserve,
        /// Speed, Timeout) обновляются, ручные (SlaveId, Protocol, Parity, StopBit, DataBit,
        /// Reverse, MaxLength) сохраняются, Protocol заполняется эвристикой для новых/пустых.
        /// Изменения применяются кнопкой «Сохранить правила SlaveId».</summary>
        private void FillSlaveRules()
        {
            try
            {
                var merged = ModbusBlockPlannerService.MergeSlaveRulesFromPoints(
                    SlaveRules.ToList(), _s.Repo.GetAll());
                SlaveRules.Clear();
                foreach (var r in merged) SlaveRules.Add(r);
                StatusText = $"Станций после автосбора: {merged.Count} (IP/скорость/таймаут — из IO_LIST, " +
                             "Protocol/UART — уточните вручную при необходимости) — сохраните кнопкой «Сохранить».";
                _s.Log.Info($"F21: автосбор станций из IO_LIST: {merged.Count} (SCADA_MB_SLAVE_RULES)");
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F21: ошибка автосбора станций из IO_LIST: {msg}");
                StatusText = $"Ошибка автосбора станций: {msg}";
            }
        }

        // ================== F22: залитые шаблоны (мастер / DataIn) ==================

        /// <summary>F22: перечитать список мастер-шаблонов блоков (SCADA_MASTER_TEMPLATES).</summary>
        private void LoadMasterTemplateList()
        {
            try
            {
                MasterTemplates.Clear();
                foreach (var t in _s.ModbusBlocks.LoadMasterTemplates()) MasterTemplates.Add(t);
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F22: ошибка чтения SCADA_MASTER_TEMPLATES: {msg}");
                StatusText = $"Ошибка чтения списка мастер-шаблонов: {msg}";
            }
        }

        /// <summary>F22: перечитать список шаблонов привязок DataIn (SCADA_DATAIN_TEMPLATES).</summary>
        private void LoadDataInTemplateList()
        {
            try
            {
                DataInTemplates.Clear();
                foreach (var t in _s.ModbusBlocks.LoadDataInTemplates()) DataInTemplates.Add(t);
                RefreshDataTemplates(); // F31: обновить объединённый перечень
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F22: ошибка чтения SCADA_DATAIN_TEMPLATES: {msg}");
                StatusText = $"Ошибка чтения списка шаблонов DataIn: {msg}";
            }
        }

        // ================== F31: объединённый перечень data-шаблонов ==================

        /// <summary>F31: объединить dataout+datain в DataTemplates (поштучно — без Reset,
        /// чтобы не сбрасывать выбор/привязки) и дополнить DataOutTemplateNames именами
        /// DataIn: грид Typno «Шаблон привязок» ссылается на любой data-шаблон.</summary>
        private void RefreshDataTemplates()
        {
            // 1) объединённый перечень: удалённые убрать, новые добавить (чтение, затем запись)
            var wanted = DataOutTemplates.Select(t => ("чтение", t.Name, t.FilePath))
                .Concat(DataInTemplates.Select(t => ("запись", t.Name, t.FilePath))).ToList();
            foreach (var gone in DataTemplates
                .Where(r => !wanted.Any(w => string.Equals(w.Name, r.Name, StringComparison.OrdinalIgnoreCase)
                                          && w.Item1 == r.Kind)).ToList())
                DataTemplates.Remove(gone);
            foreach (var w in wanted)
            {
                var cur = DataTemplates.FirstOrDefault(r =>
                    string.Equals(r.Name, w.Name, StringComparison.OrdinalIgnoreCase) && r.Kind == w.Item1);
                if (cur == null)
                    DataTemplates.Add(new MbDataTemplateRow { Kind = w.Item1, Name = w.Name, FilePath = w.FilePath });
                else cur.FilePath = w.FilePath;
            }

            // 2) имена для колонки «Шаблон привязок» грида Typno: dataout ∪ datain (поштучно)
            foreach (var t in DataInTemplates)
                if (!DataOutTemplateNames.Any(n => string.Equals(n, t.Name, StringComparison.OrdinalIgnoreCase)))
                    DataOutTemplateNames.Add(t.Name);
        }

        /// <summary>F31: «Открыть шаблон из файла…» — тип определяется по содержимому
        /// (есть «.DataIn[» → шаблон записи), файл сохраняется в БД и появляется
        /// в объединённом перечне (выбирается).</summary>
        private void OpenDataTemplate()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "XML шаблон привязок (*.xml)|*.xml|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                string body = File.ReadAllText(dlg.FileName);
                string name = Path.GetFileNameWithoutExtension(dlg.FileName);
                bool isWrite = body.Contains(".DataIn[", StringComparison.OrdinalIgnoreCase);
                if (isWrite) _s.ModbusBlocks.SaveDataInTemplate(name, body, dlg.FileName);
                else _s.ModbusBlocks.SaveDataOutTemplate(name, body, dlg.FileName);
                LoadDataOutTemplateList();
                LoadDataInTemplateList();
                SelectedDataTemplate = DataTemplates.FirstOrDefault(r =>
                    string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    r.IsWrite == isWrite);
                StatusText = $"Шаблон ({(isWrite ? "запись DataIn" : "чтение DataOut")}) залит: «{name}» " +
                             $"({body.Length} симв.) — сохранён в БД и добавлен в перечень.";
                _s.Log.Info($"F31: data-шаблон из файла сохранён «{name}» ({(isWrite ? "datain" : "dataout")}; {dlg.FileName})");
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F31: ошибка открытия data-шаблона: {msg}");
                StatusText = $"Ошибка открытия шаблона: {msg}";
            }
        }

        /// <summary>F31: «В редактор» — тело строки перечня: чтение → редактор привязок
        /// Data_Out (режим «Код»); запись → сводка (генерация цепочек записи автоматическая).</summary>
        private void LoadDataTemplate()
        {
            var r = SelectedDataTemplate;
            if (r == null) return;
            if (r.IsWrite)
            {
                var di = DataInTemplates.FirstOrDefault(t =>
                    string.Equals(t.Name, r.Name, StringComparison.OrdinalIgnoreCase));
                SelectedDataInTemplate = di;
                StatusText = di == null
                    ? $"Шаблон записи «{r.Name}» не найден в SCADA_DATAIN_TEMPLATES."
                    : $"Шаблон записи «{r.Name}»: {di.Body?.Length ?? 0} симв., путь: {di.FilePath} " +
                      "(цепочки записи генерируются по нему автоматически — типы/Typno).";
                return;
            }
            SelectedDataOutTemplate = DataOutTemplates.FirstOrDefault(t =>
                string.Equals(t.Name, r.Name, StringComparison.OrdinalIgnoreCase));
            if (SelectedDataOutTemplate == null)
            {
                StatusText = $"Шаблон чтения «{r.Name}» не найден в SCADA_DATAOUT_TEMPLATES.";
                return;
            }
            LoadDataOutTemplateIntoEditor();
        }

        /// <summary>F31: «Удалить» — по направлению строки (подтверждения — в методах F16/F22).</summary>
        private void DeleteDataTemplate()
        {
            var r = SelectedDataTemplate;
            if (r == null) return;
            if (r.IsWrite)
            {
                SelectedDataInTemplate = DataInTemplates.FirstOrDefault(t =>
                    string.Equals(t.Name, r.Name, StringComparison.OrdinalIgnoreCase));
                DeleteDataInTemplate();
            }
            else
            {
                SelectedDataOutTemplate = DataOutTemplates.FirstOrDefault(t =>
                    string.Equals(t.Name, r.Name, StringComparison.OrdinalIgnoreCase));
                DeleteDataOutTemplate();
            }
            RefreshDataTemplates();
        }

        /// <summary>F31: «Из файла…» — мастер-шаблон пакетов из XML-файла в SCADA_MASTER_TEMPLATES
        /// (Protocol — эвристика по имени: «RTU» → RTU, иначе TCP; шаблон становится выбранным).</summary>
        private void OpenMasterTemplate()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "XML шаблон пакетов (*.xml)|*.xml|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                string body = File.ReadAllText(dlg.FileName);
                string name = Path.GetFileNameWithoutExtension(dlg.FileName);
                _s.ModbusBlocks.SaveMasterTemplate(name, body, "", dlg.FileName);
                LoadMasterTemplateList();
                SelectedMasterTemplate = MasterTemplates.FirstOrDefault(t =>
                    string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
                bool rtu = name.Contains("RTU", StringComparison.OrdinalIgnoreCase);
                StatusText = $"Мастер-шаблон «{name}» ({(rtu ? "RTU" : "TCP")}) залит в БД — выберите его " +
                             "как TCP/RTU-шаблон или в правиле станции («Шаблон пакетов»).";
                _s.Log.Info($"F31: мастер-шаблон из файла сохранён «{name}» ({dlg.FileName})");
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F31: ошибка открытия мастер-шаблона: {msg}");
                StatusText = $"Ошибка открытия мастер-шаблона: {msg}";
            }
        }

        /// <summary>F22: «Сохранить текущий…» — текст шаблона блоков (файла/кода) кладётся
        /// в SCADA_MASTER_TEMPLATES; Protocol: имя со «RTU» → RTU, иначе TCP.</summary>
        private void SaveMasterTemplateAs()
        {
            try
            {
                if (_templateText == null) CheckTemplate();
                if (_templateText == null)
                {
                    StatusText = "Сохранение мастер-шаблона невозможно: шаблон не загружен (укажите файл или код).";
                    return;
                }
                string name = (PromptTemplateName(
                    "Введите имя мастер-шаблона (со «RTU» в имени → протокол RTU, иначе TCP):",
                    Path.GetFileNameWithoutExtension(Settings.TemplateFilePath ?? "")) ?? "").Trim();
                if (name.Length == 0)
                {
                    StatusText = "Имя шаблона не задано — мастер-шаблон не сохранён.";
                    return;
                }
                _s.ModbusBlocks.SaveMasterTemplate(name, _templateText!, "",
                    Settings.TemplateFilePath ?? "");
                LoadMasterTemplateList();
                _s.Log.Info($"F22: мастер-шаблон сохранён «{name}» (SCADA_MASTER_TEMPLATES)");
                StatusText = $"Мастер-шаблон сохранён: «{name}» — выберите его в «TCP-шаблон»/«RTU-шаблон».";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F22: ошибка сохранения мастер-шаблона: {msg}");
                StatusText = $"Ошибка сохранения мастер-шаблона: {msg}";
            }
        }

        /// <summary>F22: «В редактор» — тело выбранного мастер-шаблона → режим «Код».</summary>
        private void LoadMasterTemplateIntoEditor()
        {
            var t = SelectedMasterTemplate;
            if (t == null)
            {
                StatusText = "Выберите мастер-шаблон в списке «Мастер-блоки».";
                return;
            }
            InlineTemplateText = t.Body ?? "";
            ModeInline = true;
            if (!string.IsNullOrWhiteSpace(t.FilePath)) Settings.TemplateFilePath = t.FilePath;
            CheckTemplate();
            StatusText = $"Мастер-шаблон «{t.Name}» загружен в редактор: {TemplateStatus}";
        }

        /// <summary>F22: «Удалить» мастер-шаблон (подтверждение; настройки TCP/RTU-выбора
        /// ссылающиеся на него — очищаются).</summary>
        private void DeleteMasterTemplate()
        {
            var t = SelectedMasterTemplate;
            if (t == null)
            {
                StatusText = "Выберите мастер-шаблон в списке «Мастер-блоки».";
                return;
            }
            var ans = System.Windows.MessageBox.Show(
                $"Удалить мастер-шаблон «{t.Name}» ({t.Protocol})? Настройки «TCP-шаблон»/" +
                "«RTU-шаблон», ссылающиеся на него, будут очищены.",
                "Удаление мастер-шаблона",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (ans != System.Windows.MessageBoxResult.Yes) return;
            try
            {
                bool removed = _s.ModbusBlocks.DeleteMasterTemplate(t.Name);
                if (string.Equals(Settings.MasterTcpTemplateName, t.Name, StringComparison.OrdinalIgnoreCase))
                    Settings.MasterTcpTemplateName = "";
                if (string.Equals(Settings.MasterRtuTemplateName, t.Name, StringComparison.OrdinalIgnoreCase))
                    Settings.MasterRtuTemplateName = "";
                LoadMasterTemplateList();
                StatusText = removed
                    ? $"Мастер-шаблон «{t.Name}» удалён."
                    : $"Шаблон «{t.Name}» не найден в SCADA_MASTER_TEMPLATES.";
                _s.Log.Info($"F22: мастер-шаблон удалён «{t.Name}» (найден: {removed})");
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F22: ошибка удаления мастер-шаблона: {msg}");
                StatusText = $"Ошибка удаления мастер-шаблона: {msg}";
            }
        }

        /// <summary>F22: «Из файла…» — залить шаблон привязок DataIn (имя = имя файла;
        /// повторная загрузка того же имени заменяет тело).</summary>
        private void AddDataInFromFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "XML шаблон DataIn (*.xml)|*.xml|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                string body = File.ReadAllText(dlg.FileName);
                string name = Path.GetFileNameWithoutExtension(dlg.FileName);
                _s.ModbusBlocks.SaveDataInTemplate(name, body, dlg.FileName);
                LoadDataInTemplateList();
                StatusText = $"Шаблон DataIn залит: «{name}» ({body.Length} симв.) — SCADA_DATAIN_TEMPLATES.";
                _s.Log.Info($"F22: шаблон DataIn сохранён «{name}» ({dlg.FileName})");
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F22: ошибка заливки шаблона DataIn: {msg}");
                StatusText = $"Ошибка заливки шаблона DataIn: {msg}";
            }
        }

        /// <summary>F22: «Удалить» шаблон DataIn (с подтверждением).</summary>
        private void DeleteDataInTemplate()
        {
            var t = SelectedDataInTemplate;
            if (t == null)
            {
                StatusText = "Выберите шаблон DataIn в списке.";
                return;
            }
            var ans = System.Windows.MessageBox.Show(
                $"Удалить шаблон привязок DataIn «{t.Name}»?",
                "Удаление шаблона DataIn",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (ans != System.Windows.MessageBoxResult.Yes) return;
            try
            {
                bool removed = _s.ModbusBlocks.DeleteDataInTemplate(t.Name);
                LoadDataInTemplateList();
                StatusText = removed
                    ? $"Шаблон DataIn «{t.Name}» удалён."
                    : $"Шаблон DataIn «{t.Name}» не найден.";
                _s.Log.Info($"F22: шаблон DataIn удалён «{t.Name}» (найден: {removed})");
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F22: ошибка удаления шаблона DataIn: {msg}");
                StatusText = $"Ошибка удаления шаблона DataIn: {msg}";
            }
        }

        // ================== F34: маппинг входов мастер-шаблона (F23/F25) убран ==================
        // Входы SlaveId/Mode/Address/Count/TimeOut/IpOctet_1..4/Port заполняются автоматически
        // из плана пакета (ScadaTemplateXmlService.MasterInputValue); недостающие в шаблоне
        // связи синтезируются блоками-константами при экспорте.

        // ================== F33 (этап 7): маппинг объектов data-шаблонов ==================

        /// <summary>F34: ключ маппинга текущего data-шаблона редактора — настройка
        /// DataOutTemplateName (имя файла/сохранённого шаблона), для вставленного кода —
        /// «dataout». Совпадает с ключом поиска dataVars при экспорте.</summary>
        private string CurrentDataTemplateKey()
        {
            string n = (Settings.DataOutTemplateName ?? "").Trim();
            if (n.Length > 0) return n;
            return ModbusBlockPlannerService.InlineDataOutTemplateName;
        }

        /// <summary>F33: перечитать строки маппинга объектов data-шаблонов (SCADA_TEMPLATE_VARS
        /// scope dataout/datain — по каждому сохранённому шаблону). F34: дополнительно строки
        /// ключа текущего шаблона редактора (шаблон мог не сохраняться в перечень).</summary>
        private void LoadDataTemplateVars()
        {
            try
            {
                DataTemplateVars.Clear();
                // F38: текстовые объекты ([TEXT] примитивов основного шаблона) хранятся тем же
                // scope (dataout), но помечены суффиксом «(Text)» в Var — Direction «текст»
                static string DirectionOf(string scope, string varName) =>
                    varName.EndsWith("(Text)", StringComparison.OrdinalIgnoreCase)
                        ? "текст"
                        : scope == "datain" ? "запись" : "чтение";
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in DataOutTemplates)
                    foreach (var v in _s.ModbusBlocks.LoadTemplateVars("dataout", t.Name))
                        if (seen.Add(t.Name + "|" + v.Var))
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            {
                                TemplateName = t.Name, Direction = DirectionOf("dataout", v.Var), Var = v.Var,
                                IoColumn = v.IoColumn, Literal = v.Literal
                            });
                foreach (var t in DataInTemplates)
                    foreach (var v in _s.ModbusBlocks.LoadTemplateVars("datain", t.Name))
                        if (seen.Add(t.Name + "|" + v.Var))
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            {
                                TemplateName = t.Name, Direction = DirectionOf("datain", v.Var), Var = v.Var,
                                IoColumn = v.IoColumn, Literal = v.Literal
                            });
                // F34: текущий шаблон редактора (ключ = DataOutTemplateName) — если его
                // нет в перечне сохранённых, строки маппинга всё равно должны быть видны
                string cur = CurrentDataTemplateKey();
                if (!DataOutTemplates.Any(t => string.Equals(t.Name, cur, StringComparison.OrdinalIgnoreCase)) &&
                    !DataInTemplates.Any(t => string.Equals(t.Name, cur, StringComparison.OrdinalIgnoreCase)))
                    foreach (var v in _s.ModbusBlocks.LoadTemplateVars("dataout", cur))
                        if (seen.Add(cur + "|" + v.Var))
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            {
                                TemplateName = cur, Direction = DirectionOf("dataout", v.Var), Var = v.Var,
                                IoColumn = v.IoColumn, Literal = v.Literal
                            });
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F33: ошибка чтения маппинга объектов data-шаблонов: {msg}");
            }
        }

        /// <summary>F33: дополнить перечень объектами-переменными всех data-шаблонов (блоки
        /// 31/34 вне цепочки источников; функции — вне таблицы); заведённые строки
        /// (шаблон + объект) не затираются. F34: объекты включают блок-приёмник (DCSTAG);
        /// текущий шаблон редактора обходится, даже если не сохранён в перечне.</summary>
        private void FillDataTemplateVars()
        {
            try
            {
                var have = new HashSet<string>(
                    DataTemplateVars.Select(r => r.TemplateName + "|" + r.Var),
                    StringComparer.OrdinalIgnoreCase);
                int added = 0;
                foreach (var t in DataOutTemplates)
                    foreach (var o in _dataOut.ParseTemplateObjects(t.Body))
                        if (have.Add(t.Name + "|" + o.Name))
                        {
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            { TemplateName = t.Name, Direction = "чтение", Var = o.Name });
                            added++;
                        }
                foreach (var t in DataInTemplates)
                    foreach (var o in _dataOut.ParseTemplateObjects(t.Body))
                        if (have.Add(t.Name + "|" + o.Name))
                        {
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            { TemplateName = t.Name, Direction = "запись", Var = o.Name });
                            added++;
                        }
                // F34: текущий шаблон редактора — объекты видны без сохранения в перечень
                string cur = CurrentDataTemplateKey();
                if (!DataOutTemplates.Any(t => string.Equals(t.Name, cur, StringComparison.OrdinalIgnoreCase)) &&
                    !DataInTemplates.Any(t => string.Equals(t.Name, cur, StringComparison.OrdinalIgnoreCase)) &&
                    _dataOutTemplateText != null)
                    foreach (var o in _dataOut.ParseTemplateObjects(_dataOutTemplateText))
                        if (have.Add(cur + "|" + o.Name))
                        {
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            { TemplateName = cur, Direction = "чтение", Var = o.Name });
                            added++;
                        }
                // F38: текстовые объекты примитивов ОСНОВНОГО шаблона — [TEXT]=LOOPComment…
                // (идентификатор — первое слово; маркер объекта «(Text)» в Var), Direction
                // «текст»; хранение — scope dataout под ключом текущего data-шаблона
                var (mainTplText, _) = LoadTemplateText();
                if (mainTplText != null)
                    foreach (var o in _dataOut.ParseTextObjects(mainTplText))
                        if (have.Add(cur + "|" + o))
                        {
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            { TemplateName = cur, Direction = "текст", Var = o });
                            added++;
                        }
                StatusText = added > 0
                    ? $"Объектов data-шаблонов добавлено: {added} (всего {DataTemplateVars.Count}). " +
                      "Задайте колонку/шаблон или литерал и «Сохранить маппинг объектов»."
                    : $"Новых объектов data-шаблонов не найдено (всего {DataTemplateVars.Count}).";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F33: ошибка заполнения объектов data-шаблонов: {msg}");
                StatusText = $"Ошибка заполнения объектов data-шаблонов: {msg}";
            }
        }

        /// <summary>F33: сохранить маппинг объектов (SCADA_TEMPLATE_VARS scope dataout/datain,
        /// полная замена строк [scope][имя шаблона]).</summary>
        private void SaveDataTemplateVars()
        {
            try
            {
                int total = 0;
                foreach (var g in DataTemplateVars
                             .Where(r => r.TemplateName.Trim().Length > 0 && r.Var.Trim().Length > 0)
                             .GroupBy(r => new
                             {
                                 Scope = r.Direction == "запись" ? "datain" : "dataout",
                                 Name = r.TemplateName.Trim()
                             }))
                {
                    var rows = g.Select(r => new MbTemplateVar
                    {
                        Var = r.Var.Trim(), IoColumn = r.IoColumn.Trim(), Literal = r.Literal.Trim()
                    }).ToList();
                    total += _s.ModbusBlocks.SaveTemplateVars(g.Key.Scope, g.Key.Name, rows);
                }
                _s.Log.Info($"F33: маппинг объектов data-шаблонов сохранён: {total} строк");
                StatusText = $"Сохранено строк маппинга объектов: {total}.";
                LoadDataTemplateVars(); // перечитать
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F33: ошибка сохранения маппинга объектов: {msg}");
                StatusText = $"Ошибка сохранения маппинга объектов: {msg}";
            }
        }

        /// <summary>F34: окончание редактирования ячейки превью пакета (CellEditEnding) —
        /// пересчёт зависимых полей: вход Address (формат «регистр + адрес») и привязки
        /// (смещение/путь DataOut/In от начального адреса). Экспорт использует те же
        /// экземпляры MbPacketPlan — правки применяются автоматически.</summary>
        public void NotifyPacketEdited(MbPacketPlan? packet)
        {
            if (packet == null || _built == null) return;
            packet.AddressInput = ScadaTemplateXmlService.FormatBlockAddress(
                packet.Address, packet.FuncCode, Settings.FormatAddress);
            foreach (var b in packet.Bindings)
            {
                b.Offset = b.Address - packet.Address;
                string exprField = packet.IsWrite ? "DataIn" : "DataOut";
                b.DataOutExpr = $"{packet.MasterName}.{exprField}[" +
                    $"{ModbusBlockPlannerService.DataOutByteIndex(b.Offset, packet.IsBit)}]";
            }
            // обновить грид привязок (те же объекты MbBinding — пересоздание строк)
            BindingsPreview.Clear();
            foreach (var pkt in _built.Packets)
                foreach (var b in pkt.Bindings)
                    BindingsPreview.Add(b);
        }
    }
}
