using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

/// <summary>
/// E18: подставляет ли форма «Матрица отклика» диапазон КРИВОЙ, когда матрицы
/// ещё нет, и называет ли расхождение, когда матрица есть.
///
/// Зачем проба. Правка живёт в конструкторе формы (`LoadExisting`), и увидеть
/// её иначе как открыв форму руками — нельзя; а ошибка тут молчаливая ровно
/// того сорта, ради которого строка и заведена: человек выставил кривой нижнюю
/// границу 20 кэВ, форма показала умолчание 30, матрица посчиталась в чужом
/// диапазоне, и никто не заметил. Поэтому проверяются ЗНАЧЕНИЯ ПОЛЕЙ, а не то,
/// что код «отработал без исключения».
///
/// Три случая, ровно как в строке:
///   (а) кривая есть, матрицы нет  -> поля обязаны стать краями кривой,
///                                    в подробностях — строка «взят у кривой»;
///   (в) кривой нет вовсе          -> поля обязаны остаться умолчаниями;
///   (б) матрица есть, диапазоны   -> поля обязаны остаться МАТРИЧНЫМИ,
///       разошлись                    а расхождение — быть НАЗВАНО.
///
/// Случай (б) проверяется на `DescribeRangeMismatch` напрямую: класть настоящий
/// `.rmx` в хранилище пользователя проба не имеет права (конфиг BecqMoni —
/// только на чтение), а матрица для него строится честная, но крошечная.
///
/// Сборка — `probes/build_all.ps1`. Запуск без ключей.
/// </summary>
static class MatrixRangeProbe
{
    const double CurveLo = 20.0;
    const double CurveHi = 2000.0;

    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        DeviceType.InitializeDeviceTypes();
        GlobalConfigManager.GetInstance();

        bool ok = true;
        ok &= CaseCurveNoMatrix();
        ok &= CaseNoCurve();
        ok &= CaseMatrixDiffers();

