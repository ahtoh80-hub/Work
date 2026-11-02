using System;
using System.Windows;
using System.Windows.Input;

namespace IO_PJT.Views
{
    /// <summary>F65: окно «Генерация экземпляров типовых алгоритмов» — шаблон логической
    /// схемы, привязки сигналов ↔ TYPNO, контуры Loop, генерация BufScadaPOUS с пагинацией.</summary>
    public partial class ScadaAlgoInstancesWindow : Window
    {
        public ScadaAlgoInstancesWindow()
        {
            InitializeComponent();
            Loaded += OnLoadedFirst;
        }

        /// <summary>F18.1-паттерн: вписать окно в рабочую область экрана и отцентрировать.</summary>
        private void OnLoadedFirst(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoadedFirst;
            var wa = SystemParameters.WorkArea;
            double h = Math.Min(ActualHeight, wa.Height - 30);
            double w = Math.Min(ActualWidth, wa.Width);
            if (h >= ActualHeight && w >= ActualWidth) return;
            Height = Math.Max(h, 560);
            Width = Math.Max(w, 900);
            Left = wa.Left + Math.Max(0, (wa.Width - Width) / 2);
            Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
        }

        /// <summary>F18.1-паттерн: колёсико мыши не «залипает» в гридах — после их конца
        /// прокрутка продолжается во внешнем ScrollViewer (здесь его нет: гриды растянуты
        /// Grid-ом, метод оставлен для совместимости с XAML-подписью PreviewMouseWheel).</summary>
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // гриды F65 растянуты ячейками Grid и прокрутки внутрь себя не уводят — no-op
        }
    }
}
