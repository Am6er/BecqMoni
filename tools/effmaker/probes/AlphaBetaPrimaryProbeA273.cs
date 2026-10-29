using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;

/// <summary>
/// `AMBER273` (29.10.2026): ЕСТЬ ЛИ В МАТРИЦЕ ОТКЛИКА АЛЬФА И БЕТА — ответ замером.
/// Задача Amber 29.10.2026, дословно: «Проверить, есть ли учет альфа и бета излучения
/// в матрице отклика.» Журнал — `handover/handover-2026-10-29-amber273-alpha-beta-in-matrix.md`.
///
/// Проба ничего не пишет: ни склада, ни базы (базы читаются `Mode=ReadOnly`). Сцена
/// строится ТЕМ ЖЕ симулятором, что у построителя (`ResponseMatrixBuilder.MakeSimulator`
/// отражением, умолчания `ResponseMatrixOptions`), внутренние ходы переноса зовутся
/// отражением — копии переноса здесь нет.
///
/// Части (каждая со своим положительным контролем):
///  1. ПЕРВИЧНАЯ ЧАСТИЦА. Входы истории симулятора (публичные методы с энергией кванта)
///     и виды розыгрыша точки вылета (`Sampler`) — перечнем отражением; затем точки
///     вылета самой сцены: сколько из них лежит В КРИСТАЛЛЕ. Контроль детектора
///     «точка в кристалле»: точки, равномерные в описанном брусе кристалла, обязаны
///     дать долю объёма (π/4 у цилиндра, 1 у бруса).
///  2. ВТОРИЧНЫЕ ЭЛЕКТРОНЫ ВНЕ КРИСТАЛЛА (занос): строка узла 1461 кэВ по каналам,
///     счётчики `CountLayerBorn`/`CountLayerCarries`. Контроль — рычаг абляции
///     `ElectronCarryDetour = 0` («заноса нет»): занесённых обязано стать ноль.
///  3. «ЧТО БЫЛО БЫ» С БЕТОЙ ПРОБЫ: электрон с энергией беты из точек вылета сцены,
///     изотропно, ведётся ТОЙ ЖЕ машинерией заноса (`CarriedElectronDeposit`), что
///     вторичный электрон из пробы. Доля вошедших в кристалл и осевшая энергия —
///     моноэнергетически и спектром беты (разрешённая форма с функцией Ферми; среднее
///     сверяется со средним `nucdb`), на распад — против гамма-канала того же нуклида
///     (`TotalEfficiency`). Контроли: электрон, рождённый ВНУТРИ кристалла, входит
///     всегда; рычаг `ElectronCarryDetour = 0` — не входит никто.
///  4. ОБРАЗЫ FSA: какие строки `decay_radiations` попадают в линии образа
///     (`FsaSampleLibrary.DecayLines`) — G, X, B+ (как 511); A и B- — нет. Контроль:
///     у Na-22 линия 511 есть, у Cs-137 — 661.657. Плюс наличие данных для слоя
///     собственной активности (ASTAR по веществам кристалла, конверсия).
///  5. ИСТОЧНИК В ОБЪЁМЕ КРИСТАЛЛА (`AMBER274`, только с `--labr=`): фотон и электрон,
///     рождённые в объёме кристалла, ведутся теми же `InCrystal`/`ElectronLoss`; событие
///     распада La-138 целиком (β + γ 788.7; захват: γ 1435.8 + релаксация Ba K) против
///     одной гаммы. Контроль: без суммирования линия стоит на энергии гаммы.
///
///     AlphaBetaPrimaryProbeA273 --scene=<a.in> [--scene=<b.in> ...] [--labr=<labr.in>]
///                               [--n=200000] [--seed=7] [--beta=40K,137CS,...] [--gmin=1]
///                               [--only=beta]
///
/// (29.10.2026, разбор «как делать слои альфа и бета») `--beta=` — нуклиды части 3
/// (строки `B-` базы; заряд дочернего ядра — из `nucdb.nuclides`), `--gmin=` — порог
/// выхода гамма-линий сравнения, %; `--only=beta` — только часть 3 по сценам.
///
/// Код 0 — все контроли выполнены; 1 — нарушен контроль или ответ (названо); 2 — ключи,
/// отражение или данные.
/// </summary>
public static class AlphaBetaPrimaryProbeA273
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const double ElectronMassKev = 510.99895;
    const double FineStructure = 1.0 / 137.035999;

    static readonly Type Sim = typeof(EfficiencySimulator);
    static MethodInfo atM, inCrystalM, lossM, carriedM, forgetRayM, ensureM, nextM;
    static FieldInfo sourceF, regionArrayF, isCrystalF, materialF, isBoxF, rInF, rOutF, axF, ayF, zMinF, zMaxF;
    static object birthIsotropic;
    static ulong rng = 0x2545F4914F6CDD1DUL;
    static int failures;
    static string nucdbPath, matdbPath;
    static string[] betaNuclides = { "40K", "137CS", "90SR", "90Y", "176LU" };
    static double gammaMinPercent = 1.0;
    static bool onlyBeta;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var scenes = new List<string>();
        string labr = null;
        int n = 200000;
        int seed = 7;
        foreach (string a in args)
        {
            if (a.StartsWith("--scene=", StringComparison.Ordinal)) scenes.Add(a.Substring(8));
            else if (a.StartsWith("--labr=", StringComparison.Ordinal)) labr = a.Substring(7);
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--beta=", StringComparison.Ordinal)) betaNuclides = a.Substring(7).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            else if (a.StartsWith("--gmin=", StringComparison.Ordinal)) gammaMinPercent = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a == "--only=beta") onlyBeta = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (scenes.Count == 0 && labr == null)
        {
            Console.Error.WriteLine("нужен хотя бы один --scene=<сцена.in> или --labr=<сцена.in>");
            return 2;
        }

        rng ^= (ulong)seed * 0x9E3779B97F4A7C15UL;
        nucdbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        matdbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "matdb.sqlite");
        GlobalConfigManager.GetInstance();
        string bad = Bind();
        if (bad != null)
        {
            Console.Error.WriteLine("⛔ отражение: нет члена " + bad + " — проба устарела против приложения");
            return 2;
        }

        Console.WriteLine("AMBER273: альфа и бета в матрице отклика; историй на замер {0}, зерно {1}", n, seed);
        if (!onlyBeta) PartApi();
        foreach (string scene in scenes)
        {
            Console.WriteLine();
            Console.WriteLine("################ сцена {0}", Path.GetFileNameWithoutExtension(scene));
            if (!onlyBeta)
            {
                PartSource(scene, n, seed);
                PartCarry(scene, n, seed);
            }

            PartBeta(scene, n, seed);
        }

        Console.WriteLine();
        if (!onlyBeta) PartFsa();
        if (labr != null && !onlyBeta)
        {
            Console.WriteLine();
            PartIntrinsic(labr, n, seed);
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ИТОГ: все контроли выполнены" : "ИТОГ: ОТКАЗ, нарушений " + failures);
        return failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ отражение

    static string Bind()
    {
        Type d = typeof(double);
        atM = Sim.GetMethod("At", NP, null, new[] { d, d, d }, null);
        if (atM == null) return "At(x, y, z)";
        inCrystalM = Sim.GetMethod("InCrystal", NP, null, new[] { d, d, d, d, d, d, d, typeof(int) }, null);
        if (inCrystalM == null) return "InCrystal";
        lossM = Sim.GetMethod("ElectronLoss", NP, null, new[] { d, d, d, d, typeof(int) }, null);
        if (lossM == null) return "ElectronLoss(x, y, z, te, depth)";
        carriedM = Sim.GetMethod("CarriedElectronDeposit", NP);
        if (carriedM == null) return "CarriedElectronDeposit";
        forgetRayM = Sim.GetMethod("ForgetRay", NP);
        if (forgetRayM == null) return "ForgetRay";
        ensureM = Sim.GetMethod("EnsureBuilt", NP);
        if (ensureM == null) return "EnsureBuilt";
        sourceF = Sim.GetField("source", NP);
        regionArrayF = Sim.GetField("regionArray", NP);
        if (sourceF == null || regionArrayF == null) return "source / regionArray";
        Type samplerT = Sim.GetNestedType("Sampler", BindingFlags.NonPublic);
        Type regionT = Sim.GetNestedType("Region", BindingFlags.NonPublic);
        Type birthT = Sim.GetNestedType("ElectronBirth", BindingFlags.NonPublic);
        if (samplerT == null || regionT == null || birthT == null) return "Sampler / Region / ElectronBirth";
        nextM = samplerT.GetMethod("Next");
        isCrystalF = regionT.GetField("IsCrystal");
        materialF = regionT.GetField("Material");
        isBoxF = regionT.GetField("IsBox");
        rInF = regionT.GetField("RIn");
        rOutF = regionT.GetField("ROut");
        axF = regionT.GetField("AX");
        ayF = regionT.GetField("AY");
        zMinF = regionT.GetField("ZMin");
        zMaxF = regionT.GetField("ZMax");
        if (nextM == null || isCrystalF == null || materialF == null || zMaxF == null) return "Sampler.Next / Region.*";
        birthIsotropic = Enum.Parse(birthT, "Isotropic");
        return null;
    }

    static EfficiencySimulator Make(string scene, int seed, double kev, int n)
    {
        GeometryModel g = GeometryModel.Load(scene);
        var options = new ResponseMatrixOptions();
        MethodInfo make = typeof(ResponseMatrixBuilder).GetMethod("MakeSimulator", BindingFlags.NonPublic | BindingFlags.Static);
        var sim = (EfficiencySimulator)make.Invoke(null, new object[] { g, options, seed, kev });
        sim.Histories = n;
        ensureM.Invoke(sim, null);
        return sim;
    }

    static object RegionAt(EfficiencySimulator sim, double x, double y, double z)
    {
        return atM.Invoke(sim, new object[] { x, y, z });
    }

    static bool InCrystalVolume(EfficiencySimulator sim, double x, double y, double z)
    {
        object r = RegionAt(sim, x, y, z);
        return r != null && (bool)isCrystalF.GetValue(r);
    }

    static void SourcePoint(EfficiencySimulator sim, out double x, out double y, out double z)
    {
        object src = sourceF.GetValue(sim);
        var a = new object[] { sim, 0.0, 0.0, 0.0 };
        nextM.Invoke(src, a);
        x = (double)a[1];
        y = (double)a[2];
        z = (double)a[3];
    }

    static List<object> CrystalRegions(EfficiencySimulator sim)
    {
        var list = new List<object>();
        foreach (object r in (Array)regionArrayF.GetValue(sim))
        {
            if ((bool)isCrystalF.GetValue(r)) list.Add(r);
        }

        return list;
    }

    static double D(FieldInfo f, object o) { return (double)f.GetValue(o); }

    // ------------------------------------------------------------------ ГСЧ пробы

    static double U()
    {
        rng ^= rng << 13;
        rng ^= rng >> 7;
        rng ^= rng << 17;
        return ((rng >> 11) + 0.5) * (1.0 / 9007199254740992.0);
    }

    static void Iso(out double ux, out double uy, out double uz)
    {
        uz = 2.0 * U() - 1.0;
        double s = Math.Sqrt(Math.Max(0.0, 1.0 - uz * uz)), phi = 2.0 * Math.PI * U();
        ux = s * Math.Cos(phi);
        uy = s * Math.Sin(phi);
    }

    /// <summary>Точка, равномерная в объёме области кристалла (цилиндр, кольцо или брус).</summary>
    static void CrystalPoint(object r, out double x, out double y, out double z)
    {
        z = D(zMinF, r) + (D(zMaxF, r) - D(zMinF, r)) * U();
        if ((bool)isBoxF.GetValue(r))
        {
            x = (2.0 * U() - 1.0) * D(axF, r);
            y = (2.0 * U() - 1.0) * D(ayF, r);
            return;
        }

        double rin = D(rInF, r), rout = D(rOutF, r);
        double rr = Math.Sqrt(rin * rin + (rout * rout - rin * rin) * U()), phi = 2.0 * Math.PI * U();
        x = rr * Math.Cos(phi);
        y = rr * Math.Sin(phi);
    }

    static void Check(bool ok, string what)
    {
        if (!ok)
        {
            failures++;
            Console.WriteLine("   ⛔ НАРУШЕНО: " + what);
        }
    }

    // ------------------------------------------------------------------ часть 1

    static void PartApi()
    {
        Console.WriteLine();
        Console.WriteLine("== 1. Входы истории симулятора (публичные методы с энергией первым параметром)");
        foreach (MethodInfo m in Sim.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            ParameterInfo[] p = m.GetParameters();
            if (p.Length > 0 && (p[0].Name.EndsWith("Kev", StringComparison.Ordinal) || p[0].Name == "energies"))
            {
                var sb = new StringBuilder();
                foreach (ParameterInfo q in p) sb.Append(sb.Length > 0 ? ", " : "").Append(q.ParameterType.Name).Append(' ').Append(q.Name);
                Console.WriteLine("   {0}({1})", m.Name, sb);
            }
        }

        Console.WriteLine("   виды розыгрыша точки вылета (наследники Sampler):");
        Type samplerT = Sim.GetNestedType("Sampler", BindingFlags.NonPublic);
        foreach (Type t in Sim.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (t != samplerT && samplerT.IsAssignableFrom(t)) Console.WriteLine("     " + t.Name);
        }

        Console.WriteLine("   вид частицы входом не задаётся: энергия — энергия КВАНТА (ToCrystal/InCrystal ведут фотон)");
    }

    static void PartSource(string scene, int n, int seed)
    {
        EfficiencySimulator sim = Make(scene, seed, 661.657, n);
        Console.WriteLine("== 1. Точки вылета сцены: источник {0}", sourceF.GetValue(sim).GetType().Name);
        int inCrystal = 0, m = n;
        for (int i = 0; i < m; i++)
        {
            double x, y, z;
            SourcePoint(sim, out x, out y, out z);
            if (InCrystalVolume(sim, x, y, z)) inCrystal++;
        }

        // Контроль детектора: точки, равномерные в описанном брусе кристалла.
        List<object> crystal = CrystalRegions(sim);
        object r0 = crystal[0];
        bool box = (bool)isBoxF.GetValue(r0);
        double hx = box ? D(axF, r0) : D(rOutF, r0), hy = box ? D(ayF, r0) : D(rOutF, r0);
        int hit = 0;
        for (int i = 0; i < m; i++)
        {
            double x = (2.0 * U() - 1.0) * hx, y = (2.0 * U() - 1.0) * hy;
            double z = D(zMinF, r0) + (D(zMaxF, r0) - D(zMinF, r0)) * U();
            if (InCrystalVolume(sim, x, y, z)) hit++;
        }

        double expect = box ? 1.0 : Math.PI / 4.0 * (1.0 - Math.Pow(D(rInF, r0) / D(rOutF, r0), 2));
        double share = (double)hit / m;
        Console.WriteLine("   точек вылета {0}, из них в кристалле {1}", m, inCrystal);
        Console.WriteLine("   контроль: точки в описанном брусе кристалла ({0} областей кристалла) — в кристалле {1:F4}, ожидание {2:F4}",
                          crystal.Count, share, expect);
        Check(inCrystal == 0, "точка вылета пробы лежит в кристалле");
        Check(Math.Abs(share - expect) < 0.01, "детектор «точка в кристалле» не даёт долю объёма");
    }

    // ------------------------------------------------------------------ часть 2

    static void PartCarry(string scene, int n, int seed)
    {
        Console.WriteLine("== 2. Вторичные электроны вне кристалла (фото, комптон, пара в пробе и обвязке), узел 1461 кэВ");
        long[] born = new long[2], carried = new long[2];
        for (int arm = 0; arm < 2; arm++)
        {
            EfficiencySimulator sim = Make(scene, seed, 1460.82, n);
            if (arm == 1) sim.ElectronCarryDetour = 0.0;
            double err;
            sim.ResponseByChannel(1460.82, 2.0, out err);
            born[arm] = sim.CountLayerBorn;
            carried[arm] = sim.CountLayerCarries;
            Console.WriteLine("   {0,-34} рождено вне кристалла и отдано переносу {1,8}, вошло в кристалл {2,8}",
                              arm == 0 ? "как склад (eltr=1)" : "контроль: ElectronCarryDetour = 0", born[arm], carried[arm]);
        }

        Check(carried[0] > 0, "занос вторичных электронов не работает (вошедших ноль)");
        Check(carried[1] == 0, "рычаг абляции заноса не снял занос");
    }

    // ------------------------------------------------------------------ часть 3

    sealed class Beta
    {
        public string Nucid;
        public int DaughterZ;
        public double EndpointKev, MeanKev, Intensity;
        public double[] Grid, Cdf;
    }

    /// <summary>
    /// Заряд дочернего ядра β⁻: элемент из имени нуклида (`234PAm1` → Pa) по
    /// `nucdb.nuclides`, плюс один. Нет элемента — отказ кодом 2 у вызывающего.
    /// </summary>
    static int DaughterZ(SqliteConnection c, string nucid)
    {
        int i = 0;
        while (i < nucid.Length && char.IsDigit(nucid[i])) i++;
        int j = i;
        while (j < nucid.Length && char.IsUpper(nucid[j])) j++;
        string symbol = nucid.Substring(i, j - i);
        using (SqliteCommand cmd = c.CreateCommand())
        {
            cmd.CommandText = "select z from nuclides where upper(symbol) = $s and z is not null limit 1";
            cmd.Parameters.AddWithValue("$s", symbol.ToUpperInvariant());
            object z = cmd.ExecuteScalar();
            if (z == null || z == DBNull.Value) throw new InvalidOperationException("нет элемента " + symbol + " у " + nucid);
            return Convert.ToInt32(z, CultureInfo.InvariantCulture) + 1;
        }
    }

    static List<Beta> BetasOf(string nucid)
    {
        var list = new List<Beta>();
        var b = new SqliteConnectionStringBuilder { DataSource = nucdbPath, Mode = SqliteOpenMode.ReadOnly };
        using (var c = new SqliteConnection(b.ToString()))
        {
            c.Open();
            int daughterZ = DaughterZ(c, nucid);
            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "select energy_num, endpoint, intensity_num from decay_radiations"
                                  + " where parent_nucid = $n and type_a = 'B-' and intensity_num > 0";
                cmd.Parameters.AddWithValue("$n", nucid);
                using (SqliteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string ep = r.IsDBNull(1) ? "" : r.GetString(1).Trim().Trim('(', ')');
                        double e0;
                        if (!double.TryParse(ep, NumberStyles.Float, CultureInfo.InvariantCulture, out e0)) continue;
                        var beta = new Beta
                        {
                            Nucid = nucid, DaughterZ = daughterZ, EndpointKev = e0,
                            MeanKev = r.IsDBNull(0) ? double.NaN : r.GetDouble(0), Intensity = r.GetDouble(2),
                        };
                        BuildSpectrum(beta);
                        list.Add(beta);
                    }
                }
            }
        }

        return list;
    }

    /// <summary>Гамма-линии нуклида из `nucdb` (энергия, выход %) с выходом не ниже порога.</summary>
    static List<double[]> GammasOf(string nucid, double minPercent)
    {
        var list = new List<double[]>();
        var b = new SqliteConnectionStringBuilder { DataSource = nucdbPath, Mode = SqliteOpenMode.ReadOnly };
        using (var c = new SqliteConnection(b.ToString()))
        {
            c.Open();
            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "select energy_num, intensity_num from decay_radiations"
                                  + " where parent_nucid = $n and type_a = 'G' and energy_num not null and intensity_num >= $m";
                cmd.Parameters.AddWithValue("$n", nucid);
                cmd.Parameters.AddWithValue("$m", minPercent);
                using (SqliteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(new[] { r.GetDouble(0), r.GetDouble(1) });
                }
            }
        }

        return list;
    }

    /// <summary>
    /// Разрешённая форма N(T) ∝ p·W·(W0 − W)²·F(Z, W), F — нерелятивистская функция Ферми
    /// 2πη/(1 − e^(−2πη)), η = αZW/p (β⁻). Запрещённые формы (K-40 — уникальный третий,
    /// Cs-137 и Y-90 — уникальный первый) не заводятся: среднее сверяется с `nucdb`.
    /// </summary>
    static void BuildSpectrum(Beta b)
    {
        const int K = 4000;
        b.Grid = new double[K + 1];
        b.Cdf = new double[K + 1];
        double w0 = 1.0 + b.EndpointKev / ElectronMassKev, sum = 0.0, prev = 0.0;
        for (int i = 0; i <= K; i++)
        {
            double t = b.EndpointKev * i / K;
            b.Grid[i] = t;
            double w = 1.0 + t / ElectronMassKev, p = Math.Sqrt(Math.Max(0.0, w * w - 1.0));
            double f = 0.0;
            if (p > 0.0 && w < w0)
            {
                double eta = FineStructure * b.DaughterZ * w / p;
                double fermi = 2.0 * Math.PI * eta / (1.0 - Math.Exp(-2.0 * Math.PI * eta));
                f = p * w * (w0 - w) * (w0 - w) * fermi;
            }

            if (i > 0) sum += 0.5 * (f + prev);
            b.Cdf[i] = sum;
            prev = f;
        }

        for (int i = 0; i <= K; i++) b.Cdf[i] /= sum;
    }

    static double SampleBeta(Beta b)
    {
        double u = U();
        int lo = 0, hi = b.Cdf.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (b.Cdf[mid] < u) lo = mid; else hi = mid;
        }

        double span = b.Cdf[hi] - b.Cdf[lo];
        double f = span > 0.0 ? (u - b.Cdf[lo]) / span : 0.0;
        return b.Grid[lo] + f * (b.Grid[hi] - b.Grid[lo]);
    }

    /// <summary>Один электрон из точки вылета: осевшая в кристалле энергия (0 — не вошёл).</summary>
    static double CarryOne(EfficiencySimulator sim, double te, bool fromCrystal, ref long bremPhotons)
    {
        double x, y, z;
        if (fromCrystal)
        {
            CrystalPoint(CrystalRegions(sim)[0], out x, out y, out z);
        }
        else
        {
            SourcePoint(sim, out x, out y, out z);
        }

        object region = RegionAt(sim, x, y, z);
        GeometryMaterial material = region != null ? (GeometryMaterial)materialF.GetValue(region) : null;
        long count = 0;
        Action<double, double, double, double, double, double, double> push =
            (px, py, pz, ux, uy, uz, e) => { count++; };
        forgetRayM.Invoke(sim, null);
        double deposit = (double)carriedM.Invoke(sim, new object[]
        {
            x, y, z, birthIsotropic, te, 0.0, 0.0, 0.0, material, push,
        });
        bremPhotons += count;
        return deposit;
    }

    static void PartBeta(string scene, int n, int seed)
    {
        Console.WriteLine("== 3. «Что было бы»: бета пробы, ведомая машинерией заноса (CarriedElectronDeposit), изотропно из точек вылета");
        EfficiencySimulator sim = Make(scene, seed, 661.657, n);
        int m = n;
        Console.WriteLine("   моноэнергетически, {0} электронов на энергию: доля вошедших в кристалл, средняя осевшая у вошедших", m);
        double first = -1.0, last = -1.0;
        foreach (double t in new[] { 100.0, 200.0, 300.0, 400.0, 500.0, 700.0, 1000.0, 1500.0, 2000.0, 2500.0 })
        {
            long brem = 0;
            int entered = 0;
            double dep = 0.0;
            for (int i = 0; i < m; i++)
            {
                double d = CarryOne(sim, t, false, ref brem);
                if (d > 0.0) { entered++; dep += d; }
            }

            double share = (double)entered / m;
            if (first < 0.0) first = share;
            last = share;
            Console.WriteLine("     T {0,6:F0} кэВ: вошло {1,9:F6} (ст. ош. {2:F6}), осело {3,7:F1} кэВ, квантов тормозного на электрон {4:F5}",
                              t, share, Math.Sqrt(share * (1.0 - share) / m), entered > 0 ? dep / entered : 0.0, (double)brem / m);
        }

        Check(last >= first, "доля вошедших убывает с энергией электрона");

        // Контроли машинерии: электрон, рождённый ВНУТРИ кристалла, «входит» всегда;
        // рычаг абляции заноса — не входит никто.
        {
            long brem = 0;
            int entered = 0;
            for (int i = 0; i < 20000; i++) if (CarryOne(sim, 1000.0, true, ref brem) > 0.0) entered++;
            // ⚠ Не ровно 1: электрон, рождённый у самой грани и летящий наружу, уходит
            // с полной энергией — доли процента, физика, а не отказ машинерии.
            Console.WriteLine("   контроль: электрон 1000 кэВ, рождённый в кристалле — вошло {0:F4} (ожидание ≈ 1)", entered / 20000.0);
            Check(entered >= 0.99 * 20000, "электрон в кристалле не засчитан");
            EfficiencySimulator off = Make(scene, seed, 661.657, n);
            off.ElectronCarryDetour = 0.0;
            entered = 0;
            for (int i = 0; i < 20000; i++) if (CarryOne(off, 2500.0, false, ref brem) > 0.0) entered++;
            Console.WriteLine("   контроль: ElectronCarryDetour = 0, 2500 кэВ — вошло {0}", entered);
            Check(entered == 0, "рычаг абляции заноса не снял занос беты");
        }

        Console.WriteLine("   спектром беты (разрешённая форма; среднее против nucdb), на распад — против гамма-канала:");
        foreach (string nucid in betaNuclides)
        {
            List<Beta> betas = BetasOf(nucid);
            double perDecayIn = 0.0, perDecay30 = 0.0, perDecay300 = 0.0;
            double meanSampledW = 0.0, meanBaseW = 0.0;
            foreach (Beta b in betas)
            {
                double mean = 0.0;
                int entered = 0, above30 = 0, above300 = 0;
                long brem = 0;
                for (int i = 0; i < m; i++)
                {
                    double t = SampleBeta(b);
                    mean += t;
                    double d = CarryOne(sim, t, false, ref brem);
                    if (d > 0.0) entered++;
                    if (d > 30.0) above30++;
                    if (d > 300.0) above300++;
                }

                mean /= m;
                double dev = mean / b.MeanKev - 1.0;
                Console.WriteLine("     {0} ветвь {1:F2} %, E0 {2:F1} кэВ: среднее {3:F1} против nucdb {4:F1} ({5:+0.0;-0.0} %); вошло {6:F5}, осело > 30 кэВ {7:F5}, > 300 кэВ {8:F5}",
                                  nucid, b.Intensity, b.EndpointKev, mean, b.MeanKev, 100.0 * dev,
                                  (double)entered / m, (double)above30 / m, (double)above300 / m);
                meanSampledW += b.Intensity * mean;
                meanBaseW += b.Intensity * b.MeanKev;
                perDecayIn += b.Intensity / 100.0 * entered / m;
                perDecay30 += b.Intensity / 100.0 * above30 / m;
                perDecay300 += b.Intensity / 100.0 * above300 / m;
            }

            // ⚠ Контроль розыгрыша — по СРЕДНЕВЗВЕШЕННОМУ среднему нуклида: у отдельной
            // слабой ветви разрешённая форма уходит дальше (Ac-228, ветвь 0.59 %: +17 %),
            // и это свойство формы, а не отказ розыгрыша; по ветвям — печать выше.
            double devW = meanBaseW > 0.0 ? meanSampledW / meanBaseW - 1.0 : 0.0;
            Console.WriteLine("     {0}: среднее беты по ветвям (вес — выход) против nucdb {1:+0.0;-0.0} %", nucid, 100.0 * devW);
            Check(Math.Abs(devW) < 0.15, nucid + ": средневзвешенное разыгранной беты дальше 15 % от nucdb");

            // Гамма-канал того же нуклида: линии `G` базы с выходом не ниже `--gmin=` (умолчание 1 %), регистрация
            // (полная эффективность) и пик — тем же симулятором сцены.
            double gamma = 0.0, gammaPeak = 0.0;
            var used = new StringBuilder();
            foreach (double[] line in GammasOf(nucid, gammaMinPercent))
            {
                EfficiencySimulator g = Make(scene, seed, line[0], n);
                double err;
                gamma += line[1] / 100.0 * g.TotalEfficiency(line[0], out err);
                gammaPeak += line[1] / 100.0 * g.Efficiency(line[0], out err);
                used.Append(used.Length > 0 ? ", " : "").Append(line[0].ToString("F2", CultureInfo.InvariantCulture));
            }

            Console.WriteLine("     {0}: на распад бета в кристалле {1:E3} (> 30 кэВ {2:E3}, > 300 кэВ {3:E3}); гамма [{4}] кэВ — регистраций {5:E3}, в пике {6:E3}; бета/гамма (> 30 кэВ) {7}",
                              nucid, perDecayIn, perDecay30, perDecay300, used.Length > 0 ? used.ToString() : "нет", gamma, gammaPeak,
                              gamma > 0.0 ? (perDecay30 / gamma).ToString("F3", CultureInfo.InvariantCulture) : "—");
        }
    }

    // ------------------------------------------------------------------ часть 4

    static void PartFsa()
    {
        Console.WriteLine("== 4. Образы FSA: строки decay_radiations против линий образа (FsaSampleLibrary.DecayLines)");
        var b = new SqliteConnectionStringBuilder { DataSource = nucdbPath, Mode = SqliteOpenMode.ReadOnly };
        using (var c = new SqliteConnection(b.ToString()))
        {
            c.Open();
            foreach (string nucid in new[] { "137CS", "22NA", "40K", "90Y", "138LA", "176LU", "227AC", "241AM" })
            {
                var byType = new SortedDictionary<string, int>(StringComparer.Ordinal);
                double alphaMax = 0.0;
                using (SqliteCommand cmd = c.CreateCommand())
                {
                    cmd.CommandText = "select type_a, energy_num from decay_radiations where parent_nucid = $n and intensity_num > 0";
                    cmd.Parameters.AddWithValue("$n", nucid);
                    using (SqliteDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            string t = r.IsDBNull(0) ? "?" : r.GetString(0);
                            int k;
                            byType.TryGetValue(t, out k);
                            byType[t] = k + 1;
                            if (t == "A" && !r.IsDBNull(1)) alphaMax = Math.Max(alphaMax, r.GetDouble(1));
                        }
                    }
                }

                var report = new FsaSampleLibrary.Report();
                List<double[]> lines = FsaSampleLibrary.DecayLines(nucid, report);
                double top = 0.0, at511 = 0.0, at662 = 0.0;
                foreach (double[] l in lines)
                {
                    top = Math.Max(top, l[0]);
                    if (Math.Abs(l[0] - 511.0) < 0.1) at511 += l[1];
                    if (Math.Abs(l[0] - 661.657) < 0.05) at662 += l[1];
                }

                var sb = new StringBuilder();
                foreach (KeyValuePair<string, int> kv in byType) sb.Append(kv.Key).Append(' ').Append(kv.Value).Append("; ");
                Console.WriteLine("   {0,-6} в базе: {1}линий образа {2}, самая жёсткая {3:F1} кэВ, у 511 {4:F2} %{5}",
                                  nucid, sb, lines.Count, top, at511,
                                  alphaMax > 0.0 ? string.Format(CultureInfo.InvariantCulture, "; альфа базы до {0:F0} кэВ — в образе нет", alphaMax) : "");
                Check(top < 3000.0 || alphaMax == 0.0, nucid + ": линия образа выше 3 МэВ (альфа попала в образ?)");
                if (nucid == "22NA") Check(at511 > 150.0, "22NA: нет аннигиляционной линии 511 из строки B+");
                if (nucid == "137CS") Check(at662 > 80.0, "137CS: нет линии 661.657");
            }
        }

        // Данные, на которых стоял бы слой собственной активности (`AMBER274`).
        var mb = new SqliteConnectionStringBuilder { DataSource = matdbPath, Mode = SqliteOpenMode.ReadOnly };
        using (var c = new SqliteConnection(mb.ToString()))
        {
            c.Open();
            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "select m.name, count(*) from star_stopping_powers s join star_materials m"
                                  + " on m.star_id = s.material_star_id where s.particle = 'alpha'"
                                  + " and (m.name like '%IODIDE%' or m.name like '%BROMIDE%' or m.name like '%LANTHAN%'"
                                  + " or m.name like '%CERI%' or m.name like '%LUTET%' or m.name like '%GERMANATE%')"
                                  + " group by m.name order by m.name";
                var sb = new StringBuilder();
                using (SqliteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read()) sb.Append(r.GetString(0)).Append(" (").Append(r.GetInt64(1)).Append(") ");
                }

                Console.WriteLine("   matdb ASTAR (пробег альф) по веществам кристаллов: {0}", sb.Length > 0 ? sb.ToString() : "нет ни одного");
                cmd.CommandText = "select count(*) from icc_coefficients";
                Console.WriteLine("   matdb icc_coefficients (коэффициенты конверсии): {0} строк; строк E/AU (конверсионные, оже) в decay_radiations — нет (типы выше)",
                                  Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture));
            }
        }
    }

    // ------------------------------------------------------------------ часть 5

    static double PhotonDeposit(EfficiencySimulator sim, double x, double y, double z, double kev)
    {
        double ux, uy, uz;
        Iso(out ux, out uy, out uz);
        forgetRayM.Invoke(sim, null);
        double escaped = (double)inCrystalM.Invoke(sim, new object[] { x, y, z, ux, uy, uz, kev, 0 });
        return kev - escaped;
    }

    static double ElectronDeposit(EfficiencySimulator sim, double x, double y, double z, double te)
    {
        forgetRayM.Invoke(sim, null);
        double escaped = (double)lossM.Invoke(sim, new object[] { x, y, z, te, 0 });
        return te - escaped;
    }

    static void PartIntrinsic(string scene, int n, int seed)
    {
        Console.WriteLine("== 5. Источник в объёме кристалла (AMBER274): сцена {0}", Path.GetFileNameWithoutExtension(scene));
        EfficiencySimulator sim = Make(scene, seed, 1435.795, n);
        object crystal = CrystalRegions(sim)[0];
        int m = n;

        // 5а. Фотон из объёма кристалла: доля полного поглощения.
        double fep32 = 0.0, fep1436 = 0.0;
        foreach (double kev in new[] { 32.193, 306.78, 788.742, 1435.795 })
        {
            int full = 0;
            for (int i = 0; i < m; i++)
            {
                double x, y, z;
                CrystalPoint(crystal, out x, out y, out z);
                if (PhotonDeposit(sim, x, y, z, kev) >= kev - 0.5) full++;
            }

            double share = (double)full / m;
            if (kev < 40.0) fep32 = share;
            if (kev > 1400.0) fep1436 = share;
            Console.WriteLine("   фотон {0,8:F3} кэВ из объёма: полное поглощение {1:F4}", kev, share);
        }

        Check(fep32 > 0.9 && fep32 > fep1436, "фотон из объёма: мягкий не поглощается почти целиком или не отличается от жёсткого");

        // 5б. Бета La-138 в кристалле: доля энергии, оставшаяся в кристалле.
        Beta la = null;
        foreach (Beta b in BetasOf("138LA")) la = b;
        double sumT = 0.0, sumDep = 0.0;
        int leak = 0;
        for (int i = 0; i < m; i++)
        {
            double x, y, z;
            CrystalPoint(crystal, out x, out y, out z);
            double t = SampleBeta(la);
            double d = ElectronDeposit(sim, x, y, z, t);
            sumT += t;
            sumDep += d;
            if (d < t - 0.5) leak++;
        }

        Console.WriteLine("   бета La-138 (E0 {0:F0} кэВ) из объёма: осело {1:F5} энергии, с утечкой > 0.5 кэВ {2:F5} событий",
                          la.EndpointKev, sumDep / sumT, (double)leak / m);

        // 5в. Событие распада целиком: ветвь β (34.5 %) — бета + γ 788.742 из одной точки;
        // ветвь захвата (65.5 %) — γ 1435.795 + релаксация K-вакансии Ba. Доли K-рентгена —
        // из nucdb (KA1, KA2, KB), ω_K(Ba) = 0.902 и энергии связи — числа пробы, не базы.
        double kx = 0.0;
        var xKev = new List<double>();
        var xI = new List<double>();
        var b2 = new SqliteConnectionStringBuilder { DataSource = nucdbPath, Mode = SqliteOpenMode.ReadOnly };
        using (var c = new SqliteConnection(b2.ToString()))
        {
            c.Open();
            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "select energy_num, intensity_num from decay_radiations where parent_nucid = '138LA'"
                                  + " and type_a = 'X' and type_c in ('KA1', 'KA2', 'KB')";
                using (SqliteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read()) { xKev.Add(r.GetDouble(0)); xI.Add(r.GetDouble(1)); kx += r.GetDouble(1); }
                }
            }
        }

        const double ecShare = 65.5, bindK = 37.441, bindL = 5.989, omegaK = 0.902;
        double pX = kx / ecShare, pK = Math.Min(1.0, pX / omegaK);
        Console.WriteLine("   захват: K-рентген на захват {0:F3} (nucdb), K-захват {1:F3} при ω_K = {2:F3}", pX, pK, omegaK);

        double[] line789Arm = new double[2];
        for (int arm = 0; arm < 2; arm++)
        {
            bool summing = arm == 0;
            var hist = new double[3001];
            for (int i = 0; i < m; i++)
            {
                double x, y, z;
                CrystalPoint(crystal, out x, out y, out z);
                double dep;
                if (U() < 0.345)
                {
                    dep = PhotonDeposit(sim, x, y, z, 788.742);
                    if (summing) dep += ElectronDeposit(sim, x, y, z, SampleBeta(la));
                }
                else
                {
                    dep = PhotonDeposit(sim, x, y, z, 1435.795);
                    if (summing)
                    {
                        double u = U();
                        if (u < pX)
                        {
                            double pick = U() * kx, acc = 0.0, e = xKev[xKev.Count - 1];
                            for (int k = 0; k < xKev.Count; k++) { acc += xI[k]; if (pick < acc) { e = xKev[k]; break; } }
                            dep += PhotonDeposit(sim, x, y, z, e) + (bindK - e);
                        }
                        else
                        {
                            dep += u < pK ? bindK : bindL;
                        }
                    }
                }

                int bin = (int)Math.Floor(dep);
                if (bin >= 0 && bin < hist.Length) hist[bin] += 1.0 / m;
            }

            int peak = 1400;
            for (int k = 1400; k < 1500; k++) if (hist[k] > hist[peak]) peak = k;
            double line789 = 0.0, cont = 0.0;
            for (int k = 786; k <= 790; k++) line789 += hist[k];
            for (int k = 800; k < 1060; k++) cont += hist[k];
            Console.WriteLine("   {0}: максимум у 1400–1500 кэВ в бине {1}–{2} кэВ; доля в окне 786–791 кэВ {3:F5}; в 800–1060 кэВ {4:F5}",
                              summing ? "событие целиком (β+γ, γ+Ba K)" : "контроль: одна гамма", peak, peak + 1, line789, cont);
            line789Arm[arm] = line789;
            if (summing)
            {
                Check(Math.Abs(peak + 0.5 - (1435.795 + bindK)) < 2.0, "сумма захвата: пик не у 1435.8 + 37.4 кэВ");
            }
            else
            {
                Check(Math.Abs(peak + 0.5 - 1435.795) < 1.0, "контроль: пик одной гаммы не у 1435.8 кэВ");
                Check(line789 > 0.005, "контроль: линии 788.7 нет без суммирования");
            }
        }

        // ⚠ Не ноль: бета с T < 2 кэВ (функция Ферми у β⁻ конечна при T → 0) оставляет
        // событие в окне линии. Мерило — во сколько раз линия гаснет против одной гаммы.
        double ratio = line789Arm[0] / Math.Max(line789Arm[1], 1e-300);
        Console.WriteLine("   линия 788.7 с суммированием / без: {0:F4}", ratio);
        Check(ratio < 0.1, "сумма β-ветви: линия 788.7 погасла меньше чем вдесятеро");
    }
}
