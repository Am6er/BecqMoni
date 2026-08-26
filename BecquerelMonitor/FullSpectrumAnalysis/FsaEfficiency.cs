using System;
using System.Collections.Generic;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Кривая эффективности регистрации: лог-лог интерполяция по точкам.
    ///
    /// Интерполятор в программе ОДИН. Раньше их было два — этот и монотонный
    /// кубический сплайн в <see cref="Utils.ROIAriphmetics"/>, — и по одним и
    /// тем же точкам они давали разную эффективность: активность зависела от
    /// того, кто её спросил.
    ///
    /// Политик на краях две, и это осознанно:
    ///
    /// * <see cref="TryEval"/> — строгая. Вне таблицы значения НЕТ, и об этом
    ///   говорится вслух. По ней считается коэффициент перевода в беккерели:
    ///   там ошибка молча превращается в неверную активность, а «нет значения»
    ///   пользователь увидит и поправит.
    /// * <see cref="Eval"/> — мягкая, держит края константой. По ней работает
    ///   полноспектральная декомпозиция: у неё в шаблоне сотни линий по всей
    ///   шкале, и отказ на краю выбросил бы линию из шаблона целиком — это
    ///   хуже заниженного веса далёкой линии. Экстраполировать наклоном на
    ///   краю опаснее и того.
    /// </summary>
    public sealed class FsaEfficiency
    {
        readonly double[] logEnergy;
        readonly double[] logEfficiency;
        readonly double[] errorPercent;

        FsaEfficiency(List<Point> points)
        {
            points.Sort((a, b) => a.Energy.CompareTo(b.Energy));
            List<double> energies = new List<double>(points.Count);
            List<double> values = new List<double>(points.Count);
            List<double> errors = new List<double>(points.Count);
            foreach (Point point in points)
            {
                // дубль энергии дал бы нулевой шаг интерполяции и NaN в образе
                if (energies.Count > 0 && point.Energy <= Math.Exp(energies[energies.Count - 1]))
                {
                    continue;
                }

                energies.Add(Math.Log(point.Energy));
                values.Add(Math.Log(Math.Max(point.Efficiency, 1e-12)));
                errors.Add(point.ErrorPercent);
            }

            this.logEnergy = energies.ToArray();
            this.logEfficiency = values.ToArray();
            this.errorPercent = errors.ToArray();
        }

        struct Point
        {
            public double Energy;
            public double Efficiency;
            public double ErrorPercent;
        }

        /// <summary>Нижний край таблицы, кэВ.</summary>
        public double MinEnergy
        {
            get { return this.logEnergy.Length == 0 ? 0.0 : Math.Exp(this.logEnergy[0]); }
        }

        /// <summary>Верхний край таблицы, кэВ.</summary>
        public double MaxEnergy
        {
            get
            {
                return this.logEnergy.Length == 0
                    ? 0.0
                    : Math.Exp(this.logEnergy[this.logEnergy.Length - 1]);
            }
        }


        /// <summary>
        /// Кривая из конфигурации эффективности прибора или из снимка, который
        /// спектр несёт в своём файле.
        /// </summary>
        public static FsaEfficiency FromConfig(EfficiencyConfigData config)
        {
            return config == null ? null : FromPoints(config.Curve);
        }

        /// <summary>
        /// Точки с неположительной эффективностью отбрасываются: в поставочных
        /// файлах встречаются и нули, и физически невозможные значения. Меньше
        /// двух годных точек — кривой нет вовсе, и возвращается null, а не
        /// пустой объект: пустая кривая обязана отвечать «значения нет», а не
        /// подставлять единицу, как делала прежняя.
        /// </summary>
        static FsaEfficiency FromPoints(List<ROIEfficiencyData> source)
        {
            if (source == null)
            {
                return null;
            }

            List<Point> points = new List<Point>();
            foreach (ROIEfficiencyData point in source)
            {
                if (point == null || point.Energy <= 0.0 || point.Efficiency <= 0.0 || point.Efficiency > 1.0)
                {
                    continue;
                }

                points.Add(new Point
                {
                    Energy = point.Energy,
                    Efficiency = point.Efficiency,
                    ErrorPercent = point.ErrorPercent,
                });
            }

            if (points.Count < 2)
            {
                return null;
            }

            FsaEfficiency curve = new FsaEfficiency(points);
            return curve.logEnergy.Length >= 2 ? curve : null;
        }

        /// <summary>
        /// ⛔ ПОЛ ПОЛОСЫ БИБЛИОТЕКИ ПО САМОЙ КРИВОЙ (решение Amber 27.08.2026,
        /// `S98`): наименьшая ТАБЛИЧНАЯ энергия, где эффективность достигает
        /// заданной доли от максимума кривой. Ниже неё модель предсказывает
        /// практически нулевой отклик, и линии там дают почти вырожденные
        /// столбцы в NNLS.
        ///
        /// Почему доля, а не число килоэлектронвольт: пол 10 кэВ, поставленный
        /// умолчанием 25.08.2026, стоил понятной части корпуса +49 % Σχ²
        /// (465.9 → 694.8) именно потому, что у 79 кривых из 83 эффективность в
        /// первой точке ниже 1e-5. Развёртка по полу показала, что вся цена
        /// лежит между 10 и 20 кэВ, а 20 — такое же произвольное число: у 19
        /// спектров кривая набирает 1 % от максимума только к 30 кэВ.
        ///
        /// Возвращает 0, если кривой нет или доля недостижима — «пола по кривой
        /// назначить нечем», и звать это отказом нельзя: у 38 спектров корпуса
        /// из 121 кривой нет вовсе, и им положена запасная ветвь.
        /// </summary>
        public double FloorAtFraction(double fraction)
        {
            if (this.logEnergy.Length < 2 || !(fraction > 0.0) || fraction > 1.0)
            {
                return 0.0;
            }

            double best = double.NegativeInfinity;
            for (int i = 0; i < this.logEfficiency.Length; i++)
            {
                if (this.logEfficiency[i] > best)
                {
                    best = this.logEfficiency[i];
                }
            }

            if (double.IsNegativeInfinity(best))
            {
                return 0.0;
            }

            // сравнение в логарифмах — тех же, в которых кривая и хранится
            double want = best + Math.Log(fraction);
            for (int i = 0; i < this.logEfficiency.Length; i++)
            {
                if (this.logEfficiency[i] >= want)
                {
                    return Math.Exp(this.logEnergy[i]);
                }
            }

            return 0.0;
        }

        /// <summary>
        /// Строгая выборка: эффективность и её погрешность в процентах.
        /// false — значения нет: кривая пуста либо энергия за краем таблицы.
        /// Продолжать константой здесь нельзя: у края кривая уже падает круто,
        /// и «эффективность как на 3000 кэВ» на 4000 кэВ — не приближение, а
        /// выдумка, которая молча уедет в активность.
        /// </summary>
        public bool TryEval(double energy, out double efficiency, out double errorPercent)
        {
            efficiency = 0.0;
            errorPercent = 0.0;
            if (this.logEnergy.Length < 2 || !(energy > 0.0))
            {
                return false;
            }

            double x = Math.Log(energy);
            if (x < this.logEnergy[0] || x > this.logEnergy[this.logEnergy.Length - 1])
            {
                return false;
            }

            int hi = 1;
            while (hi < this.logEnergy.Length - 1 && this.logEnergy[hi] < x)
            {
                hi++;
            }

            double f = (x - this.logEnergy[hi - 1]) / (this.logEnergy[hi] - this.logEnergy[hi - 1]);
            efficiency = Math.Exp(this.logEfficiency[hi - 1]
                                  + f * (this.logEfficiency[hi] - this.logEfficiency[hi - 1]));
            // Погрешность интерполируется линейно, а не в логарифме: она уже
            // относительная (проценты) и через ноль не проходит.
            errorPercent = this.errorPercent[hi - 1]
                           + f * (this.errorPercent[hi] - this.errorPercent[hi - 1]);
            return true;
        }

        /// <summary>
        /// Мягкая выборка для полноспектральной декомпозиции: за краями таблицы
        /// держится константой. Ноль означает «кривой нет» — вызывающий такую
        /// линию пропускает.
        /// </summary>
        public double Eval(double energy)
        {
            if (this.logEnergy.Length == 0)
            {
                return 0.0;
            }

            double x = Math.Log(Math.Max(energy, 1.0));
            if (x <= this.logEnergy[0])
            {
                return Math.Exp(this.logEfficiency[0]);
            }

            if (x >= this.logEnergy[this.logEnergy.Length - 1])
            {
                return Math.Exp(this.logEfficiency[this.logEfficiency.Length - 1]);
            }

            int hi = 1;
            while (this.logEnergy[hi] < x)
            {
                hi++;
            }

            double f = (x - this.logEnergy[hi - 1]) / (this.logEnergy[hi] - this.logEnergy[hi - 1]);
            return Math.Exp(this.logEfficiency[hi - 1] + f * (this.logEfficiency[hi] - this.logEfficiency[hi - 1]));
        }
    }
}
