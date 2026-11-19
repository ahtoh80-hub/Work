using System.Windows;

namespace IO_PJT.Views
{
    /// <summary>F81: большое всплывающее окно редактора кода шаблона (data/мастер):
    /// просмотр/правка/вставка тела шаблона; «Сохранить» → DialogResult=true,
    /// ResultText — отредактированный код (вызывающая сторона пишет его в БД).</summary>
    public partial class TemplateCodeWindow : Window
    {
        /// <summary>Отредактированный код шаблона (текст из редактора).</summary>
        public string ResultText => CodeBox.Text;

        public TemplateCodeWindow(string title, string text)
        {
            InitializeComponent();
            Title = title;
            CodeBox.Text = text ?? "";
        }

        private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
