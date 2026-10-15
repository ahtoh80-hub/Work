using System;
using System.Collections.Generic;

namespace IO_PJT.Models
{
    /// <summary>
    /// Точка ввода-вывода в системе автоматизации.
    /// Структура — 1:1 с деревом IOclass.md (источник истины).
    /// Все текстовые поля — строки (TEXT(255) в БД, ParsedTag — TEXT(50));
    /// шкалы и уставки хранятся как строки (конвертация — при валидации).
    /// </summary>
    public class IoPoint
    {
        /// <summary>Служебный идентификатор записи в БД (ID, COUNTER).</summary>
        public long Id { get; set; }

        /// <summary>Идентификационные данные точки (включая Logic).</summary>
        public Identification Identification { get; set; } = new();

        /// <summary>Технологические параметры сигнала.</summary>
        public Signal? Signal { get; set; }

        /// <summary>Диапазоны измерений и уставки.</summary>
        public Ranges? Ranges { get; set; }

        /// <summary>Кабельное хозяйство.</summary>
        public Cable? Cable { get; set; }

        /// <summary>Привязка к оборудованию контроллера.</summary>
        public Equipment? Equipment { get; set; }

        /// <summary>F9: данные карты адресации Modbus (Register Table Function 02/03/06).
        /// Заполняются сервисом связывания ModbusLinkService; колонки M_* перед CBL_*.</summary>
        public Modbus? Modbus { get; set; }

        /// <summary>Метаданные версии/аудита записи.</summary>
        public Revision? Revision { get; set; }

        /// <summary>Дополнительные текстовые поля (данные Excel вне типовой структуры).</summary>
        public Extra? Extra { get; set; }

        /// <summary>Уникальный тег сигнала — обязательное поле импорта.</summary>
        public string? Tag => Identification.Tag;
    }

    /// <summary>Идентификация точки (ветвь дерева Identification).</summary>
    public class Identification
    {
        public int? Code { get; set; }              // LONG; автo = REV_RowNumber - 1
        public string? Area { get; set; }           // TEXT(255)
        public string? Plant { get; set; }          // TEXT(255) — установка/площадка (идентичность с System)
        public string? Title { get; set; }
        public string? Loop { get; set; }
        public string? SubLoop { get; set; }
        public string? ProcessLoop { get; set; }
        public string? Logic { get; set; }          // Logic No из Excel
        public string? Tag { get; set; }            // ОБЯЗАТЕЛЬНОЕ поле
        public InstrumentTag? ParsedTag { get; set; } // вычисляется из Tag
        public string? ProcessTag { get; set; }
        public string? System { get; set; }         // произвольная строка: DCS / SIS / GDS / РСУ / ПСБ / …
        public string? IoType { get; set; }
        public string? Controller { get; set; }
        public string? Location { get; set; }
    }

    /// <summary>Распарсенный тег (ParsedTag) — TEXT(50), вычисляется программой из Tag.</summary>
    public class InstrumentTag
    {
        public string? TagArea { get; set; }        // код площадки (1-6 цифр)
        public string? DeviceClass { get; set; }    // класс прибора (1-5 букв)
        public string? LoopNumber { get; set; }     // цифровая часть Loop (1-5 цифр)
        public string? TagNumber { get; set; }      // номер прибора (1-3 цифры)
        public string? Suffix { get; set; }         // суффикс (0-5 символов)
        public string? FullTag { get; set; }        // = Tag
        public string? Separator { get; set; }      // разделитель компонентов
    }

    /// <summary>Технологические параметры сигнала (ветвь Signal, без префиксов в БД).</summary>
    public class Signal
    {
        public string? Service { get; set; }
        public string? ServiceEnglish { get; set; }
        public string? InstrumentType { get; set; }
        public string? InstrumentTypeEnglish { get; set; }
        public string? PID { get; set; }
        public string? SignalType { get; set; }
        public string? ExType { get; set; }         // Ex.d, Ex.i, ... (строка)
        public string? Subsystem { get; set; }
        public string? Auxiliary { get; set; }

        /// <summary>Уровень напряжения питания сигнала (например, 24V, 220V).</summary>
        public string? VoltageLevel { get; set; }

        /// <summary>Мощность потребления контура на данном сигнале (например, 5W).</summary>
        public string? PowerConsumption { get; set; }
    }

    /// <summary>Диапазоны и уставки (ветвь Ranges).</summary>
    public class Ranges
    {
        /// <summary>Основной диапазон (шкала) — значения TEXT.</summary>
        public Range Primary { get; set; } = new();

