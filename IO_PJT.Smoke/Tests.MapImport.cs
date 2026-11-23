using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IO_PJT.Data;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.Smoke
{
    public static partial class Program
    {
        // ============ 3. Импорт карты MODBUS_MAP (сравнение) ============
        private static void WriteMapFile(string path, (string Plc, string Desc, string Func, string Addr)[] rows)
        {
            using var wb = new ClosedXML.Excel.XLWorkbook();
            var iface = wb.Worksheets.Add("Interface parameters");
            iface.Cell(1, 4).Value = "Slave Station (Data Source)";
            iface.Cell(1, 5).Value = "Slave ID main";
            iface.Cell(1, 6).Value = "Slave ID back-up";
            iface.Cell(1, 7).Value = "speed";
            iface.Cell(1, 12).Value = "timeout";
            iface.Cell(2, 4).Value = "ST100";
            iface.Cell(2, 5).Value = "10.0.0.1";
            iface.Cell(2, 6).Value = "10.0.0.2";
            iface.Cell(2, 7).Value = "115200";
            iface.Cell(2, 12).Value = "1000";

            string[] hdr =
            {
                "", "Project Functional Designation", "PLC Tag/Variable Name",
                "Description of the variable", "Units of measurement", "scale", "LL", "LA", "HA", "HH",
                "Scaling factors", "Signal type (Impulse/Permanent)", "Register type", "Address/Bit",
                "Access type", "Data type", "Function code", "DCS channel", "DCS Tag/Name variable",
                "DCS functions", "note"
            };
            var ws = wb.Worksheets.Add("Register Table Function 02");
            for (int c = 1; c <= 21; c++)
                if (hdr[c - 1].Length > 0) ws.Cell(2, c).Value = hdr[c - 1];
            int r = 3;
            foreach (var row in rows)
            {
                ws.Cell(r, 3).Value = row.Plc;
                ws.Cell(r, 4).Value = row.Desc;
                ws.Cell(r, 14).Value = row.Addr;
                ws.Cell(r, 17).Value = row.Func;
                r++;
            }
            ws.Cell(r, 4).Value = "строка без тега — пропуск"; // без PlcTag → RowsSkipped
            wb.SaveAs(path);
        }

        private static List<ColumnMap> DetectMapColumns(string xlsx, MappingService mapping)
        {
            using var rd = new ExcelReader(xlsx);
            return mapping.AutoDetect(rd.GetHeaders("Register Table Function 02", 2), ImportTargets.ModbusMap);
        }
    }
}
