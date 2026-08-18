using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    /// <summary>
    /// Степенная модель разрешения: FWHM(ch) = a · ch^p (V2, 16.08.2026).
    ///
    /// Зачем третья кривая, когда есть две корневые. Обе имеющиеся — это
    /// FWHM² = многочлен по каналу, то есть по сути FWHM ~ √ch. Корпус говорит
    /// другое: по измеренным линиям показатель степени у ВСЕХ сцинтилляторов
    /// ВЫШЕ половины — ASN16 0.569, RC-103 0.585, Гамма-1С 0.614 и 0.616,
    /// AS80x80 0.656, GS4000 0.684, ASN3 0.701, AS1PRO 0.775 (у германия
    /// наоборот, 0.24…0.41). Ширина растёт БЫСТРЕЕ корня, поэтому корневая
    /// модель, посаженная на верх шкалы, внизу выходит ШИРЕ настоящей: на 76
    /// измеренных точках ниже 200 кэВ отношение измеренного к модельному
    /// 0.87 по медиане, а у группы Гамма-1С 2016 года — 0.73.
    ///
    /// Замер и подбор формы: `tools/CORPUS/scripts/res_low.py` и
    /// `res_form.py`; там же сказано, почему форма со свободным членом и форма
    /// GADRAS отпали (у обеих относительная ширина перестаёт падать, то есть
    /// они нефизичны).
    ///
    /// ⚠ Почему степень В КАНАЛАХ, когда мерили по энергии. У линейной
    /// энергетической шкалы E ≈ g·ch это одна и та же форма с другой
    /// амплитудой: a·E^p = (a·g^p)·ch^p. Все прочие кривые здесь работают в
    /// каналах, и заводить единственную по энергии значило бы сломать общий
    /// интерфейс ради тождественной записи.
    ///
    /// ⚠ Чего эта модель НЕ умеет: шумовой полки на нулевом канале. При ch → 0
    /// она даёт FWHM → 0, тогда как у настоящего тракта там остаётся
    /// электронный шум (для него в настройках поиска пиков есть отдельное
    /// `FWHM_AT_0`). Ниже первой опорной точки модель — экстраполяция, как и
    /// две соседние.
    /// </summary>
    public class PowerFwhmCalibration : FwhmCalibration
    {
        // Fwhm [ch]
        // Fwhm(ch) = a * ch^p
        string formula = "FWHM = {0} * ch^{1}";

        List<CalibrationPeak> peaks = new List<CalibrationPeak>();
        double[] coefficients = new double[2];

        [XmlArrayItem("Peak")]
        public override List<CalibrationPeak> CalibrationPeaks { get => peaks; set => peaks = value; }

        [XmlArrayItem("Coefficient")]
        public override double[] Coefficients { get => coefficients; set => coefficients = value; }

        public override double ChannelToFwhm(double channel)
        {
            if (channel <= 0.0 || coefficients[0] <= 0.0) return 0.0;
            return coefficients[0] * Math.Pow(channel, coefficients[1]);
        }

        public override double FwhmToChannel(double fwhm)
        {
            if (fwhm <= 0.0 || coefficients[0] <= 0.0 || coefficients[1] == 0.0) return 0.0;
            return Math.Pow(fwhm / coefficients[0], 1.0 / coefficients[1]);
        }

        public override FwhmCalibration Clone()
        {
            return new PowerFwhmCalibration
            {
                CalibrationPeaks = CalibrationPeak.ClonePeaks(this.CalibrationPeaks),
                Coefficients = (double[])this.Coefficients.Clone(),
                PeakType = this.PeakType,
                ExpGaussExpLeftTail = this.ExpGaussExpLeftTail,
                ExpGaussExpRightTail = this.ExpGaussExpRightTail,
                VoigtSigma = this.VoigtSigma,
                VoigtGamma = this.VoigtGamma,
                GaussianChi2Total = this.GaussianChi2Total,
                ExpGaussExpChi2Total = this.ExpGaussExpChi2Total,
                VoigtChi2Total = this.VoigtChi2Total,
                Chi2pNdp = this.Chi2pNdp,
            };
        }

        public override string GetFormula()
        {
            return String.Format(formula, "a", "p");
        }

        /// <summary>
        /// F = a·ch^p. Подставив ch = ch'·mul и поделив ширину на mul:
        /// F'(ch') = a·mul^(p−1)·ch'^p — меняется ТОЛЬКО множитель, показатель
        /// остаётся (`S54`). Он и не должен меняться: p — свойство детектора, а
        /// не разбиения шкалы.
        /// </summary>
        public override void RescaleCoefficients(double mul)
        {
            coefficients[0] = coefficients[0] * Math.Pow(mul, coefficients[1] - 1.0);
        }

        public override int MinPeaksRequirement()
        {
            return 2;
        }

        public override bool PerformCalibration(int maxchannels)
        {
            if (peaks.Count <= 1) return false;
            coefficients = Utils.CalibrationSolver.SolvePower(peaks);
            return CheckCalibration(maxchannels);
        }

        public override string ToString()
        {
            return String.Format(formula, coefficients[0], coefficients[1]);
        }

        public override bool NotCalibrated()
        {
            return (coefficients[0] == 0 && coefficients[1] == 0);
        }

        /// <summary>
        /// Кривая обязана расти, а ОТНОСИТЕЛЬНАЯ ширина — падать: за неё
        /// отвечает статистика фотоэлектронов, и модель, у которой разрешение
        /// с энергией не улучшается, описывает не детектор, а подгонку. У
        /// степенной формы это ровно условие 0 &lt; p &lt; 1, поэтому проверка
        /// здесь по коэффициентам, а не перебором каналов, как у корневых:
        /// перебор дал бы то же самое за тысячи шагов.
        /// </summary>
        private bool CheckCalibration(int maxchannels)
        {
            if (!(coefficients[0] > 0.0)) return false;
            if (!(coefficients[1] > 0.0) || !(coefficients[1] < 1.0)) return false;
            return ChannelToFwhm(Math.Max(1, maxchannels - 1)) > 0.0;
        }

        public override int PeakType { get => this.peak_type; set => this.peak_type = value; }

        public override double ExpGaussExpLeftTail { get => this.left_tail; set => this.left_tail = value; }

        public override double ExpGaussExpRightTail { get => this.right_tail; set => this.right_tail = value; }

        public override double Chi2pNdp { get => this.chi2pndp; set => this.chi2pndp = value; }

        public override double VoigtSigma { get => this.voigt_sigma; set => this.voigt_sigma = value; }

        public override double VoigtGamma { get => this.voigt_gamma; set => this.voigt_gamma = value; }

        public override double GaussianChi2Total { get => this.gaussian_chi2_total; set => this.gaussian_chi2_total = value; }

        public override double ExpGaussExpChi2Total { get => this.exp_gauss_exp_chi2_total; set => this.exp_gauss_exp_chi2_total = value; }

        public override double VoigtChi2Total { get => this.voigt_chi2_total; set => this.voigt_chi2_total = value; }

        int peak_type = 0;

        double left_tail = 1.0;

        double right_tail = 1.0;

        double voigt_sigma = 1.0;

        double voigt_gamma = 1.0;

        double gaussian_chi2_total = -1.0;

        double exp_gauss_exp_chi2_total = -1.0;

        double voigt_chi2_total = -1.0;

        double chi2pndp = -1.0;
    }
}
