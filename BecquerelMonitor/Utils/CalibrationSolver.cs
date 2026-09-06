using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearRegression;
using System.Linq;

namespace BecquerelMonitor.Utils
{
    public static class CalibrationSolver
    {
        public static double[] Solve (List<CalibrationPoint> points, int PolynomialOrder)
        {
            Matrix<double> matrix;
            Vector<double> vector;
            double[,] dense_matrix = new double[points.Count, PolynomialOrder + 1];
            double[] dense_vector = new double[points.Count];

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = PolynomialOrder; j >= 0; j--)
                {
                    double val = (double)Math.Pow(points[i].Channel, j);
                    dense_matrix[i, j] = val;
                }
                dense_vector[i] = (double)points[i].Energy;
            }

            matrix = Matrix<double>.Build.DenseOfArray(dense_matrix);
            vector = Vector<double>.Build.Dense(dense_vector);

            double[] retvalue= matrix.Solve(vector).ToArray();

            return retvalue;

            /*
            Matrix<double> matrix = Matrix<double>.Build.DenseOfArray(new double[,] {
                        { (double)Math.Pow(ch1,4), (double)Math.Pow(ch1,3), (double)Math.Pow(ch1,2), (double)ch1, 1.0 },
                        { (double)Math.Pow(ch2,4), (double)Math.Pow(ch2,3), (double)Math.Pow(ch2,2), (double)ch2, 1.0 },
                        { (double)Math.Pow(ch3,4), (double)Math.Pow(ch3,3), (double)Math.Pow(ch3,2), (double)ch3, 1.0 },
                        { (double)Math.Pow(ch4,4), (double)Math.Pow(ch4,3), (double)Math.Pow(ch4,2), (double)ch4, 1.0 },
                        { (double)Math.Pow(ch5,4), (double)Math.Pow(ch5,3), (double)Math.Pow(ch5,2), (double)ch5, 1.0 }
                    });
            Vector<double> matrix2 = Vector<double>.Build.Dense(new double[] {
                        (double)this.calibrationPoints[0].Energy,
                        (double)this.calibrationPoints[1].Energy,
                        (double)this.calibrationPoints[2].Energy,
                        (double)this.calibrationPoints[3].Energy,
                        (double)this.calibrationPoints[4].Energy
                    });
            */
        }

        public static decimal getEnergyFromNthPoly(float[] coeff_vector, int channel)
        {
            decimal energy = 0;
            for (int i = 0; i < coeff_vector.Length; i++)
            {
                energy += (decimal)coeff_vector[i] * (decimal)Math.Pow(channel, i);
            }
            return energy;
        }

        public static double[] Solve(List<CalibrationPeak> peak, int PolynomialOrder)
        {
            Matrix<double> matrix;
            Vector<double> vector;
            double[,] dense_matrix = new double[peak.Count, PolynomialOrder + 1];
            double[] dense_vector = new double[peak.Count];

            for (int i = 0; i < peak.Count; i++)
            {
                for (int j = PolynomialOrder; j >= 0; j--)
                {
                    double val = (double)Math.Pow(peak[i].Channel, j);
                    dense_matrix[i, j] = val;
                }
                dense_vector[i] = peak[i].FWHM * peak[i].FWHM;
            }

            matrix = Matrix<double>.Build.DenseOfArray(dense_matrix);
            vector = Vector<double>.Build.Dense(dense_vector);

            double[] retvalue = matrix.Solve(vector).ToArray();

            return retvalue;
        }

        /// <summary>
        /// Степенная модель разрешения: FWHM = a · ch^p (V2).
        ///
        /// Подгонка идёт по ЛОГАРИФМАМ — ln FWHM = ln a + p · ln ch, — потому
        /// что в них задача линейная и решается тем же наименьшим квадратом,
        /// что и остальные кривые. Прямая подгонка степени потребовала бы
        /// итераций и начального приближения, а выигрыша не дала бы: точек
        /// калибровки единицы, и разница между весами в логарифмах и в
        /// линейной шкале меньше их разброса.
        ///
        /// Точки с нулевым или отрицательным каналом и шириной отбрасываются:
        /// логарифма у них нет. Ноль канала — это не редкость, а обычная первая
        /// опорная точка (`FWHM_AT_0`), поэтому молчаливое NaN здесь было бы
        /// самым частым исходом.
        /// </summary>
        public static double[] SolvePower(List<CalibrationPeak> peak)
        {
            List<double> x = new List<double>();
            List<double> y = new List<double>();
            foreach (CalibrationPeak p in peak)
            {
                if (p.Channel <= 0 || p.FWHM <= 0.0) continue;
                x.Add(Math.Log(p.Channel));
                y.Add(Math.Log(p.FWHM));
            }

            if (x.Count < 2) return new double[2];

            double[,] dense_matrix = new double[x.Count, 2];
            double[] dense_vector = new double[x.Count];
            for (int i = 0; i < x.Count; i++)
            {
                dense_matrix[i, 0] = 1.0;
                dense_matrix[i, 1] = x[i];
                dense_vector[i] = y[i];
            }

            Matrix<double> matrix = Matrix<double>.Build.DenseOfArray(dense_matrix);
            Vector<double> vector = Vector<double>.Build.Dense(dense_vector);
            double[] fit = matrix.Solve(vector).ToArray();
            return new double[] { Math.Exp(fit[0]), fit[1] };
        }

        public static double[] SolveWeighted (List<CalibrationPoint> points, int PolynomialOrder)
        {
            Matrix<double> matrix;
            Vector<double> vector;
            Matrix<double> weight;
            double[,] dense_matrix = new double[points.Count, PolynomialOrder + 1];
            double[] dense_vector = new double[points.Count];
            double[,] dense_weight = new double[points.Count, points.Count];

            double max_count = points.Max(point => point.Count);

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = 0; j < points.Count; j ++)
                {
                    if (i == j)
                    {
                        dense_weight[i, j] = Math.Sqrt((double)points[i].Count / max_count);
                    }
                    else
                    {
                        dense_weight[i, j] = 0;
                    }
                }
            }

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = PolynomialOrder; j >= 0; j--)
                {
                    double val = (double)Math.Pow(points[i].Channel, j);
                    dense_matrix[i, j] = val;
                }
                dense_vector[i] = (double)points[i].Energy;
            }

            matrix = Matrix<double>.Build.DenseOfArray(dense_matrix);
            vector = Vector<double>.Build.Dense(dense_vector);
            weight = Matrix<double>.Build.DenseOfArray(dense_weight);

            double[] retvalue = null;
            try
            {
                retvalue = WeightedRegression.Weighted(matrix, vector, weight).ToArray();
            } catch (Exception) { }

            return retvalue;
        }

        /// <summary>
        /// Наибольший допустимый ИЗГИБ подгонки относительно прямой, проведённой
        /// по тем же опорным точкам, в долях энергии этой прямой (`S42`).
        /// Значение перенесено из конвейера корпуса
        /// (`tools/CORPUS/scripts/calibrate.py`, <c>fit_ecal(max_bend=0.15)</c>),
        /// а не назначено заново.
        /// </summary>
        public const double MaxBend = 0.15;

        /// <summary>
        /// Пол допуска изгиба, кэВ: у самого низа шкалы 15 % от энергии прямой —
        /// это единицы кэВ, и без пола сторож отвергал бы честную квадратичную
        /// из-за пары кэВ на пятом канале. Тоже перенос из `calibrate.py`
        /// (<c>tol = np.maximum(40.0, max_bend * |straight|)</c>).
        /// </summary>
        public const double BendFloorKeV = 40.0;

        static double Poly(double[] coefficients, double x)
        {
            double value = 0.0;
            for (int i = coefficients.Length - 1; i >= 0; i--)
            {
                value = value * x + coefficients[i];
            }
            return value;
        }

        static bool AllFinite(double[] v)
        {
            if (v == null) return false;
            for (int i = 0; i < v.Length; i++)
            {
                if (double.IsNaN(v[i]) || double.IsInfinity(v[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// ⛔ Сторож ИЗГИБА: подгонка не имеет права уйти от прямой, проведённой
        /// по её же опорным точкам, дальше чем на <see cref="MaxBend"/> (но не
        /// менее <see cref="BendFloorKeV"/> кэВ) НИ В ОДНОМ канале шкалы.
        ///
        /// Перенос <c>bend_ok</c> из `tools/CORPUS/scripts/calibrate.py` — там
        /// он написан по живому дефекту: квадратичная, подогнанная по пяти
        /// опорам между каналами 689 и 2510, проходит через все пять и при этом
        /// даёт 5133 кэВ на канале 8191, процентов на семьдесят выше того, что
        /// говорит усиление. Опоры такую кривую не ловят по построению —
        /// невязка подгонки на них нулевая, — а человек видит неверную шкалу
        /// ровно там, где опор нет.
        ///
        /// ⚠ Сравнение идёт с прямой ПО ТЕМ ЖЕ ТОЧКАМ, а не с прежней
        /// (хранимой) калибровкой: в корпусе пробовали второе, и оно не
        /// работает — хранимые кривые высоких степеней сами гуляют на концах и
        /// штрафуют за это хорошие переподгонки.
        ///
        /// ⚠ Проверка идёт от <paramref name="lo"/> = половины самой нижней
        /// опоры, а не от нулевого канала: ниже этого прямая уходит в минус и
        /// сравнение теряет смысл (в корпусе это стоило RC-103 правильной
        /// квадратичной, отвергнутой из-за 1.8 кэВ на пятом канале).
        /// </summary>
        /// <param name="coefficients">коэффициенты испытуемой подгонки, младший первым</param>
        /// <param name="line">прямая по тем же точкам (два коэффициента); null — сторож пропускает</param>
        /// <param name="channels">число каналов шкалы</param>
        /// <param name="lo">канал, с которого начинается проверка</param>
        public static bool BendOk(double[] coefficients, double[] line, int channels, double lo)
        {
            if (line == null || line.Length < 2) return true;
            if (!AllFinite(coefficients) || !AllFinite(line)) return false;
            int step = Math.Max(1, channels / 400);
            for (double ch = lo; ch < channels; ch += step)
            {
                double straight = line[0] + line[1] * ch;
                double tol = Math.Max(BendFloorKeV, MaxBend * Math.Abs(straight));
                if (Math.Abs(Poly(coefficients, ch) - straight) > tol) return false;
            }
            return true;
        }

        /// <summary>
        /// Подгонка энергетической шкалы по опорным точкам, ЗАЩИЩЁННАЯ от
        /// дикой экстраполяции: степень понижается до тех пор, пока кривая не
        /// станет годной (<see cref="PolynomialEnergyCalibration.CheckCalibration"/>
        /// — монотонность и вменяемость энергий) и не перестанет гнуться
        /// сверх меры (<see cref="BendOk"/>). Перенос <c>fit_ecal</c> из
        /// `tools/CORPUS/scripts/calibrate.py` (`S42`).
        ///
        /// Возвращает коэффициенты принятой подгонки и её степень в
        /// <paramref name="usedOrder"/>; <c>null</c>, если годной не нашлось ни
        /// на какой степени — вызывающий сообщает об этом ровно так же, как
        /// сообщал о неудаче обычного решения.
        ///
        /// ⚠ Степень не «отбрасывается» (<c>Downgrade</c>), а ПЕРЕПОДГОНЯЕТСЯ:
        /// отбрасывание старшего члена оставляет остальные посчитанными для
        /// другой степени, и полученная кривая не проходит через собственные
        /// опоры.
        /// </summary>
        public static double[] SolveGuarded(List<CalibrationPoint> points, int polynomialOrder,
                                            int channels, bool weighted, out int usedOrder)
        {
            usedOrder = 0;
            if (points == null || points.Count == 0) return null;

            // Столько же степеней, сколько позволяют точки: у питона корпуса
            // order = min(order, len(ch) - 1).
            int order = Math.Min(polynomialOrder, points.Count - 1);
            if (order < 1) order = 1;

            double[] line = null;
            if (points.Count >= 2)
            {
                try
                {
                    line = weighted ? SolveWeighted(points, 1) : Solve(points, 1);
                }
                catch (Exception) { line = null; }
                if (!AllFinite(line)) line = null;
            }

            double chMin = points.Min(p => (double)p.Channel);
            double lo = Math.Max(5.0, 0.5 * chMin);

            while (order >= 1)
            {
                double[] coefficients = null;
                try
                {
                    coefficients = weighted ? SolveWeighted(points, order) : Solve(points, order);
                }
                catch (Exception) { coefficients = null; }

                if (AllFinite(coefficients) && coefficients.Length == order + 1)
                {
                    PolynomialEnergyCalibration candidate = new PolynomialEnergyCalibration
                    {
                        Coefficients = (double[])coefficients.Clone(),
                        PolynomialOrder = order
                    };
                    if (candidate.CheckCalibration(channels) && BendOk(coefficients, line, channels, lo))
                    {
                        usedOrder = order;
                        return coefficients;
                    }
                }
                order--;
            }
            return null;
        }

        public static double MSE(double[] coefficients, List<CalibrationPoint> points)
        {
            try
            {
                PolynomialEnergyCalibration pol = new PolynomialEnergyCalibration
                {
                    Coefficients = coefficients,
                    PolynomialOrder = coefficients.Length - 1
                };
                double retvalue = 0.0;
                foreach (CalibrationPoint point in points)
                {
                    retvalue += Math.Pow(pol.ChannelToEnergy(point.Channel) - (double)point.Energy, 2);
                }
                retvalue /= points.Count;
                return retvalue;
            }
            catch
            {
                return -1;
            }
        }

        public static double MSE(FwhmCalibration fwhmCalibration, List<CalibrationPeak> points)
        {
            try
            {
                double retvalue = 0.0;
                foreach (CalibrationPeak point in points)
                {
                    retvalue += Math.Pow(fwhmCalibration.ChannelToFwhm(point.Channel) - (double)point.FWHM, 2);
                }
                retvalue /= points.Count;
                return retvalue;
            }
            catch
            {
                return -1;
            }
        }

        public static double WMSE(double[] coefficients, List<CalibrationPoint> points)
        {
            try
            {
                double retvalue = 0.0;
                double max_count = points.Max(point => point.Count);
                double weight_sum = 0.0;
                PolynomialEnergyCalibration pol = new PolynomialEnergyCalibration
                {
                    Coefficients = coefficients,
                    PolynomialOrder = coefficients.Length - 1
                };
                for (int i = 0; i < points.Count; i++)
                {
                    double weight = Math.Sqrt((double)points[i].Count / max_count);
                    weight_sum += weight;
                    retvalue += weight * Math.Pow(pol.ChannelToEnergy(points[i].Channel) - (double)points[i].Energy, 2);
                }
                // Weighted mean = sum(w*e^2) / sum(w). Dividing additionally by points.Count
                // understated the value by a factor of N.
                if (weight_sum == 0.0)
                {
                    return -1;
                }
                retvalue /= weight_sum;
                return retvalue;
            } catch
            {
                return -1;
            }
        }
    }
}
