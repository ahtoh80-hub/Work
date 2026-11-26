using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.ViewModels
{
    /// <summary>F13: окно «SCADA → Генерация блоков Modbus (XML)» — XML-шаблон (файл или
    /// вставленный код), планирование пакетов из точек с адресацией Modbus (M_PlcTag),
    /// превью пакетов и привязок «MB_TCP_MASTER_4_1.DataOut[i]», запись привязок в IO_LIST
    /// (M_BusBlock/M_BusDataOut, резервная пара F71 — M_BusBlockRes/M_BusDataOutRes) и экспорт BufScadaPOUS + файла привязок. Настройки —
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

        public ObservableCollection<MbDataTemplateVarRow> MasterObjectVars { get; } = new(); // F101

        /// <summary>F101: MasterObjectVars с фильтром по выбранному мастер-шаблону.</summary>
        public ICollectionView MasterObjectVarsView { get; }

        private MbMasterTemplate? _selectedMasterTemplate;
        /// <summary>F22: выбранный мастер-шаблон в списке.</summary>
        public MbMasterTemplate? SelectedMasterTemplate
        {
            get => _selectedMasterTemplate;
            set
            {
                if (Set(ref _selectedMasterTemplate, value, nameof(SelectedMasterTemplate)))
                    MasterObjectVarsView?.Refresh(); // F101
            }
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

        /// <summary>F46: представление грида «Объекты data-шаблонов» — та же коллекция
        /// DataTemplateVars с фильтром по выделенному data-шаблону в перечне «Data-шаблоны»
        /// (SelectedDataTemplate): показываются только объекты этого шаблона; выделение
        /// снято — все строки. Фильтр — уровень представления: «Заполнить из шаблонов» и
        /// «Сохранить маппинг объектов» работают со всей коллекцией.</summary>
        public ICollectionView DataTemplateVarsView { get; }

        /// <summary>Превью пакетов (по одному блоку мастера на строку). F45: AddRange одним Reset.</summary>
        public RangeObservableCollection<MbPacketPlan> Preview { get; } = new();

        /// <summary>Превью привязок точек к выходам DataOut блоков. F45: AddRange одним Reset.</summary>
        public RangeObservableCollection<MbBinding> BindingsPreview { get; } = new();

        /// <summary>F61: превью пакетов — только станции с включённой галочкой «Генерировать»
        /// (FilterPacketsByStationRules — тот же фильтр, что у экспорта/записи привязок;
        /// станции без правил — генерятся). Строка превью = пакет (F58-грид тегов заменён);
        /// F45: AddRange одним Reset.</summary>
        public RangeObservableCollection<MbPacketPlan> PreviewPackets { get; } = new();

        private MbPacketPlan? _selectedPreviewPacket;
        /// <summary>F61: выбранный пакет превью (мастер-detail): таблица «Привязки DataOut»
        /// внизу показывает привязки этого пакета; снятие выбора (повторный клик по строке —
        /// PreviewGrid_PreviewMouseLeftButtonDown) — все привязки пакетов превью.</summary>
        public MbPacketPlan? SelectedPreviewPacket
        {
            get => _selectedPreviewPacket;
            set
            {
                if (Set(ref _selectedPreviewPacket, value, nameof(SelectedPreviewPacket)))
                    RefreshBindingsView();
            }
        }

        /// <summary>F53: подсказка выбора станций («генерить станций: X из Y») — по галочкам
        /// «Генерировать» в перечне SCADA_MB_SLAVE_RULES.</summary>
        private string _stationsHint = "";
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
            "Привязки (M_BusBlock/M_BusDataOut, M_BusBlockRes/M_BusDataOutRes) пишутся кнопкой «Записать привязки в IO_LIST».";

        private string _statusText = "Загрузите шаблон, настройте пакетирование и нажмите «Построить превью»";
        public string StatusText { get => _statusText; set => Set(ref _statusText, value, nameof(StatusText)); }

        private string _statsText = "";
        public string StatsText { get => _statsText; set => Set(ref _statsText, value, nameof(StatsText)); }

        private string _dataTemplatesText = "";
        /// <summary>F98: строка превью «какой data-шаблон подходит и его версия» — статус
        /// ворот основного шаблона привязок (файл/код, валидность) + соответствие
        /// «Typno плана → именованный шаблон SCADA_DATAOUT_TEMPLATES (версия, дата)».</summary>
        public string DataTemplatesText { get => _dataTemplatesText; set => Set(ref _dataTemplatesText, value, nameof(DataTemplatesText)); }

        private string _nameWarningsText = "";
        /// <summary>F69: красный список сообщений «ИМЯ:» (автозамена спецсимволов в именах
        /// блоков/объектов/марках) — собирается после построения превью и экспорта.</summary>
        public string NameWarningsText
        {
            get => _nameWarningsText;
            set => Set(ref _nameWarningsText, value, nameof(NameWarningsText));
        }

        /// <summary>F69: заменить красный блок предупреждений «ИМЯ:» (список заменяется
        /// каждой операцией: построение превью / экспорт).</summary>
        private void SetNameWarnings(IEnumerable<string>? warnings)
        {
            NameWarningsText = warnings == null
                ? ""
                : string.Join(Environment.NewLine,
                    warnings.Where(w => w != null &&
                        w.StartsWith("ИМЯ:", StringComparison.Ordinal)));
        }

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
        public RelayCommand SaveAllSettingsCommand { get; } // F85 p8: save all settings at once
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
            set
            {
                if (Set(ref _selectedDataTemplate, value, nameof(SelectedDataTemplate)))
                    DataTemplateVarsView?.Refresh(); // F46: переживаем фильтр объектов по шаблону
            }
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
        public RelayCommand FillMasterObjectVarsCommand { get; }   // F101
        public RelayCommand SaveMasterObjectVarsCommand { get; }   // F101

        /// <summary>F16: сохранить текущий текст шаблона привязок под именем (имя спрашивается).</summary>
        public RelayCommand SaveDataOutTemplateAsCommand { get; }

        /// <summary>F16: загрузить выбранный именованный шаблон в редактор.</summary>
        public RelayCommand LoadDataOutTemplateCommand { get; }

        /// <summary>F81: удалить выбранный именованный шаблон.</summary>
        public RelayCommand DeleteDataOutTemplateCommand { get; }

        private MbSlaveIdRule? _selectedRule;
        public MbSlaveIdRule? SelectedRule
        {
            get => _selectedRule;
            set
            {
                if (ReferenceEquals(_selectedRule, value)) return;
                // F58: подписка на правку Контроллера/Станции выбранного правила (INPC) —
                // превью тегов пересобирается сразу при редактировании ячейки в гриде
                if (_selectedRule != null) _selectedRule.PropertyChanged -= OnSelectedRuleChanged;
                if (value != null) value.PropertyChanged += OnSelectedRuleChanged;
                Set(ref _selectedRule, value, nameof(SelectedRule));
                RefreshPreviewPackets(); // F61: ключ правила участвует в фильтре Generate-станций
            }
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
            // F46: фильтр грида «Объекты data-шаблонов» — только объекты выделенного
            // data-шаблона из перечня «Data-шаблоны»; выделение снято — все строки
            DataTemplateVarsView = CollectionViewSource.GetDefaultView(DataTemplateVars);
            DataTemplateVarsView.Filter = o =>
                _selectedDataTemplate == null ||
                (o is MbDataTemplateVarRow row && string.Equals(
                    row.TemplateName, _selectedDataTemplate.Name,
                    StringComparison.OrdinalIgnoreCase));
            MasterObjectVarsView = CollectionViewSource.GetDefaultView(MasterObjectVars);
            MasterObjectVarsView.Filter = o =>
                _selectedMasterTemplate == null ||
                (o is MbDataTemplateVarRow row && string.Equals(
                    row.TemplateName, _selectedMasterTemplate.Name,
                    StringComparison.OrdinalIgnoreCase));
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
            SaveAllSettingsCommand = new RelayCommand(SaveAllSettings); // F85 p8
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
            FillMasterObjectVarsCommand = new RelayCommand(FillMasterObjectVars);  // F101
            SaveMasterObjectVarsCommand = new RelayCommand(SaveMasterObjectVars);  // F101
            SelectAllStationsCommand = new RelayCommand(() => SetAllStations(true));   // F28
            ClearAllStationsCommand = new RelayCommand(() => SetAllStations(false));   // F28
            SaveDataOutTemplateAsCommand = new RelayCommand(SaveDataOutTemplateAs);
            LoadDataOutTemplateCommand = new RelayCommand(LoadDataOutTemplateIntoEditor);
            DeleteDataOutTemplateCommand = new RelayCommand(DeleteDataOutTemplate);
            // F45: PERF-замеры загрузки (открытие окна F13) — этапы в журнал INFO;
            // узкое место до оптимизации — EnsureTables на каждом Load* (см. IOclass.md F45)
            var swPerf = System.Diagnostics.Stopwatch.StartNew();
            var swStage = System.Diagnostics.Stopwatch.StartNew();
            void Perf(string stage) => _s.Log.Info($"F45 PERF: конструктор F13/{stage} — " +
                $"{swStage.ElapsedMilliseconds} мс; с начала — {swPerf.ElapsedMilliseconds} мс");
            LoadRules();                                Perf("LoadRules");
            swStage.Restart();
            LoadTypnoRules();                           Perf("LoadTypnoRules");
            swStage.Restart();
            LoadDataOutTemplateList();                  Perf("LoadDataOutTemplateList"); // F16
            swStage.Restart();
            LoadMasterTemplateList();                   Perf("LoadMasterTemplateList");  // F22
            swStage.Restart();
            LoadDataInTemplateList();                   Perf("LoadDataInTemplateList");  // F22
            swStage.Restart();
            LoadDataTemplateVars();                     // F33 (этап 7): PERF — внутри метода
            LoadMasterObjectVars(); // F101
            swStage.Restart();
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
            CheckTemplate();                            Perf("CheckTemplate"); // автозагрузка шаблона
            swStage.Restart();
            CheckDataOutTemplate();                     Perf("CheckDataOutTemplate"); // F13.1
            _s.Log.Info($"F45 PERF: конструктор F13 (итого) — {swPerf.ElapsedMilliseconds} мс");
            StatusText = $"Настройки загружены. Правил «станция → SlaveId»: {SlaveRules.Count}. " +
                         "Постройте превью для просмотра пакетов.";
        }

        /// <summary>Прочитать правила SlaveId из SCADA_MB_SLAVE_RULES.</summary>
        private void LoadRules()
        {
            try
            {
                SlaveRules.Clear();
                foreach (var r in _s.ModbusBlocks.LoadSlaveRules())
                {
                    HookStationRule(r); // F53: галочка «Генерировать» → подсказка
                    SlaveRules.Add(r);
                }
                RenumberSlaveRules(); // F53: колонка «№»
                UpdateStationsHint(); // F53
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
                // F88 (Этап 0): файл не найден — fallback на мастер-шаблон из БД
                // (SCADA_MASTER_TEMPLATES по MasterTemplateName); позволяет работать
                // без привязки к путям файлов (задача п.7: хранение только в БД)
                var fb = TryLoadMasterTemplateFromDb();
                if (fb != null)
                    return (fb, $"БД «{(Settings.MasterTcpTemplateName ?? "").Trim()}» " +
                        $"(файл не найден: {Settings.TemplateFilePath})");
                return (null, $"файл по пути не найден: {Settings.TemplateFilePath}");
            }
            string? path = ScadaTemplateXmlService.ResolveDefaultTemplatePath();
            if (path != null)
                return (File.ReadAllText(path), path);
            // F88: авто-поиск не дал результата — fallback на БД
            var fb2 = TryLoadMasterTemplateFromDb();
            if (fb2 != null)
                return (fb2, $"БД «{(Settings.MasterTcpTemplateName ?? "").Trim()}»");
            return (null, "шаблон не найден — укажите файл или вставьте код");
        }

        /// <summary>F88: попытка загрузить тело мастер-шаблона из БД (SCADA_MASTER_TEMPLATES)
        /// по имени Settings.MasterTemplateName; null — нет шаблона или тело пустое.</summary>
        private string? TryLoadMasterTemplateFromDb()
        {
            string name = (Settings.MasterTcpTemplateName ?? "").Trim();
            if (name.Length == 0) return null;
            var tpl = MasterTemplates.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            return tpl != null && !string.IsNullOrWhiteSpace(tpl.Body) ? tpl.Body : null;
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
        /// не найден. F107: файл по пути недоступен (перенос БД на другой компьютер —
        /// в настройках путь старого файла) → fallback на именованный шаблон из БД
        /// (SCADA_DATAOUT_TEMPLATES по DataOutTemplateName), как это уже сделано для
        /// мастер-шаблона (F88); иначе dataTpValid=false и экспорт уходит в «простые
        /// привязки» без data-цепочек по Typno.</summary>
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
                // F107: файл не найден — fallback на именованный шаблон из БД
                // (задача п.7: шаблоны живут в БД, пути не должны ломать генерацию)
                var fb = TryLoadDataOutTemplateFromDb();
                if (fb != null)
                    return (fb, $"БД «{(Settings.DataOutTemplateName ?? "").Trim()}» " +
                        $"(файл не найден: {Settings.DataOutTemplateFilePath})");
                return (null, $"файл по пути не найден: {Settings.DataOutTemplateFilePath}");
            }
            string? path = ScadaDataOutXmlService.ResolveDefaultTemplatePath();
            if (path != null)
                return (File.ReadAllText(path), path);
            // F107: авто-поиск не дал результата — fallback на БД (F81/F16)
            var fb2 = TryLoadDataOutTemplateFromDb();
            if (fb2 != null)
                return (fb2, $"БД «{(Settings.DataOutTemplateName ?? "").Trim()}»");
            return (null, "шаблон привязок не найден — укажите файл или вставьте код");
        }

        /// <summary>F107: попытка загрузить тело dataout-шаблона из БД (SCADA_DATAOUT_TEMPLATES)
        /// по имени Settings.DataOutTemplateName; null — имя не задано, шаблона нет или тело
        /// пустое (аналог TryLoadMasterTemplateFromDb, F88).</summary>
        private string? TryLoadDataOutTemplateFromDb()
        {
            string name = (Settings.DataOutTemplateName ?? "").Trim();
            if (name.Length == 0) return null;
            var tpl = DataOutTemplates.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            return tpl != null && !string.IsNullOrWhiteSpace(tpl.Body) ? tpl.Body : null;
        }

        /// <summary>F13.1: загрузить и провалидировать шаблон привязок (кнопка «Проверить»).</summary>
        private void CheckDataOutTemplate()
        {
            try
            {
                var (text, source) = LoadDataOutTemplateText();
                _dataOutTemplateText = text;
                // F107: явная отметка в журнале, что путь файла недоступен и шаблон
                // подхвачен из БД (типичный симптом переноса базы на другой компьютер)
                if (source.Contains("файл не найден", StringComparison.Ordinal))
                    _s.Log.Warn($"F107: {source} — подхвачен шаблон привязок из SCADA_DATAOUT_TEMPLATES");
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
                LoadMasterObjectVars(); // F101
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
                // F81: источник — путь только в режиме «Файл» (имя без пути), иначе пусто →
                // сохранится прежнее имя источника
                _s.ModbusBlocks.SaveDataOutTemplate(name, text,
                    ModeDataOutFile ? Settings.DataOutTemplateFilePath ?? "" : "");
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
            // F81: FilePath — только имя файла-источника (без пути), в настройки «Файл» не пишем
            CheckDataOutTemplate();
            StatusText = $"Шаблон «{t.Name}» загружен в редактор: {DataOutTemplateStatus}";
        }

        /// <summary>F81: тело выбранного data-шаблона для окна редактора кода
        /// (null — нет строки или тела в БД).</summary>
        public string? GetDataTemplateCode(MbDataTemplateRow? r)
        {
            if (r == null)
            {
                StatusText = "Выберите шаблон в перечне «Data-шаблоны».";
                return null;
            }
            string? body = r.IsWrite
                ? DataInTemplates.FirstOrDefault(t => string.Equals(t.Name, r.Name, StringComparison.OrdinalIgnoreCase))?.Body
                : DataOutTemplates.FirstOrDefault(t => string.Equals(t.Name, r.Name, StringComparison.OrdinalIgnoreCase))?.Body;
            if (body == null)
            {
                StatusText = $"Шаблон «{r.Name}» не найден в БД — обновите перечень.";
                return null;
            }
            return body;
        }

        /// <summary>F81: сохранить отредактированное тело data-шаблона в БД
        /// (Version +1, SavedAt; путь файла-источника не используется).</summary>
        public void SaveTemplateCode(MbDataTemplateRow? r, string text, string? newName = null, int? version = null)
        {
            if (r == null) return;
            try
            {
                string name = string.IsNullOrWhiteSpace(newName) ? r.Name : newName.Trim();
            if (r.IsWrite) _s.ModbusBlocks.SaveDataInTemplate(name, text, null, version);
                else _s.ModbusBlocks.SaveDataOutTemplate(name, text, null, version);
                LoadDataOutTemplateList();
                LoadDataInTemplateList();
                SelectedDataTemplate = DataTemplates.FirstOrDefault(x =>
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && x.Kind == r.Kind);
                _s.Log.Info($"F81: код шаблона «{r.Name}» сохранён в БД");
                StatusText = $"Шаблон «{r.Name}» сохранён в БД (версия обновлена).";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F81: ошибка сохранения кода шаблона: {msg}");
                StatusText = $"Ошибка сохранения шаблона: {msg}";
            }
        }

        /// <summary>F81: тело выбранного мастер-шаблона для окна редактора кода.</summary>
        public string? GetMasterTemplateCode()
        {
            var t = SelectedMasterTemplate;
            if (t == null)
            {
                StatusText = "Выберите мастер-шаблон в перечне.";
                return null;
            }
            return t.Body ?? "";
        }

        /// <summary>F81: сохранить отредактированный код мастер-шаблона в БД
        /// (Version +1, SavedAt; Protocol сохраняется).</summary>
        public void SaveMasterTemplateCode(string text, string? newName = null, int? version = null)
        {
            var t = SelectedMasterTemplate;
            if (t == null) return;
            try
            {
                string name = string.IsNullOrWhiteSpace(newName) ? t.Name : newName.Trim();
            _s.ModbusBlocks.SaveMasterTemplate(name, text, t.Protocol, null, version);
                LoadMasterTemplateList();
                SelectedMasterTemplate = MasterTemplates.FirstOrDefault(x =>
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                _s.Log.Info($"F81: код мастер-шаблона «{t.Name}» сохранён в БД");
                StatusText = $"Мастер-шаблон «{t.Name}» сохранён в БД (версия обновлена).";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F81: ошибка сохранения кода мастер-шаблона: {msg}");
                StatusText = $"Ошибка сохранения мастер-шаблона: {msg}";
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
            var swPerf = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (_template == null || !_template.Valid) CheckTemplate();
                if (_template == null || !_template.Valid)
                {
                    StatusText = $"Превью не построено: {_template?.Error ?? "шаблон не загружен"}.";
                    return;
                }
                var swStage = System.Diagnostics.Stopwatch.StartNew();
                var points = _s.Repo.GetAll();
                _s.Log.Info($"F45 PERF: Build/Repo.GetAll ({points.Count} точек) — {swStage.ElapsedMilliseconds} мс");
                swStage.Restart();
                var rules = SlaveRules.Where(r => !string.IsNullOrWhiteSpace(r.Station)).ToList();
                // F33 (этап 6): размеры переменных правил Typno (Size) — приоритет над
                // M_DataType в ширине пакета (бит/8/16bit → 1 регистр, 32 → 2, 64 → 4)
                var sizeByTypno = new System.Collections.Generic.Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var r in TypnoRules)
                    if (r.Typno.Trim().Length > 0 && !sizeByTypno.ContainsKey(r.Typno.Trim()))
                        sizeByTypno[r.Typno.Trim()] = MbTypnoMapRule.NormalizeSize(r.Size);
                var result = _s.ModbusBlocks.BuildPlan(points, Settings, rules, _template.MasterNameBase, sizeByTypno);
                _s.Log.Info($"F45 PERF: Build/BuildPlan ({result.Packets.Count} пакетов) — {swStage.ElapsedMilliseconds} мс");
                foreach (var w in result.Warnings) _s.Log.Warn(w); // F69: предупреждения в журнал
                SetNameWarnings(result.Warnings); // F69: красный блок «ИМЯ:»
                swStage.Restart();
                _built = result;
                // F45: батч-заполнение одним событием Reset вместо N CollectionChanged
                Preview.Clear();
                Preview.AddRange(result.Packets);
                RefreshPreviewPackets(); // F61: превью = пакеты Generate-станций + привязки внизу
                _s.Log.Info($"F45 PERF: Build/заполнение превью ({Preview.Count} пакетов, " +
                            $"{BindingsPreview.Count} привязок) — {swStage.ElapsedMilliseconds} мс");
                RefreshStationPacketsHint(); // F53: подсказка «Генерить станций X из Y» + пакетов
                StatsText = $"Пакетов (блоков мастера): {result.Packets.Count} " +
                            $"(в т.ч. резервных reserve: {result.ReservePackets}); " + // F68
                            $"привязок DataOut: {BindingsPreview.Count}; " +
                            $"точек в пакетах: {result.TotalPoints} из {result.WithAddressing} с адресацией; " +

                            $"дублей адресации (одна точка): {result.DedupedSignals}; " +
                            $"пропущено: без адресации {result.SkippedNoAddressing}, " +
                            $"без Typno {result.SkippedNoTypno}, " +
                            $"запись {result.SkippedWrite}, адрес {result.SkippedBadAddress}, " +
                            $"IP {result.SkippedBadEndpoint}, функция {result.SkippedBadFunc}; " +
                            $"Typno без маппинга: {CountUnmappedTypno(result)} (F33: привязки пропускаются); " +
                            $"предупреждений: {result.Warnings.Count}";
                // F98: какой data-шаблон подходит и его версия — статус ворот (файл/код,
                // включая «файл по пути не найден») + Typno → именованный шаблон
                // SCADA_DATAOUT_TEMPLATES (Version/SavedAt)
                CheckDataOutTemplate();
                DataTemplatesText = BuildDataTemplatesText(CollectPlanTypnos(result),
                    TypnoRules, DataOutTemplates, DataOutTemplateStatus);
                StatusText = result.Packets.Count > 0
                    ? "Превью построено. Кнопки: «Записать привязки в IO_LIST», «Сформировать XML...»."
                    : "Пакеты не сформированы — проверьте адресацию Modbus (M_PlcTag) в таблице.";
                _s.Log.Info($"F45 PERF: Build (итого) — {swPerf.ElapsedMilliseconds} мс");
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F13: ошибка планирования: {msg}");
                StatusText = $"Ошибка планирования: {msg}";
            }
        }

        /// <summary>F53: подписка на правило станции — обновление подсказки при переключении
        /// галочки «Генерировать» (Generate — INPC, TwoWay-привязка пишет напрямую).</summary>
        private void HookStationRule(MbSlaveIdRule r)
        {
            r.PropertyChanged -= OnStationRuleChanged;
            r.PropertyChanged += OnStationRuleChanged;
        }

        /// <summary>F53: изменение правила станции → пересчёт подсказки; галочка
        /// «Генерировать» дополнительно пересобирает превью пакетов (F61).</summary>
        private void OnStationRuleChanged(object? sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MbSlaveIdRule.Generate))
            {
                UpdateStationsHint();
                RefreshPreviewPackets(); // F61: фильтр превью — по галочкам станций
            }
        }

        /// <summary>F61: правка Контроллера/Станции у ВЫБРАННОГО правила (INPC) — пересборка
        /// превью пакетов (ключи правила участвуют в FilterPacketsByStationRules).</summary>
        private void OnSelectedRuleChanged(object? sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MbSlaveIdRule.Station) or nameof(MbSlaveIdRule.Controller))
                RefreshPreviewPackets();
        }

        /// <summary>F53: нумерация строк перечня станций (колонка «№» грида).</summary>
        private void RenumberSlaveRules()
        {
            for (int i = 0; i < SlaveRules.Count; i++) SlaveRules[i].No = i + 1;
        }

        /// <summary>F53: «Выбрать все»/«Снять все» — галочки «Генерировать» перечня станций
        /// (SCADA_MB_SLAVE_RULES; кнопки в тулбаре грида станций).</summary>
        private void SetAllStations(bool selected)
        {
            foreach (var r in SlaveRules) r.Generate = selected;
            UpdateStationsHint();
            RefreshPreviewPackets(); // F61: массовые галочки пересобирают превью пакетов
        }

        /// <summary>F53: подсказка «генерить станций: X из Y» по галочкам перечня станций.</summary>
        private void UpdateStationsHint()
        {
            int sel = SlaveRules.Count(r => r.Generate);
            StationsHint = SlaveRules.Count > 0
                ? $"Генерить станций: {sel} из {SlaveRules.Count}" +
                  (_built != null ? $"; пакетов в плане: {_built.Packets.Count}" : "")
                : "Правила станций не заданы — генерятся все станции плана.";
        }

        /// <summary>F53: обновить подсказку после построения превью.</summary>
        private void RefreshStationPacketsHint() => UpdateStationsHint();

        /// <summary>F61: превью = пакеты станций с включённой галочкой «Генерировать»
        /// (FilterPacketsByStationRules: точный ключ «Контроллер|Станция», затем
        /// универсальный; станции без правил — генерятся). Выбор пакета сбрасывается,
        /// привязки внизу — все привязки пакетов превью. Пересборка — на «Построить
        /// превью», переключении галочек/кнопок станций, смене/правке правила (F61).</summary>
        private void RefreshPreviewPackets()
        {
            PreviewPackets.Clear();
            Set(ref _selectedPreviewPacket, null, nameof(SelectedPreviewPacket));
            if (_built == null) { BindingsPreview.Clear(); return; }
            var packets = ModbusBlockPlannerService.FilterPacketsByStationRules(
                _built.Packets, SlaveRules);
            PreviewPackets.AddRange(packets);
            RefreshBindingsView(); // F61: привязки без выбора — всех пакетов превью
            _s.Log.Info($"F61: превью — пакетов {PreviewPackets.Count} из {_built.Packets.Count}, " +
                        $"привязок {BindingsPreview.Count}");
        }

        /// <summary>F61: таблица «Привязки DataOut» (мастер-detail) — привязки выбранного
        /// пакета превью; выбора нет — все привязки пакетов, отображённых в превью.</summary>
        private void RefreshBindingsView()
        {
            BindingsPreview.Clear();
            if (_built == null) return;
            System.Collections.Generic.IEnumerable<MbBinding> binds = SelectedPreviewPacket != null
                ? SelectedPreviewPacket.Bindings
                : PreviewPackets.SelectMany(p => p.Bindings);
            BindingsPreview.AddRange(binds);
        }

        /// <summary>F53: пакеты плана без станций, отключённых галочкой «Генерировать»
        /// (ModbusBlockPlannerService.FilterPacketsByStationRules: точный ключ
        /// «Контроллер|Станция», затем универсальный «Станция»); все станции отключены —
        /// null (ошибка экспорта/записи привязок).</summary>
        private List<MbPacketPlan>? SelectedStationPackets()
        {
            if (_built == null) return null;
            var filtered = ModbusBlockPlannerService.FilterPacketsByStationRules(
                _built.Packets, SlaveRules);
            if (filtered.Count == 0 && _built.Packets.Count > 0) return null;
            return filtered;
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

        /// <summary>F13/F71: «Записать привязки в IO_LIST» — M_BusBlock/M_BusDataOut и
        /// M_BusBlockRes/M_BusDataOutRes (резервная пара, F68) по плану
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
                    StatusText = "Запись привязок невозможна: сняты все галочки «Генерировать» в перечне станций.";
                    return;
                }
                var r = _s.ModbusBlocks.WriteBindings(plan, _s.Repo);
                _s.Log.Info($"F13: {r.Summary}");
                foreach (var e in r.Errors) _s.Log.Warn(e);
                foreach (var w in r.Warnings) _s.Log.Warn(w); // п.8
                string msg8 = r.Summary;
                if (r.Warnings.Count > 0) msg8 += "\n\n" + string.Join("\n", r.Warnings.Take(10));
                if (r.Errors.Count > 0) msg8 += "\n\n" + string.Join("\n", r.Errors.Take(10));
                System.Windows.MessageBox.Show(
                    msg8,
                    "Привязки Modbus (IO_LIST)",
                    System.Windows.MessageBoxButton.OK,
                    (r.Errors.Count > 0 || r.Warnings.Count > 0) ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Information);
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

                // F53: экспорт только станций с галочкой «Генерировать» (перечень правил)
                var packets = SelectedStationPackets();
                if (packets == null)
                {
                    StatusText = "Экспорт невозможен: сняты все галочки «Генерировать» в перечне станций.";
                    return;
                }
                int stationsTotal = SlaveRules.Count;
                int stSel = SlaveRules.Count(r => r.Generate); // F53

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
                MarkGenerated(packets);
                    return;
                }

                // F14/F16/F33/F37: карты Typno/шаблонов/dataVars — общий хелпер
                // (тот же набор использует split-экспорт ExportSplit);
                // F38: dataVars нужен и WriteProtocolSplit — тексты примитивов [TEXT]
                var (typnoMap, namedTemplates, dataInTemplates, dataVars) = BuildDataMaps();
                var masterObjMap = BuildMasterObjMap(); // F101

                var gen = _s.ModbusBlocksXml.WriteProtocolSplit(
                    packets, templateByStation, _templateText!, rtuText, Settings,
                    dataVars, Settings.DataOutTemplateName, masterObjMap);
                foreach (var w in gen.Warnings) _s.Log.Warn(w);
                var nameWarns = new List<string>(gen.Warnings); // F69: красный блок «ИМЯ:»
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
                    bindingsXml = _dataOut.WriteMulti(packets, _dataOutTemplateText,
                        gen.MasterMeta, Settings, warnings, typnoMap, namedTemplates, dataInTemplates,
                        dataVars, Settings.DataOutTemplateName,
                        _template?.MasterNameBase,      // F69: main-токен подмены имён
                        _template?.ReserveTokenBase);   // F70: резервный токен (2-й мастер)
                    fromTemplate = true;
                    foreach (var w in warnings) _s.Log.Warn(w);
                    nameWarns.AddRange(warnings);
                }
                SetNameWarnings(nameWarns); // F69: красный блок «ИМЯ:»
                File.WriteAllText(bindingsPath, bindingsXml, new System.Text.UTF8Encoding(true));
                MarkGenerated(packets);
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
            var masterObjMap = BuildMasterObjMap(); // F101
            bool dataTplValid = _dataOutTemplateText != null && _dataOutTemplate != null && _dataOutTemplate.Valid;
            if (!dataTplValid) CheckDataOutTemplate();
            dataTplValid = _dataOutTemplateText != null && _dataOutTemplate != null && _dataOutTemplate.Valid;
            var utf8 = new System.Text.UTF8Encoding(true);
            var settings = Settings.Clone();
            int seq = 0;
            var written = new List<string>();
            var nameWarns69 = new List<string>(); // F69: красный блок «ИМЯ:» по всем файлам

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
                    dataVars, Settings.DataOutTemplateName, masterObjMap); // F38: тексты примитивов [TEXT]
                foreach (var w in gen.Warnings) _s.Log.Warn(w);
                nameWarns69.AddRange(gen.Warnings); // F69
                string blockPath = Path.Combine(dir, $"{ctrlPart}_{stPart}_{seq}.xml");
                File.WriteAllText(blockPath, gen.PousXml, utf8);
                written.Add(blockPath);
                settings = ScadaTemplateXmlService.ShiftSettingsForNextFile(
                    settings, gen.PousXml, gen.BindingsXml);

                // —— файлы data-объектов: по одному на (Typno, шаблон привязок) внутри пары ——
                // F51: Typno может иметь несколько правил (шаблонов) — цепочки каждого шаблона
                // генерятся в свой отдельный XML-файл (импорт в разные POU); в WriteMulti
                // передаётся карта только с правилами текущего шаблона (пустое имя шаблона
                // в правиле — основной шаблон редактора, в имени файла — «main»)
                if (dataTplValid)
                {
                    var warnings = new System.Collections.Generic.List<string>();
                    foreach (var tg in groupPackets.GroupBy(p =>
                                 p.Bindings.Count > 0 ? p.Bindings[0].Typno.Trim() : "")
                             .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (tg.Key.Length == 0) continue;
                        var byTpl = new Dictionary<string,
                            List<(string Template, string Exemplar, string Size)>>(
                            StringComparer.OrdinalIgnoreCase);
                        if (typnoMap.TryGetValue(tg.Key, out var rulesF51))
                            foreach (var r in rulesF51)
                            {
                                string tn = r.Template.Length > 0 ? r.Template : "main";
                                if (!byTpl.TryGetValue(tn, out var listF51))
                                    byTpl[tn] = listF51 = new List<(string Template, string Exemplar, string Size)>();
                                listF51.Add(r);
                            }
                        foreach (var ftg in byTpl.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            seq++;
                            string typnoPart = SanitizeFilePart(tg.Key, "NoTypno");
                            string tplPart = SanitizeFilePart(ftg.Key, "main");
                            // F51: карта только с правилами текущего шаблона — цепочки
                            // других шаблонов Typno в этот файл не попадают
                            var mapF51 = new Dictionary<string,
                                List<(string Template, string Exemplar, string Size)>>(
                                StringComparer.OrdinalIgnoreCase) { [tg.Key] = ftg.Value };
                            string dataXml = _dataOut.WriteMulti(tg.ToList(), _dataOutTemplateText!,
                                gen.MasterMeta, settings, warnings, mapF51, namedTemplates,
                                dataInTemplates, dataVars, Settings.DataOutTemplateName,
                                _template?.MasterNameBase,      // F69: main-токен подмены имён
                                _template?.ReserveTokenBase);   // F70: резервный токен
                            string dataPath = Path.Combine(dir,
                                $"{ctrlPart}_{stPart}_{typnoPart}_{tplPart}_{seq}.xml");
                            File.WriteAllText(dataPath, dataXml, utf8);
                            written.Add(dataPath);
                            settings = ScadaTemplateXmlService.ShiftSettingsForNextFile(settings, dataXml);
                        }
                    }
                    foreach (var w in warnings) _s.Log.Warn(w);
                    nameWarns69.AddRange(warnings); // F69
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

            int stSel53 = SlaveRules.Count(r => r.Generate); // F53
            int stationsTotal53 = SlaveRules.Count;
            _s.Log.Info($"F37: split-экспорт в «{dir}»: файлов {written.Count} " +
                        $"(станций: {stSel53}/{stationsTotal53}, пакетов: {packets.Count}): " +
                        string.Join(", ", written.Select(Path.GetFileName)));
            StatusText = $"Файлов сформировано: {written.Count} в папке {dir} " +
                         $"(станций {stSel53}/{stationsTotal53}, пакетов {packets.Count}; " +
                         (dataTplValid ? "блоки Modbus + data-объекты по Typno)." : "простые привязки).");
            SetNameWarnings(nameWarns69); // F69: красный блок «ИМЯ:»
        }

        /// <summary>F37: карты генерации data-привязок — «Typno → список (шаблон, эталон, размер)»
        /// (SCADA_TYPNO_MAP; F51: несколько правил на Typno — по одному на шаблон), именованные
        /// dataout- (F16) и datain-шаблоны (F30) и маппинг объектов data-шаблонов
        /// (F33/F37 «шаблон → объект → (колонка, литерал)»).</summary>
        private (Dictionary<string, List<(string Template, string Exemplar, string Size)>> Typno,
                 Dictionary<string, string> Named,
                 Dictionary<string, string> DataIn,
                 Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal, string BitMode, string BitNo)>> Vars)
            BuildDataMaps()
        {
            // F51: один Typno — несколько правил (по шаблону); порядок строк сохраняется
            var typnoMap = new System.Collections.Generic.Dictionary<string,
                List<(string Template, string Exemplar, string Size)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in TypnoRules)
                if (r.Typno.Trim().Length > 0 && r.Exemplar.Trim().Length > 0)
                {
                    string key = r.Typno.Trim();
                    if (!typnoMap.TryGetValue(key, out var listRules))
                        typnoMap[key] = listRules = new List<(string Template, string Exemplar, string Size)>();
                    listRules.Add((r.Template.Trim(), r.Exemplar.Trim(),
                        MbTypnoMapRule.NormalizeSize(r.Size)));
                }
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
                    (string IoColumn, string Literal, string BitMode, string BitNo)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in DataTemplateVars)
            {
                string tn = row.TemplateName.Trim();
                string vn = row.Var.Trim();
                if (tn.Length == 0 || vn.Length == 0) continue;
                System.Collections.Generic.Dictionary<string,
                    (string IoColumn, string Literal, string BitMode, string BitNo)> m;
                if (dataVars.TryGetValue(tn, out var existing))
                    m = (System.Collections.Generic.Dictionary<string,
                        (string IoColumn, string Literal, string BitMode, string BitNo)>)existing;
                else
                {
                    m = new System.Collections.Generic.Dictionary<string,
                        (string IoColumn, string Literal, string BitMode, string BitNo)>(StringComparer.OrdinalIgnoreCase);
                    dataVars[tn] = m;
                }
                m[vn] = (row.IoColumn.Trim(), row.Literal.Trim(),
                    row.BitMode.Trim(), row.BitNo.Trim());
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
                        // F81: источник — путь только в режиме «Файл» (имя без пути)
                        _s.ModbusBlocks.SaveDataOutTemplate(dName, dText,
                            ModeDataOutFile ? Settings.DataOutTemplateFilePath ?? "" : "");
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

        /// <summary>F85 (p.8): one button saves generation settings + SlaveId rules + Typno map.</summary>
        private void SaveAllSettings()
        {
            SaveSettings();
            SaveRules();
            SaveTypnoMap();
            StatusText = "Настройки сохранены.";
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
            HookStationRule(r); // F53
            SlaveRules.Add(r);
            RenumberSlaveRules(); // F53
            SelectedRule = r;
            StatusText = $"Правило добавлено (не сохранено): {SlaveRules.Count}. Не забудьте «Сохранить правила SlaveId».";
        }

        private void DeleteRule()
        {
            if (SelectedRule == null) return;
            SlaveRules.Remove(SelectedRule);
            SelectedRule = null;
            RenumberSlaveRules(); // F53
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
            {
                if (p.IsReserve) continue; // F68: twin дублирует привязки main-пакета
                foreach (var b in p.Bindings)
                    if (b.Typno.Length > 0 && !mapped.Contains(b.Typno)) n++;
            }
            return n;
        }

        /// <summary>F98: Typno привязок превью (без reserve-дублей F68), по алфавиту;
        /// пустые Typno исключаются — именно они участвуют в генерации data-шаблонов.</summary>
        private static List<string> CollectPlanTypnos(MbPlanResult result)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var p in result.Packets)
            {
                if (p.IsReserve) continue;
                foreach (var b in p.Bindings)
                    if (b.Typno.Length > 0 && seen.Add(b.Typno)) list.Add(b.Typno);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>F98: строка превью «data-шаблоны для генерации»: статус ворот основного
        /// шаблона (gateStatus) + по каждому Typno плана — правило SCADA_TYPNO_MAP →
        /// именованный шаблон SCADA_DATAOUT_TEMPLATES с версией и датой сохранения;
        /// проблемы помечаются ⚠ (шаблон не найден в БД, Typno без правила).
        /// Публичный чистый хелпер — покрывается смоук-проверками F98.</summary>
        public static string BuildDataTemplatesText(
            IReadOnlyCollection<string> planTypnos,
            IEnumerable<MbTypnoMapRule> rules,
            IEnumerable<MbDataOutTemplate> templates,
            string gateStatus)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Data-шаблоны: ")
              .Append(string.IsNullOrWhiteSpace(gateStatus) ? "статус шаблона неизвестен" : gateStatus);
            var byTypno = new Dictionary<string, List<MbTypnoMapRule>>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rules)
            {
                string tn = (r.Typno ?? "").Trim();
                if (tn.Length == 0 || string.IsNullOrWhiteSpace(r.Exemplar)) continue;
                if (!byTypno.TryGetValue(tn, out var list)) byTypno[tn] = list = new List<MbTypnoMapRule>();
                list.Add(r);
            }
            var tplByName = new Dictionary<string, MbDataOutTemplate>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in templates)
                if (!string.IsNullOrWhiteSpace(t.Name)) tplByName[t.Name] = t;
            var noRule = new List<string>();
            foreach (var tn in planTypnos)
            {
                if (!byTypno.TryGetValue(tn, out var rs))
                {
                    noRule.Add(tn);
                    continue;
                }
                foreach (var r in rs)
                {
                    string tplName = (r.Template ?? "").Trim();
                    if (tplName.Length == 0)
                    {
                        sb.Append("; ").Append(tn).Append(" → основной шаблон");
                        continue;
                    }
                    if (tplByName.TryGetValue(tplName, out var t))
                    {
                        string ver = t.Version > 0 ? "v" + t.Version : "без версии";
                        string when = string.IsNullOrWhiteSpace(t.SavedAt) ? "" : " (" + t.SavedAt + ")";
                        sb.Append("; ").Append(tn).Append(" → ").Append(t.Name)
                          .Append(' ').Append(ver).Append(when);
                    }
                    else
                        sb.Append("; ⚠ ").Append(tn).Append(" → «").Append(tplName)
                          .Append("» не найден в SCADA_DATAOUT_TEMPLATES");
                }
            }
            if (noRule.Count > 0)
                sb.Append("; ⚠ без правила Typno: ").Append(string.Join(", ", noRule));
            if (planTypnos.Count == 0)
                sb.Append("; привязок нет — нажмите «Построить превью»");
            return sb.ToString();
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
                // п.9: direction validation — warn if Typno direction doesn't match template direction
                var warnings9 = ValidateTypnoDirections();
                int n = _s.ModbusBlocks.SaveTypnoMap(TypnoRules);
                _s.Log.Info($"F14: сопоставлений Typno сохранено: {n} (SCADA_TYPNO_MAP)");
                string status9 = $"Сохранено сопоставлений Typno: {n}.";
                if (warnings9.Count > 0)
                {
                    foreach (var w in warnings9) _s.Log.Warn(w);
                    status9 += $" Предупреждений (направление): {warnings9.Count}.";
                    System.Windows.MessageBox.Show(
                        string.Join("\n", warnings9.Take(15)),
                        "Направление Typno → шаблон (п.9)",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
                StatusText = status9;
                LoadTypnoRules(); // перечитать с ID
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F14: ошибка сохранения SCADA_TYPNO_MAP: {msg}");
                StatusText = $"Ошибка сохранения сопоставления Typno: {msg}";
            }
        }

        /// <summary>п.9: направление Typno (по кодам функций сигналов из плана) ↔ направление
        /// шаблона (dataout=чтение / datain=запись). Несовпадение → предупреждение.</summary>
        private List<string> ValidateTypnoDirections()
        {
            var warnings = new List<string>();
            if (_built == null) return warnings;
            // 1. направление Typno из плана пакетов
            var typnoDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var packet in _built.Packets)
            {
                string dir = packet.IsWrite ? "запись" : "чтение";
                foreach (var b in packet.Bindings)
                {
                    string t = (b.Typno ?? "").Trim();
                    if (t.Length == 0) continue;
                    if (typnoDir.TryGetValue(t, out var existing))
                    {
                        if (existing != dir && existing != "смешанное") typnoDir[t] = "смешанное";
                    }
                    else
                    {
                        typnoDir[t] = dir;
                    }
                }
            }
            // 2. направление шаблонов из DataTemplates
            var tmplDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in DataTemplates)
                if (!string.IsNullOrWhiteSpace(row.Name))
                    tmplDir[row.Name.Trim()] = row.IsWrite ? "запись" : "чтение";
            // 3. валидация каждого правила
            foreach (var rule in TypnoRules)
            {
                string t = (rule.Typno ?? "").Trim();
                string tmpl = (rule.Template ?? "").Trim();
                if (t.Length == 0 || tmpl.Length == 0) continue;
                if (!typnoDir.TryGetValue(t, out var td)) continue;
                if (!tmplDir.TryGetValue(tmpl, out var md)) continue;
                if (td != "смешанное" && md != td)
                    warnings.Add($"Typno «{t}» ({td}) → шаблон «{tmpl}» ({md}) — направление не совпадает");
            }
            return warnings;
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
                foreach (var r in merged)
                {
                    HookStationRule(r); // F53
                    SlaveRules.Add(r);
                }
                RenumberSlaveRules(); // F53: колонка «№»
                UpdateStationsHint(); // F53
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
            var wanted = DataOutTemplates.Select(t => ("чтение", t.Name, t.FilePath, t.Version, t.SavedAt))
                .Concat(DataInTemplates.Select(t => ("запись", t.Name, t.FilePath, t.Version, t.SavedAt))).ToList();
            foreach (var gone in DataTemplates
                .Where(r => !wanted.Any(w => string.Equals(w.Name, r.Name, StringComparison.OrdinalIgnoreCase)
                                          && w.Item1 == r.Kind)).ToList())
                DataTemplates.Remove(gone);
            foreach (var w in wanted)
            {
                var cur = DataTemplates.FirstOrDefault(r =>
                    string.Equals(r.Name, w.Name, StringComparison.OrdinalIgnoreCase) && r.Kind == w.Item1);
                if (cur == null)
                    DataTemplates.Add(new MbDataTemplateRow
                    {
                        Kind = w.Item1, Name = w.Name, FilePath = w.FilePath,
                        Version = w.Version, SavedAt = w.SavedAt
                    });
                else
                {
                    cur.FilePath = w.FilePath;
                    cur.Version = w.Version;
                    cur.SavedAt = w.SavedAt;
                }
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
                // F81: источник — путь только в режиме «Файл» (имя без пути), иначе пусто →
                // сохранится прежнее имя источника
                _s.ModbusBlocks.SaveMasterTemplate(name, _templateText!, "",
                    ModeFile ? Settings.TemplateFilePath ?? "" : "");
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
            var swPerf = System.Diagnostics.Stopwatch.StartNew();
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
                // F45: два скоупа — два запроса (раньше: запрос на каждый шаблон + EnsureTables
                // на каждый — N×~20 открытий соединений при открытии окна F13)
                var outByKey = _s.ModbusBlocks.LoadTemplateVarsAll("dataout", out var outLegacy);
                var inByKey = _s.ModbusBlocks.LoadTemplateVarsAll("datain", out var inLegacy);
                foreach (var t in DataOutTemplates)
                    foreach (var v in ModbusBlockPlannerService.MergeTemplateVars(
                        outByKey.TryGetValue(t.Name, out var own) ? own : new(), outLegacy))
                        if (seen.Add(t.Name + "|" + v.Var))
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            {
                                TemplateName = t.Name, Direction = DirectionOf("dataout", v.Var), Var = v.Var,
                                IoColumn = v.IoColumn, Literal = v.Literal,
                                BitMode = v.BitMode, BitNo = v.BitNo
                            });
                foreach (var t in DataInTemplates)
                    foreach (var v in ModbusBlockPlannerService.MergeTemplateVars(
                        inByKey.TryGetValue(t.Name, out var own) ? own : new(), inLegacy))
                        if (seen.Add(t.Name + "|" + v.Var))
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            {
                                TemplateName = t.Name, Direction = DirectionOf("datain", v.Var), Var = v.Var,
                                IoColumn = v.IoColumn, Literal = v.Literal,
                                BitMode = v.BitMode, BitNo = v.BitNo
                            });
                // F34: текущий шаблон редактора (ключ = DataOutTemplateName) — если его
                // нет в перечне сохранённых, строки маппинга всё равно должны быть видны
                string cur = CurrentDataTemplateKey();
                if (!DataOutTemplates.Any(t => string.Equals(t.Name, cur, StringComparison.OrdinalIgnoreCase)) &&
                    !DataInTemplates.Any(t => string.Equals(t.Name, cur, StringComparison.OrdinalIgnoreCase)))
                    foreach (var v in ModbusBlockPlannerService.MergeTemplateVars(
                        outByKey.TryGetValue(cur, out var ownCur) ? ownCur : new(), outLegacy))
                        if (seen.Add(cur + "|" + v.Var))
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            {
                                TemplateName = cur, Direction = DirectionOf("dataout", v.Var), Var = v.Var,
                                IoColumn = v.IoColumn, Literal = v.Literal,
                                BitMode = v.BitMode, BitNo = v.BitNo
                            });
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F33: ошибка чтения маппинга объектов data-шаблонов: {msg}");
            }
            ApplyBitModeDefaults(); // F80: дефолты режима «Адрес»/«Адрес/бит»
            _s.Log.Info($"F45 PERF: LoadDataTemplateVars ({DataTemplateVars.Count} строк) — " +
                        $"{swPerf.ElapsedMilliseconds} мс");
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
                {
                    foreach (var o in _dataOut.ParseTemplateObjects(_dataOutTemplateText))
                        if (have.Add(cur + "|" + o.Name))
                        {
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            { TemplateName = cur, Direction = "чтение", Var = o.Name });
                            added++;
                        }
                    // F44: текстовые объекты [TEXT] несохранённого тела редактора
                    foreach (var o in _dataOut.ParseTextObjects(_dataOutTemplateText))
                        if (have.Add(cur + "|" + o))
                        {
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            { TemplateName = cur, Direction = "текст", Var = o });
                            added++;
                        }
                }
                // F44: текстовые объекты ([TEXT]=…) САМИХ data-шаблонов — примитивы их
                // OnePOU клонируются с каждой цепочкой в файл привязок (см. Write);
                // объект получает ключ ИМЕНИ своего шаблона (MB_AI_REAL32: Comment(Text)),
                // Direction «текст»; для основного шаблона (ключ cur) — ниже, F38
                foreach (var t in DataOutTemplates)
                    foreach (var o in _dataOut.ParseTextObjects(t.Body))
                        if (have.Add(t.Name + "|" + o))
                        {
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            { TemplateName = t.Name, Direction = "текст", Var = o });
                            added++;
                        }
                foreach (var t in DataInTemplates)
                    foreach (var o in _dataOut.ParseTextObjects(t.Body))
                        if (have.Add(t.Name + "|" + o))
                        {
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            { TemplateName = t.Name, Direction = "текст", Var = o });
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
                // F52: объекты основного МАСТЕР-шаблона (блоки 31/34 юнита мастера, кроме
                // самого мастера 37) — переименование по колонке ИЛИ начальное значение
                // при экспорте пакетного файла (ScadaTemplateXmlService.Write); Direction
                // «мастер», ключ — текущий data-шаблон (как у текстов F38)
                if (mainTplText != null)
                    foreach (var o in _dataOut.ParseTemplateObjects(mainTplText))
                        if (have.Add(cur + "|" + o.Name))
                        {
                            DataTemplateVars.Add(new MbDataTemplateVarRow
                            { TemplateName = cur, Direction = "мастер", Var = o.Name });
                            added++;
                        }
                    ApplyBitModeDefaults(); // F80: дефолты режима для новых строк
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

        /// <summary>F80: дефолты режима привязки для строк без режима — объект «Bit0..Bit15» →
        /// «Адрес/бит» + номер бита (значение M_Bit 0..15), остальные → «Адрес». Режим,
        /// заданный пользователем, не перезаписывается. Вызывается после загрузки и заполнения.</summary>
        private void ApplyBitModeDefaults()
        {
            foreach (var r in DataTemplateVars)
            {
                if (!string.IsNullOrWhiteSpace(r.BitMode)) continue;
                string vn = (r.Var ?? "").Trim();
                if (vn.Length > 3 && vn.StartsWith("Bit", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(vn.AsSpan(3), out int bi) && bi >= 0 && bi <= 16 &&
                    string.Equals(vn, "Bit" + bi, StringComparison.OrdinalIgnoreCase))
                {
                    r.BitMode = "Адрес/бит";
                    r.BitNo = bi.ToString();
                }
                else r.BitMode = "Адрес";
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
                                 Scope = (r.Direction == "запись" || DataTemplateVars.Any(x => x.Direction == "запись" && string.Equals(x.TemplateName.Trim(), r.TemplateName.Trim(), StringComparison.OrdinalIgnoreCase))) ? "datain" : "dataout", // F85 p6: [TEXT] rows inherit template scope
                                 Name = r.TemplateName.Trim()
                             }))
                {
                    var rows = g.Select(r => new MbTemplateVar
                    {
                        Var = r.Var.Trim(), IoColumn = r.IoColumn.Trim(), Literal = r.Literal.Trim(),
                        BitMode = r.BitMode.Trim(), BitNo = r.BitMode.Trim().IndexOf('/') >= 0 ? r.BitNo.Trim() : ""
                    }).ToList();
                    total += _s.ModbusBlocks.SaveTemplateVars(g.Key.Scope, g.Key.Name, rows);
                }
                _s.Log.Info($"F33: маппинг объектов data-шаблонов сохранён: {total} строк");
                StatusText = $"Сохранено строк маппинга объектов: {total}.";
                LoadDataTemplateVars(); // перечитать
                LoadMasterObjectVars(); // F101
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
            // F61: привязки внизу — по выбору пакета превью (те же объекты MbBinding —
            // пересоздание строк синхронизирует смещения/пути после правки адреса)
            RefreshBindingsView();
        }

        /// <summary>F85: пометить M_GEN_STS по сформированным data-шаблонам: сигналы из
        /// привязок пакетов — «сгенерирован», остальные строки очищаются.</summary>
        private void MarkGenerated(IReadOnlyList<MbPacketPlan> packets)
        {
            try
            {
                var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in packets)
                    foreach (var b in p.Bindings)
                    {
                        if (!string.IsNullOrWhiteSpace(b.Tag)) tags.Add(b.Tag.Trim());
                        foreach (var sh in b.SharedTags)
                            if (!string.IsNullOrWhiteSpace(sh)) tags.Add(sh.Trim());
                    }
                int n = _s.ModbusBlocks.MarkGenerationStatus(_s.Repo.TableName, tags);
                if (n > 0) _s.Log.Info($"F85: M_GEN_STS проставлен: {n} строк");
            }
            catch (Exception ex)
            {
                _s.Log.Error($"F85: ошибка записи M_GEN_STS: {ex.Message}");
            }
        }

        /// <summary>F101: перечитать строки перечня «Объекты мастер-шаблона»
        /// (SCADA_TEMPLATE_VARS scope masterobj, по каждому мастер-шаблону).</summary>
        private void LoadMasterObjectVars()
        {
            try
            {
                MasterObjectVars.Clear();
                var byKey = _s.ModbusBlocks.LoadTemplateVarsAll("masterobj", out var legacy);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in MasterTemplates)
                    foreach (var v in ModbusBlockPlannerService.MergeTemplateVars(
                        byKey.TryGetValue(t.Name, out var own) ? own : new(), legacy))
                        if (seen.Add(t.Name + "|" + v.Var))
                            MasterObjectVars.Add(new MbDataTemplateVarRow
                            {
                                TemplateName = t.Name, Var = v.Var,
                                IoColumn = v.IoColumn, Literal = v.Literal,
                                NameSuffix = v.NameSuffix
                            });
                MasterObjectVarsView?.Refresh();
            }
            catch (Exception ex)
            {
                _s.Log.Error("F101: ошибка загрузки перечня объектов мастер-шаблона: " +
                    DbProbe.ClassifyMessage(ex.Message));
            }
        }

        /// <summary>F101: плоская карта перечня «Объекты мастер-шаблона» для экспорта:
        /// «шаблонное имя объекта → строка»; при совпадении имён приоритет — строки
        /// активных мастер-шаблонов (TCP/RTU из настроек).</summary>
        private System.Collections.Generic.Dictionary<string,
            (string NameSuffix, string IoColumn, string Literal)> BuildMasterObjMap()
        {
            var map = new System.Collections.Generic.Dictionary<string,
                (string NameSuffix, string IoColumn, string Literal)>(StringComparer.OrdinalIgnoreCase);
            void AddRow(MbDataTemplateVarRow r)
            {
                string k = r.Var.Trim();
                if (k.Length == 0 || map.ContainsKey(k)) return;
                map[k] = (r.NameSuffix?.Trim() ?? "", r.IoColumn?.Trim() ?? "", r.Literal?.Trim() ?? "");
            }
            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string tk = (Settings.MasterTcpTemplateName ?? "").Trim();
            string rk = (Settings.MasterRtuTemplateName ?? "").Trim();
            if (tk.Length > 0) active.Add(tk);
            if (rk.Length > 0) active.Add(rk);
            foreach (var r in MasterObjectVars)
                if (active.Contains(r.TemplateName.Trim())) AddRow(r);
            foreach (var r in MasterObjectVars) AddRow(r);
            return map;
        }

        /// <summary>F101: «Заполнить из шаблонов» перечня объектов мастер-шаблона: все блоки
        /// 31/34 выбранного мастер-шаблона (юнит + дополнительная логика), кроме блоков
        /// мастера и блоков-источников; существующие строки сохраняются.</summary>
        private void FillMasterObjectVars()
        {
            var t = SelectedMasterTemplate;
            if (t == null)
            {
                StatusText = "Выберите мастер-шаблон в перечне «Шаблоны пакетов (Master)».";
                return;
            }
            try
            {
                var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in MasterObjectVars)
                    if (string.Equals(r.TemplateName, t.Name, StringComparison.OrdinalIgnoreCase))
                        have.Add(r.Var);
                int added = 0;
                foreach (var o in _s.ModbusBlocksXml.ParseMasterTemplateObjects(t.Body))
                    if (have.Add(o.Name))
                    {
                        MasterObjectVars.Add(new MbDataTemplateVarRow
                        { TemplateName = t.Name, Var = o.Name, NameSuffix = "" });
                        added++;
                    }
                MasterObjectVarsView?.Refresh();
                StatusText = added > 0
                    ? $"Объектов мастер-шаблона добавлено: {added} (всего {MasterObjectVars.Count}). " +
                      "Задайте «Окончание»/«Колонка / шаблон» и «Сохранить маппинг объектов»."
                    : "Новых объектов мастер-шаблона не найдено.";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error("F101: ошибка заполнения объектов мастер-шаблона: " + msg);
                StatusText = "Ошибка заполнения объектов мастер-шаблона: " + msg;
            }
        }

        /// <summary>F101: «Сохранить маппинг объектов» перечня мастер-шаблона: заменяются
        /// только строки выбранного мастер-шаблона (scope masterobj).</summary>
        private void SaveMasterObjectVars()
        {
            var t = SelectedMasterTemplate;
            if (t == null)
            {
                StatusText = "Выберите мастер-шаблон в перечне «Шаблоны пакетов (Master)».";
                return;
            }
            try
            {
                var rows = new List<MbTemplateVar>();
                foreach (var r in MasterObjectVars)
                    if (string.Equals(r.TemplateName, t.Name, StringComparison.OrdinalIgnoreCase))
                        rows.Add(new MbTemplateVar
                        {
                            Var = r.Var, IoColumn = r.IoColumn,
                            Literal = r.Literal, NameSuffix = r.NameSuffix
                        });
                int n = _s.ModbusBlocks.SaveTemplateVars("masterobj", t.Name, rows);
                StatusText = $"Маппинг объектов мастер-шаблона «{t.Name}» сохранён (строк: {n}).";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error("F101: ошибка сохранения перечня объектов мастер-шаблона: " + msg);
                StatusText = "Ошибка сохранения перечня объектов мастер-шаблона: " + msg;
            }
        }
    }
}
