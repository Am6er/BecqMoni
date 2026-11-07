using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER303`, читательская часть `AMBER292`) ПРИЁМКА ЧИТАТЕЛЯ GDML: незнакомое и
    /// нечитаемое — строкой сводки и «не измерено», никогда молчаливым нулём или единицей.
    /// Окон нет, рисунок не трогается: смотрит только `GdmlGeometry.Read`.
    ///
    /// Ключи:
    ///   --dir=&lt;каталог&gt;  входы; умолчание ..\data\gdml_a303 от exe;
    ///   --real=&lt;каталог&gt; настоящие файлы (12 МБ, в git их нет): числа маринелли и сеток;
    ///   --g4=&lt;файл&gt;      G4NistMaterialBuilder.cc: построчная сверка таблицы G4_*;
    ///   --examples=&lt;каталог&gt; examples/extended/persistency/gdml: число томов и ложные строки;
    ///   --table=&lt;файл&gt;   строки «группа, вход, наблюдение, вердикт» с табуляцией;
    ///   --dump=&lt;файл&gt;    все тома настоящих файлов: для сравнения «было — стало»;
    ///   --tmp=&lt;каталог&gt;   куда писать цепочки в 200000 ссылок (снимаются после прогона);
    ///   --one=&lt;файл&gt;     служебный: один опасный вход в дочернем процессе.
    ///
    /// Код 0 — всё сошлось, 1 — есть расхождение. Опасные входы идут дочерним процессом:
    /// на старом читателе они кончаются переполнением стека (0xC00000FD) или сторожем
    /// памяти, и пробу это не роняет. Положительный контроль: та же проба на бинаре
    /// `e354a0f6` даёт отказы — таблица в `handover/handover-2026-11-05-fixgdml-a.md`.
    /// Проба читает только поля, которые были и до правки, поэтому запускается на обоих.
    /// </summary>
    static class GdmlReaderProbeA303
    {
        [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
        [DllImport("kernel32.dll")] static extern int WerSetFlags(uint flags);

        static readonly List<string[]> Rows = new List<string[]>();
        static GdmlGeometry G;
        static string Summary = "";
        static List<string> Problems;
        static string Dir;
        static int Failed;

        static string F(double value)
        {
            return value.ToString("G7", CultureInfo.InvariantCulture);
        }

        static int Main(string[] args)
        {
            // Окна отчёта Windows о падении: дочерний процесс падает нарочно.
            SetErrorMode(0x0001 | 0x0002 | 0x8000);
            WerSetFlags(0x20);
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            string real = null, g4 = null, table = null, dump = null, tmp = null, one = null, examples = null;
            Dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "data", "gdml_a303");
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=")) Dir = a.Substring(6);
                else if (a.StartsWith("--real=")) real = a.Substring(7);
                else if (a.StartsWith("--g4=")) g4 = a.Substring(5);
                else if (a.StartsWith("--examples=")) examples = a.Substring(11);
                else if (a.StartsWith("--table=")) table = a.Substring(8);
                else if (a.StartsWith("--dump=")) dump = a.Substring(7);
                else if (a.StartsWith("--tmp=")) tmp = a.Substring(6);
                else if (a.StartsWith("--one=")) one = a.Substring(6);
                else
                {
                    Console.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (one != null)
            {
                return RunOne(one);
            }

            Console.OutputEncoding = Encoding.UTF8;
            if (!Directory.Exists(Dir))
            {
                Console.WriteLine("ОТКАЗ: нет каталога входов " + Dir);
                return 2;
            }

            Numbers(tmp);
            Mass();
            Rotation();
            Tags();
            Values();
            World();
            Subtraction();
            Units();
            Compositions();
            MaterialTable(g4);
            Meshes();
            Verdict();
            Third();
            Controls();
            if (real != null)
            {
                Real(real, dump);
            }

            if (examples != null)
            {
                Examples(examples);
            }

            if (table != null)
            {
                using (StreamWriter writer = new StreamWriter(table, false, new UTF8Encoding(false)))
                {
                    foreach (string[] row in Rows)
                    {
                        writer.WriteLine(string.Join("\t", row));
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("ИТОГ: входов " + Rows.Count + ", с расхождением " + Failed);
            return Failed == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // Оснастка
        // ------------------------------------------------------------------

        static void Case(string group, string file, Action check)
        {
            Problems = new List<string>();
            string path = Path.Combine(Dir, file + ".gdml");
            string observed;
            try
            {
                G = GdmlGeometry.Read(path);
                observed = Observe(G);
                Summary = G.Describe();
                G.DescribeLog();
                check();
            }
            catch (Exception ex)
            {
                observed = "ИСКЛЮЧЕНИЕ " + ex.GetType().Name;
                Problems.Add(observed + ": " + ex.Message);
            }

            Finish(group, file, observed);
        }

        static void Finish(string group, string name, string observed)
        {
            string verdict = Problems.Count == 0 ? "OK" : "ОТКАЗ: " + string.Join("; ", Problems.ToArray());
            if (Problems.Count > 0)
            {
                Failed++;
            }

            Rows.Add(new[] { group, name, observed, verdict });
            Console.WriteLine((Problems.Count == 0 ? "OK    " : "ОТКАЗ ") + group + " " + name + " | " + observed
                              + (Problems.Count == 0 ? "" : " | " + string.Join("; ", Problems.ToArray())));
        }

        static string Observe(GdmlGeometry g)
        {
            StringBuilder text = new StringBuilder();
            if (!string.IsNullOrEmpty(g.FatalError))
            {
                return "fatal";
            }

            int shown = 0;
            foreach (GdmlVolumeInstance v in g.Volumes)
            {
                if (++shown > 2)
                {
                    text.Append(" ...+" + (g.Volumes.Count - 2));
                    break;
                }

                text.Append(v.VolumeName).Append(": V=").Append(v.VolumeCm3 < 0.0 ? "?" : F(v.VolumeCm3));
                text.Append(" m=").Append(double.IsNaN(v.MassG) ? "?" : F(v.MassG));
                if (v.BoundsKnown)
                {
                    text.Append(" x[" + F(v.Min[0]) + ";" + F(v.Max[0]) + "] z[" + F(v.Min[2]) + ";" + F(v.Max[2]) + "]");
                }
                else
                {
                    text.Append(" габарит ?");
                }

                if (v.Material != null && v.Material.Fractions.Count > 0)
                {
                    text.Append(" {" + v.Material.CompositionText() + "}");
                }

                text.Append("; ");
            }

            if (g.Volumes.Count == 0)
            {
                text.Append("томов 0; ");
            }

            if (g.UnknownSolids > 0 || g.UnknownMaterials > 0)
            {
                text.Append("неподд=" + g.UnknownSolids + " сост=" + g.UnknownMaterials + "; ");
            }

            string firstWarning = "";
            foreach (string warning in g.Warnings)
            {
                if (warning.IndexOf("taken as the world", StringComparison.Ordinal) < 0)
                {
                    firstWarning = warning;
                    break;
                }
            }

            text.Append("строк=" + g.Warnings.Count);
            if (firstWarning.Length > 0)
            {
                text.Append(" «" + (firstWarning.Length > 90 ? firstWarning.Substring(0, 90) + "…" : firstWarning) + "»");
            }

            return text.ToString();
        }

        static GdmlVolumeInstance V(string name)
        {
            foreach (GdmlVolumeInstance volume in G.Volumes)
            {
                if (volume.VolumeName == name)
                {
                    return volume;
                }
            }

            Problems.Add("нет тома " + name);
            return null;
        }

        static void Near(string what, double actual, double expected, double tolerance)
        {
            if (double.IsNaN(actual) || Math.Abs(actual - expected) > tolerance)
            {
                Problems.Add(what + " = " + F(actual) + ", ждали " + F(expected));
            }
        }

        static double Tol(double expected)
        {
            return Math.Max(1e-5, Math.Abs(expected) * 1e-5);
        }

        static void Vol(string name, double expected)
        {
            GdmlVolumeInstance v = V(name);
            if (v != null)
            {
                Near(name + ".V", v.VolumeCm3, expected, Tol(expected));
            }
        }

        static void NoVol(string name)
        {
            GdmlVolumeInstance v = V(name);
            if (v != null && v.VolumeCm3 >= 0.0)
            {
                Problems.Add(name + ": объём " + F(v.VolumeCm3) + ", ждали «не измерено»");
            }

            if (v != null && !double.IsNaN(v.MassG))
            {
                Problems.Add(name + ": масса " + F(v.MassG) + " без объёма");
            }
        }

        static void Mass(string name, double expected)
        {
            GdmlVolumeInstance v = V(name);
            if (v != null)
            {
                Near(name + ".m", v.MassG, expected, Tol(expected));
            }
        }

        static void NoMass(string name)
        {
            GdmlVolumeInstance v = V(name);
            if (v != null && !double.IsNaN(v.MassG))
            {
                Problems.Add(name + ": масса " + F(v.MassG) + ", ждали «?»");
            }
        }

        static void Range(string name, int axis, double low, double high)
        {
            GdmlVolumeInstance v = V(name);
            if (v == null)
            {
                return;
            }

            if (!v.BoundsKnown)
            {
                Problems.Add(name + ": габарит не измерен, ждали числа");
                return;
            }

            Near(name + ".min" + "xyz"[axis], v.Min[axis], low, 1e-6 * Math.Max(1.0, Math.Abs(low)));
            Near(name + ".max" + "xyz"[axis], v.Max[axis], high, 1e-6 * Math.Max(1.0, Math.Abs(high)));
        }

        static void NoBounds(string name)
        {
            GdmlVolumeInstance v = V(name);
            if (v != null && v.BoundsKnown)
            {
                Problems.Add(name + ": габарит x[" + F(v.Min[0]) + ";" + F(v.Max[0]) + "], ждали «не измерено»");
            }
        }

        static void Warn(string fragment)
        {
            foreach (string warning in G.Warnings)
            {
                if (warning.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    return;
                }
            }

            Problems.Add("нет строки сводки про «" + fragment + "»");
        }

        static void NoWarn(string fragment)
        {
            foreach (string warning in G.Warnings)
            {
                if (warning.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    Problems.Add("лишняя строка: " + warning);
                    return;
                }
            }
        }

        static void Comp(string name, string symbol, double expected, double tolerance)
        {
            GdmlVolumeInstance v = V(name);
            if (v == null || v.Material == null)
            {
                Problems.Add(name + ": нет вещества");
                return;
            }

            foreach (KeyValuePair<int, double> fraction in v.Material.Fractions)
            {
                if (GdmlG4Materials.SymbolOf(fraction.Key) == symbol)
                {
                    Near(name + "." + symbol, fraction.Value, expected, tolerance);
                    return;
                }
            }

            Problems.Add(name + ": в составе нет " + symbol + " {" + v.Material.CompositionText() + "}");
        }

        static void Axis(char expected)
        {
            if (G.Axis != expected)
            {
                Problems.Add("ось " + G.Axis + ", ждали " + expected);
            }
        }

        static void Header(int solids, int materials)
        {
            if (G.UnknownSolids != solids)
            {
                Problems.Add("шапка: неподдержанных " + G.UnknownSolids + ", ждали " + solids);
            }

            if (G.UnknownMaterials != materials)
            {
                Problems.Add("шапка: неизвестных составов " + G.UnknownMaterials + ", ждали " + materials);
            }
        }

        // ------------------------------------------------------------------
        // Опасные входы — дочерним процессом
        // ------------------------------------------------------------------

        static int RunOne(string path)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread guard = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(100);
                    long megabytes = Process.GetCurrentProcess().PrivateMemorySize64 / (1024 * 1024);
                    if (megabytes > 1500)
                    {
                        Console.WriteLine("memory guard: " + megabytes + " MB");
                        Console.Out.Flush();
                        Environment.Exit(77);
                    }
                }
            });
            guard.IsBackground = true;
            guard.Start();

            Stopwatch watch = Stopwatch.StartNew();
            GdmlGeometry geometry = GdmlGeometry.Read(path);
            Console.WriteLine("volumes=" + geometry.Volumes.Count + " warnings=" + geometry.Warnings.Count
                              + " ms=" + watch.ElapsedMilliseconds + " peakMB="
                              + Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024));
            foreach (string warning in geometry.Warnings)
            {
                Console.WriteLine(warning);
            }

            return 0;
        }

        static void Hazard(string group, string name, string path, string fragment, int volumes)
        {
            Problems = new List<string>();
            ProcessStartInfo start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                "\"--one=" + path + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            string observed;
            using (Process child = Process.Start(start))
            {
                string output = "";
                child.ErrorDataReceived += (s, e) => { };
                child.BeginErrorReadLine();
                Thread reader = new Thread(() => output = child.StandardOutput.ReadToEnd());
                reader.Start();
                if (!child.WaitForExit(180000))
                {
                    child.Kill();
                    child.WaitForExit();
                    Problems.Add("завис");
                }

                reader.Join();
                string first = output.Split('\n')[0].Trim();
                observed = "код 0x" + child.ExitCode.ToString("X8", CultureInfo.InvariantCulture) + " " + first;
                if (child.ExitCode != 0)
                {
                    Problems.Add("процесс упал: код 0x" + child.ExitCode.ToString("X8", CultureInfo.InvariantCulture));
                }
                else
                {
                    if (output.IndexOf(fragment, StringComparison.Ordinal) < 0)
                    {
                        Problems.Add("нет строки про «" + fragment + "»");
                    }

                    if (!first.StartsWith("volumes=" + volumes.ToString(CultureInfo.InvariantCulture) + " ", StringComparison.Ordinal))
                    {
                        Problems.Add("ждали томов " + volumes);
                    }

                    int line = output.IndexOf('\n');
                    int stop = output.IndexOf(fragment, StringComparison.Ordinal);
                    if (line >= 0 && stop >= 0)
                    {
                        int end = output.IndexOf('\n', stop);
                        int begin = output.LastIndexOf('\n', stop) + 1;
                        observed += " «" + output.Substring(begin, (end < 0 ? output.Length : end) - begin).Trim() + "»";
                    }
                }
            }

            Finish(group, name, observed);
        }

        // ------------------------------------------------------------------
        // 1. Неконечные числа, рекурсия, циклы
        // ------------------------------------------------------------------

        static void Numbers(string tmp)
        {
            const string g = "1";
            Case(g, "16a_nan", () => { NoVol("A"); NoBounds("A"); Warn("x = \"NaN\""); });
            Case(g, "16b_inf", () => { NoVol("A"); NoBounds("A"); Warn("x = \"Infinity\""); });
            Case(g, "16d_nan_pc", () => { NoVol("A"); Warn("rmax = \"NaN\""); });
            Case(g, "16e_pos_inf", () => { Vol("A", 3.92699); NoBounds("A"); Warn("x = \"Infinity\""); });
            Case(g, "c9_1e400", () => { NoVol("A"); Warn("x = \"1e400\""); });
            Case(g, "c1_nan_vertex", () => { NoVol("A"); Warn("x = \"NaN\""); });
            Case(g, "c2_rot_nan", () => { NoBounds("A"); Warn("x = \"NaN\""); });
            Case(g, "c3_gp_nan", () => { NoVol("A"); Warn("r = \"NaN\""); });
            Case(g, "c10_rmax_1e300", () => { NoVol("A"); Warn("out of range"); });

            // Конечные огромные значения читатель пропускает: рисунок обязан пережить их сам.
            Case(g, "16c_huge", () => { Vol("A", 1e299); Range("A", 0, -5e299, 5e299); });
            Case(g, "c1b_1e39_vertex", () => { NoVol("A"); Range("A", 0, 0.0, 1e39); Warn("inside out"); });
            Case(g, "c1c_1e300_vertex", () => { NoVol("A"); Range("A", 0, 0.0, 1e300); Warn("inside out"); });
            Case(g, "c2b_rot_big", () => { Vol("A", 3.92699); NoBounds("A"); Warn("out of range"); });

            Case(g, "24_cycle1", () => { NoMass("A"); Warn("contains itself"); });
            Case(g, "25_subcycle", () => { NoVol("A"); Warn("reference cycle"); });
            Case(g, "26_matcycle", () => { Header(0, 1); Warn("reference cycle"); });

            Hazard(g, "23_cycle2", Path.Combine(Dir, "23_cycle2.gdml"), "contains itself", 1);
            Hazard(g, "d1_dag", Path.Combine(Dir, "d1_dag.gdml"), "places more than", 0);
            Hazard(g, "k1_dag_after_500_warnings", Path.Combine(Dir, "k1_dag_after_500_warnings.gdml"), "places more than", 0);

            string folder = Path.Combine(tmp ?? Path.GetTempPath(), "gdml_a303_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            try
            {
                const int N = 200000;
                string w = "<box name=\"WB\" x=\"2000\" y=\"2000\" z=\"2000\" lunit=\"mm\"/>";
                string p = "<polycone name=\"P\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"><zplane z=\"0\" rmin=\"0\" rmax=\"5\"/><zplane z=\"50\" rmin=\"0\" rmax=\"5\"/></polycone>";
                string tail = "<volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"WB\"/><physvol name=\"p\"><volumeref ref=\"A\"/></physvol></volume></structure><setup name=\"Default\" version=\"1.0\"><world ref=\"World\"/></setup></gdml>";

                string subPath = Path.Combine(folder, "sub_chain.gdml");
                using (StreamWriter writer = new StreamWriter(subPath))
                {
                    writer.Write("<?xml version=\"1.0\"?><gdml><solids>" + w + "<box name=\"B\" x=\"1\" y=\"1\" z=\"1\"/><box name=\"S" + N + "\" x=\"10\" y=\"10\" z=\"10\"/>");
                    for (int i = 0; i < N; i++)
                    {
                        writer.Write("<subtraction name=\"S" + i + "\"><first ref=\"S" + (i + 1) + "\"/><second ref=\"B\"/></subtraction>");
                    }

                    writer.Write("</solids><structure><volume name=\"A\"><materialref ref=\"G4_AIR\"/><solidref ref=\"S0\"/></volume>" + tail);
                }

                Hazard(g, "sub_chain_200000", subPath, "nested deeper", 1);

                string matPath = Path.Combine(folder, "mat_chain.gdml");
                using (StreamWriter writer = new StreamWriter(matPath))
                {
                    writer.Write("<?xml version=\"1.0\"?><gdml><materials>");
                    for (int i = 0; i < N; i++)
                    {
                        writer.Write("<material name=\"M" + i + "\"><D value=\"1\"/><fraction n=\"1\" ref=\"M" + (i + 1) + "\"/></material>");
                    }

                    writer.Write("</materials><solids>" + w + p + "</solids><structure><volume name=\"A\"><materialref ref=\"M0\"/><solidref ref=\"P\"/></volume>" + tail);
                }

                Hazard(g, "mat_chain_200000", matPath, "nested deeper", 1);

                // `XElement.Value` рекурсивен: у `expression` это было падение процесса.
                string nestedPath = Path.Combine(folder, "e20000.gdml");
                using (StreamWriter writer = new StreamWriter(nestedPath))
                {
                    writer.Write("<?xml version=\"1.0\"?><gdml><define><expression name=\"E\">");
                    for (int i = 0; i < 20000; i++)
                    {
                        writer.Write("<a>");
                    }

                    for (int i = 0; i < 20000; i++)
                    {
                        writer.Write("</a>");
                    }

                    writer.Write("</expression></define><solids>" + w + p + "</solids><structure><volume name=\"A\"><materialref ref=\"G4_Al\"/><solidref ref=\"P\"/></volume>" + tail);
                }

                Hazard(g, "e20000", nestedPath, "nested elements", 1);

                // Шаги обхода кончаются раньше томов: строка не должна говорить о томах.
                string stepsPath = Path.Combine(folder, "k2_steps_limit.gdml");
                using (StreamWriter writer = new StreamWriter(stepsPath))
                {
                    writer.Write("<?xml version=\"1.0\"?><gdml><solids>" + w + "</solids><structure><volume name=\"A\"><materialref ref=\"G4_AIR\"/><solidref ref=\"WB\"/>");
                    for (int i = 0; i < 1100000; i++)
                    {
                        writer.Write("<physvol name=\"p\"><volumeref ref=\"A\"/></physvol>");
                    }

                    writer.Write("</volume>" + tail);
                }

                Hazard(g, "k2_steps_limit", stepsPath, "steps", 0);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // ------------------------------------------------------------------
        // 2. Масса
        // ------------------------------------------------------------------

        static void Mass()
        {
            const string g = "2";
            Case(g, "32_container", () =>
            {
                Vol("Housing", 1000.0);
                Mass("Housing", (1000.0 - 729.0) * 2.699);
                Mass("Cry", 729.0 * 3.667);
                if (Summary.IndexOf("own volume 271", StringComparison.Ordinal) < 0)
                {
                    Problems.Add("в сводке нет собственного объёма матери");
                }

                // Числа — раньше состава: на строке кристалла «729 cm3» стоит до «[».
                int line = Summary.IndexOf("Cry  ", StringComparison.Ordinal);
                int number = line < 0 ? -1 : Summary.IndexOf("729", line, StringComparison.Ordinal);
                int bracket = line < 0 ? -1 : Summary.IndexOf('[', line);
                if (number < 0 || bracket < 0 || number > bracket)
                {
                    Problems.Add("в строке тома размеры, объём и масса должны стоять раньше состава");
                }
            });
            Case(g, "a_overlap", () => { Vol("A", 1.0); NoMass("A"); Warn("children take 8"); Mass("Inner", 8.0 * 7.874); });
            Case(g, "a_child_unknown", () => { Vol("A", 1.0); NoMass("A"); });
            Case(g, "a_child_missing", () => { Vol("A", 1.0); NoMass("A"); Warn("Ghost"); });
        }

        // ------------------------------------------------------------------
        // 3. Поворот и единицы углов
        // ------------------------------------------------------------------

        static void Rotation()
        {
            const string g = "3";
            Case(g, "03_rot_sign", () => { Range("A", 0, -50.0, 0.0); Range("A", 2, -5.0, 5.0); Axis('x'); });
            Case(g, "34_pc_rot_end", () => { Range("A", 0, -50.0, 50.0); Axis('x'); });
            Case(g, "33_pc_offset_y", () => { Axis('z'); });
            Case(g, "28_ylong_box", () => { Axis('y'); });
            Case(g, "05_rot_default_rad", () => { Range("A", 0, -50.0, 0.0); Range("A", 2, -5.0, 5.0); });
            Case(g, "06_polycone_no_aunit", () => { Vol("A", 3.92699); NoWarn("partial"); });
            Case(g, "a_rot_304050", () =>
            {
                GdmlVolumeInstance v = V("A");
                if (v != null)
                {
                    GdmlPoint o = v.Transform.Apply(new GdmlPoint(0, 0, 0));
                    GdmlPoint z = v.Transform.Apply(new GdmlPoint(0, 0, 1));
                    Near("образ +z по x", z.X - o.X, -0.6427876, 1e-6);
                    Near("образ +z по y", z.Y - o.Y, 0.3830222, 1e-6);
                    Near("образ +z по z", z.Z - o.Z, 0.6634139, 1e-6);
                }
            });
            Case(g, "a_angle_unit_bad", () => { Vol("A", 3.92699); NoBounds("A"); Warn("DEG"); });
            Case(g, "a_root_unit", () => { Range("A", 2, 0.0, 50.0); Warn("aunit"); Warn("lunit"); });
        }

        // ------------------------------------------------------------------
        // 4. Теги
        // ------------------------------------------------------------------

        static void Tags()
        {
            const string g = "4";
            // (fixGdml2, третий заход) Репликация вдоль x читается, как `G4PVReplica`: 10 копий Seg по 10 мм, копия i в
            // −45 + 10·i; воздуха в A не остаётся, масса — 0. Вдоль phi — по-прежнему «не поддержано», вещество копий — «не учтено».
            Case(g, "08_replica", () =>
            {
                Vol("A", 1000.0); Mass("A", 0.0); Warn("<replicavol> is read, copies: 10 (Seg along x, 10 mm each)");
                int copies = 0;
                foreach (GdmlVolumeInstance v in G.Volumes) { if (v.VolumeName == "Seg") copies++; }
                if (copies != 10) Problems.Add("копий Seg " + copies + ", ждали 10");
                Range("Seg", 0, -50.0, -40.0);
                Mass("Seg", 100.0 * 11.35);
            });
            // (четвёртый заход) Без `number` Geant4 берёт 1 (`G4GDMLReadStructure.cc:977`): одна копия, воздуха не остаётся.
            Case(g, "a_replica_nonumber", () =>
            {
                Mass("A", 0.0); Warn("<replicavol> is read, copies: 1 (Seg along x, 10 mm each)");
                Range("Seg", 0, -5.0, 5.0);
            });
            // (четвёртый заход) Тела внутри `loop` — в «не учтено», как у `paramvol`/`divisionvol`.
            Case(g, "a_loop_content", () =>
            {
                NoMass("A"); Warn("<loop>");
                System.Reflection.FieldInfo loopField = typeof(GdmlGeometry).GetField("UncountedMaterials");
                IDictionary<string, string> loopUncounted = loopField == null ? null : loopField.GetValue(G) as IDictionary<string, string>;
                string loopReason;
                if (loopUncounted == null || !loopUncounted.TryGetValue("G4_Cu", out loopReason) || loopReason != "<loop>") Problems.Add("G4_Cu не в «не учтено»");
            });
            Case(g, "a_replica_phi", () =>
            {
                NoMass("A"); Warn("<replicavol> is not supported");
                // Отражением: проба собирается и против прежнего приложения (положительный контроль).
                System.Reflection.FieldInfo field = typeof(GdmlGeometry).GetField("UncountedMaterials");
                IDictionary<string, string> uncounted = field == null ? null : field.GetValue(G) as IDictionary<string, string>;
                string reason;
                if (uncounted == null || !uncounted.TryGetValue("G4_Pb", out reason) || reason != "<replicavol>") Problems.Add("G4_Pb не в «не учтено»");
            });
            Case(g, "09_scale", () => { Warn("<scale>"); Range("A", 2, -50.0, 0.0); });
            Case(g, "10_file", () => { Warn("sub.gdml"); Warn("fromfile"); });
            Case(g, "a_tags_copies", () => { Vol("A", 1000.0); NoMass("A"); Warn("<paramvol>"); Warn("<divisionvol>"); Warn("<loop>"); });
            Case(g, "a_tag_unknown", () => { Warn("<frobnicate>"); Warn("<wobble>"); NoWarn("auxiliary"); });
        }

        // ------------------------------------------------------------------
        // 5. Значения: умолчания схемы, выражения, quantity, lunit грани
        // ------------------------------------------------------------------

        static void Values()
        {
            const string g = "5";
            Case(g, "04a_pos_inline_partial", () => { Range("A", 2, 30.0, 80.0); });
            Case(g, "04b_rot_inline_partial", () => { Range("A", 0, -50.0, 0.0); });
            Case(g, "04c_pos_define_partial", () => { Range("A", 2, 30.0, 80.0); NoWarn("position"); });
            Case(g, "07a_expr_rmin", () => { Vol("A", 47.12389); });
            Case(g, "07b_expr_pos", () => { Range("A", 2, 6.0, 56.0); });
            Case(g, "07c_expr_box", () => { Vol("A", 1.0); Range("A", 0, -5.0, 5.0); });
            Case(g, "07d_quantity_unit", () => { Vol("A", 5.0); Range("A", 0, -25.0, 25.0); });
            Case(g, "07e_const_expr", () => { Vol("A", 47.12389); });
            Case(g, "07f_twopi", () => { Vol("A", 3.92699); NoWarn("partial"); Warn("TWOPI is not defined"); });
            // (fixGdml2, решение Amber 07.11.2026 «Добавить функции (Рекомендую)») Функции набора Geant4 читаются: файл с
            // прежним именем `a_expr_refused` теперь даёт √2 мм; отказ остаётся у значения вне области и у степени `^`.
            Case(g, "a_expr_refused", () => { Vol("A", Math.Sqrt(2.0) * 100.0 / 1000.0); NoWarn("sqrt(2)"); });
            Case(g, "a_expr_trig_deg", () => { Vol("A", 1.0); Range("A", 2, 25.0, 35.0); NoWarn("sin("); NoWarn("atan2("); });
            Case(g, "a_expr_function_bad", () => { NoVol("A"); Warn("sqrt(-4)"); });
            Case(g, "a_expr_const_e", () => { Vol("A", 1.0); NoWarn("exp(1)"); NoWarn("gamma"); });
            Case(g, "o10b_functions", () => { NoVol("L1"); Warn("2^3"); NoWarn("sqrt(16)"); NoWarn("pow(2,3)"); });
            Case(g, "31_tess_lunit", () => { Vol("A", 1.0 / 6000.0); Warn("lunit=\"cm\""); });
            Case(g, "a_face_lunit", () => { Vol("A", 1000.0 / 6000.0); Range("A", 0, 0.0, 10.0); });
            Case(g, "a_tess_relative", () => { Vol("A", 1.0e6 / 6000.0); });
        }

        // ------------------------------------------------------------------
        // 6. setup/world
        // ------------------------------------------------------------------

        static void World()
        {
            const string g = "6";
            Case(g, "11_setup_world", () =>
            {
                Vol("Det", 3.92699);
                if (G.Volumes.Count != 1)
                {
                    Problems.Add("томов " + G.Volumes.Count + ", ждали 1");
                }

                NoWarn("taken as the world");
            });
            Case(g, "a_setup_missing", () => { Warn("Nope"); Warn("taken as the world"); Vol("A", 3.92699); });
            Case(g, "01_box_cm", () => { Warn("taken as the world"); });
        }

        // ------------------------------------------------------------------
        // 7. Вычитание
        // ------------------------------------------------------------------

        static void Subtraction()
        {
            const string g = "7";
            Case(g, "12_sub_second_pos", () => { Vol("A", 1.0); });
            Case(g, "13_sub_inside_hole", () => { NoVol("A"); Warn("Ring"); });
            Case(g, "13b_sub_nested", () => { NoVol("A"); Warn("S1"); });
            Case(g, "36_ctrl_sub", () => { Vol("A", 1.0 - 0.064); });
            Case(g, "a_sub_box_partial", () => { Vol("A", 0.5); });
            Case(g, "a_sub_rot_partial", () => { NoVol("A"); Warn("partly overlaps"); });
            Case(g, "a_sub_inside", () => { Vol("A", 1000.0 - Math.PI * 25.0 * 10.0 / 1000.0); });
            Case(g, "c7_sub_missing", () => { NoVol("A"); Warn("NOPE"); });
            Case(g, "22_union", () => { Header(0, 0); Vol("A", 1.0); NoVol("C"); Vol("D", Math.PI * 25.0 * 10.0 / 1000.0); });
            Case(g, "a_unknown_sphere", () => { Header(1, 0); NoVol("A"); Warn("<sphere>"); });
        }

        // ------------------------------------------------------------------
        // 8. Единицы
        // ------------------------------------------------------------------

        static void Units()
        {
            const string g = "8";
            Case(g, "15a_unit_km", () => { Vol("A", 1.0e15); });
            Case(g, "15b_unit_upper", () => { NoVol("A"); Warn("\"CM\""); });
            Case(g, "15c_density_bad", () => { Vol("A", 3.92699); NoMass("A"); Warn("kg/cm3"); });
            Case(g, "a_unit_ang_fm", () => { Vol("A", 1.0); });
            Case(g, "a_unit_fm", () => { Vol("A", 0.001); });
        }

        // ------------------------------------------------------------------
        // 9. Составы
        // ------------------------------------------------------------------

        static void Compositions()
        {
            const string g = "9";
            Case(g, "14a_mat_Z", () => { Comp("A", "Pb", 1.0, 1e-9); Header(0, 0); Mass("A", 3.92699 * 11.35); });
            Case(g, "14b_mat_composite", () => { Comp("A", "H", 0.1119, 2e-4); Comp("A", "O", 0.8881, 2e-4); Header(0, 0); });
            Case(g, "14c_mat_element", () => { Comp("A", "H", 0.112, 1e-9); Comp("A", "O", 0.888, 1e-9); Header(0, 0); });
            Case(g, "14d_fraction_density_only", () => { Comp("A", "Fe", 0.5, 1e-9); Header(0, 1); Warn("DO"); });
            Case(g, "14e_steel", () => { Comp("A", "Fe", 0.7462, 1e-9); Comp("A", "Cr", 0.169, 1e-9); Comp("A", "Ni", 0.0848, 1e-9); Mass("A", 3.92699 * 8.0); });
            Case(g, "a_mat_isotope", () => { Comp("A", "U", 237.448 / (237.448 + 31.998), 1e-5); Comp("A", "O", 31.998 / (237.448 + 31.998), 1e-5); Header(0, 0); });
            Case(g, "a_mat_z_and_fraction", () => { Comp("A", "Cs", 0.5, 1e-9); Comp("A", "I", 0.5, 1e-9); Header(0, 0); });
            Case(g, "a_mat_symbol", () => { Comp("A", "Fe", 0.5, 1e-9); Comp("A", "Cu", 0.5, 1e-9); Header(0, 0); });
            Case(g, "a_mat_g4_in_file", () => { Comp("A", "Fe", 1.0, 1e-9); Mass("A", 3.92699 * 3.0); });
            Case(g, "a_mat_sum", () => { Warn("sum to 1.4"); Comp("A", "Fe", 0.5, 1e-9); Comp("A", "Cu", 0.5, 1e-9); });
            Case(g, "a_mat_composite_bad", () => { Header(0, 1); Warn("not an element"); });
        }

        // ------------------------------------------------------------------
        // 10. Таблица G4_*
        // ------------------------------------------------------------------

        static double Fraction(GdmlMaterial material, string symbol)
        {
            foreach (KeyValuePair<int, double> fraction in material.Fractions)
            {
                if (GdmlG4Materials.SymbolOf(fraction.Key) == symbol)
                {
                    return fraction.Value;
                }
            }

            return 0.0;
        }

        static void MaterialTable(string g4)
        {
            const string g = "10";
            Problems = new List<string>();
            double worst = 0.0;
            foreach (KeyValuePair<string, GdmlMaterial> row in GdmlG4Materials.Table)
            {
                double sum = 0.0;
                foreach (double fraction in row.Value.Fractions.Values)
                {
                    sum += fraction;
                }

                worst = Math.Max(worst, Math.Abs(sum - 1.0));
                if (Math.Abs(sum - 1.0) > 2e-5)
                {
                    Problems.Add(row.Key + ": сумма долей " + F(sum));
                }
            }

            GdmlMaterial steel = GdmlG4Materials.Table["G4_STAINLESS-STEEL"];
            Near("сталь ρ", steel.Density, 8.0, 1e-12);
            Near("сталь Fe", Fraction(steel, "Fe"), 0.7462, 1e-12);
            Near("сталь Cr", Fraction(steel, "Cr"), 0.169, 1e-12);
            Near("сталь Ni", Fraction(steel, "Ni"), 0.0848, 1e-12);
            Near("G4_S ρ", GdmlG4Materials.Table["G4_S"].Density, 2.0, 1e-12);
            Near("воздух C", Fraction(GdmlG4Materials.Table["G4_AIR"], "C"), 0.000124, 1e-12);
            Finish(g, "таблица G4", "строк " + GdmlG4Materials.Table.Count + ", наибольшая |Σ−1| = " + F(worst));

            if (g4 == null)
            {
                return;
            }

            Problems = new List<string>();
            string text = File.ReadAllText(g4);
            int compared = 0;
            foreach (KeyValuePair<string, GdmlMaterial> row in GdmlG4Materials.Table)
            {
                Match m = Regex.Match(text, "AddMaterial\\(\"" + Regex.Escape(row.Key) + "\"\\s*,\\s*([0-9.eE+-]+)[^\\n]*\\n((?:\\s*AddElement[^\\n]*\\n)*)");
                if (!m.Success && row.Key == "G4_Galactic")
                {
                    // (fixGdml2) Пустота задана в G4 не числом, а `universe_mean_density` (1e-25 г/см³, CLHEP) и Z = 1.
                    compared++;
                    if (!Regex.IsMatch(text, "AddMaterial\\(\"G4_Galactic\"\\s*,\\s*density\\s*,\\s*1\\s*,")
                        || !Regex.IsMatch(text, "density\\s*=\\s*universe_mean_density\\s*\\*\\s*cm3\\s*/\\s*g")
                        || row.Value.Density != 1e-25 || row.Value.Fractions.Count != 1 || Fraction(row.Value, "H") != 1.0)
                    {
                        Problems.Add("G4_Galactic: в G4 universe_mean_density и Z = 1, у нас ρ " + F(row.Value.Density));
                    }

                    continue;
                }

                if (!m.Success)
                {
                    Problems.Add(row.Key + ": нет в G4NistMaterialBuilder.cc");
                    continue;
                }

                compared++;
                double density = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (Math.Abs(density - row.Value.Density) > 1e-12 * density)
                {
                    Problems.Add(row.Key + ": ρ " + F(row.Value.Density) + ", в G4 " + F(density));
                }

                try
                {
                    CheckAtomCounts(m.Groups[2].Value, row.Value, row.Key);
                }
                catch (Exception ex) when (ex is TypeLoadException || ex is MissingMemberException)
                {
                    Problems.Add("нет таблицы атомных масс приложения: " + ex.GetType().Name);
                }

                foreach (Match e in Regex.Matches(m.Groups[2].Value, "AddElementByWeightFraction\\(\\s*(\\d+)\\s*,\\s*([0-9.eE+-]+)"))
                {
                    int z = int.Parse(e.Groups[1].Value, CultureInfo.InvariantCulture);
                    double weight = double.Parse(e.Groups[2].Value, CultureInfo.InvariantCulture);
                    double mine;
                    row.Value.Fractions.TryGetValue(z, out mine);
                    if (Math.Abs(mine - weight) > 1e-9)
                    {
                        Problems.Add(row.Key + ": доля Z=" + z + " " + F(mine) + ", в G4 " + F(weight));
                    }
                }
            }

            Finish(g, "сверка с G4NistMaterialBuilder.cc", "сверено строк " + compared);
        }

        /// <summary>Соединение Geant4 задано числом атомов: сверка долей по таблице масс приложения.</summary>
        static void CheckAtomCounts(string text, GdmlMaterial material, string name)
        {
            MatchCollection atoms = Regex.Matches(text, @"AddElementByAtomCount\(\s*""(\w+)""\s*,\s*(\d+)");
            double total = 0.0;
            foreach (Match a in atoms)
            {
                total += double.Parse(a.Groups[2].Value, CultureInfo.InvariantCulture)
                         * AttenuationData.AtomicMass[GdmlG4Materials.ZOf(a.Groups[1].Value)];
            }

            foreach (Match a in atoms)
            {
                int z = GdmlG4Materials.ZOf(a.Groups[1].Value);
                double expected = double.Parse(a.Groups[2].Value, CultureInfo.InvariantCulture)
                                  * AttenuationData.AtomicMass[z] / total;
                double mine;
                material.Fractions.TryGetValue(z, out mine);
                if (Math.Abs(mine - expected) > 2e-4)
                {
                    Problems.Add(name + ": доля " + a.Groups[1].Value + " " + F(mine) + ", по числу атомов " + F(expected));
                }
            }
        }

        // ------------------------------------------------------------------
        // 11. Сетка, плотность, кольцо
        // ------------------------------------------------------------------

        static void Meshes()
        {
            const string g = "11";
            Case(g, "30_tess_open", () => { NoVol("A"); Warn("6 unpaired"); });
            Case(g, "a_tess_closed", () => { Vol("A", 1.0e6 / 6000.0); NoWarn("unpaired"); });
            Case(g, "c4_rmin_gt_rmax", () => { NoVol("A"); Warn("rmin = 20"); });
            Case(g, "c5_neg_box", () => { NoVol("A"); Warn("not positive"); });
            Case(g, "c11_density_nan", () => { Vol("A", 3.92699); NoMass("A"); Warn("NaN"); });
            Case(g, "c12_density_neg", () => { Vol("A", 3.92699); NoMass("A"); Warn("-3"); });
            Case(g, "c13_fraction_nan", () => { Header(0, 1); Warn("\"NaN\""); });
            Case(g, "a_deltaphi_bad", () => { Vol("A", 3.92699); Warn("720"); NoWarn("partial"); });
            Case(g, "a_deltaphi_missing", () => { Vol("A", 3.92699); Warn("deltaphi is missing"); });
            Case(g, "yf1", () => { Vol("A", 3.141592654); Warn("6.28319"); Warn("full revolution"); });
            Case(g, "yf5", () => { Vol("A", 3.141592654); Warn("full revolution"); });
            Case(g, "yf6", () => { Vol("A", 3.141592654); Warn("full revolution"); });
            Case(g, "yf7", () => { Vol("A", 3.141592654); Warn("full revolution"); });
            Case(g, "yf3", () => { Near("A.V", V("A").VolumeCm3, 3.1415925, 1e-6); Warn("partial revolution"); });
            Case(g, "y15_gp_self", () => { NoVol("A"); Warn("crosses itself"); });
            Case(g, "y14_gp_cw", () => { Vol("A", 3.141592654); NoWarn("crosses"); });
            Case(g, "e04_dup_vertex_1e-12", () => { Vol("A", 0.1666667); NoWarn("unpaired"); });
            Case(g, "e03_dup_vertex_exact", () => { Vol("A", 0.1666667); NoWarn("unpaired"); });
            Case(g, "e1_relative_nondyadic", () => { Near("A.V", V("A").VolumeCm3, 0.343 / 6000.0, 1e-9); NoWarn("unpaired"); });
            Case(g, "a_dup_vertex_1e-8", () => { NoVol("A"); Warn("unpaired"); });
            Case(g, "e05_relative_lower", () => { Warn("type=\"relative\""); Warn("3 unpaired"); });
            Case(g, "e07_face_lunit_bad", () => { NoVol("A"); Warn("\"XX\""); Warn("1 face(s) skipped"); NoWarn("vertex:"); });
            Case(g, "a_zplane_unordered", () => { NoVol("A"); Warn("not ordered"); });
            Case(g, "a_position_missing", () =>
            {
                Vol("A", 3.92699);
                NoBounds("A");
                Warn("ghost");
                if (Summary.IndexOf("placement is not read", StringComparison.Ordinal) < 0)
                {
                    Problems.Add("том без положения не в списке «не измерено»");
                }
            });
        }

        // ------------------------------------------------------------------
        // Вердикт критика: дважды заданное, пустые ссылки, деление
        // ------------------------------------------------------------------

        static void Verdict()
        {
            const string g = "критик";
            Case(g, "p03_zero_div", () => { Vol("A", 3.92699); NoBounds("A"); Warn("1/(1/0)"); });
            Case(g, "a_div_overflow", () => { NoVol("A"); Warn("1/(1e308*10)"); });
            Case(g, "n10_expr_zero_div", () => { NoVol("A"); Warn("is not a finite number"); });
            Case(g, "t1_material_twice", () => { Comp("A", "Fe", 1.0, 1e-9); Mass("A", 3.92699); Warn("first one is read"); });
            Case(g, "a_element_twice", () => { Comp("A", "Fe", 1.0, 1e-9); Warn("element E is defined twice"); });
            Case(g, "a_isotope_twice", () => { Comp("A", "U", 1.0, 1e-9); Warn("isotope I is defined twice"); });
            Case(g, "a_solid_twice", () => { Vol("A", 1.0); Warn("solid X is defined twice"); });
            Case(g, "a_volume_twice", () => { Mass("A", 3.92699 * 2.699); Warn("volume A is defined twice"); });
            Case(g, "t2_position_twice", () => { Range("A", 2, 99.0, 149.0); Warn("position q is defined twice"); });
            Case(g, "a_rotation_twice", () => { Range("A", 0, -50.0, 0.0); Warn("rotation r is defined twice"); });
            Case(g, "d07_const_redefined", () => { NoVol("A"); Warn("define/constant L:"); Warn("x = \"L\""); });
            Case(g, "a_const_same", () => { Vol("A", 1.0); Warn("same value"); });
            Case(g, "d09_shadow_unit", () => { NoVol("A"); Warn("define/constant cm"); });
            Case(g, "t06_composite_zero", () => { Header(0, 1); Warn("composition is empty"); });
            Case(g, "t05_fraction_zero", () => { Header(0, 1); Warn("composition is empty"); });
            Case(g, "s05_vol_twice", () =>
            {
                Warn("volume Top is defined twice; the first one");
                if (G.Volumes.Count != 0)
                {
                    Problems.Add("томов " + G.Volumes.Count + ", у первого Top детей нет");
                }
            });
            Case(g, "s02_two_setups_nodefault", () => { Warn("none is Default"); });
            Case(g, "s1_second_empty", () => { NoVol("A"); Warn("empty reference"); NoWarn("reference  not"); });
            Case(g, "s2_volume_noname", () => { Warn("<volume> without a name"); });
            Case(g, "t14_mat_noname", () => { Warn("<material> without a name"); Warn("no <materialref>"); NoMass("A"); });
            Case(g, "v01_no_solidref", () => { Warn("no <solidref>"); NoWarn("solid  not"); });
            Case(g, "v02_no_materialref", () => { Warn("no <materialref>"); NoMass("A"); });
            Case(g, "w02_child_outside", () => { NoMass("A"); Warn("stick out"); });
            Case(g, "w05_almost_fill_over2", () => { NoMass("A"); Warn("children take 1.000000001"); NoWarn("children take 1 cm3"); });
        }

        // ------------------------------------------------------------------
        // Третий заход: критик со статьями и новые тела
        // ------------------------------------------------------------------

        static void NoRefusal()
        {
            foreach (string warning in G.Warnings)
            {
                if ((warning.Contains("TWOPI") || warning.Contains("HALFPI") || warning.Contains(" PI")) && warning.Contains("refuses"))
                {
                    Problems.Add("ложная строка: " + warning);
                }
            }
        }

        static void Third()
        {
            const string g = "третий";
            Case(g, "o10a_const_twopi", () => { NoRefusal(); Vol("L2", Math.PI * 25.0 * 10.0 / 1000.0); Vol("L1", 2.0 * 1.0 * (0.3 + 2.0 / 3.0)); });
            Case(g, "a_entity", () =>
            {
                if (G.FatalError == null || !G.FatalError.Contains("ENTITY") || G.FatalError.Contains("DtdProcessing"))
                {
                    Problems.Add("отказ должен называть ENTITY простыми словами: " + G.FatalError);
                }
            });
            Case(g, "o05_tess_inverted", () => { NoVol("L1"); Warn("inside out"); });
            Case(g, "e02_flipped_face", () => { NoVol("A"); Warn("turned inside out"); NoWarn("not closed"); });
            Case(g, "30_tess_open", () => { NoVol("A"); Warn("not closed"); NoWarn("inside out"); });
            Case(g, "a_root_extension", () => { Vol("A", 3.92699); Warn("gdml_simple_extension"); Warn("<extension>"); });
            Case(g, "a_hex_names", () => { Vol("detLV", 3.92699); Comp("detLV", "Al", 1.0, 1e-9); Mass("detLV", 3.92699 * 2.699); NoWarn("not found"); });
            Case(g, "a_loop_opt", () => { Vol("A", 3.92699); Warn("solids: <loop>"); Warn("optical surface os"); NoWarn("unsupported body"); NoWarn("()"); NoMass("A"); });
            Case(g, "o02_partial_phi", () =>
            {
                // Сектор 30°…120°, rmin 10, rmax 40 мм, сдвиг x = 100, как `DiskExtent`.
                Range("L1", 0, 100.0 + 40.0 * Math.Cos(Math.PI * 2.0 / 3.0), 100.0 + 40.0 * Math.Cos(Math.PI / 6.0));
                Range("L1", 1, 10.0 * Math.Sin(Math.PI / 6.0), 40.0);
            });
            Case(g, "a_scale_near1", () => { Range("A", 0, 0.0, 10.0); Range("A", 2, 0.0, 50.0); Warn("1.00004 1.00004 1.00004"); });
            Case(g, "a_scale_ref", () => { Range("A", 2, -50.0, 0.0); Warn("mirror"); });
            Case(g, "a_scale_half_mirror", () => { NoBounds("A"); Warn("not a mirror"); });

            Case(g, "a_tube", () => { Vol("A", Math.PI * (100.0 - 25.0) * 20.0 / 1000.0); Range("A", 0, -10.0, 10.0); Range("A", 2, -10.0, 10.0); NoWarn("tube"); });
            Case(g, "a_tube_rad_cm", () => { Vol("A", Math.PI * 100.0 * 20.0 / 1000.0); NoRefusal(); NoWarn("partial"); });
            Case(g, "a_tube_phi", () =>
            {
                Vol("A", Math.PI * (900.0 - 100.0) * 20.0 / 4.0 / 1000.0);
                Range("A", 0, 30.0 * Math.Cos(Math.PI * 2.0 / 3.0), 30.0 * Math.Cos(Math.PI / 6.0));
                Range("A", 1, 10.0 * Math.Sin(Math.PI / 6.0), 30.0);
            });
            Case(g, "a_tube_nophi", () => { NoVol("A"); Warn("deltaphi is missing"); });
            Case(g, "a_tube_badradii", () => { NoVol("A"); Warn("not a ring"); });
            Case(g, "a_cone", () => { Vol("A", Math.PI / 3.0 * 30.0 * (100.0 + 200.0 + 400.0) / 1000.0); Range("A", 0, -20.0, 20.0); Range("A", 2, -15.0, 15.0); });
            Case(g, "a_cone_hole_phi", () =>
            {
                Vol("A", Math.PI / 3.0 * 30.0 * ((100.0 + 200.0 + 400.0) - (25.0 + 50.0 + 100.0)) / 2.0 / 1000.0);
                Range("A", 1, 0.0, 20.0);
            });
            Case(g, "a_cone_tip", () => { Vol("A", Math.PI / 3.0 * 30.0 * 400.0 / 1000.0); });
            Case(g, "a_union_disjoint", () => { Vol("A", 2.0); Range("A", 0, -5.0, 105.0); });
            Case(g, "a_union_boxes", () => { Vol("A", 1.5); Range("A", 0, -5.0, 10.0); });
            Case(g, "a_union_inside", () => { Vol("A", 1000.0); });
            Case(g, "a_union_overlap", () => { NoVol("A"); Warn("not axis-aligned boxes"); Range("A", 0, -10.0, 15.0); Header(0, 0); });
            Case(g, "a_union_nested", () => { Vol("A", 2.0 + Math.PI * 25.0 * 10.0 / 1000.0); Range("A", 0, -105.0, 105.0); });
            Case(g, "a_union_rot", () => { Vol("A", 12.0); Range("A", 0, -5.0, 110.0); Range("A", 1, -10.0, 10.0); });
            Case(g, "a_union_first_pos", () => { Vol("A", 2.0); Range("A", 1, -5.0, 105.0); });
            Case(g, "a_union_unknown", () => { NoVol("A"); Header(1, 0); });
            Case(g, "a_union_in_sub", () => { Vol("A", 998.0); });
            Case(g, "a_union_cycle", () => { NoVol("A"); Warn("reference cycle"); });

            // Критик `critGdml5`: дыры пробы. Без `degrees <= 0.0` труба с deltaphi 0 мерилась нулём молча;
            // без `firstBox` тело в осевой коробке второго считалось лежащим внутри трубы по её габариту.
            Case(g, "x03_tube_dphi0", () => { NoVol("V"); Warn("deltaphi = 0 is not positive"); NoWarn("finite"); });
            Case(g, "u14_box_in_tube", () => { NoVol("V"); Warn("not axis-aligned boxes"); });

            // Хвост указателя `0x…` в показе: причины объединения, вычитания и массы.
            Case(g, "u25_tube_hex", () => { NoVol("V"); Warn("overlap of A and B"); NoHex(); });
            Case(g, "a_hex_sub", () => { NoVol("V"); Warn("overlap with C"); NoHex(); });
            Case(g, "a_hex_part", () =>
            {
                NoVol("V");
                NoHex();
                if (!Summary.Contains("body A is not measured"))
                {
                    Problems.Add("в сводке нет причины с именем части: " + Summary);
                }
            });
            Case(g, "a_hex_mass", () => { Vol("A", 1.0); NoMass("A"); Warn("no <volumeref>"); NoHex(); });
        }

        /// <summary>Хвост указателя не доходит до сводки: как `G4GDMLRead::StripName`.</summary>
        static void NoHex()
        {
            if (Summary.IndexOf("0x", StringComparison.Ordinal) >= 0)
            {
                Problems.Add("хвост 0x в сводке: " + Summary.Substring(Summary.IndexOf("0x", StringComparison.Ordinal) - Math.Min(30, Summary.IndexOf("0x", StringComparison.Ordinal))));
            }
        }

        /// <summary>Примеры Geant4: читатель не падает, ни одной ложной строки про TWOPI.</summary>
        static void Examples(string folder)
        {
            const string g = "примеры";
            string[] files = Directory.GetFiles(folder, "*.gdml", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string path in files)
            {
                Problems = new List<string>();
                string observed;
                try
                {
                    G = GdmlGeometry.Read(path);
                    Summary = G.Describe();
                    NoRefusal();
                    NoHex();
                    int measured = 0;
                    foreach (GdmlVolumeInstance v in G.Volumes)
                    {
                        if (v.VolumeCm3 >= 0.0)
                        {
                            measured++;
                        }
                    }

                    observed = "томов=" + G.Volumes.Count + " с объёмом=" + measured + " строк=" + G.Warnings.Count
                               + (string.IsNullOrEmpty(G.FatalError) ? "" : " отказ");
                }
                catch (Exception ex)
                {
                    observed = "ИСКЛЮЧЕНИЕ " + ex.GetType().Name;
                    Problems.Add(observed);
                }

                Finish(g, Path.GetFileName(Path.GetDirectoryName(path)) + "/" + Path.GetFileName(path), observed);
            }
        }

        // ------------------------------------------------------------------
        // Контроль: то, что было верно, верно и осталось
        // ------------------------------------------------------------------

        static void Controls()
        {
            const string g = "контроль";
            Case(g, "a_ctrl_clean", () =>
            {
                Vol("A", 3.92699);
                Range("A", 2, 10.0, 60.0);
                if (G.Warnings.Count != 0)
                {
                    Problems.Add("чистый файл дал строк: " + G.Warnings.Count + " (" + G.Warnings[0] + ")");
                }
            });
            Case(g, "02_tube_hole", () => { Vol("A", 18.84956); });
            Case(g, "27_cup", () => { Vol("A", 19.50301); });
            Case(g, "35_partial_phi", () => { Vol("A", 15.70796); Warn("partial revolution 90"); });
            Case(g, "37_ctrl_gp", () => { Vol("A", 9.42478); });
            Case(g, "38_ctrl_exp", () => { Vol("A", 1.0); });
        }

        // ------------------------------------------------------------------
        // Настоящие файлы
        // ------------------------------------------------------------------

        static void Real(string folder, string dump)
        {
            const string g = "настоящие";
            string[] files = Directory.GetFiles(folder, "*.gdml");
            Array.Sort(files, StringComparer.Ordinal);
            StreamWriter writer = dump == null ? null : new StreamWriter(dump, false, new UTF8Encoding(false));
            foreach (string path in files)
            {
                Problems = new List<string>();
                string name = Path.GetFileNameWithoutExtension(path);
                long bestMs = long.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    G = GdmlGeometry.Read(path);
                    bestMs = Math.Min(bestMs, watch.ElapsedMilliseconds);
                }

                if (!string.IsNullOrEmpty(G.FatalError))
                {
                    Problems.Add("fatal: " + G.FatalError);
                }

                Header(0, 0);
                if (name.StartsWith("GS2020_") || name.StartsWith("geometry_"))
                {
                    Axis('z');
                    int open = 0;
                    foreach (string warning in G.Warnings)
                    {
                        if (warning.IndexOf("unpaired", StringComparison.Ordinal) >= 0)
                        {
                            open++;
                        }
                    }

                    if (open != 3)
                    {
                        Problems.Add("незамкнутых сеток " + open + ", ждали 3");
                    }

                    Warn("S_7175025104_1_31: mesh is not closed: 168");
                    Warn("S_7175025104_2_32: mesh is not closed: 168");
                    Warn("S_Open_CASCADE_STEP_translator_7_5_2_1_1_1_33: mesh is not closed: 104");
                }

                if (name.EndsWith("th232"))
                {
                    Near("LV_VesselWall.m", V("LV_VesselWall").MassG, 171.67, 0.01);
                }

                if (name.EndsWith("1000ml_water"))
                {
                    Near("LV_VesselWall.m", V("LV_VesselWall").MassG, 251.34, 0.01);
                }

                if (name == "RC103_detector")
                {
                    Warn("DetectorModule_log");

                    // Доли делятся на сумму, как `G4Material::FillVectors`: у Geant4 здесь 0.333333.
                    Comp("ESR_x_log", "C", 1.0 / 3.0, 1e-9);

                    // Z у вещества с долями не значит ничего: в файле <material Z="55"> с долями Cs и I.
                    Comp("Crystal_log", "Cs", 0.5, 1e-9);
                    Comp("Crystal_log", "I", 0.5, 1e-9);
                }

                Finish(g, name, "мс=" + bestMs + " томов=" + G.Volumes.Count + " веществ=" + G.MaterialCount
                                + " неподд=" + G.UnknownSolids + " сост=" + G.UnknownMaterials + " строк=" + G.Warnings.Count);

                if (writer != null)
                {
                    writer.WriteLine("FILE\t" + name + "\t" + G.Volumes.Count + "\t" + G.MaterialCount + "\t" + G.UnknownSolids + "\t" + G.UnknownMaterials + "\t" + G.Axis);
                    foreach (GdmlVolumeInstance v in G.Volumes)
                    {
                        writer.WriteLine("VOL\t" + name + "\t" + v.VolumeName + "\t" + (v.Solid == null ? "?" : v.Solid.Tag)
                            + "\t" + F(v.VolumeCm3) + "\t" + F(v.MassG)
                            + "\t" + (v.BoundsKnown ? F(v.Min[0]) + ";" + F(v.Max[0]) + ";" + F(v.Min[1]) + ";" + F(v.Max[1]) + ";" + F(v.Min[2]) + ";" + F(v.Max[2]) : "?")
                            + "\t" + (v.Material == null ? "?" : v.Material.CompositionText())
                            + "\t" + (v.Material == null ? "?" : F(v.Material.Density)));
                    }

                    foreach (string warning in G.Warnings)
                    {
                        writer.WriteLine("WARN\t" + name + "\t" + warning);
                    }
                }
            }

            if (writer != null)
            {
                writer.Dispose();
            }
        }
    }
}
