using System;
using System.Collections.Generic;
using System.Globalization;
using ClosedXML.Excel;
using IO_PJT.Models;

namespace IO_PJT.Services
{
    /// <summary>
    /// Чтение Excel-файлов через ClosedXML (ТЗ F2.1):
    /// при открытии выбираются вкладка (лист) и строка начала чтения —
    /// первая строка выбранных данных содержит названия колонок.
    /// </summary>
    public sealed class ExcelReader : IExcelReader
    {
        private readonly XLWorkbook _workbook;
        private readonly string _path;

        public ExcelReader(string path)
        {
            _path = path;
            _workbook = new XLWorkbook(path); // ClosedXML (MIT)
        }

        /// <summary>Путь к открытому файлу (для трассировки REV_FileName).</summary>
        public string Path => _path;

        /// <summary>Список вкладок (листов) файла.</summary>
        public IReadOnlyList<string> GetSheetNames()
        {
            var names = new List<string>();
            foreach (var ws in _workbook.Worksheets)
                names.Add(ws.Name);
            return names;
        }

        /// <summary>Заголовки листа: строка HeaderRow (1-based) — названия колонок.
        /// F2.13: пустая ячейка шапки читается из строки выше (двухуровневый заголовок);
        /// при headerRow = 1 fallback не применяется.</summary>
        public IReadOnlyList<string> GetHeaders(string sheetName, int headerRow)
        {
            var ws = _workbook.Worksheet(sheetName);
            var result = new List<string>();
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
            for (int c = 1; c <= lastCol; c++)
            {
                var text = CellText(ws.Cell(headerRow, c));
                // F2.13: колонка без имени в шапке → берём имя из строки выше
                if (string.IsNullOrWhiteSpace(text) && headerRow > 1)
                    text = CellText(ws.Cell(headerRow - 1, c));
                result.Add(text ?? "");
            }
            return result;
        }

        /// <summary>Строки данных (после строки заголовка). Строки-разделители «-» возвращаются как есть — их отсекает импортёр.
        /// F2.12: для каждой ячейки фиксируется признак зачёркивания (Strikethrough).</summary>
        public IReadOnlyList<ExcelRowData> ReadRows(string sheetName, int headerRow)
        {
            var ws = _workbook.Worksheet(sheetName);
            var rows = new List<ExcelRowData>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                var cells = new List<string?>(lastCol);
                var struck = new List<bool>(lastCol);
                bool anyValue = false;
                for (int c = 1; c <= lastCol; c++)
                {
                    var cell = ws.Cell(r, c);
                    string? text = CellText(cell);
                    cells.Add(text);
                    // F2.12: зачёркнутая ячейка (модель Excel) = значение отменено автором
                    struck.Add(cell.Style.Font.Strikethrough);
                    if (!string.IsNullOrWhiteSpace(text)) anyValue = true;
                }
                if (anyValue) rows.Add(new ExcelRowData { Cells = cells, Struck = struck });
            }
            return rows;
        }

        /// <summary>Номер строки Excel (1-based) для индекса строки данных (0-based).</summary>
        public int GetExcelRowNumber(string sheetName, int headerRow, int dataIndex) =>
            headerRow + 1 + dataIndex;

        /// <summary>Текстовое представление ячейки без потерь (числа — без хвостовых нулей).</summary>
        private static string? CellText(IXLCell cell)
        {
            var v = cell.Value; // XLCellValue (ClosedXML 0.104: TryGetValue<object> не поддерживается)
            if (v.IsBlank) return null;
            return v.Type switch
            {
                XLDataType.Number => v.GetNumber().ToString("0.########", CultureInfo.InvariantCulture),
                XLDataType.DateTime => v.GetDateTime().ToString("yyyy-MM-dd HH:mm:ss"),
                XLDataType.Boolean => v.GetBoolean() ? "1" : "0",
                XLDataType.Text => string.IsNullOrEmpty(v.GetText()) ? null : v.GetText(),
                _ => v.ToString()
            };
        }

        public void Dispose() => _workbook.Dispose();
    }
}
