using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using IO_PJT.Models;
using IO_PJT.Services;
using IO_PJT.Views;

namespace IO_PJT.ViewModels
{
    /// <summary>Ячейка предпросмотра: значение + признак некорректности (подсветка) + зачёркивание (F2.12).</summary>
    public sealed class PreviewCell
    {
        public string Value { get; init; } = "";
        public bool IsError { get; init; }
        public bool IsStruck { get; init; }
        public string Hint { get; init; } = "";
    }

    /// <summary>Строка предпросмотра (индексатор — доступ по номеру колонки для DataGrid).</summary>
    public sealed class PreviewRow
    {
        public int ExcelRow { get; init; }
        public PreviewCell[] Cells { get; init; } = Array.Empty<PreviewCell>();
        public PreviewCell this[int i] => Cells[i];
    }

    /// <summary>Строка маппинга: заголовок Excel + выбранная колонка таблицы (Bind).</summary>
    public sealed class MapRow : ViewModelBase
    {
        private string? _accessColumn;
        public string ExcelColumn { get; init; } = "";

        /// <summary>F2.14: 0-based индекс колонки Excel — различие дубликатов заголовков по позиции.</summary>
        public int? ExcelColumnIndex { get; init; }

        /// <summary>F2.14: подсказка из AutoDetect (дубликаты заголовков E008).</summary>
        public string Note { get; init; } = "";

        /// <summary>F2.14: отображение «MIN (столбец H)» — различие одинаковых заголовков в списке.</summary>
        public string ExcelColumnDisplay => ExcelColumnIndex is int i
            ? $"{ExcelColumn} (столбец {ExcelColumns.Letter(i)})"
            : ExcelColumn;

        public string? AccessColumn
        {
            get => _accessColumn;
            set
            {
                if (Set(ref _accessColumn, value, nameof(AccessColumn)))
                {
                    Raise(nameof(Description));
                    Changed?.Invoke();
                }
            }
        }

        public Action? Changed { get; set; }
        public bool IsBound => !string.IsNullOrEmpty(AccessColumn);
        public bool IsTag => string.Equals(AccessColumn, "Tag", StringComparison.OrdinalIgnoreCase);

        /// <summary>Всплывающее пояснение поля (назначение) — тултип в окне маппинга + подсказка F2.14.</summary>
        public string Description
        {
            get
            {
                string baseDesc = string.IsNullOrEmpty(AccessColumn)
                    ? "Не привязано — выберите колонку таблицы Access"
                    : Data.FieldDescriptions.Get(AccessColumn);
                return string.IsNullOrEmpty(Note) ? baseDesc : $"{baseDesc} | {Note}";
            }
        }
    }

    /// <summary>Единое окно импорта (ТЗ F6.3): файл, вкладка/строка, таблица (+создание),
    /// панель привязки Excel↔колонки, предпросмотр, Apply.</summary>
    public sealed class ImportViewModel : ViewModelBase
    {
        private readonly AppServices _s;

        // —— Источник Excel (F2.1) ——
        private string _excelPath = "";
        public string ExcelPath { get => _excelPath; set => Set(ref _excelPath, value, nameof(ExcelPath)); }

        public ObservableCollection<string> Sheets { get; } = new();
        private string? _selectedSheet;
        public string? SelectedSheet { get => _selectedSheet; set => Set(ref _selectedSheet, value, nameof(SelectedSheet)); }

        private string _headerRow = "1";
        public string HeaderRow { get => _headerRow; set => Set(ref _headerRow, value, nameof(HeaderRow)); }

        // —— Целевая таблица Access (F1.2/F1.3) ——
        public ObservableCollection<string> Tables { get; } = new();
        private string? _selectedTable;
        public string? SelectedTable { get => _selectedTable; set => Set(ref _selectedTable, value, nameof(SelectedTable)); }

        private string _newTableName = "IO_LIST";
        public string NewTableName { get => _newTableName; set => Set(ref _newTableName, value, nameof(NewTableName)); }

        // —— Режим импорта (F2.7) ——
        public ObservableCollection<string> Modes { get; } = new() { "Добавить (upsert по Tag)", "Заменить (очистить)" };
        private string _selectedMode = "Добавить (upsert по Tag)";
        public string SelectedMode { get => _selectedMode; set => Set(ref _selectedMode, value, nameof(SelectedMode)); }