        Console.WriteLine();
        Console.WriteLine(ok ? "СОШЛОСЬ" : "РАЗОШЛОСЬ");
        return ok ? 0 : 1;
    }

    // ----------------------------------------------------------------------
    // (а) кривая есть, матрицы нет — поля обязаны стать краями кривой
    // ----------------------------------------------------------------------
    static bool CaseCurveNoMatrix()
    {
        Console.WriteLine("== (а) кривая 20–2000 кэВ, матрицы нет ==");
        EfficiencyConfigData config = MakeConfig(withCurve: true);
        using (ResponseMatrixForm form = new ResponseMatrixForm(config))
        {
            decimal lo = Box(form, "minEnergyBox").Value;
            decimal hi = Box(form, "maxEnergyBox").Value;
            string details = (string)Field(form, "detailsText");
            Console.WriteLine("   поля      : {0} .. {1} кэВ", lo, hi);
            Console.WriteLine("   подробно  : {0}", Short(details));

            bool ok = lo == (decimal)CurveLo && hi == (decimal)CurveHi;
            bool said = !string.IsNullOrEmpty(details);
            Console.WriteLine("   диапазон подставлен: {0}", ok ? "ДА" : "НЕТ");
            Console.WriteLine("   сказано человеку   : {0}", said ? "ДА" : "НЕТ");
            return ok && said;
        }
    }

    // ----------------------------------------------------------------------
    // (в) кривой нет — поля обязаны остаться умолчаниями разметки
    // ----------------------------------------------------------------------
    static bool CaseNoCurve()
    {
        Console.WriteLine();
        Console.WriteLine("== (в) кривой нет ==");
        EfficiencyConfigData bare = MakeConfig(withCurve: false);
        decimal defLo, defHi;
        using (ResponseMatrixForm form = new ResponseMatrixForm(bare))
        {
            defLo = Box(form, "minEnergyBox").Value;
            defHi = Box(form, "maxEnergyBox").Value;
            string details = (string)Field(form, "detailsText");
            Console.WriteLine("   поля      : {0} .. {1} кэВ (умолчания разметки)", defLo, defHi);
            Console.WriteLine("   подробно  : {0}", details.Length == 0 ? "(пусто)" : Short(details));
            bool ok = defLo != (decimal)CurveLo || defHi != (decimal)CurveHi;
            // Умолчание обязано ОТЛИЧАТЬСЯ от краёв кривой, иначе случай (а)
            // ничего не проверяет: поля совпали бы и без правки.
            Console.WriteLine("   умолчание отличается от краёв кривой: {0}",
                              ok ? "ДА (случай (а) значим)" : "НЕТ — проба слепа!");
            return ok && details.Length == 0;
        }
    }

    // ----------------------------------------------------------------------
    // (б) матрица есть и её диапазон другой — расхождение обязано быть названо
    // ----------------------------------------------------------------------
    static bool CaseMatrixDiffers()
    {
        Console.WriteLine();
        Console.WriteLine("== (б) матрица 30–1000 кэВ против кривой 20–2000 ==");
        EfficiencyConfigData config = MakeConfig(withCurve: true);
        var options = new ResponseMatrixOptions
        {
            MinEnergyKev = 30.0,
            MaxEnergyKev = 1000.0,
            NodeCount = 3,
            BinKev = 20.0,
            Histories = 2000,
            Threads = 1,
        };
        ResponseMatrix matrix = ResponseMatrixBuilder.Build(config.Geometry, options, null,
                                                           System.Threading.CancellationToken.None);
        Console.WriteLine("   матрица   : {0} узлов, {1:F0} .. {2:F0} кэВ",
                          matrix.NodeCount, matrix.Energies[0],
                          matrix.Energies[matrix.NodeCount - 1]);

        using (ResponseMatrixForm form = new ResponseMatrixForm(config))
        {
            string said = (string)typeof(ResponseMatrixForm)
                .GetMethod("DescribeRangeMismatch", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { matrix });
            Console.WriteLine("   сказано   : {0}", Short(said));
            bool named = !string.IsNullOrEmpty(said)
                         && said.Contains("2000") && said.Contains("1000");
            Console.WriteLine("   расхождение названо и числа в нём те: {0}", named ? "ДА" : "НЕТ");

            // И встречная проверка: совпадающие диапазоны молчат.
            var same = new ResponseMatrixOptions
            {
                MinEnergyKev = CurveLo, MaxEnergyKev = CurveHi,
                NodeCount = 3, BinKev = 20.0, Histories = 2000, Threads = 1,
            };
            ResponseMatrix fitting = ResponseMatrixBuilder.Build(config.Geometry, same, null,
                                                                System.Threading.CancellationToken.None);
            string quiet = (string)typeof(ResponseMatrixForm)
                .GetMethod("DescribeRangeMismatch", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { fitting });
            Console.WriteLine("   при совпадении диапазонов молчит: {0}",
                              string.IsNullOrEmpty(quiet) ? "ДА" : "НЕТ — '" + Short(quiet) + "'");
            return named && string.IsNullOrEmpty(quiet);
        }
    }

    // ----------------------------------------------------------------------
    static EfficiencyConfigData MakeConfig(bool withCurve)
    {
        var config = new EfficiencyConfigData();
        config.Guid = Guid.NewGuid().ToString();
        config.Name = "проба E18";

        GeometryModel g = GeometryEditorPanel.Blank();
        GeometryPresets.Preset preset =
            GeometryPresets.Items.FirstOrDefault(p => p.Name == "Gamma-1S UDS-GC 63x63");
        if (preset == null)
        {
            throw new InvalidOperationException("нет пресета «Gamma-1S UDS-GC 63x63»");
        }

        preset.Apply(g);
        g.SourceType = GeometrySourceType.Point;
        g.PointDistance = 50.0;
        config.Geometry = g;

        if (withCurve)
        {
            // Точки НЕ по возрастанию нарочно: края берутся минимумом и
            // максимумом, и проба обязана это подтверждать, а не полагаться на
            // соглашение о порядке.
            var curve = new List<ROIEfficiencyData>();
            foreach (double e in new[] { 662.0, CurveHi, 100.0, CurveLo, 1460.0 })
            {
                curve.Add(new ROIEfficiencyData { Energy = e, Efficiency = 0.01, ErrorPercent = 3.0 });
            }

            config.Curve = curve;
        }

        return config;
    }

    static NumericUpDown Box(object form, string name)
    {
        return (NumericUpDown)Field(form, name);
    }

    static object Field(object target, string name)
    {
        FieldInfo f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null)
        {
            throw new InvalidOperationException("нет поля " + name);
        }

        return f.GetValue(target);
    }

    static string Short(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "(пусто)";
        }

        s = s.Replace(Environment.NewLine, " | ").Replace("\n", " | ");
        return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
    }
}
