using System;
using System.Collections.Generic;

namespace IO_PJT.Models
{
    /// <summary>
    /// Тип системы управления
    /// </summary>
    public enum SystemType
    {
        DCS,
        SIS,
        GDS
    }

    /// <summary>
    /// Точка ввода-вывода (I/O Point) для систем автоматизации.
    /// </summary>
    public class IoPoint
    {
        public Identification? Id { get; set; }
        public Signal? Signal { get; set; }
        public Ranges? Ranges { get; set; }
        public Cable? Cable { get; set; }
        public Controller? Hardware { get; set; }
        public Revision? Rev { get; set; }
    }

    /// <summary>
    /// Идентификация точки ввода-вывода
    /// </summary>
    public class Identification
    {
        public int? Code { get; set; }
        public int? Area { get; set; }
        public string? Title { get; set; }
        public string? Loop { get; set; }
        public string? ILoop { get; set; }
        public string? LoopPc { get; set; }
        public string? Tag { get; set; }
        public string TagPc { get; set; } = string.Empty;
        public string? Service { get; set; }
        public string? InstrumentType { get; set; }
        public string? ServiceEng { get; set; }
        public string? InstrumentTypeEng { get; set; }
        public SystemType? Sys { get; set; }
        public string? IoType { get; set; }
        public string? Controller { get; set; }
        public string? Location { get; set; }
        public string? Pid { get; set; }
    }

    /// <summary>
    /// Технологические параметры сигнала
    /// </summary>
    public class Signal
    {
        public string? Sys { get; set; }
        public SystemType? SysType { get; set; }
        public string? Pid { get; set; }
        public string? Loc { get; set; }
        public string? SigType { get; set; }
        public string? Ex { get; set; }
        public string? SubSys { get; set; }
        public string? Aux { get; set; }
    }

    /// <summary>
    /// Диапазоны измерений и уставки аварийной сигнализации
    /// </summary>
    public class Ranges
    {
        public Range Primary { get; set; } = new();
        public Range? Secondary { get; set; }
        public Alarm? Alarms { get; set; }
        public string? AlarmUnit { get; set; }

        public class Range
        {
            public decimal Min { get; set; } = 0;
            public decimal Max { get; set; } = 100;
            public string? Unit { get; set; }
        }

        public class Alarm
        {
            public decimal? LL { get; set; }
            public decimal? L { get; set; }
            public decimal? H { get; set; }
            public decimal? HH { get; set; }
        }
    }

    /// <summary>
    /// Кабельное хозяйство
    /// </summary>
    public class Cable
    {
        public string? Id { get; set; }
        public string? Desc { get; set; }
        public string? Type { get; set; }
        public string? Desig { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public int? Len { get; set; }
        public string? Color { get; set; }
        public int? Pair { get; set; }
        public string? Note { get; set; }
        public string? TitleAker { get; set; }
        public string? Drum { get; set; }
        public string? Volt { get; set; }
    }

    /// <summary>
    /// Оборудование контроллера
    /// </summary>
    public class Controller
    {
        public string? CableTecon { get; set; }
        public string? Mcc { get; set; }
        public string? CtrlCab { get; set; }
        public string? MarshCab { get; set; }
        public string? Cpu { get; set; }
        public ChassisInfo? Chassis { get; set; }
        public ModuleInfo? Module { get; set; }
    }

    /// <summary>
    /// Шасси (стойка) контроллера
    /// </summary>
    public class ChassisInfo
    {
        public string? Main { get; set; }
        public string? Red { get; set; }
    }

    /// <summary>
    /// Модуль ввода-вывода (I/O Module)
    /// </summary>
    public class ModuleInfo
    {
        public string? Slot { get; set; }
        public string? SlotRed { get; set; }
        public string? Main1 { get; set; }
        public string? Main2 { get; set; }
        public string? Red1 { get; set; }
        public string? Red2 { get; set; }
        public int? Ch { get; set; }
        public string? Type { get; set; }
    }

    /// <summary>
    /// Управление версиями документации
    /// </summary>
    public class Revision
    {
        public int? No { get; set; }
        public string? Desc { get; set; }
        public string? Package { get; set; }
        public string? Field1 { get; set; }
        public string? Field2 { get; set; }
        public string? Doc { get; set; }
        public string? Aker { get; set; }
    }

