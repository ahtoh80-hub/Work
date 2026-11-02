using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace IO_PJT.Services
{
    /// <summary>F45: ObservableCollection с батч-добавлением — AddRange шлёт одно событие
    /// Reset вместо N CollectionChanged. Гриды главного окна (Points) и окна F13
    /// (Preview/BindingsPreview) на тысячах строк получали по одному уведомлению на строку.</summary>
    public sealed class RangeObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppress;

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppress) base.OnCollectionChanged(e);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (!_suppress) base.OnPropertyChanged(e);
        }

        /// <summary>Добавить все элементы одним уведомлением Reset (грид-виртуализация
        /// дорисует видимые строки). Перед добавлением коллекция очищается вызывающим.</summary>
        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) return;
            bool any = false;
            _suppress = true;
            try
            {
                foreach (var item in items)
                {
                    Items.Add(item);
                    any = true;
                }
            }
            finally
            {
                _suppress = false;
            }
            if (!any) return;
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}