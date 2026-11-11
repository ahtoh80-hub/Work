using System.Collections.Generic;
using System.Collections.ObjectModel;

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IO_PJT.Models
{
    /// <summary>F12: строка перечня технологических объектов SCADA (20 колонок A..T
    /// по образцу «ТехОбъекты_простой.xls»; спецификация — «Генерация технологических объектов Modbus.md»).</summary>
    public sealed class ScadaTechObjectRow
    {
        /// <summary>№ — не заполняется.</summary>
        public string No { get; set; } = "";
        /// <summary>Статус — не заполняется.</summary>
        public string Status { get; set; } = "";
        /// <summary>Mode (Раздел) — константа, по умолчанию TEHOBJ.</summary>
        public string Mode { get; set; } = "TEHOBJ";
        /// <summary>MARKA (Марка) — из выбранного поля, всегда начинается с «_», «-» → «_».</summary>
        public string Marka { get; set; } = "";
        /// <summary>OBJTYPE (Тип объекта) — M_DataType через таблицу соответствия SCADA-типам.</summary>
        public string ObjType { get; set; } = "";
        /// <summary>NAME (Наименование) — по умолчанию Service.</summary>
        public string Name { get; set; } = "";
        /// <summary>DISC (Описание) — по умолчанию Loop.</summary>
        public string Disc { get; set; } = "";
        /// <summary>OBJSIGN (Подпись) — не заполняется.</summary>
        public string ObjSign { get; set; } = "";
        /// <summary>OBJNUMBER (Номер) — не заполняется.</summary>
        public string ObjNumber { get; set; } = "";
        /// <summary>PLC переменная — не заполняется.</summary>
        public string PlcVarName { get; set; } = "";
        /// <summary>ARH_PER (Период архив) — константа 0.</summary>
        public string ArhPer { get; set; } = "0";
        /// <summary>KKS — по умолчанию Tag.</summary>
        public string Kks { get; set; } = "";
        /// <summary>OBJDPARAM (Доп.параметр) — краткий адрес Modbus по шаблону, ≤50 символов.</summary>
        public string ObjDParam { get; set; } = "";
        /// <summary>SREZCONTROL (Маска упр. в срезах) — константа 1 (по образцу).</summary>
        public string SrezControl { get; set; } = "1";
        /// <summary>USERGROUP (Классификатор) — заполняется вручную.</summary>
        public string UserGroup { get; set; } = "";
        /// <summary>EVGROUP (Группа событий) — заполняется вручную.</summary>
        public string EvGroup { get; set; } = "";
        /// <summary>PLCNAME (КОНТРОЛЛЕР) — по умолчанию Controller (FCS).</summary>
        public string PlcName { get; set; } = "";
        /// <summary>PLC_ADRESS (Адрес) — константа 0.</summary>
        public string PlcAdress { get; set; } = "0";
        /// <summary>PLC_GR (№ ресурса или группа) — по умолчанию 1, можно менять вручную.</summary>
        public string PlcGr { get; set; } = "1";
        /// <summary>TEMPLATE (Шаблон) — всегда «простой».</summary>
        public string Template { get; set; } = "простой";
        /// <summary>F27: Tag точки-источника IO_LIST (служебное поле для «Записать Марку в IO_LIST»;
        /// в Excel не экспортируется).</summary>
        public string SourceTag { get; set; } = "";
    }

    /// <summary>F12: соответствие типа данных IO_LIST (колонка M_DataType карты Modbus)
    /// и типа данных SCADA (BOOL/INT/WORD/UDINT/REAL). Хранится в таблице SCADA_TYPE_MAP.</summary>
    public sealed class ScadaTypeMapping
    {
        public long Id { get; set; }

        /// <summary>Тип в IO List (например «32-Bit Floating»; сравнение IgnoreCase).</summary>
        public string Source { get; set; } = "";

        /// <summary>Тип в SCADA (BOOL/INT/WORD/UINT/UDINT/REAL — см. 2.5.1 спецификации).</summary>
        public string Target { get; set; } = "";
    }

    /// <summary>F12: настройки генерации перечня (поля-источники + константы).
    /// Три формы значения — единое правило для всех полей: (1) имя колонки IO_LIST (Tag/Loop/Service/…)
    /// → её значение точки; (2) структура с подстановками {ИмяКолонки} (как у Доп.параметра,
    /// напр. «{Service} ({Loop})») → составное значение из полей; (3) любой другой текст → константа.
    /// Персистятся в SCADA_TEHOBJ_SETTINGS (Name/Value).</summary>
    public sealed class ScadaGenSettings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnChanged(name);
            return true;
        }

        // —— поля-источники (или константы) ——
        /// <summary>Источник MARKA: Tag (по умолчанию) / Loop / любое поле / константа.</summary>
        private string _markaField = "Tag";         public string MarkaField { get => _markaField; set => Set(ref _markaField, value ?? ""); }

        /// <summary>Источник NAME (Наименование): Service.</summary>
        private string _nameField = "Service";         public string NameField { get => _nameField; set => Set(ref _nameField, value ?? ""); }

        /// <summary>Источник DISC (Описание): Loop.</summary>
        private string _discField = "Loop";         public string DiscField { get => _discField; set => Set(ref _discField, value ?? ""); }

        /// <summary>Источник KKS: Tag.</summary>
        private string _kksField = "Tag";         public string KksField { get => _kksField; set => Set(ref _kksField, value ?? ""); }

        /// <summary>Источник PLCNAME (КОНТРОЛЛЕР): Controller (FCS).</summary>
        private string _controllerField = "Controller";         public string ControllerField { get => _controllerField; set => Set(ref _controllerField, value ?? ""); }

        /// <summary>Шаблон OBJDPARAM (Доп.параметр) — подстановки {ИмяКолонки} из IO_LIST/M_*,
        /// результат обрезается до 50 символов (спецификация: «не более 50 символов»);
        /// обрезка — специфика только этого поля, остальные поля со структурами не усекаются.</summary>
        private string _dparamTemplate = "FC{M_FuncCode} {M_RegType}{M_Address} {M_SlaveStation}";         public string DParamTemplate { get => _dparamTemplate; set => Set(ref _dparamTemplate, value ?? ""); }

        // —— константы ——
        /// <summary>Mode (Раздел): TEHOBJ.</summary>
        private string _section = "TEHOBJ";         public string Section { get => _section; set => Set(ref _section, value ?? ""); }

        /// <summary>ARH_PER (Период архив): 0.</summary>
        private string _arhPer = "0";
        public string ArhPer { get => _arhPer; set => Set(ref _arhPer, value ?? ""); }

        /// <summary>SREZCONTROL (Маска упр. в срезах): 1 (по образцу).</summary>
        private string _srezControl = "1";
        public string SrezControl { get => _srezControl; set => Set(ref _srezControl, value ?? ""); }

        /// <summary>USERGROUP (Классификатор): по умолчанию «[Все]\[Все технологические]» (спецификация).</summary>
        public string UserGroup { get; set; } = "[Все]\\[Все технологические]";

        /// <summary>EVGROUP (Группа событий): заполняется вручную.</summary>
        private string _evGroup = "";
        public string EvGroup { get => _evGroup; set => Set(ref _evGroup, value ?? ""); }

        /// <summary>PLC_ADRESS (Адрес): 0.</summary>
        private string _plcAdress = "0";         public string PlcAdress { get => _plcAdress; set => Set(ref _plcAdress, value ?? ""); }         /// <summary>PLC_GR (№ ресурса или группа): 1.</summary>
        private string _plcGr = "1";         public string PlcGr { get => _plcGr; set => Set(ref _plcGr, value ?? ""); }         /// <summary>TEMPLATE (Шаблон): простой.</summary>
        private string _template = "простой";         public string Template { get => _template; set => Set(ref _template, value ?? ""); }

        /// <summary>F68: набираемые условия фильтра по столбцам источника IO_LIST (отбор
        /// точек-источников до построения строки перечня; хранятся в профиле настроек —
        /// ключи Filter:*). устые Field/Text — условие неактивно.</summary>
        public ObservableCollection<ScadaFilterCondition> FilterConditions { get; } = new();
    }

    /// <summary>F12: результат построения перечня (строки + статистика/предупреждения).</summary>
    public sealed class ScadaBuildResult
    {
        public List<ScadaTechObjectRow> Rows { get; } = new();

        /// <summary>Точек с заполненной адресацией Modbus (M_PlcTag) — кандидаты в перечень.</summary>
        public int WithAddressing { get; set; }

        /// <summary>Точек пропущено (нет адресации Modbus).</summary>
        public int SkippedNoAddressing { get; set; }

        /// <summary>F68: точек отброшено набираемым фильтром по столбцам источника.</summary>
        public int SkippedByFilter { get; set; }

        /// <summary>Типов M_DataType без соответствия в таблице SCADA_TYPE_MAP (оставлен исходный тип).</summary>
        public int UnmappedTypes { get; set; }

        /// <summary>Строк с обрезанным Доп.параметром (было &gt;50 символов).</summary>
        public int TruncatedDParam { get; set; }

        /// <summary>Предупреждения (пустой Tag/Марка, неизвестный плейсхолдер шаблона, …).</summary>
        public List<string> Warnings { get; } = new();
    }

    /// <summary>F68: условие набираемого фильтра по столбцу источника IO_LIST — точка попадает
    /// в перечень, если Text входит в значение столбца Field (substring, регистр не важен);
    /// пустые Field/Text — условие неактивно. INPC — TextBox/ComboBox в окне редактируют
    /// условие «на лету» (автоперестроение превью в VM).</summary>
    public sealed class ScadaFilterCondition : INotifyPropertyChanged
    {
        private string _field = "";
        private string _text = "";

        /// <summary>Столбец-источник (имя FieldRegistry; ввод набираемый).</summary>
        public string Field
        {
            get => _field;
            set
            {
                if (_field == value) return;
                _field = value ?? "";
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Field)));
            }
        }

        /// <summary>Набираемая подстрока (регистр не важен).</summary>
        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value ?? "";
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
