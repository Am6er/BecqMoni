using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// ОТБОР ЛИНИЙ ПОДГОНКИ КРИВИЗНЫ ТРАКТА κ (`AMBER295`, полоса `a270k`, 14.11.2026).
    /// Решения Amber вопросником 14.11.2026, дословно: «Отказ с причиной (Рекомендую)» (линия
    /// с неизмеренным центром или промахом больше порога в фит не идёт, годных меньше трёх —
    /// отказ); «Гаусс для κ (Рекомендую)» (центр линии — гаусс на прямой); «Считать свет
    /// суммой (Рекомендую)» (сумм-пик — светом L(E₁) + L(E₂)); «Отказ (Рекомендую)» (четыре
    /// линии с одной промахнувшейся); «Писать центр в файл (Рекомендую)»; «Две линии ≥ 1.5 МэВ
    /// (Рекомендую)».
    ///
    ///   tractkapparejectprobea270k --cases=&lt;файл&gt; [--ui=en|ru] [--weights=1] [--snap=&lt;каталог&gt;]
    ///
    /// Файл случаев — строки «имя|спектр|E1,E2,…|ожидание[|мутант]» (# — комментарий):
    ///   ожидание  ok[:κ₀:допуск] — κ подогнана и записана (и, если задано, |κ − κ₀| ≤ допуск);
    ///             sum:E          — подогнана, и строка E пошла светом суммы (её {E₁, E₂} названы);
    ///             refuse         — отказ: κ спектра побитово прежняя, причина в строке состояния,
    ///                              строке под таблицей и подсказке поля, отброшенные названы;
    ///             either:κ₀:допуск — отказ либо |κ − κ₀| ≤ допуск;
    ///             drop:E         — подогнана, отброшена ровно линия E (промахом);
    ///             auto:E         — подогнана, сумм-пик E (583.2 + 2614.5 у тория) кнопка взяла сама;
    ///             autofound:E    — сумм-пик E найден сам и пошёл в отбор (исход любой);
    ///             nosum          — ни одна строка не принята за сумм-пик (исход любой);
    ///             automiss:E     — отказ, причина: сумм-пик E не найден или лежит за краем спектра;
    ///             file           — подогнана; документ записан настоящим `SaveDocument` и открыт
    ///                              заново: центр пика каждой строки приехал из файла, κ по ним — та
    ///                              же побитово;
    ///             grow           — как file, но до записи спектр «набирался дальше» (отсчёты ×1.1):
    ///                              отсчёты канала строки уже не те, центр — из файла, κ та же
    ///                              побитово. ⛔ Контроль — тот же файл без элементов центра (как
    ///                              файл прежней версии): строки отброшены «центр не измерен»;
    ///             as:имя         — исход и κ побитово те же, что у случая «имя» (тот же спектр без мутанта);
    ///             free           — только печать.
    ///   мутант    miss:E:Δ (каналы), missf:E:Δ (доли FWHM) — запомненный центр строки E сдвинут;
    ///             nocentre:E     — отсчёты строки не те, что у спектра: центр не измеряется;
    ///             grow:F         — своя строка заведена во время набора: центр запомнен, затем спектр ×F.
    ///             Несколько — через «+».
    /// --weights=1 — режим «Веса» вкладки: меняет шкалу энергии; κ подгоняется с весами всегда
    ///             (решение Amber 14.11.2026 «„Веса“ для κ»), промах мерится без весов.
    ///
    /// Каждый случай: настоящая «Выполнить калибровку» вкладки (`RunCalibration` на оболочке
    /// главного окна), строки — пики линий выделением ±1.5 FWHM. Печать: центр масс и центр
    /// гаусса каждой линии, промах каждой линии от подгонки по всем без весов, итог.
    /// `--snap=` — вид вкладки в окне (только со скрытого стола): PNG на диск, не в git.
    /// Запускать из каталога приложения (рядом `config\`, `matdb.sqlite`, `nucdb.sqlite`).
    /// Коды: 0 — сошлось; 1 — не сошлось; 2 — ключи; 3 — сбой.
    /// </summary>
    static class TractKappaRejectProbeA270k
    {
        static int bad;
        /// <summary>Итоги уже пройденных случаев — для ожидания «as:имя».</summary>
        static readonly Dictionary<string, DCFwhmCalibrationView.TractCurvatureFit> Results = new Dictionary<string, DCFwhmCalibrationView.TractCurvatureFit>();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var opt = new Dictionary<string, string>();
            foreach (string a in args)
            {
                int eq = a.IndexOf('=');
                if (!a.StartsWith("--", StringComparison.Ordinal) || eq < 0)
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
                opt[a.Substring(2, eq - 2)] = a.Substring(eq + 1);
            }
            string casesPath, ui, snap, weights;
            if (!opt.TryGetValue("cases", out casesPath))
            {
                Console.Error.WriteLine("нужен --cases=<файл>");
                return 2;
            }
            ui = opt.TryGetValue("ui", out ui) ? ui : "en";
            snap = opt.TryGetValue("snap", out snap) ? snap : null;
            bool weighted = opt.TryGetValue("weights", out weights) && weights == "1";
            CultureInfo uiCulture = ui == "ru" ? new CultureInfo("ru-RU") : new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = uiCulture;
            Resources.Culture = uiCulture;
            // ⛔ культура потока — с запятой нарочно: числа в текстах обязаны остаться с точкой
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            try
            {
                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                NuclideDefinitionManager.GetInstance();
                Console.WriteLine("сборка: " + typeof(ResultData).Assembly.Location);
                Console.WriteLine("порог промаха: {0} FWHM; верхние линии от {1} кэВ; язык: {2}; веса: {3}",
                                  DCFwhmCalibrationView.TractCurvatureMissFwhm.ToString("R", Inv),
                                  DCFwhmCalibrationView.TractCurvatureHighLineKev.ToString("R", Inv), ui, weighted);
                if (snap != null)
                {
                    Directory.CreateDirectory(snap);
                }
                foreach (string raw in File.ReadAllLines(casesPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string[] f = line.Split('|');
                    if (f.Length < 4)
                    {
                        throw new ArgumentException("строка случая: " + line);
                    }
                    try
                    {
                        RunCase(f[0], f[1], f[2].Split(',').Select(t => double.Parse(t, Inv)).ToArray(), f[3], f.Length > 4 ? f[4] : "", ui, snap, weighted);
                    }
                    catch (Exception ex) when (!(ex is ArgumentException))
                    {
                        Check("случай " + f[0] + " без сбоя: " + Inner(ex), false);
                    }
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("СБОЙ: " + Inner(ex));
                return 3;
            }
            Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static void RunCase(string name, string file, double[] lineEnergies, string expect, string mutants, string ui, string snap, bool weighted)
        {
            Console.WriteLine();
            Console.WriteLine("=== {0}: {1} | линии {2} | ожидание {3}{4}", name, Path.GetFileName(file),
                              string.Join(",", lineEnergies.Select(e => e.ToString("0.###", Inv))), expect,
                              mutants.Length > 0 ? " | мутант " + mutants : "");
            string copy = Path.Combine(Path.GetTempPath(), "a270k_" + Guid.NewGuid().ToString("N") + ".xml");
            File.Copy(file, copy, true);
            DocEnergySpectrum doc = DocumentManager.GetInstance().OpenDocument(copy);
            Form host = null;
            try
            {
                ResultData rd = doc.ActiveResultData;
                EnergySpectrum es = rd.EnergySpectrum;
                var rows = new List<CalibrationPeak>();
                foreach (Pick p in Picks(rd, lineEnergies))
                {
                    int ch = (int)Math.Round(p.Centroid);
                    if (rows.Any(r => r.Channel == ch))
                    {
                        continue;
                    }
                    rows.Add(new CalibrationPeak { Channel = ch, Energy = p.Line, FWHM = p.Fwhm, EnergyLine = true, Count = p.Count });
                }
                foreach (string m in mutants.Split('+').Where(s => s.Length > 0))
                {
                    string[] mf = m.Split(':');
                    if (mf[0] == "grow")
                    {
                        // (критик `crit270k`) своя строка, заведённая во время набора: центр запомнен,
                        // затем спектр набрался дальше — строка обязана остаться своей
                        foreach (CalibrationPeak r0 in rows)
                        {
                            double c0 = DCFwhmCalibrationView.CentroidOf(r0, es);
                            r0.SpectrumCentroid = c0;
                            r0.SpectrumCentroidChannel = double.IsNaN(c0) ? -1 : r0.Channel;
                        }
                        double g = double.Parse(mf[1], Inv);
                        for (int i = 0; i < es.Spectrum.Length; i++)
                        {
                            es.Spectrum[i] = (int)Math.Round(es.Spectrum[i] * g);
                        }
                        Console.WriteLine("  grow: строк с разошедшимися отсчётами " + rows.Count(r0 => es.Spectrum[r0.Channel] != r0.Count));
                        continue;
                    }
                    if (mf[0] == "nocentre" && mf[1] == "all")
                    {
                        foreach (CalibrationPeak r0 in rows)
                        {
                            r0.Count = es.Spectrum[r0.Channel] + 1;
                            r0.SpectrumCentroid = double.NaN;
                            r0.SpectrumCentroidChannel = -1;
                        }
                        continue;
                    }
                    double e = double.Parse(mf[1], Inv);
                    CalibrationPeak r = rows.First(x => Math.Abs(x.Energy - e) < 1e-6);
                    if (mf[0] == "miss" || mf[0] == "missf")
                    {
                        // как у пробы критика: центр не измерен — сдвигать нечего, строка без центра
                        double c = DCFwhmCalibrationView.LineCentroid(es, r.Channel, r.FWHM);
                        if (double.IsNaN(c)) Console.WriteLine("  мутант: у линии " + mf[1] + " центр и так не измерен — строка без центра");
                        r.SpectrumCentroid = c + double.Parse(mf[2], Inv) * (mf[0] == "missf" ? r.FWHM : 1.0);
                        r.SpectrumCentroidChannel = double.IsNaN(c) ? -1 : r.Channel;
                    }
                    else if (mf[0] == "nocentre")
                    {
                        r.Count = es.Spectrum[r.Channel] + 1;
                        r.SpectrumCentroid = double.NaN;
                        r.SpectrumCentroidChannel = -1;
                    }
                    else
                    {
                        throw new ArgumentException("неизвестный мутант: " + m);
                    }
                }
                var com = new SpectrumAriphmetics();
                foreach (CalibrationPeak r in rows)
                {
                    double c = DCFwhmCalibrationView.CentroidOf(r, es);
                    int half = Math.Max(3, (int)Math.Ceiling(1.5 * r.FWHM));
                    double cm = com.FindCentroid(es, r.Channel, Math.Max(0, r.Channel - half), Math.Min(es.NumberOfChannels - 1, r.Channel + half), true);
                    Console.WriteLine("  строка {0} кэВ: канал {1}, FWHM {2} кан.; центр масс {3}; центр для κ {4}{5}", r.Energy.ToString("0.###", Inv), r.Channel,
                                      r.FWHM.ToString("0.0", Inv), cm.ToString("0.000", Inv),
                                      double.IsNaN(c) ? "НЕ ИЗМЕРЕН" : c.ToString("0.000", Inv),
                                      double.IsNaN(c) ? "" : " (гаусс − ц.м. = " + ((c - cm) / r.FWHM).ToString("+0.000;-0.000", Inv) + " FWHM)");
                }
                rd.CalibrationLines.Clear();
                rd.CalibrationLines.AddRange(rows);

                MainForm form = Shell(doc);
                DCEnergyCalibrationView view = new DCEnergyCalibrationView(form);
                EnergyCalibration original = es.EnergyCalibration.Clone();
                Call(view, "SetEnergyCalibration", original, original);
                Call(view, "UpdateFwhmCalibration", true);
                if (snap != null)
                {
                    host = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), Size = new Size(1000, 760), Text = "a270k " + name };
                    view.TopLevel = false;
                    view.FormBorderStyle = FormBorderStyle.None;
                    view.Dock = DockStyle.Fill;
                    host.Controls.Add(view);
                    view.Show();
                    host.Show();
                    Application.DoEvents();
                    Call(view, "SetEnergyCalibration", original, original);
                    Call(view, "UpdateFwhmCalibration", true);
                }
                // режим «Веса» вкладки меняет только шкалу энергии: κ с `AMBER295` всегда с весами
                ((CheckBox)Field(view, "checkBox2")).Checked = weighted;
                double before = rd.TractCurvature;
                bool ownBefore = rd.HasOwnTractCurvature;
                string material = DCFwhmCalibrationView.ScintillatorOf(rd);
                bool done = (bool)Call(view, "RunCalibration");
                var fit = (DCFwhmCalibrationView.TractCurvatureFit)Field(view, "lastTractFit");
                double after = rd.TractCurvature;
                string status = ((ToolStripStatusLabel)Field(form, "toolStripStatusLabel1")).Text;
                string note = (string)Field(view, "tractCurvatureNote");
                string summary = (string)Field(view, "runSummary");

                Console.WriteLine("  вещество «{0}»; веса {1}; κ спектра до {2} ({3})", material, weighted ? "да" : "нет",
                                  before.ToString("R", Inv), ownBefore ? "своя" : "прибора");
                Console.WriteLine("  промах линий от подгонки по всем без весов (доли FWHM): " + Misses(rows, es, material));
                Console.WriteLine("  итог: " + Describe(fit));
                Console.WriteLine("  κ спектра после: {0} ({1})", after.ToString("R", Inv), rd.HasOwnTractCurvature ? "своя" : "прибора");
                Console.WriteLine("  строка состояния: " + status);
                Console.WriteLine("  строка под таблицей: " + summary);
                Console.WriteLine("  подсказка поля κ: " + note);

                // шкала энергии обязана приняться там, где κ ждут; у спектров, где её отвергает
                // сторож изгиба (AS1Pro, LaBr — так и до правки), κ не подгоняется вовсе
                bool needEnergy = !(expect == "refuse" || expect == "free");
                if (needEnergy || done)
                {
                    Check("«Выполнить калибровку»: шкала энергии принята", done);
                    Check("подгонка κ была", fit != null);
                }
                else
                {
                    Console.WriteLine("  шкала энергии отвергнута сторожем изгиба — κ не подгонялась (так и до правки)");
                    Check("κ спектра побитово прежняя", BitConverter.DoubleToInt64Bits(rd.TractCurvature) == BitConverter.DoubleToInt64Bits(before));
                }
                if (fit == null) return;
                Check("в строке состояния нет запятой-разделителя", !Regex.IsMatch(status, @"\d,\d"));
                bool unchanged = BitConverter.DoubleToInt64Bits(after) == BitConverter.DoubleToInt64Bits(before) && rd.HasOwnTractCurvature == ownBefore;
                if (fit.Ok)
                {
                    Check("κ спектра = подогнанной", after.Equals(fit.Kappa) && rd.HasOwnTractCurvature);
                    Check("строка состояния говорит κ", status.Contains(fit.Kappa.ToString("0.00000", Inv)));
                    Check("строка под таблицей держит итог FWHM", summary != null && summary.Contains("FWHM"));
                }
                else
                {
                    Check("отказ: κ спектра побитово прежняя, признак своей κ прежний", unchanged);
                    Check("причина — в строке состояния, строке под таблицей и подсказке поля",
                          fit.Refusal != null && status.Contains(fit.Refusal) && summary != null && summary.Contains(fit.Refusal) && note == fit.Refusal);
                    Check("строка под таблицей держит итог FWHM и после отказа", summary != null && summary.Contains("FWHM"));
                }
                // (находка 2 второго круга критика) итог FWHM виден: он стоит ПЕРЕД итогом κ
                Check("итог FWHM стоит перед итогом κ (виден в первых строках)", summary != null && summary.IndexOf("FWHM:", StringComparison.Ordinal) >= 0
                      && summary.IndexOf("FWHM:", StringComparison.Ordinal) < summary.IndexOf('κ'));
                string text = Status(fit);
                // строки без центра свёрнуты одним перечнем: шесть названы, остальные — «+N»
                var noCentre = fit.Rejected.Where(r => r.CentreNotMeasured).ToList();
                foreach (var r in fit.Rejected.Where(r => !r.CentreNotMeasured).Concat(noCentre.Take(6)))
                {
                    string e = r.Energy.ToString("0.###", Inv);
                    Check("отброшенная линия " + e + " кэВ названа в итоге", text.Contains(e));
                }
                if (noCentre.Count > 6)
                {
                    Check("остальные строки без центра — «+" + (noCentre.Count - 6) + "»", text.Contains(" +" + (noCentre.Count - 6).ToString(Inv)));
                }
                Check("итог на языке окна (" + ui + ")", Lang(text) == ui);

                string[] ex = expect.Split(':');
                switch (ex[0])
                {
                    case "ok":
                        Check("κ подогнана", fit.Ok);
                        if (ex.Length == 3)
                        {
                            double k0 = double.Parse(ex[1], Inv), tol = double.Parse(ex[2], Inv);
                            Check("κ в " + ex[2] + " от " + ex[1], fit.Ok && Math.Abs(fit.Kappa - k0) <= tol);
                        }
                        break;
                    case "sum":
                        double es0 = double.Parse(ex[1], Inv);
                        Check("κ подогнана", fit.Ok);
                        Check("строка " + ex[1] + " кэВ пошла светом суммы", fit.SumPeaks.Any(sp => Math.Abs(sp[0] - es0) < 1e-6));
                        Check("и итог об этом говорит", text.Contains(es0.ToString("0.###", Inv) + " = "));
                        break;
                    case "refuse":
                        Check("отказ", !fit.Ok);
                        break;
                    case "auto":
                        double ea = double.Parse(ex[1], Inv);
                        Check("κ подогнана", fit.Ok);
                        Check("сумм-пик " + ex[1] + " кэВ взят сам", fit.AutoSum != null && Math.Abs(fit.AutoSum[0] - ea) < 0.01);
                        Check("и итог об этом говорит", text.Contains(ea.ToString("0.###", Inv)));
                        break;
                    case "nosum":
                        Check("ни одна строка не принята за сумм-пик (одиночная линия на энергии суммы — фотопик)", fit.SumPeaks.Count == 0 && fit.AutoSum == null);
                        break;
                    case "autofound":
                        double ef = double.Parse(ex[1], Inv);
                        Check("сумм-пик " + ex[1] + " кэВ найден сам и пошёл в отбор", fit.AutoSum != null && Math.Abs(fit.AutoSum[0] - ef) < 0.01);
                        Check("итог называет взятый сам сумм-пик", text.Contains(ef.ToString("0.###", Inv)));
                        break;
                    case "automiss":
                        double em = double.Parse(ex[1], Inv);
                        Check("отказ", !fit.Ok);
                        Check("причина говорит, что сумм-пик " + ex[1] + " кэВ не найден (или за краем спектра)", fit.AutoSumMissing != null && fit.Refusal != null
                              && fit.Refusal.Contains(em.ToString("0.###", Inv)));
                        break;
                    case "either":
                        double ek = double.Parse(ex[1], Inv), et = double.Parse(ex[2], Inv);
                        Check("отказ или κ в " + ex[2] + " от " + ex[1], !fit.Ok || Math.Abs(fit.Kappa - ek) <= et);
                        break;
                    case "drop":
                        double de = double.Parse(ex[1], Inv);
                        Check("подогнана", fit.Ok);
                        Check("отброшена ровно линия " + ex[1] + " (промахом)", fit.Rejected.Count(r => !r.CentreNotMeasured) == 1
                              && fit.Rejected.Any(r => !r.CentreNotMeasured && Math.Abs(r.Energy - de) < 1e-6));
                        break;
                    case "file":
                    case "grow":
                        Check("подогнана", fit.Ok);
                        if (fit.Ok)
                        {
                            RoundTrip(doc, rd, copy, fit, material, weighted, ex[0] == "grow");
                        }
                        break;
                    case "as":
                        // исход тот же, что у случая ex[1] (тот же спектр без мутанта), κ побитово
                        DCFwhmCalibrationView.TractCurvatureFit was;
                        Check("исход как у «" + ex[1] + "» (" + (fit.Ok ? fit.Kappa.ToString("R", Inv) : "отказ") + ")",
                              Results.TryGetValue(ex[1], out was) && was.Ok == fit.Ok
                              && (!fit.Ok || BitConverter.DoubleToInt64Bits(was.Kappa) == BitConverter.DoubleToInt64Bits(fit.Kappa)));
                        break;
                    case "free":
                        break;
                    default:
                        throw new ArgumentException("неизвестное ожидание: " + expect);
                }
                if (fit.AutoSum != null)
                {
                    var ch = new List<double>(); var en = new List<double>(); var pr = new List<double[]>();
                    foreach (CalibrationPeak r in rows)
                    {
                        double c = DCFwhmCalibrationView.CentroidOf(r, es);
                        if (double.IsNaN(c)) continue;
                        ch.Add(c); en.Add(r.Energy); pr.Add(null);
                    }
                    ch.Add(fit.AutoSum[3]); en.Add(fit.AutoSum[0]); pr.Add(new[] { fit.AutoSum[1], fit.AutoSum[2] });
                    var free = DCFwhmCalibrationView.FitTractCurvature(ch, en, null, null, pr, es.EnergyCalibration, material);
                    Console.WriteLine("  справка: κ с сумм-пиком без отбора и правил — " + Describe(free));
                }
                Results[name] = fit;
                Console.WriteLine("KAPPA\t{0}\t{1}\t{2}", name, fit.Ok ? fit.Kappa.ToString("R", Inv) : "NaN",
                                  fit.Ok ? "" : Regex.Replace(fit.Refusal ?? "", @"\s+", " "));

                if (snap != null && host != null)
                {
                    Application.DoEvents();
                    string png = Path.Combine(snap, name + "_" + ui + ".png");
                    using (var bmp = new Bitmap(host.Width, host.Height))
                    {
                        host.DrawToBitmap(bmp, new Rectangle(0, 0, host.Width, host.Height));
                        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    var tip = (ToolTip)Field(view, "toolTip1");
                    var box = (Control)Field(view, "tractCurvatureTextBox");
                    string tipText = tip.GetToolTip(box);
                    Console.WriteLine("  снимок: " + png);
                    Console.WriteLine("  подсказка поля κ целиком:");
                    foreach (string l in tipText.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
                    {
                        Console.WriteLine("    | " + l);
                    }
                    int longest = tipText.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Max(l => l.Length);
                    Check("подсказка блоком: строка не длиннее 80 знаков (" + longest + ")", longest <= 80);
                }
            }
            finally
            {
                if (host != null)
                {
                    host.Close();
                    host.Dispose();
                }
                DocumentManager.GetInstance().DocumentList.Remove(doc);
                try { File.Delete(copy); } catch (IOException) { }
            }
        }

        /// <summary>
        /// Запись документа настоящим `SaveDocument` и чтение заново: центр каждой строки — из
        /// файла, κ по ним побитово та же. <paramref name="grow"/> — до записи спектр набрался
        /// дальше (отсчёты ×1.1); ⛔ контроль — тот же файл без элементов центра.
        /// </summary>
        static void RoundTrip(DocEnergySpectrum doc, ResultData rd, string path, DCFwhmCalibrationView.TractCurvatureFit fit, string material, bool weighted, bool grow)
        {
            var cal = rd.EnergySpectrum.EnergyCalibration;
            int withCentre = rd.CalibrationLines.Count(r => r.EnergyLine && r.SpectrumCentroidChannel == r.Channel && !double.IsNaN(r.SpectrumCentroid));
            Check("строки несут центр до записи (" + withCentre + " из " + rd.CalibrationLines.Count(r => r.EnergyLine) + ")",
                  withCentre == fit.Lines + fit.Rejected.Count(r => !r.CentreNotMeasured));
            if (grow)
            {
                int[] s = rd.EnergySpectrum.Spectrum;
                for (int i = 0; i < s.Length; i++)
                {
                    s[i] = (int)Math.Round(s[i] * 1.1);
                }
                int moved = rd.CalibrationLines.Count(r => r.EnergyLine && s[r.Channel] != r.Count);
                Check("спектр «набрался дальше»: отсчёты канала разошлись с весом у " + moved + " строк", moved > 0);
            }
            Check("документ записан настоящим SaveDocument", DocumentManager.GetInstance().SaveDocument(doc));
            string xml = File.ReadAllText(path, Encoding.UTF8);
            int elements = Regex.Matches(xml, "<Centroid>").Count;
            Console.WriteLine("  в файле элементов <Centroid>: {0}", elements);
            Check("центр пика записан в файл (" + elements + ")", elements >= withCentre && withCentre > 0);
            // документ закрывается: иначе «открыть» вернуло бы его же из памяти, а не файл
            DocumentManager.GetInstance().DocumentList.Remove(doc);

            DCFwhmCalibrationView.TractCurvatureFit reopened = Reopen(path, material, weighted, cal);
            Console.WriteLine("  после открытия файла: " + Describe(reopened));
            Check("после открытия κ та же побитово (" + (reopened.Ok ? reopened.Kappa.ToString("R", Inv) : "отказ") + ")",
                  reopened.Ok && BitConverter.DoubleToInt64Bits(reopened.Kappa) == BitConverter.DoubleToInt64Bits(fit.Kappa));

            // ⛔ контроль: файл прежней версии — без элементов центра
            string old = path + ".old.xml";
            File.WriteAllText(old, Regex.Replace(Regex.Replace(xml, @"\s*<Centroid>[^<]*</Centroid>", ""), @"\s*<CentroidChannel>[^<]*</CentroidChannel>", ""), new UTF8Encoding(false));
            try
            {
                DCFwhmCalibrationView.TractCurvatureFit legacy = Reopen(old, material, weighted, cal);
                Console.WriteLine("  тот же файл без центра (прежний формат): " + Describe(legacy));
                if (grow)
                {
                    Check("контроль: без центра в файле строки отброшены «центр не измерен» (" + legacy.Rejected.Count(r => r.CentreNotMeasured) + ")",
                          legacy.Rejected.Count(r => r.CentreNotMeasured) > 0);
                }
                else
                {
                    Check("контроль: без центра в файле свои строки мерятся по спектру — κ та же",
                          legacy.Ok && BitConverter.DoubleToInt64Bits(legacy.Kappa) == BitConverter.DoubleToInt64Bits(fit.Kappa));
                }
            }
            finally
            {
                try { File.Delete(old); } catch (IOException) { }
            }
        }

        static DCFwhmCalibrationView.TractCurvatureFit Reopen(string path, string material, bool weighted, EnergyCalibration cal)
        {
            DocEnergySpectrum again = DocumentManager.GetInstance().OpenDocument(path);
            try
            {
                ResultData rd = again.ActiveResultData;
                Check("файл прочитан заново (новый документ, " + rd.CalibrationLines.Count + " строк)", rd.CalibrationLines.Count > 0);
                return DCFwhmCalibrationView.FitTractCurvature(rd.CalibrationLines, rd.EnergySpectrum, cal, material, true); // κ — с весами всегда (как приложение)
            }
            finally
            {
                DocumentManager.GetInstance().DocumentList.Remove(again);
            }
        }

        /// <summary>Промах линий в долях FWHM от подгонки «свет → канал» по всем им без весов — той же арифметикой ядра.</summary>
        static string Misses(List<CalibrationPeak> rows, EnergySpectrum es, string material)
        {
            var ch = new List<double>();
            var e = new List<double>();
            var fw = new List<double>();
            foreach (CalibrationPeak r in rows.OrderBy(r => r.Channel))
            {
                double c = DCFwhmCalibrationView.CentroidOf(r, es);
                if (double.IsNaN(c)) continue;
                ch.Add(c); e.Add(r.Energy); fw.Add(r.FWHM);
            }
            if (ch.Count < 4)
            {
                return "измеренных линий " + ch.Count + " — квадрат проходит через все, промаха нет";
            }
            var an = new FsaAnalyzer();
            if (string.IsNullOrEmpty(material) && string.IsNullOrEmpty(an.AnchorLightCurve))
            {
                return "вещество не названо";
            }
            string curve = string.IsNullOrEmpty(an.AnchorLightCurve) ? CurveFor(material) : an.AnchorLightCurve;
            var table = rows.Select(r => r.Energy).ToList();
            var lights = new List<double>();
            for (int i = 0; i < e.Count; i++)
            {
                double[] parts = DCFwhmCalibrationView.SumPartsOf(e[i], table);
                lights.Add(parts == null ? Light(an, curve, e[i]) : Light(an, curve, parts[0]) + Light(an, curve, parts[1]));
            }
            MethodInfo solve = typeof(DCFwhmCalibrationView).GetMethod("SolveTractQuadratic", F);
            object[] a = { ch, lights, Enumerable.Repeat(1.0, ch.Count).ToList(), 0.0 };
            double[] p = (double[])solve.Invoke(null, a);
            if (p == null)
            {
                return "квадрат не решается";
            }
            double mid = (double)a[3];
            var outParts = new List<string>();
            double worst = 0.0;
            for (int i = 0; i < ch.Count; i++)
            {
                double t = lights[i] - mid;
                double miss = Math.Abs(p[0] + p[1] * t + p[2] * t * t - ch[i]) / Math.Max(fw[i], 1.0);
                worst = Math.Max(worst, miss);
                outParts.Add(string.Format(Inv, "{0}: {1}", e[i].ToString("0.###", Inv), miss.ToString("0.000", Inv)));
            }
            Console.WriteLine("MISS\t{0}\t{1}", ch.Count, worst.ToString("R", Inv));
            return string.Join("; ", outParts);
        }

        static double Light(FsaAnalyzer an, string curve, double e)
        {
            double s = an.AnchorLightMaxKev > 0.0 && e > an.AnchorLightMaxKev ? 0.0 : ShiftKev(curve, e, an.AnchorLightReferenceKev);
            return e + s;
        }

        static Type LightScaleType
        {
            get { return typeof(FsaAnalyzer).Assembly.GetType("BecquerelMonitor.FullSpectrumAnalysis.FsaLightScale", true); }
        }

        static string CurveFor(string material)
        {
            return (string)LightScaleType.GetMethod("CurveFor", F).Invoke(null, new object[] { material });
        }

        static double ShiftKev(string curve, double e, double reference)
        {
            return (double)LightScaleType.GetMethod("ShiftKev", F).Invoke(null, new object[] { curve, e, reference });
        }

        /// <summary>Итог κ строкой состояния — тем же текстом, что приложение (`TractCurvatureStatus`, внутренний: отражением).</summary>
        static string Status(DCFwhmCalibrationView.TractCurvatureFit f)
        {
            return (string)typeof(DCFwhmCalibrationView).GetMethod("TractCurvatureStatus", F).Invoke(null, new object[] { f });
        }

        static string Lang(string text)
        {
            return text.Any(c => c >= 'А' && c <= 'я') ? "ru" : "en";
        }

        static string Describe(DCFwhmCalibrationView.TractCurvatureFit f)
        {
            if (f == null)
            {
                return "подгонки не было";
            }
            string rejected = f.Rejected.Count == 0 ? "" : "; отброшено: " + string.Join(", ", f.Rejected.Select(r => r.Energy.ToString("0.###", Inv)
                + (r.CentreNotMeasured ? " (центр не измерен)" : " (промах " + r.MissFwhm.ToString("0.000", Inv) + " FWHM)")));
            // сумм-пик, взятый сам, называется один раз — своей записью ниже (находка 4 критика)
            var listed = f.SumPeaks.Where(sp => f.AutoSum == null || Math.Abs(sp[0] - f.AutoSum[0]) > 1e-9).ToList();
            string sums = listed.Count == 0 ? "" : "; сумм-пики: " + string.Join(", ", listed.Select(sp => sp[0].ToString("0.###", Inv)));
            if (f.AutoSum != null) sums += "; сам взят сумм-пик " + f.AutoSum[0].ToString("0.###", Inv) + " у канала " + f.AutoSum[3].ToString("0.0", Inv);
            if (f.AutoSumMissing != null) sums += "; сумм-пик " + f.AutoSumMissing[0].ToString("0.###", Inv) + " не найден, ожидался у канала " + f.AutoSumMissing[3].ToString("0", Inv);
            return (f.Ok
                ? string.Format(Inv, "κ = {0} 1/МэВ (линий {1}, наибольший промах {2} кан.)", f.Kappa.ToString("0.00000", Inv), f.Lines, f.WorstChannels.ToString("0.000", Inv))
                : "ОТКАЗ: " + f.Refusal) + rejected + sums;
        }

        sealed class Pick
        {
            public double Line, Centroid, Fwhm;
            public int Count;
        }

        /// <summary>Пики линий: окно ±1.5 FWHM вокруг канала линии, центр масс (как `TractCurvatureFitProbeA270` и проба критика `crit270k`).</summary>
        static List<Pick> Picks(ResultData rd, double[] lines)
        {
            EnergySpectrum es = rd.EnergySpectrum;
            EnergyCalibration cal = es.EnergyCalibration;
            var picks = new List<Pick>();
            foreach (double e in lines)
            {
                double ch0 = cal.EnergyToChannel(e, maxChannels: es.NumberOfChannels);
                double centroid = ch0;
                double fw = 10.0;
                for (int pass = 0; pass < 3; pass++)
                {
                    int center = (int)Math.Round(centroid);
                    fw = rd.FwhmCalibration != null ? rd.FwhmCalibration.ChannelToFwhm(Math.Max(1, center)) : 10.0;
                    int half = Math.Max(3, (int)Math.Ceiling(1.5 * fw));
                    int start = Math.Max(0, center - half), end = Math.Min(es.NumberOfChannels - 1, center + half);
                    int max = start;
                    for (int c = start; c <= end; c++) if (es.Spectrum[c] > es.Spectrum[max]) max = c;
                    if (pass == 0)
                    {
                        centroid = max;
                        continue;
                    }
                    centroid = new SpectrumAriphmetics().FindCentroid(es, max, start, end, true);
                }
                int k = (int)Math.Round(centroid);
                picks.Add(new Pick { Line = e, Centroid = centroid, Fwhm = fw, Count = k >= 0 && k < es.Spectrum.Length ? es.Spectrum[k] : 0 });
            }
            return picks;
        }

        /// <summary>Оболочка главного окна без окна: только то, что зовёт вкладка калибровки (как у `UnifiedCalibrationProbeA276`).</summary>
        static MainForm Shell(DocEnergySpectrum doc)
        {
            MainForm form = (MainForm)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(MainForm));
            SetField(form, "activeDocument", doc);
            SetField(form, "documentManager", DocumentManager.GetInstance());
            SetField(form, "dcResultViewList", new List<DCResultView>());
            SetField(form, "toolStripStatusLabel1", new ToolStripStatusLabel());
            try { SetField(form, "dcPeakDetectionView", new DCPeakDetectionView(form)); } catch (Exception) { }
            return form;
        }

        static object Field(object o, string name)
        {
            return Field(o.GetType(), name).GetValue(o);
        }

        static FieldInfo Field(Type t, string name)
        {
            for (; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, F | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            throw new MissingFieldException(name);
        }

        static void SetField(object o, string name, object value)
        {
            Field(o.GetType(), name).SetValue(o, value);
        }

        static object Call(object target, string name, params object[] args)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                foreach (MethodInfo m in t.GetMethods(F | BindingFlags.DeclaredOnly))
                {
                    if (m.Name == name && !m.IsStatic && m.GetParameters().Length == args.Length)
                    {
                        try { return m.Invoke(target, args); }
                        catch (TargetInvocationException ex) { throw ex.InnerException; }
                    }
                }
            }
            throw new MissingMethodException(target.GetType().Name, name);
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine("  {0} {1}", ok ? "да " : "НЕТ", what);
            if (!ok) bad++;
        }

        static string Inner(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split('\n')[0].Trim();
        }
    }
}
