using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
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

        /// <summary>F27: «Записать Марку в IO_LIST» — колонка M_Marka по превью.</summary>
        public RelayCommand WriteMarkaCommand { get; }
        public RelayCommand SaveSettingsCommand { get; }

        /// <summary>F57: «Сохранить как…» / «Удалить профиль» — именованные варианты настроек.</summary>
        public RelayCommand SaveAsProfileCommand { get; }
        public RelayCommand DeleteProfileCommand { get; }

        /// <summary>F57: имена сохранённых профилей настроек (SCADA_TEHOBJ_SETTINGS.Profile).</summary>
        public ObservableCollection<string> Profiles { get; } = new();

        private string? _selectedProfile;
        /// <summary>F57: выбранный профиль — смена загружает его настройки в Settings.</summary>
        public string? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (!Set(ref _selectedProfile, value, nameof(SelectedProfile))) return;
                if (string.IsNullOrWhiteSpace(value)) return;
                try
                {
                    _s.ScadaTechObjects.LoadInto(Settings, value);
                    StatusText = $"профиль «{value}» загружен";
                }
                catch (Exception ex)
                {
                    StatusText = $"сбой загрузки профиля: {ex.Message}";
                }
            }
        }
        public RelayCommand SaveMappingsCommand { get; }
        public RelayCommand AddMappingCommand { get; }
        public RelayCommand DeleteMappingCommand { get; }

        /// <summary>F68: условия набираемого фильтра по столбцам источника (настройки профиля).</summary>
        public ObservableCollection<ScadaFilterCondition> FilterConditions => Settings.FilterConditions;

        /// <summary>F68: столбцы источника для ComboBox фильтра (имена FieldRegistry).</summary>
        public IReadOnlyList<string> FieldNames { get; } =
            FieldRegistry.Fields.Select(f => f.Column).ToArray();

        public RelayCommand AddFilterCommand { get; }
        public RelayCommand<ScadaFilterCondition> RemoveFilterCommand { get; }
        public RelayCommand ClearFiltersCommand { get; }

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
            foreach (var pr in _s.ScadaTechObjects.LoadProfiles()) Profiles.Add(pr);
            _selectedProfile = ScadaTechObjectService.DefaultProfile; // Settings уже загружены — без перезагрузки
            BuildCommand = new RelayCommand(Build);
            ExportCommand = new RelayCommand(Export, () => Preview.Count > 0);
            WriteMarkaCommand = new RelayCommand(WriteMarkaToDb, () => Preview.Count > 0);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            SaveAsProfileCommand = new RelayCommand(SaveAsProfile);
            DeleteProfileCommand = new RelayCommand(DeleteProfile);
            SaveMappingsCommand = new RelayCommand(SaveMappings);
            AddMappingCommand = new RelayCommand(AddMapping);
            DeleteMappingCommand = new RelayCommand(DeleteMapping, () => SelectedMapping != null);
            AddFilterCommand = new RelayCommand(AddFilter);
            RemoveFilterCommand = new RelayCommand<ScadaFilterCondition>(f => RemoveFilter(f));
            ClearFiltersCommand = new RelayCommand(ClearFilters);
            FilterConditions.CollectionChanged += OnFiltersChanged;
            foreach (var fc in FilterConditions) fc.PropertyChanged += OnFilterChanged; // F68
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
                var result = _s.ScadaTechObjects.BuildRows(points, Settings, TypeMappings.ToList(),
                    FilterConditions.ToList()); // F68: набираемый фильтр по столбцам источника
                _built = result;
                Preview.Clear();
                foreach (var row in result.Rows) Preview.Add(row);
                Raise(nameof(Preview));

                StatsText = $"Строк: {result.Rows.Count} (с адресацией: {result.WithAddressing}, " +
                            $"пропущено без адресации: {result.SkippedNoAddressing}, отсеяно фильтром: {result.SkippedByFilter}; " +
                            $"типов без маппинга: {result.UnmappedTypes}; " +
                            $"обрезано Доп.параметров: {result.TruncatedDParam}; " +
                            $"предупреждений: {result.Warnings.Count})";
                StatusText = result.Rows.Count > 0
                    ? "Превью построено. При необходимости сохраните настройки и сформируйте Excel."
                    : "Нет строк: в текущей таблице нет точек с заполненной адресацией Modbus " +
                      "(заполните M_-поля кнопкой «Обновить адресацию Modbus» — F9.4).";
                if (result.Rows.Count == 0 && FilterConditions.Any(f => !string.IsNullOrWhiteSpace(f.Text)))
                    StatusText = $"ет строк: набираемый фильтр отсеял точки (с адресацией: " +
                                 $"{result.WithAddressing}, отсеяно фильтром: {result.SkippedByFilter}) — измените условия или «Сброс».";
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

        /// <summary>F27: «Записать Марку в IO_LIST» — колонка M_Marka по SourceTag строк превья
        /// (MessageBox со счётчиками, аудит Update).</summary>
        private void WriteMarkaToDb()
        {
            if (_built == null) return;
            try
            {
                var (updated, notFound) = _s.ScadaTechObjects.WriteMarka(_built.Rows, _s.Repo);
                string summary = $"Марка записана в IO_LIST (M_Marka): обновлено точек: {updated}; " +
                                 $"тегов не найдено: {notFound}";
                _s.Log.Info($"F12/F27: {summary}");
                MessageBox.Show(
                    summary + (notFound > 0 ? $"\n\nТеги не найдены: {notFound} (первые — в журнале)." : ""),
                    "Марка в IO_LIST",
                    MessageBoxButton.OK,
                    notFound > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                StatusText = summary;
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F12/F27: ошибка записи Марки: {msg}");
                StatusText = $"Ошибка записи Марки: {msg}";
            }
        }

        /// <summary>Сохранить настройки в SCADA_TEHOBJ_SETTINGS (переживают перезапуск).</summary>
        private void SaveSettings()
        {
            try
            {
                _s.ScadaTechObjects.SaveSettings(Settings, SelectedProfile);
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

                /// <summary>F57: «Сохранить как…» — сохранить текущие настройки под новым именем.</summary>
        private void SaveAsProfile()
        {
            string? name = IO_PJT.Views.NamePromptWindow.Ask(
                "вариант настроек:", SelectedProfile ?? "", null, "Сохранение настроек");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            try
            {
                _s.ScadaTechObjects.SaveSettings(Settings, name);
                if (!Profiles.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)))
                    Profiles.Add(name);
                SelectedProfile = name;
                _s.Log.Info($"F57: настройки сохранены в профиль «{name}»");
                StatusText = $"профиль «{name}» сохранён";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F57: сбой сохранения профиля: {msg}");
                StatusText = $"сбой сохранения профиля: {msg}";
            }
        }

        /// <summary>F57: удалить выбранный именованный профиль (по умолчанию — нельзя).</summary>
        private void DeleteProfile()
        {
            string name = SelectedProfile ?? ScadaTechObjectService.DefaultProfile;
            if (string.Equals(name, ScadaTechObjectService.DefaultProfile, StringComparison.OrdinalIgnoreCase))
                return;
            if (MessageBox.Show($"Удалить профиль «{name}»?", "Удаление профиля",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try
            {
                bool removed = _s.ScadaTechObjects.DeleteProfile(name);
                foreach (var pr in Profiles.Where(pr => string.Equals(pr, name, StringComparison.OrdinalIgnoreCase)).ToList())
                    Profiles.Remove(pr);
                SelectedProfile = ScadaTechObjectService.DefaultProfile;
                _s.Log.Info($"F57: профиль «{name}» удалён: {removed}");
                StatusText = removed ? $"профиль «{name}» удалён" : $"профиль «{name}» не найден";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F57: сбой удаления профиля: {msg}");
                StatusText = $"сбой удаления профиля: {msg}";
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

        // ===== F68: набираемый фильтр по столбцам источника =====

        private void AddFilter()
        {
            FilterConditions.Add(new ScadaFilterCondition { Field = "Tag" });
            StatusText = $"словий фильтра: {FilterConditions.Count}. аберите текст — строки с вхождением покажутся в превью (условия — «»).";
        }

        private void RemoveFilter(ScadaFilterCondition? f)
        {
            if (f != null) FilterConditions.Remove(f);
        }

        private void ClearFilters()
        {
            FilterConditions.Clear();
            StatusText = "ильтр сброшен — в превью попадают все точки с адресацией (нажмите «остроить превью»).";
        }

        // подписки условий: INPC-правки в окне перестраивают превью, если оно уже строилось
        private void OnFiltersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (var it in e.NewItems.OfType<ScadaFilterCondition>())
                    it.PropertyChanged += OnFilterChanged;
            if (e.OldItems != null)
                foreach (var it in e.OldItems.OfType<ScadaFilterCondition>())
                    it.PropertyChanged -= OnFilterChanged;
            RebuildIfBuilt();
        }

        private void OnFilterChanged(object? sender, PropertyChangedEventArgs e) => RebuildIfBuilt();

        private void RebuildIfBuilt()
        {
            if (_built == null) return;
            Build(); // ошибки построения уже попадают в StatusText внутри Build
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

