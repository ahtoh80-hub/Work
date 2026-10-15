using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// F9: связывание записей IO_LIST со строками карты адресации Modbus (MODBUS_MAP).
    /// Алгоритм (по результатам анализа файлов EBSM102/EBSM103):
    /// 1) из Service записи извлекается кандидат-объект ПЛК: текст в скобках «(1110-KM-101A-1)»
    ///    (прямая ссылка) либо ведущее обозначение «1115-P-101A» с нормализацией класса
    ///    привода P→PM, K→KM; fallback — Loop с подстановкой классов PM/KM по номеру;
    /// 2) по типу сигнала определяется строка карты: команда (DO-COM / «command:») → Function 06
    ///    (Control word, PlcTag = объект без суффикса); статусы → Function 02 с суффиксом
    ///    _FAULT.MCU / _STA.MCU / _REDAY.MCU / _TX.MCU / _PERMIT.MCU / _EM_STOP.MCU /
    ///    _SWITCH_ON.MCU / «_MCCB POSITION.MCU»; ток (AI / «current») → Function 03 «_I_A»;
    /// 3) совпадение: точное по ObjectTag → по префиксу → по базовому устройству (срез .XX / -N);
    /// 4) при совпадении заполняются LinkTag/DcsTag строки карты (F9.4: M_-поля в IO_LIST при
    ///    импорте НЕ пишутся — запись адресации выполняет кнопка «Обновить адресацию Modbus»,
    ///    UpdateAddressing); связь допустима только для Modbus-сигнала (IO Type «M…»/«COM»).
    /// Несопоставимые записи попадают в UnlinkedSamples (превью окна импорта карты).
    /// F9.6: класс объекта сопоставляется гибко — точное написание → таблица эквивалентов
    /// {PM↔M, EM↔E, AGM↔AG} → итеративное удаление букв (титул/числовой номер строго).
    /// F9.7: теги сравниваются в канонизированном виде (ведущие нули номера: 003A ↔ 3A).
    /// F9.8: сообщения связывания/адресации («Не связано», ошибки) пишутся в аудит
    /// IO_LIST_Audit (ModbusWarning — жёлтый, ModbusError — красный).
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
        /// LinkTag/LinkState/DcsTag; M_-поля в IO_LIST не записываются, см. UpdateAddressing).</summary>
        public ModbusLinkResult Link(IProgress<(int Done, int Total, string? Tag)>? progress = null)
        {
            var result = new ModbusLinkResult();
            var points = _repo.GetAll();
            var map = _maps.LoadFromDb();
            if (map.Count == 0)
            {
                _log.Warn("MODBUS_MAP пуст — сначала импортируйте карту адресации (F9)");
                return result;
            }

            // F9.3: сброс устаревшей привязки перед новым прогоном — LinkState/LinkTag
            // обнуляются и в БД, и в памяти; каждый прогон расставляет состояния заново.
            _maps.ResetLinkResults();
            foreach (var m in map)
            {
                m.LinkTag = null;
                m.LinkState = null;
            }

            // Индекс карты: CanonTag(ObjectTag) (upper + ведущие нули номера → 1120-PM-3A) → строки
            var byObject = map
                .Where(m => !string.IsNullOrWhiteSpace(m.ObjectTag))
                .GroupBy(m => CanonTag(m.ObjectTag!))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            // F9.11: ручные правила связывания (MODBUS_LINK_RULES) — применяются ПЕРЕД
            // встроенными ролями DetectRole; невалидный шаблон правила пропускается с предупреждением
            var rules = new List<ModbusLinkRule>();
            if (_rules != null)
            {
                rules = _rules.LoadEnabled();
                foreach (var r in rules)
                {
                    string? err = ModbusLinkRuleService.ValidatePattern(r.TagPattern)
                                  ?? ModbusLinkRuleService.ValidatePattern(r.ValuePattern);
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
            var pending = new List<(long Id, string? LinkTag, string? DcsTag)>();
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (i % 25 == 0)
                    progress?.Report((i, points.Count, p.Tag));

                if (string.IsNullOrWhiteSpace(p.Tag)) continue;
                var candidates = ExtractCandidates(p);
                if (candidates.Count == 0) continue; // не MCC-сигнал — не применимо
                result.Examined++;

                // F9.11: сначала ручные правила связывания (MODBUS_LINK_RULES) — они приоритетнее
                // встроенных ролей; DetectRole вызывается только если правило не сработало
                var entry = FindMapRowByRules(byObject, candidates, rules, p, out var matchedRule);
                string? role = null;
                if (entry == null)
                {
                    role = DetectRole(p);
                    entry = role == null ? null : FindMapRow(byObject, candidates, role);
                }
                if (entry == null)
                {
                    result.Unlinked++;
                    if (result.UnlinkedSamples.Count < 20)
                    {
                        string msg = $"{p.Tag} (кандидат: {candidates[0].Tag}; роль: {role ?? "?"})";
                        result.UnlinkedSamples.Add(msg);
                        // F9.8: то же сообщение, что в журнале («Не связано: …»), — в аудит
                        auditMsgs.Add((p.Tag, AuditAction.ModbusWarning,
                            p.Revision?.FileName, p.Revision?.Sheet, $"Не связано: {msg}"));
                    }
                    continue;
                }

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

                // F9.3: несколько команд, делящих один Control word, — все теги через запятую;
                // F9.4: DcsTag — тот же список для кнопки обновления адресации: ручной ввод
                // в колонке не затирается, теги дописываются без дублей
                string nextLink = AppendLinkTag(entry.LinkTag, p.Tag);
                string nextDcs = AppendLinkTag(entry.DcsTag, p.Tag);
                if (nextLink != entry.LinkTag || nextDcs != entry.DcsTag)
                {
                    entry.LinkTag = nextLink;
                    entry.DcsTag = nextDcs;
                    entry.LinkState = ModbusMapService.LinkStateLinked;
                    pending.Add((entry.Id, entry.LinkTag, entry.DcsTag));
                }
            }

            // F9.3: привязки + разметка строк без результата («не привязано») — одна транзакция,
            // уже без открытой сессии репозитория
            _maps.ApplyLinkResults(pending);
            result.MapRowsLinked = map.Count(m =>
                string.Equals(m.LinkState, ModbusMapService.LinkStateLinked, StringComparison.Ordinal));
            result.MapRowsUnlinked = map.Count - result.MapRowsLinked;
            foreach (var t in _maps.GetUnlinkedPlcTags(10))
                result.MapUnlinkedSamples.Add(t);

            progress?.Report((points.Count, points.Count, null));
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

        /// <summary>Кандидат объекта ПЛК: тег + приоритет (1 — ссылка в скобках, 2 — ведущее
        /// обозначение Service, 3 — Loop).</summary>
        public readonly record struct Candidate(string Tag, int Priority);

        private static readonly Regex ParenTag = new(@"\(([0-9]{3,4}-[A-Z0-9\-\.]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // F9.3/A: границы — только ASCII-буквы/цифры: в .NET \b считает кириллицу «словом»,
        // и тег, приклеенный к кириллице («1115-P-101AСостояние»), не распознавался
        private static readonly Regex HeadTag = new(@"(?<![A-Za-z0-9])([0-9]{3,4})-([A-Z]{1,3})-([0-9]+[A-Z]?)(?![A-Za-z0-9])",
            RegexOptions.Compiled);
        private static readonly Regex LoopTag = new(@"^([0-9]{3,4})-([A-Z]{1,3})-([0-9]+[A-Z]?)$",
            RegexOptions.Compiled);

        /// <summary>Извлечь кандидатов объекта ПЛК из Service/Loop записи (по приоритету).
        /// F9.3/A: сканируются оба поля — Service и ServiceEnglish (дубли скобочных ссылок отбрасываются).</summary>
        public static List<Candidate> ExtractCandidates(IoPoint p)
        {
            var list = new List<Candidate>();
            string service = ((p.Signal?.Service ?? "").Trim() + " " +
                              (p.Signal?.ServiceEnglish ?? "").Trim()).Trim();

            // 1) ссылка в скобках: «(1110-KM-101A-1)» — самый точный признак
            foreach (Match m in ParenTag.Matches(service))
            {
                string? tag = NormalizePlcTag(m.Groups[1].Value);
                if (tag != null && !list.Any(c => c.Tag == tag)) list.Add(new Candidate(tag, 1));
            }

            // 2) ведущее обозначение: «1115-P-101A motor …» → 1115-PM-101A
            var head = HeadTag.Match(service);
            if (head.Success)
            {
                string? tag = NormalizePlcTag(
                    $"{head.Groups[1].Value}-{head.Groups[2].Value}-{head.Groups[3].Value}");
                if (tag != null && !list.Any(c => c.Tag == tag))
                    list.Add(new Candidate(tag, 2));
            }

            // 3) Loop: «1115-M-00101A» → 1115-PM-101A / 1115-KM-101A (класс не раскрывается —
            //    подставляем приводные классы по номеру, низкий приоритет)
            var loop = LoopTag.Match((p.Identification.Loop ?? "").Trim());
            if (loop.Success)
            {
                string num = loop.Groups[3].Value.TrimStart('0');
                if (num.Length == 0) num = "0";
                string area = loop.Groups[1].Value;
                foreach (string cls in new[] { "PM", "KM" })
                {
                    string tag = $"{area}-{cls}-{num}";
                    if (!list.Any(c => c.Tag == tag)) list.Add(new Candidate(tag, 3));
                }
            }

            return list.OrderBy(c => c.Priority).ToList();
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

        /// <summary>F9.7: канонический вид тега для сравнения — ведущие нули номера срезаются
        /// (1120-PM-003A ↔ 1120-PM-3A); титул/класс/хвост (суффикс «_…», «-N», «.XX») не меняются.
        /// Нормализуется только числовой номер после класса: «1120-M-00003A_TX.PLC» → «1120-M-3A_TX.PLC».</summary>
        public static string CanonTag(string tag)
        {
            string t = tag.Trim().ToUpperInvariant();
            var m = Regex.Match(t, @"^([0-9]{3,4}-[A-Z]{1,3}-)([0-9]+)(.*)$");
            if (!m.Success) return t;
            string num = m.Groups[2].Value.TrimStart('0');
            if (num.Length == 0) num = "0";
            return m.Groups[1].Value + num + m.Groups[3].Value;
        }

        /// <summary>F9.6: варианты записи класса объекта для сопоставления кандидата со строками карты:
        /// 1) точное написание (приоритет выше всех); 2) таблица эквивалентов {PM↔M, EM↔E, AGM↔AG} —
        /// карта пишет «1162-M-401», IO_LIST — «1162-PM-401»; 3) итеративное удаление букв класса
        /// (сначала «M» — наиболее вероятная разница карты, затем по одной букве; класс не может
        /// стать пустым — должна остаться минимум 1 буква). Титул и номер (включая хвосты
        /// «-N»/«_…»/«.XX») сохраняются строго.</summary>
        public static List<string> ClassVariants(string tag)
        {
            string t = tag.Trim().ToUpperInvariant();
            var parts = t.Split('-');
            var result = new List<string> { t };
            if (parts.Length < 3 || parts[0].Length == 0 || !Regex.IsMatch(parts[1], "^[A-Z]+$"))
                return result; // нестандартная форма — только точное совпадение
            string title = parts[0], cls = parts[1], rest = string.Join('-', parts.Skip(2));
            var seen = new HashSet<string>(result) { t };
            void Add(string c)
            {
                if (c.Length == 0) return;
                string v = $"{title}-{c}-{rest}";
                if (seen.Add(v)) result.Add(v);
            }

            // 2) таблица эквивалентов класса
            foreach (var v in ClassEquivalents(cls)) Add(v);

            // 3) итеративные удаления букв (BFS по уровням удалений; на уровне сначала удаляется
            //    «M», затем буквы слева направо — «больше удалённых M» приоритетнее «меньше удалений»)
            var level = new List<string> { cls };
            var visited = new HashSet<string>(level) { cls };
            while (level.Count > 0)
            {
                var next = new List<string>();
                foreach (var c in level)
                {
                    if (c.Length <= 1) continue; // минимум 1 буква должна остаться
                    foreach (var r in c.Select((ch, i) => (ch, v: c.Remove(i, 1)))
                                       .OrderByDescending(x => x.ch == 'M'))
                    {
                        if (visited.Add(r.v)) { next.Add(r.v); Add(r.v); }
                    }
                }
                level = next;
            }
            return result;
        }

        /// <summary>F9.6: эквиваленты класса привода (симметричная таблица PM↔M, EM↔E, AGM↔AG).</summary>
        private static IEnumerable<string> ClassEquivalents(string cls)
        {
            (string A, string B)[] pairs = { ("PM", "M"), ("EM", "E"), ("AGM", "AG"), ("BLM", "BL") };
            foreach (var (a, b) in pairs)
            {
                if (cls == a) yield return b;
                if (cls == b) yield return a;
            }
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

        // ================== Роль сигнала → строка карты ==================

        /// <summary>Роль сигнала: суффикс PLC Tag карты ПОСЛЕ «_» (пустая строка — Control word
        /// Function 06, PlcTag = объект без суффикса), либо null — роль не распознана.
        /// F9.10: распознаются также MSC/MSI (задание/считывание скорости VFD), run-сигналы
        /// («run signal» без «running») и роль по суффиксу тега DCS (MXA…A/B/C, MII, MHS, MSC, MSI),
        /// если Service не распознан ключевыми словами.</summary>
        public static string? DetectRole(IoPoint p)
        {
            string io = (p.Identification.IoType ?? "").Trim().ToUpperInvariant();
            string svc = ((p.Signal?.Service ?? "") + " " + (p.Signal?.ServiceEnglish ?? "")).ToLowerInvariant();
            string tag = (p.Tag ?? "").Trim().ToUpperInvariant(); // F9.10: тег DCS — источник роли

            // Команды: DO-COM или «DCS command:…» → Function 06 (Control word, без суффикса)
            if (io == "DO-COM" || svc.Contains("command:")) return "";

            // F9.10: задание скорости VFD (MSC: AO-COM / «speed control») → Function 06
            // «Frequency Setting» — строка, на которую претендует только один сигнал
            if (io == "AO-COM" || svc.Contains("speed control") ||
                Regex.IsMatch(tag, @"^[0-9]{3,4}-MSC-"))
                return "FREQ_SET";

            // F9.10: считывание скорости VFD (MSI) — раньше ветки тока, иначе AI-COM уводит
            // MSI в «Output Current» (_I_A по описанию «current»)
            if (svc.Contains("speed signal") || Regex.IsMatch(tag, @"^[0-9]{3,4}-MSI-") ||
                (io == "AI-COM" && svc.Contains("speed")))
                return "SPEED";

            // Ток: AI или «status:current» → Function 03 «_I_A»
            if (io == "AI-COM" || svc.Contains("status:current") || svc.Contains(":current") ||
                Regex.IsMatch(svc, @"\bcurrent\b"))
                return "I_A";

            // Статусы DI: по ключевым словам Service (суффиксы карты — без ведущего «_»);
            // F9.10: «run signal»/«Run» — то же состояние, что и «running»
            if (svc.Contains("fault")) return "FAULT.MCU";
            if (svc.Contains("running") || Regex.IsMatch(svc, @"\brun\b")) return "STA.MCU";
            if (svc.Contains("ready")) return "REDAY.MCU"; // опечатка «REDAY» сохранена в карте
            if (svc.Contains("communication")) return "TX.MCU";
            if (svc.Contains("permitted") || svc.Contains("permissive")) return "PERMIT.MCU";
            if (svc.Contains("emergency")) return "EM_STOP.MCU";
            if (svc.Contains("switch")) return "SWITCH_ON.MCU";
            if (svc.Contains("mccb")) return "MCCB POSITION.MCU";

            // F9.10: роль по суффиксу тега DCS, если Service не распознан ключевыми словами:
            // MXA…A — авария, MXA…B — работа, MXA…C — готовность; MII — ток; MHS — команда;
            // MSC/MSI — задание/считывание скорости (страховка к верхним веткам)
            var mt = Regex.Match(tag, @"^[0-9]{3,4}-([A-Z]{1,3})-[0-9]+([A-Z]?)$");
            if (mt.Success)
            {
                switch (mt.Groups[1].Value)
                {
                    case "MII": return "I_A";
                    case "MHS": return "";
                    case "MSC": return "FREQ_SET";
                    case "MSI": return "SPEED";
                    case "MXA":
                        return mt.Groups[2].Value switch
                        {
                            "A" => "FAULT.MCU",
                            "B" => "STA.MCU",
                            "C" => "REDAY.MCU",
                            _ => null
                        };
                }
            }
            return null;
        }

        /// <summary>Найти строку карты: точное совпадение ObjectTag → префикс → базовое устройство.
        /// suffix: «» — Function 06 (Control word); иначе PlcTag = объект + «_» + suffix.
        /// F9.3/C: на КАЖДОМ уровне сначала стандартный суффикс (точное совпадение / EndsWith),
        /// затем — запасной уровень «по описанию строки карты» (_Q3_F.PLC / _Q3_READY.PLC /
        /// _Q3_BYPS.PLC / _M3_PERMIT.PLC и т.п.). Так строки точного объекта-кандидата
        /// приоритетнее строк другого объекта, совпавшего только по базовому устройству.</summary>
        private static ModbusMapEntry? FindMapRow(
            Dictionary<string, List<ModbusMapEntry>> byObject,
            List<Candidate> candidates, string suffix)
        {
            foreach (var cand in candidates)
            {
                // F9.6/F9.7: варианты класса на канонизированном теге (ведущие нули: 003A ↔ 3A):
                // точное написание → таблица {PM↔M, EM↔E, AGM↔AG} → удаление букв; точные
                // совпадения всех вариантов приоритетнее префиксных (уровень 2)
                var variants = ClassVariants(CanonTag(cand.Tag));

                // 1) точное совпадение ObjectTag
                foreach (var key in variants)
                {
                    if (byObject.TryGetValue(key, out var exact))
                    {
                        var hit = PickByRole(exact, key, suffix, byDescription: false)
                                  ?? PickByRole(exact, key, suffix, byDescription: true);
                        if (hit != null) return hit;
                    }
                }

                // 2) префикс: ObjectTag = кандидат + «.XX» (напр. 1110-KM-101A.EH02)
                foreach (var key in variants)
                {
                    foreach (var kv in byObject)
                    {
                        if (kv.Key.Length > key.Length && kv.Key.StartsWith(key, StringComparison.Ordinal) &&
                            !char.IsLetterOrDigit(kv.Key[key.Length]))
                        {
                            var hit = PickByRole(kv.Value, key, suffix, byDescription: false)
                                      ?? PickByRole(kv.Value, key, suffix, byDescription: true);
                            if (hit != null) return hit;
                        }
                    }
                }

                // 3) базовое устройство: срез «-N» / «.XX» у ObjectTag и кандидата.
                //    Строки всех объектов-совпадений собираются в один список — ранжирование
                //    по машинному номеру/базовому объекту выполняется внутри PickByRole.
                string baseCand = BaseDevice(CanonTag(cand.Tag));
                var baseVariants = ClassVariants(baseCand).ToHashSet(StringComparer.Ordinal);
                var baseRows = new List<ModbusMapEntry>();
                foreach (var kv in byObject)
                    if (baseVariants.Contains(BaseDevice(kv.Key)))
                        baseRows.AddRange(kv.Value);
                if (baseRows.Count > 0)
                {
                    var hit = PickByRole(baseRows, CanonTag(cand.Tag), suffix, byDescription: false)
                              ?? PickByRole(baseRows, CanonTag(cand.Tag), suffix, byDescription: true);
                    if (hit != null) return hit;
                }
            }
            return null;
        }
        /// <summary>F9.11: поиск строки карты ручными правилами MODBUS_LINK_RULES (ПЕРЕД встроенными
        /// ролями). Для каждого правила по порядку Priority: поле IO точки (IOField) матчится с
        /// TagPattern → собираются строки карты-объекта теми же тремя уровнями, что и FindMapRow
        /// (точный ObjectTag → префикс «.XX» → базовое устройство), но фильтр строки — поле
        /// MapField матчится с ValuePattern (+ FuncCode, если задан). Ранжирование строк — как в
        /// PickByRole (точный объект → машинный номер → базовый). Возвращает строку и правило,
        /// которое сработало (matchedRule) — для статистики/журнала.</summary>
        private static ModbusMapEntry? FindMapRowByRules(
            Dictionary<string, List<ModbusMapEntry>> byObject,
            List<Candidate> candidates, List<ModbusLinkRule> rules, IoPoint p,
            out ModbusLinkRule? matchedRule)
        {
            matchedRule = null;
            foreach (var rule in rules)
            {
                // Шаг 1: поле IO-стороны точки против TagPattern (например Tag «2711-MXA-27116A»
                // против regex «MXA+A» либо wildcard «*MXA*A»)
                string ioValue = ModbusLinkRuleService.GetIoValue(p, rule.IOField) ?? "";
                if (!ModbusLinkRuleService.PatternMatch(ioValue, rule.TagPattern)) continue;

                // Шаг 2: строки карты-кандидата, чьё поле MapField совпадает с ValuePattern
                bool RowMatch(ModbusMapEntry r)
                {
                    if (!string.IsNullOrWhiteSpace(rule.FuncCode) &&
                        !string.Equals(r.FuncCode?.Trim(), rule.FuncCode.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                        return false;
                    return ModbusLinkRuleService.PatternMatch(
                        ModbusLinkRuleService.GetMapValue(r, rule.MapField), rule.ValuePattern);
                }

                foreach (var cand in candidates)
                {
                    var variants = ClassVariants(CanonTag(cand.Tag));

                    // 1) точное совпадение ObjectTag
                    foreach (var key in variants)
                        if (byObject.TryGetValue(key, out var exact))
                        {
                            var hit = PickByRuleRows(exact, key, RowMatch);
                            if (hit != null) { matchedRule = rule; return hit; }
                        }

                    // 2) префикс: ObjectTag = кандидат + «.XX»
                    foreach (var key in variants)
                        foreach (var kv in byObject)
                            if (kv.Key.Length > key.Length && kv.Key.StartsWith(key, StringComparison.Ordinal) &&
                                !char.IsLetterOrDigit(kv.Key[key.Length]))
                            {
                                var hit = PickByRuleRows(kv.Value, key, RowMatch);
                                if (hit != null) { matchedRule = rule; return hit; }
                            }

                    // 3) базовое устройство (срез «-N»/«.XX»)
                    string baseCand = BaseDevice(CanonTag(cand.Tag));
                    var baseVariants = ClassVariants(baseCand).ToHashSet(StringComparer.Ordinal);
                    var baseRows = new List<ModbusMapEntry>();
                    foreach (var kv in byObject)
                        if (baseVariants.Contains(BaseDevice(kv.Key)))
                            baseRows.AddRange(kv.Value);
                    if (baseRows.Count > 0)
                    {
                        var hit = PickByRuleRows(baseRows, CanonTag(cand.Tag), RowMatch);
                        if (hit != null) { matchedRule = rule; return hit; }
                    }
                }
            }
            return null;
        }

        /// <summary>F9.11: выбор строки из отфильтрованных правилом (RowMatch) строк карты —
        /// тот же ранжиринг, что в PickByRole: точный объект → машинный номер «-N» → базовый объект.</summary>
        private static ModbusMapEntry? PickByRuleRows(List<ModbusMapEntry> rows, string objectTag,
            Func<ModbusMapEntry, bool> rowMatch)
        {
            string baseObj = BaseDevice(objectTag);
            string tail = objectTag.Length > baseObj.Length ? objectTag[baseObj.Length..] : "";
            bool tailIsMachine = tail.Length > 0 && char.IsDigit(tail[^1]);

            string objCanon = CanonTag(objectTag);
            return rows.OrderByDescending(r =>
            {
                string? ot = r.ObjectTag == null ? null : CanonTag(r.ObjectTag);
                if (ot == null) return 0;
                if (tailIsMachine && ot.StartsWith(objCanon, StringComparison.OrdinalIgnoreCase))
                    return 2;
                if (!Regex.IsMatch(ot, @"-[0-9]$"))
                    return 1;
                return 0;
            }).FirstOrDefault(rowMatch);
        }

        /// <summary>Выбор строки карты по роли: suffix «» → Function 06; иначе PlcTag = объект + «_» + suffix.
        /// Приоритет объектов: точное совпадение → тот же хвост «-N» → базовый объект («.XX», без «-N»).
        /// byDescription=true — запасной уровень F9.3/C: совпадение по ключевым словам роли
        /// в Description строки карты (только FuncCode роли).</summary>
        private static ModbusMapEntry? PickByRole(List<ModbusMapEntry> rows, string objectTag, string suffix,
            bool byDescription)
        {
            string baseObj = BaseDevice(objectTag);
            string tail = objectTag.Length > baseObj.Length ? objectTag[baseObj.Length..] : "";
            bool tailIsMachine = tail.Length > 0 && char.IsDigit(tail[^1]); // «-1»/«-2» — машинный номер

            // Предпочтение: тот же хвост «-N» → базовый объект (без «-N», в т.ч. «.XX») → прочие
            // F9.7: ObjectTag сравнивается в канонизированном виде (ведущие нули номера)
            string objCanon = CanonTag(objectTag);
            IEnumerable<ModbusMapEntry> q = rows.OrderByDescending(r =>
            {
                string? ot = r.ObjectTag == null ? null : CanonTag(r.ObjectTag);
                if (ot == null) return 0;
                if (tailIsMachine && ot.StartsWith(objCanon, StringComparison.OrdinalIgnoreCase))
                    return 2; // тот же машинный номер
                if (!Regex.IsMatch(ot, @"-[0-9]$"))
                    return 1; // базовый объект (в т.ч. «.XX»)
                return 0;     // другой машинный номер
            });

            if (suffix.Length == 0)
            {
                // Control word: Function 06, PlcTag = объект без суффикса;
                // если точного нет (машинный номер «-1» отсутствует в карте) — базовая строка
                // объекта с предпочтительным рангом (например, 1110-KM-101A.EH02)
                // F9.7: PlcTag сравнивается канонизированно (1120-PM-003A ↔ 1120-PM-3A)
                // F9.10: у VFD рядом с Control word лежит строка «Frequency Setting» (задание
                // скорости, только для MSC) — сначала ищется именно Control word
                // («Value:1=Stop,…»), и только затем действует прежняя логика
                bool Fc06(ModbusMapEntry r) =>
                    string.Equals(r.FuncCode?.Trim(), "06", StringComparison.OrdinalIgnoreCase);
                bool ControlWord(ModbusMapEntry r) =>
                    !string.IsNullOrEmpty(r.Description) &&
                    (r.Description!.Contains("control word", StringComparison.OrdinalIgnoreCase) ||
                     r.Description!.Contains("Value:1=Stop", StringComparison.OrdinalIgnoreCase));
                bool ExactPlc(ModbusMapEntry r) =>
                    string.Equals(CanonTag(r.PlcTag?.Trim() ?? ""), objCanon, StringComparison.OrdinalIgnoreCase);

                return q.FirstOrDefault(r => Fc06(r) && ExactPlc(r) && ControlWord(r))
                       ?? q.FirstOrDefault(r => Fc06(r) && ExactPlc(r))
                       ?? q.FirstOrDefault(r => Fc06(r) && ControlWord(r))
                       ?? q.FirstOrDefault(r => Fc06(r));
            }

            // Запасной уровень F9.3/C: описание строки карты (ключевые слова роли) —
            // для карт с «нестандартными» суффиксами (_Q3_F.PLC, _Q3_READY.PLC, _Q3_BYPS.PLC,
            // _M3_PERMIT.PLC, _FAULT.PLC, _REDAY.PLC …). Словарь IO и карты совпадает по-английски
            // (fault/Ready/Running/Permit/Communication/Emergency…); фильтр FuncCode обязателен —
            // отсекает Control word (06, «…1024=Permit…») и ток (03).
            if (byDescription)
            {
                if (!RoleDescriptionKeywords.TryGetValue(suffix, out var kw)) return null;
                return q.FirstOrDefault(r =>
                    string.Equals(r.FuncCode?.Trim(), kw.Func, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(r.Description) &&
                    kw.Words.Any(w => r.Description!.Contains(w, StringComparison.OrdinalIgnoreCase)));
            }

            string want = objCanon + "_" + suffix; // F9.7: objCanon — канонический объект
            return q.FirstOrDefault(r =>
                string.Equals(CanonTag(r.PlcTag?.Trim() ?? ""), want, StringComparison.OrdinalIgnoreCase))
                ?? q.FirstOrDefault(r =>
                r.PlcTag != null && r.PlcTag.Trim().EndsWith("_" + suffix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>F9.3/C: ключевые слова роли в Description строки карты (запасной уровень сопоставления).
        /// Ключ — суффикс роли из DetectRole; значение — FuncCode строки и слова описания.
        /// Неоднозначных совпадений на данных EBSM102/103 нет, кроме двух каналов связи
        /// 2701-PK-1201A/B-M (_TX.VFD + _TX.PLC) — берётся первый (порядок строк карты).</summary>
        private static readonly Dictionary<string, (string Func, string[] Words)> RoleDescriptionKeywords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["FAULT.MCU"] = ("02", new[] { "fault" }),
                ["STA.MCU"] = ("02", new[] { "running", "operation" }),
                ["REDAY.MCU"] = ("02", new[] { "ready" }),
                ["PERMIT.MCU"] = ("02", new[] { "permit" }),
                ["TX.MCU"] = ("02", new[] { "communication" }),
                ["EM_STOP.MCU"] = ("02", new[] { "emergency" }),
                ["SWITCH_ON.MCU"] = ("02", new[] { "switch on", "switching on" }),
                ["MCCB POSITION.MCU"] = ("02", new[] { "mccb" }),
                ["I_A"] = ("03", new[] { "current" }),
                // F9.10: VFD — задание скорости (Function 06 «Frequency Setting») и считывание
                // скорости (Function 03 «Operating Frequency»); фильтр FuncCode исключает
                // Control word (06) и Status Word (03, «…Forward Running…» — без «speed»)
                ["FREQ_SET"] = ("06", new[] { "frequency setting", "speed setpoint" }),
                ["SPEED"] = ("03", new[] { "operating frequency", "speed" }),
            };

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

        /// <summary>Базовое устройство: срез суффикса «.XX» и хвоста «-N» (1110-KM-101A.EH02 → 1110-KM-101A).
        /// F9.3/C: нормализуются и классы привода карты P→PM, K→KM — строка «1120-P-203A.HE-JB»
        /// (насос) сопоставляется с кандидатом «1120-PM-203A» (двигатель того же насоса).</summary>
        private static string BaseDevice(string tag)
        {
            string t = tag.Trim().ToUpperInvariant();
            int dot = t.IndexOf('.');
            if (dot > 0) t = t[..dot];
            // хвост «-N» (одна цифра) — после как минимум двух дефисов: 1110-KM-101A-1 → 1110-KM-101A
            var m = Regex.Match(t, @"^(.*-[A-Z]+-[0-9]+[A-Z]?)-[0-9]$");
            if (m.Success) t = m.Groups[1].Value;
            var cls = Regex.Match(t, @"^([0-9]{3,4})-([A-Z])-");
            if (cls.Success)
            {
                string c = cls.Groups[2].Value;
                if (c is "P" or "K") // однобуквенный класс привода → нормализованный (как NormalizePlcTag)
                    t = $"{cls.Groups[1].Value}-{c}M-{t[cls.Length..]}";
            }
            return CanonTag(t); // F9.7: ведущие нули номера — и здесь (срез 003A → 3A)
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


