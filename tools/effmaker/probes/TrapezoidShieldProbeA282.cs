using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace TrapezoidShieldProbeA282
{
    /// <summary>
    /// (`AMBER282`, полоса `a282`, 03.11.2026) Маринелли с колодцем-ТРАПЕЦИЕЙ и защита СЛОЯМИ в
    /// расчёте — сцена Atom Spectra Nano 3 с нитратом калия (задача Amber 02.11.2026, журнал
    /// `handover/handover-2026-11-03-amber282-trapezoid-shield.md`).
    ///
    /// Что меряется (каждое — с положительным контролем):
    ///   1. круг «модель → `.in` → модель» и «модель → XML → модель»: текст `.in` тот же до байта;
    ///      контроль — модель без трапеции и без слоёв даёт текст БЕЗ новых ключей (прежний вид);
    ///   2. клеймо матрицы: трапеция и слои его МЕНЯЮТ (положительный контроль), а признак «в защите»
    ///      без слоёв — нет (прежнее правило `AMBER12`);
    ///   3. геометрия сцены: на случайных лучах область каждого отрезка разбора луча
    ///      (`CollectCrossings`) совпадает с «первой победившей» по `Contains` в точках отрезка;
    ///      контроль — та же сверка с порченой гранью (сдвиг d на 1 мм) обязана разойтись;
    ///   4. объём пробы розыгрышем по области сцены против формулы (цилиндр минус призма), и
    ///      розыгрыш источника: все точки — в области пробы, доля под донышком и средняя высота;
    ///   5. эффективность пика на узлах `--energies` (сцена с защитой и без неё, трапеция и круглый
    ///      колодец той же площади) — числа для журнала и сверки с Geant4 (`--dump=` — дамп сцены
    ///      для `tools/g4cf`).
    ///
    ///   TrapezoidShieldProbeA282 [--out=&lt;каталог&gt;] [--n=400000] [--threads=4]
    ///        [--energies=1460.82,661.66,351.93,238.63] [--rays=20000] [--no-eff]
    ///
    /// Код возврата: 0 — все сверки сошлись и все положительные контроли сработали; 1 — нет.
    /// </summary>
    static class Program
    {
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        /// <summary>Печать — только инвариантной культурой (разделитель дробной части — точка).</summary>
        static void W(string s)
        {
            Console.WriteLine(s);
        }

        static void W(string format, params object[] args)
        {
            Console.WriteLine(string.Format(Ci, format, args));
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string outDir = ".";
            int n = 400000, threads = 4, rays = 20000;
            bool eff = true;
            double histBin = 0.0;
            string scenePath = null;
            double[] energies = { 1460.82, 661.66, 351.93, 238.63 };
            foreach (string a in args)
            {
                if (a.StartsWith("--out=")) outDir = a.Substring(6);
                else if (a.StartsWith("--n=")) n = int.Parse(a.Substring(4), Ci);
                else if (a.StartsWith("--threads=")) threads = int.Parse(a.Substring(10), Ci);
                else if (a.StartsWith("--rays=")) rays = int.Parse(a.Substring(7), Ci);
                else if (a == "--no-eff") eff = false;
                else if (a.StartsWith("--hist=")) histBin = double.Parse(a.Substring(7), Ci);
                else if (a.StartsWith("--scene=")) scenePath = a.Substring(8);
                else if (a.StartsWith("--energies="))
                {
                    string[] parts = a.Substring(11).Split(',');
                    energies = new double[parts.Length];
                    for (int i = 0; i < parts.Length; i++) energies[i] = double.Parse(parts[i], Ci);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            Directory.CreateDirectory(outDir);
            bool ok = true;

            if (histBin > 0.0)
            {
                // Гистограмма ПОГЛОЩЁННОЙ энергии на квант в 4π (свет выключен) — наша сторона сверки с
                // `g4cf hist` (строки OURHIST, как у effsim --hist; читатель — tools/g4cf/cmp_hist.py).
                // Физика — умолчания склада (поля EfficiencySimulator по умолчанию).
                GeometryModel hg = scenePath != null ? GeometryModel.Load(scenePath) : Nano3KnoScene(true);
                var hsim = new EfficiencySimulator(hg) { Histories = n, LightNonproportionality = false };
                foreach (double e in energies)
                {
                    double relErr;
                    double[] h = hsim.Response(e, histBin, out relErr);
                    W("OURHISTBEGIN e_kev={0:R} bins={1} bin_kev={2:R} hist={3}", e, h.Length, histBin, n);
                    for (int i = 0; i < h.Length; i++)
                    {
                        if (h[i] > 0.0) W("OURHIST {0} {1:E6}", i, h[i]);
                    }

                    W("OURHISTEND");
                }

                return 0;
            }

            GeometryModel scene = Nano3KnoScene(true);
            W("сцена: " + scene.Describe());
            foreach (string w in scene.Warnings) W("  предупреждение: " + w);

            // --- 1. Круги `.in` и XML ---
            string text = GeometryWriter.Render(scene);
            string inPath = Path.Combine(outDir, "Nano3_KNO3_trap_shield.in");
            GeometryWriter.Save(scene, inPath);
            GeometryModel back = GeometryModel.Load(inPath);
            back.Name = scene.Name;
            string text2 = GeometryWriter.Render(back);
            bool inRound = text == text2;
            W("1. круг .in: {0} (трапеция {1}, слоёв {2}, защита в сцене {3})", inRound ? "СОШЁЛСЯ" : "НЕ СОШЁЛСЯ",
                              back.HasTrapezoidHole, back.ShieldLayers.Count, back.ShieldInScene);
            ok &= inRound && back.HasTrapezoidHole && back.ShieldInScene;

            var xs = new XmlSerializer(typeof(GeometryModel));
            string xml;
            using (var sw = new StringWriter(Ci))
            {
                xs.Serialize(sw, scene);
                xml = sw.ToString();
            }

            GeometryModel fromXml;
            using (var sr = new StringReader(xml))
            {
                fromXml = (GeometryModel)xs.Deserialize(sr);
            }

            bool xmlRound = GeometryWriter.Render(fromXml) == text;
            W("   круг XML: {0}", xmlRound ? "СОШЁЛСЯ" : "НЕ СОШЁЛСЯ");
            ok &= xmlRound;

            GeometryModel plain = Nano3KnoScene(true);
            plain.MarinelliHoleShape = GeometryHoleShape.Round;
            plain.MarinelliHoleDiameter = 40.0;
            plain.ShieldLayers.Clear();
            string plainText = GeometryWriter.Render(plain);
            string plainXml;
            using (var sw = new StringWriter(Ci))
            {
                xs.Serialize(sw, plain);
                plainXml = sw.ToString();
            }

            bool plainClean = !plainText.Contains("SM_BeakerHoleShape") && !plainText.Contains("DS_nShieldLayers")
                              && !plainXml.Contains("MarinelliHoleShape") && !plainXml.Contains("ShieldLayers")
                              && !plainXml.Contains("ShieldInnerDiameter");
            bool newKeys = text.Contains("SM_BeakerHoleShape = TRAPEZOID") && text.Contains("DS_nShieldLayers = 2")
                           && xml.Contains("<MarinelliHoleShape>Trapezoid</MarinelliHoleShape>") && xml.Contains("<ShieldLayers>");
            W("   контроль: без трапеции и слоёв новых ключей нет — {0}; у сцены они есть — {1}",
                              plainClean ? "да" : "НЕТ", newKeys ? "да" : "НЕТ");
            ok &= plainClean && newKeys;

            // --- 2. Клеймо ---
            var options = new ResponseMatrixOptions();
            string stamp = ResponseMatrix.ComputeStamp(scene, options);
            GeometryModel noShield = Nano3KnoScene(true);
            noShield.ShieldLayers.Clear();
            string stampNoLayers = ResponseMatrix.ComputeStamp(noShield, options);
            GeometryModel noShieldFlag = noShield.Clone();
            noShieldFlag.InShield = false;
            string stampNoFlag = ResponseMatrix.ComputeStamp(noShieldFlag, options);
            GeometryModel round = Nano3KnoScene(true);
            // Площадь — пока колодец ещё трапеция (у круга `TrapezoidHoleArea` = 0: прежняя полоса брала
            // её после переключения и получала D = −0.9 мм — колодца не было, проба накрывала детектор).
            double eqDiameter = EqualAreaDiameter(round);
            round.MarinelliHoleShape = GeometryHoleShape.Round;
            round.MarinelliHoleDiameter = eqDiameter;
            ok &= eqDiameter > 20.0;
            string stampRound = ResponseMatrix.ComputeStamp(round, options);
            {
                var simRound = new EfficiencySimulator(round);
                W("   круглый колодец той же площади: D = {0:F3} мм; сцена: {1}", round.MarinelliHoleDiameter, simRound.DescribeScene());
                foreach (string line in simRound.DumpScene().Split('\n'))
                {
                    if (line.StartsWith("source", StringComparison.Ordinal)) W("   источник: " + line);
                }
            }
            bool stampOk = stamp != stampNoLayers && stamp != stampRound && stampNoLayers == stampNoFlag;
            W("2. клеймо: со слоями {0}… без слоёв {1}… без признака {2}… круглый колодец {3}…",
                              Tail(stamp), Tail(stampNoLayers), Tail(stampNoFlag), Tail(stampRound));
            W("   слои меняют клеймо — {0}; трапеция меняет — {1}; признак без слоёв не меняет — {2}",
                              stamp != stampNoLayers ? "да" : "НЕТ", stamp != stampRound ? "да" : "НЕТ",
                              stampNoLayers == stampNoFlag ? "да" : "НЕТ");
            ok &= stampOk;

            // --- 3. Геометрия: разбор луча против «первой победившей» ---
            var sim = new EfficiencySimulator(scene);
            string dump = sim.DumpScene();
            File.WriteAllText(Path.Combine(outDir, "Nano3_KNO3_trap_shield.scene.txt"), dump + "\n");
            File.WriteAllText(Path.Combine(outDir, "Nano3_KNO3_trap_shield.describe.txt"), sim.DescribeScene() + "\n");
            W("   дамп сцены: " + Path.Combine(outDir, "Nano3_KNO3_trap_shield.scene.txt"));
            int mismatch = RayCheck(sim, rays, 11UL, 0.0);
            W("3. лучей {0}: расхождений разбора луча с поиском области — {1}", rays, mismatch);
            ok &= mismatch == 0;
            // Разбор луча и поиск области читают ОДНИ И ТЕ ЖЕ грани (`PolyDE`), поэтому их согласие
            // само по себе не говорит, что грани верны. Независимая сверка — классификатор точки,
            // написанный здесь заново по чертежу (цилиндр, трапеция неравенствами |y| < h, |x| < c + k·y,
            // слои защиты), против «первой победившей» области симулятора.
            int classified, wrong = PointClassCheck(sim, scene, 400000, 0.0, out classified);
            W("3а. независимый классификатор точки: точек {0}, расхождений с областями сцены — {1}", classified, wrong);
            ok &= wrong == 0 && classified > 100000;
            var simBad = new EfficiencySimulator(scene);
            int classifiedBad, bad = PointClassCheck(simBad, scene, 400000, 0.1, out classifiedBad);
            W("   контроль (наружные грани гильзы сдвинуты на 1 мм в областях сцены): расхождений {0} — {1}",
              bad, bad > 0 ? "поймано" : "НЕ ПОЙМАНО");
            ok &= bad > 0;

            // Ранний выход электрона: шар радиуса `RegionNearestFace` вокруг точки не выходит из её
            // области (нижняя граница пути до выхода). Контроль — шар в 1.5 раза больше (+0.5 мм).
            int balls, ballBad = NearestFaceCheck(sim, 60000, 0.999, 0.0, out balls);
            int ballsBig, ballBig = NearestFaceCheck(new EfficiencySimulator(scene), 60000, 1.5, 0.05, out ballsBig);
            W("3в. ближайшая грань (электрон): точек {0} × 16 направлений, выходов из области — {1}; контроль (шар ×1.5 + 0.5 мм): {2} — {3}",
              balls, ballBad, ballBig, ballBig > 0 ? "поймано" : "НЕ ПОЙМАНО");
            ok &= ballBad == 0 && ballBig > 0 && balls > 10000;

            // Трапеция с равными основаниями — прямоугольник: вершины ±(a/2 + t), ±(w/2 + t).
            GeometryModel rect = Nano3KnoScene(true);
            rect.MarinelliHoleBase1 = 30.0;
            rect.MarinelliHoleBase2 = 30.0;
            rect.MarinelliHoleWidth = 20.0;
            double[] rx = new double[4], ry = new double[4];
            rect.TrapezoidHoleVertices(0.45, rx, ry);
            double rectErr = 0.0;
            double[] ex = { -15.45, 15.45, 15.45, -15.45 }, ey = { -10.45, -10.45, 10.45, 10.45 };
            for (int k = 0; k < 4; k++) rectErr = Math.Max(rectErr, Math.Max(Math.Abs(rx[k] - ex[k]), Math.Abs(ry[k] - ey[k])));
            double rectArea = rect.TrapezoidHoleArea(0.45);
            bool rectOk = rectErr < 1e-12 && Math.Abs(rectArea - 30.9 * 20.9) < 1e-9;
            W("3б. равные основания 30/30 × 20, стенка 0.45: наибольшее отклонение вершин от прямоугольника {0:E2} мм, площадь {1:F4} (ожидание {2:F4}) — {3}",
              rectErr, rectArea, 30.9 * 20.9, rectOk ? "прямоугольник" : "НЕ ПРЯМОУГОЛЬНИК");
            ok &= rectOk;
            // Площадь трапеции сцены: формула полей против шнуровки вершин и против (a + b)/2 · w наружного контура.
            double[] tx = new double[4], ty = new double[4];
            scene.TrapezoidHoleVertices(scene.MarinelliHoleSideThickness, tx, ty);
            double lace = 0.0;
            for (int k = 0; k < 4; k++) lace += tx[k] * ty[(k + 1) % 4] - tx[(k + 1) % 4] * ty[k];
            lace *= 0.5;
            double outerArea = 0.5 * ((tx[1] - tx[0]) + (tx[2] - tx[3])) * (ty[2] - ty[1]);
            bool areaOk = Math.Abs(lace - scene.TrapezoidHoleArea(scene.MarinelliHoleSideThickness)) < 1e-9
                          && Math.Abs(outerArea - 0.5 * (35.0 + 38.0) * 22.0) < 1e-4;
            W("   наружный контур гильзы: основания {0:F4}/{1:F4}, высота {2:F4} мм (чертёж 35/38 × 22); площадь {3:F4} = шнуровка {4:F4} — {5}",
              tx[1] - tx[0], tx[2] - tx[3], ty[2] - ty[1], outerArea, lace, areaOk ? "сошлось" : "НЕ СОШЛОСЬ");
            ok &= areaOk;

            // --- 4. Объём пробы и розыгрыш источника ---
            double vFormula = SampleVolumeFormula(scene);
            double vMc, vErr;
            SampleVolumeMc(sim, 2000000, out vMc, out vErr);
            double vScenes = GeometryScenes.SampleVolumeCm3(scene);
            W("4. объём пробы: формула {0:F3} см³, редактор (GeometryScenes) {1:F3} см³, розыгрыш по областям {2:F3} ± {3:F3} см³",
                              vFormula, vScenes, vMc, vErr);
            bool vOk = Math.Abs(vMc - vFormula) < 4.0 * vErr;
            // Объём редактора (`GeometryScenes`, полоса B) печатается, но приёмку переноса не решает:
            // его трапецию ведёт редактор.
            W("   объём переноса против формулы: {0:F2} σ — {1}; редактор против формулы: {2} ",
              (vMc - vFormula) / vErr, vOk ? "сошлось" : "НЕ СОШЛОСЬ",
              Math.Abs(vScenes - vFormula) < 1e-6 * vFormula ? "сошлось" : "НЕ СОШЛОСЬ (редактор, полоса B)");
            ok &= vOk;
            W("   плотность пробы 300 г / V = {0:F4} г/см³ (в сцене {1:F4})", 300.0 / vFormula, scene.Source.Density);
            int inSample, total = 200000;
            double meanZ;
            SourceCheck(sim, total, out inSample, out meanZ);
            W("   розыгрыш источника: в области пробы {0} из {1}; средняя высота над дном {2:F3} см (ожидание 3.000)",
                              inSample, total, meanZ);
            ok &= inSample == total && Math.Abs(meanZ - 3.0) < 0.02;

            // --- 4б. Круглый маринелли «пробы под колодцем нет» (глубина = высоте пробы, торец 0) ---
            // Опция редактора выражается прежними полями. Обязано: нет области нулевой толщины под
            // колодцем, источник под колодцем не разыгрывается, объём = формуле. Контроль: глубина на
            // 1 мм меньше — слой пробы 1 мм под колодцем появляется и в областях, и в розыгрыше.
            foreach (double less in new[] { 0.0, 1.0 })
            {
                GeometryModel rg = Nano3KnoScene(true);
                rg.MarinelliHoleShape = GeometryHoleShape.Round;
                rg.MarinelliHoleDiameter = 31.0;
                rg.MarinelliHoleHeight = rg.MarinelliSourceHeight - less;
                rg.MarinelliHoleEndWallThickness = 0.0;
                string tag = less == 0.0 ? "round_through" : "round_cap1mm";
                GeometryWriter.Save(rg, Path.Combine(outDir, "Nano3_KNO3_" + tag + ".in"));
                var rs = new EfficiencySimulator(rg);
                string describe = rs.DescribeScene();
                int sampleRegions = 0;
                foreach (string part in describe.Split(';'))
                {
                    if (part.Contains("Potassium nitrate")) sampleRegions++;
                }

                double rIn = 0.05 * 31.0 + 0.1 * rg.MarinelliHoleSideThickness, rOut = 0.05 * rg.MarinelliBeakerDiameter;
                double hs = 0.1 * rg.MarinelliSourceHeight, cap = 0.1 * less;
                double vForm = Math.PI * (rOut * rOut - rIn * rIn) * hs + Math.PI * rIn * rIn * cap;
                double rvMc, rvErr;
                SampleVolumeMc(rs, 2000000, out rvMc, out rvErr);
                // розыгрыш источника: доля точек под колодцем (r < rIn)
                object src = Field(rs, "source");
                MethodInfo next = src.GetType().GetMethod("Next", Any);
                var argsBuf = new object[] { rs, 0.0, 0.0, 0.0 };
                int under = 0, rTotal = 200000;
                for (int i = 0; i < rTotal; i++)
                {
                    next.Invoke(src, argsBuf);
                    double x = (double)argsBuf[1], y = (double)argsBuf[2];
                    if (x * x + y * y < rIn * rIn) under++;
                }

                double expectUnder = Math.PI * rIn * rIn * cap / vForm;
                bool okRound = less == 0.0
                    ? sampleRegions == 1 && under == 0 && Math.Abs(rvMc - vForm) < 4.0 * rvErr
                    : sampleRegions == 2 && Math.Abs(under / (double)rTotal - expectUnder) < 5.0 * Math.Sqrt(expectUnder / rTotal)
                      && Math.Abs(rvMc - vForm) < 4.0 * rvErr;
                W("4б. {0}: областей пробы {1}; источник под колодцем {2} из {3} (ожидание {4:F5}); объём формула {5:F3} см³, розыгрыш {6:F3} ± {7:F3} — {8}",
                  tag, sampleRegions, under, rTotal, expectUnder, vForm, rvMc, rvErr, okRound ? "сошлось" : "НЕ СОШЛОСЬ");
                W("    сцена: " + describe);
                ok &= okRound;
            }

            // --- 5. Эффективность пика ---
            if (eff)
            {
                var variants = new List<KeyValuePair<string, GeometryModel>>
                {
                    new KeyValuePair<string, GeometryModel>("trap+shield", scene),
                    new KeyValuePair<string, GeometryModel>("trap", noShieldFlag),
                    new KeyValuePair<string, GeometryModel>("round-eq-area+shield", round),
                };
                var csv = new List<string> { "variant,E_keV,eps,err_pct" };
                foreach (double e in energies)
                {
                    var results = new double[variants.Count];
                    var errs = new double[variants.Count];
                    Parallel.For(0, variants.Count, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
                    {
                        GeometryModel g = variants[i].Value;
                        var s = new EfficiencySimulator(g) { Histories = n, PeakHalfWidthKev = g.PeakHalfWidthKev(e) };
                        double err;
                        results[i] = s.Efficiency(e, out err);
                        errs[i] = err;
                    });
                    for (int i = 0; i < variants.Count; i++)
                    {
                        W("5. {0,-22} E={1,8:F2} кэВ: ε = {2:E4} ± {3:F2} %", variants[i].Key, e, results[i], errs[i]);
                        csv.Add(string.Format(Ci, "{0},{1:R},{2:R},{3:R}", variants[i].Key, e, results[i], errs[i]));
                    }
                }

                File.WriteAllLines(Path.Combine(outDir, "eff.csv"), csv);
            }

            W(ok ? "ИТОГ: все сверки сошлись, контроли сработали" : "ИТОГ: ЕСТЬ РАСХОЖДЕНИЯ");
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Сцена Amber 02.11.2026 (журнал `AMBER282`): Nano 3 — обвязка ASN16 с пластиковым корпусом,
        /// CsI 10×10×30, центр кристалла — в геометрическом центре пробы; проба — цилиндр Ø80 × 60 мм
        /// нитрата калия 300 г с колодцем-трапецией 35/38 × 22 мм (размеры по пробе — наружный контур
        /// гильзы Al 0.45 мм); защита — Al 0.45 мм по полости Ø82, дальше сталь до Ø160, дно и крышка
        /// — Al 0.45 + сталь. Окончательно (Amber 03.11.2026, чертёж в клетках по 1 см, вопросником:
        /// «Чертёж верен, снаружи ~180», «Упростить: Ø82 × 100»): полость Ø82 × 100, дно и крышка —
        /// Al 0.45 + сталь 39.55 (по 40), бок — Al 0.45 + сталь 38.55 (до Ø160), снаружи 180; сталь —
        /// «Steel, Carbon» PNNL (7.82; C 0.005, Fe 0.995); корпус — поликарбонат 1.20; проба на дне.
        /// ДОПУЩЕНИЕ: выемка Ø120 × 20 под крышкой заменена сталью кольца Ø82–120.
        /// </summary>
        internal static GeometryModel Nano3KnoScene(bool shield)
        {
            var g = new GeometryModel { Name = "Nano3_KNO3_trap_shield", IsScintillator = true };
            g.Shape = CrystalShape.Box;
            g.CrystalBoxX = 10.0;
            g.CrystalBoxY = 10.0;
            g.CrystalBoxZ = 30.0;
            g.FrontReflectorThickness = 1.3;
            g.SideReflectorThickness = 1.0;
            g.FrontCladdingThickness = 1.8;
            g.SideCladdingThickness = 2.0;
            g.MountingThickness = 2.0;
            g.Crystal = Mat("Cesium iodide", 0.0);
            g.Reflector = Mat("Polytetrafluoroethylene", 0.0);
            g.Gap = Mat("Air, dry", 0.0);
            g.Cladding = Mat("Polycarbonate", 0.0);
            g.FwhmAt662Percent = 7.1;

            g.SourceType = GeometrySourceType.Marinelli;
            g.MarinelliBeakerDiameter = 80.0;
            g.MarinelliBeakerHeight = 60.0;
            g.MarinelliSourceHeight = 60.0;
            g.MarinelliHoleHeight = 60.0;
            g.MarinelliSideThickness = 0.0;
            g.MarinelliEndWallThickness = 0.0;
            g.MarinelliHoleSideThickness = 0.45;
            g.MarinelliHoleEndWallThickness = 0.0;
            g.MarinelliHoleShape = GeometryHoleShape.Trapezoid;
            double b1, b2, w;
            InnerTrapezoid(35.0, 38.0, 22.0, 0.45, out b1, out b2, out w);
            g.MarinelliHoleBase1 = b1;
            g.MarinelliHoleBase2 = b2;
            g.MarinelliHoleWidth = w;
            // Как пишет редактор (`GeometryEditorPanel`, полоса B): у трапеции SM_BeakerHoleDiameter —
            // равноплощадный ПРОСВЕТУ (перенос его не читает, но он в тексте `.in` и в клейме).
            g.MarinelliHoleDiameter = 2.0 * Math.Sqrt(g.TrapezoidHoleArea(0.0) / Math.PI);
            // Центр кристалла (15 мм над торцом) — на середине высоты пробы: торец корпуса на
            // 3.1 мм ниже кристалла (отражатель 1.3 + корпус 1.8), дно колодца — на 15 мм ниже кристалла.
            g.MarinelliToDetectorDistance = 15.0 - (1.3 + 1.8);
            g.BeakerWall = Mat("Aluminum", 0.0);

            var kno3 = new GeometryMaterial { Name = "Potassium nitrate", Density = 1.0 };
            double mK = 39.0983, mN = 14.0067, mO = 15.999, m = mK + mN + 3.0 * mO;
            kno3.Fractions[7] = mN / m;
            kno3.Fractions[8] = 3.0 * mO / m;
            kno3.Fractions[19] = mK / m;
            g.Source = kno3;
            g.Source.Density = Math.Round(300.0 / SampleVolumeFormula(g), 5);

            if (shield)
            {
                g.InShield = true;
                g.ShieldInnerDiameter = 82.0;
                g.ShieldInnerHeight = 100.0;
                g.ShieldFloorGap = 0.0;
                g.ShieldLayers.Add(new GeometryShieldLayer
                {
                    Material = Mat("Aluminum", 0.0), SideThickness = 0.45, FloorThickness = 0.45, LidThickness = 0.45,
                });
                g.ShieldLayers.Add(new GeometryShieldLayer
                {
                    Material = Mat("Steel, Carbon", 0.0), SideThickness = 0.5 * (160.0 - 82.9), FloorThickness = 39.55, LidThickness = 39.55,
                });
            }

            return g;
        }

        /// <summary>Просвет трапеции по наружному контуру и толщине стенки (параллельный сдвиг граней внутрь).</summary>
        internal static void InnerTrapezoid(double a, double b, double w, double t, out double a0, out double b0, out double w0)
        {
            double k = (b - a) / (2.0 * w);
            double c = 0.25 * (a + b) - t * Math.Sqrt(1.0 + k * k);
            w0 = w - 2.0 * t;
            a0 = Math.Round(2.0 * (c - k * 0.5 * w0), 6);
            b0 = Math.Round(2.0 * (c + k * 0.5 * w0), 6);
            w0 = Math.Round(w0, 6);
        }

        static GeometryMaterial Mat(string name, double density)
        {
            GeometryMaterialLibrary.Entry e = GeometryMaterialLibrary.ByName(name);
            if (e == null)
            {
                throw new InvalidOperationException("нет вещества в библиотеке: " + name);
            }

            return GeometryMaterialLibrary.Make(e, density > 0.0 ? density : e.Density);
        }

        /// <summary>Объём пробы, см³: цилиндр минус призма наружного контура гильзы (мм → см).</summary>
        internal static double SampleVolumeFormula(GeometryModel g)
        {
            double r = 0.5 * g.MarinelliBeakerDiameter - g.MarinelliSideThickness;
            double hole = g.TrapezoidHoleArea(g.MarinelliHoleSideThickness);
            return (Math.PI * r * r - hole) * g.MarinelliSourceHeight / 1000.0;
        }

        /// <summary>Хвост клейма (хеш) — для печати.</summary>
        static string Tail(string stamp)
        {
            int i = stamp.LastIndexOf(';');
            string h = i >= 0 ? stamp.Substring(i + 1) : stamp;
            return h.Length > 12 ? h.Substring(0, 12) : h;
        }

        /// <summary>
        /// Независимый классификатор точки по ЧЕРТЕЖУ (см): проба — цилиндр Ø80 × 60, центр кристалла
        /// на середине её высоты; колодец — трапеция 35/38 × 22 наружным контуром гильзы (|y| &lt; h,
        /// |x| &lt; c + k·y), просвет — грани внутрь на 0.45 мм; защита — полость Ø82 × 120 на дне
        /// пробы, Al 0.45 мм, сталь до Ø160, дно стали 39.55, крышка 20. Точки в габарите детектора
        /// пропускаются. Против «первой победившей» области симулятора по `Region.Contains`;
        /// <paramref name="spoil"/> — сдвиг (см) НАРУЖНЫХ граней гильзы в областях сцены (контроль).
        /// </summary>
        static int PointClassCheck(EfficiencySimulator sim, GeometryModel g, int n, double spoil, out int classified)
        {
            typeof(EfficiencySimulator).GetMethod("EnsureBuilt", Any).Invoke(sim, null);
            Array regions = (Array)Field(sim, "regionArray");
            Type regionType = regions.GetType().GetElementType();
            MethodInfo contains = regionType.GetMethod("Contains", new[] { typeof(double), typeof(double), typeof(double) });
            FieldInfo material = regionType.GetField("Material");
            if (spoil != 0.0)
            {
                foreach (object r in regions)
                {
                    if ((int)regionType.GetField("Shape").GetValue(r) == 3)
                    {
                        var de = (double[])((double[])regionType.GetField("PolyDE").GetValue(r)).Clone();
                        for (int j = 0; j < de.Length; j++) de[j] += spoil;
                        regionType.GetField("PolyDE").SetValue(r, de);
                    }
                }
            }

            GeometryModel gc = (GeometryModel)Field(sim, "geometry");
            double zc = 0.05 * g.CrystalBoxZ;
            double zs0 = zc - 3.0, zs1 = zc + 3.0, rs = 4.0;
            double a = 3.5, b = 3.8, w = 2.2, t = 0.045;
            double k = (b - a) / (2.0 * w), c = 0.25 * (a + b), h = 0.5 * w;
            double hi = h - t, ci = c - t * Math.Sqrt(1.0 + k * k);
            double lid = zs0 + 10.0;
            var rng = new Random(23);
            int bad = 0;
            classified = 0;
            for (int i = 0; i < n; i++)
            {
                double x, y, z;
                if ((i & 1) == 0)
                {
                    x = (2.0 * rng.NextDouble() - 1.0) * 8.5;
                    y = (2.0 * rng.NextDouble() - 1.0) * 8.5;
                    z = zs0 - 4.5 + 19.5 * rng.NextDouble();
                }
                else
                {
                    x = (2.0 * rng.NextDouble() - 1.0) * 2.2;
                    y = (2.0 * rng.NextDouble() - 1.0) * 1.4;
                    z = zs0 - 0.5 + 7.0 * rng.NextDouble();
                }

                if (Math.Abs(x) < 0.801 && Math.Abs(y) < 0.801 && z > -0.311 && z < 3.201)
                {
                    continue;                           // габарит детектора
                }

                double r = Math.Sqrt(x * x + y * y);
                bool inZ = z >= zs0 && z < zs1;
                bool inOuter = Math.Abs(y) < h && Math.Abs(x) < c + k * y;
                bool inInner = Math.Abs(y) < hi && Math.Abs(x) < ci + k * y;
                string expected;
                if (inZ && inOuter && !inInner) expected = "liner";
                else if (inZ && !inOuter && r < rs) expected = "sample";
                else if (r < 4.1 && z >= zs0 && z < lid) expected = "air";
                else if (r < 4.145 && z >= zs0 - 0.045 && z < lid + 0.045) expected = "shieldAl";
                else if (r < 8.0 && z >= zs0 - 4.0 && z < lid + 4.0) expected = "steel";
                else expected = "air";

                object winner = null;
                foreach (object reg in regions)
                {
                    if ((bool)contains.Invoke(reg, new object[] { x, y, z }))
                    {
                        winner = material.GetValue(reg);
                        break;
                    }
                }

                string got = winner == null ? "air"
                    : ReferenceEquals(winner, gc.Source) ? "sample"
                    : ReferenceEquals(winner, gc.BeakerWall) ? "liner"
                    : ReferenceEquals(winner, gc.ShieldLayers[0].Material) ? "shieldAl"
                    : ReferenceEquals(winner, gc.ShieldLayers[1].Material) ? "steel"
                    : "other:" + ((GeometryMaterial)winner).Name;
                classified++;
                if (got != expected)
                {
                    if (bad < 5 && spoil == 0.0)
                    {
                        W("   расхождение: ({0:F4}, {1:F4}, {2:F4}) ожидалось {3}, в сцене {4}", x, y, z, expected, got);
                    }

                    bad++;
                }
            }

            return bad;
        }

        /// <summary>
        /// Шар `RegionNearestFace` (ранний выход переноса электрона): для случайной точки в области
        /// формы, в пробе или в защите точки на сфере радиуса scale·d + add обязаны остаться в той же
        /// «первой победившей» области. Возвращает число выходов.
        /// </summary>
        static int NearestFaceCheck(EfficiencySimulator sim, int n, double scale, double add, out int tested)
        {
            typeof(EfficiencySimulator).GetMethod("EnsureBuilt", Any).Invoke(sim, null);
            Array regions = (Array)Field(sim, "regionArray");
            Type regionType = regions.GetType().GetElementType();
            MethodInfo contains = regionType.GetMethod("Contains", new[] { typeof(double), typeof(double), typeof(double) });
            MethodInfo face = typeof(EfficiencySimulator).GetMethod("RegionNearestFace", Any);
            FieldInfo shape = regionType.GetField("Shape");
            FieldInfo isCrystal = regionType.GetField("IsCrystal");
            Func<double, double, double, int> at = (x, y, z) =>
            {
                for (int i = 0; i < regions.Length; i++)
                {
                    if ((bool)contains.Invoke(regions.GetValue(i), new object[] { x, y, z })) return i;
                }

                return -1;
            };
            var rng = new Random(41);
            int bad = 0;
            tested = 0;
            for (int k = 0; k < n; k++)
            {
                double x = (2.0 * rng.NextDouble() - 1.0) * 4.3, y = (2.0 * rng.NextDouble() - 1.0) * 4.3;
                double z = -1.7 + 6.4 * rng.NextDouble();
                int i = at(x, y, z);
                if (i < 0)
                {
                    continue;
                }

                object r = regions.GetValue(i);
                if ((bool)isCrystal.GetValue(r) || (i < 7 && (int)shape.GetValue(r) == 0))
                {
                    continue;                           // детектор — не предмет этой сверки
                }

                double d = (double)face.Invoke(sim, new object[] { r, x, y, z });
                double rad = scale * d + add;
                tested++;
                for (int j = 0; j < 16; j++)
                {
                    double uz = 2.0 * rng.NextDouble() - 1.0, phi = 2.0 * Math.PI * rng.NextDouble();
                    double s = Math.Sqrt(1.0 - uz * uz);
                    if (at(x + rad * s * Math.Cos(phi), y + rad * s * Math.Sin(phi), z + rad * uz) != i)
                    {
                        bad++;
                        break;
                    }
                }
            }

            return bad;
        }

        static double EqualAreaDiameter(GeometryModel g)
        {
            // Круглый колодец той же площади наружного контура: (D/2 + t)² π = площадь трапеции.
            double area = g.TrapezoidHoleArea(g.MarinelliHoleSideThickness);
            return 2.0 * (Math.Sqrt(area / Math.PI) - g.MarinelliHoleSideThickness);
        }

        static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        static object Field(object o, string name)
        {
            return o.GetType().GetField(name, Any).GetValue(o);
        }

        /// <summary>
        /// Случайные лучи из случайных точек габарита сцены: область каждого отрезка разбора
        /// (`CollectCrossings` → `raySeg`) против «первой победившей» по `Region.Contains` в пяти
        /// точках внутри отрезка. <paramref name="spoil"/> — порча (см) граней гильзы ТОЛЬКО в
        /// разборе луча (`PolyDE`), для положительного контроля.
        /// </summary>
        static int RayCheck(EfficiencySimulator sim, int rays, ulong seed, double spoil)
        {
            MethodInfo ensure = typeof(EfficiencySimulator).GetMethod("EnsureBuilt", Any);
            ensure.Invoke(sim, null);
            Array regions = (Array)Field(sim, "regionArray");
            Type regionType = regions.GetType().GetElementType();
            MethodInfo contains = regionType.GetMethod("Contains", new[] { typeof(double), typeof(double), typeof(double) });
            if (spoil != 0.0)
            {
                foreach (object r in regions)
                {
                    if ((int)regionType.GetField("Shape").GetValue(r) == 3)
                    {
                        var de = (double[])regionType.GetField("PolyDE").GetValue(r);
                        de = (double[])de.Clone();
                        for (int j = 0; j < de.Length; j++) de[j] += spoil;
                        regionType.GetField("PolyDE").SetValue(r, de);
                    }
                }
            }

            MethodInfo collect = typeof(EfficiencySimulator).GetMethod("CollectCrossings", Any);
            double sceneR = (double)Field(sim, "sceneRMax");
            double z0 = (double)Field(sim, "sceneZMin"), z1 = (double)Field(sim, "sceneZMax");
            var rng = new Random((int)seed);
            int bad = 0;
            for (int k = 0; k < rays; k++)
            {
                double x = (2.0 * rng.NextDouble() - 1.0) * sceneR;
                double y = (2.0 * rng.NextDouble() - 1.0) * sceneR;
                double z = z0 + (z1 - z0) * rng.NextDouble();
                // Половина лучей — к оси (через колодец), половина — изотропно.
                double ux, uy, uz;
                if ((k & 1) == 0)
                {
                    double tx = (2.0 * rng.NextDouble() - 1.0) * 1.5, ty = (2.0 * rng.NextDouble() - 1.0) * 1.5;
                    ux = tx - x; uy = ty - y; uz = (z0 + (z1 - z0) * rng.NextDouble()) - z;
                }
                else
                {
                    ux = rng.NextDouble() - 0.5; uy = rng.NextDouble() - 0.5; uz = rng.NextDouble() - 0.5;
                }

                double norm = Math.Sqrt(ux * ux + uy * uy + uz * uz);
                ux /= norm; uy /= norm; uz /= norm;
                collect.Invoke(sim, new object[] { x, y, z, ux, uy, uz });
                var cross = (double[])Field(sim, "rayCross");
                var seg = (Array)Field(sim, "raySeg");
                int count = (int)Field(sim, "rayCount");
                double start = 0.0;
                for (int s = 0; s <= count && s < 64; s++)
                {
                    double end = s < count ? cross[s] : start + 2.0 * (sceneR + (z1 - z0));
                    object expected = seg.GetValue(s);
                    for (int p = 1; p <= 5; p++)
                    {
                        double t = start + (end - start) * p / 6.0;
                        if (end - start < 1e-6) break;
                        double px = x + t * ux, py = y + t * uy, pz = z + t * uz;
                        object winner = null;
                        foreach (object r in regions)
                        {
                            if ((bool)contains.Invoke(r, new object[] { px, py, pz }))
                            {
                                winner = r;
                                break;
                            }
                        }

                        if (!ReferenceEquals(winner, expected))
                        {
                            bad++;
                        }
                    }

                    start = end;
                }
            }

            return bad;
        }

        /// <summary>Объём области пробы розыгрышем по габариту сцены: доля точек, «первая победившая» которых — область с веществом пробы.</summary>
        static void SampleVolumeMc(EfficiencySimulator sim, int n, out double volume, out double error)
        {
            Array regions = (Array)Field(sim, "regionArray");
            Type regionType = regions.GetType().GetElementType();
            MethodInfo contains = regionType.GetMethod("Contains", new[] { typeof(double), typeof(double), typeof(double) });
            FieldInfo material = regionType.GetField("Material");
            GeometryModel g = (GeometryModel)Field(sim, "geometry");
            double r = 4.0, zA = -1.6, zB = 4.6;   // см: габарит пробы с запасом
            var rng = new Random(7);
            int hit = 0;
            for (int i = 0; i < n; i++)
            {
                double x = (2.0 * rng.NextDouble() - 1.0) * r, y = (2.0 * rng.NextDouble() - 1.0) * r;
                double z = zA + (zB - zA) * rng.NextDouble();
                foreach (object reg in regions)
                {
                    if ((bool)contains.Invoke(reg, new object[] { x, y, z }))
                    {
                        if (ReferenceEquals(material.GetValue(reg), g.Source)) hit++;
                        break;
                    }
                }
            }

            double box = 4.0 * r * r * (zB - zA);
            double p = hit / (double)n;
            volume = p * box;
            error = Math.Sqrt(p * (1.0 - p) / n) * box;
        }

        /// <summary>Розыгрыш источника сцены: доля точек в области пробы и средняя высота над дном пробы.</summary>
        static void SourceCheck(EfficiencySimulator sim, int n, out int inSample, out double meanZ)
        {
            object source = Field(sim, "source");
            MethodInfo next = source.GetType().GetMethod("Next", Any);
            Array regions = (Array)Field(sim, "regionArray");
            Type regionType = regions.GetType().GetElementType();
            MethodInfo contains = regionType.GetMethod("Contains", new[] { typeof(double), typeof(double), typeof(double) });
            FieldInfo material = regionType.GetField("Material");
            GeometryModel g = (GeometryModel)Field(sim, "geometry");
            double z0 = (double)Field(source, "z0");
            inSample = 0;
            double sumZ = 0.0;
            var argsBuf = new object[] { sim, 0.0, 0.0, 0.0 };
            for (int i = 0; i < n; i++)
            {
                next.Invoke(source, argsBuf);
                double x = (double)argsBuf[1], y = (double)argsBuf[2], z = (double)argsBuf[3];
                sumZ += z - z0;
                foreach (object reg in regions)
                {
                    if ((bool)contains.Invoke(reg, new object[] { x, y, z }))
                    {
                        if (ReferenceEquals(material.GetValue(reg), g.Source)) inSample++;
                        break;
                    }
                }
            }

            meanZ = sumZ / n;
        }
    }
}
