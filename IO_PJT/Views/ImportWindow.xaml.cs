using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using IO_PJT.ViewModels;

namespace IO_PJT.Views
{
    /// <summary>
    /// Единое окно импорта (ТЗ F6.3). Колонки предпросмотра строятся динамически;
    /// ячейки с некорректными данными подсвечиваются красным (Задание п.2).
    /// </summary>
    public partial class ImportWindow : Window
    {
        public ImportWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, _) => BuildPreviewColumns();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ImportViewModel old) old.PreviewColumnsChanged -= BuildPreviewColumns;
            if (e.NewValue is ImportViewModel vm) vm.PreviewColumnsChanged += BuildPreviewColumns;
        }

        /// <summary>Построение колонок предпросмотра по заголовкам Excel + подсветка ошибок.</summary>
        private void BuildPreviewColumns()
        {
            if (DataContext is not ImportViewModel vm) return;

            previewGrid.Columns.Clear();
            previewGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Строка",
                Binding = new Binding(nameof(PreviewRow.ExcelRow)),
                Width = new DataGridLength(60)
            });

            for (int i = 0; i < vm.PreviewColumns.Count - 1; i++)
            {
                var column = new DataGridTextColumn
                {
                    Header = vm.PreviewColumns[i + 1],
                    Binding = new Binding($"[{i}].Value") { Mode = BindingMode.OneWay },
                    Width = new DataGridLength(130)
                };

                // Стиль ячейки: подсказка + красная подсветка при ошибке
                var style = new Style(typeof(TextBlock));
                style.Setters.Add(new Setter(TextBlock.ToolTipProperty, new Binding($"[{i}].Hint")));

                var trigger = new DataTrigger
                {
                    Binding = new Binding($"[{i}].IsError"),
                    Value = true
                };
                trigger.Setters.Add(new Setter(TextBlock.BackgroundProperty,
                    new SolidColorBrush(Color.FromRgb(0xFF, 0xC7, 0xCE))));
                trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty,
                    new SolidColorBrush(Color.FromRgb(0x9C, 0x00, 0x06))));
                style.Triggers.Add(trigger);

                // F2.12: зачёркнутая в Excel ячейка — зачёркнутый текст в предпросмотре
                var struckTrigger = new DataTrigger
                {
                    Binding = new Binding($"[{i}].IsStruck"),
                    Value = true
                };
                struckTrigger.Setters.Add(new Setter(TextBlock.TextDecorationsProperty,
                    TextDecorations.Strikethrough));
                struckTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty,
                    new SolidColorBrush(Color.FromRgb(0x7F, 0x7F, 0x7F))));
                style.Triggers.Add(struckTrigger);

                column.ElementStyle = style;
                previewGrid.Columns.Add(column);
            }
        }
    }
}
