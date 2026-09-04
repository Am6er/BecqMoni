using BecquerelMonitor;
using BecquerelMonitor.Utils;
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace CalibrationDoorsProbe
{
    /// <summary>
    /// СТОРОЖ ДВУХ ДВЕРЕЙ, ГДЕ КАЛИБРОВКИ МОЖЕТ НЕ ОКАЗАТЬСЯ (`A111`, `A114`).
    ///
    /// ЗАЧЕМ. После `A95` снимок спектра (<c>EnergySpectrum.Clone</c>) на
    /// спектре без энергетической калибровки НАЗЫВАЕТ причину и отказывает.
    /// Это обнажило две двери, которые такой спектр выдают наружу:
    ///
    ///   `A111` — <c>DCControlPanel.RebuildSpectrum</c> создаёт новый спектр и
    ///            калибровку ему не ставит; чинил её единственный вызывающий
    ///            двумя операторами позже. Метод <c>public</c>: второй
    ///            вызывающий получил бы спектр, который не снимается копией.
    ///   `A114` — <c>SpectrumAriphmetics.CutoffSpectrumChannels</c> приводит
    ///            калибровку жёстким <c>(PolynomialEnergyCalibration)</c> и
    ///            отдаёт её копирующему конструктору без проверки: на
    ///            отсутствующей калибровке это БЕЗЫМЯННЫЙ NRE, на неполиномиальной
    ///            — <c>InvalidCastException</c>.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — ВНЕШНИЙ И ВНУТРЕННИЙ.
    ///
    ///   * ВНЕШНИЙ: та же проба на СТАРОЙ сборке ОБЯЗАНА отказать. Там
    ///     <c>RebuildSpectrum</c> отдаёт спектр без калибровки (снимок его
    ///     бросает), а <c>CutoffSpectrumChannels</c> молчит пустым NRE. Проба,
    ///     довольная обеими сборками, не меряла бы ничего.
    ///   * ВНУТРЕННИЙ: судья «названа ли причина» прогоняется по заведомо
    ///     БЕЗЫМЯННОМУ сообщению настоящего NRE и обязан сказать «нет».
    ///
    /// ⚠ ЧИСЛА ТОЖЕ СВЕРЯЮТСЯ, а не только отказы: рабочий путь обеих дверей
    /// (калибровка на месте, полиномиальная) обязан дать те же отсчёты, ту же
    /// ширину канала и те же коэффициенты. Названный отказ, который заодно
    /// сдвинул числа, — не починка.
    ///
    ///     calibrationdoorsprobe
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0.
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            Rebuild();
            Cutoff();
            Control();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 1. `A111` — RebuildSpectrum
        // ------------------------------------------------------------------

        static void Rebuild()
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. A111: спектр из RebuildSpectrum снимается копией ===");

            ResultData rd = Sample();
            long before = rd.EnergySpectrum.Spectrum.Sum(x => (long)x);
            double keep = rd.EnergySpectrum.MeasurementTime;

            object panel = Panel();
            MethodInfo rebuild = typeof(DCControlPanel).GetMethod("RebuildSpectrum",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (rebuild == null)
            {
                Say("метод RebuildSpectrum найден", false, "его нет в сборке");
                return;
            }

            try
            {
                rebuild.Invoke(panel, new object[] { rd });
            }
            catch (TargetInvocationException ex)
            {
                Say("RebuildSpectrum отработал", false, Named(ex.InnerException));
                return;
            }

            EnergySpectrum built = rd.EnergySpectrum;

            // ЧИСЛА. Гистограмма собрана из импульсов, а не унаследована.
            long counts = built.Spectrum.Sum(x => (long)x);
            Say("отсчёты собраны из импульсов: " + counts, counts == 7,
                "ждали 7, было до пересборки " + before);
            Say("время измерения перенесено", Math.Abs(built.MeasurementTime - keep) < 1e-12,
                built.MeasurementTime.ToString(CultureInfo.InvariantCulture));
            Say("ширина канала взята у прибора",
                Math.Abs(built.ChannelPitch - rd.DeviceConfig.ChannelPitch) < 1e-12,
                built.ChannelPitch.ToString(CultureInfo.InvariantCulture));
            Say("число каналов взято у прибора",
                built.NumberOfChannels == rd.DeviceConfig.NumberOfChannels,
                built.NumberOfChannels.ToString(CultureInfo.InvariantCulture));

            // ГЛАВНОЕ. Спектр обязан сниматься копией — это и есть `A111`.
            Say("калибровка у нового спектра ЕСТЬ", built.EnergyCalibration != null,
                "EnergyCalibration == null: спектр не снимется копией");
            if (built.EnergyCalibration != null)
            {
                Say("калибровка та же, что у прибора",
                    built.EnergyCalibration.Equals(rd.DeviceConfig.EnergyCalibration),
                    "калибровка чужая — шкала подменена");
            }

            try
            {
                EnergySpectrum copy = built.Clone();
                Say("снимок спектра прошёл", copy != null, "снимок пуст");
            }
            catch (Exception ex)
            {
                Say("снимок спектра прошёл", false, Named(ex));
            }

            // ОТКАЗ ИМЕНЕМ, когда брать калибровку неоткуда.
            ResultData blind = Sample();
            blind.DeviceConfig.EnergyCalibration = null;
            Exception refusal = Throws(rebuild, panel, blind);
            Say("без калибровки у прибора дверь ОТКАЗЫВАЕТ", refusal != null,
                "дверь промолчала и отдала спектр без шкалы");
            if (refusal != null)
            {
                Say("отказ НАЗЫВАЕТ причину: " + Named(refusal),
                    Names(refusal, "RebuildSpectrum", "EnergyCalibration"),
                    "причина не названа — это тот же безымянный отказ");
            }
        }

        static Exception Throws(MethodInfo m, object target, ResultData rd)
        {
            try
            {
                m.Invoke(target, new object[] { rd });
                return null;
            }
            catch (TargetInvocationException ex)
            {
                return ex.InnerException;
            }
        }

        /// <summary>
        /// <c>DCControlPanel</c> — это <c>UserControl</c>, и его конструктор
        /// требует <c>MainForm</c> со всеми менеджерами. Поднимать окно пробе
        /// нельзя, поэтому берётся НЕИНИЦИАЛИЗИРОВАННЫЙ экземпляр: измеряемый
        /// метод не трогает ни одного поля <c>this</c>, и это видно по его
        /// телу — он работает только с переданным <c>ResultData</c>.
        /// </summary>
        static object Panel()
        {
            return FormatterServices.GetUninitializedObject(typeof(DCControlPanel));
        }

        static ResultData Sample()
        {
            ResultData rd = new ResultData();
            rd.DeviceConfig = new DeviceConfigInfo();
            rd.DeviceConfig.NumberOfChannels = 1024;
            rd.DeviceConfig.ChannelPitch = 0.04;
            PolynomialEnergyCalibration cal = new PolynomialEnergyCalibration();
            cal.Coefficients = new double[] { 1.5, 2.75, 0.0 };
            rd.DeviceConfig.EnergyCalibration = cal;

            EnergySpectrum old = new EnergySpectrum(0.08, 512);
            old.MeasurementTime = 123.5;
            old.TotalPulseCount = 7L;
            old.NumberOfSamples = 4242L;
            old.Spectrum[10] = 99;

            rd.EnergySpectrum = old;
            for (int i = 0; i < 7; i++)
            {
                rd.PulseCollection.Pulses.Add(new Pulse { Height = 0.4 + 0.4 * i, Time = i });
            }
            return rd;
        }

        // ------------------------------------------------------------------
        // 2. `A114` — CutoffSpectrumChannels
        // ------------------------------------------------------------------

        static void Cutoff()
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. A114: CutoffSpectrumChannels называет причину ===");

            // РАБОЧИЙ ПУТЬ — числа обязаны остаться теми же.
            EnergySpectrum good = new EnergySpectrum(0.04, 64);
            PolynomialEnergyCalibration cal = new PolynomialEnergyCalibration();
            cal.Coefficients = new double[] { 3.0, 1.25, 0.0 };
            good.EnergyCalibration = cal;
            for (int i = 0; i < 64; i++)
            {
                good.Spectrum[i] = i;
            }
            good.MeasurementTime = 60.0;
            good.NumberOfSamples = 11L;

            EnergySpectrum cut = null;
            try
            {
                cut = SpectrumAriphmetics.CutoffSpectrumChannels(good, 32);
            }
            catch (Exception ex)
            {
                Say("рабочий путь двери прошёл", false, Named(ex));
            }
            if (cut != null)
            {
                long sum = cut.Spectrum.Sum(x => (long)x);
                Say("отсчёты после обрезки: " + sum, sum == 496, "ждали 496 (сумма 0..31)");
                Say("ширина канала сохранена", Math.Abs(cut.ChannelPitch - 0.04) < 1e-12,
                    cut.ChannelPitch.ToString(CultureInfo.InvariantCulture));
                PolynomialEnergyCalibration got = cut.EnergyCalibration as PolynomialEnergyCalibration;
                bool same = got != null && got.Coefficients.Length == cal.Coefficients.Length;
                if (same)
                {
                    for (int i = 0; i < got.Coefficients.Length; i++)
                    {
                        same &= Math.Abs(got.Coefficients[i] - cal.Coefficients[i]) < 1e-12;
                    }
                }
                Say("коэффициенты калибровки те же", same, "калибровка уехала");
            }

            // ОТКАЗ 1: калибровки нет вовсе — прежде безымянный NRE.
            EnergySpectrum blind = new EnergySpectrum(0.04, 64);
            Exception no = Catch(blind);
            Say("без калибровки дверь ОТКАЗЫВАЕТ", no != null, "дверь промолчала");
            if (no != null)
            {
                Console.WriteLine("    отказ: {0}", Named(no));
                bool named = Names(no, "CutoffSpectrumChannels", "EnergyCalibration");
                Say("отказ НАЗЫВАЕТ причину", named,
                    no is NullReferenceException
                        ? "СТАРАЯ СБОРКА: пустой NullReferenceException, причина не названа"
                        : "причина не названа");
            }

            // ОТКАЗ 2: калибровка есть, но НЕ полиномиальная — прежде
            // InvalidCastException. Здесь берётся СВОЙ наследник
            // `EnergyCalibration`, а не приложенческий: класс нелинейной
            // калибровки объявлен мёртвым и трогать его нельзя.
            EnergySpectrum other = new EnergySpectrum(0.04, 64);
            other.EnergyCalibration = new ProbeCalibration();
            Exception cast = Catch(other);
            Say("на неполиномиальной калибровке дверь ОТКАЗЫВАЕТ", cast != null, "дверь промолчала");
            if (cast != null)
            {
                Console.WriteLine("    отказ: {0}", Named(cast));
                Say("отказ НАЗЫВАЕТ причину",
                    Names(cast, "CutoffSpectrumChannels", "ProbeCalibration"),
                    cast is InvalidCastException
                        ? "СТАРАЯ СБОРКА: голый InvalidCastException, вида калибровки не назвал"
                        : "причина не названа");
            }
        }

        static Exception Catch(EnergySpectrum s)
        {
            try
            {
                SpectrumAriphmetics.CutoffSpectrumChannels(s, 32);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>
        /// Наследник <c>EnergyCalibration</c>, живущий ТОЛЬКО в пробе: нужен,
        /// чтобы проверить ветку «калибровка не та» и при этом не касаться
        /// <c>NonlinearEnergyCalibration</c>.
        /// </summary>
        sealed class ProbeCalibration : EnergyCalibration
        {
            public override double ChannelToEnergy(double n) { return n; }
            public override double EnergyToChannel(double e, int maxChannels = 10000) { return e; }
            public override EnergyCalibration Clone() { return new ProbeCalibration(); }
            public override EnergyCalibration Downgrade(int p) { return new ProbeCalibration(); }
            public override bool Equals(EnergyCalibration calib) { return calib is ProbeCalibration; }
            public override int MaxChannels() { return 10000; }
        }

        // ------------------------------------------------------------------
        // 3. ВНУТРЕННИЙ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ
        // ------------------------------------------------------------------

        /// <summary>
        /// Судья «названа ли причина» обязан отказать настоящему безымянному
        /// NRE: судья, довольный чем угодно, не судит ничего.
        /// </summary>
        static void Control()
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. положительный контроль судьи ===");
            Exception bare;
            try
            {
                object nothing = null;
                nothing.ToString();
                bare = null;
            }
            catch (NullReferenceException ex)
            {
                bare = ex;
            }
            Say("настоящий NRE пойман", bare != null, "не бросился");
            if (bare != null)
            {
                Console.WriteLine("    безымянное сообщение: {0}", Named(bare));
                Say("судья говорит про НЕГО «причина не названа»",
                    !Names(bare, "CutoffSpectrumChannels", "EnergyCalibration"),
                    "судья принимает что угодно");
            }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Причина словами — ТОЙ ЖЕ дверью, что и всё приложение
        /// (<c>AppUi.Reason</c>): второго соглашения о том, как называется
        /// причина, заводить нельзя.
        /// </summary>
        static string Named(Exception ex)
        {
            return AppUi.Reason(ex);
        }

        static bool Names(Exception ex, params string[] words)
        {
            if (ex == null)
            {
                return false;
            }
            string text = AppUi.Reason(ex);
            foreach (string w in words)
            {
                if (text.IndexOf(w, StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        static void Say(string what, bool ok, string why)
        {
            Console.WriteLine("{0} {1}{2}", ok ? "  ok " : "  НЕТ", what, ok ? "" : " — " + why);
            if (!ok)
            {
                bad++;
            }
        }
    }
}
