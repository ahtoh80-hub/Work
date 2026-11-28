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
        // ============ 1. Схема БД и реестр колонок ============
        private static void TestSchema()
        {
            Check(FieldRegistry.Columns.Length == Schema.DataColumns.Length,
                $"FieldRegistry = DataColumns ({FieldRegistry.Columns.Length} vs {Schema.DataColumns.Length})");
            Check(FieldRegistry.HasColumn("Tag") && FieldRegistry.HasColumn("M_Address"),
                "FieldRegistry знает Tag и M_-колонки");
            var probe = new IoPoint();
            FieldRegistry.SetValue(probe, "M_PlcTag", "1115-PM-101A_STA.MCU");
            Check(probe.Modbus?.PlcTag == "1115-PM-101A_STA.MCU", "FieldRegistry.SetValue → Modbus.PlcTag");
            Check(Schema.CreateModbusMapTable.Contains("[LinkState] TEXT(255)"),
                "DDL MODBUS_MAP содержит LinkState");
            Check(Schema.CreateScadaTemplateVarsTable.Contains("[NameSuffix] TEXT(255)"),
                "DDL SCADA_TEMPLATE_VARS содержит NameSuffix");

            string db = NewDbPath();
            try
            {
                var creator = CreateDb(db);
                new ModbusBlockPlannerService(db).EnsureTables();
                var tables = creator.GetTableNames();
                Check(new[] { Schema.DefaultTable, Schema.ModbusMapTable, Schema.ModbusLinkRulesTable,
                              Schema.ScadaTemplateVarsTable }.All(t => tables.Contains(t)),
                    "созданы IO_LIST, MODBUS_MAP, MODBUS_LINK_RULES, SCADA_TEMPLATE_VARS");
                var cols = creator.GetColumns(Schema.DefaultTable);
                Check(cols.Contains("Tag") && cols.Contains("M_Marka"), "IO_LIST содержит Tag и M_Marka");
                var ipCols = new[] { "M_IP_Main", "M_IP_Reserve", "M_IP_Mask_Main", "M_IP_Mask_Reserve", "M_IP_Port_Main", "M_IP_Port_Reserve" };
                Check(ipCols.All(cols.Contains), "IO_LIST contains split M_IP_* columns");
                var ruleCols = creator.GetColumns(Schema.ScadaMbSlaveRulesTable);
                Check(ipCols.All(ruleCols.Contains) && ruleCols.Contains("IpMain") && ruleCols.Contains("IpReserve"),
                    "SCADA_MB_SLAVE_RULES contains new M_IP_* and legacy IP columns");
                creator.EnsureServiceTables();
                new ModbusBlockPlannerService(db).EnsureTables();
                Check(true, "повторное создание служебных таблиц идемпотентно (без исключений)");
            }
            finally { DeleteDb(db); }
        }

        // ============ 2. IO_LIST: upsert и аудит ============
        private static void TestIoListUpsert()
        {
            string db = NewDbPath();
            try
            {
                CreateDb(db);
                var log = new EventLogger();
                var audit = new AuditService(db, log);
                var repo = new IOListRepository(db, Schema.DefaultTable, audit, log);

                repo.BeginSession();
                var r1 = repo.Upsert(MakePoint("1001-MXA-00001", "DI-COM", "сервис 1"));
                var r2 = repo.Upsert(MakePoint("1001-MXA-00001", "DI-COM", "сервис 1"));
                var r3 = repo.Upsert(MakePoint("1001-MXA-00001", "DI-COM", "сервис 2"));
                repo.EndSession();

                Check(r1.Outcome == UpsertOutcome.Inserted, $"новый тег → Inserted (факт {r1.Outcome})");
                Check(r2.Outcome == UpsertOutcome.Unchanged, $"тот же тег без правок → Unchanged (факт {r2.Outcome})");
                Check(r3.Outcome == UpsertOutcome.Updated, $"изменённое поле → Updated (факт {r3.Outcome})");
                Check(repo.Count() == 1, $"в таблице 1 запись (факт {repo.Count()})");
                Check(repo.GetByTag("1001-MXA-00001")?.Signal?.Service == "сервис 2",
                    "GetByTag возвращает обновлённое значение");

                var hist = audit.GetHistory(tag: "1001-MXA-00001");
                Check(hist.Any(h => h.Action == AuditAction.Insert) &&
                      hist.Any(h => h.Action == AuditAction.Update),
                    $"аудит содержит Insert и Update (записей {hist.Count})");

                var conflictPoint = MakePoint("1001-MXA-00001", "DI-COM", "сервис 2");
                conflictPoint.Identification.System = "ПАЗ";
                var rc = repo.Upsert(conflictPoint);
                Check(rc.Outcome == UpsertOutcome.Conflict,
                    $"тот же Tag другой System → Conflict (факт {rc.Outcome})");
            }
            finally { DeleteDb(db); }
        }
    }
}
