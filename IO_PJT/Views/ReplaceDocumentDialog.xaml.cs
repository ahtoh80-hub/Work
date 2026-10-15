using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace IO_PJT.Views
{
    /// <summary>F2.11/F2.16/F2.17: диалог «взамен каких документов импортируется файл» —
    /// список документов (пары REV_FileName + REV_Sheet) из целевой таблицы Access.
    /// Документ = файл + вкладка; мультивыбор (Ctrl/Shift) — документы делятся по ревизиям.</summary>
    public partial class ReplaceDocumentDialog : Window
    {
        private readonly List<(string FileName, string? Sheet)> _docs;

        /// <summary>F2.17: выбранные заменяемые документы (файл + вкладка); пусто — ничего не выбрано.</summary>
        public IReadOnlyList<(string FileName, string? Sheet)> SelectedDocuments
        {
            get
            {
                var result = new List<(string FileName, string? Sheet)>();
                for (int i = 0; i < lstDocs.Items.Count; i++)
                    if (lstDocs.SelectedItems.Contains(lstDocs.Items[i]) && i < _docs.Count)
                        result.Add(_docs[i]);
                return result;
            }
        }

        public ReplaceDocumentDialog(IReadOnlyList<(string FileName, string? Sheet)> documents)
        {
            InitializeComponent();
            _docs = new List<(string, string?)>(documents);
            foreach (var d in _docs)
                lstDocs.Items.Add(FormatDoc(d.FileName, d.Sheet));
            if (lstDocs.Items.Count > 0) lstDocs.SelectedIndex = 0;
            UpdateSelectedInfo();
        }

        /// <summary>Отображение документа: «файл :: вкладка»; старые записи без вкладки — «файл (без вкладки)».</summary>
        public static string FormatDoc(string fileName, string? sheet) =>
            string.IsNullOrWhiteSpace(sheet) ? $"{fileName} (без вкладки)" : $"{fileName} :: {sheet}";

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            lstDocs.SelectAll();
            UpdateSelectedInfo();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            lstDocs.UnselectAll();
            UpdateSelectedInfo();
        }

        private void List_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
            UpdateSelectedInfo();

        private void UpdateSelectedInfo() =>
            tbSelected.Text = lstDocs.SelectedItems.Count > 0
                ? $"выбрано: {lstDocs.SelectedItems.Count}"
                : "выбрано: 0";

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDocuments.Count == 0)
            {
                MessageBox.Show("Отметьте хотя бы один заменяемый документ из списка " +
                                "(Ctrl/Shift — множественный выбор).", "Замена документов",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}