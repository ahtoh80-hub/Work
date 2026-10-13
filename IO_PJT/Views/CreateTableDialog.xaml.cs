using System.Text.RegularExpressions;
using System.Windows;

namespace IO_PJT.Views
{
    /// <summary>Диалог имени таблицы (ТЗ F1.3): имя любое, по умолчанию IO_LIST.</summary>
    public partial class CreateTableDialog : Window
    {
        /// <summary>Введённое имя таблицы.</summary>
        public string TableName => txtName.Text.Trim();

        public CreateTableDialog(string suggestedName)
        {
            InitializeComponent();
            txtName.Text = string.IsNullOrWhiteSpace(suggestedName) ? "IO_LIST" : suggestedName;
            txtName.SelectAll();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // Имя таблицы Access: буквы, цифры, подчёркивание (без пробелов и спецсимволов)
            if (!Regex.IsMatch(TableName, @"^[A-Za-z_А-Яа-я0-9]+$"))
            {
                MessageBox.Show("Имя таблицы: только буквы, цифры и знак «_».",
                    "Проверка имени", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }

        private void Default_Click(object sender, RoutedEventArgs e) => txtName.Text = "IO_LIST";
    }
}
