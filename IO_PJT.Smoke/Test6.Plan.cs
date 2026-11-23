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
        private static IoPoint MbPoint(string tag, string? plcTag, string? address, string? typno,
            string? backup = null, string func = "03") => new()
        {
            Identification = new Identification { Tag = tag, Typno = typno, IoType = "AI-COM" },
            Modbus = new Modbus
            {
                PlcTag = plcTag, FuncCode = func, Address = address, DataType = "16-Bit Unsigned",
                SlaveStation = "ST100", SlaveIdMain = Ip10, SlaveIdBackup = backup, Timeout = "150ms"
            }
        };

        // ============ 6. План блоков (пакеты) ============
        private static void TestBlockPlan()
        {
            Check(ModbusBlockPlannerService.ParseSlaveIdMain(Ip10, 502) is (192, 168, 4, 10, 502),
                "ParseSlaveIdMain: IP и порт из текста адреса");
            Check(ModbusBlockPlannerService.NormalizeFuncCode("03") == 3 &&
                  ModbusBlockPlannerService.ParseAddress("100") == 100,
                "NormalizeFuncCode/ParseAddress");

            var svc = new ModbusBlockPlannerService(NewDbPath()); // БД не нужна для BuildPlan
            var points = new List<IoPoint>
            {
                MbPoint("T-1", "PLC_A", "10", "TYP1", Ip12),
                MbPoint("T-2", "PLC_B", "11", "TYP1", Ip12),
                MbPoint("T-3", "PLC_C", "12", null, Ip12),   // без Typno → вне генерации
                MbPoint("T-4", null, null, "TYP1")            // без адресации Modbus
            };
            var plan = svc.BuildPlan(points, new ScadaMbGenSettings { DefaultPort = 502 },
                new List<MbSlaveIdRule>(), "MB_TCP_MASTER_1");

            Check(plan.SkippedNoTypno == 1 && plan.SkippedNoAddressing == 1,
                $"отсев: без Typno 1, без адресации 1 (факт {plan.SkippedNoTypno}/{plan.SkippedNoAddressing})");

            var mains = plan.Packets.Where(p => !p.IsReserve).ToList();
            Check(mains.Count >= 1 && mains.Sum(p => p.Bindings.Count) == 2,
                $"2 точки попали в основные пакеты (пакетов {mains.Count})");
            var main = mains[0];
            Check(main.FuncCode == 3 && main.Address == 10 && main.SlaveId >= 1 &&
                  main.Ip4 == 10 && main.Port == 502,
                $"основной пакет: FC03, адрес 10, IP 192.168.4.10:502 (факт {main.Endpoint})");
            Check(main.Bindings.All(b => b.DataOutExpr.StartsWith(main.MasterName + ".DataOut[")),
                "привязки ссылаются на DataOut блока мастера");

            Check(plan.ReservePackets == 1 && main.ReserveTwin != null,
                $"резервный IP → создан twin-пакет (ReservePackets {plan.ReservePackets})");
            var twin = main.ReserveTwin!;
            Check(twin.IsReserve && twin.Ip4 == 12 && twin.Address == main.Address &&
                  twin.Count == main.Count && twin.SlaveId == main.SlaveId &&
                  twin.FuncCode == main.FuncCode,
                "twin: тот же адрес/Count/SlaveId/FC, резервный IP 192.168.4.12");
            Check(twin.MasterName != main.MasterName && !string.IsNullOrWhiteSpace(twin.MasterName),
                $"имена блоков main/reserve различаются ({main.MasterName} / {twin.MasterName})");
            Check(plan.Packets.Select(p => p.MasterName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                  == plan.Packets.Count,
                "имена всех блоков уникальны");
        }
    }
}