        private bool _importWithAudit = true;
        /// <summary>Галочка «Импорт с аудитом» (F2.7; по умолчанию включена).</summary>
        public bool ImportWithAudit
        {
            get => _importWithAudit;
            set => Set(ref _importWithAudit, value, nameof(ImportWithAudit));
        }

        // —— Маппинг (F2.2) ——
        public ObservableCollection<MapRow> Mappings { get; } = new();
        public ObservableCollection<string> AccessColumns { get; } = new();

        private bool _hasTagMapping;
        public bool HasTagMapping { get => _hasTagMapping; set => Set(ref _hasTagMapping, value, nameof(HasTagMapping)); }

        // —— Предпросмотр с подсветкой ——
        public ObservableCollection<string> PreviewColumns { get; } = new();
        public ObservableCollection<PreviewRow> PreviewRows { get; } = new();

        // —— Шаблоны (таблица MappingTemplates) ——
        public ObservableCollection<string> TemplateNames { get; } = new();
        private string? _selectedTemplate;
        public string? SelectedTemplate { get => _selectedTemplate; set => Set(ref _selectedTemplate, value, nameof(SelectedTemplate)); }
        private string _templateName = "";
        public string TemplateName { get => _templateName; set => Set(ref _templateName, value, nameof(TemplateName)); }

        // —— Итог ——
        private string _reportText = "Выберите файл Excel.";
        public string ReportText { get => _reportText; set => Set(ref _reportText, value, nameof(ReportText)); }

        public ObservableCollection<ValidationError> Issues { get; } = new();

