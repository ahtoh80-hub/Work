using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace IO_PJT
{
    /// <summary>
    /// Точка входа WPF-приложения. При первом запуске создаётся
    /// база данных IO_PJT.accdb и служебные таблицы по структуре IOclass (раздел Б8).
    /// Часть C: проверка доступности БД на старте (с понятным сообщением и выбором
    /// другой базы) + глобальные обработчики необработанных исключений.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>Путь к файлу базы данных по умолчанию (рядом с программой).</summary>
        public static string DbPath { get; private set; } = string.Empty;

        /// <summary>Имя таблицы данных по умолчанию (решение ТЗ F1.2).</summary>
        public const string DefaultTableName = "IO_LIST";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ручное управление показом главного окна: сначала — проверка базы
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // ===== Часть C: глобальная защита от «вылета без сообщения» =====
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // База данных всегда создаётся рядом с исполняемым файлом
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DbPath = System.IO.Path.Combine(baseDir, "IO_PJT.accdb");

            try
            {
                // Создаём базу с нуля при первом запуске (F1.3 / п.3 ТЗ)
                var creator = new Services.TableCreator(DbPath);
                if (!creator.DatabaseExists())
                {
                    creator.CreateDatabase();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось создать базу данных IO_PJT.accdb:\n" + ex.Message,
                    "IO_PJT", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // ===== Часть C: проверка доступности базы + понятное сообщение вместо тишины =====
            while (!Services.DbProbe.TryOpen(DbPath, out string error))
            {
                var answer = MessageBox.Show(
                    "База данных недоступна:\n" + error,
                    "IO_PJT — база недоступна",
                    MessageBoxButton.YesNo, MessageBoxImage.Error);
                if (answer != MessageBoxResult.Yes)
                {
                    Shutdown(-1);
                    return;
                }
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Базы Access (*.accdb;*.mdb)|*.accdb;*.mdb"
                };
                if (dlg.ShowDialog() != true)
                {
                    Shutdown(-1);
                    return;
                }
                DbPath = dlg.FileName;
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var window = new Views.MainWindow();
            MainWindow = window;
            window.Show();
        }

        /// <summary>Необработанная ошибка UI-потока: показать причину и продолжить работу (часть C).</summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                MessageBox.Show(
                    "Непредвиденная ошибка:\n" + e.Exception.Message +
                    "\n\nПрограмма продолжит работу; при повторении ошибки обратитесь к журналу событий.",
                    "IO_PJT — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* защита от исключения внутри обработчика */ }
            e.Handled = true;
        }

        /// <summary>Необработанная ошибка вне UI-потока (последний рубеж).</summary>
        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.IsTerminating)
            {
                try
                {
                    MessageBox.Show(
                        "Критическая ошибка — приложение будет закрыто:\n" +
                        (e.ExceptionObject as Exception)?.Message,
                        "IO_PJT — критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { /* исключение в обработчике — выходим как есть */ }
            }
        }

        /// <summary>Незамеченное исключение фоновой задачи — сообщить пользователю (часть C).</summary>
        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            try
            {
                MessageBox.Show(
                    "Ошибка фоновой задачи:\n" + e.Exception?.GetBaseException().Message,
                    "IO_PJT — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* исключение в обработчике */ }
        }
    }
}
