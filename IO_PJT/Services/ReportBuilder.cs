using System;
using System.Collections.Generic;
using System.Linq;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>F7.8: построение отчёта «Формирование отчёта» по агрегату истории (AuditService.GetSummary):
    /// для каждого документа (файл + вкладка) — по каждой ячейке (полю) количество изменений по типам,
    /// строка-итог документа и общий итог.</summary>
    public static class ReportBuilder
    {
        /// <summary>Собрать строки отчёта. Порядок: документ → ячейки (по убыванию Total) → «ИТОГО» документа;
        /// последняя строка — «ВСЕГО» (IsTotal). Пустой агрегат → пустой список.</summary>
        public static List<AuditReportRow> Build(IEnumerable<AuditSummary> summary)
        {
            var data = summary.ToList();
            var rows = new List<AuditReportRow>();

            foreach (var doc in data.GroupBy(s => (s.FileName, s.Sheet))
                                    .OrderBy(g => g.Key.FileName ?? "", StringComparer.CurrentCulture)
                                    .ThenBy(g => g.Key.Sheet ?? "", StringComparer.CurrentCulture))
            {
                var fieldRows = new List<AuditReportRow>();
                foreach (var f in doc.GroupBy(s => s.Field))
                {
                    var row = new AuditReportRow
                    {
                        FileName = doc.Key.FileName,
                        Sheet = doc.Key.Sheet,
                        Document = FormatDoc(doc.Key.FileName, doc.Key.Sheet),
                        Field = f.Key,
                        Total = f.Sum(x => x.Count)
                    };
                    foreach (var a in f)
                    {
                        switch (a.Action)
                        {
                            case AuditAction.Insert: row.InsertCount += a.Count; break;
                            case AuditAction.Update: row.UpdateCount += a.Count; break;
                            case AuditAction.Delete: row.DeleteCount += a.Count; break;
                            case AuditAction.CriticalError: row.CriticalErrorCount += a.Count; break;
                            case AuditAction.MarkedDelete: row.MarkedDeleteCount += a.Count; break;
                            case AuditAction.TagCleaned: row.TagCleanedCount += a.Count; break;
                            case AuditAction.Duplicate: row.DuplicateCount += a.Count; break;
                            case AuditAction.ModbusWarning: row.ModbusWarningCount += a.Count; break;
                            case AuditAction.ModbusError: row.ModbusErrorCount += a.Count; break;
                        }
                    }
                    fieldRows.Add(row);
                }

                rows.AddRange(fieldRows.OrderByDescending(r => r.Total)
                                       .ThenBy(r => r.Field, StringComparer.CurrentCulture));

                // Строка-итог по документу («файл :: вкладка → ИТОГО»)
                var sub = new AuditReportRow
                {
                    FileName = doc.Key.FileName,
                    Sheet = doc.Key.Sheet,
                    Document = FormatDoc(doc.Key.FileName, doc.Key.Sheet),
                    Field = "ИТОГО",
                    IsSubtotal = true,
                    Total = doc.Sum(x => x.Count)
                };
                foreach (var r in fieldRows)
                {
                    sub.InsertCount += r.InsertCount;
                    sub.UpdateCount += r.UpdateCount;
                    sub.DeleteCount += r.DeleteCount;
                    sub.CriticalErrorCount += r.CriticalErrorCount;
                    sub.MarkedDeleteCount += r.MarkedDeleteCount;
                    sub.TagCleanedCount += r.TagCleanedCount;
                    sub.DuplicateCount += r.DuplicateCount;
                    sub.ModbusWarningCount += r.ModbusWarningCount;
                    sub.ModbusErrorCount += r.ModbusErrorCount;
                }
                rows.Add(sub);
            }

            if (rows.Count > 0)
            {
                // Общий итог по всем документам
                var total = new AuditReportRow
                {
                    Document = "ВСЕГО",
                    Field = "ВСЕГО",
                    IsTotal = true,
                    Total = data.Sum(x => x.Count)
                };
                foreach (var sub in rows.Where(r => r.IsSubtotal))
                {
                    total.InsertCount += sub.InsertCount;
                    total.UpdateCount += sub.UpdateCount;
                    total.DeleteCount += sub.DeleteCount;
                    total.CriticalErrorCount += sub.CriticalErrorCount;
                    total.MarkedDeleteCount += sub.MarkedDeleteCount;
                    total.TagCleanedCount += sub.TagCleanedCount;
                    total.DuplicateCount += sub.DuplicateCount;
                    total.ModbusWarningCount += sub.ModbusWarningCount;
                    total.ModbusErrorCount += sub.ModbusErrorCount;
                }
                rows.Add(total);
            }
            return rows;
        }

        /// <summary>Отображение документа: «файл :: вкладка» (как в диалоге замены F2.16);
        /// NULL — «(без файла)», «(без вкладки)».</summary>
        public static string FormatDoc(string? fileName, string? sheet)
        {
            string file = string.IsNullOrWhiteSpace(fileName) ? "(без файла)" : fileName!;
            return string.IsNullOrWhiteSpace(sheet) ? $"{file} (без вкладки)" : $"{file} :: {sheet}";
        }
    }
}