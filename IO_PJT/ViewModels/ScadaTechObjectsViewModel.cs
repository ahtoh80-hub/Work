using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.ViewModels
{
    /// <summary>F12: окно «SCADA → Генерация перечня Modbus (Тех.объекты)» — сборка строк
    /// по ScadaGenSettings (поля-источники/константы/структуры {ИмяКолонки}), таблица маппинга
    /// типов SCADA_TYPE_MAP (редактируется, персистится), превью и экспорт
    /// в формате образца «ТехОбъекты_простой.xls» (ClosedXML .xlsx).</summary>
    public sealed class ScadaTechObjectsViewModel : ViewModelBase
    {
        private readonly AppServices _s;

        /// <summary>Редактируемые настройки (поля-источники + константы); персистятся SaveSettingsCommand.</summary>
        public ScadaGenSettings Settings { get; }

        /// <summary>Таблица соответствия типов IO List ↔ SCADA (редактируется в гриде).</summary>
        public ObservableCollection<ScadaTypeMapping> TypeMappings { get; } = new();

        /// <summary>Превью строк перечня.</summary>
        public ObservableCollection<ScadaTechObjectRow> Preview { get; } = new();

        /// <summary>Подсказка: доступные поля-источники (колонки FieldRegistry) и синтаксис структур.</summary>
        public string FieldsHint =>
            "Поля-источники и константы: колонка IO List (Tag, Loop, Service, Controller, IoType, …) → её значение; " +
            "структура вида {Service} ({Loop}) — составное значение из полей (как у Доп.параметра); " +
            "любой текст без скобок — константа. Доп.параметр дополнительно обрезается до 50 символов.";

        private string _statusText = "Настройте параметры и нажмите «Построить превью»";
        public string StatusText { get => _statusText; set => Set(ref _statusText, value, nameof(StatusText)); }

        private string _statsText = "";
        public string StatsText { get => _statsText; set => Set(ref _statsText, value, nameof(StatsText)); }

        /// <summary>Последний построенный результат (для экспорта).</summary>
        private ScadaBuildResult? _built;

        public RelayCommand BuildCommand { get; }
        public RelayCommand ExportCommand { get; }
        public RelayCommand SaveSettingsCommand { get; }
        public RelayCommand SaveMappingsCommand { get; }
        public RelayCommand AddMappingCommand { get; }
        public RelayCommand DeleteMappingCommand { get; }

        private ScadaTypeMapping? _selectedMapping;
        public ScadaTypeMapping? SelectedMapping
        {
            get => _selectedMapping;
            set => Set(ref _selectedMapping, value, nameof(SelectedMapping));
        }

        public ScadaTechObjectsViewModel(AppServices services)
        {
            _s = services;
            Settings = _s.ScadaTechObjects.LoadSettings();
            BuildCommand = new RelayCommand(Build);
            ExportCommand = new RelayCommand(Export, () => Preview.Count > 0);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            SaveMappingsCommand = new RelayCommand(SaveMappings);
            AddMappingCommand = new RelayCommand(AddMapping);
            DeleteMappingCommand = new RelayCommand(DeleteMapping, () => SelectedMapping != null);
            LoadMappings();
            StatusText = $"Настройки загружены. Маппинг типов: {TypeMappings.Count}. " +
                         "Постройте превью для просмотра перечня.";
        }

        /// <summary>Прочитать таблицу маппинга типов из SCADA_TYPE_MAP.</summary>
        private void LoadMappings()
        {
            try
            {
                TypeMappings.Clear();
                foreach (var m in _s.ScadaTechObjects.LoadTypeMappings()) TypeMappings.Add(m);
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F12: ошибка чтения SCADA_TYPE_MAP: {msg}");
                StatusText = $"Ошибка чтения маппинга типов: {msg}";
            }
        }

        /// <summary>Построить превью по текущим настройкам и маппингу.</summary>
        private void Build()
        {
            try
            {
                var points = _s.Repo.GetAll();
                var result = _s.ScadaTechObjects.BuildRows(points, Settings, TypeMappings.ToList());
                _built = result;
                Preview.Clear();
                foreach (var row in result.Rows) Preview.Add(row);
                Raise(nameof(Preview));

                StatsText = $"Строк: {result.Rows.Count} (с адресацией: {result.WithAddressing}, " +
                            $"пропущено без адресации: {result.SkippedNoAddressing}; " +
                            $"типов без маппинга: {result.UnmappedTypes}; " +
                            $"обрезано Доп.параметров: {result.TruncatedDParam}; " +
                            $"предупреждений: {result.Warnings.Count})";
                StatusText = result.Rows.Count > 0
                    ? "Превью построено. При необходимости сохраните настройки и сформируйте Excel."
                    : "Нет строк: в текущей таблице нет точек с заполненной адресацией Modbus " +
                      "(заполните M_-поля кнопкой «Обновить адресацию Modbus» — F9.4).";
                if (result.Warnings.Count > 0)
                    _s.Log.Info($"F12: предупреждения построения ({result.Warnings.Count}): " +
                                string.Join("; ", result.Warnings.Take(5)) +
                                (result.Warnings.Count > 5 ? "; …" : ""));
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F12: ошибка построения перечня: {msg}");
                StatusText = $"Ошибка построения: {msg}";
            }
        }

        /// <summary>Экспорт превью в Excel (диалог сохранения, расширение на выбор пользователя).</summary>
        private void Export()
        {
            if (Preview.Count == 0 || _built == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx|Все файлы (*.*)|*.*",
                FileName = $"ТехОбъекты_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _s.ScadaTechObjects.Export(_built.Rows, dlg.FileName);
                _s.Log.Info($"F12: перечень Тех.объекты сформирован: {dlg.FileName} ({Preview.Count} строк)");
                StatusText = $"Файл сформирован: {dlg.FileName} ({Preview.Count} строк)";
            }
            catch (Exception ex)
            {
                _s.Log.Error($"F12: ошибка экспорта: {ex.Message}");
                StatusText = $"Ошибка экспорта: {ex.Message}";
            }
        }

        /// <summary>Сохранить настройки в SCADA_TEHOBJ_SETTINGS (переживают перезапуск).</summary>
        private void SaveSettings()
        {
            try
            {
                _s.ScadaTechObjects.SaveSettings(Settings);
                _s.Log.Info($"F12: настройки генерации сохранены ({ScadaTechObjectService.SettingKeys.Length} параметров)");
                StatusText = "Настройки сохранены в SCADA_TEHOBJ_SETTINGS.";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F12: ошибка сохранения настроек: {msg}");
                StatusText = $"Ошибка сохранения настроек: {msg}";
            }
        }

        /// <summary>Сохранить таблицу маппинга типов в SCADA_TYPE_MAP (полная замена).</summary>
        private void SaveMappings()
        {
            try
            {
                int n = _s.ScadaTechObjects.SaveTypeMappings(TypeMappings);
                _s.Log.Info($"F12: маппинг типов сохранён: {n} строк (SCADA_TYPE_MAP)");
                StatusText = $"Сохранено соответствий типов: {n}.";
                LoadMappings(); // перечитать с ID
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F12: ошибка сохранения SCADA_TYPE_MAP: {msg}");
                StatusText = $"Ошибка сохранения маппинга: {msg}";
            }
        }

        private void AddMapping()
        {
            var m = new ScadaTypeMapping { Source = "", Target = "BOOL" };
            TypeMappings.Add(m);
            SelectedMapping = m;
            StatusText = $"Маппинг добавлен (не сохранён): {TypeMappings.Count}. Не забудьте «Сохранить маппинг типов».";
        }

        private void DeleteMapping()
        {
            if (SelectedMapping == null) return;
            TypeMappings.Remove(SelectedMapping);
            SelectedMapping = null;
            StatusText = $"Маппинг удалён (не сохранён): {TypeMappings.Count}. Не забудьте «Сохранить маппинг типов».";
        }
    }
}