        // —— Прогресс записи в Access (данные + аудит) ——
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (Set(ref _isBusy, value, nameof(IsBusy)))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    Raise(nameof(ApplyCommand));
                    Raise(nameof(RefreshSheetCommand));
                }
            }
        }

        private int _progressValue;
        public int ProgressValue { get => _progressValue; set => Set(ref _progressValue, value, nameof(ProgressValue)); }

        private int _progressMaximum = 100;
        public int ProgressMaximum { get => _progressMaximum; set => Set(ref _progressMaximum, value, nameof(ProgressMaximum)); }

        private string _progressText = "";
        public string ProgressText { get => _progressText; set => Set(ref _progressText, value, nameof(ProgressText)); }

        // —— Запоминаемый шаблон и кэш листа ——
        private MappingTemplate? _appliedTemplate;
        private IReadOnlyList<string>? _lastHeaders;
        private IReadOnlyList<ExcelRowData>? _lastRows;
        private int _lastHeaderRow = 1;
        private bool _suppressChanged;

        public RelayCommand BrowseFileCommand { get; }
        public RelayCommand CreateTableCommand { get; }
        public RelayCommand SaveTemplateCommand { get; }
        public RelayCommand LoadTemplateCommand { get; }
        public RelayCommand DeleteTemplateCommand { get; }
        public RelayCommand ApplyCommand { get; }

        public event Action? PreviewColumnsChanged;
        public event Action? Imported;

        public ImportViewModel(AppServices services)
        {
            _s = services;
            _selectedTable = _s.CurrentTable;

            foreach (var c in MappingService.AvailableColumns) AccessColumns.Add(c);

            BrowseFileCommand = new RelayCommand(BrowseFile);
            CreateTableCommand = new RelayCommand(CreateTable, () => !string.IsNullOrWhiteSpace(NewTableName));
            SaveTemplateCommand = new RelayCommand(SaveTemplate, () => Mappings.Count > 0);
            LoadTemplateCommand = new RelayCommand(LoadTemplate, () => SelectedTemplate != null);
            DeleteTemplateCommand = new RelayCommand(DeleteTemplate, () => SelectedTemplate != null);
            ApplyCommand = new RelayCommand(() => { _ = ApplyAsync(); },
                () => !IsBusy && Mappings.Any(m => m.IsBound));

            foreach (var t in _s.Creator.GetTableNames().Where(t =>
                         !t.Equals("MappingTemplates", StringComparison.OrdinalIgnoreCase) &&
                         !t.Equals("IO_LIST_Audit", StringComparison.OrdinalIgnoreCase) &&
                         // MODBUS_MAP — не цель импорта Excel (не IO_LIST-структура)
                         !t.Equals(Data.Schema.ModbusMapTable, StringComparison.OrdinalIgnoreCase)))
                Tables.Add(t);
            if (Tables.Count > 0) SelectedTable ??= Tables[0];

            ReloadTemplateNames();
            RefreshSheetCommand = new RelayCommand(() => { _ = LoadSheetDataAsync(); }, () => !IsBusy);
        }

        /// <summary>Команда «Считать»: заголовки вкладки, авто-маппинг, предпросмотр (F2.1/F2.2).
        /// Чтение Excel идёт в фоне (часть C): окно не замораживается на файлах 8 МБ+.</summary>
        public RelayCommand RefreshSheetCommand { get; private set; } = null!;

        /// <summary>Выбор файла Excel; загружаются вкладки (лист).</summary>
        private void BrowseFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;
            OpenFile(dlg.FileName);
        }

        /// <summary>A4: открытие файла — кнопка «Обзор...» и drag&drop в окно импорта.</summary>
        public void OpenFile(string path)
        {
            ExcelPath = path;
            try
            {
                using var reader = new ExcelReader(ExcelPath);
                Sheets.Clear();
                foreach (var s in reader.GetSheetNames()) Sheets.Add(s);
                if (Sheets.Count > 0) SelectedSheet = Sheets[0];
                _ = LoadSheetDataAsync();
            }
            catch (Exception ex)
            {
                _s.Log.Error($"Ошибка открытия Excel: {ex.Message}");
                ReportText = "Ошибка чтения файла: " + ex.Message;
            }
        }

        /// <summary>Считать заголовки, авто-определить маппинг, построить предпросмотр.
        /// Чтение XLWorkbook — в фоне (часть C): 8 МБ+ файлы больше не замораживают окно.</summary>
        public async Task LoadSheetDataAsync()
        {
            if (IsBusy) return;
            if (string.IsNullOrWhiteSpace(ExcelPath) || SelectedSheet == null)
            {
                ReportText = "Сначала выберите файл Excel.";
                return;
            }
            if (!int.TryParse(HeaderRow?.Trim(), out int headerRow) || headerRow < 1)
            {
                ReportText = "Строка начала чтения — целое число ≥ 1 (первая строка — названия колонок).";
                return;
            }

            IsBusy = true;
            ProgressText = "Чтение Excel-файла…";
            try
            {
                string sheet = SelectedSheet;
                string path = ExcelPath;
                // Тяжёлый I/O и разбор книги — в отдельном потоке (окно отзывчиво)
                var (headers, rows) = await Task.Run(() =>
                {
                    using var reader = new ExcelReader(path);
                    return (reader.GetHeaders(sheet, headerRow), reader.ReadRows(sheet, headerRow));
                });
                _lastHeaders = headers;
                _lastHeaderRow = headerRow;
                _lastRows = rows;

                // Авто-определение маппинга; дальнейшая привязка — вручную (Bind в окне)
                var maps = _s.Mapping.AutoDetect(headers);
                Mappings.Clear();
                foreach (var m in maps)
                {
                    var row = new MapRow
                    {
                        ExcelColumn = m.ExcelColumn,
                        ExcelColumnIndex = m.ExcelColumnIndex, // F2.14: позиция для дубликатов заголовков
                        Note = m.Note,                          // F2.14: подсказка о дубликатах
                        AccessColumn = string.IsNullOrEmpty(m.AccessColumn) ? null : m.AccessColumn
                    };
                    row.Changed = () => OnMappingChanged(row);
                    Mappings.Add(row);
                }

                // Шаблон приоритетнее авто-определения: порядок «Загрузить»/«Считать» не важен
                _suppressChanged = true;
                var (applied, total, missing) = ApplyPendingTemplate();
                _suppressChanged = false;

                UpdateTagStatus();
                BuildPreviewCore();

                ReportText = $"Файл: {System.IO.Path.GetFileName(ExcelPath)} | вкладка: {SelectedSheet} | " +
                             $"строка заголовка: {headerRow} | колонок: {headers.Count} | строк данных: {rows.Count}" +
                             (rows.Count > 50 ? " (предпросмотр — первые 50 строк)" : "") +
                             $" | привязано: {Mappings.Count(m => m.IsBound)}" +
                             (total > 0
                                 ? $" | шаблон «{_appliedTemplate?.Name}»: {applied} из {total}" +
                                   (missing.Count > 0
                                       ? $" (нет в листе: {string.Join(", ", missing.Take(5))})"
                                       : "")
                                 : "");
            }
            catch (Exception ex)
            {
                _s.Log.Error($"Ошибка чтения листа: {ex.Message}");
                ReportText = "Ошибка: " + ex.Message;
            }
            finally
            {
                ProgressText = "";
                IsBusy = false;
            }
        }

        /// <summary>Статус привязки обязательного поля Tag (F2.2).</summary>
        private void UpdateTagStatus()
        {
            HasTagMapping = Mappings.Any(m => m.IsTag);
            Raise(nameof(ApplyCommand));
        }

        /// <summary>
        /// Смена привязки пользователем: колонка таблицы уникальна (1:1).
        /// При выборе занятой колонки — перенос (last-wins): у старой строки привязка сбрасывается.
        /// </summary>
        private void OnMappingChanged(MapRow row)
        {
            if (!_suppressChanged && !string.IsNullOrEmpty(row.AccessColumn))
            {
                var dup = Mappings.FirstOrDefault(m => !ReferenceEquals(m, row) &&
                    string.Equals(m.AccessColumn, row.AccessColumn, StringComparison.OrdinalIgnoreCase));
                if (dup != null)
                {
                    string from = dup.ExcelColumn;
                    _suppressChanged = true;
                    dup.AccessColumn = null; // сброс у старого владельца (без рекурсии конфликта)
                    _suppressChanged = false;
                    string msg = $"Колонка «{row.AccessColumn}»: привязка перенесена с «{from}» на «{row.ExcelColumn}»";
                    _s.Log.Warn(msg);
                    ReportText = msg;
                }
            }
            UpdateTagStatus();
            if (!_suppressChanged) RebuildPreviewFromCache();
        }

        /// <summary>Привязать колонку строке, снимая её с других строк (уникальность 1:1).</summary>
        private void BindColumn(MapRow row, string column)
        {
            foreach (var other in Mappings)
                if (!ReferenceEquals(other, row) &&
                    string.Equals(other.AccessColumn, column, StringComparison.OrdinalIgnoreCase))
                    other.AccessColumn = null;
            row.AccessColumn = column;
        }

        /// <summary>Наложение запомненного шаблона поверх авто-маппинга (шаблон приоритетнее).</summary>
        private (int applied, int total, List<string> missing) ApplyPendingTemplate()
        {
            if (_appliedTemplate == null || Mappings.Count == 0)
                return (0, 0, new List<string>());

            int applied = 0;
            var missing = new List<string>();
            foreach (var m in _appliedTemplate.Mappings)
            {
                if (string.IsNullOrWhiteSpace(m.ExcelColumn) || string.IsNullOrEmpty(m.AccessColumn)) continue;
                // F2.14: приоритет — точное совпадение имени И позиции колонки (дубликаты MIN/MIN);
                // старые шаблоны без индекса — резолв по имени (первое вхождение)
                MapRow? row = null;
                if (m.ExcelColumnIndex is int idx)
                    row = Mappings.FirstOrDefault(r => r.ExcelColumnIndex == idx &&
                        string.Equals(r.ExcelColumn.Trim(), m.ExcelColumn.Trim(), StringComparison.OrdinalIgnoreCase));
                row ??= Mappings.FirstOrDefault(r =>
                    string.Equals(r.ExcelColumn.Trim(), m.ExcelColumn.Trim(), StringComparison.OrdinalIgnoreCase));
                if (row == null) { missing.Add(m.ExcelColumn); continue; }
                BindColumn(row, m.AccessColumn); // шаблон вытесняет авто-привязку
                applied++;
            }
            int total = _appliedTemplate.Mappings.Count(m =>
                !string.IsNullOrWhiteSpace(m.ExcelColumn) && !string.IsNullOrEmpty(m.AccessColumn));
            return (applied, total, missing);
        }

        /// <summary>Колонки таблицы, привязанные более одного раза (E008).</summary>
        public static List<string> FindDuplicateBindings(IEnumerable<MapRow> rows) =>
            rows.Where(r => r.IsBound)
                .GroupBy(r => r.AccessColumn!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

        /// <summary>Перестройка предпросмотра из кэша листа (после изменения привязок).</summary>
        private void RebuildPreviewFromCache()
        {
            if (_lastHeaders != null && _lastRows != null) BuildPreviewCore();
        }

        private void ReloadTemplateNames()
        {
            TemplateNames.Clear();
            try
            {
                foreach (var n in _s.Mapping.GetTemplateNames()) TemplateNames.Add(n);
            }
            catch (Exception ex) { _s.Log.Warn($"Шаблоны недоступны: {ex.Message}"); }
        }


        /// <summary>Ядро предпросмотра: работает из кэша (перестройка без повторного чтения файла).</summary>
        private void BuildPreviewCore()
        {
            if (_lastHeaders == null || _lastRows == null || SelectedSheet == null) return;
            IReadOnlyList<string> headers = _lastHeaders;
            int headerRow = _lastHeaderRow;
            IReadOnlyList<ExcelRowData> rows = _lastRows;

            PreviewColumns.Clear();
            PreviewColumns.Add("Строка");
            foreach (var h in headers) PreviewColumns.Add(h);
            PreviewColumnsChanged?.Invoke();
            PreviewRows.Clear();
            Issues.Clear();

            // сопоставление «индекс колонки Excel → целевая колонка таблицы»
            var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
                if (!string.IsNullOrWhiteSpace(headers[i]) && !headerIndex.ContainsKey(headers[i]))
                    headerIndex[headers[i]] = i;
            var targetByIndex = new Dictionary<int, string>();
            foreach (var m in Mappings.Where(x => x.IsBound))
            {
                // F2.14: резолв по позиции колонки (дубликаты заголовков), иначе по имени
                int? idx = ExcelColumns.Resolve(
                    new ColumnMap { ExcelColumn = m.ExcelColumn, ExcelColumnIndex = m.ExcelColumnIndex },
                    headers, headerIndex);
                if (idx.HasValue) targetByIndex[idx.Value] = m.AccessColumn!;
            }

            int shown = Math.Min(rows.Count, 50);

            for (int d = 0; d < shown; d++)
            {
                var row = rows[d];
                int excelRow = headerRow + 1 + d;
                bool separator = row.Cells.All(c => string.IsNullOrWhiteSpace(c) || c.Trim() == "-") &&
                                 row.Cells.Any(c => !string.IsNullOrWhiteSpace(c));
                var cells = new PreviewCell[row.Cells.Count];

                // F2.12: зачёркнутая ячейка Tag — строка целиком не будет импортирована
                int tagIdx = -1;
                foreach (var kv in targetByIndex)
                    if (string.Equals(kv.Value, "Tag", StringComparison.OrdinalIgnoreCase)) { tagIdx = kv.Key; break; }
                bool struckRow = !separator && tagIdx >= 0 && row.IsStruck(tagIdx);

                for (int i = 0; i < row.Cells.Count; i++)
                {
                    string value = row.Cells[i] ?? "";
                    bool struck = row.IsStruck(i);
                    bool error = false;
                    string hint = separator ? "строка-разделитель (не импортируется)" : "";

                    if (struckRow)
                    {
                        // вся строка зачёркнута по Tag — подсветка всех ячеек строки
                        hint = "ячейка Tag зачёркнута — строка не будет импортирована (F2.12)";
                        if (i == tagIdx)
                            Issues.Add(new ValidationError
                            {
                                Code = ErrCode.W005_StruckThroughRow,
                                Field = "Tag",
                                RowNumber = excelRow,
                                Message = $"Строка зачёркнута в Excel — не будет импортирована (Tag={value})"
                            });
                    }
                    else if (!separator && targetByIndex.TryGetValue(i, out string? target))
                    {
                        bool empty = string.IsNullOrWhiteSpace(value) || value.Trim() == "-";

                        if (string.Equals(target, "Tag", StringComparison.OrdinalIgnoreCase))
                        {
                            // E002: ошибка ТОЛЬКО при ПУСТОМ обязательном поле Tag
                            if (empty)
                            {
                                error = true;
                                hint = "пустой обязательный Tag (E002)";
                                Issues.Add(new ValidationError
                                {
                                    Code = ErrCode.E002_EmptyTag,
                                    Field = "Tag",
                                    RowNumber = excelRow,
                                    Message = "Пустое значение обязательного поля Tag"
                                });
                            }
                        }
                        else if (struck)
                        {
                            // F2.12: зачёркнутая ячейка = значение отменено автором → пусто (NULL)
                            hint = "зачёркнута — значение не будет импортировано (NULL, F2.12)";
                        }
                        else if (!empty && Data.Schema.ScaleAlarmColumns.Contains(target) &&
                                 !ValueConverter.IsDecimal(value))
                        {
                            // W001: шкала/уставка не в формате Decimal (F2.3)
                            error = true;
                            hint = "не в формате Decimal (F2.3) — значение сохранится как текст";
                            Issues.Add(new ValidationError
                            {
                                Code = ErrCode.W001_NotNumeric,
                                Field = target,
                                RowNumber = excelRow,
                                Message = $"{target} = «{value}» не в формате Decimal"
                            });
                        }
                    }
                    cells[i] = new PreviewCell { Value = value, IsError = error, IsStruck = struck || struckRow, Hint = hint };
                }
                PreviewRows.Add(new PreviewRow { ExcelRow = excelRow, Cells = cells });
            }
        }

        /// <summary>Создать таблицу с нуля по структуре Б8 (F1.3); имя — любое, по умолчанию IO_LIST.</summary>
        private void CreateTable()
        {
            string name = NewTableName.Trim();
            try
            {
                _s.Creator.CreateDataTable(name);
                _s.Log.Info($"Создана таблица [{name}]: {Data.Schema.DataColumns.Length + 1} колонок + уникальный индекс UX_{name}_Tag");
                if (!Tables.Contains(name)) Tables.Add(name);
                SelectedTable = name;
                ReportText = $"Таблица [{name}] создана и выбрана для записи.";
            }
            catch (Exception ex)
            {
                _s.Log.Error($"Ошибка создания таблицы: {ex.Message}");
                ReportText = "Ошибка создания таблицы: " + ex.Message;
            }
        }

        /// <summary>Сохранить текущий маппинг как шаблон (в таблицу MappingTemplates).</summary>
        private void SaveTemplate()
        {
            string name = string.IsNullOrWhiteSpace(TemplateName)
                ? $"{System.IO.Path.GetFileNameWithoutExtension(ExcelPath)}_{SelectedSheet}"
                : TemplateName.Trim();
            try
            {
                var t = new MappingTemplate
                {
                    Name = name,
                    TableName = SelectedTable ?? "",
                    SheetHint = SelectedSheet ?? "",
                    HeaderRow = int.TryParse(HeaderRow, out int hr) ? hr : 1,
                    Author = AuditService.CurrentUser,
                    Mappings = BoundDistinctMappings()
                };
                _s.Mapping.SaveTemplate(t);
                TemplateName = name;
                ReloadTemplateNames();
                SelectedTemplate = name;
                _s.Log.Info($"Шаблон маппинга сохранён: «{name}»");
                ReportText = $"Шаблон «{name}» сохранён в базе данных.";
            }
            catch (Exception ex) { _s.Log.Error($"Ошибка сохранения шаблона: {ex.Message}"); }
        }

        /// <summary>Загрузить шаблон: запоминается и применяется к листу (порядок с «Считать» не важен).</summary>
        private void LoadTemplate()
        {
            if (SelectedTemplate == null) return;
            try
            {
                var t = _s.Mapping.LoadTemplate(SelectedTemplate);
                if (t == null) { ReportText = "Шаблон не найден."; return; }

                _appliedTemplate = t;
                TemplateName = t.Name;
                HeaderRow = t.HeaderRow.ToString();
                if (Tables.Contains(t.TableName)) SelectedTable = t.TableName;

                if (!string.IsNullOrWhiteSpace(ExcelPath) && SelectedSheet != null)
                {
                    // Файл уже выбран — перечитать лист: авто-маппинг + наложение шаблона + предпросмотр
                    _ = LoadSheetDataAsync();
                }
                else
                {
                    ReportText = $"Шаблон «{t.Name}» загружен. Выберите файл Excel и нажмите «Считать» — привязки будут применены.";
                    _s.Log.Info($"Шаблон загружен (ожидает файла): «{t.Name}»");
                }
                UpdateTagStatus();
            }
            catch (Exception ex) { _s.Log.Error($"Ошибка загрузки шаблона: {ex.Message}"); }
        }

        /// <summary>Привязки без дублей колонок — для сохранения шаблона.</summary>
        private List<ColumnMap> BoundDistinctMappings()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<ColumnMap>();
            foreach (var m in Mappings)
            {
                // F2.14: индекс колонки сохраняется в шаблоне — различие дубликатов заголовков
                if (m.IsBound && seen.Add(m.AccessColumn!))
                    result.Add(new ColumnMap
                    {
                        ExcelColumn = m.ExcelColumn,
                        ExcelColumnIndex = m.ExcelColumnIndex,
                        AccessColumn = m.AccessColumn!
                    });
            }
            return result;
        }

        private void DeleteTemplate()
        {
            if (SelectedTemplate == null) return;
            try
            {
                _s.Mapping.DeleteTemplate(SelectedTemplate);
                _s.Log.Info($"Шаблон удалён: «{SelectedTemplate}»");
                SelectedTemplate = null;
                ReloadTemplateNames();
            }
            catch (Exception ex) { _s.Log.Error($"Ошибка удаления шаблона: {ex.Message}"); }
        }

        /// <summary>Проверки перед импортом; null — можно начинать (E001 Tag, E008 дубли, F2.7).</summary>
        private string? ValidateBeforeApply()
        {
            if (SelectedTable == null || SelectedSheet == null || string.IsNullOrWhiteSpace(ExcelPath))
                return "Выберите файл, вкладку и целевую таблицу.";

            if (!HasTagMapping)
            {
                _s.Log.Error("Apply: нет привязки поля Tag — импорт заблокирован (F2.2)");
                return "Импорт заблокирован: не задана привязка обязательного поля Tag (F2.2).";
            }

            // Уникальность привязок 1:1 — страховка (E008)
            foreach (var old in Issues.Where(i => i.Code == ErrCode.E008_DuplicateBinding).ToList())
                Issues.Remove(old);
            var duplicates = FindDuplicateBindings(Mappings);
            if (duplicates.Count > 0)
            {
                foreach (var col in duplicates)
                {
                    var owners = Mappings.Where(m => m.IsBound &&
                        string.Equals(m.AccessColumn, col, StringComparison.OrdinalIgnoreCase))
                        .Select(m => m.ExcelColumn);
                    Issues.Add(new ValidationError
                    {
                        Code = ErrCode.E008_DuplicateBinding,
                        Field = col,
                        Message = $"Колонка таблицы привязана более одного раза: {string.Join(" + ", owners)}"
                    });
                }
                _s.Log.Error("Apply: колонка таблицы привязана дважды (E008)");
                return "Импорт заблокирован: одна колонка таблицы привязана к двум колонкам Excel (E008).";
            }

            if (!int.TryParse(HeaderRow, out int headerRow) || headerRow < 1)
                return "Строка начала чтения — целое число ≥ 1.";

            return null;
        }

        /// <summary>Импорт в фоновом потоке с прогрессом записи (данные + аудит).</summary>
        private async Task ApplyAsync()
        {
            if (IsBusy) return;
            var problem = ValidateBeforeApply();
            if (problem != null) { ReportText = problem; return; }

            var options = new ImportOptions
            {
                ExcelPath = ExcelPath,
                SheetName = SelectedSheet!,
                HeaderRow = int.Parse(HeaderRow!),
                TableName = SelectedTable!,
                Mode = SelectedMode.StartsWith("Заменить") ? ImportMode.Replace : ImportMode.Upsert,
                WithAudit = ImportWithAudit
            };

            // ===== F2.11: новый документ или замена существующего =====
            if (options.Mode == ImportMode.Upsert)
            {
                var answer = MessageBox.Show(
                    "Импортируется новый документ?\n\n" +
                    "«Да» — новый документ (штатный импорт upsert).\n" +
                    "«Нет» — замена существующих документов (можно выбрать несколько — ревизии/вкладки): отсутствующие в новом файле теги будут помечены к удалению (без физического удаления).\n" +
                    "«Отмена» — не импортировать.",
                    "Импорт: новый документ или замена (F2.11)",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (answer == MessageBoxResult.Cancel)
                {
                    ReportText = "Импорт отменён пользователем.";
                    return;
                }
                if (answer == MessageBoxResult.No)
                {
                    // Список документов взамен — по целевой таблице (EnsureColumns + переключение TableName)
                    _s.UseTable(options.TableName);
                    var docs = _s.Repo.GetDocuments(); // F2.16: документ = файл + вкладка
                    if (docs.Count == 0)
                    {
                        ReportText = "Замена невозможна: в таблице нет документов (REV_FileName пусто).";
                        _s.Log.Warn("Замена документов отменена: в таблице нет документов (REV_FileName пусто).");
                        return;
                    }
                    var dlg = new ReplaceDocumentDialog(docs) { Owner = Application.Current?.MainWindow };
                    if (dlg.ShowDialog() != true)
                    {
                        ReportText = "Импорт отменён: не выбраны заменяемые документы.";
                        return;
                    }
                    // F2.17: мультивыбор — документы делятся по ревизиям/вкладкам
                    options.ReplacedDocuments = new List<(string FileName, string? Sheet)>(dlg.SelectedDocuments);
                    _s.Log.Info($"Замена документов ({options.ReplacedDocuments.Count}): " +
                        string.Join(", ", options.ReplacedDocuments.Select(d =>
                            d.Sheet == null ? $"«{d.FileName}» (без вкладки)" : $"«{d.FileName}» :: «{d.Sheet}»")));
                }
            }

            var maps = Mappings.Where(m => m.IsBound)
                .Select(m => new ColumnMap
                {
                    ExcelColumn = m.ExcelColumn,
                    ExcelColumnIndex = m.ExcelColumnIndex, // F2.14: позиция колонки для резолва при импорте
                    AccessColumn = m.AccessColumn!
                })
                .ToList();

            IsBusy = true;
            ProgressValue = 0;
            ProgressMaximum = 100;
            ProgressText = "Подготовка…";

            try
            {
                // Progress<T> создаётся в UI-потоке — callbacks безопасны для свойств VM
                var progress = new Progress<ImportProgress>(p =>
                {
                    ProgressMaximum = Math.Max(1, p.Total);
                    ProgressValue = p.Processed;
                    ProgressText = $"{p.Phase}: {p.Processed} / {p.Total}" +
                                   $" · добавлено {p.Inserted} · обновлено {p.Updated}" +
                                   (string.IsNullOrEmpty(p.CurrentTag) ? "" : $" · {p.CurrentTag}");
                });

                var report = await Task.Run(() =>
                {
                    _s.UseTable(options.TableName);
                    _s.Creator.EnsureDuplicateTagTable(); // F2.17: гарантия таблицы копий перед записью
                    return _s.Import.Import(options, maps, progress);
                });

                Issues.Clear();
                foreach (var i in report.Validation.Items) Issues.Add(i);
                ReportText = report.Summary +
                             (report.Validation.HasErrors ? " | Подробности — в списке ошибок." : "");
                ProgressText = "Готово: " + report.Summary;
                Imported?.Invoke();
            }
            catch (Exception ex)
            {
                _s.Log.Error($"Импорт прерван: {ex.Message}");
                ReportText = "Ошибка импорта: " + ex.Message;
                ProgressText = "Ошибка: " + ex.Message;
                // Часть C: критическая ошибка импорта — не только текст отчёта, но и явное окно
                MessageBox.Show("Импорт прерван:\n" + ex.Message,
                    "Импорт — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
