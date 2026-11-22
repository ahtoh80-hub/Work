using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// F9: связывание записей IO_LIST со строками карты адресации Modbus (MODBUS_MAP).
    /// п.10: связывание ТОЛЬКО по правилам (маски) MODBUS_LINK_RULES — встроенные эвристики
    /// F9/F55/F66 (роль сигнала, полное совпадение имени, семейство объекта) отключены и
    /// из кода удалены. Маска правила оценивается по колонкам IO_LIST точки
    /// (ModbusBlockPlannerService.ColumnValuesOf) → паттерн ищется в значении MapField
    /// каждой строки карты (FindMapRowByMask, wildcard «*»); без маски — точное совпадение
    /// IOField ↔ MapField; FuncCode — опциональный фильтр строки карты.
    /// Из Service/Loop/Tag записи извлекаются кандидаты-объекты ПЛК (ExtractCandidates) —
    /// они отбирают MCC-сигналы (не-MCC точки в связывании не участвуют) и показываются
    /// в сообщении «Не связано».
    /// При совпадении заполняются LinkTag/DcsTag/LinkState строки карты (F9.4: M_-поля в
    /// IO_LIST при импорте НЕ пишутся — запись адресации выполняет кнопка
    /// «Обновить адресацию Modbus», UpdateAddressing); связь допустима только для
    /// Modbus-сигнала (IO Type «M…»/«COM»).
    /// Несопоставимые записи попадают в UnlinkedSamples (превью окна импорта карты).
    /// F9.8: сообщения связывания/адресации («Не связано», ошибки) пишутся в аудит
    /// IO_LIST_Audit (ModbusWarning — жёлтый, ModbusError — красный).
    /// F59: два режима связывания — Full (полное обновление: LinkTag/DcsTag/LinkState всех строк
    /// карты пересчитываются заново, устаревшие DcsTag стираются) и FillEmpty (заполняются только
    /// строки без привязки; заполненные — ручные или из файла — не трогаются, их теги не
    /// перенаправляются).
    /// </summary>
    public sealed class ModbusLinkService
    {
        private readonly IIOListRepository _repo;
        private readonly ModbusMapService _maps;
        private readonly IEventLogger _log;
        private readonly IAuditService _audit;
        private readonly ModbusLinkRuleService? _rules;

        /// <summary>rules (F9.11): хранилище ручных правил связывания MODBUS_LINK_RULES —
        /// null допустим (правила не применяются, прежнее поведение).</summary>
        public ModbusLinkService(IIOListRepository repo, ModbusMapService maps, IEventLogger log,
            IAuditService audit, ModbusLinkRuleService? rules = null)
        {
            _repo = repo;
            _maps = maps;
            _log = log;
            _audit = audit;
            _rules = rules;
        }

        /// <summary>Выполнить связывание текущей таблицы IO_LIST с MODBUS_MAP (F9.4: только связь —
        /// LinkTag/LinkState/DcsTag; M_-поля в IO_LIST не записываются, см. UpdateAddressing).
        /// F59: mode — Full (по умолчанию): сброс LinkTag/LinkState/DcsTag у ВСЕХ строк карты
        /// и полный пересчёт (ручные правки затираются — вызывающая сторона предупреждает);
        /// FillEmpty: заполняются только строки карты без привязки (пустой LinkTag и DcsTag) —
        /// вручную привязанные сигналы не сбрасываются, а точки из заполненных связей
        /// не перенаправляются на другие строки.</summary>
        public ModbusLinkResult Link(ModbusLinkMode mode = ModbusLinkMode.Full,
            IProgress<(int Done, int Total, string? Tag)>? progress = null)
        {
            var result = new ModbusLinkResult { Mode = mode };
            var points = _repo.GetAll();
            var map = _maps.LoadFromDb();
            if (map.Count == 0)
            {
                _log.Warn("MODBUS_MAP пуст — сначала импортируйте карту адресации (F9)");
                return result;
            }

            // F59: закреплённые строки — заполненные вручную/из файла (FillEmpty);
            // масочный матчинг (FindMapRowByMask) их не трогает
            var locked = new HashSet<long>();

            // F9.3/F59: полное обновление — сброс устаревшей привязки перед новым прогоном:
            // LinkState/LinkTag обнуляются и в БД, и в памяти, каждый прогон расставляет
            // состояния заново. F59: полный режим сбрасывает и DcsTag (иначе у строк,
            // потерявших связь, остаётся устаревший тег адресации — остаток F55
            // «1612-P-611A vs 1612-P-611A.HE-JB»); FillEmpty ничего не сбрасывает.
            bool fillEmpty = mode == ModbusLinkMode.FillEmpty;
            var takenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (fillEmpty)
            {
                // Строка с непустым LinkTag (автосвязь) либо DcsTag (ручной ввод/файл) —
                // заполненная: закрепляется, а её теги исключаются из кандидатов-точек
                foreach (var m in map)
                {
                    bool filled = !string.IsNullOrWhiteSpace(m.LinkTag) ||
                                  !string.IsNullOrWhiteSpace(m.DcsTag);
                    if (!filled) continue;
                    locked.Add(m.Id);
                    foreach (var part in SplitTags(m.LinkTag).Concat(SplitTags(m.DcsTag)))
                        takenTags.Add(part);
                }
                result.PreservedFilled = locked.Count;
                _log.Info($"F59: режим «только пустые связи» — заполненных строк карты: " +
                          $"{locked.Count} (сохраняются без изменений)");
            }
            else
            {
                _maps.ResetLinkResults(includeDcsTag: true);
                foreach (var m in map)
                {
                    m.LinkTag = null;
                    m.LinkState = null;
                    m.DcsTag = null;
                }
            }

            // F9.11/F99: ручные правила связывания (MODBUS_LINK_RULES) — невалидное правило
            // (неизвестная колонка в маске) пропускается с предупреждением
            var rules = new List<ModbusLinkRule>();
            if (_rules != null)
            {
                rules = _rules.LoadEnabled();
                foreach (var r in rules)
                {
                    string? err = ModbusLinkRuleService.Validate(r);
                    if (err != null)
                    {
                        _log.Warn($"F9.11: правило «{r.Name}» пропущено: {err}");
                        r.Enabled = false;
                    }
                }
                rules.RemoveAll(r => !r.Enabled);
                if (rules.Count > 0)
                    _log.Info($"F9.11: применяются ручные правила связывания ({rules.Count}): " +
                              string.Join(", ", rules.Select(r => $"«{r.Name}»")));
            }

            // F9.8: сообщения связывания (предупреждения/ошибки) копятся и пишутся в аудит
            // (IO_LIST_Audit) после ApplyLinkResults — без открытой транзакции MODBUS_MAP.
            var auditMsgs = new List<(string? Tag, AuditAction Action, string? FileName, string? Sheet, string Text)>();

            // F9-фикс: результаты привязки копятся в памяти и записываются после цикла
            // одной транзакцией (ApplyLinkResults) — без открытой сессии по IO_LIST
            // (JET 3260 «Could not update; currently locked» при параллельных соединениях).
            // F9.4: M_-поля в IO_LIST при импорте НЕ пишутся — только связь карты
            // (LinkTag/LinkState/DcsTag); адресацию заполняет кнопка «Обновить адресацию Modbus».
            // каждая строка несёт своё состояние LinkState («привязано» при привязке)
            var pending = new List<(long Id, string? LinkTag, string? DcsTag, string LinkState)>();

            // F66: один сигнал — не более одного объекта: тег точки, уже привязанный к строке
            // карты, не может привязаться и к другой строке (дубль тега в IO_LIST и т.п.) —
            // второй адрес на один сигнал запрещён
            var boundTags = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            // F9-F55/F99: точки с кандидатами готовятся один раз; F99 — из связывания
            // ушли ExtractCandidates/ServiceField: участвуют ВСЕ точки с заполненным Tag
            // (отбор MCC-сигналов больше не выполняется — только маски правил)
            var work = new List<IoPoint>();
            foreach (var wp in points)
            {
                if (string.IsNullOrWhiteSpace(wp.Tag)) continue;
                // F59: FillEmpty — точка уже указана в заполненной связи строки карты →
                // не перенаправляем её на другую строку (существующая связь авторитетна)
                if (fillEmpty && takenTags.Contains(wp.Tag!.Trim()))
                {
                    result.SkippedAlreadyLinked++;
                    continue;
                }
                work.Add(wp);
            }
            result.Examined = work.Count;

            var done = new bool[work.Count];

            // п.10: связывание ТОЛЬКО по правилам (маски) — эвристики F9/F55/F66 отключены
            for (int wi = 0; wi < work.Count; wi++)
            {
                var p = work[wi];
                if (wi % 25 == 0)
                    progress?.Report((wi, work.Count, p.Tag));

                ModbusMapEntry? entry = null;
                ModbusLinkRule? matchedRule = null;
                int matchScore = 0;
                {
                    // п.10/F99: масочный матчинг по правилам (MODBUS_LINK_RULES) —
                    // маска оценивается по колонкам точки, паттерн ищется в MapField;
                    // из совпавших строк выбирается лучшая (минимальное отклонение Score)
                    var cols = ModbusBlockPlannerService.ColumnValuesOf(p);
                    foreach (var rule in rules)
                    {
                        var (row, score) = FindMapRowByMask(map, rule, p, locked, cols);
                        if (row != null) { entry = row; matchedRule = rule; matchScore = score; break; }
                    }
                }
                if (entry == null)
                {
                    // п.10: «Не связано» — после единственной фазы (правила/маски)
                    result.Unlinked++;
                    if (result.UnlinkedSamples.Count < 20)
                    {
                        string msg = p.Tag!;
                        result.UnlinkedSamples.Add(msg);
                        // F9.8: то же сообщение, что в журнале («Не связано: …»), — в аудит
                        auditMsgs.Add((p.Tag, AuditAction.ModbusWarning,
                            p.Revision?.FileName, p.Revision?.Sheet, $"Не связано: {msg}"));
                    }
                    continue;
                }

                done[wi] = true; // точка обработана (успех или ошибка)

                // F9.4: связь с картой допустима только для Modbus-сигнала (IO Type «M…» либо
                // содержит «COM»); не-Modbus точка не должна иметь адресацию — связь не
                // записываем, формируем ошибку
                if (!IsModbusSignal(p))
                {
                    result.Unlinked++;
                    string err =
                        $"связь отклонена: «{p.Tag}» — IO Type «{p.Identification.IoType ?? "?"}» " +
                        "не Modbus-сигнала (нет признака M*/COM); в карту не записана";
                    result.Errors.Add(err);
                    if (result.UnlinkedSamples.Count < 20)
                        result.UnlinkedSamples.Add($"{p.Tag} (не Modbus-сигнал)");
                    // F9.8: ошибка связывания → аудит (текст с префиксом журнала «Связывание Modbus: »)
                    auditMsgs.Add((p.Tag, AuditAction.ModbusError,
                        p.Revision?.FileName, p.Revision?.Sheet, $"Связывание Modbus: {err}"));
                    continue;
                }

                result.Linked++;
                result.MapRowsMatched++;
                // F9.11: статистика срабатывания ручного правила (LinkedByRule — только
                // реально записанные связи, т.е. после проверки IsModbusSignal)
                if (matchedRule != null)
                {
                    result.LinkedByRule++;
                    result.RuleHits[matchedRule.Name] =
                        result.RuleHits.TryGetValue(matchedRule.Name, out int n) ? n + 1 : 1;
                }

                // F66: один сигнал — не более одного объекта: если тег уже привязан к другой
                // строке карты (дубль тега в IO_LIST), повторная привязка к другому адресу
                // запрещена — иначе два адреса принадлежали бы одному сигналу
                if (boundTags.TryGetValue(p.Tag!, out long boundRow) && boundRow != entry.Id)
                {
                    result.SkippedAlreadyLinked++;
                    string warn = $"{p.Tag} — сигнал уже привязан к строке карты ID {boundRow}; " +
                                  $"повторная привязка к ID {entry.Id} пропущена (F66)";
                    _log.Warn(warn);
                    auditMsgs.Add((p.Tag, AuditAction.ModbusWarning,
                        p.Revision?.FileName, p.Revision?.Sheet, $"Связывание Modbus: {warn}"));
                    continue; // done[wi] уже true — точка обработана, строка не меняется
                }

                // F9.3: несколько команд, делящих один Control word, — все теги через запятую;
                // F9.4: DcsTag — тот же список для кнопки обновления адресации: ручной ввод
                // в колонке не затирается, теги дописываются без дублей
                string nextLink = AppendLinkTag(entry.LinkTag, p.Tag!);
                string nextDcs = AppendLinkTag(entry.DcsTag, p.Tag!);
                if (nextLink != entry.LinkTag || nextDcs != entry.DcsTag)
                {
                    entry.LinkTag = nextLink;
                    entry.DcsTag = nextDcs;
                    // F99: полное совпадение (Score=0) → «привязано»; маска с wildcard,
                    // поглотившим символы (Score>0) → «привязано частично» + предупреждение
                    bool partial = matchScore > 0;
                    entry.LinkState = partial
                        ? ModbusMapService.LinkStateLinkedPartial
                        : ModbusMapService.LinkStateLinked;
                    boundTags[p.Tag!] = entry.Id;
                    pending.Add((entry.Id, entry.LinkTag, entry.DcsTag, entry.LinkState));
                    if (partial)
                    {
                        string pwarn = $"{p.Tag} — привязано частично (маска): отклонение " +
                                       $"{matchScore} симв., строка «{entry.PlcTag}»";
                        _log.Warn(pwarn);
                        auditMsgs.Add((p.Tag, AuditAction.ModbusWarning,
                            p.Revision?.FileName, p.Revision?.Sheet, $"Связывание Modbus: {pwarn}"));
                    }
                }
            }

            // F9.3: привязки + разметка строк без результата («не привязано») — одна транзакция,
            // уже без открытой сессии репозитория
            _maps.ApplyLinkResults(pending);
            // F66: привязанными считаются оба состояния («привязано» и «привязано не по
            // полным совпадением!»)
            result.MapRowsLinked = map.Count(m => ModbusMapService.IsLinkedState(m.LinkState));
            result.MapRowsUnlinked = map.Count - result.MapRowsLinked;
            foreach (var t in _maps.GetUnlinkedPlcTags(10))
                result.MapUnlinkedSamples.Add(t);

            progress?.Report((work.Count, work.Count, null));
            _log.Info("Связывание Modbus завершено. " + result.Summary);
            // F9.11: какие ручные правила сработали и сколько точек они связали
            if (result.LinkedByRule > 0)
                _log.Info("F9.11: ручные правила связали " + result.LinkedByRule + " тег(ов): " +
                          string.Join(", ", result.RuleHits.Select(kv => $"«{kv.Key}» — {kv.Value}")));
            foreach (var s in result.UnlinkedSamples)
                _log.Warn($"Не связано: {s}");
            foreach (var e in result.Errors)
                _log.Warn($"Связывание Modbus: {e}");
            // F9.8: сообщения связывания → аудит (IO_LIST_Audit) — после ApplyLinkResults (транзакция закрыта)
            WriteLinkAudit(auditMsgs);
            return result;
        }

        /// <summary>F9.4: «Обновить адресацию Modbus» — записать M_-поля IO_LIST по парам
        /// MODBUS_MAP.DcsTag ↔ IO_LIST.Tag (одна сессия репозитория: транзакция + батч аудита F2.9/F7).
        /// Обновляются только Modbus-сигналы (см. IsModbusSignal); DcsTag, указывающий на точку
        /// без признака Modbus в IO Type, — ошибка «в карте адресации modbus указан DCSTAG не
        /// Modbus-сигнала», M_-поля этой точки не заполняются.</summary>
        public ModbusAddressingResult UpdateAddressing(
            IProgress<(int Done, int Total, string? Tag)>? progress = null)
        {
            var result = new ModbusAddressingResult();
            // F9.8: сообщения адресации (предупреждения/ошибки) → аудит после EndSession
            var auditMsgs = new List<(string? Tag, AuditAction Action, string? FileName, string? Sheet, string Text)>();
            var map = _maps.LoadFromDb();
            if (map.Count == 0)
            {
                _log.Warn("MODBUS_MAP пуст — сначала импортируйте карту адресации (F9)");
                return result;
            }

            // Индекс карты: каждый тег из DcsTag (список через запятую) → строки карты;
            // ручной ввод в колонке участвует наравне с заполненным импортом (ключ связи F9.4)
            var byDcsTag = new Dictionary<string, List<ModbusMapEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in map)
            {
                if (string.IsNullOrWhiteSpace(row.DcsTag)) continue;
                foreach (var part in row.DcsTag.Split(','))
                {
                    string tag = part.Trim();
                    if (tag.Length == 0) continue;
                    if (!byDcsTag.TryGetValue(tag, out var list))
                        byDcsTag[tag] = list = new List<ModbusMapEntry>();
                    if (!list.Contains(row)) list.Add(row); // «A, a» вручную — одна строка
                }
            }
            if (byDcsTag.Count == 0)
            {
                _log.Warn("В MODBUS_MAP нет заполненных DcsTag — адресация не обновлялась " +
                          "(заполните связь вручную в Access либо импортируйте карту с привязкой)");
                return result;
            }

            // Индекс IO_LIST по Tag (дубли тегов — первая запись, как в неоднозначности F9.3)
            var byTag = new Dictionary<string, IoPoint>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _repo.GetAll())
                if (!string.IsNullOrWhiteSpace(p.Tag))
                    byTag.TryAdd(p.Tag!.Trim(), p);

            _repo.BeginSession();
            try
            {
                int done = 0;
                foreach (var (tag, rows) in byDcsTag)
                {
                    done++;
                    if (done % 25 == 0) progress?.Report((done, byDcsTag.Count, tag));
                    result.Examined++;

                    if (!byTag.TryGetValue(tag, out var p))
                    {
                        result.NotFound++;
                        if (result.NotFoundSamples.Count < 10)
                            result.NotFoundSamples.Add(tag);
                        // F9.8: тег из DcsTag не найден → предупреждение в аудит (контекст — строка карты)
                        auditMsgs.Add((tag, AuditAction.ModbusWarning, rows[0].MapFile, rows[0].MapSheet,
                            $"тег из DcsTag не найден в IO_LIST: {tag} (карта «{rows[0].PlcTag}»)"));
                        continue;
                    }

                    if (!IsModbusSignal(p))
                    {
                        string err =
                            $"в карте адресации modbus указан DCSTAG не Modbus сигнала: «{tag}» " +
                            $"(IO Type «{p.Identification.IoType ?? "?"}» — нет признака M*/COM)";
                        result.Errors.Add(err);
                        // F9.8: ошибка адресации → аудит (контекст — строка карты-источника)
                        auditMsgs.Add((tag, AuditAction.ModbusError, rows[0].MapFile, rows[0].MapSheet,
                            $"Обновление адресации: {err}"));
                        continue;
                    }

                    if (rows.Count > 1)
                        _log.Warn($"F9.4: DcsTag «{tag}» указан в {rows.Count} строках карты — " +
                                  $"взята первая «{rows[0].PlcTag}»");

                    FillModbus(p, rows[0]);
                    if (_repo.Update(p) > 0) result.Updated++; // аудит по изменённым M_-полям (F7)
                }
                _repo.EndSession();
            }
            catch (Exception ex)
            {
                _repo.AbortSession();
                _log.Error($"Ошибка обновления адресации Modbus: {ex.Message}");
                throw;
            }

            // F9.8: сообщения адресации → аудит (IO_LIST_Audit) — после закрытия сессии репозитория
            WriteLinkAudit(auditMsgs);
            _log.Info("Обновление адресации Modbus завершено. " + result.Summary);
            foreach (var e in result.Errors)
                _log.Warn(e);
            return result;
        }

        // ================== Кандидаты-объекты ПЛК ==================

        /// <summary>Кандидат объекта ПЛК: тег + приоритет (0 — тег точки в виде полного имени
        /// объекта F55, 1 — ссылка в скобках, 2 — ведущее обозначение Service с хвостом
        /// суффикса F55, 3 — ведущее обозначение Service, 4 — Loop).</summary>
        public readonly record struct Candidate(string Tag, int Priority);

        private static readonly Regex ParenTag = new(@"\(([0-9]{3,4}-[A-Z0-9\-\.]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // F9.3/A: границы — только ASCII-буквы/цифры: в .NET \b считает кириллицу «словом»,
        // и тег, приклеенный к кириллице («1115-P-101AСостояние»), не распознавался
        private static readonly Regex HeadTag = new(@"(?<![A-Za-z0-9])([0-9]{3,4})-([A-Z]{1,3})-([0-9]+[A-Z]?)(?![A-Za-z0-9])",
            RegexOptions.Compiled);
        // F55: то же, что HeadTag, но С хвостом-суффиксом полного имени: «1612-P-611A.HE-JB motor …»
        // → группы: титул, класс, номер, хвост («.HE-JB», «-HE-JB», «.EH02», «-1»)
        private static readonly Regex HeadTagFull = new(
            @"(?<![A-Za-z0-9])([0-9]{3,4})-([A-Z]{1,3})-([0-9]+[A-Z]?)((?:[.\-][A-Za-z0-9]+)+)(?![A-Za-z0-9])",
            RegexOptions.Compiled);
        private static readonly Regex LoopTag = new(@"^([0-9]{3,4})-([A-Z]{1,3})-([0-9]+[A-Z]?)$",
            RegexOptions.Compiled);

        /// <summary>Извлечь кандидатов объекта ПЛК из Service/Loop/Tag записи (по приоритету).
        /// F9.3/A: сканируются оба поля — Service и ServiceEnglish (дубли скобочных ссылок отбрасываются).
        /// F55: приоритет 0 — сам тег точки, если это полное имя объекта («1612-P-611A.HE-JB»);
        /// приоритет 2 — ведущее обозначение Service ВМЕСТЕ с хвостом суффикса.
        /// Кандидаты используются для отбора MCC-сигналов и сообщения «Не связано» (п.10).</summary>
        // F69: текст-источник Service для кандидатов: default (null/иное) — Service +
        // ServiceEnglish; "Service"/"ServiceEnglish" — только выбранное поле (SCADA_LINK_SETTINGS)
        private static string ServiceText(IoPoint p, string? serviceField)
        {
            string f = (serviceField ?? "").Trim();
            if (f.Equals("Service", StringComparison.OrdinalIgnoreCase))
                return (p.Signal?.Service ?? "").Trim();
            if (f.Equals("ServiceEnglish", StringComparison.OrdinalIgnoreCase))
                return (p.Signal?.ServiceEnglish ?? "").Trim();
            return ((p.Signal?.Service ?? "").Trim() + " " +
                    (p.Signal?.ServiceEnglish ?? "").Trim()).Trim();
        }

        /// <summary>F69: serviceField — поле-источник Service (null — Service + ServiceEnglish).</summary>
        public static List<Candidate> ExtractCandidates(IoPoint p, string? serviceField = null)
        {
            var list = new List<Candidate>();
            string service = ServiceText(p, serviceField).Trim(); // F69

            // 0) F55: тег точки — полное имя объекта («1612-P-611A.HE-JB», «1612-PM-611B-HE-JB»);
            //    DCS-теги без хвоста-суффикса («1612-MXA-611A») именем объекта не являются
            string? tagFull = NormalizeFullName(p.Tag ?? "");
            if (tagFull != null) list.Add(new Candidate(tagFull, 0));

            // 1) ссылка в скобках: «(1110-KM-101A-1)» — самый точный признак;
            //    F55: полное имя в скобках — «(1612-P-611A.HE-JB)» — тоже кандидат
            foreach (Match m in ParenTag.Matches(service))
            {
                string? tag = NormalizePlcTag(m.Groups[1].Value) ?? NormalizeFullName(m.Groups[1].Value);
                if (tag != null && !list.Any(c => c.Tag == tag)) list.Add(new Candidate(tag, 1));
            }

            // 2) F55: ведущее обозначение С хвостом суффикса: «1612-P-611A.HE-JB motor …» →
            //    1612-PM-611A.HE-JB (полное имя приоритетнее базового без хвоста)
            foreach (Match m in HeadTagFull.Matches(service))
            {
                string? tag = NormalizeFullName(
                    $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}{m.Groups[4].Value}");
                if (tag != null && !list.Any(c => c.Tag == tag)) list.Add(new Candidate(tag, 2));
            }

            // 3) ведущее обозначение: «1115-P-101A motor …» → 1115-PM-101A
            var head = HeadTag.Match(service);
            if (head.Success)
            {
                string? tag = NormalizePlcTag(
                    $"{head.Groups[1].Value}-{head.Groups[2].Value}-{head.Groups[3].Value}");
                if (tag != null && !list.Any(c => c.Tag == tag))
                    list.Add(new Candidate(tag, 3));
            }

            // 4) Loop: «1115-M-00101A» → 1115-PM-101A / 1115-KM-101A (класс не раскрывается —
            //    подставляем приводные классы по номеру, низкий приоритет).
            //    F67: подстановка ЧУЖОГО приводного класса отбрасывается, если тег/Service
            //    уже дали кандидат P- или K-класса: первый символ класса ключевой
            //    (PM≡P, KM≡K) — точка «2650-PM-2001A - Oil pump motor …» не «дотягивается»
            //    до объекта 2650-K-2001A (Loop-подстановка KM), и зеркально K-точка — до
            //    2650-P-2001A. Loop-only точка (кандидатов 0–3 нет) и DCS-классы
            //    (MXA/MII/MHS… — первый символ «M») подстановку не ограничивают.
            var loop = LoopTag.Match((p.Identification.Loop ?? "").Trim());
            if (loop.Success)
            {
                string num = loop.Groups[3].Value.TrimStart('0');
                if (num.Length == 0) num = "0";
                string area = loop.Groups[1].Value;
                foreach (string cls in new[] { "PM", "KM" })
                {
                    if (HasForeignDriveClass(list, cls)) continue; // F67
                    string tag = $"{area}-{cls}-{num}";
                    if (!list.Any(c => c.Tag == tag)) list.Add(new Candidate(tag, 4));
                }
            }

            return list.OrderBy(c => c.Priority).ToList();
        }

        /// <summary>F67: подстановка приводного класса Loop чужда точке? Истинна, если среди
        /// кандидатов ТЕГА/SERVICE (приоритеты 0–3, не сами Loop-подстановки) есть кандидат
        /// P- или K-класса (первый символ класса — ключевой признак объекта: PM≡P, KM≡K),
        /// а первый символ подстановки другой.</summary>
        private static bool HasForeignDriveClass(List<Candidate> cands, string loopCls)
        {
            char want = loopCls[0]; // подстановка: PM → 'P', KM → 'K'
            foreach (var c in cands)
            {
                if (c.Priority > 3) continue; // Loop-подстановки — не источник истины (F67)
                char got = FirstClassChar(c.Tag);
                if ((got == 'P' || got == 'K') && got != want) return true;
            }
            return false;
        }

        /// <summary>F67: первый символ класса объектного тега «ТТТТ-КЛ-НОМ[…]» ('P' у
        /// 2650-PM-2001A, 'M' у 2650-MXA-2001A); '\0' — тег не в формате объекта.</summary>
        private static char FirstClassChar(string tag)
        {
            int i = tag.IndexOf('-');
            if (i < 0 || i + 1 >= tag.Length) return '\0';
            int j = tag.IndexOf('-', i + 1);
            string cls = j < 0 ? tag.Substring(i + 1) : tag.Substring(i + 1, j - i - 1);
            return cls.Length > 0 ? char.ToUpperInvariant(cls[0]) : '\0';
        }

        /// <summary>Нормализация тега ПЛК: класс привода P→PM, K→KM; уборка ведущих нулей номера;
        /// хвост «-N» (машинный номер, напр. 1110-KM-101A-1) сохраняется.</summary>
        public static string? NormalizePlcTag(string raw)
        {
            var m = Regex.Match(raw.Trim(), @"^([0-9]{3,4})-([A-Z]{1,3})-([0-9]+[A-Z]?)(-[0-9])?$");
            if (!m.Success) return null;
            string cls = m.Groups[2].Value.ToUpperInvariant();
            if (cls == "P") cls = "PM";
            else if (cls == "K") cls = "KM";
            string num = m.Groups[3].Value.TrimStart('0');
            if (num.Length == 0) num = "0";
            string tail = m.Groups[4].Value; // «-1»/«-2» или пусто
            return $"{m.Groups[1].Value}-{cls}-{num}{tail}";
        }

        // ================== F55: полное совпадение имени ==================

        /// <summary>F55: полное имя объекта — NNNN-КЛАСС-НОМЕР + хвост-суффикс
        /// («.HE-JB», «-HE-JB», «.EH02», «-1»); класс нормализуется P→PM/K→KM, номер — без
        /// ведущих нулей (как NormalizePlcTag). Возвращает null, если тег — не полное
        /// имя объекта (нет хвоста-суффикса, напр. DCS-тег «1612-MXA-611A»).</summary>
        public static string? NormalizeFullName(string raw)
        {
            var m = Regex.Match(raw.Trim(),
                @"^([0-9]{3,4})-([A-Z]{1,3})-([0-9]+[A-Z]?)((?:[.\-][A-Za-z0-9]+)+)$");
            if (!m.Success) return null;
            string cls = m.Groups[2].Value;
            if (cls == "P") cls = "PM";
            else if (cls == "K") cls = "KM";
            string num = m.Groups[3].Value.TrimStart('0');
            if (num.Length == 0) num = "0";
            return $"{m.Groups[1].Value}-{cls}-{num}{m.Groups[4].Value}";
        }

        /// <summary>F9.8: записать сообщения связывания/адресации в аудит (IO_LIST_Audit) —
        /// предупреждения «Не связано»/«не найден» (ModbusWarning, жёлтый) и ошибки
        /// (ModbusError, красный); текст — тот же, что уходит в журнал событий.
        /// Вызывается после закрытия транзакции/сессии (ApplyLinkResults/EndSession);
        /// сбой записи аудита не должен ронять связывание — только предупреждение в журнал.</summary>
        private void WriteLinkAudit(List<(string? Tag, AuditAction Action, string? FileName, string? Sheet, string Text)> msgs)
        {
            // F9.9: аудит выключен (чекбокс окна импорта карт) — сообщения связывания остаются
            // только в журнале событий (они и так логируются в Link/UpdateAddressing)
            if (!_audit.Enabled) return;
            foreach (var (tag, action, fileName, sheet, text) in msgs)
            {
                try
                {
                    _audit.Record(_repo.TableName, tag, action, "Modbus", null, text, fileName, "Modbus", sheet);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Modbus аудит: не удалось записать сообщение в IO_LIST_Audit: {ex.Message}");
                }
            }
        }

        /// <summary>F59: разбор списка тегов из LinkTag/DcsTag («A, B») — отдельные теги.</summary>
        private static IEnumerable<string> SplitTags(string? s) =>
            (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>п.10/F99: строка карты по МАСКЕ правила — маска оценивается по колонкам
        /// IO_LIST точки (ColumnValuesOf) → паттерн ищется в значении MapField каждой строки
        /// карты (wildcard «*» = любые символы); из совпавших строк выбирается лучшая —
        /// с минимальным отклонением Score (см. MaskPattern.Score). Без маски — точное
        /// совпадение IOField ↔ MapField (Score=0). FuncCode — опциональный фильтр строки
        /// карты. locked — закреплённые строки (FillEmpty).
        /// Возвращает (строка, Score); строка=null — совпадений нет.</summary>
        private static (ModbusMapEntry? Row, int Score) FindMapRowByMask(
            List<ModbusMapEntry> map, ModbusLinkRule rule, IoPoint p,
            HashSet<long> locked, IReadOnlyDictionary<string, string> cols)
        {
            string? mask = string.IsNullOrWhiteSpace(rule.Mask) ? null : rule.Mask;
            MaskPattern? mp = mask != null ? MaskPattern.Parse(mask) : null;
            string pattern;
            if (mp != null)
            {
                pattern = mp.Evaluate(cols);
                if (pattern.Length == 0) return (null, 0);
            }
            else
            {
                pattern = ModbusLinkRuleService.GetIoValue(p, rule.IOField) ?? "";
                if (pattern.Length == 0) return (null, 0);
            }
            ModbusMapEntry? best = null;
            int bestScore = int.MaxValue;
            foreach (var r in map)
            {
                if (locked.Contains(r.Id)) continue;
                if (!string.IsNullOrWhiteSpace(rule.FuncCode) &&
                    !string.Equals(r.FuncCode?.Trim(), rule.FuncCode.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                string mapVal = ModbusLinkRuleService.GetMapValue(r, rule.MapField) ?? "";
                // F99: Score — отклонение совпадения (null — не совпадает; 0 — полное);
                // меньше Score — точнее, побеждает минимальный (первый при равенстве)
                int? s = mp != null
                    ? MaskPattern.Score(pattern, mapVal)
                    : (string.Equals(pattern, mapVal, StringComparison.OrdinalIgnoreCase)
                        ? 0 : (int?)null);
                if (s is int sc && sc < bestScore) { bestScore = sc; best = r; }
            }
            return (best, best == null ? 0 : bestScore);
        }

        /// <summary>F9.3: добавить тег в LinkTag строки карты (несколько команд делят один Control word;
        /// перечисление через запятую, без дублей).</summary>
        private static string AppendLinkTag(string? current, string tag)
        {
            if (string.IsNullOrWhiteSpace(current)) return tag;
            foreach (var part in current.Split(','))
                if (string.Equals(part.Trim(), tag, StringComparison.OrdinalIgnoreCase))
                    return current; // уже указан
            return current + ", " + tag;
        }

        // ================== Заполнение M_-полей ==================

        /// <summary>F9.4: признак Modbus-сигнала по IO Type: первая буква «M» (MAI, MDI…)
        /// либо вхождение «COM» (AI-COM, DO-COM…) без учёта регистра.</summary>
        public static bool IsModbusSignal(IoPoint p)
        {
            string io = (p.Identification.IoType ?? "").Trim().ToUpperInvariant();
            return io.StartsWith("M", StringComparison.Ordinal) ||
                   io.Contains("COM", StringComparison.Ordinal);
        }

        /// <summary>Заполнить M_-поля записи из строки карты (интерфейс + строка Register Table).
        /// F9.4: вызывается кнопкой «Обновить адресацию Modbus» (UpdateAddressing) — при импорте
        /// карты M_-поля в IO_LIST не записываются.</summary>
        public static void FillModbus(IoPoint p, ModbusMapEntry e)
        {
            p.Modbus = new Modbus
            {
                MapFile = e.MapFile,
                SlaveStation = e.SlaveStation,
                SlaveIdMain = e.SlaveIdMain,
                SlaveIdBackup = e.SlaveIdBackup,
                Speed = e.Speed,
                Timeout = e.Timeout,
                PrfDesignation = e.PrfDesignation,
                PlcTag = e.PlcTag,
                ObjectTag = e.ObjectTag,
                Description = e.Description,
                Units = e.Units,
                Scale = e.Scale,
                LL = e.LL,
                LA = e.LA,
                HA = e.HA,
                HH = e.HH,
                Scaling = e.Scaling,
                SigType = e.SigType,
                RegType = e.RegType,
                Address = e.Address,
                AccessType = e.AccessType,
                DataType = e.DataType,
                FuncCode = e.FuncCode,
                DcsChannel = e.DcsChannel,
                DcsTag = e.DcsTag,
                DcsFunctions = e.DcsFunctions,
                Note = e.Note
            };
        }
    }
}


