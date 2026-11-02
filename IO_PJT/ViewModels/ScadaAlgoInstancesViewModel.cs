using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.ViewModels
{
    /// <summary>F65: окно «SCADA → Генерация экземпляров типовых алгоритмов» — шаблон
    /// логической схемы (BufScadaPOUS), привязки «сигнал → строка TYPNO» с маркой
    /// (предпросмотр для первого Loop), контуры Loop (экземпляры), пагинация файлов
    /// (InstancesPerFile) и экспорт BufScadaPOUS. Настройки/привязки/шаблоны — БД.</summary>
    public sealed class ScadaAlgoInstancesViewModel : ViewModelBase
    {
        private readonly AppServices _s;

        /// <summary>Редактируемые настройки; персистятся SaveSettingsCommand.</summary>
        public AlgoGenSettings Settings { get; }

        /// <summary>Сохранённые шаблоны (SCADA_ALGO_TEMPLATES).</summary>
        public ObservableCollection<AlgoTemplate> Templates { get; } = new();

        /// <summary>Строки TYPNO текущего шаблона (фильтр по колонке «Шаблон»).</summary>
        public ObservableCollection<AlgoTypnoRow> TypnoRows { get; } = new();

        /// <summary>Все строки TYPNO (источник для ComboBox привязок).</summary>
        public ObservableCollection<AlgoTypnoRow> AllTypnoRows { get; } = new();

        /// <summary>Привязки «сигнал шаблона → строка TYPNO» (грид).</summary>
        public ObservableCollection<AlgoBinding> Bindings { get; } = new();

        /// <summary>Контуры Loop (экземпляры) из IO_LIST.</summary>
        public ObservableCollection<AlgoLoopInfo> Loops { get; } = new();

        private string _statusText = "Загрузите шаблон, заполните привязки и нажмите «Построить превью»";
        public string StatusText { get => _statusText; set => Set(ref _statusText, value, nameof(StatusText)); }

        private string _statsText = "";
        public string StatsText { get => _statsText; set => Set(ref _statsText, value, nameof(StatsText)); }

        private string _templateStatus = "Шаблон не загружен";
        public string TemplateStatus { get => _templateStatus; set => Set(ref _templateStatus, value, nameof(TemplateStatus)); }

        private string _templateText = "";
        /// <summary>Текущий шаблон (XML-тело; редактируемо в окне).</summary>
        public string TemplateText
        {
            get => _templateText;
            set => Set(ref _templateText, value, nameof(TemplateText));
        }

        private string _templateFilePath = "";
        /// <summary>Путь файла-источника шаблона (справочно).</summary>
        public string TemplateFilePath
        {
            get => _templateFilePath;
            set => Set(ref _templateFilePath, value, nameof(TemplateFilePath));
        }

        private AlgoTemplate? _selectedTemplate;
        /// <summary>Выбранный шаблон в списке — смена загружает тело и привязки.</summary>
        public AlgoTemplate? SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (!Set(ref _selectedTemplate, value, nameof(SelectedTemplate))) return;
                if (value == null) return;
                TemplateText = value.Body;
                TemplateFilePath = value.FilePath;
                Settings.TemplateName = value.Name;
                LoadBindingsFor(value.Name);
                ParseTemplate();
            }
        }

        private AlgoBinding? _selectedBinding;
        /// <summary>Выбранная привязка (для удаления).</summary>
        public AlgoBinding? SelectedBinding
        {
            get => _selectedBinding;
            set => Set(ref _selectedBinding, value, nameof(SelectedBinding));
        }

        private AlgoLoopInfo? _selectedLoop;
        /// <summary>Выбранный контур (для предпросмотра экземпляра).</summary>
        public AlgoLoopInfo? SelectedLoop
        {
            get => _selectedLoop;
            set => Set(ref _selectedLoop, value, nameof(SelectedLoop));
        }

        /// <summary>Команды окна.</summary>
        public RelayCommand BuildCommand { get; }
        public RelayCommand GenerateCommand { get; }
        public RelayCommand SaveSettingsCommand { get; }
        public RelayCommand SaveBindingsCommand { get; }
        public RelayCommand SaveTemplateCommand { get; }
        public RelayCommand SaveTemplateAsCommand { get; }
        public RelayCommand DeleteTemplateCommand { get; }
        public RelayCommand LoadTemplateFileCommand { get; }
        public RelayCommand UpdateFromFileCommand { get; }
        public RelayCommand BrowseOutDirCommand { get; }
        public RelayCommand AddBindingCommand { get; }
        public RelayCommand DeleteBindingCommand { get; }
        public RelayCommand SelectAllLoopsCommand { get; }
        public RelayCommand ClearAllLoopsCommand { get; }
        public RelayCommand FillBindingsFromTypnoCommand { get; }

        /// <summary>Последний разобранный шаблон.</summary>
        private ScadaAlgoXmlService.TemplateInfo? _parsed;

        /// <summary>Кэш TYPNO (все строки).</summary>
        private List<AlgoTypnoRow> _typnoCache = new();

        /// <summary>Последний результат генерации (для записи файлов).</summary>
        private AlgoGenResult? _generated;

        public ScadaAlgoInstancesViewModel(AppServices services)
        {
            _s = services;
            Settings = _s.AlgoTemplates.LoadSettings();
            BuildCommand = new RelayCommand(Build);
            GenerateCommand = new RelayCommand(Generate, () => _parsed is { Valid: true });
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            SaveBindingsCommand = new RelayCommand(SaveBindings);
            SaveTemplateCommand = new RelayCommand(SaveTemplate);
            SaveTemplateAsCommand = new RelayCommand(SaveTemplateAs);
            DeleteTemplateCommand = new RelayCommand(DeleteTemplate);
            LoadTemplateFileCommand = new RelayCommand(LoadTemplateFile);
            UpdateFromFileCommand = new RelayCommand(UpdateFromFile);
            BrowseOutDirCommand = new RelayCommand(BrowseOutDir);
            AddBindingCommand = new RelayCommand(AddBinding);
            DeleteBindingCommand = new RelayCommand(DeleteBinding);
            SelectAllLoopsCommand = new RelayCommand(() => SetAllLoops(true));
            ClearAllLoopsCommand = new RelayCommand(() => SetAllLoops(false));
            FillBindingsFromTypnoCommand = new RelayCommand(FillBindingsFromTypno);
            ReloadAll();
        }

        // ================== Загрузка ==================

        /// <summary>F65: загрузить шаблоны, TYPNO, привязки и контуры при открытии окна.</summary>
        private void ReloadAll()
        {
            try
            {
                _s.AlgoTemplates.EnsureTables();
                _typnoCache = _s.AlgoTemplates.LoadTypno();

                Templates.Clear();
                foreach (var t in _s.AlgoTemplates.LoadTemplates()) Templates.Add(t);

                AllTypnoRows.Clear();
                foreach (var r in _typnoCache) AllTypnoRows.Add(r);

                // текущий шаблон из настроек (или первый)
                SelectedTemplate = Templates.FirstOrDefault(t =>
                    string.Equals(t.Name, Settings.TemplateName, StringComparison.OrdinalIgnoreCase))
                    ?? Templates.FirstOrDefault();

                LoadLoops();
                StatusText = $"шаблонов: {Templates.Count}, строк TYPNO: {_typnoCache.Count}, контуров: {Loops.Count}";
            }
            catch (Exception ex)
            {
                StatusText = $"сбой загрузки: {ex.Message}";
            }
        }

        /// <summary>F65: привязки текущего шаблона + синхронизация с сигналами шаблона
        /// (новые сигналы — пустые строки, удалённые — не трогаются, показываются).</summary>
        private void LoadBindingsFor(string templateName)
        {
            Bindings.Clear();
            var saved = _s.AlgoTemplates.LoadBindings(templateName);
            var signals = ScadaAlgoXmlService.ParseSignals(TemplateText);
            foreach (var sig in signals)
            {
                var b = saved.FirstOrDefault(x =>
                    string.Equals(x.Signal.Trim(), sig, StringComparison.OrdinalIgnoreCase));
                Bindings.Add(b ?? new AlgoBinding { Signal = sig });
            }
            // сохранённые привязки к несуществующим сигналам — добавляем в конец (ручная правка)
            foreach (var b in saved)
                if (!Bindings.Any(x => string.Equals(x.Signal.Trim(), b.Signal.Trim(),
                        StringComparison.OrdinalIgnoreCase)))
                    Bindings.Add(b);
            UpdateTypnoFilter();
        }

        /// <summary>F65: отфильтровать строки TYPNO по текущему шаблону (колонка «Шаблон»).</summary>
        private void UpdateTypnoFilter()
        {
            TypnoRows.Clear();
            string tpl = (Settings.TemplateName ?? "").Trim();
            foreach (var r in _typnoCache
                         .Where(r => tpl.Length == 0 ||
                                     string.Equals(r.Template.Trim(), tpl, StringComparison.OrdinalIgnoreCase)))
                TypnoRows.Add(r);
        }

        /// <summary>F65: контуры Loop из текущей таблицы IO_LIST.</summary>
        private void LoadLoops()
        {
            Loops.Clear();
            var points = _s.Repo.GetAll();
            var (loops, warnings) = ScadaAlgoService.LoadLoops(points);
            foreach (var l in loops) Loops.Add(l);
            foreach (var w in warnings) _s.Log.Warn(w);
        }

        // ================== Превью ==================

        /// <summary>F65: разобрать шаблон, обновить статус/предпросмотры марок.</summary>
        private void ParseTemplate()
        {
            _parsed = _s.AlgoXml.Parse(TemplateText);
            TemplateStatus = _parsed.Summary;
            UpdateMarkPreviews();
            Raise(nameof(GenerateCommand));
        }

        /// <summary>F65: предпросмотр марки в гриде привязок — для первого попавшегося Loop.</summary>
        private void UpdateMarkPreviews()
        {
            var first = Loops.FirstOrDefault();
            foreach (var b in Bindings)
                b.MarkPreview = first == null ? ""
                    : ScadaAlgoService.MarkFor(first.Loop, b.Mask, b.Suffix, b.Constructor, first.FirstRow);
        }

        // ================== Команды ==================

        /// <summary>F65: «Построить превью» — парсинг шаблона + пересчёт марок/статистики.</summary>
        private void Build()
        {
            try
            {
                ParseTemplate();
                if (_parsed is not { Valid: true })
                {
                    StatsText = "";
                    StatusText = "шаблон не валиден: " + _parsed?.Error;
                    return;
                }
                int withTypno = Loops.Count(l => l.Generate);
                int marked = Bindings.Count(b => b.Mask.Length > 0 || b.Suffix.Length > 0);
                StatsText = $"контуров: {Loops.Count} (к генерации {withTypno}); " +
                            $"привязок с TYPNO: {marked} из {Bindings.Count}";
                StatusText = "превью построено; проверьте марки и нажмите «Сгенерировать XML…»";
            }
            catch (Exception ex)
            {
                StatusText = $"сбой превью: {ex.Message}";
            }
        }

        /// <summary>F65: карта «сигнал → марка» для контура (из привязок с заполненной строкой TYPNO).</summary>
        private List<(string Signal, string Mark)> MarkMapFor(AlgoLoopInfo loop)
        {
            var map = new List<(string, string)>();
            foreach (var b in Bindings)
            {
                string sig = b.Signal.Trim();
                if (sig.Length == 0) continue;
                string mark = ScadaAlgoService.MarkFor(loop.Loop, b.Mask, b.Suffix,
                    b.Constructor, loop.FirstRow);
                if (mark.Length == 0) continue;
                map.Add((sig, mark));
            }
            return map;
        }

        /// <summary>F65: «Сгенерировать XML…» — сборка файлов и запись в OutDir
        /// (пустой — диалог выбора папки).</summary>
        private void Generate()
        {
            try
            {
                if (_parsed is not { Valid: true })
                {
                    StatusText = "шаблон не валиден — генерация невозможна";
                    return;
                }
                var opt = new ScadaAlgoXmlService.GenerateOptions
                {
                    InstancesPerFile = Settings.InstancesPerFile,
                    FilePattern = Settings.FilePattern,
                    IdStride = Settings.IdStride,
                    TemplateName = Settings.TemplateName
                };
                var result = _s.AlgoXml.Generate(TemplateText, Loops.ToList(), MarkMapFor, opt);
                _generated = result;

                foreach (var w in result.Warnings) _s.Log.Warn(w);

                string dir = (Settings.OutDir ?? "").Trim();
                if (dir.Length == 0)
                {
                    var dlg = new Microsoft.Win32.OpenFolderDialog
                    {
                        Title = "F65: папка для сгенерированных XML-файлов"
                    };
                    if (dlg.ShowDialog() != true)
                    {
                        StatusText = "выбор папки отменён — файлы не записаны";
                        return;
                    }
                    dir = dlg.FolderName;
                    Settings.OutDir = dir;
                }
                Directory.CreateDirectory(dir);
                foreach (var (fileName, xml) in result.Files)
                    File.WriteAllText(Path.Combine(dir, fileName), xml,
                        new System.Text.UTF8Encoding(true));

                StatsText = $"файлов: {result.Files.Count}; экземпляров: {result.Instances}; " +
                            $"пропущено контуров: {result.Skipped}; предупреждений: {result.Warnings.Count}";
                StatusText = $"записано в {dir}: {string.Join(", ", result.Files.Select(f => f.FileName))}";
            }
            catch (Exception ex)
            {
                StatusText = $"сбой генерации: {ex.Message}";
            }
        }

        private void SaveSettings()
        {
            try
            {
                _s.AlgoTemplates.SaveSettings(Settings);
                StatusText = "настройки сохранены (SCADA_ALGO_SETTINGS)";
            }
            catch (Exception ex) { StatusText = $"сбой сохранения настроек: {ex.Message}"; }
        }

        private void SaveBindings()
        {
            try
            {
                string tpl = (Settings.TemplateName ?? "").Trim();
                if (tpl.Length == 0) { StatusText = "имя шаблона пустое — привязки не сохранены"; return; }
                _s.AlgoTemplates.SaveBindings(tpl, Bindings);
                StatusText = $"привязки сохранены ({Bindings.Count(b => b.Signal.Trim().Length > 0)} строк)";
            }
            catch (Exception ex) { StatusText = $"сбой сохранения привязок: {ex.Message}"; }
        }

        private void SaveTemplate()
        {
            try
            {
                string tpl = (Settings.TemplateName ?? "").Trim();
                if (tpl.Length == 0) { SaveTemplateAs(); return; }
                _s.AlgoTemplates.SaveTemplate(new AlgoTemplate
                {
                    Name = tpl,
                    Body = TemplateText,
                    FilePath = TemplateFilePath
                });
                ReloadTemplates();
                StatusText = $"шаблон «{tpl}» сохранён (SCADA_ALGO_TEMPLATES)";
            }
            catch (Exception ex) { StatusText = $"сбой сохранения шаблона: {ex.Message}"; }
        }

        private void SaveTemplateAs()
        {
            try
            {
                string? name = Views.NamePromptWindow.Ask("Сохранить шаблон под именем:",
                    Settings.TemplateName ?? "");
                if (string.IsNullOrWhiteSpace(name)) return;
                Settings.TemplateName = name!.Trim();
                _s.AlgoTemplates.SaveTemplate(new AlgoTemplate
                {
                    Name = Settings.TemplateName,
                    Body = TemplateText,
                    FilePath = TemplateFilePath
                });
                ReloadTemplates();
                StatusText = $"шаблон «{Settings.TemplateName}» сохранён";
            }
            catch (Exception ex) { StatusText = $"сбой сохранения шаблона: {ex.Message}"; }
        }

        private void DeleteTemplate()
        {
            try
            {
                string tpl = (Settings.TemplateName ?? "").Trim();
                if (tpl.Length == 0) return;
                if (MessageBox.Show($"Удалить шаблон «{tpl}»?", "F65",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _s.AlgoTemplates.DeleteTemplate(tpl);
                Settings.TemplateName = "";
                TemplateText = "";
                ReloadTemplates();
                StatusText = $"шаблон «{tpl}» удалён";
            }
            catch (Exception ex) { StatusText = $"сбой удаления шаблона: {ex.Message}"; }
        }

        /// <summary>F65: перечитать список шаблонов и выбрать текущий.</summary>
        private void ReloadTemplates()
        {
            string cur = (Settings.TemplateName ?? "").Trim();
            Templates.Clear();
            foreach (var t in _s.AlgoTemplates.LoadTemplates()) Templates.Add(t);
            SelectedTemplate = Templates.FirstOrDefault(t =>
                string.Equals(t.Name, cur, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>F65: «Загрузить из файла…» — прочитать XML-шаблон в редактор.</summary>
        private void LoadTemplateFile()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "XML шаблон (*.xml)|*.xml|Все файлы (*.*)|*.*",
                    FileName = TemplateFilePath
                };
                if (dlg.ShowDialog() != true) return;
                TemplateFilePath = dlg.FileName;
                TemplateText = File.ReadAllText(dlg.FileName);
                if (string.IsNullOrWhiteSpace(Settings.TemplateName))
                    Settings.TemplateName = Path.GetFileNameWithoutExtension(dlg.FileName);
                ParseTemplate();
                StatusText = $"шаблон загружен из {dlg.FileName}";
            }
            catch (Exception ex) { StatusText = $"сбой загрузки файла: {ex.Message}"; }
        }

        /// <summary>F65: «Обновить из файла» — перечитать тело по FilePath текущего шаблона.</summary>
        private void UpdateFromFile()
        {
            try
            {
                string path = (TemplateFilePath ?? "").Trim();
                if (path.Length == 0 || !File.Exists(path))
                {
                    StatusText = "путь файла шаблона пуст/не найден — используйте «Загрузить из файла…»";
                    return;
                }
                TemplateText = File.ReadAllText(path);
                SaveTemplate();
                ParseTemplate();
                StatusText = $"тело шаблона перечитано из {path}";
            }
            catch (Exception ex) { StatusText = $"сбой обновления из файла: {ex.Message}"; }
        }

        /// <summary>F65: «Обзор…» папки вывода.</summary>
        private void BrowseOutDir()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Папка для сгенерированных файлов" };
            if (dlg.ShowDialog() != true) return;
            Settings.OutDir = dlg.FolderName;
        }

        private void AddBinding()
        {
            Bindings.Add(new AlgoBinding());
        }

        private void DeleteBinding()
        {
            if (SelectedBinding != null) Bindings.Remove(SelectedBinding);
        }

        private void SetAllLoops(bool value)
        {
            foreach (var l in Loops) l.Generate = value;
        }

        /// <summary>F65: «Заполнить из TYPNO» — для сигналов шаблона подобрать строки TYPNO
        /// текущего шаблона по маске/суффиксу (эвристика; ручная правка в гриде).</summary>
        private void FillBindingsFromTypno()
        {
            try
            {
                var rows = TypnoRows.Count > 0 ? TypnoRows.ToList() : _typnoCache;
                if (rows.Count == 0) { StatusText = "строки TYPNO не найдены"; return; }
                foreach (var b in Bindings)
                {
                    string sig = b.Signal.Trim();
                    if (sig.Length == 0) continue;
                    var hit = rows.FirstOrDefault(r => r.Suffix.Length > 0 &&
                                       sig.EndsWith(r.Suffix, StringComparison.OrdinalIgnoreCase))
                              ?? rows.FirstOrDefault(r => r.Mask.Length > 0 && r.Mask != "*" &&
                                       sig.Contains(r.Mask, StringComparison.OrdinalIgnoreCase))
                              ?? rows.FirstOrDefault(r => r.Mask == "*");
                    if (hit == null) continue;
                    b.Mask = hit.Mask;
                    b.Suffix = hit.Suffix;
                    b.ObjType = hit.ObjType;
                }
                UpdateMarkPreviews();
                StatusText = "привязки заполнены по TYPNO (проверьте марки — эвристика)";
            }
            catch (Exception ex) { StatusText = $"сбой заполнения из TYPNO: {ex.Message}"; }
        }
    }
}
