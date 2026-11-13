using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IO_PJT.Data;
using IO_PJT.Models;
using IO_PJT.Services;

namespace IO_PJT.Smoke
{
    /// <summary>Смоук-тесты F9: парсинг карт Modbus, кандидаты/роли, связывание на реальных файлах
    /// EBSM102/EBSM103, аудит Duplicate (F9.1), гибкие классы/ведущие нули (F9.6/F9.7),
    /// сообщения связывания в аудите (F9.8).</summary>
    public static class Program
    {
        private static int _passed;
        private static int _failed;

        private static void Check(bool cond, string name)
        {
            if (cond) { _passed++; Console.WriteLine($"  OK   {name}"); }
            else { _failed++; Console.WriteLine($"  FAIL {name}"); }
        }

        public static int Main()
        {
            string work = @"c:\VSCode\Work";
            string map102 = Path.Combine(work, "IMCS TO DCS Register Table Modbus - EBSM102.xlsx");
            string map103 = Path.Combine(work, "IMCS TO DCS Register Table Modbus - EBSM103.xlsx");
            if (!File.Exists(map102) || !File.Exists(map103))
            {
                Console.WriteLine("Файлы карт не найдены — смоук прерван");
                return 2;
            }

            Console.WriteLine("== F9 Схема и реестр ==");
            Check(Schema.DataColumns.Length == 130, $"DataColumns = 130 (факт {Schema.DataColumns.Length})");
            Check(Schema.CreateModbusMapTable.Contains("[LinkState] TEXT(255)"),
                "DDL MODBUS_MAP содержит LinkState");
            int mStart = Array.IndexOf(Schema.DataColumns, "M_MapFile");
            int mEnd = Array.IndexOf(Schema.DataColumns, "M_Marka"); // F27: последняя M_-колонка
            int cbl = Array.IndexOf(Schema.DataColumns, "CBL_Id");
            Check(mStart > 0 && mEnd > mStart && cbl == mEnd + 1, "M_* идут перед CBL_Id");
            Check(FieldRegistry.Columns.Length == Schema.DataColumns.Length,
                $"FieldRegistry = DataColumns ({FieldRegistry.Columns.Length} vs {Schema.DataColumns.Length})");
            Check(FieldRegistry.HasColumn("M_Address") && FieldRegistry.HasColumn("M_FuncCode"),
                "FieldRegistry знает M_-колонки");
            var probe = new IoPoint();
            FieldRegistry.SetValue(probe, "M_PlcTag", "1115-PM-101A_STA.MCU");
            Check(probe.Modbus?.PlcTag == "1115-PM-101A_STA.MCU", "FieldRegistry.SetValue → Modbus.PlcTag");

            Console.WriteLine("== F9 Нормализация тегов ПЛК ==");
            Check(ModbusLinkService.NormalizePlcTag("1115-P-101A") == "1115-PM-101A", "P → PM");
            Check(ModbusLinkService.NormalizePlcTag("1110-K-101A") == "1110-KM-101A", "K → KM");
            Check(ModbusLinkService.NormalizePlcTag("1115-M-00101A") == "1115-M-101A", "уборка ведущих нулей");
            Check(ModbusLinkService.NormalizePlcTag("мусор") == null, "мусор → null");

            Console.WriteLine("== F9 Кандидаты и роли (MCC-примеры) ==");
            var pStop = MakePoint("1115-MHS-00101A", "1115-M-00101A", "DO-COM",
                "1115-P-101A motor DCS command:stop");
            var cands = ModbusLinkService.ExtractCandidates(pStop);
            Check(cands.Count > 0 && cands[0].Tag == "1115-PM-101A",
                $"Service → 1115-PM-101A (факт {cands.FirstOrDefault().Tag})");
            Check(ModbusLinkService.DetectRole(pStop) == "", "команда → Control word (Function 06)");

            var pFault = MakePoint("1115-MXA-00101A", "1115-M-00101A", "DI-COM",
                "1115-P-101A motor Motor status:fault");
            Check(ModbusLinkService.DetectRole(pFault) == "FAULT.MCU", "fault → FAULT.MCU");

            var pRun = MakePoint("1115-MXA-00101B", "1115-M-00101A", "DI-COM",
                "1115-P-101A motor Motor status:running");
            Check(ModbusLinkService.DetectRole(pRun) == "STA.MCU", "running → STA.MCU");

            var pCur = MakePoint("1115-MII-00101", "1115-M-00101A", "AI-COM",
                "1115-P-101A motor Motor status:current");
            Check(ModbusLinkService.DetectRole(pCur) == "I_A", "current → I_A");

            var pBr = MakePoint("1110-MHS-00101A", "1110-M-00101A", "DO-COM",
                "1110-K-101A main driver motor (1110-KM-101A-1)DCS command:stop");
            var cbr = ModbusLinkService.ExtractCandidates(pBr);
            Check(cbr.Count > 0 && cbr[0].Tag == "1110-KM-101A-1" && cbr[0].Priority == 1,
                $"скобки → 1110-KM-101A-1 приоритет 1 (факт {cbr.FirstOrDefault().Tag}/{cbr.FirstOrDefault().Priority})");

            Console.WriteLine("== F9 Кандидаты: склейка и два поля Service ==");
            // A1: кириллица «прилипает» к тегу — \b в .NET считает её словом, границы только ASCII
            var pGlue = MakePoint("1115-MXA-00101D", "1115-M-00101A", "DI-COM",
                "Электродвигатель 1115-P-101AСостояние двигателя: авария");
            var cGlue = ModbusLinkService.ExtractCandidates(pGlue);
            Check(cGlue.Count > 0 && cGlue[0].Tag == "1115-PM-101A",
                $"склейка с кириллицей → 1115-PM-101A (факт {cGlue.FirstOrDefault().Tag ?? "нет"})");
            // A2: тег только в ServiceEnglish (Service без тега) — сканируются оба поля
            var pEn = MakePoint("1115-MXA-00101E", "1115-M-00101A", "DI-COM", "сигнал без обозначения");
            pEn.Signal!.ServiceEnglish = "1115-P-101A motor Motor status:fault";
            var cEn = ModbusLinkService.ExtractCandidates(pEn);
            Check(cEn.Count > 0 && cEn[0].Tag == "1115-PM-101A",
                $"ServiceEnglish → 1115-PM-101A (факт {cEn.FirstOrDefault().Tag ?? "нет"})");
            // A2: дедуп скобочных ссылок между полями
            pEn.Signal.Service = "(1115-P-101A) ссылка в Service";
            pEn.Signal.ServiceEnglish = "(1115-P-101A) та же ссылка в ServiceEnglish";
            var cDup = ModbusLinkService.ExtractCandidates(pEn);
            Check(cDup.Count(c => c.Tag == "1115-PM-101A") == 1,
                $"скобочный дедуп: 1 кандидат 1115-PM-101A (факт {cDup.Count(c => c.Tag == "1115-PM-101A")})");

            var pReady = MakePoint("1115-MXA-00101F", "1115-M-00101A", "DI-COM",
                "1115-P-101A motor Motor status:ready");
            Check(ModbusLinkService.DetectRole(pReady) == "REDAY.MCU", "ready → REDAY.MCU");

            // ===== F9.10: run-сигнал, MSC/MSI (скорость VFD), роль по суффиксу тега =====
            var pRunSig = MakePoint("2711-MXA-27116B", "2711-M-27116", "DI-COM",
                "SM Transfer Pump 2711-P-1302A run signal");
            Check(ModbusLinkService.DetectRole(pRunSig) == "STA.MCU",
                "F9.10: run signal → STA.MCU");
            var pMsc = MakePoint("2701-MSC-31201", "2701-M-31201", "AO-COM",
                "ITF Waste Gas Fan 2701-PK-1201B speed control");
            Check(ModbusLinkService.DetectRole(pMsc) == "FREQ_SET",
                "F9.10: speed control (AO-COM) → FREQ_SET");
            var pMsi = MakePoint("2701-MSI-31201", "2701-M-31201", "AI-COM",
                "ITF Waste Gas Fan 2701-PK-1201B speed signal");
            Check(ModbusLinkService.DetectRole(pMsi) == "SPEED",
                "F9.10: speed signal (AI-COM) → SPEED, не I_A");
            var pSfxC = MakePoint("9967-MXA-1007C", "9967-M-1007", "DI-COM",
                "9967-P-1007 motor сервис без ключевых слов");
            Check(ModbusLinkService.DetectRole(pSfxC) == "REDAY.MCU",
                "F9.10: суффикс тега MXA…C → REDAY.MCU");
            var pSfxB = MakePoint("9967-MXA-1007B", "9967-M-1007", "DI-COM",
                "9967-P-1007 motor сервис без ключевых слов");
            Check(ModbusLinkService.DetectRole(pSfxB) == "STA.MCU",
                "F9.10: суффикс тега MXA…B → STA.MCU");

            Console.WriteLine("== F9 Парсинг карт EBSM102/103 ==");
            var svc = new ModbusMapService();
            var entries = new List<ModbusMapEntry>();
            var r102 = svc.Read(map102, entries);
            var r103 = svc.Read(map103, entries);
            Console.WriteLine($"  EBSM102: {r102.RowsRead} строк, станция {r102.SlaveStation}");
            Console.WriteLine($"  EBSM103: {r103.RowsRead} строк, станция {r103.SlaveStation}");
            Check(r102.RowsRead >= 790, $"EBSM102 ≥790 строк (факт {r102.RowsRead})");
            Check(r103.RowsRead >= 790, $"EBSM103 ≥790 строк (факт {r103.RowsRead})");
            Check(!string.IsNullOrWhiteSpace(r102.SlaveStation), $"станция EBSM102: {r102.SlaveStation}");
            Check(!string.IsNullOrWhiteSpace(r103.SlaveStation), $"станция EBSM103: {r103.SlaveStation} (в исходнике указана как EBSM102)");
            Check(entries.All(e => !string.IsNullOrWhiteSpace(e.ObjectTag)), "ObjectTag выведен у всех строк");
            Check(entries.Any(e => e.PlcTag == "1115-PM-101A_STA.MCU"), "найден 1115-PM-101A_STA.MCU");
            Check(entries.Any(e => e.FuncCode == "06"), "есть Function 06");
            Check(entries.Any(e => e.FuncCode == "03"), "есть Function 03");

            // ===== F9.10: регрессия на реальных файлах OBSL + 2000 MCC (если есть в Work) =====
            string map2203 = Path.Combine(work, "IMCS TO DCS Register Table Modbus - OBSL2203.xlsx");
            string map3404 = Path.Combine(work, "IMCS TO DCS Register Table Modbus - OBSL3404.xlsx");
            string io2000 = Path.Combine(work, "NKNH21002-2000-ATX.LST-0003_0_EN MCC通讯清单.xlsx");
            if (File.Exists(map2203) && File.Exists(map3404) && File.Exists(io2000))
            {
                Console.WriteLine("== F9.10 Реальные файлы: OBSL2203/3404 + 2000 MCC ==");
                var obsl = new List<ModbusMapEntry>();
                var r2203 = svc.Read(map2203, obsl);
                var r3404 = svc.Read(map3404, obsl);
                Check(r2203.RowsRead >= 300 && r3404.RowsRead >= 300,
                    $"OBSL2203/3404 читаются (факт {r2203.RowsRead}/{r3404.RowsRead})");

                // VFD: две строки Function 06 у одного объекта (Frequency Setting + Control word)
                var vfdFs2203 = obsl.Where(e => e.FuncCode == "06" &&
                                                (e.Description ?? "").Contains("Frequency Setting")).ToList();
                var vfdCw2203 = obsl.Where(e => e.FuncCode == "06" &&
                                                (e.Description ?? "").Contains("Control word")).ToList();
                Check(vfdFs2203.Count > 0 && vfdCw2203.Count > 0,
                    $"OBSL: есть и Frequency Setting ({vfdFs2203.Count}), и Control word ({vfdCw2203.Count})");

                // run-сигнал из 2000 MCC → строка _STA.MCU существует в OBSL3404
                Check(obsl.Any(e => e.PlcTag == "2711-PM-1302A_STA.MCU"),
                    "OBSL3404: найдена строка 2711-PM-1302A_STA.MCU");
                // эквивалент класса BLM↔BL: карта пишет 2622-BLM-101A
                Check(obsl.Any(e => (e.ObjectTag ?? "").StartsWith("2622-BLM-101")),
                    "OBSL2203: объект 2622-BLM-101A (BLM в карте ↔ BL в IO_LIST)");

                // связывание синтетической точки 2000 MCC с реальной картой OBSL3404
                var pRunReal = MakePoint("2711-MXA-27116B", "2711-M-27116", "DI-COM",
                    "SM Transfer Pump 2711-P-1302A run signal");
                var cRunReal = ModbusLinkService.ExtractCandidates(pRunReal);
                Check(cRunReal.Count > 0 && cRunReal[0].Tag == "2711-PM-1302A",
                    $"run signal → кандидат 2711-PM-1302A (факт {cRunReal.FirstOrDefault().Tag ?? "нет"})");
                Check(ModbusLinkService.DetectRole(pRunReal) == "STA.MCU",
                    "run signal (2000 MCC) → STA.MCU");
                var pMsiReal = MakePoint("2701-MSI-31201", "2701-M-31201", "AI-COM",
                    "ITF Waste Gas Fan 2701-PK-1201B speed signal");
                var cMsiReal = ModbusLinkService.ExtractCandidates(pMsiReal);
                Check(ModbusLinkService.DetectRole(pMsiReal) == "SPEED" &&
                      cMsiReal.Count > 0 && cMsiReal[0].Tag == "2701-PK-1201B",
                    "MSI (2000 MCC) → SPEED, кандидат 2701-PK-1201B");
            }
            else
            {
                Console.WriteLine("== F9.10 Реальные файлы OBSL/2000 MCC не найдены — пропуск ==");
            }

            return RunDbPart(entries, r102.FileName, r103.FileName);
        }

        private static IoPoint MakePoint(string tag, string loop, string ioType, string service) => new()
        {
            Identification = new Identification { Tag = tag, Loop = loop, IoType = ioType, System = "РСУ" },
            Signal = new Signal { Service = service },
            Revision = new Revision { FileName = "MCC.xlsx", Sheet = "ALL" }
        };

        /// <summary>Часть смоука с реальной БД Access: MCC-строки → карта → связывание → аудит.</summary>
        private static int RunDbPart(List<ModbusMapEntry> entries, string file102, string file103)
        {
            Console.WriteLine("== F9 Связывание на реальной MCC-вкладке (tmp-БД) ==");
            var mccPoints = new List<IoPoint>
            {
                MakePoint("1115-MHS-00101A", "1115-M-00101A", "DO-COM", "1115-P-101A motor DCS command:stop"),
                MakePoint("1115-MHS-00101B", "1115-M-00101A", "DO-COM", "1115-P-101A motor DCS command:start"),
                MakePoint("1115-MXA-00101A", "1115-M-00101A", "DI-COM", "1115-P-101A motor Motor status:fault"),
                MakePoint("1115-MXA-00101B", "1115-M-00101A", "DI-COM", "1115-P-101A motor Motor status:running"),
                MakePoint("1115-MXA-00101C", "1115-M-00101A", "DI-COM", "1115-P-101A motor Motor status:ready"),
                MakePoint("1115-MII-00101", "1115-M-00101A", "AI-COM", "1115-P-101A motor Motor status:current"),
                MakePoint("1110-MHS-00101A", "1110-M-00101A", "DO-COM",
                    "1110-K-101A main driver motor (1110-KM-101A-1)DCS command:stop"),
                MakePoint("1110-MXA-00101A", "1110-M-00101A", "DI-COM",
                    "1110-K-101A main driver motor(1110-KM-101A-1) Motor status:fault"),
                // F9.3/C: DI-COM контуров ПБНН — в карте суффиксы .PLC (_Q3_F / _Q3_BYPS / _Q3_READY),
                // связывание идёт по описанию строки карты (уровень «по Description»)
                MakePoint("1120-MXA-00203A", "1120-M-00203A", "DI-COM", "1120-P-203A motor Motor status:fault"),
                MakePoint("1120-MXA-00203B", "1120-M-00203A", "DI-COM", "1120-P-203A motor Motor status:running"),
                MakePoint("1120-MXA-00203C", "1120-M-00203A", "DI-COM", "1120-P-203A motor Motor status:ready"),
                MakePoint("1120-MII-00203", "1120-M-00203A", "AI-COM", "1120-P-203A motor Motor status:current"),
                // F9.3/C2: ток насоса, строка карты — только с классом P и суффиксом .HE-JB
                MakePoint("9999-MII-9999", "9999-M-9999", "AI-COM", "9999-P-999A motor Motor status:current"),
                // F9.6/F9.7: синтетические точки — класс в карте написан иначе (PM↔M, AGM↔AG),
                // номер с ведущими нулями (9963-EM-0003A ↔ кандидат 9963-EM-3A)
                MakePoint("9961-MXA-10401", "9961-PM-401", "DI-COM", "9961-PM-401 motor Motor status:fault"),
                MakePoint("9962-MXA-10202A", "9962-AGM-1202A", "DI-COM", "9962-AGM-1202A agitator Motor status:running"),
                MakePoint("9963-MXA-1003A", "9963-EM-3A", "DI-COM", "9963-EM-3A exhauster Motor status:ready"),
                // F9.10: VFD — четыре команды делят Control word, MSC → Frequency Setting,
                // MSI → Operating Frequency, run-сигнал → _STA.MCU (Service без «running»)
                MakePoint("9964-MHS-9904A", "9964-M-9904", "DO-COM", "9964-PK-9904B fan stop"),
                MakePoint("9964-MHS-9904B", "9964-M-9904", "DO-COM", "9964-PK-9904B fan start"),
                MakePoint("9964-MHS-9904C", "9964-M-9904", "DO-COM", "9964-PK-9904B fan permissive"),
                MakePoint("9964-MHS-9904F", "9964-M-9904", "DO-COM", "9964-PK-9904B fan ban permit"),
                MakePoint("9964-MSC-9904", "9964-M-9904", "AO-COM", "9964-PK-9904B fan speed control"),
                MakePoint("9964-MSI-9904", "9964-M-9904", "AI-COM", "9964-PK-9904B fan speed signal"),
                MakePoint("9964-MXA-9904B", "9964-M-9904", "DI-COM", "9964-PK-9904B fan run signal"),
            };

            // F9.3/C2: синтетическая строка карты 9999-P-999A.HE-JB_I_A (нормализация P→PM в BaseDevice)
            entries.Add(new ModbusMapEntry
            {
                MapFile = file103,
                MapSheet = "Synthetic",
                MapRow = 999999,
                PlcTag = "9999-P-999A.HE-JB_I_A",
                ObjectTag = "9999-P-999A.HE-JB",
                Description = "9999-P-999A - synthetic pump_Phase A current",
                FuncCode = "03"
            });

            // F9.6: класс в карте написан иначе, чем в IO_LIST: 9961 PM↔M, 9962 AGM↔AG
            entries.Add(new ModbusMapEntry
            {
                MapFile = file103,
                MapSheet = "Synthetic",
                MapRow = 999997,
                PlcTag = "9961-M-401_FAULT.MCU",
                ObjectTag = "9961-M-401",
                Description = "9961-M-401 - synthetic motor Motor status:fault",
                FuncCode = "02"
            });
            entries.Add(new ModbusMapEntry
            {
                MapFile = file103,
                MapSheet = "Synthetic",
                MapRow = 999996,
                PlcTag = "9962-AG-1202A_STA.MCU",
                ObjectTag = "9962-AG-1202A",
                Description = "9962-AG-1202A - synthetic agitator Motor status:running",
                FuncCode = "02"
            });
            // F9.7: ведущие нули номера в карте: 9963-EM-0003A ↔ кандидат 9963-EM-3A
            entries.Add(new ModbusMapEntry
            {
                MapFile = file103,
                MapSheet = "Synthetic",
                MapRow = 999995,
                PlcTag = "9963-EM-0003A_REDAY.MCU",
                ObjectTag = "9963-EM-0003A",
                Description = "9963-EM-0003A - synthetic exhauster Motor status:ready",
                FuncCode = "02"
            });

            // F9.10: синтетический VFD — ДВЕ строки Function 06 (Frequency Setting + Control word),
            // статусы/скорость Function 02/03; объект «…-M», кандидат IO_LIST — без «-M» (как 2701-PK-1201B)
            entries.Add(new ModbusMapEntry
            {
                MapFile = file103,
                MapSheet = "Synthetic",
                MapRow = 999995,
                PlcTag = "9964-PK-9904B-M",
                ObjectTag = "9964-PK-9904B-M",
                Description = "9964-PK-9904B-M synthetic fan : VFD Frequency Setting:Hz",
                FuncCode = "06"
            });
            entries.Add(new ModbusMapEntry
            {
                MapFile = file103,
                MapSheet = "Synthetic",
                MapRow = 999994,
                PlcTag = "9964-PK-9904B-M",
                ObjectTag = "9964-PK-9904B-M",
                Description = "9964-PK-9904B-M synthetic fan : VFD TYPE VFD:Control word Pulse signal " +
                              ":Value:1=Stop,2=Start Forward ,4=Start Reverse,32=Reset,1024=Permit,2048=Ban Permit",
                FuncCode = "06"
            });
            entries.Add(new ModbusMapEntry
            {
                MapFile = file103,
                MapSheet = "Synthetic",
                MapRow = 999993,
                PlcTag = "9964-PK-9904B-M_STA.MCU",
                ObjectTag = "9964-PK-9904B-M",
                Description = "MCU.9964-PK-9904B-M - synthetic fan_Motor Operation 1:Running 0:Stopped",
                FuncCode = "02"
            });
            entries.Add(new ModbusMapEntry
            {
                MapFile = file103,
                MapSheet = "Synthetic",
                MapRow = 999992,
                PlcTag = "9964-PK-9904B-M_OP_F",
                ObjectTag = "9964-PK-9904B-M",
                Description = "9964-PK-9904B-M - synthetic fan_Operating Frequency",
                FuncCode = "03"
            });
            entries.Add(new ModbusMapEntry
            {
                MapFile = file103,
                MapSheet = "Synthetic",
                MapRow = 999991,
                PlcTag = "9964-PK-9904B-M_OUT_CUR",
                ObjectTag = "9964-PK-9904B-M",
                Description = "9964-PK-9904B-M - synthetic fan_Output Current",
                FuncCode = "03"
            });

            string tmpDb = Path.Combine(Path.GetTempPath(), $"iosmoke_{Guid.NewGuid():N}.accdb");
            try
            {
                var log = new EventLogger();
                var creator = new TableCreator(tmpDb);
                creator.CreateDatabase();

                // F9.3/B: миграция — MODBUS_MAP в старой структуре (без LinkState)
                using (var cn = new System.Data.OleDb.OleDbConnection(
                           $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
                {
                    cn.Open();
                    using var cmd = new System.Data.OleDb.OleDbCommand(
                        Schema.CreateModbusMapTable.Replace(", [LinkState] TEXT(255)", ""), cn);
                    cmd.ExecuteNonQuery();
                }
                creator.EnsureServiceTables();
                Check(creator.GetColumns(Schema.ModbusMapTable).Contains("LinkState"),
                    "миграция: ALTER добавил LinkState в старый MODBUS_MAP");

                creator.CreateDataTable(Schema.DefaultTable);
                var audit = new AuditService(tmpDb, log);
                var repo = new IOListRepository(tmpDb, Schema.DefaultTable, audit, log);

                repo.BeginSession();
                foreach (var p in mccPoints) repo.Upsert(p);
                repo.EndSession();

                var maps = new ModbusMapService(tmpDb);
                maps.EnsureTable();
                maps.SaveToDb(entries.Where(e => e.MapFile == file102).ToList(), file102);
                maps.SaveToDb(entries.Where(e => e.MapFile == file103).ToList(), file103);
                var mapRows = maps.LoadFromDb();
                Check(mapRows.Count == entries.Count, $"MODBUS_MAP: {mapRows.Count} == {entries.Count}");

                var linker = new ModbusLinkService(repo, maps, log, audit,
                    new ModbusLinkRuleService(tmpDb)); // F9.11: ручные правила связывания
                var linkResult = linker.Link();
                Console.WriteLine("  " + linkResult.Summary);
                Check(linkResult.Examined == mccPoints.Count, $"проверено {mccPoints.Count} (факт {linkResult.Examined})");
                Check(linkResult.Linked >= 12, $"связано ≥12 из {mccPoints.Count} (факт {linkResult.Linked})");

                // F9.4: при импорте карты M_-поля в IO_LIST НЕ записываются — только связь
                // (LinkTag/LinkState/DcsTag); адресацию пишет кнопка «Обновить адресацию Modbus»
                Check(linkResult.Errors.Count == 0,
                    $"F9.4: при импорте нет ошибок связывания (факт {linkResult.Errors.Count})");
                Check(repo.GetByTag("1115-MHS-00101A")?.Modbus?.FuncCode == null,
                    "F9.4: Link не записывает M_-поля (стоп)");
                Check(repo.GetByTag("1115-MXA-00101A")?.Modbus?.FuncCode == null,
                    "F9.4: Link не записывает M_-поля (fault)");

                Console.WriteLine("== F9.3/B LinkState строк карты ==");
                var states = maps.LoadFromDb();
                // F66: привязанными считаются оба состояния («привязано» и «привязано не по
                // полным совпадением!»), немаркированных строк после прогона не остаётся
                int linkedRows = states.Count(s => ModbusMapService.IsLinkedState(s.LinkState));
                int unlinkedRows = states.Count(s => s.LinkState == ModbusMapService.LinkStateUnlinked);
                Check(linkedRows + unlinkedRows == states.Count,
                    $"все строки размечены: {linkedRows}+{unlinkedRows} == {states.Count}");
                Check(linkResult.MapRowsLinked == linkedRows,
                    $"MapRowsLinked={linkedRows} (факт {linkResult.MapRowsLinked})");
                Check(linkResult.MapRowsUnlinked == unlinkedRows,
                    $"MapRowsUnlinked={unlinkedRows} (факт {linkResult.MapRowsUnlinked})");
                var cwRow = states.FirstOrDefault(s => s.FuncCode == "06" && s.PlcTag == "1115-PM-101A");
                Check(cwRow?.LinkState == ModbusMapService.LinkStateLinked &&
                      cwRow.LinkTag == "1115-MHS-00101A, 1115-MHS-00101B" &&
                      cwRow.DcsTag == "1115-MHS-00101A, 1115-MHS-00101B",
                    $"Control word: LinkTag/DcsTag обе команды + «привязано» (факт {cwRow?.LinkTag} / {cwRow?.DcsTag} / {cwRow?.LinkState})");
                var f203Row = states.FirstOrDefault(s => s.PlcTag == "1120-PM-203A_Q3_F.PLC");
                Check(f203Row?.LinkState == ModbusMapService.LinkStateLinked &&
                      f203Row.LinkTag == "1120-MXA-00203A",
                    $"Q3_F.PLC: привязка (факт {f203Row?.LinkTag} / {f203Row?.LinkState})");
                var unlinkedRow = states.FirstOrDefault(s => s.LinkState == ModbusMapService.LinkStateUnlinked);
                Check(unlinkedRow != null && string.IsNullOrEmpty(unlinkedRow.LinkTag),
                    $"строка без привязки помечена, LinkTag пуст (факт {unlinkedRow?.PlcTag})");
                Check(linkResult.MapUnlinkedSamples.Count > 0 &&
                      linkResult.MapUnlinkedSamples.Count <= 10,
                    $"превью без привязки: {linkResult.MapUnlinkedSamples.Count} строк (≤10)");

                // F9.3/B: повторный прогон идемпотентен — счётчики и LinkTag не растут
                var link2 = linker.Link();
                var states2 = maps.LoadFromDb();
                var cwRow2 = states2.FirstOrDefault(s => s.FuncCode == "06" && s.PlcTag == "1115-PM-101A");
                Check(link2.Linked == linkResult.Linked && link2.MapRowsLinked == linkResult.MapRowsLinked,
                    $"повторный Link идемпотентен (Linked {link2.Linked}, MapRows {link2.MapRowsLinked})");
                Check(cwRow2?.LinkTag == "1115-MHS-00101A, 1115-MHS-00101B" &&
                      cwRow2.DcsTag == "1115-MHS-00101A, 1115-MHS-00101B",
                    $"повторный Link: LinkTag/DcsTag без дублей (факт {cwRow2?.LinkTag} / {cwRow2?.DcsTag})");

                // ===== F9.4: кнопка «Обновить адресацию Modbus» — запись M_-полей по DcsTag ↔ Tag =====
                Console.WriteLine("== F9.4 Обновить адресацию Modbus ==");
                var addr = linker.UpdateAddressing();
                Console.WriteLine("  " + addr.Summary);
                Check(addr.Updated > 0 && addr.Errors.Count == 0 && addr.NotFound == 0,
                    $"адресация: обновлено {addr.Updated}, ошибок {addr.Errors.Count}, не найдено {addr.NotFound}");

                var stop = repo.GetByTag("1115-MHS-00101A");
                Check(stop?.Modbus?.FuncCode == "06", $"стоп → Function 06 (факт {stop?.Modbus?.FuncCode})");
                Check(!string.IsNullOrWhiteSpace(stop?.Modbus?.Address), $"стоп → адрес {stop?.Modbus?.Address}");
                var fault = repo.GetByTag("1115-MXA-00101A");
                Check(fault?.Modbus?.PlcTag == "1115-PM-101A_FAULT.MCU",
                    $"fault → 1115-PM-101A_FAULT.MCU (факт {fault?.Modbus?.PlcTag})");
                Check(fault?.Modbus?.SlaveStation == "IMCS EBSM102", "fault → станция EBSM102");
                var cur = repo.GetByTag("1115-MII-00101");
                Check(cur?.Modbus?.FuncCode == "03" && cur?.Modbus?.PlcTag == "1115-PM-101A_I_A",
                    $"ток → Function 03 _I_A (факт {cur?.Modbus?.FuncCode}/{cur?.Modbus?.PlcTag})");
                var km = repo.GetByTag("1110-MHS-00101A");
                Check(km?.Modbus?.PlcTag != null,
                    $"KM команда: тег заполнен ({km?.Modbus?.PlcTag}; адрес {km?.Modbus?.Address})");

                Console.WriteLine("== F9.3/C DI-COM контуров: связывание по описанию карты ==");
                var f203 = repo.GetByTag("1120-MXA-00203A");
                Check(f203?.Modbus?.PlcTag == "1120-PM-203A_Q3_F.PLC" && f203?.Modbus?.FuncCode == "02",
                    $"fault 00203 → 1120-PM-203A_Q3_F.PLC/02 (факт {f203?.Modbus?.PlcTag}/{f203?.Modbus?.FuncCode})");
                var run203 = repo.GetByTag("1120-MXA-00203B");
                Check(run203?.Modbus?.PlcTag == "1120-PM-203A_Q3_BYPS.PLC",
                    $"running 00203 → _Q3_BYPS.PLC (факт {run203?.Modbus?.PlcTag})");
                var rdy203 = repo.GetByTag("1120-MXA-00203C");
                Check(rdy203?.Modbus?.PlcTag == "1120-PM-203A_Q3_READY.PLC",
                    $"ready 00203 → _Q3_READY.PLC (факт {rdy203?.Modbus?.PlcTag})");
                Check(f203?.Modbus?.PlcTag?.Contains("QF3") != true &&
                      run203?.Modbus?.PlcTag?.Contains("QF3") != true &&
                      rdy203?.Modbus?.PlcTag?.Contains("QF3") != true,
                    "позиция автомата QF3_* не перехватывает статусы (running/ready/fault)");
                var cur203 = repo.GetByTag("1120-MII-00203");
                Check(cur203?.Modbus?.PlcTag == "1120-PM-203A_I_A",
                    $"ток 00203 → 1120-PM-203A_I_A (факт {cur203?.Modbus?.PlcTag})");

                Console.WriteLine("== F9.3/C2 BaseDevice: класс P в строке карты ==");
                var cur9999 = repo.GetByTag("9999-MII-9999");
                Check(cur9999?.Modbus?.PlcTag == "9999-P-999A.HE-JB_I_A",
                    $"ток → 9999-P-999A.HE-JB_I_A через нормализацию P→PM (факт {cur9999?.Modbus?.PlcTag})");

                // F9.4: повторное нажатие кнопки идемпотентно — без изменений Update возвращает 0
                var addr2 = linker.UpdateAddressing();
                Check(addr2.Updated == 0 && addr2.Errors.Count == 0,
                    $"повторное обновление адресации идемпотентно (Updated {addr2.Updated})");

                var hist = audit.GetHistory(field: "M_FuncCode");
                Check(hist.Count > 0 && hist.All(h => h.Action == AuditAction.Update),
                    $"аудит M_FuncCode: {hist.Count} записей Update");

                // ===== F9.4: не-Modbus-сигнал не может быть связан с картой =====
                Console.WriteLine("== F9.4 Не-Modbus сигнал (IO Type без M*/COM) ==");
                var pNonModbus = MakePoint("1115-AIN-00101", "1115-M-00101A", "AI",
                    "1115-P-101A motor Motor status:current");
                repo.BeginSession();
                repo.Upsert(pNonModbus);
                repo.EndSession();

                var linkNm = linker.Link();
                Check(linkNm.Errors.Count == 1 && linkNm.Errors[0].Contains("не Modbus-сигнал"),
                    $"импорт: связь не-Modbus отклонена (ошибок {linkNm.Errors.Count}: {linkNm.Errors.FirstOrDefault() ?? "-"})");
                var nmRow = maps.LoadFromDb().FirstOrDefault(s => s.PlcTag == "1115-PM-101A_I_A");
                Check(nmRow?.DcsTag == "1115-MII-00101",
                    $"не-Modbus тег не попал в DcsTag (факт {nmRow?.DcsTag})");
                Check(repo.GetByTag("1115-AIN-00101")?.Modbus?.FuncCode == null,
                    "не-Modbus точка: M_-поля пусты после Link");

                // DcsTag, вручную указывающий на не-Modbus точку, — ошибка кнопки, M_ не заполняются
                var manualRow = maps.LoadFromDb()
                    .First(s => s.LinkState == ModbusMapService.LinkStateUnlinked);
                using (var cnErr = new System.Data.OleDb.OleDbConnection(
                           $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
                {
                    cnErr.Open();
                    using var cmdErr = new System.Data.OleDb.OleDbCommand(
                        $"UPDATE [{Schema.ModbusMapTable}] SET [DcsTag] = ? WHERE [ID] = ?", cnErr);
                    cmdErr.Parameters.AddWithValue("@dcs", "1115-AIN-00101");
                    cmdErr.Parameters.AddWithValue("@id", manualRow.Id);
                    cmdErr.ExecuteNonQuery();
                }
                var addrErr = linker.UpdateAddressing();
                Check(addrErr.Errors.Count == 1 &&
                      addrErr.Errors[0].Contains("DCSTAG не Modbus сигнала"),
                    $"адресация: ошибка «DCSTAG не Modbus» (факт: {addrErr.Errors.FirstOrDefault() ?? "-"})");
                Check(repo.GetByTag("1115-AIN-00101")?.Modbus?.FuncCode == null,
                    "не-Modbus точка: M_-поля не заполнены кнопкой");

                // ===== F9.6/F9.7: гибкое сопоставление классов и ведущие нули номера =====
                Console.WriteLine("== F9.6/F9.7 ClassVariants / CanonTag ==");
                Check(ModbusLinkService.CanonTag("1120-PM-003A") == "1120-PM-3A",
                    "CanonTag: 003A → 3A");
                Check(ModbusLinkService.CanonTag("1120-M-00003A_TX.PLC") == "1120-M-3A_TX.PLC",
                    "CanonTag: 00003A_TX.PLC → 3A_TX.PLC");
                var vPM = ModbusLinkService.ClassVariants("1162-PM-401");
                Check(vPM.Count > 0 && vPM[0] == "1162-PM-401" && vPM.Contains("1162-M-401"),
                    $"ClassVariants(PM): точное первым + таблица → M (факт {string.Join(", ", vPM.Take(3))})");
                Check(ModbusLinkService.ClassVariants("1162-M-401").Contains("1162-PM-401"),
                    "ClassVariants(M): обратная таблица → PM");
                Check(ModbusLinkService.ClassVariants("1162-AGM-401").Contains("1162-AG-401"),
                    "ClassVariants(AGM): таблица → AG");
                Check(!ModbusLinkService.ClassVariants("1162-M-401").Any(v => v.Contains("--")),
                    "ClassVariants: пустой класс не генерируется");

                Console.WriteLine("== F9.6/F9.7 Связывание: PM↔M, AGM↔AG, 0003A↔3A ==");
                var s9961 = states.FirstOrDefault(s => s.PlcTag == "9961-M-401_FAULT.MCU");
                Check(s9961?.LinkState == ModbusMapService.LinkStateLinked &&
                      s9961.LinkTag == "9961-MXA-10401",
                    $"PM↔M: строка карты «9961-M-401» привязана к «9961-PM-401» (факт {s9961?.LinkTag}/{s9961?.LinkState})");
                var p9961 = repo.GetByTag("9961-MXA-10401");
                Check(p9961?.Modbus?.PlcTag == "9961-M-401_FAULT.MCU" && p9961?.Modbus?.FuncCode == "02",
                    $"PM↔M: адресация fault (факт {p9961?.Modbus?.PlcTag}/{p9961?.Modbus?.FuncCode})");
                var p9962 = repo.GetByTag("9962-MXA-10202A");
                Check(p9962?.Modbus?.PlcTag == "9962-AG-1202A_STA.MCU" && p9962?.Modbus?.FuncCode == "02",
                    $"AGM↔AG: адресация running (факт {p9962?.Modbus?.PlcTag}/{p9962?.Modbus?.FuncCode})");
                var p9963 = repo.GetByTag("9963-MXA-1003A");
                Check(p9963?.Modbus?.PlcTag == "9963-EM-0003A_REDAY.MCU" && p9963?.Modbus?.FuncCode == "02",
                    $"0003A↔3A: адресация ready (факт {p9963?.Modbus?.PlcTag}/{p9963?.Modbus?.FuncCode})");

                // ===== F9.10: VFD — Control word против Frequency Setting, MSC/MSI, run-сигнал =====
                Console.WriteLine("== F9.10 VFD: команды/скорость/run ==");
                var st10 = maps.LoadFromDb();
                var vfdCw = st10.FirstOrDefault(s => s.FuncCode == "06" && s.PlcTag == "9964-PK-9904B-M" &&
                                                     (s.Description ?? "").Contains("Control word"));
                // F66: VFD-строки привязываются фазой 2 (полного совпадения имени нет —
                // «9964-PK-9904B» против ObjectTag «9964-PK-9904B-M») → маркер отклонения
                Check(vfdCw?.LinkState == ModbusMapService.LinkStateLinkedApprox &&
                      vfdCw.LinkTag == "9964-MHS-9904A, 9964-MHS-9904B, 9964-MHS-9904C, 9964-MHS-9904F",
                    $"F9.10: Control word — 4 команды, LinkState F66 (факт {vfdCw?.LinkTag} / {vfdCw?.LinkState})");
                var vfdFs = st10.FirstOrDefault(s => s.FuncCode == "06" && s.PlcTag == "9964-PK-9904B-M" &&
                                                     (s.Description ?? "").Contains("Frequency Setting"));
                Check(vfdFs?.LinkState == ModbusMapService.LinkStateLinkedApprox &&
                      vfdFs.LinkTag == "9964-MSC-9904",
                    $"F9.10: Frequency Setting ← только MSC, LinkState F66 (факт {vfdFs?.LinkTag} / {vfdFs?.LinkState})");
                var vfdOpF = st10.FirstOrDefault(s => s.PlcTag == "9964-PK-9904B-M_OP_F");
                Check(vfdOpF?.LinkTag == "9964-MSI-9904",
                    $"F9.10: MSI → Operating Frequency (факт {vfdOpF?.LinkTag})");
                var vfdCur = st10.FirstOrDefault(s => s.PlcTag == "9964-PK-9904B-M_OUT_CUR");
                Check(vfdCur?.LinkState == ModbusMapService.LinkStateUnlinked,
                    $"F9.10: Output Current не занят MSI (факт {vfdCur?.LinkState} / {vfdCur?.LinkTag})");
                var vfdSta = st10.FirstOrDefault(s => s.PlcTag == "9964-PK-9904B-M_STA.MCU");
                Check(vfdSta?.LinkTag == "9964-MXA-9904B",
                    $"F9.10: run signal → _STA.MCU (факт {vfdSta?.LinkTag})");

                // ===== F9.8: тег из DcsTag, отсутствующий в IO_LIST → NotFound + предупреждение в аудит =====
                var missingRow = maps.LoadFromDb()
                    .First(s => s.LinkState == ModbusMapService.LinkStateUnlinked && s.Id != manualRow.Id);
                using (var cnMiss = new System.Data.OleDb.OleDbConnection(
                           $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
                {
                    cnMiss.Open();
                    using var cmdMiss = new System.Data.OleDb.OleDbCommand(
                        $"UPDATE [{Schema.ModbusMapTable}] SET [DcsTag] = ? WHERE [ID] = ?", cnMiss);
                    cmdMiss.Parameters.AddWithValue("@dcs", "NO-SUCH-TAG-1");
                    cmdMiss.Parameters.AddWithValue("@id", missingRow.Id);
                    cmdMiss.ExecuteNonQuery();
                }
                var addrMiss = linker.UpdateAddressing();
                Check(addrMiss.NotFound == 1,
                    $"адресация: NotFound=1 для NO-SUCH-TAG-1 (факт {addrMiss.NotFound})");

                Console.WriteLine("== F9.8 Сообщения связывания в аудит (IO_LIST_Audit) ==");
                var modbusHist = audit.GetHistory(field: "Modbus");
                Check(modbusHist.Count > 0 && modbusHist.All(h => h.System == "Modbus"),
                    $"аудит Modbus: все записи с System=«Modbus» ({modbusHist.Count} шт.)");
                Check(modbusHist.Any(h => h.Action == AuditAction.ModbusError &&
                                          h.Tag == "1115-AIN-00101" &&
                                          h.NewValue != null && h.NewValue.Contains("не Modbus-сигнал")),
                    "аудит: ModbusError «связь отклонена … не Modbus-сигнал» (Link)");
                Check(modbusHist.Any(h => h.Action == AuditAction.ModbusError &&
                                          h.NewValue != null && h.NewValue.Contains("DCSTAG не Modbus сигнала")),
                    "аудит: ModbusError «DCSTAG не Modbus сигнала» (UpdateAddressing)");
                Check(modbusHist.Any(h => h.Action == AuditAction.ModbusWarning &&
                                          h.Tag == "NO-SUCH-TAG-1" &&
                                          h.NewValue != null && h.NewValue.Contains("не найден")),
                    "аудит: ModbusWarning «тег из DcsTag не найден»");
                var warnRec = modbusHist.First(h => h.Action == AuditAction.ModbusWarning);
                Check(warnRec.ChangeDescription.Contains("предупреждение связывания Modbus"),
                    $"ChangeDescription: {warnRec.ChangeDescription}");
                var sumMod = audit.GetSummary();
                Check(sumMod.Any(s => s.Action == AuditAction.ModbusWarning) &&
                      sumMod.Any(s => s.Action == AuditAction.ModbusError),
                    "GetSummary учитывает ModbusWarning/ModbusError");
                var repMod = ReportBuilder.Build(sumMod);
                Check(repMod.Sum(r => r.ModbusWarningCount) > 0 && repMod.Sum(r => r.ModbusErrorCount) > 0,
                    "ReportBuilder считает ModbusWarningCount/ModbusErrorCount");

                // ===== F9.9: глобальный переключатель аудита — при выключении минимизация в журнал =====
                Console.WriteLine("== F9.9 Аудит выключен: только теги в журнале, истории нет ==");
                int histTotalBefore = audit.GetSummary().Sum(s => s.Count);
                int modbusAuditBefore = audit.GetHistory(field: "Modbus").Count;
                int logBefore = log.Entries.Count;
                audit.Enabled = false;

                // (а) единичная запись вне пакета — в истории пусто, в журнале только имя тега
                audit.Record(Schema.DefaultTable, "TEST-TAG-F99", AuditAction.Update, "Service", "a", "b");
                Check(audit.GetSummary().Sum(s => s.Count) == histTotalBefore,
                    "аудит выключен: Record не пишет в IO_LIST_Audit");
                Check(log.Entries.Any(e => e.Message.Contains("Аудит выключен") &&
                                           e.Message.Contains("TEST-TAG-F99")),
                    "аудит выключен: журнал содержит только имя тега TEST-TAG-F99");

                // (б) пакет (режим импорта/сессии) — сводная строка журнала со списком тегов
                audit.BeginBatch();
                audit.Record(Schema.DefaultTable, "BATCH-TAG-1", AuditAction.Insert, "Tag", null, "x");
                audit.Record(Schema.DefaultTable, "BATCH-TAG-2", AuditAction.Update, "Service", "a", "b");
                audit.EndBatch();
                Check(audit.GetSummary().Sum(s => s.Count) == histTotalBefore,
                    "аудит выключен: пакет не записан в IO_LIST_Audit");
                Check(log.Entries.Any(e => e.Message.Contains("BATCH-TAG-1") &&
                                           e.Message.Contains("BATCH-TAG-2") &&
                                           e.Message.Contains("не записаны в историю")),
                    "аудит выключен: сводка журнала содержит оба тега пакета");

                // (в) связывание при выключенном аудите — сообщения F9.8 в историю не попадают
                var linkNoAudit = linker.Link();
                Check(linkNoAudit.Errors.Count == 1,
                    $"Link с выключенным аудитом: ошибки связывания формируются ({linkNoAudit.Errors.Count})");
                Check(audit.GetHistory(field: "Modbus").Count == modbusAuditBefore,
                    "аудит выключен: сообщения связывания не попали в IO_LIST_Audit");

                audit.Enabled = true; // восстановить для последующих проверок (F9.1)
                Check(log.Entries.Count > logBefore,
                    "журнал событий пополнялся при выключенном аудите (минимизация)");

                // ===== F9.11: ручные правила связывания MODBUS_LINK_RULES =====
                Console.WriteLine("== F9.11 Ручные правила связывания (MODBUS_LINK_RULES) ==");

                // (1) схема: DDL + идемпотентная миграция таблицы правил
                Check(Schema.CreateModbusLinkRulesTable.Contains("[MODBUS_LINK_RULES]"),
                    "DDL MODBUS_LINK_RULES содержит имя таблицы");
                Check(creator.TableExists(Schema.ModbusLinkRulesTable),
                    "таблица правил создана EnsureServiceTables");

                // (2) матчинг шаблонов: wildcard / regex / защита от невалидного regex
                Check(ModbusLinkRuleService.PatternMatch("Motor status:Fault", "*fault*"),
                    "wildcard *fault* совпадает (IgnoreCase)");
                Check(!ModbusLinkRuleService.PatternMatch("running", "*fault*"),
                    "wildcard *fault* не совпадает с «running»");
                Check(ModbusLinkRuleService.PatternMatch("2711-MXA-27116A", "MXA.*A") &&
                      ModbusLinkRuleService.PatternMatch("2711-MXA-27116A", "*MXA*A"),
                    "regex «MXA.*A» и wildcard «*MXA*A» ловят MXA-тег");
                Check(ModbusLinkRuleService.ValidatePattern("MXA+A") == null,
                    "regex «MXA+A» валиден");
                Check(ModbusLinkRuleService.ValidatePattern("[Fault") != null,
                    "незакрытая скобка → ошибка валидации");
                Check(!ModbusLinkRuleService.PatternMatch("x", "[Fault"),
                    "невалидный шаблон → false (связывание не падает)");

                // (3) валидация правил
                var vRule = new ModbusLinkRule { Name = "", TagPattern = "*MXA*", MapField = "PlcTag", ValuePattern = "*TX*" };
                Check(ModbusLinkRuleService.Validate(vRule) != null, "правило без имени → ошибка");
                vRule.Name = "x"; vRule.MapField = "NoSuchField";
                Check(ModbusLinkRuleService.Validate(vRule) != null, "неизвестное поле карты → ошибка");
                vRule.MapField = "PlcTag"; vRule.TagPattern = "*MXA*"; vRule.ValuePattern = "*_TX.MCU*";
                Check(ModbusLinkRuleService.Validate(vRule) == null, "валидное правило → без ошибок");

                // (4) CRUD round-trip + фильтр LoadEnabled
                var ruleSvc = new ModbusLinkRuleService(tmpDb);
                ruleSvc.SaveAll(new[]
                {
                    new ModbusLinkRule { Name = "MXA→TX", IOField = "Tag", TagPattern = "1115-MXA-00101A",
                        MapField = "PlcTag", ValuePattern = "*_TX.MCU*", Priority = 5, Enabled = true }
                });
                var loadedRules = ruleSvc.Load();
                Check(loadedRules.Count == 1 &&
                      loadedRules[0].Name == "MXA→TX" && loadedRules[0].IOField == "Tag" &&
                      loadedRules[0].TagPattern == "1115-MXA-00101A" && loadedRules[0].MapField == "PlcTag" &&
                      loadedRules[0].ValuePattern == "*_TX.MCU*" && loadedRules[0].Priority == 5 &&
                      loadedRules[0].Enabled,
                    "SaveAll → Load: все поля правила сохранились");
                Check(ruleSvc.LoadEnabled().Count == 1, "LoadEnabled: включённое правило возвращается");

                // (5) правило применяется РАНЬШЕ DetectRole: fault-точка 1115-MXA-00101A
                //     уходит со строки _FAULT.MCU (встроенная роль) на строку _TX.MCU
                var linkR1 = linker.Link();
                var stR1 = maps.LoadFromDb();
                var txRow = stR1.FirstOrDefault(s => s.PlcTag == "1115-PM-101A_TX.MCU");
                Check(txRow?.LinkTag == "1115-MXA-00101A" &&
                      txRow.LinkState == ModbusMapService.LinkStateLinked,
                    $"F9.11: правило MXA→TX перехватило точку (факт {txRow?.LinkTag} / {txRow?.LinkState})");
                var faultRow = stR1.FirstOrDefault(s => s.PlcTag == "1115-PM-101A_FAULT.MCU");
                Check(string.IsNullOrEmpty(faultRow?.LinkTag),
                    $"F9.11: _FAULT.MCU потеряла точку (правило приоритетнее роли) (факт «{faultRow?.LinkTag}»)");
                bool hit1 = linkR1.RuleHits.TryGetValue("MXA→TX", out int h1);
                Check(linkR1.LinkedByRule == 1 && hit1 && h1 == 1,
                    $"F9.11: LinkedByRule=1, RuleHits[MXA→TX]=1 (факт {linkR1.LinkedByRule} / {(hit1 ? h1 : 0)})");
                Check(log.Entries.Any(e => e.Message.Contains("F9.11") && e.Message.Contains("MXA→TX")),
                    "F9.11: журнал сообщает о применении правил");

                // (6) приоритет двух правил: меньший Priority побеждает
                ruleSvc.SaveAll(new[]
                {
                    new ModbusLinkRule { Name = "MXA→TX", IOField = "Tag", TagPattern = "1115-MXA-00101A",
                        MapField = "PlcTag", ValuePattern = "*_TX.MCU*", Priority = 5, Enabled = true },
                    new ModbusLinkRule { Name = "MXA→STA", IOField = "Tag", TagPattern = "1115-MXA-00101A",
                        MapField = "PlcTag", ValuePattern = "*_STA.MCU*", Priority = 10, Enabled = true }
                });
                linker.Link();
                var stR2 = maps.LoadFromDb();
                var txRow2 = stR2.FirstOrDefault(s => s.PlcTag == "1115-PM-101A_TX.MCU");
                Check(txRow2?.LinkTag == "1115-MXA-00101A",
                    $"F9.11: Priority 5 < 10 → побеждает MXA→TX (факт «{txRow2?.LinkTag}»)");

                // (7) фильтр FuncCode правила: строка _TX.MCU имеет FuncCode 02 —
                //     правило с FuncCode=06 не срабатывает, точка уходит по встроенной роли
                ruleSvc.SaveAll(new[]
                {
                    new ModbusLinkRule { Name = "MXA→TX только 06", IOField = "Tag", TagPattern = "1115-MXA-00101A",
                        MapField = "PlcTag", ValuePattern = "*_TX.MCU*", FuncCode = "06", Priority = 5, Enabled = true }
                });
                var linkR3 = linker.Link();
                var stR3 = maps.LoadFromDb();
                var faultRow3 = stR3.FirstOrDefault(s => s.PlcTag == "1115-PM-101A_FAULT.MCU");
                Check(linkR3.LinkedByRule == 0,
                    $"F9.11: FuncCode-фильтр 06 не совпал со строкой 02 → правила не сработали (факт {linkR3.LinkedByRule})");
                Check(faultRow3?.LinkTag == "1115-MXA-00101A",
                    $"F9.11: без срабатывания правила точка вернулась в _FAULT.MCU (факт «{faultRow3?.LinkTag}»)");

                // (8) выключенное правило (Enabled=0) не применяется
                ruleSvc.SaveAll(new[]
                {
                    new ModbusLinkRule { Name = "MXA→TX выкл", IOField = "Tag", TagPattern = "1115-MXA-00101A",
                        MapField = "PlcTag", ValuePattern = "*_TX.MCU*", Priority = 5, Enabled = false }
                });
                Check(ruleSvc.LoadEnabled().Count == 0, "F9.11: LoadEnabled не возвращает выключенное правило");
                var linkR4 = linker.Link();
                Check(linkR4.LinkedByRule == 0,
                    $"F9.11: выключенное правило игнорируется (LinkedByRule={linkR4.LinkedByRule})");

                // (9) очистка: без правил — прежнее поведение (fault → _FAULT.MCU)
                ruleSvc.SaveAll(Array.Empty<ModbusLinkRule>());
                linker.Link();
                var stClean = maps.LoadFromDb();
                var faultClean = stClean.FirstOrDefault(s => s.PlcTag == "1115-PM-101A_FAULT.MCU");
                Check(faultClean?.LinkTag == "1115-MXA-00101A",
                    $"F9.11: после удаления правил встроенная роль снова привязала fault (факт «{faultClean?.LinkTag}»)");

                // (10) миграция: удаление таблицы правил и воссоздание EnsureServiceTables
                using (var cnR = new System.Data.OleDb.OleDbConnection(
                           $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
                {
                    cnR.Open();
                    using var cmdDrop = cnR.CreateCommand();
                    cmdDrop.CommandText = $"DROP TABLE [{Schema.ModbusLinkRulesTable}]";
                    cmdDrop.ExecuteNonQuery();
                }
                Check(!creator.TableExists(Schema.ModbusLinkRulesTable), "F9.11: таблица правил удалена");
                creator.EnsureServiceTables();
                Check(creator.TableExists(Schema.ModbusLinkRulesTable),
                    "F9.11: миграция — EnsureServiceTables воссоздаёт MODBUS_LINK_RULES");

                RunF12Part(tmpDb, creator); // F12: генерация перечня Тех.объекты (счётчики — общие)
                RunF13Part(tmpDb, creator); // F13: генерация блоков Modbus (XML) (счётчики — общие)
                RunF51Part(tmpDb, creator); // F51/F52/F53: шаблоны на Typno, мастер-объекты, станции
                RunF65Part(tmpDb, creator); // F65: генерация экземпляров типовых алгоритмов
                RunF55Part(); // F55: полное совпадение имени в связывании Modbus (своя tmp-БД)
                RunF58Part(); // F58: StationPackets — пакеты станции для превью тегов
                RunF59Part(); // F59: режимы связывания Modbus (полное обновление / только пустые)
                RunF66Part(); // F66: семейство объекта + LinkState «привязано не по полным совпадением!»
                RunF67Part(); // F67: Loop-подстановка — первый символ класса ключевой (не «дотягаться» до чужого объекта)
                return RunDuplicatePart(repo, audit);
            }
            finally
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); // освободить handle ACE
                try { File.Delete(tmpDb); } catch { /* мог остаться lock */ }
            }
        }

        /// <summary>F55: связывание в три фазы — полное совпадение имени (FullKey, «по буквам»),
        /// закрепление совпавших строк («не трогать») и матчинг «максимальное количество букв».</summary>
        private static void RunF55Part()
        {
            Console.WriteLine("== F55 Полное совпадение имени и матчинг «максимум букв» ==");
            string tmp55 = Path.Combine(Path.GetTempPath(), $"iosmoke_{Guid.NewGuid():N}.accdb");
            try
            {
                var log55 = new EventLogger();
                var creator55 = new TableCreator(tmp55);
                creator55.CreateDatabase();
                creator55.EnsureServiceTables();
                creator55.CreateDataTable(Schema.DefaultTable);
                var audit55 = new AuditService(tmp55, log55);
                var repo55 = new IOListRepository(tmp55, Schema.DefaultTable, audit55, log55);
                var maps55 = new ModbusMapService(tmp55);
                maps55.EnsureTable();

                // Карта (ObjectTag пересчитывается из PlcTag при чтении — DeriveObjectTag):
                // R1 — JB-строка полного имени в P-форме (с пробелом в описании, как в реале);
                // R2 — дефисная форма B (1612-PM-611B-HE-JB); R3/R4 — свободные .HE01-строки
                // мотора A; R10 — .HE-JB1 (без разделителя: не ловится префиксным уровнем);
                // R_C — контрольная dot-строка PM-формы (никем не закрепляется)
                const string f55File = "F55.xlsx";
                const string f55Sheet = "Register Table Function 02/03/06";
                var map55 = new List<ModbusMapEntry>
                {
                    new() { MapFile = f55File, MapSheet = f55Sheet, MapRow = 3, SlaveStation = "IMCS F55",
                        FuncCode = "02",
                        PlcTag = "MCU.1612-P-611A.HE-JB - Oil heater junction box_MCCB POSITION.MCU",
                        Description = "1612-P-611A.HE-JB - MCCB position" },
                    new() { MapFile = f55File, MapSheet = f55Sheet, MapRow = 4, SlaveStation = "IMCS F55",
                        FuncCode = "02", PlcTag = "1612-PM-611B-HE-JB_MCCB POSITION.MCU",
                        Description = "1612-PM-611B-HE-JB - MCCB position" },
                    new() { MapFile = f55File, MapSheet = f55Sheet, MapRow = 5, SlaveStation = "IMCS F55",
                        FuncCode = "02", PlcTag = "1612-PM-611A.HE01_STA.MCU", Description = "running" },
                    new() { MapFile = f55File, MapSheet = f55Sheet, MapRow = 6, SlaveStation = "IMCS F55",
                        FuncCode = "02", PlcTag = "1612-PM-611A.HE01_FAULT.MCU", Description = "fault" },
                    new() { MapFile = f55File, MapSheet = f55Sheet, MapRow = 7, SlaveStation = "IMCS F55",
                        FuncCode = "02", PlcTag = "1612-PM-611A.HE-JB1_STA.MCU", Description = "running" },
                    new() { MapFile = f55File, MapSheet = f55Sheet, MapRow = 8, SlaveStation = "IMCS F55",
                        FuncCode = "02", PlcTag = "1612-PM-611C.HE-JB_MCCB POSITION.MCU",
                        Description = "1612-PM-611C.HE-JB - MCCB position" },
                };
                maps55.SaveToDb(map55, f55File);
                var pts55 = new List<IoPoint>
                {
                    // полное имя в теге (P-форма и PM-форма) → одна и та же строка R1 (фаза 1)
                    MakePoint("1612-P-611A.HE-JB", "1612-M-611A", "MDI",
                        "1612-P-611A.HE-JB junction box MCCB position"),
                    MakePoint("1612-PM-611A.HE-JB", "1612-M-611A", "MDI",
                        "1612-PM-611A.HE-JB junction box MCCB position"),
                    // мотор A: fault → свободная .HE01_FAULT (НЕ JB-строка)
                    MakePoint("1612-MXA-611A", "1612-M-611A", "DI-COM",
                        "1612-P-611A motor Motor status:fault"),
                    // базовый кандидат без полного имени: R1 закреплена → «Не связано»
                    MakePoint("1612-MDI-611AM", "1612-M-611A", "DI-COM",
                        "1612-P-611A motor MCCB position"),
                    // B: дефисная форма полного имени → R2 (фаза 1); базовый кандидат → «Не связано»
                    MakePoint("1612-PM-611B-HE-JB", "1612-M-611B", "MDI",
                        "1612-PM-611B-HE-JB junction box MCCB position"),
                    MakePoint("1612-MDI-611BM", "1612-M-611B", "DI-COM",
                        "1612-PM-611B motor MCCB position"),
                    // C: контроль — незакреплённая dot-строка PM-формы ловится базовым кандидатом
                    MakePoint("1612-MDI-611CM", "1612-M-611C", "DI-COM",
                        "1612-PM-611C motor MCCB position"),
                    // «максимум букв»: точного совпадения нет — фаза 2, уровень 3: .HE-JB1_STA
                    // (общий префикс 14 букв) важнее .HE01_STA (10 букв; прежний ранжир взял бы его)
                    MakePoint("1612-MXA-611AS", "1612-M-611A", "DI-COM",
                        "1612-PM-611A.HE-JB motor Motor status:running"),
                };
                repo55.BeginSession();
                foreach (var p in pts55) repo55.Upsert(p);
                repo55.EndSession();

                var linker55 = new ModbusLinkService(repo55, maps55, log55, audit55,
                    new ModbusLinkRuleService(tmp55));
                var res55 = linker55.Link();
                Console.WriteLine("  " + res55.Summary);
                var rows55 = maps55.LoadFromDb();
                ModbusMapEntry? Row55(string plc) => rows55.FirstOrDefault(r => r.PlcTag == plc);
                const string r1Plc = "MCU.1612-P-611A.HE-JB - Oil heater junction box_MCCB POSITION.MCU";

                Check(res55.Errors.Count == 0, $"F55: ошибок связывания нет (факт {res55.Errors.Count})");
                Check(res55.Examined == pts55.Count, $"F55: проверено {pts55.Count} (факт {res55.Examined})");
                Check(res55.Linked == 6 && res55.Unlinked == 2,
                    $"F55: связано 6, не связано 2 (факт {res55.Linked}/{res55.Unlinked})");
                Check(res55.LinkedFullMatch == 3,
                    $"F55: полных совпадений 3 — P-форма, PM-форма, дефисная B (факт {res55.LinkedFullMatch})");

                // (1) P/PM-идентичность полного имени: обе формы тега делят закреплённую JB-строку
                var r1 = Row55(r1Plc);
                Check(r1?.LinkTag == "1612-P-611A.HE-JB, 1612-PM-611A.HE-JB" &&
                      r1.LinkState == ModbusMapService.LinkStateLinked,
                    $"F55(1): полное имя (P и PM) → одна строка R1 (факт «{r1?.LinkTag}»)");
                Check(r1?.DcsTag == "1612-P-611A.HE-JB, 1612-PM-611A.HE-JB",
                    $"F55(1): DcsTag R1 без посторонних тегов (факт «{r1?.DcsTag}»)");

                // (2) дефисная форма полного имени — идентична точечной
                var r2 = Row55("1612-PM-611B-HE-JB_MCCB POSITION.MCU");
                Check(r2?.LinkTag == "1612-PM-611B-HE-JB",
                    $"F55(2): 1612-PM-611B-HE-JB (дефис) → полное совпадение (факт «{r2?.LinkTag}»)");

                // (3) мотор A не крадёт JB-строку: fault → свободная .HE01_FAULT
                var r4 = Row55("1612-PM-611A.HE01_FAULT.MCU");
                Check(r4?.LinkTag == "1612-MXA-611A",
                    $"F55(3): мотор fault → .HE01_FAULT (факт «{r4?.LinkTag}»)");
                Check(r1?.LinkTag?.Contains("1612-MXA-611A") != true,
                    "F55(3): тег мотора НЕ дописан в закреплённую JB-строку");

                // (4) базовый кандидат мимо закреплённой строки → «Не связано» (старое поведение
                //     связывало бы его с R1/R2); контрольная C-строка (не закреплена) ловится
                Check(res55.UnlinkedSamples.Any(s => s.StartsWith("1612-MDI-611AM")),
                    "F55(4): 1612-MDI-611AM не связан (R1 закреплена); факты: " +
                    string.Join("; ", res55.UnlinkedSamples));
                Check(res55.UnlinkedSamples.Any(s => s.StartsWith("1612-MDI-611BM")),
                    "F55(4): 1612-MDI-611BM не связан (R2 закреплена дефисным полным именем)");
                var rC = Row55("1612-PM-611C.HE-JB_MCCB POSITION.MCU");
                Check(rC?.LinkTag == "1612-MDI-611CM",
                    $"F55(4): контроль — незакреплённая строка ловится базовым кандидатом (факт «{rC?.LinkTag}»)");

                // (5) «максимальное количество букв»: .HE-JB1 (14) важнее .HE01 (10)
                var r10 = Row55("1612-PM-611A.HE-JB1_STA.MCU");
                var r3 = Row55("1612-PM-611A.HE01_STA.MCU");
                Check(r10?.LinkTag == "1612-MXA-611AS",
                    $"F55(5): running → .HE-JB1_STA по максимуму букв (факт «{r10?.LinkTag}»)");
                Check(string.IsNullOrEmpty(r3?.LinkTag),
                    $"F55(5): .HE01_STA не получила тег (факт «{r3?.LinkTag}»)");

                // (6) F9.4: адресация по DcsTag → M_-поля; идемпотентность повторного связывания
                var addr55 = linker55.UpdateAddressing();
                Check(addr55.Updated == 6 && addr55.NotFound == 0 && addr55.Errors.Count == 0,
                    $"F55(6): адресация — 6 точек, без потерь " +
                    $"(факт {addr55.Updated}/{addr55.NotFound}/{addr55.Errors.Count})");
                var p1_55 = repo55.GetByTag("1612-P-611A.HE-JB");
                Check(p1_55?.Modbus?.FuncCode == "02" && p1_55.Modbus.PlcTag == r1Plc,
                    $"F55(6): M_-поля полного имени (факт {p1_55?.Modbus?.FuncCode}/{p1_55?.Modbus?.PlcTag})");

                var res55b = linker55.Link();
                var r1b = maps55.LoadFromDb().FirstOrDefault(r => r.PlcTag == r1Plc);
                Check(res55b.Linked == res55.Linked && res55b.MapRowsLinked == res55.MapRowsLinked &&
                      r1b?.LinkTag == r1?.LinkTag,
                    $"F55(6): повторный Link идемпотентен " +
                    $"(факт {res55b.Linked}/{res55b.MapRowsLinked}, R1 «{r1b?.LinkTag}»)");

                Check(log55.Entries.Any(e => e.Message.Contains("F55")),
                    "F55: журнал сообщает о полных совпадениях");
            }
            finally
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); // освободить handle ACE
                try { File.Delete(tmp55); } catch { /* мог остаться lock */ }
            }
        }

        /// <summary>F58: StationPackets — отбор пакетов станции для превью «все теги»
        /// (точный ключ «Controller|Station», затем универсальный; Generate не учитывается).</summary>
        private static void RunF58Part()
        {
            Console.WriteLine("== F58 Пакеты станции для превью тегов ==");
            var pax = new MbPacketPlan { No = 1, Controller = "PLC-1", Station = "IMCS A" };
            var pbx = new MbPacketPlan { No = 2, Controller = "PLC-2", Station = "IMCS A" };
            var pay = new MbPacketPlan { No = 3, Controller = "PLC-1", Station = "IMCS B" };
            var packets = new List<MbPacketPlan> { pax, pbx, pay };

            var exact = ModbusBlockPlannerService.StationPackets(packets, "PLC-1", "IMCS A");
            Check(exact.Count == 1 && ReferenceEquals(exact[0], pax),
                "F58: точный ключ «Контроллер|Станция» — только пакеты PLC-1|IMCS A");

            var any = ModbusBlockPlannerService.StationPackets(packets, "", "imcs a");
            Check(any.Count == 2 && any.Contains(pax) && any.Contains(pbx),
                "F58: пустой контроллер — все контроллеры станции (регистр не важен)");

            var fallback = ModbusBlockPlannerService.StationPackets(packets, "PLC-9", "IMCS A");
            Check(fallback.Count == 2 && fallback.Contains(pax) && fallback.Contains(pbx),
                "F58: неизвестный контроллер → универсальный фильтр станции");

            var all = ModbusBlockPlannerService.StationPackets(packets, "PLC-1", "");
            Check(all.Count == 3, "F58: пустая станция — все пакеты плана");

            var none = ModbusBlockPlannerService.StationPackets(packets, "PLC-1", "IMCS Z");
            Check(none.Count == 0, "F58: станция без пакетов — пустой список");

            // строка тега ссылается на ТОТ ЖЕ экземпляр пакета — F34-правки пакета общие
            var b = new MbBinding
            {
                Tag = "1131-MXA-0131A", Typno = "PID1", DataType = "16-Bit Int",
                Width = 1, Address = 579, Offset = 3,
                BlockName = "MB_1", DataOutExpr = "MB_1.DataOut[3]"
            };
            b.SharedTags.Add("1131-MXA-0131B");
            pax.Bindings.Add(b);
            var row = new MbStationTagRow { Packet = pax, Tag = b.Tag, Path = b.DataOutExpr };
            Check(ReferenceEquals(row.Packet, pax) && row.Tag == "1131-MXA-0131A" &&
                  b.SharedTags.Count == 1,
                "F58: MbStationTagRow ссылается на исходный пакет плана (F34-правки общие)");
        }

        /// <summary>F59: два режима связывания — Full (полное обновление: перезапись LinkTag/
        /// DcsTag/LinkState, устаревшие DcsTag стираются — остаток F55) и FillEmpty (заполняются
        /// только пустые строки карты; заполненные — ручные/авто — сохраняются).</summary>
        private static void RunF59Part()
        {
            Console.WriteLine("== F59 Режимы связывания: полное обновление / только пустые ==");
            string tmp59 = Path.Combine(Path.GetTempPath(), $"iosmoke_{Guid.NewGuid():N}.accdb");
            try
            {
                var log59 = new EventLogger();
                var creator59 = new TableCreator(tmp59);
                creator59.CreateDatabase();
                creator59.EnsureServiceTables();
                creator59.CreateDataTable(Schema.DefaultTable);
                var audit59 = new AuditService(tmp59, log59);
                var repo59 = new IOListRepository(tmp59, Schema.DefaultTable, audit59, log59);
                var maps59 = new ModbusMapService(tmp59);
                maps59.EnsureTable();

                const string f59File = "F59.xlsx";
                const string f59Sheet = "Register Table Function 02/03/06";
                var map59 = new List<ModbusMapEntry>
                {
                    new() { MapFile = f59File, MapSheet = f59Sheet, MapRow = 3, SlaveStation = "IMCS F59",
                        FuncCode = "02", PlcTag = "1131-PM-101A_STA.MCU", Description = "running" },
                    new() { MapFile = f59File, MapSheet = f59Sheet, MapRow = 4, SlaveStation = "IMCS F59",
                        FuncCode = "02", PlcTag = "1131-PM-102A_STA.MCU", Description = "running" },
                    new() { MapFile = f59File, MapSheet = f59Sheet, MapRow = 5, SlaveStation = "IMCS F59",
                        FuncCode = "02", PlcTag = "1131-PM-103A_STA.MCU", Description = "running" },
                };
                maps59.SaveToDb(map59, f59File);
                var pts59 = new List<IoPoint>
                {
                    MakePoint("1131-MXA-101A", "1131-M-101A", "DI-COM", "1131-P-101A motor Motor status:running"),
                    MakePoint("1131-MXA-102A", "1131-M-102A", "DI-COM", "1131-P-102A motor Motor status:running"),
                    MakePoint("1131-MXA-103A", "1131-M-103A", "DI-COM", "1131-P-103A motor Motor status:running"),
                };
                repo59.BeginSession();
                foreach (var p in pts59) repo59.Upsert(p);
                repo59.EndSession();

                var linker59 = new ModbusLinkService(repo59, maps59, log59, audit59,
                    new ModbusLinkRuleService(tmp59));

                // ===== (1) полное обновление: связывает всё, DcsTag = теги точек =====
                var resFull = linker59.Link(ModbusLinkMode.Full);
                var rows59 = maps59.LoadFromDb();
                ModbusMapEntry? Row59(string plc) => rows59.FirstOrDefault(r => r.PlcTag == plc);
                var m101 = Row59("1131-PM-101A_STA.MCU");
                var m103 = Row59("1131-PM-103A_STA.MCU");
                Check(resFull.Mode == ModbusLinkMode.Full && resFull.Linked == 3 &&
                      resFull.PreservedFilled == 0,
                    $"F59(1): полное обновление — связано 3 (факт {resFull.Linked}, режим {resFull.Mode})");
                Check(m101?.LinkTag == "1131-MXA-101A" && m101.DcsTag == "1131-MXA-101A",
                    $"F59(1): строки 101 LinkTag/DcsTag (факт «{m101?.LinkTag}»/«{m101?.DcsTag}»)");
                Check(m103?.LinkState == ModbusMapService.LinkStateLinked &&
                      m103.DcsTag == "1131-MXA-103A",
                    $"F59(1): строка 103 связана, DcsTag = тег точки (факт «{m103?.DcsTag}»)");
                Check(resFull.Summary.Contains("полное обновление"),
                    "F59(1): итог сообщает режим прогона");

                // ===== (2) ручные правки в Access: строка 102 — свой тег; строка 103 — DcsTag =====
                using (var cn59 = new System.Data.OleDb.OleDbConnection(
                           $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmp59};"))
                {
                    cn59.Open();
                    using var upd = cn59.CreateCommand();
                    upd.CommandText =
                        $"UPDATE [{Schema.ModbusMapTable}] SET [LinkTag] = ?, [DcsTag] = ? WHERE [PlcTag] = ?";
                    upd.Parameters.AddWithValue("@lt", "MANUAL-102");
                    upd.Parameters.AddWithValue("@dt", "MANUAL-102");
                    upd.Parameters.AddWithValue("@pt", "1131-PM-102A_STA.MCU");
                    upd.ExecuteNonQuery();
                    using var upd3 = cn59.CreateCommand();
                    upd3.CommandText =
                        $"UPDATE [{Schema.ModbusMapTable}] SET [DcsTag] = ? WHERE [PlcTag] = ?";
                    upd3.Parameters.AddWithValue("@dt", "MANUAL-103");
                    upd3.Parameters.AddWithValue("@pt", "1131-PM-103A_STA.MCU");
                    upd3.ExecuteNonQuery();
                }

                // ===== (3) FillEmpty: ручные/авто-привязки сохраняются, точки не перенаправляются =====
                var resFill = linker59.Link(ModbusLinkMode.FillEmpty);
                rows59 = maps59.LoadFromDb();
                var m102f = Row59("1131-PM-102A_STA.MCU");
                m103 = Row59("1131-PM-103A_STA.MCU");
                var m101c = Row59("1131-PM-101A_STA.MCU");
                Check(resFill.Mode == ModbusLinkMode.FillEmpty && resFill.Linked == 0 &&
                      resFill.PreservedFilled == 3 && resFill.SkippedAlreadyLinked == 2,
                    $"F59(3): FillEmpty — сохранено 3 заполненных, пропущено 2 точки " +
                    $"(факт {resFill.PreservedFilled}/{resFill.SkippedAlreadyLinked}, связано {resFill.Linked})");
                Check(m102f?.LinkTag == "MANUAL-102" && m102f.DcsTag == "MANUAL-102",
                    $"F59(3): ручная связь строки 102 не сброшена (факт «{m102f?.LinkTag}»/«{m102f?.DcsTag}»)");
                Check(m103?.DcsTag == "MANUAL-103" && m103.LinkTag == "1131-MXA-103A",
                    $"F59(3): ручной DcsTag строки 103 сохранён, LinkTag не тронут (факт «{m103?.DcsTag}»/«{m103?.LinkTag}»)");
                Check(m101c?.LinkTag == "1131-MXA-101A",
                    $"F59(3): строка 101 без изменений (факт «{m101c?.LinkTag}»)");
                Check(resFill.Summary.Contains("только пустые") &&
                      resFill.Summary.Contains("сохранено заполненных связей: 3"),
                    "F59(3): итог сообщает режим и число сохранённых связей");

                // ===== (4) FillEmpty дозаполняет ПУСТЫЕ строки (новая строка файла F59b) =====
                maps59.SaveToDb(new List<ModbusMapEntry>
                {
                    new() { MapFile = "F59b.xlsx", MapSheet = f59Sheet, MapRow = 3, SlaveStation = "IMCS F59",
                        FuncCode = "02", PlcTag = "1131-PM-104A_STA.MCU", Description = "running" }
                }, "F59b.xlsx");
                repo59.BeginSession();
                repo59.Upsert(MakePoint("1131-MXA-104A", "1131-M-104A", "DI-COM",
                    "1131-P-104A motor Motor status:running"));
                repo59.EndSession();

                var resFill2 = linker59.Link(ModbusLinkMode.FillEmpty);
                rows59 = maps59.LoadFromDb();
                var m104 = Row59("1131-PM-104A_STA.MCU");
                Check(resFill2.Linked == 1 && resFill2.PreservedFilled == 3 &&
                      m104?.LinkTag == "1131-MXA-104A" && m104.DcsTag == "1131-MXA-104A",
                    $"F59(4): FillEmpty заполнил пустую строку 104 (факт {resFill2.Linked}/" +
                    $"{resFill2.PreservedFilled}, «{m104?.LinkTag}»)");

                // идемпотентность FillEmpty: всё заполнено — только пропуски
                var resFill3 = linker59.Link(ModbusLinkMode.FillEmpty);
                Check(resFill3.Linked == 0 && resFill3.PreservedFilled == 4 &&
                      resFill3.SkippedAlreadyLinked == 3,
                    $"F59(4): повторный FillEmpty идемпотентен (факт {resFill3.PreservedFilled}/" +
                    $"{resFill3.SkippedAlreadyLinked})");

                // ===== (5) полное обновление затирает ручные правки и устаревшие DcsTag =====
                var resFull2 = linker59.Link(ModbusLinkMode.Full);
                rows59 = maps59.LoadFromDb();
                var m102 = Row59("1131-PM-102A_STA.MCU");
                var m101f = Row59("1131-PM-101A_STA.MCU");
                m104 = Row59("1131-PM-104A_STA.MCU");
                Check(resFull2.Linked == 4 && resFull2.PreservedFilled == 0,
                    $"F59(5): полное обновление пересчитало всё (факт {resFull2.Linked})");
                Check(m102?.LinkTag == "1131-MXA-102A" && m102.DcsTag == "1131-MXA-102A",
                    $"F59(5): ручной MANUAL-102 стёрт, строка 102 перепривязана (факт «{m102?.LinkTag}»/«{m102?.DcsTag}») — " +
                    "остаток F55 (DcsTag не сбрасывался при перепривязке) закрыт");
                var m103f = Row59("1131-PM-103A_STA.MCU");
                Check(m101f?.DcsTag == "1131-MXA-101A" && m104?.DcsTag == "1131-MXA-104A" &&
                      m103f?.DcsTag == "1131-MXA-103A",
                    "F59(5): после полного DcsTag = свежие теги точек (ручной MANUAL-103 стёрт)");

                // ===== (6) адресация по свежим DcsTag =====
                var addr59 = linker59.UpdateAddressing();
                Check(addr59.Updated == 4 && addr59.NotFound == 0 && addr59.Errors.Count == 0,
                    $"F59(6): адресация 4 точек по свежим DcsTag (факт {addr59.Updated}/" +
                    $"{addr59.NotFound}/{addr59.Errors.Count})");
            }
            finally
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); // освободить handle ACE
                try { File.Delete(tmp59); } catch { /* мог остаться lock */ }
            }
        }

        /// <summary>F66: семейство объекта в связывании — строгое разделение «своё/чужое»
        /// (Loop-контура против чужих семейств 2701-PK-1201B-M / 2701-PM-1201B), LinkState
        /// «привязано не по полным совпадением!» для фазы 2 и инвариант «один сигнал —
        /// не более одного объекта» (своя tmp-БД).</summary>
        private static void RunF66Part()
        {
            Console.WriteLine("== F66 Семейство объекта: разделение A/B, LinkState отклонения ==");

            // ===== (1) unit: ObjectBase — базовый ключ семейства =====
            Check(ModbusLinkService.ObjectBase("2701-PK-1201B-M") == "2701-PK-1201B",
                "F66(1): ObjectBase срезает «-M» после литеры");
            Check(ModbusLinkService.ObjectBase("1612-PM-611A.HE-JB") == "1612-PM-611A",
                "F66(1): ObjectBase срезает «.HE-JB»");
            Check(ModbusLinkService.ObjectBase("1110-KM-101A-1") == "1110-KM-101A",
                "F66(1): ObjectBase срезает машинный номер «-1»");
            Check(ModbusLinkService.ObjectBase("9999-P-999A.HE-JB") == "9999-PM-999A",
                "F66(1): ObjectBase нормализует класс P→PM");
            Check(ModbusLinkService.ObjectBase("2701-PM-1201B") !=
                      ModbusLinkService.ObjectBase("2701-PK-1201B-M"),
                "F66(1): 2701-PM-1201B и 2701-PK-1201B — разные семейства");

            // ===== (2) unit: LoopFamilyMatches — якорь Loop-контура =====
            Check(ModbusLinkService.LoopFamilyMatches("2701-PK-1201A",
                      ModbusLinkService.ObjectBase("2701-PK-1201A-M")),
                "F66(2): Loop литеры A пускает строку A своего объекта");
            Check(!ModbusLinkService.LoopFamilyMatches("2701-PK-1201A",
                      ModbusLinkService.ObjectBase("2701-PK-1201B-M")),
                "F66(2): Loop литеры A не пускает строку B");
            Check(!ModbusLinkService.LoopFamilyMatches("2701-PK-1201A",
                      ModbusLinkService.ObjectBase("2701-PM-1201B")),
                "F66(2): Loop PK не пускает строку PM (класс чужого объекта)");
            Check(ModbusLinkService.LoopFamilyMatches("9964-M-9904",
                      ModbusLinkService.ObjectBase("9964-PK-9904B-M")),
                "F66(2): Loop без литеры — литера строки не значима (VFD)");
            Check(ModbusLinkService.LoopFamilyMatches("9962-AGM-1202A",
                      ModbusLinkService.ObjectBase("9962-AG-1202A")),
                "F66(2): Loop AGM ↔ строка AG — эквивалент класса F9.6");
            Check(ModbusLinkService.LoopFamilyMatches("1612-M-611A",
                      ModbusLinkService.ObjectBase("1612-PM-611A.HE01")),
                "F66(2): однобуквенный Loop-класс не раскрывает класс — строка PM в семействе");
            Check(!ModbusLinkService.LoopFamilyMatches("1612-M-611A",
                      ModbusLinkService.ObjectBase("1612-PM-611B-HE-JB")),
                "F66(2): Loop литеры A не пускает строку 611B");
            Check(!ModbusLinkService.LoopFamilyMatches("1115-M-00101A",
                      ModbusLinkService.ObjectBase("1120-PM-203A")),
                "F66(2): Loop другого титула/номера — чужая строка");

            // ===== (3) интеграция: tmp-БД, карта из семейств A / B / PM-B =====
            string tmp66 = Path.Combine(Path.GetTempPath(), $"iosmoke_{Guid.NewGuid():N}.accdb");
            try
            {
                var log66 = new EventLogger();
                var creator66 = new TableCreator(tmp66);
                creator66.CreateDatabase();
                creator66.EnsureServiceTables();
                creator66.CreateDataTable(Schema.DefaultTable);
                var audit66 = new AuditService(tmp66, log66);
                var repo66 = new IOListRepository(tmp66, Schema.DefaultTable, audit66, log66);
                var maps66 = new ModbusMapService(tmp66);
                maps66.EnsureTable();

                const string f66File = "F66.xlsx";
                const string f66Sheet = "Register Table Function 02/03/06";
                var map66 = new List<ModbusMapEntry>
                {
                    new() { MapFile = f66File, MapSheet = f66Sheet, MapRow = 3, SlaveStation = "IMCS F66",
                        FuncCode = "02", PlcTag = "2701-PK-1201A-M_FAULT.MCU", ObjectTag = "2701-PK-1201A-M",
                        Description = "ITF Waste Gas Fan General Fault" },
                    new() { MapFile = f66File, MapSheet = f66Sheet, MapRow = 4, SlaveStation = "IMCS F66",
                        FuncCode = "02", PlcTag = "2701-PK-1201A-M_STA.MCU", ObjectTag = "2701-PK-1201A-M",
                        Description = "ITF Waste Gas Fan Motor Operation" },
                    new() { MapFile = f66File, MapSheet = f66Sheet, MapRow = 5, SlaveStation = "IMCS F66",
                        FuncCode = "02", PlcTag = "2701-PK-1201B-M_STA.MCU", ObjectTag = "2701-PK-1201B-M",
                        Description = "ITF Waste Gas Fan Motor Operation" },
                    new() { MapFile = f66File, MapSheet = f66Sheet, MapRow = 6, SlaveStation = "IMCS F66",
                        FuncCode = "02", PlcTag = "2701-PM-1201B_FAULT.MCU", ObjectTag = "2701-PM-1201B",
                        Description = "Benzene Transfer Pump General Fault" },
                    new() { MapFile = f66File, MapSheet = f66Sheet, MapRow = 7, SlaveStation = "IMCS F66",
                        FuncCode = "03", PlcTag = "2701-PM-1201B_I_A", ObjectTag = "2701-PM-1201B",
                        Description = "Benzene Transfer Pump Motor Current" },
                };
                maps66.SaveToDb(map66, f66File);

                var pts66 = new List<IoPoint>
                {
                    // сигналы объекта A: Loop литер A — только строки семейства A
                    MakePoint("2701-MXA-1201A", "2701-PK-1201A", "DI-COM",
                        "2701-PK-1201A fan Motor status:fault"),
                    MakePoint("2701-MXB-1201A", "2701-PK-1201A", "DI-COM",
                        "2701-PK-1201A fan Motor status:running"),
                    MakePoint("2701-MXB-1201B", "2701-PK-1201B", "DI-COM",
                        "2701-PK-1201B fan Motor status:running"),
                    // перекрёстные данные: Loop литер A, Service указывает на чужой PM-объект —
                    // Loop приоритетнее чужого упоминания в Service
                    MakePoint("2701-MXA-1201C", "2701-PK-1201A", "DI-COM",
                        "2701-PM-1201B pump Motor status:fault"),
                };
                repo66.BeginSession();
                foreach (var p in pts66) repo66.Upsert(p);
                repo66.EndSession();

                var linker66 = new ModbusLinkService(repo66, maps66, log66, audit66,
                    new ModbusLinkRuleService(tmp66));

                var res66 = linker66.Link(ModbusLinkMode.Full);
                var rows66 = maps66.LoadFromDb();
                ModbusMapEntry? Row66(string plc) => rows66.FirstOrDefault(r => r.PlcTag == plc);
                var aFault = Row66("2701-PK-1201A-M_FAULT.MCU");
                var aSta = Row66("2701-PK-1201A-M_STA.MCU");
                var bSta = Row66("2701-PK-1201B-M_STA.MCU");
                var pmFault = Row66("2701-PM-1201B_FAULT.MCU");
                var pmIa = Row66("2701-PM-1201B_I_A");

                Check(res66.Linked == 3 && res66.Unlinked == 1 && res66.LinkedApprox == 3,
                    $"F66(3): привязано 3 (семейства точек), не связано 1, все — с отклонением " +
                    $"(факт {res66.Linked}/{res66.Unlinked}/{res66.LinkedApprox})");
                Check(aFault?.LinkTag == "2701-MXA-1201A" && aSta?.LinkTag == "2701-MXB-1201A",
                    $"F66(3): сигналы объекта A — к строкам 2701-PK-1201A-M " +
                    $"(факт «{aFault?.LinkTag}» / «{aSta?.LinkTag}»)");
                Check(bSta?.LinkTag == "2701-MXB-1201B",
                    $"F66(3): сигнал объекта B — только к своей строке (факт «{bSta?.LinkTag}»)");
                Check(pmFault?.LinkState == ModbusMapService.LinkStateUnlinked &&
                      string.IsNullOrEmpty(pmFault.LinkTag) &&
                      pmIa?.LinkState == ModbusMapService.LinkStateUnlinked &&
                      string.IsNullOrEmpty(pmIa.LinkTag),
                    $"F66(3): строки PM-1201B «не привязано» — чужое семейство недоступно " +
                    $"(факт «{pmFault?.LinkTag}»/«{pmIa?.LinkTag}»)");
                Check(aFault?.LinkState == ModbusMapService.LinkStateLinkedApprox &&
                      aSta?.LinkState == ModbusMapService.LinkStateLinkedApprox,
                    $"F66(4): фаза 2 → LinkState «привязано не по полным совпадением!» " +
                    $"(факт {aFault?.LinkState})");

                // инвариант «один сигнал — не более одного объекта»: тег не встречается
                // в LinkTag двух строк карты
                var dupTags = rows66
                    .Where(r => !string.IsNullOrWhiteSpace(r.LinkTag))
                    .SelectMany(r => (r.LinkTag ?? "").Split(',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();
                Check(dupTags.Count == 0,
                    $"F66(5): каждый сигнал привязан не более чем к одной строке " +
                    $"(дубли: {(dupTags.Count > 0 ? string.Join(", ", dupTags) : "нет")})");

                Check(res66.MapRowsLinked == 3 && res66.MapRowsUnlinked == 2,
                    $"F66(6): карта — привязано 3 (оба состояния), без привязки 2 " +
                    $"(факт {res66.MapRowsLinked}/{res66.MapRowsUnlinked})");
                Check(res66.Summary.Contains("с отклонением F66: 3"),
                    $"F66(6): итог сообщает число привязок с отклонением (факт «{res66.Summary}»)");

                // ===== (4) идемпотентность повторного полного прогона =====
                var res66b = linker66.Link(ModbusLinkMode.Full);
                rows66 = maps66.LoadFromDb();
                Check(res66b.Linked == 3 && res66b.Unlinked == 1 && res66b.LinkedApprox == 3 &&
                      rows66.First(r => r.PlcTag == "2701-PK-1201A-M_FAULT.MCU").LinkTag ==
                          "2701-MXA-1201A",
                    $"F66(4): повторный Full идемпотентен (факт {res66b.Linked}/" +
                    $"{res66b.Unlinked}/{res66b.LinkedApprox})");

                // ===== (5) FillEmpty: заполненные связи не трогаются, занятые теги пропущены =====
                var resFill66 = linker66.Link(ModbusLinkMode.FillEmpty);
                Check(resFill66.Linked == 0 && resFill66.PreservedFilled == 3 &&
                      resFill66.SkippedAlreadyLinked == 3,
                    $"F66(5): FillEmpty — заполнено сохранено, занятые теги пропущены " +
                    $"(факт {resFill66.Linked}/{resFill66.PreservedFilled}/" +
                    $"{resFill66.SkippedAlreadyLinked})");
            }
            finally
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); // освободить handle ACE
                try { File.Delete(tmp66); } catch { /* мог остаться lock */ }
            }
        }

        /// <summary>F67: Loop-подстановка приводного класса не «дотягивается» до чужого
        /// объекта — точка «2650-PM-2001A» не получает кандидата KM (и зеркально K-точка —
        /// PM): первый символ класса ключевой (PM≡P, KM≡K). unit ExtractCandidates +
        /// tmp-БД: строка P привязана, строка K «не привязана», ловушка (роль P-строки
        /// не подходит сигналу) не уводит сигнал в объект K.</summary>
        private static void RunF67Part()
        {
            Console.WriteLine("== F67 Loop-подстановка: первый символ класса ключевой ==");

            // ===== (1) unit: ExtractCandidates — фильтр чужой Loop-подстановки =====
            var p67 = MakePoint("2650-MXA-2001A", "2650-M-2001A", "DI-COM",
                "MCU.2650-PM-2001A - Oil pump motor_General Fault 1:Fault 0:Normal");
            var c67 = ModbusLinkService.ExtractCandidates(p67);
            Check(c67.Any(c => c.Tag == "2650-PM-2001A"),
                $"F67(1): P-точка — кандидат 2650-PM-2001A из Service " +
                $"(факт: {string.Join(", ", c67.Select(c => c.Tag))})");
            Check(!c67.Any(c => c.Tag == "2650-KM-2001A"),
                $"F67(1): Loop-подстановка KM чужда P-точке " +
                $"(факт: {string.Join(", ", c67.Select(c => c.Tag))})");

            // зеркало: K-точка не «дотягивается» до P-объекта
            var pk67 = MakePoint("2650-MXB-101A", "2650-M-101A", "DI-COM",
                "2650-KM-101A cooler Motor status:fault");
            var cpk = ModbusLinkService.ExtractCandidates(pk67);
            Check(cpk.Any(c => c.Tag == "2650-KM-101A") && !cpk.Any(c => c.Tag == "2650-PM-101A"),
                $"F67(1): зеркало — K-точка без PM-подстановки " +
                $"(факт: {string.Join(", ", cpk.Select(c => c.Tag))})");
            // Loop-only точка (кандидатов 0–3 нет) — обе подстановки, как раньше
            var loopOnly = MakePoint("MCC_SIG_1", "2650-M-2001A", "DI-COM", "");
            var cloop = ModbusLinkService.ExtractCandidates(loopOnly);
            Check(cloop.Any(c => c.Tag == "2650-PM-2001A") && cloop.Any(c => c.Tag == "2650-KM-2001A"),
                $"F67(1): Loop-only — обе подстановки " +
                $"(факт: {string.Join(", ", cloop.Select(c => c.Tag))})");

            // DCS-кандидат (класс MXA, первый символ «M») Loop-fallback не ограничивает
            var dcs67 = MakePoint("2650-MXA-3001A", "2650-M-3001A", "DI-COM",
                "2650-MXA-3001A - DCS mention status:fault");
            var cdcs = ModbusLinkService.ExtractCandidates(dcs67);
            Check(cdcs.Any(c => c.Tag == "2650-PM-3001A") && cdcs.Any(c => c.Tag == "2650-KM-3001A"),
                $"F67(1): DCS-кандидат не ограничивает Loop-fallback " +
                $"(факт: {string.Join(", ", cdcs.Select(c => c.Tag))})");

            // полное имя с хвостом-суффиксом (приоритет 0) — тот же первый символ класса
            var tail67 = MakePoint("2650-PM-2001A.HE-JB", "2650-M-2001A", "DI-COM", "");
            var ctail = ModbusLinkService.ExtractCandidates(tail67);
            Check(!ctail.Any(c => c.Tag == "2650-KM-2001A"),
                $"F67(1): кандидат-полное имя с хвостом тоже фильтрует KM " +
                $"(факт: {string.Join(", ", ctail.Select(c => c.Tag))})");

            // ===== (2) интеграция: tmp-БД — P-строка привязана, K-строка «не привязана» =====
            string tmp67 = Path.Combine(Path.GetTempPath(), $"iosmoke_{Guid.NewGuid():N}.accdb");
            try
            {
                var log67 = new EventLogger();
                var creator67 = new TableCreator(tmp67);
                creator67.CreateDatabase();
                creator67.EnsureServiceTables();
                creator67.CreateDataTable(Schema.DefaultTable);
                var audit67 = new AuditService(tmp67, log67);
                var repo67 = new IOListRepository(tmp67, Schema.DefaultTable, audit67, log67);
                var maps67 = new ModbusMapService(tmp67);
                maps67.EnsureTable();
                const string f67File = "F67.xlsx";
                const string f67Sheet = "Register Table Function 02/03/06";
                var map67 = new List<ModbusMapEntry>
                {
                    // объекты P и K с одним номером/литерой — разные объекты (F67)
                    new() { MapFile = f67File, MapSheet = f67Sheet, MapRow = 3, SlaveStation = "IMCS F67",
                        FuncCode = "02", PlcTag = "2650-P-2001A_FAULT.MCU", ObjectTag = "2650-P-2001A",
                        Description = "Oil pump motor general fault" },
                    new() { MapFile = f67File, MapSheet = f67Sheet, MapRow = 4, SlaveStation = "IMCS F67",
                        FuncCode = "02", PlcTag = "2650-K-2001A_FAULT.MCU", ObjectTag = "2650-K-2001A",
                        Description = "Cooler motor general fault" },
                    new() { MapFile = f67File, MapSheet = f67Sheet, MapRow = 5, SlaveStation = "IMCS F67",
                        FuncCode = "02", PlcTag = "2650-P-2001B_FAULT.MCU", ObjectTag = "2650-P-2001B",
                        Description = "Oil pump motor general fault" },
                    new() { MapFile = f67File, MapSheet = f67Sheet, MapRow = 6, SlaveStation = "IMCS F67",
                        FuncCode = "02", PlcTag = "2650-K-2001B_FAULT.MCU", ObjectTag = "2650-K-2001B",
                        Description = "Cooler motor general fault" },
                    // ловушка: у P-строки роль STA, у K-строки — _FAULT.MCU: без фильтра F67
                    // фаза 1 (кандидат KM) уводила сигнал FAULT в чужой объект K
                    new() { MapFile = f67File, MapSheet = f67Sheet, MapRow = 7, SlaveStation = "IMCS F67",
                        FuncCode = "02", PlcTag = "2650-P-3001A_STA.MCU", ObjectTag = "2650-P-3001A",
                        Description = "Oil pump motor status" },
                    new() { MapFile = f67File, MapSheet = f67Sheet, MapRow = 8, SlaveStation = "IMCS F67",
                        FuncCode = "02", PlcTag = "2650-K-3001A_FAULT.MCU", ObjectTag = "2650-K-3001A",
                        Description = "Cooler motor general fault" },
                };
                maps67.SaveToDb(map67, f67File);

                var pts67 = new List<IoPoint>
                {
                    MakePoint("2650-MXA-2001A", "2650-M-2001A", "DI-COM",
                        "MCU.2650-PM-2001A - Oil pump motor_General Fault 1:Fault 0:Normal"),
                    MakePoint("2650-MXA-2001B", "2650-M-2001B", "DI-COM",
                        "MCU.2650-PM-2001B - Oil pump motor_General Fault 1:Fault 0:Normal"),
                    MakePoint("2650-MXA-3001A", "2650-M-3001A", "DI-COM",
                        "MCU.2650-PM-3001A - Oil pump motor status:fault"),
                };
                repo67.BeginSession();
                foreach (var p in pts67) repo67.Upsert(p);
                repo67.EndSession();
                var linker67 = new ModbusLinkService(repo67, maps67, log67, audit67,
                    new ModbusLinkRuleService(tmp67));

                var res67 = linker67.Link(ModbusLinkMode.Full);
                var rows67 = maps67.LoadFromDb();
                ModbusMapEntry? Row67(string plc) => rows67.FirstOrDefault(r => r.PlcTag == plc);
                var pA67 = Row67("2650-P-2001A_FAULT.MCU");
                var kA67 = Row67("2650-K-2001A_FAULT.MCU");
                var pB67 = Row67("2650-P-2001B_FAULT.MCU");
                var kB67 = Row67("2650-K-2001B_FAULT.MCU");
                var pTrap67 = Row67("2650-P-3001A_STA.MCU");
                var kTrap67 = Row67("2650-K-3001A_FAULT.MCU");

                Check(res67.Linked == 2 && res67.Unlinked == 1 && res67.LinkedApprox == 0,
                    $"F67(2): привязано 2 (P-строки A/B), не связано 1, без отклонений " +
                    $"(факт {res67.Linked}/{res67.Unlinked}/{res67.LinkedApprox})");
                Check(pA67?.LinkTag == "2650-MXA-2001A" &&
                      pA67.LinkState == ModbusMapService.LinkStateLinked,
                    $"F67(2): строка 2650-P-2001A привязана полным совпадением " +
                    $"(факт «{pA67?.LinkTag}»/{pA67?.LinkState})");
                Check(kA67?.LinkState == ModbusMapService.LinkStateUnlinked &&
                      string.IsNullOrEmpty(kA67.LinkTag),
                    $"F67(2): строка 2650-K-2001A «не привязана» — чужой объект " +
                    $"(факт «{kA67?.LinkTag}»/{kA67?.LinkState})");
                Check(pB67?.LinkTag == "2650-MXA-2001B" &&
                      kB67?.LinkState == ModbusMapService.LinkStateUnlinked,
                    $"F67(2): литер B — привязана только P-строка " +
                    $"(факт «{pB67?.LinkTag}» / «{kB67?.LinkTag}»)");
                Check(string.IsNullOrEmpty(pTrap67?.LinkTag) &&
                      string.IsNullOrEmpty(kTrap67?.LinkTag),
                    $"F67(3): ловушка — сигнал не уведён в объект K при неподходящей " +
                    $"роли P-строки (факт «{pTrap67?.LinkTag}» / «{kTrap67?.LinkTag}»)");
                // инвариант «один сигнал — не более одного объекта» (F66)
                var dup67 = rows67
                    .Where(r => !string.IsNullOrWhiteSpace(r.LinkTag))
                    .SelectMany(r => (r.LinkTag ?? "").Split(',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();
                Check(dup67.Count == 0,
                    $"F67(4): каждый сигнал привязан не более чем к одной строке " +
                    $"(дубли: {(dup67.Count > 0 ? string.Join(", ", dup67) : "нет")})");
            }
            finally
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); // освободить handle ACE
                try { File.Delete(tmp67); } catch { /* мог остаться lock */ }
            }
        }

        /// <summary>F12: генерация перечня «Тех.объекты» — схема/сид, построение строк,
        /// маппинг типов, персист настроек, экспорт в формате образца.</summary>
        private static void RunF12Part(string tmpDb, TableCreator creator)
        {
            Console.WriteLine("== F12 Генерация перечня Modbus (Тех.объекты) ==");

            // (1) схема: таблицы созданы EnsureServiceTables, сид маппинга по 2.5.4
            Check(creator.TableExists(Schema.ScadaTypeMapTable), "F12: таблица SCADA_TYPE_MAP создана");
            Check(creator.TableExists(Schema.ScadaSettingsTable), "F12: таблица SCADA_TEHOBJ_SETTINGS создана");
            Check(Schema.DefaultScadaTypeMap.Length == 9,
                $"F12: маппинг по умолчанию — 9 строк (факт {Schema.DefaultScadaTypeMap.Length})");
            Check(Schema.CreateScadaTypeMapTable.Contains("[Source] TEXT(255)") &&
                  Schema.CreateScadaTypeMapTable.Contains("[Target] TEXT(255)"),
                "F12: DDL SCADA_TYPE_MAP содержит Source/Target");
            Check(Schema.CreateScadaSettingsTable.Contains("[Name] TEXT(255)") &&
                  Schema.CreateScadaSettingsTable.Contains("[Value] TEXT(255)"),
                "F12: DDL SCADA_TEHOBJ_SETTINGS содержит Name/Value");

            var svc = new ScadaTechObjectService(tmpDb);
            var map = svc.LoadTypeMappings();
            Check(map.Count == 9, $"F12: сид SCADA_TYPE_MAP — 9 строк (факт {map.Count})");
            Check(map.Any(m => m.Source == "32-Bit Floating" && m.Target == "REAL"),
                "F12: сид содержит 32-Bit Floating → REAL");
            Check(map.Any(m => m.Source == "16-Bit Unsigned" && m.Target == "WORD"),
                "F12: сид содержит 16-Bit Unsigned → WORD");

            // (2) Марка: тире → «_», ведущий «_»
            Check(ScadaTechObjectService.MakeMarka("1115-MHS-00101A") == "_1115_MHS_00101A",
                "F12: MakeMarka 1115-MHS-00101A → _1115_MHS_00101A");
            Check(ScadaTechObjectService.MakeMarka("3103-XZY-67401") == "_3103_XZY_67401",
                "F12: MakeMarka 3103-XZY-67401 → _3103_XZY_67401");
            Check(ScadaTechObjectService.MakeMarka("_READY") == "_READY",
                "F12: MakeMarka не задваивает ведущий «_»");
            Check(ScadaTechObjectService.MakeMarka("  ") == "", "F12: MakeMarka пустого значения → «»");

            // (3) резолв поля-источника: поле из FieldRegistry vs константа
            var pT = MakePoint("3103-XZY-67401", "3103-XZV-67401", "DI-COM", "V-6704, блок. отсечки");
            pT.Identification.Controller = "3000_S_SC_B03";
            Check(ScadaTechObjectService.ResolveSource(pT, "Tag") == "3103-XZY-67401",
                "F12: ResolveSource(Tag) — поле");
            Check(ScadaTechObjectService.ResolveSource(pT, "CONSTANT_TEXT") == "CONSTANT_TEXT",
                "F12: ResolveSource(нет в реестре) — константа");
            Check(ScadaTechObjectService.ResolveSource(pT, "Controller") == "3000_S_SC_B03",
                "F12: ResolveSource(Controller) — FCS из IO_LIST");

            // (4) маппинг типов: IgnoreCase + без соответствия → исходный тип
            Check(ScadaTechObjectService.MapDataType("32-bit floating", map, null) == "REAL",
                "F12: MapDataType IgnoreCase (32-bit floating → REAL)");
            var resB = new ScadaBuildResult();
            Check(ScadaTechObjectService.MapDataType("CustomType", map, resB) == "CustomType" &&
                  resB.UnmappedTypes == 1,
                "F12: немапп. тип остаётся исходным + счётчик UnmappedTypes");

            // (5) Доп.параметр: подстановки {M_*} + обрезка до 50
            pT.Modbus = new Modbus
            {
                PlcTag = "3103-PM-67401_STA.MCU", DataType = "BOOL",
                FuncCode = "02", RegType = "I", Address = "10001", SlaveStation = "IMCS EBSM102"
            };
            var resD = new ScadaBuildResult();
            string d1 = ScadaTechObjectService.RenderDParam("FC{M_FuncCode} {M_RegType}{M_Address} {M_SlaveStation}", pT, resD);
            Check(d1 == "FC02 I10001 IMCS EBSM102", $"F12: Доп.параметр по шаблону (факт «{d1}»)");
            string d2 = ScadaTechObjectService.RenderDParam(new string('x', 60), pT, resD);
            Check(d2.Length == ScadaTechObjectService.DParamMaxLength && resD.TruncatedDParam == 1,
                $"F12: Доп.параметр обрезается до 50 (факт {d2.Length})");
            ScadaTechObjectService.RenderDParam("{NO_SUCH_COLUMN}", pT, resD);
            Check(resD.Warnings.Any(w => w.Contains("NO_SUCH_COLUMN")),
                "F12: неизвестный плейсхолдер → предупреждение");

            // (6) построение строк: отбор по M_PlcTag, константы, поля-источники
            var pNoAddr = MakePoint("3103-XZY-67402", "3103-XZV-67402", "DI-COM", "сигнал без адресации");
            var settings = new ScadaGenSettings
            {
                MarkaField = "Tag", NameField = "Service", DiscField = "Loop",
                KksField = "Tag", ControllerField = "Controller",
                DParamTemplate = "FC{M_FuncCode} {M_Address}", Section = "TEHOBJ",
                ArhPer = "0", SrezControl = "1", UserGroup = "[Все]\\[Все технологические]",
                EvGroup = "", PlcAdress = "0", PlcGr = "1", Template = "простой"
            };
            var build = svc.BuildRows(new[] { pT, pNoAddr }, settings, map);
            Check(build.Rows.Count == 1 && build.WithAddressing == 1 && build.SkippedNoAddressing == 1,
                $"F12: BuildRows — 1 строка с адресацией, 1 пропущена (факт {build.Rows.Count}/{build.WithAddressing}/{build.SkippedNoAddressing})");
            var row1 = build.Rows[0];
            Check(row1.Mode == "TEHOBJ", $"F12: Раздел = TEHOBJ (факт «{row1.Mode}»)");
            Check(row1.Marka == "_3103_XZY_67401", $"F12: Марка = _3103_XZY_67401 (факт «{row1.Marka}»)");
            Check(row1.ObjType == "BOOL", $"F12: Тип объекта = BOOL (факт «{row1.ObjType}»)");
            Check(row1.Name == "V-6704, блок. отсечки", $"F12: Наименование = Service (факт «{row1.Name}»)");
            Check(row1.Disc == "3103-XZV-67401", $"F12: Описание = Loop (факт «{row1.Disc}»)");
            Check(row1.Kks == "3103-XZY-67401", $"F12: KKS = Tag (факт «{row1.Kks}»)");
            Check(row1.PlcName == "3000_S_SC_B03", $"F12: КОНТРОЛЛЕР = Controller (факт «{row1.PlcName}»)");
            Check(row1.ArhPer == "0" && row1.PlcAdress == "0" && row1.PlcGr == "1" &&
                  row1.Template == "простой" && row1.SrezControl == "1" &&
                  row1.UserGroup == "[Все]\\[Все технологические]",
                "F12: константы (Период/Адрес/Ресурс/Шаблон/Маска/Классификатор)");
            Check(row1.ObjDParam == "FC02 10001", $"F12: Доп.параметр (факт «{row1.ObjDParam}»)");
            Check(row1.No == "" && row1.Status == "" && row1.ObjSign == "" &&
                  row1.ObjNumber == "" && row1.PlcVarName == "",
                "F12: №/Статус/Подпись/Номер/PLC перем. не заполняются");

            // (7) поля-источники: смена источника Марки на Loop вручную
            settings.MarkaField = "Loop";
            var build2 = svc.BuildRows(new[] { pT }, settings, map);
            Check(build2.Rows.Count == 1 && build2.Rows[0].Marka == "_3103_XZV_67401",
                $"F12: Марка из Loop при ручной смене поля (факт «{build2.Rows[0].Marka}»)");
            settings.MarkaField = "Tag";

            // (7а) структуры данных {Колонка} — в полях-источниках и константах (помимо ссылок/текста)
            Check(ScadaTechObjectService.RenderValue(pT, "CONSTANT_TEXT", null, "тест") == "CONSTANT_TEXT",
                "F12: RenderValue без скобок — прежнее поведение (текст-константа)");
            Check(ScadaTechObjectService.RenderValue(pT, "Tag", null, "тест") == "3103-XZY-67401",
                "F12: RenderValue без скобок — прежнее поведение (поле IO List)");
            Check(ScadaTechObjectService.RenderValue(pT, "{Service} ({Loop})", null, "тест")
                  == "V-6704, блок. отсечки (3103-XZV-67401)",
                "F12: структура — составное значение из полей с литералами");
            var resS = new ScadaBuildResult();
            string sv = ScadaTechObjectService.RenderValue(pT, "{Nope}", resS, "Описание");
            Check(sv == "{Nope}" && resS.Warnings.Any(w => w.Contains("Описание") && w.Contains("Nope")),
                "F12: неизвестный плейсхолдер в поле → литерал как есть + предупреждение с именем поля");
            string svcVal = ScadaTechObjectService.RenderValue(pT, "Service", null, "тест");
            string longStruct = ScadaTechObjectService.RenderValue(pT, "{Service} {Service} {Service}", null, "тест");
            Check(longStruct == $"{svcVal} {svcVal} {svcVal}" && longStruct.Length > 50,
                "F12: структуры вне Доп.параметра не обрезаются до 50");

            var settingsStruct = new ScadaGenSettings
            {
                MarkaField = "PRE-{Tag}", NameField = "{Service} ({Loop})", DiscField = "Loop",
                KksField = "Tag", ControllerField = "Controller",
                DParamTemplate = "FC{M_FuncCode} {M_Address}", EvGroup = "{Tag} {M_SlaveStation}"
            };
            var buildS = svc.BuildRows(new[] { pT }, settingsStruct, map);
            var rowS = buildS.Rows[0];
            Check(rowS.Marka == "_PRE_3103_XZY_67401",
                $"F12: структура в Марке + MakeMarka (факт «{rowS.Marka}»)");
            Check(rowS.Name == "V-6704, блок. отсечки (3103-XZV-67401)",
                $"F12: структура в Наименовании (факт «{rowS.Name}»)");
            Check(rowS.EvGroup == "3103-XZY-67401 IMCS EBSM102",
                $"F12: структура в константе «Группа событий» (факт «{rowS.EvGroup}»)");
            Check(rowS.Disc == "3103-XZV-67401" && rowS.PlcName == "3000_S_SC_B03",
                "F12: поля без скобок рядом со структурами — прежнее поведение");

            // (8) экспорт: файл в формате образца (шапка 4 строки, данные с 5-й)
            string outFile = Path.Combine(Path.GetTempPath(), $"f12_tehobj_{DateTime.Now:HHmmssfff}.xlsx");
            try
            {
                svc.Export(build.Rows, outFile);
                Check(File.Exists(outFile), "F12: файл экспорта создан");
                using (var wb = new ClosedXML.Excel.XLWorkbook(outFile))
                {
                    var ws = wb.Worksheet(1);
                    Check(wb.Worksheets.Count == 1, "F12: один лист");
                    Check(ws.Cell(1, 1).GetValue<string>() == "Cлужебные",
                        $"F12: A1 = Cлужебные (факт «{ws.Cell(1, 1).GetValue<string>()}»)");
                    Check(ws.Cell(1, 3).GetValue<string>() == "ОБЩЕЕ СВ-ВО", "F12: C1 = ОБЩЕЕ СВ-ВО");
                    Check(ws.Cell(3, 4).GetValue<string>() == "Марка", "F12: строка 3 — русские заголовки (D3=Марка)");
                    Check(ws.Cell(4, 4).GetValue<string>() == "MARKA", "F12: строка 4 — имена колонок (D4=MARKA)");
                    Check(ws.Cell(4, 5).GetValue<string>() == "OBJTYPE", "F12: строка 4 — E4=OBJTYPE");
                    Check(ws.Cell(4, 20).GetValue<string>() == "TEMPLATE", "F12: строка 4 — T4=TEMPLATE");
                    Check(ws.Cell(5, 4).GetValue<string>() == "_3103_XZY_67401", "F12: данные с 5-й строки (D5=Марка)");
                    Check(ws.Cell(5, 5).GetValue<string>() == "BOOL", "F12: E5 = BOOL (Тип объекта)");
                    Check(ws.Cell(5, 3).GetValue<string>() == "TEHOBJ", "F12: C5 = TEHOBJ (Раздел)");
                    Check(ws.Cell(5, 20).GetValue<string>() == "простой", "F12: T5 = простой (Шаблон)");
                    Check(ws.Cell(3, 13).GetValue<string>() == "Доп.параметр" &&
                          ScadaTechObjectService.FieldNames[12] == "OBJDPARAM",
                        "F12: колонка 13 = Доп.параметр/OBJDPARAM");
                    Check(ScadaTechObjectService.RussianHeaders.Length == 20 &&
                          ScadaTechObjectService.FieldNames.Length == 20,
                        "F12: шапка — 20 колонок");
                }
            }
            finally
            {
                try { File.Delete(outFile); } catch { /* мог остаться handle */ }
            }

            // (9) персист маппинга: сохранение/повторное чтение
            map.Add(new ScadaTypeMapping { Source = "64-Bit Signed", Target = "INT" });
            int savedMap = svc.SaveTypeMappings(map);
            var map2 = svc.LoadTypeMappings();
            Check(savedMap == 10 && map2.Count == 10 &&
                  map2.Any(m => m.Source == "64-Bit Signed" && m.Target == "INT"),
                $"F12: SaveTypeMappings/LoadTypeMappings roundtrip (факт {savedMap}/{map2.Count})");

            // (10) персист настроек: сохранение/повторное чтение
            var s1 = new ScadaGenSettings
            {
                MarkaField = "Loop", NameField = "ServiceEnglish", DiscField = "Title",
                KksField = "ProcessTag", ControllerField = "Controller",
                DParamTemplate = "{M_SlaveStation}:{M_Address}", Section = "TEHOBJ",
                ArhPer = "0", SrezControl = "1", UserGroup = "КАСТОМ\\ГРУППА",
                EvGroup = "СОБЫТИЕ\\1", PlcAdress = "0", PlcGr = "7", Template = "простой"
            };
            svc.SaveSettings(s1);
            var s2 = svc.LoadSettings();
            Check(s2.MarkaField == "Loop" && s2.NameField == "ServiceEnglish" &&
                  s2.DiscField == "Title" && s2.KksField == "ProcessTag" &&
                  s2.ControllerField == "Controller",
                "F12: настройки полей-источников — roundtrip");
            Check(s2.UserGroup == "КАСТОМ\\ГРУППА" && s2.EvGroup == "СОБЫТИЕ\\1" &&
                  s2.PlcGr == "7" && s2.DParamTemplate == "{M_SlaveStation}:{M_Address}",
                "F12: константы (Классификатор/Группа/Ресурс/шаблон) — roundtrip");
            svc.SaveSettings(new ScadaGenSettings()); // вернуть defaults для последующих прогонов
            var s3 = svc.LoadSettings();
            Check(s3.MarkaField == "Tag" && s3.UserGroup == "[Все]\\[Все технологические]",
                "F12: сброс настроек в defaults");

            // (11) миграция: удаление таблиц и воссоздание EnsureServiceTables (сид повторно)
            // F57: именованные варианты настроек (SCADA_TEHOBJ_SETTINGS.Profile)
            Check(creator.GetColumns(Schema.ScadaSettingsTable).Contains("Profile"),
                "F57: DDL SCADA_TEHOBJ_SETTINGS содержит Profile");
            Check(ScadaTechObjectService.DefaultProfile == "По умолчаню",
                $"F57: DefaultProfile = «По умолчанию» (факт «{ScadaTechObjectService.DefaultProfile}»)");
            var sAlt = new ScadaGenSettings { MarkaField = "Loop", NameField = "Title", Section = "ALT" };
            svc.SaveSettings(sAlt, "профиль-2");
            var profiles57 = svc.LoadProfiles();
            Check(profiles57.Count == 2 && profiles57.Contains(ScadaTechObjectService.DefaultProfile) &&
                  profiles57.Contains("профиль-2"),
                $"F57: два профиля (факт: {string.Join(", ", profiles57)})");
            var sAlt2 = svc.LoadSettings("профиль-2");
            Check(sAlt2.MarkaField == "Loop" && sAlt2.NameField == "Title" && sAlt2.Section == "ALT",
                $"F57: профиль-2 читается (факт {sAlt2.MarkaField}/{sAlt2.NameField}/{sAlt2.Section})");
            Check(svc.LoadSettings().NameField == "Service", "F57: профиль по умолчанию не затёрт");
            var sTgt = new ScadaGenSettings();
            svc.LoadInto(sTgt, "профиль-2");
            Check(sTgt.Section == "ALT", $"F57: LoadInto переносит значения (факт {sTgt.Section})");
            using (var cnL = new System.Data.OleDb.OleDbConnection(
                       "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + tmpDb + ";"))
            {
                cnL.Open();
                using var cmdL = cnL.CreateCommand();
                cmdL.CommandText = "INSERT INTO [SCADA_TEHOBJ_SETTINGS] ([Profile], [Name], [Value]) VALUES (NULL, 'MarkaField', 'LegacyTag')";
                cmdL.ExecuteNonQuery();
            }
            Check(svc.LoadSettings().MarkaField == "LegacyTag",
                "F57: строки без профиля читаются как по умолчанию");
            Check(svc.DeleteProfile(ScadaTechObjectService.DefaultProfile) == false,
                "F57: профиль по умолчанию удалить нельзя");
            Check(svc.DeleteProfile("профиль-2") && svc.LoadProfiles().Count == 1,
                "F57: удаление профиля возвращает список к одному");
            using (var cnF = new System.Data.OleDb.OleDbConnection(
                       $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
            {
                cnF.Open();
                using var cmdDrop = cnF.CreateCommand();
                cmdDrop.CommandText = $"DROP TABLE [{Schema.ScadaTypeMapTable}]";
                cmdDrop.ExecuteNonQuery();
                cmdDrop.CommandText = $"DROP TABLE [{Schema.ScadaSettingsTable}]";
                cmdDrop.ExecuteNonQuery();
            }
            Check(!creator.TableExists(Schema.ScadaTypeMapTable) &&
                  !creator.TableExists(Schema.ScadaSettingsTable),
                "F12: таблицы удалены");
            creator.EnsureServiceTables();
            Check(creator.TableExists(Schema.ScadaTypeMapTable) &&
                  creator.TableExists(Schema.ScadaSettingsTable),
                "F12: миграция — EnsureServiceTables воссоздаёт таблицы F12");
            var map3 = svc.LoadTypeMappings();
            Check(map3.Count == 9, $"F12: после миграции сид повторно — 9 строк (факт {map3.Count})");
        }

        /// <summary>F13: генерация блоков Modbus (XML) — схема/настройки, шаблон
        /// (MB_TCP_MASTER_пример_new.xml), планирование пакетов с привязками DataOut,
        /// генерация BufScadaPOUS + файла привязок, запись привязок в IO_LIST.</summary>
        private static void RunF13Part(string tmpDb, TableCreator creator)
        {
            Console.WriteLine("== F13 Генерация блоков Modbus (XML, шаблон) ==");

            // (1) схема: таблицы созданы EnsureServiceTables
            Check(creator.TableExists(Schema.ScadaMbSettingsTable), "F13: таблица SCADA_MB_SETTINGS создана");
            Check(creator.TableExists(Schema.ScadaMbSlaveRulesTable), "F13: таблица SCADA_MB_SLAVE_RULES создана");
            Check(creator.TableExists(Schema.ScadaMbTemplateTable), "F13: таблица SCADA_MB_TEMPLATE создана");
            Check(Schema.CreateScadaMbTemplateTable.Contains("[Name] TEXT(50)") &&
                  Schema.CreateScadaMbTemplateTable.Contains("[Body] MEMO"),
                "F13: DDL SCADA_MB_TEMPLATE содержит Name TEXT(50)/Body MEMO");
            Check(FieldRegistry.HasColumn("M_BusBlock") && FieldRegistry.HasColumn("M_BusDataOut") &&
                  FieldRegistry.HasColumn("M_Marka"), // F27
                "F13: FieldRegistry знает M_BusBlock/M_BusDataOut/M_Marka");
            var colProbe = new IoPoint();
            FieldRegistry.SetValue(colProbe, "M_BusBlock", "MB_TCP_MASTER_4_1");
            FieldRegistry.SetValue(colProbe, "M_BusDataOut", "MB_TCP_MASTER_4_1.DataOut[3]");
            FieldRegistry.SetValue(colProbe, "M_Marka", "_1115_MHS_00101A"); // F27
            Check(colProbe.Modbus?.BusBlock == "MB_TCP_MASTER_4_1" &&
                  colProbe.Modbus?.BusDataOut == "MB_TCP_MASTER_4_1.DataOut[3]" &&
                  colProbe.Modbus?.Marka == "_1115_MHS_00101A",
                "F13/F27: FieldRegistry.SetValue → Modbus.BusBlock/BusDataOut/Marka");

            var svc = new ModbusBlockPlannerService(tmpDb);

            // (2) парсеры M_-полей (реальные значения из IMCS TO DCS Register Table Modbus)
            var ep1 = ModbusBlockPlannerService.ParseSlaveIdMain("192.168.4.10(Default)\n255.255.255.0\nport 502", 502);
            Check(ep1 != null && ep1.Value.Ip1 == 192 && ep1.Value.Ip4 == 10 && ep1.Value.Port == 502,
                "F13: ParseSlaveIdMain — IP + port 502 (EBSM102)");
            var ep2 = ModbusBlockPlannerService.ParseSlaveIdMain("192.168.86.27(Default)\n255.255.255.0", 502);
            Check(ep2 != null && ep2.Value.Ip3 == 86 && ep2.Value.Port == 502,
                "F13: ParseSlaveIdMain без port — DefaultPort");
            var ep3 = ModbusBlockPlannerService.ParseSlaveIdMain("192.168.1.5\nport 503", 502);
            Check(ep3 != null && ep3.Value.Port == 503, "F13: ParseSlaveIdMain — свой port 503");
            Check(ModbusBlockPlannerService.ParseSlaveIdMain("нет адреса", 502) == null,
                "F13: ParseSlaveIdMain без IP — null");
            Check(ModbusBlockPlannerService.ParseSlaveIdMain("", 502) == null,
                "F13: ParseSlaveIdMain пустого значения — null");
            Check(ModbusBlockPlannerService.ParseTimeoutMs("100ms") == 100 &&
                  ModbusBlockPlannerService.ParseTimeoutMs("200") == 200 &&
                  ModbusBlockPlannerService.ParseTimeoutMs("abc") == null &&
                  ModbusBlockPlannerService.ParseTimeoutMs(null) == null,
                "F13: ParseTimeoutMs («100ms»/«200»/мусор/null)");
            Check(ModbusBlockPlannerService.GetRegisterWidth("BOOL") == 1 &&
                  ModbusBlockPlannerService.GetRegisterWidth("16-Bit Unsigned") == 1 &&
                  ModbusBlockPlannerService.GetRegisterWidth("32-Bit Floating") == 2 &&
                  ModbusBlockPlannerService.GetRegisterWidth("64-Bit Floating") == 4 &&
                  ModbusBlockPlannerService.GetRegisterWidth(null) == 1,
                "F13: GetRegisterWidth (BOOL→1, 16→1, 32→2, 64→4, null→1)");
            Check(ModbusBlockPlannerService.NormalizeFuncCode("02") == 2 &&
                  ModbusBlockPlannerService.NormalizeFuncCode(" 03 ") == 3 &&
                  ModbusBlockPlannerService.NormalizeFuncCode("06") == 6 &&
                  ModbusBlockPlannerService.NormalizeFuncCode("0") == null &&
                  ModbusBlockPlannerService.NormalizeFuncCode("abc") == null,
                "F13: NormalizeFuncCode («02»→2, «03»→3, «06»→6, мусор→null)");
            Check(ModbusBlockPlannerService.ParseAddress("602") == 602 &&
                  ModbusBlockPlannerService.ParseAddress("0500") == 500 &&
                  ModbusBlockPlannerService.ParseAddress("12.3") == 12 &&
                  ModbusBlockPlannerService.ParseAddress("abc") == null,
                "F13: ParseAddress («602»/«0500»/«12.3»/мусор)");

            // (3) шаблон: файл Work\MB_TCP_MASTER_пример_new.xml + валидация + имена пакетов
            string tplPath = Path.Combine(@"c:\VSCode\Work", "MB_TCP_MASTER_пример_new.xml");
            Check(File.Exists(tplPath), $"F13: шаблон найден: {tplPath}");
            string tpl = File.ReadAllText(tplPath);
            var xmlSvc = new ScadaTemplateXmlService();
            var info = xmlSvc.Parse(tpl);
            Check(info.Valid, $"F13: Parse шаблона валиден ({info.Error})");
            Check(info.MasterNameBase == "MB_TCP_MASTER_4_1",
                $"F13: имя мастера шаблона MB_TCP_MASTER_4_1 (факт {info.MasterNameBase})");
            Check(info.BlockCount == 10 && info.LinkCount == 9 && info.MasterCount == 1,
                $"F13: шаблон — 10 блоков, 9 связей, 1 мастер (факт {info.BlockCount}/{info.LinkCount}/{info.MasterCount})");
            Check(!xmlSvc.Parse("мусор").Valid, "F13: Parse мусора — невалиден");
            Check(!xmlSvc.Parse("").Valid, "F13: Parse пустого шаблона — невалиден");
            Check(!xmlSvc.Parse("<?xml version=\"1.0\"?><BufScadaPOUS><POUS><OnePOU><ISAGraf><Blocks>" +
                  "<Block GROBJTYPE=\"34\" Info=\"1\" T11ID=\"1\"><Graphics X=\"0\" Y=\"0\" WIDTH=\"10\" HEIGHT=\"10\"/>" +
                  "<Params T11Text=\"1\" CI=\"0\" CO=\"1\" VMODE=\"0\" Commented=\"false\" IsaObjId=\"-100\" cardId=\"0\"/>" +
                  "</Block></Blocks><Links/></ISAGraf></OnePOU></POUS></BufScadaPOUS>").Valid,
                "F13: шаблон без мастера — невалиден");
            Check(ScadaTemplateXmlService.MasterNameFor("MB_TCP_MASTER_4_1", 1) == "MB_TCP_MASTER_4_1" &&
                  ScadaTemplateXmlService.MasterNameFor("MB_TCP_MASTER_4_1", 2) == "MB_TCP_MASTER_4_2" &&
                  ScadaTemplateXmlService.MasterNameFor("MB_TCP_MASTER_4_1", 15) == "MB_TCP_MASTER_4_15" &&
                  ScadaTemplateXmlService.MasterNameFor("MASTER", 2) == "MASTER_2",
                "F13: MasterNameFor (внутренние цифры → номер пакета; без цифр → «_No»)");
            Check(ScadaTemplateXmlService.ResolveDefaultTemplatePath() != null,
                "F13: шаблон блоков доступен по умолчанию (файл Work/MB_TCP_MASTER_пример_new.xml)");

            // (4) планирование: точки с адресацией Modbus → пакеты + привязки DataOut
            string ip102 = "192.168.4.10(Default)\n255.255.255.0\nport 502";
            string ip103 = "192.168.5.10(Default)\n255.255.255.0\nport 502";
            var pts = new List<IoPoint>
            {
                MakeMbPoint("A-TX-1", "1115-PM-101A_TX.MCU", ip102, "02", "0", "BOOL"),
                MakeMbPoint("A-TX-2", "1115-PM-101B_TX.MCU", ip102, "02", "1", "BOOL"),
                MakeMbPoint("A-TX-3", "1115-PM-101C_TX.MCU", ip102, "02", "2", "BOOL"),
                MakeMbPoint("A-TX-4", "1115-PM-101D_TX.MCU", ip102, "02", "10", "BOOL"),
                MakeMbPoint("B-I-1", "1630-PM-214A_I_A", ip103, "03", "0", "32-Bit Floating"),
                MakeMbPoint("B-I-2", "1630-PM-214B_I_A", ip103, "03", "2", "32-Bit Floating"),
                MakeMbPoint("C-WR", "1115-MHS-00101A", ip102, "06", "100", "16-Bit Unsigned"),
                MakeMbPoint("C-BADIP", "1115-PM-X_TX.MCU", "без адреса", "02", "5", "BOOL"),
                MakeMbPoint("C-NOADDR", "1115-PM-Y", null, null, null, null),
                MakeMbPoint("C-NOPLC", null, ip102, "02", "7", "BOOL"),
                new IoPoint { Identification = new Identification { Tag = "EMPTY" } }
            };
            var st = svc.BuildPlan(pts, new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            // группа 192.168.4.10 FC02: адреса 0,1,2 + разрыв 10 → 2 пакета; FC06 C-WR → 1 пакет
            // (F30: запись включается в план); 192.168.5.10 FC03: 0..3 → 1
            Check(st.Packets.Count == 4,
                $"F13/F30: Contiguous — 4 пакета (0-2 | 10 | FC06 | float 0..3) (факт {st.Packets.Count})");
            var p02a = st.Packets.FirstOrDefault(p => p.Ip4 == 10 && p.Address == 0);
            Check(p02a != null && p02a.Count == 3 && p02a.FuncCode == 2 && p02a.Mode == 2 && p02a.PointCount == 3,
                $"F13: пакет 0-2: Count=3 FC02 (факт {p02a?.Count}/{p02a?.FuncCode}/{p02a?.PointCount})");
            Check(p02a != null && p02a.Port == 502 && p02a.Endpoint == "192.168.4.10:502",
                $"F13: IP/порт из M_SlaveIdMain (факт {p02a?.Endpoint})");
            var p02b = st.Packets.FirstOrDefault(p => p.Ip4 == 10 && p.Address == 10);
            Check(p02b != null && p02b.Count == 1 && p02b.PointCount == 1,
                $"F13: разрыв → отдельный пакет addr=10 Count=1 (факт {p02b?.Count}/{p02b?.PointCount})");
            var p03 = st.Packets.FirstOrDefault(p => p.FuncCode == 3);
            Check(p03 != null && p03.Address == 0 && p03.Count == 4 && p03.PointCount == 2,
                $"F13: 32-Bit Float: 2 точки по 2 регистра (0-1, 2-3) → Count=4 (факт {p03?.Count}/{p03?.PointCount})");
            Check(st.WithAddressing == 9 && st.SkippedWrite == 0 && st.SkippedBadEndpoint == 1 &&
                  st.SkippedNoAddressing == 2 && st.SkippedNoTypno == 0,
                $"F13/F30: статистика — запись FC06 в плане (SkippedWrite=0) (факт {st.WithAddressing}/{st.SkippedWrite}/{st.SkippedBadEndpoint}/{st.SkippedNoAddressing}/{st.SkippedNoTypno})");
            Check(st.Packets.All(p => p.Count <= 100), "F13/F20: Count каждого пакета ≤ лимита (100/100)");
            Check(st.Packets.Select(p => p.MasterName).Distinct().Count() == st.Packets.Count,
                "F13: имена мастеров уникальны");
            Check(st.Packets[0].MasterName == "MB_TCP_MASTER_4_1" &&
                  st.Packets[1].MasterName == "MB_TCP_MASTER_4_2" &&
                  st.Packets[2].MasterName == "MB_TCP_MASTER_4_3",
                $"F13: имена мастеров от шаблона (факт {string.Join(",", st.Packets.Select(p => p.MasterName))})");

            // F18/F33: привязки DataOut — BOOL-индекс (FC02: каждая битовая точка — свой
            // элемент dataOut, индекс = адрес в пределах пакета)
            Check(p02a != null && p02a.Bindings.Count == 3 &&
                  p02a.Bindings[0].DataOutExpr == "MB_TCP_MASTER_4_1.DataOut[0]" &&
                  p02a.Bindings[1].DataOutExpr == "MB_TCP_MASTER_4_1.DataOut[1]" &&
                  p02a.Bindings[2].DataOutExpr == "MB_TCP_MASTER_4_1.DataOut[2]",
                $"F13: привязки пакета 0-2 (F33: биты 0-2 → dataOut[0..2], адрес) (факт {string.Join(";", p02a?.Bindings.Select(b => b.DataOutExpr) ?? new List<string>())})");
            Check(p02a != null && p02a.Bindings[0].Tag == "A-TX-1" && p02a.Bindings[0].Address == 0,
                "F13: привязка несёт тег IO_LIST и адрес");
            Check(p03 != null && p03.Bindings.Count == 2 &&
                  p03.Bindings[0].Offset == 0 && p03.Bindings[1].Offset == 2 &&
                  p03.Bindings[1].DataOutExpr == "MB_TCP_MASTER_4_4.DataOut[4]",
                $"F13: привязки float-пакета (4-й пакет F30): регистр 2 → байт 4 (факт {string.Join(";", p03?.Bindings.Select(b => b.DataOutExpr) ?? new List<string>())})");
            // F30: пакет записи FC06 — DataIn[], ровно 1 операция, направление «запись»
            var p06 = st.Packets.FirstOrDefault(p => p.FuncCode == 6);
            Check(p06 != null && p06.IsWrite && p06.Count == 1 && p06.PointCount == 1 &&
                  p06.Direction == "запись",
                $"F30: пакет FC06 — одиночная запись (факт {p06?.Count}/{p06?.PointCount}/{p06?.Direction})");
            Check(p06 != null && p06.Bindings.Count == 1 && p06.Bindings[0].IsWrite &&
                  p06.Bindings[0].DataOutExpr == $"{p06.MasterName}.DataIn[0]",
                $"F30: привязка записи — DataIn[0] (факт {p06?.Bindings.FirstOrDefault()?.DataOutExpr})");
            Check(st.Packets.All(p => p.Bindings.All(b => b.IsWrite == p.IsWrite)),
                "F30: направление привязок совпадает с пакетом (чтение/запись не смешиваются)");

            // Cover/FixedSize/лимит/правила «станция → SlaveId»
            // F56: одинаковая адресация — одна точка; FC06 пакетируется по адресному пространству
            string ip56 = "192.168.9.10(Default)\n255.255.255.0\nport 502";
            var pts56 = new List<IoPoint>
            {
                MakeMbPoint("D-CW-A", "1630-PM-214A", ip56, "06", "0", "16-Bit Unsigned"),
                MakeMbPoint("D-CW-A2", "1630-PM-214A", ip56, "06", "0", "16-Bit Unsigned"),
                MakeMbPoint("D-CW-A3", "1630-PM-214A", ip56, "06", "0", "16-Bit Unsigned"),
                MakeMbPoint("D-CW-B", "1115-PM-101A", ip56, "06", "1", "16-Bit Unsigned"),
                MakeMbPoint("D-RD-1", "1630-PM-214A_I_A", ip56, "03", "0", "16-Bit Unsigned"),
                MakeMbPoint("D-RD-2", "1630-PM-214B_I_A", ip56, "03", "0", "16-Bit Unsigned")
            };
            var st56 = svc.BuildPlan(pts56, new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_6_1");
            var p56w = st56.Packets.FirstOrDefault(p => p.FuncCode == 6);
            var p56r = st56.Packets.FirstOrDefault(p => p.FuncCode == 3);
            Check(st56.Packets.Count == 2 && p56w != null && p56r != null,
                $"F56: FC06 адреса 0..1 + FC03 дубль адреса → 2 пакета (факт {st56.Packets.Count})");
            Check(p56w != null && p56w.Address == 0 && p56w.Count == 2 && p56w.PointCount == 2,
                $"F56: FC06 — Count по адресному пространству: 0..1, точек 2 (факт {p56w?.Count}/{p56w?.PointCount})");
            Check(p56w != null && p56w.Bindings.Count == 2 &&
                  p56w.Bindings[0].Tag == "D-CW-A" && p56w.Bindings[1].Tag == "D-CW-B",
                $"F56: привязка по адресу — первый тег (факт {string.Join(",", p56w?.Bindings.Select(b => b.Tag) ?? new List<string>())})");
            Check(p56w != null && p56w.Bindings[0].SharedTags.Count == 2 &&
                  p56w.Bindings[0].SharedTags.Contains("D-CW-A2") && p56w.Bindings[0].SharedTags.Contains("D-CW-A3"),
                "F56: SharedTags — сигналы того же адреса");
            Check(p56r != null && p56r.PointCount == 1 && p56r.Count == 1,
                $"F56: два сигнала FC03 на одном адресе → одна точка (факт {p56r?.PointCount}/{p56r?.Count})");
            Check(st56.DedupedSignals == 3 && st56.Warnings.Any(w => w.Contains("F56")),
                $"F56: статистика дублей адресации (факт {st56.DedupedSignals})");
            var stCover = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502, Policy = MbPacketPolicy.Cover },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            Check(stCover.Packets.Count == 3,
                $"F13/F30: Cover — 3 пакета (покрытие 0..10 + FC06 + float) (факт {stCover.Packets.Count})");
            var cov = stCover.Packets.FirstOrDefault(p => p.FuncCode == 2);
            Check(cov != null && cov.Address == 0 && cov.Count == 11 && cov.PointCount == 4,
                $"F13: Cover: Address=0 Count=11 (0..10 вхолостую) (факт {cov?.Count}/{cov?.PointCount})");
            var stFix = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 5, MaxCountBits = 5, DefaultPort = 502, Policy = MbPacketPolicy.FixedSize },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            var fixGroup = stFix.Packets.Where(p => p.FuncCode == 2).OrderBy(p => p.Address).ToList();
            Check(fixGroup.Count == 2 && fixGroup[0].Count == 5 && fixGroup[1].Count == 1 &&
                  fixGroup[0].Address == 0 && fixGroup[1].Address == 10,
                $"F13: FixedSize(5): пакеты 0..4 и 10 (пустые пропускаются) (факт {fixGroup.Count})");
            var stSmall = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 8, MaxCountBits = 8, DefaultPort = 502, Policy = MbPacketPolicy.Cover },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            var smallGroup = stSmall.Packets.Where(p => p.FuncCode == 2).OrderBy(p => p.Address).ToList();
            Check(smallGroup.Count == 2 && smallGroup.All(p => p.Count <= 8),
                $"F13: Cover с лимитом 8: прореха 2..10 делит пакет (факт {smallGroup.Count})");
            var stRules = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502, DefaultSlaveId = 1 },
                new List<MbSlaveIdRule> { new MbSlaveIdRule { Station = "IMCS EBSM102", SlaveId = 7 } },
                "MB_TCP_MASTER_4_1");
            Check(stRules.Packets.All(p => p.SlaveId == 7),
                $"F13: правило «IMCS EBSM102 → 7» (факт {string.Join(",", stRules.Packets.Select(p => p.SlaveId).Distinct())})");
            Check(stRules.Packets.All(p => p.TimeoutMs == 100),
                $"F13: таймаут группы = 100 мс из M_Timeout (факт {string.Join(",", stRules.Packets.Select(p => p.TimeoutMs).Distinct())})");

            // (5) генерация BufScadaPOUS из шаблона (4 пакета stRules: FC02×2, FC06, FC03)
            var gen = xmlSvc.Write(stRules.Packets, tpl,
                new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502, ColumnsPerRow = 2 });
            var xdoc = System.Xml.Linq.XDocument.Parse(gen.PousXml); // well-formed
            Check(xdoc.Root != null && xdoc.Root.Name == "BufScadaPOUS",
                "F13: XML well-formed, корень BufScadaPOUS");
            Check(gen.PousXml.Contains("Info=\"MB_TCP_MASTER_4_1\"") &&
                  gen.PousXml.Contains("Info=\"MB_TCP_MASTER_4_2\"") &&
                  gen.PousXml.Contains("Info=\"MB_TCP_MASTER_4_3\"") &&
                  gen.PousXml.Contains("Info=\"MB_TCP_MASTER_4_4\""),
                "F13/F30: 4 блока мастеров MB_TCP_MASTER_4_1..4 (имена от шаблона)");
            Check(gen.PousXml.Contains("192,168,4,10,502") && gen.PousXml.Contains("192,168,5,10,502"),
                "F13: IV мастеров содержит IP и порт (192,168,4,10,502)");
            foreach (var port in new[] { "SlaveId", "TimeOut", "Mode", "Address", "Count", "Request" })
                Check(gen.PousXml.Contains($"|True|{port}|"),
                    $"F13: связь к входу мастера «{port}» (из шаблона)");
            // подстановки: SlaveId=7 (правило), Mode=2/3, Address=0/10, Count=3/1/4, таймаут=100
            Check(gen.PousXml.Contains("<Params T11Text=\"7\""), "F13: подстановка SlaveId=7 (правило станции)");
            Check(gen.PousXml.Contains("<Params T11Text=\"3\""), "F13: подстановка Count=3 (пакет 0-2)");
            // F14: FormatAddress=true по умолчанию — вход Address блока в формате «регистр + адрес»:
            // пакет Address=10 (FC02) → вход «1010» (регистр 1 + адрес 010)
            Check(gen.PousXml.Contains("<Params T11Text=\"1010\""),
                $"F14: подстановка Address=10 → вход «1010» (факт {gen.PousXml.Contains("<Params T11Text=\"1010\"")})");
            Check(gen.PousXml.Contains("<Params T11Text=\"4\""), "F13: подстановка Count=4 (float-пакет)");
            Check(gen.PousXml.Contains("<Params T11Text=\"100\"") && !gen.PousXml.Contains("<Params T11Text=\"200\""),
                "F13: подстановка таймаута 100 мс вместо шаблонного 200");
            Check(gen.PousXml.Contains("<Params T11Text=\"2\""), "F13: подстановка Mode=2 (FC02)");
            // имена инстансов шаблона сдвигаются: PULSE_1..3, TON_8..10
            Check(gen.PousXml.Contains("Info=\"PULSE_2\"") && gen.PousXml.Contains("Info=\"PULSE_3\""),
                "F13: клоны PULSE_2/PULSE_3 (сдвиг шаблонных имён)");
            Check(gen.PousXml.Contains("Info=\"TON_9\"") && gen.PousXml.Contains("Info=\"TON_10\""),
                "F13: клоны TON_9/TON_10 (сдвиг шаблонных имён)");
            // T11ID уникальны
            var t11Ids = xdoc.Root!.Descendants("Block")
                .Select(b => (string?)b.Attribute("T11ID")).Where(v => v != null).ToList();
            Check(t11Ids.Count > 0 && t11Ids.Distinct().Count() == t11Ids.Count,
                $"F13: T11ID всех блоков уникальны ({t11Ids.Count} блоков)");
            // F60: cardId/T11 — шаблонные ID НЕ переиспользуются ни при k=1, ни при k>1
            var masterCards = xdoc.Root.Descendants("Block")
                .Where(b => b.Element("Params")?.Attribute("IsaObjId")?.Value == "15003")
                .Select(b => (string?)b.Element("Params")?.Attribute("cardId")).ToList();
            Check(masterCards.Count == 4 && masterCards.All(c => c != "597529") &&
                  masterCards.Distinct().Count() == masterCards.Count,
                $"F13/F60: cardId всех мастеров — новые (шаблонный 597529 не переиспользуется, " +
                $"включая k=1) и без дублей (факт {string.Join(",", masterCards)})");
            var tplDocF60 = System.Xml.Linq.XDocument.Parse(tpl);
            string tplMasterT11 = tplDocF60.Root?.Descendants("Block")
                .FirstOrDefault(b => b.Attribute("GROBJTYPE")?.Value == "37")
                ?.Attribute("T11ID")?.Value ?? "";
            Check(tplMasterT11.Length > 0 && !t11Ids.Contains(tplMasterT11),
                $"F13/F60: шаблонный T11 мастера {tplMasterT11} не переиспользуется (k=1)");
            Check(gen.PousXml.Contains("<rec ID=\"" + masterCards[0] + "\" Info=\"MB_TCP_MASTER_4_1\""),
                "F13/F60: ISACARDSINFO блоков — rec карточки первого мастера с новым ID");
            Check(gen.PousXml.Contains("Info=\"MB_TCP_MASTER_4_2\" IsRetain") ||
                  gen.PousXml.Contains("<rec ID=\"" + masterCards[1] + "\" Info=\"MB_TCP_MASTER_4_2\""),
                "F13: ISACARDSINFO содержит rec клона MB_TCP_MASTER_4_2");
            // смещение координат клона: X мастера пакета2 = 322 + ColumnStep(1150) = 1472
            var master2X = xdoc.Root.Descendants("Block")
                .FirstOrDefault(b => ((string?)b.Attribute("Info")) == "MB_TCP_MASTER_4_2")
                ?.Element("Graphics")?.Attribute("X")?.Value;
            Check(master2X == "1472",
                $"F13: кластер пакета 2 сдвинут на ColumnStep (X=1472, факт {master2X})");
            // секции шаблона переиспользуются
            Check(gen.PousXml.Contains("ControllerTypeName=\"TENIXRT-CPU850\"") &&
                  gen.PousXml.Contains("NAME=\"MB_Master_4\""),
                "F13: Common/OnePOU из шаблона (TENIXRT-CPU850, MB_Master_4)");
            Check(gen.PousXml.Contains("LibName=\"TenixRtLib\"") && gen.PousXml.Contains("Info=\"TON\""),
                "F13: ISAOBJSINFO из шаблона (TenixRtLib, TON)");

            // (6) файл привязок (корень BufScada): блок 31 «Master.DataOut[i]»
            var bdoc = System.Xml.Linq.XDocument.Parse(gen.BindingsXml); // well-formed
            Check(bdoc.Root != null && bdoc.Root.Name == "BufScada",
                "F13: привязки well-formed, корень BufScada");
            Check(bdoc.Root?.Element("Common")?.Attribute("PageID")?.Value == "19054" &&
                  bdoc.Root?.Element("Common")?.Attribute("PageMode")?.Value == "4",
                "F13: Common привязок — PageID=19054 (OnePOU шаблона), PageMode=4");
            // F18: блоков — столько, сколько уникальных пар (блок, индекс DataOut):
            // F33: битовые точки не схлопываются в байты — у каждой свой индекс (адрес)
            int totalBindings = stRules.Packets.Sum(p => p.Bindings
                .Select(b => ModbusBlockPlannerService.DataOutByteIndex(b.Offset, p.IsBit))
                .Distinct().Count());
            int bindBlocks = bdoc.Root?.Descendants("Block").Count() ?? 0;
            Check(bindBlocks == totalBindings,
                $"F13: блоков привязок = числу уникальных пар (блок, индекс) ({bindBlocks} == {totalBindings})");
            Check(gen.BindingsXml.Contains("Info=\"MB_TCP_MASTER_4_1.DataOut[0]\"") &&
                  gen.BindingsXml.Contains("T11Text=\".DataOut[0]\""),
                "F13: привязка MB_TCP_MASTER_4_1.DataOut[0] + T11Text=.DataOut[0]");
            Check(gen.BindingsXml.Contains("Info=\"MB_TCP_MASTER_4_4.DataOut[4]\""),
                "F18: привязка float-пакета (4-й F30) — регистр 2 → байт 4 (MB_TCP_MASTER_4_4.DataOut[4])");
            var bindFirst = bdoc.Root?.Descendants("Block").FirstOrDefault();
            Check(bindFirst?.Element("Params")?.Attribute("cardId")?.Value == masterCards[0] &&
                  bindFirst?.Element("Params")?.Element("IV")?.Value?.Contains("192,168,4,10,502") == true,
                "F13: привязка наследует cardId/IV мастера пакета (новый cardId F60)");
            Check(gen.BindingsXml.Contains("<rec ID=\"" + masterCards[0] + "\" Info=\"MB_TCP_MASTER_4_1\""),
                "F13: ISACARDSINFO привязок — rec карточки мастера");
            Check(gen.BindingsXml.Contains("<rec ID=\"15003\" Info=\"MB_TCP_MASTER\" LibName=\"TenixRtLib\""),
                "F13: ISAOBJSINFO привязок — rec мастера");

            // (7) запись привязок в IO_LIST (M_BusBlock/M_BusDataOut) + очистка + аудит Update
            var log = new EventLogger();
            var auditSvc = new AuditService(tmpDb, log);
            var repo = new IOListRepository(tmpDb, Schema.DefaultTable, auditSvc, log);
            using (var cnB = new System.Data.OleDb.OleDbConnection(
                       $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
            {
                cnB.Open();
                void Ins(string tag, string ioType, string plcTag, string addr)
                {
                    using var cmd = cnB.CreateCommand();
                    cmd.CommandText = "INSERT INTO [IO_LIST] ([Tag], [IoType], [Typno], [M_PlcTag], [M_SlaveStation], " +
                        "[M_SlaveIdMain], [M_FuncCode], [M_Address], [M_DataType], [M_Timeout]) " +
                        "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
                    foreach (var v in new object[] { tag, ioType, "PID1000-01", plcTag, "IMCS EBSM102", ip102, "02", addr, "BOOL", "100ms" })
                        OleDbArgs.Add(cmd, v);
                    cmd.ExecuteNonQuery();
                }
                Ins("BIND-A", "MAI", "T-BIND-A", "0");
                Ins("BIND-B", "MAI", "T-BIND-B", "1");
                Ins("BIND-GONE", "MAI", "T-BIND-GONE", "2");
                Ins("BIND-NOTMOD", "DI", "T-BIND-NOTMOD", "3"); // IO Type не Modbus — привязка отклоняется
                void InsShared(string tag, string addr)
                {
                    using var cmd = cnB.CreateCommand();
                    cmd.CommandText = "INSERT INTO [IO_LIST] ([Tag], [IoType], [Typno], [M_PlcTag], [M_SlaveStation], " +
                        "[M_SlaveIdMain], [M_FuncCode], [M_Address], [M_DataType], [M_Timeout]) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
                    foreach (var v in new object[] { tag, "MAI", "PID1000-01", "T-" + tag, "IMCS EBSM102", ip102, "06", addr, "16-Bit Unsigned", "100ms" })
                        OleDbArgs.Add(cmd, v);
                    cmd.ExecuteNonQuery();
                }
                InsShared("SHRD-A", "0");
                InsShared("SHRD-B", "0");
                InsShared("SHRD-C", "0");
                InsShared("SHRD-D", "1");
            }
            var dbPts = repo.GetAll().Where(p => (p.Tag ?? "").StartsWith("BIND-")).ToList();
            Check(dbPts.Count == 4, $"F13: в IO_LIST 4 точки BIND-* (факт {dbPts.Count})");

            var planB = svc.BuildPlan(dbPts, new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            Check(planB.Packets.Count == 1 && planB.Packets[0].PointCount == 4,
                $"F13: BIND-точки → 1 пакет на 4 точки (факт {planB.Packets.Count}/{(planB.Packets.Count > 0 ? planB.Packets[0].PointCount : 0)})");
            var wb1 = svc.WriteBindings(planB, repo);
            Check(wb1.Updated == 3 && wb1.Errors.Count == 1 && wb1.NotFound == 0 && wb1.Cleared == 0,
                $"F13: WriteBindings — обновлено 3, ошибка1 (не Modbus) (факт {wb1.Updated}/{wb1.Errors.Count}/{wb1.NotFound}/{wb1.Cleared})");
            Check(wb1.Errors.Count == 1 && wb1.Errors[0].Contains("BIND-NOTMOD"),
                "F13: ошибка привязки — IO Type «DI» не Modbus-сигнала (BIND-NOTMOD)");
            var afterWb = repo.GetAll().ToDictionary(p => p.Tag ?? "", p => p, StringComparer.OrdinalIgnoreCase);
            Check(afterWb.TryGetValue("BIND-A", out var pa) &&
                  pa.Modbus?.BusBlock == "MB_TCP_MASTER_4_1" &&
                  pa.Modbus?.BusDataOut == "MB_TCP_MASTER_4_1.DataOut[0]",
                $"F13: IO_LIST — M_BusBlock/M_BusDataOut у BIND-A (факт {pa?.Modbus?.BusBlock}/{pa?.Modbus?.BusDataOut})");
            Check(afterWb.TryGetValue("BIND-B", out var pb) &&
                  pb.Modbus?.BusDataOut == "MB_TCP_MASTER_4_1.DataOut[1]",
                $"F18/F33: IO_LIST — BIND-B (бит 1) → DataOut[1] (индекс = адрес) (факт {pb?.Modbus?.BusDataOut})");
            Check(afterWb.TryGetValue("BIND-NOTMOD", out var pn) &&
                  string.IsNullOrEmpty(pn.Modbus?.BusBlock),
                "F13: IO_LIST — BIND-NOTMOD (DI) привязка не записана");
            var wb2 = svc.WriteBindings(planB, repo);
            Check(wb2.Updated == 0 && wb2.Cleared == 0,
                $"F13: повторный WriteBindings идемпотентен (0 изменений, факт {wb2.Updated}/{wb2.Cleared})");
            // точка выпала из плана → привязка очищается
            var planC = svc.BuildPlan(dbPts.Where(p => p.Tag != "BIND-GONE").ToList(),
                new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            var wb3 = svc.WriteBindings(planC, repo);
            Check(wb3.Cleared == 1 && wb3.Updated == 0,
                $"F13: точка вне пакетов — привязка очищена (факт {wb3.Cleared}/{wb3.Updated})");
            var afterClear = repo.GetAll().ToDictionary(p => p.Tag ?? "", p => p, StringComparer.OrdinalIgnoreCase);
            Check(afterClear.TryGetValue("BIND-GONE", out var pg) &&
                  string.IsNullOrEmpty(pg.Modbus?.BusBlock) && string.IsNullOrEmpty(pg.Modbus?.BusDataOut),
                "F13: IO_LIST — M_BusBlock/M_BusDataOut очищены у BIND-GONE");
            using (var cnA = new System.Data.OleDb.OleDbConnection(
                       $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
            {
                cnA.Open();
                using var cmd = cnA.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM [IO_LIST_Audit] WHERE [FieldName] = 'M_BusBlock'";
                long n = Convert.ToInt64(cmd.ExecuteScalar());
                Check(n > 0, $"F13: аудит Update по M_BusBlock (записей: {n})");
            }

            // (7b) F27: запись Марки в IO_LIST (M_Marka) по SourceTag строк превья F12
            // (7e) F56: привязки пишутся каждому сигналу адреса (одна точка — первый тег)
            var shrdPts = repo.GetAll().Where(p => (p.Tag ?? "").StartsWith("SHRD-")).ToList();
            var planS = svc.BuildPlan(shrdPts, new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_7_1");
            Check(planS.Packets.Count == 1 && planS.Packets[0].PointCount == 2 && planS.Packets[0].Count == 2,
                $"F56: SHRD — 1 пакет, 2 точки/адреса (факт {planS.Packets.Count}/{(planS.Packets.Count > 0 ? planS.Packets[0].PointCount : 0)})");
            var wbS = svc.WriteBindings(planS, repo);
            var afterS = repo.GetAll().ToDictionary(p => p.Tag ?? "", p => p, StringComparer.OrdinalIgnoreCase);
            Check(wbS.Updated == 4 &&
                  afterS["SHRD-A"].Modbus?.BusDataOut == "MB_TCP_MASTER_7_1.DataIn[0]" &&
                  afterS["SHRD-B"].Modbus?.BusDataOut == "MB_TCP_MASTER_7_1.DataIn[0]" &&
                  afterS["SHRD-C"].Modbus?.BusDataOut == "MB_TCP_MASTER_7_1.DataIn[0]" &&
                  afterS["SHRD-D"].Modbus?.BusDataOut == "MB_TCP_MASTER_7_1.DataIn[2]",
                $"F56: WriteBindings — все сигналы адреса получили путь (факт {wbS.Updated})");
            var svcT12 = new ScadaTechObjectService(tmpDb);
            var rowsT = svcT12.BuildRows(dbPts, new ScadaGenSettings(), new List<ScadaTypeMapping>()).Rows;
            Check(rowsT.Count == 4 && rowsT.All(r => r.SourceTag.StartsWith("BIND-")),
                $"F27: превью F12 — 4 строки с SourceTag BIND-* (факт {rowsT.Count})");
            rowsT.First(r => r.SourceTag == "BIND-A").Marka = "_CHANGED_MARKA"; // переопределяем Марку
            rowsT.Add(new ScadaTechObjectRow { SourceTag = "NO-SUCH", Marka = "_GHOST" }); // тега нет в IO_LIST
            var wm1 = svcT12.WriteMarka(rowsT, repo);
            Check(wm1.Updated == 4 && wm1.NotFound == 1,
                $"F27: WriteMarka — обновлено 4, не найдено 1 (факт {wm1.Updated}/{wm1.NotFound})");
            var afterWm = repo.GetAll().ToDictionary(p => p.Tag ?? "", p => p, StringComparer.OrdinalIgnoreCase);
            afterWm.TryGetValue("BIND-A", out var pma);
            afterWm.TryGetValue("BIND-B", out var pmb);
            Check(pma?.Modbus?.Marka == "_CHANGED_MARKA" && pmb?.Modbus?.Marka == "_BIND_B", // MakeMarka: «-» → «_»
                $"F27: IO_LIST — M_Marka у BIND-A/BIND-B (факт {pma?.Modbus?.Marka}/{pmb?.Modbus?.Marka})");
            var wm2 = svcT12.WriteMarka(rowsT, repo);
            Check(wm2.Updated == 0,
                $"F27: повторный WriteMarka идемпотентен (0 изменений, факт {wm2.Updated})");
            using (var cnM = new System.Data.OleDb.OleDbConnection(
                       $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
            {
                cnM.Open();
                using var cmdM = cnM.CreateCommand();
                cmdM.CommandText = "SELECT COUNT(*) FROM [IO_LIST_Audit] WHERE [FieldName] = 'M_Marka'";
                long nm = Convert.ToInt64(cmdM.ExecuteScalar());
                Check(nm > 0, $"F27: аудит Update по M_Marka (записей: {nm})");
            }

            // (7c) F53: выбор станций — галочка «Генерировать» правил (фильтр пакетов)
            var f53Probe = new MbSlaveIdRule { Station = "IMCS EBSM102" };
            Check(f53Probe.Generate, "F53: MbSlaveIdRule.Generate по умолчанию true");
            var planF = svc.BuildPlan(dbPts, new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            var stationsF = planF.Packets.Select(p => (p.Station ?? "").Trim()).Distinct().ToList();
            Check(stationsF.Count >= 1, $"F53: план содержит станции (факт: {string.Join(",", stationsF)})");
            // только первая станция снята с генерации (Generate=false) — её пакеты отфильтрованы
            var offF = new List<MbSlaveIdRule>
            {
                new MbSlaveIdRule { Station = stationsF[0], Generate = false }
            };
            var filteredF = ModbusBlockPlannerService.FilterPacketsByStationRules(planF.Packets, offF);
            Check(filteredF.All(p => !string.Equals((p.Station ?? "").Trim(), stationsF[0],
                      StringComparison.OrdinalIgnoreCase)),
                $"F53: фильтр по Generate=false — станция исключена ({filteredF.Count} из {planF.Packets.Count})");
            Check(filteredF.Count == planF.Packets.Count(p =>
                      !string.Equals((p.Station ?? "").Trim(), stationsF[0], StringComparison.OrdinalIgnoreCase)),
                "F53: число отфильтрованных пакетов = числу пакетов остальных станций");
            // Generate=true (и пустой список правил) — пакеты не трогаются
            var onF = new List<MbSlaveIdRule> { new MbSlaveIdRule { Station = stationsF[0], Generate = true } };
            Check(ModbusBlockPlannerService.FilterPacketsByStationRules(planF.Packets, onF).Count == planF.Packets.Count &&
                  ModbusBlockPlannerService.FilterPacketsByStationRules(planF.Packets, new List<MbSlaveIdRule>()).Count == planF.Packets.Count,
                "F53: Generate=true/без правил — все пакеты генерятся");
            // универсальное правило (пустой Controller) отключает станцию у всех контроллеров
            var offUniv = new List<MbSlaveIdRule> { new MbSlaveIdRule { Station = stationsF[0], Controller = "", Generate = false } };
            Check(ModbusBlockPlannerService.FilterPacketsByStationRules(planF.Packets, offUniv)
                      .All(p => (p.Station ?? "").Trim() != stationsF[0]),
                "F53: универсальное правило (без Controller) отключает станцию у всех контроллеров");

            // (7d) F29: правило станции с шаблоном пакетов — сохранение/чтение SCADA_MB_SLAVE_RULES
            svc.SaveSlaveRules(new List<MbSlaveIdRule>
            {
                new MbSlaveIdRule { Station = "F29-ST", SlaveId = 5, Protocol = "RTU", MasterTemplate = "MB_RTU_X" },
                new MbSlaveIdRule { Station = "F29-ST2", SlaveId = 6, MasterTemplate = "" }
            });
            var rules29 = svc.LoadSlaveRules();
            var r29 = rules29.FirstOrDefault(r => r.Station == "F29-ST");
            var r29b = rules29.FirstOrDefault(r => r.Station == "F29-ST2");
            Check(r29 != null && r29.MasterTemplate == "MB_RTU_X" && r29.Protocol == "RTU",
                $"F29: MasterTemplate сохранён/прочитан (факт {r29?.MasterTemplate})");
            Check(r29b != null && r29b.MasterTemplate == "",
                $"F29: пустой MasterTemplate — roundtrip (факт '{r29b?.MasterTemplate}')");
            Check(Schema.CreateScadaMbSlaveRulesTable.Contains("[MasterTemplate] TEXT(255)"),
                "F29: DDL SCADA_MB_SLAVE_RULES содержит MasterTemplate");

            // (7e) F32: строковые шаблоны привязок переменных (несколько колонок + литералы)
            var cv32 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["M_DcsTag"] = "TAG_AI_1", ["M_Address"] = "602", ["M_SlaveStation"] = "IMCS EBSM102"
            };
            var unr32 = new List<string>();
            string rr32 = ModbusBlockPlannerService.RenderVarTemplate(
                "{M_DcsTag} Adress: {M_Address}", cv32, out unr32);
            Check(rr32 == "TAG_AI_1 Adress: 602" && unr32.Count == 0,
                $"F32: строка-шаблон резолвится целиком (факт «{rr32}», unresolved: {unr32.Count})");
            rr32 = ModbusBlockPlannerService.RenderVarTemplate(
                "Reg: {M_RegType} Adress: {M_Address} {M_SlaveStation}", cv32, out unr32);
            Check(rr32 == "Reg: {M_RegType} Adress: 602 IMCS EBSM102" &&
                  unr32.SequenceEqual(new[] { "M_RegType" }),
                $"F32: неизвестная колонка → токен-литерал + unresolved (факт «{rr32}»)");
            rr32 = ModbusBlockPlannerService.RenderVarTemplate("просто текст", cv32, out unr32);
            Check(rr32 == "просто текст" && unr32.Count == 0,
                "F32: вход без токенов возвращается как есть (литерал)");
            rr32 = ModbusBlockPlannerService.RenderVarTemplate("", cv32, out unr32);
            Check(rr32 == "" && unr32.Count == 0, "F32: пустой шаблон — без ошибок");

            // (8) персист настроек: новые ключи roundtrip, устаревшие игнорируются/удаляются
            var s2 = new ScadaMbGenSettings
            {
                MaxCountRegisters = 77, MaxCountBits = 77, Policy = MbPacketPolicy.Cover,
                TemplateMode = MbTemplateMode.Inline, TemplateFilePath = "C:\\tpl\\custom.xml",
                MaxGap = 5, BlockNamePattern = "MB_TCP_Master_{Slave}_{Seq}",
                BlockNamePatternRtu = "MB_RTU_{Station}_{Seq}", FormatAddress = false,
                DataOutTemplateMode = MbTemplateMode.Inline, DataOutTemplateFilePath = "C:\\do\\data.xml",
                DefaultSlaveId = 9, BaseT11Id = 2000000, BaseCardId = 700000,
                ColumnsPerRow = 3, RowStep = 700, ColumnStep = 1300,
                MasterTcpTemplateName = "MB_TCP_v1", MasterRtuTemplateName = "MB_RTU_v1",
                DataPacketGap = 25, // F63
                DataChainStep = 40 // F64
            };
            svc.SaveSettings(s2);
            var s3 = svc.LoadSettings();
            Check(s3.MaxCountRegisters == 77 && s3.MaxCountBits == 77 && s3.Policy == MbPacketPolicy.Cover &&
                  s3.TemplateMode == MbTemplateMode.Inline && s3.TemplateFilePath == "C:\\tpl\\custom.xml" &&
                  s3.MaxGap == 5 && s3.BlockNamePattern == "MB_TCP_Master_{Slave}_{Seq}" &&
                  s3.BlockNamePatternRtu == "MB_RTU_{Station}_{Seq}" && !s3.FormatAddress &&
                  s3.DataOutTemplateMode == MbTemplateMode.Inline &&
                  s3.DataOutTemplateFilePath == "C:\\do\\data.xml" &&
                  s3.DefaultSlaveId == 9 && s3.BaseT11Id == 2000000 && s3.BaseCardId == 700000 &&
                  s3.MasterTcpTemplateName == "MB_TCP_v1" && s3.MasterRtuTemplateName == "MB_RTU_v1" &&
                  s3.ColumnsPerRow == 3 && s3.RowStep == 700 && s3.ColumnStep == 1300 &&
                  s3.DataPacketGap == 25 && s3.DataChainStep == 40, // F63/F64
                "F13/F20: SaveSettings/LoadSettings roundtrip (MaxCountRegisters/Bits/Policy/MaxGap/имя TCP+RTU/адрес/шаблоны/раскладка)");
            Check(ModbusBlockPlannerService.SettingKeys.Length == 29 && // F63/F64: +DataPacketGap, +DataChainStep
                  ModbusBlockPlannerService.SettingKeys.Contains("MaxGap") &&
                  ModbusBlockPlannerService.SettingKeys.Contains("BlockNamePattern") &&
                  ModbusBlockPlannerService.SettingKeys.Contains("BlockNamePatternRtu") &&
                  ModbusBlockPlannerService.SettingKeys.Contains("FormatAddress") &&
                  ModbusBlockPlannerService.SettingKeys.Contains("DataOutTemplateMode") &&
                  ModbusBlockPlannerService.SettingKeys.Contains("DataOutTemplateFilePath") &&
                  ModbusBlockPlannerService.SettingKeys.Contains("DataOutTemplateName") &&
                  ModbusBlockPlannerService.SettingKeys.Contains("PouNamePattern") &&
                  ModbusBlockPlannerService.SettingKeys.Contains("DataOutPouNamePattern") &&
                  ModbusBlockPlannerService.SettingKeys.Contains("DataChainGap") && // F61
                  ModbusBlockPlannerService.SettingKeys.Contains("PouCommentPattern") && // F61
                  ModbusBlockPlannerService.SettingKeys.Contains("DataOutPouCommentPattern") && // F61
                  ModbusBlockPlannerService.SettingKeys.Contains("DataChainStep") && // F64
                  !ModbusBlockPlannerService.SettingKeys.Contains("PouName") &&
                  !ModbusBlockPlannerService.SettingKeys.Contains("ShowTitles"),
                "F13/F20/F22/F61/F64: SettingKeys — 29 ключей (F20: лимиты регистры/биты; F22: TCP/RTU-шаблоны; пакетирование/шаблоны/адрес/раскладка/F16-имя/F17-имена POU/F61-зазор+комментарии POU/F64-шаг цепочки)");
            // устаревший ключ в БД: LoadSettings игнорирует, SaveSettings удаляет
            using (var cnS = new System.Data.OleDb.OleDbConnection(
                       $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
            {
                cnS.Open();
                using var ins = cnS.CreateCommand();
                ins.CommandText = "INSERT INTO [SCADA_MB_SETTINGS] ([Name], [Value]) VALUES ('PouName', 'Old')";
                ins.ExecuteNonQuery();
            }
            var sOld = svc.LoadSettings(); // не падает, устаревший ключ игнорируется
            Check(sOld.MaxCountRegisters == 77 && sOld.MaxCountBits == 77,
                "F13: LoadSettings игнорирует устаревший ключ PouName");
            svc.SaveSettings(sOld);
            using (var cnS2 = new System.Data.OleDb.OleDbConnection(
                       $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
            {
                cnS2.Open();
                using var cmd = cnS2.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM [SCADA_MB_SETTINGS] WHERE [Name] = 'PouName'";
                Check(Convert.ToInt64(cmd.ExecuteScalar()) == 0,
                    "F13: SaveSettings удаляет устаревший ключ PouName");
            }

            // ===== F20: раздельные Count-лимиты (FC03/04 регистры 0-123, FC01/02 биты 0-246) =====
            // миграция старых баз: единый ключ MaxCount → оба лимита, при сохранении ключ удаляется
            using (var cnM = new System.Data.OleDb.OleDbConnection(
                       $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
            {
                cnM.Open();
                using var del = cnM.CreateCommand();
                del.CommandText = "DELETE FROM [SCADA_MB_SETTINGS] WHERE [Name] IN ('MaxCountRegisters','MaxCountBits','MaxCount')";
                del.ExecuteNonQuery();
                using var ins = cnM.CreateCommand();
                ins.CommandText = "INSERT INTO [SCADA_MB_SETTINGS] ([Name], [Value]) VALUES ('MaxCount', '64')";
                ins.ExecuteNonQuery();
            }
            var sMig = svc.LoadSettings();
            Check(sMig.MaxCountRegisters == 64 && sMig.MaxCountBits == 64,
                $"F20: миграция старого ключа MaxCount=64 → оба лимита (факт {sMig.MaxCountRegisters}/{sMig.MaxCountBits})");
            svc.SaveSettings(sMig);
            using (var cnM2 = new System.Data.OleDb.OleDbConnection(
                       $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
            {
                cnM2.Open();
                using var cmd = cnM2.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM [SCADA_MB_SETTINGS] WHERE [Name] = 'MaxCount'";
                Check(Convert.ToInt64(cmd.ExecuteScalar()) == 0,
                    "F20: SaveSettings удаляет устаревший единый ключ MaxCount");
            }
            var sMig2 = svc.LoadSettings();
            Check(sMig2.MaxCountRegisters == 64 && sMig2.MaxCountBits == 64,
                $"F20: после миграции новые ключи roundtrip (факт {sMig2.MaxCountRegisters}/{sMig2.MaxCountBits})");

            // лимит независим по типу функции: FC03 — по регистрам, FC02 — по битам
            var stLim = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 3, MaxCountBits = 1, DefaultPort = 502 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            var lim03 = stLim.Packets.Where(p => p.FuncCode == 3).ToList();
            var lim02 = stLim.Packets.Where(p => p.FuncCode == 2).ToList();
            Check(lim03.Count > 0 && lim03.All(p => p.Count <= 3),
                $"F20: FC03-пакеты ≤ MaxCountRegisters=3 (факт {string.Join(",", lim03.Select(p => p.Count))})");
            Check(lim02.Count > 0 && lim02.All(p => p.Count <= 1),
                $"F20: FC02-пакеты ≤ MaxCountBits=1 при MaxCountRegisters=3 (факт {string.Join(",", lim02.Select(p => p.Count))})");

            // значения вне диапазона блока подрезаются до констант (123 регистра / 246 бит)
            var ptsBig = new List<IoPoint>();
            for (int i = 0; i < 300; i++)
            {
                ptsBig.Add(MakeMbPoint($"BIG-B-{i}", $"TAG.B{i}", ip102, "02", i.ToString(), "BOOL"));
                ptsBig.Add(MakeMbPoint($"BIG-R-{i}", $"TAG.R{i}", ip103, "03", i.ToString(), "16-Bit Unsigned"));
            }
            var stBig = svc.BuildPlan(ptsBig, new ScadaMbGenSettings
            { MaxCountRegisters = 500, MaxCountBits = 300, DefaultPort = 502 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            var big02 = stBig.Packets.Where(p => p.FuncCode == 2).OrderBy(p => p.Address).ToList();
            var big03 = stBig.Packets.Where(p => p.FuncCode == 3).OrderBy(p => p.Address).ToList();
            Check(big02.Count == 2 && big02.Max(p => p.Count) == 246 &&
                  big03.Count == 3 && big03.Max(p => p.Count) == 123,
                $"F20: clamp к лимитам блока: FC02 [{string.Join(",", big02.Select(p => p.Count))}] max=246, " +
                $"FC03 [{string.Join(",", big03.Select(p => p.Count))}] max=123");

            // ===== F21: станции slave — параметры, автосбор из IO_LIST, протокол пакета =====
            // (1) миграция старой таблицы (Station/SlaveId) → колонки параметров станции
            using (var cnR = new System.Data.OleDb.OleDbConnection(
                       $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
            {
                cnR.Open();
                using var drop = cnR.CreateCommand();
                drop.CommandText = "DROP TABLE [SCADA_MB_SLAVE_RULES]";
                drop.ExecuteNonQuery();
                using var create = cnR.CreateCommand();
                create.CommandText =
                    "CREATE TABLE [SCADA_MB_SLAVE_RULES] (ID COUNTER PRIMARY KEY, [Station] TEXT(255), [SlaveId] LONG)";
                create.ExecuteNonQuery();
                using var insL = cnR.CreateCommand();
                insL.CommandText = "INSERT INTO [SCADA_MB_SLAVE_RULES] ([Station], [SlaveId]) VALUES ('LEGACY ST', 5)";
                insL.ExecuteNonQuery();
            }
            creator.EnsureServiceTables(); // F21: ALTER ADD параметров станции в старую таблицу
            var legacyRules = svc.LoadSlaveRules();
            Check(legacyRules.Count == 1 && legacyRules[0].Station == "LEGACY ST" &&
                  legacyRules[0].SlaveId == 5 && legacyRules[0].IpMain == "" &&
                  legacyRules[0].Protocol == "" && legacyRules[0].StopBit == 1 &&
                  legacyRules[0].DataBit == 8,
                $"F21: миграция старой SCADA_MB_SLAVE_RULES (строк {legacyRules.Count}, " +
                $"StopBit/DataBit дефолт {legacyRules.FirstOrDefault()?.StopBit}/{legacyRules.FirstOrDefault()?.DataBit})");

            // (2) roundtrip параметров станции (IP main/reserve, протокол, скорость, UART, таймаут, длина)
            svc.SaveSlaveRules(new List<MbSlaveIdRule>
            {
                new MbSlaveIdRule
                {
                    Station = "EBSM102", SlaveId = 7,
                    IpMain = "192.168.4.10(Default)\n255.255.255.0\nport 502",
                    IpReserve = "192.168.4.11", Protocol = "TCP", Speed = "100Mbps",
                    Parity = true, StopBit = 2, DataBit = 7, Reverse = true,
                    Timeout = 250, MaxLength = 260
                },
                new MbSlaveIdRule { Station = "RTU-COM1", SlaveId = 2, IpMain = "COM1 (Bus 1)", Protocol = "RTU" }
            });
            var rulesBack = svc.LoadSlaveRules();
            var rE = rulesBack.FirstOrDefault(r => r.Station == "EBSM102");
            var rR = rulesBack.FirstOrDefault(r => r.Station == "RTU-COM1");
            Check(rulesBack.Count == 2 && rE != null && rE.SlaveId == 7 && rE.Parity &&
                  rE.StopBit == 2 && rE.DataBit == 7 && rE.Reverse && rE.Timeout == 250 &&
                  rE.MaxLength == 260 && rE.Speed == "100Mbps" && rE.IpMain.Contains("192.168.4.10") &&
                  rE.IpReserve == "192.168.4.11" && rE.Protocol == "TCP" &&
                  rR != null && rR.Protocol == "RTU" && rR.IpMain == "COM1 (Bus 1)",
                $"F21: SCADA_MB_SLAVE_RULES roundtrip (строк {rulesBack.Count}: {rE?.Protocol}/{rR?.Protocol})");

            // (3) автосбор/merge из IO_LIST: авто-поля обновляются, ручные сохраняются
            var ptsF21 = new List<IoPoint>
            {
                MakeMbPoint("F21-A", "TAG.A1", "192.168.9.10(Default)\n255.255.255.0\nport 502",
                    "03", "0", "16-Bit Unsigned", "STA-TCP"),
                MakeMbPoint("F21-B", "TAG.B1", "COM1 (Bus 1)", "03", "0", "16-Bit Unsigned", "STA-RTU"),
                MakeMbPoint("F21-C", "TAG.C1", null, "03", "0", "16-Bit Unsigned", "STA-NOADDR")
            };
            ptsF21[0].Modbus!.SlaveIdBackup = "192.168.9.11";
            ptsF21[0].Modbus!.Speed = "100Mbps";
            var manualRule = new MbSlaveIdRule
            { Station = "STA-TCP", SlaveId = 9, Protocol = "RTU", Parity = true, MaxLength = 260 };
            var onlyManual = new MbSlaveIdRule { Station = "STA-MANUAL", SlaveId = 4 };
            var mergedRules = ModbusBlockPlannerService.MergeSlaveRulesFromPoints(
                new List<MbSlaveIdRule> { manualRule, onlyManual }, ptsF21);
            var mA = mergedRules.FirstOrDefault(r => r.Station == "STA-TCP");
            var mB = mergedRules.FirstOrDefault(r => r.Station == "STA-RTU");
            var mC = mergedRules.FirstOrDefault(r => r.Station == "STA-NOADDR");
            var mD = mergedRules.FirstOrDefault(r => r.Station == "STA-MANUAL");
            Check(mergedRules.Count == 4 && mA != null && mB != null && mC != null && mD != null,
                $"F21: merge — 3 станции из IO_LIST + ручная STA-MANUAL (факт {mergedRules.Count}: " +
                $"{string.Join(",", mergedRules.Select(r => r.Station))})");
            Check(mA != null && mA.SlaveId == 9 && mA.Protocol == "RTU" && mA.Parity && mA.MaxLength == 260 &&
                  mA.IpMain.Contains("192.168.9.10") && mA.IpReserve == "192.168.9.11" &&
                  mA.Speed == "100Mbps" && mA.Timeout == 100,
                $"F21: merge STA-TCP — ручные сохранены, авто обновлены (Speed {mA?.Speed}, Timeout {mA?.Timeout})");
            Check(mB != null && mB.Protocol == "RTU" && mB.IpMain == "COM1 (Bus 1)",
                $"F21: эвристика протокола COM → RTU (факт {mB?.Protocol})");
            Check(mC != null && mC.Protocol == "TCP" && mC.IpMain == "",
                $"F21: пустой адрес → TCP (факт {mC?.Protocol})");
            Check(mD != null && mD.SlaveId == 4,
                $"F21: ручная станция без точек IO_LIST не теряется (SlaveId {mD?.SlaveId})");

            // (4) нормализация/эвристика протокола и протокол пакетов из правил станций
            Check(ModbusBlockPlannerService.NormalizeProtocol("rtu") == "RTU" &&
                  ModbusBlockPlannerService.NormalizeProtocol("anything") == "TCP" &&
                  ModbusBlockPlannerService.DetectProtocol("192.168.1.1") == "TCP" &&
                  ModbusBlockPlannerService.DetectProtocol("COM2") == "RTU" &&
                  ModbusBlockPlannerService.DetectProtocol("") == "TCP",
                "F21: NormalizeProtocol/DetectProtocol (IP → TCP, COM → RTU, пусто → TCP)");
            var stProto = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502, DefaultSlaveId = 1 },
                new List<MbSlaveIdRule>
                { new MbSlaveIdRule { Station = "IMCS EBSM102", SlaveId = 7, Protocol = "RTU" } },
                "MB_TCP_MASTER_4_1");
            Check(stProto.Packets.Count > 0 && stProto.Packets.All(p => p.Protocol == "RTU" && p.IsRtu),
                $"F21: правило «IMCS EBSM102 → RTU» → пакеты Protocol=RTU (факт " +
                $"{string.Join(",", stProto.Packets.Select(p => p.Protocol).Distinct())})");
            Check(st.Packets.Count > 0 && st.Packets.All(p => p.Protocol == "TCP" && !p.IsRtu),
                "F21: без правила станции → Protocol=TCP (историческое поведение)");

            // ===== F22: залитые шаблоны — SCADA_MASTER_TEMPLATES / SCADA_DATAIN_TEMPLATES =====
            svc.SaveMasterTemplate("MB_TCP_v1", "<BufScadaPOUS tcp/>", "", @"c:\Work\tcp.xml");
            svc.SaveMasterTemplate("MB_RTU_v1", "<BufScadaPOUS rtu/>", "");
            svc.SaveMasterTemplate("MB_TCP_v1", "<BufScadaPOUS tcp2/>", "", @"c:\Work\tcp.xml"); // замена по имени
            var mtList = svc.LoadMasterTemplates();
            var mtTcp = mtList.FirstOrDefault(t => t.Name == "MB_TCP_v1");
            var mtRtu = mtList.FirstOrDefault(t => t.Name == "MB_RTU_v1");
            Check(mtList.Count == 2 && mtTcp != null && mtTcp.Protocol == "TCP" &&
                  mtTcp.Body == "<BufScadaPOUS tcp2/>" && mtTcp.FilePath == @"c:\Work\tcp.xml" &&
                  mtRtu != null && mtRtu.Protocol == "RTU" && mtRtu.Body == "<BufScadaPOUS rtu/>",
                $"F22: SCADA_MASTER_TEMPLATES roundtrip + замена + эвристика протокола " +
                $"(строк {mtList.Count}: {mtTcp?.Protocol}/{mtRtu?.Protocol})");
            Check(svc.DeleteMasterTemplate("MB_TCP_v1") && !svc.DeleteMasterTemplate("MB_TCP_v1") &&
                  svc.LoadMasterTemplates().Count == 1,
                "F22: DeleteMasterTemplate (повторный — false)");
            svc.SaveMasterTemplate("MB_TCP_v1", "<BufScadaPOUS tcp2/>", "");

            svc.SaveDataInTemplate("DataIn_A", "<DataIn a/>", @"c:\Work\di.xml");
            var diList = svc.LoadDataInTemplates();
            var diA = diList.FirstOrDefault(t => t.Name == "DataIn_A");
            Check(diList.Count == 1 && diA != null && diA.Body == "<DataIn a/>" &&
                  diA.FilePath == @"c:\Work\di.xml",
                $"F22: SCADA_DATAIN_TEMPLATES roundtrip (строк {diList.Count})");
            Check(svc.DeleteDataInTemplate("DataIn_A") && svc.LoadDataInTemplates().Count == 0,
                "F22: DeleteDataInTemplate");

            // ===== F22: WriteProtocolSplit — генерация по протоколу станции =====
            // (a) только TCP-пакеты → идентичен Write (rtuText не влияет)
            var setA = new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 };
            var genDirect = xmlSvc.Write(st.Packets, tpl, setA);
            var genSplitA = xmlSvc.WriteProtocolSplit(st.Packets, tpl, null, setA);
            Check(genDirect.PousXml == genSplitA.PousXml &&
                  genDirect.BindingsXml == genSplitA.BindingsXml &&
                  genDirect.Warnings.Count == genSplitA.Warnings.Count,
                "F22: WriteProtocolSplit без RTU-пакетов идентичен Write");

            // (b) смешанные TCP+RTU → один документ: оба мастера, уникальные T11/Info
            var ptsMix = new List<IoPoint>
            {
                MakeMbPoint("MX-A1", "TAG.MX1", ip102, "03", "0", "16-Bit Unsigned", "STA-A"),
                MakeMbPoint("MX-A2", "TAG.MX2", ip102, "03", "1", "16-Bit Unsigned", "STA-A"),
                MakeMbPoint("MX-B1", "TAG.MX3", ip103, "03", "0", "16-Bit Unsigned", "STA-B"),
                MakeMbPoint("MX-B2", "TAG.MX4", ip103, "03", "1", "16-Bit Unsigned", "STA-B")
            };
            var mixRules = new List<MbSlaveIdRule>
            {
                new MbSlaveIdRule { Station = "STA-A", SlaveId = 1, Protocol = "TCP" },
                new MbSlaveIdRule { Station = "STA-B", SlaveId = 2, Protocol = "RTU" }
            };
            var stMix = svc.BuildPlan(ptsMix, new ScadaMbGenSettings
            { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 },
                mixRules, "MB_TCP_MASTER_4_1");
            Check(stMix.Packets.Count == 2 && stMix.Packets.Count(p => p.IsRtu) == 1,
                $"F22: план смешанных станций — 2 пакета, 1 RTU " +
                $"(факт {stMix.Packets.Count}/{stMix.Packets.Count(p => p.IsRtu)})");

            var genMix = xmlSvc.WriteProtocolSplit(stMix.Packets, tpl, tpl, new ScadaMbGenSettings
            { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502, ColumnsPerRow = 2 });
            var mixDoc = System.Xml.Linq.XDocument.Parse(genMix.PousXml);
            Check(mixDoc.Root != null, "F22: склейка TCP+RTU — XML well-formed, корень BufScadaPOUS");
            var mixT11 = mixDoc.Root!.Descendants("Block")
                .Select(b => (string?)b.Attribute("T11ID")).Where(v => v != null).ToList();
            Check(mixT11.Count > 0 && mixT11.Distinct().Count() == mixT11.Count,
                $"F22: склейка — все T11ID уникальны (блоков {mixT11.Count})");
            var mixInfo = mixDoc.Root!.Descendants("Block")
                .Select(b => (string?)b.Attribute("Info") ?? "").Where(v => v.Length > 0).ToList();
            // F22: уникальность ИМЕННЫХ Info (мастера/теги/шаблонные клоны); числовые Info —
            // локальные константы шаблона («1»/«100»/«4000»), клонируются в каждый POU — допустимы
            var mixNamedInfo = mixInfo.Where(v =>
                !int.TryParse(v, out _)).ToList();
            var dupInfo = mixNamedInfo.GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => $"{g.Key}×{g.Count()}").ToList();
            Check(mixNamedInfo.Distinct(StringComparer.OrdinalIgnoreCase).Count() == mixNamedInfo.Count,
                $"F22: склейка — Info блоков без дублей ({mixInfo.Count}, именных {mixNamedInfo.Count}; дубли: {string.Join(", ", dupInfo)})");
            Check(genMix.PousXml.Contains("MB_TCP_MASTER_4_1") &&
                  genMix.PousXml.Contains("MB_TCP_MASTER_4_2"),
                "F22: в одном документе оба мастера (TCP- и RTU-станции)");
            Check(genMix.PousXml.Contains("192,168,4,10,502") &&
                  genMix.PousXml.Contains("192,168,5,10,502"),
                "F22: IV каждого мастера — адрес его станции (TCP …4.10, RTU …5.10)");
            Check(genMix.MasterMeta.Count == 2,
                $"F22: MasterMeta — 2 мастера (факт {genMix.MasterMeta.Count})");

            // (c) RTU-шаблон не задан → RTU-пакет пропущен с предупреждением
            var genNoRtu = xmlSvc.WriteProtocolSplit(stMix.Packets, tpl, null, new ScadaMbGenSettings
            { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 });
            Check(genNoRtu.Warnings.Any(w => w.Contains("RTU") && w.Contains("пропущены")) &&
                  genNoRtu.PousXml.Contains("MB_TCP_MASTER_4_1") &&
                  !genNoRtu.PousXml.Contains("MB_TCP_MASTER_4_2"),
                $"F22: без RTU-шаблона — предупреждение и только TCP-мастер " +
                $"(warnings: {string.Join(" | ", genNoRtu.Warnings)})");

            // inline-шаблон: MEMO roundtrip (тело шаблона > 5 КБ)
            svc.SaveInlineTemplate(tpl);
            string tplBack = svc.LoadInlineTemplate() ?? "";
            Check(tplBack == tpl && tplBack.Length > 5000,
                $"F13: SCADA_MB_TEMPLATE MEMO roundtrip (факт {tplBack.Length} симв.)");
            var infoInline = xmlSvc.Parse(tplBack);
            Check(infoInline.Valid && infoInline.MasterNameBase == "MB_TCP_MASTER_4_1",
                "F13: шаблон из SCADA_MB_TEMPLATE валиден (мастер из MEMO)");

            // ===== F23: переменные мастер-шаблона (SCADA_TEMPLATE_VARS) =====
            // (a) парс входов мастера из связей шаблона
            var inVars = xmlSvc.ParseInputVariables(tpl);
            string inVarsTxt = string.Join(",", inVars);
            foreach (var ivName in new[] { "SlaveId", "TimeOut", "Mode", "Address", "Count", "Request" })
                Check(inVars.Contains(ivName, StringComparer.OrdinalIgnoreCase),
                    $"F23: ParseInputVariables содержит «{ivName}» (факт {inVarsTxt})");

            // (b) ColumnValues первой точки пакета — источник токенов и маппинга
            var fpVars = stRules.Packets[0];
            bool hasTagCol = fpVars.ColumnValues.TryGetValue("Tag", out var fpTag) && fpTag.Length > 0;
            Check(hasTagCol && fpVars.ColumnValues.ContainsKey("M_Timeout"),
                $"F23: ColumnValues пакета — Tag и M_Timeout (Tag={fpTag}, колонок {fpVars.ColumnValues.Count})");

            // (c) CRUD SCADA_TEMPLATE_VARS: сохранить → заменить → прочитать
            int sv1 = svc.SaveTemplateVars("master", new List<MbTemplateVar>
            {
                new MbTemplateVar { Scope = "master", Var = "TimeOut", IoColumn = "M_Timeout", Literal = "" },
                new MbTemplateVar { Scope = "master", Var = "SlaveId", IoColumn = "", Literal = "7" }
            });
            var load1 = svc.LoadTemplateVars("master");
            Check(sv1 == 2 && load1.Count == 2 &&
                  load1.Any(v => v.Var == "TimeOut" && v.IoColumn == "M_Timeout") &&
                  load1.Any(v => v.Var == "SlaveId" && v.Literal == "7"),
                $"F23: сохранение/чтение SCADA_TEMPLATE_VARS (факт {sv1}/{load1.Count})");
            int sv2 = svc.SaveTemplateVars("master", new List<MbTemplateVar>
            {
                new MbTemplateVar { Scope = "master", Var = "TimeOut", IoColumn = "M_Timeout", Literal = "" }
            });
            var load2 = svc.LoadTemplateVars("master");
            Check(sv2 == 1 && load2.Count == 1 && load2[0].IoColumn == "M_Timeout",
                $"F23: повторное сохранение заменяет строки скоупа (факт {sv2}/{load2.Count})");

            // (d) F35: VarValues (приоритет маппинга входов F23) удалён — входы мастера
            // строго из плана; TimeOut из M_Timeout (100 мс), скрытых подстановок нет
            var genPlan = xmlSvc.Write(stRules.Packets, tpl,
                new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502, ColumnsPerRow = 2 });
            Check(genPlan.PousXml.Contains("Info=\"100\""),
                "F23→F35: VarValues удалён — TimeOut из плана (100 мс из M_Timeout)");

            // ===== F25: маппинг «на каждый шаблон» (TemplateName) =====
            // после F23(c) в скоупе master есть легаси-строка (пустое имя): TimeOut→M_Timeout
            int f25a = svc.SaveTemplateVars("master", "ModbusTCP_v0", new List<MbTemplateVar>
            {
                new MbTemplateVar { Scope = "master", Var = "TimeOut", IoColumn = "", Literal = "999" },
                new MbTemplateVar { Scope = "master", Var = "SlaveId", IoColumn = "", Literal = "7" }
            });
            int f25b = svc.SaveTemplateVars("master", "ModbusTCP_v1",
                new List<MbTemplateVar>
                {
                    new MbTemplateVar { Scope = "master", Var = "Count", IoColumn = "", Literal = "50" }
                });
            var f25Load0 = svc.LoadTemplateVars("master", "ModbusTCP_v0");
            var f25Load1 = svc.LoadTemplateVars("master", "ModbusTCP_v1");
            Check(f25a == 2 && f25b == 1,
                $"F25: сохранение маппинга под двумя ключами шаблонов (факт {f25a}/{f25b})");
            Check(f25Load0.Count == 2 &&
                  f25Load0.Any(v => v.Var == "TimeOut" && v.Literal == "999" && v.IoColumn == "") &&
                  f25Load0.Any(v => v.Var == "SlaveId" && v.Literal == "7"),
                $"F25: строки ключа ModbusTCP_v0, приоритет ключа над легаси (строк {f25Load0.Count})");
            Check(f25Load1.Count == 2 &&
                  f25Load1.Any(v => v.Var == "Count" && v.Literal == "50") &&
                  f25Load1.Any(v => v.Var == "TimeOut" && v.IoColumn == "M_Timeout"),
                $"F25: чужой шаблон не затёрся + легаси-fallback TimeOut (строк {f25Load1.Count})");
            var f25All = svc.LoadTemplateVars("master");
            Check(f25All.Count == 4, // легаси 1 + key0 2 + key1 1
                $"F25: общий чтение скоупа — все ключи целы (строк {f25All.Count}, ожидалось 4)");
            int f25c = svc.SaveTemplateVars("master", "ModbusTCP_v0",
                new List<MbTemplateVar>
                {
                    new MbTemplateVar { Scope = "master", Var = "TimeOut", IoColumn = "", Literal = "999" }
                });
            var f25Load1b = svc.LoadTemplateVars("master", "ModbusTCP_v1");
            var f25Allb = svc.LoadTemplateVars("master");
            Check(f25c == 1 && f25Load1b.Any(v => v.Var == "Count" && v.Literal == "50") &&
                  f25Allb.Count == 3, // легаси 1 + key0 1 + key1 1
                $"F25: замена ключа ModbusTCP_v0 не затрагивает ModbusTCP_v1 " +
                $"(факт {f25c}, строк чужого {f25Load1b.Count}, всего {f25Allb.Count})");

            // ===== F24: токены {ИмяКолонки} в паттернах имён =====
            var pktT = new MbPacketPlan { No = 2, SlaveId = 5, Port = 502, Station = "STA-1" };
            pktT.ColumnValues["Tag"] = "MY_TAG_1";
            pktT.ColumnValues["M_SlaveStation"] = "IMCS EBSM102";
            string nCol = ScadaTemplateXmlService.ApplyBlockNamePattern("MB_{Tag}_{Seq}", pktT, "BASE");
            Check(nCol == "MB_MY_TAG_1_2", $"F24: ApplyBlockNamePattern {{Tag}}+{{Seq}} (факт {nCol})");
            string nStCol = ScadaTemplateXmlService.ApplyBlockNamePattern("MB_{M_SlaveStation}_{Seq}", pktT, "BASE");
            Check(nStCol == "MB_IMCS_EBSM102_2",
                $"F24: токен колонки с пробелом санитизируется (факт {nStCol})");
            string nStTok = ScadaTemplateXmlService.ApplyBlockNamePattern("MB_{Station}_{Seq}", pktT, "BASE");
            Check(nStTok == "MB_STA-1_2",
                $"F24: служебный токен {{Station}} сохранён (факт {nStTok})");
            string nUnk = ScadaTemplateXmlService.ApplyBlockNamePattern("MB_{Nope}_{Seq}", pktT, "BASE");
            Check(nUnk.Contains("{Nope}") && nUnk.EndsWith("_2"),
                $"F24: неизвестный токен остаётся литералом (факт {nUnk})");
            string pouCol = ScadaTemplateXmlService.BuildPouName("{Tag}_POU_{Seq}", "MB_TCP", pktT);
            Check(pouCol == "MY_TAG_1_POU_2", $"F24: BuildPouName {{Tag}}+{{Seq}} (факт {pouCol})");
            string pouUnk = ScadaTemplateXmlService.BuildPouName("{Nope}_POU", "MB_TCP", pktT);
            Check(pouUnk.Contains("{Nope}"), $"F24: BuildPouName неизвестный токен как литерал (факт {pouUnk})");

            // ===== F13.1: политика Gap (допуск прорехи ≤ MaxGap) =====
            var stGap5 = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502, Policy = MbPacketPolicy.Gap, MaxGap = 5 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            var gap5Group = stGap5.Packets.Where(p => p.FuncCode == 2).OrderBy(p => p.Address).ToList();
            Check(gap5Group.Count == 2 && gap5Group[0].Count == 3 && gap5Group[1].Address == 10,
                $"F13: Gap(5): прореха 3→10 = 6 > 5 → 2 пакета (факт {gap5Group.Count})");
            var stGap7 = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502, Policy = MbPacketPolicy.Gap, MaxGap = 7 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            var gap7Group = stGap7.Packets.Where(p => p.FuncCode == 2).OrderBy(p => p.Address).ToList();
            Check(gap7Group.Count == 1 && gap7Group[0].Address == 0 && gap7Group[0].Count == 11 &&
                  gap7Group[0].PointCount == 4,
                $"F13: Gap(7): прореха 6 ≤ 7 → 1 пакет Count=11 (факт {gap7Group.Count}/{gap7Group[0]?.Count})");
            var stGapBig = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 8, MaxCountBits = 8, DefaultPort = 502, Policy = MbPacketPolicy.Gap, MaxGap = 100 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            var gapBigGroup = stGapBig.Packets.Where(p => p.FuncCode == 2).OrderBy(p => p.Address).ToList();
            Check(gapBigGroup.Count == 2 && gapBigGroup[0].Count == 3 && gapBigGroup[1].Count == 1,
                $"F13: Gap(100)+MaxCount=8: лимит рвёт пакет (0-2 и 10) (факт {string.Join(",", gapBigGroup.Select(p => p.Count))})");
            var stGap0 = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502, Policy = MbPacketPolicy.Gap, MaxGap = 0 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            var stContig = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502, Policy = MbPacketPolicy.Contiguous },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            Check(stGap0.Packets.Count == stContig.Packets.Count &&
                  stGap0.Packets.Zip(stContig.Packets).All(z => z.First.Address == z.Second.Address &&
                                                                z.First.Count == z.Second.Count),
                "F13: Gap(0) идентичен Contiguous (пакеты совпадают)");

            // ===== F13.1: настраиваемое имя блока (BlockNamePattern) =====
            var pktName = new MbPacketPlan { No = 3, SlaveId = 7, Station = "IMCS EBSM102", Ip1 = 192, Ip2 = 168, Ip3 = 4, Ip4 = 10, FuncCode = 3 };
            Check(ScadaTemplateXmlService.ApplyBlockNamePattern("MB_TCP_Master_{Slave}_{Seq}", pktName, "X_1")
                  == "MB_TCP_Master_7_3",
                "F13: ApplyBlockNamePattern «MB_TCP_Master_{Slave}_{Seq}» → MB_TCP_Master_7_3");
            Check(ScadaTemplateXmlService.ApplyBlockNamePattern("M_{Station}_{Ip}_{Func}", pktName, "X_1")
                  == "M_IMCS_EBSM102_192_168_4_10_3",
                "F13: токены {Station}/{Ip}/{Func} (пробелы станции → «_»)");
            Check(ScadaTemplateXmlService.ApplyBlockNamePattern("", pktName, "MB_TCP_MASTER_4_1")
                  == ScadaTemplateXmlService.MasterNameFor("MB_TCP_MASTER_4_1", 3),
                "F13: пустой паттерн — историческое имя MB_TCP_MASTER_4_3");

            // ===== F62: ведущий/замыкающий «_» паттерна сохраняется =====
            // SanitizeNamePart раньше делал Trim('_') по результату — паттерн
            // «_MB_{M_SlaveStation}_{Typno}» терял первый символ; срезаются только
            // «_», порождённые заменой пробела/точки на краю
            string n62 = ScadaTemplateXmlService.ApplyBlockNamePattern(
                "_MB_{M_SlaveStation}_{Seq}", pktT, "BASE");
            Check(n62 == "_MB_IMCS_EBSM102_2",
                $"F62: ведущий «_» паттерна блока сохраняется (ожидалось _MB_IMCS_EBSM102_2, факт {n62})");
            string n62t = ScadaTemplateXmlService.ApplyBlockNamePattern(
                "MB_{Station}_", pktName, "X_1");
            Check(n62t == "MB_IMCS_EBSM102_",
                $"F62: замыкающий «_» паттерна блока сохраняется (факт {n62t})");
            Check(ScadaTemplateXmlService.ApplyBlockNamePattern(".{Seq}", pktName, "X_1") == "3",
                "F62: «_», порождённый заменой ведущей точки, по-прежнему срезается (.3 → 3)");
            string p62 = ScadaTemplateXmlService.BuildPouName("_{Tag}_POU", "MB_TCP", pktT);
            Check(p62 == "_MY_TAG_1_POU",
                $"F62: ведущий «_» паттерна имени POU сохраняется (факт {p62})");
            var stName = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502, DefaultSlaveId = 1, BlockNamePattern = "MB_TCP_Master_{Slave}_{Seq}" },
                new List<MbSlaveIdRule> { new MbSlaveIdRule { Station = "IMCS EBSM102", SlaveId = 7 } },
                "MB_TCP_MASTER_4_1");
            Check(stName.Packets.Select(p => p.MasterName).All(n => n.StartsWith("MB_TCP_Master_7_")),
                $"F13: имена пакетов по паттерну (факт {string.Join(",", stName.Packets.Select(p => p.MasterName))})");
            var stNameFix = svc.BuildPlan(pts, new ScadaMbGenSettings
            { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502, BlockNamePattern = "SAME_{Func}" },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            Check(stNameFix.Packets.Select(p => p.MasterName).Distinct().Count() == stNameFix.Packets.Count,
                "F13: коллизия имени (нет {Seq}) — MakeUniqueName добавляет суффикс");

            // ===== F13.1: шаблон привязок Data_Out (отдельный от шаблона блоков) =====
            string doTplPath = Path.Combine(@"c:\VSCode\Work", "Data_Out_пример.xml");
            Check(File.Exists(doTplPath), $"F13: шаблон привязок найден: {doTplPath}");
            string doTpl = File.ReadAllText(doTplPath);
            var doSvc = new ScadaDataOutXmlService();
            var doInfo = doSvc.Parse(doTpl);
            Check(doInfo.Valid && doInfo.ExemplarCount == 4 &&
                  doInfo.Exemplars.ContainsKey("BOOL") && doInfo.Exemplars.ContainsKey("INT") &&
                  doInfo.Exemplars.ContainsKey("DINT") && doInfo.Exemplars.ContainsKey("REAL"),
                $"F13: шаблон привязок — 4 эталона BOOL/INT/DINT/REAL ({doInfo.Summary})");
            Check(doInfo.Exemplars["BOOL"].SourceCount == 1 && doInfo.Exemplars["REAL"].SourceCount == 2,
                "F13: BOOL-эталон — 1 источник, REAL-эталон — 2 регистра");
            Check(ScadaDataOutXmlService.ResolveDefaultTemplatePath() != null,
                "F18: шаблон привязок доступен по умолчанию (файл Work/Data_Out_пример.xml)");
            Check(!doSvc.Parse("мусор").Valid && !doSvc.Parse(
                  "<?xml version=\"1.0\"?><BufScadaPOUS><POUS><OnePOU Disc=\"BOOL\"/>" +
                  "</POUS></BufScadaPOUS>").Valid,
                "F13: шаблон привязок: мусор/эталон без цепочки — невалидны");
            Check(ScadaDataOutXmlService.MapTypeKey("BOOL") == "BOOL" &&
                  ScadaDataOutXmlService.MapTypeKey("16-Bit Unsigned") == "INT" &&
                  ScadaDataOutXmlService.MapTypeKey("32-Bit Signed") == "DINT" &&
                  ScadaDataOutXmlService.MapTypeKey("32-Bit Floating") == "REAL" &&
                  ScadaDataOutXmlService.MapTypeKey("") == "INT",
                "F13: MapTypeKey (BOOL/INT/DINT/REAL/пусто→INT)");
            Check(ScadaDataOutXmlService.TagNameFor("1115-PM-101A") == "_1115_PM_101A" &&
                  ScadaDataOutXmlService.TagNameFor("_X") == "_X",
                "F13: TagNameFor (тире/пробелы → «_», ведущий «_»)");

            // генерация привязок из эталонов по типам точек (stRules: BOOL×4, REAL×2, FC06×1)
            // F30: dataInTemplates — datain-шаблон (Work\MB_AO_WORD16.xml) для пакета записи C-WR
            var doWarn = new List<string>();
            string datainTpl = File.ReadAllText(@"c:\VSCode\Work\MB_AO_WORD16.xml");
            var dataInDict = new Dictionary<string, string> { ["MB_AO_WORD16"] = datainTpl };
            var doGen = doSvc.Write(stRules.Packets, doTpl, gen.MasterMeta,
                new ScadaMbGenSettings(), doWarn, null, null, dataInDict);
            var dxDoc = System.Xml.Linq.XDocument.Parse(doGen); // well-formed
            Check(dxDoc.Root != null && dxDoc.Root.Name == "BufScadaPOUS",
                "F13: привязки из шаблона — well-formed, корень BufScadaPOUS");
            int dxPous = dxDoc.Root?.Element("POUS")?.Elements("OnePOU").Count() ?? 0;
            Check(dxPous == stRules.Packets.Count,
                $"F13: привязки — OnePOU на каждый пакет ({dxPous} == {stRules.Packets.Count})");
            Check(doGen.Contains("Info=\"MB_TCP_MASTER_4_1.DataOut[0]\"") &&
                  doGen.Contains("T11Text=\".DataOut[0]\""),
                "F13: BOOL-эталон: источник MB_TCP_MASTER_4_1.DataOut[0] + T11Text=.DataOut[0]");
            Check(doGen.Contains("Info=\"MB_TCP_MASTER_4_4.DataOut[4]\"") &&
                  doGen.Contains("Info=\"MB_TCP_MASTER_4_4.DataOut[5]\""),
                "F18: REAL-эталон: источник float-пакета (4-й F30) байты DataOut[4]/DataOut[5] (регистр 2 → байты 4-5)");
            // F30: цепочка записи из MB_AO_WORD16 — тег C-WR → DataIn[0]/DataIn[1] пакета FC06
            var dataInHits = System.Text.RegularExpressions.Regex
                .Matches(doGen, @"Info=""[^""]*DataIn\[\d+\]""").Select(m => m.Value).ToList();
            var wrHits = System.Text.RegularExpressions.Regex
                .Matches(doGen, @"Info=""[^""]*C[_-]WR[^""]*""").Select(m => m.Value).ToList();
            Check(doGen.Contains("Info=\"MB_TCP_MASTER_4_3.DataIn[0]\"") &&
                  doGen.Contains("Info=\"MB_TCP_MASTER_4_3.DataIn[1]\"") &&
                  doGen.Contains("Info=\"_C_WR\""), // TagNameFor: ведущий «_» (как MakeMarka)
                "F30: datain-эталон — привязка записи MB_TCP_MASTER_4_3.DataIn[0]/[1] + тег _C_WR " +
                $"(факт DataIn: [{string.Join("; ", dataInHits)}] теги: [{string.Join("; ", wrHits)}] " +
                $"warnings: {string.Join("; ", doWarn)})");
            // F35: приёмник без строки маппинга «Объекты data-шаблонов» — Info шаблона
            // БЕЗ изменений (не тег привязки — скрытых подстановок нет)
            Check(doGen.Contains("Info=\"_8100_TAG_0001_A_FAIL.Xin\""),
                "F13→F35: BOOL-эталон: приёмник без маппинга — Info шаблона как в шаблоне");
            Check(doGen.Contains("Info=\"_8100_TAG_0004_A_REAL\""),
                "F13→F35: REAL-эталон: приёмники без маппинга — Info шаблона (без члена)");
            var dxMaster = dxDoc.Root?.Descendants("Block")
                .FirstOrDefault(b => ((string?)b.Attribute("Info")) == "MB_TCP_MASTER_4_1.DataOut[0]");
            Check(dxMaster?.Element("Params")?.Attribute("cardId")?.Value == masterCards[0] &&
                  dxMaster?.Element("Params")?.Element("IV")?.Value?.Contains("192,168,4,10,502") == true,
                "F13: источник привязок наследует cardId/IV мастера пакета (новый cardId F60)");
            Check(System.Text.RegularExpressions.Regex
                    .IsMatch(doGen, "<rec[^>]*Info=\"_8100_TAG_0001_A_FAIL\\.Xin\""),
                "F13→F35: ISACARDSINFO привязок — rec карточки приёмника (имя шаблона)");
            Check(doWarn.Count == 0,
                $"F13: генерация привязок без предупреждений (факт: {string.Join("; ", doWarn)})");
            // F18: граница в байтах — REAL-эталон (2 байта) на регистре 1 (байт 2) пакета
            // Count=1 (2 байта данных) выходит за границу → пропуск с предупреждением
            var pktSmall = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, Address = 0, Count = 1, MasterName = "MB_S",
                    Bindings = { new MbBinding { Tag = "T-1", Address = 1, Offset = 1,
                        BlockName = "MB_S", DataType = "32-Bit Floating" } } }
            };
            var doWarn2 = new List<string>();
            doSvc.Write(pktSmall, doTpl, new Dictionary<string, (int, string)>(), new ScadaMbGenSettings(), doWarn2);
            Check(doWarn2.Any(w => w.Contains("выходит за данные пакета")),
                $"F18: REAL-привязка за границей байтов пакета — предупреждение (факт: {string.Join("; ", doWarn2)})");

            // inline-шаблон привязок: MEMO roundtrip в строке «dataout»
            svc.SaveInlineTemplate(ModbusBlockPlannerService.InlineDataOutTemplateName, doTpl);
            string doBack = svc.LoadInlineTemplate(ModbusBlockPlannerService.InlineDataOutTemplateName) ?? "";
            Check(doBack == doTpl && svc.LoadInlineTemplate() == tplBack,
                "F13: SCADA_MB_TEMPLATE — строки «current» и «dataout» независимы");

            // (9) миграция: удаление таблицы шаблона и воссоздание EnsureServiceTables
            using (var cnF = new System.Data.OleDb.OleDbConnection(
                       $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
            {
                cnF.Open();
                using var cmdDrop = cnF.CreateCommand();
                cmdDrop.CommandText = $"DROP TABLE [{Schema.ScadaMbTemplateTable}]";
                cmdDrop.ExecuteNonQuery();
            }
            Check(!creator.TableExists(Schema.ScadaMbTemplateTable), "F13: таблица шаблона удалена");
            creator.EnsureServiceTables();
            Check(creator.TableExists(Schema.ScadaMbTemplateTable),
                "F13: миграция — EnsureServiceTables воссоздаёт SCADA_MB_TEMPLATE");
            Check(svc.LoadInlineTemplate() == null, "F13: после миграции inline-шаблон пуст");

            // миграция IO_LIST: EnsureColumns добавляет M_BusBlock/M_BusDataOut/M_Marka старой таблице
            Check(Schema.DataColumns.Contains("M_BusBlock") && Schema.DataColumns.Contains("M_BusDataOut") &&
                  Schema.DataColumns.Contains("M_Marka") && // F27
                  Schema.DataColumns[Array.IndexOf(Schema.DataColumns, "M_Marka") + 1] == "CBL_Id",
                "F13/F27: M_BusBlock/M_BusDataOut/M_Marka в DataColumns перед CBL_Id");

            // ===== F14: поле Typno, фильтр, SCADA_TYPNO_MAP, имена TCP/RTU, формат Address =====

            // (1) поле Typno в схеме/реестре
            Check(Array.IndexOf(Schema.DataColumns, "Logic") + 1 == Array.IndexOf(Schema.DataColumns, "Typno") &&
                  Schema.DataColumns[Array.IndexOf(Schema.DataColumns, "Typno") + 1] == "Tag",
                "F14: Typno в DataColumns между Logic и Tag");
            Check(FieldRegistry.HasColumn("Typno"), "F14: FieldRegistry знает Typno");
            var typnoProbe = new IoPoint();
            FieldRegistry.SetValue(typnoProbe, "Typno", "PID1200-01R");
            Check(typnoProbe.Identification.Typno == "PID1200-01R",
                "F14: FieldRegistry.SetValue → Identification.Typno");
            Check(creator.GetColumns(Schema.DefaultTable).Contains("Typno", StringComparer.OrdinalIgnoreCase),
                "F14: колонка Typno в IO_LIST (DDL/миграция)");

            // (2) таблица SCADA_TYPNO_MAP: создание, roundtrip, валидация
            Check(creator.TableExists(Schema.ScadaTypnoMapTable), "F14: таблица SCADA_TYPNO_MAP создана");
            int tnSaved = svc.SaveTypnoMap(new List<MbTypnoMapRule>
            {
                new() { Typno = "PID1200-01R", Exemplar = "REAL" },
                new() { Typno = "  KLPD-01 ", Exemplar = "BOOL" },   // пробелы срезаются
                new() { Typno = "NO-EX", Exemplar = "  " },           // без эталона — отброшено
                new() { Typno = "  ", Exemplar = "INT" },             // без Typno — отброшено
                new() { Typno = "pid1200-01r", Exemplar = "DINT" }    // дубль IgnoreCase — последний
            });
            Check(tnSaved == 2, $"F14: SaveTypnoMap — 2 правила (валидация/дубль) (факт {tnSaved})");
            var tnBack = svc.LoadTypnoMap();
            Check(tnBack.Count == 2 &&
                  tnBack.Any(r => r.Typno == "PID1200-01R" && r.Exemplar == "DINT") &&
                  tnBack.Any(r => r.Typno == "KLPD-01" && r.Exemplar == "BOOL"),
                $"F14: LoadTypnoMap roundtrip (факт: {string.Join("; ", tnBack.Select(r => r.Typno + "→" + r.Exemplar))})");
            svc.SaveTypnoMap(new List<MbTypnoMapRule>());
            Check(svc.LoadTypnoMap().Count == 0, "F14: SaveTypnoMap — полная замена (пустой список очищает)");

            // (3) фильтр «без Typno → вне генерации»; F18: float-точки — FC03 (регистры),
            // FC02 отдан BOOL-точкам C/D (из битов float не собрать — байтовая модель)
            var ptsF14 = new List<IoPoint>
            {
                MakeMbPoint("F14-A", "PLC_0", ip102, "03", "0", "32-Bit Floating", typno: "ALG-1"),
                MakeMbPoint("F14-B", "PLC_1", ip102, "03", "2", "32-Bit Floating", typno: "ALG-1"),
                MakeMbPoint("F14-C", "PLC_2", ip102, "02", "4", "BOOL", typno: null),   // без Typno
                MakeMbPoint("F14-D", "PLC_3", ip102, "02", "5", "BOOL", typno: "  ")     // пустой Typno
            };
            var stF14 = svc.BuildPlan(ptsF14,
                new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            Check(stF14.SkippedNoTypno == 2 && stF14.WithAddressing == 4,
                $"F14: без Typno — SkippedNoTypno=2 из 4 с адресацией (факт {stF14.SkippedNoTypno}/{stF14.WithAddressing})");
            Check(stF14.Packets.Count == 1 && stF14.Packets[0].Count == 4 && stF14.Packets[0].PointCount == 2,
                $"F14: в пакете только точки с Typno (пакетов/Count/PointCount = 1/4/2) (факт {stF14.Packets.Count}/{stF14.Packets[0]?.Count}/{stF14.Packets[0]?.PointCount})");
            Check(stF14.Warnings.Any(w => w.Contains("без Typno")),
                "F14: предупреждение о точках без Typno");
            Check(stF14.Packets[0].Bindings.All(b => b.Typno == "ALG-1"),
                "F14: MbBinding несёт Typno");
            Check(stF14.Packets[0].AddressInput == "4000",
                $"F14: AddressInput пакета addr=0 FC03 → «4000» (факт {stF14.Packets[0].AddressInput})");

            // (4) имя блока: RTU-паттерн по протоколу мастера, fallback
            var sRtu = new ScadaMbGenSettings
            {
                BlockNamePattern = "MB_TCP_{Seq}",
                BlockNamePatternRtu = "MB_RTU_{Station}_{Seq}"
            };
            Check(ScadaTemplateXmlService.SelectBlockNamePattern(sRtu, "MB_RTU_MASTER_1_1")
                  == "MB_RTU_{Station}_{Seq}",
                "F14: SelectBlockNamePattern — мастер «…RTU…» → RTU-маска");
            Check(ScadaTemplateXmlService.SelectBlockNamePattern(sRtu, "MB_TCP_MASTER_4_1")
                  == "MB_TCP_{Seq}",
                "F14: SelectBlockNamePattern — TCP-мастер → TCP-маска");
            Check(ScadaTemplateXmlService.SelectBlockNamePattern(
                      new ScadaMbGenSettings { BlockNamePattern = "FALLBACK_{Seq}" }, "MB_RTU_MASTER_1_1")
                  == "FALLBACK_{Seq}",
                "F14: SelectBlockNamePattern — пустая RTU-маска → fallback на BlockNamePattern");
            var stRtu = svc.BuildPlan(pts, sRtu, new List<MbSlaveIdRule>(), "MB_RTU_MASTER_1_1");
            Check(stRtu.Packets.Count > 0 && stRtu.Packets.All(p => p.MasterName.StartsWith("MB_RTU_IMCS_EBSM102_")),
                $"F14: имена RTU-пакетов по RTU-маске (факт {string.Join(",", stRtu.Packets.Select(p => p.MasterName))})");

            // (5) формат Address «регистр + адрес»
            Check(ScadaTemplateXmlService.FormatBlockAddress(1, 3, true) == "4001" &&
                  ScadaTemplateXmlService.FormatBlockAddress(506, 3, true) == "4506" &&
                  ScadaTemplateXmlService.FormatBlockAddress(1506, 3, true) == "41506" &&
                  ScadaTemplateXmlService.FormatBlockAddress(40506, 3, true) == "40506",
                "F14: FormatBlockAddress FC3: 1→4001, 506→4506, 1506→41506, 40506→40506 (5 знаков как есть)");
            Check(ScadaTemplateXmlService.FormatBlockAddress(1, 2, true) == "1001" &&
                  ScadaTemplateXmlService.FormatBlockAddress(1, 4, true) == "3001" &&
                  ScadaTemplateXmlService.FormatBlockAddress(1, 1, true) == "0001",
                "F14: FormatBlockAddress — префикс по регистру: FC2→1, FC4→3, FC1→0");
            Check(ScadaTemplateXmlService.FormatBlockAddress(602, 3, false) == "602",
                "F14: FormatBlockAddress off → сырой адрес");

            // (6) приоритет эталона по Typno в генерации привязок (SCADA_TYPNO_MAP)
            svc.SaveTypnoMap(new List<MbTypnoMapRule>
            {
                new() { Typno = "ALG-1", Exemplar = "REAL" }  // приоритет Typno над M_DataType
            });
            var typnoDict = svc.LoadTypnoMap()
                .ToDictionary(r => r.Typno, r => (r.Template, r.Exemplar, r.Size), StringComparer.OrdinalIgnoreCase);
            var doTplF14 = File.ReadAllText(ScadaDataOutXmlService.ResolveDefaultTemplatePath()!);
            var doSvcF14 = new ScadaDataOutXmlService();
            var warnF14 = new List<string>();
            string doGenF14 = doSvcF14.Write(stF14.Packets, doTplF14,
                new Dictionary<string, (int, string)>(), new ScadaMbGenSettings(), warnF14, typnoDict);
            // REAL-эталон: 2 источника DataOut[0]/DataOut[1] (байт 0 пакета FC02) вместо BOOL
            Check(doGenF14.Contains("Info=\"MB_TCP_MASTER_4_1.DataOut[1]\""),
                "F14: Typno ALG-1 → эталон REAL (2 байта: DataOut[0] и DataOut[1])");
            Check(doGenF14.Contains("Info=\"_8100_TAG_0004_A_REAL\""),
                "F14: несколько Tag с одним Typno → цепочки каждого тега (приёмники — Info шаблона F35)");
            Check(!warnF14.Any(w => w.Contains("не сопоставлен")),
                $"F14: сопоставленный Typno — без предупреждений (факт: {string.Join("; ", warnF14)})");
            // Typno без строки маппинга → M_DataType + предупреждение на привязку
            var warnF14b = new List<string>();
            doSvcF14.Write(stF14.Packets, doTplF14,
                new Dictionary<string, (int, string)>(), new ScadaMbGenSettings(), warnF14b,
                new Dictionary<string, (string Template, string Exemplar, string Size)>(StringComparer.OrdinalIgnoreCase));
            Check(warnF14b.Count >= 2 && warnF14b.All(w => w.Contains("не сопоставлен")),
                $"F14: Typno без маппинга — предупреждение на привязку (факт: {string.Join("; ", warnF14b)})");
            // без typnoMap (null) — историческое поведение F13 без предупреждений о Typno
            var warnF14c = new List<string>();
            doSvcF14.Write(stF14.Packets, doTplF14,
                new Dictionary<string, (int, string)>(), new ScadaMbGenSettings(), warnF14c);
            Check(!warnF14c.Any(w => w.Contains("не сопоставлен")),
                "F14: typnoMap=null — без предупреждений о Typno (историческое поведение F13)");
            svc.SaveTypnoMap(new List<MbTypnoMapRule>()); // очистить

            // ===== F15: опознавание мастера по имени объекта (не по значению IsaObjId) =====

            // (1) эталонный шаблон: MasterIsaId взят из блока/rec, генерация не изменилась
            Check(info.MasterIsaId == 15003,
                $"F15: MasterIsaId эталонного шаблона = 15003 (факт {info.MasterIsaId})");

            // (2) шаблон пользователя Work\MB_TCP_MASTER_Шаблон.xml: rec ISAOBJSINFO ID=17573 —
            // раньше отвергался («нет блока мастера IsaObjId 15003»), теперь валиден
            string userTplPath = Path.Combine(@"c:\VSCode\Work", "MB_TCP_MASTER_Шаблон.xml");
            Check(File.Exists(userTplPath), $"F15: шаблон найден: {userTplPath}");
            string userTpl = File.ReadAllText(userTplPath);
            var infoU = xmlSvc.Parse(userTpl);
            Check(infoU.Valid && infoU.MasterNameBase == "MB_TCP_MASTER_4_1",
                $"F15: шаблон с IsaObjId=17573 валиден, мастер MB_TCP_MASTER_4_1 (факт: {infoU.Error})");
            Check(infoU.MasterIsaId == 17573,
                $"F15: MasterIsaId шаблона пользователя = 17573 (факт {infoU.MasterIsaId})");
            var ptsU = new List<IoPoint>
            {
                MakeMbPoint("U-1", "1115-PM-101A_TX.MCU", ip102, "02", "0", "BOOL")
            };
            var stU = svc.BuildPlan(ptsU, new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 },
                new List<MbSlaveIdRule>(), infoU.MasterNameBase);
            Check(stU.Packets.Count == 1,
                $"F15: план по шаблону 17573 — 1 пакет (факт {stU.Packets.Count})");
            var genU = xmlSvc.Write(stU.Packets, userTpl,
                new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 });
            Check(genU.PousXml.Contains("IsaObjId=\"17573\""),
                "F15: блок мастера копируется с исходным IsaObjId=17573");
            Check(genU.BindingsXml.Contains("<rec ID=\"17573\" Info=\"MB_TCP_MASTER\""),
                "F15: ISAOBJSINFO привязок — rec 17573 шаблона (не исторический 15003)");
            Check(!genU.BindingsXml.Contains("ID=\"15003\""),
                "F15: в привязках шаблона 17573 нет исторического 15003");

            // (3) fallback по имени блока: rec ISAOBJSINFO с «чужим» ID (связка блок↔rec
            // не совпала) — мастер опознаётся по GROBJTYPE=37 + Info «MB_TCP_MASTER*»
            string altTpl = tpl.Replace("<rec ID=\"15003\" Info=\"MB_TCP_MASTER\"",
                                        "<rec ID=\"99999\" Info=\"MB_TCP_MASTER\"");
            Check(altTpl != tpl, "F15: rec ISAOBJSINFO переписан на 99999 (fallback-тест)");
            var infoAlt = xmlSvc.Parse(altTpl);
            Check(infoAlt.Valid && infoAlt.MasterNameBase == "MB_TCP_MASTER_4_1",
                $"F15: связка rec↔блок не совпала → fallback по имени блока (факт: {infoAlt.Error})");

            // (4) fallback без ISAOBJSINFO вовсе — мастер по имени блока
            var fbDoc = System.Xml.Linq.XDocument.Parse(tpl);
            fbDoc.Root?.Element("ISAOBJSINFO")?.Remove();
            var infoFb = xmlSvc.Parse(fbDoc.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
            Check(infoFb.Valid && infoFb.MasterNameBase == "MB_TCP_MASTER_4_1",
                $"F15: шаблон без ISAOBJSINFO — мастер по имени блока (факт: {infoFb.Error})");

            // ===== F16: именованные шаблоны привязок + Typno → (шаблон, эталон) =====

            // (1) DDL/таблицы
            Check(Schema.CreateScadaTypnoMapTable.Contains("[Template] TEXT(255)"),
                "F16: DDL SCADA_TYPNO_MAP содержит [Template] TEXT(255)");
            Check(Schema.CreateScadaDataOutTemplatesTable.Contains("[Name] TEXT(255)") &&
                  Schema.CreateScadaDataOutTemplatesTable.Contains("[Body] MEMO") &&
                  Schema.CreateScadaDataOutTemplatesTable.Contains("[FilePath] TEXT(255)"),
                "F16: DDL SCADA_DATAOUT_TEMPLATES — Name/Body/FilePath");
            Check(creator.TableExists(Schema.ScadaDataOutTemplatesTable),
                "F16: таблица SCADA_DATAOUT_TEMPLATES создана EnsureServiceTables");

            // (2) миграция старой SCADA_TYPNO_MAP (без колонки Template) → ALTER ADD
            using (var cnM = new System.Data.OleDb.OleDbConnection(
                       $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
            {
                cnM.Open();
                using var cmd = cnM.CreateCommand();
                cmd.CommandText = $"DROP TABLE [{Schema.ScadaTypnoMapTable}]";
                cmd.ExecuteNonQuery();
                cmd.CommandText = $"CREATE TABLE [{Schema.ScadaTypnoMapTable}] " +
                                  "(ID COUNTER PRIMARY KEY, [Typno] TEXT(255), [Exemplar] TEXT(255))";
                cmd.ExecuteNonQuery();
            }
            Check(!creator.GetColumns(Schema.ScadaTypnoMapTable)
                    .Contains("Template", StringComparer.OrdinalIgnoreCase),
                "F16: старая SCADA_TYPNO_MAP без колонки Template (до миграции)");
            creator.EnsureServiceTables();
            Check(creator.GetColumns(Schema.ScadaTypnoMapTable)
                    .Contains("Template", StringComparer.OrdinalIgnoreCase),
                "F16: миграция — EnsureServiceTables добавляет SCADA_TYPNO_MAP.Template");

            // (3) CRUD именованных шаблонов: roundtrip, дубль IgnoreCase → замена, удаление
            svc.SaveDataOutTemplate("MainA", "<A/>", @"c:\Work\a.xml");
            svc.SaveDataOutTemplate("Second", "<B/>");
            var tplList = svc.LoadDataOutTemplates();
            Check(tplList.Count == 2 && tplList.Any(t => t.Name == "MainA" && t.Body == "<A/>" &&
                      t.FilePath.EndsWith("a.xml")) && tplList.Any(t => t.Name == "Second"),
                $"F16: Save/LoadDataOutTemplates roundtrip (факт: {string.Join(", ", tplList.Select(t => t.Name))})");
            svc.SaveDataOutTemplate("maina", "<A2/>"); // дубль IgnoreCase → замена тела
            tplList = svc.LoadDataOutTemplates();
            Check(tplList.Count == 2 && tplList.First(t => t.Name.Equals("MainA",
                      StringComparison.OrdinalIgnoreCase)).Body == "<A2/>",
                "F16: дубль имени IgnoreCase — замена, а не вторая строка");
            bool delMissing = !svc.DeleteDataOutTemplate("NOSUCH");
            bool delOk = svc.DeleteDataOutTemplate("Second");
            Check(delMissing && delOk && svc.LoadDataOutTemplates().Count == 1,
                "F16: DeleteDataOutTemplate (несуществующего — false, существующего — true)");
            bool nameThrown = false;
            try { svc.SaveDataOutTemplate("   ", "<x/>"); }
            catch (InvalidOperationException) { nameThrown = true; }
            Check(nameThrown, "F16: пустое имя шаблона — InvalidOperationException");

            // (4) SCADA_TYPNO_MAP: roundtrip «Typno → (шаблон, эталон)»
            svc.SaveTypnoMap(new List<MbTypnoMapRule>
            {
                new() { Typno = "MULTI-1", Template = "MainA", Exemplar = "BOOL" },
                new() { Typno = "PLAIN-1", Exemplar = "INT" }   // без шаблона — основной
            });
            var tn16 = svc.LoadTypnoMap();
            Check(tn16.Count == 2 &&
                  tn16.Any(r => r.Typno == "MULTI-1" && r.Template == "MainA" && r.Exemplar == "BOOL") &&
                  tn16.Any(r => r.Typno == "PLAIN-1" && r.Template == "" && r.Exemplar == "INT"),
                $"F16: Typno→(шаблон, эталон) roundtrip " +
                $"(факт: {string.Join("; ", tn16.Select(r => r.Typno + "/" + r.Template + "/" + r.Exemplar))})");

            // (5) генерация привязок из РАЗНЫХ шаблонов: цепочка берётся по имени из правила
            string tplB = doTpl.Replace("T11Text=\".Xin\"", "T11Text=\".Y\"");
            Check(tplB != doTpl, "F16: шаблон «B» — приёмник BOOL с членом .Y (отличим от основного)");
            svc.SaveDataOutTemplate("B", tplB);
            svc.SaveTypnoMap(new List<MbTypnoMapRule>
            {
                new() { Typno = "F16-T", Template = "B", Exemplar = "BOOL" }
            });
            var pts16 = new List<IoPoint>
            {
                MakeMbPoint("F16-TAG", "1115-PM-101A_TX.MCU", ip102, "02", "0", "BOOL", typno: "F16-T")
            };
            var st16 = svc.BuildPlan(pts16, new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            var map16 = svc.LoadTypnoMap()
                .ToDictionary(r => r.Typno, r => (r.Template, r.Exemplar, r.Size), StringComparer.OrdinalIgnoreCase);
            var named16 = svc.LoadDataOutTemplates()
                .ToDictionary(t => t.Name, t => t.Body, StringComparer.OrdinalIgnoreCase);
            var warn16 = new List<string>();
            string gen16 = doSvc.Write(st16.Packets, doTpl,
                new Dictionary<string, (int, string)>(), new ScadaMbGenSettings(), warn16, map16, named16);
            Check(gen16.Contains("Info=\"_8100_TAG_0001_A_FAIL.Xin\"") &&
                  gen16.Contains("T11Text=\".Y\""),
                $"F16: привязка из шаблона «B» (приёмник .Y вместо .Xin, Info шаблона F35) (warnings: {string.Join("; ", warn16)})");

            // F33: шаблон из правила не сохранён → привязка пропускается (не «основной»)
            var mapMiss = new Dictionary<string, (string Template, string Exemplar, string Size)>(
                StringComparer.OrdinalIgnoreCase) { ["F16-T"] = ("MISSING", "BOOL", "") };
            var warn16b = new List<string>();
            string gen16b = doSvc.Write(st16.Packets, doTpl,
                new Dictionary<string, (int, string)>(), new ScadaMbGenSettings(), warn16b, mapMiss, named16);
            Check(warn16b.Any(w => w.Contains("MISSING") && w.Contains("пропущена")) &&
                  !gen16b.Contains("Info=\"_F16_TAG.Xin\""),
                $"F16/F33: шаблон «MISSING» из правила — привязка пропущена, без fallback " +
                $"на основной (warnings: {string.Join("; ", warn16b)})");

            // F33: эталона нет в указанном шаблоне (DICT) → пропуск (fallback «INT» отключён)
            var mapNoEx = new Dictionary<string, (string Template, string Exemplar, string Size)>(
                StringComparer.OrdinalIgnoreCase) { ["F16-T"] = ("B", "DICT", "") };
            var warn16c = new List<string>();
            string gen16c = doSvc.Write(st16.Packets, doTpl,
                new Dictionary<string, (int, string)>(), new ScadaMbGenSettings(), warn16c, mapNoEx, named16);
            Check(warn16c.Any(w => w.Contains("нет эталона «DICT»") && w.Contains("пропущена")) &&
                  !warn16c.Any(w => w.Contains("взят INT")) &&
                  !gen16c.Contains("Info=\"_F16_TAG.Xin\""),
                $"F16/F33: эталона «DICT» нет в шаблоне «B» → пропуск, без INT-fallback " +
                $"(warnings: {string.Join("; ", warn16c)})");

            // typnoMap/namedTemplates=null — историческое поведение F13/F14
            var warn16d = new List<string>();
            doSvc.Write(st16.Packets, doTpl,
                new Dictionary<string, (int, string)>(), new ScadaMbGenSettings(), warn16d);
            Check(!warn16d.Any(w => w.Contains("F16:")),
                "F16: typnoMap=null — без предупреждений F16 (историческое поведение)");
            svc.SaveTypnoMap(new List<MbTypnoMapRule>());
            svc.DeleteDataOutTemplate("B");
            svc.DeleteDataOutTemplate("MainA");

            // ===== F17: именование POU (основной файл мастер-блоков + файл привязок) =====
            // (1) SettingKeys = 21 — проверено выше (+PouNamePattern/DataOutPouNamePattern)

            // (2) roundtrip новых ключей SCADA_MB_SETTINGS
            svc.SaveSettings(new ScadaMbGenSettings
            {
                PouNamePattern = "PU_{Master}_{Date}",
                DataOutPouNamePattern = "DO_{Seq}_{Slave}"
            });
            var s17 = svc.LoadSettings();
            Check(s17.PouNamePattern == "PU_{Master}_{Date}" &&
                  s17.DataOutPouNamePattern == "DO_{Seq}_{Slave}",
                "F17: PouNamePattern/DataOutPouNamePattern roundtrip в SCADA_MB_SETTINGS");

            // (3) BuildPouName: пусто/не задан → «» (историческое имя не меняется)
            Check(ScadaTemplateXmlService.BuildPouName("", "MB_TCP_MASTER_4_1", null) == "" &&
                  ScadaTemplateXmlService.BuildPouName(null, "MB_TCP_MASTER_4_1", null) == "",
                "F17: пустой/не заданный паттерн POU → «» (историческое имя сохраняется)");
            string pn17 = ScadaTemplateXmlService.BuildPouName(
                "P_{Master}_{Date}", "MB_TCP_MASTER_4_1", null);
            Check(System.Text.RegularExpressions.Regex.IsMatch(pn17, @"^P_MB_TCP_MASTER_4_1_\d{8}$"),
                $"F17: BuildPouName {{Master}}+{{Date}} → 8 цифр (факт {pn17})");

            // (4) токены контекста пакета + санитизация
            var ctx17 = new MbPacketPlan
            {
                No = 4, SlaveId = 7, Station = "S 1", Ip1 = 10, Ip2 = 0, Ip3 = 0, Ip4 = 5,
                Port = 503, FuncCode = 3, MasterName = "MB_TCP_MASTER_4_4"
            };
            Check(ScadaTemplateXmlService.BuildPouName("M_{Seq}_{Slave}_{Port}", "X", ctx17) ==
                      "M_4_7_503",
                "F17: токены {Seq}/{Slave}/{Port} в имени POU");
            Check(ScadaTemplateXmlService.BuildPouName("M_{Station}_{Ip}", "X", ctx17) ==
                      "M_S_1_10_0_0_5",
                "F17: токены {Station}/{Ip} + санитизация пробела станции");

            // (5) основной файл: NAME POU по паттерну; пусто — историческое имя шаблона
            var gen17 = xmlSvc.Write(stRules.Packets, tpl,
                new ScadaMbGenSettings { PouNamePattern = "PU_{Master}" });
            Check(gen17.PousXml.Contains("NAME=\"PU_MB_TCP_MASTER_4_1\""),
                "F17: BufScadaPOUS — NAME POU по PouNamePattern (PU_{Master})");
            var gen17e = xmlSvc.Write(stRules.Packets, tpl, new ScadaMbGenSettings());
            string tplPouName17 = System.Xml.Linq.XDocument.Parse(tpl).Root?
                .Element("POUS")?.Element("OnePOU")?.Attribute("NAME")?.Value ?? "";
            Check(tplPouName17.Length > 0 && gen17e.PousXml.Contains($"NAME=\"{tplPouName17}\""),
                $"F17: пустой PouNamePattern — историческое NAME POU из шаблона " +
                $"({tplPouName17})");

            // (6) файл привязок: NAME POU пакета по паттерну; пусто — имя блока мастера
            string gen17d = doSvc.Write(stRules.Packets, doTpl,
                new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings { DataOutPouNamePattern = "{Master}_DATA" },
                new List<string>(), null, null, dataInDict);
            Check(gen17d.Contains("NAME=\"MB_TCP_MASTER_4_1_DATA\""),
                "F17: файл привязок — NAME POU по DataOutPouNamePattern ({Master}_DATA)");
            string gen17d0 = doSvc.Write(stRules.Packets, doTpl,
                new Dictionary<string, (int, string)>(), new ScadaMbGenSettings(),
                new List<string>(), null, null, dataInDict);
            Check(gen17d0.Contains("NAME=\"MB_TCP_MASTER_4_1\"") &&
                  gen17d0.Contains("NAME=\"MB_TCP_MASTER_4_4\""),
                "F17: пустой DataOutPouNamePattern — исторические NAME = имена блоков мастера");

            // (7) дубли имени внутри файла → суффикс _2; токен {Seq} в именах POU
            string gen17u = doSvc.Write(stRules.Packets, doTpl,
                new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings { DataOutPouNamePattern = "DOUT" },
                new List<string>(), null, null, dataInDict);
            Check(stRules.Packets.Count == 4 && gen17u.Contains("NAME=\"DOUT\"") &&
                  gen17u.Contains("NAME=\"DOUT_2\"") && gen17u.Contains("NAME=\"DOUT_3\"") &&
                  gen17u.Contains("NAME=\"DOUT_4\""),
                $"F17: константный паттерн — дубль имени POU → суффиксы _2/_3/_4 " +
                $"(пакетов: {stRules.Packets.Count})");
            string gen17s = doSvc.Write(stRules.Packets, doTpl,
                new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings { DataOutPouNamePattern = "DO_{Seq}" },
                new List<string>(), null, null, dataInDict);
            Check(gen17s.Contains("NAME=\"DO_1\"") && gen17s.Contains("NAME=\"DO_2\"") &&
                  gen17s.Contains("NAME=\"DO_3\"") && gen17s.Contains("NAME=\"DO_4\""),
                "F17: файл привязок — токен {Seq} в имени POU каждого пакета (4 пакета F30)");

            // ===== F18: байтовая адресация DataOut + шаблон V2 (float = 4 байта, MARKA) =====
            Check(ModbusBlockPlannerService.DataOutByteIndex(0, false) == 0 &&
                  ModbusBlockPlannerService.DataOutByteIndex(2, false) == 4 &&
                  ModbusBlockPlannerService.DataOutByteIndex(16, false) == 32 &&
                  ModbusBlockPlannerService.DataOutByteIndex(10, false) == 20 &&
                  ModbusBlockPlannerService.DataOutByteIndex(0, true) == 0 &&
                  ModbusBlockPlannerService.DataOutByteIndex(9, true) == 9 &&
                  ModbusBlockPlannerService.DataOutByteIndex(17, true) == 17,
                "F18/F33: DataOutByteIndex (регистр→*2; BOOL: индекс = адрес в пределах пакета)");

            // шаблон в стиле Data_Out_шаблоны_32-Bit Floating_V2: один эталон (Disc пуст,
            // NAME=test), 4 байтовых источника → MB_REGs_TO_REAL_CPU850 → приёмник MARKA
            string v2Tpl = """
                <BufScadaPOUS>
                  <Common VER="7.0" Project="Test"/>
                  <POUS>
                    <OnePOU ID="19365" NAME="test" Disc="" isFBD="1" GroupID="0" Enabled="1" X="100" Y="100" WIDTH="900" HEIGHT="700">
                      <ISAGraf>
                      <Blocks>
                        <Block GROBJTYPE="31" T11ID="2170128" Info="_R08_MB_MRTU_TIK_BLOCK.DataOut[32]">
                          <Graphics X="100" Y="180" WIDTH="260" HEIGHT="20"/>
                          <Params T11Text=".DataOut[32]" CI="1" CO="1" cardId="597529"><IV>172,16,1,20,502</IV></Params>
                        </Block>
                        <Block GROBJTYPE="31" T11ID="2170129" Info="_R08_MB_MRTU_TIK_BLOCK.DataOut[33]">
                          <Graphics X="100" Y="160" WIDTH="260" HEIGHT="20"/>
                          <Params T11Text=".DataOut[33]" CI="1" CO="1" cardId="597529"><IV>172,16,1,20,502</IV></Params>
                        </Block>
                        <Block GROBJTYPE="31" T11ID="2170130" Info="_R08_MB_MRTU_TIK_BLOCK.DataOut[34]">
                          <Graphics X="100" Y="140" WIDTH="260" HEIGHT="20"/>
                          <Params T11Text=".DataOut[34]" CI="1" CO="1" cardId="597529"><IV>172,16,1,20,502</IV></Params>
                        </Block>
                        <Block GROBJTYPE="36" T11ID="2170131" Info="MB_REGs_TO_REAL_CPU850">
                          <Graphics X="450" Y="150" WIDTH="200" HEIGHT="60"/>
                          <Params T11Text="" CI="4" CO="1" cardId="610000"/>
                        </Block>
                        <Block GROBJTYPE="31" T11ID="2170132" Info="_R08_MB_MRTU_TIK_BLOCK.DataOut[35]">
                          <Graphics X="100" Y="120" WIDTH="260" HEIGHT="20"/>
                          <Params T11Text=".DataOut[35]" CI="1" CO="1" cardId="597529"><IV>172,16,1,20,502</IV></Params>
                        </Block>
                        <Block GROBJTYPE="31" T11ID="2170133" Info="MARKA">
                          <Graphics X="700" Y="100" WIDTH="200" HEIGHT="20"/>
                          <Params T11Text="" CI="1" CO="1" cardId="630723"/>
                        </Block>
                      </Blocks>
                      <Links>
                        <Link><FirstPoint FP="2170128|185"/><LastPoint LP="2170131|3"/><PointList PL="(360,190)(450,165)"/></Link>
                        <Link><FirstPoint FP="2170129|185"/><LastPoint LP="2170131|4"/><PointList PL="(360,170)(450,167)"/></Link>
                        <Link><FirstPoint FP="2170130|185"/><LastPoint LP="2170131|5"/><PointList PL="(360,150)(450,169)"/></Link>
                        <Link><FirstPoint FP="2170132|185"/><LastPoint LP="2170131|6"/><PointList PL="(360,130)(450,171)"/></Link>
                        <Link><FirstPoint FP="2170131|11"/><LastPoint LP="2170133|9"/><PointList PL="(650,180)(700,110)"/></Link>
                      </Links>
                      </ISAGraf>
                    </OnePOU>
                  </POUS>
                  <ISAOBJSINFO>
                    <rec ID="17572" Info="MB_RTU_MASTER" LibName="TenixRtLib"/>
                    <rec ID="18419" Info="MB_REGs_TO_REAL_CPU850" LibName="TenixRtLib"/>
                  </ISAOBJSINFO>
                  <ISACARDSINFO/>
                </BufScadaPOUS>
                """;
            var v2Info = doSvc.Parse(v2Tpl);
            Check(v2Info.Valid && v2Info.ExemplarCount == 1 &&
                  v2Info.Exemplars.ContainsKey("test") &&
                  v2Info.Exemplars["test"].SourceCount == 4 &&
                  v2Info.Exemplars["test"].Blocks.Count == 6 &&
                  ((string?)v2Info.Exemplars["test"].TargetTpl.Attribute("Info")) == "MARKA",
                $"F18: V2-шаблон разобран — 1 эталон «test», 4 источника, приёмник MARKA ({v2Info.Summary})");

            var v2Packets = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, FuncCode = 3, Address = 100, Count = 8,
                    MasterName = "MB_RTU_MASTER_1",
                    Bindings = { new MbBinding { Tag = "1110-MII-00101", Address = 102, Offset = 2,
                        BlockName = "MB_RTU_MASTER_1", DataType = "32-Bit Floating" } } }
            };
            var v2Meta = new Dictionary<string, (int, string)>
                { ["MB_RTU_MASTER_1"] = (597529, "172,16,1,20,502") };
            var warnV2 = new List<string>();
            string genV2 = doSvc.Write(v2Packets, v2Tpl, v2Meta, new ScadaMbGenSettings(), warnV2);
            System.Xml.Linq.XDocument.Parse(genV2); // well-formed
            Check(genV2.Contains("Info=\"MB_RTU_MASTER_1.DataOut[4]\"") &&
                  genV2.Contains("Info=\"MB_RTU_MASTER_1.DataOut[7]\"") &&
                  !genV2.Contains(".DataOut[32]"),
                $"F18: V2 — регистр 2 → байты [4..7] (warnings: {string.Join("; ", warnV2)})");
            Check(warnV2.Any(w => w.Contains("F18:") && w.Contains("единственный эталон")),
                $"F18: V2 — fallback единственного эталона с предупреждением ({string.Join("; ", warnV2)})");
            Check(genV2.Contains("Info=\"MB_REGs_TO_REAL_CPU850\"") && genV2.Contains("GROBJTYPE=\"36\""),
                "F18: V2 — конвертер MB_REGs_TO_REAL (GROBJTYPE 36) клонирован в цепочку");
            Check(genV2.Contains("Info=\"MARKA\""),
                "F18: V2 — приёмник без маппинга — Info шаблона MARKA (F35, не тег)");
            Check(genV2.Contains("ID=\"18419\""),
                "F18: V2 — ISAOBJSINFO файла привязок содержит rec конвертера 18419");
            Check(genV2.Contains("cardId=\"597529\"") &&
                  genV2.Contains("Info=\"MB_RTU_MASTER_1.DataOut[4]\""),
                "F18: V2 — источник наследует cardId/IV мастера пакета");
            Check(genV2.Contains("NAME=\"MB_RTU_MASTER_1\"") &&
                  genV2.Contains("T11Text=\".DataOut[4]\""),
                "F18: V2 — NAME POU = имя мастера, T11Text источника = .DataOut[4]");
            int v2Pous = System.Xml.Linq.XDocument.Parse(genV2).Root?
                .Element("POUS")?.Elements("OnePOU").Count() ?? 0;
            Check(v2Pous == 1, $"F18: V2 — OnePOU создан (пакетов 1, POU {v2Pous})");

            // правило Typno → эталон «test» (SCADA_TYPNO_MAP) — напрямую, без fallback
            v2Packets[0].Bindings[0].Typno = "V2-T";
            var warnV2m = new List<string>();
            string genV2m = doSvc.Write(v2Packets, v2Tpl, v2Meta, new ScadaMbGenSettings(), warnV2m,
                new Dictionary<string, (string Template, string Exemplar, string Size)>(
                    StringComparer.OrdinalIgnoreCase) { ["V2-T"] = ("", "test", "") });
            Check(genV2m.Contains("Info=\"MB_RTU_MASTER_1.DataOut[7]\"") &&
                  !warnV2m.Any(w => w.Contains("единственный эталон")),
                $"F18: V2 — Typno→эталон «test» напрямую, без fallback ({string.Join("; ", warnV2m)})");

            // граница байтов: Offset=7 (байт 14) + 4 байта > 16 байт Count=8 → пропуск, POU нет
            v2Packets[0].Bindings[0].Offset = 7;
            var warnV2b = new List<string>();
            string genV2b = doSvc.Write(v2Packets, v2Tpl, v2Meta, new ScadaMbGenSettings(), warnV2b);
            Check(warnV2b.Any(w => w.Contains("выходит за данные пакета")) &&
                  !genV2b.Contains("<OnePOU"),
                $"F18: V2 — привязка за границей байтов пакета пропускается, POU не создаётся ({string.Join("; ", warnV2b)})");

            // ===== F18.1: прокрутка окна генерации (левая колонка настроек) =====
            string mbWinXaml = File.ReadAllText(Path.Combine(
                @"c:\VSCode\IO_PJT", "Views", "ScadaModbusBlocksWindow.xaml"));
            Check(mbWinXaml.Contains("PreviewMouseWheel=\"Window_PreviewMouseWheel\"") &&
                  mbWinXaml.Contains("x:Name=\"LeftScroller\""),
                "F18.1: окно — обработчик PreviewMouseWheel + имя внешнего ScrollViewer (LeftScroller)");
            string mbWinCs = File.ReadAllText(Path.Combine(
                @"c:\VSCode\IO_PJT", "Views", "ScadaModbusBlocksWindow.xaml.cs"));
            Check(mbWinCs.Contains("SystemParameters.WorkArea") &&
                  mbWinCs.Contains("ScrollToVerticalOffset"),
                "F18.1: code-behind — вписывание окна в экран + доскролл внешнего ScrollViewer");

            // ===== F19: имена блоков = превью; один юнит-мастер из XML-шаблона; карточки =====
            // Синтетический шаблон в духе пользовательского: ДВА блока мастера (артефакт
            // прошлого экспорта) + rec со старыми именами «…_4_1/…_4_2»
            string tpl19 = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><BufScadaPOUS>" +
                "<Common/>" +
                "<POUS><OnePOU ID=\"1001\" NAME=\"MB_Master\"><PARAMS WIDTH=\"600\" HEIGHT=\"600\"/>" +
                "<ISAGraf><Blocks>" +
                "<Block T11ID=\"630701\" GROBJTYPE=\"37\" Info=\"MB_TCP_MASTER_A\">" +
                "<Graphics X=\"100\" Y=\"100\"/>" +
                "<Params cardId=\"630712\" IsaObjId=\"17573\">" +
                "<IV>0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,10,0,0,1,502</IV></Params></Block>" +
                "<Block T11ID=\"630703\" GROBJTYPE=\"37\" Info=\"MB_TCP_MASTER_B\">" +
                "<Graphics X=\"100\" Y=\"400\"/>" +
                "<Params cardId=\"630713\" IsaObjId=\"17573\">" +
                "<IV>0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,10,0,0,1,502</IV></Params></Block>" +
                "</Blocks><Links/></ISAGraf></OnePOU></POUS>" +
                "<ISAOBJSINFO><rec ID=\"17573\" Info=\"MB_TCP_MASTER\" LibName=\"TenixRtLib\"/></ISAOBJSINFO>" +
                "<ISACARDSINFO><rec ID=\"630712\" Info=\"MB_TCP_MASTER_4_1\"/>" +
                "<rec ID=\"630713\" Info=\"MB_TCP_MASTER_4_2\"/></ISACARDSINFO></BufScadaPOUS>";
            var pkts19 = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, MasterName = "MB_TCP_MASTER_A_1", Ip1 = 10, Ip4 = 5,
                    Port = 502, SlaveId = 1, FuncCode = 3, Address = 0, Count = 2, TimeoutMs = 200 },
                new MbPacketPlan { No = 2, MasterName = "MB_TCP_MASTER_A_2", Ip1 = 10, Ip4 = 2,
                    Port = 502, SlaveId = 1, FuncCode = 3, Address = 0, Count = 2, TimeoutMs = 200 }
            };
            var gen19 = xmlSvc.Write(pkts19, tpl19, new ScadaMbGenSettings());
            var doc19 = System.Xml.Linq.XDocument.Parse(gen19.PousXml);
            var gt37 = doc19.Root!.Element("POUS")!.Element("OnePOU")!.Element("ISAGraf")!
                .Element("Blocks")!.Elements("Block")
                .Where(b => b.Attribute("GROBJTYPE")?.Value == "37").ToList();
            Check(gt37.Count == 2,
                $"F19: блоков мастера = числу пакетов, юнит-дублёр исключён (факт {gt37.Count})");
            string n19a = (string?)gt37[0].Attribute("Info") ?? "";
            string n19b = gt37.Count > 1 ? (string?)gt37[1].Attribute("Info") ?? "" : "";
            Check(n19a == "MB_TCP_MASTER_A_1" && n19b == "MB_TCP_MASTER_A_2",
                $"F19: имена блоков = превью, без ShiftInstanceName-дублей ({n19a}, {n19b})");
            string cards19 = doc19.Root!.Element("ISACARDSINFO")!.ToString();
            Check(cards19.Contains("Info=\"MB_TCP_MASTER_A_1\"") &&
                  !cards19.Contains("MB_TCP_MASTER_4_1"),
                "F19: rec 630712 — Info = имя блока пакета 1 (старое «MB_TCP_MASTER_4_1» заменено)");
            Check(!cards19.Contains("MB_TCP_MASTER_4_2") && !cards19.Contains("ID=\"630713\""),
                "F19: rec исключённого юнита-мастера B (630713) — сирота, не включён");
            Check(gen19.Warnings.Count == 1 &&
                  gen19.Warnings[0].Contains("MB_TCP_MASTER_B"),
                $"F19: предупреждение об исключении лишнего юнита ({string.Join("; ", gen19.Warnings)})");
            Check(gen19.PousXml.Contains("10,0,0,5,502") &&
                  gen19.PousXml.Contains("10,0,0,2,502"),
                "F19: IV обоих пакетов подставлены (IP п1=…5, п2=…2)");
            var gen19r = xmlSvc.Write(stRules.Packets, tpl, new ScadaMbGenSettings());
            Check(gen19r.Warnings.Count == 0,
                $"F19: одиночный мастер шаблона-примера — без предупреждений (факт {gen19r.Warnings.Count})");

            // ===== F33 (этапы 1–3): INPC настроек, пустой IV, IpOctet-входы, BOOL-индекс =====
            var set33 = new ScadaMbGenSettings();
            var changed33 = new List<string>();
            set33.PropertyChanged += (_, e) => changed33.Add(e.PropertyName ?? "");
            set33.TemplateFilePath = @"C:\Work\m.xml";
            set33.MaxCountBits = 246;
            set33.MaxCountBits = 246; // то же значение — события нет
            Check(changed33.Count == 2 && changed33.Contains("TemplateFilePath") &&
                  changed33.Contains("MaxCountBits"),
                $"F33: ScadaMbGenSettings INPC — событие при смене, нет при повторе ({string.Join(",", changed33)})");
            Check(set33.Clone().TemplateFilePath == @"C:\Work\m.xml" &&
                  set33.Clone().MaxCountBits == 246,
                "F33: Clone() переносит INPC-свойства");

            // Шаблон с пустым IV мастера и связью входа IpOctet_1 → константа-источник
            string tpl33 = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><BufScadaPOUS><Common/>" +
                "<POUS><OnePOU ID=\"1001\" NAME=\"MB_Master\"><PARAMS WIDTH=\"600\" HEIGHT=\"600\"/>" +
                "<ISAGraf><Blocks>" +
                "<Block T11ID=\"630701\" GROBJTYPE=\"37\" Info=\"MB_TCP_MASTER_A\">" +
                "<Graphics X=\"100\" Y=\"100\"/>" +
                "<Params cardId=\"630712\" IsaObjId=\"17573\"><IV/></Params></Block>" +
                "<Block T11ID=\"630702\" GROBJTYPE=\"34\" Info=\"X\">" +
                "<Graphics X=\"10\" Y=\"10\"/><Params T11Text=\"X\"/></Block>" +
                "</Blocks><Links>" +
                "<Link><FirstPoint FP=\"630702|0|Out|rect\"/>" +
                "<LastPoint LP=\"630701|0|IpOctet_1|rect\"/>" +
                "<PointList PL=\"(10,10)(20,20)\"/></Link>" +
                "</Links></ISAGraf></OnePOU></POUS>" +
                "<ISAOBJSINFO><rec ID=\"17573\" Info=\"MB_TCP_MASTER\" LibName=\"TenixRtLib\"/></ISAOBJSINFO>" +
                "<ISACARDSINFO><rec ID=\"630712\" Info=\"MB_TCP_MASTER_A_1\"/></ISACARDSINFO></BufScadaPOUS>";
            var pkts33 = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, MasterName = "MB_TCP_MASTER_A_1",
                    Ip1 = 192, Ip2 = 168, Ip3 = 4, Ip4 = 10,
                    Port = 502, SlaveId = 7, FuncCode = 3, Address = 0, Count = 2, TimeoutMs = 200 }
            };
            var gen33 = xmlSvc.Write(pkts33, tpl33, new ScadaMbGenSettings());
            var doc33 = System.Xml.Linq.XDocument.Parse(gen33.PousXml);
            var blocks33 = doc33.Root!.Element("POUS")!.Element("OnePOU")!.Element("ISAGraf")!
                .Element("Blocks")!.Elements("Block").ToList();
            var master33 = blocks33.First(b => b.Attribute("GROBJTYPE")?.Value == "37");
            string iv33 = master33.Element("Params")?.Element("IV")?.Value ?? "";
            // F35: пустой IV шаблона — скелет эталонов + значения плана по карте позиций
            string expIv33 =
                $"FALSE,7,200,3,{ScadaTemplateXmlService.FormatBlockAddress(0, 3, true)},(246(0)),2,(246(0))," +
                "FALSE,TRUE,3(0),FALSE,0,65533,0,6(0),192,168,4,10,502";
            Check(iv33 == expIv33,
                $"F33→F35: пустой IV шаблона — скелет + значения плана (факт «{iv33}»)");
            Check(!gen33.Warnings.Any(w => w.Contains("IV шаблона мастера пуст")),
                $"F33→F35: IV-предупреждение отменено (факт {string.Join("; ", gen33.Warnings)})");
            var const33 = blocks33.First(b => b.Attribute("GROBJTYPE")?.Value == "34");
            Check(const33.Attribute("Info")?.Value == "192",
                $"F33: вход IpOctet_1 → константа получила октет пакета (Info={const33.Attribute("Info")?.Value})");

            // Непустой IV — значения по позициям, хвост — адрес пакета
            string tpl33b = tpl33.Replace("<IV/>", "<IV>0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,10,0,0,1,502</IV>");
            var gen33b = xmlSvc.Write(pkts33, tpl33b, new ScadaMbGenSettings());
            Check(gen33b.PousXml.Contains("192,168,4,10,502") &&
                  !gen33b.Warnings.Any(w => w.Contains("IV шаблона мастера пуст")),
                "F33→F35: непустой IV шаблона — позиции заполнены планом, хвост = адрес пакета");

            // ===== F33 (этап 6): Size в SCADA_TYPNO_MAP — нормализация/размеры/авто =====
            Check(MbTypnoMapRule.NormalizeSize("32BIT") == "32bit" &&
                  MbTypnoMapRule.NormalizeSize(" bit ") == "bit" &&
                  MbTypnoMapRule.NormalizeSize("mumble") == "" &&
                  MbTypnoMapRule.NormalizeSize(null) == "",
                "F33: NormalizeSize — trim/lower, недопустимое → пусто (авто по M_DataType)");
            Check(MbTypnoMapRule.SizeBytes("32bit") == 4 && MbTypnoMapRule.SizeBytes("16bit") == 2 &&
                  MbTypnoMapRule.SizeBytes("64bit") == 8 && MbTypnoMapRule.SizeBytes("bit") == 1 &&
                  MbTypnoMapRule.SizeRegisters("32bit") == 2 && MbTypnoMapRule.SizeRegisters("16bit") == 1 &&
                  MbTypnoMapRule.SizeRegisters("8bit") == 1 && MbTypnoMapRule.SizeRegisters("64bit") == 4 &&
                  MbTypnoMapRule.SizeRegisters("bit") == 0,
                "F33: SizeBytes/SizeRegisters (32bit=4байта/2рег, 16bit=2/1, 64bit=8/4, bit=1/0)");

            // авто-размер по телу data-шаблонов Work (число источников первого эталона)
            var doSvc33 = new ScadaDataOutXmlService();
            var szReal32 = doSvc33.DetectTemplateSize(File.ReadAllText(@"c:\VSCode\Work\MB_AI_REAL32.xml"));
            var szWord16 = doSvc33.DetectTemplateSize(File.ReadAllText(@"c:\VSCode\Work\MB_AI_WORD16.xml"));
            var szDisc = doSvc33.DetectTemplateSize(File.ReadAllText(@"c:\VSCode\Work\MB_DI_DISCRETE_INPUT.xml"));
            var szCoil = doSvc33.DetectTemplateSize(File.ReadAllText(@"c:\VSCode\Work\MB_DO_COIL.xml"));
            var szAo = doSvc33.DetectTemplateSize(File.ReadAllText(@"c:\VSCode\Work\MB_AO_WORD16.xml"));
            Check(szReal32 == (4, "32bit") && szWord16 == (2, "16bit") &&
                  szDisc == (1, "bit") && szCoil == (1, "bit") && szAo == (2, "16bit"),
                $"F33: авто-размер шаблонов: REAL32→4/32bit, WORD16→2/16bit, DISCRETE→1/bit, " +
                $"COIL→1/bit, AO16→2/16bit (факт {szReal32}/{szWord16}/{szDisc}/{szCoil}/{szAo})");

            // Size=32bit приоритетен над M_DataType в ширине пакета (BOOL-точка → 2 регистра)
            var pts33w = new List<IoPoint>
            {
                MakeMbPoint("SZ-A", "1115-PM-101A_TX.MCU", "192.168.4.10", "03", "0", "BOOL", typno: "SZ-T"),
                MakeMbPoint("SZ-B", "1115-PM-101A_TX.MCU", "192.168.4.10", "03", "2", "BOOL", typno: "SZ-T")
            };
            var st33w = svc.BuildPlan(pts33w, new ScadaMbGenSettings { MaxCountRegisters = 100 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1",
                new Dictionary<string, string> { ["SZ-T"] = "32bit" });
            Check(st33w.Packets.Count == 1 && st33w.Packets[0].Count == 4,
                $"F33: Size=32bit → ширина 2 регистра (BOOL-точки 0 и 2 → один пакет Count=4; " +
                $"факт {st33w.Packets.Count}/{st33w.Packets[0].Count})");
            var st33r = svc.BuildPlan(pts33w, new ScadaMbGenSettings { MaxCountRegisters = 100 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            Check(st33r.Packets.Count > 1,
                $"F33: без Size (null) — историческое поведение по M_DataType (BOOL → 1 регистр, " +
                $"факт {st33r.Packets.Count} пакета)");

            // валидация Size ↔ эталон шаблона и тип функции (V2: эталон 4 источника)
            v2Packets[0].Bindings[0].Typno = "V2-SZ";
            var warnSz = new List<string>();
            doSvc.Write(v2Packets, v2Tpl, v2Meta, new ScadaMbGenSettings(), warnSz,
                new Dictionary<string, (string Template, string Exemplar, string Size)>(
                    StringComparer.OrdinalIgnoreCase) { ["V2-SZ"] = ("", "test", "16bit") });
            Check(warnSz.Any(w => w.Contains("Size=16bit") && w.Contains("4 источников")),
                $"F33: Size=16bit ≠ эталон 4 источника — предупреждение ({string.Join("; ", warnSz)})");
            var warnSz2 = new List<string>();
            v2Packets[0].Bindings[0].Offset = 4; // байт 8..11 ≤ 16 байт Count=8 (в границе)
            string genSz2 = doSvc.Write(v2Packets, v2Tpl, v2Meta, new ScadaMbGenSettings(), warnSz2,
                new Dictionary<string, (string Template, string Exemplar, string Size)>(
                    StringComparer.OrdinalIgnoreCase) { ["V2-SZ"] = ("", "test", "32bit") });
            Check(genSz2.Contains("MB_RTU_MASTER_1.DataOut[8]") &&
                  !warnSz2.Any(w => w.Contains("Size=")),
                $"F33: Size=32bit = эталону (4 источника) — цепочка без Size-предупреждений ({string.Join("; ", warnSz2)})");

            // Size=bit в регистровом пакете — предупреждение (FC03, а DataOut — биты)
            var warnSz3 = new List<string>();
            doSvc.Write(st33w.Packets, v2Tpl, v2Meta, new ScadaMbGenSettings(), warnSz3,
                new Dictionary<string, (string Template, string Exemplar, string Size)>(
                    StringComparer.OrdinalIgnoreCase) { ["SZ-T"] = ("", "test", "bit") });
            Check(warnSz3.Any(w => w.Contains("Size=bit") && w.Contains("регистровый")),
                $"F33: Size=bit в регистровом пакете (FC03) — предупреждение ({string.Join("; ", warnSz3)})");

            // roundtrip Size в SCADA_TYPNO_MAP (нормализация при сохранении/чтении)
            svc.SaveTypnoMap(new List<MbTypnoMapRule>
            {
                new() { Typno = "SZ-RT", Exemplar = "REAL", Template = "T1", Size = "32BIT" }
            });
            var rtRule = svc.LoadTypnoMap().FirstOrDefault(r => r.Typno == "SZ-RT");
            Check(rtRule != null && rtRule.Size == "32bit" && rtRule.Template == "T1" &&
                  rtRule.Exemplar == "REAL",
                $"F33: Save/Load SCADA_TYPNO_MAP — Size roundtrip с нормализацией (факт {rtRule?.Size})");
            svc.SaveTypnoMap(new List<MbTypnoMapRule>());

            // ===== F33 (этап 7): маппинг объектов data-шаблонов =====
            // мини-шаблон: источник .DataOut[0] → константа DCSTAG_CONST (34) → приёмник TAG_OUT
            string dvTpl = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><BufScadaPOUS><Common/><POUS>" +
                "<OnePOU ID=\"9001\" NAME=\"BOOL\" Disc=\"BOOL\"><ISAGraf><Blocks>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"910001\" Info=\"MB_TCP_MASTER_1.DataOut[0]\">" +
                "<Graphics X=\"10\" Y=\"10\"/><Params T11Text=\".DataOut[0]\" CI=\"1\" CO=\"1\"/></Block>" +
                "<Block GROBJTYPE=\"34\" T11ID=\"910002\" Info=\"DCSTAG_CONST\">" +
                "<Graphics X=\"40\" Y=\"10\"/><Params T11Text=\"DCSTAG_CONST\"/></Block>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"910003\" Info=\"TAG_OUT\">" +
                "<Graphics X=\"70\" Y=\"10\"/><Params T11Text=\"\" CI=\"1\" CO=\"1\"/></Block>" +
                "</Blocks><Links>" +
                "<Link><FirstPoint FP=\"910001|1\"/><LastPoint LP=\"910002|1\"/><PointList PL=\"(1,1)(2,2)\"/></Link>" +
                "<Link><FirstPoint FP=\"910002|1\"/><LastPoint LP=\"910003|1\"/><PointList PL=\"(2,2)(3,3)\"/></Link>" +
                "</Links></ISAGraf></OnePOU></POUS>" +
                "<ISAOBJSINFO/><ISACARDSINFO/></BufScadaPOUS>";
            var objs33 = doSvc33.ParseTemplateObjects(dvTpl);
            // F34: приёмник (TAG_OUT) включён в перечень — его Info переопределяемо маппингом
            Check(objs33.Count == 2 &&
                  objs33.Any(o => o.Name == "DCSTAG_CONST" && o.GrobjType == 34) &&
                  objs33.Any(o => o.Name == "TAG_OUT" && o.GrobjType == 31),
                $"F33: ParseTemplateObjects — константа 34 DCSTAG_CONST + приёмник TAG_OUT (F34), " +
                $"источники/функции вне таблицы (факт {objs33.Count}: {string.Join(",", objs33.Select(o => o.Name))})");

            var dvPkts = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, FuncCode = 2, Address = 0, Count = 1,
                    MasterName = "MB_TCP_MASTER_1",
                    ColumnValues = new Dictionary<string, string>
                    {
                        ["M_Marka"] = "_1115_MHS_00101A", ["M_DcsTag"] = "TAG_A", ["M_Address"] = "7"
                    },
                    Bindings = { new MbBinding { Tag = "1115-MHS-00101A", Address = 0, Offset = 0,
                        BlockName = "MB_TCP_MASTER_1", DataType = "BOOL", Typno = "DV-T" } } }
            };
            var dvNamed = new Dictionary<string, string> { ["DV-TPL"] = dvTpl };
            var dvMap = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["DV-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                {
                    ["DCSTAG_CONST"] = ("{M_DcsTag} A{M_Address}", "")
                }
            };
            var warnDv = new List<string>();
            string genDv = doSvc.Write(dvPkts, dvTpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), warnDv,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["DV-T"] = ("DV-TPL", "BOOL", "bit") },
                dvNamed, null, dvMap, "");
            Check(genDv.Contains("Info=\"TAG_A A7\"") && genDv.Contains("T11Text=\"TAG_A A7\""),
                $"F33: объект-константа получил значение строкового шаблона «TAG_A A7» " +
                $"(warnings: {string.Join("; ", warnDv)})");

            // литерал — когда колонка/шаблон не заданы; неразрешённая колонка — предупреждение
            dvMap["DV-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
            {
                ["DCSTAG_CONST"] = ("NOPE", "LIT-VAL")
            };
            var warnDv2 = new List<string>();
            string genDv2 = doSvc.Write(dvPkts, dvTpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), warnDv2,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["DV-T"] = ("DV-TPL", "BOOL", "bit") },
                dvNamed, null, dvMap, "");
            Check(genDv2.Contains("Info=\"LIT-VAL\"") &&
                  warnDv2.Any(w => w.Contains("NOPE") && w.Contains("не разрешился")),
                $"F33: объект — неразрешённая колонка «NOPE» → предупреждение + литерал " +
                $"«LIT-VAL» ({string.Join("; ", warnDv2)})");

            // dataVars пуст/имя шаблона не совпало — цепочка без подстановок (Info шаблона)
            var warnDv3 = new List<string>();
            string genDv3 = doSvc.Write(dvPkts, dvTpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), warnDv3,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["DV-T"] = ("DV-TPL", "BOOL", "bit") },
                dvNamed, null, null, "");
            Check(genDv3.Contains("Info=\"DCSTAG_CONST\""),
                "F33: dataVars=null — объект-константа без подстановок (шаблонное Info)");

            // ===== F34 (1): авто-синтез недостающих входов мастера =====
            // шаблон только со SlaveId/TimeOut (в духе Work\ModbusTCP_v0.xml) →
            // Mode/Address/Count/IpOctet_1..4/Port добавляются блоками-константами
            string tpl34 = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><BufScadaPOUS><Common/>" +
                "<POUS><OnePOU ID=\"1001\" NAME=\"MB_TCP_MASTER_1\"><PARAMS WIDTH=\"800\" HEIGHT=\"800\"/>" +
                "<ISAGraf><Blocks>" +
                "<Block T11ID=\"740101\" GROBJTYPE=\"37\" Info=\"_NAME_PACK_TCP\">" +
                "<Graphics X=\"440\" Y=\"130\" WIDTH=\"150\" HEIGHT=\"310\"/>" +
                "<Params T11Text=\"\" CI=\"7\" CO=\"6\" IsaObjId=\"17573\" cardId=\"740110\"><IV/></Params></Block>" +
                "<Block T11ID=\"740102\" GROBJTYPE=\"31\" Info=\"SLAVEID\">" +
                "<Graphics X=\"180\" Y=\"180\" WIDTH=\"190\" HEIGHT=\"20\"/>" +
                "<Params T11Text=\"\" CI=\"1\" CO=\"1\" IsaObjId=\"-13\" cardId=\"740111\"><IV>1</IV></Params></Block>" +
                "<Block T11ID=\"740103\" GROBJTYPE=\"31\" Info=\"TIME_OUT\">" +
                "<Graphics X=\"180\" Y=\"210\" WIDTH=\"190\" HEIGHT=\"20\"/>" +
                "<Params T11Text=\"\" CI=\"1\" CO=\"1\" IsaObjId=\"-15\" cardId=\"740112\"><IV>100</IV></Params></Block>" +
                "</Blocks><Links>" +
                "<Link Negative=\"false\" asPointer=\"false\" UserEdit=\"false\" Color=\"0\" GetBitNum=\"-1\" ConvertTo=\"0\">" +
                "<PointList PL=\"(370,190);(440,190);\"/>" +
                "<FirstPoint FP=\"740102|False|0|0,0,100,20\"/>" +
                "<LastPoint LP=\"740101|True|SlaveId|0,0,100,20\"/></Link>" +
                "<Link Negative=\"false\" asPointer=\"false\" UserEdit=\"false\" Color=\"0\" GetBitNum=\"-1\" ConvertTo=\"0\">" +
                "<PointList PL=\"(370,220);(440,220);\"/>" +
                "<FirstPoint FP=\"740103|False|0|0,0,100,20\"/>" +
                "<LastPoint LP=\"740101|True|TimeOut|0,0,100,20\"/></Link>" +
                "</Links></ISAGraf></OnePOU></POUS>" +
                "<ISAOBJSINFO><rec ID=\"17573\" Info=\"MB_TCP_MASTER\" LibName=\"TenixRtLib\"/></ISAOBJSINFO>" +
                "<ISACARDSINFO>" +
                "<rec ID=\"740110\" Info=\"_NAME_PACK_TCP\" IsRetain=\"-1\" Name=\"\" SSize=\"0\" KLPath=\"\"/>" +
                "<rec ID=\"740111\" Info=\"SLAVEID\" IsRetain=\"-1\" Name=\"\" SSize=\"0\" KLPath=\"\"/>" +
                "<rec ID=\"740112\" Info=\"TIME_OUT\" IsRetain=\"-1\" Name=\"\" SSize=\"0\" KLPath=\"\"/>" +
                "</ISACARDSINFO></BufScadaPOUS>";
            var pkts34 = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, MasterName = "_NAME_PACK_TCP_1",
                    Ip1 = 192, Ip2 = 168, Ip3 = 4, Ip4 = 10, Port = 502, SlaveId = 7,
                    FuncCode = 3, Address = 602, Count = 5, TimeoutMs = 150 }
            };
            var gen34 = xmlSvc.Write(pkts34, tpl34, new ScadaMbGenSettings());
            var doc34 = System.Xml.Linq.XDocument.Parse(gen34.PousXml);
            // F35: синтез блоков-констант F34 отменён — блоков 34 в результате нет
            var synth34 = doc34.Root!.Descendants("Block")
                .Where(b => b.Attribute("GROBJTYPE")?.Value == "34").ToList();
            Check(synth34.Count == 0,
                $"F35: синтез блоков-констант отменён (факт блоков 34: {synth34.Count})");
            // F35: значения плана — в позициях <IV> мастера (скелет эталонов при пустом IV)
            var master34 = doc34.Root.Descendants("Block")
                .First(b => b.Attribute("GROBJTYPE")?.Value == "37");
            string iv34 = master34.Element("Params")?.Element("IV")?.Value ?? "";
            string expIv34 =
                $"FALSE,7,150,3,{ScadaTemplateXmlService.FormatBlockAddress(602, 3, true)},(246(0)),5,(246(0))," +
                "FALSE,TRUE,3(0),FALSE,0,65533,0,6(0),192,168,4,10,502";
            Check(iv34 == expIv34,
                $"F35: IV мастера — карта позиций 2=SlaveId,3=TimeOut,4=Mode,5=Address,7=Count," +
                $"17-21=IP:порт (факт «{iv34}», ожидал «{expIv34}»)");
            Check(gen34.PousXml.Contains("<IV>7</IV>") && gen34.PousXml.Contains("<IV>150</IV>"),
                "F34: подключённые шаблоном SlaveId/TimeOut получают значения плана (7/150)");

            // ===== F34 (2): RTU-мастер — IpOctet/Port не синтезируются + предупреждение =====
            string tpl34r = tpl34.Replace("_NAME_PACK_TCP", "MB_RTU_MASTER_X")
                                  .Replace("Info=\"MB_TCP_MASTER\"", "Info=\"MB_RTU_MASTER\"");
            var pkts34r = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, MasterName = "MB_RTU_MASTER_X_1", Protocol = "RTU",
                    SlaveId = 3, FuncCode = 4, Address = 10, Count = 2, TimeoutMs = 300 }
            };
            var gen34r = xmlSvc.Write(pkts34r, tpl34r, new ScadaMbGenSettings());
            var doc34r = System.Xml.Linq.XDocument.Parse(gen34r.PousXml);
            var master34r = doc34r.Root!.Descendants("Block")
                .First(b => b.Attribute("GROBJTYPE")?.Value == "37");
            string iv34r = master34r.Element("Params")?.Element("IV")?.Value ?? "";
            // F35: RTU — IP/порт (поз. 17-21) не пишутся (хвост скелета — нули);
            // SlaveId/TimeOut/Mode/Address/Count — пишутся
            Check(iv34r.StartsWith("FALSE,3,300,4,") && iv34r.EndsWith(",0,0,0,0,0") &&
                  iv34r.Contains("(246(0)),2,(246(0)),"),
                $"F35: RTU — в IV входы плана без IP/порта (факт «{iv34r}»)");
            Check(gen34r.Warnings.Any(w => w.Contains("F35: RTU-мастер")),
                $"F35: RTU — предупреждение о пропуске IP/порта ({string.Join("; ", gen34r.Warnings)})");

            // ===== F34 (3): приёмник (DCSTAG/TAG_OUT) участвует в маппинге =====
            var dvMapT = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["DV-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                { ["TAG_OUT"] = ("{M_DcsTag}", "") }
            };
            var warnT = new List<string>();
            string genT = doSvc.Write(dvPkts, dvTpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), warnT,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["DV-T"] = ("DV-TPL", "BOOL", "bit") },
                dvNamed, null, dvMapT, "DV-TPL");
            Check(genT.Contains("Info=\"_TAG_A\""),
                $"F34→F35: приёмник — значение маппинга «TAG_A» + TagNameFor («_TAG_A») " +
                $"({string.Join("; ", warnT)})");

            // пустой тег привязки + нет маппинга → Info шаблона сохраняется + предупреждение
            var dvPktsE = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, FuncCode = 2, Address = 0, Count = 1,
                    MasterName = "MB_TCP_MASTER_1",
                    ColumnValues = new Dictionary<string, string> { ["M_Address"] = "7" },
                    Bindings = { new MbBinding { Tag = "", Address = 0, Offset = 0,
                        BlockName = "MB_TCP_MASTER_1", DataType = "BOOL", Typno = "DV-T" } } }
            };
            var warnE = new List<string>();
            string genE = doSvc.Write(dvPktsE, dvTpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), warnE,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["DV-T"] = ("DV-TPL", "BOOL", "bit") },
                dvNamed, null, null, "");
            Check(genE.Contains("Info=\"TAG_OUT\""),
                $"F34→F35: приёмник без таблицы маппинга — Info шаблона без изменений " +
                $"(скрытых подстановок нет) ({string.Join("; ", warnE)})");

            // ===== F34 (4): MB_AI_REAL32 — DCSTAG и SLAVE_BYTES_ORGER в перечне объектов =====
            var objsReal = doSvc33.ParseTemplateObjects(
                File.ReadAllText(@"c:\VSCode\Work\MB_AI_REAL32.xml"));
            Check(objsReal.Any(o => o.Name == "SLAVE_BYTES_ORGER") &&
                  objsReal.Any(o => o.Name == "DCSTAG"),
                $"F34: MB_AI_REAL32 — SLAVE_BYTES_ORGER и DCSTAG (приёмник) в объектах " +
                $"(факт: {string.Join(",", objsReal.Select(o => o.Name))})");

            // ===== F34 (5) → F35: строгий ключ — несовпавший ключ маппинга без подстановок =====
            var dvMap1k = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["OTHER-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                { ["TAG_OUT"] = ("", "FALLBACK-NAME") }
            };
            var warnK = new List<string>();
            string genK = doSvc.Write(dvPkts, dvTpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), warnK,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["DV-T"] = ("DV-TPL", "BOOL", "bit") },
                dvNamed, null, dvMap1k, "");
            Check(!genK.Contains("FALLBACK-NAME") && genK.Contains("Info=\"TAG_OUT\"") &&
                  warnK.Any(w => w.Contains("не найден")),
                $"F35: несовпавший ключ + один шаблон маппинга — подстановок НЕТ " +
                $"(fallback F34 удалён) ({string.Join("; ", warnK)})");

            // ===== F35: пер-точечный маппинг — колонки СВОЕЙ точки привязки =====
            // два приёмника в одном пакете получают M_Marka СВОЕЙ точки (579 → …01301A,
            // 580 → …01302A), а не первой точки пакета
            var dvPkts2 = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, FuncCode = 2, Address = 579, Count = 2,
                    MasterName = "MB_TCP_MASTER_1",
                    Bindings =
                    {
                        new MbBinding { Tag = "1130-MXA-01301A", Address = 579, Offset = 0,
                            BlockName = "MB_TCP_MASTER_1", DataType = "BOOL", Typno = "DV-T",
                            ColumnValues = new Dictionary<string, string>
                            { ["M_Marka"] = "1130-MXA-01301A", ["M_DcsTag"] = "TAG_A" } },
                        new MbBinding { Tag = "1130-MXA-01302A", Address = 580, Offset = 1,
                            BlockName = "MB_TCP_MASTER_1", DataType = "BOOL", Typno = "DV-T",
                            ColumnValues = new Dictionary<string, string>
                            { ["M_Marka"] = "1130-MXA-01302A", ["M_DcsTag"] = "TAG_B" } }
                    } }
            };
            var dvMapP = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["DV-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                { ["TAG_OUT"] = ("M_Marka", "") }
            };
            var warnP = new List<string>();
            string genP = doSvc.Write(dvPkts2, dvTpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), warnP,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["DV-T"] = ("DV-TPL", "BOOL", "bit") },
                dvNamed, null, dvMapP, "DV-TPL");
            Check(genP.Contains("Info=\"_1130_MXA_01301A\"") &&
                  genP.Contains("Info=\"_1130_MXA_01302A\""),
                $"F35: пер-точечный маппинг — каждый приёмник получил M_Marka СВОЕЙ точки " +
                $"({string.Join("; ", warnP)})");

            // ===== F36: канал подстановки — колонка/«{}» → ИМЯ (Info), литерал → ЗНАЧЕНИЕ (IV) =====
            // шаблон: цепочка источник .DataOut[0] → константа CST (34) → тег-объект TAG_VAR (31,
            // <IV>0</IV>) → приёмник TAG_OUT (<IV>DEF</IV>) — CST/TAG_VAR клонируются как объекты
            // маппинга (вне цепочки блоки не клонируются вовсе)
            string f36Tpl = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><BufScadaPOUS><Common/><POUS>" +
                "<OnePOU ID=\"9301\" NAME=\"BOOL\" Disc=\"BOOL\"><ISAGraf><Blocks>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"930001\" Info=\"MB_TCP_MASTER_1.DataOut[0]\">" +
                "<Graphics X=\"10\" Y=\"10\"/><Params T11Text=\".DataOut[0]\" CI=\"1\" CO=\"1\"/></Block>" +
                "<Block GROBJTYPE=\"34\" T11ID=\"930002\" Info=\"CST\">" +
                "<Graphics X=\"40\" Y=\"10\"/><Params T11Text=\"CST\"><IV>5</IV></Params></Block>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"930003\" Info=\"TAG_OUT\">" +
                "<Graphics X=\"70\" Y=\"10\"/><Params T11Text=\"\" CI=\"1\" CO=\"1\"><IV>DEF</IV></Params></Block>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"930004\" Info=\"TAG_VAR\">" +
                "<Graphics X=\"70\" Y=\"40\"/><Params T11Text=\"\" CI=\"1\" CO=\"1\"><IV>0</IV></Params></Block>" +
                "</Blocks><Links>" +
                "<Link><FirstPoint FP=\"930001|1\"/><LastPoint LP=\"930002|1\"/><PointList PL=\"(1,1)(2,2)\"/></Link>" +
                "<Link><FirstPoint FP=\"930002|1\"/><LastPoint LP=\"930004|1\"/><PointList PL=\"(2,2)(3,3)\"/></Link>" +
                "<Link><FirstPoint FP=\"930004|1\"/><LastPoint LP=\"930003|1\"/><PointList PL=\"(3,3)(4,4)\"/></Link>" +
                "</Links></ISAGraf></OnePOU></POUS>" +
                "<ISAOBJSINFO/><ISACARDSINFO/></BufScadaPOUS>";
            var f36Pkts = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, FuncCode = 2, Address = 0, Count = 1,
                    MasterName = "MB_TCP_MASTER_1",
                    ColumnValues = new Dictionary<string, string> { ["M_Marka"] = "1115-MII-00101" },
                    Bindings = { new MbBinding { Tag = "1115-MII-00101", Address = 0, Offset = 0,
                        BlockName = "MB_TCP_MASTER_1", DataType = "BOOL", Typno = "DV-T" } } }
            };
            var f36Named = new Dictionary<string, string> { ["F36-TPL"] = f36Tpl };
            var f36Typno = new Dictionary<string, (string Template, string Exemplar, string Size)>
            { ["DV-T"] = ("F36-TPL", "BOOL", "bit") };


            // F36 (1): колонка → канал ИМЕНИ: Info тега/приёмника = TagNameFor(M_Marka),
            // IV шаблонов сохраняются; константа 34 — Info/T11Text (значение — без изменений)
            var f36MapCol = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["F36-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                {
                    ["TAG_VAR"] = ("M_Marka", ""),
                    ["TAG_OUT"] = ("M_Marka", ""),
                    ["CST"] = ("M_Marka", "")
                }
            };
            var f36WarnCol = new List<string>();
            string f36GenCol = doSvc.Write(f36Pkts, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), f36WarnCol, f36Typno, f36Named, null, f36MapCol, "F36-TPL");
            var f36DocCol = System.Xml.Linq.XDocument.Parse(f36GenCol);
            var f36Renamed = f36DocCol.Root!.Descendants("Block")
                .Where(bl => bl.Attribute("GROBJTYPE")?.Value == "31" &&
                             bl.Attribute("Info")?.Value == "_1115_MII_00101").ToList();
            var f36Ivs = f36Renamed
                .Select(bl => bl.Element("Params")?.Element("IV")?.Value ?? "<нет>")
                .OrderBy(v => v).ToList();
            Check(f36Renamed.Count == 2 && f36Ivs.SequenceEqual(new[] { "0", "DEF" }),
                $"F36: колонка → ИМЯ: тег-объект и приёмник — Info=TagNameFor(M_Marka)=" +
                $"_1115_MII_00101, IV шаблонов НЕ тронуты (факт IV: «{string.Join("/", f36Ivs)}») " +
                $"({string.Join("; ", f36WarnCol)})");
            var f36Cst = f36DocCol.Root.Descendants("Block")
                .FirstOrDefault(bl => bl.Attribute("GROBJTYPE")?.Value == "34");
            Check(f36Cst?.Attribute("Info")?.Value == "1115-MII-00101" &&
                  f36Cst.Element("Params")?.Attribute("T11Text")?.Value == "1115-MII-00101",
                $"F36: константа 34 — колонка заменяет Info/T11Text значением колонки как есть " +
                $"(константа — значение, не имя тега; факт Info={f36Cst?.Attribute("Info")?.Value})");

            // F36 (2): литерал → канал ЗНАЧЕНИЯ: <IV> тега/приёмника = литерал, Info шаблонный;
            // карточка приёмника — шаблонное имя объекта
            var f36MapLit = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["F36-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                {
                    ["TAG_VAR"] = ("", "55"),
                    ["TAG_OUT"] = ("", "LIT")
                }
            };
            var f36WarnLit = new List<string>();
            string f36GenLit = doSvc.Write(f36Pkts, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), f36WarnLit, f36Typno, f36Named, null, f36MapLit, "F36-TPL");
            var f36DocLit = System.Xml.Linq.XDocument.Parse(f36GenLit);
            var f36VarLit = f36DocLit.Root!.Descendants("Block")
                .FirstOrDefault(bl => bl.Attribute("Info")?.Value == "TAG_VAR");
            var f36OutLit = f36DocLit.Root.Descendants("Block")
                .FirstOrDefault(bl => bl.Attribute("GROBJTYPE")?.Value == "31" &&
                                       bl.Attribute("Info")?.Value == "TAG_OUT");
            Check(f36VarLit?.Element("Params")?.Element("IV")?.Value == "55" &&
                  f36OutLit?.Element("Params")?.Element("IV")?.Value == "LIT" &&
                  f36OutLit.Attribute("Info")?.Value == "TAG_OUT",
                $"F36: литерал → ЗНАЧЕНИЕ: <IV> тег-объекта=55, приёмника=LIT, Info шаблонный " +
                $"({string.Join("; ", f36WarnLit)})");
            Check(f36DocLit.Root.Descendants("rec").Any(r => r.Attribute("Info")?.Value == "TAG_OUT"),
                "F36: карточка приёмника при литерале — шаблонное имя объекта (TAG_OUT)");


            // ===== F35: BuildMasterIv — скелет/усечённый IV, RTU-пропуск =====
            var ivPk = new MbPacketPlan { No = 1, Ip1 = 10, Ip2 = 0, Ip3 = 0, Ip4 = 1,
                Port = 502, SlaveId = 9, FuncCode = 4, Address = 5, Count = 8, TimeoutMs = 250 };
            string ivSkel = ScadaTemplateXmlService.BuildMasterIv("", ivPk,
                new ScadaMbGenSettings(), false);
            Check(ivSkel == "FALSE,9,250,4," + ScadaTemplateXmlService.FormatBlockAddress(5, 4, true) +
                  ",(246(0)),8,(246(0)),FALSE,TRUE,3(0),FALSE,0,65533,0,6(0),10,0,0,1,502",
                $"F35: BuildMasterIv — пустой IV → скелет + значения (факт «{ivSkel}»)");
            string ivTrunc = ScadaTemplateXmlService.BuildMasterIv("FALSE,1,2,3", ivPk,
                new ScadaMbGenSettings(), true);
            Check(ivTrunc.StartsWith("FALSE,9,250,4,") && ivTrunc.EndsWith(",0,0,0,0,0"),
                $"F35: BuildMasterIv — усечённый IV дополняется скелетом, RTU без IP/порта " +
                $"(факт «{ivTrunc}»)");

            // ===== F34 (6): UI — маппинг мастер-шаблона убран, превью редактируемо =====
            string mbXaml34 = File.ReadAllText(@"c:\VSCode\IO_PJT\Views\ScadaModbusBlocksWindow.xaml");
            Check(!mbXaml34.Contains("Переменные мастер-шаблона") &&
                  !mbXaml34.Contains("RefreshTemplateVarsCommand"),
                "F34: группa «Переменные мастер-шаблона (F23)» удалена из окна");
            Check(mbXaml34.Contains("PreviewGrid_CellEditEnding") &&
                  mbXaml34.Contains("x:Name=\"PreviewGrid\"") &&
                  mbXaml34.Contains("IsReadOnly=\"False\""),
                "F34: превью пакетов — редактируемые ячейки (CellEditEnding + IsReadOnly=False)");
            string mbCs34 = File.ReadAllText(@"c:\VSCode\IO_PJT\Views\ScadaModbusBlocksWindow.xaml.cs");
            Check(mbCs34.Contains("NotifyPacketEdited"),
                "F34: code-behind — обработчик правки ячейки превью → VM");

            // ===== F34 (7): MbPacketPlan INPC + Endpoint-парсер =====
            var pkEp = new MbPacketPlan { Ip1 = 1, Ip2 = 2, Ip3 = 3, Ip4 = 4, Port = 502 };
            var epEvents = new List<string>();
            pkEp.PropertyChanged += (_, e) => epEvents.Add(e.PropertyName ?? "");
            pkEp.Endpoint = "192.168.4.10:602";
            Check(pkEp.Ip1 == 192 && pkEp.Ip4 == 10 && pkEp.Port == 602 &&
                  pkEp.Endpoint == "192.168.4.10:602" && epEvents.Contains("Endpoint"),
                $"F34: Endpoint-сеттер парсит a.b.c.d:port (факт {pkEp.Endpoint})");
            pkEp.Endpoint = "не-адрес";
            Check(pkEp.Ip1 == 192 && pkEp.Port == 602,
                "F34: невалидный Endpoint игнорируется");
            epEvents.Clear();
            pkEp.FuncCode = 4;
            Check(epEvents.Contains("Mode") && epEvents.Contains("IsWrite") &&
                  epEvents.Contains("Direction"),
                $"F34: смена FuncCode уведомляет Mode/IsWrite/Direction ({string.Join(",", epEvents)})");

            // ===== F37 (1): MB_AI_REAL32 — литерал «2» → <IV> SLAVE_BYTES_ORGER =====
            // баг-репорт: литерал не прописывался — воспроизведение на реальном эталоне Work\
            // + диагностика «var-объект без строки маппинга» (раньше молчал — этап F37.1)
            string f37RealTpl = File.ReadAllText(@"c:\VSCode\Work\MB_AI_REAL32.xml");
            var f37VPkts = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, FuncCode = 3, Address = 100, Count = 2,
                    MasterName = "MB_TCP_MASTER_1",
                    ColumnValues = new Dictionary<string, string> { ["M_Marka"] = "1130-MXA-01301A" },
                    Bindings = { new MbBinding { Tag = "1130-MXA-01301A", Address = 100, Offset = 0,
                        BlockName = "MB_TCP_MASTER_1", DataOutExpr = "MB_TCP_MASTER_1.DataOut[0]",
                        DataType = "32-Bit Floating", Typno = "REAL32-T",
                        ColumnValues = new Dictionary<string, string> { ["M_Marka"] = "1130-MXA-01301A" } } } }
            };
            var f37VNamed = new Dictionary<string, string> { ["MB_AI_REAL32"] = f37RealTpl };
            var f37VTypno = new Dictionary<string, (string Template, string Exemplar, string Size)>
            { ["REAL32-T"] = ("MB_AI_REAL32", "MB_AI_REAL32", "32bit") };
            var f37VMap = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["MB_AI_REAL32"] = new Dictionary<string, (string IoColumn, string Literal)>
                { ["SLAVE_BYTES_ORGER"] = ("", "2") }
            };
            var f37VWarn = new List<string>();
            string f37VGen = doSvc.Write(f37VPkts, f37RealTpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), f37VWarn, f37VTypno, f37VNamed, null, f37VMap, "MB_AI_REAL32");
            var f37VDoc = System.Xml.Linq.XDocument.Parse(f37VGen);
            var f37Orger = f37VDoc.Root!.Descendants("Block")
                .FirstOrDefault(bl => bl.Attribute("Info")?.Value == "SLAVE_BYTES_ORGER");
            Check(f37Orger?.Element("Params")?.Element("IV")?.Value == "2",
                $"F37.1: MB_AI_REAL32 — литерал «2» → <IV> SLAVE_BYTES_ORGER (факт: " +
                $"{f37Orger?.Element("Params")?.Element("IV")?.Value ?? "нет IV"}) " +
                $"({string.Join("; ", f37VWarn)})");

            // F37.1: та же строка маппинга, но имя объекта НЕ совпало (ORDER ≠ ORGER) —
            // подстановки нет + предупреждение F37 «нет строки объекта» (диагностика бага)
            var f37NoRow = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["MB_AI_REAL32"] = new Dictionary<string, (string IoColumn, string Literal)>
                { ["SLAVE_BYTES_ORDER"] = ("", "2") }
            };
            var f37NW = new List<string>();
            string f37NGen = doSvc.Write(f37VPkts, f37RealTpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), f37NW, f37VTypno, f37VNamed, null, f37NoRow, "MB_AI_REAL32");
            var f37NDoc = System.Xml.Linq.XDocument.Parse(f37NGen);
            var f37NOrger = f37NDoc.Root!.Descendants("Block")
                .FirstOrDefault(bl => bl.Attribute("Info")?.Value == "SLAVE_BYTES_ORGER");
            Check(f37NW.Any(w => w.Contains("F37") && w.Contains("SLAVE_BYTES_ORGER")) &&
                  (f37NOrger?.Element("Params")?.Element("IV")?.Value ?? "") != "2",
                $"F37.1: несовпавшее имя (ORDER≠ORGER) — <IV> не подставлен + предупреждение F37 " +
                $"({string.Join("; ", f37NW)})");

            // ===== F37 (4): один data-объект на адрес — 4 DO-тега одного регистра → 1 цепочка =====
            MbBinding F37SameAddr(string tag) => new MbBinding
            {
                Tag = tag, Address = 579, Offset = 0,
                BlockName = "MB_TCP_MASTER_1",
                DataOutExpr = "MB_TCP_MASTER_1.DataOut[0]", // один адрес — один DataOutExpr
                DataType = "BOOL", Typno = "DV-T",
                ColumnValues = new Dictionary<string, string> { ["M_Marka"] = tag }
            };
            var f37DPkts = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, FuncCode = 2, Address = 579, Count = 1,
                    MasterName = "MB_TCP_MASTER_1",
                    Bindings =
                    {
                        F37SameAddr("1115-MHS-00101A"), F37SameAddr("1115-MHS-00101B"),
                        F37SameAddr("1115-MHS-00101C"), F37SameAddr("1115-MHS-00101F")
                    } }
            };
            var f37DMap = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["F36-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                { ["TAG_OUT"] = ("M_Marka", "") }
            };
            var f37DW = new List<string>();
            string f37DGen = doSvc.Write(f37DPkts, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), f37DW, f36Typno, f36Named, null, f37DMap, "F36-TPL");
            var f37DDoc = System.Xml.Linq.XDocument.Parse(f37DGen);
            var f37Targets = f37DDoc.Root!.Descendants("Block")
                .Where(bl => bl.Attribute("GROBJTYPE")?.Value == "31" &&
                             bl.Attribute("Info")?.Value?.StartsWith("_1115_MHS") == true)
                .Select(bl => bl.Attribute("Info")!.Value).ToList();
            Check(f37Targets.Count == 1 && f37Targets[0] == "_1115_MHS_00101A",
                $"F37.4: 4 тега одного адреса → ОДНА цепочка (Info первого тега); " +
                $"остальные не клонируются (факт: {string.Join(",", f37Targets)})");

            // ===== F38 (1): текстовые объекты [TEXT] примитивов основного шаблона =====
            // Идентификатор объекта — ПЕРВОЕ СЛОВО значения [TEXT] («LOOPComment, …» →
            // «LOOPComment(Text)»); при экспорте ВСЯ строка [TEXT] клона примитива заменяется
            // резолвом строки маппинга (шаблон «{Кол}» — по ColumnValues СВОЕЙ точки пакета,
            // иначе литерал)
            string f38Tpl = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><BufScadaPOUS><Common/><POUS>" +
                "<OnePOU ID=\"9801\" NAME=\"MB_Master_4\" Disc=\"\"><PARAMS WIDTH=\"600\" HEIGHT=\"400\"/>" +
                "<ISAGraf><Blocks>" +
                "<Block GROBJTYPE=\"37\" T11ID=\"980001\" Info=\"MB_TCP_MASTER_4\">" +
                "<Graphics X=\"10\" Y=\"10\"/>" +
                "<Params T11Text=\"MB_TCP_MASTER_4\" cardId=\"980101\" IsaObjId=\"777\"><IV/></Params></Block>" +
                "</Blocks><Gotos/><Links/></ISAGraf>" +
                "<GrObj>" +
                "<OnePrim GROBJTYPE=\"1\" SourceT11ID=\"980001\" X=\"10\" Y=\"200\">" +
                "<PARAMS>[TEXT]=LOOPComment, AdressComment, TypeComment, StationComment\n[FONTID]=0\n[HINT]=</PARAMS>" +
                "</OnePrim>" +
                "<OnePrim GROBJTYPE=\"1\" SourceT11ID=\"980001\" X=\"300\" Y=\"200\">" +
                "<PARAMS>[TEXT]=TagComment, Tail\n[FONTID]=0</PARAMS>" +
                "</OnePrim>" +
                "</GrObj></OnePOU></POUS>" +
                "<ISAOBJSINFO><rec ID=\"777\" Info=\"MB_TCP_MASTER\"/></ISAOBJSINFO>" +
                "<ISACARDSINFO><rec ID=\"980101\" Info=\"MB_TCP_MASTER_4\"/></ISACARDSINFO></BufScadaPOUS>";
            var svc38 = new ScadaTemplateXmlService();
            Check(svc38.Parse(f38Tpl).Valid, "F38: синтетический основной шаблон с примитивами валиден");
            var f38TextObjs = new ScadaDataOutXmlService().ParseTextObjects(f38Tpl);
            Check(f38TextObjs.SequenceEqual(new[] { "LOOPComment(Text)", "TagComment(Text)" }),
                $"F38: ParseTextObjects — идентификатор = первое слово [TEXT], объект «(Text)», " +
                $"дедуп (факт: {string.Join(",", f38TextObjs)})");
            MbBinding F38B(string tag, string master) => new MbBinding
            {
                Tag = tag, Address = 100, Offset = 0, BlockName = master,
                DataOutExpr = master + ".DataOut[0]", DataType = "INT", Typno = "AI-T",
                ColumnValues = new Dictionary<string, string>
                { ["M_Marka"] = tag, ["M_Address"] = "0130" + tag.Substring(tag.Length - 2, 1) }
            };
            var f38Pkts = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, FuncCode = 3, Address = 100, Count = 1,
                    MasterName = "MB_TCP_MASTER_4_1",
                    Bindings = { F38B("1130-MXA-01301A", "MB_TCP_MASTER_4_1") } },
                new MbPacketPlan { No = 2, FuncCode = 3, Address = 100, Count = 1,
                    MasterName = "MB_TCP_MASTER_4_2",
                    Bindings = { F38B("1130-MXA-01302A", "MB_TCP_MASTER_4_2") } }
            };
            var f38Vars = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["MAIN-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                {
                    ["LOOPComment(Text)"] = ("{M_Marka}, {M_Address}, AI, ST1", ""),
                    ["TagComment"] = ("", "Фиксированный комментарий") // fallback без «(Text)»
                }
            };
            var f38Gen = svc38.Write(f38Pkts, f38Tpl, new ScadaMbGenSettings(), f38Vars, "MAIN-TPL");
            var f38Doc = System.Xml.Linq.XDocument.Parse(f38Gen.PousXml);
            var f38Prims = f38Doc.Root!.Descendants("OnePrim").ToList();
            string F38TextOf(System.Xml.Linq.XElement pr) =>
                System.Text.RegularExpressions.Regex.Match(
                    pr.Element("PARAMS")?.Value ?? "", @"(?m)^\[TEXT\]=([^\r\n]*)")
                    .Groups[1].Value;
            Check(f38Prims.Count == 4,
                $"F38: 2 пакета × 2 примитива → 4 клона (факт: {f38Prims.Count})");
            Check(f38Prims.Any(pr => F38TextOf(pr) == "1130-MXA-01301A, 01301, AI, ST1") &&
                  f38Prims.Any(pr => F38TextOf(pr) == "1130-MXA-01302A, 01302, AI, ST1"),
                $"F38: шаблон «{{Кол}}» — ВСЯ строка [TEXT] заменена пер-пакетным рендером " +
                $"(факт: {string.Join(" | ", f38Prims.Select(F38TextOf))})");
            Check(f38Prims.Count(pr => F38TextOf(pr) == "Фиксированный комментарий") == 2,
                $"F38: литерал (объект без «(Text)» — fallback FindTextRow) одинаков для пакетов " +
                $"({string.Join(" | ", f38Prims.Select(F38TextOf))})");
            Check(f38Gen.Warnings.Count == 0,
                $"F38: подстановки без предупреждений ({string.Join("; ", f38Gen.Warnings)})");

            // F38: строки маппинга нет — тексты без изменений + предупреждение раз на (пакет, объект)
            var f38VarsNo = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["MAIN-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                { ["OTHER"] = ("", "x") }
            };
            var f38GenNo = svc38.Write(f38Pkts, f38Tpl, new ScadaMbGenSettings(), f38VarsNo, "MAIN-TPL");
            var f38DocNo = System.Xml.Linq.XDocument.Parse(f38GenNo.PousXml);
            Check(f38DocNo.Root!.Descendants("OnePrim").Count(pr =>
                      F38TextOf(pr).StartsWith("LOOPComment,")) == 2 &&
                  f38GenNo.Warnings.Count(w => w.Contains("F38") && w.Contains("LOOPComment")) == 2,
                $"F38: нет строки объекта — [TEXT] не тронут + предупреждение на каждый пакет " +
                $"({string.Join("; ", f38GenNo.Warnings)})");

            // F38: dataVars не передан (null) — строгий режим F35: тексты как в шаблоне, без предупреждений
            var f38GenNull = svc38.Write(f38Pkts, f38Tpl, new ScadaMbGenSettings());
            Check(f38GenNull.PousXml.Contains("LOOPComment, AdressComment") &&
                  f38GenNull.Warnings.Count == 0,
                "F38: dataVars=null — тексты как в шаблоне, без предупреждений");

            // F38: неразрешённая колонка → фолбэк на литерал + предупреждение
            var f38VarsBad = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["MAIN-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                { ["LOOPComment(Text)"] = ("M_NetTakoyKolonki", "ЛИТ") }
            };
            var f38GenBad = svc38.Write(f38Pkts, f38Tpl, new ScadaMbGenSettings(), f38VarsBad, "MAIN-TPL");
            Check(f38GenBad.PousXml.Contains("[TEXT]=ЛИТ") &&
                  f38GenBad.Warnings.Any(w => w.Contains("F38") && w.Contains("не разрешился")),
                $"F38: неразрешённая колонка — фолбэк литерал «ЛИТ» + предупреждение " +
                $"({string.Join("; ", f38GenBad.Warnings)})");

            // ===== F38 (2): пакетов data на POU — чанки по N, пакет атомарен =====
            var f38Pk3 = new List<MbPacketPlan>();
            for (int f38i = 1; f38i <= 3; f38i++)
            {
                string f38m = "MB_TCP_MASTER_" + f38i;
                string f38t = "1130-DI-0000" + f38i + "A";
                f38Pk3.Add(new MbPacketPlan
                {
                    No = f38i, FuncCode = 2, Address = 100, Count = 1, MasterName = f38m,
                    Bindings =
                    {
                        new MbBinding
                        {
                            Tag = f38t, Address = 100, Offset = 0,
                            BlockName = f38m, DataOutExpr = f38m + ".DataOut[0]",
                            DataType = "BOOL", Typno = "DV-T",
                            ColumnValues = new Dictionary<string, string> { ["M_Marka"] = f38t }
                        }
                    }
                });
            }
            var doSvc38 = new ScadaDataOutXmlService();
            string f38G1 = doSvc38.Write(f38Pk3, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), null, f36Typno, f36Named, null, null, "");
            int f38Pou1 = System.Xml.Linq.XDocument.Parse(f38G1).Root!
                .Element("POUS")!.Elements("OnePOU").Count();
            Check(f38Pou1 == 3, $"F38.4: N=1 (по умолчанию) — 3 пакета → 3 POU (факт: {f38Pou1})");
            string f38G2 = doSvc38.Write(f38Pk3, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings { DataPacketsPerPage = 2 }, null, f36Typno, f36Named, null, null, "");
            var f38Pous2 = System.Xml.Linq.XDocument.Parse(f38G2).Root!
                .Element("POUS")!.Elements("OnePOU").ToList();
            Check(f38Pous2.Count == 2,
                $"F38.4: N=2 — 3 пакета → 2 POU (2+1), пакет не рвётся между POU (факт: {f38Pous2.Count})");
            Check(f38Pous2.Select(p => p.Attribute("NAME")?.Value ?? "")
                      .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2 &&
                  f38Pous2.All(p => (p.Element("ISAGraf")?.Element("Blocks")?.Elements("Block").Count() ?? 0) > 0),
                $"F38.4: имена POU чанков уникальны, оба POU не пусты " +
                $"(факт: {string.Join(",", f38Pous2.Select(p => p.Attribute("NAME")?.Value))})");
            Check(new ScadaMbGenSettings().DataPacketsPerPage == 1 &&
                  new ScadaMbGenSettings { DataPacketsPerPage = 5 }.Clone().DataPacketsPerPage == 5,
                "F38.4: DataPacketsPerPage по умолчанию 1 (историческое «один POU на пакет»), Clone сохраняет");
            Check(mbXaml34.Contains("DataPacketsPerPage") && mbXaml34.Contains("Пакетов data на POU"),
                "F38.4: поле «Пакетов data на POU» в окне F13 (группа «Пакетирование»)");

            // ===== F63: зазор между пакетами чанка — настраиваемый (default 10 вместо +100) =====
            Check(new ScadaMbGenSettings().DataPacketGap == 10 &&
                  new ScadaMbGenSettings { DataPacketGap = 0 }.Clone().DataPacketGap == 0 &&
                  ModbusBlockPlannerService.SettingKeys.Contains("DataPacketGap"),
                "F63: DataPacketGap default 10 (до F63 жёсткие +100), Clone сохраняет, ключ в SettingKeys");
            Check(mbXaml34.Contains("DataPacketGap") && mbXaml34.Contains("Зазор между пакетами data"),
                "F63: поле «Зазор между пакетами data (px)» в окне F13 (группа «Пакетирование»)");
            int F63PouMaxY(string xml, int pouIdx) => System.Xml.Linq.XDocument.Parse(xml).Root!
                .Element("POUS")!.Elements("OnePOU").ElementAt(pouIdx)
                .Descendants("Graphics")
                .Select(g => int.TryParse((string?)g.Attribute("Y"), out int y) ? y : 0)
                .DefaultIfEmpty(0).Max();
            string f63G0 = doSvc38.Write(f38Pk3, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings { DataPacketsPerPage = 2, DataPacketGap = 0 },
                null, f36Typno, f36Named, null, null, "");
            string f63Gd = doSvc38.Write(f38Pk3, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings { DataPacketsPerPage = 2 }, null, f36Typno, f36Named, null, null, "");
            string f63G50 = doSvc38.Write(f38Pk3, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings { DataPacketsPerPage = 2, DataPacketGap = 50 },
                null, f36Typno, f36Named, null, null, "");
            // чанк 2+1: первый POU содержит два пакета — межпакетный шаг виден по maxY POU(0)
            Check(F63PouMaxY(f63Gd, 0) == F63PouMaxY(f63G0, 0) + 10 &&
                  F63PouMaxY(f63G50, 0) == F63PouMaxY(f63G0, 0) + 50,
                $"F63: зазор пакетов чанка — maxY POU(0): gap0={F63PouMaxY(f63G0, 0)}, " +
                $"default(10)={F63PouMaxY(f63Gd, 0)}, gap50={F63PouMaxY(f63G50, 0)} (раствор +10/+50)");

            // ===== F64: холст POU привязок по bbox (+100), PRINT=рабочему, ID/POUNum из шаблона, шаг цепочки =====
            Check(new ScadaMbGenSettings().DataChainStep == 0 &&
                  new ScadaMbGenSettings { DataChainStep = 300 }.Clone().DataChainStep == 300 &&
                  ModbusBlockPlannerService.SettingKeys.Contains("DataChainStep"),
                "F64: DataChainStep default 0 (естественный шаг шаблона), Clone сохраняет, ключ в SettingKeys");
            Check(mbXaml34.Contains("DataChainStep") && mbXaml34.Contains("Шаг цепочки data"),
                "F64: поле «Шаг цепочки data (px)» в окне F13 (группа «Пакетирование»)");
            // холст: HEIGHT/PRINTHEIGHT = maxY+100, WIDTH/PRINTWIDTH = maxX+100 (строгая
            // подгонка по фактическому контенту чанка, без max с шаблонным canvas)
            int F64Max(string xml, int pouIdx, bool x) => System.Xml.Linq.XDocument.Parse(xml).Root!
                .Element("POUS")!.Elements("OnePOU").ElementAt(pouIdx)
                .Descendants("Graphics")
                .Select(g => (int.TryParse((string?)g.Attribute(x ? "X" : "Y"), out int a) ? a : 0) +
                             (int.TryParse((string?)g.Attribute(x ? "WIDTH" : "HEIGHT"), out int b) ? b : 0))
                .DefaultIfEmpty(0).Max();
            string F64Param(string xml, int pouIdx, string attr) =>
                (string?)System.Xml.Linq.XDocument.Parse(xml).Root!
                    .Element("POUS")!.Elements("OnePOU").ElementAt(pouIdx)
                    .Element("PARAMS")?.Attribute(attr) ?? "";
            string f64H = F64Param(f63Gd, 0, "HEIGHT");
            Check(f64H == (F64Max(f63Gd, 0, false) + 100).ToString() &&
                  F64Param(f63Gd, 0, "WIDTH") == (F64Max(f63Gd, 0, true) + 100).ToString() &&
                  F64Param(f63Gd, 0, "PRINTWIDTH") == F64Param(f63Gd, 0, "WIDTH") &&
                  F64Param(f63Gd, 0, "PRINTHEIGHT") == f64H,
                $"F64: холст POU привязок = bbox+100 (HEIGHT {f64H} = maxY {F64Max(f63Gd, 0, false)}+100, " +
                $"WIDTH {F64Param(f63Gd, 0, "WIDTH")} = maxX {F64Max(f63Gd, 0, true)}+100; PRINT =рабочему)");
            // POU чанка (2 пакета) выше POU одного пакета (N=1) — холст растёт с числом экземпляров
            Check(int.Parse(f64H) > int.Parse(F64Param(f38G1, 0, "HEIGHT")),
                $"F64: POU чанка (2 пакета) выше POU одного пакета ({f64H} > {F64Param(f38G1, 0, "HEIGHT")})");
            // ID/POUNum всех экземпляров POU — verbatim из шаблона (f36Tpl: ID=9301, POUNum нет → "0")
            var f64Pous = System.Xml.Linq.XDocument.Parse(f38G2).Root!
                .Element("POUS")!.Elements("OnePOU").ToList();
            Check(f64Pous.Count == 2 && f64Pous.All(p => p.Attribute("ID")?.Value == "9301") &&
                  f64Pous.All(p => p.Attribute("POUNum")?.Value == "0"),
                $"F64: ID/POUNum всех POU привязок = шаблону (9301/0; факт: " +
                $"{string.Join(", ", f64Pous.Select(p => p.Attribute("ID")?.Value + "/" + p.Attribute("POUNum")?.Value))})");
            // шаг цепочки: 0 = естественный (== default), >0 = заданный глобально (точно)
            string f64S0 = doSvc38.Write(f38Pk3, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings { DataChainStep = 0 }, null, f36Typno, f36Named, null, null, "");
            string f64S300 = doSvc38.Write(f38Pk3, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings { DataPacketsPerPage = 2, DataChainStep = 300 },
                null, f36Typno, f36Named, null, null, "");
            string f64S500 = doSvc38.Write(f38Pk3, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings { DataPacketsPerPage = 2, DataChainStep = 500 },
                null, f36Typno, f36Named, null, null, "");
            Check(F63PouMaxY(f64S0, 0) == F63PouMaxY(f38G1, 0),
                $"F64: DataChainStep=0 — естественный шаг шаблона, как default (maxY {F63PouMaxY(f64S0, 0)})");
            Check(F63PouMaxY(f64S500, 0) == F63PouMaxY(f64S300, 0) + 200,
                $"F64: DataChainStep — шаг цепочки ровно заданный: 500 vs 300 → maxY +200 " +
                $"(факт {F63PouMaxY(f64S300, 0)} → {F63PouMaxY(f64S500, 0)})");

            // ===== F44: [TEXT] примитивов DATA-шаблонов — объекты маппинга + клон цепочки =====
            // Примитивы OnePOU data-шаблона (текстовые подписи, как [TEXT]=Comment,
            // AdressComment… в MB_AI_REAL32) — объекты «ident(Text)» ключа ЭТОГО шаблона;
            // при экспорте клонируются с каждой цепочкой (Y как у блоков, SourceT11ID
            // перенумеровывается при ссылке на блок цепочки, иначе остаётся), ВСЯ строка
            // [TEXT] заменяется резолвом строки маппинга (канал значения по ColumnValues
            // СВОЕЙ точки, без TagNameFor)

            // F44.1: ParseTextObjects обходит ВСЕ OnePOU шаблона (эталоны в разных POU)
            string f44TwoPou = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><BufScadaPOUS><Common/><POUS>" +
                "<OnePOU ID=\"9501\" NAME=\"BOOL\" Disc=\"BOOL\"><ISAGraf><Blocks>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"951001\" Info=\"M.DataOut[0]\">" +
                "<Graphics X=\"10\" Y=\"10\"/><Params T11Text=\".DataOut[0]\"/></Block>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"951002\" Info=\"OUT_A\">" +
                "<Graphics X=\"70\" Y=\"10\"/><Params T11Text=\"\"/></Block>" +
                "</Blocks><Gotos/><Links>" +
                "<Link><FirstPoint FP=\"951001|1\"/><LastPoint LP=\"951002|1\"/><PointList PL=\"(1,1)(2,2)\"/></Link>" +
                "</Links></ISAGraf>" +
                "<GrObj><OnePrim X=\"1\" Y=\"1\"><PARAMS>[TEXT]=Comment, X\n[FONTID]=0</PARAMS></OnePrim></GrObj>" +
                "</OnePOU>" +
                "<OnePOU ID=\"9502\" NAME=\"INT\" Disc=\"INT\"><ISAGraf><Blocks>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"952001\" Info=\"M.DataOut[0]\">" +
                "<Graphics X=\"10\" Y=\"10\"/><Params T11Text=\".DataOut[0]\"/></Block>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"952002\" Info=\"OUT_B\">" +
                "<Graphics X=\"70\" Y=\"10\"/><Params T11Text=\"\"/></Block>" +
                "</Blocks><Gotos/><Links>" +
                "<Link><FirstPoint FP=\"952001|1\"/><LastPoint LP=\"952002|1\"/><PointList PL=\"(1,1)(2,2)\"/></Link>" +
                "</Links></ISAGraf>" +
                "<GrObj><OnePrim X=\"1\" Y=\"1\"><PARAMS>[TEXT]=Loop, Y\n[FONTID]=0</PARAMS></OnePrim></GrObj>" +
                "</OnePOU></POUS><ISAOBJSINFO/><ISACARDSINFO/></BufScadaPOUS>";
            var f44Texts = new ScadaDataOutXmlService().ParseTextObjects(f44TwoPou);
            Check(f44Texts.SequenceEqual(new[] { "Comment(Text)", "Loop(Text)" }),
                $"F44: ParseTextObjects обходит ВСЕ OnePOU data-шаблона " +
                $"(факт: {string.Join(",", f44Texts)})");

            // шаблон F36-цепочки + GrObj: примитив «Comment» со ссылкой на блок приёмника
            // (940002 → перенумеруется) и плавающая подпись «Loop» (999999 → без изменений)
            string f44Tpl = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><BufScadaPOUS><Common/><POUS>" +
                "<OnePOU ID=\"9401\" NAME=\"BOOL\" Disc=\"BOOL\"><ISAGraf><Blocks>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"940001\" Info=\"MB_TCP_MASTER_1.DataOut[0]\">" +
                "<Graphics X=\"10\" Y=\"10\"/><Params T11Text=\".DataOut[0]\" CI=\"1\" CO=\"1\"/></Block>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"940002\" Info=\"TAG_OUT\">" +
                "<Graphics X=\"70\" Y=\"10\"/><Params T11Text=\"\" CI=\"1\" CO=\"1\"><IV>DEF</IV></Params></Block>" +
                "</Blocks><Gotos/><Links>" +
                "<Link><FirstPoint FP=\"940001|1\"/><LastPoint LP=\"940002|1\"/><PointList PL=\"(1,1)(2,2)\"/></Link>" +
                "</Links></ISAGraf>" +
                "<GrObj>" +
                "<OnePrim GROBJTYPE=\"1\" SourceT11ID=\"940002\" X=\"70\" Y=\"40\">" +
                "<PARAMS>[TEXT]=Comment, AdressComment, TypeComment, StationComment\n[FONTID]=0\n[HINT]=</PARAMS>" +
                "</OnePrim>" +
                "<OnePrim GROBJTYPE=\"1\" SourceT11ID=\"999999\" X=\"200\" Y=\"60\">" +
                "<PARAMS>[TEXT]=Loop\n[FONTID]=0</PARAMS>" +
                "</OnePrim>" +
                "</GrObj></OnePOU></POUS>" +
                "<ISAOBJSINFO/><ISACARDSINFO/></BufScadaPOUS>";
            MbBinding F44B(string tag, string master) => new MbBinding
            {
                Tag = tag, Address = 0, Offset = 0, BlockName = master,
                DataOutExpr = master + ".DataOut[0]", DataType = "BOOL", Typno = "DV-T",
                ColumnValues = new Dictionary<string, string> { ["M_Marka"] = tag }
            };
            var f44Pkts = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, FuncCode = 2, Address = 0, Count = 1,
                    MasterName = "MB_TCP_MASTER_1",
                    Bindings = { F44B("1115-MII-00101", "MB_TCP_MASTER_1") } },
                new MbPacketPlan { No = 2, FuncCode = 2, Address = 0, Count = 1,
                    MasterName = "MB_TCP_MASTER_2",
                    Bindings = { F44B("1115-MII-00102", "MB_TCP_MASTER_2") } }
            };
            var f44Named = new Dictionary<string, string> { ["F44-TPL"] = f44Tpl };
            var f44Typno = new Dictionary<string, (string Template, string Exemplar, string Size)>
            { ["DV-T"] = ("F44-TPL", "BOOL", "bit") };
            var f44Map = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["F44-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                {
                    ["Comment(Text)"] = ("M_Marka", ""),
                    ["Loop"] = ("", "Фикс") // fallback FindTextRow без суффикса «(Text)» — литерал
                }
            };
            var f44Svc = new ScadaDataOutXmlService();
            string F44Text(System.Xml.Linq.XElement pr)
            {
                var mm = System.Text.RegularExpressions.Regex.Match(
                    pr.Element("PARAMS")?.Value ?? "", @"(?m)^\[TEXT\]=([^\r\n]*)");
                return mm.Success ? mm.Groups[1].Value : "";
            }

            // F44.2: колонка/литерал — подстановка в клон примитива СВОЕЙ точки привязки
            var f44Warn = new List<string>();
            string f44Gen = f44Svc.Write(f44Pkts, f44Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), f44Warn, f44Typno, f44Named, null, f44Map, "F44-TPL");
            var f44Doc = System.Xml.Linq.XDocument.Parse(f44Gen);
            var f44Prims = f44Doc.Root!.Descendants("OnePrim").ToList();
            Check(f44Prims.Count == 4,
                $"F44: 2 пакета × 2 примитива эталона → 4 клона в GrObj (факт: {f44Prims.Count})");
            Check(f44Prims.Any(pr => F44Text(pr) == "1115-MII-00101") &&
                  f44Prims.Any(pr => F44Text(pr) == "1115-MII-00102"),
                "F44: [TEXT]=Comment,… заменён значением колонки M_Marka СВОЕЙ точки привязки " +
                "(пер-пакетно, вся строка [TEXT])");
            Check(f44Prims.Count(pr => F44Text(pr) == "Фикс") == 2,
                $"F44: литерал (fallback «Loop» без «(Text)») подставлен в оба клона " +
                $"(факт: {string.Join(" | ", f44Prims.Select(F44Text))})");
            var f44BlockIds = f44Doc.Root.Descendants("Block")
                .Select(bl => bl.Attribute("T11ID")?.Value ?? "").ToHashSet();
            Check(f44Prims.Count(pr => pr.Attribute("SourceT11ID")?.Value == "999999") == 2 &&
                  f44Prims.Any(pr => pr.Attribute("SourceT11ID")?.Value is string st &&
                                     st != "999999" && f44BlockIds.Contains(st)),
                "F44: SourceT11ID примитива-ссылки перенумерован в клон блока цепочки, " +
                "плавающая подпись (999999) — без изменений");
            var f44PrimY = f44Prims.FirstOrDefault(pr => F44Text(pr) == "1115-MII-00101");
            Check(f44PrimY?.Attribute("Y")?.Value == "240",
                $"F44: Y примитива смещён как у блоков (40+200=240; факт: {f44PrimY?.Attribute("Y")?.Value})");
            Check(f44Warn.All(w => !w.Contains("F44")),
                $"F44: подстановки без предупреждений ({string.Join("; ", f44Warn)})");

            // F44.3: строки объекта нет в маппинге — текст как в шаблоне + предупреждение
            // на каждый (пакет, объект); dataVars=null — без подстановок и предупреждений
            var f44MapMiss = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["F44-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                { ["OTHER_OBJ"] = ("", "x") }
            };
            var f44WarnMiss = new List<string>();
            string f44GenMiss = f44Svc.Write(f44Pkts, f44Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), f44WarnMiss, f44Typno, f44Named, null, f44MapMiss, "F44-TPL");
            var f44MissPrims = System.Xml.Linq.XDocument.Parse(f44GenMiss).Root!
                .Descendants("OnePrim").ToList();
            Check(f44MissPrims.Count == 4 &&
                  f44MissPrims.Count(pr => F44Text(pr) == "Comment, AdressComment, TypeComment, StationComment") == 2 &&
                  f44MissPrims.Count(pr => F44Text(pr) == "Loop") == 2 &&
                  f44WarnMiss.Count(w => w.Contains("F44")) == 4,
                "F44: нет строки объекта — текст как в шаблоне + предупреждение на каждый " +
                $"(пакет, объект); факт предупреждений: {f44WarnMiss.Count(w => w.Contains("F44"))}");
            var f44WarnNull = new List<string>();
            string f44GenNull = f44Svc.Write(f44Pkts, f44Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), f44WarnNull, f44Typno, f44Named, null, null, "");
            Check(f44WarnNull.All(w => !w.Contains("F44")) &&
                  System.Xml.Linq.XDocument.Parse(f44GenNull).Root!.Descendants("OnePrim")
                      .Count(pr => (pr.Element("PARAMS")?.Value ?? "").StartsWith("[TEXT]=Comment")) == 2,
                "F44: dataVars=null — тексты как в шаблоне, без предупреждений");

            // F44.4: шаблон без примитивов (f36Tpl) — GrObj в POU не появляется (регрессия F13)
            string f44GPlain = f44Svc.Write(f36Pkts, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), null, f36Typno, f36Named, null, null, "");
            Check(System.Xml.Linq.XDocument.Parse(f44GPlain).Root!.Descendants("GrObj").Count() == 0,
                "F44: шаблон без примитивов — GrObj в OnePOU не появляется (регрессия F13)");

            // ===== F45: производительность окна F13 (кэш схемы, пакетный маппинг, батч-коллекции) =====

            // F45.1: EnsureTables идемпотентно и не падает после повторных вызовов
            // (_ensured-флаг: второй вызов — пропуск; TableCreator — одним GetTableNames)
            var f45Planner = new ModbusBlockPlannerService(tmpDb);
            f45Planner.EnsureTables();
            f45Planner.EnsureTables(); // повторный — пропуск, результат тот же
            Check(creator.TableExists(Schema.ScadaTemplateVarsTable),
                "F45: EnsureTables повторный — таблица на месте (кэш _ensured)");
            // EnsureServiceTables с одним GetTableNames: таблицы всех поколений на месте
            Check(creator.TableExists(Schema.ScadaMbSlaveRulesTable) &&
                  creator.TableExists(Schema.ScadaDataOutTemplatesTable) &&
                  creator.TableExists(Schema.ScadaMasterTemplatesTable),
                "F45: EnsureServiceTables (batch TableExists) гарантирует все таблицы F13");

            // F45.2: LoadTemplateVarsAll — один запрос на скоуп; группировка по TemplateName,
            // легаси (пустое имя) — отдельно; мерж = семантика LoadTemplateVars (F25)
            f45Planner.SaveTemplateVars("dataout", "TPL_A", new List<MbTemplateVar>
            {
                new MbTemplateVar { Var = "BlockIn", IoColumn = "M_BusBlock" },
                new MbTemplateVar { Var = "Comment", IoColumn = "M_Marka" }
            });
            f45Planner.SaveTemplateVars("dataout", "TPL_B", new List<MbTemplateVar>
            {
                new MbTemplateVar { Var = "BlockIn", Literal = "MB_TCP_MASTER_9" }
            });
            f45Planner.SaveTemplateVars("dataout", "", new List<MbTemplateVar> // легаси
            {
                new MbTemplateVar { Var = "LegacyVar", IoColumn = "M_Loop" }
            });
            var f45All = f45Planner.LoadTemplateVarsAll("dataout", out var f45Legacy);
            Check(f45All.Count == 2 && f45All.ContainsKey("TPL_A") && f45All.ContainsKey("TPL_B"),
                $"F45: LoadTemplateVarsAll группирует по TemplateName (факт: {f45All.Count} ключей)");
            Check(f45Legacy.Count == 1 && f45Legacy[0].Var == "LegacyVar",
                "F45: легаси-строки (пустое имя) собраны отдельно");
            var f45MergedA = ModbusBlockPlannerService.MergeTemplateVars(f45All["TPL_A"], f45Legacy);
            Check(f45MergedA.Count == 3 &&
                  f45MergedA.Any(v => v.Var == "BlockIn" && v.IoColumn == "M_BusBlock") &&
                  f45MergedA.Any(v => v.Var == "LegacyVar"),
                "F45: мерж своего шаблона + легаси (строки своего ключа приоритетны)");
            // эквивалентность одиночному LoadTemplateVars(scope, name) — регрессия F25
            var f45Single = f45Planner.LoadTemplateVars("dataout", "TPL_A");
            Check(f45Single.Count == f45MergedA.Count &&
                  f45Single.Select(v => v.Var).SequenceEqual(f45MergedA.Select(v => v.Var)),
                "F45: LoadTemplateVarsAll+Merge == LoadTemplateVars(scope, name) по составу");
            f45Planner.SaveTemplateVars("dataout", "", new List<MbTemplateVar>()); // очистить легаси
            f45Planner.SaveTemplateVars("dataout", "TPL_A", new List<MbTemplateVar>());
            f45Planner.SaveTemplateVars("dataout", "TPL_B", new List<MbTemplateVar>());

            // F45.3: RangeObservableCollection.AddRange — одно событие Reset вместо N Add
            var f45Coll = new RangeObservableCollection<string>();
            int f45Reset = 0, f45Add = 0;
            void F45OnChanged(object? _, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) f45Reset++;
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) f45Add++;
            }
            f45Coll.CollectionChanged += F45OnChanged;
            f45Coll.AddRange(new[] { "a", "b", "c", "d" });
            Check(f45Coll.Count == 4 && f45Reset == 1 && f45Add == 0,
                $"F45: AddRange(4) → 1 Reset, 0 Add (факт: {f45Reset} Reset, {f45Add} Add)");
            f45Coll.Add("e"); // одиночный Add — обычное событие
            Check(f45Coll.Count == 5 && f45Add == 1 && f45Reset == 1,
                "F45: одиночный Add не подавлен");
            f45Coll.AddRange(Array.Empty<string>()); // пустой диапазон — без событий
            Check(f45Add == 1 && f45Reset == 1, "F45: AddRange(пусто) — без событий");
            f45Coll.CollectionChanged -= F45OnChanged;

            // ===== F46 (1): простые эталоны «тег → тег» (MB_DI_TO_LOOP) =====
            string f46Tpl = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><BufScadaPOUS><Common/><POUS>" +
                "<OnePOU ID=\"9601\" NAME=\"MB_DI_TO_LOOP\" Disc=\"\"><ISAGraf><Blocks>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"960001\" Info=\"DCSTAG_IN\">" +
                "<Graphics X=\"130\" Y=\"110\"/><Params T11Text=\"\" CI=\"1\" CO=\"1\"/></Block>" +
                "<Block GROBJTYPE=\"31\" T11ID=\"960002\" Info=\"DCS_LOOP_TAG\">" +
                "<Graphics X=\"540\" Y=\"110\"/><Params T11Text=\"\" CI=\"1\" CO=\"1\"/></Block>" +
                "</Blocks><Links>" +
                "<Link><FirstPoint FP=\"960001|0\"/><LastPoint LP=\"960002|0\"/>" +
                "<PointList PL=\"(340,120)(420,120)\"/></Link>" +
                "</Links></ISAGraf></OnePOU></POUS>" +
                "<ISAOBJSINFO/><ISACARDSINFO/></BufScadaPOUS>";
            var f46Info = doSvc.Parse(f46Tpl);
            var f46Ex = f46Info.Valid ? f46Info.Exemplars.Values.First() : null;
            Check(f46Info.Valid && f46Ex != null && f46Ex.IsSimple && !f46Ex.IsWrite &&
                  f46Ex.SourceCount == 1 && f46Ex.Blocks.Count == 2 &&
                  f46Ex.SourceTpl.Attribute("Info")?.Value == "DCSTAG_IN" &&
                  f46Ex.TargetTpl.Attribute("Info")?.Value == "DCS_LOOP_TAG",
                $"F46: эталон «тег → тег» валиден (IsSimple, корень DCSTAG_IN, приёмник " +
                $"DCS_LOOP_TAG) (факт: {f46Info.Summary})");

            // F46: оба блока — переменные маппинга (корень раньше исключался как SourceTpl)
            var f46Objs = doSvc.ParseTemplateObjects(f46Tpl);
            Check(f46Objs.Any(o => o.Name == "DCSTAG_IN") && f46Objs.Any(o => o.Name == "DCS_LOOP_TAG"),
                $"F46: простой эталон — DCSTAG_IN (корень) и DCS_LOOP_TAG в объектах " +
                $"(факт: {string.Join(",", f46Objs.Select(o => o.Name))})");

            // F46: «тег → тег» без связей — приёмник вне компоненты корня → невалиден
            string f46TplNoLink = f46Tpl.Replace("<Link>", "<!--Link ").Replace("</Link>", "</Link-->");
            var f46BadInfo = doSvc.Parse(f46TplNoLink);
            Check(!f46BadInfo.Valid && f46BadInfo.Error.Contains("цепочке"),
                $"F46: «тег → тег» без связей — невалиден (факт: {f46BadInfo.Error})");

            // ===== F46 (2): write-эталон MB_AO_WORD16 — оба корня в объектах маппинга =====
            var f46Ao = doSvc.ParseTemplateObjects(datainTpl);
            Check(f46Ao.Any(o => o.Name == "SLAVE_SWAP_BYTES") && f46Ao.Any(o => o.Name == "DCSTAG_WORD"),
                $"F46: MB_AO_WORD16 — SLAVE_SWAP_BYTES (корень) и DCSTAG_WORD в объектах " +
                $"(факт: {string.Join(",", f46Ao.Select(o => o.Name))})");

            // ===== F46 (3): запись — маппинг корня перебивает автотег =====
            var f46WrMeta = new Dictionary<string, (int CardId, string Iv)>
            {
                ["MB_TCP_MASTER_1"] = (777001, "m-iv")
            };
            var f46WrPkts = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, FuncCode = 6, Address = 0, Count = 1,
                    MasterName = "MB_TCP_MASTER_1",
                    ColumnValues = new Dictionary<string, string> { ["M_DcsTag"] = "TAG_W" },
                    Bindings = { new MbBinding { Tag = "C-WR", Address = 0, Offset = 0,
                        BlockName = "MB_TCP_MASTER_1", DataType = "INT", Typno = "F46-W",
                        IsWrite = true } } }
            };
            var f46WrDict = new Dictionary<string, string> { ["MB_AO_WORD16"] = datainTpl };
            var f46WrMap = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["MB_AO_WORD16"] = new Dictionary<string, (string IoColumn, string Literal)>
                {
                    ["SLAVE_SWAP_BYTES"] = ("", "TRUE"),
                    ["DCSTAG_WORD"] = ("{M_DcsTag}", "")
                }
            };
            var f46WrWarn = new List<string>();
            string f46WrGen = doSvc.Write(f46WrPkts, f46Tpl, f46WrMeta, new ScadaMbGenSettings(), f46WrWarn,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["F46-W"] = ("MB_AO_WORD16", "MB_AO_WORD16", "16bit") },
                null, f46WrDict, f46WrMap, "MB_AO_WORD16");
            Check(f46WrGen.Contains("Info=\"_TAG_W\"") && f46WrGen.Contains("Info=\"SLAVE_SWAP_BYTES\"") &&
                  f46WrGen.Contains("<IV>TRUE</IV>") && !f46WrGen.Contains("_C_WR"),
                $"F46: write-корень с маппингом — литерал «TRUE» в <IV> (Info шаблонный, автотег " +
                $"_C_WR подавлен), DCSTAG_WORD — колонка → Info=_TAG_W ({string.Join("; ", f46WrWarn)})");

            // F46: запись без строк маппинга — исторический автотег F30 (регрессия)
            var f46WrWarn2 = new List<string>();
            string f46WrGen2 = doSvc.Write(f46WrPkts, f46Tpl, f46WrMeta, new ScadaMbGenSettings(), f46WrWarn2,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["F46-W"] = ("MB_AO_WORD16", "MB_AO_WORD16", "16bit") },
                null, f46WrDict, null, "");
            Check(f46WrGen2.Contains("Info=\"_C_WR\""),
                $"F46: write-корень без маппинга — автотег _C_WR как раньше (F30) " +
                $"({string.Join("; ", f46WrWarn2)})");

            // ===== F46 (4): простой эталон чтения — подстановка ТОЛЬКО по маппингу =====
            var f46Pkts = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 1, FuncCode = 2, Address = 0, Count = 1,
                    MasterName = "MB_TCP_MASTER_1",
                    ColumnValues = new Dictionary<string, string> { ["M_DcsTag"] = "TAG_A" },
                    Bindings = { new MbBinding { Tag = "1115-DI-001", Address = 0, Offset = 0,
                        BlockName = "MB_TCP_MASTER_1", DataType = "BOOL", Typno = "F46-R" } } }
            };
            var f46Map = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["DI-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                {
                    ["DCSTAG_IN"] = ("", "FALSE"),
                    ["DCS_LOOP_TAG"] = ("{M_DcsTag}", "")
                }
            };
            var f46Warn = new List<string>();
            string f46Gen = doSvc.Write(f46Pkts, f46Tpl, f46WrMeta, new ScadaMbGenSettings(), f46Warn,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["F46-R"] = ("DI-TPL", "MB_DI_TO_LOOP", "bit") },
                new Dictionary<string, string> { ["DI-TPL"] = f46Tpl }, null, f46Map, "");
            Check(f46Gen.Contains("Info=\"_TAG_A\"") && f46Gen.Contains("<IV>FALSE</IV>") &&
                  f46Gen.Contains("Info=\"DCSTAG_IN\"") && !f46Gen.Contains(".DataOut["),
                $"F46: простой эталон — корень по маппингу (<IV>FALSE, без автоподстановки " +
                $"DataOut), приёмник колонкой «_TAG_A» ({string.Join("; ", f46Warn)})");

            // F46: простой эталон без строк маппинга — Info шаблона + предупреждение F37
            var f46Warn2 = new List<string>();
            string f46Gen2 = doSvc.Write(f46Pkts, f46Tpl, f46WrMeta, new ScadaMbGenSettings(), f46Warn2,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["F46-R"] = ("DI-TPL", "MB_DI_TO_LOOP", "bit") },
                new Dictionary<string, string> { ["DI-TPL"] = f46Tpl }, null,
                new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
                { ["DI-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>() }, "");
            Check(f46Gen2.Contains("Info=\"DCSTAG_IN\"") && f46Gen2.Contains("Info=\"DCS_LOOP_TAG\"") &&
                  f46Warn2.Any(w => w.Contains("F37") && w.Contains("DCSTAG_IN")),
                $"F46: простой эталон без строк маппинга — шаблонные Info + предупреждение F37 " +
                $"({string.Join("; ", f46Warn2)})");

            // ===== F46 (5): фильтр грида объектов по выбранному шаблону =====
            // представление (ICollectionView) — уровень UI; логика фильтра: совпадение
            // TemplateName с SelectedDataTemplate.Name (IgnoreCase) либо выделение снято
            var f46ViewRows = new[]
            {
                new MbDataTemplateVarRow { TemplateName = "MB_DI_TO_LOOP", Var = "DCSTAG_IN" },
                new MbDataTemplateVarRow { TemplateName = "MB_AO_WORD16", Var = "SLAVE_SWAP_BYTES" }
            };
            static bool F46Filter(object? sel, MbDataTemplateVarRow row) =>
                sel == null || string.Equals(row.TemplateName, (string)sel, StringComparison.OrdinalIgnoreCase);
            Check(F46Filter(null, f46ViewRows[0]) && F46Filter(null, f46ViewRows[1]),
                "F46: фильтр объектов — выделение снято, видны все шаблоны");
            Check(F46Filter("MB_DI_TO_LOOP", f46ViewRows[0]) &&
                  !F46Filter("MB_DI_TO_LOOP", f46ViewRows[1]),
                "F46: фильтр объектов — по шаблону MB_DI_TO_LOOP видна только его строка (IgnoreCase)");

            // ===== F49: «Колонка/шаблон» + «Начальное значение» — оба канала разом =====
            // F49 (1): тег-объект 31 и приёмник — Info переименовывается по колонке
            // (TagNameFor), <IV> = начальное значение; константа 34 — Info/T11Text =
            // значение колонки + <IV> литерала
            var f49Map = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["F36-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                {
                    ["TAG_VAR"] = ("M_Marka", "42"),
                    ["TAG_OUT"] = ("M_Marka", "LIT7"),
                    ["CST"] = ("M_Marka", "9")
                }
            };
            var f49Warn = new List<string>();
            string f49Gen = doSvc.Write(f36Pkts, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), f49Warn, f36Typno, f36Named, null, f49Map, "F36-TPL");
            var f49Doc = System.Xml.Linq.XDocument.Parse(f49Gen);
            var f49Tags = f49Doc.Root!.Descendants("Block")
                .Where(bl => bl.Attribute("GROBJTYPE")?.Value == "31" &&
                             bl.Attribute("Info")?.Value == "_1115_MII_00101").ToList();
            var f49Ivs = f49Tags
                .Select(bl => bl.Element("Params")?.Element("IV")?.Value ?? "<нет>")
                .OrderBy(v => v).ToList();
            Check(f49Tags.Count == 2 && f49Ivs.SequenceEqual(new[] { "42", "LIT7" }),
                $"F49: колонка + начальное значение — тег-объект и приёмник: Info=TagNameFor(M_Marka), " +
                $"<IV> литералов (факт IV: «{string.Join("/", f49Ivs)}», блоков {f49Tags.Count}) " +
                $"({string.Join("; ", f49Warn)})");
            var f49Cst = f49Doc.Root.Descendants("Block")
                .FirstOrDefault(bl => bl.Attribute("GROBJTYPE")?.Value == "34");
            Check(f49Cst?.Attribute("Info")?.Value == "1115-MII-00101" &&
                  f49Cst.Element("Params")?.Attribute("T11Text")?.Value == "1115-MII-00101" &&
                  f49Cst.Element("Params")?.Element("IV")?.Value == "9",
                $"F49: константа 34 — Info/T11Text = значение колонки, <IV> = начальное значение " +
                $"«9» (факт IV={f49Cst?.Element("Params")?.Element("IV")?.Value})");
            Check(!f49Doc.Root.Descendants("Block").Any(bl =>
                      bl.Attribute("Info")?.Value == "TAG_VAR" ||
                      bl.Attribute("Info")?.Value == "TAG_OUT" ||
                      bl.Attribute("Info")?.Value == "CST"),
                "F49: шаблонные Info объектов полностью заменены (TAG_VAR/TAG_OUT/CST отсутствуют)");

            // F49 (2): write-корень MB_AO_WORD16 — колонка + начальное значение разом:
            // Info = TagNameFor(автотега), <IV> = литерал, автотег _C_WR подавлен
            var f49WrMap = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["MB_AO_WORD16"] = new Dictionary<string, (string IoColumn, string Literal)>
                {
                    ["SLAVE_SWAP_BYTES"] = ("M_DcsTag", "TRUE"),
                    ["DCSTAG_WORD"] = ("M_DcsTag", "")
                }
            };
            var f49WrWarn = new List<string>();
            string f49WrGen = doSvc.Write(f46WrPkts, f46Tpl, f46WrMeta, new ScadaMbGenSettings(), f49WrWarn,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["F46-W"] = ("MB_AO_WORD16", "MB_AO_WORD16", "16bit") },
                null, f46WrDict, f49WrMap, "MB_AO_WORD16");
            Check(f49WrGen.Contains("Info=\"_TAG_W\"") && f49WrGen.Contains("<IV>TRUE</IV>") &&
                  !f49WrGen.Contains("Info=\"SLAVE_SWAP_BYTES\"") && !f49WrGen.Contains("_C_WR"),
                $"F49: write-корень «колонка + начальное значение» — Info переименован (автотег _C_WR " +
                $"подавлен), <IV>TRUE разом ({string.Join("; ", f49WrWarn)})");

            // F49 (3): оба поля пустые — Info/IV шаблонные без изменений (F36-регрессия)
            var f49MapEmpty = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["F36-TPL"] = new Dictionary<string, (string IoColumn, string Literal)>
                {
                    ["TAG_VAR"] = ("", ""),
                    ["TAG_OUT"] = ("", ""),
                    ["CST"] = ("", "")
                }
            };
            var f49WarnE = new List<string>();
            string f49GenE = doSvc.Write(f36Pkts, f36Tpl, new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings(), f49WarnE, f36Typno, f36Named, null, f49MapEmpty, "F36-TPL");
            var f49DocE = System.Xml.Linq.XDocument.Parse(f49GenE);
            Check(f49DocE.Root!.Descendants("Block").Any(bl => bl.Attribute("Info")?.Value == "TAG_VAR") &&
                  f49DocE.Root.Descendants("Block").Any(bl => bl.Attribute("Info")?.Value == "TAG_OUT") &&
                  f49DocE.Root.Descendants("Block").Any(bl =>
                      bl.Attribute("GROBJTYPE")?.Value == "34" &&
                      bl.Attribute("Info")?.Value == "CST" &&
                      bl.Element("Params")?.Element("IV")?.Value == "5"),
                "F49: пустые колонка и начальное значение — Info/IV шаблонные без изменений");

            // F49 (4): UI — колонка «Литерал» переименована в «Начальное значение»
            string mbXaml49 = File.ReadAllText(@"c:\VSCode\IO_PJT\Views\ScadaModbusBlocksWindow.xaml");
            Check(mbXaml49.Contains("Header=\"Начальное значение\"") &&
                  !mbXaml49.Contains("Header=\"Литерал\""),
                "F49: грид объектов data-шаблонов — колонка «Начальное значение» (бывш. «Литерал»)");

            // ===== F50: шаг кластера мастера — от локальной позиции пакета, не от глобального No =====
            // Шаблон: один POU + один блок мастера (GROBJTYPE 37, IsaObjId 15003)
            string f50Tpl = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><BufScadaPOUS>" +
                "<Common/>" +
                "<POUS><OnePOU ID=\"2001\" NAME=\"MB_Master_F50\"><PARAMS WIDTH=\"600\" HEIGHT=\"600\"/>" +
                "<ISAGraf><Blocks>" +
                "<Block T11ID=\"730100\" GROBJTYPE=\"37\" Info=\"MB_TCP_MASTER_F50_1\">" +
                "<Graphics X=\"322\" Y=\"100\"/>" +
                "<Params cardId=\"730112\" IsaObjId=\"15003\">" +
                "<IV>0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,10,0,0,1,502</IV></Params></Block>" +
                "</Blocks><Links/></ISAGraf></OnePOU></POUS>" +
                "<ISAOBJSINFO><rec ID=\"15003\" Info=\"MB_TCP_MASTER\" LibName=\"TenixRtLib\"/></ISAOBJSINFO>" +
                "<ISACARDSINFO><rec ID=\"730112\" Info=\"MB_TCP_MASTER_F50_1\"/></ISACARDSINFO></BufScadaPOUS>";
            // пакеты «второго» файла плана (глобальные No 17..19): раньше кластер уезжал
            // вниз на ряд (17-1)/cols — «дыры от старых блоков» (F50)
            MbPacketPlan F50Pkt(int no, string name, string station) => new MbPacketPlan
            {
                No = no, MasterName = name, Ip1 = 10, Ip4 = 5, Port = 502, SlaveId = 1,
                FuncCode = 3, Address = 0, Count = 2, TimeoutMs = 200, Station = station
            };
            var f50Set = new ScadaMbGenSettings { ColumnsPerRow = 2, RowStep = 560, ColumnStep = 1150 };
            var f50Pkts = new List<MbPacketPlan>
            {
                F50Pkt(17, "MB_TCP_MASTER_F50_17", "ST_A"),
                F50Pkt(18, "MB_TCP_MASTER_F50_18", "ST_A"),
                F50Pkt(19, "MB_TCP_MASTER_F50_19", "ST_A")
            };
            var f50Gen = xmlSvc.Write(f50Pkts, f50Tpl, f50Set);
            var f50Doc = System.Xml.Linq.XDocument.Parse(f50Gen.PousXml);
            var f50Masters = f50Doc.Root!.Descendants("Block")
                .Where(b => b.Element("Params")?.Attribute("IsaObjId")?.Value == "15003")
                .ToList();
            Check(f50Masters.Count == 3,
                $"F50: 3 блока мастеров (факт {f50Masters.Count})");
            string F50XY(int i) => $"{f50Masters[i].Element("Graphics")?.Attribute("X")?.Value}," +
                f50Masters[i].Element("Graphics")?.Attribute("Y")?.Value;
            Check(F50XY(0) == "322,100" && F50XY(1) == "1472,100" && F50XY(2) == "322,660",
                $"F50: кластер No=17..19 с верха POU: (322,100)/(1472,100)/(322,660) " +
                $"(факт {F50XY(0)}/{F50XY(1)}/{F50XY(2)})");
            string f50H = (string?)f50Doc.Root.Descendants("OnePOU").First()
                .Element("PARAMS")?.Attribute("HEIGHT")?.Value ?? "";
            Check(f50H == "1420",
                $"F50: HEIGHT POU = 300 + 2 ряда*560 = 1420 (факт {f50H})");
            Check(f50Masters.All(m => (int?)m.Attribute("T11ID") != 730100) &&
                  f50Masters.All(m => m.Element("Params")?.Attribute("cardId")?.Value != "730112"),
                "F50: k!=1 — шаблонные T11/cardId не переиспользуются (уникальность между файлами)");
            var f50T11s = f50Masters.Select(m => (int?)m.Attribute("T11ID")).ToList();
            Check(f50T11s.Distinct().Count() == f50T11s.Count,
                $"F50: T11ID мастеров уникальны (факт {string.Join(",", f50T11s)})");

            // F50 (2): WriteProtocolSplit — TCP + RTU (разные тела шаблонов): кластер группы 2
            // продолжает группу 1 (без наложений и пропусков), склеенный POU вмещает оба
            string f50RtuTpl = f50Tpl
                .Replace("MB_Master_F50", "MB_Master_F50R")
                .Replace("730100", "730200").Replace("730112", "730212")
                .Replace("MB_TCP_MASTER_F50_1", "MB_RTU_MASTER_F50_1");
            var f50SplitPkts = new List<MbPacketPlan>
            {
                F50Pkt(17, "MB_TCP_MASTER_F50_17", "ST_A"),
                F50Pkt(18, "MB_TCP_MASTER_F50_18", "ST_A"),
                new MbPacketPlan { No = 19, MasterName = "MB_RTU_MASTER_F50_19", Ip1 = 10, Ip4 = 5,
                    Port = 502, SlaveId = 1, FuncCode = 3, Address = 0, Count = 2, TimeoutMs = 200,
                    Station = "ST_B", Protocol = "RTU" }
            };
            var f50Split = xmlSvc.WriteProtocolSplit(f50SplitPkts, f50Tpl, f50RtuTpl, f50Set);
            var f50SDoc = System.Xml.Linq.XDocument.Parse(f50Split.PousXml);
            var f50SMasters = f50SDoc.Root!.Descendants("Block")
                .Where(b => b.Element("Params")?.Attribute("IsaObjId")?.Value == "15003")
                .ToList();
            Check(f50SMasters.Count == 3,
                $"F50: склейка TCP+RTU — 3 блока мастеров (факт {f50SMasters.Count})");
            string F50SXY(int i) => $"{f50SMasters[i].Element("Graphics")?.Attribute("X")?.Value}," +
                f50SMasters[i].Element("Graphics")?.Attribute("Y")?.Value;
            Check(F50SXY(0) == "322,100" && F50SXY(1) == "1472,100" && F50SXY(2) == "322,660",
                $"F50: RTU-кластер сразу за TCP (322,660) — без «дыр» и наложений " +
                $"(факт {F50SXY(0)}/{F50SXY(1)}/{F50SXY(2)})");
            string f50SH = (string?)f50SDoc.Root.Descendants("OnePOU").First()
                .Element("PARAMS")?.Attribute("HEIGHT")?.Value ?? "";
            Check(f50SH == "1420",
                $"F50: HEIGHT склеенного POU = max(групп) = 1420 (факт {f50SH})");
        }

        /// <summary>F51/F52/F53: несколько шаблонов на Typno (цепочки каждого шаблона + отдельные
        /// файлы), объекты основного мастер-шаблона в маппинге, галочки «Генерировать» станций.</summary>
        private static void RunF51Part(string tmpDb, TableCreator creator)
        {
            Console.WriteLine("== F51/F52/F53 Шаблоны на Typno, мастер-объекты, станции ==");
            var svc51 = new ModbusBlockPlannerService(tmpDb);
            var doSvc51 = new ScadaDataOutXmlService();
            var xmlSvc51 = new ScadaTemplateXmlService();

            // ===== F51 (1): SaveTypnoMap — несколько правил на один Typno =====
            svc51.SaveTypnoMap(new List<MbTypnoMapRule>
            {
                new() { Typno = "BOOL_DI", Template = "MB_DI_DISCRETE_INPUT", Exemplar = "BOOL" },
                new() { Typno = "BOOL_DI", Template = "MB_DI_TO_LOOP", Exemplar = "INT" },
                // дубль (Typno, Template) IgnoreCase — последний выигрывает
                new() { Typno = "BOOL_DI", Template = "MB_DI_DISCRETE_INPUT", Exemplar = "DINT" },
                new() { Typno = "  bool_di ", Template = "mb_di_to_loop", Exemplar = "REAL" },
                new() { Typno = "SINGLE", Exemplar = "REAL" }   // пустой Template — основной
            });
            var tn51 = svc51.LoadTypnoMap();
            Check(tn51.Count == 3 &&
                  tn51.Any(r => r.Typno == "BOOL_DI" && r.Template == "MB_DI_DISCRETE_INPUT" && r.Exemplar == "DINT") &&
                  tn51.Any(r => r.Typno == "BOOL_DI" && r.Template.Equals("MB_DI_TO_LOOP", StringComparison.OrdinalIgnoreCase) && r.Exemplar == "REAL") &&
                  tn51.Any(r => r.Typno == "SINGLE" && r.Template == "" && r.Exemplar == "REAL"),
                $"F51: SaveTypnoMap — 3 правила (Typno с 2 шаблонами + дубли пар) " +
                $"(факт: {string.Join("; ", tn51.Select(r => r.Typno + "→" + r.Template + "/" + r.Exemplar))})");
            svc51.SaveTypnoMap(new List<MbTypnoMapRule>());

            // ===== F51 (2): WriteMulti — цепочка КАЖДОГО правила Typno =====
            string doTpl51 = File.ReadAllText(@"c:\VSCode\Work\Data_Out_пример.xml");
            Check(doSvc51.Parse(doTpl51).Valid, "F51: шаблон привязок Work\\Data_Out_пример.xml валиден");
            string tplB51 = doTpl51.Replace("T11Text=\".Xin\"", "T11Text=\".Y\"");
            Check(tplB51 != doTpl51, "F51: шаблон «B» — приёмник BOOL с членом .Y (отличим от основного)");
            var pts51 = new List<IoPoint>
            {
                MakeMbPoint("F51-TAG", "1115-PM-101A_TX.MCU", "192.168.4.10", "02", "0", "BOOL", typno: "F51-T")
            };
            var st51 = svc51.BuildPlan(pts51, new ScadaMbGenSettings { MaxCountRegisters = 100, MaxCountBits = 100, DefaultPort = 502 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_4_1");
            Check(st51.Packets.Count > 0, $"F51: план построен (пакетов {st51.Packets.Count})");
            var map51 = new Dictionary<string, List<(string Template, string Exemplar, string Size)>>
            {
                ["F51-T"] = new List<(string Template, string Exemplar, string Size)>
                {
                    ("", "BOOL", ""),        // основной шаблон редактора
                    ("B", "BOOL", "")        // именованный шаблон «B»
                }
            };
            var named51 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["B"] = tplB51 };
            var warn51 = new List<string>();
            string gen51 = doSvc51.WriteMulti(st51.Packets, doTpl51,
                new Dictionary<string, (int, string)>(), new ScadaMbGenSettings(), warn51, map51, named51);
            Check(gen51.Contains("T11Text=\".Xin\"") && gen51.Contains("T11Text=\".Y\""),
                $"F51: два правила на Typno — цепочки ОБОИХ шаблонов (.Xin основного + .Y «B») " +
                $"(warnings: {string.Join("; ", warn51)})");
            int src51 = System.Text.RegularExpressions.Regex
                .Matches(gen51, "T11Text=\"\\.DataOut\\[").Count;
            Check(src51 == 2, $"F51: источник .DataOut[ на каждое правило (факт {src51})");

            // compat: одиночная карта (старый формат Write) — одна цепочка, поведение F14–F50
            var warn51c = new List<string>();
            string gen51c = doSvc51.Write(st51.Packets, doTpl51,
                new Dictionary<string, (int, string)>(), new ScadaMbGenSettings(), warn51c,
                new Dictionary<string, (string Template, string Exemplar, string Size)>
                { ["F51-T"] = ("B", "BOOL", "") }, named51);
            Check(gen51c.Contains("T11Text=\".Y\"") && !gen51c.Contains("T11Text=\".Xin\""),
                $"F51: compat-перегрузка Write (одиночное правило) — одна цепочка шаблона «B» " +
                $"(warnings: {string.Join("; ", warn51c)})");

            // ===== F52: объекты основного мастер-шаблона в маппинге =====
            // шаблон в духе F34 (tpl34) + тег LOOP_TAG и константа CNST — объекты юнита мастера
            string tpl52 = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><BufScadaPOUS><Common/>" +
                "<POUS><OnePOU ID=\"1001\" NAME=\"MB_TCP_MASTER_1\"><PARAMS WIDTH=\"800\" HEIGHT=\"800\"/>" +
                "<ISAGraf><Blocks>" +
                "<Block T11ID=\"752001\" GROBJTYPE=\"37\" Info=\"_NAME_PACK_TCP\">" +
                "<Graphics X=\"440\" Y=\"130\" WIDTH=\"150\" HEIGHT=\"310\"/>" +
                "<Params T11Text=\"\" CI=\"7\" CO=\"6\" IsaObjId=\"17573\" cardId=\"752010\"><IV/></Params></Block>" +
                "<Block T11ID=\"752002\" GROBJTYPE=\"31\" Info=\"SLAVEID\">" +
                "<Graphics X=\"180\" Y=\"180\" WIDTH=\"190\" HEIGHT=\"20\"/>" +
                "<Params T11Text=\"\" CI=\"1\" CO=\"1\" IsaObjId=\"-13\" cardId=\"752011\"><IV>1</IV></Params></Block>" +
                "<Block T11ID=\"752003\" GROBJTYPE=\"31\" Info=\"LOOP_TAG\">" +
                "<Graphics X=\"180\" Y=\"240\" WIDTH=\"190\" HEIGHT=\"20\"/>" +
                "<Params T11Text=\"\" CI=\"1\" CO=\"1\" IsaObjId=\"-13\" cardId=\"752013\"><IV/></Params></Block>" +
                "<Block T11ID=\"752004\" GROBJTYPE=\"34\" Info=\"CNST\">" +
                "<Graphics X=\"180\" Y=\"270\" WIDTH=\"190\" HEIGHT=\"20\"/>" +
                "<Params T11Text=\"502\" CI=\"1\" CO=\"1\" IsaObjId=\"-1\" cardId=\"752014\"><IV>502</IV></Params></Block>" +
                "</Blocks><Links>" +
                "<Link Negative=\"false\" asPointer=\"false\" UserEdit=\"false\" Color=\"0\" GetBitNum=\"-1\" ConvertTo=\"0\">" +
                "<PointList PL=\"(370,190);(440,190);\"/>" +
                "<FirstPoint FP=\"752002|False|0|0,0,100,20\"/>" +
                "<LastPoint LP=\"752001|True|SlaveId|0,0,100,20\"/></Link>" +
                // LOOP_TAG/CNST — объекты юнита мастера: входят в компоненту связями
                // к SLAVEID (как «объекты с Info» в реальных мастер-шаблонах)
                "<Link Negative=\"false\" asPointer=\"false\" UserEdit=\"false\" Color=\"0\" GetBitNum=\"-1\" ConvertTo=\"0\">" +
                "<PointList PL=\"(370,250);(390,250);\"/>" +
                "<FirstPoint FP=\"752003|False|0|0,0,100,20\"/>" +
                "<LastPoint LP=\"752002|False|0|0,0,100,20\"/></Link>" +
                "<Link Negative=\"false\" asPointer=\"false\" UserEdit=\"false\" Color=\"0\" GetBitNum=\"-1\" ConvertTo=\"0\">" +
                "<PointList PL=\"(370,280);(390,280);\"/>" +
                "<FirstPoint FP=\"752004|False|0|0,0,100,20\"/>" +
                "<LastPoint LP=\"752002|False|0|0,0,100,20\"/></Link>" +
                "</Links></ISAGraf></OnePOU></POUS>" +
                "<ISAOBJSINFO><rec ID=\"17573\" Info=\"MB_TCP_MASTER\" LibName=\"TenixRtLib\"/></ISAOBJSINFO>" +
                "<ISACARDSINFO>" +
                "<rec ID=\"752010\" Info=\"_NAME_PACK_TCP\" IsRetain=\"-1\" Name=\"\" SSize=\"0\" KLPath=\"\"/>" +
                "<rec ID=\"752011\" Info=\"SLAVEID\" IsRetain=\"-1\" Name=\"\" SSize=\"0\" KLPath=\"\"/>" +
                "<rec ID=\"752013\" Info=\"LOOP_TAG\" IsRetain=\"-1\" Name=\"\" SSize=\"0\" KLPath=\"\"/>" +
                "<rec ID=\"752014\" Info=\"CNST\" IsRetain=\"-1\" Name=\"\" SSize=\"0\" KLPath=\"\"/>" +
                "</ISACARDSINFO></BufScadaPOUS>";
            var objs52 = doSvc51.ParseTemplateObjects(tpl52);
            Check(objs52.Any(o => o.Name == "SLAVEID") && objs52.Any(o => o.Name == "LOOP_TAG") &&
                  objs52.Any(o => o.Name == "CNST") && objs52.All(o => o.Name != "_NAME_PACK_TCP"),
                $"F52: ParseTemplateObjects основного шаблона — SLAVEID/LOOP_TAG/CNST, без мастера " +
                $"(факт: {string.Join(", ", objs52.Select(o => o.Name))})");
            var pkts52 = new List<MbPacketPlan>
            {
                new MbPacketPlan { No = 2, MasterName = "_NAME_PACK_TCP_2",
                    Ip1 = 192, Ip2 = 168, Ip3 = 4, Ip4 = 10, Port = 502, SlaveId = 7,
                    FuncCode = 3, Address = 602, Count = 5, TimeoutMs = 150,
                    Bindings =
                    {
                        new MbBinding { Tag = "1130-MXA-01501A", Address = 602, Offset = 0,
                            BlockName = "_NAME_PACK_TCP_2", DataType = "BOOL", Typno = "P52-T",
                            ColumnValues = new Dictionary<string, string> { ["M_Marka"] = "1130-MXA-01501A" } }
                    } }
            };
            var dv52 = new Dictionary<string, IReadOnlyDictionary<string, (string IoColumn, string Literal)>>
            {
                ["cur52"] = new Dictionary<string, (string IoColumn, string Literal)>
                {
                    ["LOOP_TAG"] = ("M_Marka", ""),   // канал ИМЕНИ → TagNameFor(M_Marka)
                    ["CNST"] = ("", "777"),           // канал ЗНАЧЕНИЯ константы → Info/T11Text
                    ["SLAVEID"] = ("", "999")         // вход мастера — план ПЕРЕБИВАЕТ (F35)
                }
            };
            var gen52 = xmlSvc51.Write(pkts52, tpl52, new ScadaMbGenSettings(), dv52, "cur52");
            var doc52 = System.Xml.Linq.XDocument.Parse(gen52.PousXml);
            var bLoop52 = doc52.Root!.Descendants("Block")
                .FirstOrDefault(b => b.Attribute("Info")?.Value == "_1130_MXA_01501A");
            Check(bLoop52?.Attribute("GROBJTYPE")?.Value == "31",
                "F52: тег LOOP_TAG основного шаблона переименован по колонке M_Marka (TagNameFor)");
            var bCnst52 = doc52.Root.Descendants("Block").FirstOrDefault(b => b.Attribute("GROBJTYPE")?.Value == "34");
            Check(bCnst52?.Attribute("Info")?.Value == "777" &&
                  bCnst52?.Element("Params")?.Attribute("T11Text")?.Value == "777",
                $"F52: константа CNST — Info/T11Text = «Начальное значение» 777 " +
                $"(факт Info={bCnst52?.Attribute("Info")?.Value})");
            var bSlave52 = doc52.Root.Descendants("Block")
                .FirstOrDefault(b => (b.Attribute("Info")?.Value ?? "").StartsWith("SLAVEID"));
            Check(bSlave52?.Element("Params")?.Element("IV")?.Value == "7",
                $"F52: вход мастера SLAVEID — план (SlaveId=7) перебивает маппинг «999» " +
                $"(факт IV={bSlave52?.Element("Params")?.Element("IV")?.Value})");
            Check(doc52.Root.Descendants("rec").Any(r => r.Attribute("Info")?.Value == "_1130_MXA_01501A"),
                "F52: rec карточки клона LOOP_TAG назван по переименованию");
            var gen52b = xmlSvc51.Write(pkts52, tpl52, new ScadaMbGenSettings());
            Check(gen52b.PousXml.Contains("Info=\"LOOP_TAG") && gen52b.PousXml.Contains("Info=\"CNST"),
                "F52: без таблицы маппинга — Info объектов основного шаблона без изменений");

            // ===== F53: SCADA_MB_SLAVE_RULES.Generate — DDL/миграция/roundtrip =====
            Check(Schema.CreateScadaMbSlaveRulesTable.Contains("[Generate] YESNO"),
                "F53: DDL SCADA_MB_SLAVE_RULES содержит [Generate] YESNO");
            Check(creator.GetColumns(Schema.ScadaMbSlaveRulesTable)
                    .Contains("Generate", StringComparer.OrdinalIgnoreCase),
                "F53: миграция — колонка Generate в SCADA_MB_SLAVE_RULES");
            svc51.SaveSlaveRules(new List<MbSlaveIdRule>
            {
                new MbSlaveIdRule { Station = "F53-ST", SlaveId = 5, Generate = false },
                new MbSlaveIdRule { Station = "F53-ST2", SlaveId = 6, Generate = true }
            });
            var sr53 = svc51.LoadSlaveRules();
            Check(sr53.Any(r => r.Station == "F53-ST" && !r.Generate) &&
                  sr53.Any(r => r.Station == "F53-ST2" && r.Generate),
                $"F53: Generate roundtrip (false сохраняется, true по умолчанию) " +
                $"(факт: {string.Join("; ", sr53.Select(r => r.Station + "=" + r.Generate))})");

            // очистка за собой
            svc51.SaveSlaveRules(new List<MbSlaveIdRule>());
            svc51.SaveTypnoMap(new List<MbTypnoMapRule>());

            // ===== F61: зазор data-цепочек, комментарии POU, привязки/Packet (мастер-detail) =====
            // (1) настройки: дефолты / Clone / roundtrip новых ключей SCADA_MB_SETTINGS
            Check(new ScadaMbGenSettings().DataChainGap == 0 &&
                  new ScadaMbGenSettings().PouCommentPattern == "" &&
                  new ScadaMbGenSettings().DataOutPouCommentPattern == "",
                "F61: дефолты — зазор цепочек 0 (историческая плотная раскладка), комментарии POU пусты");
            Check(new ScadaMbGenSettings { DataChainGap = 99, PouCommentPattern = "C_{Master}" }
                      .Clone().DataChainGap == 99 &&
                  new ScadaMbGenSettings { DataOutPouCommentPattern = "D_{Seq}" }
                      .Clone().DataOutPouCommentPattern == "D_{Seq}",
                "F61: Clone сохраняет DataChainGap/PouCommentPattern/DataOutPouCommentPattern");
            svc51.SaveSettings(new ScadaMbGenSettings
            {
                DataChainGap = 150, PouCommentPattern = "C_{Master}",
                DataOutPouCommentPattern = "D_{Seq}"
            });
            var s61 = svc51.LoadSettings();
            Check(s61.DataChainGap == 150 && s61.PouCommentPattern == "C_{Master}" &&
                  s61.DataOutPouCommentPattern == "D_{Seq}",
                "F61: DataChainGap/PouCommentPattern/DataOutPouCommentPattern roundtrip в SCADA_MB_SETTINGS");
            svc51.SaveSettings(new ScadaMbGenSettings());

            // (2) DataChainGap: два правила Typno → две цепочки в одном POU; Gap=200
            // сдвигает вторую цепочку ровно на +200 (дефолт 0 — историческая раскладка)
            int F61MaxY(string xml) => System.Xml.Linq.XDocument.Parse(xml).Root!
                .Descendants("Graphics")
                .Select(g => int.TryParse((string?)g.Attribute("Y"), out int y) ? y : 0)
                .DefaultIfEmpty(0).Max();
            string f61G0 = doSvc51.WriteMulti(st51.Packets, doTpl51,
                new Dictionary<string, (int, string)>(), new ScadaMbGenSettings(), null, map51, named51);
            string f61G2 = doSvc51.WriteMulti(st51.Packets, doTpl51,
                new Dictionary<string, (int, string)>(), new ScadaMbGenSettings { DataChainGap = 200 },
                null, map51, named51);
            Check(F61MaxY(f61G2) == F61MaxY(f61G0) + 200,
                $"F61: DataChainGap=200 — цепочки сдвинуты на +200 " +
                $"(факт maxY {F61MaxY(f61G0)} → {F61MaxY(f61G2)})");

            // (3) Disc POU файла привязок: по умолчанию пустой (слово «Обработка» убрано);
            // паттерн — контекст первого пакета чанка
            var f61Pou0 = System.Xml.Linq.XDocument.Parse(f61G0).Root!
                .Element("POUS")!.Element("OnePOU");
            Check((f61Pou0?.Attribute("Disc")?.Value ?? "") == "",
                "F61: Disc POU файла привязок по умолчанию пустой (слово «Обработка» больше не ставится)");
            string f61Gc = doSvc51.WriteMulti(st51.Packets, doTpl51,
                new Dictionary<string, (int, string)>(),
                new ScadaMbGenSettings { DataOutPouCommentPattern = "{Master} опрос" },
                null, map51, named51);
            var f61PouC = System.Xml.Linq.XDocument.Parse(f61Gc).Root!
                .Element("POUS")!.Element("OnePOU");
            Check(f61PouC?.Attribute("Disc")?.Value == "MB_TCP_MASTER_4_1 опрос",
                $"F61: DataOutPouCommentPattern — Disc = контекст первого пакета чанка " +
                $"(факт: «{f61PouC?.Attribute("Disc")?.Value}»)");

            // (4) PouCommentPattern: Disc POU основного файла; пусто — Disc шаблона
            var gen61m = xmlSvc51.Write(pkts52, tpl52,
                new ScadaMbGenSettings { PouCommentPattern = "Опрос {Seq}" });
            var pou61m = System.Xml.Linq.XDocument.Parse(gen61m.PousXml).Root!
                .Element("POUS")!.Element("OnePOU");
            Check(pou61m?.Attribute("Disc")?.Value == "Опрос 2",
                $"F61: PouCommentPattern — Disc POU основного файла «Опрос {{Seq}}» → «Опрос 2» " +
                $"(факт: «{pou61m?.Attribute("Disc")?.Value}»)");
            var gen61m0 = xmlSvc51.Write(pkts52, tpl52, new ScadaMbGenSettings());
            var pou61m0 = System.Xml.Linq.XDocument.Parse(gen61m0.PousXml).Root!
                .Element("POUS")!.Element("OnePOU");
            Check((pou61m0?.Attribute("Disc")?.Value ?? "") == "",
                "F61: PouCommentPattern пуст — Disc POU основного файла из шаблона (без изменений)");

            // (5) MbBinding.Packet (мастер-detail «Привязки DataOut») + Direction
            Check(st51.Packets.Count > 0 && st51.Packets[0].Bindings.Count > 0 &&
                  ReferenceEquals(st51.Packets[0].Bindings[0].Packet, st51.Packets[0]),
                "F61: MbBinding.Packet — привязка ссылается на свой пакет плана");
            Check(st51.Packets[0].Bindings[0].Direction == "чтение" &&
                  new MbBinding { IsWrite = true }.Direction == "запись",
                "F61: Direction привязки — «чтение»/«запись» (колонка превью)");

            // (6) UI окна F13: новые поля настроек + превью пакетов вместо тегов F58
            string mbXaml61 = File.ReadAllText(@"c:\VSCode\IO_PJT\Views\ScadaModbusBlocksWindow.xaml");
            Check(mbXaml61.Contains("DataChainGap") && mbXaml61.Contains("Зазор между цепочками data") &&
                  mbXaml61.Contains("PouCommentPattern") && mbXaml61.Contains("Комментарий POU (мастер-блоки)") &&
                  mbXaml61.Contains("DataOutPouCommentPattern") && mbXaml61.Contains("Комментарий POU (привязки)"),
                "F61: поля «Зазор между цепочками data» и «Комментарий POU» в окне F13");
            Check(mbXaml61.Contains("ItemsSource=\"{Binding PreviewPackets}\"") &&
                  mbXaml61.Contains("SelectedPreviewPacket") && !mbXaml61.Contains("TagPreview"),
                "F61: превью — грид пакетов (PreviewPackets/SelectedPreviewPacket), грид тегов F58 убран");
        }

        /// <summary>F13/F14: точка IO_LIST с адресацией Modbus для планировщика.
        /// typno по умолчанию непустой — F14-фильтр «без Typno → вне генерации» не мешает
        /// F13-проверкам; передача null/"" проверяет фильтр SkippedNoTypno.</summary>
        private static IoPoint MakeMbPoint(string tag, string? plcTag, string? slaveMain,
            string? funcCode, string? address, string? dataType, string? station = "IMCS EBSM102",
            string? typno = "PID1000-01")
        {
            return new IoPoint
            {
                Identification = new Identification { Tag = tag, Typno = typno },
                Modbus = new Modbus
                {
                    PlcTag = plcTag,
                    SlaveIdMain = slaveMain,
                    FuncCode = funcCode,
                    Address = address,
                    DataType = dataType,
                    SlaveStation = station,
                    Timeout = "100ms"
                }
            };
        }

        /// <summary>F65: генерация экземпляров типовых алгоритмов — таблицы, формула марки,
        /// настройки/привязки/шаблоны, парсинг сигналов, точечная замена, пагинация файлов.</summary>
        private static void RunF65Part(string tmpDb, TableCreator creator)
        {
            Console.WriteLine("== F65 Генерация экземпляров типовых алгоритмов ==");
            var svc65 = new ScadaAlgoService(tmpDb);
            var xml65 = new ScadaAlgoXmlService();

            // (1) DDL: таблицы F65 созданы идемпотентно + повторный Ensure не падает
            Check(creator.TableExists(Schema.ScadaAlgoSettingsTable) &&
                  creator.TableExists(Schema.ScadaAlgoTemplatesTable) &&
                  creator.TableExists(Schema.ScadaAlgoBindingsTable) &&
                  creator.TableExists(Schema.TypnoTable),
                "F65: SCADA_ALGO_SETTINGS/TEMPLATES/BINDINGS + TYPNO созданы EnsureServiceTables");
            svc65.EnsureTables();
            Check(true, "F65: повторный EnsureTables — без исключений");

            // (2) формула марки (подтверждена буквально, Loop No = колонка Loop)
            Check(ScadaAlgoService.MarkFor("1115-PM-101B", "*", "") == "_1115_PM_101B",
                $"F65: маска «*» → _1115_PM_101B (факт «{ScadaAlgoService.MarkFor("1115-PM-101B", "*", "")}»)");
            Check(ScadaAlgoService.MarkFor("1115-PM-101B", "ZSC", "") == "_1115_PZSC_101B",
                $"F65: маска ZSC → _1115_PZSC_101B (факт «{ScadaAlgoService.MarkFor("1115-PM-101B", "ZSC", "")}»)");
            Check(ScadaAlgoService.MarkFor("1115-PM-101B", "*", "_main") == "_1115_PM_101B_main",
                "F65: суффикс приклеивается к марке");
            Check(ScadaAlgoService.MarkFor("", "*", "") == "" && ScadaAlgoService.MarkFor(null, "ZSC", "") == "",
                "F65: пустой Loop → пустая марка");
            var shortMark = ScadaAlgoService.MarkFor("PM-101B", "ZSC", "");
            Check(shortMark.StartsWith("_") && shortMark.EndsWith("01B"),
                $"F65: короткий Loop без 4-символьной зоны — не падает (факт «{shortMark}»)");
            Check(ScadaAlgoService.EnsureLeadingUnderscore("1115_PM") == "_1115_PM" &&
                  ScadaAlgoService.EnsureLeadingUnderscore("_1115_PM") == "_1115_PM",
                "F65: ведущий «_» марки гарантируется");

            // (3) конструктор марки: константа + {ИмяКолонки} первой строки контура
            var ctorPt = MakePoint("TAG-1", "1115-PM-101B", "DI-COM", "svc");
            Check(ScadaAlgoService.MarkFor("1115-PM-101B", "*", "", "_X", null) == "_1115_PM_101B_X",
                "F65: конструктор-константа приклеивается к марке");
            Check(ScadaAlgoService.RenderConstructor("{Loop}", ctorPt) == "1115-PM-101B",
                $"F65: конструктор {{Loop}} → значение первой строки (факт «{ScadaAlgoService.RenderConstructor("{Loop}", ctorPt)}»)");
            Check(ScadaAlgoService.MarkFor("1115-PM-101B", "*", "", "_{Loop}", ctorPt) == "_1115_PM_101B_1115-PM-101B",
                "F65: марка + конструктор с подстановкой {Loop}");

            // (4) настройки roundtrip
            svc65.SaveSettings(new AlgoGenSettings
            {
                InstancesPerFile = 2, FilePattern = "F65_{Template}_{Seq}",
                IdStride = 500, OutDir = @"c:\temp\f65", TemplateName = "TEST"
            });
            var s65 = svc65.LoadSettings();
            Check(s65.InstancesPerFile == 2 && s65.FilePattern == "F65_{Template}_{Seq}" &&
                  s65.IdStride == 500 && s65.OutDir == @"c:\temp\f65" && s65.TemplateName == "TEST",
                "F65: InstancesPerFile/FilePattern/IdStride/OutDir/TemplateName roundtrip");
            var cl65 = s65.Clone();
            cl65.InstancesPerFile = 99;
            Check(s65.InstancesPerFile == 2 && cl65.InstancesPerFile == 99,
                "F65: Clone настроек не портит оригинал");
            Check(new AlgoGenSettings { InstancesPerFile = 0, IdStride = 1 }.IdStride == 100,
                "F65: валидация IdStride (минимум 100)");
            svc65.SaveSettings(new AlgoGenSettings());

            // (5) TYPNO: вставить тестовые строки (в tmp-БД таблица пустая), прочитать
            using (var cn65 = new System.Data.OleDb.OleDbConnection(
                       $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={tmpDb};"))
            {
                cn65.Open();
                using var ins = cn65.CreateCommand();
                ins.CommandText = "INSERT INTO [TYPNO] ([№], [TYPNO], [Маска], [Суффикс], " +
                    "[Тип объекта], [Шаблон], [Подпись]) VALUES (?, ?, ?, ?, ?, ?, ?)";
                foreach (var (no, typno, mask, suf, obj, tpl, sign) in new (string, string, string, string, string, string, string)[]
                {
                    ("1", "F65-T", "*", "", "KLPD_v2", "TEST", ""),
                    ("2", "F65-T", "ZSC", "", "DD_v3", "TEST", "Display"),
                    ("3", "F65-OTHER", "*", "", "AN_v1", "OTHER", "")
                })
                {
                    ins.Parameters.Clear();
                    OleDbArgs.Add(ins, no);
                    OleDbArgs.Add(ins, typno);
                    OleDbArgs.Add(ins, mask);
                    OleDbArgs.Add(ins, suf);
                    OleDbArgs.Add(ins, obj);
                    OleDbArgs.Add(ins, tpl);
                    OleDbArgs.Add(ins, sign);
                    ins.ExecuteNonQuery();
                }
            }
            var tn65 = svc65.LoadTypno();
            Check(tn65.Count == 3 && tn65.Any(r => r.Typno == "F65-T" && r.Mask == "ZSC" && r.ObjType == "DD_v3") &&
                  tn65.Any(r => r.Template == "OTHER"),
                $"F65: LoadTypno читает кириллические колонки (факт {tn65.Count} строк)");

            // (6) привязки roundtrip: сохранить/загрузить по шаблону, чужой шаблон — пусто
            svc65.SaveBindings("TEST", new List<AlgoBinding>
            {
                new() { Signal = "_2120_XZV_00204", Mask = "*", Suffix = "", ObjType = "KLPD_v2" },
                new() { Signal = "_2120_XZV_00204_FCOM", Mask = "ZSC", Suffix = "", ObjType = "DD_v3",
                        Constructor = "_{Loop}" },
                new() { Signal = "   " } // пустой сигнал — не сохраняется
            });
            var bn65 = svc65.LoadBindings("TEST");
            Check(bn65.Count == 2 && bn65[0].Signal == "_2120_XZV_00204" && bn65[1].Constructor == "_{Loop}",
                $"F65: Save/LoadBindings roundtrip (факт {bn65.Count} строк)");
            Check(svc65.LoadBindings("test").Count == 2, "F65: LoadBindings IgnoreCase по имени шаблона");
            Check(svc65.LoadBindings("MISSING").Count == 0, "F65: привязки чужого шаблона не подмешиваются");

            // (7) шаблоны roundtrip: сохранить/найти/удалить
            string tplBody65 = MakeF65Template();
            svc65.SaveTemplate(new AlgoTemplate { Name = "TEST", Body = tplBody65, FilePath = @"c:\w\t.xml" });
            var tpl65 = svc65.LoadTemplate("TEST");
            Check(tpl65 != null && tpl65.Body == tplBody65 && tpl65.FilePath == @"c:\w\t.xml",
                "F65: SaveTemplate/LoadTemplate roundtrip (тело + путь)");
            Check(svc65.LoadTemplates().Count >= 1 && svc65.LoadTemplate("NOPE") == null,
                "F65: LoadTemplates/LoadTemplate(нет) → null");
            svc65.SaveTemplate(new AlgoTemplate { Name = "TEST", Body = tplBody65 + "<!--x-->" });
            Check(svc65.LoadTemplate("TEST")!.Body.EndsWith("<!--x-->"),
                "F65: повторное сохранение того же имени — upsert (1 строка, тело обновлено)");
            svc65.DeleteTemplate("TEST");
            Check(svc65.LoadTemplate("TEST") == null, "F65: DeleteTemplate удаляет шаблон");

            // (8) парсинг шаблона: валидность + сигналы (уникальные Block/@Info)
            var pi65 = xml65.Parse(tplBody65);
            Check(pi65.Valid && pi65.BlockCount == 3 && pi65.SignalCount == 3 && pi65.PouName == "KLPD_TEST",
                $"F65: Parse шаблона (факт: {pi65.Summary})");
            Check(!xml65.Parse("<x/>").Valid && !xml65.Parse("").Valid,
                "F65: невалидные шаблоны отклоняются (не BufScadaPOUS / пустой)");
            var sigs65 = ScadaAlgoXmlService.ParseSignals(tplBody65);
            Check(sigs65.Count == 3 && sigs65[0] == "_2120_XZV_00204" &&
                  sigs65.Contains("_2120_XZV_00204_FCOM") && sigs65.Contains("_2120_XZV_00204_ZXSC"),
                $"F65: ParseSignals — 3 уникальных сигнала (факт: {string.Join(", ", sigs65)})");

            // (9) точечная замена: длинные имена первыми (префикс не портит), предупреждение
            var warn9 = new List<string>();
            string rep9 = ScadaAlgoXmlService.ApplyReplacements(tplBody65, new List<(string, string)>
            {
                ("_2120_XZV_00204", "_1115_PM_101B"),
                ("_2120_XZV_00204_FCOM", "_1115_PM_101B_FCOM")
            }, warn9);
            Check(rep9.Contains("_1115_PM_101B_FCOM") && !rep9.Contains("_2120_XZV_00204"),
                "F65: замена длинных первыми — FCOM не испорчен заменой префикса");
            Check(warn9.Count == 0, $"F65: без ложных предупреждений о ненайденных именах (факт {warn9.Count})");
            var warn9b = new List<string>();
            ScadaAlgoXmlService.ApplyReplacements(tplBody65, new List<(string, string)> { ("NO_SUCH", "_X") }, warn9b);
            Check(warn9b.Count == 1 && warn9b[0].Contains("не найдено"),
                "F65: ненайденное имя → предупреждение, замена не применялась");
            Check(ScadaAlgoXmlService.BuildFileName(null, "TEST", 1) == "TEST_1.xml" &&
                  ScadaAlgoXmlService.BuildFileName("{Seq}_{Template}", "T", 3) == "3_T.xml" &&
                  ScadaAlgoXmlService.BuildFileName("{Template}_{Seq}", "a:b*c", 1) == "a_b_c_1.xml",
                "F65: BuildFileName — токены + санитизация + .xml");

            // (10) контуры Loop: группировка, смешанный Typno, без Typno
            var pts65 = new List<IoPoint>
            {
                MakePoint("T1", "1115-PM-101A", "DI-COM", "s1"),
                MakePoint("T2", "1115-PM-101A", "DO-COM", "s2"),
                MakePoint("T3", "1115-PM-101B", "DI-COM", "s3"),
                MakePoint("T4", "1115-PM-101C", "DI-COM", "s4"),
                MakePoint("T5", "1115-PM-101D", "DI-COM", "s5"),
                MakePoint("T6", "1115-PM-101E", "DI-COM", "s6"),
                MakePoint("T7", "1115-PM-101F", "DI-COM", "s7"),
                MakePoint("T8", "", "DI-COM", "s8") // без Loop — не группа
            };
            pts65[0].Identification.Typno = "F65-T";
            pts65[1].Identification.Typno = "F65-T";
            pts65[2].Identification.Typno = "F65-T";
            pts65[3].Identification.Typno = "F65-T";
            pts65[4].Identification.Typno = "F65-T";
            pts65[5].Identification.Typno = "F65-T";
            pts65[6].Identification.Typno = ""; // без Typno — Generate=false
            var (loops65, warn10) = ScadaAlgoService.LoadLoops(pts65);
            Check(loops65.Count == 6 && loops65[0].Loop == "1115-PM-101A" && loops65[0].TagCount == 2,
                $"F65: LoadLoops — 6 контуров, 1115-PM-101A с 2 тегами (факт {loops65.Count})");
            Check(loops65.All(l => l.FirstRow != null) && loops65[0].FirstRow.Tag == "T1",
                "F65: FirstRow контура — первая строка группы (источник колонок конструктора)");
            Check(loops65.Single(l => l.Loop == "1115-PM-101F").Generate == false,
                "F65: контур без Typno — Generate=false");
            Check(warn10.Count == 0, "F65: без ложных предупреждений о смешанном Typno");

            // (11) генерация: 6 контуров (5 с Typno) × InstancesPerFile=2 → 3 файла (2+2+1)
            var map65 = new List<(string Signal, string Mask, string Suffix)>
            {
                ("_2120_XZV_00204", "*", ""),
                ("_2120_XZV_00204_FCOM", "ZSC", ""),
                ("_2120_XZV_00204_ZXSC", "ZSC", "")
            };
            var gen11 = xml65.Generate(tplBody65, loops65, loop =>
                map65.Select(m => (m.Signal,
                    ScadaAlgoService.MarkFor(loop.Loop, m.Mask, m.Suffix, null, loop.FirstRow))).ToList(),
                new ScadaAlgoXmlService.GenerateOptions
                {
                    InstancesPerFile = 2, TemplateName = "TEST", IdStride = 10000
                });
            Check(gen11.Files.Count == 3 && gen11.Instances == 5 && gen11.Skipped == 1,
                $"F65: 5 экз. × 2/файл → 3 файла (факт {gen11.Files.Count} файлов, {gen11.Instances} экз., проп. {gen11.Skipped})");
            Check(gen11.Files[0].FileName == "TEST_1.xml" && gen11.Files[2].FileName == "TEST_3.xml",
                $"F65: имена файлов по FilePattern (факт {gen11.Files[0].FileName}, {gen11.Files[2].FileName})");
            Check(gen11.Files[0].Xml.Contains("_1115_PM_101A") &&
                  gen11.Files[0].Xml.Contains("_1115_PZSC_101B") &&
                  !gen11.Files[0].Xml.Contains("_2120_XZV_00204"),
                "F65: марки подставлены вместо эталонных тегов шаблона");
            Check(gen11.Files[0].Xml.Contains("NAME=\"1115-PM-101A\"") &&
                  gen11.Files[0].Xml.Contains("NAME=\"1115-PM-101B\"") &&
                  !gen11.Files[0].Xml.Contains("NAME=\"1115-PM-101C\""),
                "F65: в файле 1 — POU контуров A и B (C ушёл в файл 2)");
            Check(gen11.Files[2].Xml.Contains("NAME=\"1115-PM-101E\""),
                "F65: последний файл — остаток пачки (E)");

            // (12) FileName заполнен в гриде контуров (проверяем сразу после gen11,
            // до повторного Generate с другими настройками)
            Check(loops65.Single(l => l.Loop == "1115-PM-101A").FileName == "TEST_1.xml" &&
                  loops65.Single(l => l.Loop == "1115-PM-101E").FileName == "TEST_3.xml",
                "F65: AlgoLoopInfo.FileName — распределение по файлам видно в гриде");

            // уникальность ID внутри файла: 2 экземпляра — сдвинутые T11ID/cardId
            var xdoc11 = System.Xml.Linq.XDocument.Parse(gen11.Files[0].Xml);
            var t11s11 = xdoc11.Root!.Descendants("Block")
                .Select(b => (string?)b.Attribute("T11ID")).Where(v => v != null).ToList();
            Check(t11s11.Count == 6 && t11s11.Distinct().Count() == 6,
                $"F65: 2 экземпляра × 3 блока — все T11ID уникальны (факт {string.Join(",", t11s11)})");
            var cards11 = xdoc11.Root.Element("ISACARDSINFO")?.Elements("rec")
                .Select(r => (string?)r.Attribute("ID")).Where(v => v != null).ToList()
                ?? new List<string?>();
            Check(cards11.Count == 6 && cards11.Distinct().Count() == 6,
                $"F65: rec ISACARDSINFO — 6 карточек без дублей ID (факт {cards11.Count})");
            Check(gen11.Files[0].Xml.Contains("ISAOBJSINFO") && gen11.Files[0].Xml.Contains("KLPD_v2"),
                "F65: ISAOBJSINFO копируется из шаблона в каждый файл");
            // связь во втором экземпляре сдвинута (FP/LP)
            var fps11 = xdoc11.Root.Descendants("FirstPoint")
                .Select(f => ((string?)f.Attribute("FP"))?.Split('|')[0]).ToList();
            Check(fps11.Count == 2 && fps11[0] != fps11[1] && fps11.Contains("2173002"),
                $"F65: FP/LP связей сдвинуты между экземплярами (факт {string.Join(",", fps11)})");

            // контуры, исключённые галочкой, не попадают в файлы
            // (A выключен вручную; F остаётся выключенным — без Typno, как в LoadLoops)
            foreach (var l in loops65) l.Generate = l.Loop is not ("1115-PM-101A" or "1115-PM-101F");
            var gen11b = xml65.Generate(tplBody65, loops65, loop =>
                map65.Select(m => (m.Signal,
                    ScadaAlgoService.MarkFor(loop.Loop, m.Mask, m.Suffix, null, loop.FirstRow))).ToList(),
                new ScadaAlgoXmlService.GenerateOptions { InstancesPerFile = 10, TemplateName = "TEST" });
            Check(gen11b.Instances == 4 && gen11b.Files.Count == 1 &&
                  !gen11b.Files[0].Xml.Contains("NAME=\"1115-PM-101A\""),
                $"F65: снятая галочка «Генерировать» исключает контур (факт {gen11b.Instances} экз.)");
            foreach (var l in loops65) l.Generate = true;

            // (13) XAML/VM: меню главного окна, окно генератора, команды VM
            string mainXaml65 = File.ReadAllText(@"c:\VSCode\IO_PJT\Views\MainWindow.xaml");
            Check(mainXaml65.Contains("Генерация экземпляров типовых алгоритмов") &&
                  mainXaml65.Contains("ScadaAlgoInstancesCommand"),
                "F65: пункт меню «Генерация экземпляров типовых алгоритмов» + команда в MainWindow.xaml");
            string winXaml65 = File.ReadAllText(@"c:\VSCode\IO_PJT\Views\ScadaAlgoInstancesWindow.xaml");
            Check(winXaml65.Contains("BufScadaPOUS") && winXaml65.Contains("InstancesPerFile") &&
                  winXaml65.Contains("TYPNO") && winXaml65.Contains("BuildCommand") &&
                  winXaml65.Contains("GenerateCommand") && winXaml65.Contains("SaveBindingsCommand") &&
                  winXaml65.Contains("FillBindingsFromTypnoCommand") && winXaml65.Contains("DeleteTemplateCommand"),
                "F65: ScadaAlgoInstancesWindow — шаблон/настройки/сетка TYPNO/команды");
            string vmXaml65 = File.ReadAllText(@"c:\VSCode\IO_PJT\ViewModels\ScadaAlgoInstancesViewModel.cs");
            Check(vmXaml65.Contains("AlgoXml.Generate") && vmXaml65.Contains("LoadLoops") &&
                  vmXaml65.Contains("FillBindingsFromTypno"),
                "F65: VM использует ScadaAlgoService/ScadaAlgoXmlService");
            string mainVm65 = File.ReadAllText(@"c:\VSCode\IO_PJT\ViewModels\MainViewModel.cs");
            Check(mainVm65.Contains("ScadaAlgoInstancesCommand") && mainVm65.Contains("OpenScadaAlgoInstancesWindow"),
                "F65: MainViewModel — команда и открытие окна генератора");

            svc65.SaveSettings(new AlgoGenSettings());
        }

        /// <summary>F65: мини-шаблон BufScadaPOUS для смоука (1 OnePOU, 3 блока, 1 связь,
        /// 3 карточки, ISAOBJSINFO).</summary>
        private static string MakeF65Template() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<BufScadaPOUS><Common VER=\"29\" Project=\"test\"/>" +
            "<POUS><OnePOU ID=\"20279\" NAME=\"KLPD_TEST\" isFBD=\"1\" GroupID=\"20278\" Enabled=\"1\" POUNum=\"67\" Disc=\"\">" +
            "<PARAMS DPARAMS=\"0\" HEIGHT=\"100\" WIDTH=\"100\"/>" +
            "<ISAGraf><Blocks>" +
            "<Block GROBJTYPE=\"37\" Info=\"_2120_XZV_00204\" T11ID=\"2163001\">" +
            "<Graphics X=\"0\" Y=\"0\" WIDTH=\"100\" HEIGHT=\"80\"/>" +
            "<Params T11Text=\"\" CI=\"1\" CO=\"1\" VMODE=\"0\" IsaObjId=\"1407\" cardId=\"717001\"><IV/></Params></Block>" +
            "<Block GROBJTYPE=\"31\" Info=\"_2120_XZV_00204_FCOM\" T11ID=\"2163002\">" +
            "<Graphics X=\"0\" Y=\"100\" WIDTH=\"100\" HEIGHT=\"20\"/>" +
            "<Params T11Text=\"\" CI=\"1\" CO=\"1\" VMODE=\"0\" IsaObjId=\"-9\" cardId=\"717002\"><IV/></Params></Block>" +
            "<Block GROBJTYPE=\"31\" Info=\"_2120_XZV_00204_ZXSC\" T11ID=\"2163003\">" +
            "<Graphics X=\"0\" Y=\"200\" WIDTH=\"100\" HEIGHT=\"20\"/>" +
            "<Params T11Text=\"\" CI=\"1\" CO=\"1\" VMODE=\"0\" IsaObjId=\"-9\" cardId=\"717003\"><IV/></Params></Block>" +
            "</Blocks><Links>" +
            "<Link Negative=\"false\" asPointer=\"false\" UserEdit=\"false\" Color=\"0\" GetBitNum=\"-1\" ConvertTo=\"0\">" +
            "<PointList PL=\"(10,10);(20,20);\"/>" +
            "<FirstPoint FP=\"2163002|False|0|0,0,0,0\"/>" +
            "<LastPoint LP=\"2163003|True|0|0,0,0,0\"/></Link>" +
            "</Links></ISAGraf></OnePOU></POUS>" +
            "<ISAOBJSINFO><rec ID=\"1407\" Info=\"KLPD_v2\"/></ISAOBJSINFO>" +
            "<ISACARDSINFO>" +
            "<rec ID=\"717001\" Info=\"_2120_XZV_00204\" IsRetain=\"1\" Name=\"Клапан\" SSize=\"0\" KLPath=\"ПАЗ\"/>" +
            "<rec ID=\"717002\" Info=\"_2120_XZV_00204_FCOM\" IsRetain=\"1\" Name=\"Формирователь\" SSize=\"0\" KLPath=\"ПАЗ\"/>" +
            "<rec ID=\"717003\" Info=\"_2120_XZV_00204_ZXSC\" IsRetain=\"1\" Name=\"КВ закрыт\" SSize=\"0\" KLPath=\"ПАЗ\"/>" +
            "</ISACARDSINFO></BufScadaPOUS>";

        /// <summary>F9.1: задвоение тега между документами — аудит Duplicate (равноправная запись).</summary>
        private static int RunDuplicatePart(IOListRepository repo, AuditService audit)




        {
            Console.WriteLine("== F9.1 Задвоение тега между документами ==");
            var pOtherDoc = MakePoint("1115-MHS-00101A", "1115-M-00101A", "DO-COM",
                "1115-P-101A motor DCS command:stop");
            pOtherDoc.Revision!.FileName = "ДРУГОЙ_ФАЙЛ.xlsx";
            pOtherDoc.Revision.Sheet = "Лист2";
            pOtherDoc.Signal!.Service = "1115-P-101A motor DCS command:stop CHANGED";
            var res = repo.Upsert(pOtherDoc,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Service" });
            Check(!string.IsNullOrEmpty(res.ExistingFileName) && res.ExistingFileName != "ДРУГОЙ_ФАЙЛ.xlsx",
                $"Upsert вернул прежний документ: {res.ExistingFileName}");

            repo.RecordDuplicateTagAudit(pOtherDoc.Tag, res.ExistingFileName, res.ExistingSheet,
                pOtherDoc.Revision.FileName, pOtherDoc.Revision.Sheet, pOtherDoc.Identification.System);
            var dupHist = audit.GetHistory(field: "Tag");
            Check(dupHist.Any(h => h.Action == AuditAction.Duplicate),
                "аудит содержит Duplicate по полю Tag");
            var dupRec = dupHist.First(h => h.Action == AuditAction.Duplicate);
            Check(dupRec.OldValue != null && dupRec.NewValue != null &&
                  dupRec.OldValue.Contains("MCC") && dupRec.NewValue.Contains("ДРУГОЙ_ФАЙЛ"),
                $"Duplicate: «{dupRec.OldValue}» → «{dupRec.NewValue}»");
            Check(dupRec.FileName == "ДРУГОЙ_ФАЙЛ.xlsx" && dupRec.Sheet == "Лист2",
                $"Duplicate: контекст-документ = {dupRec.FileName} :: {dupRec.Sheet}");

            var summary = audit.GetSummary();
            Check(summary.Any(s => s.Action == AuditAction.Duplicate), "GetSummary учитывает Duplicate");
            var reportRows = ReportBuilder.Build(summary);
            Check(reportRows.Sum(r => r.DuplicateCount) > 0, "ReportBuilder считает DuplicateCount");
            Check(!string.IsNullOrWhiteSpace(dupRec.ChangeDescription) &&
                  dupRec.ChangeDescription.Contains("задвоение"),
                $"ChangeDescription: {dupRec.ChangeDescription}");

            // ===== F37 (5): DeriveObjectTag — до пробела → префикс контроллера → до «_» =====
            Check(ModbusMapService.DeriveObjectTag(
                      "MCU.1120-P-203A.HE-JB - Oil heater junction box_MCCB Working Position 1") ==
                  "1120-P-203A.HE-JB",
                "F37.5: ObjectTag — до пробела, MCU. срезан, до «_» (полное имя двигателя сохранено)");
            Check(ModbusMapService.DeriveObjectTag("1115-PM-101A_STA.MCU") == "1115-PM-101A",
                "F37.5: ObjectTag — историческое «до первого _» для _STA.MCU-суффиксов");
            Check(ModbusMapService.DeriveObjectTag("1120-P-203A.HE-JB") == "1120-P-203A.HE-JB",
                "F37.5: ObjectTag — полное имя без пробелов/префиксов не режется (уровень «до точки» — в FindMapRow)");
            Check(ModbusMapService.DeriveObjectTag("_STA.MCU") == "",
                "F37.5: ObjectTag — ведущее «_» → пусто (как Split('_')[0])");

            // ===== F37 (2): Controller — группировка пакетов и правила SlaveId =====
            var f37Planner = new ModbusBlockPlannerService("");
            IoPoint F37CtrlPoint(string tag, string controller, string addr)
            {
                var pt = MakePoint(tag, controller == "CTRL_A" ? "1120-M-00203A" : "1120-M-00203B",
                    "DI-COM", "1120-P-203A motor Motor status:running");
                pt.Modbus ??= new Modbus();
                pt.Modbus.PlcTag = "1120-PM-00203A_STA.MCU";
                pt.Modbus.FuncCode = "03";
                pt.Modbus.Address = addr;
                pt.Modbus.SlaveIdMain = "192.168.4.10\r\nport 502";
                pt.Modbus.SlaveStation = "ST1";
                pt.Modbus.DataType = "16-Bit Unsigned";
                pt.Identification.Typno = "T1";
                pt.Identification.Controller = controller;
                return pt;
            }
            var f37CPoints = new List<IoPoint>
            {
                F37CtrlPoint("1120-MXA-00203A", "CTRL_A", "10"),
                F37CtrlPoint("1120-MXA-00203B", "CTRL_B", "10")
            };
            var f37CRules = new List<MbSlaveIdRule>
            {
                new MbSlaveIdRule { Controller = "CTRL_A", Station = "ST1", SlaveId = 5 }
            };
            var f37Plan = f37Planner.BuildPlan(f37CPoints,
                new ScadaMbGenSettings { DefaultSlaveId = 1, DefaultPort = 502 },
                f37CRules, "MB_TCP_MASTER_4");
            Check(f37Plan.Packets.Count == 2 &&
                  f37Plan.Packets.All(p => p.Station == "ST1") &&
                  f37Plan.Packets.Select(p => p.Controller).OrderBy(c => c)
                      .SequenceEqual(new[] { "CTRL_A", "CTRL_B" }),
                $"F37.2: одна станция у двух контроллеров → 2 пакета со своим Controller " +
                $"(факт: {string.Join(", ", f37Plan.Packets.Select(p => p.Controller + "/" + p.Station))})");
            var f37Pa = f37Plan.Packets.FirstOrDefault(p => p.Controller == "CTRL_A");
            var f37Pb = f37Plan.Packets.FirstOrDefault(p => p.Controller == "CTRL_B");
            Check(f37Pa?.SlaveId == 5 && f37Pb?.SlaveId == 1 &&
                  f37Plan.Packets.Select(p => p.MasterName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
                $"F37.2: SlaveId — точное правило (CTRL_A|ST1)=5, CTRL_B → Default=1; имена уникальны " +
                $"(факт: {f37Pa?.SlaveId}/{f37Pb?.SlaveId}, " +
                $"{string.Join(",", f37Plan.Packets.Select(p => p.MasterName))})");

            // F37.2: merge правил — пара (Controller, Station); универсальное (без Controller)
            // покрывает все контроллеры станции
            var f37Merged = ModbusBlockPlannerService.MergeSlaveRulesFromPoints(
                new List<MbSlaveIdRule> { new MbSlaveIdRule { Controller = "CTRL_A", Station = "ST1", SlaveId = 5 } },
                f37CPoints);
            Check(f37Merged.Count == 2 &&
                  f37Merged.Any(r => r.Controller == "CTRL_A" && r.SlaveId == 5) &&
                  f37Merged.Any(r => r.Controller == "CTRL_B" && r.SlaveId == 1),
                $"F37.2: MergeSlaveRulesFromPoints — (CTRL_A|ST1) сохранено, (CTRL_B|ST1) создано " +
                $"(факт: {string.Join(", ", f37Merged.Select(r => r.Controller + "|" + r.Station + "=" + r.SlaveId))})");
            var f37Merged2 = ModbusBlockPlannerService.MergeSlaveRulesFromPoints(
                new List<MbSlaveIdRule> { new MbSlaveIdRule { Station = "ST1", SlaveId = 9 } },
                f37CPoints);
            Check(f37Merged2.Count == 1 && f37Merged2[0].Controller == "" && f37Merged2[0].SlaveId == 9,
                "F37.2: универсальное правило (без Controller) не дублируется на контроллеры");

            // ===== F37 (3): split-экспорт — непрерывность T11/cardId между файлами =====
            string f37X1 = "<BufScadaPOUS><POUS><OnePOU ID=\"1\"/></POUS>" +
                "<Blocks><Block T11ID=\"2000001\"><Params cardId=\"2000002\"/></Block></Blocks>" +
                "<ISACARDSINFO><rec ID=\"2000010\"/></ISACARDSINFO></BufScadaPOUS>";
            var f37Shift = ScadaTemplateXmlService.ShiftSettingsForNextFile(
                new ScadaMbGenSettings { BaseT11Id = 1000000, BaseCardId = 500000 }, f37X1);
            Check(f37Shift.BaseT11Id == 2000002 && f37Shift.BaseCardId == 2000020,
                $"F37.3: ShiftSettingsForNextFile — BaseT11Id/BaseCardId выше максимумов файла " +
                $"(факт: {f37Shift.BaseT11Id}/{f37Shift.BaseCardId})");
            Check(ScadaTemplateXmlService.MaxT11(f37X1) == 2000001 &&
                  ScadaTemplateXmlService.MaxCardId(f37X1) == 2000010,
                "F37.3: MaxT11/MaxCardId видят Block/Params/rec-счётчики файла");
            Check(new ScadaMbGenSettings().SplitByControllerStation,
                "F37.3: SplitByControllerStation по умолчанию включён (новый режим экспорта)");
            Check(!new ScadaMbGenSettings { SplitByControllerStation = false }.Clone()
                      .SplitByControllerStation,
                "F37.3: Clone настроек сохраняет SplitByControllerStation (сводный режим)");

            Console.WriteLine();
            Console.WriteLine($"Итого: passed={_passed}, failed={_failed}");
            return _failed == 0 ? 0 : 1;
        }
    }
}
