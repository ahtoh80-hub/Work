using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.ViewModels
{
    /// <summary>
    /// Главное окно: репозиторий (грид), журнал событий снизу,
    /// команды импорта/истории/создания таблицы/бэкапа/выгрузки (Задание.md).
    /// </summary>
    public sealed class MainViewModel : ViewModelBase
    {
        private readonly AppServices _services;

        public AppServices Services => _services;
        public IEventLogger Log => _services.Log;

        private string _infoText = "";
        public string InfoText { get => _infoText; set => Set(ref _infoText, value, nameof(InfoText)); }

        private string _filterText = "";
        /// <summary>Строка поиска по Tag, Service, Loop, InstrumentType (F3.3).</summary>
        public string FilterText { get => _filterText; set => Set(ref _filterText, value, nameof(FilterText)); }

        private string _selectedSystem = "Все";
        /// <summary>Фильтр по системе (F3.4): «Все» или значение System; System в БД произвольный (DCS, РСУ, ПСБ и т.п.) — можно ввести вручную.</summary>
        public string SelectedSystem
        {
            get => _selectedSystem;
            set { if (Set(ref _selectedSystem, value, nameof(SelectedSystem))) Refresh(); }
        }

        /// <summary>F45: AddRange одним Reset — грид на тысячах строк не дёргается построчно.</summary>
        public RangeObservableCollection<IoPoint> Points { get; } = new();
        public ObservableCollection<string> Tables { get; } = new();
        /// <summary>Список систем: «Все» + уникальные значения System из текущей таблицы (F3.4; обновляется в Refresh).</summary>
        public ObservableCollection<string> Systems { get; } = new() { "Все" };

        private string? _selectedTable;
        public string? SelectedTable
        {
            get => _selectedTable;
            set { if (Set(ref _selectedTable, value, nameof(SelectedTable)) && value != null) SelectTable(value); }
        }

        private IoPoint? _selectedPoint;
        public IoPoint? SelectedPoint { get => _selectedPoint; set => Set(ref _selectedPoint, value, nameof(SelectedPoint)); }

        private IReadOnlyList<IoPoint> _selectedPoints = Array.Empty<IoPoint>();
        /// <summary>F113: выбранные строки главного грида (множественный выбор) — для
        /// кнопки «Заполнить M_bit из M_Address»; обновляется из code-behind.</summary>
        public IReadOnlyList<IoPoint> SelectedPoints => _selectedPoints;

        /// <summary>F113: передать выбранные строки из DataGrid (SelectedItems не bindable).</summary>
        public void SetSelectedPoints(IEnumerable<IoPoint> points)
        {
            _selectedPoints = points.ToList();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        /// <summary>Очередь журнала: события из фонового потока, UI забирает пачкой по таймеру (F2.9).</summary>
        private readonly ConcurrentQueue<LogEntry> _logQueue = new();
        private readonly DispatcherTimer _logTimer;

        public RelayCommand RefreshCommand { get; }
        public RelayCommand SearchCommand { get; }
        public RelayCommand ImportCommand { get; }
        public RelayCommand HistoryCommand { get; }
        public RelayCommand CreateTableCommand { get; }
        public RelayCommand NewPointCommand { get; }
        public RelayCommand EditPointCommand { get; }
        public RelayCommand DeletePointCommand { get; }
        /// <summary>F113: заполнить M_Bit из совмещённого M_Address для выбранных строк.</summary>
        public RelayCommand FillBitsFromAddressCommand { get; }
        public RelayCommand BackupCommand { get; }
        public RelayCommand ClearTableCommand { get; }
        public RelayCommand ExportExcelCommand { get; }
        public RelayCommand OpenDatabaseCommand { get; }
        public RelayCommand CopyLogCommand { get; }
        public RelayCommand ClearLogCommand { get; }
        /// <summary>F100: импорт карты адресации Modbus → MODBUS_MAP (пресет профиля
        /// в едином окне импорта: сопоставление столбцов, шаблоны, аудит, связывание F9.3).</summary>
        public RelayCommand ImportModbusCommand { get; }

        /// <summary>F99: единое окно «Обновление адресации Modbus согласно MODBUS_MAP» —
        /// три действия (связи полностью / только пустые / M_-адресация), правила
        /// связывания MODBUS_LINK_RULES и проверка маски на теге. Заменяет прежние четыре
        /// пункта меню и две кнопки тулбара (F99: устранение дублирования).</summary>
        public RelayCommand ModbusAddressingCommand { get; }

        /// <summary>F12: генерация перечня Modbus (Тех.объекты) для SCADA.</summary>
        public RelayCommand ScadaTechObjectsCommand { get; }

        /// <summary>F13: генерация блоков Modbus (BufScadaPOUS XML) для SCADA.</summary>
        public RelayCommand ScadaModbusBlocksCommand { get; }

        /// <summary>F65: генерация экземпляров типовых алгоритмов (логические схемы XML).</summary>
        public RelayCommand ScadaAlgoInstancesCommand { get; }

        /// <summary>Хуки открытия дочерних окон (назначаются code-behind главного окна).
        /// F100: OpenImportWindow принимает DisplayName профиля-пресета (ImportTargets):
        /// пункт «Импорт IO LIST из Excel...» → IO_LIST, «Импорт Modbus_MAP из Excel...» → MODBUS_MAP.</summary>
        public Action<string>? OpenImportWindow { get; set; }
        /// <summary>F99: окно «Обновление адресации Modbus согласно MODBUS_MAP».</summary>
        public Action? OpenModbusAddressingWindow { get; set; }
        /// <summary>F12: окно генерации перечня «Тех.объекты» (меню SCADA).</summary>
        public Action? OpenScadaTechObjectsWindow { get; set; }
        /// <summary>F13: окно генерации блоков Modbus (XML) (меню SCADA).</summary>
        public Action? OpenScadaModbusBlocksWindow { get; set; }
        /// <summary>F65: окно генерации экземпляров типовых алгоритмов (меню SCADA).</summary>
        public Action? OpenScadaAlgoInstancesWindow { get; set; }
        public Action? OpenHistoryWindow { get; set; }
        public Action<IoPoint, bool>? OpenCardWindow { get; set; }
        public Action<string>? OpenCreateTableDialog { get; set; }

        public MainViewModel(AppServices services)
        {
            _services = services;
            // Журнал принимается пачками: таймер 200 мс переливает очередь в грид —
            // построчное добавление на сотнях событий импорта дёргало UI-поток.
            _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _logTimer.Tick += (_, _) => FlushLogQueue();
            _logTimer.Start();
            _services.Log.EntryAdded += OnLogEntry;

            RefreshCommand = new RelayCommand(Refresh);
            SearchCommand = new RelayCommand(Refresh);
            ImportCommand = new RelayCommand(() => OpenImportWindow?.Invoke(ImportTargets.IoList.DisplayName));
            // F100: «Импорт Modbus_MAP из Excel...» — пресет профиля в том же окне импорта
            ImportModbusCommand = new RelayCommand(() => OpenImportWindow?.Invoke(ImportTargets.ModbusMap.DisplayName));
            // F99: одно окно вместо четырёх пунктов меню и двух кнопок тулбара
            ModbusAddressingCommand = new RelayCommand(() => OpenModbusAddressingWindow?.Invoke());
            // F12: генерация перечня всегда доступна (диалог предупредит, если нет строк)
            ScadaTechObjectsCommand = new RelayCommand(() => OpenScadaTechObjectsWindow?.Invoke());
            // F13: генерация блоков Modbus (XML) всегда доступна (предупредит, если нет пакетов)
            ScadaModbusBlocksCommand = new RelayCommand(() => OpenScadaModbusBlocksWindow?.Invoke());
            // F65: генерация экземпляров типовых алгоритмов (логические схемы XML)
            ScadaAlgoInstancesCommand = new RelayCommand(() => OpenScadaAlgoInstancesWindow?.Invoke());
            HistoryCommand = new RelayCommand(() => OpenHistoryWindow?.Invoke());
            CreateTableCommand = new RelayCommand(() => OpenCreateTableDialog?.Invoke(_services.CurrentTable));
            NewPointCommand = new RelayCommand(
                () => OpenCardWindow?.Invoke(new IoPoint(), true),
                () => _services.Creator.TableExists(_services.CurrentTable));
            EditPointCommand = new RelayCommand(
                () => { if (SelectedPoint != null) OpenCardWindow?.Invoke(SelectedPoint, true); },
                () => SelectedPoint != null);
            DeletePointCommand = new RelayCommand(DeleteSelected, () => SelectedPoint != null);
            FillBitsFromAddressCommand = new RelayCommand(FillBitsFromAddress,
                () => SelectedPoints.Count > 0);
            BackupCommand = new RelayCommand(BackupTable, () => _services.Creator.TableExists(_services.CurrentTable));
            ClearTableCommand = new RelayCommand(ClearTable, () => _services.Creator.TableExists(_services.CurrentTable));
            ExportExcelCommand = new RelayCommand(ExportExcel, () => Points.Count > 0);
            OpenDatabaseCommand = new RelayCommand(OpenDatabase);
            CopyLogCommand = new RelayCommand(CopyLog, () => LogEntries.Count > 0);
            ClearLogCommand = new RelayCommand(() => LogEntries.Clear());

            foreach (var e in _services.Log.Entries) LogEntries.Add(e);
            LoadTables();
            Refresh();
        }

        /// <summary>Событие журнала (любой поток): только поставить в очередь — без обращений к UI.</summary>
        private void OnLogEntry(LogEntry entry) => _logQueue.Enqueue(entry);

        /// <summary>Пачковая вставка журнала в грид: последнее событие сверху (A1) +
        /// ограничение размера — последние 5000 строк (срез с конца).</summary>
        private void FlushLogQueue()
        {
            // Пачка сохраняет хронологию: первый событие пачки — ближе к низу списка
            var batch = new List<LogEntry>();
            while (_logQueue.TryDequeue(out var e)) batch.Add(e);
            for (int i = batch.Count - 1; i >= 0; i--) LogEntries.Insert(0, batch[i]);
            while (LogEntries.Count > 5000) LogEntries.RemoveAt(LogEntries.Count - 1);
        }

        /// <summary>A1: копировать журнал событий в буфер обмена (полный текст со временем и уровнем).</summary>
        private void CopyLog()
        {
            try
            {
                string text = string.Join(Environment.NewLine,
                    LogEntries.Select(e => $"[{e.Time:yyyy-MM-dd HH:mm:ss}] {e.Level}: {e.Message}"));
                Clipboard.SetText(text);
                InfoText = $"Журнал скопирован в буфер обмена: {LogEntries.Count} строк.";
                _services.Log.Info($"Журнал скопирован в буфер обмена: {LogEntries.Count} строк.");
            }
            catch (Exception ex)
            {
                _services.Log.Error($"Ошибка копирования журнала: {ex.Message}");
            }
        }

        /// <summary>Загрузить список пользовательских таблиц базы (F1.2).</summary>
        private void LoadTables()
        {
            Tables.Clear();
            if (!_services.Creator.DatabaseExists()) return;
            foreach (var t in _services.Creator.GetTableNames().Where(t =>
                         !t.Equals("MappingTemplates", StringComparison.OrdinalIgnoreCase) &&
                         !t.Equals("IO_LIST_Audit", StringComparison.OrdinalIgnoreCase) &&
                         // MODBUS_MAP — не IO_LIST: сетка читает колонки FieldRegistry и не отображает её
                         !t.Equals(Data.Schema.ModbusMapTable, StringComparison.OrdinalIgnoreCase)))
                Tables.Add(t);

            if (Tables.Count > 0)
            {
                _selectedTable = Tables.Contains(_services.CurrentTable)
                    ? _services.CurrentTable : Tables[0];
                Raise(nameof(SelectedTable));
                _services.UseTable(_selectedTable);
            }
        }

        private void SelectTable(string table)
        {
            _services.UseTable(table);
            Refresh();
        }

        /// <summary>
        /// F3.4: обновить список систем из БД — «Все» + уникальные значения System (произвольные: DCS, РСУ, ПСБ и т.п.).
        /// Если выбранная система исчезла — выбор сбрасывается на «Все» без повторного Refresh.
        /// </summary>
        private void LoadSystems()
        {
            List<string> systems;
            try
            {
                systems = _services.Creator.TableExists(_services.CurrentTable)
                    ? _services.Repo.GetSystems()
                    : new List<string>();
            }
            catch (Exception ex)
            {
                _services.Log.Error($"Ошибка чтения списка систем: {ex.Message}");
                systems = new List<string>();
            }

            string current = _selectedSystem;
            Systems.Clear();
            Systems.Add("Все");
            foreach (var s in systems) Systems.Add(s);

            if (!Systems.Contains(current))
            {
                _selectedSystem = "Все";       // напрямую — без setter, чтобы не вызвать рекурсивный Refresh
                Raise(nameof(SelectedSystem)); // ComboBox обновит выбранное значение
            }
        }

        /// <summary>Обновить грид с учётом фильтров (F3.1/F3.3/F3.4).</summary>
        public void Refresh()
        {
            try
            {
                LoadSystems();
                Points.Clear();
                if (!_services.Creator.TableExists(_services.CurrentTable))
                {
                    InfoText = $"Таблица [{_services.CurrentTable}] не создана — используйте «Создать таблицу»";
                    return;
                }

                List<IoPoint> list;
                if (!string.IsNullOrWhiteSpace(FilterText))
                    list = _services.Repo.Search(FilterText.Trim());
                else if (SelectedSystem != "Все")
                    list = _services.Repo.GetBySystem(SelectedSystem);
                else
                    list = _services.Repo.GetAll();

                // F45: батч-заполнение одним Reset вместо N CollectionChanged (грид на
                // тысячах строк получал по уведомлению на строку); Clear — на строке 348
                Points.AddRange(list);
                InfoText = $"База: {System.IO.Path.GetFileName(_services.DbPath)} | " +
                           $"Таблица: {_services.CurrentTable} | Записей: {_services.Repo.Count()} (показано: {Points.Count})";
            }
            catch (Exception ex)
            {
                _services.Log.Error($"Ошибка чтения: {ex.Message}");
                InfoText = "Ошибка: " + ex.Message;
            }
        }

        /// <summary>Пересоздать список таблиц (после «Создать таблицу»).</summary>
        public void ReloadTables() => LoadTables();

        /// <summary>F113: заполнить M_Bit из M_Address только для выбранных строк грида.</summary>
        private void FillBitsFromAddress()
        {
            if (SelectedPoints.Count == 0)
            {
                InfoText = "F113: выберите строки в таблице для заполнения M_Bit из M_Address.";
                return;
            }
            try
            {
                var r = _services.ModbusLink.FillBitsFromCombinedAddress(SelectedPoints);
                _services.Log.Info(r.Summary);
                InfoText = r.Summary;
                if (r.Errors.Count > 0)
                    _services.Log.Warn("F113: " + string.Join("; ", r.Samples.Take(10)));
                Refresh();
            }
            catch (Exception ex)
            {
                _services.Log.Error($"F113: ошибка заполнения M_Bit из M_Address: {ex.Message}");
                InfoText = "F113: ошибка — " + ex.Message;
            }
        }

        private void DeleteSelected()
        {
            if (SelectedPoint == null) return;
            var res = MessageBox.Show($"Удалить запись Tag={SelectedPoint.Tag}?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
            try
            {
                _services.Repo.Delete(SelectedPoint.Id);
                Refresh();
            }
            catch (Exception ex) { _services.Log.Error($"Ошибка удаления: {ex.Message}"); }
        }

        /// <summary>Резервная копия таблицы с датой в имени (Задание п.5).</summary>
        private void BackupTable()
        {
            try
            {
                string copy = _services.Backup.BackupTable(_services.CurrentTable);
                _services.Log.Info($"Создана резервная копия: [{copy}]");
                LoadTables();
            }
            catch (Exception ex) { _services.Log.Error($"Ошибка резервного копирования: {ex.Message}"); }
        }

        private void ClearTable()
        {
            var res = MessageBox.Show(
                $"Очистить таблицу [{_services.CurrentTable}]? Действие необратимо.\n" +
                "Рекомендуется предварительно создать резервную копию.",
                "Очистка таблицы", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
            try { _services.Repo.Clear(); Refresh(); }
            catch (Exception ex) { _services.Log.Error($"Ошибка очистки: {ex.Message}"); }
        }

        /// <summary>Выгрузка текущей выборки в Excel (F7.3/F7.4).</summary>
        private void ExportExcel()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = $"IO_LIST_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _services.Exporter.ExportPoints(Points, dlg.FileName);
                _services.Log.Info($"Данные выгружены в Excel: {dlg.FileName}");
            }
            catch (Exception ex) { _services.Log.Error($"Ошибка выгрузки: {ex.Message}"); }
        }

        /// <summary>Открыть другой файл базы Access (F1.1) — с предварительной проверкой
        /// доступности (часть C: понятное сообщение вместо зависаний/тихих ошибок).</summary>
        private void OpenDatabase()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Базы Access (*.accdb;*.mdb)|*.accdb;*.mdb" };
            if (dlg.ShowDialog() != true) return;

            if (!DbProbe.TryOpen(dlg.FileName, out string probeError))
            {
                _services.Log.Error($"База недоступна: {probeError}");
                MessageBox.Show("База данных недоступна:\n" + probeError,
                    "Открытие базы", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                _services.UseDatabase(dlg.FileName);
                LoadTables();
                Refresh();
            }
            catch (Exception ex)
            {
                _services.Log.Error($"Ошибка открытия базы: {ex.Message}");
                MessageBox.Show("Не удалось открыть базу данных:\n" + ex.Message,
                    "Открытие базы", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
