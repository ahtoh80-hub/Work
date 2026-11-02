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
    /// F55: связывание в три фазы — R: ручные правила F9.11; 1: полное совпадение имени
    /// (FullKey — сравнение «по буквам»: разделители «.»/«-» и класс P/PM не значимы), совпавшие
    /// строки закрепляются; 2: остальные точки по прежним трём уровням, но без закреплённых
    /// строк («совпавшие не трогать»), кандидаты ранжируются по длине общего префикса имени
    /// («максимальное количество букв»). Полные имена извлекаются из тега точки, скобок и
    /// Service с хвостом суффикса (ExtractCandidates → NormalizeFullName/HeadTagFull).
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

            // F55: строки, закреплённые от матчинга (фаза 1 F55) или уже заполненные
            // вручную (F59 FillEmpty), — остальные фазы/точки их не трогают
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

            // Индекс карты: CanonTag(ObjectTag) (upper + ведущие нули номера → 1120-PM-3A) → строки
            var byObject = map
                .Where(m => !string.IsNullOrWhiteSpace(m.ObjectTag))
                .GroupBy(m => CanonTag(m.ObjectTag!))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            // F66: базовый ключ семейства по каждому ключу индекса (ObjectBase — один раз;
            // ключи фильтрованного FilterLockedMap — подмножество исходных)
            var baseByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var k in byObject.Keys) baseByKey[k] = ObjectBase(k);

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
            // F66: каждая строка несёт своё состояние — «привязано» (фазы R/1) либо
            // «привязано не по полным совпадением!» (фаза 2).
            var pending = new List<(long Id, string? LinkTag, string? DcsTag, string LinkState)>();

            // F66: один сигнал — не более одного объекта: тег точки, уже привязанный к строке
            // карты, не может привязаться и к другой строке (дубль тега в IO_LIST и т.п.) —
            // второй адрес на один сигнал запрещён
            var boundTags = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            // F55: точки с кандидатами готовятся один раз (Examined не зависит от проходов);
            // далее три фазы на весь прогон: R — ручные правила F9.11 (прежняя логика трёх
            // уровней), 1 — полное совпадение имени (FullKey: разделители «.»/«-» и класс
            // P/PM не значимы), 2 — остальные точки: прежние три уровня без строк,
            // закреплённых фазой 1 («совпавшие не трогать»), ранжирование «по буквам».
            var work = new List<(IoPoint P, List<Candidate> Cands)>();
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
                var wc = ExtractCandidates(wp);
                if (wc.Count == 0) continue; // не MCC-сигнал — не применимо
                work.Add((wp, wc));
            }
            result.Examined = work.Count;

            // F55: индекс полного имени: FullKey(ObjectTag) → строки карты
            var byFullKey = map
                .Where(m => !string.IsNullOrWhiteSpace(m.ObjectTag))
                .GroupBy(m => FullKey(m.ObjectTag!))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            // F59: FillEmpty — заполненные строки убираются из индексов поиска целиком,
            // чтобы все три фазы (R/1/2) работали только с пустыми строками карты
            var byObjectAll = byObject; // F66: все строки карты — «представлено семейство?» без учёта locked
            if (fillEmpty && locked.Count > 0)
            {
                byObject = FilterLockedMap(byObject, locked);
                byFullKey = FilterLockedMap(byFullKey, locked);
            }

            var done = new bool[work.Count];

            // F66: семейство объекта — фильтр строк фазы 2. Loop-контура точки — авторитет
            // данных (буква/номер Loop различают объекты: 2701-PK-1201A ↔ 2701-PK-1201B);
            // при непригодном Loop — семейство лучшего кандидата точки. Строки чужого
            // семейства в поиске не участвуют: к сигналам объекта 2701-PK-1201A не могут
            // привязаться 2701-PK-1201B-M / 2701-PM-1201B. Отфильтрованные индексы
            // кэшируются по ключу семейства на весь прогон.
            var famCache = new Dictionary<string, Dictionary<string, List<ModbusMapEntry>>>(
                StringComparer.Ordinal);

            string FamilyKey(IoPoint p, List<Candidate> cands)
            {
                string loop = (p.Identification.Loop ?? "").Trim();
                if (Regex.IsMatch(loop, @"^[0-9]{3,4}-[A-Z]{1,3}-[0-9]+[A-Z]?$"))
                    return "L:" + CanonTag(loop);            // Loop парсится — якорь семейства
                return "C:" + ObjectBase(cands[0].Tag);      // fallback — семейство кандидата
            }

            Dictionary<string, List<ModbusMapEntry>> FilterFamily(
                Dictionary<string, List<ModbusMapEntry>> idx, string famKey)
            {
                var res = new Dictionary<string, List<ModbusMapEntry>>(StringComparer.Ordinal);
                foreach (var kv in idx)
                {
                    string rowBase = baseByKey.TryGetValue(kv.Key, out var b) ? b : kv.Key;
                    bool ok = famKey.StartsWith("L:", StringComparison.Ordinal)
                        ? LoopFamilyMatches(famKey.Substring(2), rowBase)
                        : ClassVariants(famKey.Substring(2)).Contains(rowBase);
                    if (ok) res[kv.Key] = kv.Value;
                }
                return res;
            }

            // F66: кэшированное семейство точки. Fallback (Loop → семейство кандидата) —
            // только когда Loop-семейство не представлено в карте ВООБЩЕ (Loop противоречит
            // Service/карте — напр. «9999-M-9999» при объекте 9999-PM-999A); «все строки
            // семейства закреплены» (FillEmpty) fallback не оправдывает.
            var loopPresent = new Dictionary<string, bool>(StringComparer.Ordinal);
            Dictionary<string, List<ModbusMapEntry>> GetFamily(IoPoint p, List<Candidate> cands)
            {
                string famKey = FamilyKey(p, cands);
                if (famKey.StartsWith("L:", StringComparison.Ordinal))
                {
                    if (!loopPresent.TryGetValue(famKey, out bool present))
                    {
                        present = FilterFamily(byObjectAll, famKey).Count > 0;
                        loopPresent[famKey] = present;
                    }
                    if (!present) famKey = "C:" + ObjectBase(cands[0].Tag);
                }
                if (!famCache.TryGetValue(famKey, out var idx))
                {
                    idx = FilterFamily(byObject, famKey);
                    famCache[famKey] = idx;
                }
                return idx;
            }

            // F66: строка (найденная фазой 1 по полному совпадению имени) принадлежит
            // семейству точки — полное имя чужого объекта (Loop другого контура) не привязывается
            bool FamilyAllows(IoPoint p, List<Candidate> cands, ModbusMapEntry row) =>
                string.IsNullOrWhiteSpace(row.ObjectTag) ||
                GetFamily(p, cands).ContainsKey(CanonTag(row.ObjectTag!));

            for (int phase = 0; phase < 3; phase++)
            for (int wi = 0; wi < work.Count; wi++)
            {
                if (done[wi]) continue;
                var (p, candidates) = work[wi];
                if (wi % 25 == 0)
                    progress?.Report((phase * work.Count + wi, work.Count * 3, p.Tag));

                ModbusMapEntry? entry;
                ModbusLinkRule? matchedRule = null;
                string? role = null;
                if (phase == 0)
                {
                    // Фаза R: ручные правила связывания (MODBUS_LINK_RULES) — приоритетнее
                    // встроенных ролей DetectRole (F9.11; прежняя логика трёх уровней).
                    // F66: ручные правила авторитетны — семейство объекта их не ограничивает
                    entry = FindMapRowByRules(byObject, candidates, rules, p, out matchedRule);
                }
                else
                {
                    role = DetectRole(p);
                    if (role == null) entry = null;
                    else if (phase == 1)
                    {
                        entry = FindFullMatchRow(byFullKey, candidates, role); // F55
                        // F66: полное совпадение имени чужого семейства точки не
                        // привязывается (Loop другого контура приоритетнее совпадения
                        // по одному упоминанию в Service)
                        if (entry != null && !FamilyAllows(p, candidates, entry)) entry = null;
                    }
                    else
                    {
                        // F66: только строки семейства объекта точки (Loop-якорь либо
                        // семейство лучшего кандидата, fallback — внутри GetFamily)
                        entry = FindMapRow(GetFamily(p, candidates), candidates, role, locked);
                    }
                }
                if (entry == null)
                {
                    if (phase == 2) // F55: «Не связано» — только после прохода по трём уровням
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
                    }
                    continue;
                }

                done[wi] = true; // точка обработана (успех или ошибка) — дальше фазы пропускаются

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
                if (phase == 1)
                {
                    // F55: полное совпадение имени — строка закрепляется, фаза 2 её не трогает
                    result.LinkedFullMatch++;
                    locked.Add(entry.Id);
                }
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
                    // F66: фаза 2 привязывает с отклонением от полного совпадения имени
                    // (усечённое имя/базовое устройство) — в LinkState пишется маркер;
                    // полные совпадения (фазы R/1) — «привязано» и не деградируют
                    entry.LinkState = phase == 2 &&
                                      entry.LinkState != ModbusMapService.LinkStateLinked
                        ? ModbusMapService.LinkStateLinkedApprox
                        : ModbusMapService.LinkStateLinked;
                    boundTags[p.Tag!] = entry.Id;
                    pending.Add((entry.Id, entry.LinkTag, entry.DcsTag, entry.LinkState));
                    if (phase == 2 &&
                        entry.LinkState == ModbusMapService.LinkStateLinkedApprox)
                        result.LinkedApprox++;
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

            progress?.Report((work.Count * 3, work.Count * 3, null));
            _log.Info("Связывание Modbus завершено. " + result.Summary);
            // F55: строки полных совпадений закреплены — на втором проходе им не дописываются теги
            if (result.LinkedFullMatch > 0)
                _log.Info($"F55: полных совпадений имени — {result.LinkedFullMatch}; " +
                          "закреплённые строки на втором проходе не изменяются");
            // F66: семейство объекта + привязки с отклонением от полного совпадения имени
            if (result.LinkedApprox > 0)
                _log.Info($"F66: привязано с отклонением от полного совпадения — " +
                          $"{result.LinkedApprox} тег(ов) (LinkState «" +
                          $"{ModbusMapService.LinkStateLinkedApprox}»); семейство объекта " +
                          "ограничено Loop-контура (иначе — лучшим кандидатом точки)");
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
        /// приоритет 2 — ведущее обозначение Service ВМЕСТЕ с хвостом суффикса — полные имена
        /// участвуют в проходе полного совпадения (FindFullMatchRow).</summary>
        public static List<Candidate> ExtractCandidates(IoPoint p)
        {
            var list = new List<Candidate>();
            string service = ((p.Signal?.Service ?? "").Trim() + " " +
                              (p.Signal?.ServiceEnglish ?? "").Trim()).Trim();

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

        // ================== F55: полное совпадение имени ==================

        /// <summary>F55: полное имя объекта — NNNN-КЛАСС-НОМЕР + хвост-суффикс
        /// («.HE-JB», «-HE-JB», «.EH02», «-1»); класс нормализуется P→PM/K→KM, номер — без
        /// ведущих нулей (как NormalizePlcTag/CanonTag). Возвращает null, если тег — не полное
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

        /// <summary>F55: сравнительный ключ полного имени — ТОЛЬКО буквы/цифры верхнего регистра:
        /// разделители «.»/«-»/«_» не значимы (1612-P-611A.HE-JB ≡ 1612-PM-611A-HE-JB
        /// ≡ 1612PM611AHEJB после нормализации класса P→PM и ведущих нулей номера).</summary>
        public static string FullKey(string tag)
        {
            string t = CanonTag(tag);
            var m = Regex.Match(t, @"^([0-9]{3,4}-)([A-Z]{1,3}-)(.*)$");
            if (m.Success)
            {
                string cls = m.Groups[2].Value.TrimEnd('-');
                if (cls == "P") cls = "PM";
                else if (cls == "K") cls = "KM";
                t = m.Groups[1].Value + cls + "-" + m.Groups[3].Value;
            }
            return Regex.Replace(t, "[^A-Z0-9]", "");
        }

        /// <summary>F55: ключи полного имени кандидата по вариантам записи класса (F9.6) —
        /// для поиска строки карты с равным полным именем.</summary>
        public static List<string> FullKeyVariants(string tag) =>
            ClassVariants(CanonTag(tag)).Select(FullKey).Distinct(StringComparer.Ordinal).ToList();

        // ================== F66: семейство объекта ==================

        /// <summary>F66: базовый ключ семейства объекта — NNNN-КЛАСС-НОМЕР[ЛИТЕРА]
        /// («2701-PK-1201B-M» → «2701-PK-1201B», «1612-PM-611A.HE-JB» → «1612-PM-611A»,
        /// «1110-KM-101A-1» → «1110-KM-101A», «9999-P-999A.HE-JB» → «9999-PM-999A»):
        /// хвосты-суффиксы («.HE01», «-HE-JB», машинный «-N», «-M» после литеры) отбрасываются,
        /// класс нормализуется P→PM/K→KM, номер — без ведущих нулей. Строки с равным
        /// ObjectBase — один физический объект (под-строки «.HE01»/«.HE-JB1»); строки разных
        /// семейств (2701-PK-1201A ↔ 2701-PK-1201B ↔ 2701-PM-1201B) — разные объекты.</summary>
        public static string ObjectBase(string tag)
        {
            string t = CanonTag(tag ?? "");
            var m = Regex.Match(t, @"^([0-9]{3,4}-)([A-Z]{1,3}-)([0-9]+)([A-Z]?)");
            if (!m.Success) return t;
            string cls = m.Groups[2].Value.TrimEnd('-');
            if (cls == "P") cls = "PM";
            else if (cls == "K") cls = "KM";
            return m.Groups[1].Value + cls + "-" + m.Groups[3].Value + m.Groups[4].Value;
        }

        /// <summary>F66: строка (в базовом ключе семейства) принадлежит семейству Loop-контура
        /// точки — Loop несёт идентичность объекта («у loop контура объекта буквенный суффикс
        /// различает объекты» — заказчик): титул и номер обязаны совпасть; буквенный суффикс
        /// Loop обязан совпасть с литерой строки, Loop без литеры — литера строки не значима
        /// (VFD-контуры пишутся без неё: «9964-M-9904»); класс сравнивается по эквивалентам
        /// F9.6, только если Loop-класс многобуквенный (однобуквенный M/P/K в Loop класс
        /// не раскрывает — как в Loop-кандидатах ExtractCandidates). Возвращает false для
        /// нестандартного Loop/строки — вызывающая сторона использует семейство кандидата.</summary>
        public static bool LoopFamilyMatches(string loop, string rowBase)
        {
            var l = Regex.Match(CanonTag(loop ?? ""), @"^([0-9]{3,4}-)([A-Z]{1,3}-)([0-9]+)([A-Z]?)$");
            if (!l.Success) return false;
            var r = Regex.Match(CanonTag(rowBase ?? ""), @"^([0-9]{3,4}-)([A-Z]{1,3}-)([0-9]+)([A-Z]?)$");
            if (!r.Success) return false;
            if (!string.Equals(l.Groups[1].Value, r.Groups[1].Value, StringComparison.Ordinal))
                return false; // титул
            if (!string.Equals(l.Groups[3].Value, r.Groups[3].Value, StringComparison.Ordinal))
                return false; // номер (CanonTag уже убрал ведущие нули)
            string loopLetter = l.Groups[4].Value, rowLetter = r.Groups[4].Value;
            if (loopLetter.Length > 0 && loopLetter != rowLetter)
                return false; // литер различает объекты (A ↔ B)
            string loopCls = l.Groups[2].Value.TrimEnd('-');
            if (loopCls.Length > 1)
            {
                string rowProbe = r.Groups[1].Value + r.Groups[2].Value.TrimEnd('-') +
                                  "-" + r.Groups[3].Value;
                string loopProbe = l.Groups[1].Value + loopCls + "-" + l.Groups[3].Value;
                if (!ClassVariants(rowProbe).Contains(loopProbe))
                    return false; // класс строке чужого объекта (PK ≠ PM, AGM ↔ AG и т.д.)
            }
            return true;
        }

        /// <summary>F55: длина общего префикса двух ключей — «максимальное количество букв».</summary>
        private static int CommonPrefixLen(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length), i = 0;
            while (i < n && a[i] == b[i]) i++;
            return i;
        }

        /// <summary>F55: строка карты по ПОЛНОМУ совпадению имени — ключ FullKey кандидата (в
        /// вариантах класса F9.6) равен FullKey(ObjectTag) строки. Роль/суффикс отбираются как
        /// на уровне 1 FindMapRow (стандартный суффикс → по описанию строки карты).</summary>
        private static ModbusMapEntry? FindFullMatchRow(
            Dictionary<string, List<ModbusMapEntry>> byFullKey,
            List<Candidate> candidates, string suffix)
        {
            foreach (var cand in candidates)
            {
                var spellings = ClassVariants(CanonTag(cand.Tag)); // написания — для want/рангира
                foreach (var fkey in FullKeyVariants(cand.Tag))
                {
                    if (!byFullKey.TryGetValue(fkey, out var rows)) continue;
                    foreach (var spelling in spellings)
                    {
                        var hit = PickByRole(rows, spelling, suffix, byDescription: false)
                                  ?? PickByRole(rows, spelling, suffix, byDescription: true);
                        if (hit != null) return hit;
                    }
                }
            }
            return null;
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
        /// приоритетнее строк другого объекта, совпавшего только по базовому устройству.
        /// F55: locked — строки, закреплённые проходом полного совпадения, исключаются
        /// («совпавшие не трогать»); на уровнях 2–3 строки ранжируются по длине общего префикса
        /// полного имени («максимальное количество букв»), прежний ранжир PickByRole — тай-брейк.</summary>
        private static ModbusMapEntry? FindMapRow(
            Dictionary<string, List<ModbusMapEntry>> byObject,
            List<Candidate> candidates, string suffix, HashSet<long>? locked = null)
        {
            foreach (var cand in candidates)
            {
                // F9.6/F9.7: варианты класса на канонизированном теге (ведущие нули: 003A ↔ 3A):
                // точное написание → таблица {PM↔M, EM↔E, AGM↔AG} → удаление букв; точные
                // совпадения всех вариантов приоритетнее префиксных (уровень 2)
                var variants = ClassVariants(CanonTag(cand.Tag));
                string candFull = FullKey(cand.Tag); // F55: ключ для ранжирования «по буквам»

                // 1) точное совпадение ObjectTag
                foreach (var key in variants)
                {
                    if (byObject.TryGetValue(key, out var exact))
                    {
                        var rows = FilterLocked(exact, locked);
                        if (rows.Count == 0) continue;
                        var hit = PickByRole(rows, key, suffix, byDescription: false, candFull)
                                  ?? PickByRole(rows, key, suffix, byDescription: true, candFull);
                        if (hit != null) return hit;
                    }
                }

                // 2) префикс: ObjectTag = кандидат + «.XX»/«-…» (напр. 1110-KM-101A.EH02);
                //    F55: кандидаты-ключи варианта — по убыванию длины общего префикса полного имени
                foreach (var key in variants)
                {
                    var prefixKvs = byObject
                        .Where(kv => kv.Key.Length > key.Length &&
                                     kv.Key.StartsWith(key, StringComparison.Ordinal) &&
                                     !char.IsLetterOrDigit(kv.Key[key.Length]))
                        .OrderByDescending(kv => CommonPrefixLen(candFull, FullKey(kv.Key)))
                        .ToList();
                    foreach (var kv in prefixKvs)
                    {
                        var rows = FilterLocked(kv.Value, locked);
                        if (rows.Count == 0) continue;
                        var hit = PickByRole(rows, key, suffix, byDescription: false, candFull)
                                  ?? PickByRole(rows, key, suffix, byDescription: true, candFull);
                        if (hit != null) return hit;
                    }
                }

                // 3) базовое устройство: срез «-N» / «.XX» у ObjectTag и кандидата.
                //    Строки всех объектов-совпадений собираются в один список; F55: ранжирование
                //    «по буквам» выполняется внутри PickByRole (lettersKey), затем прежний ранжир
                //    по машинному номеру/базовому объекту
                string baseCand = BaseDevice(CanonTag(cand.Tag));
                var baseVariants = ClassVariants(baseCand).ToHashSet(StringComparer.Ordinal);
                var baseRows = new List<ModbusMapEntry>();
                foreach (var kv in byObject)
                    if (baseVariants.Contains(BaseDevice(kv.Key)))
                        baseRows.AddRange(kv.Value);
                baseRows = FilterLocked(baseRows, locked);
                if (baseRows.Count > 0)
                {
                    var hit = PickByRole(baseRows, CanonTag(cand.Tag), suffix, byDescription: false, candFull)
                              ?? PickByRole(baseRows, CanonTag(cand.Tag), suffix, byDescription: true, candFull);
                    if (hit != null) return hit;
                }
            }
            return null;
        }

        /// <summary>F55: отфильтровать закреплённые строки (locked — Id строк полного совпадения;
        /// null/пусто — без ограничений).</summary>
        private static List<ModbusMapEntry> FilterLocked(List<ModbusMapEntry> rows, HashSet<long>? locked) =>
            locked == null || locked.Count == 0 ? rows : rows.Where(r => !locked.Contains(r.Id)).ToList();

        /// <summary>F59: убрать из индекса «ключ → строки карты» закреплённые строки
        /// (заполненные в режиме FillEmpty); ключи без оставшихся строк не переносятся.</summary>
        private static Dictionary<string, List<ModbusMapEntry>> FilterLockedMap(
            Dictionary<string, List<ModbusMapEntry>> index, HashSet<long> locked)
        {
            var res = new Dictionary<string, List<ModbusMapEntry>>(StringComparer.Ordinal);
            foreach (var kv in index)
            {
                var rows = kv.Value.Where(r => !locked.Contains(r.Id)).ToList();
                if (rows.Count > 0) res[kv.Key] = rows;
            }
            return res;
        }

        /// <summary>F59: разбор списка тегов из LinkTag/DcsTag («A, B») — отдельные теги.</summary>
        private static IEnumerable<string> SplitTags(string? s) =>
            (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
        /// в Description строки карты (только FuncCode роли).
        /// lettersKey (F55): полное имя кандидата — строки ранжируются ПО УБЫВАНИЮ длины общего
        /// префикса («максимальное количество букв»), равные буквы → прежний ранжир (хвост «-N»/база);
        /// null — прежнее поведение (правила F9.11 и проход полного совпадения).</summary>
        private static ModbusMapEntry? PickByRole(List<ModbusMapEntry> rows, string objectTag, string suffix,
            bool byDescription, string? lettersKey = null)
        {
            string baseObj = BaseDevice(objectTag);
            string tail = objectTag.Length > baseObj.Length ? objectTag[baseObj.Length..] : "";
            bool tailIsMachine = tail.Length > 0 && char.IsDigit(tail[^1]); // «-1»/«-2» — машинный номер

            // Предпочтение: тот же хвост «-N» → базовый объект (без «-N», в т.ч. «.XX») → прочие
            // F9.7: ObjectTag сравнивается в канонизированном виде (ведущие нули номера)
            string objCanon = CanonTag(objectTag);
            int Rank(ModbusMapEntry r)
            {
                string? ot = r.ObjectTag == null ? null : CanonTag(r.ObjectTag);
                if (ot == null) return 0;
                if (tailIsMachine && ot.StartsWith(objCanon, StringComparison.OrdinalIgnoreCase))
                    return 2; // тот же машинный номер
                if (!Regex.IsMatch(ot, @"-[0-9]$"))
                    return 1; // базовый объект (в т.ч. «.XX»)
                return 0;     // другой машинный номер
            }

            IEnumerable<ModbusMapEntry> q = lettersKey == null
                ? rows.OrderByDescending(Rank)
                : rows.OrderByDescending(r => CommonPrefixLen(lettersKey, FullKey(r.ObjectTag ?? "")))
                      .ThenByDescending(Rank);

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


