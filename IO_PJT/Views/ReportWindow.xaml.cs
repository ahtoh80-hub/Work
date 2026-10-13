using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.Views
{
    /// <summary>F7.8: окно «Формирование отчёта» — сводка аудита по документам (файл + вкладка):
    /// по каждой ячейке (полю) количество изменений по типам (Insert/Update/…) + итоги документа
    /// и общий итог; выгрузка отчёта в Excel. Открывается из окна истории (HistoryViewModel.OpenReport).</summary>
    public partial class ReportWindow : Window
    {
        private readonly IReadOnlyList<AuditReportRow> _rows;
        private readonly string _filterText;
        private readonly AppServices _services;

        public ReportWindow(IReadOnlyList<AuditReportRow> rows, string filterText, AppServices services)
        {
            InitializeComponent();
            _rows = rows;
            _filterText = filterText;
            _services = services;

            FilterText.Text = filterText;
            ReportGrid.ItemsSource = rows;

            if (rows.Count == 0)
            {
                TotalText.Text = "Нет записей аудита по текущим фильтрам";
                ExportButton.IsEnabled = false;
                return;
            }
            int docs = rows.Count(r => r.IsSubtotal);
            int total = rows.LastOrDefault(r => r.IsTotal)?.Total ?? 0; // последняя строка Build — «ВСЕГО»
            TotalText.Text = $"Документов: {docs} · Всего изменений: {total}";
        }

        /// <summary>F7.8: выгрузка отчёта в Excel.</summary>
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = $"AuditReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _services.Exporter.ExportAuditReport(_rows, _filterText, dlg.FileName);
                _services.Log.Info($"Отчёт сформирован и выгружен: {dlg.FileName}");
                StatusText.Text = "Выгружено: " + dlg.FileName;
            }
            catch (Exception ex)
            {
                _services.Log.Error($"Ошибка выгрузки отчёта: {ex.Message}");
                MessageBox.Show("Ошибка выгрузки отчёта: " + ex.Message, "Формирование отчёта",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}