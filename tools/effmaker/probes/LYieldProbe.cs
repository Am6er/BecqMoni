using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.Linq;
using System.Text;

// `M9` (П23 12.09.2026, решение Amber «ω_L из fluorescence_yield + f13 в
// СЛЕДУЮЩИЙ единый счёт склада»): ВЫХОД L-ФЛУОРЕСЦЕНЦИИ ПО УРОВНЯМ КЛЮЧА
// `LYieldSupply` — числом из таблиц, без розыгрыша.
//
// Две части, и обе нужны (тот же довод, что у `OmegaProbe`): первая — что
// поставка ДОЕХАЛА до кода (ω_L xraylib и переходы Костера—Кронига читаются
// из базы и различимы от EADL), вторая — цена ключа на элемент: ПОЛНЫЙ
// радиационный выход дырки на L1/L2/L3 (ν_i, `Fluorescence.LYield`) по уровням
// 0 / 1 / 2 и отношение к уровню 0. Ноль разницы на тяжёлых значил бы, что
// ключ не доехал; на лёгких (Z < 50, ω_L ≲ 1e-3) разница ОБЯЗАНА быть
// мала — это контроль, что ключ не трогает того, чего не должен.
//
// Уровень 2 требует таблицы `coster_kronig` (импортёр
// `tools/nucdb/import_coster_kronig.py`, базу пишет только Amber): без неё
// столбец печатается прочерком, а не откатом на EADL.
//
//   lyieldprobe [--z=26,29,53,55,74,82,83] [--e=59.541]
//
// `--e=` — энергия кванта для части 3 (доли подоболочек EPICS2017 зависят от
// неё): 30 кэВ — ниже K-края иода, там L-кванты рождаются только первичным
// поглощением и счётчик `CountLXray` у `G4RawProbe` сверяется с частью 3 прямо.
class LYieldProbe
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        int[] zs = { 13, 26, 29, 53, 55, 74, 82, 83, 92 };
        double energyKev = 59.541;
        foreach (string a in args)
        {
            if (a.StartsWith("--z=", StringComparison.Ordinal))
                zs = a.Substring(4).Split(',').Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
            else if (a.StartsWith("--e=", StringComparison.Ordinal))
                energyKev = double.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        bool ckSupply = MaterialDatabase.HasCosterKronigSupply;
        Console.WriteLine("таблица coster_kronig (xraylib) в matdb: {0}",
                          ckSupply ? "ЕСТЬ — уровень 2 доступен" : "НЕТ — уровень 2 отказал бы (импортёр tools/nucdb/import_coster_kronig.py)");
        Console.WriteLine();
        Console.WriteLine("=== 1. Поставка доехала до кода? ω_L и переходы Костера—Кронига по элементам");
        Console.WriteLine("  Z  эл    ω_L1 EADL  xraylib | ω_L2 EADL  xraylib | ω_L3 EADL  xraylib | f12 EADL xraylib | f13 EADL xraylib | f23 EADL xraylib");
        int reached = 0, missing = 0;
        foreach (int z in zs)
        {
            MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(z);
            if (f == null || !f.HasL)
            {
                Console.WriteLine("{0,3}  {1,-3}  L-серии в базе нет", z, MaterialDatabase.SymbolOf(z));
                continue;
            }

            if (f.OmegaLSupply != null) reached++; else missing++;
            var sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0,3}  {1,-3}  ", z, MaterialDatabase.SymbolOf(z)));
            for (int li = 0; li < 3; li++)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0,8:F4}  {1,7} | ",
                                        f.OmegaLAt(li, 0),
                                        f.OmegaLSupply != null && f.OmegaLSupply[li] > 0.0
                                            ? f.OmegaLSupply[li].ToString("F4", CultureInfo.InvariantCulture) : "—"));
            }

            for (int j = 0; j < 3; j++)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0,7:F4} {1,7} | ",
                                        f.CkAt(j, 1),
                                        f.CkSupply != null ? f.CkSupply[j].ToString("F4", CultureInfo.InvariantCulture) : "—"));
            }

            Console.WriteLine(sb.ToString());
        }

        Console.WriteLine("  поставка ω_L есть у {0} из {1} запрошенных, нет у {2} (у них ключ берёт EADL и на ВКЛ)",
                          reached, reached + missing, missing);
        Console.WriteLine();
        Console.WriteLine("=== 2. Полный радиационный выход дырки ν_i (с переходами) по уровням ключа; отношение к уровню 0");
        Console.WriteLine("  Z  эл  |   ν_L1: ур.0    ур.1    ур.2   ур.1/0  ур.2/0 |   ν_L2: ур.0    ур.1    ур.2   ур.1/0  ур.2/0 |   ν_L3: ур.0    ур.1    ур.2   ур.1/0  ур.2/0");
        double worstLight = 0.0;
        int worstLightZ = 0;
        foreach (int z in zs)
        {
            MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(z);
            if (f == null || !f.HasL)
            {
                continue;
            }

            var sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0,3}  {1,-3} |", z, MaterialDatabase.SymbolOf(z)));
            for (int li = 0; li < 3; li++)
            {
                double y0 = f.LYield(li, 0), y1 = f.LYield(li, 1);
                double y2 = ckSupply ? f.LYield(li, 2) : double.NaN;
                sb.Append(string.Format(CultureInfo.InvariantCulture, " {0,9:F5} {1,7:F5} {2,7} {3,7:F3} {4,7} |",
                                        y0, y1,
                                        ckSupply ? y2.ToString("F5", CultureInfo.InvariantCulture) : "—",
                                        y0 > 0.0 ? y1 / y0 : 0.0,
                                        ckSupply && y0 > 0.0 ? (y2 / y0).ToString("F3", CultureInfo.InvariantCulture) : "—"));
                if (z < 50)
                {
                    // Контроль на лёгких: абсолютная разница выходов, доли.
                    double d = Math.Abs(y1 - y0);
                    if (d > worstLight)
                    {
                        worstLight = d;
                        worstLightZ = z;
                    }
                }
            }

            Console.WriteLine(sb.ToString());
        }

        Console.WriteLine();
        Console.WriteLine("контроль лёгких (Z < 50): наибольшая абсолютная разница ν(ур.1) − ν(ур.0) = {0:E3} (Z={1})",
                          worstLight, worstLightZ);

        // 3. Ожидаемое число L-квантов на ОДНО фотопоглощение при 59.541 кэВ —
        // Σ lFrac_i(E)·ν_i по открытым подоболочкам (доли EPICS2017,
        // `PhotoShellModel.LFractions`). Это прямая проверка розыгрыша: счётчик
        // `CountLXray` у `G4RawProbe` на голом NaI обязан вырасти между уровнями
        // в ту же долю, что это число у иода (каскад K→L считается отдельно).
        Console.WriteLine();
        Console.WriteLine("=== 3. Ожидаемое число L-квантов на фотопоглощение при {0} кэВ (Σ lFrac_i·ν_i, доли EPICS2017)",
                          energyKev.ToString("F3", CultureInfo.InvariantCulture));
        Console.WriteLine("  Z  эл  |  lFrac L1    L2    L3  |   ур.0      ур.1      ур.2   | ур.1/0  ур.2/0");
        foreach (int z in zs)
        {
            MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(z);
            MaterialDatabase.PhotoShellModel shells = MaterialDatabase.PhotoShellOf(z);
            if (f == null || !f.HasL || shells == null)
            {
                continue;
            }

            double[] lFrac = shells.LFractions(energyKev);
            if (lFrac == null)
            {
                Console.WriteLine("{0,3}  {1,-3} |  долей подоболочек нет", z, MaterialDatabase.SymbolOf(z));
                continue;
            }

            double[] expect = new double[3];
            for (int level = 0; level < 3; level++)
            {
                if (level == 2 && !ckSupply)
                {
                    expect[level] = double.NaN;
                    continue;
                }

                for (int li = 0; li < 3 && li < lFrac.Length; li++)
                {
                    if (li < f.LEdgeKev.Length && energyKev <= f.LEdgeKev[li])
                    {
                        continue;
                    }

                    expect[level] += lFrac[li] * f.LYield(li, level);
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0,3}  {1,-3} |  {2,6:F4} {3,6:F4} {4,6:F4} | {5,9:E3} {6,9:E3} {7,9} | {8,6:F3}  {9,6}",
                z, MaterialDatabase.SymbolOf(z), lFrac[0], lFrac.Length > 1 ? lFrac[1] : 0.0, lFrac.Length > 2 ? lFrac[2] : 0.0,
                expect[0], expect[1], ckSupply ? expect[2].ToString("E3", CultureInfo.InvariantCulture) : "—",
                expect[0] > 0.0 ? expect[1] / expect[0] : 0.0,
                ckSupply && expect[0] > 0.0 ? (expect[2] / expect[0]).ToString("F3", CultureInfo.InvariantCulture) : "—"));
        }
        if (reached == 0)
        {
            Console.Error.WriteLine("⛔ поставка ω_L не доехала ни до одного элемента — таблица fluorescence_yield (xraylib, L1..L3) не читается.");
            return 1;
        }

        return 0;
    }
}
