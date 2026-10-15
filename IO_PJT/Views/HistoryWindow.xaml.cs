using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IO_PJT.Views
{
    /// <summary>Окно истории изменений (F7) + экспорт в Excel; A2 — внешний ползунок
    /// горизонтальной прокрутки под DataGrid, синхронизированный с внутренним ScrollViewer таблицы.</summary>
    public partial class HistoryWindow : Window
    {
        private ScrollViewer? _gridScroll;
        private bool _syncing;

        public HistoryWindow()
        {
            InitializeComponent();
            grid.Loaded += (_, _) => AttachToGridScroll();
            // Перестройка строк/колонок меняет размеры — обновляем диапазон ползунка
            grid.LayoutUpdated += (_, _) => UpdateSliderFromScroll();
        }

        /// <summary>Найти внутренний ScrollViewer DataGrid (содержит горизонтальную прокрутку колонок).</summary>
        private ScrollViewer? FindScroll(DependencyObject root)
        {
            if (root is ScrollViewer sv) return sv;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var found = FindScroll(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private void AttachToGridScroll()
        {
            _gridScroll ??= FindScroll(grid);
            if (_gridScroll == null) return;
            _gridScroll.ScrollChanged -= OnGridScrollChanged;
            _gridScroll.ScrollChanged += OnGridScrollChanged;
            UpdateSliderFromScroll();
        }

        /// <summary>Таблица прокручена (колёсико/перетаскивание) → двигаем ползунок.</summary>
        private void OnGridScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateSliderFromScroll();

        private void UpdateSliderFromScroll()
        {
            if (_gridScroll == null) { AttachToGridScroll(); if (_gridScroll == null) return; }
            _syncing = true;
            try
            {
                double max = System.Math.Max(0, _gridScroll.ExtentWidth - _gridScroll.ViewportWidth);
                hSlider.Minimum = 0;
                hSlider.Maximum = max;
                hSlider.IsEnabled = max > 0;
                if (System.Math.Abs(hSlider.Value - _gridScroll.HorizontalOffset) > 0.5)
                    hSlider.Value = _gridScroll.HorizontalOffset;
            }
            finally { _syncing = false; }
        }

        /// <summary>Ползунок двигают → прокручиваем таблицу.</summary>
        private void HSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing || _gridScroll == null) return;
            _syncing = true;
            try { _gridScroll.ScrollToHorizontalOffset(e.NewValue); }
            finally { _syncing = false; }
        }
    }
}
