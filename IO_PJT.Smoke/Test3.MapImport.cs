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
        private static void TestMapImport()
        {
            string db = NewDbPath();
            string xlsx = Path.Combine(Path.GetTempPath(), $"smoke_map_{Guid.NewGuid():N}.xlsx");
            try
            {
                CreateDb(db);
                var log = new EventLogger();
                var audit = new AuditService(db, log);
                var mapping = new MappingService(db);
                var svc = new ModbusImportService(db, mapping, audit, log);
                var maps = new ModbusMapService(db);

                WriteMapFile(xlsx, new[]
                {
                    ("1001-PM-101A_STA.MCU", "old desc", "02", "100"),
                    ("1001-PM-101A_FAULT.MCU", "fault", "02", "101"),
                    ("1001-PM-101A_I_A", "current", "03", "200")
                });
                var cols = DetectMapColumns(xlsx, mapping);

                var read = svc.Read(xlsx, cols, 2, "Register Table", true, null);
                Check(read.Entries.Count == 3 && read.RowsSkipped == 1,
                    $"чтение файла: 3 строки, 1 пропуск (факт {read.Entries.Count}/{read.RowsSkipped})");
                Check(read.SlaveStation == "ST100" && read.Entries.All(e => e.SlaveStation == "ST100"),
                    $"станция из Interface parameters (факт {read.SlaveStation})");

                var imp1 = svc.Import(xlsx, cols, 2, "Register Table", true, null, false, true);
                Check(imp1.Inserted == 3 && imp1.Updated == 0 && imp1.Deleted == 0,
                    $"первый импорт +3 (факт +{imp1.Inserted} ~{imp1.Updated} -{imp1.Deleted})");
                int auditN = audit.GetHistory().Count;
                var imp2 = svc.Import(xlsx, cols, 2, "Register Table", true, null, false, true);
                Check(imp2.Inserted == 0 && imp2.Updated == 0 && imp2.Deleted == 0 &&
                      audit.GetHistory().Count == auditN,
                    "повторный импорт без правок → 0/0/0, аудит не вырос");

                // привязка должна пережить сравнительный импорт (ID и LinkTag сохраняются)
                var rowA = maps.LoadFromDb().First(x => x.PlcTag == "1001-PM-101A_STA.MCU");
                maps.ApplyLinkResults(new List<(long Id, string? LinkTag, string? DcsTag, string LinkState)>
                    { (rowA.Id, "1001-MXA-00001", "1001-MXA-00001", ModbusMapService.LinkStateLinked) });

                WriteMapFile(xlsx, new[]
                {
                    ("1001-PM-101A_STA.MCU", "new desc", "02", "100"),
                    ("1001-PM-101A_I_A", "current", "03", "200"),
                    ("1001-PM-101A_TX.MCU", "comm", "02", "300")
                });
                var imp3 = svc.Import(xlsx, cols, 2, "Register Table", true, null, false, true);
                Check(imp3.Inserted == 1 && imp3.Updated == 1 && imp3.Deleted == 1,
                    $"правка файла: +1 ~1 -1 (факт +{imp3.Inserted} ~{imp3.Updated} -{imp3.Deleted})");
                var rowA2 = maps.LoadFromDb().FirstOrDefault(x => x.PlcTag == "1001-PM-101A_STA.MCU");
                Check(rowA2 != null && rowA2.Id == rowA.Id && rowA2.LinkTag == "1001-MXA-00001" &&
                      rowA2.Description == "new desc",
                    "ID и привязка сохранены, описание обновлено");
                var upd = audit.GetHistory(field: "Description").FirstOrDefault(h => h.Action == AuditAction.Update);
                Check(upd != null && upd.OldValue == "old desc" && upd.NewValue == "new desc",
                    "аудит Update Description: old → new");

                var imp4 = svc.Import(xlsx, cols, 2, "Register Table", true, null, true, true);
                int rowsAfter = maps.LoadFromDb().Count;
                Check(imp4.Replaced && rowsAfter == 3,
                    $"режим «Заменить строки файла»: Replaced, строк 3 (факт {rowsAfter})");

                var noTag = cols.Where(m => m.AccessColumn != "PlcTag").ToList();
                bool blocked = false;
                try { svc.Import(xlsx, noTag, 2, "Register Table", true, null, false, true); }
                catch (InvalidOperationException ex) { blocked = ex.Message.Contains("PlcTag"); }
                Check(blocked, "импорт без привязки PlcTag блокируется с понятной причиной");
            }
            finally
            {
                DeleteDb(db);
                try { File.Delete(xlsx); } catch { }
            }
        }
    }
}
