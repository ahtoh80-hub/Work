using System;
using System.Collections.Generic;
using System.Linq;
using IO_PJT.Data;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.Smoke
{
    public static partial class Program
    {
        private static ModbusMapEntry MapEntry(int row, string plc, string desc, string func, string addr) => new()
        {
            MapFile = "smoke_map.xlsx", MapSheet = "Register Table Function 02", MapRow = row,
            SlaveStation = "ST100", SlaveIdMain = Ip10,
            PlcTag = plc, ObjectTag = ModbusMapService.DeriveObjectTag(plc),
            Description = desc, FuncCode = func, Address = addr, DataType = "BOOL"
        };

        // ============ 5. Связывание и адресация Modbus ============
        private static void TestLinkAndAddressing()
        {
            string db = NewDbPath();
            try
            {
                CreateDb(db);
                var log = new EventLogger();
                var audit = new AuditService(db, log);
                var repo = new IOListRepository(db, Schema.DefaultTable, audit, log);

                repo.BeginSession();
                repo.Upsert(MakePoint("1115-MXA-00101A", "DI-COM", "fault"));   // точное совпадение
                repo.Upsert(MakePoint("1115-MXA-00102A", "DI-COM", "running")); // маска с wildcard
                repo.Upsert(MakePoint("1115-MXA-00103A", "DI", "не Modbus"));   // IoType без COM/M
                repo.Upsert(MakePoint("1115-MXA-00104A", "DI-COM", "нет в карте"));
                repo.EndSession();

                var maps = new ModbusMapService(db);
                maps.SaveToDb(new List<ModbusMapEntry>
                {
                    MapEntry(3, "1115-PM-101A_FAULT.MCU", "1115-MXA-00101A", "02", "100"),
                    MapEntry(4, "1115-PM-101A_STA.MCU", "1115-MXA-00102A extra", "02", "101"),
                    MapEntry(5, "1115-PM-101A_TX.MCU", "1115-MXA-00103A", "02", "102")
                }, "smoke_map.xlsx");

                var rules = new ModbusLinkRuleService(db);
                rules.SaveAll(new[]
                {
                    new ModbusLinkRule { Name = "Точно", IOField = "Tag", MapField = "Description",
                        Mask = "", Priority = 10 },
                    new ModbusLinkRule { Name = "Маска", IOField = "Tag", MapField = "Description",
                        Mask = "{Tag}*", Priority = 20 }
                });
                Check(rules.Load().Count == 2 && rules.LoadEnabled().Count == 2,
                    "правила сохранены и загружены (2 шт., оба включены)");
                Check(ModbusLinkRuleService.Validate(new ModbusLinkRule { Name = "x", IOField = "НетКолонки" }) != null,
                    "Validate правила: неизвестная колонка IO → ошибка");

                var linker = new ModbusLinkService(repo, maps, log, audit, rules);
                var res = linker.Link();
                Check(res.Examined == 4, $"проверено 4 точки (факт {res.Examined})");
                Check(res.Linked == 2, $"связано 2 точки (факт {res.Linked})");
                Check(res.Unlinked == 2 && res.Errors.Count == 1,
                    $"2 не связаны, из них 1 ошибка «не Modbus» (факт {res.Unlinked}/{res.Errors.Count})");

                var states = maps.LoadFromDb();
                var exact = states.First(s => s.MapRow == 3);
                var part = states.First(s => s.MapRow == 4);
                Check(exact.LinkState == ModbusMapService.LinkStateLinked && exact.LinkTag == "1115-MXA-00101A",
                    $"точное правило → «привязано» (факт {exact.LinkState})");
                Check(part.LinkState == ModbusMapService.LinkStateLinkedPartial &&
                      ModbusMapService.IsLinkedState(part.LinkState),
                    $"маска с wildcard → «привязано частично» (факт {part.LinkState})");

                var link2 = linker.Link();
                Check(link2.Linked == res.Linked && link2.MapRowsLinked == res.MapRowsLinked,
                    "повторный Link идемпотентен");

                // адресация: M_-поля IO_LIST заполняются по DcsTag ↔ Tag
                Check(repo.GetByTag("1115-MXA-00101A")?.Modbus?.FuncCode == null,
                    "Link не пишет M_-поля (только связь)");
                var addr = linker.UpdateAddressing();
                Check(addr.Updated == 2, $"адресация: обновлено 2 (факт {addr.Updated})");
                var p1 = repo.GetByTag("1115-MXA-00101A");
                Check(p1?.Modbus?.PlcTag == "1115-PM-101A_FAULT.MCU" && p1?.Modbus?.FuncCode == "02" &&
                      p1?.Modbus?.Address == "100",
                    $"M_PlcTag/M_FuncCode/M_Address записаны (факт {p1?.Modbus?.PlcTag}/{p1?.Modbus?.FuncCode}/{p1?.Modbus?.Address})");
                Check(linker.UpdateAddressing().Updated == 0, "повторное обновление адресации → 0 изменений");
            }
            finally { DeleteDb(db); }
        }
    }
}