    /// <summary>
    /// Вспомогательный класс для получения структуры таблицы
    /// </summary>
    public static class TableStructure
    {
        /// <summary>
        /// Возвращает список полей для создания таблицы в Access
        /// </summary>
        public static List<TableField> GetFields()
        {
            return new List<TableField>
            {
                // ===== Identification =====
                new("Code", "INTEGER"),
                new("Area", "INTEGER"),
                new("Title", "TEXT(50)"),
                new("Loop", "TEXT(50)"),
                new("ILoop", "TEXT(50)"),
                new("LoopPc", "TEXT(50)"),
                new("Tag", "TEXT(50)"),
                new("TagPc", "TEXT(50)"),
                new("Service", "TEXT(255)"),
                new("InstrumentType", "TEXT(100)"),
                new("ServiceEng", "TEXT(255)"),
                new("InstrumentTypeEng", "TEXT(100)"),
                new("Sys", "TEXT(20)"),
                new("IoType", "TEXT(20)"),
                new("Controller", "TEXT(50)"),
                new("Location", "TEXT(50)"),
                new("Pid", "TEXT(50)"),

                // ===== Signal =====
                new("Sig_Sys", "TEXT(50)"),
                new("Sig_SysType", "TEXT(20)"),
                new("Sig_Pid", "TEXT(50)"),
                new("Sig_Loc", "TEXT(50)"),
                new("Sig_Type", "TEXT(30)"),
                new("Sig_Ex", "TEXT(20)"),
                new("Sig_SubSys", "TEXT(50)"),
                new("Sig_Aux", "TEXT(50)"),

                // ===== Ranges =====
                new("Range_Min", "DECIMAL(20,6)"),
                new("Range_Max", "DECIMAL(20,6)"),
                new("Range_Unit", "TEXT(20)"),
                new("Range2_Min", "DECIMAL(20,6)"),
                new("Range2_Max", "DECIMAL(20,6)"),
                new("Range2_Unit", "TEXT(20)"),
                new("Alarm_LL", "DECIMAL(20,6)"),
                new("Alarm_L", "DECIMAL(20,6)"),
                new("Alarm_H", "DECIMAL(20,6)"),
                new("Alarm_HH", "DECIMAL(20,6)"),
                new("AlarmUnit", "TEXT(20)"),

                // ===== Cable =====
                new("Cable_Id", "TEXT(50)"),
                new("Cable_Desc", "TEXT(255)"),
                new("Cable_Type", "TEXT(100)"),
                new("Cable_Desig", "TEXT(50)"),
                new("Cable_From", "TEXT(50)"),
                new("Cable_To", "TEXT(50)"),
                new("Cable_Len", "INTEGER"),
                new("Cable_Color", "TEXT(20)"),
                new("Cable_Pair", "INTEGER"),
                new("Cable_Note", "TEXT(255)"),
                new("Cable_TitleAker", "TEXT(50)"),
                new("Cable_Drum", "TEXT(50)"),
                new("Cable_Volt", "TEXT(20)"),

                // ===== Controller =====
                new("Ctrl_CableTecon", "TEXT(50)"),
                new("Ctrl_Mcc", "TEXT(50)"),
                new("Ctrl_CtrlCab", "TEXT(50)"),
                new("Ctrl_MarshCab", "TEXT(50)"),
                new("Ctrl_Cpu", "TEXT(50)"),
                new("Ctrl_ChassisMain", "TEXT(50)"),
                new("Ctrl_ChassisRed", "TEXT(50)"),
                new("Ctrl_Slot", "TEXT(20)"),
                new("Ctrl_SlotRed", "TEXT(20)"),
                new("Ctrl_Main1", "TEXT(20)"),
                new("Ctrl_Main2", "TEXT(20)"),
                new("Ctrl_Red1", "TEXT(20)"),
                new("Ctrl_Red2", "TEXT(20)"),
                new("Ctrl_Channel", "INTEGER"),
                new("Ctrl_ModuleType", "TEXT(20)"),

                // ===== Revision =====
                new("Rev_No", "INTEGER"),
                new("Rev_Desc", "TEXT(255)"),
                new("Rev_Package", "TEXT(50)"),
                new("Rev_Field1", "TEXT(50)"),
                new("Rev_Field2", "TEXT(50)"),
                new("Rev_Doc", "TEXT(255)"),
                new("Rev_Aker", "TEXT(50)"),

                // ===== Primary Key =====
                new("ID", "COUNTER PRIMARY KEY")
            };
        }
    }

    /// <summary>
    /// Поле таблицы
    /// </summary>
    public class TableField
    {
        public string Name { get; set; }
        public string Type { get; set; }

        public TableField(string name, string type)
        {
            Name = name;
            Type = type;
        }
    }
}