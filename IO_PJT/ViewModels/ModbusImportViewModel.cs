using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using IO_PJT.Data;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.ViewModels
{
    /// <summary>F9: окно «Импорт карты Modbus» — выбор файлов карт адресации (можно несколько),
    /// разбор Register Table Function 02/03/06 + Interface parameters → MODBUS_MAP (замена по файлу),
    /// затем связывание IO_LIST (M_-поля + аудит) с превью итогов.</summary>
    public sealed class ModbusImportViewModel : ViewModelBase
    {
        private readonly AppServices _s;

        /// <summary>Выбранные файлы карт адресации.</summary>
        public ObservableCollection<string> Files { get; } = new();

        private string _statusText = "Выберите файл(ы) карты адресации Modbus";
        public string StatusText { get => _statusText; set => Set(ref _statusText, value, nameof(StatusText)); }

        private bool _busy;
        public bool IsBusy { get => _busy; set => Set(ref _busy, value, nameof(IsBusy)); }

        private bool _auditEnabled;

        /// <summary>F9.9: «Вести аудит» — глобальный переключатель AuditService.Enabled.
        /// Выключенный аудит: сообщения связывания и изменения полей не пишутся в IO_LIST_Audit;
        /// в журнале событий остаются сообщения связывания и минимизированный список
        /// затронутых Tag («изменён тег …» / сводка по пакету).</summary>
        public bool AuditEnabled
        {
            get => _auditEnabled;
            set { Set(ref _auditEnabled, value, nameof(AuditEnabled)); _s.Audit.Enabled = value; }
        }

        /// <summary>Итоги последнего связывания (строки для превью).</summary>
        public ObservableCollection<string> Results { get; } = new();

        public RelayCommand AddFilesCommand { get; }
        public RelayCommand RemoveSelectedCommand { get; }
        public RelayCommand ImportCommand { get; }

        /// <summary>Запрос закрытия окна (назначается окном).</summary>
        public Action? CloseRequested { get; set; }

        public ModbusImportViewModel(AppServices services)
        {
            _s = services;
            _auditEnabled = services.Audit.Enabled; // F9.9: текущее глобальное состояние аудита
            AddFilesCommand = new RelayCommand(AddFilesDialog);
            RemoveSelectedCommand = new RelayCommand(() => { }, () => false); // назначается окном (SelectedItem)
            ImportCommand = new RelayCommand(Import, () => !IsBusy && Files.Count > 0);
        }

        /// <summary>Мультивыбор файлов карты (*.xlsx).</summary>
        private void AddFilesDialog()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Карта адресации Modbus (*.xlsx)|*.xlsx",
                Multiselect = true,
                Title = "Выбор карты адресации Modbus (Register Table)"
            };
            if (dlg.ShowDialog() != true) return;
            AddFiles(dlg.FileNames);
        }

        /// <summary>F9.4: добавить файл(ы) карты — из диалога и по drag&drop в окно
        /// (принимаются только *.xlsx, повторы игнорируются).</summary>
        public void AddFiles(IEnumerable<string> paths)
        {
            int added = 0;
            foreach (string f in paths)
            {
                if (!f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) continue;
                if (Files.Contains(f)) continue;
                Files.Add(f);
                added++;
            }
            if (added > 0 || Files.Count > 0)
                StatusText = $"Выбрано файлов: {Files.Count}";
        }

        /// <summary>Импорт карт (MODBUS_MAP, замена по имени файла) + связывание IO_LIST.</summary>
        private void Import()
        {
            if (Files.Count == 0) return;
            IsBusy = true;
            Results.Clear();
            try
            {
                _s.Creator.EnsureServiceTables(); // гарантия MODBUS_MAP
                _s.ModbusMaps.EnsureTable();

                // ===== 1. Разбор и запись карт в MODBUS_MAP =====
                int totalRows = 0;
                foreach (string path in Files)
                {
                    var entries = new List<ModbusMapEntry>();
                    var read = _s.ModbusMaps.Read(path, entries);
                    int saved;
                    try
                    {
                        saved = _s.ModbusMaps.SaveToDb(entries, read.FileName);
                    }
                    catch (Exception ex)
                    {
                        // F9-фикс: имя файла + человекочитаемая причина (блокировка БД и т.п.)
                        throw new InvalidOperationException(
                            $"Импорт карты «{read.FileName}» не выполнен: " +
                            DbProbe.ClassifyMessage(ex.Message), ex);
                    }
                    totalRows += saved;
                    Results.Add($"Карта «{read.FileName}» (станция {read.SlaveStation ?? "?"}): " +
                                $"прочитано {read.RowsRead}, записано в {Schema.ModbusMapTable}: {saved}");
                    _s.Log.Info($"F9: карта «{read.FileName}» — {saved} строк в {Schema.ModbusMapTable}");
                }

                // ===== 2. Связывание IO_LIST с картой =====
                ModbusLinkResult linkResult;
                try
                {
                    linkResult = _s.ModbusLink.Link();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Связывание не выполнено: " + DbProbe.ClassifyMessage(ex.Message), ex);
                }
                // F9-фикс: если блокировки переживались повторами — фиксируем в журнале
                string? lockWarn = ModbusMapService.TakeLockWarning();
                if (lockWarn != null)
                    _s.Log.Warn($"F9: {lockWarn}");
                Results.Add(linkResult.Summary);
                foreach (var s in linkResult.UnlinkedSamples)
                    Results.Add("  не связано: " + s);
                foreach (var s in linkResult.MapUnlinkedSamples)
                    Results.Add("  карта без привязки: " + s);
                // F9.4: связь с не-Modbus-сигналом не записывается — показываем ошибки
                foreach (var e in linkResult.Errors)
                    Results.Add("  ОШИБКА: " + e);
                Results.Add("M_-поля IO_LIST не записаны — используйте «Обновить адресацию Modbus» (F9.4)");

                StatusText = $"Готово. Строк карты: {totalRows}. {linkResult.Summary}";
            }
            catch (Exception ex)
            {
                _s.Log.Error($"Ошибка импорта карты Modbus: {ex.Message}");
                StatusText = "Ошибка: " + ex.Message;
                MessageBox.Show(ex.Message, "Ошибка импорта карты Modbus",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}