// Проба `a305m` (AMBER274, находка критика `critamb56`): строка «Бета-излучение пробы» окна отчёта FSA
// при матрице БЕЗ раздела электронов. Проверяется ТА ЖЕ функция, что рисует окно (`FSAReportView.SampleBetaText`),
// и привязка `FsaMatrixBinding.Bind` (различение сцены `Iso`).
//   --ui=en|ru  язык текста (по умолчанию en)
// Код: 0 — все проверки сошлись, 1 — нет.
using System;
using System.Globalization;
using System.Threading;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;

static class SampleBetaMissingProbeA305m
{
    static int fails;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine("  [{0}] {1}{2}", ok ? "ok" : "ПРОВАЛ", name, detail == null ? "" : " — " + detail);
        if (!ok) fails++;
    }

    static FsaResult R(bool used, bool keyOn, ElectronSectionRefusal refusal)
    {
        return new FsaResult { ResponseMatrixUsed = used, SampleBetaKeyOn = keyOn, SampleBetaRefusal = refusal, SampleBetaCounts = 1234.0 };
    }

    static ElectronSectionRefusal Bound(GeometryModel g)
    {
        var analyzer = new FsaAnalyzer();
        FsaMatrixBinding.Bind(analyzer, g, new ResponseMatrix());   // матрица без раздела электронов
        return analyzer.SampleBetaRefusal;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string ui = "en";
        foreach (string s in args) if (s.StartsWith("--ui=", StringComparison.Ordinal)) ui = s.Substring(5);
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(ui == "ru" ? "ru-RU" : "en-US");
        Console.WriteLine("SampleBetaMissingProbeA305m, ui=" + ui);

        string miss = FSAReportView.SampleBetaText(R(true, true, ElectronSectionRefusal.Missing));
        Console.WriteLine("  матрица без раздела, ключ ВКЛ: " + (miss ?? "<null>"));
        string needle = ui == "ru" ? "пересчитайте матрицу" : "recompute the matrix";
        Check("без раздела, ключ ВКЛ -> строка отказа есть", miss != null && miss.Contains(needle) && !miss.StartsWith("FSAReport_", StringComparison.Ordinal), null);
        Check("без раздела, ключ ВЫКЛ -> строки нет (прежнее)", FSAReportView.SampleBetaText(R(true, false, ElectronSectionRefusal.Missing)) == null, null);
        Check("без матрицы -> строки нет (прежнее)", FSAReportView.SampleBetaText(R(false, true, ElectronSectionRefusal.Missing)) == null, null);

        string ok = FSAReportView.SampleBetaText(R(true, true, ElectronSectionRefusal.None));
        Console.WriteLine("  матрица с годным разделом: " + ok);
        Check("годный раздел -> прежняя строка (с отсчётами 1234)", ok != null && ok.Contains("1234") && !ok.Contains(needle), null);

        // Привязка: у матрицы без раздела причина — Missing; у сцены Iso — IsoScene
        var plain = new GeometryModel { Scene = GeometrySceneKind.None };
        var iso = new GeometryModel { Scene = GeometrySceneKind.Iso };
        ElectronSectionRefusal rp = Bound(plain), ri = Bound(iso);
        Console.WriteLine("  Bind: сцена None -> {0}; сцена Iso -> {1}", rp, ri);
        Check("Bind: обычная сцена без раздела -> Missing", rp == ElectronSectionRefusal.Missing, rp.ToString());
        Check("Bind: сцена Iso без раздела -> IsoScene", ri == ElectronSectionRefusal.IsoScene, ri.ToString());

        string isoText = FSAReportView.SampleBetaText(R(true, true, ri));
        Console.WriteLine("  Iso без раздела, строка: " + (isoText ?? "<null>"));
        Check("сцена Iso без раздела -> отказа «пересчитайте» НЕТ", isoText != null && !isoText.Contains(needle), null);
        // положительный контроль самой пробы: без Bind-различения Iso+Missing дал бы «пересчитайте»
        Check("контроль: Iso, посланная как Missing, ловится как «пересчитайте»", FSAReportView.SampleBetaText(R(true, true, ElectronSectionRefusal.Missing)).Contains(needle), null);

        Console.WriteLine(fails == 0 ? "ИТОГ: все проверки сошлись" : "ИТОГ: провалов " + fails);
        return fails == 0 ? 0 : 1;
    }
}