        /// <summary>Вторичный диапазон (пересчет в другие единицы).</summary>
        public Range? Secondary { get; set; }

        /// <summary>Уставки аварийной сигнализации — TEXT, без единиц измерения.</summary>
        public AlarmsData? Alarms { get; set; }

        /// <summary>Диапазон измерения (шкала). Значения — строки: нечисловое не теряется.</summary>
        public class Range
        {
            public string? Min { get; set; }        // Range_MIN / Range_MIN2
            public string? Max { get; set; }        // Range_MAX / Range_MAX2
            public string? Unit { get; set; }       // Eng_Unit / Eng_Unit2
        }

        /// <summary>Уставки: LL2 &lt; LL &lt; L &lt; H &lt; HH &lt; HH2 (только значения, TEXT).</summary>
        public class AlarmsData
        {
            public string? LL { get; set; }
            public string? LL2 { get; set; }
            public string? L { get; set; }
            public string? H { get; set; }
            public string? HH { get; set; }
            public string? HH2 { get; set; }
        }
    }

    /// <summary>F9: данные карты адресации Modbus (ветвь Modbus, колонки M_* перед CBL_*).
    /// Источник: файлы «IMCS TO DCS Register Table Modbus» (Interface parameters + Register Table
    /// Function 02/03/06). Заполняется автоматическим связыванием ModbusLinkService, не Excel-импортом.</summary>
    public class Modbus
    {
        // —— Interface parameters (станция) ——
        /// <summary>Имя файла карты адресации (напр. «IMCS TO DCS Register Table Modbus - EBSM102.xlsx»).</summary>
        public string? MapFile { get; set; }
        /// <summary>Slave-станция (Slave Station, напр. IMCS EBSM102).</summary>
        public string? SlaveStation { get; set; }
        /// <summary>Slave ID основной (IP main: адрес/маска/порт).</summary>
        public string? SlaveIdMain { get; set; }
        /// <summary>Slave ID резервный (IP backup).</summary>
        public string? SlaveIdBackup { get; set; }
        /// <summary>Скорость обмена (speed; для TCP — 100Mbps).</summary>
        public string? Speed { get; set; }
        /// <summary>Таймаут ответа устройства (timeout, мс).</summary>
        public string? Timeout { get; set; }

        // —— Register Table (строка карты) ——
        /// <summary>Project Functional Designation — функциональное обозначение объекта ПЛК.</summary>
        public string? PrfDesignation { get; set; }
        /// <summary>PLC Tag/Variable Name — полное имя переменной ПЛК (напр. 1115-PM-101A_STA.MCU).</summary>
        public string? PlcTag { get; set; }
        /// <summary>Базовый тег объекта (PLC Tag до первого «_») — ключ связи с тегом РСУ.</summary>
        public string? ObjectTag { get; set; }
        /// <summary>Description of the variable — описание переменной.</summary>
        public string? Description { get; set; }
        /// <summary>Units of measurement — единицы измерения.</summary>
        public string? Units { get; set; }
        /// <summary>Scale — шкала.</summary>
        public string? Scale { get; set; }
        /// <summary>Low Low alarm value.</summary>
        public string? LL { get; set; }
        /// <summary>Low alarm value.</summary>
        public string? LA { get; set; }
        /// <summary>High alarm value.</summary>
        public string? HA { get; set; }
        /// <summary>High High alarm value.</summary>
        public string? HH { get; set; }
        /// <summary>Scaling factors — коэффициенты масштабирования.</summary>
        public string? Scaling { get; set; }
        /// <summary>Signal type (Impulse/Permanent).</summary>
        public string? SigType { get; set; }
        /// <summary>Register type — тип регистра (1/4/6).</summary>
        public string? RegType { get; set; }
        /// <summary>Address/Bit — адрес регистра или бит.</summary>
        public string? Address { get; set; }
        /// <summary>Access type (Read/Write).</summary>
        public string? AccessType { get; set; }
        /// <summary>Data type (BOOL / 16-Bit Unsigned / 32-Bit Float…).</summary>
        public string? DataType { get; set; }
        /// <summary>Function code Modbus (02/03/06).</summary>
        public string? FuncCode { get; set; }
        /// <summary>DCS channel — канал РСУ.</summary>
        public string? DcsChannel { get; set; }
        /// <summary>DCS Tag/Name variable — тег РСУ в карте («Tег РСУ»).</summary>
        public string? DcsTag { get; set; }
        /// <summary>DCS functions — функции РСУ.</summary>
        public string? DcsFunctions { get; set; }
        /// <summary>Примечание (note).</summary>
        public string? Note { get; set; }
    }

