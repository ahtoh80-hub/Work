using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IO_PJT.Data;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// Оркестрация импорта Excel → таблица Access (ТЗ F2):
    /// вкладка/строка, привязка «Excel ↔ колонки таблицы», правила очистки,
    /// автозаполнение, валидация, upsert по Tag, отчёт и журнал.
    /// </summary>
    public sealed class ImportService
    {
        private readonly IIOListRepository _repo;
        private readonly IValidator _validator;
        private readonly IEventLogger _log;

        public ImportService(IIOListRepository repo, IValidator validator, IEventLogger log)
        {
            _repo = repo;
            _validator = validator;
            _log = log;
        }

        /// <summary>Выполнить импорт по параметрам и маппингу. Возвращает отчёт (F2.6).
        /// progress — прогресс записи (строки + аудит) для ProgressBar.</summary>
        public ImportReport Import(ImportOptions options, IReadOnlyList<ColumnMap> mappings,
            IProgress<ImportProgress>? progress = null)
        {
            var report = new ImportReport();

            // F2.2 — обязательное минимальное поле Tag: без привязки импорт блокируется
            bool hasTag = mappings.Any(m =>
                string.Equals(m.AccessColumn, "Tag", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(m.ExcelColumn));
            if (!hasTag)
            {
                report.Validation.Add(ErrCode.E001_MissingTagMapping,
                    "Не задана привязка обязательного поля Tag — импорт невозможен");
                _log.Error("Импорт заблокирован: нет привязки поля Tag (F2.2)");
                return report;
            }

            // Галочка «Импорт с аудитом» (F2.7): задаётся на время импорта
            bool previousAudit = _repo.WriteAudit;
            _repo.WriteAudit = options.WithAudit;

            try
            {
                // F2.9: одна сессия на весь импорт — одно соединение + одна транзакция + батч аудита
                _repo.BeginSession();
                progress?.Report(new ImportProgress { Phase = "Чтение Excel" });
                using var reader = new ExcelReader(options.ExcelPath);

                // Заголовки строки HeaderRow (первая строка данных — названия колонок)
                var headers = reader.GetHeaders(options.SheetName, options.HeaderRow);
                var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Count; i++)
                    if (!string.IsNullOrWhiteSpace(headers[i]) && !headerIndex.ContainsKey(headers[i]))
                        headerIndex[headers[i]] = i;

                // Активные привязки: колонка резолвится по позиции ExcelColumnIndex (F2.14 — дубликаты
                // заголовков), иначе по имени; автозаполняемые поля исключаются
                var active = new List<(string Column, int Index)>();
                foreach (var m in mappings)
                {
                    if (string.IsNullOrWhiteSpace(m.AccessColumn) ||
                        string.IsNullOrWhiteSpace(m.ExcelColumn) ||
                        Schema.AutoColumns.Contains(m.AccessColumn))
                        continue;
                    int? idx = ExcelColumns.Resolve(m, headers, headerIndex);
                    if (idx.HasValue) active.Add((m.AccessColumn, idx.Value));
                }

                // Режим «Заменить» (F2.7)
                if (options.Mode == ImportMode.Replace)
                {
                    progress?.Report(new ImportProgress { Phase = "Очистка таблицы" });
                    _repo.Clear();
                }

                var rows = reader.ReadRows(options.SheetName, options.HeaderRow);
                report.RowsRead = rows.Count;
                progress?.Report(new ImportProgress
                {
                    Phase = options.WithAudit ? "Запись (данные + аудит)" : "Запись (данные)",
                    Processed = 0, Total = rows.Count
                });
                var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var result = ProcessRows(options, mappings, active, rows, report, seenTags, progress);

                // ===== F2.11: замена документа — пометка отсутствующих в новой версии тегов =====
                if (options.Mode == ImportMode.Upsert && !string.IsNullOrWhiteSpace(options.ReplacedFileName))
                {
                    progress?.Report(new ImportProgress
                    {
                        Phase = "Замена документа: пометка отсутствующих тегов",
                        Processed = rows.Count, Total = rows.Count
                    });
                    string newFile = Path.GetFileName(options.ExcelPath);
                    var marked = _repo.MarkMissingTagsForDeletion(seenTags,
                        options.ReplacedFileName!, options.ReplacedSheet, newFile, options.SheetName);
                    report.MarkedForDeletion = marked.Count;
                    string docOld = string.IsNullOrWhiteSpace(options.ReplacedSheet)
                        ? $"«{options.ReplacedFileName}»" : $"«{options.ReplacedFileName}» (вкладка «{options.ReplacedSheet}»)";
                    foreach (var tag in marked)
                    {
                        _log.Error($"Tag {tag}: отсутствует в новой версии файла {newFile} — помечен к удалению (необходимо удалить из БД)");
                        report.Validation.Add(ErrCode.W004_MarkedForDeletion,
                            $"Тег документа {docOld} отсутствует в новой версии файла {newFile} — помечен к удалению (REV_Description); физическое удаление не выполняется",
                            tag, "REV_Description");
                    }
                }

                progress?.Report(new ImportProgress
                {
                    Phase = options.WithAudit ? "Фиксация (данные + аудит)" : "Фиксация (данные)",
                    Processed = rows.Count,
                    Total = rows.Count
                });
                _repo.EndSession(); // COMMIT данных + запись батча аудита
                progress?.Report(new ImportProgress
                {
                    Phase = "Готово",
                    Processed = rows.Count,
                    Total = rows.Count,
                    Inserted = report.Inserted,
                    Updated = report.Updated
                });
                _log.Info("Импорт завершён. " + report.Summary);
                return result;
            }
            catch (Exception ex)
            {
                report.Errors++;
                report.Validation.Add(ErrCode.E006_ExcelReadError, ex.Message);
                _log.Error($"Ошибка импорта: {ex.Message}");
                return report;
            }
            finally
            {
                _repo.AbortSession(); // после успешного EndSession — бездействует (ROLLBACK при ошибке)
                _repo.WriteAudit = previousAudit; // восстановление режима аудита
            }
        }

        /// <summary>Построчная обработка данных: очистка, автозаполнение, валидация, upsert + прогресс.
        /// seenTags — все уникальные теги файла (нужен замене документа F2.11).</summary>
        private ImportReport ProcessRows(ImportOptions options, IReadOnlyList<ColumnMap> mappings,
            List<(string Column, int Index)> active,
            IReadOnlyList<ExcelRowData> rows, ImportReport report,
            HashSet<string> seenTags,
            IProgress<ImportProgress>? progress = null)
        {
            string phase = options.WithAudit ? "Запись (данные + аудит)" : "Запись (данные)";

            // F2.12: индекс ячейки Tag в строке — зачёркнутая ячейка Tag исключает всю строку
            int tagIndex = -1;
            foreach (var a in active)
                if (string.Equals(a.Column, "Tag", StringComparison.OrdinalIgnoreCase))
                { tagIndex = a.Index; break; }

            // Троттлинг: UI получает ≤10 обновлений/с (прогресс-бар + текущий тег), последняя строка — всегда.
            // Построчные отчёты дёргали UI-поток на каждой строке импорта.
            long lastReportMs = Environment.TickCount64;
            void ReportProgress(int processed, string? tag)
            {
                long now = Environment.TickCount64;
                if (processed < rows.Count && now - lastReportMs < 100) return;
                lastReportMs = now;
                progress?.Report(new ImportProgress
                {
                    Phase = phase,
                    Processed = processed,
                    Total = rows.Count,
                    Inserted = report.Inserted,
                    Updated = report.Updated,
                    CurrentTag = tag
                });
            }

            // F2.10: множество замаппленных колонок — upsert сравнивает только их,
            // чтобы немаппленные колонки Excel (null) не затирали поля БД.
            // Code заполняется программой (RowNumber-1) — сравниваем всегда.
            var mappedCols = new HashSet<string>(
                active.Select(a => a.Column), StringComparer.OrdinalIgnoreCase)
            {
                "Code"
            };

            // F2.12: итоговый перечень зачёркнутых тегов — в журнал по завершении
            var struckTags = new List<string>();

            for (int d = 0; d < rows.Count; d++)
            {
                var row = rows[d];
                int excelRow = options.HeaderRow + 1 + d;

                // F2.3 — строки-разделители (все ячейки «-») не импортируются
                if (IsSeparatorRow(row.Cells)) { report.SeparatorsSkipped++; ReportProgress(d + 1, null); continue; }

                // ===== F2.12: ячейка Tag зачёркнута → строка исключена из проекта =====
                // Не в БД и НЕ в seenTags — при замене документа (F2.11) пометится как отсутствующий.
                if (tagIndex >= 0 && row.IsStruck(tagIndex))
                {
                    report.StruckThroughSkipped++;
                    string struckTag = row.Cell(tagIndex)?.Trim() ?? "";
                    report.Validation.Add(ErrCode.W005_StruckThroughRow,
                        $"Строка зачёркнута в Excel — не импортирована" +
                        (struckTag.Length > 0 ? $" (Tag={struckTag})" : ""),
                        struckTag.Length > 0 ? struckTag : null, "Tag", excelRow);
                    _log.Warn($"Строка {excelRow}: зачёркнута ячейка Tag" +
                              (struckTag.Length > 0 ? $" ({struckTag})" : "") +
                              " — строка исключена из импорта (F2.12)");
                    if (struckTag.Length > 0) struckTags.Add(struckTag);
                    ReportProgress(d + 1, struckTag.Length > 0 ? struckTag : null);
                    continue;
                }

                var point = new IoPoint();
                foreach (var (column, index) in active)
                    if (index < row.Cells.Count)
                    {
                        // F2.12: зачёркнутая ячейка = значение отменено автором → пусто (NULL)
                        object? val = row.IsStruck(index) ? null : row.Cells[index];
                        // F2.3: «-» в ячейке означает отсутствие значения → NULL
                        if (val is string s && s.Trim() == "-") val = null;
                        FieldRegistry.SetValue(point, column, val);
                    }

                // ===== F2.15: очистка Tag от пробельных символов (в т.ч. NBSP \u00A0) =====
                // до TagParser.Fill (PTag_* считаются из чистого Tag), валидации и upsert
                string? rawTag = point.Identification.Tag;
                string? cleanTag = TagParser.CleanWhitespace(rawTag);
                if (!string.Equals(rawTag, cleanTag, StringComparison.Ordinal))
                {
                    point.Identification.Tag = cleanTag;
                    report.Validation.Add(ErrCode.W006_TagCleaned,
                        $"Из Tag удалены пробельные символы: «{rawTag}» → «{cleanTag ?? ""}»",
                        cleanTag, "Tag", excelRow);
                    _log.Warn($"Строка {excelRow}: Tag очищен от пробелов: «{rawTag}» → «{cleanTag ?? ""}» (F2.15)");
                    if (options.WithAudit)
                        _repo.RecordTagCleanedAudit(rawTag, cleanTag,
                            Path.GetFileName(options.ExcelPath), options.SheetName,
                            point.Identification.System); // красный аудит TagCleaned (F2.15)
                }

                // ===== Автозаполнение (Б8 п.9, ТЗ F2.5) =====
                TagParser.Fill(point);                       // PTag_* — из Tag
                point.Revision ??= new Revision();
                point.Revision.FileName = Path.GetFileName(options.ExcelPath);
                point.Revision.Sheet = options.SheetName;         // F2.16: документ = файл + вкладка
                point.Revision.RowNumber = excelRow;         // № строки Excel, где Tag
                point.Revision.Author = AuditService.CurrentUser;
                if (!active.Any(a => string.Equals(a.Column, "Code", StringComparison.OrdinalIgnoreCase)))
                    point.Identification.Code = excelRow - 1; // Code = RowNumber - 1

                // Пустой Tag (F5.1)
                if (string.IsNullOrWhiteSpace(point.Tag))
                {
                    report.Errors++;
                    report.Validation.Add(ErrCode.E002_EmptyTag,
                        "Пустое значение обязательного поля Tag", null, "Tag", excelRow);
                    _log.Error($"Строка {excelRow}: пустой Tag — строка пропущена");
                    ReportProgress(d + 1, null);
                    continue;
                }

                // Повтор тега внутри одного файла (F2.7)
                if (!seenTags.Add(point.Tag!))
                {
                    report.Errors++;
                    report.Validation.Add(ErrCode.E005_DuplicateTagInFile,
                        $"Повтор тега в файле: {point.Tag}", point.Tag, "Tag", excelRow);
                    _log.Error($"Строка {excelRow}: повтор тега {point.Tag} — строка пропущена");
                    ReportProgress(d + 1, point.Tag);
                    continue;
                }

                // Валидация F5: предупреждения и E003 (иерархия уставок) НЕ блокируют — строка пишется;
                // прочие ошибки (E002/E004/E005...) — строка не записывается.
                var validation = _validator.Validate(point, excelRow);
                report.Validation.Items.AddRange(validation.Items);
                // Гарантия журнала (Задание п.8): каждая критическая ошибка валидации — в журнал
                // немедленно и всегда, даже при выключенной галочке «Импорт с аудитом» (журнал от WriteAudit не зависит)
                foreach (var err in validation.Errors)
                    _log.Error($"Строка {excelRow} (Tag={point.Tag}): {err.Code}: {err.Message}");
                bool blocking = validation.Errors.Any(e => e.Code != ErrCode.E003_AlarmHierarchy);
                if (blocking)
                {
                    report.Errors++;
                    _log.Error($"Строка {excelRow} (Tag={point.Tag}): ошибки валидации — не записана");
                    ReportProgress(d + 1, point.Tag);
                    continue;
                }

                // Upsert по Tag (F2.7/F2.8): вставка / частичное обновление / без изменений / конфликт
                var result = _repo.Upsert(point, mappedCols);
                switch (result.Outcome)
                {
                    case UpsertOutcome.Inserted:
                        report.Inserted++;
                        break;
                    case UpsertOutcome.Updated:
                        report.Updated++;
                        break;
                    case UpsertOutcome.Unchanged:
                        report.Unchanged++;
                        break;
                    case UpsertOutcome.Conflict:
                        report.Conflicts++;
                        report.Errors++;
                        report.Validation.Add(ErrCode.E009_TagConflict,
                            $"Конфликт System/Plant — строка игнорирована (БД: {result.OldIdentity}; Excel: {result.NewIdentity})",
                            point.Tag, "System/Plant", excelRow);
                        // Гарантия журнала: критическая ошибка — в журнал всегда, с кодом E009
                        _log.Error($"Строка {excelRow} (Tag={point.Tag}): E009: конфликт System/Plant — строка игнорирована (БД: {result.OldIdentity}; Excel: {result.NewIdentity})");
                        break;
                }
                if (result.IdentityNotChecked)
                    report.Validation.Add(ErrCode.W003_IdentityNotChecked,
                        "System/Plant не задан в импортируемых данных — проверка идентичности пропущена",
                        point.Tag, "System/Plant", excelRow);
                ReportProgress(d + 1, point.Tag);
            }

            // F2.12: итоговый перечень зачёркнутых тегов — журнал (жёлтым, как предупреждение)
            if (struckTags.Count > 0)
                _log.Warn($"Зачёркнуты в Excel и пропущены (F2.12): {struckTags.Count} — {string.Join(", ", struckTags)}");

            return report;
        }

        /// <summary>Строка-разделитель: все непустые ячейки равны «-».</summary>
        private static bool IsSeparatorRow(IReadOnlyList<string?> cells) =>
            cells.All(c => string.IsNullOrWhiteSpace(c) || c.Trim() == "-") &&
            cells.Any(c => !string.IsNullOrWhiteSpace(c));
    }
}
