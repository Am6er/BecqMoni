using System;
using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// ⛔ (`A72`, П27 12.09.2026; решение Amber 12.09.2026, вопросником,
    /// дословно: «Вести электрон переносом») ПЕРЕНОС ЭЛЕКТРОНА ПО КРИСТАЛЛУ —
    /// вторая половина <see cref="EfficiencySimulator"/>, под ключом
    /// <see cref="EfficiencySimulator.ElectronTransport"/> (умолчание склада
    /// ВКЛ с 13.09.2026 — физика 17, П37; клеймо `etr=1`, хвост файла `ETRN`).
    ///
    /// ЧТО БЫЛО НЕ ТАК. Вылет электрона считался ЭФФЕКТИВНОЙ ГЛУБИНОЙ
    /// (<see cref="EfficiencySimulator.ElectronLoss"/>, ветка без ключа):
    /// изотропное направление, прямой путь до грани, порогово-линейная доля
    /// пробега с мягкой добавкой (~~`A63`~~) и подгоняемый показатель расхода
    /// (~~`A70`~~). Три допущения неверны каждое, и П20 §3 измерила цену на
    /// голых кристаллах ~1 см³ против Geant4 при 1461 кэВ: пик +5.7 % (RC103
    /// CsI 10×10×10) и +5.5 % (OBS CsI 7×7×30), полоса 0–25 %E −13…−17 %,
    /// полосы 50–100 %E +7…+9 % — при сходящейся полной эффективности. Наклон
    /// эффективной глубины подгоняет ПИК, но разводит полосы в разные стороны:
    /// неверна ФОРМА уноса, а не его величина.
    ///
    /// ЧТО СДЕЛАНО. Электрон ведётся сгущённой историей (condensed history,
    /// класс I — вторичных δ-электронов нет, потери непрерывны):
    ///
    /// 1. Направление рождения — по процессу, а не изотропно:
    ///    * фотоэффект — Заутер—Гаврила (K-оболочка, тот же розыгрыш, что у
    ///      `G4SauterGavrilaAngularDistribution`), относительно направления
    ///      поглощённого кванта;
    ///    * комптон — из кинематики: импульс электрона = импульс кванта до
    ///      рассеяния минус после, направления оба известны в точке события;
    ///    * пара — по существующей схеме деления энергии, направление
    ///      лептона — модифицированный Цай (`G4ModifiedTsai`), вперёд с углом
    ///      ~mc²/E;
    ///    * электроны оже-каскада (EADL) — изотропно, как и положено.
    /// 2. Шаг по ПРОБЕГУ: остаточный пробег CSDA из ESTAR
    ///    (<see cref="ElectronData.RangeOf"/>, `matdb`), шаг —
    ///    <see cref="EfficiencySimulator.ElectronStepFraction"/> остаточного
    ///    пробега; энергия в конце шага — обратной таблицей
    ///    <see cref="ElectronData.EnergyOfRange"/>. Разброса потерь (Ландау)
    ///    нет.
    /// 3. Многократное рассеяние — «случайный шарнир» (random hinge, PENELOPE):
    ///    шаг проходится прямо до случайной точки, там направление
    ///    поворачивается на угол всего шага, дальше снова прямо. Ширина угла —
    ///    формула Хайленда (PDG) по радиационной длине кристалла, посчитанной
    ///    из состава по Цаю (`CsI` 8.39 г/см², `NaI` 9.49 — сходится с PDG);
    ///    распределение по 1−cos θ — экспонента со средним θ₀², обрезанная на
    ///    2 (в пределе больших θ₀ — изотропия, в пределе малых — гауссова
    ///    ширина Хайленда). ⚠ Ширина ПРОВЕРЕНА, а не принята: развёртка
    ///    множителем ×1.5/×2/×3 (П27 §4) на RC103 1461 кэВ уводит пик с
    ///    +1.5 % до +3.9/+4.8/+5.3 % против Geant4, а на 59.5 кэВ полосы не
    ///    двигает вовсе — форма уноса внизу шкалы решается не рассеянием, а
    ///    раздельным каскадом (`LightCascadeSplit`, `kdip=1`).
    /// 4. Вылет через грань — РЕАЛЬНЫЙ: прямой отрезок шага пересёк грань
    ///    кристалла (<see cref="EfficiencySimulator.CrystalPath"/>) — электрон
    ///    уходит с той энергией, что отвечает остаточному пробегу в точке
    ///    выхода. Обратно из обвязки электрон не возвращается (вне кристалла
    ///    его вести нечем — как и позитрон в `PositronStop`).
    /// 5. Тормозное — КАК БЫЛО: кванты разыгрываются в точке рождения по
    ///    <see cref="ThickTargetBrem"/>, излучённое зажимает уносимую энергию
    ///    (`M3` — отдельная строка, не трогается).
    ///
    /// ЦЕНА. Горячее ядро: <see cref="EfficiencySimulator.ElectronLoss"/>
    /// зовётся на каждое взаимодействие. Чтобы платить только там, где вылет
    /// возможен, перед первым шагом и после каждого проверяется расстояние до
    /// ближайшей грани (<see cref="CrystalNearestFace"/>): остаточный пробег
    /// короче него — электрон погибнет внутри при любой траектории, и остаток
    /// кладётся на месте без единого розыгрыша. На крупном кристалле это
    /// почти каждый электрон. Цена на узел измерена П27 §5.
    ///
    /// ⛔ ВЫКЛЮЧЕННЫЙ КЛЮЧ НЕ ТЯНЕТ НИ ОДНОГО СЛУЧАЙНОГО ЧИСЛА и оставляет
    /// прежний результат до последнего бита (приёмка `MatrixDiffProbe`
    /// 0.000/0.000, П27 §2). Включённый — тянет свои (направление рождения,
    /// точка шарнира, угол, азимут на каждом шаге), поток другой.
    /// </summary>
    public sealed partial class EfficiencySimulator
    {
        /// <summary>Откуда у электрона направление рождения (`A72`).</summary>
        enum ElectronBirth
        {
            /// <summary>Изотропно — оже-электроны каскада, неизвестный процесс.</summary>
            Isotropic,

            /// <summary>Фотоэлектрон: Заутер—Гаврила относительно направления кванта.</summary>
            Photo,

            /// <summary>Направление задано вызывающим (комптон — из кинематики).</summary>
            Given,

            /// <summary>Лептон пары: модифицированный Цай относительно направления кванта.</summary>
            Pair,
        }

        /// <summary>Ниже этой энергии электрон кладётся на месте, кэВ.</summary>
        const double TransportCutKev = 1.0;

        /// <summary>
        /// Предохранитель от бесконечного шага: остаточный пробег убывает на
        /// долю шага за шаг, и от 3 МэВ до 1 кэВ при доле 0.1 нужно ~120
        /// шагов; ранний выход по ближайшей грани обрывает почти все треки
        /// много раньше.
        /// </summary>
        const int TransportMaxSteps = 400;

        /// <summary>Радиационная длина кристалла, г/см²; отрицательная — не считалась.</summary>
        double crystalRadiationLength = -1.0;

        /// <summary>
        /// Радиационная длина вещества кристалла по Цаю (PDG, «Passage of
        /// particles through matter», ф. 34.25–34.26), г/см². Состав — массовые
        /// доли <see cref="GeometryMaterial.Fractions"/>, массы —
        /// <see cref="MaterialDatabase.AtomicMass"/>. Смесь — по обратным
        /// длинам с массовыми долями. Проверено на CsI (8.39) и NaI (9.49)
        /// против таблицы PDG — сходится в третьем знаке.
        /// </summary>
        double CrystalRadiationLength()
        {
            if (this.crystalRadiationLength > 0.0)
            {
                return this.crystalRadiationLength;
            }

            double inverse = 0.0;
            Dictionary<int, double> mass = MaterialDatabase.AtomicMass;
            foreach (KeyValuePair<int, double> pair in this.geometry.Crystal.Fractions)
            {
                double a;
                if (pair.Value > 0.0 && mass.TryGetValue(pair.Key, out a) && a > 0.0)
                {
                    inverse += pair.Value / TsaiRadiationLength(pair.Key, a);
                }
            }

            // Состава нет (сцена без вещества) — вылета всё равно не будет:
            // без `electron` ветка переноса не заходит. Единица — чтобы не
            // делить на ноль, если всё же зайдёт.
            this.crystalRadiationLength = inverse > 0.0 ? 1.0 / inverse : 1.0;
            return this.crystalRadiationLength;
        }

        /// <summary>Радиационная длина элемента по Цаю, г/см².</summary>
        static double TsaiRadiationLength(int z, double a)
        {
            double lrad, lradPrime;
            switch (z)
            {
                case 1: lrad = 5.31; lradPrime = 6.144; break;
                case 2: lrad = 4.79; lradPrime = 5.621; break;
                case 3: lrad = 4.74; lradPrime = 5.805; break;
                case 4: lrad = 4.71; lradPrime = 5.924; break;
                default:
                    lrad = Math.Log(184.15 * Math.Pow(z, -1.0 / 3.0));
                    lradPrime = Math.Log(1194.0 * Math.Pow(z, -2.0 / 3.0));
                    break;
            }

            double alpha = z / 137.035999;
            double a2 = alpha * alpha;
            double f = a2 * (1.0 / (1.0 + a2) + 0.20206 - 0.0369 * a2 + 0.0083 * a2 * a2
                             - 0.002 * a2 * a2 * a2);
            return 716.408 * a / (z * (double)z * (lrad - f) + z * lradPrime);
        }

        /// <summary>
        /// Расстояние от точки внутри кристалла до ближайшей его грани, см —
        /// нижняя граница пути до выхода по ЛЮБОМУ направлению. Внутренний
        /// радиус кольца не смотрится — как и в <see cref="CrystalPath"/>.
        /// </summary>
        double CrystalNearestFace(double x, double y, double z)
        {
            Region c = this.crystal;
            double d = Math.Min(z - c.ZMin, c.ZMax - z);
            if (c.IsBox)
            {
                d = Math.Min(d, Math.Min(c.AX - Math.Abs(x), c.AY - Math.Abs(y)));
            }
            else
            {
                d = Math.Min(d, c.ROut - Math.Sqrt(x * x + y * y));
            }

            return d > 0.0 ? d : 0.0;
        }

        /// <summary>
        /// Ширина θ₀ многократного рассеяния на шаге <paramref name="xOverX0"/>
        /// радиационных длин при кинетической энергии <paramref name="tKev"/> —
        /// формула Хайленда в редакции PDG (с β² под логарифмом), радианы.
        /// Скобка зажата снизу: формула подогнана для 10⁻³ &lt; x/X₀ &lt; 100, а
        /// на самых коротких шагах логарифм увёл бы её в ноль и ниже.
        /// </summary>
        static double HighlandTheta0(double tKev, double xOverX0)
        {
            double gamma = 1.0 + tKev / ElectronMassKev;
            double beta2 = 1.0 - 1.0 / (gamma * gamma);
            double p = Math.Sqrt(tKev * (tKev + 2.0 * ElectronMassKev));    // кэВ/c
            double betaP = Math.Sqrt(beta2) * p;                            // кэВ
            double bracket = 1.0 + 0.038 * Math.Log(xOverX0 / beta2);
            if (bracket < 0.25)
            {
                bracket = 0.25;
            }

            return 13600.0 / betaP * Math.Sqrt(xOverX0) * bracket;
        }

        /// <summary>
        /// Розыгрыш 1−cos θ отклонения шага: экспонента со средним θ₀²,
        /// обрезанная на 2. Для малых θ₀ это в точности гауссово (рэлеевское)
        /// распределение пространственного угла с проекционной шириной θ₀; для
        /// больших — переходит в изотропию, куда и уходит трек мягкого
        /// электрона.
        /// </summary>
        double SampleHingeMu(double theta0)
        {
            double s = theta0 * theta0;
            if (s > 50.0)
            {
                return 2.0 * this.Uniform();
            }

            double r = this.Uniform();
            double mu = -s * Math.Log(1.0 - r * (1.0 - Math.Exp(-2.0 / s)));
            return mu > 2.0 ? 2.0 : (mu < 0.0 ? 0.0 : mu);
        }

        /// <summary>
        /// Косинус угла фотоэлектрона к направлению кванта — Заутер—Гаврила
        /// для K-оболочки, розыгрыш отбором как в Geant4
        /// (`G4SauterGavrilaAngularDistribution::SampleDirection`).
        /// </summary>
        double SauterCosine(double tKev)
        {
            double tau = tKev / ElectronMassKev;
            if (tau > 1000.0)
            {
                return 1.0;
            }

            double gamma = tau + 1.0;
            double beta = Math.Sqrt(tau * (tau + 2.0)) / gamma;
            double a = (1.0 - beta) / beta;
            double ap2 = a + 2.0;
            double b = 0.5 * beta * gamma * (gamma - 1.0) * (gamma - 2.0);
            double grej = 2.0 * (1.0 + a * b) / a;
            double z, g;
            int guard = 0;
            do
            {
                double q = this.Uniform();
                z = 2.0 * a * (2.0 * q + ap2 * Math.Sqrt(q)) / (ap2 * ap2 - 4.0 * q);
                g = (2.0 - z) * (1.0 / (a + z) + b);
            }
            while (g < this.Uniform() * grej && ++guard < 1000);

            return 1.0 - z;
        }

        /// <summary>
        /// Косинус угла лептона пары к направлению кванта — модифицированный
        /// Цай (`G4ModifiedTsai::SampleCosTheta`), <paramref name="tKev"/> —
        /// кинетическая энергия лептона.
        /// </summary>
        double TsaiCosine(double tKev)
        {
            const double A1 = 1.6, A2 = A1 / 3.0, Border = 0.25;
            double uMax = 2.0 * (1.0 + tKev / ElectronMassKev);
            double u;
            int guard = 0;
            do
            {
                double uu = -Math.Log(this.Uniform() * this.Uniform());
                u = Border > this.Uniform() ? uu * A1 : uu * A2;
            }
            while (u > uMax && ++guard < 1000);

            return 1.0 - 2.0 * u * u / (uMax * uMax);
        }

        /// <summary>
        /// Направление рождения электрона по процессу (<see cref="ElectronBirth"/>):
        /// опорное направление <paramref name="rx"/>… — квант для `Photo`/`Pair`,
        /// сам электрон для `Given`; для `Isotropic` не читается.
        /// </summary>
        void ElectronBirthDirection(ElectronBirth birth, double te, double rx, double ry, double rz,
                                    out double ux, out double uy, out double uz)
        {
            switch (birth)
            {
                case ElectronBirth.Photo:
                    ux = rx; uy = ry; uz = rz;
                    this.Rotate(ref ux, ref uy, ref uz, this.SauterCosine(te));
                    return;
                case ElectronBirth.Pair:
                    ux = rx; uy = ry; uz = rz;
                    this.Rotate(ref ux, ref uy, ref uz, this.TsaiCosine(te));
                    return;
                case ElectronBirth.Given:
                    double norm = Math.Sqrt(rx * rx + ry * ry + rz * rz);
                    if (norm > 1e-12)
                    {
                        ux = rx / norm; uy = ry / norm; uz = rz / norm;
                        return;
                    }

                    break;
            }

            this.Isotropic(out ux, out uy, out uz);
        }

        /// <summary>
        /// Направление комптоновского электрона из кинематики: импульс кванта
        /// до рассеяния минус после (кэВ/c, направления единичные). Нормировка
        /// — в <see cref="ElectronBirthDirection"/>; нулевой вектор (квант не
        /// отклонился) там же уходит в изотропию.
        /// </summary>
        static void ComptonElectronDirection(double e, double ux0, double uy0, double uz0,
                                             double scattered, double ux1, double uy1, double uz1,
                                             out double dx, out double dy, out double dz)
        {
            dx = e * ux0 - scattered * ux1;
            dy = e * uy0 - scattered * uy1;
            dz = e * uz0 - scattered * uz1;
        }

        /// <summary>
        /// Провести электрон кинетической энергии <paramref name="te"/> из точки
        /// (x, y, z) в направлении (ux, uy, uz) до гибели или до грани.
        /// Возвращает энергию, унесённую через грань (0 — погиб внутри), не
        /// больше <paramref name="cap"/> (то, что осталось после тормозного).
        /// </summary>
        double TransportElectron(double x, double y, double z, double ux, double uy, double uz,
                                 double te, double cap)
        {
            double density = this.geometry.Crystal.Density;
            double x0 = this.CrystalRadiationLength();
            double residual = ElectronData.RangeOf(this.electron, te);     // г/см²
            if (!(residual > 0.0) || !(density > 0.0))
            {
                return 0.0;
            }

            // Ранний выход: пробег короче расстояния до ближайшей грани —
            // погибнет внутри при любой траектории.
            if (residual / density <= this.CrystalNearestFace(x, y, z))
            {
                return 0.0;
            }

            double fraction = this.ElectronStepFraction;
            if (!(fraction > 0.0) || fraction > 1.0)
            {
                fraction = 1.0;
            }

            double t = te;
            for (int step = 0; step < TransportMaxSteps && t > TransportCutKev; step++)
            {
                double stepG = residual * fraction;                          // г/см²
                double stepCm = stepG / density;

                // Прямой ход до шарнира.
                double first = stepCm * this.Uniform();
                double toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (first >= toEdge)
                {
                    return this.EscapeEnergy(residual - toEdge * density, cap);
                }

                x += ux * first;
                y += uy * first;
                z += uz * first;

                // Поворот на угол всего шага, ширина — по энергии в его середине.
                double tMid = ElectronData.EnergyOfRange(this.electron, residual - 0.5 * stepG);
                if (tMid < TransportCutKev)
                {
                    tMid = TransportCutKev;
                }

                double theta0 = HighlandTheta0(tMid, stepG / x0);
                this.Rotate(ref ux, ref uy, ref uz, 1.0 - this.SampleHingeMu(theta0));

                // Прямой ход до конца шага.
                double second = stepCm - first;
                toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (second >= toEdge)
                {
                    return this.EscapeEnergy(residual - (first + toEdge) * density, cap);
                }

                x += ux * second;
                y += uy * second;
                z += uz * second;
                residual -= stepG;
                t = ElectronData.EnergyOfRange(this.electron, residual);

                if (residual / density <= this.CrystalNearestFace(x, y, z))
                {
                    return 0.0;
                }
            }

            return 0.0;
        }

        /// <summary>Энергия электрона на грани по остаточному пробегу, кэВ, не больше зажима.</summary>
        double EscapeEnergy(double residualG, double cap)
        {
            if (!(residualG > 0.0))
            {
                return 0.0;
            }

            double t = ElectronData.EnergyOfRange(this.electron, residualG);
            return t < cap ? (t > 0.0 ? t : 0.0) : (cap > 0.0 ? cap : 0.0);
        }
    }
}