    /// <summary>Кабельное хозяйство (ветвь Cable, префикс CBL_).</summary>
    public class Cable
    {
        public string? Id { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; }
        public string? Designation { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public int? Length { get; set; }            // LONG
        public string? Color { get; set; }
        public int? Pair { get; set; }              // LONG
        public string? Note { get; set; }
        public string? VendorDesignation { get; set; }
        public string? Drum { get; set; }
        public string? Voltage { get; set; }
    }

    /// <summary>Оборудование контроллера (ветвь Equipment, префикс EQP_).</summary>
    public class Equipment
    {
        public string? CableTecon { get; set; }
        public string? Mcc { get; set; }
        public string? ControlCabinet { get; set; }
        public string? MarshallingCabinet { get; set; }
        public string? CPU { get; set; }
        public Chassis? Chassis { get; set; }
        public Module? Module { get; set; }
    }

    /// <summary>Шасси (основное/резервное).</summary>
    public class Chassis
    {
        public string? Main { get; set; }
        public string? Redundant { get; set; }
    }

    /// <summary>Модуль ввода-вывода. Квадрирование: ModuleRedundant и Module2Redundant — разное оборудование.</summary>
    public class Module
    {
        public string? SlotMain { get; set; }
        public string? SlotRedundant { get; set; }
        public string? SlotMain2 { get; set; }
        public string? SlotRedundant2 { get; set; }
        public string? ModuleMain { get; set; }
        public string? ModuleRedundant { get; set; }
        public string? ModuleMain2 { get; set; }
        public string? Module2Redundant { get; set; }
        public int? Channel { get; set; }           // LONG
        public string? Type { get; set; }
    }

    /// <summary>Метаданные записи (ветвь Revision, префикс REV_) — аудит и трассировка.</summary>
    public class Revision
    {
        public int? Number { get; set; }            // LONG
        public string? Description { get; set; }
        public string? Package { get; set; }
        public string? VendorField1 { get; set; }
        public string? VendorField2 { get; set; }
        public string? Document { get; set; }
        public string? AkerRevision { get; set; }
        public string? FileName { get; set; }       // автo: имя открытого Excel
        public string? Sheet { get; set; }          // авто: имя вкладки (листа)-источника (F2.16)
        public string? Author { get; set; }         // автo: Windows-логин (домен\логин)
        public DateTime? DateEntered { get; set; }  // автo: дата внесения в БД
        public int? RowNumber { get; set; }         // автo: номер строки Excel, где находится Tag
        public string? Type { get; set; }
        public string? Count { get; set; }          // ИД записи из Excel (TEXT)
        public DateTime? DateModified { get; set; } // автo: дата изменения записи
    }

    /// <summary>Дополнительные поля column1...column10 — данные Excel вне типовой структуры.</summary>
    public class Extra
    {
        public string? Column1 { get; set; }
        public string? Column2 { get; set; }
        public string? Column3 { get; set; }
        public string? Column4 { get; set; }
        public string? Column5 { get; set; }
        public string? Column6 { get; set; }
        public string? Column7 { get; set; }
        public string? Column8 { get; set; }
        public string? Column9 { get; set; }
        public string? Column10 { get; set; }

        /// <summary>Доступ по номеру поля 1..10 (индексатор — требование Задания п.6).</summary>
        public string? this[int number]
        {
            get => number switch
            {
                1 => Column1, 2 => Column2, 3 => Column3, 4 => Column4, 5 => Column5,
                6 => Column6, 7 => Column7, 8 => Column8, 9 => Column9, 10 => Column10,
                _ => throw new ArgumentOutOfRangeException(nameof(number), "Допустимы поля 1..10")
            };
            set
            {
                switch (number)
                {
                    case 1: Column1 = value; break;
                    case 2: Column2 = value; break;
                    case 3: Column3 = value; break;
                    case 4: Column4 = value; break;
                    case 5: Column5 = value; break;
                    case 6: Column6 = value; break;
                    case 7: Column7 = value; break;
                    case 8: Column8 = value; break;
                    case 9: Column9 = value; break;
                    case 10: Column10 = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(number), "Допустимы поля 1..10");
                }
            }
        }
    }
}
