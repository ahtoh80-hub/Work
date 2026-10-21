using System.Windows;

namespace IO_PJT.Views
{
    /// <summary>F16: диалог ввода имени шаблона привязок («спросить имя» — при вставке
    /// кода в режиме «Код» или при сохранении без заданного имени).</summary>
    public partial class NamePromptWindow : Window
    {
        /// <summary>Введённое имя (обрезанные края).</summary>
        public string EnteredName => NameBox.Text.Trim();

        public NamePromptWindow(string prompt, string initial = "")
        {
            InitializeComponent();
            PromptText.Text = prompt;
            NameBox.Text = initial;
            Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show(this, "Введите имя шаблона.", "Имя шаблона привязок",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }

        /// <summary>F16: показать диалог ввода имени; null — отмена.</summary>
        public static string? Ask(string prompt, string initial = "", Window? owner = null)
        {
            var w = new NamePromptWindow(prompt, initial);
            if (owner != null) w.Owner = owner;
            return w.ShowDialog() == true ? w.EnteredName : null;
        }
    }
}
