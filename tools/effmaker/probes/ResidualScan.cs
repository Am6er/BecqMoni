using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;

// Карта НЕВЯЗКИ разложения: где измерение выше модели и на сколько сигм.
//
// Файл без `Main` — он идёт довеском к пробам, как `GadrasDetector.cs`
// (`build_all.ps1` знает про такие). Заведён 13.08.2026 при разборе V4:
// правило нужно было сразу двум пробам — `FsaCascadeProbe` (один спектр,
// подробно) и `CorpusFsaProbe` (весь корпус), — а второй копии того же
// правила в дереве уже хватило одной беды: S37 ровно про то, как два места,
// считающие одно и то же по своей копии кода, разъезжаются молча.
static class ResidualScan
{
    /// <summary>
    /// Окно, в котором копится невязка, — 16 каналов. Примерно ПШПВ в середине
    /// шкалы у 1024-канального сцинтиллятора; точная ширина не важна, ищем
    /// крупное, а не измеряем площадь.
    /// </summary>
    public const int WindowChannels = 16;

    /// <summary>Одно окно: энергия середины, избыток в отсчётах и в сигмах.</summary>
    public struct Excess
    {
        public double EnergyKev;
        public double Counts;
        public double Sigmas;
    }

    /// <summary>
    /// Превышения измерения над моделью по окнам, от крупного к мелкому.
    /// Сигма — пуассоновская по ИЗМЕРЕННОМУ в окне: модель в знаменателе
    /// давала бы бесконечность там, где её нет вовсе, а это как раз самые
    /// интересные места.
    /// </summary>
    public static List<Excess> Top(EnergySpectrum spectrum, FsaResult result, int top)
    {
        var windows = new List<Excess>();
        if (spectrum == null || result == null || result.Model == null)
        {
            return windows;
        }

        EnergyCalibration calibration = spectrum.EnergyCalibration;
        int[] raw = spectrum.Spectrum;
        if (calibration == null || raw == null)
        {
            return windows;
        }

        for (int lo = result.FirstChannel;
             lo + WindowChannels <= result.LastChannel;
             lo += WindowChannels)
        {
            double measured = 0.0, model = 0.0;
            for (int i = lo; i < lo + WindowChannels && i < raw.Length; i++)
            {
                measured += raw[i];
                model += result.Model[i];
            }

            if (measured < 1.0)
            {
                continue;
            }

            double sigma = Math.Sqrt(Math.Max(measured, 1.0));
            windows.Add(new Excess
            {
                EnergyKev = calibration.ChannelToEnergy(lo + WindowChannels / 2.0),
                Counts = measured - model,
                Sigmas = (measured - model) / sigma
            });
        }

        windows.Sort((x, y) => y.Sigmas.CompareTo(x.Sigmas));
        if (top > 0 && windows.Count > top)
        {
            windows.RemoveRange(top, windows.Count - top);
        }

        return windows;
    }

    /// <summary>Печать той же карты — общая, чтобы и вид не разъехался.</summary>
    public static void Print(EnergySpectrum spectrum, FsaResult result, int top, string indent)
    {
        foreach (Excess e in Top(spectrum, result, top))
        {
            Console.WriteLine("{0}{1,8:F1} кэВ   избыток {2,10:F0} {3,9:F1} сигм",
                              indent, e.EnergyKev, e.Counts, e.Sigmas);
        }
    }

    /// <summary>
    /// Самое крупное превышение ВНУТРИ окна энергий — для вопросов вида
    /// «а что осталось около 460 кэВ» (V4). Возвращает false, если окно
    /// пусто: молчаливый ноль на таком вопросе неотличим от «не мерили».
    /// </summary>
    public static bool Near(EnergySpectrum spectrum, FsaResult result,
                            double fromKev, double toKev, out Excess found)
    {
        found = default(Excess);
        bool any = false;
        foreach (Excess e in Top(spectrum, result, 0))
        {
            if (e.EnergyKev < fromKev || e.EnergyKev > toKev)
            {
                continue;
            }

            if (!any || e.Sigmas > found.Sigmas)
            {
                found = e;
                any = true;
            }
        }

        return any;
    }

    public static string Describe(Excess e)
    {
        return string.Format(CultureInfo.InvariantCulture,
                             "{0:F1} кэВ: {1:F0} отсчётов, {2:F1} сигм",
                             e.EnergyKev, e.Counts, e.Sigmas);
    }
}
