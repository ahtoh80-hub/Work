using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>Выгрузка данных и истории изменений в Excel (.xlsx) — ТЗ F7.3.</summary>
    public sealed class ExcelExportService : IExcelExportService
    {
        /// <summary>Экспорт истории «что на что менялось» с фильтрами (F7.3).</summary>
        public void ExportAudit(IEnumerable<AuditRecord> rows, string filePath)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("История");

            string[] headers = { "Дата/время", "Таблица", "Tag", "Действие", "Поле",
                                 "Старое значение", "Новое значение", "Файл (REV_FileName)",
                                 "Вкладка (REV_Sheet)", "Система", "Автор", "Изменение" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            ws.Row(1).Style.Font.Bold = true;

            int r = 2;
            foreach (var a in rows)
            {
                ws.Cell(r, 1).Value = a.ChangedAt.ToString("yyyy-MM-dd HH:mm:ss");
                ws.Cell(r, 2).Value = a.TableName;
                ws.Cell(r, 3).Value = a.Tag ?? "";
                ws.Cell(r, 4).Value = a.Action.ToString();
                ws.Cell(r, 5).Value = a.FieldName;
                ws.Cell(r, 6).Value = a.OldValue ?? "";
                ws.Cell(r, 7).Value = a.NewValue ?? "";
                ws.Cell(r, 8).Value = a.FileName ?? ""; // F7.6: документ-источник
                ws.Cell(r, 9).Value = a.Sheet ?? "";     // F7.7: вкладка-источник (F2.16)
                ws.Cell(r, 10).Value = a.System ?? "";   // F7.6: система
                ws.Cell(r, 11).Value = a.ChangedBy;
                ws.Cell(r, 12).Value = a.ChangeDescription;
                // F2.11/F2.15: пометка на удаление и очистка Tag — красным шрифтом
                if (a.Action == AuditAction.MarkedDelete || a.Action == AuditAction.TagCleaned)
                    ws.Row(r).Style.Font.FontColor = XLColor.FromHtml("#C42B1C");
                r++;
            }
            ws.Columns().AdjustToContents();
            wb.SaveAs(filePath);
        }

        /// <summary>F7.8: экспорт отчёта «Формирование отчёта» (сводка: документ → ячейка → количество по типам
        /// + итоги документа и общий итог) в .xlsx.</summary>
        public void ExportAuditReport(IEnumerable<AuditReportRow> rows, string filterText, string filePath)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Отчёт");

            ws.Cell(1, 1).Value = "Отчёт по истории изменений IO_LIST_Audit";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(2, 1).Value = filterText;

            string[] headers = { "Документ (файл :: вкладка)", "Ячейка (поле)", "Insert", "Update", "Delete",
                                 "CriticalError", "MarkedDelete", "TagCleaned", "Всего" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(4, i + 1).Value = headers[i];
            ws.Row(4).Style.Font.Bold = true;

            int r = 5;
            foreach (var row in rows)
            {
                ws.Cell(r, 1).Value = row.Document;
                ws.Cell(r, 2).Value = row.Field;
                ws.Cell(r, 3).Value = row.InsertCount;
                ws.Cell(r, 4).Value = row.UpdateCount;
                ws.Cell(r, 5).Value = row.DeleteCount;
                ws.Cell(r, 6).Value = row.CriticalErrorCount;
                ws.Cell(r, 7).Value = row.MarkedDeleteCount;
                ws.Cell(r, 8).Value = row.TagCleanedCount;
                ws.Cell(r, 9).Value = row.Total;
                if (row.IsSubtotal || row.IsTotal)
                {
                    // Итоги — полужирным с заливкой (как в окне отчёта)
                    ws.Row(r).Style.Font.Bold = true;
                    ws.Row(r).Style.Fill.BackgroundColor =
                        XLColor.FromHtml(row.IsTotal ? "#DDE6F0" : "#EFF3F8");
                }
                r++;
            }
            ws.Columns().AdjustToContents();
            wb.SaveAs(filePath);
        }

        /// <summary>Экспорт списка точек (все колонки дерева IOclass) в .xlsx.</summary>
        public void ExportPoints(IEnumerable<IoPoint> points, string filePath)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("IO_LIST");

            var columns = FieldRegistry.Columns;
            for (int i = 0; i < columns.Length; i++)
            {
                ws.Cell(1, i + 1).Value = columns[i];
            }
            ws.Row(1).Style.Font.Bold = true;

            int r = 2;
            foreach (var p in points)
            {
                for (int i = 0; i < columns.Length; i++)
                {
                    var v = FieldRegistry.GetValue(p, columns[i]);
                    string text = v switch
                    {
                        null => "",
                        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm"),
                        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                        _ => v.ToString() ?? ""
                    };
                    ws.Cell(r, i + 1).Value = text;
                }
                r++;
            }
            ws.Columns().AdjustToContents();
            wb.SaveAs(filePath);
        }
    }

    /// <summary>Резервное копирование таблицы: копия с датой/временем в имени (Задание п.5).</summary>
    public sealed class BackupService : IBackupService
    {
        private readonly string _dbPath;

        public BackupService(string dbPath) => _dbPath = dbPath;

        private string ConnStr => $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={_dbPath};";

        /// <summary>SELECT * INTO [имя_копии] FROM [таблица]. Возвращает имя таблицы-копии.</summary>
        public string BackupTable(string tableName)
        {
            string copyName = $"{tableName}_BAK_{DateTime.Now:yyyyMMdd_HHmmss}";
            string sql = $"SELECT * INTO [{copyName}] FROM [{tableName}]";
            using var cn = new System.Data.OleDb.OleDbConnection(ConnStr);
            cn.Open();
            using var cmd = new System.Data.OleDb.OleDbCommand(sql, cn);
            cmd.ExecuteNonQuery();
            return copyName;
        }
    }
}
