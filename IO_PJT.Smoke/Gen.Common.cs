using System;
using System.Collections.Generic;
using IO_PJT.Models;

namespace IO_PJT.Smoke
{
    public static partial class Program
    {
        /// <summary>Минимальный мастер-шаблон: блоки мастера main/reserve (GROBJTYPE=37) и три
        /// объекта с токенами имени мастера (GROBJTYPE=31) + константа SLAVEID, связанная с мастером.</summary>
        private static string MasterTemplateXml() => (
            "<?xml version='1.0' encoding='UTF-8'?><BufScadaPOUS><Common/>" +
            "<POUS><OnePOU ID='2001' NAME='MB_TCP_MASTER_1'><PARAMS WIDTH='800' HEIGHT='800'/>" +
            "<ISAGraf><Blocks>" +
            "<Block T11ID='761001' GROBJTYPE='37' Info='NAME_PACK_TCP_MAINE'>" +
            "<Graphics X='440' Y='130' WIDTH='150' HEIGHT='310'/>" +
            "<Params T11Text='' CI='7' CO='6' IsaObjId='17573' cardId='761010'><IV/></Params></Block>" +
            "<Block T11ID='761002' GROBJTYPE='37' Info='NAME_PACK_TCP_RESERVE'>" +
            "<Graphics X='440' Y='530' WIDTH='150' HEIGHT='310'/>" +
            "<Params T11Text='' CI='7' CO='6' IsaObjId='17573' cardId='761011'><IV/></Params></Block>" +
            "<Block T11ID='761003' GROBJTYPE='31' Info='NAME_PACK_TCP_MAINE.Busy'>" +
            "<Graphics X='180' Y='180' WIDTH='190' HEIGHT='20'/>" +
            "<Params T11Text='.Busy' CI='1' CO='1' IsaObjId='-9' cardId='761012'><IV/></Params></Block>" +
            "<Block T11ID='761004' GROBJTYPE='31' Info='NAME_PACK_TCP_MAINE.BUSY.Busy'>" +
            "<Graphics X='180' Y='240' WIDTH='190' HEIGHT='20'/>" +
            "<Params T11Text='.Busy' CI='1' CO='1' IsaObjId='-9' cardId='761013'><IV/></Params></Block>" +
            "<Block T11ID='761005' GROBJTYPE='31' Info='NAME_PACK_TCP_RESERVE.Busy'>" +
            "<Graphics X='180' Y='300' WIDTH='190' HEIGHT='20'/>" +
            "<Params T11Text='.Busy' CI='1' CO='1' IsaObjId='-9' cardId='761014'><IV/></Params></Block>" +
            "<Block T11ID='761006' GROBJTYPE='31' Info='SLAVEID'>" +
            "<Graphics X='180' Y='360' WIDTH='190' HEIGHT='20'/>" +
            "<Params T11Text='' CI='1' CO='1' IsaObjId='-13' cardId='761015'><IV>1</IV></Params></Block>" +
            "</Blocks><Links>" +
            "<Link Negative='false' asPointer='false' UserEdit='false' Color='0' GetBitNum='-1' ConvertTo='0'>" +
            "<PointList PL='(370,190);(440,190);'/><FirstPoint FP='761006|False|0|0,0,100,20'/>" +
            "<LastPoint LP='761001|True|SlaveId|0,0,100,20'/></Link>" +
            "</Links></ISAGraf></OnePOU></POUS>" +
            "<ISAOBJSINFO><rec ID='17573' Info='MB_TCP_MASTER' LibName='TenixRtLib'/></ISAOBJSINFO>" +
            "<ISACARDSINFO>" +
            "<rec ID='761010' Info='NAME_PACK_TCP_MAINE' IsRetain='-1' Name='' SSize='0' KLPath=''/>" +
            "<rec ID='761011' Info='NAME_PACK_TCP_RESERVE' IsRetain='-1' Name='' SSize='0' KLPath=''/>" +
            "<rec ID='761012' Info='NAME_PACK_TCP_MAINE.Busy' IsRetain='-1' Name='' SSize='0' KLPath=''/>" +
            "<rec ID='761013' Info='NAME_PACK_TCP_MAINE.BUSY.Busy' IsRetain='-1' Name='' SSize='0' KLPath=''/>" +
            "<rec ID='761014' Info='NAME_PACK_TCP_RESERVE.Busy' IsRetain='-1' Name='' SSize='0' KLPath=''/>" +
            "<rec ID='761015' Info='SLAVEID' IsRetain='-1' Name='' SSize='0' KLPath=''/>" +
            "</ISACARDSINFO></BufScadaPOUS>").Replace('\'', '"');

        /// <summary>Пара пакетов main + reserve (twin) с одной привязкой.</summary>
        private static List<MbPacketPlan> MasterPackets()
        {
            var reserve = new MbPacketPlan
            {
                No = 2, MasterName = "_MB_X_RESERVE", IsReserve = true,
                Ip1 = 192, Ip2 = 168, Ip3 = 4, Ip4 = 11, Port = 502,
                SlaveId = 8, FuncCode = 3, Address = 10, Count = 5, TimeoutMs = 150
            };
            var main = new MbPacketPlan
            {
                No = 1, MasterName = "_MB_X_MAIN",
                Ip1 = 192, Ip2 = 168, Ip3 = 4, Ip4 = 10, Port = 502,
                SlaveId = 7, FuncCode = 3, Address = 10, Count = 5, TimeoutMs = 150,
                ReserveTwin = reserve
            };
            main.Bindings.Add(new MbBinding
            {
                Tag = "1130-MXA-01501A", Address = 10, Offset = 0, BlockName = "_MB_X_MAIN",
                DataType = "BOOL", Typno = "P101-T",
                ColumnValues = new Dictionary<string, string> { ["M_Marka"] = "1130-MXA-01501A" }
            });
            return new List<MbPacketPlan> { main, reserve };
        }
    }
}
