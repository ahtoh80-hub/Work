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
        /// <summary>Фильтр по системе: Все / DCS / SIS / GDS (F3.4).</summary>
        public string SelectedSystem
        {
            get => _selectedSystem;
            set { if (Set(ref _selectedSystem, value, nameof(SelectedSystem))) Refresh(); }
        }

        public ObservableCollection<IoPoint> Points { get; } = new();
        public ObservableCollection<string> Tables { get; } = new();
        public ObservableCollection<string> Systems { get; } = new() { "Все", "DCS", "SIS", "GDS" };

        private string? _selectedTable;
        public string? SelectedTable
        {
            get => _selectedTable;
            set { if (Set(ref _selectedTable, value, nameof(SelectedTable)) && value != null) SelectTable(value); }
        }

        private IoPoint? _selectedPoint;
        public IoPoint? SelectedPoint { get => _selectedPoint; set => Set(ref _selectedPoint, value, nameof(SelectedPoint)); }

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
        public RelayCommand BackupCommand { get; }
        public RelayCommand ClearTableCommand { get; }
        public RelayCommand ExportExcelCommand { get; }
        public RelayCommand OpenDatabaseCommand { get; }

        /// <summary>Хуки открытия дочерних окон (назначаются code-behind главного окна).</summary>
        public Action? OpenImportWindow { get; set; }
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
            ImportCommand = new RelayCommand(() => OpenImportWindow?.Invoke());
            HistoryCommand = new RelayCommand(() => OpenHistoryWindow?.Invoke());
            CreateTableCommand = new RelayCommand(() => OpenCreateTableDialog?.Invoke(_services.CurrentTable));
            NewPointCommand = new RelayCommand(
                () => OpenCardWindow?.Invoke(new IoPoint(), true),
                () => _services.Creator.TableExists(_services.CurrentTable));
            EditPointCommand = new RelayCommand(
                () => { if (SelectedPoint != null) OpenCardWindow?.Invoke(SelectedPoint, true); },
                () => SelectedPoint != null);
            DeletePointCommand = new RelayCommand(DeleteSelected, () => SelectedPoint != null);
            BackupCommand = new RelayCommand(BackupTable, () => _services.Creator.TableExists(_services.CurrentTable));
            ClearTableCommand = new RelayCommand(ClearTable, () => _services.Creator.TableExists(_services.CurrentTable));
            ExportExcelCommand = new RelayCommand(ExportExcel, () => Points.Count > 0);
            OpenDatabaseCommand = new RelayCommand(OpenDatabase);

            foreach (var e in _services.Log.Entries) LogEntries.Add(e);
            LoadTables();
            Refresh();
        }

        /// <summary>Событие журнала (любой поток): только поставить в очередь — без обращений к UI.</summary>
        private void OnLogEntry(LogEntry entry) => _logQueue.Enqueue(entry);

        /// <summary>Пачковая вставка журнала в грид + ограничение размера (последние 5000 строк).</summary>
        private void FlushLogQueue()
        {
            while (_logQueue.TryDequeue(out var e)) LogEntries.Add(e);
            while (LogEntries.Count > 5000) LogEntries.RemoveAt(0);
        }

        /// <summary>Загрузить список пользовательских таблиц базы (F1.2).</summary>
        private void LoadTables()
        {
            Tables.Clear();
            if (!_services.Creator.DatabaseExists()) return;
            foreach (var t in _services.Creator.GetTableNames().Where(t =>
                         !t.Equals("MappingTemplates", StringComparison.OrdinalIgnoreCase) &&
                         !t.Equals("IO_LIST_Audit", StringComparison.OrdinalIgnoreCase)))
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

        /// <summary>Обновить грид с учётом фильтров (F3.1/F3.3/F3.4).</summary>
        public void Refresh()
        {
            try
            {
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

                foreach (var p in list) Points.Add(p);
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

        /// <summary>Открыть другой файл базы Access (F1.1).</summary>
        private void OpenDatabase()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Базы Access (*.accdb;*.mdb)|*.accdb;*.mdb" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _services.UseDatabase(dlg.FileName);
                LoadTables();
                Refresh();
            }
            catch (Exception ex) { _services.Log.Error($"Ошибка открытия базы: {ex.Message}"); }
        }
    }
}
