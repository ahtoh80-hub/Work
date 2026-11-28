using System;
using System.Collections.Generic;
using System.Linq;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.Smoke
{
    public static partial class Program
    {
        private static bool Parses(string raw, string? bitOffset, string address, string bit) =>
            ModbusAddressParser.TryParse(raw, bitOffset,
                out string a, out string b, out _) && a == address && b == bit;

        private static IoPoint FillPoint(string tag, string address,
            string? bit = null, string? bitOffset = null) => new()
        {
            Identification = new Identification { Tag = tag, IoType = "M", System = "РСУ" },
            Modbus = new Modbus { Address = address, Bit = bit, BitOffset = bitOffset },
            Revision = new Revision { FileName = "smoke.xlsx", Sheet = "ALL" }
        };

        // ============ 14. F113: заполнение M_Bit из M_Address ============
        private static void Test14_FillBitFromAddress()
        {
            // Разбор: разделитель — один спецсимвол, пробелы вокруг допустимы.
            Check(Parses("32.1", null, "32", "1"), "F113: 32.1 → Address=32, Bit=1");
            Check(Parses("32,1", null, "32", "1"), "F113: запятая как разделитель");
            Check(Parses("32_1", null, "32", "1"), "F113: подчёркивание как разделитель");
            Check(Parses("32-1", null, "32", "1"), "F113: дефис как разделитель");
            Check(Parses("32 / 1", null, "32", "1"), "F113: слэш и пробелы вокруг разделителя");
            Check(Parses("0032.01", null, "0032", "1"), "F113: ведущие нули регистра сохранены, бит нормализован");
            Check(!ModbusAddressParser.HasSeparator("32") &&
                  !ModbusAddressParser.TryParse("32", null, out _, out _, out _),
                "F113: обычный адрес 32 не является совмещённым");
            Check(!ModbusAddressParser.TryParse("32.1.2", null, out _, out _, out _),
                "F113: два разделителя — ошибка");
            Check(!ModbusAddressParser.TryParse("32.a", null, out _, out _, out _),
                "F113: буква в бите — ошибка");
            Check(!ModbusAddressParser.TryParse("32.16", "0", out _, out _, out _),
                "F113: бит 16 без M_Bit_Offset — ошибка");
            Check(Parses("32.16", "1", "32", "16"),
                "F113: бит 16 при M_Bit_Offset=1 допустим");
            Check(!ModbusAddressParser.TryParse("32.0", "1", out _, out _, out _),
                "F113: бит 0 при M_Bit_Offset=1 — ошибка");

            string db = NewDbPath();
            try
            {
                CreateDb(db);
                var log = new EventLogger();
                var audit = new AuditService(db, log);
                var repo = new IOListRepository(db, Data.Schema.DefaultTable, audit, log);
                var maps = new ModbusMapService(db);
                var rules = new ModbusLinkRuleService(db);
                var svc = new ModbusLinkService(repo, maps, log, audit, rules);

                repo.BeginSession();
                repo.Upsert(FillPoint("F113-DOT", "32.1"));
                repo.Upsert(FillPoint("F113-COMMA", "33,2"));
                repo.Upsert(FillPoint("F113-UNDER", "34_3"));
                repo.Upsert(FillPoint("F113-PLAIN", "35"));
                repo.Upsert(FillPoint("F113-BAD", "36.4.5"));
                repo.Upsert(FillPoint("F113-OFF16", "37.16", bitOffset: "1"));
                repo.Upsert(FillPoint("F113-OFF0", "38.0", bitOffset: "1"));
                repo.EndSession();

                var r = svc.FillBitsFromCombinedAddress();
                Check(r.Examined == 7 && r.Updated == 4 && r.ChangedFields == 8 &&
                      r.Skipped == 1 && r.Invalid == 2,
                    $"F113: итог all (examined/updated/fields/skipped/invalid = " +
                    $"{r.Examined}/{r.Updated}/{r.ChangedFields}/{r.Skipped}/{r.Invalid})");

                Check(repo.GetByTag("F113-DOT")?.Modbus?.Address == "32" &&
                      repo.GetByTag("F113-DOT")?.Modbus?.Bit == "1",
                    "F113: 32.1 записан как Address=32, Bit=1");
                Check(repo.GetByTag("F113-COMMA")?.Modbus?.Address == "33" &&
                      repo.GetByTag("F113-COMMA")?.Modbus?.Bit == "2",
                    "F113: запятая разобрана и записана");
                Check(repo.GetByTag("F113-PLAIN")?.Modbus?.Address == "35" &&
                      repo.GetByTag("F113-PLAIN")?.Modbus?.Bit == null,
                    "F113: обычный адрес 35 не изменён");
                Check(repo.GetByTag("F113-BAD")?.Modbus?.Address == "36.4.5",
                    "F113: некорректный адрес не изменён");
                Check(repo.GetByTag("F113-OFF16")?.Modbus?.Bit == "16",
                    "F113: M_Bit_Offset=1 разрешает бит 16");

                var r2 = svc.FillBitsFromCombinedAddress();
                Check(r2.Updated == 0 && r2.Skipped == 5 && r2.Invalid == 2,
                    "F113: повторный запуск идемпотентен (нет повторных изменений)");

                // Выборочный режим: обновляется только переданная точка.
                repo.Upsert(FillPoint("F113-SEL1", "39.5"));
                repo.Upsert(FillPoint("F113-SEL2", "40.6"));
                var sel = repo.GetByTag("F113-SEL1")!;
                var rSel = svc.FillBitsFromCombinedAddress(new[] { sel });
                Check(rSel.Updated == 1 &&
                      repo.GetByTag("F113-SEL1")?.Modbus?.Bit == "5" &&
                      repo.GetByTag("F113-SEL2")?.Modbus?.Address == "40.6",
                    "F113: выбранные строки — обновлена только выбранная");
            }
            finally { DeleteDb(db); }
        }
    }
}
