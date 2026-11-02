using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace LightModelDumpA272c
{
    /// <summary>
    /// (`AMBER272`, полоса `dbdata2`, 02.11.2026) Входы модели света Пейна для подгонки вне переноса.
    ///
    /// Подгонка параметров GAGG (η, S_Trap, S_Birks) идёт в питоне по коэффициентам
    /// `LightFitP200 --jac=` (свет пика — линейная функция узлов кривой). Чтобы питон строил
    /// кривую ТЕМИ ЖЕ числами, что `MaterialDatabase.LoadLightYieldPayne`, ему нужны ровно
    /// входы кода: тормозная способность вещества счётом ESTAR по составу
    /// (`EstarCalculator.Stopping` по `ElectronData.CompoundByName`), плотность и I. Проба их
    /// печатает (запись «R»), а заодно — узлы кривой кода при параметрах базы и умолчаниях
    /// склада (продолжение ниже 1 кэВ, обрыв `LightTrackEndKev`/`LightTrackEndPower` симулятора):
    /// по ним питон сверяет свою кривую — положительный контроль модели.
    ///
    ///     lightmodeldumpa272c [--material=GAGG]
    ///
    /// Строки вывода: «density ρ», «potential_ev I», «S E_кэВ S_МэВ/см» (collision·ρ, как у
    /// кода), «Y E_кэВ y» — узлы кривой кода, «T E_кэВ y» — узлы кривой без продолжения и
    /// обрыва (форма таблицы `scint_electron_light_yield`).
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string material = "GAGG";
            foreach (string a in args)
            {
                if (a.StartsWith("--material=", StringComparison.Ordinal)) material = a.Substring(11);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            int colon = material.IndexOf(':');
            EstarCalculator.Compound compound = ElectronData.CompoundByName(colon > 0 ? material.Substring(0, colon) : material);
            if (compound == null)
            {
                Console.Error.WriteLine("нет состава вещества: " + material);
                return 3;
            }

            double[] energyMev, collision, radiative;
            double potentialEv;
            EstarCalculator.Stopping(compound, out energyMev, out collision, out radiative, out potentialEv);
            Console.WriteLine("material {0}", material);
            Console.WriteLine("density {0}", compound.DensityGCm3.ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("potential_ev {0}", potentialEv.ToString("R", CultureInfo.InvariantCulture));
            for (int i = 0; i < energyMev.Length; i++)
            {
                Console.WriteLine("S {0} {1}",
                    (energyMev[i] * 1000.0).ToString("R", CultureInfo.InvariantCulture),
                    (collision[i] * compound.DensityGCm3).ToString("R", CultureInfo.InvariantCulture));
            }

            var sim = new EfficiencySimulator(new GeometryModel());
            FieldInfo fe = typeof(MaterialDatabase.LightYieldCurve).GetField("energyKev", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo fy = typeof(MaterialDatabase.LightYieldCurve).GetField("yieldRel", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fe == null || fy == null)
            {
                Console.Error.WriteLine("поля кривой energyKev/yieldRel не найдены отражением — проба устарела");
                return 3;
            }

            Console.WriteLine("track_end_kev {0}", sim.LightTrackEndKev.ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("track_end_power {0}", sim.LightTrackEndPower.ToString("R", CultureInfo.InvariantCulture));
            MaterialDatabase.LightYieldCurve code = MaterialDatabase.LightYieldPayne(material, 0.0, true, sim.LightTrackEndKev, true, sim.LightTrackEndPower);
            MaterialDatabase.LightYieldCurve table = MaterialDatabase.LightYieldPayne(material, 0.0, false, 0.0, true, 2.0);
            if (code == null || table == null)
            {
                Console.Error.WriteLine("у вещества нет кривой Пейна в базе: " + material);
                return 3;
            }

            Console.WriteLine("variant {0}", code.Variant);
            Dump("Y", (double[])fe.GetValue(code), (double[])fy.GetValue(code));
            Dump("T", (double[])fe.GetValue(table), (double[])fy.GetValue(table));
            return 0;
        }

        static void Dump(string tag, double[] e, double[] y)
        {
            for (int i = 0; i < e.Length; i++)
            {
                Console.WriteLine("{0} {1} {2}", tag,
                    e[i].ToString("R", CultureInfo.InvariantCulture),
                    y[i].ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }
}
