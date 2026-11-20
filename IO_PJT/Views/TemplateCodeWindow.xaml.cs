using System.Windows;

namespace IO_PJT.Views
{
    /// <summary>Template code editor window. Provides optional name and version inputs;
    /// on save the caller writes body/name/version to DB (version can be edited manually).</summary>
    public partial class TemplateCodeWindow : Window
    {
        public string ResultText => CodeBox.Text;

        public string? TemplateName => string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim();

        public int? TemplateVersion
        {
            get
            {
                if (string.IsNullOrWhiteSpace(VersionBox.Text)) return null;
                return int.TryParse(VersionBox.Text.Trim(), out var v) && v >= 0 ? v : (int?)null;
            }
        }

        public TemplateCodeWindow(string title, string text) : this(title, null, null, text) { }

        public TemplateCodeWindow(string title, string? name, int? version, string text)
        {
            InitializeComponent();
            Title = title;
            CodeBox.Text = text ?? "";
            if (!string.IsNullOrWhiteSpace(name)) NameBox.Text = name;
            if (version.HasValue) VersionBox.Text = version.Value.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
