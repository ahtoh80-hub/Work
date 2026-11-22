using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using IO_PJT.Data;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.ViewModels
{
    /// <summary>F99: окно «Обновление адресации Modbus согласно MODBUS_MAP» — единая точка
    /// трёх действий (связи полностью / только пустые связи / M_-адресация), редактора
    /// правил связывания MODBUS_LINK_RULES и проверки маски на конкретном теге.
    /// Журнал результатов действий — в окне (LogText). Заменяет четыре пункта меню и две
    /// кнопки главного окна (F99: устранение дублирования).</summary>
    public sealed class ModbusAddressingViewModel : ViewModelBase
    {
        private readonly AppServices _s;

        /// <summary>Обновление грида главного окна после действий (назначается code-behind).</summary>
        public Action? RefreshMain { get; set; }

        public ModbusAddressingViewModel(AppServices services)
        {
            _s = services;
            AddCommand = new RelayCommand(AddRule);
            DeleteCommand = new RelayCommand(DeleteRule, () => SelectedRule != null);
            SaveRulesCommand = new RelayCommand(SaveRules);
            ValidateRulesCommand = new RelayCommand(ValidateRules);
            LinkFullCommand = new RelayCommand(() => RunLink(ModbusLinkMode.Full));
            LinkFillEmptyCommand = new RelayCommand(() => RunLink(ModbusLinkMode.FillEmpty));
            UpdateAddressingCommand = new RelayCommand(RunAddressing);
            CheckMaskCommand = new RelayCommand(CheckMask, () => SelectedRule != null);
            LoadRules();
        }

        // ================== Правила связывания (MODBUS_LINK_RULES) ==================

        /// <summary>Правила из MODBUS_LINK_RULES (редактируются в гриде; сохранение — SaveRulesCommand).</summary>
        public ObservableCollection<ModbusLinkRule> Rules { get; } = new();

        private ModbusLinkRule? _selectedRule;
        public ModbusLinkRule? SelectedRule
        { get => _selectedRule; set { if (Set(ref _selectedRule, value, nameof(SelectedRule))) RaiseCommands(); } }

        private string _statusText = "Правила связывания: маска {Колонка}&… — чем меньше отклонение wildcard, тем точнее строка карты";
        public string StatusText { get => _statusText; set => Set(ref _statusText, value, nameof(StatusText)); }

        private string _logText = "";
        /// <summary>Журнал результатов действий окна (F99 — в окне, не в MessageBox).</summary>
        public string LogText { get => _logText; set => Set(ref _logText, value, nameof(LogText)); }

        private string _checkTag = "";
        /// <summary>Тег для проверки маски выбранного правила (F99).</summary>
        public string CheckTag { get => _checkTag; set => Set(ref _checkTag, value, nameof(CheckTag)); }

        private string _checkResult = "";
        /// <summary>Результат проверки маски (паттерн + лучшая строка карты либо «нет совпадения»).</summary>
        public string CheckResult { get => _checkResult; set => Set(ref _checkResult, value, nameof(CheckResult)); }

        /// <summary>Колонки IO_LIST (FieldRegistry) — источник для ComboBox поля IO.</summary>
        public string[] IoColumns => FieldRegistry.Columns;

        /// <summary>Строковые поля карты — источник для ComboBox поля карты.</summary>
        public string[] MapColumns => ModbusLinkRuleService.MapFields;

        public string FieldsHint =>
            $"IO: колонка IO_LIST ({FieldRegistry.Columns.Length} шт., напр. Tag, Service, Loop); " +
            "карта: любое строковое поле MODBUS_MAP (Description, PlcTag, FuncCode, …). " +
            "Маска: {Колонка} + «-» между компонентами, «*» — wildcard, (−N)/(+N) — обрезка; " +
            "пустая маска — точное совпадение. Правила применяются при «Обновить связи».";

        public RelayCommand AddCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand SaveRulesCommand { get; }
        public RelayCommand ValidateRulesCommand { get; }
        public RelayCommand LinkFullCommand { get; }
        public RelayCommand LinkFillEmptyCommand { get; }
        public RelayCommand UpdateAddressingCommand { get; }
        public RelayCommand CheckMaskCommand { get; }

        private void RaiseCommands() =>
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();

        /// <summary>Прочитать правила из БД.</summary>
        private void LoadRules()
        {
            try
            {
                Rules.Clear();
                foreach (var r in _s.ModbusLinkRules.Load()) Rules.Add(r);
                StatusText = Rules.Count > 0
                    ? $"Правил в базе: {Rules.Count}"
                    : "Правил нет — добавьте первое (поле IO, поле карты, маска)";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F9.11: ошибка чтения правил связывания: {msg}");
                StatusText = $"Ошибка чтения: {msg}";
            }
        }

        private void AddRule()
        {
            var r = new ModbusLinkRule
            {
                Name = $"Правило {Rules.Count + 1}",
                IOField = "Tag",
                MapField = "Description",
                // F99: маска по умолчанию — полное совпадение по имени тега (PTag_* колонки)
                Mask = MaskPattern.DefaultTagMask,
                Priority = 100 + Rules.Count,
                Enabled = true
            };
            Rules.Add(r);
            SelectedRule = r;
            StatusText = $"Правил (не сохранено): {Rules.Count} — не забудьте «Сохранить правила»";
        }

        private void DeleteRule()
        {
            if (SelectedRule == null) return;
            Rules.Remove(SelectedRule);
            SelectedRule = null;
            StatusText = $"Правил (не сохранено): {Rules.Count} — не забудьте «Сохранить правила»";
        }

        /// <summary>Валидация всех правил без записи (текст результата в StatusText).</summary>
        private void ValidateRules()
        {
            if (Rules.Count == 0) { StatusText = "Правил нет — валидировать нечего"; return; }
            int bad = 0;
            foreach (var r in Rules)
            {
                string? err = ModbusLinkRuleService.Validate(r);
                if (err != null) { StatusText = $"Ошибка в правиле «{r.Name}»: {err}"; bad++; }
            }
            if (bad == 0)
                StatusText = $"Все {Rules.Count} правил валидны";
        }

        /// <summary>Записать набор правил целиком в MODBUS_LINK_RULES (DELETE + INSERT).</summary>
        private void SaveRules()
        {
            try
            {
                int n = _s.ModbusLinkRules.SaveAll(Rules);
                _s.Log.Info($"F9.11: сохранено правил связывания: {n} ({Schema.ModbusLinkRulesTable})");
                StatusText = $"Сохранено правил: {n}. Применятся при следующем «Обновить связи»";
                LoadRules(); // перечитать ID из БД
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F9.11: ошибка сохранения правил: {msg}");
                StatusText = $"Ошибка сохранения: {msg}";
            }
        }

        // ================== Проверка маски на теге (F99) ==================

        /// <summary>Прогнать маску выбранного правила по точке с введённым тегом: показать
        /// паттерн и лучшую (минимальное отклонение) строку карты либо «нет совпадения».</summary>
        private void CheckMask()
        {
            var rule = SelectedRule;
            if (rule == null) { CheckResult = "Выберите правило"; return; }
            string tag = (CheckTag ?? "").Trim();
            if (tag.Length == 0) { CheckResult = "Введите тег точки IO_LIST"; return; }
            try
            {
                var points = _s.Repo.GetAll();
                var p = points.FirstOrDefault(x =>
                    string.Equals(x.Tag?.Trim(), tag, StringComparison.OrdinalIgnoreCase));
                if (p == null) { CheckResult = $"Тег «{tag}» не найден в IO_LIST"; return; }

                string? err = ModbusLinkRuleService.Validate(rule);
                if (err != null) { CheckResult = $"Правило невалидно: {err}"; return; }

                var cols = ModbusBlockPlannerService.ColumnValuesOf(p);
                string? mask = string.IsNullOrWhiteSpace(rule.Mask) ? null : rule.Mask;
                string pattern;
                if (mask != null)
                {
                    var mp = MaskPattern.Parse(mask);
                    pattern = mp?.Evaluate(cols) ?? "";
                }
                else
                {
                    pattern = ModbusLinkRuleService.GetIoValue(p, rule.IOField) ?? "";
                }
                if (pattern.Length == 0)
                {
                    CheckResult = $"Паттерн пуст (маска не оценилась по колонкам точки «{tag}»)";
                    return;
                }

                // лучшая строка карты по Score (как в FindMapRowByMask)
                var map = _s.ModbusMaps.LoadFromDb();
                ModbusMapEntry? best = null;
                int bestScore = int.MaxValue;
                foreach (var r in map)
                {
                    if (!string.IsNullOrWhiteSpace(rule.FuncCode) &&
                        !string.Equals(r.FuncCode?.Trim(), rule.FuncCode.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    string mapVal = ModbusLinkRuleService.GetMapValue(r, rule.MapField) ?? "";
                    int? s = mask != null
                        ? MaskPattern.Score(pattern, mapVal)
                        : (string.Equals(pattern, mapVal, StringComparison.OrdinalIgnoreCase)
                            ? 0 : (int?)null);
                    if (s is int sc && sc < bestScore) { bestScore = sc; best = r; }
                }
                CheckResult = best == null
                    ? $"Паттерн «{pattern}»: совпадений в карте нет"
                    : $"Паттерн «{pattern}» → «{best.PlcTag}» (отклонение {bestScore}, " +
                      $"{rule.MapField}: «{ModbusLinkRuleService.GetMapValue(best, rule.MapField)}»)";
            }
            catch (Exception ex)
            {
                CheckResult = $"Ошибка: {DbProbe.ClassifyMessage(ex.Message)}";
            }
        }
        // ================== Три действия окна (F99: перенесены из главного окна) ==================

        /// <summary>F9.9/F59: связывание текущей таблицы с картой MODBUS_MAP по правилам.
        /// Full — полное обновление (подтверждение: ручные привязки потеряются);
        /// FillEmpty — только пустые связи. M_-поля не записываются (их пишет RunAddressing).</summary>
        private void RunLink(ModbusLinkMode mode)
        {
            string title = mode == ModbusLinkMode.Full
                ? "Обновить связи Modbus — полное обновление"
                : "Заполнить пустые связи Modbus";
            try
            {
                if (_s.ModbusMaps.LoadFromDb().Count == 0)
                {
                    AppendLog("MODBUS_MAP пуст — сначала импортируйте карту адресации «Импорт → Карта Modbus...».");
                    return;
                }

                // F59: полное обновление затирает LinkTag/DcsTag/LinkState всех строк карты —
                // предупреждаем и запрашиваем подтверждение (в т.ч. ручные правки в Access)
                if (mode == ModbusLinkMode.Full)
                {
                    var yes = MessageBox.Show(
                        "ПОЛНОЕ обновление связей Modbus.\n\n" +
                        "Все поля LinkTag / DcsTag / LinkState строк карты MODBUS_MAP будут " +
                        "перезаписаны результатом расчёта — ручные привязки будут потеряны.\n\n" +
                        "Продолжить?\n\n" +
                        "(Чтобы сохранить ручные привязки, используйте «Заполнить пустые связи».)",
                        title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (yes != MessageBoxResult.Yes) return;
                }

                var r = _s.ModbusLink.Link(mode);
                string? lockWarn = ModbusMapService.TakeLockWarning();
                if (lockWarn != null) _s.Log.Warn($"F9.9: {lockWarn}");
                _s.Log.Info($"F9.9: {r.Summary}");

                var sb = new StringBuilder(r.Summary);
                if (r.UnlinkedSamples.Count > 0)
                {
                    sb.AppendLine().AppendLine().AppendLine("Не связано (примеры до 10):");
                    foreach (var s in r.UnlinkedSamples.Take(10)) sb.AppendLine("  " + s);
                }
                if (r.Errors.Count > 0)
                {
                    sb.AppendLine().AppendLine().AppendLine("Ошибки (до 10):");
                    foreach (var e in r.Errors.Take(10)) sb.AppendLine("  " + e);
                    if (r.Errors.Count > 10) sb.AppendLine($"  … и ещё {r.Errors.Count - 10}");
                }
                if (r.MapUnlinkedSamples.Count > 0)
                {
                    sb.AppendLine().AppendLine().AppendLine("Карта без привязки (примеры до 10):");
                    foreach (var s in r.MapUnlinkedSamples.Take(10)) sb.AppendLine("  " + s);
                }
                AppendLog(sb.ToString());
                RefreshMain?.Invoke(); // сетка обновляется (связь пишется в MODBUS_MAP, M_-поля — нет)
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F9.9: ошибка обновления связей Modbus: {msg}");
                AppendLog($"{title}: ОШИБКА — {msg}");
            }
        }

        /// <summary>F9.4: записать M_-поля IO_LIST по парам MODBUS_MAP.DcsTag ↔ IO_LIST.Tag
        /// (только Modbus-сигналы); результат — в журнал окна.</summary>
        private void RunAddressing()
        {
            try
            {
                var r = _s.ModbusLink.UpdateAddressing();
                string? lockWarn = ModbusMapService.TakeLockWarning();
                if (lockWarn != null) _s.Log.Warn($"F9.4: {lockWarn}");
                _s.Log.Info($"F9.4: {r.Summary}");

                var sb = new StringBuilder(r.Summary);
                if (r.Errors.Count > 0)
                {
                    sb.AppendLine().AppendLine().AppendLine("Ошибки:");
                    foreach (var e in r.Errors.Take(10)) sb.AppendLine("  " + e);
                    if (r.Errors.Count > 10) sb.AppendLine($"  … и ещё {r.Errors.Count - 10}");
                }
                if (r.NotFoundSamples.Count > 0)
                {
                    sb.AppendLine().AppendLine("Теги DcsTag, отсутствующие в IO_LIST:");
                    foreach (var s in r.NotFoundSamples) sb.AppendLine("  " + s);
                }
                AppendLog(sb.ToString());
                RefreshMain?.Invoke(); // сетка показывает записанные M_-поля
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F9.4: ошибка обновления адресации Modbus: {msg}");
                AppendLog($"Обновить адресацию: ОШИБКА — {msg}");
            }
        }

        /// <summary>Добавить запись в журнал окна (с отметкой времени; к предыдущему тексту).</summary>
        private void AppendLog(string text)
        {
            LogText = $"[{DateTime.Now:HH:mm:ss}]{Environment.NewLine}{text}{Environment.NewLine}{LogText}";
        }
    }
}