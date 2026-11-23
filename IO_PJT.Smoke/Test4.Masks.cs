using System;
using System.Collections.Generic;
using System.Linq;
using IO_PJT.Services;

namespace IO_PJT.Smoke
{
    public static partial class Program
    {
        // ============ 4. Маски связывания ============
        private static void TestMasks()
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Area"] = "1120",
                ["Class"] = "M",
                ["Loop"] = "1120-M-01203A",
                ["Empty"] = ""
            };

            Check(MaskPattern.Parse("") == null && MaskPattern.Parse("  ") == null,
                "пустая маска → Parse = null (полное совпадение)");
            Check(MaskPattern.Parse("{Area}&{Class}")!.Evaluate(row) == "1120-M",
                "{Area}&{Class} → «1120-M» (& = разделитель «-»)");
            Check(MaskPattern.Parse("{Area}&{Empty}&{Class}")!.Evaluate(row) == "1120-M",
                "пустой компонент не даёт двойного разделителя");
            Check(MaskPattern.Parse("{Loop}(-1)")!.Evaluate(row) == "1120-M-01203",
                "(-1) убирает 1 символ справа");
            Check(MaskPattern.Parse("{Area}(+2)")!.Evaluate(row) == "11",
                "(+2) берёт 2 символа слева");
            Check(MaskPattern.Parse("{Loop}(-1)*")!.Evaluate(row) == "1120-M-01203*",
                "wildcard «*» добавляется в паттерн");

            Check(MaskPattern.Score("1120-M-01203", "1120-M-01203") == 0 &&
                  MaskPattern.Score("1120-M-01203*", "1120-M-01203") == 0,
                "Score: полное совпадение = 0");
            int? partial = MaskPattern.Score("1120-M-01203*", "1120-M-01203A_STA");
            Check(partial is > 0, $"Score: wildcard поглотил символы > 0 (факт {partial})");
            Check(MaskPattern.Score("1120-M-01203", "9999-M-01203") == null,
                "Score: не совпало → null");
            Check(MaskPattern.IsMatch("1120-*", "1120-M-1") && !MaskPattern.IsMatch("1121-*", "1120-M-1"),
                "IsMatch: wildcard и несовпадение");

            var cands = new List<(string Row, string? Value)>
            {
                ("long", "1120-M-01203A_STA"),
                ("exact", "1120-M-01203"),
                ("none", "2000-X")
            };
            Check(MaskPattern.FindBest("1120-M-01203*", cands) == "exact",
                "FindBest выбирает минимальное отклонение");

            Check(MaskPattern.Validate("") == null && MaskPattern.Validate("{Tag}&{M_Marka}") == null,
                "Validate: пустая и корректная маски — ок");
            Check(MaskPattern.Validate("{НетТакойКолонки}") != null,
                "Validate: неизвестная колонка → текст ошибки");
        }
    }
}
