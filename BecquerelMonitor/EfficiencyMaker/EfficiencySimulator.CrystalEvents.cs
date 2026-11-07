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

        /// <summary>Энергия связи L дочернего атома (средняя линия L-рентгена `nucdb`), кэВ.</summary>
        public double LEdgeKev;
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
                if (!(e > 0.0))
                {
                    continue;
                }

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
                    double e = relaxation.LineKev[i];
                    double ax, ay, az;
                    this.Isotropic(out ax, out ay, out az);
                    double gone = this.InCrystal(x, y, zAxis, ax, ay, az, e, 0);
                    return Math.Max(0.0, e - gone) + Math.Max(0.0, relaxation.KEdgeKev - e);
                }
            }

            return u < relaxation.KCapture ? relaxation.KEdgeKev : relaxation.LEdgeKev;
        }
    }
}
