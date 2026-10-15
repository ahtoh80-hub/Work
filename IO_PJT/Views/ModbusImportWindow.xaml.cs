using System;
using System.Windows;
using IO_PJT.ViewModels;

namespace IO_PJT.Views
{
    /// <summary>F9: окно импорта карты адресации Modbus (MVVM — логика в ModbusImportViewModel).
    /// F9.4: перетаскивание файлов *.xlsx в окно (кнопка «Добавить файлы...»).</summary>
    public partial class ModbusImportWindow : Window
    {
        public ModbusImportWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (DataContext is ModbusImportViewModel vm)
                    vm.CloseRequested = Close;
            };

            // F9.4: drag&drop файлов карты — то же, что «Добавить файлы...» (все *.xlsx из списка)
            AllowDrop = true;
            DragEnter += OnFileDragEnter;
            Drop += OnFileDrop;
        }

        /// <summary>Перетащенные файлы: принимаем только .xlsx.</summary>
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
            if (DataContext is ModbusImportViewModel vm) vm.AddFiles(files);
            e.Handled = true;
        }
    }
}