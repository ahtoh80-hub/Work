using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IO_PJT.Models
{
    /// <summary>F65: настройки окна «Генерация экземпляров типовых алгоритмов»
    /// (SCADA_ALGO_SETTINGS). Источник — строки IO_LIST, сгруппированные по Loop;
    /// экземпляр = один контур Loop; шаблон — BufScadaPOUS с одним OnePOU.</summary>
    public sealed class AlgoGenSettings : INotifyPropertyChanged
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

        /// <summary>F65: ключи SCADA_ALGO_SETTINGS (полная замена при сохранении).</summary>
        public static readonly string[] SettingKeys =
        {
            "InstancesPerFile", "FilePattern", "IdStride", "OutDir", "TemplateName"
        };

        private int _instancesPerFile = 10;
        /// <summary>F65: сколько экземпляров (OnePOU) помещается в один XML-файл;
        /// превышение — следующий файл, нумерация продолжается. Минимум 1.</summary>
        public int InstancesPerFile
        {
            get => _instancesPerFile;
            set => Set(ref _instancesPerFile, value < 1 ? 1 : value);
        }

        private string _filePattern = "{Template}_{Seq}";
        /// <summary>F65: шаблон имени файла: {Template} — имя шаблона, {Seq} — № файла
        /// (с 1). Пусто/битый — «{Template}_{Seq}».</summary>
        public string FilePattern
        {
            get => _filePattern;
            set => Set(ref _filePattern, value ?? "");
        }

        private int _idStride = 10000;
        /// <summary>F65: шаг сдвига T11ID/cardId между экземплярами в одном файле
        /// (ID/POUNum/GroupID OnePOU тоже уникальны). Минимум 100.</summary>
        public int IdStride
        {
            get => _idStride;
            set => Set(ref _idStride, value < 100 ? 100 : value);
        }

        private string _outDir = "";
        /// <summary>F65: папка для сгенерированных файлов; пусто — выбирать при экспорте.</summary>
        public string OutDir
        {
            get => _outDir;
            set => Set(ref _outDir, value ?? "");
        }

        private string _templateName = "";
        /// <summary>F65: имя текущего шаблона в редакторе (SCADA_ALGO_TEMPLATES).</summary>
        public string TemplateName
        {
            get => _templateName;
            set => Set(ref _templateName, value ?? "");
        }

        /// <summary>F65: копия настроек (экземпляр окна не портит загруженные значения).</summary>
        public AlgoGenSettings Clone() => new()
        {
            InstancesPerFile = InstancesPerFile,
            FilePattern = FilePattern,
            IdStride = IdStride,
            OutDir = OutDir,
            TemplateName = TemplateName
        };
    }

    /// <summary>F65: привязка одного сигнала шаблона (Info блока / имя функционального блока)
    /// к строке TYPNO: марка для контура = формула(Mask, Suffix) + Constructor.</summary>
    public sealed class AlgoBinding : INotifyPropertyChanged
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

        private string _signal = "";
        /// <summary>Имя переменной/блока шаблона (Block/@Info) — заменяется на марку.</summary>
        public string Signal
        {
            get => _signal;
            set => Set(ref _signal, value ?? "");
        }

        private string _mask = "";
        /// <summary>Маска строки TYPNO («*» — без маски).</summary>
        public string Mask
        {
            get => _mask;
            set => Set(ref _mask, value ?? "");
        }

        private string _suffix = "";
        /// <summary>Суффикс строки TYPNO (приклеивается к марке).</summary>
        public string Suffix
        {
            get => _suffix;
            set => Set(ref _suffix, value ?? "");
        }

        private string _objType = "";
        /// <summary>Тип объекта строки TYPNO (KLPD_v2, DD_v3, …) — справочно/фильтр.</summary>
        public string ObjType
        {
            get => _objType;
            set => Set(ref _objType, value ?? "");
        }

        private string _constructor = "";
        /// <summary>F65: конструктор марки (как Доп.параметр F12): константа + структуры
        /// {ИмяКолонки} по первой строке контура — приклеивается к марке.</summary>
        public string Constructor
        {
            get => _constructor;
            set => Set(ref _constructor, value ?? "");
        }

        private string _markPreview = "";
        /// <summary>F65: предпросмотр марки для первого попавшегося Loop (read-only).</summary>
        public string MarkPreview
        {
            get => _markPreview;
            set => Set(ref _markPreview, value ?? "");
        }
    }

    /// <summary>F65: контур Loop (экземпляр) — строки IO_LIST с одним Loop,
    /// сгруппированные в один OnePOU логической схемы.</summary>
    public sealed class AlgoLoopInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        /// <summary>Имя контура (IO_LIST Loop) — марка экземпляра и имя POU.</summary>
        public string Loop { get; set; } = "";

        /// <summary>Typno контура (первая непустая строка группы; смешанный — предупреждение).</summary>
        public string Typno { get; set; } = "";

        /// <summary>Число сигналов (строк IO_LIST) в контуре.</summary>
        public int TagCount { get; set; }

        /// <summary>F65: первая строка контура — источник колонок для конструктора марки.</summary>
        public IoPoint FirstRow { get; set; } = null!;

        private bool _generate = true;
        /// <summary>F65: галочка «Генерировать» в гриде контуров.</summary>
        public bool Generate
        {
            get => _generate;
            set { if (Set(ref _generate, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenerateText))); }
        }

        /// <summary>Текст галочки для привязки DataGridCheckBoxColumn.</summary>
        public string GenerateText => Generate ? "да" : "нет";

        private string _fileName = "";
        /// <summary>F65: имя файла, в который попадёт экземпляр (заполняется генерацией).</summary>
        public string FileName
        {
            get => _fileName;
            set => Set(ref _fileName, value ?? "");
        }
    }

    /// <summary>F65: строка таблицы описаний TYPNO (Typno → объект с маской/суффиксом).</summary>
    public sealed class AlgoTypnoRow
    {
        /// <summary>№ (колонка «№» таблицы TYPNO).</summary>
        public string No { get; set; } = "";

        /// <summary>Typno (ключ соответствия IO_LIST.Typno).</summary>
        public string Typno { get; set; } = "";

        /// <summary>Маска («*» — без маски в марке).</summary>
        public string Mask { get; set; } = "";

        /// <summary>Суффикс (приклеивается к марке).</summary>
        public string Suffix { get; set; } = "";

        /// <summary>Тип объекта (KLPD_v2, DD_v3, AN_v1, …).</summary>
        public string ObjType { get; set; } = "";

        /// <summary>Описание Typno.</summary>
        public string TypnoDesc { get; set; } = "";

        /// <summary>Имя XML-шаблона логической схемы (KLPD0001_v1, DD_MOS, AN, …).</summary>
        public string Template { get; set; } = "";

        /// <summary>Описание шаблона.</summary>
        public string TemplateDesc { get; set; } = "";

        /// <summary>Подпись (Display).</summary>
        public string Sign { get; set; } = "";

        /// <summary>Представление в ComboBox привязок.</summary>
        public override string ToString() =>
            $"{Typno} | маска {Mask} | суф. {Suffix} | {ObjType}";
    }

    /// <summary>F65: именованный XML-шаблон логической схемы (SCADA_ALGO_TEMPLATES).</summary>
    public sealed class AlgoTemplate
    {
        /// <summary>Имя шаблона (ключ TYPNO.Шаблон).</summary>
        public string Name { get; set; } = "";

        /// <summary>XML-тело (BufScadaPOUS с одним OnePOU).</summary>
        public string Body { get; set; } = "";

        /// <summary>Путь файла-источника (справочно; «Обновить из файла» перечитывает).</summary>
        public string FilePath { get; set; } = "";

        /// <summary>Для списка шаблонов в окне.</summary>
        public override string ToString() => Name;
    }

    /// <summary>F65: результат генерации экземпляров — файлы + статистика.</summary>
    public sealed class AlgoGenResult
    {
        /// <summary>Сгенерированные файлы (имя → XML-текст BufScadaPOUS).</summary>
        public List<(string FileName, string Xml)> Files { get; } = new();

        /// <summary>Контуров к генерации.</summary>
        public int LoopsTotal { get; set; }

        /// <summary>Экземпляров (OnePOU) сгенерировано.</summary>
        public int Instances { get; set; }

        /// <summary>Контуров пропущено (нет Typno / нет строк TYPNO / не выбраны).</summary>
        public int Skipped { get; set; }

        /// <summary>Предупреждения (смешанный Typno, пустые замены, …).</summary>
        public List<string> Warnings { get; } = new();
    }
}
