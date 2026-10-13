using System;
using System.Collections.ObjectModel;
using System.Linq;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.ViewModels
{
    /// <summary>
    /// Окно истории изменений (ТЗ F7): «что на что менялось» по IO_LIST_Audit,
    /// фильтры по Tag/полю/периоду, выгрузка в Excel.
    /// </summary>
    public sealed class HistoryViewModel : ViewModelBase
    {
        private readonly AppServices _s;

        public ObservableCollection<AuditRecord> Records { get; } = new();

        private string _filterTag = "";
        public string FilterTag { get => _filterTag; set => Set(ref _filterTag, value, nameof(FilterTag)); }

        private string _filterField = "";
        public string FilterField { get => _filterField; set => Set(ref _filterField, value, nameof(FilterField)); }

        private string _filterFileName = "";
        /// <summary>F7.6: фильтр по документу-источнику (REV_FileName, подстрока).</summary>
        public string FilterFileName { get => _filterFileName; set => Set(ref _filterFileName, value, nameof(FilterFileName)); }

        private string _filterSheet = "";
        /// <summary>F7.7: фильтр по вкладке-источнику (REV_Sheet, подстрока) — документ = файл + вкладка (F2.16).</summary>
        public string FilterSheet { get => _filterSheet; set => Set(ref _filterSheet, value, nameof(FilterSheet)); }

        private string _filterSystem = "";
        /// <summary>F7.6: фильтр по системе (System: DCS/SIS/GDS, подстрока).</summary>
        public string FilterSystem { get => _filterSystem; set => Set(ref _filterSystem, value, nameof(FilterSystem)); }

        private DateTime? _from;
        public DateTime? From { get => _from; set => Set(ref _from, value, nameof(From)); }

        private DateTime? _to;
        public DateTime? To { get => _to; set => Set(ref _to, value, nameof(To)); }

        private string _statusText = "";
        public string StatusText { get => _statusText; set => Set(ref _statusText, value, nameof(StatusText)); }

        public RelayCommand RefreshCommand { get; }
        public RelayCommand ExportCommand { get; }

        /// <summary>F7.8: открыть окно «Формирование отчёта» (хук — как OpenHistoryWindow в MainViewModel;
        /// окно открывает MainWindow, VM не ссылается на Views).</summary>
        public Action<List<AuditReportRow>, string>? OpenReport { get; set; }

        public RelayCommand ReportCommand { get; }

        public HistoryViewModel(AppServices services)
        {
            _s = services;
            RefreshCommand = new RelayCommand(Refresh);
            ExportCommand = new RelayCommand(Export, () => Records.Count > 0);
            ReportCommand = new RelayCommand(BuildReport);
            Refresh();
        }

        /// <summary>Загрузить историю по фильтрам.</summary>
        private void Refresh()
        {
            try
            {
                Records.Clear();
                var list = _s.Audit.GetHistory(
                    string.IsNullOrWhiteSpace(FilterTag) ? null : FilterTag.Trim(),
                    string.IsNullOrWhiteSpace(FilterField) ? null : FilterField.Trim(),
                    string.IsNullOrWhiteSpace(FilterFileName) ? null : FilterFileName.Trim(), // F7.6
                    string.IsNullOrWhiteSpace(FilterSystem) ? null : FilterSystem.Trim(),     // F7.6
                    From, To,
                    string.IsNullOrWhiteSpace(FilterSheet) ? null : FilterSheet.Trim());      // F7.7
                foreach (var r in list) Records.Add(r);
                StatusText = $"Записей истории: {Records.Count}" +
                             (Records.Count > 500 ? " (показаны первые 500 — уточните фильтр)" : "");
            }
            catch (Exception ex)
            {
                _s.Log.Error($"Ошибка чтения истории: {ex.Message}");
                StatusText = "Ошибка: " + ex.Message;
            }
        }

        /// <summary>Экспорт истории в Excel (F7.3).</summary>
        private void Export()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = $"History_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _s.Exporter.ExportAudit(Records.Take(5000), dlg.FileName);
                _s.Log.Info($"История выгружена в Excel: {dlg.FileName}");
                StatusText = "Выгружено: " + dlg.FileName;
            }
            catch (Exception ex) { _s.Log.Error($"Ошибка выгрузки истории: {ex.Message}"); }
        }

        /// <summary>F7.8: сводка аудита по текущим фильтрам и открытие окна «Формирование отчёта».
        /// Агрегат считается SQL GROUP BY по всей таблице (не по 500 отображаемым записям).</summary>
        private void BuildReport()
        {
            try
            {
                var summary = _s.Audit.GetSummary(
                    string.IsNullOrWhiteSpace(FilterTag) ? null : FilterTag.Trim(),
                    string.IsNullOrWhiteSpace(FilterField) ? null : FilterField.Trim(),
                    string.IsNullOrWhiteSpace(FilterFileName) ? null : FilterFileName.Trim(), // F7.6
                    string.IsNullOrWhiteSpace(FilterSystem) ? null : FilterSystem.Trim(),     // F7.6
                    From, To,
                    string.IsNullOrWhiteSpace(FilterSheet) ? null : FilterSheet.Trim());      // F7.7
                var rows = ReportBuilder.Build(summary);
                OpenReport?.Invoke(rows, BuildFilterText());
                int total = rows.Count > 0 ? rows[rows.Count - 1].Total : 0;
                int docs = rows.Count(r => r.IsSubtotal);
                _s.Log.Info($"Сформирован отчёт: документов {docs}, изменений {total} ({summary.Count} агрегатов)");
            }
            catch (Exception ex)
            {
                _s.Log.Error($"Ошибка формирования отчёта: {ex.Message}");
                StatusText = "Ошибка отчёта: " + ex.Message;
            }
        }

        /// <summary>F7.8: условие отбора для шапки отчёта (по текущим фильтрам окна истории).</summary>
        private string BuildFilterText()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(FilterTag)) parts.Add($"Tag ~ «{FilterTag.Trim()}»");
            if (!string.IsNullOrWhiteSpace(FilterField)) parts.Add($"поле = «{FilterField.Trim()}»");
            if (!string.IsNullOrWhiteSpace(FilterFileName)) parts.Add($"файл ~ «{FilterFileName.Trim()}»");
            if (!string.IsNullOrWhiteSpace(FilterSheet)) parts.Add($"вкладка ~ «{FilterSheet.Trim()}»");
            if (!string.IsNullOrWhiteSpace(FilterSystem)) parts.Add($"система ~ «{FilterSystem.Trim()}»");
            if (From.HasValue) parts.Add($"с {From:yyyy-MM-dd}");
            if (To.HasValue) parts.Add($"по {To:yyyy-MM-dd}");
            return parts.Count == 0 ? "Фильтр: не задан (вся история)" : "Фильтр: " + string.Join("; ", parts);
        }
    }
}
