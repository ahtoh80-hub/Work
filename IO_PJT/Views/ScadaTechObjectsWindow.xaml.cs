using System;
using System.Windows;
using System.Windows.Controls;

namespace IO_PJT.Views
{
    /// <summary>F12: окно «Генерация перечня Modbus (Тех.объекты)» — настройки, превью, экспорт.</summary>
    public partial class ScadaTechObjectsWindow : Window
    {
        public ScadaTechObjectsWindow()
        {
            InitializeComponent();
        }

        /// <summary>F79 (по образцу F77): красный список сообщений «ИМЯ:» — новая порция
        /// сообщений докручивает список до конца, иначе при большом числе сообщений конец
        /// уходит за край панели. Проверка после layout (DispatcherPriority.Loaded).</summary>
        private void NameWarningsBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            tb.Dispatcher.BeginInvoke(new Action(() => tb.ScrollToEnd()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>F79: «✕» — закрыть (очистить) список сообщений; панель скрывается по
        /// пустому тексту (DataTrigger), при следующей операции сообщения появятся заново.</summary>
        private void NameWarningsClear_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ScadaTechObjectsViewModel vm)
                vm.NameWarningsText = "";
        }
    }
}