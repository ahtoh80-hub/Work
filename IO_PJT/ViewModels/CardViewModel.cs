using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.ViewModels
{
    /// <summary>Строка карточки: колонка таблицы + текущее значение (редактируемое).</summary>
    public sealed class FieldRow : ViewModelBase
    {
        private string? _value;
        public string Column { get; init; } = "";
        public string? Value { get => _value; set => Set(ref _value, value, nameof(Value)); }
    }

    /// <summary>
    /// Карточка сигнала (ТЗ F6.2): все поля дерева IOclass в виде списка
    /// «колонка ↔ значение»; запись — Insert/Update с аудитом.
    /// </summary>
    public sealed class CardViewModel : ViewModelBase
    {
        private readonly AppServices _s;
        private readonly IoPoint _point;
        private readonly bool _isNew;

        public string Title { get; }
        public ObservableCollection<FieldRow> Rows { get; } = new();

        public RelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }

        /// <summary>Запрос закрытия окна (назначается окном).</summary>
        public Action? CloseRequested { get; set; }

        public CardViewModel(AppServices services, IoPoint point, bool isNew)
        {
            _s = services;
            _point = point;
            _isNew = isNew;
            Title = isNew ? "Новый сигнал" : $"Сигнал: {point.Tag ?? "(без тега)"} (ID={point.Id})";

            // Все колонки дерева (FieldRegistry.Columns) — для просмотра и редактирования, включая M_* (F9)
            foreach (var col in FieldRegistry.Columns)
            {
                Rows.Add(new FieldRow
                {
                    Column = col,
                    Value = FieldRegistry.GetValue(point, col)?.ToString()
                });
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
        }

        /// <summary>Запись значений из списка обратно в модель и в базу (F4).</summary>
        private void Save()
        {
            // 1. Значения из строк → модель
            foreach (var row in Rows)
                FieldRegistry.SetValue(_point, row.Column,
                    string.IsNullOrWhiteSpace(row.Value) ? null : row.Value);

            // 2. F2.15: очистка Tag от пробельных символов (в т.ч. NBSP) — до проверки и PTag_*
            string? rawTag = _point.Tag;
            string? cleanTag = TagParser.CleanWhitespace(rawTag);
            if (!string.Equals(rawTag, cleanTag, StringComparison.Ordinal))
            {
                _point.Identification.Tag = cleanTag;
                foreach (var r in Rows)
                    if (string.Equals(r.Column, "Tag", StringComparison.OrdinalIgnoreCase))
                        r.Value = cleanTag;
                _s.Repo.RecordTagCleanedAudit(rawTag, cleanTag,
                    _point.Revision?.FileName, _point.Revision?.Sheet, _point.Identification.System);
                _s.Log.Warn($"Карточка: Tag очищен от пробелов: «{rawTag}» → «{cleanTag ?? ""}» (F2.15)");
            }

            // 3. Обязательный Tag (F5.1)
            if (string.IsNullOrWhiteSpace(_point.Tag))
            {
                MessageBox.Show("Поле Tag обязательно и не может быть пустым.",
                    "Валидация", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 4. PTag_* — только программно из Tag (Б8 п.1/9)
            TagParser.Fill(_point);

            try
            {
                if (_isNew || _point.Id == 0)
                    _s.Repo.Insert(_point);
                else
                    _s.Repo.Update(_point);
                _s.Log.Info($"Карточка сохранена: Tag={_point.Tag}");
                CloseRequested?.Invoke();
            }
            catch (Exception ex)
            {
                _s.Log.Error($"Ошибка сохранения карточки: {ex.Message}");
                MessageBox.Show("Ошибка сохранения: " + ex.Message, "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
