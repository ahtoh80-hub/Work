using System;
using System.Windows;
using IO_PJT.Models;
using IO_PJT.Services;
using IO_PJT.ViewModels;

namespace IO_PJT.Views
{
    /// <summary>Главное окно: связка VM и открытие дочерних окон.</summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();

            var services = new AppServices(App.DbPath);
            _vm = new MainViewModel(services);
            DataContext = _vm;

            // Хуки дочерних окон (MVVM без ссылок на Views внутри VM)
            _vm.OpenImportWindow = ShowImport;
            _vm.OpenModbusImportWindow = ShowModbusImport;
            _vm.OpenModbusLinkRulesWindow = ShowModbusLinkRules;
            _vm.OpenHistoryWindow = ShowHistory;
            _vm.OpenCardWindow = ShowCard;
            _vm.OpenCreateTableDialog = ShowCreateTable;
        }

        /// <summary>Единое окно импорта (F6.3).</summary>
        private void ShowImport()
        {
            var vm = new ImportViewModel(_vm.Services);
            var window = new ImportWindow { DataContext = vm, Owner = this };
            vm.Imported += _vm.Refresh;
            window.ShowDialog();
            _vm.ReloadTables();
            _vm.Refresh();
        }

        /// <summary>F9: окно импорта карты адресации Modbus + связывание IO_LIST.</summary>
        private void ShowModbusImport()
        {
            var vm = new ModbusImportViewModel(_vm.Services);
            var window = new ModbusImportWindow { DataContext = vm, Owner = this };
            vm.CloseRequested = window.Close;
            window.ShowDialog();
            _vm.Refresh(); // M_-поля могли обновиться
        }

        /// <summary>F9.11: окно редактора ручных правил связывания MODBUS_LINK_RULES.</summary>
        private void ShowModbusLinkRules()
        {
            var vm = new ModbusLinkRulesViewModel(_vm.Services);
            var window = new ModbusLinkRulesWindow { DataContext = vm, Owner = this };
            window.ShowDialog();
            _vm.Refresh(); // связи не меняются, но правила влияют на следующий Link()
        }

        /// <summary>Окно истории изменений (F7) + окно «Формирование отчёта» (F7.8).</summary>
        private void ShowHistory()
        {
            var vm = new HistoryViewModel(_vm.Services);
            vm.OpenReport = (rows, filterText) =>
            {
                var report = new ReportWindow(rows, filterText, _vm.Services) { Owner = this };
                report.ShowDialog();
            };
            var window = new HistoryWindow { DataContext = vm, Owner = this };
            window.ShowDialog();
        }

        /// <summary>Карточка сигнала — модальное окно (F6.2, стиль Screen forma.JPG).</summary>
        private void ShowCard(IoPoint point, bool isNew)
        {
            var vm = new CardViewModel(_vm.Services, point, isNew);
            var window = new CardWindow { DataContext = vm, Owner = this };
            vm.CloseRequested = window.Close;
            window.ShowDialog();
            _vm.Refresh();
        }

        /// <summary>Диалог имени таблицы (F1.3, стиль Screen forma1.JPG).</summary>
        private void ShowCreateTable(string suggestedName)
        {
            var dialog = new CreateTableDialog(suggestedName) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            try
            {
                _vm.Services.Creator.CreateDataTable(dialog.TableName);
                _vm.Services.Log.Info(
                    $"Создана таблица [{dialog.TableName}]: {Data.Schema.DataColumns.Length + 1} колонок + индекс UX_{dialog.TableName}_Tag");
                _vm.ReloadTables();
                _vm.Refresh();
            }
            catch (Exception ex)
            {
                _vm.Services.Log.Error($"Ошибка создания таблицы: {ex.Message}");
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenDatabase_Click(object sender, RoutedEventArgs e) =>
            _vm.OpenDatabaseCommand.Execute(null);

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>A3: окно «Руководство пользователя».</summary>
        private void Help_Click(object sender, RoutedEventArgs e)
        {
            var window = new HelpWindow { Owner = this };
            window.ShowDialog();
        }

        private void About_Click(object sender, RoutedEventArgs e) =>
            MessageBox.Show(
                "IO_PJT — импорт IO List из Excel в Access.\n" +
                "Модель данных: IOclass.md (127 колонок); ТЗ: IOLISTtoDB_v0.MD v2.0 (F9 — карта Modbus).\n" +
                "Стек: WPF / .NET 8, ClosedXML, OleDb (ACE OLE DB).\n" +
                "Разработчик: Решетов Антон",
                "О программе", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}