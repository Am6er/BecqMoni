using System;
using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// (`AMBER274`, Э2, 06.11.2026) РЕЛАКСАЦИЯ ДОЧЕРНЕГО АТОМА после захвата электрона:
    /// линии K-рентгена с вероятностью на ОДИН захват (из `decay_radiations`, а не числом
    /// в коде), доля K-вакансий без рентгена (оже) и энергии связи. Захват не с K-оболочки
    /// отдаёт энергию связи L на месте.
    /// </summary>
    public sealed class CaptureRelaxation
    {
        /// <summary>Энергии линий K-рентгена, кэВ.</summary>
        public double[] LineKev = new double[0];

        /// <summary>Накопленная вероятность линии на один захват.</summary>
        public double[] LineCdf = new double[0];

        /// <summary>Вероятность K-вакансии на один захват: P(K-рентген) / ω_K.</summary>
        public double KCapture;

        /// <summary>Энергия связи K дочернего атома, кэВ.</summary>
        public double KEdgeKev;

        /// <summary>
        /// Энергия связи L дочернего атома, кэВ: средняя L1–L3 `matdb.eadl_binding`; нет её в базе —
        /// средняя линия L-рентгена `nucdb` (прежнее значение: у Ba 4.97 вместо 5.2–6.0 кэВ).
        /// </summary>
        public double LEdgeKev;

        /// <summary>
        /// (`AMBER274`, ревизия по статьям, 12.11.2026) Выход флуоресценции K атома — для вакансии
        /// ИЗВЕСТНОЙ оболочки (внутренняя конверсия, <see cref="EfficiencySimulator.VacancyDeposit"/>).
        /// </summary>
        public double OmegaK;

        /// <summary>Линии K-рентгена атома, кэВ, — для вакансии известной оболочки.</summary>
        public double[] VacancyLineKev = new double[0];

        /// <summary>Накопленные доли линий K-рентгена на одну K-вакансию с рентгеном (последняя — 1).</summary>
        public double[] VacancyLineCdf = new double[0];

        /// <summary>
        /// Атом с вакансией известной оболочки по таблицам `matdb` (флуоресценция K) и энергии связи
        /// L. null — данных об атоме нет, и энергия связи остаётся неучтённой, как прежде.
        /// </summary>
        public static CaptureRelaxation ForAtom(int z, double lEdgeKev)
        {
            MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(z);
            if (f == null)
            {
                return null;
            }

            var atom = new CaptureRelaxation
            {
                KEdgeKev = f.KEdgeKev,
                LEdgeKev = lEdgeKev,
                OmegaK = f.Omega(true),
            };
            if (f.LineKev != null && f.LineWeight != null && f.LineKev.Length == f.LineWeight.Length && f.LineKev.Length > 0)
            {
                double total = 0.0;
                foreach (double w in f.LineWeight)
                {
                    total += Math.Max(0.0, w);
                }

                if (total > 0.0)
                {
                    atom.VacancyLineKev = (double[])f.LineKev.Clone();
                    atom.VacancyLineCdf = new double[f.LineKev.Length];
                    double run = 0.0;
                    for (int i = 0; i < f.LineKev.Length; i++)
                    {
                        run += Math.Max(0.0, f.LineWeight[i]) / total;
                        atom.VacancyLineCdf[i] = i == f.LineKev.Length - 1 ? 1.0 : run;
                    }
                }
            }

            return atom;
        }
    }

    /// <summary>
    /// (`AMBER274`, этап Э2, 04.11.2026) СОБЫТИЯ РАСПАДА В ОБЪЁМЕ КРИСТАЛЛА.
    ///
    /// ⛔ Зачем отдельным куском. Собственная активность кристалла — это распад
    /// ВНУТРИ объёма: бета там же и оседает, каскад суммируется безусловно, а
    /// атомная релаксация дочернего атома (захват La-138) светит рентгеном с
    /// места распада. Ни один из путей пробы (`BranchEventFrom`,
    /// `CarriedElectronDeposit`) этого не даёт: они ведут частицу СНАРУЖИ и
    /// знают капсулу, которой у распада в кристалле нет.
    ///
    /// Перенос — существующий и не тронут: фотон ведёт <see cref="InCrystal"/>,
    /// электрон — <see cref="ElectronLoss"/>. Розыгрыш точки по объёму — здесь;
    /// событие целиком — <see cref="CrystalEventFrom"/> (ветвь беты со своим
    /// каскадом, как у `BranchEventFrom` физики 28, но без капсулы).
    ///
    /// ⛔ АЛЬФЫ СЮДА НЕ ВХОДЯТ: у LaBr3/CeBr3 нет ни пробегов ASTAR, ни гашения
    /// света (итог `AMBER273`, 29.10.2026), это этап Э3.
    /// </summary>
    public sealed partial class EfficiencySimulator
    {
        /// <summary>Сколько раз пробуем попасть в фигурную область, прежде чем сдаться.</summary>
        const int CrystalPointAttempts = 64;

        /// <summary>
        /// Точка, равномерная по ОБЪЁМУ кристалла сцены. false — кристалла в сцене
        /// нет (тогда и слоя собственной активности быть не может).
        ///
        /// Формы: цилиндр (`RIn..ROut`, `ZMin..ZMax`), коробка (`AX`, `AY`) и
        /// фигурная (`Shape`, полигон с дырой — `AMBER282`). У цилиндра радиус
        /// берётся как sqrt(U·(R²−r²)+r²) — иначе точка липнет к оси; у фигурной
        /// области точка отбирается по «внутри всех рёбер».
        /// </summary>
        internal bool TryCrystalPoint(out double x, out double y, out double z)
        {
            x = 0.0;
            y = 0.0;
            z = 0.0;
            this.EnsureBuilt();
            Region region = this.crystal;
            if (region == null || !(region.ZMax > region.ZMin))
            {
                return false;
            }

            for (int attempt = 0; attempt < CrystalPointAttempts; attempt++)
            {
                double px, py;
                if (region.IsBox)
                {
                    px = (2.0 * this.Uniform() - 1.0) * region.AX;
                    py = (2.0 * this.Uniform() - 1.0) * region.AY;
                }
                else
                {
                    double rIn = Math.Max(0.0, region.RIn);
                    double rOut = Math.Max(rIn, region.ROut);
                    double rho = Math.Sqrt(this.Uniform() * (rOut * rOut - rIn * rIn) + rIn * rIn);
                    double phi = 2.0 * Math.PI * this.Uniform();
                    px = rho * Math.Cos(phi);
                    py = rho * Math.Sin(phi);
                }

                double pz = region.ZMin + (region.ZMax - region.ZMin) * this.Uniform();
                if (!region.Contains(px, py, pz))
                {
                    continue;
                }

                x = px;
                y = py;
                z = pz;
                return true;
            }

            return false;
        }

        /// <summary>
        /// (`AMBER274`, Э2) СОБЫТИЕ РАСПАДА ЦЕЛИКОМ из точки внутри кристалла:
        /// бета (если у ветви она есть) + каскад уровня (гамма и конверсия) +
        /// bremsstrahlung обоих. Возвращает ВСЮ осевшую энергию, кэВ;
        /// <paramref name="gammaDeposit"/> — часть каскада (для отчёта).
        ///
        /// ⛔ Каскадные гаммы ведутся <see cref="InCrystal"/> напрямую, а не через
        /// очередь `PushPending`: очередь — путь СНАРУЖИ, у неё своя геометрия
        /// обхода. Из объёма кристалла перенос тот же, что у фотона пробы,
        /// попавшего в кристалл, — `InCrystal`.
        /// </summary>
        internal double CrystalEventFrom(ElectronBranchSpec spec, double x, double y, double z,
                                         bool withBeta, out double gammaDeposit)
        {
            return this.CrystalEventFrom(spec, null, x, y, z, withBeta, out gammaDeposit);
        }

        /// <summary>
        /// (`AMBER274`, ревизия по статьям, 12.11.2026) То же с РЕЛАКСАЦИЕЙ ВАКАНСИИ ПОСЛЕ ВНУТРЕННЕЙ
        /// КОНВЕРСИИ: электрон конверсии уносит E_γ − E_связи (так собрана строка ветви,
        /// <c>FsaBetaLibrary.EventSpec</c>), а энергия связи остаётся у дочернего атома и в
        /// кристалле оседает тоже — K-рентгеном (ведётся фотоном <see cref="InCrystal"/>) или
        /// оже-электронами на месте. Без <paramref name="daughter"/> (null) — прежний счёт: энергия
        /// связи пропадает (у пробы СНАРУЖИ это оговорка `Ф8`, у распада в объёме — дефект: переход
        /// 88.35 кэВ дочки Lu-176 конвертирован на 85 %, и слой клал 23 / 78 / 86 кэВ вместо 88.35).
        /// Общий механизм с захватом — <see cref="VacancyDeposit"/>.
        /// </summary>
        internal double CrystalEventFrom(ElectronBranchSpec spec, CaptureRelaxation daughter,
                                         double x, double y, double z,
                                         bool withBeta, out double gammaDeposit)
        {
            gammaDeposit = 0.0;
            if (spec == null)
            {
                return 0.0;
            }

            this.ForgetRay();
            double limit = this.PathLimit(x, y, z);
            this.lossAnnihilation = 0.0;
            this.annihilationEscapes = 0;
            this.lossXray = 0.0;
            this.lossXrayK = 0.0;
            this.lossXrayL = 0.0;
            this.lightDeposit = 0.0;
            this.ResetTrace();
            this.fromOutsideAnnihilation = false;
            this.outsideCos = NoOutsideScatter;
            this.pendCount = 0;

            double deposited = 0.0;
            if (withBeta && spec.BetaT != null && spec.BetaCdf != null && spec.BetaT.Length >= 2)
            {
                double te = SampleBranchBeta(spec, this.Uniform());
                if (te > 0.0)
                {
                    // ⛔ `ElectronLoss` возвращает УНЕСЁННУЮ энергию, а не осевшую
                    // (внутри: `escapedSelf` плюс вылет тормозных квантов). Осевшая —
                    // разность. Прежний счёт «прибавить возврат» терял бету целиком.
                    deposited += Math.Max(0.0, te - this.ElectronLoss(x, y, z, te, 0));
                    deposited = this.WalkPendingFrom(limit, deposited);
                }
            }

            double betaDeposit = deposited;
            int level = spec.StartLevel;
            int steps = 0;
            for (; steps < CascadeMaxSteps; steps++)
            {
                if (spec.LevelCount == null || level < 0 || level >= spec.LevelCount.Length)
                {
                    break;
                }

                int count = spec.LevelCount[level];
                if (count <= 0)
                {
                    break;
                }

                int first = spec.LevelFirst[level];
                double u = this.Uniform();
                int tr = first;
                while (tr < first + count - 1 && u >= spec.TransitionCdf[tr])
                {
                    tr++;
                }

                double v = this.Uniform();
                int kind = 0;
                while (kind < ElectronBranchSpec.EmitKinds - 1
                       && v >= spec.EmitCdf[tr * ElectronBranchSpec.EmitKinds + kind])
                {
                    kind++;
                }

                double e = spec.EmitKev[tr * ElectronBranchSpec.EmitKinds + kind];
                level = spec.TransitionTo[tr];

                // Энергия связи оболочки, с которой выбит электрон конверсии: энергия перехода
                // (вид 0) минус энергия электрона. Электрона нет (энергии связи в базе не нашлось,
                // `EmitKev` = 0) — у атома остаётся весь переход.
                double bound = 0.0;
                if (kind != 0 && daughter != null)
                {
                    double transition = spec.EmitKev[tr * ElectronBranchSpec.EmitKinds];
                    bound = Math.Max(0.0, transition - Math.Max(0.0, e));
                }

                if (e > 0.0)
                {
                    double ax, ay, az;
                    this.Isotropic(out ax, out ay, out az);
                    if (kind == 0)
                    {
                        double gone = this.InCrystal(x, y, z, ax, ay, az, e, 0);
                        deposited += Math.Max(0.0, e - gone);
                    }
                    else
                    {
                        deposited += Math.Max(0.0, e - this.ElectronLoss(x, y, z, e, 0));
                    }
                }

                if (bound > 0.0)
                {
                    deposited += this.VacancyDeposit(daughter, kind == 1, bound, x, y, z);
                }
            }

            deposited = this.WalkPendingFrom(limit, deposited);
            gammaDeposit = deposited - betaDeposit;
            return deposited;
        }

        /// <summary>
        /// (`AMBER274`, Э2) СОБЫТИЕ БЕЗ БЕТЫ из точки внутри кристалла: каскад
        /// гамм (захват электрона у La-138) и, если задана, атомная релаксация
        /// дочернего атома. Возвращает осевшую энергию, кэВ.
        /// </summary>
        internal double CrystalGammaEvent(double[] gammas, CaptureRelaxation relaxation, double x, double y, double z)
        {
            this.ForgetRay();
            double limit = this.PathLimit(x, y, z);
            this.lossAnnihilation = 0.0;
            this.annihilationEscapes = 0;
            this.lossXray = 0.0;
            this.lossXrayK = 0.0;
            this.lossXrayL = 0.0;
            this.lightDeposit = 0.0;
            this.ResetTrace();
            this.fromOutsideAnnihilation = false;
            this.outsideCos = NoOutsideScatter;
            this.pendCount = 0;

            double deposited = 0.0;
            if (gammas != null)
            {
                foreach (double e in gammas)
                {
                    if (!(e > 0.0))
                    {
                        continue;
                    }

                    double ax, ay, az;
                    this.Isotropic(out ax, out ay, out az);
                    double gone = this.InCrystal(x, y, z, ax, ay, az, e, 0);
                    deposited += Math.Max(0.0, e - gone);
                }
            }

            if (relaxation != null)
            {
                deposited += this.CrystalRelaxation(relaxation, x, y, z);
            }

            return this.WalkPendingFrom(limit, deposited);
        }

        /// <summary>
        /// (`AMBER274`, Э2) АТОМНАЯ РЕЛАКСАЦИЯ ДОЧЕРНЕГО АТОМА на месте распада: линия
        /// K-рентгена по вероятности на захват, остаток энергии вакансии — на месте;
        /// K-вакансия без рентгена отдаёт энергию связи K оже-электронами; захват не
        /// с K-оболочки — энергию связи L. Возвращает осевшую энергию, кэВ.
        ///
        /// ⛔ Это НЕ флуоресценция кристалла (`SampleFluorescence`): там атом —
        /// вещество сцены, здесь — дочерний атом распада.
        /// </summary>
        internal double CrystalRelaxation(CaptureRelaxation relaxation, double x, double y, double zAxis)
        {
            double u = this.Uniform();
            for (int i = 0; i < relaxation.LineKev.Length; i++)
            {
                if (u < relaxation.LineCdf[i])
                {
                    return this.KXrayDeposit(relaxation.LineKev[i], relaxation.KEdgeKev, x, y, zAxis);
                }
            }

            if (u < relaxation.KCapture)
            {
                return this.KAugerDeposit(relaxation.KEdgeKev, relaxation.LEdgeKev);
            }

            this.AddLight(relaxation.LEdgeKev, relaxation.LEdgeKev);
            return relaxation.LEdgeKev;
        }

        /// <summary>
        /// (`AMBER274`, ревизия по статьям, 12.11.2026) ВАКАНСИЯ ИЗВЕСТНОЙ ОБОЛОЧКИ с энергией связи
        /// <paramref name="boundKev"/> — общий механизм захвата и внутренней конверсии. K-вакансия
        /// (<paramref name="kShell"/>): с вероятностью ω_K линия K-рентгена по долям линий
        /// (<see cref="CaptureRelaxation.VacancyLineCdf"/>) фотоном из точки распада, остаток — на
        /// месте; иначе оже. Вакансия L, M и выше — энергия связи на месте. Возвращает осевшее, кэВ.
        ///
        /// ⚠ Отличие от <see cref="CrystalRelaxation"/> одно: там оболочка РАЗЫГРЫВАЕТСЯ (доля
        /// K-захвата), здесь она известна из вида испускания перехода.
        /// </summary>
        internal double VacancyDeposit(CaptureRelaxation atom, bool kShell, double boundKev, double x, double y, double zAxis)
        {
            if (atom == null || !(boundKev > 0.0))
            {
                return 0.0;
            }

            if (!kShell || atom.VacancyLineKev == null || atom.VacancyLineKev.Length == 0)
            {
                this.AddLight(boundKev, boundKev);
                return boundKev;
            }

            if (this.Uniform() < atom.OmegaK)
            {
                double v = this.Uniform();
                int line = 0;
                while (line < atom.VacancyLineKev.Length - 1 && v >= atom.VacancyLineCdf[line])
                {
                    line++;
                }

                return this.KXrayDeposit(atom.VacancyLineKev[line], boundKev, x, y, zAxis);
            }

            return this.KAugerDeposit(boundKev, atom.LEdgeKev);
        }

        /// <summary>
        /// K-вакансия с рентгеном: квант ведётся <see cref="InCrystal"/>, остаток энергии связи
        /// (вакансия верхней оболочки) оседает на месте. Свет остатка — как у электрона этой энергии.
        /// </summary>
        double KXrayDeposit(double lineKev, double kEdgeKev, double x, double y, double zAxis)
        {
            double e = Math.Min(lineKev, kEdgeKev);
            double ax, ay, az;
            this.Isotropic(out ax, out ay, out az);
            double gone = this.InCrystal(x, y, zAxis, ax, ay, az, e, 0);
            double rest = Math.Max(0.0, kEdgeKev - e);
            this.AddLight(rest, rest);
            return Math.Max(0.0, e - gone) + rest;
        }

        /// <summary>
        /// K-вакансия без рентгена: энергия связи K оседает на месте. Свет — как у оже-электрона
        /// KLL (E_K − 2·E_L) и двух L-вакансий: непропорциональность света считает электроны
        /// порознь, а не «один электрон в E_K». Энергии связи L нет — одним куском.
        /// </summary>
        double KAugerDeposit(double kEdgeKev, double lEdgeKev)
        {
            double kll = kEdgeKev - 2.0 * lEdgeKev;
            if (lEdgeKev > 0.0 && kll > 0.0)
            {
                this.AddLight(kll, kll);
                this.AddLight(lEdgeKev, lEdgeKev);
                this.AddLight(lEdgeKev, lEdgeKev);
            }
            else
            {
                this.AddLight(kEdgeKev, kEdgeKev);
            }

            return kEdgeKev;
        }

        /// <summary>(`AMBER274`, ревизия по статьям) Свет текущего события в кэВ-эквивалентах кривой L(E) — для слоя «по свету».</summary>
        internal double CrystalEventLight
        {
            get { return this.lightDeposit; }
        }

        /// <summary>(`AMBER274`, ревизия по статьям) Есть ли у кристалла сцены кривая света (иначе шкала слоя — энергия).</summary>
        internal bool HasLightCurve
        {
            get
            {
                this.EnsureBuilt();
                return this.lightYield != null;
            }
        }
    }
}
