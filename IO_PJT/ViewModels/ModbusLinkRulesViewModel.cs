using System;
using System.Collections.ObjectModel;
using System.Windows;
using IO_PJT.Data;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.ViewModels
{
    /// <summary>F9.11: окно «Правила связывания Modbus» — редактор таблицы MODBUS_LINK_RULES
    /// (ручные зависимости «поле IO по шаблону → поле карты по шаблону»). Правила применяются
    /// в Link() ПЕРЕД встроенными ролями DetectRole; невалидные шаблоны не сохраняются
    /// (валидация ModbusLinkRuleService.Validate при сохранении).</summary>
    public sealed class ModbusLinkRulesViewModel : ViewModelBase
    {
        private readonly AppServices _s;

        /// <summary>Правила из MODBUS_LINK_RULES (редактируется в гриде; сохранение — SaveCommand).</summary>
        public ObservableCollection<ModbusLinkRule> Rules { get; } = new();

        private string _statusText = "Добавьте правило: поле IO (Tag) по шаблону → поле карты (Description) по шаблону";
        public string StatusText { get => _statusText; set => Set(ref _statusText, value, nameof(StatusText)); }

        private ModbusLinkRule? _selectedRule;
        public ModbusLinkRule? SelectedRule { get => _selectedRule; set => Set(ref _selectedRule, value, nameof(SelectedRule)); }

        public RelayCommand AddCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand ValidateCommand { get; }

        /// <summary>Поля IO и карты — подсказка для грида (валидация — при сохранении).</summary>
        public string FieldsHint =>
            $"IO: {string.Join(", ", ModbusLinkRuleService.IoFields)}; " +
            "карта: любое строковое поле MODBUS_MAP (Description, Scale, PlcTag, FuncCode, …). " +
            "Шаблон с «*»/«?» (напр. *Fault*) — wildcard; с метасимволами regex (напр. MXA.*A) — регулярное выражение.";

        public ModbusLinkRulesViewModel(AppServices services)
        {
            _s = services;
            AddCommand = new RelayCommand(Add);
            DeleteCommand = new RelayCommand(Delete, () => SelectedRule != null);
            SaveCommand = new RelayCommand(Save);
            ValidateCommand = new RelayCommand(Validate);
            Load();
        }

        /// <summary>Прочитать правила из БД.</summary>
        private void Load()
        {
            try
            {
                Rules.Clear();
                foreach (var r in _s.ModbusLinkRules.Load()) Rules.Add(r);
                StatusText = Rules.Count > 0
                    ? $"Правил в базе: {Rules.Count}"
                    : "Правил нет — добавьте первое (пример: IOField=Tag, TagPattern=MXA+A → MapField=Description, ValuePattern=*Fault*)";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F9.11: ошибка чтения правил связывания: {msg}");
                StatusText = $"Ошибка чтения: {msg}";
            }
        }

        private void Add()
        {
            var r = new ModbusLinkRule
            {
                Name = $"Правило {Rules.Count + 1}",
                IOField = "Tag",
                TagPattern = "*MXA*A",
                MapField = "Description",
                ValuePattern = "*Fault*",
                Priority = 100 + Rules.Count,
                Enabled = true
            };
            Rules.Add(r);
            SelectedRule = r;
            StatusText = $"Правил (не сохранено): {Rules.Count} — не забудьте «Сохранить»";
        }

        private void Delete()
        {
            if (SelectedRule == null) return;
            Rules.Remove(SelectedRule);
            SelectedRule = null;
            StatusText = $"Правил (не сохранено): {Rules.Count} — не забудьте «Сохранить»";
        }

        /// <summary>Валидация всех правил без записи (текст результата в StatusText).</summary>
        private void Validate()
        {
            if (Rules.Count == 0) { StatusText = "Правил нет — валидировать нечего"; return; }
            int bad = 0;
            foreach (var r in Rules)
            {
                string? err = ModbusLinkRuleService.Validate(r);
                if (err != null) { StatusText = $"Ошибка в правиле «{r.Name}»: {err}"; bad++; }
            }
            if (bad == 0)
                StatusText = $"Все {Rules.Count} правил валидны ({FieldsHint})";
        }

        /// <summary>Записать набор правил целиком в MODBUS_LINK_RULES (DELETE + INSERT).</summary>
        private void Save()
        {
            try
            {
                if (Rules.Count > 0)
                {
                    foreach (var r in Rules)
                    {
                        string? err = ModbusLinkRuleService.Validate(r);
                        if (err != null)
                        {
                            MessageBox.Show($"Правило «{r.Name}»: {err}", "Правила связывания Modbus",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                }
                int n = _s.ModbusLinkRules.SaveAll(Rules);
                _s.Log.Info($"F9.11: сохранено правил связывания: {n} ({Schema.ModbusLinkRulesTable})");
                StatusText = $"Сохранено правил: {n}. Применятся при следующем связывании («Обновить связи Modbus»)";
            }
            catch (Exception ex)
            {
                string msg = DbProbe.ClassifyMessage(ex.Message);
                _s.Log.Error($"F9.11: ошибка сохранения правил: {msg}");
                MessageBox.Show(msg, "Правила связывания Modbus", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText = $"Ошибка сохранения: {msg}";
            }
        }
    }
}
