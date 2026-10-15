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

                // ===== F2.11/F2.17: замена документов (мультивыбор — ревизии/вкладки) =====
                if (options.Mode == ImportMode.Upsert && options.ReplacedDocuments.Count > 0)
                {
                    progress?.Report(new ImportProgress
                    {
                        Phase = "Замена документов: пометка отсутствующих тегов",
                        Processed = rows.Count, Total = rows.Count
                    });
                    string newFile = Path.GetFileName(options.ExcelPath);
                    var marked = _repo.MarkMissingTagsForDeletion(seenTags,
                        options.ReplacedDocuments, newFile, options.SheetName);
                    report.MarkedForDeletion = marked.Count;
                    string docsOld = string.Join(", ", options.ReplacedDocuments.Select(d =>
                        d.Sheet == null ? $"«{d.FileName}» (без вкладки)" : $"«{d.FileName}» (вкладка «{d.Sheet}»)"));
                    foreach (var tag in marked)
                    {
                        _log.Error($"Tag {tag}: отсутствует в новой версии файла {newFile} — помечен к удалению (необходимо удалить из БД)");
                        report.Validation.Add(ErrCode.W004_MarkedForDeletion,
                            $"Тег документов [{docsOld}] отсутствует в новой версии файла {newFile} — помечен к удалению (REV_Description); физическое удаление не выполняется",
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

            // ===== F2.17: первый проход — частота тегов в файле =====
            // Массовый маркер (повтор ≥ MassMarkerThreshold раз) — служебная заглушка: строка не пишется
            // ни в IO_LIST, ни в IO_LIST_DublicateTag. Обычный повтор — строка пишется (upsert, не
            // пропускается), а все копии тега (включая первую) копируются в IO_LIST_DublicateTag.
            const int MassMarkerThreshold = 10;
            var tagFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (tagIndex >= 0)
            {
                foreach (var row in rows)
                {
                    if (IsSeparatorRow(row.Cells)) continue;
                    if (row.IsStruck(tagIndex)) continue; // зачёркнутая строка в импорт не идёт (F2.12)
                    string? key = TagParser.CleanWhitespace(row.Cell(tagIndex)); // нормализация как в цикле (F2.15)
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    tagFreq[key] = tagFreq.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
            var dupReported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // F9.1: теги, для которых в этой сессии импорта уже записано задвоение между документами
            // (один тег — одна запись Duplicate, даже если встречается несколько раз)
            var dupOtherDocReported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                // ===== F2.17: повторы тегов внутри файла (E005 больше не блокирует строку) =====
                string tagKey = point.Tag!.Trim();
                tagFreq.TryGetValue(tagKey, out int freq);
                if (freq >= MassMarkerThreshold)
                {
                    // Массовый маркер/заглушка — не записывается вовсе (ни в IO_LIST, ни в копии)
                    report.Errors++;
                    report.MassMarkersSkipped++;
                    report.Validation.Add(ErrCode.E005_DuplicateTagInFile,
                        $"Массовый маркер: тег повторяется в файле {freq} раз (≥{MassMarkerThreshold}) — служебная заглушка, строка не записана",
                        point.Tag, "Tag", excelRow);
                    _log.Error($"Строка {excelRow}: тег {point.Tag} встречается в файле {freq} раз — массовый маркер, строка пропущена");
                    ReportProgress(d + 1, point.Tag);
                    continue;
                }

                // Все уникальные теги файла — для замены документов (F2.11); повтор НЕ пропускает строку
                seenTags.Add(point.Tag!);
                bool duplicateInFile = freq >= 2;
                if (duplicateInFile && dupReported.Add(point.Tag!))
                {
                    // F2.17: строка с повтором пишется (upsert), все копии тега → IO_LIST_DublicateTag
                    report.Validation.Add(ErrCode.W007_DuplicateTagRecorded,
                        $"Повтор тега в файле ({freq} раз): строка записана, все копии продублированы в {Schema.DuplicateTable}",
                        point.Tag, "Tag", excelRow);
                    _log.Warn($"Строка {excelRow}: повтор тега {point.Tag} ({freq} раз) — строка записана, копии → {Schema.DuplicateTable} (F2.17)");
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

                // ===== F9.1: задвоение тега между документами =====
                // Тег уже существует в БД из другого документа (файл + вкладка), а импортируется
                // из текущего — равноправная запись аудита Duplicate (жёлтым) + W008 + журнал.
                // Замена документов (F2.11/F2.16) — штатное перенесение, не задвоение.
                if ((result.Outcome is UpsertOutcome.Updated or UpsertOutcome.Unchanged) &&
                    !string.IsNullOrWhiteSpace(result.ExistingFileName) &&
                    !IsSameDocument(result.ExistingFileName, result.ExistingSheet,
                        point.Revision?.FileName, point.Revision?.Sheet) &&
                    !IsReplacedDocument(options, result.ExistingFileName, result.ExistingSheet) &&
                    dupOtherDocReported.Add(point.Tag!))
                {
                    report.DuplicatesOtherDoc++;
                    _repo.RecordDuplicateTagAudit(point.Tag,
                        result.ExistingFileName, result.ExistingSheet,
                        point.Revision?.FileName, point.Revision?.Sheet,
                        point.Identification.System);
                    report.Validation.Add(ErrCode.W008_DuplicateTagOtherDoc,
                        $"Тег уже существует в БД из другого документа «{result.ExistingFileName}»" +
                        (string.IsNullOrWhiteSpace(result.ExistingSheet) ? "" : $" :: «{result.ExistingSheet}»") +
                        " — задвоение записано в историю (Duplicate)",
                        point.Tag, "Tag", excelRow);
                    _log.Warn($"Строка {excelRow}: Tag {point.Tag} — задвоение между документами " +
                              $"(БД: «{result.ExistingFileName}»; импорт: «{point.Revision?.FileName}») — аудит Duplicate (F9.1)");
                }

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

                // F2.17: каждая строка с повторяющимся тегом (включая первую) копируется в
                // IO_LIST_DublicateTag — кроме конфликтов E009 (строка не записана в IO_LIST)
                if (duplicateInFile && result.Outcome != UpsertOutcome.Conflict)
                {
                    _repo.InsertDuplicate(point);
                    report.DuplicateCopies++;
                }
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

        /// <summary>F9.1: один и тот же документ (файл + вкладка, без учёта регистра).</summary>
        private static bool IsSameDocument(string? file1, string? sheet1, string? file2, string? sheet2) =>
            string.Equals(file1 ?? "", file2 ?? "", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(sheet1 ?? "", sheet2 ?? "", StringComparison.OrdinalIgnoreCase);

        /// <summary>F9.1: документ записи входит в список заменяемых (F2.11) — при замене документов
        /// перенос записей штатный, задвоением не считается.</summary>
        private static bool IsReplacedDocument(ImportOptions options, string? fileName, string? sheet) =>
            options.ReplacedDocuments.Any(d =>
                string.Equals(d.FileName ?? "", fileName ?? "", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.Sheet ?? "", sheet ?? "", StringComparison.OrdinalIgnoreCase));
    }
}
