using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IO_PJT.Views
{
    /// <summary>F13/F18: окно «Генерация блоков Modbus (XML)» — планирование пакетов,
    /// превью, экспорт BufScadaPOUS XML. F18.1: прокрутка левой колонки настроек —
    /// ① окно при открытии вписывается в рабочую область экрана (на невысоких мониторах
    /// низ окна и ползунок уходили за край — нижние группы, напр. «Typno», были недостижимы);
    /// ② колёсико мыши не «залипает» во внутренних таблицах фиксированной высоты
    /// («станция → SlaveId», «Сопоставление Typno») — когда такая таблица дошла до своего
    /// конца, прокрутка продолжается во внешнем ScrollViewer левой колонки.</summary>
    public partial class ScadaModbusBlocksWindow : Window
    {
        public ScadaModbusBlocksWindow()
        {
            InitializeComponent();
            Loaded += OnLoadedFirst;
            // PreviewMouseWheel подписан в XAML окна (Window_PreviewMouseWheel)
        }

        /// <summary>F18.1: вписать окно в рабочую область экрана и заново отцентрировать,
        /// чтобы ползунок левой колонки всегда докручивался до низа.</summary>
        private void OnLoadedFirst(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoadedFirst;
            var wa = SystemParameters.WorkArea;
            double h = Math.Min(ActualHeight, wa.Height - 30); // минус высота заголовка окна
            double w = Math.Min(ActualWidth, wa.Width);
            if (h >= ActualHeight && w >= ActualWidth) return; // окно и так помещается
            Height = Math.Max(h, 560);
            Width = Math.Max(w, 900);
            Left = wa.Left + Math.Max(0, (wa.Width - Width) / 2);
            Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
        }

        /// <summary>F28: клик по чекбоксу станции — обновить подсказку «Генерить станций: X из Y»
        /// (IsSelected уже записан TwoWay-привязкой в момент Click).</summary>
        private void StationFilter_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ScadaModbusBlocksViewModel vm)
                vm.NotifyStationFilterChanged();
        }

        /// <summary>F18.1: «залипшее» колёсико внутренней таблицы левой колонки →
        /// продолжить прокрутку внешнего ScrollViewer (до нижних групп настроек).</summary>
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled) return;
            if (e.OriginalSource is not DependencyObject src) return;
            var left = LeftScroller;
            if (left == null) return;
            var first = FindAncestorScrollViewer(src);
            // обычная область (сам внешний скролл) или правая колонка — штатное поведение
            if (first == null || ReferenceEquals(first, left)) return;
            if (!left.IsAncestorOf(first)) return;
            bool innerCanUp = first.VerticalOffset > 0.5;
            bool innerCanDown = first.VerticalOffset < first.ScrollableHeight - 0.5;
            // внутренняя таблица ещё может крутиться в эту сторону — отдать ей
            if ((e.Delta > 0 && innerCanUp) || (e.Delta < 0 && innerCanDown)) return;
            double step = e.Delta / 120.0 * 48.0; // ~3 строки на щелчок колеса
            left.ScrollToVerticalOffset(left.VerticalOffset - step);
            e.Handled = true;
        }

        /// <summary>F18.1: ближайший ScrollViewer-предок (учитывает и текстовые
        /// FrameworkContentElement — например Run в ячейке DataGrid).</summary>
        private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? from)
        {
            for (DependencyObject? d = from; d != null;)
            {
                if (d is ScrollViewer sv) return sv;
                d = d is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : d is FrameworkContentElement fce ? LogicalTreeHelper.GetParent(fce) : null;
            }
            return null;
        }
    }
}
