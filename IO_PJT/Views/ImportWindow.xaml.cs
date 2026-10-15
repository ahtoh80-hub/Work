using System;
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

            // A4: перетаскивание .xlsx в окно — то же, что «Обзор...» (открытие + вкладки + «Считать»)
            AllowDrop = true;
            DragEnter += OnFileDragEnter;
            Drop += OnFileDrop;
        }

        /// <summary>Перетащенный файл: принимаем только .xlsx.</summary>
        private static bool IsExcelFile(DragEventArgs e) =>
            e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files &&
            files[0].EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

        private void OnFileDragEnter(object sender, DragEventArgs e)
        {
            e.Effects = IsExcelFile(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnFileDrop(object sender, DragEventArgs e)
        {
            if (!IsExcelFile(e)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (DataContext is ImportViewModel vm) vm.OpenFile(files[0]);
            e.Handled = true;
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
