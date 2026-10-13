using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// КЛЮЧ ПАР В ГОТОВОЙ МАТРИЦЕ (полоса П147, 24.09.2026, физика 24; решение Amber
/// 24.09.2026 «ВКЛ в физике 24 (Рекомендую)»): матрица, посчитанная штатным
/// рецептом, обязана нести `XcomPairThreshold` = ВКЛ в своих настройках (хвост
/// `OPTF`), её клеймо — совпадать с клеймом умолчаний и ОТЛИЧАТЬСЯ от клейма
/// абляции `--pairth=0` (иначе ключ в клейме не виден, `T42`).
///
///     pairkeystampprobep147 --matrix=&lt;rmx&gt; --in=&lt;.in&gt;
///
/// Код 0 — всё так; 1 — нет.
/// </summary>
static class PairKeyStampProbeP147
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string rmx = null, inPath = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--matrix=", StringComparison.Ordinal)) rmx = a.Substring(9);
            else if (a.StartsWith("--in=", StringComparison.Ordinal)) inPath = a.Substring(5);
        }

        if (rmx == null || inPath == null || !File.Exists(rmx) || !File.Exists(inPath))
        {
            Console.Error.WriteLine("нужны --matrix= и --in=");
            return 2;
        }

        GlobalConfigManager.GetInstance();
        ResponseMatrix m = ResponseMatrix.Load(rmx);
        GeometryModel g = GeometryModel.Load(inPath);
        var on = new ResponseMatrixOptions();
        var off = new ResponseMatrixOptions { XcomPairThreshold = false };
        string sOn = ResponseMatrix.ComputeStamp(g, on);
        string sOff = ResponseMatrix.ComputeStamp(g, off);
        bool keyInFile = m.Options != null && m.Options.XcomPairThreshold;
        bool ok = on.XcomPairThreshold && keyInFile && m.Stamp == sOn && sOn != sOff;
        Console.WriteLine("{0}: ключ в файле {1}; клеймо файла {2} клейму умолчаний; клеймо абляции --pairth=0 {3} {4}",
                          Path.GetFileName(rmx), keyInFile ? "ВКЛ" : "ВЫКЛ",
                          m.Stamp == sOn ? "РАВНО" : "НЕ равно",
                          sOn != sOff ? "другое" : "ТО ЖЕ", ok ? "✅" : "⛔");
        return ok ? 0 : 1;
    }
}
