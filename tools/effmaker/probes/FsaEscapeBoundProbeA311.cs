using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER311`, полосы `a311f`/`a311g`) ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ РЫЧАГОВ `FsaAnalyzer.EscapeBoundToParent` и
    /// `FsaAnalyzer.EscapeOutOfTopZ`: СУММА образов вылета одной родительской линии (SE + DE) без матрицы не больше
    /// границы σ_пар/σ_фото × пик этой линии, и образы — не вершина порога отсева. Оба рычага умолчанием выключены.
    ///
    /// Прямой вызов анализатора без матрицы на спектре корпуса (`--spectrum=`, фон из файла). Состав: `--lines=E1,E2,…`
    /// — по одной линии на компонент (`L1`, `L2`, …) и/или `--parent=E:I,E:I,…` — ОДИН многолинейный компонент `P`
    /// (интенсивности в %); имён нуклидов проба не знает. Образы вылета — `FsaLibrary.EscapeImages` при веществе
    /// кристалла `--crystal=` (умолчание «Sodium iodide», `--density=`). Группа — родитель и его линия (SE и DE
    /// этой линии). Площадь ЛИНИИ родителя считается пробой независимо от кода границы: кривая родителя
    /// (<c>FsaComponentResult.Curve</c>) × доля линии по весам «выход × эффективность» (допуск 1e-3 — край полосы и
    /// хвосты формы); площади образов — их кривые.
    ///
    ///   (1) рычаги выключены: печатаются группы БОЛЬШЕ границы — сцена видит дефект (`--expect-violation`);
    ///   (2) граница: ни одна группа не больше границы, счётчик > 0 при нарушителе в (1), заметка называет образы
    ///       групп, стоящих на границе в итоге;
    ///   (3) вещество не задано (граница NaN): побитно как без рычага, счётчик 0;
    ///   (4) повтор (1) побитно (детерминизм);
    ///   (5) образы вне вершины порога: вершина с рычагом не выше; если вершиной был образ — строго ниже;
    ///   (6) σ_пар/σ_фото положительна и растёт с энергией;
    ///   (7) ОТСЕВ ВЫКЛЮЧЕН (`RefitZ = 0`), чтобы нарушитель не уходил отсевом: каждая группа, нарушавшая
    ///       границу без рычага ЗАМЕТНО (сумма больше полутора границ), с рычагом стоит РОВНО НА границе (мутант
    ///       «граница строже физики», κ×0.5); слабого нарушителя перевзвешивание Хубера может увести с грани
    ///       законно — его судит только «не больше границы». `--expect-pair` — у нарушавшей группы без рычага ОБА
    ///       образа ненулевые: тогда граница на каждый образ (мутант) пустила бы сумму до двух границ, и это
    ///       ловит «групп больше границы нет».
    ///
    ///   (8) `--session` — ПУТЬ ПРИЛОЖЕНИЯ: сеанс документа (`FsaAnalysisSession`, `FsaCalculationOptions.Of`, состав
    ///       из подписей пиков; `--set=` — набор нуклидов) с обоими рычагами (крючок
    ///       `FsaAnalysisSession.ProbeAnalyzerHook`): ни одного принятого решения граней с отрицательным образом
    ///       (`FsaResult.EscapeNegativeAccepted`, мутант «отрицательный опорный принимается»); `--expect-switch` —
    ///       ветка смены опорного исполнилась (`EscapePivotSwitches` > 0, сцена — торий без матрицы). Сцены (1)–(7)
    ///       в этом режиме не идут.
    ///
    /// Коды: 0 — сошлось; 1 — нет; 2 — ключи или файл.
    /// </summary>
    static class FsaEscapeBoundProbeA311
    {
        const double Tol = 1.0E-3;
        // сверка x_р⁰ решателя (те же веса Хубера) с независимым разбором без образов
        const double TolRef = 0.05;
        // сверка x_р⁰ ЗАМЕТКИ с разбором без образов своей группы — строже: на сценах пробы правильный код расходится
        // на 0.003–0.09 %, а граница от x_р совместного решения (мутант М11) — на 0.18–1 % (Y-88: 112.2 против 113.3)
        const double TolNote = 0.005;
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            FsaTuningReport.Snapshot();
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string path = null;
            var lines = new List<double>();
            var parent = new List<KeyValuePair<double, double>>();
            string crystal = "Sodium iodide";
            double density = 3.667;
            bool expectViolation = false;
            bool expectPair = false;
            bool session = false;
            bool expectSwitch = false;
            string setName = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--lines=", StringComparison.Ordinal))
                {
                    foreach (string e in a.Substring(8).Split(','))
                    {
                        lines.Add(double.Parse(e, CultureInfo.InvariantCulture));
                    }
                }
                else if (a.StartsWith("--parent=", StringComparison.Ordinal))
                {
                    foreach (string e in a.Substring(9).Split(','))
                    {
                        string[] ei = e.Split(':');
                        parent.Add(new KeyValuePair<double, double>(double.Parse(ei[0], CultureInfo.InvariantCulture),
                                                                    double.Parse(ei[1], CultureInfo.InvariantCulture)));
                    }
                }
                else if (a.StartsWith("--crystal=", StringComparison.Ordinal)) crystal = a.Substring(10);
                else if (a.StartsWith("--density=", StringComparison.Ordinal)) density = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a == "--expect-violation") expectViolation = true;
                else if (a == "--expect-pair") expectPair = true;
                else if (a == "--session") session = true;
                else if (a == "--expect-switch") expectSwitch = true;
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (session)
            {
                return Session(path, setName, expectSwitch);
            }

            if (path == null || !File.Exists(path) || (lines.Count == 0 && parent.Count == 0))
            {
                Console.Error.WriteLine("нужны --spectrum=<файл спектра> и --lines=E1,… и/или --parent=E:I,…");
                return 2;
            }

            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(crystal);
            GeometryMaterial material = entry != null ? GeometryMaterialLibrary.Make(entry, density) : null;
            if (material == null) { Console.Error.WriteLine("вещества «{0}» в библиотеке нет", crystal); return 2; }
            var fractions = new Dictionary<int, double>(material.Fractions);

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            // (6) довод границы
            double c1100 = FsaLibrary.PairToPhotoRatio(fractions, 1100.0);
            double c1461 = FsaLibrary.PairToPhotoRatio(fractions, 1460.82);
            double c2614 = FsaLibrary.PairToPhotoRatio(fractions, 2614.5);
            Console.WriteLine("(6) σ_пар/σ_фото «{0}»: 1100 кэВ {1:G5}; 1460.82 кэВ {2:G5}; 2614.5 кэВ {3:G5}",
                              crystal, c1100, c1461, c2614);
            Same("(6) граница положительна и растёт с энергией", true, c1100 > 0.0 && c1461 > c1100 && c2614 > c1461);
            Same("(6) без вещества границы нет (NaN)", true, double.IsNaN(FsaLibrary.PairToPhotoRatio(null, 1460.82)));

            Scene off = Run(path, nuclides, lines, parent, fractions, false, false, false);
            Same("(1) разбор состоялся", true, off.Result != null);
            if (off.Result == null)
            {
                return Finish();
            }

            int violations = Report("(1) рычаги выключены", off, null).Over.Count;
            if (expectViolation)
            {
                Same("(1) сцена видит дефект: группа больше границы есть", true, violations > 0);
            }

            Same("(1) счётчик границы без рычага — нуль", 0, off.Result.EscapeBoundHits);

            Scene on = Run(path, nuclides, lines, parent, fractions, true, false, false);
            Dictionary<string, Scene> bare = References(path, nuclides, lines, parent, fractions, false, on);
            Same("(2) разбор с границей состоялся", true, on.Result != null && AllDone(bare));
            if (on.Result != null && AllDone(bare))
            {
                Verdict v = Report("(2) граница включена", on, bare);
                Same("(2) групп больше границы нет", 0, v.Over.Count);
                Same("(2) правило тяги: пик родителя не выше, чем без образов (" + string.Join(", ", v.Pulled) + ")", 0, v.Pulled.Count);
                Same("(2) x_р⁰ заметки сходится с разбором без образов (" + string.Join(", ", v.NoteOffRef) + ")", 0, v.NoteOffRef.Count);
                Same("(2) отрицательных образов нет", 0, v.Negative.Count + on.Result.EscapeNegativeAccepted);
                if (violations > 0)
                {
                    Same("(2) граница задета (счётчик > 0)", true, on.Result.EscapeBoundHits > 0);
                }

                string note = on.Result.EscapeBoundNote ?? "";
                bool named = true;
                foreach (string image in v.OnFaceImages)
                {
                    named &= note.Contains(image);
                }

                Same("(2) заметка называет образы групп на границе итога (" + string.Join(", ", v.OnFaceImages) + ")", true,
                     named && (v.OnFaceImages.Count == 0) == string.IsNullOrEmpty(on.Result.EscapeBoundNote));
                Console.WriteLine("  (2) EscapeBoundHits {0}; EscapeBoundNote: {1}", on.Result.EscapeBoundHits, on.Result.EscapeBoundNote ?? "—");
                Console.WriteLine("  (2) χ²/ndf {0:R} → {1:R}", off.Result.Chi2Ndf, on.Result.Chi2Ndf);
            }

            Scene noCrystal = Run(path, nuclides, lines, parent, null, true, false, false);
            Scene offNoCrystal = Run(path, nuclides, lines, parent, null, false, false, false);
            Same("(3) без вещества: разбор состоялся", true, noCrystal.Result != null && offNoCrystal.Result != null);
            if (noCrystal.Result != null && offNoCrystal.Result != null)
            {
                Same("(3) без вещества граница не действует: побитно как без рычага", Print(offNoCrystal.Result), Print(noCrystal.Result));
                Same("(3) счётчик — нуль", 0, noCrystal.Result.EscapeBoundHits);
            }

            Scene again = Run(path, nuclides, lines, parent, fractions, false, false, false);
            Same("(4) повтор без рычагов побитно как (1)", Print(off.Result), again.Result != null ? Print(again.Result) : "нет разбора");

            Scene topOut = Run(path, nuclides, lines, parent, fractions, false, true, false);
            Same("(5) разбор с образами вне вершины состоялся", true, topOut.Result != null);
            if (topOut.Result != null)
            {
                Console.WriteLine("  (5) вершина порога: без рычага {0:R} (порог {1:R}); с рычагом {2:R} (порог {3:R})",
                                  off.TopZ, off.Threshold, topOut.TopZ, topOut.Threshold);
                string topName = TopName(off.Result);
                bool imageOnTop = topName != null && (topName.StartsWith("SE-", StringComparison.Ordinal) || topName.StartsWith("DE-", StringComparison.Ordinal));
                Console.WriteLine("  (5) наибольшая z итогового фита без рычага — у «{0}»", topName ?? "—");
                Same("(5) вершина с рычагом не выше", true, !(topOut.TopZ > off.TopZ));
                if (imageOnTop)
                {
                    Same("(5) вершиной был образ — с рычагом вершина строго ниже", true, topOut.TopZ < off.TopZ);
                }
                else
                {
                    Same("(5) вершина — не образ: побитно как (1)", Print(off.Result), Print(topOut.Result));
                }
            }

            // (7) отсев выключен: нарушитель обязан стоять РОВНО на границе
            Scene off7 = Run(path, nuclides, lines, parent, fractions, false, false, true);
            Scene on7 = Run(path, nuclides, lines, parent, fractions, true, false, true);
            Dictionary<string, Scene> bare7 = References(path, nuclides, lines, parent, fractions, true, on7);
            Same("(7) разборы без отсева состоялись", true, off7.Result != null && on7.Result != null && AllDone(bare7));
            if (off7.Result != null && on7.Result != null && AllDone(bare7))
            {
                Verdict v0 = Report("(7) без отсева, рычаги выключены", off7, null);
                Verdict v1 = Report("(7) без отсева, граница включена", on7, bare7);
                Same("(7) групп больше границы нет", 0, v1.Over.Count);
                Same("(7) правило тяги: пик родителя не выше, чем без образов (" + string.Join(", ", v1.Pulled) + ")", 0, v1.Pulled.Count);
                Same("(7) x_р⁰ заметки сходится с разбором без образов (" + string.Join(", ", v1.NoteOffRef) + ")", 0, v1.NoteOffRef.Count);
                Same("(7) отрицательных образов нет", 0, v1.Negative.Count + on7.Result.EscapeNegativeAccepted);
                Console.WriteLine("  (7) смен опорного {0}; EscapeBoundNote: {1}", on7.Result.EscapePivotSwitches, on7.Result.EscapeBoundNote ?? "—");
                bool allOnFace = true;
                bool pair = false;
                foreach (string g in v0.Strong)
                {
                    allOnFace &= v1.OnFace.Contains(g);
                }

                pair = v0.BothPositive.Count > 0;
                Same("(7) каждая заметно нарушавшая группа (" + string.Join(", ", v0.Strong) + ") стоит ровно на границе", true, allOnFace);
                if (expectViolation)
                {
                    Same("(7) нарушитель без отсева есть", true, v0.Over.Count > 0);
                }

                if (expectPair)
                {
                    Same("(7) у нарушавшей группы без рычага оба образа ненулевые (сцена суммы)", true, pair);
                }
            }

            return Finish();
        }

        /// <summary>(8) Путь приложения: сеанс документа с обоими рычагами.</summary>
        static int Session(string path, string setName, bool expectSwitch)
        {
            if (path == null || !File.Exists(path)) { Console.Error.WriteLine("нужен --spectrum=<файл>"); return 2; }
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            if (setName != null)
            {
                NuclideSet picked = nuclides.NuclideSets.Find(x => string.Equals(x.Name, setName, StringComparison.Ordinal));
                if (picked == null) { Console.Error.WriteLine("набора «{0}» нет", setName); return 2; }
                nuclides.ActiveSet = picked;
            }

            ResultData rd = Load(path, nuclides);
            EnergySpectrum s = rd.EnergySpectrum;
            if (s.TotalPulseCount == 0)
            {
                long total = 0;
                foreach (int c in s.Spectrum) total += c;
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            FsaAnalysisSession.ProbeAnalyzerHook = an =>
            {
                an.EscapeBoundToParent = true;
                an.EscapeOutOfTopZ = true;
                FsaTuningReport.Print(an);
            };
            var sessionRun = new FsaAnalysisSession();
            var done = new System.Threading.ManualResetEvent(false);
            sessionRun.Completed += (o, e) => done.Set();
            FsaResult r;
            try
            {
                sessionRun.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, FsaCalculationOptions.Of(rd));
                r = done.WaitOne(TimeSpan.FromSeconds(900)) ? sessionRun.Result : null;
            }
            finally
            {
                FsaAnalysisSession.ProbeAnalyzerHook = null;
            }

            Same("(8) разбор сеанса состоялся", true, r != null);
            if (r == null)
            {
                return Finish();
            }

            Console.WriteLine("  (8) граница задета {0} раз; смен опорного {1}; принятых решений с отрицательным образом {2}; заметка: {3}",
                              r.EscapeBoundHits, r.EscapePivotSwitches, r.EscapeNegativeAccepted, r.EscapeBoundNote ?? "—");
            int negative = 0;
            foreach (FsaComponentResult c in r.Components)
            {
                if ((c.Name.StartsWith("SE-", StringComparison.Ordinal) || c.Name.StartsWith("DE-", StringComparison.Ordinal))
                    && c.Curve != null && Sum(c.Curve) < 0.0)
                {
                    negative++;
                }
            }

            Same("(8) отрицательных образов нет (итог и все принятые решения граней)", 0, negative + r.EscapeNegativeAccepted);
            if (expectSwitch)
            {
                Same("(8) ветка смены опорного исполнилась", true, r.EscapePivotSwitches > 0);
            }

            return Finish();
        }

        sealed class Scene
        {
            public FsaResult Result;
            public double TopZ = double.NaN;
            public double Threshold = double.NaN;
            public FsaEfficiency Efficiency;
            public List<FsaComponent> Composition = new List<FsaComponent>();
            public List<FsaComponent> Images = new List<FsaComponent>();
        }

        sealed class Verdict
        {
            public List<string> Over = new List<string>();
            public HashSet<string> OnFace = new HashSet<string>();
            public HashSet<string> BothPositive = new HashSet<string>();
            public HashSet<string> Strong = new HashSet<string>();
            public List<string> OnFaceImages = new List<string>();
            public List<string> Negative = new List<string>();
            public List<string> NoteOffRef = new List<string>();
            public List<string> Pulled = new List<string>();
        }

        static Scene Run(string path, NuclideDefinitionManager nuclides, List<double> lines,
                         List<KeyValuePair<double, double>> parent, Dictionary<int, double> fractions,
                         bool bound, bool topOut, bool noRefit, string dropGroup = null)
        {
            ResultData rd = Load(path, nuclides);
            var scene = new Scene();
            for (int i = 0; i < lines.Count; i++)
            {
                string name = "L" + (i + 1).ToString(CultureInfo.InvariantCulture);
                var component = new FsaComponent(name, FsaComponentKind.Single);
                component.Lines.Add(new FsaLine(name, lines[i], 100.0));
                scene.Composition.Add(component);
            }

            if (parent.Count > 0)
            {
                var component = new FsaComponent("P", FsaComponentKind.Single);
                foreach (KeyValuePair<double, double> line in parent)
                {
                    component.Lines.Add(new FsaLine("P", line.Key, line.Value));
                }

                scene.Composition.Add(component);
            }

            var library = new List<FsaComponent>(scene.Composition);
            foreach (FsaComponent image in FsaLibrary.EscapeImages(scene.Composition, fractions))
            {
                if (dropGroup == null || (dropGroup != "*" && dropGroup != GroupKey(image)))
                {
                    library.Add(image);
                }

                scene.Images.Add(image);
            }

            var an = new FsaAnalyzer();
            an.SampleBeta = false;
            an.EscapeBoundToParent = bound;
            an.EscapeOutOfTopZ = topOut;
            if (noRefit)
            {
                an.RefitZ = 0.0;
            }

            FsaTuningReport.Print(an);
            scene.Efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
            scene.Result = an.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration,
                                      library, scene.Efficiency);
            scene.TopZ = an.RefitZTopZ;
            scene.Threshold = an.RefitZUsed;
            return scene;
        }

        static string GroupKey(FsaComponent image)
        {
            return image.EscapeParent + "@" + image.EscapeParentEnergy.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Опорные разборы правила тяги: на каждую группу — тот же разбор (рычаги выключены) БЕЗ образов ТОЛЬКО ЭТОЙ
        /// группы (решение Amber 12.11.2026 «Только своей группы»): пик линии родителя в нём — x_р⁰·A_линии.
        /// </summary>
        static Dictionary<string, Scene> References(string path, NuclideDefinitionManager nuclides, List<double> lines,
                                                    List<KeyValuePair<double, double>> parent, Dictionary<int, double> fractions,
                                                    bool noRefit, Scene scene)
        {
            var refs = new Dictionary<string, Scene>(StringComparer.Ordinal);
            foreach (FsaComponent image in scene.Images)
            {
                string key = GroupKey(image);
                if (!refs.ContainsKey(key))
                {
                    refs[key] = Run(path, nuclides, lines, parent, fractions, false, false, noRefit, key);
                }
            }

            return refs;
        }

        static double Sum(double[] curve)
        {
            double sum = 0.0;
            if (curve != null)
            {
                foreach (double v in curve)
                {
                    sum += v;
                }
            }

            return sum;
        }

        /// <summary>Доля линии родителя в его кривой — по весам «выход × эффективность» (своим счётом пробы).</summary>
        static double LineShare(FsaComponent component, double energy, FsaEfficiency efficiency)
        {
            double total = 0.0, line = 0.0;
            foreach (FsaLine l in component.Lines)
            {
                double e = 1.0, err;
                if (efficiency != null && !(efficiency.TryEval(l.Energy, out e, out err) && e > 0.0))
                {
                    continue;
                }

                double w = l.Intensity * e;
                total += w;
                if (Math.Abs(l.Energy - energy) <= 1.0E-6 * energy)
                {
                    line += w;
                }
            }

            return total > 0.0 ? line / total : 0.0;
        }

        /// <summary>
        /// Печать групп и их границ; вердикт: кто больше границы, кто ровно на ней. <paramref name="references"/> —
        /// по группе тот же разбор БЕЗ образов СВОЕЙ группы (правило тяги, решение Amber 12.11.2026 «Только своей
        /// группы»): граница — σ_пар/σ_фото × пик линии родителя без образов своей группы. Внутри решателя x_р⁰
        /// берётся из решения с теми же весами, поэтому граница группы на грани читается из заметки (её «… отсч.»),
        /// а с независимым разбором сверяется с допуском <see cref="TolRef"/>. Без <paramref name="references"/>
        /// (рычаг выключен) граница — от пика родителя в самом разборе.
        /// </summary>
        static bool AllDone(Dictionary<string, Scene> refs)
        {
            foreach (Scene r in refs.Values)
            {
                if (r.Result == null)
                {
                    return false;
                }
            }

            return true;
        }

        static Verdict Report(string what, Scene scene, Dictionary<string, Scene> references)
        {
            var area = Areas(scene.Result);
            Dictionary<string, double> noted = NotedLimits(scene.Result.EscapeBoundNote);

            var groups = new SortedDictionary<string, List<FsaComponent>>(StringComparer.Ordinal);
            foreach (FsaComponent image in scene.Images)
            {
                string key = image.EscapeParent + "@" + image.EscapeParentEnergy.ToString("R", CultureInfo.InvariantCulture);
                List<FsaComponent> list;
                if (!groups.TryGetValue(key, out list))
                {
                    list = new List<FsaComponent>();
                    groups.Add(key, list);
                }

                list.Add(image);
            }

            var verdict = new Verdict();
            foreach (KeyValuePair<string, List<FsaComponent>> g in groups)
            {
                FsaComponent first = g.Value[0];
                FsaComponent host = scene.Composition.Find(c => c.Name == first.EscapeParent);
                double share = host != null ? LineShare(host, first.EscapeParentEnergy, scene.Efficiency) : 0.0;
                double parentArea;
                area.TryGetValue(first.EscapeParent, out parentArea);
                double lineArea = parentArea * share;
                double cap = first.EscapeShareCap;
                double refLine = double.NaN;
                Dictionary<string, double> refArea = null;
                Scene reference;
                if (references != null && references.TryGetValue(g.Key, out reference))
                {
                    refArea = Areas(reference.Result);
                    double a0;
                    refArea.TryGetValue(first.EscapeParent, out a0);
                    refLine = a0 * share;
                }

                double notedLine;
                bool inNote = noted.TryGetValue(first.EscapeParentEnergy.ToString("G6", CultureInfo.InvariantCulture) + "|" + first.EscapeParent, out notedLine);
                double limit = refArea == null ? cap * lineArea : cap * (inNote ? notedLine : refLine);
                double slack = refArea == null || inNote ? Tol : TolRef;
                double sum = 0.0;
                int positive = 0;
                var parts = new StringBuilder();
                foreach (FsaComponent image in g.Value)
                {
                    double a;
                    area.TryGetValue(image.Name, out a);
                    sum += a;
                    positive += a > 0.0 ? 1 : 0;
                    if (a < 0.0) verdict.Negative.Add(image.Name);
                    parts.AppendFormat(CultureInfo.InvariantCulture, "{0} {1:F1}; ", image.Name, a);
                }

                bool exceeds = !double.IsNaN(cap) && sum > limit * (1.0 + slack) + 1.0E-6;
                // на грани: сумма равна границе; граница нулевая (родителя без образов своей группы нет) — сумма ноль
                bool onFace = !double.IsNaN(cap)
                              && ((sum > 0.0 && Math.Abs(sum - limit) <= Tol * limit)
                                  || (refArea != null && !(limit > 1.0E-6) && !(sum > 1.0E-6)));
                if (exceeds) verdict.Over.Add(g.Key);
                if (exceeds && sum > 1.5 * limit) verdict.Strong.Add(g.Key);
                if (exceeds && positive >= 2) verdict.BothPositive.Add(g.Key);
                if (onFace)
                {
                    verdict.OnFace.Add(g.Key);
                    foreach (FsaComponent image in g.Value)
                    {
                        double a;
                        area.TryGetValue(image.Name, out a);
                        if (a > 0.0) verdict.OnFaceImages.Add(image.Name);
                    }
                }

                if (inNote && refArea != null && !(Math.Abs(notedLine - refLine) <= TolNote * refLine))
                {
                    verdict.NoteOffRef.Add(g.Key);
                }

                // правило тяги: пик линии родителя с рычагом не выше, чем без образов
                if (refArea != null && lineArea > refLine * (1.0 + TolRef) + 1.0E-6)
                {
                    verdict.Pulled.Add(g.Key);
                }

                Console.WriteLine("  {0}: группа {1}: {2}сумма {3:F1} отсч.; линия родителя {4:F1} отсч. (без образов {5:F1}, по заметке {6}); граница {7:G5} → {8:F1} отсч. {9}",
                                  what, g.Key, parts, sum, lineArea, refLine,
                                  inNote ? notedLine.ToString("F1", CultureInfo.InvariantCulture) : "—", cap, limit,
                                  exceeds ? "⛔ БОЛЬШЕ ГРАНИЦЫ" : onFace ? "НА ГРАНИЦЕ" : "в границе");
            }

            return verdict;
        }

        static Dictionary<string, double> Areas(FsaResult result)
        {
            var area = new Dictionary<string, double>();
            foreach (FsaComponentResult c in result.Components)
            {
                area[c.Name] = Sum(c.Curve);
            }

            return area;
        }

        /// <summary>Пики родителей без образов из заметки границы: ключ «E (G6)|родитель» → отсчёты.</summary>
        static Dictionary<string, double> NotedLimits(string note)
        {
            var result = new Dictionary<string, double>();
            if (string.IsNullOrEmpty(note))
            {
                return result;
            }

            var re = new System.Text.RegularExpressions.Regex(@"пик (\S+) без образов \(линия ([0-9.eE+-]+) кэВ, ([0-9.eE+-]+) отсч\.\)");
            foreach (System.Text.RegularExpressions.Match m in re.Matches(note))
            {
                double e = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                double a = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                result[e.ToString("G6", CultureInfo.InvariantCulture) + "|" + m.Groups[1].Value] = a;
            }

            return result;
        }

        static string TopName(FsaResult result)
        {
            string name = null;
            double top = double.NaN;
            foreach (FsaComponentResult c in result.Components)
            {
                if (!double.IsNaN(c.Z) && (double.IsNaN(top) || c.Z > top))
                {
                    top = c.Z;
                    name = c.Name;
                }
            }

            return name;
        }

        /// <summary>Отпечаток разбора: χ², площади и z компонентов — побитно (R).</summary>
        static string Print(FsaResult r)
        {
            var sb = new StringBuilder();
            sb.Append(r.Chi2Ndf.ToString("R", CultureInfo.InvariantCulture));
            foreach (FsaComponentResult c in r.Components)
            {
                sb.Append('|').Append(c.Name).Append(':').Append(Sum(c.Curve).ToString("R", CultureInfo.InvariantCulture))
                  .Append(':').Append(c.Z.ToString("R", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        static int Finish()
        {
            Console.WriteLine(bad == 0 ? "ИТОГ: сошлось" : "ИТОГ: расхождений " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        static void Same<T>(string what, T expected, T actual)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            if (!ok) bad++;
            string e = expected is string ? Short(expected as string) : Convert.ToString(expected, CultureInfo.InvariantCulture);
            string g = actual is string ? Short(actual as string) : Convert.ToString(actual, CultureInfo.InvariantCulture);
            Console.WriteLine("  {0} {1}: ждали {2}, есть {3}", ok ? "OK " : "⛔ ", what, e, g);
        }

        static string Short(string s)
        {
            return s == null || s.Length <= 160 ? s : s.Substring(0, 160) + "…";
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            ProbeDeviceConfig.Attach(rd);
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            return rd;
        }
    }
}
