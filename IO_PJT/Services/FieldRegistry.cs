using System;
using System.Collections.Generic;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>Описание одной колонки таблицы: чтение/запись свойства модели IoPoint.</summary>
    public sealed class FieldDef
    {
        public string Column { get; }
        public Func<IoPoint, object?> Get { get; }
        public Action<IoPoint, object?> Set { get; }

        public FieldDef(string column, Func<IoPoint, object?> get, Action<IoPoint, object?> set)
        {
            Column = column;
            Get = get;
            Set = set;
        }
    }

    /// <summary>
    /// Реестр 126 колонок данных (без ID): колонка Access ↔ вложенное свойство IoPoint.
    /// Единственное место знания о соответствии «дерево IOclass ↔ плоская таблица».
    /// </summary>
    public static class FieldRegistry
    {
        /// <summary>Приведение значения OleDb к строке (NULL/DBNull → null).</summary>
        private static string? S(object? v) => v == null || v is DBNull ? null : Convert.ToString(v);

        /// <summary>Приведение значения к int? (строка с запятой, double — допустимы).</summary>
        private static int? I(object? v)
        {
            if (v == null || v is DBNull) return null;
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is double d) return (int)Math.Round(d);
            var s = Convert.ToString(v)?.Replace('.', ',').Trim();
            return int.TryParse(s, out var r) ? r : null;
        }

        /// <summary>Приведение значения к DateTime?.</summary>
        private static DateTime? DT(object? v)
        {
            if (v == null || v is DBNull) return null;
            if (v is DateTime dt) return dt;
            return DateTime.TryParse(Convert.ToString(v), out var r) ? r : null;
        }

        // —— Короткие фабрики определений колонок ——
        private static FieldDef FS(string c, Func<IoPoint, string?> g, Action<IoPoint, string?> s) =>
            new(c, p => g(p), (p, v) => s(p, S(v)));
        private static FieldDef FI(string c, Func<IoPoint, int?> g, Action<IoPoint, int?> s) =>
            new(c, p => g(p), (p, v) => s(p, I(v)));
        private static FieldDef FDT(string c, Func<IoPoint, DateTime?> g, Action<IoPoint, DateTime?> s) =>
            new(c, p => g(p), (p, v) => s(p, DT(v)));

        public static readonly FieldDef[] Fields =
        {
            // ===== Identification (13) =====
            FI("Code", p => p.Identification.Code, (p, v) => p.Identification.Code = v),
            FS("Area", p => p.Identification.Area, (p, v) => p.Identification.Area = v),
            FS("Plant", p => p.Identification.Plant, (p, v) => p.Identification.Plant = v),
            FS("Title", p => p.Identification.Title, (p, v) => p.Identification.Title = v),
            FS("Loop", p => p.Identification.Loop, (p, v) => p.Identification.Loop = v),
            FS("SubLoop", p => p.Identification.SubLoop, (p, v) => p.Identification.SubLoop = v),
            FS("ProcessLoop", p => p.Identification.ProcessLoop, (p, v) => p.Identification.ProcessLoop = v),
            FS("Logic", p => p.Identification.Logic, (p, v) => p.Identification.Logic = v),
            FS("Tag", p => p.Identification.Tag, (p, v) => p.Identification.Tag = v),
            FS("ProcessTag", p => p.Identification.ProcessTag, (p, v) => p.Identification.ProcessTag = v),
            FS("System", p => p.Identification.System, (p, v) => p.Identification.System = v),
            FS("IoType", p => p.Identification.IoType, (p, v) => p.Identification.IoType = v),
            FS("Controller", p => p.Identification.Controller, (p, v) => p.Identification.Controller = v),
            FS("Location", p => p.Identification.Location, (p, v) => p.Identification.Location = v),

            // ===== ParsedTag (7) — TEXT(50), вычисляется из Tag =====
            FS("PTag_TagArea", p => p.Identification.ParsedTag?.TagArea, (p, v) => (p.Identification.ParsedTag ??= new()).TagArea = v),
            FS("PTag_DeviceClass", p => p.Identification.ParsedTag?.DeviceClass, (p, v) => (p.Identification.ParsedTag ??= new()).DeviceClass = v),
            FS("PTag_LoopNumber", p => p.Identification.ParsedTag?.LoopNumber, (p, v) => (p.Identification.ParsedTag ??= new()).LoopNumber = v),
            FS("PTag_TagNumber", p => p.Identification.ParsedTag?.TagNumber, (p, v) => (p.Identification.ParsedTag ??= new()).TagNumber = v),
            FS("PTag_Suffix", p => p.Identification.ParsedTag?.Suffix, (p, v) => (p.Identification.ParsedTag ??= new()).Suffix = v),
            FS("PTag_FullTag", p => p.Identification.ParsedTag?.FullTag, (p, v) => (p.Identification.ParsedTag ??= new()).FullTag = v),
            FS("PTag_Separator", p => p.Identification.ParsedTag?.Separator, (p, v) => (p.Identification.ParsedTag ??= new()).Separator = v),

            // ===== Signal (9) =====
            FS("Service", p => p.Signal?.Service, (p, v) => (p.Signal ??= new()).Service = v),
            FS("ServiceEnglish", p => p.Signal?.ServiceEnglish, (p, v) => (p.Signal ??= new()).ServiceEnglish = v),
            FS("InstrumentType", p => p.Signal?.InstrumentType, (p, v) => (p.Signal ??= new()).InstrumentType = v),
            FS("InstrumentTypeEnglish", p => p.Signal?.InstrumentTypeEnglish, (p, v) => (p.Signal ??= new()).InstrumentTypeEnglish = v),
            FS("PID", p => p.Signal?.PID, (p, v) => (p.Signal ??= new()).PID = v),
            FS("SignalType", p => p.Signal?.SignalType, (p, v) => (p.Signal ??= new()).SignalType = v),
            FS("ExType", p => p.Signal?.ExType, (p, v) => (p.Signal ??= new()).ExType = v),
            FS("Subsystem", p => p.Signal?.Subsystem, (p, v) => (p.Signal ??= new()).Subsystem = v),
            FS("Auxiliary", p => p.Signal?.Auxiliary, (p, v) => (p.Signal ??= new()).Auxiliary = v),
            FS("VoltageLevel", p => p.Signal?.VoltageLevel, (p, v) => (p.Signal ??= new()).VoltageLevel = v),
            FS("PowerConsumption", p => p.Signal?.PowerConsumption, (p, v) => (p.Signal ??= new()).PowerConsumption = v),

            // ===== Ranges: шкалы (6) — TEXT =====
            FS("Range_MIN", p => p.Ranges?.Primary.Min, (p, v) => (p.Ranges ??= new()).Primary.Min = v),
            FS("Range_MAX", p => p.Ranges?.Primary.Max, (p, v) => (p.Ranges ??= new()).Primary.Max = v),
            FS("Eng_Unit", p => p.Ranges?.Primary.Unit, (p, v) => (p.Ranges ??= new()).Primary.Unit = v),
            FS("Range_MIN2", p => p.Ranges?.Secondary?.Min, (p, v) => ((p.Ranges ??= new()).Secondary ??= new()).Min = v),
            FS("Range_MAX2", p => p.Ranges?.Secondary?.Max, (p, v) => ((p.Ranges ??= new()).Secondary ??= new()).Max = v),
            FS("Eng_Unit2", p => p.Ranges?.Secondary?.Unit, (p, v) => ((p.Ranges ??= new()).Secondary ??= new()).Unit = v),

            // ===== Alarms (6) — TEXT, без единиц =====
            FS("LL", p => p.Ranges?.Alarms?.LL, (p, v) => ((p.Ranges ??= new()).Alarms ??= new()).LL = v),
            FS("LL2", p => p.Ranges?.Alarms?.LL2, (p, v) => ((p.Ranges ??= new()).Alarms ??= new()).LL2 = v),
            FS("L", p => p.Ranges?.Alarms?.L, (p, v) => ((p.Ranges ??= new()).Alarms ??= new()).L = v),
            FS("H", p => p.Ranges?.Alarms?.H, (p, v) => ((p.Ranges ??= new()).Alarms ??= new()).H = v),
            FS("HH", p => p.Ranges?.Alarms?.HH, (p, v) => ((p.Ranges ??= new()).Alarms ??= new()).HH = v),
            FS("HH2", p => p.Ranges?.Alarms?.HH2, (p, v) => ((p.Ranges ??= new()).Alarms ??= new()).HH2 = v),

            // ===== Modbus (27) — F9: данные карты адресации Modbus, перед CBL_* =====
            FS("M_MapFile", p => p.Modbus?.MapFile, (p, v) => (p.Modbus ??= new()).MapFile = v),
            FS("M_SlaveStation", p => p.Modbus?.SlaveStation, (p, v) => (p.Modbus ??= new()).SlaveStation = v),
            FS("M_SlaveIdMain", p => p.Modbus?.SlaveIdMain, (p, v) => (p.Modbus ??= new()).SlaveIdMain = v),
            FS("M_SlaveIdBackup", p => p.Modbus?.SlaveIdBackup, (p, v) => (p.Modbus ??= new()).SlaveIdBackup = v),
            FS("M_Speed", p => p.Modbus?.Speed, (p, v) => (p.Modbus ??= new()).Speed = v),
            FS("M_Timeout", p => p.Modbus?.Timeout, (p, v) => (p.Modbus ??= new()).Timeout = v),
            FS("M_PrfDesignation", p => p.Modbus?.PrfDesignation, (p, v) => (p.Modbus ??= new()).PrfDesignation = v),
            FS("M_PlcTag", p => p.Modbus?.PlcTag, (p, v) => (p.Modbus ??= new()).PlcTag = v),
            FS("M_ObjectTag", p => p.Modbus?.ObjectTag, (p, v) => (p.Modbus ??= new()).ObjectTag = v),
            FS("M_Description", p => p.Modbus?.Description, (p, v) => (p.Modbus ??= new()).Description = v),
            FS("M_Units", p => p.Modbus?.Units, (p, v) => (p.Modbus ??= new()).Units = v),
            FS("M_Scale", p => p.Modbus?.Scale, (p, v) => (p.Modbus ??= new()).Scale = v),
            FS("M_LL", p => p.Modbus?.LL, (p, v) => (p.Modbus ??= new()).LL = v),
            FS("M_LA", p => p.Modbus?.LA, (p, v) => (p.Modbus ??= new()).LA = v),
            FS("M_HA", p => p.Modbus?.HA, (p, v) => (p.Modbus ??= new()).HA = v),
            FS("M_HH", p => p.Modbus?.HH, (p, v) => (p.Modbus ??= new()).HH = v),
            FS("M_Scaling", p => p.Modbus?.Scaling, (p, v) => (p.Modbus ??= new()).Scaling = v),
            FS("M_SigType", p => p.Modbus?.SigType, (p, v) => (p.Modbus ??= new()).SigType = v),
            FS("M_RegType", p => p.Modbus?.RegType, (p, v) => (p.Modbus ??= new()).RegType = v),
            FS("M_Address", p => p.Modbus?.Address, (p, v) => (p.Modbus ??= new()).Address = v),
            FS("M_AccessType", p => p.Modbus?.AccessType, (p, v) => (p.Modbus ??= new()).AccessType = v),
            FS("M_DataType", p => p.Modbus?.DataType, (p, v) => (p.Modbus ??= new()).DataType = v),
            FS("M_FuncCode", p => p.Modbus?.FuncCode, (p, v) => (p.Modbus ??= new()).FuncCode = v),
            FS("M_DcsChannel", p => p.Modbus?.DcsChannel, (p, v) => (p.Modbus ??= new()).DcsChannel = v),
            FS("M_DcsTag", p => p.Modbus?.DcsTag, (p, v) => (p.Modbus ??= new()).DcsTag = v),
            FS("M_DcsFunctions", p => p.Modbus?.DcsFunctions, (p, v) => (p.Modbus ??= new()).DcsFunctions = v),
            FS("M_Note", p => p.Modbus?.Note, (p, v) => (p.Modbus ??= new()).Note = v),

            // ===== Cable (13) =====
            FS("CBL_Id", p => p.Cable?.Id, (p, v) => (p.Cable ??= new()).Id = v),
            FS("CBL_Description", p => p.Cable?.Description, (p, v) => (p.Cable ??= new()).Description = v),
            FS("CBL_Type", p => p.Cable?.Type, (p, v) => (p.Cable ??= new()).Type = v),
            FS("CBL_Designation", p => p.Cable?.Designation, (p, v) => (p.Cable ??= new()).Designation = v),
            FS("CBL_From", p => p.Cable?.From, (p, v) => (p.Cable ??= new()).From = v),
            FS("CBL_To", p => p.Cable?.To, (p, v) => (p.Cable ??= new()).To = v),
            FI("CBL_Length", p => p.Cable?.Length, (p, v) => (p.Cable ??= new()).Length = v),
            FS("CBL_Color", p => p.Cable?.Color, (p, v) => (p.Cable ??= new()).Color = v),
            FI("CBL_Pair", p => p.Cable?.Pair, (p, v) => (p.Cable ??= new()).Pair = v),
            FS("CBL_Note", p => p.Cable?.Note, (p, v) => (p.Cable ??= new()).Note = v),
            FS("CBL_VendorDesignation", p => p.Cable?.VendorDesignation, (p, v) => (p.Cable ??= new()).VendorDesignation = v),
            FS("CBL_Drum", p => p.Cable?.Drum, (p, v) => (p.Cable ??= new()).Drum = v),
            FS("CBL_Voltage", p => p.Cable?.Voltage, (p, v) => (p.Cable ??= new()).Voltage = v),

            // ===== Equipment (17) =====
            FS("EQP_CableTecon", p => p.Equipment?.CableTecon, (p, v) => (p.Equipment ??= new()).CableTecon = v),
            FS("EQP_Mcc", p => p.Equipment?.Mcc, (p, v) => (p.Equipment ??= new()).Mcc = v),
            FS("EQP_ControlCabinet", p => p.Equipment?.ControlCabinet, (p, v) => (p.Equipment ??= new()).ControlCabinet = v),
            FS("EQP_MarshallingCabinet", p => p.Equipment?.MarshallingCabinet, (p, v) => (p.Equipment ??= new()).MarshallingCabinet = v),
            FS("EQP_CPU", p => p.Equipment?.CPU, (p, v) => (p.Equipment ??= new()).CPU = v),
            FS("EQP_Chassis_Main", p => p.Equipment?.Chassis?.Main, (p, v) => ((p.Equipment ??= new()).Chassis ??= new()).Main = v),
            FS("EQP_Chassis_Redundant", p => p.Equipment?.Chassis?.Redundant, (p, v) => ((p.Equipment ??= new()).Chassis ??= new()).Redundant = v),
            FS("EQP_SlotMain", p => p.Equipment?.Module?.SlotMain, (p, v) => ((p.Equipment ??= new()).Module ??= new()).SlotMain = v),
            FS("EQP_SlotRedundant", p => p.Equipment?.Module?.SlotRedundant, (p, v) => ((p.Equipment ??= new()).Module ??= new()).SlotRedundant = v),
            FS("EQP_SlotMain2", p => p.Equipment?.Module?.SlotMain2, (p, v) => ((p.Equipment ??= new()).Module ??= new()).SlotMain2 = v),
            FS("EQP_SlotRedundant2", p => p.Equipment?.Module?.SlotRedundant2, (p, v) => ((p.Equipment ??= new()).Module ??= new()).SlotRedundant2 = v),
            FS("EQP_ModuleMain", p => p.Equipment?.Module?.ModuleMain, (p, v) => ((p.Equipment ??= new()).Module ??= new()).ModuleMain = v),
            FS("EQP_ModuleRedundant", p => p.Equipment?.Module?.ModuleRedundant, (p, v) => ((p.Equipment ??= new()).Module ??= new()).ModuleRedundant = v),
            FS("EQP_ModuleMain2", p => p.Equipment?.Module?.ModuleMain2, (p, v) => ((p.Equipment ??= new()).Module ??= new()).ModuleMain2 = v),
            FS("EQP_Module2Redundant", p => p.Equipment?.Module?.Module2Redundant, (p, v) => ((p.Equipment ??= new()).Module ??= new()).Module2Redundant = v),
            FI("EQP_Channel", p => p.Equipment?.Module?.Channel, (p, v) => ((p.Equipment ??= new()).Module ??= new()).Channel = v),
            FS("EQP_Type", p => p.Equipment?.Module?.Type, (p, v) => ((p.Equipment ??= new()).Module ??= new()).Type = v),

            // ===== Revision (14) — аудит/трассировка =====
            FI("REV_Number", p => p.Revision?.Number, (p, v) => (p.Revision ??= new()).Number = v),
            FS("REV_Description", p => p.Revision?.Description, (p, v) => (p.Revision ??= new()).Description = v),
            FS("REV_Package", p => p.Revision?.Package, (p, v) => (p.Revision ??= new()).Package = v),
            FS("REV_VendorField1", p => p.Revision?.VendorField1, (p, v) => (p.Revision ??= new()).VendorField1 = v),
            FS("REV_VendorField2", p => p.Revision?.VendorField2, (p, v) => (p.Revision ??= new()).VendorField2 = v),
            FS("REV_Document", p => p.Revision?.Document, (p, v) => (p.Revision ??= new()).Document = v),
            FS("REV_AkerRevision", p => p.Revision?.AkerRevision, (p, v) => (p.Revision ??= new()).AkerRevision = v),
            FS("REV_FileName", p => p.Revision?.FileName, (p, v) => (p.Revision ??= new()).FileName = v),
            FS("REV_Sheet", p => p.Revision?.Sheet, (p, v) => (p.Revision ??= new()).Sheet = v), // F2.16: вкладка-источник
            FS("REV_Author", p => p.Revision?.Author, (p, v) => (p.Revision ??= new()).Author = v),
            FDT("REV_DateEntered", p => p.Revision?.DateEntered, (p, v) => (p.Revision ??= new()).DateEntered = v),
            FI("REV_RowNumber", p => p.Revision?.RowNumber, (p, v) => (p.Revision ??= new()).RowNumber = v),
            FS("REV_Type", p => p.Revision?.Type, (p, v) => (p.Revision ??= new()).Type = v),
            FS("REV_Count", p => p.Revision?.Count, (p, v) => (p.Revision ??= new()).Count = v),
            FDT("REV_DateModified", p => p.Revision?.DateModified, (p, v) => (p.Revision ??= new()).DateModified = v),

            // ===== Extra: column1..column10 (10) =====
            FS("column1", p => p.Extra?[1], (p, v) => (p.Extra ??= new())[1] = v),
            FS("column2", p => p.Extra?[2], (p, v) => (p.Extra ??= new())[2] = v),
            FS("column3", p => p.Extra?[3], (p, v) => (p.Extra ??= new())[3] = v),
            FS("column4", p => p.Extra?[4], (p, v) => (p.Extra ??= new())[4] = v),
            FS("column5", p => p.Extra?[5], (p, v) => (p.Extra ??= new())[5] = v),
            FS("column6", p => p.Extra?[6], (p, v) => (p.Extra ??= new())[6] = v),
            FS("column7", p => p.Extra?[7], (p, v) => (p.Extra ??= new())[7] = v),
            FS("column8", p => p.Extra?[8], (p, v) => (p.Extra ??= new())[8] = v),
            FS("column9", p => p.Extra?[9], (p, v) => (p.Extra ??= new())[9] = v),
            FS("column10", p => p.Extra?[10], (p, v) => (p.Extra ??= new())[10] = v),
        };

        private static readonly Dictionary<string, FieldDef> _byColumn =
            new(StringComparer.OrdinalIgnoreCase);

        static FieldRegistry()
        {
            foreach (var f in Fields) _byColumn[f.Column] = f;
        }

        /// <summary>Имена колонок в порядке DDL (126 шт.; вместе с ID — 127).</summary>
        public static string[] Columns
        {
            get
            {
                var r = new string[Fields.Length];
                for (int i = 0; i < Fields.Length; i++) r[i] = Fields[i].Column;
                return r;
            }
        }

        /// <summary>Прочитать значение колонки из модели.</summary>
        public static object? GetValue(IoPoint p, string column) =>
            _byColumn.TryGetValue(column, out var f) ? f.Get(p) : null;

        /// <summary>Записать значение колонки в модель (приведение типов выполняется здесь).</summary>
        public static bool SetValue(IoPoint p, string column, object? value)
        {
            if (!_byColumn.TryGetValue(column, out var f)) return false;
            f.Set(p, value);
            return true;
        }

        /// <summary>Проверка: колонка существует в структуре таблицы.</summary>
        public static bool HasColumn(string column) => _byColumn.ContainsKey(column);
    }
}
