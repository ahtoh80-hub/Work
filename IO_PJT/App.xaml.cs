using System;
using System.Windows;

namespace IO_PJT
{
    /// <summary>
    /// Точка входа WPF-приложения. При первом запуске создаётся
    /// база данных IO_PJT.accdb и служебные таблицы по структуре IOclass (раздел Б8).
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
        }
    }
}
