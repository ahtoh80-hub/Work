using System.Collections.Generic;
using System.Windows;

namespace IO_PJT.Views
{
    /// <summary>F2.11/F2.16: диалог «взамен какого документа импортируется файл» —
    /// список документов (пары REV_FileName + REV_Sheet) из целевой таблицы Access.
    /// Документ = файл + вкладка: один файл с разными вкладками — разные документы.</summary>
    public partial class ReplaceDocumentDialog : Window
    {
        private readonly List<(string FileName, string? Sheet)> _docs;

        /// <summary>Выбранный заменяемый документ (файл + вкладка); null — не выбран.</summary>
        public (string FileName, string? Sheet)? SelectedDocument
        {
            get
            {
                int i = cboDocs.SelectedIndex;
                return i >= 0 && i < _docs.Count ? _docs[i] : null;
            }
        }

        public ReplaceDocumentDialog(IReadOnlyList<(string FileName, string? Sheet)> documents)
        {
            InitializeComponent();
            _docs = new List<(string, string?)>(documents);
            foreach (var d in _docs)
                cboDocs.Items.Add(FormatDoc(d.FileName, d.Sheet));
            if (cboDocs.Items.Count > 0) cboDocs.SelectedIndex = 0;
        }

        /// <summary>Отображение документа: «файл :: вкладка»; старые записи без вкладки — «файл (без вкладки)».</summary>
        public static string FormatDoc(string fileName, string? sheet) =>
            string.IsNullOrWhiteSpace(sheet) ? $"{fileName} (без вкладки)" : $"{fileName} :: {sheet}";

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDocument == null)
            {
                MessageBox.Show("Выберите заменяемый документ из списка.", "Замена документа",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}